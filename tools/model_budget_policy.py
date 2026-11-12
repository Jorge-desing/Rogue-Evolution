"""Performance-calibrated model triangle and face budgets.

The policy deliberately has no per-asset face ceiling.  It derives one scene
triangle capacity from measured unscaled frame-interval samples, then distributes that
capacity across the scene's visible assets and LODs.  Blender face counts are
reported separately because Unity renders triangulated meshes.
"""

from __future__ import annotations

from dataclasses import dataclass
import json
import math
from pathlib import Path
from typing import Any, Iterable, Mapping, Sequence


SCHEMA_VERSION = 1
DEFAULT_TARGET_FPS = 60.0
LOD_NAMES = ("LOD0", "LOD1", "LOD2")


class ModelBudgetError(ValueError):
    """A profile cannot produce a safe, reproducible geometry budget."""


@dataclass(frozen=True)
class FrameSample:
    triangles: int
    p95_unscaled_frame_interval_ms: float


@dataclass(frozen=True)
class AssetDemand:
    asset_id: str
    source_faces: int
    source_triangles: int
    max_visible_instances: int
    screen_height_fraction: float
    role_priority: float
    lod_screen_heights: tuple[float, ...]


def _frame_samples(values: Iterable[Mapping[str, Any]]) -> list[FrameSample]:
    samples: list[FrameSample] = []
    for index, item in enumerate(values):
        try:
            triangles = int(item["triangles"])
            frame_interval = float(item["p95UnscaledFrameIntervalMs"])
        except (KeyError, TypeError, ValueError) as exc:
            raise ModelBudgetError(f"calibrationSamples[{index}] is invalid") from exc
        if triangles < 0 or not math.isfinite(frame_interval) or frame_interval <= 0.0:
            raise ModelBudgetError(f"calibrationSamples[{index}] must have nonnegative triangles and positive p95 frame interval")
        samples.append(FrameSample(triangles, frame_interval))
    return samples


def fit_triangle_capacity(
    samples: Sequence[FrameSample],
    target_fps: float = DEFAULT_TARGET_FPS,
    capacity_model: Mapping[str, Any] | None = None,
) -> dict[str, float | int]:
    """Fit p95 unscaled frame interval to triangle count and find scene capacity.

    At least three measurements with two distinct geometry loads are required.
    The default model fits a positive linear slope. An explicit measured-envelope
    model instead bounds the incremental asset budget to a reserved fraction of
    the observed load range and never extrapolates beyond measured triangles.
    """
    if not math.isfinite(target_fps) or target_fps <= 0.0:
        raise ModelBudgetError("targetFps must be finite and greater than zero")
    if len(samples) < 3 or len({sample.triangles for sample in samples}) < 2:
        raise ModelBudgetError("calibration needs at least three samples at two or more triangle loads")

    if capacity_model is not None:
        method = str(capacity_model.get("method", ""))
        if method != "conservative-measured-incremental-envelope-v1":
            raise ModelBudgetError(f"unsupported capacity model: {method or '<missing>'}")
        try:
            reserve_fraction = float(capacity_model["reserveFraction"])
        except (KeyError, TypeError, ValueError) as exc:
            raise ModelBudgetError("measured-envelope capacity model needs reserveFraction") from exc
        if not math.isfinite(reserve_fraction) or not 0.0 < reserve_fraction < 0.5:
            raise ModelBudgetError("measured-envelope reserveFraction must be greater than 0 and less than 0.5")
        target_frame_time_ms = 1000.0 / target_fps
        if any(sample.p95_unscaled_frame_interval_ms > target_frame_time_ms for sample in samples):
            raise ModelBudgetError(
                f"measured samples exceed the {target_frame_time_ms:.3f} ms target; "
                "a passing 60-FPS geometry envelope cannot be derived"
            )
        baseline_triangles = min(sample.triangles for sample in samples)
        maximum_triangles = max(sample.triangles for sample in samples)
        observed_increment = maximum_triangles - baseline_triangles
        capacity = math.floor(observed_increment * (1.0 - reserve_fraction))
        if baseline_triangles <= 0 or observed_increment <= 0 or capacity <= 0:
            raise ModelBudgetError("measured-envelope samples need a positive baseline and load range")
        return {
            "targetFps": float(target_fps),
            "targetFrameTimeMs": target_frame_time_ms,
            "fitMethod": method,
            "capacityScope": "incremental-asset-geometry-above-lowest-observed-load",
            "observedBaselineTriangles": int(baseline_triangles),
            "observedMaximumTriangles": int(maximum_triangles),
            "observedTriangleRange": int(observed_increment),
            "sceneTriangleCapacity": int(capacity),
            "reservedTriangleHeadroom": int(observed_increment - capacity),
            "safetyReserveFraction": reserve_fraction,
            "maxMeasuredP95FrameIntervalMs": max(sample.p95_unscaled_frame_interval_ms for sample in samples),
            "extrapolationUsed": False,
            "estimateStatus": "measured-envelope-candidate",
        }

    x_mean = sum(sample.triangles for sample in samples) / len(samples)
    y_mean = sum(sample.p95_unscaled_frame_interval_ms for sample in samples) / len(samples)
    covariance = sum(
        (sample.triangles - x_mean) * (sample.p95_unscaled_frame_interval_ms - y_mean)
        for sample in samples
    )
    variance = sum((sample.triangles - x_mean) ** 2 for sample in samples)
    if variance <= 0.0:
        raise ModelBudgetError("calibration triangle loads do not vary")
    slope = covariance / variance
    intercept = y_mean - slope * x_mean
    if not math.isfinite(slope) or slope <= 0.0 or not math.isfinite(intercept):
        raise ModelBudgetError("frame-interval samples do not show a positive geometry cost")

    target_frame_time_ms = 1000.0 / target_fps
    if intercept >= target_frame_time_ms:
        raise ModelBudgetError(
            f"non-geometry baseline {intercept:.3f} ms already meets or exceeds "
            f"the {target_frame_time_ms:.3f} ms target"
        )
    capacity = math.floor((target_frame_time_ms - intercept) / slope)
    if capacity <= 0:
        raise ModelBudgetError("measured frame-interval headroom yields no positive triangle capacity")
    return {
        "targetFps": float(target_fps),
        "targetFrameTimeMs": target_frame_time_ms,
        "baselineFrameIntervalMs": intercept,
        "millisecondsPerTriangle": slope,
        "sceneTriangleCapacity": int(capacity),
    }


def _validate_demand(demand: AssetDemand) -> None:
    if not demand.asset_id.strip():
        raise ModelBudgetError("assetId cannot be empty")
    if demand.source_faces <= 0 or demand.source_triangles <= 0:
        raise ModelBudgetError(f"{demand.asset_id}: source faces and triangles must be positive")
    if demand.max_visible_instances <= 0:
        raise ModelBudgetError(f"{demand.asset_id}: maxVisibleInstances must be positive")
    if not math.isfinite(demand.screen_height_fraction) or not 0.0 < demand.screen_height_fraction <= 1.0:
        raise ModelBudgetError(f"{demand.asset_id}: screenHeightFraction must be within (0, 1]")
    if not math.isfinite(demand.role_priority) or demand.role_priority <= 0.0:
        raise ModelBudgetError(f"{demand.asset_id}: rolePriority must be positive")
    heights = demand.lod_screen_heights
    if len(heights) != len(LOD_NAMES):
        raise ModelBudgetError(f"{demand.asset_id}: exactly three LOD screen thresholds are required")
    if any(not math.isfinite(height) or not 0.0 < height <= 1.0 for height in heights):
        raise ModelBudgetError(f"{demand.asset_id}: LOD screen thresholds must be within (0, 1]")
    if not (heights[0] > heights[1] > heights[2]):
        raise ModelBudgetError(f"{demand.asset_id}: LOD screen thresholds must descend")


def allocate_scene_budgets(
    triangle_capacity: int,
    demands: Sequence[AssetDemand],
) -> dict[str, Any]:
    """Allocate a finite measured scene budget by visibility, multiplicity and role.

    The LOD triangle allowance scales with projected screen area, while each
    per-asset LOD0 allowance is allocated from the aggregate scene capacity.
    Assets that need fewer triangles return unused capacity to the remaining
    visible assets through a deterministic water-fill pass.
    """
    if triangle_capacity <= 0:
        raise ModelBudgetError("scene triangle capacity must be positive")
    if not demands:
        raise ModelBudgetError("scene must declare at least one asset demand")
    seen: set[str] = set()
    for demand in demands:
        _validate_demand(demand)
        if demand.asset_id in seen:
            raise ModelBudgetError(f"duplicate assetId: {demand.asset_id}")
        seen.add(demand.asset_id)

    weights = {
        demand.asset_id: (
            demand.max_visible_instances
            * demand.screen_height_fraction ** 2
            * demand.role_priority
        )
        for demand in demands
    }
    remaining_capacity = int(triangle_capacity)
    active = {d.asset_id for d in demands if d.source_triangles > 0}
    allocated: dict[str, int] = {d.asset_id: 0 for d in demands}
    while active and remaining_capacity > 0:
        total_weight = sum(weights[asset_id] for asset_id in active)
        if total_weight <= 0.0:
            break
        quotas = {
            asset_id: remaining_capacity * weights[asset_id] / total_weight
            for asset_id in active
        }
        pass_allocations = {
            asset_id: min(
                max(0, math.floor(quotas[asset_id])),
                next(d.source_triangles * d.max_visible_instances for d in demands if d.asset_id == asset_id)
                - allocated[asset_id],
            )
            for asset_id in active
        }
        spent = 0
        saturated: set[str] = set()
        for asset_id in sorted(active):
            demand = next(d for d in demands if d.asset_id == asset_id)
            cap = demand.source_triangles * demand.max_visible_instances
            available = max(0, cap - allocated[asset_id])
            amount = min(available, pass_allocations[asset_id])
            allocated[asset_id] += amount
            spent += amount
            if allocated[asset_id] >= cap:
                saturated.add(asset_id)
        if spent == 0:
            break
        remaining_capacity -= spent
        active -= saturated
        if remaining_capacity > 0 and active:
            # Assign at most one rounding remainder per active asset, in a
            # deterministic largest-remainder order. This prevents a tiny
            # scene budget from being exceeded when there are many asset types.
            ranked = sorted(
                active,
                key=lambda asset_id: (-(quotas[asset_id] - math.floor(quotas[asset_id])), asset_id),
            )
            remainder_spent = 0
            for asset_id in ranked:
                demand = next(d for d in demands if d.asset_id == asset_id)
                cap = demand.source_triangles * demand.max_visible_instances
                if remaining_capacity <= 0:
                    break
                if allocated[asset_id] < cap:
                    allocated[asset_id] += 1
                    remaining_capacity -= 1
                    remainder_spent += 1
            if remainder_spent == 0 and spent == 0:
                break

    rows: list[dict[str, Any]] = []
    scene_lod_totals = {name: 0 for name in LOD_NAMES}
    for demand in sorted(demands, key=lambda row: row.asset_id):
        allocated_per_instance = allocated[demand.asset_id] // demand.max_visible_instances
        average_triangles_per_face = demand.source_triangles / demand.source_faces
        lod_rows: list[dict[str, Any]] = []
        reference_height = demand.lod_screen_heights[0]
        for name, screen_height in zip(LOD_NAMES, demand.lod_screen_heights):
            screen_scale = min(1.0, (screen_height / reference_height) ** 2)
            per_instance_triangles = min(demand.source_triangles, math.floor(allocated_per_instance * screen_scale))
            per_instance_faces = (
                0 if per_instance_triangles <= 0
                else min(demand.source_faces, max(1, math.floor(per_instance_triangles / average_triangles_per_face)))
            )
            scene_triangles = per_instance_triangles * demand.max_visible_instances
            scene_lod_totals[name] += scene_triangles
            lod_rows.append({
                "lod": name,
                "screenRelativeTransitionHeight": screen_height,
                "targetFacesPerInstance": per_instance_faces,
                "targetTrianglesPerInstance": per_instance_triangles,
                "maxVisibleInstances": demand.max_visible_instances,
                "sceneTriangles": scene_triangles,
            })
        rows.append({
            "assetId": demand.asset_id,
            "sourceFaces": demand.source_faces,
            "sourceTriangles": demand.source_triangles,
            "maxVisibleInstances": demand.max_visible_instances,
            "screenHeightFraction": demand.screen_height_fraction,
            "rolePriority": demand.role_priority,
            "sceneAllocationTriangles": allocated[demand.asset_id],
            "assetVerdict": (
                "NO_SCENE_ALLOCATION" if allocated_per_instance <= 0
                else "PASS" if demand.source_triangles <= allocated_per_instance
                else "LOD0_REDUCTION_REQUIRED"
            ),
            "lods": lod_rows,
        })

    return {
        "sceneTriangleCapacity": int(triangle_capacity),
        "sceneTriangleBudgetByLod": scene_lod_totals,
        "sceneBudgetPassed": all(total <= triangle_capacity for total in scene_lod_totals.values()),
        "assets": rows,
    }


def build_budget_report(profile: Mapping[str, Any]) -> dict[str, Any]:
    """Validate a calibrated profile and return its reproducible scene report."""
    if int(profile.get("schemaVersion", 0)) != SCHEMA_VERSION:
        raise ModelBudgetError(f"model budget profile schemaVersion must be {SCHEMA_VERSION}")
    target_fps = float(profile.get("targetFps", DEFAULT_TARGET_FPS))
    if not math.isfinite(target_fps) or abs(target_fps - DEFAULT_TARGET_FPS) > 0.1:
        raise ModelBudgetError("Rogue Command geometry budgets must target 60 FPS")
    if profile.get("calibrationStatus") != "calibrated":
        reason = str(profile.get("calibrationFailureReason") or "no successful performance calibration is recorded")
        raise ModelBudgetError(f"model budget profile is not calibrated: {reason}")
    calibration = profile.get("calibration")
    if not isinstance(calibration, Mapping):
        raise ModelBudgetError("calibration object is required")
    samples = _frame_samples(calibration.get("samples", []))
    fit = fit_triangle_capacity(samples, target_fps, calibration.get("capacityModel"))
    if int(calibration.get("resolutionWidth", 0)) <= 0 or int(calibration.get("resolutionHeight", 0)) <= 0:
        raise ModelBudgetError("calibration resolution must be positive")
    camera_reference = str(calibration.get("cameraReference", "")).strip()
    if not camera_reference or camera_reference.lower().startswith(("pending", "unknown", "unverified")):
        raise ModelBudgetError("calibration.cameraReference must identify the reference camera")
    scene_mix = str(calibration.get("sceneMix", "")).strip()
    if not scene_mix or scene_mix.lower().startswith(("pending", "unknown", "unverified")):
        raise ModelBudgetError("calibration.sceneMix must describe the benchmark scene")
    if int(calibration.get("maximumVisibleInstances", 0)) <= 0:
        raise ModelBudgetError("calibration.maximumVisibleInstances must be positive")
    capacity_model = calibration.get("capacityModel")
    measurement_context = calibration.get("measurementContext")
    if (isinstance(capacity_model, Mapping) and
            capacity_model.get("method") == "conservative-measured-incremental-envelope-v1" and
            (not isinstance(measurement_context, Mapping) or measurement_context.get("capacityEligible") is not True)):
        raise ModelBudgetError("measured-envelope samples are evidence-only until their load pair is controlled")
    if isinstance(capacity_model, Mapping) and capacity_model.get("method") == "conservative-measured-incremental-envelope-v1":
        scene_reference = calibration.get("sceneReference")
        if (not isinstance(scene_reference, Mapping) or scene_reference.get("unitySceneName") != "Game" or
                measurement_context.get("sceneReference") != scene_reference):
            raise ModelBudgetError("calibration.sceneReference must match the bound runtime evidence context")
        for index, sample in enumerate(calibration.get("samples", [])):
            if not isinstance(sample, Mapping) or sample.get("measurementContext") != measurement_context:
                raise ModelBudgetError(f"calibration.samples[{index}] is not bound to the profile measurement context")
            if (sample.get("scene") != "Game" or
                    int(sample.get("pixelWidth", 0)) != int(calibration.get("resolutionWidth", 0)) or
                    int(sample.get("pixelHeight", 0)) != int(calibration.get("resolutionHeight", 0))):
                raise ModelBudgetError(f"calibration.samples[{index}] scene or resolution differs from the profile")
    scenes = profile.get("scenes")
    if not isinstance(scenes, Sequence) or isinstance(scenes, (str, bytes)) or not scenes:
        raise ModelBudgetError("profile must contain at least one scene")
    reports: list[dict[str, Any]] = []
    for scene in scenes:
        if not isinstance(scene, Mapping) or not scene.get("sceneId"):
            raise ModelBudgetError("each scene requires sceneId")
        demands: list[AssetDemand] = []
        demand_instance_total = 0
        inventory = profile.get("assetInventory")
        inventory_rows = inventory.get("assets") if isinstance(inventory, Mapping) else None
        inventory_by_id = (
            {str(row.get("assetId")): row for row in inventory_rows if isinstance(row, Mapping)}
            if isinstance(inventory_rows, Sequence) and not isinstance(inventory_rows, (str, bytes))
            else None
        )
        for item in scene.get("assets", []):
            if not isinstance(item, Mapping):
                raise ModelBudgetError(f"{scene['sceneId']}: asset entries must be objects")
            if "rolePriority" not in item:
                raise ModelBudgetError(
                    f"{scene['sceneId']}/{item.get('assetId', '<unknown>')}: rolePriority is required"
                )
            thresholds = item.get("lodScreenHeights")
            if not isinstance(thresholds, Sequence) or isinstance(thresholds, (str, bytes)):
                raise ModelBudgetError(f"{scene['sceneId']}/{item.get('assetId', '<unknown>')}: lodScreenHeights are required")
            asset_id = str(item.get("assetId", ""))
            if inventory_by_id is not None:
                source = inventory_by_id.get(asset_id)
                if source is None:
                    raise ModelBudgetError(f"{scene['sceneId']}/{asset_id}: asset demand is absent from assetInventory")
                for demand_key in ("sourceFaces", "sourceTriangles"):
                    if int(item.get(demand_key, 0)) != int(source.get(demand_key, 0)):
                        raise ModelBudgetError(f"{scene['sceneId']}/{asset_id}: {demand_key} does not match assetInventory")
                expected_sha = str(source.get("sha256", "")).lower()
                demanded_sha = str(item.get("sourceSha256", "")).lower()
                if not expected_sha or demanded_sha != expected_sha:
                    raise ModelBudgetError(f"{scene['sceneId']}/{asset_id}: sourceSha256 does not match assetInventory")
            demand_instance_total += int(item.get("maxVisibleInstances", 0))
            demands.append(AssetDemand(
                asset_id=asset_id,
                source_faces=int(item.get("sourceFaces", 0)),
                source_triangles=int(item.get("sourceTriangles", 0)),
                max_visible_instances=int(item.get("maxVisibleInstances", 0)),
                screen_height_fraction=float(item.get("screenHeightFraction", 0.0)),
                role_priority=float(item["rolePriority"]),
                lod_screen_heights=tuple(float(value) for value in thresholds),
            ))
        if demand_instance_total > int(calibration.get("maximumVisibleInstances", 0)):
            raise ModelBudgetError(
                f"{scene['sceneId']}: asset demands total {demand_instance_total} visible instances, "
                "above calibration.maximumVisibleInstances"
            )
        scene_report = allocate_scene_budgets(int(fit["sceneTriangleCapacity"]), demands)
        scene_report["sceneId"] = str(scene["sceneId"])
        reports.append(scene_report)
    return {
        "schemaVersion": SCHEMA_VERSION,
        "calibrationStatus": "calibrated",
        "targetFps": target_fps,
        "targetFrameTimeMs": fit["targetFrameTimeMs"],
        "calibrationFit": fit,
        "budgetVerdict": "CALIBRATED_FOR_RECORDED_PC_AND_SCENE",
        "hardware": profile.get("hardware", {}),
        "cameraReference": calibration["cameraReference"],
        "sceneMix": calibration["sceneMix"],
        "maximumVisibleInstances": int(calibration["maximumVisibleInstances"]),
        "resolution": {
            "width": int(calibration["resolutionWidth"]),
            "height": int(calibration["resolutionHeight"]),
        },
        "scenes": reports,
    }


def resolve_asset_budget(
    profile: Mapping[str, Any],
    asset_id: str,
    scene_id: str | None = None,
) -> dict[str, Any]:
    """Resolve one asset's calibrated LOD allocation without a face-count fallback."""
    report = build_budget_report(profile)
    matches: list[dict[str, Any]] = []
    for scene in report["scenes"]:
        if scene_id and scene["sceneId"] != scene_id:
            continue
        for asset in scene["assets"]:
            if asset["assetId"] != asset_id:
                continue
            enriched = dict(asset)
            enriched["sceneId"] = scene["sceneId"]
            enriched["sceneTriangleCapacity"] = scene["sceneTriangleCapacity"]
            enriched["sceneBudgetPassed"] = scene["sceneBudgetPassed"]
            enriched["sceneTriangleBudgetByLod"] = dict(scene["sceneTriangleBudgetByLod"])
            enriched["resolution"] = dict(report["resolution"])
            enriched["targetFps"] = report["targetFps"]
            enriched["budgetVerdict"] = report["budgetVerdict"]
            matches.append(enriched)
    if not matches:
        raise ModelBudgetError(f"{asset_id}: no calibrated geometry allocation exists in the selected scene")
    if len(matches) == 1:
        return matches[0]

    transition_sets = [
        tuple(row["screenRelativeTransitionHeight"] for row in match["lods"])
        for match in matches
    ]
    if any(value != transition_sets[0] for value in transition_sets[1:]):
        raise ModelBudgetError(
            f"{asset_id}: LOD screen thresholds conflict across scenes; Unity requires one authored transition set"
        )

    # An FBX is shared by scenes, so its authored LOD chain must satisfy the
    # tightest per-instance allocation in every calibrated scene where it is
    # visible. Keep the per-scene inputs alongside the conservative result.
    limiting_scene = min(
        matches,
        key=lambda match: min(int(row["targetTrianglesPerInstance"]) for row in match["lods"]),
    )
    worst_lods: list[dict[str, Any]] = []
    for lod_index, transition in enumerate(transition_sets[0]):
        candidates = [match["lods"][lod_index] for match in matches]
        triangle_limit = min(int(row["targetTrianglesPerInstance"]) for row in candidates)
        face_limit = min(int(row["targetFacesPerInstance"]) for row in candidates)
        chosen = min(candidates, key=lambda row: int(row["targetTrianglesPerInstance"]))
        worst_lods.append({
            **chosen,
            "screenRelativeTransitionHeight": transition,
            "targetTrianglesPerInstance": triangle_limit,
            "targetFacesPerInstance": face_limit,
            "calibratedScenes": [match["sceneId"] for match in matches],
        })

    return {
        **limiting_scene,
        "sceneId": "worst-case:" + ",".join(sorted(match["sceneId"] for match in matches)),
        "sceneTriangleCapacity": min(int(match["sceneTriangleCapacity"]) for match in matches),
        "sceneBudgetPassed": all(bool(match["sceneBudgetPassed"]) for match in matches),
        "sceneTriangleBudgetByLod": {
            match["sceneId"]: dict(match["sceneTriangleBudgetByLod"])
            for match in matches
        },
        "sceneAllocations": [
            {
                "sceneId": match["sceneId"],
                "sceneTriangleCapacity": match["sceneTriangleCapacity"],
                "sceneBudgetPassed": match["sceneBudgetPassed"],
                "sceneTriangleBudgetByLod": dict(match["sceneTriangleBudgetByLod"]),
                "sceneAllocationTriangles": match["sceneAllocationTriangles"],
                "lods": match["lods"],
            }
            for match in matches
        ],
        "sceneAllocationTriangles": min(int(match["sceneAllocationTriangles"]) for match in matches),
        "maxVisibleInstances": max(int(match["maxVisibleInstances"]) for match in matches),
        "lods": worst_lods,
        "assetVerdict": (
            "NO_SCENE_ALLOCATION" if any(match["assetVerdict"] == "NO_SCENE_ALLOCATION" for match in matches)
            else "LOD0_REDUCTION_REQUIRED" if any(match["assetVerdict"] == "LOD0_REDUCTION_REQUIRED" for match in matches)
            else "PASS"
        ),
    }


def validate_prevalidated_export_budget(
    budget: Mapping[str, Any] | None,
    expected_asset_id: str | None = None,
) -> None:
    """Fail closed unless an FBX export carries a real resolved allocation.

    This boundary is intentionally structural: builders obtain the value from
    :func:`resolve_asset_budget` before they create output paths, then pass it
    to the low-level exporter. It prevents accidental unbudgeted exports and
    cross-asset token reuse without adding a fixed geometry ceiling.
    """
    if not isinstance(budget, Mapping):
        raise ModelBudgetError("a resolved calibrated geometry budget is required for FBX export")

    asset_id = str(budget.get("assetId", "")).strip()
    if not asset_id:
        raise ModelBudgetError("FBX export budget must declare assetId")
    if expected_asset_id is not None and asset_id != expected_asset_id:
        raise ModelBudgetError(
            f"FBX export budget assetId {asset_id!r} does not match requested asset {expected_asset_id!r}"
        )
    if not str(budget.get("sceneId", "")).strip():
        raise ModelBudgetError(f"{asset_id}: FBX export budget must declare sceneId")
    if budget.get("budgetVerdict") != "CALIBRATED_FOR_RECORDED_PC_AND_SCENE":
        raise ModelBudgetError(f"{asset_id}: FBX export requires a calibrated profile verdict")
    if budget.get("sceneBudgetPassed") is not True:
        raise ModelBudgetError(f"{asset_id}: FBX export scene budget has not passed")
    if budget.get("assetVerdict") != "PASS":
        raise ModelBudgetError(f"{asset_id}: FBX export asset allocation has not passed")

    try:
        target_fps = float(budget["targetFps"])
        capacity = int(budget["sceneTriangleCapacity"])
        allocation = int(budget["sceneAllocationTriangles"])
        max_instances = int(budget["maxVisibleInstances"])
        resolution = budget["resolution"]
        width = int(resolution["width"])
        height = int(resolution["height"])
        lods = budget["lods"]
    except (KeyError, TypeError, ValueError) as exc:
        raise ModelBudgetError(f"{asset_id}: FBX export budget is incomplete") from exc

    if not math.isfinite(target_fps) or abs(target_fps - DEFAULT_TARGET_FPS) > 0.1:
        raise ModelBudgetError(f"{asset_id}: FBX export budget must target {DEFAULT_TARGET_FPS:g} FPS")
    if capacity <= 0 or allocation <= 0 or allocation > capacity:
        raise ModelBudgetError(f"{asset_id}: FBX export budget has an invalid scene allocation")
    if max_instances <= 0 or width <= 0 or height <= 0:
        raise ModelBudgetError(f"{asset_id}: FBX export budget has invalid visibility or resolution metadata")
    if not isinstance(lods, list) or len(lods) != len(LOD_NAMES):
        raise ModelBudgetError(f"{asset_id}: FBX export budget must contain exactly three LOD allocations")

    transitions: list[float] = []
    for name, row in zip(LOD_NAMES, lods):
        if not isinstance(row, Mapping) or row.get("lod") != name:
            raise ModelBudgetError(f"{asset_id}: FBX export LOD allocation order is invalid")
        try:
            transition = float(row["screenRelativeTransitionHeight"])
            target_faces = int(row["targetFacesPerInstance"])
            target_triangles = int(row["targetTrianglesPerInstance"])
        except (KeyError, TypeError, ValueError) as exc:
            raise ModelBudgetError(f"{asset_id}: {name} export allocation is incomplete") from exc
        if (
            not math.isfinite(transition)
            or not 0.0 < transition <= 1.0
            or target_faces <= 0
            or target_triangles <= 0
        ):
            raise ModelBudgetError(f"{asset_id}: {name} export allocation is invalid")
        transitions.append(transition)
    if not (transitions[0] > transitions[1] > transitions[2]):
        raise ModelBudgetError(f"{asset_id}: FBX export LOD screen thresholds must descend")


def calibrate_profile(profile: Mapping[str, Any]) -> dict[str, Any]:
    """Fit the profile samples and return an explicit calibrated or uncalibrated state."""
    result = dict(profile)
    result.pop("calibrationFit", None)
    result.pop("measurementCapacityEstimate", None)
    try:
        result["targetFps"] = float(profile.get("targetFps", DEFAULT_TARGET_FPS))
    except (TypeError, ValueError):
        result["calibrationStatus"] = "uncalibrated"
        result["calibrationFailureReason"] = "targetFps must be a finite number"
        result.pop("calibrationFit", None)
        result.pop("budgetReport", None)
        return result
    if not math.isfinite(result["targetFps"]) or abs(result["targetFps"] - DEFAULT_TARGET_FPS) > 0.1:
        result["calibrationStatus"] = "uncalibrated"
        result["calibrationFailureReason"] = "Rogue Command geometry budgets must target 60 FPS"
        result.pop("calibrationFit", None)
        result.pop("budgetReport", None)
        return result
    calibration = profile.get("calibration")
    if not isinstance(calibration, Mapping):
        result["calibrationStatus"] = "uncalibrated"
        result["calibrationFailureReason"] = "calibration metadata is missing"
        result.pop("calibrationFit", None)
        return result
    try:
        fit = fit_triangle_capacity(
            _frame_samples(calibration.get("samples", [])),
            result["targetFps"],
            calibration.get("capacityModel"),
        )
        result["measurementCapacityEstimate"] = fit
        if int(calibration.get("resolutionWidth", 0)) <= 0 or int(calibration.get("resolutionHeight", 0)) <= 0:
            raise ModelBudgetError("calibration resolution must be positive")
        camera_reference = str(calibration.get("cameraReference", "")).strip()
        scene_mix = str(calibration.get("sceneMix", "")).strip()
        if (not camera_reference or not scene_mix or
                camera_reference.lower().startswith(("pending", "unknown", "unverified")) or
                scene_mix.lower().startswith(("pending", "unknown", "unverified"))):
            raise ModelBudgetError("cameraReference and sceneMix are required")
        if int(calibration.get("maximumVisibleInstances", 0)) <= 0:
            raise ModelBudgetError("maximumVisibleInstances must be positive")
        capacity_model = calibration.get("capacityModel")
        if (isinstance(capacity_model, Mapping) and
                capacity_model.get("method") == "conservative-measured-incremental-envelope-v1"):
            context = calibration.get("measurementContext")
            if not isinstance(context, Mapping):
                raise ModelBudgetError("measured-envelope capacity requires a runtime measurementContext")
            if context.get("capacityEligible") is not True:
                result["calibrationStatus"] = "uncalibrated"
                result["calibrationFailureReason"] = str(
                    context.get("capacityIneligibilityReason")
                    or "measured-envelope samples are evidence-only until their load pair is controlled"
                )
                return result
    except (ModelBudgetError, TypeError, ValueError) as exc:
        result["calibrationStatus"] = "uncalibrated"
        result["calibrationFailureReason"] = str(exc)
        return result
    result["calibrationStatus"] = "calibrated"
    result.pop("calibrationFailureReason", None)
    result["calibrationFit"] = fit
    try:
        report = build_budget_report(result)
    except (ModelBudgetError, TypeError, ValueError) as exc:
        result["calibrationStatus"] = "uncalibrated"
        result["calibrationFailureReason"] = str(exc)
        result.pop("calibrationFit", None)
        result.pop("budgetReport", None)
        return result
    result["budgetReport"] = report
    return result


def load_budget_profile(path: Path) -> dict[str, Any]:
    try:
        with path.open("r", encoding="utf-8-sig") as stream:
            value = json.load(stream)
    except (OSError, json.JSONDecodeError) as exc:
        raise ModelBudgetError(f"cannot read model budget profile: {path}") from exc
    if not isinstance(value, dict):
        raise ModelBudgetError("model budget profile root must be an object")
    return value
