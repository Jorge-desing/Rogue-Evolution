"""Build the Overlord HQ factory's profile-driven LOD FBX.

This is the normal Blender-native replay path for
``overlord_hq_factory_hardflow_recipe.py``. Hardflow is not imported here.
The authoring export stops unless the current measured model-budget profile
contains a calibrated demand and all generated LODs fit that allocation.
"""

from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys

import bpy

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import RogueModelKit as rmk  # noqa: E402
from overlord_hq_factory_hardflow_recipe import (  # noqa: E402
    apply_overlord_hq_factory_details,
)

ASSET_ID = "OverlordCommandHQFactory"
SOURCE_BLEND = ROOT / "content/source-art/overlordcommandhqfactory.blend"
MODEL_DIR = ROOT / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models"
OUTPUT_FBX = MODEL_DIR / f"{ASSET_ID}_LODs.fbx"


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _asset_meshes() -> list[bpy.types.Object]:
    return [
        obj for obj in bpy.context.scene.objects
        if obj.type == "MESH" and obj.name.startswith(ASSET_ID)
        and "_COLLIDER_" not in obj.name
    ]


def _write_report(report: dict[str, object], label: str) -> Path:
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    report_dir = ROOT / "artifacts/model-budget-reports" / f"{ASSET_ID}-{label}-{stamp}"
    report_dir.mkdir(parents=True, exist_ok=False)
    path = report_dir / "build-report.json"
    path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return path


def main() -> None:
    if not SOURCE_BLEND.is_file():
        raise FileNotFoundError(SOURCE_BLEND)
    source_hash = _sha256(SOURCE_BLEND)
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE_BLEND), load_ui=False)
    gunmetal = bpy.data.materials.get(f"{ASSET_ID}_Gunmetal")
    if gunmetal is None:
        raise ValueError(f"{ASSET_ID}: source blend is missing its gunmetal material")

    recipe = apply_overlord_hq_factory_details(
        list(bpy.context.scene.objects),
        gunmetal_material=gunmetal,
    )
    meshes = _asset_meshes()
    source_faces = sum(len(obj.data.polygons) for obj in meshes)
    source_triangles = rmk.count_mesh_triangles(meshes)
    report: dict[str, object] = {
        "schemaVersion": 1,
        "assetId": ASSET_ID,
        "sourceBlend": str(SOURCE_BLEND.relative_to(ROOT)),
        "sourceSha256": source_hash,
        "recipe": recipe,
        "sourceBlenderFaces": source_faces,
        "sourceTriangles": source_triangles,
        "outputFbx": str(OUTPUT_FBX.relative_to(ROOT)),
        "hardflowRequired": False,
    }

    try:
        asset_budget = rmk.resolve_geometry_budget(ASSET_ID)
    except ValueError as exc:
        report.update({
            "calibrationStatus": "blocked",
            "verdict": "BLOCKED_UNCALIBRATED",
            "reason": str(exc),
            "outputWritten": False,
        })
        report_path = _write_report(report, "blocked")
        raise RuntimeError(f"{exc}. Diagnostic report: {report_path}") from exc

    try:
        ratios = rmk.lod_ratios_from_budget(asset_budget, source_triangles)
        lods = rmk.generate_lod_chain(meshes, prefix=ASSET_ID, lod_levels=ratios)
        budget_summary = rmk.summarize_lod_budget(asset_budget, lods)
        report["calibrationStatus"] = "calibrated"
        report["lodRatios"] = [{"lod": name, "ratio": ratio} for name, ratio in ratios]
        report["geometryBudget"] = budget_summary
        if budget_summary["verdict"] != "PASS":
            report.update({"verdict": "BLOCKED_OVER_CAPACITY", "outputWritten": False})
            report_path = _write_report(report, "over-capacity")
            raise RuntimeError(f"{ASSET_ID}: generated LOD chain exceeds its measured allocation; report: {report_path}")

        lod_objects = [obj for level in ("LOD0", "LOD1", "LOD2") for obj in lods[level]]
        if not lod_objects:
            raise ValueError(f"{ASSET_ID}: generated LOD chain is empty")

        candidate_dir = ROOT / "artifacts" / "overlord-hq-factory-build" / datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
        candidate_dir.mkdir(parents=True, exist_ok=False)
        candidate_base = candidate_dir / f"{ASSET_ID}.fbx"
        rmk.export_unity_fbx(candidate_base, lod_objects, include_empties=False)
        candidate_lods = candidate_dir / f"{ASSET_ID}_LODs.fbx"
        if not candidate_lods.is_file():
            raise IOError(f"{ASSET_ID}: profile LOD exporter did not create {candidate_lods}")
        candidate_hash = _sha256(candidate_lods)
        report["candidateLodFbx"] = str(candidate_lods.relative_to(ROOT))
        report["candidateLodSha256"] = candidate_hash

        # The budget and all three LODs are checked before the tracked FBX path
        # is touched. Back up any previous output, copy to a sibling temp file,
        # verify bytes, then replace the destination atomically.
        if OUTPUT_FBX.exists():
            backup_dir = ROOT / "artifacts/model-budget-reports" / f"{ASSET_ID}-pre-export-{datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')}"
            backup_dir.mkdir(parents=True, exist_ok=False)
            backup = backup_dir / OUTPUT_FBX.name
            backup.write_bytes(OUTPUT_FBX.read_bytes())
            if _sha256(backup) != _sha256(OUTPUT_FBX):
                raise IOError(f"{ASSET_ID}: pre-export FBX backup hash mismatch")
            report["previousFbxBackup"] = {
                "path": str(backup.relative_to(ROOT)),
                "sha256": _sha256(backup),
            }

        OUTPUT_FBX.parent.mkdir(parents=True, exist_ok=True)
        temporary_output = OUTPUT_FBX.with_name(OUTPUT_FBX.name + ".tmp")
        if temporary_output.exists():
            raise FileExistsError(f"Refusing to replace stale temporary output: {temporary_output}")
        shutil.copy2(candidate_lods, temporary_output)
        if _sha256(temporary_output) != candidate_hash:
            temporary_output.unlink(missing_ok=True)
            raise IOError(f"{ASSET_ID}: staged LOD FBX hash differs from candidate")
        os.replace(temporary_output, OUTPUT_FBX)
        report.update({
            "verdict": "PASS",
            "outputWritten": True,
            "outputSha256": _sha256(OUTPUT_FBX),
        })
        report_path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    except Exception:
        raise

    if _sha256(SOURCE_BLEND) != source_hash:
        raise RuntimeError(f"{ASSET_ID}: source blend changed while building its FBX")
    print(f"[model-budget] {ASSET_ID}: {report_path}")


if __name__ == "__main__":
    main()
