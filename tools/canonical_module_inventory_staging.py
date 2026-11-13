"""Isolated staging for canonical Level 2 FBX inventory.

This is a bootstrap path only. It writes candidate FBXs and a candidate
inventory under ignored ``artifacts/``; it never changes the authored profile,
canonical source blend, production FBXs, prefabs, or bundles. Measurements can
be verified separately but do not restrict authoring or the candidate geometry.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import re
from typing import Any, Callable, Mapping, Sequence

from tools.model_budget_policy import (
    DEFAULT_TARGET_FPS,
    FrameSample,
    ModelBudgetError,
    fit_triangle_capacity,
)
from tools.profile_model_budgets import _read_samples


INVENTORY_SCHEMA = "rogue-command/canonical-level2-inventory-candidates/v1"
CAPACITY_MODEL = "conservative-measured-incremental-envelope-v1"
_ASSET_ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_-]*$")
_SHA256 = re.compile(r"^[0-9a-f]{64}$")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _rooted_path(repo_root: Path, path: Path) -> Path:
    raw = Path(path)
    return (raw if raw.is_absolute() else repo_root / raw).resolve()


def resolve_artifact_stage_root(repo_root: Path, requested: Path) -> Path:
    """Validate a new, non-existing stage directory under the ignored artifacts tree."""
    root = Path(repo_root).resolve()
    artifact_root = (root / "artifacts").resolve()
    candidate = _rooted_path(root, requested)
    if candidate == artifact_root:
        raise ModelBudgetError("inventory staging requires a dedicated child directory under artifacts/")
    try:
        candidate.relative_to(artifact_root)
    except ValueError as exc:
        raise ModelBudgetError("inventory candidates may be written only under repository artifacts/") from exc
    if candidate.exists() or candidate.is_symlink():
        raise FileExistsError(f"inventory staging directory already exists; choose a new path: {candidate}")
    return candidate


@dataclass(frozen=True)
class InventoryCapacityGate:
    repo_root: Path
    staging_root: Path
    profile_path: Path
    evidence_bundle_path: Path
    profile_sha256: str
    evidence_bundle_sha256: str
    report_sources: tuple[tuple[Path, str], ...]
    target_fps: float
    scene_triangle_capacity: int
    maximum_visible_instances: int
    fit: Mapping[str, Any]
    measurement_context: Mapping[str, Any]
    calibration_status_at_stage: str

    def to_manifest(self) -> dict[str, Any]:
        return {
            "gate": "verified-runtime-capacity-for-inventory-staging",
            "passed": True,
            "calibrationStatusAtStage": self.calibration_status_at_stage,
            "targetFps": self.target_fps,
            "sceneTriangleCapacity": self.scene_triangle_capacity,
            "maximumVisibleInstances": self.maximum_visible_instances,
            "capacityFit": dict(self.fit),
            "profilePath": self.profile_path.relative_to(self.repo_root).as_posix()
            if self.profile_path.is_relative_to(self.repo_root) else self.profile_path.name,
            "profileSha256": self.profile_sha256,
            "evidenceBundlePath": self.evidence_bundle_path.relative_to(self.repo_root).as_posix()
            if self.evidence_bundle_path.is_relative_to(self.repo_root) else self.evidence_bundle_path.name,
            "evidenceBundleSha256": self.evidence_bundle_sha256,
            "verifiedReportSha256": [report_sha for _, report_sha in self.report_sources],
            "measurementContext": dict(self.measurement_context),
            "scope": "candidate inventory export only; this is not an export/build/promotion budget",
        }


@dataclass(frozen=True)
class InventoryStageContext:
    repo_root: Path
    staging_root: Path

    def to_manifest(self) -> dict[str, Any]:
        return {
            "gate": "no-calibration-limits",
            "geometryBudgetStatus": "UNCALIBRATED_USER_AUTHORIZED",
            "geometryLimitsApplied": False,
            "scope": "candidate inventory only; no performance calibration is claimed",
        }


def validate_measured_capacity_gate(
    repo_root: Path,
    staging_root: Path,
    profile_path: Path,
    evidence_bundle_path: Path,
) -> InventoryCapacityGate:
    """Verify raw runtime evidence and its exact ingestion into the profile.

    The profile may still say ``uncalibrated`` because its Level 2 demands do
    not exist yet. This limited gate validates only the measured capacity
    evidence needed to produce isolated candidate inventory files. It never
    marks the profile calibrated and cannot authorize a normal model build.
    """
    root = Path(repo_root).resolve()
    stage = resolve_artifact_stage_root(root, staging_root)
    profile_file = _rooted_path(root, profile_path)
    evidence_file = _rooted_path(root, evidence_bundle_path)
    if not profile_file.is_file():
        raise ModelBudgetError(f"model budget profile does not exist: {profile_file}")
    if not evidence_file.is_file():
        raise ModelBudgetError(f"runtime evidence bundle does not exist: {evidence_file}")

    profile_bytes = profile_file.read_bytes()
    evidence_bytes = evidence_file.read_bytes()
    try:
        profile = json.loads(profile_bytes.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ModelBudgetError("cannot read model budget profile JSON") from exc
    if not isinstance(profile, dict):
        raise ModelBudgetError("model budget profile root must be an object")
    try:
        profile_schema = int(profile.get("schemaVersion", 0))
    except (TypeError, ValueError) as exc:
        raise ModelBudgetError("inventory staging requires model budget profile schemaVersion 1") from exc
    if profile_schema != 1:
        raise ModelBudgetError("inventory staging requires model budget profile schemaVersion 1")
    calibration = profile.get("calibration")
    if not isinstance(calibration, Mapping):
        raise ModelBudgetError("calibration metadata is required for inventory staging")

    verified_samples = _read_samples(evidence_file)
    authored_samples = calibration.get("samples")
    if (not isinstance(authored_samples, list) or
            json.dumps(authored_samples, sort_keys=True, separators=(",", ":")) !=
            json.dumps(verified_samples, sort_keys=True, separators=(",", ":"))):
        raise ModelBudgetError(
            "profile calibration.samples must exactly match the SHA-256-verified runtime evidence bundle"
        )

    context = calibration.get("measurementContext")
    if not isinstance(context, Mapping) or context.get("capacityEligible") is not True:
        reason = context.get("capacityIneligibilityReason") if isinstance(context, Mapping) else None
        raise ModelBudgetError(
            "runtime measurement context is not eligible for capacity staging"
            + (f": {reason}" if reason else "")
        )
    scene_reference = context.get("sceneReference")
    if (not isinstance(scene_reference, Mapping) or scene_reference.get("unitySceneName") != "Game" or
            calibration.get("sceneReference") != scene_reference):
        raise ModelBudgetError("profile sceneReference must match a named Game scene in runtime evidence")

    camera_context = context.get("cameraReference")
    if not isinstance(camera_context, Mapping):
        raise ModelBudgetError("runtime evidence must bind a reference camera")
    try:
        target_fps = float(profile.get("targetFps", 0.0))
        width = int(calibration["resolutionWidth"])
        height = int(calibration["resolutionHeight"])
        maximum_visible_instances = int(calibration["maximumVisibleInstances"])
        context_maximum = int(context["maximumVisibleInstances"])
    except (KeyError, TypeError, ValueError) as exc:
        raise ModelBudgetError("profile FPS, resolution, or visible-instance limit is invalid") from exc
    camera_description = str(calibration.get("cameraReference", "")).strip()
    scene_mix = str(calibration.get("sceneMix", "")).strip()
    context_scene_mix = str(context.get("sceneMix", "")).strip()
    if (not math.isfinite(target_fps) or abs(target_fps - DEFAULT_TARGET_FPS) > 0.1 or
            width <= 0 or height <= 0 or maximum_visible_instances <= 0):
        raise ModelBudgetError("inventory staging requires a positive 60-FPS profile and visible-instance limit")
    if (not camera_description or str(camera_context.get("name", "")) not in camera_description or
            f"{width}x{height}" not in camera_description):
        raise ModelBudgetError("profile cameraReference must name the evidence camera and resolution")
    if not scene_mix or scene_mix != context_scene_mix:
        raise ModelBudgetError("profile calibration.sceneMix must match the runtime evidence context")
    if maximum_visible_instances != context_maximum:
        raise ModelBudgetError("profile maximumVisibleInstances differs from runtime evidence context")
    if any(
        sample.get("scene") != "Game" or
        sample.get("pixelWidth") != width or
        sample.get("pixelHeight") != height or
        sample.get("measurementContext") != context
        for sample in verified_samples
    ):
        raise ModelBudgetError("verified samples do not share the profile's Game scene, camera context, and resolution")

    capacity_model = calibration.get("capacityModel")
    if not isinstance(capacity_model, Mapping) or capacity_model.get("method") != CAPACITY_MODEL:
        raise ModelBudgetError(
            f"inventory staging requires the measured {CAPACITY_MODEL} capacity model"
        )
    frame_samples = [
        FrameSample(
            triangles=int(sample["triangles"]),
            p95_unscaled_frame_interval_ms=float(sample["p95UnscaledFrameIntervalMs"]),
        )
        for sample in verified_samples
    ]
    fit = fit_triangle_capacity(frame_samples, target_fps, capacity_model)
    capacity = int(fit["sceneTriangleCapacity"])
    if capacity <= 0 or fit.get("extrapolationUsed") is not False:
        raise ModelBudgetError("measured runtime evidence produced no bounded staging capacity")

    report_sources: list[tuple[Path, str]] = []
    for sample in verified_samples:
        report_name = str(sample.get("evidenceReportPath", ""))
        report_sha = str(sample.get("evidenceReportSha256", "")).lower()
        if not report_name or not _SHA256.fullmatch(report_sha):
            raise ModelBudgetError("each runtime sample must retain its raw report path and SHA-256")
        report_path = (evidence_file.parent / report_name).resolve()
        if report_path.parent != evidence_file.parent.resolve():
            raise ModelBudgetError("runtime report path escaped its evidence bundle directory")
        if not report_path.is_file() or sha256_file(report_path) != report_sha:
            raise ModelBudgetError(f"runtime report changed after evidence verification: {report_name}")
        report_sources.append((report_path, report_sha))

    return InventoryCapacityGate(
        repo_root=root,
        staging_root=stage,
        profile_path=profile_file,
        evidence_bundle_path=evidence_file,
        profile_sha256=hashlib.sha256(profile_bytes).hexdigest(),
        evidence_bundle_sha256=hashlib.sha256(evidence_bytes).hexdigest(),
        report_sources=tuple(report_sources),
        target_fps=target_fps,
        scene_triangle_capacity=capacity,
        maximum_visible_instances=maximum_visible_instances,
        fit=fit,
        measurement_context=context,
        calibration_status_at_stage=str(profile.get("calibrationStatus", "unknown")),
    )


def verify_gate_unchanged(gate: InventoryCapacityGate | InventoryStageContext) -> None:
    if isinstance(gate, InventoryStageContext):
        return
    if sha256_file(gate.profile_path) != gate.profile_sha256:
        raise ModelBudgetError("model budget profile changed during inventory staging")
    if sha256_file(gate.evidence_bundle_path) != gate.evidence_bundle_sha256:
        raise ModelBudgetError("runtime evidence bundle changed during inventory staging")
    for report_path, expected_sha in gate.report_sources:
        if not report_path.is_file() or sha256_file(report_path) != expected_sha:
            raise ModelBudgetError(f"verified runtime report changed during inventory staging: {report_path.name}")


def validate_candidate_triangle_count(triangles: int, gate: InventoryCapacityGate | InventoryStageContext, asset_id: str) -> None:
    if triangles <= 0:
        raise ModelBudgetError(f"{asset_id}: candidate FBX must contain positive Unity triangles")


def validate_candidate_export_path(path: Path, gate: InventoryCapacityGate | InventoryStageContext) -> Path:
    candidate = Path(path).resolve()
    try:
        candidate.relative_to(gate.staging_root)
    except ValueError as exc:
        raise ModelBudgetError("candidate FBX export path must remain under its authorized staging directory") from exc
    try:
        candidate.relative_to((gate.repo_root / "artifacts").resolve())
    except ValueError as exc:
        raise ModelBudgetError("candidate FBX export path must remain under repository artifacts/") from exc
    if candidate.suffix.lower() != ".fbx":
        raise ModelBudgetError("candidate inventory export path must end in .fbx")
    if candidate.exists() or candidate.is_symlink():
        raise FileExistsError(f"candidate FBX already exists: {candidate}")
    return candidate


def _atomic_write_manifest(path: Path, payload: Mapping[str, Any]) -> None:
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    temporary.replace(path)


def _candidate_inventory_row(
    entry: Mapping[str, Any],
    candidate_path: Path,
    stage_root: Path,
    repo_root: Path,
    inspected: Mapping[str, Any],
) -> dict[str, Any]:
    asset_id = str(entry.get("moduleName", ""))
    if not _ASSET_ID.fullmatch(asset_id):
        raise ModelBudgetError(f"invalid canonical module assetId for inventory staging: {asset_id!r}")
    if str(inspected.get("assetId", "")) != asset_id:
        raise ModelBudgetError(f"{asset_id}: imported candidate FBX identity does not match its module id")
    if not candidate_path.is_file() or candidate_path.stat().st_size <= 0:
        raise ModelBudgetError(f"{asset_id}: candidate FBX was not written or is empty")
    candidate_sha = sha256_file(candidate_path)
    reported_sha = str(inspected.get("sha256", "")).lower()
    if not _SHA256.fullmatch(reported_sha) or reported_sha != candidate_sha:
        raise ModelBudgetError(f"{asset_id}: FBX inspector SHA-256 differs from the staged file bytes")
    try:
        faces = int(inspected["sourceFaces"])
        triangles = int(inspected["sourceTriangles"])
        mesh_objects = int(inspected["meshObjects"])
        vertices = int(inspected["vertices"])
    except (KeyError, TypeError, ValueError) as exc:
        raise ModelBudgetError(f"{asset_id}: FBX inspector omitted geometry inventory counts") from exc
    if min(faces, triangles, mesh_objects, vertices) <= 0:
        raise ModelBudgetError(f"{asset_id}: candidate FBX geometry counts must be positive")
    try:
        candidate_path.resolve().relative_to(stage_root.resolve())
        relative_path = candidate_path.resolve().relative_to(repo_root.resolve()).as_posix()
    except ValueError as exc:
        raise ModelBudgetError(f"{asset_id}: staged FBX path escaped repository artifacts/") from exc
    return {
        "assetId": asset_id,
        "assetClass": "visual-module",
        "sourcePath": relative_path,
        "candidateFbxPath": relative_path,
        "sourceFaces": faces,
        "sourceTriangles": triangles,
        "meshObjects": mesh_objects,
        "vertices": vertices,
        "sha256": candidate_sha,
        "variantId": str(entry.get("variantId", "")),
        "unitId": str(entry.get("unitId", "")),
        "slotId": str(entry.get("slotId", "")),
        "preset": str(entry.get("preset", "")),
        "geometryPreset": str(entry.get("geometryPreset", "")),
        "geometryLayout": str(entry.get("geometryLayout", "")),
        "visualEngineeringRole": str(entry.get("visualEngineeringRole", "")),
    }


def stage_candidate_inventory(
    *,
    repo_root: Path,
    staging_root: Path,
    profile_path: Path,
    evidence_bundle_path: Path | None,
    entries: Sequence[Mapping[str, Any]],
    build_candidate: Callable[[Mapping[str, Any], Path, InventoryCapacityGate | InventoryStageContext], Mapping[str, Any]],
) -> dict[str, Any]:
    """Replay and inspect candidates under artifacts without a calibration gate."""
    if not entries:
        raise ModelBudgetError("canonical Level 2 inventory staging requires at least one module entry")
    ids = [str(entry.get("moduleName", "")) for entry in entries]
    if any(not _ASSET_ID.fullmatch(asset_id) for asset_id in ids) or len(ids) != len(set(ids)):
        raise ModelBudgetError("canonical module asset ids must be valid and unique before staging")

    root = Path(repo_root).resolve()
    gate = InventoryStageContext(root, resolve_artifact_stage_root(root, staging_root))
    stage_root = gate.staging_root
    stage_root.mkdir(parents=True, exist_ok=False)
    manifest: dict[str, Any] = {
        "schema": INVENTORY_SCHEMA,
        "status": "in_progress",
        "promotionAllowed": False,
        "normalBuildAllowed": True,
        "profileUpdated": False,
        "finalCalibratedProfileRequired": False,
        "createdUtc": datetime.now(timezone.utc).isoformat(),
        "moduleCountExpected": len(entries),
        "capacityGate": gate.to_manifest(),
        "measurementEvidenceUsed": False,
        "inventoryMethod": "Blender FBX import for faces/triangles/vertices; SHA-256 over exact candidate FBX bytes",
        "assetInventory": {"fileCount": 0, "assets": []},
    }
    manifest_path = stage_root / "candidate-inventory.json"
    _atomic_write_manifest(manifest_path, manifest)
    rows: list[dict[str, Any]] = []
    try:
        for index, (entry, asset_id) in enumerate(zip(entries, ids), start=1):
            verify_gate_unchanged(gate)
            candidate_path = stage_root / "fbx" / f"{asset_id}.fbx"
            inspected = build_candidate(entry, candidate_path, gate)
            validate_candidate_triangle_count(int(inspected.get("sourceTriangles", 0)), gate, asset_id)
            row = _candidate_inventory_row(entry, candidate_path, stage_root, gate.repo_root, inspected)
            rows.append(row)
            manifest["assetInventory"] = {"fileCount": len(rows), "assets": list(rows)}
            manifest["currentAsset"] = {"index": index, "assetId": asset_id}
            _atomic_write_manifest(manifest_path, manifest)
        verify_gate_unchanged(gate)
        manifest["status"] = "candidate-inventory-ready"
        manifest["assetInventory"] = {"fileCount": len(rows), "assets": rows}
        manifest.pop("currentAsset", None)
        manifest["completionNote"] = (
            "Candidate inventory only. Review the counts, geometry and final production FBXs "
            "before Unity prefab or bundle work; no performance calibration is implied."
        )
        _atomic_write_manifest(manifest_path, manifest)
        return manifest
    except Exception as exc:
        manifest["status"] = "failed"
        manifest["failure"] = f"{type(exc).__name__}: {exc}"
        manifest["assetInventory"] = {"fileCount": len(rows), "assets": rows}
        _atomic_write_manifest(manifest_path, manifest)
        raise
