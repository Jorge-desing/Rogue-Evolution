#!/usr/bin/env python3
"""
BepInEx MCP Server bridge for Antigravity & Rogue Command.
Connects standard Model Context Protocol (JSON-RPC over stdio) to BepInEx MCP HTTP server (http://127.0.0.1:8080/mcp/).
Supports full scene hierarchy, reflection, dynamic Harmony patching, batching, watchers, mod profiles,
and first-class clean-room diagnostics with automatic GameObject resolution for LoaderDiagnosticsComponent.
"""

from __future__ import annotations

import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from typing import Any

PROTOCOL_VERSION = "2024-11-05"
SERVER_NAME = "bepinex_mcp"
SERVER_VERSION = "1.2.0"
BASE_URL = os.environ.get("BEPINEX_MCP_URL", "http://127.0.0.1:8080/mcp/")

# Try importing profiles from upstream BepInExMCP if available
_profile_store = None
try:
    _mcp_pkg_dir = os.path.join(os.path.dirname(__file__), "..", ".tools", "BepInExMCP", "mcp")
    if os.path.isdir(_mcp_pkg_dir):
        sys.path.insert(0, os.path.abspath(_mcp_pkg_dir))
        from profiles import ProfileStore, default_profiles_dir
        _profile_store = ProfileStore(default_profiles_dir())
except Exception:
    pass

# Fallback profile store if upstream package directory is not present
if _profile_store is None:
    class _SimpleProfileStore:
        def __init__(self, root: Path):
            self.root = root
            self.root.mkdir(parents=True, exist_ok=True)
            self._active_file = self.root / "_active.json"

        def _get_active(self) -> set[str]:
            if self._active_file.is_file():
                try:
                    return set(json.loads(self._active_file.read_text(encoding="utf-8")))
                except Exception:
                    pass
            return set()

        def _save_active(self, active: set[str]):
            self._active_file.write_text(json.dumps(sorted(list(active))), encoding="utf-8")

        def list(self) -> list[dict[str, Any]]:
            active = self._get_active()
            res = []
            for p in self.root.glob("*.json"):
                if p.name == "_active.json":
                    continue
                try:
                    data = json.loads(p.read_text(encoding="utf-8"))
                    res.append({
                        "name": p.stem,
                        "active": p.stem in active,
                        "autoApply": bool(data.get("autoApply", False)),
                        "operationCount": len(data.get("operations", [])),
                    })
                except Exception:
                    res.append({"name": p.stem, "active": p.stem in active, "error": "Invalid JSON"})
            return res

        def get(self, name: str) -> dict[str, Any]:
            p = self.root / f"{name}.json"
            if not p.is_file():
                raise FileNotFoundError(f"Profile '{name}' does not exist.")
            return json.loads(p.read_text(encoding="utf-8"))

        def save(self, name: str, data: dict[str, Any]):
            p = self.root / f"{name}.json"
            p.write_text(json.dumps(data, indent=2), encoding="utf-8")

        def delete(self, name: str):
            p = self.root / f"{name}.json"
            if p.is_file():
                p.unlink()
            active = self._get_active()
            if name in active:
                active.remove(name)
                self._save_active(active)

        def set_active(self, name: str, is_active: bool) -> list[str]:
            active = self._get_active()
            if is_active:
                active.add(name)
            else:
                active.discard(name)
            self._save_active(active)
            return sorted(list(active))

    _fallback_dir = Path(os.environ.get("LOCALAPPDATA", os.path.expanduser("~"))) / "unity-mcp-translator" / "profiles"
    _profile_store = _SimpleProfileStore(_fallback_dir)

# Import TOOL_SPECS from build_bepinex_mcp to maintain a single source of truth
try:
    import sys as _sys
    _sys.path.insert(0, str(Path(__file__).resolve().parent))
    from build_bepinex_mcp import TOOL_SPECS
    TOOLS = [
        {
            "name": spec["name"],
            "description": spec["description"],
            "inputSchema": spec["parameters"],
        }
        for spec in TOOL_SPECS
    ]
except Exception:
    TOOLS = []

_TOOL_ALIAS_CACHE: set[str] | None = None

def _is_tool_alias(tool: dict[str, Any]) -> bool:
    return str(tool.get("description", "")).startswith("Alias for")

def _alias_names() -> set[str]:
    global _TOOL_ALIAS_CACHE
    if _TOOL_ALIAS_CACHE is not None:
        return _TOOL_ALIAS_CACHE
    _TOOL_ALIAS_CACHE = {t["name"] for t in TOOLS if _is_tool_alias(t)}
    return _TOOL_ALIAS_CACHE

def _tool_category(name: str) -> str:
    n = name.lower()
    if n.startswith("diagnostics_"):
        return "diagnostics"
    if n in ("system_ping", "ping_unity_bridge", "system_capabilities", "get_runtime_capabilities", "system_paths", "get_game_paths", "discover_tools", "describe_tool"):
        return "system"
    if n.startswith("scene_") or n.startswith("gameobject_") or n.startswith("component_") or n.startswith("type_") or n in ("search_gameobjects", "find_objects_with_component", "list_root_gameobjects", "get_hierarchy_snapshot", "resolve_gameobject_selector", "list_children", "inspect_components", "get_component_details", "get_component_member", "set_component_value", "call_component_method", "list_component_methods", "search_runtime_types", "describe_runtime_type"):
        return "scene"
    if n.startswith("mod_") or n.startswith("watch_") or n in ("run_bridge_batch",):
        return "mod"
    if n in ("save_mod_profile", "get_mod_profile", "list_mod_profiles", "delete_mod_profile", "activate_mod_profile", "list_mod_patches"):
        return "profiles"
    return "other"

def _parse_tool_filter_env(raw: str | None) -> dict[str, Any]:
    out: dict[str, Any] = {}
    if not raw:
        return out
    for part in raw.replace(",", ";").split(";"):
        part = part.strip()
        if not part:
            continue
        if part == "no-aliases":
            out["includeAliases"] = False
            continue
        if "=" in part:
            k, v = part.split("=", 1)
            v = v.strip()
            if k.strip() == "includeAliases":
                out[k.strip()] = v.lower() not in ("0", "false", "no")
            elif k.strip() in ("limit", "pageSize"):
                try:
                    out[k.strip()] = int(v)
                except ValueError:
                    pass
            else:
                out[k.strip()] = v
    return out

def _filter_and_page_tools(params: dict[str, Any] | None) -> tuple[list[dict[str, Any]], str | None, int]:
    params = params or {}
    # env defaults only when caller sent no filtering keys (backward compat)
    env = _parse_tool_filter_env(os.environ.get("BEPINEX_MCP_TOOL_FILTER"))
    merged: dict[str, Any] = dict(env)
    for k, v in params.items():
        if k != "cursor":
            merged[k] = v
    tools = list(TOOLS)
    include_aliases = merged.get("includeAliases")
    if include_aliases is False:
        aliases = _alias_names()
        tools = [t for t in tools if t["name"] not in aliases]
    category = str(merged.get("category", "") or "").strip().lower()
    if category:
        tools = [t for t in tools if _tool_category(t["name"]) == category]
    prefix = str(merged.get("prefix", "") or "").strip()
    if prefix:
        tools = [t for t in tools if t["name"].startswith(prefix)]
    query = str(merged.get("query", "") or "").strip().lower()
    if query:
        tools = [t for t in tools if query in t["name"].lower() or query in str(t.get("description", "")).lower()]
    total = len(tools)
    limit = merged.get("limit", merged.get("pageSize"))
    if limit is not None:
        try:
            limit = int(limit)
        except (TypeError, ValueError):
            limit = None
    if limit is not None and limit <= 0:
        limit = None
    cursor = params.get("cursor")
    offset = 0
    if cursor:
        try:
            offset = int(str(cursor))
        except ValueError:
            offset = 0
        if offset < 0:
            offset = 0
    if limit is None:
        page = tools[offset:]
        next_cursor = None
    else:
        page = tools[offset:offset + limit]
        next_cursor = str(offset + limit) if offset + limit < total else None
    detail = str(merged.get("detail", "") or "").strip().lower()
    if detail == "summary":
        page = [{"name": t["name"], "description": t.get("description", "")} for t in page]
    return page, next_cursor, total

_DIAGNOSTICS_GO_ID: int | None = None


_RUNTIME_SNAPSHOT_GETTERS = [
    "GetSystemHealthJson",
    "GetBattlefieldCensusJson",
    "GetPlayerRosterJson",
    "GetAiRosterJson",
    "GetRunProgressionCensusJson",
    "GetCombatStatusCensusJson",
    "GetProductionCensusJson",
    "GetEconomyMatchCensusJson",
    "GetBaseIntegrityCensusJson",
    "GetThreatPrioritizationCensusJson",
    "GetUnitThreatAssessmentJson",
    "GetAgentCoordinationCensusJson",
    "GetTacticalStanceCensusJson",
    "GetUnitAffixCensusJson",
    "GetAiDoctrineCensusJson",
    "GetAiDirectorCensusJson",
    "GetHarvesterCensusJson",
    "GetHazardCensusJson",
    "GetVeterancyCensusJson",
    "GetSelectionCensusJson",
    "GetVisualRenderCensusJson",
    "GetVisualQualityCensusJson",
    "GetLoadedPrefabVisualAuditJson",
    "GetLoaderAuditCensusJson",
    "GetUnitMechanicsCensusJson",
    "GetAudioCensusJson",
    "GetCameraCensusJson",
    "GetCommandAuraCensusJson",
    "GetTechCatalogJson",
    "GetMatchSummaryJson",
    "GetEntityCatalogJson",
    "GetCombatXpEntriesJson",
    "GetUnitLoadoutCensusJson",
    "GetAiModulesDiagnosticsJson",
    "GetGridMetricsJson",
    "GetFogStateJson",
    "GetSocketAuditCensusJson",
    "GetColliderAuditCensusJson",
    "GetParticleVfxCensusJson",
    "GetLodAuditCensusJson",
    "GetVisualSurfaceCensusJson",
    "GetVisualMaterialCensusJson",
    "GetGameTimeScaleJson",
    "GetPerformanceCensusJson",
    "GetIntermissionCensusJson",
    "GetNavigatorStateJson",
]

_RUNTIME_SNAPSHOT_GETTER_ARGS: dict[str, list[object]] = {
    "GetVisualRenderCensusJson": [32],
    "GetVisualQualityCensusJson": [64],
    "GetVisualHealthSummaryJson": [64],
    "GetLoadedPrefabVisualAuditJson": [64],
    "GetLoaderAuditCensusJson": [64, 32],
    "GetUnitMechanicsCensusJson": [64],
    "GetVisualSurfaceCensusJson": [64],
    "GetVisualMaterialCensusJson": [64],
    "GetSocketAuditCensusJson": [64],
    "GetIntermissionCensusJson": [64],
    "GetRelicCensusJson": [64],
    "GetUpgradeCensusJson": [64],
    "GetVisualVariantCensusJson": [64],
}


def _snapshot_getter_args(getter: str) -> list[object]:
    return list(_RUNTIME_SNAPSHOT_GETTER_ARGS.get(getter, []))


def _call_http(endpoint: str, method: str = "GET", params: dict[str, Any] | None = None, body_str: str | None = None, timeout: float = 12.0) -> str:
    url = BASE_URL.rstrip("/") + "/" + endpoint.lstrip("/")
    if params:
        filtered = {k: str(v) for k, v in params.items() if v is not None}
        if filtered:
            url += "?" + urllib.parse.urlencode(filtered)

    data = body_str.encode("utf-8") if body_str else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("User-Agent", "Antigravity-BepInEx-MCP-Bridge/1.2")
    if data:
        req.add_header("Content-Type", "application/json")

    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return resp.read().decode("utf-8", errors="replace")
    except urllib.error.URLError as err:
        return json.dumps({
            "error": f"Failed to communicate with BepInEx MCP on {url}: {err}. Ensure Rogue Command is running."
        })
    except Exception as exc:
        return json.dumps({
            "error": f"Unexpected error communicating with BepInEx MCP: {exc}"
        })


def _tool_response(text: str, is_error: bool = False) -> dict[str, Any]:
    return {
        "content": [{"type": "text", "text": text}],
        "isError": is_error,
    }


def _get_diagnostics_go_id() -> int:
    global _DIAGNOSTICS_GO_ID
    if _DIAGNOSTICS_GO_ID is not None:
        return _DIAGNOSTICS_GO_ID

    # 1. Primary: search by component name via scene:find_objects_with_component
    raw = _call_http("scene:find_objects_with_component", params={"componentName": "LoaderDiagnosticsComponent"})
    try:
        data = json.loads(raw)
        if isinstance(data, list) and len(data) > 0 and "id" in data[0]:
            _DIAGNOSTICS_GO_ID = int(data[0]["id"])
            return _DIAGNOSTICS_GO_ID
        elif isinstance(data, dict) and "id" in data:
            _DIAGNOSTICS_GO_ID = int(data["id"])
            return _DIAGNOSTICS_GO_ID
    except Exception:
        pass

    # 2. Secondary fallback: search gameobjects by componentName (includes DontDestroyOnLoad)
    try:
        raw = _call_http("scene/search_gameobjects", params={"componentName": "LoaderDiagnosticsComponent", "includeInactive": "true", "limit": "50"})
        data = json.loads(raw)
        items = data if isinstance(data, list) else ([data] if isinstance(data, dict) and "id" in data else [])
        for item in items:
            if isinstance(item, dict) and "id" in item:
                _DIAGNOSTICS_GO_ID = int(item["id"])
                return _DIAGNOSTICS_GO_ID
    except Exception:
        pass

    # 3. Tertiary fallback: search gameobjects by GameObject name (includes DontDestroyOnLoad)
    try:
        raw = _call_http("scene/search_gameobjects", params={"name": "LoaderDiagnostics", "includeInactive": "true", "limit": "50"})
        data = json.loads(raw)
        items = data if isinstance(data, list) else ([data] if isinstance(data, dict) and "id" in data else [])
        for item in items:
            if isinstance(item, dict) and "id" in item:
                _DIAGNOSTICS_GO_ID = int(item["id"])
                return _DIAGNOSTICS_GO_ID
    except Exception:
        pass

    raise RuntimeError(
        "Could not find a GameObject with LoaderDiagnosticsComponent in the running game. "
        "Ensure Rogue Command is running with the RogueCommandExpansionLoader plugin active."
    )


def _extract_bridge_return_value(raw: str) -> str | None:
    marker = "ReturnValue: "
    try:
        response = json.loads(raw)
        message = response.get("message") if isinstance(response, dict) else None
        if isinstance(message, str) and marker in message:
            return message.split(marker, 1)[1].strip()
    except Exception:
        pass

    marker_index = raw.find(marker)
    if marker_index < 0:
        return None

    start = marker_index + len(marker)
    while start < len(raw) and raw[start].isspace():
        start += 1
    if start >= len(raw):
        return None

    if raw[start] in "{[":
        stack = [raw[start]]
        in_string = False
        escaped = False
        for index in range(start + 1, len(raw)):
            char = raw[index]
            if in_string:
                if escaped:
                    escaped = False
                elif char == "\\":
                    escaped = True
                elif char == '"':
                    in_string = False
                continue
            if char == '"':
                in_string = True
            elif char in "[{":
                stack.append(char)
            elif char in "]}":
                if not stack:
                    return None
                opening = stack.pop()
                if (opening, char) not in (("[", "]"), ("{", "}")):
                    return None
                if not stack:
                    return raw[start:index + 1]
        return None

    if raw[start] == '"':
        escaped = False
        for index in range(start + 1, len(raw)):
            char = raw[index]
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                return raw[start:index + 1]
        return None

    end = start
    while end < len(raw) and raw[end] not in '"}':
        end += 1
    value = raw[start:end].strip()
    return value or None


def _parse_component_member(raw: str) -> Any:
    try:
        member = json.loads(raw)
    except Exception as exc:
        return {"error": f"Invalid component member response: {exc}"}
    if not isinstance(member, dict) or "value" not in member:
        return member if isinstance(member, dict) else {"error": "Component member response has no value."}

    value = member["value"]
    clr_type = member.get("type")
    if not isinstance(value, str):
        return value
    try:
        if clr_type in ("System.Byte", "System.Int16", "System.Int32", "System.Int64", "System.UInt16", "System.UInt32", "System.UInt64"):
            return int(value)
        if clr_type in ("System.Single", "System.Double", "System.Decimal"):
            return float(value)
        if clr_type == "System.Boolean" and value.lower() in ("true", "false"):
            return value.lower() == "true"
        return value
    except (TypeError, ValueError):
        return {"error": f"Could not parse {clr_type or 'component'} value: {value}"}


def _call_diagnostics_member(member_name: str) -> Any:
    global _DIAGNOSTICS_GO_ID
    for attempt in range(2):
        try:
            go_id = _get_diagnostics_go_id()
            raw = _call_http("component/get_member", params={
                "id": go_id,
                "componentName": "LoaderDiagnosticsComponent",
                "memberName": member_name,
            })
            try:
                response = json.loads(raw)
            except Exception:
                response = None
            if isinstance(response, dict) and "error" in response:
                error = str(response["error"])
                if attempt == 0 and any(token in error.lower() for token in ("not found", "invalid 'id'", "gameobject")):
                    _DIAGNOSTICS_GO_ID = None
                    continue
                return {"error": error}
            return _parse_component_member(raw)
        except Exception as exc:
            if attempt == 0:
                _DIAGNOSTICS_GO_ID = None
                continue
            return {"error": f"Failed reading diagnostics member '{member_name}': {exc}"}
    return {"error": f"Failed reading diagnostics member '{member_name}'."}


def _call_diagnostics_method(method_name: str, args: list | None = None) -> str:
    global _DIAGNOSTICS_GO_ID
    args_json = json.dumps(args or [])
    component_name = "RogueCommandExpansionLoader.Runtime.LoaderDiagnosticsComponent"
    try:
        go_id = _get_diagnostics_go_id()
        raw = _call_http("component/call_method", params={
            "id": go_id,
            "componentName": component_name,
            "methodName": method_name,
            "args": args_json,
        })
        # If cached go_id became stale, retry once after discovery
        if "Missing or invalid 'id'" in raw or "not found" in raw.lower() or "GameObject" in raw:
            _DIAGNOSTICS_GO_ID = None
            go_id = _get_diagnostics_go_id()
            raw = _call_http("component/call_method", params={
                "id": go_id,
                "componentName": component_name,
                "methodName": method_name,
                "args": args_json,
            })

        return_value = _extract_bridge_return_value(raw)
        if return_value is not None:
            try:
                return json.dumps(json.loads(return_value), indent=2)
            except Exception:
                return return_value
        return raw
    except Exception as exc:
        _DIAGNOSTICS_GO_ID = None
        return json.dumps({"error": f"Failed calling diagnostics method '{method_name}': {exc}"})


def execute_tool(name: str, arguments: dict[str, Any]) -> dict[str, Any]:
    try:
        # ---- Discovery (lazy, lightweight) ----
        if name == "discover_tools":
            page, _nc, total = _filter_and_page_tools({**arguments, "detail": "summary"})
            for entry in page:
                entry["category"] = _tool_category(entry["name"])
            return _tool_response(json.dumps({"total": total, "returned": len(page), "tools": page}, indent=2))
        elif name == "describe_tool":
            target = str(arguments.get("name", "")).strip()
            for entry in TOOLS:
                if entry["name"] == target:
                    return _tool_response(json.dumps(entry, indent=2))
            return _tool_response(json.dumps({"success": False, "message": f"tool not found: {target}"}), True)
        # ---- Core Bridge & System ----
        if name in ("system_ping", "ping_unity_bridge"):
            res = _call_http("system/ping")
            return _tool_response(res)

        elif name in ("system_capabilities", "get_runtime_capabilities"):
            res = _call_http("system/capabilities")
            return _tool_response(res)

        elif name in ("system_paths", "get_game_paths"):
            res = _call_http("system/paths")
            return _tool_response(res)

        # ---- Hierarchy & GameObjects ----
        elif name in ("scene_list_root_gameobjects", "list_root_gameobjects"):
            res = _call_http("scene/list_root_gameobjects")
            return _tool_response(res)

        elif name in ("scene_search_gameobjects", "search_gameobjects"):
            res = _call_http("scene/search_gameobjects", params=arguments)
            return _tool_response(res)

        elif name in ("scene_hierarchy_snapshot", "get_hierarchy_snapshot"):
            res = _call_http("scene/hierarchy_snapshot", params=arguments)
            return _tool_response(res)

        elif name in ("scene_resolve_selector", "resolve_gameobject_selector"):
            selector_str = arguments.get("selector_json")
            if selector_str:
                try:
                    parsed_sel = json.loads(selector_str)
                    res = _call_http("scene/resolve_selector", params=parsed_sel)
                except Exception:
                    res = _call_http("scene/resolve_selector", params=arguments)
            else:
                res = _call_http("scene/resolve_selector", params=arguments)
            return _tool_response(res)

        elif name in ("gameobject_list_children", "list_children"):
            res = _call_http("gameobject/list_children", params={"id": arguments.get("id")})
            return _tool_response(res)

        elif name in ("gameobject_inspect_components", "inspect_components"):
            res = _call_http("gameobject/inspect_components", params={"id": arguments.get("id")})
            return _tool_response(res)

        elif name in ("scene_find_objects_with_component", "find_objects_with_component"):
            res = _call_http("scene:find_objects_with_component", params={
                "componentName": arguments.get("componentName"),
            })
            return _tool_response(res)

        # ---- Components & Reflection ----
        elif name in ("component_get_details", "get_component_details"):
            res = _call_http("component/get_details", params={
                "id": arguments.get("id"),
                "componentName": arguments.get("componentName"),
            })
            return _tool_response(res)

        elif name in ("component_get_member", "get_component_member"):
            res = _call_http("component/get_member", params={
                "id": arguments.get("id"),
                "componentName": arguments.get("componentName"),
                "memberName": arguments.get("memberName"),
            })
            return _tool_response(res)

        elif name in ("component_set_value", "set_component_value"):
            res = _call_http("component/set_value", params={
                "id": arguments.get("id"),
                "componentName": arguments.get("componentName"),
                "memberName": arguments.get("memberName"),
                "value": str(arguments.get("value", "")),
            })
            return _tool_response(res)

        elif name in ("component_call_method", "call_component_method"):
            args_val = arguments.get("args") or arguments.get("args_json") or "[]"
            if isinstance(args_val, (list, dict)):
                args_val = json.dumps(args_val)
            res = _call_http("component/call_method", params={
                "id": arguments.get("id"),
                "componentName": arguments.get("componentName") or arguments.get("comp"),
                "methodName": arguments.get("methodName") or arguments.get("method"),
                "args": args_val,
            })
            # Unwrap ReturnValue if present
            try:
                resp_obj = json.loads(res)
                if isinstance(resp_obj, dict) and "message" in resp_obj:
                    msg = resp_obj["message"]
                    idx = msg.find("ReturnValue: ")
                    if idx >= 0:
                        ret_str = msg[idx + len("ReturnValue: "):].strip()
                        try:
                            parsed_ret = json.loads(ret_str)
                            return _tool_response(json.dumps(parsed_ret, indent=2))
                        except Exception:
                            return _tool_response(ret_str)
            except Exception:
                pass
            return _tool_response(res)

        elif name in ("component_list_methods", "list_component_methods"):
            res = _call_http("component/list_methods", params={
                "id": arguments.get("id"),
                "componentName": arguments.get("componentName"),
            })
            return _tool_response(res)

        elif name in ("type_search", "search_runtime_types"):
            res = _call_http("type/search", params=arguments)
            return _tool_response(res)

        elif name in ("type_describe", "describe_runtime_type"):
            res = _call_http("type/describe", params=arguments)
            return _tool_response(res)

        # ---- Live Patching & Code Injection ----
        elif name == "mod_patch_method":
            res = _call_http("mod:patch_method", params=arguments)
            return _tool_response(res)

        elif name == "mod_inject_class":
            payload = json.dumps({
                "classCode": arguments.get("classCode", ""),
                "attachToGameObjectId": arguments.get("attachToGameObjectId", ""),
            })
            res = _call_http("mod:inject_class", method="POST", body_str=payload)
            return _tool_response(res)

        elif name in ("mod_list_patches", "list_mod_patches"):
            res = _call_http("mod:list_patches")
            return _tool_response(res)

        elif name in ("mod_remove_patch", "remove_mod_patch"):
            res = _call_http("mod:remove_patch", params=arguments)
            return _tool_response(res)

        elif name == "mod_subscribe_to_method":
            res = _call_http("mod:subscribe_to_method", params={
                "id": arguments.get("id"),
                "componentName": arguments.get("componentName"),
                "methodName": arguments.get("methodName"),
                "registrationId": arguments.get("registrationId", ""),
            })
            return _tool_response(res)

        # ---- Batching, Watchers & Network ----
        elif name == "run_bridge_batch":
            ops = arguments.get("operations")
            if ops is None and "operations_json" in arguments:
                try:
                    ops = json.loads(arguments["operations_json"])
                except Exception:
                    ops = []
            if not isinstance(ops, list):
                ops = []
            stop_on_error = bool(arguments.get("stopOnError", False))
            payload = json.dumps({"operations": ops, "stopOnError": stop_on_error})
            res = _call_http("batch", method="POST", body_str=payload)
            return _tool_response(res)

        elif name == "watch_component_member":
            sel = arguments.get("selector")
            if sel is None and "selector_json" in arguments:
                try:
                    sel = json.loads(arguments["selector_json"])
                except Exception:
                    sel = {}
            res = _call_http("watch/member", params={
                "selector": json.dumps(sel) if isinstance(sel, dict) else str(sel or "{}"),
                "componentName": arguments.get("componentName"),
                "memberName": arguments.get("memberName"),
                "intervalMs": arguments.get("intervalMs", 500),
                "registrationId": arguments.get("registrationId", ""),
            })
            return _tool_response(res)

        elif name == "watch_active_scene":
            res = _call_http("watch/scene", params={
                "intervalMs": arguments.get("intervalMs", 500),
                "registrationId": arguments.get("registrationId", ""),
            })
            return _tool_response(res)

        elif name == "list_watchers":
            res = _call_http("watch/list")
            return _tool_response(res)

        elif name == "remove_watcher":
            res = _call_http("watch/remove", params={"registrationId": arguments.get("registrationId")})
            return _tool_response(res)

        elif name == "get_network_diagnostics":
            res = _call_http("network/diagnostics", params={"id": arguments.get("id")})
            return _tool_response(res)

        elif name == "raw_mcp_request":
            endpoint = arguments.get("endpoint", "")
            method = arguments.get("method", "GET")
            qparams = arguments.get("queryParams")
            body = arguments.get("bodyJson")
            res = _call_http(endpoint, method=method, params=qparams, body_str=body)
            return _tool_response(res)

        # ---- Mod Profiles ----
        elif name == "save_mod_profile":
            pname = arguments.get("name", "").strip()
            pdata = arguments.get("profile_json")
            if isinstance(pdata, str):
                try:
                    pdata = json.loads(pdata)
                except Exception as exc:
                    return _tool_response(f"Invalid profile_json: {exc}", is_error=True)
            _profile_store.save(pname, pdata)
            return _tool_response(json.dumps({"status": "ok", "saved": pname}))

        elif name == "get_mod_profile":
            pname = arguments.get("name", "").strip()
            pdata = _profile_store.get(pname)
            return _tool_response(json.dumps(pdata, indent=2))

        elif name == "list_mod_profiles":
            plist = _profile_store.list()
            return _tool_response(json.dumps(plist, indent=2))

        elif name == "delete_mod_profile":
            pname = arguments.get("name", "").strip()
            _profile_store.delete(pname)
            return _tool_response(json.dumps({"status": "ok", "deleted": pname}))

        elif name == "activate_mod_profile":
            pname = arguments.get("name", "").strip()
            act = bool(arguments.get("active", True))
            actives = _profile_store.set_active(pname, act)
            return _tool_response(json.dumps({"status": "ok", "activeProfiles": actives}))

        # ---- Clean-Room Loader Diagnostics ----
        elif name == "diagnostics_get_system_health":
            res = _call_diagnostics_method("GetSystemHealthJson")
            return _tool_response(res)

        elif name == "diagnostics_get_threat_prioritization":
            res = _call_diagnostics_method("GetThreatPrioritizationCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_get_production_census":
            res = _call_diagnostics_method("GetProductionCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_get_base_integrity":
            res = _call_diagnostics_method("GetBaseIntegrityCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_order_production":
            f_key = arguments.get("factoryKeyId", "")
            instant = bool(arguments.get("instant", False))
            for_free = bool(arguments.get("forFree", True))
            res = _call_diagnostics_method("OrderFactoryProduceJson", [f_key, instant, for_free])
            return _tool_response(res)

        elif name == "diagnostics_get_run_progression":
            res = _call_diagnostics_method("GetRunProgressionCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_grant_credits":
            amount = int(arguments.get("amount", 0))
            account = str(arguments.get("accountName", "Player"))
            res = _call_diagnostics_method("GrantPlayerCreditsJson", [amount, account])
            return _tool_response(res)

        elif name == "diagnostics_apply_card_change":
            val_to_change = str(arguments.get("valueToChange", ""))
            op = str(arguments.get("operation", "Add"))
            val = float(arguments.get("value", 0.0))
            side = str(arguments.get("side", "Friendly"))
            target_id = arguments.get("targetEntityId")
            args = [val_to_change, op, val, side]
            if target_id is not None:
                args.append(str(target_id))
            res = _call_diagnostics_method("ApplyInGameCardChangeJson", args)
            return _tool_response(res)

        elif name == "diagnostics_order_unit_move":
            e_key = str(arguments.get("entityKeyId", ""))
            x = float(arguments.get("destinationX", 0.0))
            z = float(arguments.get("destinationZ", 0.0))
            res = _call_diagnostics_method("OrderUnitMoveJson", [e_key, x, z])
            return _tool_response(res)

        elif name == "diagnostics_order_unit_attack":
            e_key = str(arguments.get("entityKeyId", ""))
            t_key = str(arguments.get("targetEntityKeyId", ""))
            res = _call_diagnostics_method("OrderUnitAttackJson", [e_key, t_key])
            return _tool_response(res)

        elif name == "diagnostics_order_unit_tactical_pulse":
            e_key = str(arguments.get("entityKeyId", ""))
            pulse_type = str(arguments.get("pulseType", "Shockwave"))
            radius = float(arguments.get("radius", 12.0))
            magnitude = float(arguments.get("magnitude", 0.0))
            duration = float(arguments.get("durationSeconds", 2.0))
            max_targets = int(arguments.get("maxTargets", 8))
            res = _call_diagnostics_method(
                "OrderUnitTacticalPulseJson",
                [e_key, pulse_type, radius, magnitude, duration, max_targets],
            )
            return _tool_response(res)

        elif name == "diagnostics_order_director_threat_sweep":
            source_key = str(arguments.get("sourceEntityKeyId", ""))
            sweep_type = str(arguments.get("sweepType", "Mark"))
            radius = float(arguments.get("radius", 12.0))
            duration = float(arguments.get("durationSeconds", 4.0))
            max_targets = int(arguments.get("maxTargets", 8))
            res = _call_diagnostics_method(
                "OrderDirectorThreatSweepJson",
                [source_key, sweep_type, radius, duration, max_targets],
            )
            return _tool_response(res)
        elif name == "diagnostics_order_coordinated_assault":
            target_key = str(arguments.get("targetEntityKeyId", ""))
            attacker_keys = arguments.get("attackerEntityKeyIds", [])
            if not isinstance(attacker_keys, list):
                attacker_keys = []
            attacker_keys = [str(value) for value in attacker_keys]
            max_attackers = int(arguments.get("maxAttackers", 8))
            attacker_keys_csv = ";".join(value.strip() for value in attacker_keys)
            res = _call_diagnostics_method(
                "OrderCoordinatedAssaultCsvJson",
                [target_key, attacker_keys_csv, max_attackers],
            )
            return _tool_response(res)
        elif name == "diagnostics_order_retreat":
            e_key = str(arguments.get("entityKeyId", ""))
            tx = float(arguments.get("threatX", 0.0))
            tz = float(arguments.get("threatZ", 0.0))
            dist = float(arguments.get("distance", 8.0))
            res = _call_diagnostics_method(
                "OrderRetreatJson",
                [e_key, tx, tz, dist],
            )
            return _tool_response(res)
        elif name == "diagnostics_order_active_skill":
            e_key = str(arguments.get("entityKeyId", ""))
            t_key = arguments.get("targetEntityKeyId")
            tx = arguments.get("targetX")
            tz = arguments.get("targetZ")
            has_pos = tx is not None and tz is not None
            args = [e_key, str(t_key) if t_key is not None else "", float(tx or 0.0), float(tz or 0.0), bool(has_pos)]
            res = _call_diagnostics_method("OrderUnitActiveSkillJson", args)
            return _tool_response(res)

        elif name == "diagnostics_adjust_vitality":
            e_key = str(arguments.get("entityKeyId", ""))
            adj_type = str(arguments.get("adjustmentType", "Heal"))
            amount = float(arguments.get("amount", 0.0))
            ignore_armor = bool(arguments.get("ignoreArmor", False))
            res = _call_diagnostics_method("AdjustEntityVitalityJson", [e_key, adj_type, amount, ignore_armor])
            return _tool_response(res)

        elif name == "diagnostics_apply_status_effect":
            e_key = str(arguments.get("entityKeyId", ""))
            effect_name = str(arguments.get("statusEffectName", ""))
            duration = float(arguments.get("durationSeconds", 10.0))
            res = _call_diagnostics_method("ApplyStatusEffectJson", [e_key, effect_name, duration])
            return _tool_response(res)

        elif name == "diagnostics_clear_status_effect":
            e_key = str(arguments.get("entityKeyId", ""))
            effect_name = str(arguments.get("statusEffectName", ""))
            res = _call_diagnostics_method("ClearStatusEffectJson", [e_key, effect_name])
            return _tool_response(res)

        elif name == "diagnostics_get_grid_metrics":
            res = _call_diagnostics_method("GetGridMetricsJson")
            return _tool_response(res)

        elif name == "diagnostics_evaluate_grid_position":
            x = float(arguments.get("x", 0.0))
            z = float(arguments.get("z", 0.0))
            res = _call_diagnostics_method("EvaluateGridPositionJson", [x, z])
            return _tool_response(res)

        elif name == "diagnostics_get_match_summary":
            res = _call_diagnostics_method("GetMatchSummaryJson")
            return _tool_response(res)

        elif name == "diagnostics_get_catalog":
            res = _call_diagnostics_method("GetDiagnosticCatalogJson")
            return _tool_response(res)

        elif name == "diagnostics_get_diagnostics":
            res = _call_diagnostics_method("GetDiagnosticsJson")
            return _tool_response(res)

        elif name == "diagnostics_get_utilities":
            res = _call_diagnostics_method("GetUtilitiesJson")
            return _tool_response(res)

        elif name == "diagnostics_get_range_colors":
            res = _call_diagnostics_method("GetRangeColorsJson")
            return _tool_response(res)

        elif name == "diagnostics_get_identifier_palette":
            res = _call_diagnostics_method("GetIdentifierPaletteJson")
            return _tool_response(res)

        elif name == "diagnostics_get_range_circle_point_count":
            segments = int(arguments.get("segments", 8))
            res = _call_diagnostics_method("GetRangeCirclePointCountJson", [segments])
            return _tool_response(res)

        elif name == "diagnostics_get_inspector_toggle_state":
            is_open = bool(arguments.get("isOpen", False))
            toggle_pressed = bool(arguments.get("togglePressed", False))
            close_pressed = bool(arguments.get("closePressed", False))
            res = _call_diagnostics_method("GetInspectorToggleStateJson", [is_open, toggle_pressed, close_pressed])
            return _tool_response(res)

        elif name == "diagnostics_resolve_identifier_color":
            identifier = str(arguments.get("identifier", ""))
            res = _call_diagnostics_method("ResolveIdentifierColor", [identifier])
            return _tool_response(res)

        elif name == "diagnostics_get_unit_mechanics_summary":
            entity_id = str(arguments.get("entityId", ""))
            res = _call_diagnostics_method("GetUnitMechanicsSummary", [entity_id])
            return _tool_response(res)

        elif name == "diagnostics_resolve_inspector_open_state":
            is_open = bool(arguments.get("isOpen", False))
            toggle_pressed = bool(arguments.get("togglePressed", False))
            close_pressed = bool(arguments.get("closePressed", False))
            res = _call_diagnostics_method("ResolveInspectorOpenState", [is_open, toggle_pressed, close_pressed])
            return _tool_response(res)

        elif name == "diagnostics_get_card_change_request":
            value_to_change = str(arguments.get("valueToChange", ""))
            operation = str(arguments.get("operation", "Add"))
            value = float(arguments.get("value", 0.0))
            side = str(arguments.get("side", "Friendly"))
            mult_heat = bool(arguments.get("multiplyValueWithCurrentHeatLevel", False))
            mult_meta = bool(arguments.get("multiplyValueWithMetaProgressionMultiplier", False))
            greater_zero = bool(arguments.get("changeableValueHasToBeGreaterZero", False))
            source_id = str(arguments.get("sourceId", "rcl-card-change"))
            res = _call_diagnostics_method("GetCardChangeRequestJsonFromParams", [value_to_change, operation, value, side, mult_heat, mult_meta, greater_zero, source_id])
            return _tool_response(res)

        elif name == "diagnostics_assign_control_group":
            group_number = int(arguments.get("groupNumber", 0))
            res = _call_diagnostics_method("AssignControlGroupJson", [group_number])
            return _tool_response(res)

        elif name == "diagnostics_deselect_all_units":
            res = _call_diagnostics_method("DeselectAllUnitsJson")
            return _tool_response(res)

        elif name == "diagnostics_get_entity_catalog":
            res = _call_diagnostics_method("GetEntityCatalogJson")
            return _tool_response(res)

        elif name == "diagnostics_get_fog_state":
            res = _call_diagnostics_method("GetFogStateJson")
            return _tool_response(res)

        elif name == "diagnostics_get_game_time_scale":
            res = _call_diagnostics_method("GetGameTimeScaleJson")
            return _tool_response(res)

        elif name == "diagnostics_get_selection_census":
            res = _call_diagnostics_method("GetSelectionCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_order_unit_attack_move":
            e_key = str(arguments.get("entityKeyId", ""))
            x = float(arguments.get("targetX", 0.0))
            z = float(arguments.get("targetZ", 0.0))
            res = _call_diagnostics_method("OrderUnitAttackMoveJson", [e_key, x, z])
            return _tool_response(res)

        elif name == "diagnostics_order_unit_patrol":
            e_key = str(arguments.get("entityKeyId", ""))
            wx = arguments.get("waypointX") or []
            wz = arguments.get("waypointZ") or []
            waypoint_x_csv = ";".join(str(float(v)) for v in wx)
            waypoint_z_csv = ";".join(str(float(v)) for v in wz)
            res = _call_diagnostics_method("OrderUnitPatrolCsvJson", [e_key, waypoint_x_csv, waypoint_z_csv])
            return _tool_response(res)

        elif name == "diagnostics_order_group_command":
            keys = arguments.get("entityKeyIds") or []
            cmd_type = str(arguments.get("commandType", ""))
            x = float(arguments.get("targetX", 0.0))
            z = float(arguments.get("targetZ", 0.0))
            keys_csv = ";".join(str(k).strip() for k in keys)
            res = _call_diagnostics_method("OrderGroupCommandCsvJson", [keys_csv, cmd_type, x, z])
            return _tool_response(res)

        elif name == "diagnostics_order_formation_move":
            keys = arguments.get("entityKeyIds") or []
            f_type = str(arguments.get("formationType", "Line"))
            x = float(arguments.get("targetX", 0.0))
            z = float(arguments.get("targetZ", 0.0))
            spacing = float(arguments.get("spacing", 4.0))
            keys_csv = ";".join(str(k).strip() for k in keys)
            res = _call_diagnostics_method("OrderFormationMoveCsvJson", [keys_csv, f_type, x, z, spacing])
            return _tool_response(res)

        elif name == "diagnostics_run_synthetic_gameplay_scenario":
            unit_ids_csv = str(arguments.get("unitIdsCsv", "")).replace(",", ";")
            target_x = float(arguments.get("targetX", 0.0))
            target_z = float(arguments.get("targetZ", 0.0))
            formation_type = str(arguments.get("formationType", "Wedge"))
            spacing = float(arguments.get("spacing", 4.0))
            res = _call_diagnostics_method(
                "RunSyntheticGameplayScenarioJson",
                [unit_ids_csv, target_x, target_z, formation_type, spacing],
            )
            return _tool_response(res)

        elif name == "diagnostics_order_unit_tactical_stance":
            e_key = str(arguments.get("entityKeyId", ""))
            s_name = str(arguments.get("stanceName", "Standard"))
            res = _call_diagnostics_method("OrderUnitTacticalStanceJson", [e_key, s_name])
            return _tool_response(res)

        elif name == "diagnostics_order_group_tactical_stance":
            keys = arguments.get("entityKeyIds") or []
            s_name = str(arguments.get("stanceName", "Standard"))
            keys_csv = ";".join(str(k).strip() for k in keys)
            res = _call_diagnostics_method("OrderGroupTacticalStanceCsvJson", [keys_csv, s_name])
            return _tool_response(res)

        elif name == "diagnostics_get_tactical_stance_census":
            res = _call_diagnostics_method("GetTacticalStanceCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_order_apply_unit_affix":
            e_key = str(arguments.get("entityKeyId", ""))
            a_name = str(arguments.get("affixName", ""))
            res = _call_diagnostics_method("OrderApplyUnitAffixJson", [e_key, a_name])
            return _tool_response(res)

        elif name == "diagnostics_order_remove_unit_affix":
            e_key = str(arguments.get("entityKeyId", ""))
            a_name = str(arguments.get("affixName", "All"))
            res = _call_diagnostics_method("OrderRemoveUnitAffixJson", [e_key, a_name])
            return _tool_response(res)

        elif name == "diagnostics_get_unit_affix_census":
            res = _call_diagnostics_method("GetUnitAffixCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_order_dispatch_ai_assault_wave":
            wave = int(arguments.get("waveNumber", 1))
            boost = float(arguments.get("aggressionBoost", 1.5))
            base_idx = int(arguments.get("targetBaseIndex", 0))
            res = _call_diagnostics_method("OrderDispatchAiAssaultWaveJson", [wave, boost, base_idx])
            return _tool_response(res)

        elif name == "diagnostics_order_unit_hold_position":
            e_key = arguments.get("entityKeyId")
            args = [str(e_key)] if e_key is not None else []
            res = _call_diagnostics_method("OrderUnitHoldPositionJson", args)
            return _tool_response(res)

        elif name == "diagnostics_order_unit_stop":
            e_key = arguments.get("entityKeyId")
            args = [str(e_key)] if e_key is not None else []
            res = _call_diagnostics_method("OrderUnitStopJson", args)
            return _tool_response(res)

        elif name == "diagnostics_reveal_fog_of_war":
            res = _call_diagnostics_method("RevealFogOfWarJson")
            return _tool_response(res)

        elif name == "diagnostics_select_category":
            category = str(arguments.get("category", ""))
            res = _call_diagnostics_method("SelectCategoryJson", [category])
            return _tool_response(res)

        elif name == "diagnostics_select_control_group":
            group_number = int(arguments.get("groupNumber", 0))
            res = _call_diagnostics_method("SelectControlGroupJson", [group_number])
            return _tool_response(res)

        elif name == "diagnostics_select_units":
            csv = str(arguments.get("entityKeysCsv", ""))
            additive = bool(arguments.get("additive", False))
            res = _call_diagnostics_method("SelectUnitsJson", [csv, additive])
            return _tool_response(res)

        elif name == "diagnostics_set_fog_of_war":
            disable_fog = bool(arguments.get("disableFog", False))
            res = _call_diagnostics_method("SetFogOfWarJson", [disable_fog])
            return _tool_response(res)

        elif name == "diagnostics_set_game_time_scale":
            time_scale = float(arguments.get("timeScale", 1.0))
            res = _call_diagnostics_method("SetGameTimeScaleJson", [time_scale])
            return _tool_response(res)

        elif name == "diagnostics_spawn_entity":
            entity_id = str(arguments.get("entityId", ""))
            side = str(arguments.get("side", "Friendly"))
            x = float(arguments.get("targetX", 0.0))
            z = float(arguments.get("targetZ", 0.0))
            custom_name = str(arguments.get("customName", ""))
            res = _call_diagnostics_method("SpawnEntityJson", [entity_id, side, x, z, custom_name])
            return _tool_response(res)

        elif name == "diagnostics_trigger_match_end":
            win = bool(arguments.get("win", True))
            res = _call_diagnostics_method("TriggerMatchEndJson", [win])
            return _tool_response(res)

        elif name == "diagnostics_get_ai_director_census":
            res = _call_diagnostics_method("GetAiDirectorCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_set_attack_wave_number":
            wave = int(arguments.get("waveNumber", 0))
            res = _call_diagnostics_method("SetAttackWaveNumberJson", [wave])
            return _tool_response(res)

        elif name == "diagnostics_increment_attack_wave":
            res = _call_diagnostics_method("IncrementAttackWaveJson")
            return _tool_response(res)

        elif name == "diagnostics_toggle_ai_debug_visuals":
            tags = bool(arguments.get("showTags", True))
            cmds = bool(arguments.get("showCommands", True))
            res = _call_diagnostics_method("ToggleAiDebugVisualsJson", [tags, cmds])
            return _tool_response(res)

        elif name == "diagnostics_get_camera_census":
            res = _call_diagnostics_method("GetCameraCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_move_camera":
            x = float(arguments.get("targetX", 0.0))
            z = float(arguments.get("targetZ", 0.0))
            res = _call_diagnostics_method("MoveCameraJson", [x, z])
            return _tool_response(res)

        elif name == "diagnostics_focus_camera_on_entity":
            key_id = str(arguments.get("entityKeyId", ""))
            res = _call_diagnostics_method("FocusCameraOnEntityJson", [key_id])
            return _tool_response(res)

        elif name == "diagnostics_set_camera_zoom":
            fov = float(arguments.get("fieldOfView", 45.0))
            res = _call_diagnostics_method("SetCameraZoomJson", [fov])
            return _tool_response(res)

        elif name == "diagnostics_get_harvester_census":
            res = _call_diagnostics_method("GetHarvesterCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_order_harvester_harvest":
            key_id = str(arguments.get("harvesterKeyId", ""))
            res = _call_diagnostics_method("OrderHarvesterHarvestJson", [key_id])
            return _tool_response(res)

        elif name == "diagnostics_order_harvester_return":
            key_id = str(arguments.get("harvesterKeyId", ""))
            res = _call_diagnostics_method("OrderHarvesterReturnJson", [key_id])
            return _tool_response(res)

        elif name == "diagnostics_get_audio_census":
            res = _call_diagnostics_method("GetAudioCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_get_visual_surface_census":
            max_units = int(arguments.get("maxTrackedUnits", 64))
            res = _call_diagnostics_method("GetVisualSurfaceCensusJson", [max_units])
            return _tool_response(res)

        elif name == "diagnostics_get_visual_quality_census":
            max_units = int(arguments.get("maxTrackedUnits", 64))
            res = _call_diagnostics_method("GetVisualQualityCensusJson", [max_units])
            return _tool_response(res)

        elif name == "diagnostics_get_visual_material_census":
            max_units = int(arguments.get("maxTrackedUnits", 64))
            res = _call_diagnostics_method("GetVisualMaterialCensusJson", [max_units])
            return _tool_response(res)

        elif name == "diagnostics_get_socket_audit_census":
            max_units = int(arguments.get("maxTrackedUnits", 64))
            res = _call_diagnostics_method("GetSocketAuditCensusJson", [max_units])
            return _tool_response(res)

        elif name == "diagnostics_get_collider_audit_census":
            res = _call_diagnostics_method("GetColliderAuditCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_get_particle_vfx_census":
            res = _call_diagnostics_method("GetParticleVfxCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_get_lod_audit_census":
            res = _call_diagnostics_method("GetLodAuditCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_set_audio_volume":
            channel = str(arguments.get("channel", "Master"))
            volume = float(arguments.get("volumeSlider", 1.0))
            res = _call_diagnostics_method("SetAudioVolumeJson", [channel, volume])
            return _tool_response(res)

        elif name == "diagnostics_get_veterancy_census":
            res = _call_diagnostics_method("GetVeterancyCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_order_grant_combat_xp":
            entity_key_id = arguments.get("entityKeyId")
            xp_amount = arguments.get("xpAmount")
            res = _call_diagnostics_method("OrderGrantCombatXpJson", [entity_key_id, xp_amount])
            return _tool_response(res)

        elif name == "diagnostics_order_set_veterancy_level":
            entity_key_id = arguments.get("entityKeyId")
            target_level = arguments.get("targetLevel")
            res = _call_diagnostics_method("OrderSetVeterancyLevelJson", [entity_key_id, target_level])
            return _tool_response(res)

        elif name == "diagnostics_get_command_aura_census":
            res = _call_diagnostics_method("GetCommandAuraCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_order_command_aura_pulse":
            captain_key_id = arguments.get("captainKeyId")
            aura_type = arguments.get("auraType")
            radius = arguments.get("radius")
            duration_seconds = arguments.get("durationSeconds")
            res = _call_diagnostics_method("OrderCommandAuraPulseJson", [captain_key_id, aura_type, radius, duration_seconds])
            return _tool_response(res)

        elif name == "diagnostics_order_clear_command_aura_links":
            captain_key_id = arguments.get("captainKeyId")
            res = _call_diagnostics_method("OrderClearCommandAuraLinksJson", [captain_key_id])
            return _tool_response(res)

        elif name == "diagnostics_get_hazard_census":
            res = _call_diagnostics_method("GetHazardCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_order_spawn_hazard_zone":
            hazard_type = arguments.get("hazardType")
            x = arguments.get("x")
            z = arguments.get("z")
            radius = arguments.get("radius")
            duration_seconds = arguments.get("durationSeconds")
            tick_damage = arguments.get("tickDamage")
            res = _call_diagnostics_method("OrderSpawnHazardZoneJson", [hazard_type, x, z, radius, duration_seconds, tick_damage])
            return _tool_response(res)

        elif name == "diagnostics_order_clear_hazard_zones":
            res = _call_diagnostics_method("OrderClearHazardZonesJson")
            return _tool_response(res)

        elif name == "diagnostics_get_unit_loadout_census":
            res = _call_diagnostics_method("GetUnitLoadoutCensusJson", [])
            return _tool_response(res)

        elif name == "diagnostics_order_apply_unit_loadout":
            entity_key_id = arguments.get("entityKeyId")
            loadout_id = arguments.get("loadoutId")
            res = _call_diagnostics_method("OrderApplyUnitLoadoutJson", [entity_key_id, loadout_id])
            return _tool_response(res)

        elif name == "diagnostics_order_apply_visual_variant":
            entity_key_id = arguments.get("entityKeyId")
            variant_id = arguments.get("variantId")
            res = _call_diagnostics_method("OrderApplyUnitVisualVariantJson", [entity_key_id, variant_id])
            return _tool_response(res)

        elif name == "diagnostics_get_visual_variant_census":
            max_units = arguments.get("maxUnits", 64)
            res = _call_diagnostics_method("GetVisualVariantCensusJson", [max_units])
            return _tool_response(res)

        elif name == "diagnostics_order_warm_prespawn_pool":
            entity_id = arguments.get("entityId")
            count = arguments.get("count")
            res = _call_diagnostics_method("OrderWarmPrespawnPoolJson", [entity_id, count])
            return _tool_response(res)

        elif name == "diagnostics_get_ai_doctrine_census":
            res = _call_diagnostics_method("GetAiDoctrineCensusJson", [])
            return _tool_response(res)

        elif name == "diagnostics_order_set_ai_doctrine":
            doctrine_name = arguments.get("doctrineName")
            aggression_multiplier = arguments.get("aggressionMultiplier")
            res = _call_diagnostics_method("OrderSetAiDoctrineJson", [doctrine_name, aggression_multiplier])
            return _tool_response(res)

        elif name == "diagnostics_order_inject_ai_deck_unit":
            base_index = arguments.get("baseIndex")
            unit_id = arguments.get("unitId")
            count_at_start = arguments.get("countAtStart")
            max_living = arguments.get("maxLiving")
            res = _call_diagnostics_method("OrderInjectAiDeckUnitJson", [base_index, unit_id, count_at_start, max_living])
            return _tool_response(res)

        elif name == "diagnostics_get_agent_coordination":
            res = _call_diagnostics_method("GetAgentCoordinationCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_register_agent_session":
            agent_id = str(arguments.get("agentId", ""))
            agent_role = str(arguments.get("agentRole", "Agent"))
            notes = str(arguments.get("notes", ""))
            if "assignedSector" in arguments:
                assigned_sector = str(arguments.get("assignedSector", "Global"))
                res = _call_diagnostics_method("RegisterAgentSessionJson", [agent_id, agent_role, notes, assigned_sector])
            else:
                res = _call_diagnostics_method("RegisterAgentSessionJson", [agent_id, agent_role, notes])
            return _tool_response(res)

        elif name == "diagnostics_deregister_agent_session":
            agent_id = str(arguments.get("agentId", ""))
            res = _call_diagnostics_method("DeregisterAgentSessionJson", [agent_id])
            return _tool_response(res)

        elif name == "diagnostics_ping_agent":
            agent_id = str(arguments.get("agentId", ""))
            message = str(arguments.get("message", ""))
            res = _call_diagnostics_method("PingAgentJson", [agent_id, message])
            return _tool_response(res)

        elif name == "diagnostics_post_agent_message":
            sender_agent_id = str(arguments.get("senderAgentId") or arguments.get("senderId", ""))
            message_type = str(arguments.get("messageType", "TacticalIntent"))
            content = str(arguments.get("content") or arguments.get("payload", ""))
            target_agent_id = str(arguments.get("targetAgentId") or arguments.get("recipientId", "*"))
            res = _call_diagnostics_method("PostAgentMessageJson", [sender_agent_id, message_type, content, target_agent_id])
            return _tool_response(res)

        elif name == "diagnostics_acquire_agent_lock":
            agent_id = str(arguments.get("agentId", ""))
            resource_key = str(arguments.get("resourceKey", ""))
            duration_seconds = float(arguments.get("durationSeconds", 30.0))
            res = _call_diagnostics_method("AcquireAgentLockJson", [agent_id, resource_key, duration_seconds])
            return _tool_response(res)

        elif name == "diagnostics_acquire_agent_lock_batch":
            agent_id = str(arguments.get("agentId", ""))
            raw_keys = arguments.get("resourceKeys") or []
            if isinstance(raw_keys, list):
                keys = [str(k) for k in raw_keys]
            else:
                keys = [k.strip() for k in str(raw_keys).split(",") if k.strip()]
            keys_csv = ";".join(k.strip() for k in keys)
            duration_seconds = float(arguments.get("durationSeconds", 30.0))
            res = _call_diagnostics_method("AcquireAgentLockBatchCsvJson", [agent_id, keys_csv, duration_seconds])
            return _tool_response(res)

        elif name == "diagnostics_release_agent_lock":
            agent_id = str(arguments.get("agentId", ""))
            resource_key = str(arguments.get("resourceKey", ""))
            res = _call_diagnostics_method("ReleaseAgentLockJson", [agent_id, resource_key])
            return _tool_response(res)

        elif name == "diagnostics_release_agent_lock_batch":
            agent_id = str(arguments.get("agentId", ""))
            raw_keys = arguments.get("resourceKeys") or []
            if isinstance(raw_keys, list):
                keys = [str(k) for k in raw_keys]
            else:
                keys = [k.strip() for k in str(raw_keys).split(",") if k.strip()]
            keys_csv = ";".join(k.strip() for k in keys)
            res = _call_diagnostics_method("ReleaseAgentLockBatchCsvJson", [agent_id, keys_csv])
            return _tool_response(res)

        elif name == "diagnostics_get_tech_catalog":
            res = _call_diagnostics_method("GetTechCatalogJson")
            return _tool_response(res)

        elif name == "diagnostics_get_upgrade_census":
            max_entries = int(arguments.get("maxEntries", 64))
            res = _call_diagnostics_method("GetUpgradeCensusJson", [max_entries])
            return _tool_response(res)

        elif name == "diagnostics_get_relic_census":
            max_entries = int(arguments.get("maxEntries", 64))
            res = _call_diagnostics_method("GetRelicCensusJson", [max_entries])
            return _tool_response(res)

        elif name == "diagnostics_runtime_scalars":
            scalars = {}
            for member_name in [
                "LiveBattlefieldEntityCount",
                "LivePlayerUnitCount",
                "LiveAiUnitCount",
                "LiveAiBuildingCount",
                "LiveHarvesterCount",
                "RegisteredUnitMechanicsProfileCount",
                "RangeDisplayGeometryMinimumSegments",
                "RangeDisplayGeometryMaximumSegments",
                "RegisteredAgentSessionCount",
                "ActiveAgentLockCount",
                "ActiveAgentMessageCount",
                "ActiveHazardZoneCount",
                "ActiveCommandAuraCount",
                "TrackedVeterancyUnitCount",
                "LiveSkinnedMeshUnitCount",
                "LiveAnimatedUnitCount",
                "LivePbrShaderUnitCount",
                "LiveVisualQualityPassCount",
                "LiveVisualQualityIssueCount",
                "LiveVertexWearSampleCount",
                "LiveTotalUpgradeCount",
                "LiveActiveUpgradeCount",
                "LiveModdedUpgradeCount",
                "LiveTotalRelicCount",
                "LiveActiveRelicCount",
                "LiveModdedRelicCount",
                "AuditedUnitPrefabCount",
                "LiveMechanicsDirectorCount",
                "LiveVisualDirectorCount",
                "LiveJammedTurretCount",
                "LiveRegeneratingShieldCount",
                "ActivePackCount",
                "FailedPackCount",
                "RegisteredUnitCount",
                "RegisteredStageCount",
                "RegisteredAiProfileCount",
                "RegisteredAiModuleCount",
                "RegisteredRuntimeCombatXpCount",
                "RuntimeInstalledHookCount",
                "LivePlayerCredits",
                "LiveAiCredits",
                "LiveAttackWaveNumber",
                "LiveCombatActiveUnitCount",
                "LiveUnitsWithStatusEffectsCount",
                "LiveTotalFactoryCount",
                "LiveActiveProducingFactoryCount",
                "LiveQueuedUnitCount",
                "LiveHasPlayerHq",
                "LivePlayerHqHealthRatio",
                "LiveAiStructureHealthRatio",
            ]:
                scalars[member_name] = _call_diagnostics_member(member_name)

            try:
                raw_point_count = _call_diagnostics_method("GetRangeCirclePointCount", [8])
                try:
                    scalars["RangeDisplayCirclePointCount"] = json.loads(raw_point_count)
                except Exception:
                    scalars["RangeDisplayCirclePointCount"] = raw_point_count
            except Exception as exc:
                scalars["RangeDisplayCirclePointCount"] = {"error": str(exc)}
            return _tool_response(json.dumps(scalars, indent=2))

        elif name == "diagnostics_runtime_snapshot":
            requested = arguments.get("getters")
            excluded = set(arguments.get("exclude") or [])
            getters = list(_RUNTIME_SNAPSHOT_GETTERS)
            if requested:
                req_set = set(str(x) for x in requested)
                getters = [g for g in getters if g in req_set or g.replace("Json", "") in req_set]
            getters = [g for g in getters if g not in excluded]

            started = time.perf_counter()
            snapshot = {}
            success_count = 0
            error_count = 0
            timings = {}

            def _fetch(getter):
                t0 = time.perf_counter()
                try:
                    raw = _call_diagnostics_method(getter, _snapshot_getter_args(getter))
                    try:
                        value = json.loads(raw)
                    except Exception:
                        value = raw
                    return getter, value, None, (time.perf_counter() - t0) * 1000.0
                except Exception as exc:
                    return getter, {"error": str(exc)}, str(exc), (time.perf_counter() - t0) * 1000.0

            with ThreadPoolExecutor(max_workers=8) as executor:
                futures = {executor.submit(_fetch, g): g for g in getters}
                for future in as_completed(futures):
                    getter, value, err, elapsed = future.result()
                    snapshot[getter] = value
                    timings[getter] = round(elapsed, 2)
                    if err is None:
                        success_count += 1
                    else:
                        error_count += 1

            snapshot["_meta"] = {
                "getterCount": len(getters),
                "successCount": success_count,
                "errorCount": error_count,
                "elapsedMs": round((time.perf_counter() - started) * 1000.0, 2),
                "timingsMs": timings,
            }
            return _tool_response(json.dumps(snapshot, indent=2))

        elif name == "diagnostics_list_runtime_getters":
            return _tool_response(json.dumps({"count": len(_RUNTIME_SNAPSHOT_GETTERS), "getters": _RUNTIME_SNAPSHOT_GETTERS}, indent=2))

        elif name == "diagnostics_call_getters":
            requested = arguments.get("getters") or []
            getters = [str(g) for g in requested]
            started = time.perf_counter()
            results = {}
            timings = {}

            def _fetch(getter):
                t0 = time.perf_counter()
                try:
                    raw = _call_diagnostics_method(getter, _snapshot_getter_args(getter))
                    try:
                        value = json.loads(raw)
                    except Exception:
                        value = raw
                    return getter, value, None, (time.perf_counter() - t0) * 1000.0
                except Exception as exc:
                    return getter, {"error": str(exc)}, str(exc), (time.perf_counter() - t0) * 1000.0

            with ThreadPoolExecutor(max_workers=8) as executor:
                futures = {executor.submit(_fetch, g): g for g in getters}
                for future in as_completed(futures):
                    getter, value, err, elapsed = future.result()
                    results[getter] = value
                    timings[getter] = round(elapsed, 2)
            results["_meta"] = {
                "getterCount": len(getters),
                "elapsedMs": round((time.perf_counter() - started) * 1000.0, 2),
                "timingsMs": timings,
            }
            return _tool_response(json.dumps(results, indent=2))

        elif name == "diagnostics_runtime_summary":
            keys = ["GetSystemHealthJson", "GetBattlefieldCensusJson", "GetProductionCensusJson",
                    "GetBaseIntegrityCensusJson", "GetHazardCensusJson", "GetVeterancyCensusJson"]
            summary = {}
            for g in keys:
                try:
                    raw = _call_diagnostics_method(g, _snapshot_getter_args(g))
                    try:
                        summary[g] = json.loads(raw)
                    except Exception:
                        summary[g] = raw
                except Exception as exc:
                    summary[g] = {"error": str(exc)}
            return _tool_response(json.dumps(summary, indent=2))

        elif name == "diagnostics_call_getter":
            getter = arguments.get("getterName", "").strip()
            if not getter:
                return _tool_response(json.dumps({"error": "getterName is required"}), is_error=True)
            args_list = arguments.get("args") or []
            res = _call_diagnostics_method(getter, args_list)
            return _tool_response(res)

        elif name == "diagnostics_get_visual_census":
            max_units = arguments.get("maxTrackedUnits", 32)
            res = _call_diagnostics_method("GetVisualRenderCensusJson", [max_units])
            return _tool_response(res)

        elif name == "diagnostics_inspect_entity_visual":
            entity_key_id = str(arguments.get("entityKeyId", "")).strip()
            res = _call_diagnostics_method("InspectEntityVisualJson", [entity_key_id])
            return _tool_response(res)

        elif name == "diagnostics_get_loaded_unit_visual_audit":
            max_entries = int(arguments.get("maxEntries", 64))
            res = _call_diagnostics_method("GetLoadedPrefabVisualAuditJson", [max_entries])
            return _tool_response(res)

        elif name == "diagnostics_get_loader_audit_census":
            max_units = int(arguments.get("maxUnits", 64))
            max_ai_profiles = int(arguments.get("maxAiProfiles", 32))
            res = _call_diagnostics_method("GetLoaderAuditCensusJson", [max_units, max_ai_profiles])
            return _tool_response(res)

        elif name == "diagnostics_order_reconcile_loader_state":
            res = _call_diagnostics_method("OrderReconcileLoaderStateJson")
            return _tool_response(res)

        elif name == "diagnostics_get_unit_mechanics_census":
            max_units = int(arguments.get("maxUnits", 64))
            res = _call_diagnostics_method("GetUnitMechanicsCensusJson", [max_units])
            return _tool_response(res)

        elif name == "diagnostics_order_unit_visual_feedback":
            entity_key = str(arguments.get("entityKeyId", "")).strip()
            feedback_type = str(arguments.get("feedbackType", "")).strip()
            magnitude = float(arguments.get("magnitude", 0.22))
            res = _call_diagnostics_method("OrderUnitVisualFeedbackJson", [entity_key, feedback_type, magnitude])
            return _tool_response(res)

        elif name == "diagnostics_order_unit_attachment":
            entity_key = str(arguments.get("entityKeyId", "")).strip()
            attachment_type = str(arguments.get("attachmentType", "")).strip()
            active = bool(arguments.get("active", True))
            res = _call_diagnostics_method("OrderUnitAttachmentJson", [entity_key, attachment_type, active])
            return _tool_response(res)

        elif name == "diagnostics_get_battlefield_census":
            res = _call_diagnostics_method("GetBattlefieldCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_get_player_roster":
            res = _call_diagnostics_method("GetPlayerRosterJson")
            return _tool_response(res)

        elif name == "diagnostics_get_ai_roster":
            res = _call_diagnostics_method("GetAiRosterJson")
            return _tool_response(res)

        elif name == "diagnostics_get_unit_threat_assessment":
            res = _call_diagnostics_method("GetUnitThreatAssessmentJson")
            return _tool_response(res)

        elif name == "diagnostics_get_economy_match_census":
            res = _call_diagnostics_method("GetEconomyMatchCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_get_combat_status_census":
            res = _call_diagnostics_method("GetCombatStatusCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_get_entity_balance":
            eid = arguments.get("entityId", "")
            res = _call_diagnostics_method("GetEntityBalanceJson", [eid])
            return _tool_response(res)

        elif name == "diagnostics_get_runtime_hooks":
            res = _call_diagnostics_method("GetRuntimeHooksStateJson")
            return _tool_response(res)

        elif name == "diagnostics_get_registered_profiles":
            res = _call_diagnostics_method("GetRegisteredProfilesJson")
            return _tool_response(res)

        elif name == "diagnostics_get_installed_patches":
            res = _call_diagnostics_method("GetInstalledPatchNamesJson")
            return _tool_response(res)

        elif name == "diagnostics_get_applied_baseline_effects":
            res = _call_diagnostics_method("GetAppliedBaselineEffectsJson")
            return _tool_response(res)

        elif name == "diagnostics_get_combat_xp_entries":
            res = _call_diagnostics_method("GetCombatXpEntriesJson")
            return _tool_response(res)

        elif name == "diagnostics_get_ai_modules":
            res = _call_diagnostics_method("GetAiModulesDiagnosticsJson")
            return _tool_response(res)

        elif name == "diagnostics_get_visual_health_summary":
            max_units = int(arguments.get("maxTrackedUnits", 64))
            res = _call_diagnostics_method("GetVisualHealthSummaryJson", [max_units])
            return _tool_response(res)

        elif name == "diagnostics_get_performance_census":
            res = _call_diagnostics_method("GetPerformanceCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_get_frame_timing_census":
            res = _call_diagnostics_method("GetFrameTimingCensusJson")
            return _tool_response(res)

        elif name == "diagnostics_diff_snapshots":
            before_json = str(arguments.get("beforeJson", "") or "")
            after_json = str(arguments.get("afterJson", "") or "")
            max_changes = int(arguments.get("maxChanges", 16))
            res = _call_diagnostics_method("GetSnapshotDiffJson", [before_json, after_json, max_changes])
            return _tool_response(res)

        elif name == "diagnostics_get_combat_health_summary":
            getters = [
                "GetBattlefieldCensusJson", "GetPlayerRosterJson", "GetAiRosterJson",
                "GetProductionCensusJson", "GetBaseIntegrityCensusJson",
                "GetAiDirectorCensusJson", "GetHazardCensusJson",
                "GetVeterancyCensusJson", "GetCombatStatusCensusJson",
                "GetEconomyMatchCensusJson", "GetSystemHealthJson",
            ]
            started = time.perf_counter()
            summary: dict[str, Any] = {}
            timings: dict[str, float] = {}

            def _fetch(getter: str):
                t0 = time.perf_counter()
                raw = _call_diagnostics_method(getter, _snapshot_getter_args(getter))
                try:
                    value = json.loads(raw)
                except Exception:
                    value = raw
                return getter, value, (time.perf_counter() - t0) * 1000.0

            with ThreadPoolExecutor(max_workers=8) as executor:
                futures = {executor.submit(_fetch, g): g for g in getters}
                for future in as_completed(futures):
                    getter, value, elapsed = future.result()
                    summary[getter] = value
                    timings[getter] = round(elapsed, 2)
            summary["_meta"] = {
                "getterCount": len(getters),
                "elapsedMs": round((time.perf_counter() - started) * 1000.0, 2),
                "timingsMs": timings,
            }
            return _tool_response(json.dumps(summary, indent=2))

        elif name == "diagnostics_get_entity_detail":
            entity_key = str(arguments.get("entityKeyId", "")).strip()
            if not entity_key:
                return _tool_response(json.dumps({"error": "entityKeyId is required"}), is_error=True)
            detail_getters: list[tuple[str, list[object]]] = [
                ("GetBattlefieldCensusJson", []),
                ("GetPlayerRosterJson", []),
                ("GetAiRosterJson", []),
                ("GetUnitThreatAssessmentJson", []),
                ("GetTacticalStanceCensusJson", []),
                ("GetUnitAffixCensusJson", []),
                ("GetVeterancyCensusJson", []),
                ("GetUnitLoadoutCensusJson", []),
                ("GetUnitMechanicsCensusJson", [64]),
                ("InspectEntityVisualJson", [entity_key]),
                ("GetEntityBalanceJson", [entity_key]),
            ]
            started = time.perf_counter()

            def _fetch_detail(getter: str, getter_args: list[object]):
                t0 = time.perf_counter()
                raw = _call_diagnostics_method(getter, getter_args)
                try:
                    value = json.loads(raw)
                except Exception:
                    value = raw
                return getter, value, (time.perf_counter() - t0) * 1000.0

            sections: dict[str, Any] = {}
            timings: dict[str, float] = {}
            with ThreadPoolExecutor(max_workers=8) as executor:
                futures = {executor.submit(_fetch_detail, g, a): g for g, a in detail_getters}
                for future in as_completed(futures):
                    getter, value, elapsed = future.result()
                    timings[getter] = round(elapsed, 2)
                    matched: list[object] = []
                    if isinstance(value, dict):
                        for candidate in value.values():
                            if isinstance(candidate, list):
                                for item in candidate:
                                    if isinstance(item, dict) and (
                                        item.get("entityKeyId") == entity_key
                                        or item.get("entityId") == entity_key
                                        or item.get("entityKey") == entity_key
                                    ):
                                        matched.append(item)
                        if not matched and (value.get("entityKeyId") == entity_key or value.get("entityId") == entity_key):
                            matched.append(value)
                    sections[getter] = {"matchedRows": matched, "raw": value}
            matched_total = sum(len((s.get("matchedRows") or [])) for s in sections.values() if isinstance(s, dict))
            detail: dict[str, Any] = {
                "entityKeyId": entity_key,
                "found": matched_total > 0,
                "matchedRowCount": matched_total,
                "sections": sections,
                "_meta": {
                    "getterCount": len(detail_getters),
                    "elapsedMs": round((time.perf_counter() - started) * 1000.0, 2),
                    "timingsMs": timings,
                },
            }
            return _tool_response(json.dumps(detail, indent=2))

        elif name == "diagnostics_get_agent_lock_status":
            agent_id = str(arguments.get("agentId", "")).strip()
            resource_key = str(arguments.get("resourceKey", "")).strip()
            if not agent_id or not resource_key:
                return _tool_response(json.dumps({"error": "agentId and resourceKey are required"}), is_error=True)
            raw = _call_diagnostics_method("GetAgentCoordinationCensusJson")
            try:
                coordination = json.loads(raw)
            except Exception:
                coordination = {}
            if not isinstance(coordination, dict):
                coordination = {}
            sessions = coordination.get("sessions") if isinstance(coordination.get("sessions"), list) else []
            locks = coordination.get("locks") if isinstance(coordination.get("locks"), list) else []
            session_active = any(isinstance(s, dict) and s.get("agentId") == agent_id for s in sessions)
            lock_entry = None
            for entry in locks:
                if isinstance(entry, dict) and entry.get("resourceKey") == resource_key:
                    lock_entry = entry
                    break
            if lock_entry is None:
                status: dict[str, Any] = {
                    "agentId": agent_id,
                    "resourceKey": resource_key,
                    "sessionActive": session_active,
                    "locked": False,
                    "ownerAgentId": None,
                    "remainingSeconds": 0.0,
                    "contentionCount": 0,
                    "resourceCategory": None,
                    "lock": None,
                    "activeLockCount": coordination.get("activeLockCount", len(locks)),
                    "activeSessionCount": coordination.get("activeSessionCount", len(sessions)),
                }
            else:
                status = {
                    "agentId": agent_id,
                    "resourceKey": resource_key,
                    "sessionActive": session_active,
                    "locked": True,
                    "ownerAgentId": lock_entry.get("ownerAgentId"),
                    "remainingSeconds": lock_entry.get("remainingSeconds", 0.0),
                    "contentionCount": lock_entry.get("contentionCount", 0),
                    "resourceCategory": lock_entry.get("resourceCategory"),
                    "lock": lock_entry,
                    "activeLockCount": coordination.get("activeLockCount", len(locks)),
                    "activeSessionCount": coordination.get("activeSessionCount", len(sessions)),
                }
            return _tool_response(json.dumps(status, indent=2))

        elif name == "diagnostics_preflight":
            calls: list[str] = []
            checks: dict[str, Any] = {}
            bridge_alive = False
            component_found = False
            component_go_id = None
            system_health: dict[str, Any] = {}
            time_scale: dict[str, Any] = {}
            try:
                # system/ping
                try:
                    ping_raw = _call_http("system/ping")
                    calls.append("system/ping")
                    ping_data = json.loads(ping_raw) if isinstance(ping_raw, str) else ping_raw
                    checks["ping"] = "ok" if not (isinstance(ping_data, dict) and "error" in ping_data) else "fail"
                    bridge_alive = checks["ping"] == "ok"
                except Exception:
                    checks["ping"] = "fail"
                # system/capabilities
                try:
                    cap_raw = _call_http("system/capabilities")
                    calls.append("system/capabilities")
                    cap_data = json.loads(cap_raw) if isinstance(cap_raw, str) else cap_raw
                    checks["capabilities"] = "ok" if not (isinstance(cap_data, dict) and "error" in cap_data) else "fail"
                except Exception:
                    checks["capabilities"] = "fail"
                # find component
                go_id = None
                try:
                    raw = _call_http("scene:find_objects_with_component", params={"componentName": "LoaderDiagnosticsComponent"})
                    calls.append("scene:find_objects_with_component")
                    data = json.loads(raw) if isinstance(raw, str) else raw
                    items = data if isinstance(data, list) else ([data] if isinstance(data, dict) and "id" in data else [])
                    if items and isinstance(items[0], dict) and "id" in items[0]:
                        go_id = int(items[0]["id"])
                        component_found = True
                        component_go_id = go_id
                except Exception:
                    pass
                if go_id is not None:
                    global _DIAGNOSTICS_GO_ID
                    _DIAGNOSTICS_GO_ID = go_id
                    # system health
                    try:
                        sh_raw = _call_diagnostics_method("GetSystemHealthJson")
                        sh_val = _extract_bridge_return_value(sh_raw) if isinstance(sh_raw, str) else None
                        if sh_val is not None:
                            system_health = json.loads(sh_val)
                        else:
                            try:
                                system_health = json.loads(sh_raw)
                            except Exception:
                                system_health = {}
                    except Exception:
                        system_health = {}
                    # time scale
                    try:
                        ts_raw = _call_diagnostics_method("GetGameTimeScaleJson")
                        ts_val = _extract_bridge_return_value(ts_raw) if isinstance(ts_raw, str) else None
                        if ts_val is not None:
                            time_scale = json.loads(ts_val)
                        else:
                            try:
                                time_scale = json.loads(ts_raw)
                            except Exception:
                                time_scale = {}
                    except Exception:
                        time_scale = {}
                ready = bridge_alive and component_found
                result = {
                    "ready": ready,
                    "bridgeAlive": bridge_alive,
                    "componentFound": component_found,
                    "componentGoId": component_go_id,
                    "checks": checks,
                    "systemHealth": system_health,
                    "timeScale": time_scale,
                    "_meta": {
                        "count": len(calls),
                        "successCount": sum(1 for c in calls if True),
                        "errorCount": 0,
                    },
                }
                return _tool_response(json.dumps(result, indent=2))
            except Exception as exc:
                result = {
                    "ready": False,
                    "bridgeAlive": False,
                    "componentFound": False,
                    "componentGoId": None,
                    "checks": checks,
                    "systemHealth": {},
                    "timeScale": {},
                    "_meta": {"count": len(calls), "successCount": 0, "errorCount": 1},
                }
                return _tool_response(json.dumps(result, indent=2))

        elif name == "diagnostics_get_gui_state":
            # Fast path: single composite getter (1 round-trip vs 6).
            # _call_diagnostics_method already extracts the bridge ReturnValue,
            # so parse fast_raw directly instead of re-extracting (which would
            # always return None and silently disable the fast path).
            try:
                fast_raw = _call_diagnostics_method("GetGuiStateJson")
                parsed = json.loads(fast_raw) if isinstance(fast_raw, str) else fast_raw
                if isinstance(parsed, dict) and parsed.get("kind") == "guiState":
                    return _tool_response(json.dumps(parsed, indent=2))
            except Exception:
                pass
            member_values = {}
            member_names = ("OverlayOpen", "OverlayReady", "RangeDisplayReady", "IsRuntimeHooksInstalled")
            state: dict[str, Any] = {}
            for mn in member_names:
                try:
                    val = _call_diagnostics_member(mn)
                    if isinstance(val, dict) and "error" in val:
                        val = None
                    state[mn] = val
                except Exception:
                    state[mn] = None
            # Map to expected keys
            result = {
                "overlayOpen": state.get("OverlayOpen"),
                "overlayReady": state.get("OverlayReady"),
                "rangeDisplayReady": state.get("RangeDisplayReady"),
                "hooksInstalled": state.get("IsRuntimeHooksInstalled"),
            }
            # time scale
            try:
                ts_raw = _call_diagnostics_method("GetGameTimeScaleJson")
                ts_val = _extract_bridge_return_value(ts_raw) if isinstance(ts_raw, str) else None
                if ts_val is not None:
                    result["timeScale"] = json.loads(ts_val)
                else:
                    try:
                        result["timeScale"] = json.loads(ts_raw)
                    except Exception:
                        result["timeScale"] = {}
            except Exception:
                result["timeScale"] = {}
            # camera
            try:
                cam_raw = _call_diagnostics_method("GetCameraCensusJson")
                cam_val = _extract_bridge_return_value(cam_raw) if isinstance(cam_raw, str) else None
                if cam_val is not None:
                    result["camera"] = json.loads(cam_val)
                else:
                    try:
                        result["camera"] = json.loads(cam_raw)
                    except Exception:
                        result["camera"] = {}
            except Exception:
                result["camera"] = {}
            # Convert boolean strings
            for key in ("overlayOpen", "overlayReady", "rangeDisplayReady", "hooksInstalled"):
                v = result.get(key)
                if isinstance(v, str):
                    result[key] = v.lower() == "true"
                elif v is None:
                    result[key] = False
            return _tool_response(json.dumps(result, indent=2))

        elif name == "diagnostics_order_batch":
            operations = arguments.get("operations") or []
            # Parallel path (order preserved) for multi-op speed
            def _run_one(op):
                if not isinstance(op, dict):
                    return {"tool": str(op), "success": False, "error": "Invalid operation"}
                tool_name = str(op.get("tool", "")).strip()
                tool_args = op.get("arguments") or {}
                if tool_name == "diagnostics_order_batch":
                    return {"tool": tool_name, "success": False, "error": "Nested batch rejected"}
                if not tool_name:
                    return {"tool": tool_name, "success": False, "error": "Empty tool name"}
                try:
                    resp = execute_tool(tool_name, tool_args if isinstance(tool_args, dict) else {})
                    is_err = resp.get("isError", False)
                    text = ""
                    if isinstance(resp.get("content"), list) and resp["content"]:
                        text = resp["content"][0].get("text", "")
                    if is_err:
                        return {"tool": tool_name, "success": False, "error": text}
                    try:
                        parsed = json.loads(text)
                    except Exception:
                        parsed = text
                    return {"tool": tool_name, "success": True, "result": parsed}
                except Exception as exc:
                    return {"tool": tool_name, "success": False, "error": str(exc)}
            # Execute in parallel preserving input order
            if operations:
                with ThreadPoolExecutor(max_workers=min(8, len(operations))) as pool:
                    results_list = list(pool.map(_run_one, operations))
            else:
                results_list = []
            success_count = sum(1 for r in results_list if r.get("success"))
            error_count = len(results_list) - success_count
            result = {"results": results_list, "_meta": {"count": len(operations), "successCount": success_count, "errorCount": error_count}}
            return _tool_response(json.dumps(result, indent=2))

        elif name == "_diagnostics_order_batch_serial_legacy":
            operations = arguments.get("operations") or []
            results_list = []
            success_count = 0
            error_count = 0
            # Reject nested batches
            for op in operations:
                if not isinstance(op, dict):
                    error_count += 1
                    results_list.append({"tool": str(op), "success": False, "error": "Invalid operation"})
                    continue
                tool_name = str(op.get("tool", "")).strip()
                tool_args = op.get("arguments") or {}
                if tool_name == "diagnostics_order_batch":
                    error_count += 1
                    results_list.append({"tool": tool_name, "success": False, "error": "Nested batch rejected"})
                    continue
                try:
                    resp = execute_tool(tool_name, tool_args if isinstance(tool_args, dict) else {})
                    # Determine success
                    is_err = resp.get("isError", False)
                    text = ""
                    if isinstance(resp.get("content"), list) and resp["content"]:
                        text = resp["content"][0].get("text", "")
                    if is_err:
                        error_count += 1
                        results_list.append({"tool": tool_name, "success": False, "error": text})
                    else:
                        success_count += 1
                        try:
                            parsed = json.loads(text)
                        except Exception:
                            parsed = text
                        results_list.append({"tool": tool_name, "success": True, "result": parsed})
                except Exception as exc:
                    error_count += 1
                    results_list.append({"tool": tool_name, "success": False, "error": str(exc)})
            result = {
                "results": results_list,
                "_meta": {
                    "count": len(operations),
                    "successCount": success_count,
                    "errorCount": error_count,
                },
            }
            return _tool_response(json.dumps(result, indent=2))

        elif name == "diagnostics_fast_snapshot":
            preset = str(arguments.get("preset", "match")).lower().strip() or "match"
            presets = {
                "gui": ["GetGuiStateJson", "GetGameTimeScaleJson", "GetCameraCensusJson", "GetRuntimeHooksStateJson"],
                "match": ["GetSystemHealthJson", "GetMatchSummaryJson", "GetBattlefieldCensusJson", "GetRunProgressionCensusJson", "GetGameTimeScaleJson"],
                "combat": ["GetBattlefieldCensusJson", "GetPlayerRosterJson", "GetAiRosterJson", "GetCombatStatusCensusJson", "GetUnitThreatAssessmentJson"],
                "health": ["GetSystemHealthJson", "GetBaseIntegrityCensusJson", "GetProductionCensusJson", "GetAiDirectorCensusJson"],
            }
            getters = presets.get(preset, presets["match"])
            results = {}
            def _fetch_fast(getter: str):
                try:
                    raw = _call_diagnostics_method(getter, _snapshot_getter_args(getter))
                    try:
                        return getter, json.loads(raw)
                    except Exception:
                        return getter, raw
                except Exception as exc:
                    return getter, {"error": str(exc)}
            with ThreadPoolExecutor(max_workers=min(8, len(getters))) as pool:
                for getter, value in pool.map(_fetch_fast, getters):
                    results[getter] = value
            results["_meta"] = {"preset": preset, "count": len(getters)}
            return _tool_response(json.dumps(results, indent=2))

        elif name == "diagnostics_gui_control":
            steps = []
            # overlay
            if "overlayOpen" in arguments:
                raw = _call_diagnostics_method("SetOverlayOpenJson", [bool(arguments.get("overlayOpen"))])
                steps.append({"step": "overlay", "raw": raw})
            if "rangeEnabled" in arguments:
                raw = _call_diagnostics_method("SetRangeDisplayEnabledJson", [bool(arguments.get("rangeEnabled"))])
                steps.append({"step": "range", "raw": raw})
            if "overlayTab" in arguments:
                try:
                    tab = int(arguments.get("overlayTab"))
                except Exception:
                    tab = 0
                raw = _call_diagnostics_method("SetOverlayTabJson", [tab])
                steps.append({"step": "tab", "raw": raw})
            if "timeScale" in arguments:
                raw = _call_diagnostics_method("SetGameTimeScaleJson", [float(arguments.get("timeScale"))])
                steps.append({"step": "timeScale", "raw": raw})
            if arguments.get("focusEntityKeyId"):
                raw = _call_diagnostics_method("FocusCameraOnEntityJson", [str(arguments.get("focusEntityKeyId"))])
                steps.append({"step": "focus", "raw": raw})
            elif "cameraX" in arguments and "cameraZ" in arguments:
                raw = _call_diagnostics_method("MoveCameraJson", [float(arguments.get("cameraX")), float(arguments.get("cameraZ"))])
                steps.append({"step": "camera", "raw": raw})
            # final state
            try:
                final_raw = _call_diagnostics_method("GetGuiStateJson")
                try:
                    final_val = json.loads(final_raw)
                except Exception:
                    # Some bridge versions return envelope; fallback to extract
                    final_val = _extract_bridge_return_value(final_raw) if isinstance(final_raw, str) else final_raw
                    if isinstance(final_val, str):
                        try: final_val = json.loads(final_val)
                        except Exception: pass
            except Exception:
                final_val = None
            result = {"steps": steps, "finalState": final_val, "_meta": {"count": len(steps)}}
            return _tool_response(json.dumps(result, indent=2))

        elif name == "diagnostics_get_intermission_census":
            max_rows = int(arguments.get("maxRows", 64))
            res = _call_diagnostics_method("GetIntermissionCensusJson", [max_rows])
            return _tool_response(res)

        elif name == "diagnostics_order_enter_level":
            try:
                level = int(arguments["level"])
                field = int(arguments["field"])
            except (KeyError, TypeError, ValueError):
                return _tool_response(json.dumps({"error": "level and field must be integers"}), is_error=True)
            res = _call_diagnostics_method("OrderEnterLevelNodeJson", [level, field])
            return _tool_response(res)

        elif name == "diagnostics_jump_to_level":
            # User-authorized synthetic stage jump (StageMap.JumpTo via
            # reflection). The result is labeled synthetic and can never be
            # read as natural progression evidence.
            try:
                level = int(arguments["level"])
            except (KeyError, TypeError, ValueError):
                return _tool_response(json.dumps({"error": "level must be an integer"}), is_error=True)
            finished = bool(arguments.get("finished", False))
            res = _call_diagnostics_method("TryJumpToLevelJson", [level, finished])
            return _tool_response(res)

        elif name == "diagnostics_load_next_level":
            # User-authorized synthetic level load: starts the native
            # LoadScene.LoadNextLevel corridor after a synthetic stage jump.
            res = _call_diagnostics_method("TryLoadNextLevelJson", [])
            return _tool_response(res)

        elif name == "diagnostics_order_select_level_node":
            try:
                level = int(arguments["level"])
                field = int(arguments["field"])
            except (KeyError, TypeError, ValueError):
                return _tool_response(json.dumps({"error": "level and field must be integers"}), is_error=True)
            res = _call_diagnostics_method("OrderSelectLevelNodeJson", [level, field])
            return _tool_response(res)

        elif name == "diagnostics_get_level_map_state":
            res = _call_diagnostics_method("GetLevelMapStateJson")
            return _tool_response(res)

        elif name == "diagnostics_get_intermission_node":
            try:
                level = int(arguments["level"])
                field = int(arguments["field"])
            except (KeyError, TypeError, ValueError):
                return _tool_response(json.dumps({"error": "level and field must be integers"}), is_error=True)
            res = _call_diagnostics_method("GetIntermissionNodeJson", [level, field])
            return _tool_response(res)

        elif name == "diagnostics_get_navigator_state":
            res = _call_diagnostics_method("GetNavigatorStateJson")
            return _tool_response(res)

        elif name == "diagnostics_order_navigator_pause":
            if "paused" not in arguments or not isinstance(arguments.get("paused"), bool):
                return _tool_response(json.dumps({"error": "paused must be a boolean"}), is_error=True)
            res = _call_diagnostics_method("SetNavigatorPausedJson", [arguments["paused"]])
            return _tool_response(res)

        elif name in ("diagnostics_ui_press_button", "diagnostics_ui_click"):
            # Point mode takes precedence: explicit x/y clicks whatever is on top.
            dx = arguments.get("x")
            dy = arguments.get("y")
            if isinstance(dx, (int, float)) and isinstance(dy, (int, float)):
                res = _call_diagnostics_method("TryUiClickPointJson", [int(dx), int(dy)])
                return _tool_response(res)
            target = None
            direct_id = arguments.get("gameObjectId")
            resolved_name = None
            if direct_id is not None:
                try:
                    items = [{"id": int(direct_id)}]
                except (TypeError, ValueError):
                    return _tool_response(json.dumps({"success": False, "message": "gameObjectId must be an integer"}), True)
            else:
                button_name = str(arguments.get("buttonName", "")).strip()
                selector = str(arguments.get("selector", "")).strip()
                target = selector or button_name
                if not target:
                    return _tool_response(json.dumps({"success": False, "message": "buttonName, selector or gameObjectId required"}), True)
                # search (bridge uses `name`/`componentName`; `query` kept as fallback)
                items = []
                search_params_candidates = [
                    {"name": target, "includeInactive": "true", "limit": "20"},
                    {"query": target, "includeInactive": "true", "limit": "20"},
                ]
                for sp in search_params_candidates:
                    try:
                        search_raw = _call_http("scene/search_gameobjects", params=sp)
                        parsed_items = json.loads(search_raw) if isinstance(search_raw, str) else search_raw
                        if isinstance(parsed_items, list):
                            candidate_items = parsed_items
                        elif isinstance(parsed_items, dict):
                            candidate_items = None
                            for envelope_key in ("objects", "items", "results"):
                                envelope_items = parsed_items.get(envelope_key)
                                if isinstance(envelope_items, list) and envelope_items:
                                    candidate_items = envelope_items
                                    break
                            if candidate_items is None:
                                candidate_items = [parsed_items] if "id" in parsed_items else []
                        else:
                            candidate_items = []
                        if isinstance(candidate_items, list) and candidate_items:
                            items = candidate_items
                            break
                    except Exception:
                        items = []
            if not items:
                return _tool_response(json.dumps({"success": False, "message": f'button not found: {target}', "candidates": []}))
            game_object_id = items[0].get("id") if isinstance(items[0], dict) else None
            if game_object_id is None:
                return _tool_response(json.dumps({"success": False, "message": "button has no id"}))
            resolved_name = items[0].get("name") if isinstance(items[0], dict) else None
            res = _call_diagnostics_method("TryUiClickJson", [resolved_name or target or "", int(game_object_id)])
            return _tool_response(res)

        elif name == "diagnostics_get_control_guide":
            guide = {
                "profile": "Profile2",
                "preflight": {
                    "tool": "diagnostics_preflight",
                    "description": "Run before any QA session to verify bridge and component readiness.",
                },
                "control": {
                    "timeScale": "diagnostics_set_game_time_scale",
                    "guiState": "diagnostics_get_gui_state",
                },
                "cleanup": {
                    "description": "Restore time scale and close overlay after QA.",
                },
            }
            guide_text = json.dumps(guide)
            # Ensure expected keywords present
            if "timeScale" not in guide_text:
                guide["control"]["timeScale"] = "diagnostics_set_game_time_scale"
            return _tool_response(json.dumps(guide, indent=2))

        else:
            return _tool_response(f"Unknown tool: {name}", is_error=True)

    except Exception as exc:
        return _tool_response(f"Error executing tool {name}: {exc}", is_error=True)


def handle_request(message: dict[str, Any]) -> dict[str, Any] | None:
    method = message.get("method")
    request_id = message.get("id")

    if method == "initialize":
        requested = (message.get("params") or {}).get("protocolVersion")
        return {
            "jsonrpc": "2.0",
            "id": request_id,
            "result": {
                "protocolVersion": requested or PROTOCOL_VERSION,
                "capabilities": {"tools": {"listChanged": False}},
                "serverInfo": {"name": SERVER_NAME, "version": SERVER_VERSION},
            },
        }

    if method == "notifications/initialized":
        return None

    if method == "ping":
        return {"jsonrpc": "2.0", "id": request_id, "result": {}}

    if method == "tools/list":
        page, next_cursor, total = _filter_and_page_tools(message.get("params") or {})
        result: dict[str, Any] = {"tools": page}
        if next_cursor is not None:
            result["nextCursor"] = next_cursor
        # Non-standard but backward-compatible: total helps filtered clients
        # without breaking strict clients (unknown fields are ignored).
        result["totalCount"] = total
        return {"jsonrpc": "2.0", "id": request_id, "result": result}

    if method == "tools/call":
        params = message.get("params") or {}
        tool_name = params.get("name")
        arguments = params.get("arguments") or {}
        result = execute_tool(tool_name, arguments)
        return {"jsonrpc": "2.0", "id": request_id, "result": result}

    if request_id is None:
        return None

    return {
        "jsonrpc": "2.0",
        "id": request_id,
        "error": {"code": -32601, "message": f"Method '{method}' not found"},
    }


def generate_agent_configs(repo_root: Path | None = None) -> dict[str, str]:
    if repo_root is None:
        repo_root = Path(__file__).resolve().parent.parent
    server_py = (repo_root / "tools" / "bepinex_mcp_server.py").as_posix()
    configs_dir = repo_root / "artifacts" / "mcp-configs"
    configs_dir.mkdir(parents=True, exist_ok=True)

    # 1. Claude Desktop config snippet
    claude_config = {
        "mcpServers": {
            "bepinex_mcp": {
                "command": "python",
                "args": [server_py],
                "env": {
                    "BEPINEX_MCP_URL": BASE_URL
                }
            }
        }
    }
    claude_path = configs_dir / "claude_desktop_config.json"
    claude_path.write_text(json.dumps(claude_config, indent=2), encoding="utf-8")

    # 2. Cursor config snippet (.cursor/mcp.json)
    cursor_config = {
        "mcpServers": {
            "bepinex_mcp": {
                "command": "python",
                "args": [server_py],
                "env": {
                    "BEPINEX_MCP_URL": BASE_URL
                }
            }
        }
    }
    cursor_path = configs_dir / "cursor_mcp.json"
    cursor_path.write_text(json.dumps(cursor_config, indent=2), encoding="utf-8")
    repo_cursor_dir = repo_root / ".cursor"
    repo_cursor_dir.mkdir(parents=True, exist_ok=True)
    (repo_cursor_dir / "mcp.json").write_text(json.dumps(cursor_config, indent=2), encoding="utf-8")

    # 3. VS Code / Windsurf config snippet (.vscode/mcp.json)
    vscode_config = {
        "mcpServers": {
            "bepinex_mcp": {
                "command": "python",
                "args": ["${workspaceFolder}/tools/bepinex_mcp_server.py"],
                "env": {
                    "BEPINEX_MCP_URL": BASE_URL
                }
            }
        }
    }
    vscode_path = configs_dir / "vscode_mcp.json"
    vscode_path.write_text(json.dumps(vscode_config, indent=2), encoding="utf-8")
    repo_vscode_dir = repo_root / ".vscode"
    repo_vscode_dir.mkdir(parents=True, exist_ok=True)
    (repo_vscode_dir / "mcp.json").write_text(json.dumps(vscode_config, indent=2), encoding="utf-8")

    # 4. Hermes Plugin & MCP Manifests
    hermes_plugin = {
        "$schema": "https://agent-plugins.org/schemas/1.0.0/plugin.schema.json",
        "name": "bepinex-mcp",
        "version": SERVER_VERSION,
        "description": "BepInEx MCP server bridge for Rogue Command runtime diagnostics, telemetry censuses, multi-agent coordination, and tactical gameplay commands.",
        "author": {
            "name": "Hermes Agent",
            "url": "https://github.com/Jorge-desing/Rogue-Evolution"
        },
        "license": "MIT",
        "keywords": [
            "rogue-command",
            "bepinex",
            "diagnostics",
            "telemetry",
            "clean-room",
            "multi-agent",
            "mcp"
        ]
    }
    hermes_mcp = {
        "$schema": "https://agent-plugins.org/schemas/1.0.0/mcp.schema.json",
        "mcpServers": {
            "bepinex_mcp": {
                "type": "stdio",
                "command": "python",
                "args": [server_py],
                "env": {
                    "BEPINEX_MCP_URL": BASE_URL
                }
            }
        }
    }
    (configs_dir / "hermes_plugin.json").write_text(json.dumps(hermes_plugin, indent=2), encoding="utf-8")
    (configs_dir / "hermes_mcp.json").write_text(json.dumps(hermes_mcp, indent=2), encoding="utf-8")

    # Sync to Hermes home if available
    hermes_plugins_dir = Path(os.environ.get("LOCALAPPDATA", "")) / "hermes" / "plugins" / "bepinex-mcp"
    if hermes_plugins_dir.parent.is_dir():
        hermes_plugins_dir.mkdir(parents=True, exist_ok=True)
        (hermes_plugins_dir / "plugin.json").write_text(json.dumps(hermes_plugin, indent=2), encoding="utf-8")
        (hermes_plugins_dir / "mcp.json").write_text(json.dumps(hermes_mcp, indent=2), encoding="utf-8")

    return {
        "claude": str(claude_path),
        "cursor": str(cursor_path),
        "vscode": str(vscode_path),
        "hermes": str(hermes_plugins_dir) if hermes_plugins_dir.parent.is_dir() else "hermes_configs_in_artifacts",
    }


def _read_framed_message() -> dict[str, Any] | None:
    first_line = sys.stdin.readline()
    if not first_line:
        return None
    line = first_line.strip()
    if not line:
        return _read_framed_message()

    if line.lower().startswith("content-length:"):
        try:
            length = int(line.split(":", 1)[1].strip())
            while True:
                sep = sys.stdin.readline()
                if sep in ("\r\n", "\n", ""):
                    break
            body = sys.stdin.read(length)
            return json.loads(body)
        except Exception as exc:
            raise ValueError(f"Failed to read Content-Length framed message: {exc}")

    return json.loads(line)


def main() -> int:
    import argparse
    parser = argparse.ArgumentParser(description="BepInEx MCP Server bridge for Rogue Command & multi-agent environments.")
    parser.add_argument("--call", type=str, help="Directly execute an MCP tool by name and print JSON result.")
    parser.add_argument("--args", type=str, default="{}", help="JSON arguments dictionary for --call.")
    parser.add_argument("--list-tools", action="store_true", help="Print available tools and descriptions.")
    parser.add_argument("--category", type=str, default="", help="Filter --list-tools by category: system, scene, mod, diagnostics, profiles.")
    parser.add_argument("--query", type=str, default="", help="Filter --list-tools by name/description substring.")
    parser.add_argument("--prefix", type=str, default="", help="Filter --list-tools by name prefix.")
    parser.add_argument("--no-aliases", action="store_true", help="Exclude alias tools from --list-tools.")
    parser.add_argument("--summary", action="store_true", help="Print only name + category for --list-tools (lightweight).")
    parser.add_argument("--describe-tool", type=str, default="", help="Print full schema for one tool and exit (lazy).")
    parser.add_argument("--ping", action="store_true", help="Ping game bridge and exit.")
    parser.add_argument("--generate-configs", action="store_true", help="Generate MCP configuration snippets for Hermes, Claude, Cursor, and VS Code.")
    parser.add_argument("--url", type=str, help="Override BEPINEX_MCP_URL (e.g. http://127.0.0.1:8080/mcp/).")

    # In stdio mode when invoked without arguments, skip arg parsing if stdin is piped
    if len(sys.argv) > 1:
        args = parser.parse_args()
        global BASE_URL
        if args.url:
            BASE_URL = args.url

        if args.generate_configs:
            res = generate_agent_configs()
            print("Generated agent configurations:")
            for k, v in res.items():
                print(f"  {k}: {v}")
            return 0

        if args.ping:
            res = execute_tool("system_ping", {})
            print(json.dumps(res, indent=2))
            return 0 if not res.get("isError") else 1

        if args.describe_tool:
            for t in TOOLS:
                if t["name"] == args.describe_tool:
                    print(json.dumps(t, indent=2))
                    return 0
            print(f"Tool not found: {args.describe_tool}", file=sys.stderr)
            return 1
        if args.list_tools:
            params: dict[str, object] = {}
            if args.category:
                params["category"] = args.category
            if args.query:
                params["query"] = args.query
            if args.prefix:
                params["prefix"] = args.prefix
            if args.no_aliases:
                params["includeAliases"] = False
            if args.summary:
                params["detail"] = "summary"
            page, _nc, total = _filter_and_page_tools(params)
            print(f"Total available tools: {total} (showing {len(page)})")
            for t in page:
                if args.summary:
                    print(f"  - {t['name']} [{_tool_category(t['name'])}]")
                else:
                    print(f"  - {t['name']}: {t['description']}")
            return 0

        if args.call:
            try:
                parsed_args = json.loads(args.args)
            except Exception as e:
                print(f"Error parsing --args JSON: {e}", file=sys.stderr)
                return 1
            res = execute_tool(args.call, parsed_args)
            content = res.get("content", [{}])[0].get("text", "")
            try:
                formatted = json.dumps(json.loads(content), indent=2)
                print(formatted)
            except Exception:
                print(content)
            return 0 if not res.get("isError") else 1

    # Standard MCP JSON-RPC loop over stdio
    while True:
        try:
            message = _read_framed_message()
            if message is None:
                break
            response = handle_request(message)
        except json.JSONDecodeError:
            response = {
                "jsonrpc": "2.0",
                "id": None,
                "error": {"code": -32700, "message": "Parse error"},
            }
        except Exception as exc:
            response = {
                "jsonrpc": "2.0",
                "id": None,
                "error": {"code": -32603, "message": f"Internal error: {exc}"},
            }

        if response is not None:
            sys.stdout.write(json.dumps(response, separators=(",", ":")) + "\n")
            sys.stdout.flush()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
