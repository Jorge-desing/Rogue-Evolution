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
import csv
import copy
import base64
import subprocess
import contextvars
import math
import re
import urllib.error
import urllib.parse
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
from typing import Any

try:
    from jsonschema import Draft202012Validator, validators as schema_validators
except ImportError:
    Draft202012Validator = None

MAX_HTTP_RESPONSE_BYTES = 16 * 1024 * 1024
_VALIDATORS: dict = {}


def _contains_reference(value):
    if isinstance(value, dict):
        return "$ref" in value or any(_contains_reference(v) for v in value.values())
    return isinstance(value, list) and any(_contains_reference(v) for v in value)


def _validate_tool_arguments(name, arguments, partial=False):
    if Draft202012Validator is None:
        return {"error": "jsonschema 4.26.0 is required; install project requirements", "tool": name, "argumentPath": "/"}
    spec = next((t for t in TOOLS if t["name"] == name), None)
    if spec is None:
        return {"error": "Unknown tool", "tool": name, "argumentPath": "/"}
    key = (name, partial)
    if key not in _VALIDATORS:
        cls = Draft202012Validator
        if partial:
            def wrap(keyword, implementation):
                def check(validator, constraint, instance, schema):
                    if isinstance(instance, dict) and set(instance) == {"$ref"}:
                        return
                    if keyword in ("oneOf", "uniqueItems") and _contains_reference(instance):
                        return
                    yield from implementation(validator, constraint, instance, schema)
                return check
            cls = schema_validators.extend(cls, {k: wrap(k, v) for k, v in cls.VALIDATORS.items()})
        _VALIDATORS[key] = cls(spec["inputSchema"])
    error = next(_VALIDATORS[key].iter_errors(arguments), None)
    if error is not None:
        path = "/" + "/".join(str(p).replace("~", "~0").replace("/", "~1") for p in error.absolute_path)
        return {"error": error.message, "tool": name, "argumentPath": path}
    return None

PROTOCOL_VERSION = "2024-11-05"
SERVER_NAME = "bepinex_mcp"
SERVER_VERSION = "1.6.0"
GAME_PROCESS_NAME = "Rogue Command.exe"
GAME_STEAM_APP_ID = 1461910
BASE_URL = os.environ.get("BEPINEX_MCP_URL", "http://127.0.0.1:8080/mcp/")
_AUTOMATION_DEADLINE: contextvars.ContextVar[float | None] = contextvars.ContextVar("bepinex_automation_deadline", default=None)

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
    "GetActiveStageAiAssociationJson",
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
    "GetIntermissionNativePanelStateJson",
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
    "GetIntermissionGraphJson": ["", 8, 64],
    "GetIntermissionRewardStateJson": [32],
    "GetIntermissionNativePanelStateJson": [32],
    "GetUiAccessibilityCensusJson": ["", False, 100],
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

    automation_deadline = _AUTOMATION_DEADLINE.get()
    effective_timeout = timeout
    if automation_deadline is not None:
        remaining = automation_deadline - time.monotonic()
        if remaining <= 0:
            return json.dumps({
                "error": "automation sequence time budget exhausted before bridge request started",
                "completionStatus": "not_started",
            })
        effective_timeout = min(timeout, remaining)

    try:
        with urllib.request.urlopen(req, timeout=effective_timeout) as resp:
            raw = resp.read(MAX_HTTP_RESPONSE_BYTES + 1)
            if len(raw) > MAX_HTTP_RESPONSE_BYTES:
                raise ValueError("HTTP response exceeds 16 MiB")
            length = resp.headers.get("Content-Length") if hasattr(resp, "headers") else None
            if isinstance(length, str) and length.isascii() and length.isdigit() and len(raw) != int(length):
                raise ValueError("HTTP response body length does not match Content-Length")
            return raw.decode("utf-8", errors="strict")
    except urllib.error.URLError as err:
        failure = {
            "error": f"Failed to communicate with BepInEx MCP on {url}: {err}. Ensure Rogue Command is running."
        }
        failure["completionStatus"] = "unknown"
        failure["message"] = "the request may have been processed remotely; inspect its postcondition before retrying"
        return json.dumps(failure)
    except Exception as exc:
        failure = {
            "error": f"Unexpected error communicating with BepInEx MCP: {exc}"
        }
        failure["completionStatus"] = "unknown"
        failure["message"] = "the request may have been processed remotely; inspect its postcondition before retrying"
        return json.dumps(failure)


def _tool_response(text: str, is_error: bool = False) -> dict[str, Any]:
    # Transport and diagnostics helpers encode failures as top-level JSON errors.
    # Preserve ordinary data (including nested error fields) without reclassifying it.
    if not is_error:
        try:
            payload = json.loads(text)
            is_error = isinstance(payload, dict) and bool(payload.get("error"))
        except (TypeError, ValueError):
            pass
    return {
        "content": [{"type": "text", "text": text}],
        "isError": is_error,
    }


def _component_detail_value(details: dict[str, Any], name: str) -> Any:
    value = details.get(name)
    if isinstance(value, dict) and "value" in value:
        return value["value"]
    return value


def _parse_component_bool(value: Any) -> bool | None:
    if isinstance(value, bool):
        return value
    if isinstance(value, str):
        folded = value.strip().casefold()
        if folded in ("true", "1"):
            return True
        if folded in ("false", "0"):
            return False
    return None


def _normalise_ui_hierarchy_path(value: str) -> tuple[str, str]:
    text = str(value or "").strip()
    scene = ""
    if "::" in text:
        scene, text = text.split("::", 1)
    text = urllib.parse.unquote(text.replace("\\", "/")).lstrip("/")
    text = re.sub(r"\[\d+\]", "", text)
    text = re.sub(r"/+", "/", text).strip("/")
    return scene.strip().casefold(), text.casefold()


def _scene_search_rows(data: Any) -> list[dict[str, Any]]:
    if isinstance(data, list):
        return [row for row in data if isinstance(row, dict)]
    if not isinstance(data, dict) or data.get("truncated") is True:
        return []
    for key in ("items", "objects", "results", "gameObjects"):
        rows = data.get(key)
        if isinstance(rows, list):
            return [row for row in rows if isinstance(row, dict)]
    return [data] if "id" in data or "gameObjectId" in data else []


def _scene_search_object_id(row: dict[str, Any]) -> int | None:
    for key in ("id", "gameObjectId"):
        value = row.get(key)
        if value is None or isinstance(value, bool):
            continue
        try:
            return int(value)
        except (TypeError, ValueError, OverflowError):
            continue
    return None


def _get_diagnostics_go_id() -> int:
    global _DIAGNOSTICS_GO_ID
    if _DIAGNOSTICS_GO_ID is not None:
        return _DIAGNOSTICS_GO_ID

    # 1. Primary: search by component name via scene:find_objects_with_component
    raw = _call_http("scene:find_objects_with_component", params={"componentName": "LoaderDiagnosticsComponent"})
    try:
        data = json.loads(raw)
        for row in _scene_search_rows(data):
            game_object_id = _scene_search_object_id(row)
            if game_object_id is not None:
                _DIAGNOSTICS_GO_ID = game_object_id
                return _DIAGNOSTICS_GO_ID
    except Exception:
        pass

    # 2. Secondary fallback: search gameobjects by componentName (includes DontDestroyOnLoad)
    try:
        raw = _call_http("scene/search_gameobjects", params={"componentName": "LoaderDiagnosticsComponent", "includeInactive": "true", "limit": "50"})
        data = json.loads(raw)
        for item in _scene_search_rows(data):
            game_object_id = _scene_search_object_id(item)
            if game_object_id is not None:
                _DIAGNOSTICS_GO_ID = game_object_id
                return _DIAGNOSTICS_GO_ID
    except Exception:
        pass

    # 3. Tertiary fallback: search gameobjects by GameObject name (includes DontDestroyOnLoad)
    try:
        raw = _call_http("scene/search_gameobjects", params={"name": "LoaderDiagnostics", "includeInactive": "true", "limit": "50"})
        data = json.loads(raw)
        for item in _scene_search_rows(data):
            game_object_id = _scene_search_object_id(item)
            if game_object_id is not None:
                _DIAGNOSTICS_GO_ID = game_object_id
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


def _parse_renderer_probe_result(raw: Any, operation: str) -> dict[str, Any]:
    try:
        result = json.loads(raw) if isinstance(raw, str) else raw
    except (TypeError, ValueError) as exc:
        return {"success": False, "error": f"{operation} returned invalid JSON: {exc}"}
    if not isinstance(result, dict):
        return {"success": False, "error": f"{operation} returned a non-object payload"}
    return result


def _matching_renderer_probe_frames(
    probe: dict[str, Any],
    *,
    active_clip_contains: str,
    is_moving: bool | None,
    require_deformation_delta: bool,
    require_retarget_bound: bool,
    require_visible_to_camera: bool,
) -> list[dict[str, Any]]:
    frames: dict[int, dict[str, Any]] = {}
    renderers = probe.get("renderers")
    if not isinstance(renderers, list):
        return []

    clip_filter = active_clip_contains.casefold()
    for renderer in renderers:
        if not isinstance(renderer, dict):
            continue
        renderer_name = renderer.get("name")
        if not isinstance(renderer_name, str):
            renderer_name = ""
        samples = renderer.get("samples")
        if not isinstance(samples, list):
            continue

        for sample in samples:
            if not isinstance(sample, dict):
                continue
            if sample.get("cameraRenderCallbackObserved") is not True or sample.get("cameraTargetTextureAssigned") is not False:
                continue
            if sample.get("rendererEnabled") is not True:
                continue
            clip = sample.get("activeClip")
            if not isinstance(clip, str):
                clip = ""
            if clip_filter and clip_filter not in clip.casefold():
                continue
            if is_moving is not None and sample.get("isMoving") is not is_moving:
                continue

            delta = sample.get("maximumLocalVertexDelta")
            has_delta = (
                sample.get("deformationDeltaAvailable") is True
                and isinstance(delta, (int, float))
                and not isinstance(delta, bool)
                and math.isfinite(float(delta))
                and float(delta) > 1e-6
            )
            if require_deformation_delta and not has_delta:
                continue

            mapping_count = sample.get("boundMappingCount")
            retarget_bound = (
                sample.get("retargetPresent") is True
                and sample.get("retargetBound") is True
                and sample.get("retargetSuspended") is False
                and isinstance(mapping_count, int)
                and not isinstance(mapping_count, bool)
                and mapping_count > 0
            )
            if require_retarget_bound and not retarget_bound:
                continue

            visible = (
                sample.get("rendererIsVisible") is True
                and sample.get("boundsIntersectsCameraFrustum") is True
                and sample.get("layerIncludedByCameraCullingMask") is True
            )
            if require_visible_to_camera and not visible:
                continue

            frame = sample.get("frame")
            if not isinstance(frame, int) or isinstance(frame, bool):
                continue
            row = frames.setdefault(frame, {
                "frame": frame,
                "rendererNames": [],
                "activeClips": [],
                "maximumLocalVertexDelta": None,
                "moving": sample.get("isMoving") is True,
                "retargetBound": retarget_bound,
                "visibleToCamera": visible,
            })
            if renderer_name and renderer_name not in row["rendererNames"]:
                row["rendererNames"].append(renderer_name)
            if clip and clip not in row["activeClips"]:
                row["activeClips"].append(clip)
            if has_delta:
                prior_delta = row["maximumLocalVertexDelta"]
                if prior_delta is None or float(delta) > prior_delta:
                    row["maximumLocalVertexDelta"] = float(delta)

    return [frames[frame] for frame in sorted(frames)]


def _validate_sequence_references(value: Any, step_index: int) -> None:
    if isinstance(value, dict):
        if "$ref" in value:
            ref = value["$ref"]
            if set(value) != {"$ref"} or not isinstance(ref, dict) or set(ref) != {"step", "path"}:
                raise ValueError("$ref must contain exactly step and path, without sibling properties")
            step, path = ref["step"], ref["path"]
            if isinstance(step, bool) or not isinstance(step, int) or not 0 <= step < step_index:
                raise ValueError("$ref step must identify an earlier step")
            if not isinstance(path, str) or not path.startswith("/") or re.search(r"~(?![01])", path):
                raise ValueError("$ref path must be a valid non-empty JSON Pointer")
        else:
            for child in value.values():
                _validate_sequence_references(child, step_index)
    elif isinstance(value, list):
        for child in value:
            _validate_sequence_references(child, step_index)


def _resolve_sequence_references(value: Any, step_index: int, prior_results: list[dict[str, Any]], argument_path: str = "$") -> Any:
    if isinstance(value, dict):
        if "$ref" in value:
            if set(value) != {"$ref"}:
                raise ValueError(f"{argument_path}: a $ref object cannot contain sibling properties")
            reference = value.get("$ref")
            if not isinstance(reference, dict) or set(reference) != {"step", "path"}:
                raise ValueError(f"{argument_path}: $ref must contain exactly 'step' and 'path'")
            source_step = reference.get("step")
            pointer = reference.get("path")
            if isinstance(source_step, bool) or not isinstance(source_step, int):
                raise ValueError(f"{argument_path}: $ref step must be a zero-based integer")
            if source_step < 0 or source_step >= step_index:
                raise ValueError(f"{argument_path}: $ref must point to an earlier step in this sequence")
            if not isinstance(pointer, str) or not pointer.startswith("/"):
                raise ValueError(f"{argument_path}: $ref path must be a non-empty JSON Pointer into the prior step result")
            source = prior_results[source_step]
            if source.get("success") is not True:
                raise ValueError(f"{argument_path}: step {source_step} did not succeed; its result cannot be reused")

            resolved = source.get("result")
            for token in pointer[1:].split("/"):
                if re.search(r"~(?![01])", token):
                    raise ValueError(f"{argument_path}: invalid JSON Pointer escape in $ref path")
                token = token.replace("~1", "/").replace("~0", "~")
                if isinstance(resolved, dict):
                    if token not in resolved:
                        raise ValueError(f"{argument_path}: $ref path segment {token!r} was not present")
                    resolved = resolved[token]
                elif isinstance(resolved, list):
                    if not token.isdigit() or (len(token) > 1 and token.startswith("0")):
                        raise ValueError(f"{argument_path}: JSON Pointer array segment {token!r} is not a canonical index")
                    array_index = int(token)
                    if array_index >= len(resolved):
                        raise ValueError(f"{argument_path}: JSON Pointer array index {array_index} is out of range")
                    resolved = resolved[array_index]
                else:
                    raise ValueError(f"{argument_path}: $ref path traverses a non-object result")
            return copy.deepcopy(resolved)

        return {
            key: _resolve_sequence_references(child, step_index, prior_results, f"{argument_path}.{key}")
            for key, child in value.items()
        }
    if isinstance(value, list):
        return [
            _resolve_sequence_references(child, step_index, prior_results, f"{argument_path}[{index}]")
            for index, child in enumerate(value)
        ]
    return value


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
        # If cached go_id became stale, retry once after discovery. Never
        # replay a request whose transport timed out: its runtime effect may
        # still have completed and must be checked through a postcondition.
        try:
            response_metadata = json.loads(raw)
        except (TypeError, ValueError):
            response_metadata = None
        transport_expired = isinstance(response_metadata, dict) and bool(response_metadata.get("completionStatus"))
        error_text = response_metadata.get("error", "") if isinstance(response_metadata, dict) else ""
        stale_id = isinstance(error_text, str) and "missing or invalid 'id'" in error_text.lower()
        if not transport_expired and stale_id:
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


WAIT_STATE_TOOLS = frozenset({
    "diagnostics_get_runtime_control_surface", "diagnostics_get_ui_accessibility_census",
    "diagnostics_get_intermission_native_panel_state", "diagnostics_get_intermission_census",
    "diagnostics_get_navigator_state", "diagnostics_get_game_time_scale",
    "diagnostics_get_intermission_reward_state", "diagnostics_get_battlefield_unit_state",
})


def _validate_state_wait(arguments, partial=False):
    try:
        json.dumps(arguments, allow_nan=False)
    except (TypeError, ValueError):
        return {"error": "Wait arguments must be finite JSON values"}
    target = arguments.get("tool")
    if not isinstance(target, str) or target not in WAIT_STATE_TOOLS:
        return {"error": "State waits require an explicitly allowed read-only tool"}
    invalid = _validate_tool_arguments(target, arguments.get("arguments", {}), partial=partial)
    if invalid:
        return invalid
    for check in arguments.get("expect", []):
        if not isinstance(check, dict) or not isinstance(check.get("path"), str) or re.search(r"~(?![01])", check["path"]):
            return {"error": "expect paths must use valid JSON Pointer escaping"}
        if len(check.keys() & {"equals", "atMost", "atLeast"}) != 1:
            return {"error": "Each wait check requires exactly one of equals, atMost or atLeast"}
    return None


def _state_failure(value):
    return isinstance(value, dict) and (
        "error" in value or value.get("success") is False
        or value.get("completionStatus") == "unknown"
    )


def _failed_state_subtree(value):
    if _state_failure(value):
        return True
    if isinstance(value, dict):
        return any(_failed_state_subtree(child) for child in value.values())
    if isinstance(value, list):
        return any(_failed_state_subtree(child) for child in value)
    return False


def _state_checks(observation, expected):
    checks = []
    for check in expected:
        value = observation
        missing = False
        for part in check["path"].split("/")[1:]:
            if _state_failure(value):
                raise ValueError("queried section failed: " + check["path"])
            key = part.replace("~1", "/").replace("~0", "~")
            if isinstance(value, dict) and key in value:
                value = value[key]
            elif isinstance(value, list) and re.fullmatch(r"0|[1-9][0-9]*", key) and int(key) < len(value):
                value = value[int(key)]
            else:
                missing = True
                break
        if not missing and _failed_state_subtree(value):
            raise ValueError("queried section failed: " + check["path"])
        operation = next(key for key in ("equals", "atMost", "atLeast") if key in check)
        threshold = check[operation]
        matched = False
        if not missing:
            if operation == "equals":
                matched = json.dumps(value, sort_keys=True, allow_nan=False) == json.dumps(threshold, sort_keys=True, allow_nan=False)
            elif type(value) in (int, float):
                matched = value <= threshold if operation == "atMost" else value >= threshold
        row = {"path": check["path"], "matched": matched, "expected": threshold, "missing": missing}
        if operation != "equals":
            row["comparison"] = operation
        if not missing:
            row["actual"] = value
        checks.append(row)
    return checks


def _wait_for_state(arguments):
    invalid = _validate_state_wait(arguments)
    if invalid:
        return _tool_response(json.dumps(invalid), is_error=True)
    started = time.monotonic()
    own_deadline = started + arguments.get("timeoutSeconds", 10)
    outer_deadline = _AUTOMATION_DEADLINE.get()
    deadline = min(own_deadline, outer_deadline) if outer_deadline is not None else own_deadline
    attempts, observation, checks = 0, None, []
    consecutive_matches = 0
    required_matches = arguments.get("stableSamples", 1)
    reason, error = "timeout", None
    while time.monotonic() < deadline:
        token = _AUTOMATION_DEADLINE.set(deadline)
        try:
            response = execute_tool(arguments["tool"], arguments.get("arguments", {}))
        finally:
            _AUTOMATION_DEADLINE.reset(token)
        attempts += 1
        observation, checks = None, []
        try:
            observation = json.loads("\n".join(block["text"] for block in response.get("content", []) if block.get("type") == "text"),
                                     parse_constant=lambda value: (_ for _ in ()).throw(ValueError("non-finite JSON")))
            if not isinstance(observation, dict):
                raise ValueError("state query must return a JSON object")
            if response.get("isError") or _state_failure(observation):
                reason, error = "query_failed", "Read-only query failed; inspect lastObservation"
                break
            checks = _state_checks(observation, arguments["expect"])
        except (ValueError, TypeError, KeyError) as exc:
            reason, error = "invalid_observation", str(exc)
            break
        if time.monotonic() >= deadline:
            break
        consecutive_matches = consecutive_matches + 1 if all(check["matched"] for check in checks) else 0
        if consecutive_matches >= required_matches:
            reason = "matched"
            break
        time.sleep(min(arguments.get("pollIntervalMs", 250) / 1000, max(0, deadline - time.monotonic())))
    if reason == "timeout" and outer_deadline is not None and outer_deadline <= own_deadline:
        reason = "sequence_budget_exhausted"
    payload = {"success": reason == "matched", "reason": reason, "tool": arguments["tool"],
               "attempts": attempts, "stableSamples": required_matches, "consecutiveMatches": consecutive_matches,
               "elapsedSeconds": round(time.monotonic() - started, 3),
               "checks": checks, "lastObservation": observation}
    if error:
        payload["error"] = error
    return _tool_response(json.dumps(payload), is_error=reason != "matched")


def execute_tool(name: str, arguments: dict[str, Any]) -> dict[str, Any]:
    try:
        invalid = _validate_tool_arguments(name, arguments)
        if invalid:
            return _tool_response(json.dumps(invalid), is_error=True)
        guarded_intermission = {
            "diagnostics_order_choose_intermission_reward": "diagnostics_get_intermission_reward_state",
            "diagnostics_order_buy_intermission_shop_offer": "diagnostics_get_intermission_native_panel_state",
        }
        if name in guarded_intermission and ("preconditions" in arguments or arguments.get("dryRun", False)):
            guards = arguments.get("preconditions", {})
            if any(not path.startswith("/") or re.search(r"~(?![01])", path) for path in guards):
                return _tool_response(json.dumps({"error": "preconditions keys must be JSON Pointers"}), is_error=True)
            state_response = execute_tool(guarded_intermission[name], {})
            try:
                state = json.loads("\n".join(block["text"] for block in state_response.get("content", []) if block.get("type") == "text"),
                                   parse_constant=lambda value: (_ for _ in ()).throw(ValueError("non-finite JSON")))
                if state_response.get("isError") or not isinstance(state, dict) or _state_failure(state):
                    raise ValueError("Intermission preflight query failed")
                checks = _state_checks(state, [{"path": path, "equals": value} for path, value in guards.items()])
            except (ValueError, TypeError, KeyError) as exc:
                return _tool_response(json.dumps({"error": str(exc), "nativeCallInvoked": False}), is_error=True)
            passed = all(check["matched"] for check in checks)
            if not passed or arguments.get("dryRun", False):
                return _tool_response(json.dumps({"success": passed, "dryRun": arguments.get("dryRun", False),
                    "nativeCallInvoked": False, "checks": checks, "observation": state,
                    "actionValidationRequired": True}), is_error=not passed)
        # ---- Discovery (lazy, lightweight) ----
        if name == "automation_wait_for_state":
            return _wait_for_state(arguments)
        if name == "diagnostics_ui_set_value":
            path = arguments.get("hierarchyPath")
            object_id = arguments.get("gameObjectId")
            kind = arguments.get("kind")
            value = arguments.get("value")
            submit = arguments.get("submit", False)
            valid = ((kind == "slider" and isinstance(value, (int, float)) and not isinstance(value, bool))
                     or (kind in ("toggle", "switch") and isinstance(value, bool))
                     or (kind == "dropdown" and isinstance(value, int) and not isinstance(value, bool))
                     or (kind == "text" and isinstance(value, str) and len(value) <= 4096))
            if (not isinstance(path, str) or "::" not in path or not isinstance(object_id, int)
                    or isinstance(object_id, bool) or not isinstance(submit, bool) or not valid
                    or (submit and kind != "text")):
                return _tool_response(json.dumps({"error": "Invalid UI target or typed value"}), is_error=True)
            try:
                value_json = "base64:" + base64.b64encode(json.dumps(value, allow_nan=False, ensure_ascii=False).encode("utf-8")).decode("ascii")
            except ValueError:
                return _tool_response(json.dumps({"error": "Value must be finite"}), is_error=True)
            return _tool_response(_call_diagnostics_method("SetUiValueJson", [path, object_id, kind, value_json, submit]))
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
        elif name == "automation_run_sequence":
            steps = arguments.get("steps")
            if not isinstance(steps, list) or not 1 <= len(steps) <= 16:
                return _tool_response(json.dumps({"error": "steps must contain 1 to 16 operations"}), is_error=True)
            stop_on_failure = arguments.get("stopOnFailure", True)
            if not isinstance(stop_on_failure, bool):
                return _tool_response(json.dumps({"error": "stopOnFailure must be a boolean"}), is_error=True)
            try:
                max_duration = int(arguments.get("maxDurationSeconds", 60))
            except (TypeError, ValueError):
                return _tool_response(json.dumps({"error": "maxDurationSeconds must be an integer"}), is_error=True)
            if not 1 <= max_duration <= 120:
                return _tool_response(json.dumps({"error": "maxDurationSeconds must be in [1, 120]"}), is_error=True)

            known_tools = {entry["name"] for entry in TOOLS}
            forbidden = {
                "automation_run_sequence",
                "diagnostics_run_synthetic_gameplay_scenario",
                "diagnostics_jump_to_level",
                "diagnostics_load_next_level",
            }
            # Validate the complete static sequence before issuing any bridge calls.
            for index, step in enumerate(steps):
                if not isinstance(step, dict):
                    return _tool_response(json.dumps({"error": f"steps[{index}] must be an object"}), is_error=True)
                step_tool = str(step.get("tool", "")).strip()
                if step_tool not in known_tools or (not step_tool.startswith("diagnostics_") and step_tool != "automation_wait_for_state") or step_tool in forbidden:
                    return _tool_response(json.dumps({"error": f"steps[{index}].tool is not an allowed diagnostics tool", "tool": step_tool}), is_error=True)
                if not isinstance(step.get("arguments", {}), dict):
                    return _tool_response(json.dumps({"error": f"steps[{index}].arguments must be an object"}), is_error=True)
                try:
                    _validate_sequence_references(step.get("arguments", {}), index)
                    invalid = _validate_tool_arguments(step_tool, step.get("arguments", {}), partial=True)
                    if invalid:
                        return _tool_response(json.dumps({**invalid, "step": index}), is_error=True)
                    if step_tool == "automation_wait_for_state":
                        invalid = _validate_state_wait(step.get("arguments", {}), partial=True)
                        if invalid:
                            return _tool_response(json.dumps({**invalid, "step": index}), is_error=True)
                except ValueError as exc:
                    return _tool_response(json.dumps({"error": f"steps[{index}].arguments: {exc}"}), is_error=True)
                checks = step.get("expect", [])
                if not isinstance(checks, list) or len(checks) > 8:
                    return _tool_response(json.dumps({"error": f"steps[{index}].expect must be an array of at most 8 checks"}), is_error=True)
                for check in checks:
                    if (not isinstance(check, dict) or set(check) != {"path", "equals"}
                            or not isinstance(check["path"], str) or not check["path"].startswith("/")
                            or re.search(r"~(?![01])", check["path"])):
                        return _tool_response(json.dumps({"error": f"steps[{index}].expect requires path (JSON Pointer) and equals"}), is_error=True)
                try:
                    delay = int(step.get("delayAfterMs", 0))
                except (TypeError, ValueError):
                    return _tool_response(json.dumps({"error": f"steps[{index}].delayAfterMs must be an integer"}), is_error=True)
                if not 0 <= delay <= 2000:
                    return _tool_response(json.dumps({"error": f"steps[{index}].delayAfterMs must be in [0, 2000]"}), is_error=True)
            started = time.monotonic()
            deadline = started + max_duration
            sequence_results: list[dict[str, Any]] = []
            failed = False
            for index, step in enumerate(steps):
                step_tool = str(step.get("tool", "")).strip()
                step_arguments = step.get("arguments", {})
                delay_after_ms = int(step.get("delayAfterMs", 0))
                if time.monotonic() - started >= max_duration:
                    failed = True
                    sequence_results.append({"index": index, "tool": step_tool, "success": False, "error": "sequence time budget exhausted"})
                    break

                step_started = time.monotonic()
                try:
                    resolved_arguments = _resolve_sequence_references(step_arguments, index, sequence_results)
                except ValueError as exc:
                    failed = True
                    sequence_results.append({
                        "index": index,
                        "tool": step_tool,
                        "success": False,
                        "budgetExceeded": False,
                        "durationMs": round((time.monotonic() - step_started) * 1000, 1),
                        "error": f"sequence argument reference failed: {exc}",
                    })
                    if stop_on_failure:
                        break
                    continue
                deadline_token = _AUTOMATION_DEADLINE.set(deadline)
                try:
                    response = execute_tool(step_tool, resolved_arguments)
                finally:
                    _AUTOMATION_DEADLINE.reset(deadline_token)
                text_parts = [
                    str(block.get("text", ""))
                    for block in response.get("content", [])
                    if isinstance(block, dict) and block.get("type") == "text"
                ]
                result_text = "\n".join(text_parts)
                step_failed = bool(response.get("isError", False))
                try:
                    parsed_step = json.loads(result_text) if result_text else None
                    if isinstance(parsed_step, dict) and (parsed_step.get("success") is False or "error" in parsed_step):
                        step_failed = True
                except (TypeError, ValueError):
                    parsed_step = result_text
                if isinstance(parsed_step, (dict, list)):
                    step_result: Any = parsed_step
                else:
                    step_result = result_text
                budget_exceeded = time.monotonic() > deadline
                if budget_exceeded:
                    step_failed = True
                uncertain = isinstance(parsed_step, dict) and (
                    parsed_step.get("completionStatus") == "unknown"
                    or (parsed_step.get("nativeCallInvoked") is True and parsed_step.get("effectObserved") is False)
                )
                if uncertain:
                    step_failed = True
                check_results = []
                if not step_failed:
                    for check in step.get("expect", []):
                        try:
                            actual = _resolve_sequence_references(
                                {"$ref": {"step": 0, "path": check["path"]}}, 1,
                                [{"success": True, "result": step_result}],
                            )
                            matched = json.dumps(actual, sort_keys=True, separators=(",", ":")) == json.dumps(check["equals"], sort_keys=True, separators=(",", ":"))
                            check_results.append({"path": check["path"], "matched": matched, "actual": actual, "expected": check["equals"]})
                        except ValueError as exc:
                            matched = False
                            check_results.append({"path": check["path"], "matched": False, "error": str(exc)})
                        if not matched:
                            step_failed = True
                sequence_results.append({
                    "index": index,
                    "tool": step_tool,
                    "success": not step_failed,
                    "budgetExceeded": budget_exceeded,
                    "durationMs": round((time.monotonic() - step_started) * 1000, 1),
                    "result": step_result,
                    "checks": check_results,
                    "postconditionInspectionRequired": uncertain,
                })
                if step_failed:
                    failed = True
                    if (stop_on_failure or step.get("expect") or uncertain or budget_exceeded or step_tool == "automation_wait_for_state"
                            or (step_tool in guarded_intermission and ("preconditions" in resolved_arguments or resolved_arguments.get("dryRun", False)))):
                        break
                if delay_after_ms:
                    remaining = max_duration - (time.monotonic() - started)
                    if remaining <= 0:
                        failed = True
                        break
                    time.sleep(min(delay_after_ms / 1000.0, remaining))

            sequence_success = not failed and len(sequence_results) == len(steps)
            stop_reason = None
            if not sequence_success:
                row = next((row for row in sequence_results if not row["success"]), sequence_results[-1] if sequence_results else {})
                code = ("sequence_budget_exhausted" if row.get("budgetExceeded") or time.monotonic() >= deadline else
                        "uncertain_effect" if row.get("postconditionInspectionRequired") else
                        "wait_failed" if row.get("tool") == "automation_wait_for_state" else
                        "expectation_failed" if any(not check["matched"] for check in row.get("checks", [])) else "step_failed")
                stop_reason = {"code": code, "step": row.get("index")}
            return _tool_response(json.dumps({
                "success": sequence_success,
                "stopReason": stop_reason,
                "completedSteps": len(sequence_results),
                "requestedSteps": len(steps),
                "elapsedSeconds": round(time.monotonic() - started, 3),
                "results": sequence_results,
                "source": "ordered BepInEx MCP tool calls; no computer-use interaction or screenshot",
            }, indent=2), is_error=not sequence_success)
        # ---- Core Bridge & System ----
        if name == "system_get_game_process_state":
            if os.name != "nt":
                return _tool_response(json.dumps({"supported": False, "error": "process lookup is currently implemented for Windows hosts"}), is_error=True)
            try:
                proc = subprocess.run(
                    ["tasklist.exe", "/FI", f"IMAGENAME eq {GAME_PROCESS_NAME}", "/FO", "CSV", "/NH"],
                    capture_output=True,
                    text=True,
                    encoding="utf-8",
                    errors="replace",
                    timeout=10,
                    check=False,
                )
                if proc.returncode != 0:
                    return _tool_response(json.dumps({"supported": True, "error": proc.stderr.strip() or f"tasklist exited {proc.returncode}"}), is_error=True)
                processes: list[dict[str, Any]] = []
                for row in csv.reader(proc.stdout.splitlines()):
                    if len(row) < 2 or row[0].strip().casefold() != GAME_PROCESS_NAME.casefold():
                        continue
                    try:
                        process_id = int(row[1].replace(",", "").strip())
                    except ValueError:
                        continue
                    processes.append({"processId": process_id, "imageName": row[0].strip()})
                return _tool_response(json.dumps({
                    "supported": True,
                    "running": bool(processes),
                    "processCount": len(processes),
                    "processes": processes,
                    "source": "Windows tasklist; no window focus or screenshot",
                }, indent=2))
            except (OSError, subprocess.SubprocessError) as exc:
                return _tool_response(json.dumps({"supported": True, "error": str(exc)}), is_error=True)

        elif name == "system_launch_game":
            if os.name != "nt":
                return _tool_response(json.dumps({"success": False, "error": "Steam URI launch is currently implemented for Windows hosts"}), is_error=True)
            process_state = execute_tool("system_get_game_process_state", {})
            process_text = "\n".join(
                str(block.get("text", ""))
                for block in process_state.get("content", [])
                if isinstance(block, dict) and block.get("type") == "text"
            )
            try:
                current_processes = json.loads(process_text)
            except (TypeError, ValueError):
                return _tool_response(json.dumps({"success": False, "error": "could not determine whether Rogue Command is already running", "processState": process_text}), is_error=True)
            if not current_processes.get("supported") or current_processes.get("error"):
                return _tool_response(json.dumps({"success": False, "error": "process-state lookup failed; refusing to launch without duplicate-process check", "processState": current_processes}), is_error=True)
            if current_processes.get("running"):
                return _tool_response(json.dumps({"success": False, "alreadyRunning": True, "processes": current_processes.get("processes", []), "message": "inspect the existing instance instead of launching another"}))
            try:
                startfile = getattr(os, "startfile", None)
                if startfile is None:
                    raise OSError("Windows ShellExecute is unavailable")
                steam_uri = f"steam://run/{GAME_STEAM_APP_ID}"
                startfile(steam_uri)
                return _tool_response(json.dumps({
                    "success": True,
                    "launchRequested": True,
                    "steamAppId": GAME_STEAM_APP_ID,
                    "uri": steam_uri,
                    "profileAction": "none; existing selector/profile state is preserved",
                    "nextTool": "system_wait_for_game_bridge",
                }, indent=2))
            except OSError as exc:
                return _tool_response(json.dumps({"success": False, "error": str(exc), "steamAppId": GAME_STEAM_APP_ID}), is_error=True)

        elif name == "system_wait_for_game_bridge":
            try:
                timeout_seconds = float(arguments.get("timeoutSeconds", 30))
                poll_interval_ms = int(arguments.get("pollIntervalMs", 500))
            except (TypeError, ValueError):
                return _tool_response(json.dumps({"error": "timeoutSeconds must be numeric and pollIntervalMs an integer"}), is_error=True)
            if not 1 <= timeout_seconds <= 60:
                return _tool_response(json.dumps({"error": "timeoutSeconds must be in [1, 60]"}), is_error=True)
            if not 250 <= poll_interval_ms <= 2000:
                return _tool_response(json.dumps({"error": "pollIntervalMs must be in [250, 2000]"}), is_error=True)
            started = time.monotonic()
            deadline = started + timeout_seconds
            last_error = "bridge not ready"
            while True:
                try:
                    remaining = deadline - time.monotonic()
                    if remaining <= 0:
                        return _tool_response(json.dumps({"success": False, "waitedSeconds": round(time.monotonic() - started, 3), "error": last_error}), is_error=False)
                    ping_raw = _call_http("system/ping", timeout=min(4.0, remaining / 2))
                    remaining = deadline - time.monotonic()
                    if remaining <= 0:
                        return _tool_response(json.dumps({"success": False, "waitedSeconds": round(time.monotonic() - started, 3), "error": last_error}), is_error=False)
                    scene_raw = _call_http("scene/list_root_gameobjects", timeout=min(4.0, remaining))
                    ping_data = json.loads(ping_raw) if isinstance(ping_raw, str) else ping_raw
                    scene_data = json.loads(scene_raw) if isinstance(scene_raw, str) else scene_raw
                    ping_ok = isinstance(ping_data, dict) and ping_data.get("status") == "ok"
                    scene_ok = isinstance(scene_data, (list, dict)) and not (isinstance(scene_data, dict) and scene_data.get("error"))
                    if ping_ok and scene_ok:
                        return _tool_response(json.dumps({
                            "success": True,
                            "waitedSeconds": round(time.monotonic() - started, 3),
                            "ping": ping_data,
                            "sceneQueryReady": True,
                            "sceneRootCount": len(scene_data) if isinstance(scene_data, list) else None,
                            "source": "live BepInEx ping plus Unity scene query; no focus or screenshot",
                        }, indent=2))
                    last_error = "ping or Unity scene query returned an error payload"
                except Exception as exc:
                    last_error = str(exc)
                if time.monotonic() >= deadline:
                    return _tool_response(json.dumps({"success": False, "waitedSeconds": round(time.monotonic() - started, 3), "error": last_error}), is_error=False)
                time.sleep(min(poll_interval_ms / 1000.0, max(0.0, deadline - time.monotonic())))

        elif name == "diagnostics_request_graceful_exit":
            confirm_exit = arguments.get("confirmExit")
            if confirm_exit is not True:
                return _tool_response(json.dumps({"error": "confirmExit must be true; normal shutdown may persist save state"}), is_error=True)
            res = _call_diagnostics_method("RequestGracefulExitJson", [True])
            return _tool_response(res)

        elif name == "diagnostics_get_steam_cloud_state":
            res = _call_diagnostics_method("GetSteamCloudStateJson", [])
            return _tool_response(res)

        elif name == "diagnostics_set_steam_cloud_enabled":
            enabled = arguments.get("enabled")
            explicit_authorization = arguments.get("explicitUserAuthorization")
            if not isinstance(enabled, bool):
                return _tool_response(json.dumps({"error": "enabled must be a boolean"}), is_error=True)
            if explicit_authorization is not True:
                return _tool_response(json.dumps({"error": "explicitUserAuthorization must be true only after explicit user authorization"}), is_error=True)
            res = _call_diagnostics_method("SetSteamCloudAppEnabledJson", [enabled, True])
            return _tool_response(res)

        elif name in ("system_ping", "ping_unity_bridge"):
            res = _call_http("system/ping")
            return _tool_response(res)

        elif name in ("system_capabilities", "get_runtime_capabilities"):
            res = _call_http("system/capabilities")
            try:
                capabilities = json.loads(res)
                if isinstance(capabilities, dict):
                    capabilities["automation"] = {
                        "serverVersion": SERVER_VERSION,
                        "screenshotCapture": False,
                        "computerUseRequired": False,
                        "runInBackgroundConfigured": True,
                        "features": [
                            "uiAccessibilityCensus",
                            "runtimeControlSurface",
                            "uiTextClickWithAmbiguityGuard",
                            "waitForUiText",
                            "orderedDiagnosticsSequences",
                            "steamLaunchWithExistingProfileState",
                            "gameProcessState",
                            "gracefulPlayerExit",
                            "steamCloudStateAndPerAppToggle",
                            "activeStageAiProfileAssociation",
                        ],
                    }
                    res = json.dumps(capabilities, indent=2)
            except (TypeError, ValueError):
                pass
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

        elif name == "diagnostics_get_active_stage_ai_association":
            res = _call_diagnostics_method("GetActiveStageAiAssociationJson")
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

        elif name == "diagnostics_get_battlefield_unit_state":
            goal = arguments.get("goal")
            res = _call_diagnostics_method("GetBattlefieldUnitStateJson", [
                arguments["entityKeyId"], goal is not None,
                float(goal["x"]) if goal is not None else 0.0,
                float(goal["z"]) if goal is not None else 0.0,
                float(goal.get("tolerance", 1.0)) if goal is not None else 1.0,
            ])
            return _tool_response(res)

        elif name == "diagnostics_order_unit_move":
            e_key = str(arguments.get("entityKeyId", ""))
            x = float(arguments.get("destinationX", 0.0))
            z = float(arguments.get("destinationZ", 0.0))
            res = _call_diagnostics_method("OrderUnitMoveJson", [e_key, x, z])
            return _tool_response(res)

        elif name == "diagnostics_order_place_building":
            x = float(arguments.get("worldX", 0.0))
            z = float(arguments.get("worldZ", 0.0))
            commit = arguments.get("commit", False)
            if not isinstance(commit, bool):
                return _tool_response(json.dumps({"success": False, "error": "commit must be a boolean"}), True)
            res = _call_diagnostics_method("OrderPlaceBuildingJson", [x, z, commit])
            return _tool_response(res)

        elif name == "diagnostics_inspect_active_building_placement":
            res = _call_diagnostics_method("InspectActiveBuildingPlacementJson")
            return _tool_response(res)

        elif name == "diagnostics_find_building_placement":
            try:
                raw_x = arguments.get("originX")
                raw_z = arguments.get("originZ")
                if (
                    isinstance(raw_x, bool) or isinstance(raw_z, bool)
                    or not isinstance(raw_x, (int, float))
                    or not isinstance(raw_z, (int, float))
                ):
                    raise ValueError("originX and originZ must be finite numbers")
                origin_x = float(raw_x)
                origin_z = float(raw_z)
                raw_radius = arguments.get("radiusCells", 8)
                if isinstance(raw_radius, bool) or not isinstance(raw_radius, (int, float)):
                    raise ValueError("radiusCells must be an integer from 0 to 12")
                radius_number = float(raw_radius)
                if not math.isfinite(origin_x) or not math.isfinite(origin_z):
                    raise ValueError("originX and originZ must be finite numbers")
                if not math.isfinite(radius_number) or not radius_number.is_integer():
                    raise ValueError("radiusCells must be an integer from 0 to 12")
                radius_cells = int(radius_number)
                if radius_cells < 0 or radius_cells > 12:
                    raise ValueError("radiusCells must be an integer from 0 to 12")
            except (TypeError, ValueError, OverflowError) as exc:
                return _tool_response(json.dumps({"found": False, "error": str(exc)}), True)

            res = _call_diagnostics_method(
                "FindBuildingPlacementJson", [origin_x, origin_z, radius_cells]
            )
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
            entity_keys_csv = str(arguments.get("entityKeysCsv", ""))
            additive = bool(arguments.get("additive", False))
            res = _call_diagnostics_method("SelectUnitsJson", [entity_keys_csv, additive])
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

        elif name == "diagnostics_start_renderer_frame_probe":
            entity_key_id = str(arguments.get("entityKeyId", "")).strip()
            renderer_filter = str(arguments.get("rendererNameContains", ""))
            sample_clip_contains = arguments.get("sampleClipContains", "")
            sample_movement_state = arguments.get("sampleMovementState", "any")
            if not isinstance(sample_clip_contains, str):
                return _tool_response(json.dumps({"success": False, "error": "sampleClipContains must be a string", "pixelCapture": False}), is_error=True)
            if not isinstance(sample_movement_state, str) or sample_movement_state not in {"any", "moving", "stationary"}:
                return _tool_response(json.dumps({"success": False, "error": "sampleMovementState must be any, moving, or stationary", "pixelCapture": False}), is_error=True)
            try:
                sample_limit = int(arguments.get("sampleLimit", 12))
            except (TypeError, ValueError):
                return _tool_response(json.dumps({"success": False, "error": "sampleLimit must be an integer", "pixelCapture": False}), is_error=True)
            res = _call_diagnostics_method(
                "StartRendererFrameProbeJson",
                [entity_key_id, renderer_filter, sample_limit, sample_clip_contains, sample_movement_state],
            )
            return _tool_response(res)

        elif name == "diagnostics_read_renderer_frame_probe":
            token = str(arguments.get("token", "")).strip()
            res = _call_diagnostics_method("ReadRendererFrameProbeJson", [token])
            return _tool_response(res)

        elif name == "diagnostics_stop_renderer_frame_probe":
            token = str(arguments.get("token", "")).strip()
            res = _call_diagnostics_method("StopRendererFrameProbeJson", [token])
            return _tool_response(res)

        elif name == "diagnostics_wait_for_renderer_frame_evidence":
            entity_key_id = arguments.get("entityKeyId")
            if not isinstance(entity_key_id, str) or not entity_key_id.strip():
                return _tool_response(json.dumps({"success": False, "error": "entityKeyId must be a non-empty string", "pixelCapture": False}), is_error=True)

            try:
                timeout_seconds = arguments.get("timeoutSeconds", 10)
                if isinstance(timeout_seconds, bool) or not isinstance(timeout_seconds, (int, float)):
                    raise ValueError
                timeout_seconds = float(timeout_seconds)
                minimum_matching_frames = arguments.get("minimumMatchingFrames", 1)
                if isinstance(minimum_matching_frames, bool) or not isinstance(minimum_matching_frames, int):
                    raise ValueError
            except (TypeError, ValueError, OverflowError):
                return _tool_response(json.dumps({"success": False, "error": "timeoutSeconds and minimumMatchingFrames must be numeric values of the declared types", "pixelCapture": False}), is_error=True)
            if not math.isfinite(timeout_seconds) or not 1 <= timeout_seconds <= 20:
                return _tool_response(json.dumps({"success": False, "error": "timeoutSeconds must be finite and in [1, 20]", "pixelCapture": False}), is_error=True)
            if not 1 <= minimum_matching_frames <= 12:
                return _tool_response(json.dumps({"success": False, "error": "minimumMatchingFrames must be in [1, 12]", "pixelCapture": False}), is_error=True)

            active_clip_contains = arguments.get("activeClipContains", "")
            if not isinstance(active_clip_contains, str):
                return _tool_response(json.dumps({"success": False, "error": "activeClipContains must be a string", "pixelCapture": False}), is_error=True)

            required_evidence = arguments.get("requiredEvidence", [])
            allowed_evidence = {"moving", "stationary", "deformation", "retargetBound", "visibleToCamera"}
            if not isinstance(required_evidence, list) or any(not isinstance(item, str) or item not in allowed_evidence for item in required_evidence):
                return _tool_response(json.dumps({"success": False, "error": "requiredEvidence must be a list of supported signal names", "pixelCapture": False}), is_error=True)
            if len(required_evidence) > len(allowed_evidence) or len(set(required_evidence)) != len(required_evidence):
                return _tool_response(json.dumps({"success": False, "error": "requiredEvidence must contain unique signal names", "pixelCapture": False}), is_error=True)
            if "moving" in required_evidence and "stationary" in required_evidence:
                return _tool_response(json.dumps({"success": False, "error": "requiredEvidence cannot require both moving and stationary", "pixelCapture": False}), is_error=True)
            is_moving = True if "moving" in required_evidence else False if "stationary" in required_evidence else None
            sample_movement_state = "moving" if is_moving is True else "stationary" if is_moving is False else "any"

            started_at = time.monotonic()
            deadline = started_at + timeout_seconds
            automation_deadline = _AUTOMATION_DEADLINE.get()
            if automation_deadline is not None:
                deadline = min(deadline, automation_deadline)
            if deadline <= started_at:
                return _tool_response(json.dumps({"success": False, "error": "automation request time budget is exhausted", "pixelCapture": False}), is_error=False)

            start_raw = _call_diagnostics_method(
                "StartRendererFrameProbeJson",
                [entity_key_id.strip(), "", 12, active_clip_contains, sample_movement_state],
            )
            start_result = _parse_renderer_probe_result(start_raw, "StartRendererFrameProbeJson")
            if start_result.get("success") is not True:
                return _tool_response(json.dumps({
                    "success": False,
                    "error": start_result.get("error", "renderer frame probe did not start"),
                    "startResult": start_result,
                    "pixelCapture": False,
                }, indent=2), is_error=False)

            probe_token = start_result.get("token")
            if not isinstance(probe_token, str) or not probe_token.strip():
                return _tool_response(json.dumps({
                    "success": False,
                    "error": "renderer frame probe started without returning a cleanup token",
                    "startResult": start_result,
                    "pixelCapture": False,
                }, indent=2), is_error=False)

            last_probe: dict[str, Any] | None = None
            read_count = 0
            read_error: str | None = None
            condition_met = False
            stop_result: dict[str, Any] | None = None
            try:
                while time.monotonic() < deadline:
                    try:
                        read_raw = _call_diagnostics_method("ReadRendererFrameProbeJson", [probe_token])
                        last_probe = _parse_renderer_probe_result(read_raw, "ReadRendererFrameProbeJson")
                        read_count += 1
                    except Exception as exc:
                        read_error = str(exc)
                        break

                    if last_probe.get("success") is not True:
                        read_error = str(last_probe.get("error", "renderer frame probe read failed"))
                        break
                    matching_frames = _matching_renderer_probe_frames(
                        last_probe,
                        active_clip_contains=active_clip_contains,
                        is_moving=is_moving,
                        require_deformation_delta="deformation" in required_evidence,
                        require_retarget_bound="retargetBound" in required_evidence,
                        require_visible_to_camera="visibleToCamera" in required_evidence,
                    )
                    if len(matching_frames) >= minimum_matching_frames:
                        condition_met = True
                        break

                    remaining = deadline - time.monotonic()
                    if remaining <= 0:
                        break
                    time.sleep(min(0.25, remaining))
            finally:
                # Observer cleanup must still run if the enclosing sequence budget expires.
                cleanup_deadline = time.monotonic() + 4.0
                cleanup_token = _AUTOMATION_DEADLINE.set(cleanup_deadline)
                try:
                    stop_raw = _call_diagnostics_method("StopRendererFrameProbeJson", [probe_token])
                    stop_result = _parse_renderer_probe_result(stop_raw, "StopRendererFrameProbeJson")
                except Exception as exc:
                    stop_result = {"success": False, "error": str(exc)}
                finally:
                    _AUTOMATION_DEADLINE.reset(cleanup_token)

            final_probe = stop_result if isinstance(stop_result, dict) and isinstance(stop_result.get("renderers"), list) else last_probe
            if final_probe is None:
                final_probe = {"success": False, "error": "no renderer frame payload was returned"}
            matching_frames = _matching_renderer_probe_frames(
                final_probe,
                active_clip_contains=active_clip_contains,
                is_moving=is_moving,
                require_deformation_delta="deformation" in required_evidence,
                require_retarget_bound="retargetBound" in required_evidence,
                require_visible_to_camera="visibleToCamera" in required_evidence,
            )
            if len(matching_frames) >= minimum_matching_frames:
                condition_met = True

            probe_stopped = isinstance(stop_result, dict) and stop_result.get("success") is True
            cleanup_verification: dict[str, Any] | None = None
            if not probe_stopped:
                try:
                    remaining_raw = _call_http("scene:find_objects_with_component", params={
                        "componentName": "RuntimeRendererFrameProbeBehaviour",
                    })
                    remaining_data = json.loads(remaining_raw) if isinstance(remaining_raw, str) else remaining_raw
                    if isinstance(remaining_data, list):
                        remaining_observers = remaining_data
                        truncated = False
                    elif isinstance(remaining_data, dict):
                        remaining_observers = next((remaining_data.get(key) for key in ("items", "objects", "results") if isinstance(remaining_data.get(key), list)), None)
                        truncated = bool(remaining_data.get("truncated", False))
                    else:
                        remaining_observers = None
                        truncated = False
                    if isinstance(remaining_observers, list) and not truncated:
                        observer_count = len(remaining_observers)
                        probe_stopped = observer_count == 0
                        cleanup_verification = {
                            "success": probe_stopped,
                            "source": "live BepInEx component census",
                            "observerComponent": "RuntimeRendererFrameProbeBehaviour",
                            "remainingObserverCount": observer_count,
                        }
                except Exception as exc:
                    cleanup_verification = {
                        "success": False,
                        "source": "live BepInEx component census",
                        "error": str(exc),
                    }
            timed_out = not condition_met and read_error is None and time.monotonic() >= deadline
            success = condition_met and probe_stopped
            response_payload = {
                "success": success,
                "conditionMet": condition_met,
                "timedOut": timed_out,
                "probeStopped": probe_stopped,
                "probeToken": probe_token,
                "entityKeyId": entity_key_id.strip(),
                "readCount": read_count,
                "minimumMatchingFrames": minimum_matching_frames,
                "requiredEvidence": required_evidence,
                "matchingFrameCount": len(matching_frames),
                "matchingFrames": matching_frames[:24],
                "elapsedSeconds": round(time.monotonic() - started_at, 3),
                "conditions": {
                    "activeClipContains": active_clip_contains,
                    "isMoving": is_moving,
                    "requireDeformationDelta": "deformation" in required_evidence,
                    "requireRetargetBound": "retargetBound" in required_evidence,
                    "requireVisibleToCamera": "visibleToCamera" in required_evidence,
                },
                "probe": final_probe,
                "stopResult": stop_result,
                "probeCleanupVerification": cleanup_verification,
                "pixelCapture": False,
                "computerUseRequired": False,
                "evidenceBoundary": "structured Unity camera, Animator, retarget, visibility, and sampled BakeMesh telemetry; not pixel or rendered-appearance proof",
                "source": "BepInEx MCP; no screenshots, focus interaction, or Computer Use",
            }
            if read_error is not None:
                response_payload["readError"] = read_error
            if isinstance(stop_result, dict) and stop_result.get("success") is not True:
                if probe_stopped:
                    response_payload["stopWarning"] = stop_result.get("error", "stop token was unavailable; observer removal was verified by component census")
                else:
                    response_payload["stopError"] = stop_result.get("error", "renderer frame probe cleanup was not confirmed")
            return _tool_response(json.dumps(response_payload, indent=2), is_error=False)

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

        elif name == "diagnostics_get_runtime_control_surface":
            query = str(arguments.get("query", "") or "")
            include_inactive = arguments.get("includeInactive", False)
            if not isinstance(include_inactive, bool):
                return _tool_response(json.dumps({"error": "includeInactive must be a boolean"}), is_error=True)
            raw_max_results = arguments.get("maxUiResults", 64)
            if isinstance(raw_max_results, bool):
                return _tool_response(json.dumps({"error": "maxUiResults must be an integer"}), is_error=True)
            try:
                max_ui_results = int(raw_max_results)
            except (TypeError, ValueError):
                return _tool_response(json.dumps({"error": "maxUiResults must be an integer"}), is_error=True)
            max_ui_results = max(1, min(100, max_ui_results))
            res = _call_diagnostics_method(
                "GetRuntimeControlSurfaceJson",
                [query, include_inactive, max_ui_results],
            )
            return _tool_response(res)

        elif name == "diagnostics_fast_snapshot":
            preset = str(arguments.get("preset", "match")).lower().strip() or "match"
            presets = {
                "gui": ["GetGuiStateJson", "GetGameTimeScaleJson", "GetCameraCensusJson", "GetRuntimeHooksStateJson", "GetUiAccessibilityCensusJson"],
                "match": ["GetSystemHealthJson", "GetMatchSummaryJson", "GetBattlefieldCensusJson", "GetRunProgressionCensusJson", "GetActiveStageAiAssociationJson", "GetGameTimeScaleJson"],
                "combat": ["GetBattlefieldCensusJson", "GetPlayerRosterJson", "GetAiRosterJson", "GetCombatStatusCensusJson", "GetUnitThreatAssessmentJson"],
                "health": ["GetSystemHealthJson", "GetBaseIntegrityCensusJson", "GetProductionCensusJson", "GetAiDirectorCensusJson"],
                "intermission": ["GetRunProgressionCensusJson", "GetIntermissionCensusJson", "GetIntermissionGraphJson", "GetIntermissionRewardStateJson", "GetIntermissionNativePanelStateJson", "GetNavigatorStateJson", "GetUiAccessibilityCensusJson"],
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
            results["_meta"] = {"preset": preset, "count": len(getters), "consistency": "bestEffort"}
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

        elif name == "diagnostics_get_intermission_graph":
            pref = str(arguments.get("preferredRewardType", "") or "")
            max_routes = int(arguments.get("maxRoutes", 8))
            max_nodes = int(arguments.get("maxNodes", 64))
            res = _call_diagnostics_method("GetIntermissionGraphJson", [pref, max_routes, max_nodes])
            return _tool_response(res)

        elif name == "diagnostics_get_intermission_reward_state":
            max_cards = int(arguments.get("maxCards", 32))
            res = _call_diagnostics_method("GetIntermissionRewardStateJson", [max_cards])
            return _tool_response(res)

        elif name == "diagnostics_get_intermission_native_panel_state":
            raw_max_rows = arguments.get("maxRows", 32)
            if isinstance(raw_max_rows, bool):
                return _tool_response(json.dumps({"error": "maxRows must be an integer"}), is_error=True)
            try:
                max_rows = int(raw_max_rows)
            except (TypeError, ValueError):
                return _tool_response(json.dumps({"error": "maxRows must be an integer"}), is_error=True)
            max_rows = max(1, min(128, max_rows))
            res = _call_diagnostics_method("GetIntermissionNativePanelStateJson", [max_rows])
            return _tool_response(res)

        elif name == "diagnostics_order_buy_intermission_shop_offer":
            offer_id = arguments.get("offerId", "")
            if not isinstance(offer_id, str) or not offer_id.strip():
                return _tool_response(json.dumps({"error": "offerId is required"}), is_error=True)
            res = _call_diagnostics_method("OrderBuyIntermissionShopOfferJson", [offer_id])
            return _tool_response(res)

        elif name == "diagnostics_order_leave_intermission_shop":
            wait_seconds = arguments.get("waitSeconds")
            if wait_seconds is not None and not 0.1 <= wait_seconds <= 30:
                return _tool_response(json.dumps({"error": "waitSeconds must be between 0.1 and 30"}), is_error=True)
            raw = _call_diagnostics_method("OrderLeaveIntermissionShopJson", [arguments["gameObjectId"]])
            if wait_seconds is None:
                return _tool_response(raw)
            try:
                action = json.loads(raw)
            except (TypeError, ValueError):
                return _tool_response(json.dumps({"error": "Unreadable Shop exit reply", "completionStatus": "unknown"}), is_error=True)
            if not isinstance(action, dict):
                return _tool_response(json.dumps({"error": "Invalid Shop exit reply", "completionStatus": "unknown"}), is_error=True)
            if action.get("nativeCallInvoked") is not True or action.get("completionStatus") == "unknown":
                return _tool_response(raw)
            wait = _wait_for_state({
                "tool": "diagnostics_get_runtime_control_surface",
                "arguments": {"maxUiResults": 1},
                "timeoutSeconds": wait_seconds,
                "stableSamples": 2,
                "expect": [
                    {"path": "/isIntermissionActive", "equals": True},
                    {"path": "/nativePanelState/isShopActive", "equals": False},
                ],
            })
            transition = json.loads(wait["content"][0]["text"])
            observed = transition.get("success") is True
            return _tool_response(json.dumps({
                "success": observed, "nativeCallInvoked": True, "effectObserved": observed,
                "nativeResult": action, "transition": transition,
            }), is_error=not observed)

        elif name == "diagnostics_order_reroll_intermission_shop":
            res = _call_diagnostics_method("OrderRerollIntermissionShopJson", [])
            return _tool_response(res)

        elif name == "diagnostics_order_remove_intermission_card":
            entity_id = arguments.get("entityId", "")
            if not isinstance(entity_id, str) or not entity_id.strip():
                return _tool_response(json.dumps({"error": "entityId is required"}), is_error=True)
            res = _call_diagnostics_method("OrderRemoveIntermissionCardJson", [entity_id])
            return _tool_response(res)

        elif name == "diagnostics_order_cancel_intermission_card_removal":
            res = _call_diagnostics_method("OrderCancelIntermissionCardRemovalJson", [])
            return _tool_response(res)

        elif name == "diagnostics_order_trigger_load_level":
            res = _call_diagnostics_method("OrderTriggerLoadLevelJson", [])
            return _tool_response(res)

        elif name == "diagnostics_order_choose_intermission_reward":
            reward_id = str(arguments.get("rewardId", "") or "")
            raw_slot = arguments.get("slotIndex", -1)
            try:
                slot_index = -1 if raw_slot is None else int(raw_slot)
            except (TypeError, ValueError):
                return _tool_response(json.dumps({"error": "slotIndex must be an integer"}), is_error=True)
            if "instanceId" in arguments:
                instance_id = arguments["instanceId"]
                if instance_id == 0:
                    return _tool_response(json.dumps({"error": "instanceId must be nonzero"}), is_error=True)
                res = _call_diagnostics_method("OrderChooseIntermissionRewardInstanceJson", [reward_id, slot_index, instance_id])
            else:
                res = _call_diagnostics_method("OrderChooseIntermissionRewardJson", [reward_id, slot_index])
            return _tool_response(res)

        elif name == "diagnostics_order_skip_intermission_reward":
            res = _call_diagnostics_method("OrderSkipIntermissionRewardJson", [])
            return _tool_response(res)

        elif name == "diagnostics_order_reroll_intermission_reward":
            res = _call_diagnostics_method("OrderRerollIntermissionRewardJson", [])
            return _tool_response(res)

        elif name == "diagnostics_order_assign_intermission_upgrade":
            entity_id = str(arguments.get("entityId", "") or "")
            cancel = bool(arguments.get("cancel", False))
            res = _call_diagnostics_method("OrderAssignIntermissionUpgradeJson", [entity_id, cancel])
            return _tool_response(res)

        elif name == "diagnostics_order_set_deck_overview_open":
            if "open" not in arguments or not isinstance(arguments.get("open"), bool):
                return _tool_response(json.dumps({"error": "open must be a boolean"}), is_error=True)
            res = _call_diagnostics_method("OrderSetDeckOverviewOpenJson", [arguments["open"]])
            return _tool_response(res)

        elif name == "diagnostics_get_navigator_state":
            res = _call_diagnostics_method("GetNavigatorStateJson")
            return _tool_response(res)

        elif name == "diagnostics_order_navigator_pause":
            if "paused" not in arguments or not isinstance(arguments.get("paused"), bool):
                return _tool_response(json.dumps({"error": "paused must be a boolean"}), is_error=True)
            res = _call_diagnostics_method("SetNavigatorPausedJson", [arguments["paused"]])
            return _tool_response(res)

        elif name == "diagnostics_get_ui_accessibility_census":
            query = str(arguments.get("query", "") or "")
            include_inactive = arguments.get("includeInactive", False)
            if not isinstance(include_inactive, bool):
                return _tool_response(json.dumps({"error": "includeInactive must be a boolean"}), is_error=True)
            try:
                max_results = int(arguments.get("maxResults", 32))
            except (TypeError, ValueError):
                return _tool_response(json.dumps({"error": "maxResults must be an integer"}), is_error=True)
            max_results = max(1, min(100, max_results))
            res = _call_diagnostics_method("GetUiAccessibilityCensusJson", [query, include_inactive, max_results])
            return _tool_response(res)

        elif name == "diagnostics_ui_invoke_native_button":
            selector = str(arguments.get("selector", "") or "").strip()
            scene = str(arguments.get("scene", "") or "").strip()
            expected_text = str(arguments.get("expectedButtonText", "") or "").strip()
            requested_path = str(arguments.get("hierarchyPath", "") or "").strip()
            if not selector or not scene or not expected_text:
                return _tool_response(json.dumps({"success": False, "message": "selector, scene, and expectedButtonText are required"}), is_error=True)

            try:
                scene_state_raw = _call_diagnostics_method("GetRunProgressionCensusJson")
                scene_state = json.loads(scene_state_raw) if isinstance(scene_state_raw, str) else scene_state_raw
            except Exception as exc:
                return _tool_response(json.dumps({"success": False, "message": f"active scene lookup failed: {exc}"}), is_error=True)
            active_scene = scene_state.get("activeSceneName") if isinstance(scene_state, dict) else None
            if not isinstance(active_scene, str) or not active_scene.strip() or active_scene.casefold() != scene.casefold():
                return _tool_response(json.dumps({
                    "success": False,
                    "message": "requested scene does not exactly match the current active scene; refusing native callback",
                    "requestedScene": scene,
                    "activeScene": active_scene,
                }))

            component_name = "Michsky.UI.Reach.ButtonManager"
            try:
                search_raw = _call_http("scene/search_gameobjects", params={
                    "name": selector,
                    "scene": scene,
                    "includeInactive": "true",
                    "limit": "100",
                })
                search_data = json.loads(search_raw) if isinstance(search_raw, str) else search_raw
            except Exception as exc:
                return _tool_response(json.dumps({"success": False, "message": f"button lookup failed: {exc}"}), is_error=True)

            if isinstance(search_data, list):
                items = search_data
                truncated = False
            elif isinstance(search_data, dict):
                items = next((search_data.get(key) for key in ("items", "objects", "results") if isinstance(search_data.get(key), list)), [])
                truncated = bool(search_data.get("truncated", False))
            else:
                items, truncated = [], False
            if truncated:
                return _tool_response(json.dumps({"success": False, "message": "button lookup was truncated; refusing an incomplete match set"}))

            target_scene = scene.casefold()
            target_path = _normalise_ui_hierarchy_path(requested_path) if requested_path else None
            matches: list[dict[str, Any]] = []
            for item in items:
                if not isinstance(item, dict) or item.get("id") is None:
                    continue
                item_name = str(item.get("name", ""))
                item_scene = str(item.get("scene", ""))
                if item_name.casefold() != selector.casefold() or item_scene.casefold() != target_scene:
                    continue
                item_path = str(item.get("path", ""))
                qualified_path = f"{item_scene}::{urllib.parse.unquote(item_path).lstrip('/')}"
                if target_path and _normalise_ui_hierarchy_path(qualified_path) != target_path:
                    continue
                try:
                    detail_raw = _call_http("component/get_details", params={"id": item["id"], "componentName": component_name})
                    details = json.loads(detail_raw) if isinstance(detail_raw, str) else detail_raw
                except Exception:
                    continue
                if not isinstance(details, dict):
                    continue
                button_text = _component_detail_value(details, "buttonText")
                if not isinstance(button_text, str) or button_text.strip().casefold() != expected_text.casefold():
                    continue
                matches.append({
                    "id": int(item["id"]),
                    "name": item_name,
                    "scene": item_scene,
                    "path": qualified_path,
                    "active": item.get("active") is True,
                    "enabled": _parse_component_bool(_component_detail_value(details, "enabled")),
                    "activeAndEnabled": _parse_component_bool(_component_detail_value(details, "isActiveAndEnabled")),
                    "interactable": _parse_component_bool(_component_detail_value(details, "isInteractable")),
                    "buttonText": button_text.strip(),
                })

            if len(matches) != 1:
                return _tool_response(json.dumps({
                    "success": False,
                    "message": "native button must resolve to exactly one matching active-scene control; refusing ambiguous or missing targets",
                    "matchCount": len(matches),
                    "candidates": matches[:12],
                }))

            target = matches[0]
            if not target["active"] or target["enabled"] is not True or target["interactable"] is not True or target["activeAndEnabled"] is not True:
                return _tool_response(json.dumps({
                    "success": False,
                    "message": "native button is inactive, disabled, or not interactable; callback was not invoked",
                    "target": target,
                }))

            try:
                invoke_raw = _call_http("component/call_method", params={
                    "id": target["id"],
                    "componentName": component_name,
                    "methodName": "InvokeOnClick",
                    "args": "[]",
                })
                invoke_data = json.loads(invoke_raw) if isinstance(invoke_raw, str) else invoke_raw
            except Exception as exc:
                return _tool_response(json.dumps({"success": False, "message": f"native button callback failed: {exc}", "target": target}), is_error=True)
            if not isinstance(invoke_data, dict) or str(invoke_data.get("status", "")).casefold() not in ("ok", "success"):
                return _tool_response(json.dumps({"success": False, "message": "native button callback returned an unsuccessful bridge status", "target": target, "callback": invoke_data}))
            return _tool_response(json.dumps({
                "success": True,
                "activated": True,
                "activation": "Michsky.UI.Reach.ButtonManager.InvokeOnClick",
                "target": target,
                "callback": invoke_data,
                "source": "BepInEx MCP component callback; no screenshot or computer-use interaction",
            }, indent=2))

        elif name == "diagnostics_ui_click_text":
            label = str(arguments.get("label", "") or "").strip()
            hierarchy_path = str(arguments.get("hierarchyPath", "") or "").strip()
            if not label and not hierarchy_path:
                return _tool_response(json.dumps({"error": "label or hierarchyPath is required"}), is_error=True)
            exact_match = arguments.get("exactMatch", True)
            if not isinstance(exact_match, bool):
                return _tool_response(json.dumps({"error": "exactMatch must be a boolean"}), is_error=True)
            res = _call_diagnostics_method("TryUiClickTextJson", [label, exact_match, hierarchy_path])
            return _tool_response(res)

        elif name == "diagnostics_wait_for_ui_text":
            label = str(arguments.get("label", "") or "").strip()
            hierarchy_path = str(arguments.get("hierarchyPath", "") or "").strip()
            if not label and not hierarchy_path:
                return _tool_response(json.dumps({"error": "label or hierarchyPath is required"}), is_error=True)
            exact_match = arguments.get("exactMatch", True)
            require_interactable = arguments.get("requireInteractable", True)
            if not isinstance(exact_match, bool) or not isinstance(require_interactable, bool):
                return _tool_response(json.dumps({"error": "exactMatch and requireInteractable must be booleans"}), is_error=True)
            try:
                timeout_seconds = float(arguments.get("timeoutSeconds", 10))
                poll_interval_ms = int(arguments.get("pollIntervalMs", 250))
            except (TypeError, ValueError):
                return _tool_response(json.dumps({"error": "timeoutSeconds must be numeric and pollIntervalMs an integer"}), is_error=True)
            if not 0.1 <= timeout_seconds <= 30:
                return _tool_response(json.dumps({"error": "timeoutSeconds must be in [0.1, 30]"}), is_error=True)
            if not 100 <= poll_interval_ms <= 1000:
                return _tool_response(json.dumps({"error": "pollIntervalMs must be in [100, 1000]"}), is_error=True)

            started = time.monotonic()
            deadline = started + timeout_seconds
            last_candidates: list[dict[str, Any]] = []
            census_query = label or hierarchy_path
            while True:
                if time.monotonic() >= deadline:
                    return _tool_response(json.dumps({
                        "success": False,
                        "label": label,
                        "hierarchyPath": hierarchy_path,
                        "waitedSeconds": round(time.monotonic() - started, 3),
                        "reason": "timeout",
                        "candidates": last_candidates,
                        "source": "Unity UI accessibility census; no screenshot captured",
                    }, indent=2))
                request_deadline = deadline
                automation_deadline = _AUTOMATION_DEADLINE.get()
                if automation_deadline is not None:
                    request_deadline = min(request_deadline, automation_deadline)
                deadline_token = _AUTOMATION_DEADLINE.set(request_deadline)
                try:
                    raw = _call_diagnostics_method("GetUiAccessibilityCensusJson", [census_query, False, 100])
                finally:
                    _AUTOMATION_DEADLINE.reset(deadline_token)
                try:
                    census = json.loads(raw)
                except (TypeError, ValueError):
                    return _tool_response(json.dumps({"success": False, "label": label, "error": "invalid UI census response", "response": raw}), is_error=True)
                if isinstance(census, dict) and census.get("error"):
                    return _tool_response(json.dumps({
                        "success": False,
                        "label": label,
                        "hierarchyPath": hierarchy_path,
                        "error": census.get("error"),
                        "completionStatus": census.get("completionStatus", "unknown"),
                        "message": census.get("message", "inspect the UI state before retrying"),
                    }, indent=2), is_error=True)
                rows = census.get("candidates", []) if isinstance(census, dict) else []
                matched: list[dict[str, Any]] = []
                for row in rows if isinstance(rows, list) else []:
                    if not isinstance(row, dict):
                        continue
                    candidate_label = str(row.get("label", ""))
                    candidate_path = str(row.get("hierarchyPath", ""))
                    label_matches = not label or (candidate_label.casefold() == label.casefold() if exact_match else label.casefold() in candidate_label.casefold())
                    path_matches = not hierarchy_path or candidate_path == hierarchy_path
                    if label_matches and path_matches:
                        matched.append(row)
                last_candidates = matched
                ready = len(matched) == 1
                if ready and require_interactable:
                    ready = bool(matched[0].get("interactable")) and bool(matched[0].get("onScreen"))
                if ready:
                    return _tool_response(json.dumps({
                        "success": True,
                        "label": label,
                        "exactMatch": exact_match,
                        "hierarchyPath": hierarchy_path,
                        "waitedSeconds": round(time.monotonic() - started, 3),
                        "candidate": matched[0],
                        "source": "Unity UI accessibility census; no screenshot captured",
                    }, indent=2))
                if time.monotonic() >= deadline:
                    reason = "ambiguous" if len(matched) > 1 else ("not_interactable_or_off_screen" if matched else "not_found")
                    return _tool_response(json.dumps({
                        "success": False,
                        "label": label,
                        "exactMatch": exact_match,
                        "hierarchyPath": hierarchy_path,
                        "waitedSeconds": round(time.monotonic() - started, 3),
                        "reason": reason,
                        "requireInteractable": require_interactable,
                        "candidates": last_candidates,
                        "source": "Unity UI accessibility census; no screenshot captured",
                    }, indent=2))
                time.sleep(min(poll_interval_ms / 1000.0, max(0.0, deadline - time.monotonic())))

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
    def invalid(code: int, text: str) -> dict[str, Any]:
        rid = message.get("id") if isinstance(message, dict) else None
        if isinstance(rid, (dict, list, bool, float)):
            rid = None
        return {"jsonrpc": "2.0", "id": rid, "error": {"code": code, "message": text}}

    if not isinstance(message, dict) or message.get("jsonrpc") != "2.0" or not isinstance(message.get("method"), str):
        return invalid(-32600, "Invalid Request")
    if "id" in message and isinstance(message["id"], (bool, float, dict, list)):
        return invalid(-32600, "Request id must be a string, integer or null")
    # Notifications cannot invoke tools: mutations must have a result channel.
    if "id" not in message:
        return None
    if "params" in message and not isinstance(message["params"], dict):
        return invalid(-32602, "params must be an object")
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
        if not isinstance(params.get("name"), str) or not params["name"].strip():
            return invalid(-32602, "Tool name must be a non-empty string")
        if "arguments" in params and not isinstance(params["arguments"], dict):
            return invalid(-32602, "Tool arguments must be an object")
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
        "description": "BepInEx MCP server bridge for Rogue Command runtime diagnostics, screenless UI automation, Steam launch/Cloud control, telemetry censuses, and tactical gameplay commands.",
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


MAX_MESSAGE_BYTES = 16 * 1024 * 1024


class MessageFramingError(ValueError):
    """Unrecoverable framing error; the stream must not be reused."""

    fatal_framing = True


def _read_framed_message() -> dict[str, Any] | None:
    stream = sys.stdin.buffer
    while True:
        first_line = stream.readline(MAX_MESSAGE_BYTES + 1)
        if len(first_line) > MAX_MESSAGE_BYTES:
            raise MessageFramingError("Message exceeds 16 MiB")
        if not first_line:
            return None
        line = first_line.strip()
        if line:
            break

    if line.lower().startswith(b"content-length:"):
        try:
            length = int(line.split(b":", 1)[1].strip())
            if not 0 <= length <= MAX_MESSAGE_BYTES:
                raise ValueError("Content-Length outside 0..16 MiB")
            header_bytes = len(first_line)
            while True:
                sep = stream.readline(8193)
                header_bytes += len(sep)
                if header_bytes > 8192:
                    raise ValueError("headers exceed 8192 bytes")
                if not sep:
                    raise EOFError("incomplete headers")
                if sep in (b"\r\n", b"\n"):
                    break
            body = stream.read(length)
            if len(body) != length:
                raise EOFError("incomplete message body")
            return json.loads(body.decode("utf-8"))
        except Exception as exc:
            raise MessageFramingError(f"Failed to read Content-Length framed message: {exc}")

    return json.loads(line.decode("utf-8"))


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

    # This loop owns stdio for its lifetime. Reload only the handler, never
    # replace the process or replay a request after an uncertain result.
    from bepinex_mcp_reload import ReloadingHandler
    live_handler = ReloadingHandler(Path(__file__))
    while True:
        message = None
        try:
            message = live_handler.read_message()
            if message is None:
                break
            response, catalog_changed = live_handler.dispatch(message)
            if catalog_changed:
                sys.stdout.write(json.dumps({"jsonrpc": "2.0", "method": "notifications/tools/list_changed"}) + "\n")
                sys.stdout.flush()
        except MessageFramingError as exc:
            sys.stdout.write(json.dumps({"jsonrpc": "2.0", "id": None, "error": {"code": -32700, "message": str(exc)}}) + "\n")
            sys.stdout.flush()
            break
        except json.JSONDecodeError:
            response = {
                "jsonrpc": "2.0",
                "id": None,
                "error": {"code": -32700, "message": "Parse error"},
            }
        except Exception as exc:
            if getattr(exc, "fatal_framing", False):
                sys.stdout.write(json.dumps({"jsonrpc": "2.0", "id": None, "error": {"code": -32700, "message": str(exc)}}) + "\n")
                sys.stdout.flush()
                break
            response = {
                "jsonrpc": "2.0",
                "id": message.get("id") if isinstance(message, dict) else None,
                "error": {"code": -32603, "message": f"Internal error: {exc}"},
            }

        if response is not None:
            sys.stdout.write(json.dumps(response, separators=(",", ":")) + "\n")
            sys.stdout.flush()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
