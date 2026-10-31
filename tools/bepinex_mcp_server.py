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

    raise RuntimeError(
        "Could not find a GameObject with LoaderDiagnosticsComponent in the running game. "
        "Ensure Rogue Command is running with the RogueCommandExpansionLoader plugin active."
    )


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
        if "Missing or invalid 'id'" in raw or "not found" in raw.lower():
            _DIAGNOSTICS_GO_ID = None
            go_id = _get_diagnostics_go_id()
            raw = _call_http("component/call_method", params={
                "id": go_id,
                "componentName": "LoaderDiagnosticsComponent",
                "methodName": method_name,
                "args": args_json,
            })

        # Unwrap ReturnValue if present
        try:
            resp_obj = json.loads(raw)
            if isinstance(resp_obj, dict) and "message" in resp_obj:
                msg = resp_obj["message"]
                idx = msg.find("ReturnValue: ")
                if idx >= 0:
                    ret_str = msg[idx + len("ReturnValue: "):].strip()
                    try:
                        parsed_ret = json.loads(ret_str)
                        return json.dumps(parsed_ret, indent=2)
                    except Exception:
                        return ret_str
        except Exception:
            pass

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
            res = _call_diagnostics_method("OrderFactoryProduceJson", [f_key, instant, forFree])
            return _tool_response(res)

        elif name == "diagnostics_get_match_summary":
            res = _call_diagnostics_method("GetMatchSummaryJson")
            return _tool_response(res)

        elif name == "diagnostics_get_catalog":
            res = _call_diagnostics_method("GetDiagnosticCatalogJson")
            return _tool_response(res)

        elif name == "diagnostics_call_getter":
            getter = arguments.get("getterName", "").strip()
            args_list = arguments.get("args") or []
            res = _call_diagnostics_method(getter, args_list)
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


def main() -> int:
    for raw_line in sys.stdin:
        line = raw_line.strip()
        if not line:
            continue
        try:
            message = json.loads(line)
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
