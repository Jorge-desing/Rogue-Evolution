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
import urllib.error
import urllib.parse
import urllib.request
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

_DIAGNOSTICS_GO_ID: int | None = None


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
    try:
        go_id = _get_diagnostics_go_id()
        raw = _call_http("component/call_method", params={
            "id": go_id,
            "componentName": "LoaderDiagnosticsComponent",
            "methodName": method_name,
            "args": args_json,
        })
        # If cached go_id became stale, retry once after discovery
        if "Missing or invalid 'id'" in raw or "not found" in raw.lower() or "GameObject" in raw:
            _DIAGNOSTICS_GO_ID = None
            go_id = _get_diagnostics_go_id()
            raw = _call_http("component/call_method", params={
                "id": go_id,
                "componentName": "LoaderDiagnosticsComponent",
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
            res = _call_diagnostics_method(
                "OrderCoordinatedAssaultJson",
                [target_key, attacker_keys, max_attackers],
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
            res = _call_diagnostics_method("OrderUnitPatrolJson", [e_key, [float(v) for v in wx], [float(v) for v in wz]])
            return _tool_response(res)

        elif name == "diagnostics_order_group_command":
            keys = arguments.get("entityKeyIds") or []
            cmd_type = str(arguments.get("commandType", ""))
            x = float(arguments.get("targetX", 0.0))
            z = float(arguments.get("targetZ", 0.0))
            res = _call_diagnostics_method("OrderGroupCommandJson", [[str(k) for k in keys], cmd_type, x, z])
            return _tool_response(res)

        elif name == "diagnostics_order_formation_move":
            keys = arguments.get("entityKeyIds") or []
            f_type = str(arguments.get("formationType", "Line"))
            x = float(arguments.get("targetX", 0.0))
            z = float(arguments.get("targetZ", 0.0))
            spacing = float(arguments.get("spacing", 4.0))
            res = _call_diagnostics_method("OrderFormationMoveJson", [[str(k) for k in keys], f_type, x, z, spacing])
            return _tool_response(res)

        elif name == "diagnostics_order_unit_tactical_stance":
            e_key = str(arguments.get("entityKeyId", ""))
            s_name = str(arguments.get("stanceName", "Standard"))
            res = _call_diagnostics_method("OrderUnitTacticalStanceJson", [e_key, s_name])
            return _tool_response(res)

        elif name == "diagnostics_order_group_tactical_stance":
            keys = arguments.get("entityKeyIds") or []
            s_name = str(arguments.get("stanceName", "Standard"))
            res = _call_diagnostics_method("OrderGroupTacticalStanceJson", [[str(k) for k in keys], s_name])
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
            duration_seconds = float(arguments.get("durationSeconds", 30.0))
            res = _call_diagnostics_method("AcquireAgentLockBatchJson", [agent_id, keys, duration_seconds])
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
            res = _call_diagnostics_method("ReleaseAgentLockBatchJson", [agent_id, keys])
            return _tool_response(res)

        elif name == "diagnostics_get_tech_catalog":
            res = _call_diagnostics_method("GetTechCatalogJson")
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
                "AuditedUnitPrefabCount",
                "LiveMechanicsDirectorCount",
                "LiveVisualDirectorCount",
                "LiveJammedTurretCount",
                "LiveRegeneratingShieldCount",
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
            snapshot = {}
            getters = [
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
                "GetUnitLoadoutAndPoolCensusJson",
                "GetAiDoctrineCensusJson",
                "GetAiDirectorCensusJson",
                "GetHarvesterCensusJson",
                "GetHazardCensusJson",
                "GetVeterancyCensusJson",
                "GetSelectionCensusJson",
                "GetVisualRenderCensusJson",
                "GetLoadedPrefabVisualAuditJson",
                "GetLoaderAuditCensusJson",
                "GetUnitMechanicsCensusJson",
            ]
            for getter in getters:
                try:
                    raw = _call_diagnostics_method(getter)
                    try:
                        snapshot[getter] = json.loads(raw)
                    except Exception:
                        snapshot[getter] = raw
                except Exception as exc:
                    snapshot[getter] = {"error": str(exc)}
            return _tool_response(json.dumps(snapshot, indent=2))

        elif name == "diagnostics_call_getter":
            getter = arguments.get("getterName", "").strip()
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
        return {
            "jsonrpc": "2.0",
            "id": request_id,
            "result": {
                "tools": TOOLS
            },
        }

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

        if args.list_tools:
            print(f"Total available tools: {len(TOOLS)}")
            for t in TOOLS:
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
