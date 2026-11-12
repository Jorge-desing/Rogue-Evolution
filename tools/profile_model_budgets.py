"""Compile an authoring profile into calibrated scene and per-asset LOD budgets."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
import sys

from tools.model_budget_policy import (
    ModelBudgetError,
    calibrate_profile,
    load_budget_profile,
)


def _read_samples(path: Path) -> list[dict[str, object]]:
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ModelBudgetError(f"cannot read calibration sample file: {path}") from exc
    if isinstance(value, dict) and value.get("schema") == "rogue-command/runtime-geometry-sample/v3":
        reports = [value]
    elif isinstance(value, dict) and "reports" in value:
        reports = value.get("reports")
        if not isinstance(reports, list):
            raise ModelBudgetError("runtime capture input 'reports' must be an array")
    elif isinstance(value, list):
        reports = value
    else:
        raise ModelBudgetError("calibration input must be a schema-v3 runtime report or a {reports: [...]} object")
    if not reports or not all(isinstance(report, dict) for report in reports):
        raise ModelBudgetError("runtime capture reports must be a nonempty array of objects")
    rows = [_sample_from_runtime_capture(report, path) for report in reports]
    capture_keys = {
        (
            row["scene"], row["camera"], row["pixelWidth"], row["pixelHeight"],
            row["unityVersion"], row["cameraSettings"],
        )
        for row in rows
    }
    if len(capture_keys) != 1:
        raise ModelBudgetError(f"{path}: runtime reports must use one scene, camera pose, Unity version, and resolution")
    return rows


def _sample_from_runtime_capture(report: object, path: Path) -> dict[str, object]:
    """Validate one complete, unpaused runtime capture and extract its fit row."""
    if not isinstance(report, dict) or report.get("schema") != "rogue-command/runtime-geometry-sample/v3":
        raise ModelBudgetError(f"{path}: runtime report schema must be rogue-command/runtime-geometry-sample/v3")
    capture = report.get("capture")
    camera = report.get("camera")
    sample = report.get("fitSample")
    if not isinstance(capture, dict) or not isinstance(camera, dict) or not isinstance(sample, dict):
        raise ModelBudgetError(f"{path}: runtime report is missing capture, camera, or fitSample metadata")
    if report.get("status") != "complete" or sample.get("calibrationReady") is not True:
        raise ModelBudgetError(f"{path}: runtime capture is incomplete or not eligible for calibration")
    if not report.get("captureStartedUtc") or not report.get("unityVersion"):
        raise ModelBudgetError(f"{path}: runtime report must identify capture time and Unity version")
    if capture.get("frameIntervalMetric") != "UnityEngine.Time.unscaledDeltaTime" or capture.get("frameIntervalUnit") != "ms":
        raise ModelBudgetError(f"{path}: runtime capture must measure unscaled frame intervals in milliseconds")
    if capture.get("geometryMetric") != "cameraFrustumMeshTriangles":
        raise ModelBudgetError(f"{path}: runtime capture must count camera-frustum mesh triangles")
    try:
        sample_count = int(capture["sampleCount"])
        requested_frames = int(capture["requestedFrames"])
        time_scale = float(capture["timeScaleAtStart"])
        pixel_width = int(camera["pixelWidth"])
        pixel_height = int(camera["pixelHeight"])
        triangles = int(sample["triangles"])
        interval = float(sample["p95UnscaledFrameIntervalMs"])
        field_of_view = float(camera["fieldOfView"])
        orthographic_size = float(camera["orthographicSize"])
        camera_aspect = float(camera["aspect"])
        camera_culling_mask = int(camera["cullingMask"])
        camera_position = tuple(float(component) for component in camera["position"])
        camera_rotation = tuple(float(component) for component in camera["rotationEuler"])
    except (KeyError, TypeError, ValueError) as exc:
        raise ModelBudgetError(f"{path}: runtime capture metadata is invalid") from exc
    frame_intervals = report.get("frameIntervalsMs")
    if sample_count < 120 or sample_count != requested_frames or not isinstance(frame_intervals, list) or len(frame_intervals) != sample_count:
        raise ModelBudgetError(f"{path}: runtime capture needs the full requested set of at least 120 rendered frames")
    try:
        intervals = [float(value) for value in frame_intervals]
    except (TypeError, ValueError) as exc:
        raise ModelBudgetError(f"{path}: runtime frame interval samples are invalid") from exc
    if any(not math.isfinite(value) or value <= 0.0 for value in intervals):
        raise ModelBudgetError(f"{path}: runtime frame intervals must be finite and positive")
    intervals.sort()
    measured_p95 = intervals[max(0, math.ceil(0.95 * sample_count) - 1)]
    if not math.isclose(measured_p95, interval, rel_tol=1e-6, abs_tol=0.001):
        raise ModelBudgetError(f"{path}: fitSample p95 does not match the captured frame intervals")
    if not math.isfinite(time_scale) or time_scale <= 0.0:
        raise ModelBudgetError(f"{path}: paused or timeScale-zero captures cannot calibrate performance")
    if (capture.get("timeScaleStayedPositive") is not True or capture.get("cameraStable") is not True or
            capture.get("geometryLoadStable") is not True):
        raise ModelBudgetError(f"{path}: capture is not eligible because time scale, camera, or geometry load changed")
    try:
        stability_checks = int(capture["geometryStabilityChecks"])
        geometry_signature = int(capture["geometrySignature"])
        candidate_renderers = int(capture["candidateMeshRenderers"])
        counted_renderers = int(capture["countedMeshRenderers"])
        unsupported_renderers = int(capture["unsupportedMeshRenderers"])
    except (KeyError, TypeError, ValueError) as exc:
        raise ModelBudgetError(f"{path}: runtime geometry stability check metadata is invalid") from exc
    if stability_checks < math.ceil(sample_count / 30):
        raise ModelBudgetError(f"{path}: runtime capture has too few periodic geometry stability checks")
    if geometry_signature == 0:
        raise ModelBudgetError(f"{path}: runtime capture is missing its renderer-set stability signature")
    if (candidate_renderers <= 0 or counted_renderers <= 0 or unsupported_renderers < 0 or
            counted_renderers + unsupported_renderers != candidate_renderers):
        raise ModelBudgetError(f"{path}: runtime geometry census does not reconcile candidate, counted, and unsupported renderers")
    if camera.get("cameraType") != "Game" or camera.get("usesRenderTexture") is not False:
        raise ModelBudgetError(f"{path}: runtime capture must use a Game camera rendering to the display")
    if (pixel_width <= 0 or pixel_height <= 0 or not report.get("scene") or not camera.get("name") or
            len(camera_position) != 3 or len(camera_rotation) != 3):
        raise ModelBudgetError(f"{path}: runtime capture needs a scene, camera pose, and positive resolution")
    if (triangles <= 0 or not math.isfinite(field_of_view) or not math.isfinite(orthographic_size) or
            not math.isfinite(camera_aspect) or camera_aspect <= 0.0 or
            not all(math.isfinite(value) for value in camera_position + camera_rotation) or not interval > 0.0):
        raise ModelBudgetError(f"{path}: runtime capture needs positive camera-frustum triangles and frame interval")
    summary = report.get("summary")
    try:
        summary_triangles = int(summary["cameraFrustumMeshTriangles"]) if isinstance(summary, dict) else -1
        counted_renderers = int(capture["countedMeshRenderers"])
    except (KeyError, TypeError, ValueError) as exc:
        raise ModelBudgetError(f"{path}: runtime geometry census metadata is invalid") from exc
    if summary_triangles != triangles:
        raise ModelBudgetError(f"{path}: fitSample triangles do not match the runtime geometry census")
    if counted_renderers <= 0:
        raise ModelBudgetError(f"{path}: runtime geometry census must count at least one mesh renderer")
    if not isinstance(camera.get("orthographic"), bool):
        raise ModelBudgetError(f"{path}: camera projection mode is invalid")
    camera_settings = (
        field_of_view,
        bool(camera.get("orthographic")),
        orthographic_size,
        camera_aspect,
        camera_culling_mask,
        camera.get("cameraType"),
        camera.get("usesRenderTexture"),
        camera_position,
        camera_rotation,
    )
    return {
        "triangles": triangles,
        "p95UnscaledFrameIntervalMs": interval,
        "captureStartedUtc": report.get("captureStartedUtc", ""),
        "scene": report.get("scene", ""),
        "camera": camera.get("name", ""),
        "cameraSettings": camera_settings,
        "pixelWidth": pixel_width,
        "pixelHeight": pixel_height,
        "unityVersion": report.get("unityVersion", ""),
        "timeScaleAtStart": time_scale,
        "sampleCount": sample_count,
        "measurementSource": "runtime-model-budget-probe-v3",
    }


def compile_profile(profile_path: Path, samples_path: Path | None = None) -> dict[str, object]:
    profile = load_budget_profile(profile_path)
    if samples_path is not None:
        calibration = profile.get("calibration")
        if not isinstance(calibration, dict):
            raise ModelBudgetError("profile calibration metadata must be an object before samples can be added")
        samples = _read_samples(samples_path)
        try:
            width = int(calibration["resolutionWidth"])
            height = int(calibration["resolutionHeight"])
        except (KeyError, TypeError, ValueError) as exc:
            raise ModelBudgetError("profile resolution must be set before runtime reports can be ingested") from exc
        if any(sample["pixelWidth"] != width or sample["pixelHeight"] != height for sample in samples):
            raise ModelBudgetError("runtime capture resolution does not match the authoring profile")
        for field in ("cameraReference", "sceneMix"):
            description = str(calibration.get(field, "")).strip()
            if not description or description.lower().startswith(("pending", "unknown", "unverified")):
                raise ModelBudgetError(f"profile calibration.{field} must be authored before runtime reports can be ingested")
        calibration["samples"] = samples
    compiled = calibrate_profile(profile)
    return compiled


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--profile", type=Path, default=Path("content/source-art/model-budget-profile.json"))
    parser.add_argument("--samples", type=Path, help="Complete schema-v3 runtime report or JSON object {reports:[report,...]}")
    parser.add_argument("--output", type=Path, required=True, help="Compiled profile JSON output path")
    args = parser.parse_args(argv)
    try:
        compiled = compile_profile(args.profile, args.samples)
    except ModelBudgetError as exc:
        print(f"model-budget profile error: {exc}", file=sys.stderr)
        return 2
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(compiled, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    status = str(compiled.get("calibrationStatus", "uncalibrated"))
    if status != "calibrated":
        print(f"calibrationStatus={status}; reason={compiled.get('calibrationFailureReason', 'not measured')}")
        return 2
    report = compiled["budgetReport"]
    print(json.dumps({
        "calibrationStatus": status,
        "targetFps": compiled["targetFps"],
        "targetFrameTimeMs": report["targetFrameTimeMs"],
        "sceneCount": len(report["scenes"]),
        "sceneBudgetPassed": all(scene["sceneBudgetPassed"] for scene in report["scenes"]),
        "output": str(args.output),
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
