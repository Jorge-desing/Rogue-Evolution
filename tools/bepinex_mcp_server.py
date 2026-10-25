#!/usr/bin/env python3
"""
BepInEx MCP Server bridge for Antigravity.
Connects standard Model Context Protocol (JSON-RPC over stdio) to BepInEx MCP HTTP server (http://127.0.0.1:8080/mcp/).
"""

from __future__ import annotations

import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from typing import Any

PROTOCOL_VERSION = "2024-11-05"
SERVER_NAME = "bepinex_mcp"
SERVER_VERSION = "1.0.0"
BASE_URL = os.environ.get("BEPINEX_MCP_URL", "http://127.0.0.1:8080/mcp/")

TOOLS = [
    {
        "name": "system_ping",
        "description": "Ping BepInEx MCP server to check if the game is alive and responding.",
        "inputSchema": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "system_capabilities",
        "description": "Get BepInEx MCP server capabilities, Unity version, runtime, and architecture.",
        "inputSchema": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "system_paths",
        "description": "Get game root directory and Assembly-CSharp.dll path.",
        "inputSchema": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "scene_list_root_gameobjects",
        "description": "List root GameObjects (name and instance ID) in the active scene.",
        "inputSchema": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "scene_search_gameobjects",
        "description": "Search GameObjects across loaded scenes by name, component, tag, or scene.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "name": {"type": "string", "description": "GameObject name filter substring."},
                "componentName": {"type": "string", "description": "Component type name filter."},
                "tag": {"type": "string", "description": "Tag filter."},
                "scene": {"type": "string", "description": "Scene name filter."},
                "includeInactive": {"type": "boolean", "description": "Whether to include inactive GameObjects (default true)."},
                "limit": {"type": "integer", "description": "Maximum objects to return (default 100)."},
            },
            "additionalProperties": False,
        },
    },
    {
        "name": "scene_hierarchy_snapshot",
        "description": "Get a hierarchy tree snapshot of GameObjects and their components starting from root or a specific GameObject ID.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "id": {"type": "integer", "description": "Root GameObject instance ID (optional, omit for scene root)."},
                "depth": {"type": "integer", "description": "Maximum tree depth to traverse (default 3, max 16)."},
                "maxNodes": {"type": "integer", "description": "Maximum total nodes to collect (default 500)."},
            },
            "additionalProperties": False,
        },
    },
    {
        "name": "scene_resolve_selector",
        "description": "Resolve a GameObject or Component reference using a selector path/name/component.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "path": {"type": "string", "description": "Transform hierarchy path (e.g. 'Canvas/Panel/Button')."},
                "name": {"type": "string", "description": "GameObject name."},
                "component": {"type": "string", "description": "Component type name."},
                "scene": {"type": "string", "description": "Scene name."},
            },
            "additionalProperties": False,
        },
    },
    {
        "name": "gameobject_list_children",
        "description": "List direct child GameObjects for a parent GameObject ID.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "id": {"type": "integer", "description": "Parent GameObject instance ID."},
            },
            "required": ["id"],
            "additionalProperties": False,
        },
    },
    {
        "name": "gameobject_inspect_components",
        "description": "List all component types attached to a GameObject ID.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "id": {"type": "integer", "description": "GameObject instance ID."},
            },
            "required": ["id"],
            "additionalProperties": False,
        },
    },
    {
        "name": "component_get_details",
        "description": "Get all fields and properties of a component on a GameObject.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "id": {"type": "integer", "description": "GameObject instance ID."},
                "componentName": {"type": "string", "description": "Component type name."},
            },
            "required": ["id", "componentName"],
            "additionalProperties": False,
        },
    },
    {
        "name": "component_get_member",
        "description": "Read a single field or property value on a component.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "id": {"type": "integer", "description": "GameObject instance ID."},
                "componentName": {"type": "string", "description": "Component type name."},
                "memberName": {"type": "string", "description": "Field or property name."},
            },
            "required": ["id", "componentName", "memberName"],
            "additionalProperties": False,
        },
    },
    {
        "name": "component_set_value",
        "description": "Set a field or property value on a component (supports string, int, float, bool, Vector3 '(x,y,z)').",
        "inputSchema": {
            "type": "object",
            "properties": {
                "id": {"type": "integer", "description": "GameObject instance ID."},
                "componentName": {"type": "string", "description": "Component type name."},
                "memberName": {"type": "string", "description": "Field or property name to set."},
                "value": {"type": "string", "description": "Serialized value string."},
            },
            "required": ["id", "componentName", "memberName", "value"],
            "additionalProperties": False,
        },
    },
    {
        "name": "component_call_method",
        "description": "Invoke a method on a component with JSON arguments.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "id": {"type": "integer", "description": "GameObject instance ID."},
                "componentName": {"type": "string", "description": "Component type name."},
                "methodName": {"type": "string", "description": "Method name to invoke."},
                "args": {"type": "string", "description": "JSON array string of arguments (e.g. '[\"val\", 123]')."},
            },
            "required": ["id", "componentName", "methodName"],
            "additionalProperties": False,
        },
    },
    {
        "name": "component_list_methods",
        "description": "List all methods available on a component.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "id": {"type": "integer", "description": "GameObject instance ID."},
                "componentName": {"type": "string", "description": "Component type name."},
            },
            "required": ["id", "componentName"],
            "additionalProperties": False,
        },
    },
    {
        "name": "scene_find_objects_with_component",
        "description": "Find all GameObjects in loaded scenes that have a component of the specified type.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "componentName": {"type": "string", "description": "Component type name (e.g. 'Balancing', 'EntityController')."},
            },
            "required": ["componentName"],
            "additionalProperties": False,
        },
    },
    {
        "name": "type_search",
        "description": "Search loaded C# / Mono types across all AppDomain assemblies.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "query": {"type": "string", "description": "Type name substring to search."},
                "offset": {"type": "integer", "description": "Pagination offset (default 0)."},
                "limit": {"type": "integer", "description": "Pagination limit (default 100)."},
            },
            "additionalProperties": False,
        },
    },
    {
        "name": "type_describe",
        "description": "Get detailed reflection metadata (fields, properties, methods) for a C# type.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "typeName": {"type": "string", "description": "Full or short type name."},
                "offset": {"type": "integer", "description": "Pagination offset (default 0)."},
                "limit": {"type": "integer", "description": "Pagination limit (default 200)."},
            },
            "required": ["typeName"],
            "additionalProperties": False,
        },
    },
    {
        "name": "mod_patch_method",
        "description": "Apply a live Harmony patch to a C# method at runtime.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "targetClass": {"type": "string", "description": "Target class name."},
                "targetMethod": {"type": "string", "description": "Target method name."},
                "patchType": {"type": "string", "enum": ["prefix", "postfix", "transpiler", "finalizer"], "description": "Patch type."},
                "patchCode": {"type": "string", "description": "C# method body/code for the patch."},
                "parameterTypes": {"type": "string", "description": "Comma-separated parameter type names for method overload disambiguation."},
                "registrationId": {"type": "string", "description": "Optional custom tracking ID."},
            },
            "required": ["targetClass", "targetMethod", "patchCode"],
            "additionalProperties": False,
        },
    },
    {
        "name": "mod_inject_class",
        "description": "Compile and dynamically inject C# code into Unity at runtime, optionally attaching to a GameObject.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "classCode": {"type": "string", "description": "Complete C# class code to compile and inject."},
                "attachToGameObjectId": {"type": "string", "description": "Optional GameObject ID to attach the compiled MonoBehaviour component to."},
            },
            "required": ["classCode"],
            "additionalProperties": False,
        },
    },
    {
        "name": "mod_list_patches",
        "description": "List all active dynamic Harmony patches applied through MCP.",
        "inputSchema": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "mod_remove_patch",
        "description": "Unpatch a dynamic Harmony patch by registration ID.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "registrationId": {"type": "string", "description": "Registration ID of the patch to remove."},
            },
            "required": ["registrationId"],
            "additionalProperties": False,
        },
    },
    {
        "name": "raw_mcp_request",
        "description": "Send a raw HTTP GET or POST request to the BepInEx MCP server.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "endpoint": {"type": "string", "description": "Endpoint relative to /mcp/ (e.g. 'system/ping', 'scene/list_root_gameobjects')."},
                "method": {"type": "string", "enum": ["GET", "POST"], "description": "HTTP method (default GET)."},
                "queryParams": {"type": "object", "description": "Key-value dictionary of query parameters."},
                "bodyJson": {"type": "string", "description": "JSON string body for POST requests."},
            },
            "required": ["endpoint"],
            "additionalProperties": False,
        },
    },
]


def _call_http(endpoint: str, method: str = "GET", params: dict[str, Any] | None = None, body_str: str | None = None, timeout: float = 10.0) -> str:
    url = BASE_URL.rstrip("/") + "/" + endpoint.lstrip("/")
    if params:
        filtered = {k: str(v) for k, v in params.items() if v is not None}
        if filtered:
            url += "?" + urllib.parse.urlencode(filtered)

    data = body_str.encode("utf-8") if body_str else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("User-Agent", "Antigravity-BepInEx-MCP-Bridge/1.0")
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


def execute_tool(name: str, arguments: dict[str, Any]) -> dict[str, Any]:
    try:
        if name == "system_ping":
            res = _call_http("system/ping")
            return _tool_response(res)

        elif name == "system_capabilities":
            res = _call_http("system/capabilities")
            return _tool_response(res)

        elif name == "system_paths":
            res = _call_http("system/paths")
            return _tool_response(res)

        elif name == "scene_list_root_gameobjects":
            res = _call_http("scene/list_root_gameobjects")
            return _tool_response(res)

        elif name == "scene_search_gameobjects":
            res = _call_http("scene/search_gameobjects", params=arguments)
            return _tool_response(res)

        elif name == "scene_hierarchy_snapshot":
            res = _call_http("scene/hierarchy_snapshot", params=arguments)
            return _tool_response(res)

        elif name == "scene_resolve_selector":
            res = _call_http("scene/resolve_selector", params=arguments)
            return _tool_response(res)

        elif name == "gameobject_list_children":
            res = _call_http("gameobject/list_children", params={"id": arguments.get("id")})
            return _tool_response(res)

        elif name == "gameobject_inspect_components":
            res = _call_http("gameobject/inspect_components", params={"id": arguments.get("id")})
            return _tool_response(res)

        elif name == "component_get_details":
            res = _call_http("component/get_details", params={
                "id": arguments.get("id"),
                "componentName": arguments.get("componentName"),
            })
            return _tool_response(res)

        elif name == "component_get_member":
            res = _call_http("component/get_member", params={
                "id": arguments.get("id"),
                "componentName": arguments.get("componentName"),
                "memberName": arguments.get("memberName"),
            })
            return _tool_response(res)

        elif name == "component_set_value":
            res = _call_http("component/set_value", params={
                "id": arguments.get("id"),
                "componentName": arguments.get("componentName"),
                "memberName": arguments.get("memberName"),
                "value": arguments.get("value"),
            })
            return _tool_response(res)

        elif name == "component_call_method":
            res = _call_http("component/call_method", params={
                "id": arguments.get("id"),
                "componentName": arguments.get("componentName"),
                "methodName": arguments.get("methodName"),
                "args": arguments.get("args", "[]"),
            })
            return _tool_response(res)

        elif name == "component_list_methods":
            res = _call_http("component/list_methods", params={
                "id": arguments.get("id"),
                "componentName": arguments.get("componentName"),
            })
            return _tool_response(res)

        elif name == "scene_find_objects_with_component":
            res = _call_http("scene:find_objects_with_component", params={
                "componentName": arguments.get("componentName"),
            })
            return _tool_response(res)

        elif name == "type_search":
            res = _call_http("type/search", params=arguments)
            return _tool_response(res)

        elif name == "type_describe":
            res = _call_http("type/describe", params=arguments)
            return _tool_response(res)

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

        elif name == "mod_list_patches":
            res = _call_http("mod:list_patches")
            return _tool_response(res)

        elif name == "mod_remove_patch":
            res = _call_http("mod:remove_patch", params=arguments)
            return _tool_response(res)

        elif name == "raw_mcp_request":
            endpoint = arguments.get("endpoint", "")
            method = arguments.get("method", "GET")
            qparams = arguments.get("queryParams")
            body = arguments.get("bodyJson")
            res = _call_http(endpoint, method=method, params=qparams, body_str=body)
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
