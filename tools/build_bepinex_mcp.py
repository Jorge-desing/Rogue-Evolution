#!/usr/bin/env python3
"""
Builder and synchronizer for BepInEx MCP tools and Antigravity tool schemas.
Updates tools/bepinex_mcp_server.py and generates tool JSON schemas in
C:/Users/Alex/.gemini/antigravity/mcp/bepinex_mcp/.
"""

from __future__ import annotations

import copy
import json
import re
from pathlib import Path

MCP_DIR = Path(r"C:\Users\Alex\.gemini\antigravity\mcp\bepinex_mcp")

# All tools with name, description, parameters schema, and handler key
TOOL_SPECS = [
    {
        "name": "system_get_game_process_state",
        "description": "Reads Rogue Command.exe process IDs from the Windows process table; no window focus or screenshot is used.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "system_launch_game",
        "description": "Launches Rogue Command through its installed Steam AppID (1461910). Refuses duplicate launches and does not select, create, or reset a profile.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "system_wait_for_game_bridge",
        "description": "Waits for both the live BepInEx HTTP bridge and a Unity scene query to become available after Steam launch; does not require focus or screenshots.",
        "parameters": {
            "type": "object",
            "properties": {
                "timeoutSeconds": {"type": "number", "description": "Wait limit from 1 to 60 seconds (default 30)."},
                "pollIntervalMs": {"type": "integer", "description": "Poll interval from 250 to 2000 ms (default 500)."},
            },
            "additionalProperties": False,
        },
    },
    # ---- Core Bridge & System ----
    {
        "name": "system_ping",
        "description": "Ping BepInEx MCP server to check if the game is alive and responding.",
        "parameters": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "ping_unity_bridge",
        "description": "Alias for system_ping: check bridge health without touching game state.",
        "parameters": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "system_capabilities",
        "description": "Get BepInEx MCP server capabilities, Unity version, runtime, and architecture.",
        "parameters": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "get_runtime_capabilities",
        "description": "Alias for system_capabilities: report connected runtime, versions, architecture, and patch types.",
        "parameters": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "system_paths",
        "description": "Get game root directory and Assembly-CSharp.dll path on disk.",
        "parameters": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "get_game_paths",
        "description": "Alias for system_paths: retrieve absolute paths to game directory and main Assembly-CSharp.dll.",
        "parameters": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },

    # ---- Hierarchy & GameObjects ----
    {
        "name": "scene_list_root_gameobjects",
        "description": "List root GameObjects (name and instance ID) in the active scene.",
        "parameters": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "list_root_gameobjects",
        "description": "Alias for scene_list_root_gameobjects: list all root GameObjects in the active scene.",
        "parameters": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "scene_search_gameobjects",
        "description": "Search GameObjects across loaded scenes by name, component, tag, or scene.",
        "parameters": {
            "type": "object",
            "properties": {
                "name": {"type": "string", "description": "GameObject name filter substring."},
                "componentName": {"type": "string", "description": "Component type name filter."},
                "tag": {"type": "string", "description": "Tag filter."},
                "scene": {"type": "string", "description": "Scene name filter."},
                "includeInactive": {"type": "boolean", "description": "Include inactive GameObjects (default true)."},
                "limit": {"type": "integer", "description": "Maximum objects to return (default 100)."},
            },
            "additionalProperties": False,
        },
    },
    {
        "name": "search_gameobjects",
        "description": "Alias for scene_search_gameobjects: search loaded GameObjects by name, component, or tag.",
        "parameters": {
            "type": "object",
            "properties": {
                "name": {"type": "string", "description": "GameObject name filter substring."},
                "componentName": {"type": "string", "description": "Component type name filter."},
                "tag": {"type": "string", "description": "Tag filter."},
                "scene": {"type": "string", "description": "Scene name filter."},
                "includeInactive": {"type": "boolean", "description": "Include inactive GameObjects (default true)."},
                "limit": {"type": "integer", "description": "Maximum objects to return (default 100)."},
            },
            "additionalProperties": False,
        },
    },
    {
        "name": "scene_hierarchy_snapshot",
        "description": "Get a hierarchy tree snapshot of GameObjects and their components starting from root or specific ID.",
        "parameters": {
            "type": "object",
            "properties": {
                "id": {"type": "integer", "description": "Root GameObject instance ID (optional)."},
                "depth": {"type": "integer", "description": "Maximum tree depth to traverse (default 3, max 16)."},
                "maxNodes": {"type": "integer", "description": "Maximum total nodes to collect (default 500)."},
            },
            "additionalProperties": False,
        },
    },
    {
        "name": "get_hierarchy_snapshot",
        "description": "Alias for scene_hierarchy_snapshot: snapshot tree of GameObjects and components.",
        "parameters": {
            "type": "object",
            "properties": {
                "id": {"type": "integer", "description": "Root GameObject instance ID (optional)."},
                "depth": {"type": "integer", "description": "Maximum tree depth to traverse (default 3, max 16)."},
                "maxNodes": {"type": "integer", "description": "Maximum total nodes to collect (default 500)."},
            },
            "additionalProperties": False,
        },
    },
    {
        "name": "scene_resolve_selector",
        "description": "Resolve a GameObject or Component reference using a stable selector path/name/component.",
        "parameters": {
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
        "name": "resolve_gameobject_selector",
        "description": "Alias for scene_resolve_selector: resolve a stable scene/path/component selector to current ID.",
        "parameters": {
            "type": "object",
            "properties": {
                "path": {"type": "string", "description": "Transform hierarchy path."},
                "name": {"type": "string", "description": "GameObject name."},
                "component": {"type": "string", "description": "Component type name."},
                "scene": {"type": "string", "description": "Scene name."},
                "selector_json": {"type": "string", "description": "JSON object string of selector."},
            },
            "additionalProperties": False,
        },
    },
    {
        "name": "gameobject_list_children",
        "description": "List direct child GameObjects for a parent GameObject ID.",
        "parameters": {
            "type": "object",
            "properties": {
                "id": {"type": "integer", "description": "Parent GameObject instance ID."},
            },
            "required": ["id"],
            "additionalProperties": False,
        },
    },
    {
        "name": "list_children",
        "description": "Alias for gameobject_list_children: list immediate children of a GameObject.",
        "parameters": {
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
        "parameters": {
            "type": "object",
            "properties": {
                "id": {"type": "integer", "description": "GameObject instance ID."},
            },
            "required": ["id"],
            "additionalProperties": False,
        },
    },
    {
        "name": "inspect_components",
        "description": "Alias for gameobject_inspect_components: list all component names on a GameObject.",
        "parameters": {
            "type": "object",
            "properties": {
                "id": {"type": "integer", "description": "GameObject instance ID."},
            },
            "required": ["id"],
            "additionalProperties": False,
        },
    },
    {
        "name": "scene_find_objects_with_component",
        "description": "Find all GameObjects in loaded scenes that have a component of the specified type.",
        "parameters": {
            "type": "object",
            "properties": {
                "componentName": {"type": "string", "description": "Component type name (e.g. 'LoaderDiagnosticsComponent')."},
            },
            "required": ["componentName"],
            "additionalProperties": False,
        },
    },
    {
        "name": "find_objects_with_component",
        "description": "Alias for scene_find_objects_with_component: search for all GameObjects with a specific component.",
        "parameters": {
            "type": "object",
            "properties": {
                "componentName": {"type": "string", "description": "Component type name."},
            },
            "required": ["componentName"],
            "additionalProperties": False,
        },
    },

    # ---- Components & Reflection ----
    {
        "name": "component_get_details",
        "description": "Get all fields and properties of a component on a GameObject.",
        "parameters": {
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
        "name": "get_component_details",
        "description": "Alias for component_get_details: inspect all fields/properties from a component.",
        "parameters": {
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
        "parameters": {
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
        "name": "get_component_member",
        "description": "Alias for component_get_member: read a single field or property value on a component.",
        "parameters": {
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
        "parameters": {
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
        "name": "set_component_value",
        "description": "Alias for component_set_value: set a field or property value on a component.",
        "parameters": {
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
        "parameters": {
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
        "name": "call_component_method",
        "description": "Alias for component_call_method: invoke a method on a component with JSON arguments.",
        "parameters": {
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
        "parameters": {
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
        "name": "list_component_methods",
        "description": "Alias for component_list_methods: list all methods on a specific component.",
        "parameters": {
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
        "name": "type_search",
        "description": "Search loaded C# / Mono types across all AppDomain assemblies.",
        "parameters": {
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
        "name": "search_runtime_types",
        "description": "Alias for type_search: search loaded types across assemblies.",
        "parameters": {
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
        "parameters": {
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
        "name": "describe_runtime_type",
        "description": "Alias for type_describe: get detailed reflection metadata for a C# type.",
        "parameters": {
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

    # ---- Live Patching & Code Injection ----
    {
        "name": "mod_patch_method",
        "description": "Apply a live Harmony patch to a C# method at runtime.",
        "parameters": {
            "type": "object",
            "properties": {
                "targetClass": {"type": "string", "description": "Target class name."},
                "targetMethod": {"type": "string", "description": "Target method name."},
                "patchType": {"type": "string", "enum": ["prefix", "postfix", "transpiler", "finalizer"], "description": "Patch type."},
                "patchCode": {"type": "string", "description": "C# method body/code for the DynamicPatcher class."},
                "parameterTypes": {"type": "string", "description": "Comma-separated parameter type names for overload disambiguation."},
                "registrationId": {"type": "string", "description": "Optional custom tracking ID."},
            },
            "required": ["targetClass", "targetMethod", "patchCode"],
            "additionalProperties": False,
        },
    },
    {
        "name": "mod_inject_class",
        "description": "Compile and dynamically inject C# code into Unity at runtime, optionally attaching to a GameObject.",
        "parameters": {
            "type": "object",
            "properties": {
                "classCode": {"type": "string", "description": "Complete C# class code to compile and inject."},
                "attachToGameObjectId": {"type": "string", "description": "Optional GameObject ID to attach the MonoBehaviour component to."},
            },
            "required": ["classCode"],
            "additionalProperties": False,
        },
    },
    {
        "name": "mod_list_patches",
        "description": "List all active dynamic Harmony patches applied through MCP.",
        "parameters": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "list_mod_patches",
        "description": "Alias for mod_list_patches: list Harmony subscriptions and dynamic patches with lifecycle IDs.",
        "parameters": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "mod_remove_patch",
        "description": "Unpatch a dynamic Harmony patch by registration ID.",
        "parameters": {
            "type": "object",
            "properties": {
                "registrationId": {"type": "string", "description": "Registration ID of the patch to remove."},
            },
            "required": ["registrationId"],
            "additionalProperties": False,
        },
    },
    {
        "name": "remove_mod_patch",
        "description": "Alias for mod_remove_patch: unpatch one subscription or dynamic patch by lifecycle ID.",
        "parameters": {
            "type": "object",
            "properties": {
                "registrationId": {"type": "string", "description": "Registration ID of the patch to remove."},
            },
            "required": ["registrationId"],
            "additionalProperties": False,
        },
    },
    {
        "name": "mod_subscribe_to_method",
        "description": "Subscribe to method invocations via Harmony to deliver live events.",
        "parameters": {
            "type": "object",
            "properties": {
                "id": {"type": "integer", "description": "GameObject instance ID."},
                "componentName": {"type": "string", "description": "Component type name."},
                "methodName": {"type": "string", "description": "Method name to subscribe to."},
                "registrationId": {"type": "string", "description": "Optional registration ID."},
            },
            "required": ["id", "componentName", "methodName"],
            "additionalProperties": False,
        },
    },

    # ---- Batching, Watchers & Network ----
    {
        "name": "run_bridge_batch",
        "description": "Execute an ordered list of operations (set_value, call_method, etc.) in a single atomic round-trip.",
        "parameters": {
            "type": "object",
            "properties": {
                "operations": {"type": "array", "description": "Array of operation objects with command, params, etc."},
                "operations_json": {"type": "string", "description": "JSON string of operations array if passed as string."},
                "stopOnError": {"type": "boolean", "description": "Whether to halt execution on first failure (default false)."},
            },
            "additionalProperties": False,
        },
    },
    {
        "name": "watch_component_member",
        "description": "Register a watcher on a component member that monitors changes at a periodic interval.",
        "parameters": {
            "type": "object",
            "properties": {
                "selector": {"type": "object", "description": "Stable selector object."},
                "selector_json": {"type": "string", "description": "JSON string of selector."},
                "componentName": {"type": "string", "description": "Component type name."},
                "memberName": {"type": "string", "description": "Member field/property name."},
                "intervalMs": {"type": "integer", "description": "Polling interval in milliseconds (default 500)."},
                "registrationId": {"type": "string", "description": "Optional custom registration ID."},
            },
            "required": ["componentName", "memberName"],
            "additionalProperties": False,
        },
    },
    {
        "name": "watch_active_scene",
        "description": "Create a watcher that emits notification when the active Unity scene changes.",
        "parameters": {
            "type": "object",
            "properties": {
                "intervalMs": {"type": "integer", "description": "Polling interval in ms (default 500)."},
                "registrationId": {"type": "string", "description": "Optional registration ID."},
            },
            "additionalProperties": False,
        },
    },
    {
        "name": "list_watchers",
        "description": "List all active member and scene watchers.",
        "parameters": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "remove_watcher",
        "description": "Remove an active watcher by registration ID.",
        "parameters": {
            "type": "object",
            "properties": {
                "registrationId": {"type": "string", "description": "Watcher registration ID to remove."},
            },
            "required": ["registrationId"],
            "additionalProperties": False,
        },
    },
    {
        "name": "get_network_diagnostics",
        "description": "Read common ownership, authority, and network state without network writes.",
        "parameters": {
            "type": "object",
            "properties": {
                "id": {"type": "integer", "description": "GameObject instance ID."},
            },
            "required": ["id"],
            "additionalProperties": False,
        },
    },
    {
        "name": "raw_mcp_request",
        "description": "Send a raw HTTP GET or POST request to the BepInEx MCP server.",
        "parameters": {
            "type": "object",
            "properties": {
                "endpoint": {"type": "string", "description": "Endpoint relative to /mcp/ (e.g. 'system/ping')."},
                "method": {"type": "string", "enum": ["GET", "POST"], "description": "HTTP method (default GET)."},
                "queryParams": {"type": "object", "description": "Key-value dictionary of query parameters."},
                "bodyJson": {"type": "string", "description": "JSON string body for POST requests."},
            },
            "required": ["endpoint"],
            "additionalProperties": False,
        },
    },

    # ---- Mod Profiles ----
    {
        "name": "save_mod_profile",
        "description": "Validates and saves a version-1 persistent mod profile to disk.",
        "parameters": {
            "type": "object",
            "properties": {
                "name": {"type": "string", "description": "Profile name (letters, digits, dots, underscores, dashes)."},
                "profile_json": {"type": "string", "description": "JSON string or object containing profile definition."},
            },
            "required": ["name", "profile_json"],
            "additionalProperties": False,
        },
    },
    {
        "name": "get_mod_profile",
        "description": "Returns one saved mod profile by name.",
        "parameters": {
            "type": "object",
            "properties": {
                "name": {"type": "string", "description": "Profile name."},
            },
            "required": ["name"],
            "additionalProperties": False,
        },
    },
    {
        "name": "list_mod_profiles",
        "description": "Lists all saved mod profile names, auto-apply state, and operation counts.",
        "parameters": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "delete_mod_profile",
        "description": "Deletes one saved mod profile and deactivates it.",
        "parameters": {
            "type": "object",
            "properties": {
                "name": {"type": "string", "description": "Profile name to delete."},
            },
            "required": ["name"],
            "additionalProperties": False,
        },
    },
    {
        "name": "activate_mod_profile",
        "description": "Opts a mod profile into or out of automatic scene application.",
        "parameters": {
            "type": "object",
            "properties": {
                "name": {"type": "string", "description": "Profile name."},
                "active": {"type": "boolean", "description": "Whether profile is active (default true)."},
            },
            "required": ["name"],
            "additionalProperties": False,
        },
    },

    # ---- Rogue Command Clean-Room Diagnostics ----
    {
        "name": "diagnostics_get_system_health",
        "description": "Queries unified live system health across active packs, units, AI modules, economy, combat status, and threat telemetry directly from LoaderDiagnosticsComponent.",
        "parameters": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_get_threat_prioritization",
        "description": "Queries aggregated threat-prioritization AI telemetry (active behaviours, processed AI/allied units, retarget counts) directly from LoaderDiagnosticsComponent.",
        "parameters": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_get_production_census",
        "description": "Queries live factory production queues, units actively producing, and per-factory status directly from LoaderDiagnosticsComponent.",
        "parameters": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_get_base_integrity",
        "description": "Queries player command HQ and enemy base structural integrity directly from LoaderDiagnosticsComponent.",
        "parameters": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_order_production",
        "description": "Triggers an in-game unit production order on a player factory directly via LoaderDiagnosticsComponent / RuntimeGameplayProductionDirector.",
        "parameters": {
            "type": "object",
            "properties": {
                "factoryKeyId": {"type": "string", "description": "Entity ID or unique ID of the target player factory."},
                "instant": {"type": "boolean", "description": "Produce unit instantly without waiting build time (default false)."},
                "forFree": {"type": "boolean", "description": "Produce unit without deducting player credits (default true)."},
            },
            "required": ["factoryKeyId"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_get_match_summary",
        "description": "Queries unified match summary combining battlefield roster, economy status, and AI combat status directly from LoaderDiagnosticsComponent.",
        "parameters": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_get_catalog",
        "description": "Retrieves the full catalog of all 18+ agent-readable diagnostic getters available on LoaderDiagnosticsComponent.",
        "parameters": {
            "type": "object",
            "properties": {},
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_call_getter",
        "description": "Invokes any diagnostic getter method on LoaderDiagnosticsComponent by name (e.g. 'GetPlayerRosterJson', 'GetAiRosterJson', 'GetCombatStatusCensusJson', 'GetEconomyMatchCensusJson', 'GetRangeColorsJson', 'GetIdentifierPaletteJson', etc.) with automatic GameObject resolution.",
        "parameters": {
            "type": "object",
            "properties": {
                "getterName": {"type": "string", "description": "Exact getter method name on LoaderDiagnosticsComponent."},
                "args": {"type": "array", "description": "Optional list of positional arguments (default empty)."},
            },
            "required": ["getterName"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_get_battlefield_census",
        "description": "Queries live battlefield census (counts + per-player-unit rows) from LoaderDiagnosticsComponent.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "diagnostics_get_player_roster",
        "description": "Queries live player roster census with base balance params from LoaderDiagnosticsComponent.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "diagnostics_get_ai_roster",
        "description": "Queries live AI units/buildings roster with base balance params from LoaderDiagnosticsComponent.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "diagnostics_get_unit_threat_assessment",
        "description": "Queries per-unit threat scores (DPS x health ratio) from LoaderDiagnosticsComponent.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "diagnostics_get_economy_match_census",
        "description": "Queries match economy, wave, and harvester census from LoaderDiagnosticsComponent.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "diagnostics_get_combat_status_census",
        "description": "Queries combat-active status effects census from LoaderDiagnosticsComponent.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "diagnostics_get_entity_balance",
        "description": "Queries base balance parameters for one unit by entity id from LoaderDiagnosticsComponent.",
        "parameters": {
            "type": "object",
            "properties": {
                "entityId": {"type": "string", "description": "Entity id to look up."},
            },
            "required": ["entityId"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_get_runtime_hooks",
        "description": "Queries installed Harmony hooks and registered combat XP count from LoaderDiagnosticsComponent.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "diagnostics_get_registered_profiles",
        "description": "Queries registered AI module profile ids from LoaderDiagnosticsComponent.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "diagnostics_get_installed_patches",
        "description": "Queries names of installed Harmony patches from LoaderDiagnosticsComponent.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "diagnostics_get_applied_baseline_effects",
        "description": "Queries applied baseline effects count and names from LoaderDiagnosticsComponent.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "diagnostics_get_combat_xp_entries",
        "description": "Queries combat XP entries by entity key from LoaderDiagnosticsComponent.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "diagnostics_get_ai_modules",
        "description": "Queries registered AI modules diagnostics from LoaderDiagnosticsComponent.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "diagnostics_get_run_progression",
        "description": "Queries live match run progression, current stage, ascension/heat level, active relics, and offered blueprint reward pools directly from LoaderDiagnosticsComponent.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "diagnostics_grant_credits",
        "description": "Grants economy credits to the player bank balance in-game via LoaderDiagnosticsComponent / RuntimeGameplayCommandDirector.",
        "parameters": {
            "type": "object",
            "properties": {
                "amount": {"type": "integer", "description": "Amount of credits to deposit into player bank (e.g. 500)."},
                "accountName": {"type": "string", "description": "Bank account name (default 'Player')."},
            },
            "required": ["amount"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_apply_card_change",
        "description": "Applies a data-driven card change / stat modifier to an entity in-game via LoaderDiagnosticsComponent and the native EntityBalancingStore pipeline.",
        "parameters": {
            "type": "object",
            "properties": {
                "valueToChange": {"type": "string", "description": "Attribute to modify (e.g. 'MaxHealth', 'MoveSpeed', 'Damage1', 'WeaponRange')."},
                "operation": {"type": "string", "description": "Operation: 'Add' or 'Multiply' (default 'Add')."},
                "value": {"type": "number", "description": "Numeric delta to apply."},
                "side": {"type": "string", "description": "Target side: 'Friendly' or 'Enemy' (default 'Friendly')."},
                "targetEntityId": {"type": "string", "description": "Specific entity ID or name to target (optional)."},
            },
            "required": ["valueToChange", "value"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_order_unit_move",
        "description": "Dispatches an in-game move command to a living unit by entity key ID via LoaderDiagnosticsComponent / RuntimeGameplayCommandDirector.",
        "parameters": {
            "type": "object",
            "properties": {
                "entityKeyId": {"type": "string", "description": "Entity ID or name of the unit to command."},
                "destinationX": {"type": "number", "description": "World X coordinate."},
                "destinationZ": {"type": "number", "description": "World Z coordinate."},
            },
            "required": ["entityKeyId", "destinationX", "destinationZ"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_order_unit_attack",
        "description": "Dispatches an in-game attack command on a target entity via LoaderDiagnosticsComponent / RuntimeGameplayCommandDirector.",
        "parameters": {
            "type": "object",
            "properties": {
                "entityKeyId": {"type": "string", "description": "Attacker unit entity ID or name."},
                "targetEntityKeyId": {"type": "string", "description": "Target entity ID or name to attack."},
            },
            "required": ["entityKeyId", "targetEntityKeyId"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_order_unit_tactical_pulse",
        "description": "Executes a bounded Shockwave, Rally, or Repair pulse around a living unit via LoaderDiagnosticsComponent / RuntimeGameplayCommandDirector.",
        "parameters": {
            "type": "object",
            "properties": {
                "entityKeyId": {"type": "string", "description": "Source unit entity ID or name."},
                "pulseType": {"type": "string", "enum": ["Shockwave", "Rally", "Repair"], "description": "Pulse mode."},
                "radius": {"type": "number", "description": "Pulse radius in world meters (3.0 to 50.0)."},
                "magnitude": {"type": "number", "description": "Damage, healing, or repair magnitude (0 to 1000)."},
                "durationSeconds": {"type": "number", "description": "Status duration in seconds (0.5 to 30.0)."},
                "maxTargets": {"type": "integer", "description": "Maximum affected units (1 to 16)."},
            },
            "required": ["entityKeyId", "pulseType", "radius", "magnitude", "durationSeconds", "maxTargets"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_order_director_threat_sweep",
        "description": "Applies a bounded Mark or Suppress sweep to nearby hostile combat units via LoaderDiagnosticsComponent / RuntimeGameplayCommandDirector.",
        "parameters": {
            "type": "object",
            "properties": {
                "sourceEntityKeyId": {"type": "string", "description": "Source unit entity ID or name."},
                "sweepType": {"type": "string", "enum": ["Mark", "Suppress"], "description": "Sweep mode."},
                "radius": {"type": "number", "description": "Sweep radius in world meters (3.0 to 50.0)."},
                "durationSeconds": {"type": "number", "description": "Status duration in seconds (0.5 to 30.0)."},
                "maxTargets": {"type": "integer", "description": "Maximum hostile combat units affected (1 to 16)."},
            },
            "required": ["sourceEntityKeyId", "sweepType", "radius", "durationSeconds", "maxTargets"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_order_coordinated_assault",
        "description": "Coordinates a bounded attack against one hostile target using only eligible hostile combat attackers via LoaderDiagnosticsComponent / RuntimeGameplayCommandDirector.",
        "parameters": {
            "type": "object",
            "properties": {
                "targetEntityKeyId": {"type": "string", "description": "Target entity ID or name."},
                "attackerEntityKeyIds": {
                    "type": "array",
                    "items": {"type": "string"},
                    "description": "Ordered attacker entity IDs or names."
                },
                "maxAttackers": {"type": "integer", "description": "Maximum dispatched attackers (1 to 16)."},
            },
            "required": ["targetEntityKeyId", "attackerEntityKeyIds", "maxAttackers"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_order_retreat",
        "description": "Orders an evasive retreat away from a threat position via LoaderDiagnosticsComponent / RuntimeGameplayCommandDirector.",
        "parameters": {
            "type": "object",
            "properties": {
                "entityKeyId": {"type": "string", "description": "Unit entity ID or name."},
                "threatX": {"type": "number", "description": "Threat world X coordinate."},
                "threatZ": {"type": "number", "description": "Threat world Z coordinate."},
                "distance": {"type": "number", "description": "Retreat distance (3 to 50)."},
            },
            "required": ["entityKeyId", "threatX", "threatZ", "distance"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_order_active_skill",
        "description": "Dispatches an active tactical skill command on a unit (Self, on Target, or at Position) via LoaderDiagnosticsComponent / RuntimeGameplayCommandDirector.",
        "parameters": {
            "type": "object",
            "properties": {
                "entityKeyId": {"type": "string", "description": "Caster unit entity ID or name."},
                "targetEntityKeyId": {"type": "string", "description": "Target entity ID or name for targeted skills (optional)."},
                "targetX": {"type": "number", "description": "World X coordinate for ground-targeted skills (optional)."},
                "targetZ": {"type": "number", "description": "World Z coordinate for ground-targeted skills (optional)."},
            },
            "required": ["entityKeyId"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_adjust_vitality",
        "description": "Safely adjusts health, armor, shield, or inflicts damage on an entity for testing and balance calibration via LoaderDiagnosticsComponent / RuntimeGameplayCommandDirector.",
        "parameters": {
            "type": "object",
            "properties": {
                "entityKeyId": {"type": "string", "description": "Target entity ID or name."},
                "adjustmentType": {
                    "type": "string",
                    "enum": ["Heal", "RepairArmor", "ChargeShield", "Damage"],
                    "description": "Type of vitality adjustment: Heal, RepairArmor, ChargeShield, or Damage."
                },
                "amount": {"type": "number", "description": "Numeric delta amount to apply."},
                "ignoreArmor": {"type": "boolean", "description": "Whether damage should bypass armor (default false)."},
            },
            "required": ["entityKeyId", "adjustmentType", "amount"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_apply_status_effect",
        "description": "Applies a status effect (e.g. Emp, Burn, SlowedDown, Stun) to an entity via LoaderDiagnosticsComponent / RuntimeGameplayCommandDirector.",
        "parameters": {
            "type": "object",
            "properties": {
                "entityKeyId": {"type": "string", "description": "Target entity ID or name."},
                "statusEffectName": {"type": "string", "description": "Status effect enum name (e.g. 'SlowedDown', 'OnFire', 'Stun', 'Armored', 'CombatBoost')."},
                "durationSeconds": {"type": "number", "description": "Duration in seconds (default 10s)."},
            },
            "required": ["entityKeyId", "statusEffectName"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_clear_status_effect",
        "description": "Clears an active status effect from an entity via LoaderDiagnosticsComponent / RuntimeGameplayCommandDirector.",
        "parameters": {
            "type": "object",
            "properties": {
                "entityKeyId": {"type": "string", "description": "Target entity ID or name."},
                "statusEffectName": {"type": "string", "description": "Status effect enum name to clear."},
            },
            "required": ["entityKeyId", "statusEffectName"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_get_grid_metrics",
        "description": "Queries match terrain dimensions, cell counts, cell size, and player/AI start cells directly from LoaderDiagnosticsComponent / RuntimeGridCensus.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "diagnostics_evaluate_grid_position",
        "description": "Evaluates an (X, Z) coordinate against the battlefield grid bounds, returning clamped coords, cell index, height, and border status via LoaderDiagnosticsComponent / RuntimeGridCensus.",
        "parameters": {
            "type": "object",
            "properties": {
                "x": {"type": "number", "description": "World X coordinate."},
                "z": {"type": "number", "description": "World Z coordinate."},
            },
            "required": ["x", "z"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_get_diagnostics",
        "description": "Queries the full loader diagnostics snapshot JSON from LoaderDiagnosticsComponent.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "diagnostics_get_utilities",
        "description": "Queries clean-room and visual utility state JSON from LoaderDiagnosticsComponent.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "diagnostics_get_range_colors",
        "description": "Queries per-kind range guide colors JSON from LoaderDiagnosticsComponent.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "diagnostics_get_identifier_palette",
        "description": "Queries the deterministic identifier accent palette JSON from LoaderDiagnosticsComponent.",
        "parameters": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "diagnostics_get_range_circle_point_count",
        "description": "Queries range circle point count JSON for the given segment count from LoaderDiagnosticsComponent.",
        "parameters": {
            "type": "object",
            "properties": {
                "segments": {"type": "integer", "description": "Segment count (8..256)."},
            },
            "required": ["segments"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_get_inspector_toggle_state",
        "description": "Queries inspector toggle resolution JSON from LoaderDiagnosticsComponent.",
        "parameters": {
            "type": "object",
            "properties": {
                "isOpen": {"type": "boolean", "description": "Current open state."},
                "togglePressed": {"type": "boolean", "description": "Toggle pressed this frame."},
                "closePressed": {"type": "boolean", "description": "Close pressed this frame."},
            },
            "required": ["isOpen", "togglePressed", "closePressed"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_resolve_identifier_color",
        "description": "Resolves the stable accent color JSON for an identifier from LoaderDiagnosticsComponent.",
        "parameters": {
            "type": "object",
            "properties": {
                "identifier": {"type": "string", "description": "Identifier to resolve."},
            },
            "required": ["identifier"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_get_unit_mechanics_summary",
        "description": "Queries a compact runtime mechanics summary for a unit from LoaderDiagnosticsComponent.",
        "parameters": {
            "type": "object",
            "properties": {
                "entityId": {"type": "string", "description": "Unit entity ID."},
            },
            "required": ["entityId"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_resolve_inspector_open_state",
        "description": "Resolves the inspector open state (boolean) from toggle/close input via LoaderDiagnosticsComponent.",
        "parameters": {
            "type": "object",
            "properties": {
                "isOpen": {"type": "boolean", "description": "Current open state."},
                "togglePressed": {"type": "boolean", "description": "Toggle pressed this frame."},
                "closePressed": {"type": "boolean", "description": "Close pressed this frame."},
            },
            "required": ["isOpen", "togglePressed", "closePressed"],
            "additionalProperties": False,
        },
    },
    {
        "name": "diagnostics_get_card_change_request",
        "description": "Builds and serializes a mod-authored card-change request from primitive parameters via LoaderDiagnosticsComponent.",
        "parameters": {
            "type": "object",
            "properties": {
                "valueToChange": {"type": "string", "description": "Attribute to modify (e.g. 'MaxHealth', 'MoveSpeed', 'Damage1', 'WeaponRange')."},
                "operation": {"type": "string", "description": "Operation: 'Add' or 'Multiply' (default 'Add')."},
                "value": {"type": "number", "description": "Numeric delta to apply."},
                "side": {"type": "string", "description": "Target side: 'Friendly' or 'Enemy' (default 'Friendly')."},
                "multiplyValueWithCurrentHeatLevel": {"type": "boolean", "description": "Scale with current heat level (default false)."},
                "multiplyValueWithMetaProgressionMultiplier": {"type": "boolean", "description": "Scale with meta progression multiplier (default false)."},
                "changeableValueHasToBeGreaterZero": {"type": "boolean", "description": "Require value > 0 (default false)."},
                "sourceId": {"type": "string", "description": "Source identifier (default 'rcl-card-change')."},
            },
            "required": ["valueToChange"],
            "additionalProperties": False,
        },
    },
    {"name": "diagnostics_assign_control_group", "description": "Assigns selected units to a control group via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"groupNumber": {"type": "integer", "description": "Control group number."}}, "required": ["groupNumber"], "additionalProperties": False}},
    {"name": "diagnostics_deselect_all_units", "description": "Deselects all units via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_get_entity_catalog", "description": "Queries entity catalog JSON via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_get_fog_state", "description": "Queries fog-of-war state JSON via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_get_game_time_scale", "description": "Queries game time scale JSON via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_get_selection_census", "description": "Queries selection census JSON via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_order_unit_attack_move", "description": "Dispatches an attack-move command to a unit via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"entityKeyId": {"type": "string", "description": "Unit entity ID."}, "targetX": {"type": "number", "description": "Target X."}, "targetZ": {"type": "number", "description": "Target Z."}}, "required": ["entityKeyId", "targetX", "targetZ"], "additionalProperties": False}},
    {"name": "diagnostics_order_unit_patrol", "description": "Dispatches a native patrol route to a unit via LoaderDiagnosticsComponent. Waypoints are parallel X/Z arrays.", "parameters": {"type": "object", "properties": {"entityKeyId": {"type": "string", "description": "Unit entity ID."}, "waypointX": {"type": "array", "items": {"type": "number"}, "description": "Waypoint X coordinates."}, "waypointZ": {"type": "array", "items": {"type": "number"}, "description": "Waypoint Z coordinates."}}, "required": ["entityKeyId", "waypointX", "waypointZ"], "additionalProperties": False}},
    {"name": "diagnostics_order_group_command", "description": "Dispatches a single command type (Move, Stop, HoldPosition, AttackMove) to a group of units via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"entityKeyIds": {"type": "array", "items": {"type": "string"}, "description": "Unit entity IDs to command."}, "commandType": {"type": "string", "description": "Command type: Move, Stop, HoldPosition, or AttackMove."}, "targetX": {"type": "number", "description": "Target X (for Move/AttackMove)."}, "targetZ": {"type": "number", "description": "Target Z (for Move/AttackMove)."}}, "required": ["entityKeyIds", "commandType"], "additionalProperties": False}},
    {"name": "diagnostics_run_synthetic_gameplay_scenario", "description": "Runs a bounded, paused synthetic gameplay seam through the loader directors in one bridge call. It spawns only the requested namespaced units, applies stance/loadout/affix/veterancy/aura/pulse/hazard/formation/visual checks, then cleans up the synthetic entities.", "parameters": {"type": "object", "properties": {"unitIdsCsv": {"type": "string", "description": "Comma-separated namespaced unit IDs (1 to 8)."}, "targetX": {"type": "number", "description": "Formation target center X."}, "targetZ": {"type": "number", "description": "Formation target center Z."}, "formationType": {"type": "string", "enum": ["Line", "Wedge", "Column", "Box", "Surround"], "description": "Formation used by the scenario (default Wedge)."}, "spacing": {"type": "number", "description": "Formation spacing in meters (1.5 to 25.0; default 4.0)."}}, "required": ["unitIdsCsv", "targetX", "targetZ"], "additionalProperties": False}},
    {"name": "diagnostics_order_formation_move", "description": "Dispatches a tactical formation movement to multiple units via LoaderDiagnosticsComponent. Formations: Line, Wedge, Column, Box, Surround.", "parameters": {"type": "object", "properties": {"entityKeyIds": {"type": "array", "items": {"type": "string"}, "description": "Unit entity IDs to command."}, "formationType": {"type": "string", "description": "Formation type: Line, Wedge, Column, Box, or Surround."}, "targetX": {"type": "number", "description": "Target center X coordinate."}, "targetZ": {"type": "number", "description": "Target center Z coordinate."}, "spacing": {"type": "number", "description": "Spacing between units in meters (default 4.0, range 1.5 to 25.0)."}}, "required": ["entityKeyIds", "formationType", "targetX", "targetZ"], "additionalProperties": False}},
    {"name": "diagnostics_order_unit_tactical_stance", "description": "Assigns a tactical combat stance to a unit via LoaderDiagnosticsComponent. Stances: Standard, Aggressive, Defensive, HoldFire, Kiting.", "parameters": {"type": "object", "properties": {"entityKeyId": {"type": "string", "description": "Unit entity ID."}, "stanceName": {"type": "string", "description": "Tactical stance: Standard, Aggressive, Defensive, HoldFire, or Kiting."}}, "required": ["entityKeyId", "stanceName"], "additionalProperties": False}},
    {"name": "diagnostics_order_group_tactical_stance", "description": "Assigns a tactical combat stance to a group of units via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"entityKeyIds": {"type": "array", "items": {"type": "string"}, "description": "Unit entity IDs to command."}, "stanceName": {"type": "string", "description": "Tactical stance: Standard, Aggressive, Defensive, HoldFire, or Kiting."}}, "required": ["entityKeyIds", "stanceName"], "additionalProperties": False}},
    {"name": "diagnostics_get_tactical_stance_census", "description": "Queries tactical combat stances across living units via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_order_apply_unit_affix", "description": "Applies an elite affix/trait to a unit via LoaderDiagnosticsComponent. Affixes: Swift, Juggernaut, Overcharged, Vampiric, Bulwark, Volatile.", "parameters": {"type": "object", "properties": {"entityKeyId": {"type": "string", "description": "Unit entity ID."}, "affixName": {"type": "string", "description": "Affix name: Swift, Juggernaut, Overcharged, Vampiric, Bulwark, or Volatile."}}, "required": ["entityKeyId", "affixName"], "additionalProperties": False}},
    {"name": "diagnostics_order_remove_unit_affix", "description": "Removes an active elite affix from a unit via LoaderDiagnosticsComponent (or 'All' to clear all affixes).", "parameters": {"type": "object", "properties": {"entityKeyId": {"type": "string", "description": "Unit entity ID."}, "affixName": {"type": "string", "description": "Affix name to remove, or 'All' to clear."}}, "required": ["entityKeyId"], "additionalProperties": False}},
    {"name": "diagnostics_get_unit_affix_census", "description": "Queries active elite affixes across living units via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_order_dispatch_ai_assault_wave", "description": "Manually triggers and coordinates an AI assault wave with elevated aggression via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"waveNumber": {"type": "integer", "description": "Attack wave number (default 1)."}, "aggressionBoost": {"type": "number", "description": "Aggression multiplier (1.0 to 3.0, default 1.5)."}, "targetBaseIndex": {"type": "integer", "description": "Target base index (default 0)."}}, "additionalProperties": False}},
    {"name": "diagnostics_order_unit_hold_position", "description": "Dispatches a hold-position command to a unit via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"entityKeyId": {"type": "string", "description": "Unit entity ID (optional)."}}, "additionalProperties": False}},
    {"name": "diagnostics_order_unit_stop", "description": "Dispatches a stop command to a unit via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"entityKeyId": {"type": "string", "description": "Unit entity ID (optional)."}}, "additionalProperties": False}},
    {"name": "diagnostics_reveal_fog_of_war", "description": "Reveals fog of war via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_select_category", "description": "Selects units by category via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"category": {"type": "string", "description": "Category name."}}, "required": ["category"], "additionalProperties": False}},
    {"name": "diagnostics_select_control_group", "description": "Selects a control group via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"groupNumber": {"type": "integer", "description": "Control group number."}}, "required": ["groupNumber"], "additionalProperties": False}},
    {"name": "diagnostics_select_units", "description": "Selects units by CSV keys via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"entityKeysCsv": {"type": "string", "description": "Comma-separated entity keys."}, "additive": {"type": "boolean", "description": "Additive selection (default false)."}}, "required": ["entityKeysCsv"], "additionalProperties": False}},
    {"name": "diagnostics_set_fog_of_war", "description": "Enables/disables fog of war via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"disableFog": {"type": "boolean", "description": "Disable fog."}}, "required": ["disableFog"], "additionalProperties": False}},
    {"name": "diagnostics_set_game_time_scale", "description": "Sets game time scale via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"timeScale": {"type": "number", "description": "Time scale."}}, "required": ["timeScale"], "additionalProperties": False}},
    {"name": "diagnostics_spawn_entity", "description": "Spawns an entity via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"entityId": {"type": "string", "description": "Entity ID."}, "side": {"type": "string", "enum": ["Player", "AI"], "description": "Spawn side: Player or AI (case-insensitive)."}, "targetX": {"type": "number", "description": "World X coordinate."}, "targetZ": {"type": "number", "description": "World Z coordinate."}, "customName": {"type": "string", "description": "Custom name (optional)."}}, "required": ["entityId", "side", "targetX", "targetZ"], "additionalProperties": False}},
    {"name": "diagnostics_trigger_match_end", "description": "Triggers match end via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"win": {"type": "boolean", "description": "Win flag."}}, "required": ["win"], "additionalProperties": False}},
    {"name": "diagnostics_get_ai_director_census", "description": "Queries native AI Director state, attack wave progress, and command queues via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_set_attack_wave_number", "description": "Sets the current AI attack wave number via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"waveNumber": {"type": "integer", "description": "Attack wave number."}}, "required": ["waveNumber"], "additionalProperties": False}},
    {"name": "diagnostics_increment_attack_wave", "description": "Increments the AI attack wave number by 1 via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_toggle_ai_debug_visuals", "description": "Toggles or sets AI debug visual tags and tactical commands on screen via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"showTags": {"type": "boolean", "description": "Show AI tags."}, "showCommands": {"type": "boolean", "description": "Show tactical commands."}}, "required": ["showTags", "showCommands"], "additionalProperties": False}},
    {"name": "diagnostics_get_camera_census", "description": "Queries active camera viewport, ground coordinates, and FOV via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_move_camera", "description": "Moves the main camera to target ground coordinates (X, Z) via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"targetX": {"type": "number", "description": "Target X."}, "targetZ": {"type": "number", "description": "Target Z."}}, "required": ["targetX", "targetZ"], "additionalProperties": False}},
    {"name": "diagnostics_focus_camera_on_entity", "description": "Centers the camera on an entity by key ID via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"entityKeyId": {"type": "string", "description": "Entity key ID."}}, "required": ["entityKeyId"], "additionalProperties": False}},
    {"name": "diagnostics_set_camera_zoom", "description": "Sets main camera FOV / zoom level via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"fieldOfView": {"type": "number", "description": "Target field of view (FOV)."}}, "required": ["fieldOfView"], "additionalProperties": False}},
    {"name": "diagnostics_get_harvester_census", "description": "Queries active harvesters, crystal payloads, and states via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_order_harvester_harvest", "description": "Orders a harvester unit to claim nearest crystal node and commence harvesting via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"harvesterKeyId": {"type": "string", "description": "Harvester entity key ID."}}, "required": ["harvesterKeyId"], "additionalProperties": False}},
    {"name": "diagnostics_order_harvester_return", "description": "Orders a harvester unit to return cargo to closest refinery via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"harvesterKeyId": {"type": "string", "description": "Harvester entity key ID."}}, "required": ["harvesterKeyId"], "additionalProperties": False}},
    {"name": "diagnostics_get_audio_census", "description": "Queries audio mixer volume slider levels via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_get_visual_surface_census", "description": "Queries visual surface material and vertex-wear (welds, mud, micro-relief) telemetry via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"maxTrackedUnits": {"type": "integer", "description": "Maximum number of units to inspect (default 64)."}}, "additionalProperties": False}},
    {"name": "diagnostics_get_visual_quality_census", "description": "Queries URP/PBR material compliance, texture bindings, GPU instancing, and procedural vertex-wear quality metrics for living units via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"maxTrackedUnits": {"type": "integer", "description": "Maximum number of units to inspect (default 64)."}}, "additionalProperties": False}},
    {"name": "diagnostics_get_visual_material_census", "description": "Queries visual material and texture binding telemetry (PBR maps, shader names, keywords) via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"maxTrackedUnits": {"type": "integer", "description": "Maximum number of units to inspect (default 64)."}}, "additionalProperties": False}},
    {"name": "diagnostics_get_socket_audit_census", "description": "Queries canonical Socket_* transform audit (muzzle, damage smoke, foot contact, engine audio, ambiguity) via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"maxTrackedUnits": {"type": "integer", "description": "Maximum number of units to inspect (default 64)."}}, "additionalProperties": False}},
    {"name": "diagnostics_get_collider_audit_census", "description": "Queries collider and rigidbody telemetry (trigger/solid counts, types, kinematic) via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_get_particle_vfx_census", "description": "Queries particle systems, trails, and line renderers per unit via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_get_lod_audit_census", "description": "Queries LOD group and level counts per unit via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_set_audio_volume", "description": "Sets volume slider level (0.0 to 1.0) for an audio channel (Master, Music, Gameplay, UI) via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"channel": {"type": "string", "enum": ["Master", "Music", "Gameplay", "UI"], "description": "Audio channel."}, "volumeSlider": {"type": "number", "description": "Volume slider value (0.0 to 1.0)."}}, "required": ["channel", "volumeSlider"], "additionalProperties": False}},
    {"name": "diagnostics_get_tech_catalog", "description": "Queries registered tech upgrades, research branches, and relics from balancing stores via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_get_upgrade_census", "description": "Queries telemetry census of registered card upgrades in the balancing store via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"maxEntries": {"type": "integer", "description": "Maximum number of upgrade entries to return (default 64)."}}, "additionalProperties": False}},
    {"name": "diagnostics_get_relic_census", "description": "Queries telemetry census of registered player relics in the balancing store via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"maxEntries": {"type": "integer", "description": "Maximum number of relic entries to return (default 64)."}}, "additionalProperties": False}},
        {"name": "diagnostics_get_veterancy_census", "description": "Queries live unit combat XP, rank tiers, and veterancy bonus multipliers via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_order_grant_combat_xp", "description": "Grants combat experience (XP) to a unit by entity key ID via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"entityKeyId": {"type": "string", "description": "Target entity key ID."}, "xpAmount": {"type": "integer", "description": "Amount of combat XP to grant."}}, "required": ["entityKeyId", "xpAmount"], "additionalProperties": False}},
    {"name": "diagnostics_order_set_veterancy_level", "description": "Sets a unit's veterancy level directly by entity key ID via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"entityKeyId": {"type": "string", "description": "Target entity key ID."}, "targetLevel": {"type": "integer", "description": "Target veterancy level (e.g. 1 to 5)."}}, "required": ["entityKeyId", "targetLevel"], "additionalProperties": False}},
    {"name": "diagnostics_get_command_aura_census", "description": "Queries active captain command auras and linked follower chains via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_order_command_aura_pulse", "description": "Dispatches a dynamic command aura pulse from a captain entity via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"captainKeyId": {"type": "string", "description": "Captain entity key ID."}, "auraType": {"type": "string", "description": "Aura type: Inspire, Aegis, Swiftness."}, "radius": {"type": "number", "description": "Command aura radius in meters (3.0 to 50.0)."}, "durationSeconds": {"type": "number", "description": "Aura duration in seconds (1.0 to 60.0)."}}, "required": ["captainKeyId", "auraType", "radius", "durationSeconds"], "additionalProperties": False}},
    {"name": "diagnostics_order_clear_command_aura_links", "description": "Clears command aura links for a captain entity via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"captainKeyId": {"type": "string", "description": "Captain entity key ID."}}, "required": ["captainKeyId"], "additionalProperties": False}},
    {"name": "diagnostics_get_hazard_census", "description": "Queries active battlefield environmental hazard zones and trigger metrics via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_order_spawn_hazard_zone", "description": "Spawns a battlefield environmental hazard zone (Firestorm, AcidPool, EmpField, CryoMist, NaniteField) via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"hazardType": {"type": "string", "enum": ["Firestorm", "AcidPool", "EmpField", "CryoMist", "NaniteField"], "description": "Hazard archetype."}, "x": {"type": "number", "description": "Center X coordinate."}, "z": {"type": "number", "description": "Center Z coordinate."}, "radius": {"type": "number", "description": "Radius in meters (2.0 to 50.0)."}, "durationSeconds": {"type": "number", "description": "Duration in seconds (1.0 to 120.0)."}, "tickDamage": {"type": "number", "description": "Damage or healing amount per tick."}}, "required": ["hazardType", "x", "z", "radius", "durationSeconds", "tickDamage"], "additionalProperties": False}},
    {"name": "diagnostics_order_clear_hazard_zones", "description": "Clears all active battlefield environmental hazard zones and trigger colliders via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_get_unit_loadout_census", "description": "Queries diagnostic telemetry census of active unit loadouts and EntityFactory pre-spawn pools via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_order_apply_unit_loadout", "description": "Dispatches a loadout or variant change (AP, HE, Speed, Siege, Reinforced, Standard) to a living unit via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"entityKeyId": {"type": "string", "description": "Target unit unique key ID or name."}, "loadoutId": {"type": "string", "description": "Loadout or variant ID (e.g. 'AP', 'HE', 'Speed', 'Siege', 'Reinforced', 'Standard')."}}, "required": ["entityKeyId", "loadoutId"], "additionalProperties": False}},
    {"name": "diagnostics_order_apply_visual_variant", "description": "Dispatches a visual variant change (Level 1 Primary Chassis or Level 2 Tactical Variant) to a living unit via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"entityKeyId": {"type": "string", "description": "Target unit unique key ID or index."}, "variantId": {"type": "string", "description": "Visual variant ID, preset slug, or 'default'/'primary' to reset."}}, "required": ["entityKeyId", "variantId"], "additionalProperties": False}},
    {"name": "diagnostics_get_visual_variant_census", "description": "Queries diagnostic telemetry census of registered visual variants (Level 1 Primary Chassis and Level 2 Tactical Variants) across units via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"maxUnits": {"type": "integer", "description": "Maximum units to inspect (default 64)."}}, "additionalProperties": False}},
    {"name": "diagnostics_order_warm_prespawn_pool", "description": "Manually warms an EntityFactory pre-spawn pool for a given entity ID via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"entityId": {"type": "string", "description": "Entity ID to pre-spawn pool."}, "count": {"type": "integer", "description": "Instance count to pre-spawn (1 to 50)."}}, "required": ["entityId", "count"], "additionalProperties": False}},
    {"name": "diagnostics_get_ai_doctrine_census", "description": "Queries diagnostic telemetry census of active AI tactical doctrine and base deck entries via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_order_set_ai_doctrine", "description": "Sets the active AI tactical doctrine (Standard, Assault, Siege, Kiting, Fortify) and aggression multiplier via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"doctrineName": {"type": "string", "description": "AI doctrine name ('Standard', 'Assault', 'Siege', 'Kiting', 'Fortify')."}, "aggressionMultiplier": {"type": "number", "description": "Aggression multiplier (0.1 to 5.0, default 1.0)."}}, "required": ["doctrineName", "aggressionMultiplier"], "additionalProperties": False}},
    {"name": "diagnostics_order_inject_ai_deck_unit", "description": "Injects or updates a unit entry in an AI base deck via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"baseIndex": {"type": "integer", "description": "Target AI base index (0-based)."}, "unitId": {"type": "string", "description": "Unit entity ID to produce."}, "countAtStart": {"type": "integer", "description": "Starting unit quota (0 to 50)."}, "maxLiving": {"type": "integer", "description": "Max simultaneous living unit count (1 to 50)."}}, "required": ["baseIndex", "unitId", "countAtStart", "maxLiving"], "additionalProperties": False}},
    {"name": "diagnostics_get_agent_coordination", "description": "Queries multi-agent coordination census: active agent sessions, heartbeats, and advisory locks via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_register_agent_session", "description": "Registers or refreshes an agent session with role, notes, and assigned sector via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"agentId": {"type": "string", "description": "Unique agent identifier (e.g. 'hermes-1', 'antigravity')."}, "agentRole": {"type": "string", "description": "Agent role (e.g. 'Coordinator', 'Tactician', 'Analyst')."}, "notes": {"type": "string", "description": "Optional notes or capabilities."}, "assignedSector": {"type": "string", "description": "Assigned tactical sector (e.g. 'North', 'South', 'Base', 'Global')."}}, "required": ["agentId"], "additionalProperties": False}},
    {"name": "diagnostics_ping_agent", "description": "Sends an agent heartbeat ping to the Rogue Command runtime via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"agentId": {"type": "string", "description": "Agent identifier."}, "message": {"type": "string", "description": "Heartbeat or message payload."}}, "required": ["agentId"], "additionalProperties": False}},
    {"name": "diagnostics_acquire_agent_lock", "description": "Acquires or renews an advisory lock on a game resource (e.g. unit key, camera, card) via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"agentId": {"type": "string", "description": "Agent identifier holding the lock."}, "resourceKey": {"type": "string", "description": "Resource identifier (e.g. 'unit:scout_1', 'camera:tactical')."}, "durationSeconds": {"type": "number", "description": "Lock duration in seconds (1.0 to 120.0, default 30.0)."}}, "required": ["agentId", "resourceKey"], "additionalProperties": False}},
    {"name": "diagnostics_release_agent_lock", "description": "Releases an advisory lock on a game resource via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"agentId": {"type": "string", "description": "Agent identifier releasing the lock."}, "resourceKey": {"type": "string", "description": "Resource identifier to unlock."}}, "required": ["resourceKey"], "additionalProperties": False}},
    {"name": "diagnostics_deregister_agent_session", "description": "Deregisters an agent session and automatically releases all advisory locks owned by that agent via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"agentId": {"type": "string", "description": "Agent identifier to deregister."}}, "required": ["agentId"], "additionalProperties": False}},
    {"name": "diagnostics_post_agent_message", "description": "Posts a tactical coordination message or intent to the shared multi-agent blackboard via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"senderAgentId": {"type": "string", "description": "Agent identifier posting the message."}, "messageType": {"type": "string", "description": "Message category: TacticalIntent, TargetLock, StatusAlert, SectorAssignment, StrategyProposal."}, "content": {"type": "string", "description": "Message body or tactical plan."}, "targetAgentId": {"type": "string", "description": "Target agent identifier (default 'all' for broadcast)."}}, "required": ["senderAgentId", "content"], "additionalProperties": False}},
    {"name": "diagnostics_acquire_agent_lock_batch", "description": "Atomically acquires advisory locks on multiple game resources for an agent via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"agentId": {"type": "string", "description": "Agent identifier holding the locks."}, "resourceKeys": {"type": "array", "items": {"type": "string"}, "description": "List of resource identifiers to lock."}, "durationSeconds": {"type": "number", "description": "Lock duration in seconds (1.0 to 120.0, default 30.0)."}}, "required": ["agentId", "resourceKeys"], "additionalProperties": False}},
    {"name": "diagnostics_release_agent_lock_batch", "description": "Releases advisory locks on multiple game resources for an agent via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"agentId": {"type": "string", "description": "Agent identifier releasing the locks."}, "resourceKeys": {"type": "array", "items": {"type": "string"}, "description": "List of resource identifiers to unlock."}}, "required": ["resourceKeys"], "additionalProperties": False}},
    {"name": "diagnostics_get_visual_census", "description": "Queries in-game visual render, rig architecture, and animation states across living units via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"maxTrackedUnits": {"type": "integer", "description": "Maximum number of units to track (default 32)."}}, "additionalProperties": False}},
    {"name": "diagnostics_inspect_entity_visual", "description": "Inspects deep visual render evidence (renderers, shaders, mesh bounds, LODs), rig hierarchy (armature bones, canonical sockets, socket ambiguity), animation parameters (RCL_IsMoving, RCL_Fire, RCL_Death), and visual director state for a specific unit via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"entityKeyId": {"type": "string", "description": "Target entity key ID (e.g. RCL_Unit_ArachnidTank, entity ID, or unique ID)."}}, "required": ["entityKeyId"], "additionalProperties": False}},
    {"name": "diagnostics_start_renderer_frame_probe", "description": "Attaches temporary bounded observers to active skinned renderers for one live unit. Samples URP RenderPipelineManager.endCameraRendering events for direct Game cameras, camera-frustum/culling state, Animator/retarget state, and a limited set of BakeMesh vertex deltas. No framebuffer, screenshot, or pixel capture. Automatically stops after 20 seconds; explicitly stop it to collect final evidence and remove observers.", "parameters": {"type": "object", "properties": {"entityKeyId": {"type": "string", "description": "Live unit entity key ID, EntityId, GameObject name, or UniqueEntityId."}, "rendererNameContains": {"type": "string", "description": "Optional case-insensitive substring to limit the renderer names included in the probe."}, "sampleLimit": {"type": "integer", "description": "Per-renderer direct Game-camera sample cap (default 12, maximum 24)."}}, "required": ["entityKeyId"], "additionalProperties": False}},
    {"name": "diagnostics_read_renderer_frame_probe", "description": "Reads bounded structured URP camera-render callbacks, visibility/frustum state, and baked-vertex samples for an active renderer frame probe. This does not capture pixels.", "parameters": {"type": "object", "properties": {"token": {"type": "string", "description": "Probe token returned by diagnostics_start_renderer_frame_probe."}}, "required": ["token"], "additionalProperties": False}},
    {"name": "diagnostics_stop_renderer_frame_probe", "description": "Stops and removes a renderer frame probe's temporary observers, returns its bounded final camera/Animator/deformation telemetry, and clears the retained result. No images are captured.", "parameters": {"type": "object", "properties": {"token": {"type": "string", "description": "Probe token returned by diagnostics_start_renderer_frame_probe."}}, "required": ["token"], "additionalProperties": False}},
    {"name": "diagnostics_get_loaded_unit_visual_audit", "description": "Queries load-time visual, rig, shader, and socket audit evidence for loaded unit and building prefabs via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"maxEntries": {"type": "integer", "description": "Maximum number of audited prefabs to return (default 64)."}}, "additionalProperties": False}},
    {"name": "diagnostics_get_loader_audit_census", "description": "Queries unit and AI loader runtime readiness audit (registered units, factories, visual prefabs, mechanics, pre-spawn pools, and AI profiles with squad defaults) via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"maxUnits": {"type": "integer", "description": "Maximum number of units to include (default 64)."}, "maxAiProfiles": {"type": "integer", "description": "Maximum number of AI profiles to include (default 32)."}}, "additionalProperties": False}},
    {"name": "diagnostics_order_reconcile_loader_state", "description": "Actively reconciles unit and AI loader runtime state on the live battlefield (warms missing pre-spawn pools, injects missing AI deck units, and re-applies unit & AI squad defaults) via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_get_unit_mechanics_census", "description": "Queries live census of active unit mechanics across battlefield: healing cooldowns/targets, active captain auras, shield regen rate/timer, XP/veterancy, applied affixes, active tactical stances, critical hit chance, min attack range, on-hit status effects, and visual director states via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"maxUnits": {"type": "integer", "description": "Maximum number of units to track (default 64)."}}, "additionalProperties": False}},
    {"name": "diagnostics_order_unit_visual_feedback", "description": "Commands immediate visual director feedback on a unit: weapon recoil with muzzle flash (WeaponRecoil), jammed turret tilt (JamTurret/UnjamTurret), factory bay door deployment (OpenBayDoors/CloseBayDoors), or walker footstep ground impact (FootstepPulse) via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"entityKeyId": {"type": "string", "description": "Target unit key ID or name."}, "feedbackType": {"type": "string", "enum": ["WeaponRecoil", "JamTurret", "UnjamTurret", "OpenBayDoors", "CloseBayDoors", "FootstepPulse"], "description": "Type of visual feedback pulse."}, "magnitude": {"type": "number", "description": "Feedback magnitude or recoil distance (default 0.22, range 0.05 to 1.0)."}}, "required": ["entityKeyId", "feedbackType"], "additionalProperties": False}},
    {"name": "diagnostics_order_unit_attachment", "description": "Commands visual hardpoint attachment state on a unit's visual hierarchy (e.g. APS radar, ERA reactive armor blocks, FuelDrums auxiliary tanks, Armor plating, Shield emitters) via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"entityKeyId": {"type": "string", "description": "Target unit key ID or name."}, "attachmentType": {"type": "string", "description": "Attachment or hardpoint name (e.g. 'APS', 'ERA', 'FuelDrums', 'Armor', 'Shield')."}, "active": {"type": "boolean", "description": "Whether to activate or deactivate the attachment visual."}}, "required": ["entityKeyId", "attachmentType", "active"], "additionalProperties": False}},
    {"name": "diagnostics_runtime_snapshot", "description": "Aggregates multiple live runtime diagnostics into a single JSON payload for agents. Optional 'getters' (list of method names) and 'exclude' (list) filter the aggregated set.", "parameters": {"type": "object", "properties": {"getters": {"type": "array", "items": {"type": "string"}, "description": "Subset of getter method names to include (default: all)."}, "exclude": {"type": "array", "items": {"type": "string"}, "description": "Getter method names to exclude."}}, "additionalProperties": False}},
    {"name": "diagnostics_list_runtime_getters", "description": "Lists the runtime diagnostic getter method names available for diagnostics_runtime_snapshot and diagnostics_call_getter.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_call_getters", "description": "Calls multiple runtime diagnostic getters in parallel and returns their decoded JSON payloads keyed by getter name.", "parameters": {"type": "object", "properties": {"getters": {"type": "array", "items": {"type": "string"}, "description": "List of getter method names to call."}}, "required": ["getters"], "additionalProperties": False}},
    {"name": "diagnostics_runtime_summary", "description": "Returns a compact summary of key runtime scalars (packs, units, AI, production, base integrity, hazards) for quick agent checks.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_runtime_scalars", "description": "Aggregates scalar runtime counters (live counts, profile count, range geometry bounds, point count) into JSON.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_get_visual_health_summary", "description": "Queries consolidated visual health verdict across quality, surface, material, and socket censuses via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"maxTrackedUnits": {"type": "integer", "description": "Maximum number of units to track (default 64)."}}, "additionalProperties": False}},
    {"name": "diagnostics_get_combat_health_summary", "description": "Aggregates battlefield, rosters, production, base, AI director, hazard, veterancy, combat status, economy, and system health into a single combat-health payload.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_get_entity_detail", "description": "Returns a single-entity drill-down across battlefield, rosters, threat, stance, affix, veterancy, loadout, mechanics, visual, and balance for one entity key.", "parameters": {"type": "object", "properties": {"entityKeyId": {"type": "string", "description": "Target entity key ID."}}, "required": ["entityKeyId"], "additionalProperties": False}},
    {"name": "diagnostics_get_agent_lock_status", "description": "Reports whether a resource is locked, its owner and remaining time, and whether the requesting agent session is active.", "parameters": {"type": "object", "properties": {"agentId": {"type": "string", "description": "Requesting agent identifier."}, "resourceKey": {"type": "string", "description": "Resource identifier to check."}}, "required": ["agentId", "resourceKey"], "additionalProperties": False}},
    {"name": "automation_run_sequence", "description": "Runs an ordered sequence of existing diagnostics MCP tools without screenshots or computer-use interaction. A shared 1-120 second budget limits delays and bridge request timeouts. If a request times out in flight, completion is unknown; inspect the postcondition before retrying. Failures can stop the sequence, and mutations keep their own tool/profile safety contract.", "parameters": {"type": "object", "properties": {"steps": {"type": "array", "minItems": 1, "maxItems": 16, "items": {"type": "object", "properties": {"tool": {"type": "string", "description": "Existing diagnostics tool name."}, "arguments": {"type": "object", "description": "Arguments for the selected tool."}, "delayAfterMs": {"type": "integer", "description": "Optional delay after the step (0-2000 ms)."}}, "required": ["tool"], "additionalProperties": False}}, "stopOnFailure": {"type": "boolean", "description": "Stop after a tool error or JSON success=false response (default true)."}, "maxDurationSeconds": {"type": "integer", "description": "Shared request and delay budget (1-120 seconds, default 60)."}}, "required": ["steps"], "additionalProperties": False}},
    {"name": "diagnostics_preflight", "description": "Runs parallel preflight checks (system ping, capabilities, diagnostics component discovery) and returns readiness verdict with checks, system health, and time scale.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_get_gui_state", "description": "Returns overlay, range display, hooks, time scale, and camera state from the running game.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_request_graceful_exit", "description": "Requests normal Unity player shutdown through Application.Quit without synthetic keyboard input. Required confirmExit=true because the game may persist normal save state; verify closure with system_get_game_process_state.", "parameters": {"type": "object", "properties": {"confirmExit": {"type": "boolean", "description": "Must be true to acknowledge normal shutdown and possible save persistence."}}, "required": ["confirmExit"], "additionalProperties": False}},
    {"name": "diagnostics_get_steam_cloud_state", "description": "Reads Steamworks account and Rogue Command app Cloud toggles; no profile or save-file access.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_set_steam_cloud_enabled", "description": "Changes only Rogue Command's Steam Cloud app toggle. Requires explicitUserAuthorization=true after the user explicitly authorized this exact Cloud change; does not touch saves. Verify the returned app state before exit/relaunch.", "parameters": {"type": "object", "properties": {"enabled": {"type": "boolean", "description": "Desired Cloud state for this app only."}, "explicitUserAuthorization": {"type": "boolean", "description": "Must be true only when the user explicitly authorized changing Rogue Command Steam Cloud."}}, "required": ["enabled", "explicitUserAuthorization"], "additionalProperties": False}},
    {"name": "diagnostics_order_batch", "description": "Executes multiple diagnostic commands in parallel and returns aggregated results with meta counts.", "parameters": {"type": "object", "properties": {"operations": {"type": "array", "items": {"type": "object", "properties": {"tool": {"type": "string"}, "arguments": {"type": "object"}}, "required": ["tool"]}, "description": "List of operations to execute."}}, "required": ["operations"], "additionalProperties": False}},
    {"name": "diagnostics_get_control_guide", "description": "Returns a control guide with preflight, control, and cleanup recipes for Profile2 QA.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_gui_control", "description": "Composite GUI control: open/close overlay, toggle range display, select tab, set timeScale, move/focus camera, and select units in one round-trip.", "parameters": {"type": "object", "properties": {"overlayOpen": {"type": "boolean"}, "rangeEnabled": {"type": "boolean"}, "overlayTab": {"type": "integer"}, "timeScale": {"type": "number"}, "cameraX": {"type": "number"}, "cameraZ": {"type": "number"}, "focusEntityKeyId": {"type": "string"}}, "additionalProperties": False}},
    {"name": "diagnostics_get_ui_accessibility_census", "description": "Screenless Unity UI discovery: returns selectable controls, accessible text/TMP labels, stable hierarchy paths, active/interactable state, selection, and screen bounds. No screenshots are captured. Use this before clicking profile/menu controls; instance IDs are ephemeral.", "parameters": {"type": "object", "properties": {"query": {"type": "string", "description": "Optional case-insensitive substring over label, object name, and hierarchy path."}, "includeInactive": {"type": "boolean", "description": "Include inactive controls (default false)."}, "maxResults": {"type": "integer", "description": "Maximum rows (1-100, default 32)."}}, "additionalProperties": False}},
    {"name": "diagnostics_ui_click_text", "description": "Clicks one active Unity UI control by accessible label or exact hierarchyPath from the UI census through the real EventSystem raycast/pointer path. Ambiguous matches fail closed; use the scene-qualified path for unlabeled controls. No screenshots or direct Button.Invoke calls.", "parameters": {"type": "object", "properties": {"label": {"type": "string", "description": "Optional Unity Text/TMP label."}, "exactMatch": {"type": "boolean", "description": "Require an exact case-insensitive label match (default true)."}, "hierarchyPath": {"type": "string", "description": "Optional exact scene-qualified hierarchy path from the UI census; required if label is empty."}}, "additionalProperties": False}},
    {"name": "diagnostics_wait_for_ui_text", "description": "Waits for one active Unity UI control by label or exact hierarchyPath to appear and optionally become interactable, polling the screenshot-free accessibility census. Bounded to 30 seconds; returns the matched control or timeout state.", "parameters": {"type": "object", "properties": {"label": {"type": "string", "description": "Optional Unity Text/TMP label to wait for."}, "exactMatch": {"type": "boolean", "description": "Require an exact case-insensitive label match (default true)."}, "hierarchyPath": {"type": "string", "description": "Optional exact scene-qualified hierarchy path; required if label is empty."}, "requireInteractable": {"type": "boolean", "description": "Wait until the control is interactable and on screen (default true)."}, "timeoutSeconds": {"type": "number", "description": "Wait limit from 0.1 to 30 seconds (default 10)."}, "pollIntervalMs": {"type": "integer", "description": "Poll interval from 100 to 1000 ms (default 250)."}}, "additionalProperties": False}},
    {"name": "diagnostics_ui_click", "description": "Click a UI element or 3D world object by real screen coordinates (not InvokeOnClick): RectTransform center → EventSystem raycast → pointer sequence replay. Accepts buttonName/selector/gameObjectId or pixel coordinates x/y; occluded, off-screen, or non-interactive targets fail closed.", "parameters": {"type": "object", "properties": {"buttonName": {"type": "string"}, "selector": {"type": "string", "description": "Name or hierarchy path."}, "gameObjectId": {"type": "integer"}, "x": {"type": "integer"}, "y": {"type": "integer"}}, "additionalProperties": False}},
    {"name": "diagnostics_ui_press_button", "description": "Alias for diagnostics_ui_click: kept for compatibility; accepts x/y for point clicks.", "parameters": {"type": "object", "properties": {"buttonName": {"type": "string"}, "selector": {"type": "string", "description": "Name or hierarchy path."}, "gameObjectId": {"type": "integer"}, "x": {"type": "integer"}, "y": {"type": "integer"}}, "additionalProperties": False}},
    {"name": "diagnostics_fast_snapshot", "description": "Curated preset snapshot: gui, match, combat, health. Faster than full runtime_snapshot.", "parameters": {"type": "object", "properties": {"preset": {"type": "string"}}, "additionalProperties": False}},
    {"name": "diagnostics_get_performance_census", "description": "Queries live frame performance (FPS, frame time, time scale, managed memory, and budget verdict) via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_get_frame_timing_census", "description": "Queries windowed frame timing (average/min/max/p95 frame time, stutter and hitch counts, smoothness verdict) via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_diff_snapshots", "description": "Compares two JSON snapshot payloads at the top level (added/removed/changed keys, bounded detail with omission counters) via LoaderDiagnosticsComponent.", "parameters": {"type": "object", "properties": {"beforeJson": {"type": "string", "description": "JSON object snapshot before the change."}, "afterJson": {"type": "string", "description": "JSON object snapshot after the change."}, "maxChanges": {"type": "integer", "description": "Maximum detail rows per category (default 16, range 1 to 128)."}}, "required": ["beforeJson", "afterJson"], "additionalProperties": False}},
    {"name": "diagnostics_get_intermission_census", "description": "Queries the read-only intermission level-node census with a bounded row count. Rows expose IntermissionNodeRow.level/field and choosable semantics; level/field are the integer keys consumed by the enter/select order tools.", "parameters": {"type": "object", "properties": {"maxRows": {"type": "integer", "description": "Max rows to return (default 64)."}}, "additionalProperties": False}},
    {"name": "diagnostics_order_enter_level", "description": "Full-flow director: selects the choosable IntermissionNodeRow (level/field from diagnostics_get_intermission_census) then drives the native LoadNextLevel corridor. MUTATION: mutates the active run; authorized QA profile only; never edits profile/save files. Success requires an observed scene transition.", "parameters": {"type": "object", "properties": {"level": {"type": "integer", "description": "IntermissionNodeRow.level from diagnostics_get_intermission_census."}, "field": {"type": "integer", "description": "IntermissionNodeRow.field from diagnostics_get_intermission_census."}}, "required": ["level", "field"], "additionalProperties": False}},
    {"name": "diagnostics_jump_to_level", "description": "SYNTHETIC stage jump (user-authorized): StageMap.JumpTo(level[, finished]) via reflection. QA profile only; result labeled synthetic (never natural progression evidence). Async: verify with diagnostics_get_intermission_census.", "parameters": {"type": "object", "properties": {"level": {"type": "integer", "description": "Target stage level (0-100)."}, "finished": {"type": "boolean", "description": "Whether the level is treated as finished (default false)."}}, "required": ["level"], "additionalProperties": False}},
    {"name": "diagnostics_load_next_level", "description": "SYNTHETIC level load (user-authorized): starts LoadScene.LoadNextLevel via reflection. QA profile only; result labeled synthetic. Async: verify with diagnostics_get_intermission_census and diagnostics_get_system_health.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_order_select_level_node", "description": "Director select (MUTATION of map selection state only, no level load): selects the choosable IntermissionNodeRow (level/field from diagnostics_get_intermission_census); gate: choosable node required.", "parameters": {"type": "object", "properties": {"level": {"type": "integer", "description": "IntermissionNodeRow.level from diagnostics_get_intermission_census."}, "field": {"type": "integer", "description": "IntermissionNodeRow.field from diagnostics_get_intermission_census."}}, "required": ["level", "field"], "additionalProperties": False}},
    {"name": "diagnostics_get_level_map_state", "description": "Live LevelMap route state (Game.StageMap.CurrentLevelMap): CurrentLevel/Field, finished/first/lastBeforeBoss/boss flags, LevelCount, MaxWidth, chosenPath, next field, choosableFieldCount (native IsChoosable rule), total fields, boss prefab names.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_get_intermission_node", "description": "One node (level, field): isChoosable, reward flags (rest/shop/card/upgrade/relic/research/coin/crystal/points/drop), BlueprintPoolId, outgoing/incoming edges, fields in its level.", "parameters": {"type": "object", "properties": {"level": {"type": "integer", "description": "Level of the node to inspect."}, "field": {"type": "integer", "description": "Field of the node to inspect."}}, "required": ["level", "field"], "additionalProperties": False}},
    {"name": "diagnostics_get_intermission_graph", "description": "Full LevelMap graph (nodes + Boss) and scored forward routes from choosable frontier to Boss.", "parameters": {"type": "object", "properties": {"preferredRewardType": {"type": "string", "description": "Reward type to prioritize (e.g. RelicReward, CardReward, Shop, RestSite)."}, "maxRoutes": {"type": "integer", "description": "Max routes (default 8)."}, "maxNodes": {"type": "integer", "description": "Max nodes (default 64)."}}, "additionalProperties": False}},
    {"name": "diagnostics_get_intermission_reward_state", "description": "Live Intermission sub-screen state: currencies, open reward queues, ChooseCard offered slots, DeckOverview/AssignUpgrade/RemoveCard flags, and deck cards with upgrades.", "parameters": {"type": "object", "properties": {"maxCards": {"type": "integer", "description": "Max offered/deck cards (default 32)."}}, "additionalProperties": False}},
    {"name": "diagnostics_order_trigger_load_level", "description": "Invokes LoadScene.LoadLevel() for the selected LevelMap node (or Boss when IsLastLevelBeforeBoss).", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_order_choose_intermission_reward", "description": "Selects an offered reward in ChooseCard by rewardId or slotIndex.", "parameters": {"type": "object", "properties": {"rewardId": {"type": "string", "description": "Offered card/upgrade/relic/research ID or productId."}, "slotIndex": {"type": "integer", "description": "0-based offered slot index."}}, "additionalProperties": False}},
    {"name": "diagnostics_order_skip_intermission_reward", "description": "Clicks the ChooseCard skip button when active.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_order_reroll_intermission_reward", "description": "Clicks the ChooseCard reroll button when active and rerolls > 0.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_order_assign_intermission_upgrade", "description": "Assigns the pending upgrade in AssignUpgradeToCardView to a viable deck card (entityId) or cancels.", "parameters": {"type": "object", "properties": {"entityId": {"type": "string", "description": "Viable deck card ID or productId."}, "cancel": {"type": "boolean", "description": "Cancel upgrade assignment instead of applying."}}, "additionalProperties": False}},
    {"name": "diagnostics_order_set_deck_overview_open", "description": "Opens or closes the Intermission DeckOverview screen.", "parameters": {"type": "object", "properties": {"open": {"type": "boolean", "description": "True to show DeckOverview, false to hide."}}, "required": ["open"], "additionalProperties": False}},
    {"name": "diagnostics_get_navigator_state", "description": "Reads the navigator state (current level node and paused flag) read-only.", "parameters": {"type": "object", "properties": {}, "additionalProperties": False}},
    {"name": "diagnostics_order_navigator_pause", "description": "Pauses or unpauses the navigator (MUTATION of in-game state only).", "parameters": {"type": "object", "properties": {"paused": {"type": "boolean", "description": "Whether the navigator should be paused."}}, "required": ["paused"], "additionalProperties": False}},
    {"name": "discover_tools", "description": "Lightweight tool index (name and description only) filtered by query or category. Use before fetching full schemas.", "parameters": {"type": "object", "properties": {"query": {"type": "string"}, "category": {"type": "string"}, "prefix": {"type": "string"}, "includeAliases": {"type": "boolean"}}, "additionalProperties": False}},
    {"name": "describe_tool", "description": "Full input schema for a single tool on demand (lazy schema fetch).", "parameters": {"type": "object", "properties": {"name": {"type": "string"}}, "required": ["name"], "additionalProperties": False}},
]

# ---------------------------------------------------------------------------
# Schema optimisation (wire-size reduction without losing functionality)
# ---------------------------------------------------------------------------
# Single source of truth remains TOOL_SPECS above (233 tools). The helpers
# below produce the optimised view that bepinex_mcp_server and the
# Antigravity schema files advertise:
# - Strip redundant LoaderDiagnosticsComponent provenance phrases from tool
#   descriptions (the tool name already encodes the action; the phrase added
#   ~132 * 30 bytes of noise and did not differentiate any tool). Distinctive
#   trailing detail (formations, stances, affixes, waypoints, examples) is
#   preserved verbatim.
# - Compact empty parameter schemas from
#   {"type":"object","properties":{},"additionalProperties":false} to
#   {"type":"object","additionalProperties":false} (70 tools, strictness kept).
# - Shorten repeated property descriptions ("GameObject instance ID." ->
#   "GameObject ID.", etc.) so alias/canonical pairs cannot drift.
# - Add the runtime-supported optional selector_json to the canonical
#   scene_resolve_selector, then copy canonical parameters to every alias so
#   all 19 aliases deep-equal their canonical tool. All 233 names, types,
#   required lists, enums, and handler endpoints are preserved.

_PROPERTY_DESCRIPTION_SHORTENING: dict[str, str] = {
    "GameObject instance ID.": "GameObject ID.",
    "Parent GameObject instance ID.": "Parent GameObject ID.",
    "Root GameObject instance ID (optional).": "Root GameObject ID (optional).",
    "Maximum number of units to inspect (default 64).": "Max units (default 64).",
    "Component type name (e.g. 'LoaderDiagnosticsComponent').": "Component type name.",
}

_PROVENANCE_PATTERNS: tuple[str, ...] = (
    r" directly from LoaderDiagnosticsComponent(?: / [A-Za-z]+)?",
    r" via LoaderDiagnosticsComponent(?: / [A-Za-z]+)?",
    r" from LoaderDiagnosticsComponent",
    r" on LoaderDiagnosticsComponent",
)


def _optimise_description(desc: str) -> str:
    if desc.startswith("Alias for"):
        return desc
    out = desc.replace(
        " via LoaderDiagnosticsComponent and the native EntityBalancingStore pipeline.",
        " (native balancing pipeline).",
    )
    for pattern in _PROVENANCE_PATTERNS:
        out = re.sub(pattern, "", out)
    out = re.sub(r"  +", " ", out)
    out = out.replace(" .", ".").replace(" ,", ",")
    return out.strip()


def _optimise_parameters(name: str, params: dict) -> dict:
    new = copy.deepcopy(params)
    if name == "scene_resolve_selector":
        props = new.setdefault("properties", {})
        if "selector_json" not in props:
            props["selector_json"] = {"type": "string", "description": "JSON object string of selector."}
    props = new.get("properties")
    if not props:
        return {"type": "object", "additionalProperties": False}
    for prop in props.values():
        d = prop.get("description")
        if d in _PROPERTY_DESCRIPTION_SHORTENING:
            prop["description"] = _PROPERTY_DESCRIPTION_SHORTENING[d]
    return new


def _optimise_spec(spec: dict) -> dict:
    return {
        "name": spec["name"],
        "description": _optimise_description(str(spec["description"])),
        "parameters": _optimise_parameters(str(spec["name"]), dict(spec["parameters"])),
    }


TOOL_SPECS = [_optimise_spec(spec) for spec in TOOL_SPECS]

_BY_NAME: dict[str, dict] = {spec["name"]: spec for spec in TOOL_SPECS}
for _spec in TOOL_SPECS:
    if str(_spec["description"]).startswith("Alias for"):
        _canon = str(_spec["description"])[len("Alias for "):].split(":", 1)[0].strip()
        if _canon in _BY_NAME:
            _spec["parameters"] = copy.deepcopy(_BY_NAME[_canon]["parameters"])
del _BY_NAME


def generate_mcp_schema_files():
    MCP_DIR.mkdir(parents=True, exist_ok=True)
    count = 0
    for spec in TOOL_SPECS:
        name = spec["name"]
        file_path = MCP_DIR / f"{name}.json"
        schema_content = {
            "name": name,
            "description": spec["description"],
            "parameters": spec["parameters"],
        }
        file_path.write_text(json.dumps(schema_content, indent=2), encoding="utf-8")
        count += 1
    print(f"Generated {count} tool schemas in {MCP_DIR}")


if __name__ == "__main__":
    generate_mcp_schema_files()
