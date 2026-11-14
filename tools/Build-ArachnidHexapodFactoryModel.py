"""Build the Arachnid Hexapod factory's authored LOD FBX.

Geometry counts are reported without imposing a calibration-derived limit.
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

ASSET_ID = "ArachnidHexapodFactory"
SOURCE_BLEND = ROOT / "content/source-art/arachnidhexapodfactory.blend"
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
        if obj.type == "MESH"
        and "_COLLIDER" not in obj.name.upper()
        and not obj.name.lower().startswith("preview")
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

    meshes = _asset_meshes()
    source_faces = sum(len(obj.data.polygons) for obj in meshes)
    source_triangles = rmk.count_mesh_triangles(meshes)
    asset_budget = rmk.resolve_geometry_budget(ASSET_ID)

    report: dict[str, object] = {
        "schemaVersion": 1,
        "assetId": ASSET_ID,
        "sourceBlend": str(SOURCE_BLEND.relative_to(ROOT)),
        "sourceSha256": source_hash,
        "sourceBlenderFaces": source_faces,
        "sourceTriangles": source_triangles,
        "outputFbx": str(OUTPUT_FBX.relative_to(ROOT)),
        "calibrationStatus": "uncalibrated",
        "geometryBudgetStatus": "UNCALIBRATED_USER_AUTHORIZED",
    }

    batched = rmk.batch_and_join_by_material(
        meshes,
        prefix=ASSET_ID,
        smooth_by_angle=True,
        weighted_normals=True,
        unity_batch_names=True,
    )
    ratios = rmk.lod_ratios_from_budget(asset_budget, source_triangles)
    lods = rmk.generate_lod_chain(batched, prefix=ASSET_ID, lod_levels=ratios, lod_name_first=True)
    report["lodRatios"] = [{"lod": name, "ratio": ratio} for name, ratio in ratios]
    report["geometryBudget"] = rmk.summarize_lod_budget(asset_budget, lods)

    lod_objects = [obj for level in ("LOD0", "LOD1", "LOD2") for obj in lods[level]]
    if not lod_objects:
        raise ValueError(f"{ASSET_ID}: generated LOD chain is empty")

    candidate_dir = ROOT / "artifacts" / "arachnid-factory-build" / datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    candidate_dir.mkdir(parents=True, exist_ok=False)
    candidate_lods = candidate_dir / f"{ASSET_ID}_LODs.fbx"
    rmk.export_unity_fbx(
        candidate_lods,
        lod_objects,
        include_empties=False,
        asset_id=ASSET_ID,
        prevalidated_budget=asset_budget,
    )
    if not candidate_lods.is_file():
        raise IOError(f"{ASSET_ID}: exporter did not create {candidate_lods}")
    candidate_hash = _sha256(candidate_lods)

    OUTPUT_FBX.parent.mkdir(parents=True, exist_ok=True)
    tmp_out = OUTPUT_FBX.with_name(OUTPUT_FBX.name + ".tmp")
    shutil.copy2(candidate_lods, tmp_out)
    if _sha256(tmp_out) != candidate_hash:
        tmp_out.unlink(missing_ok=True)
        raise IOError(f"{ASSET_ID}: staged LOD FBX hash differs from candidate")
    os.replace(tmp_out, OUTPUT_FBX)

    report.update({
        "verdict": "PASS",
        "outputWritten": True,
        "outputSha256": _sha256(OUTPUT_FBX),
    })
    report_path = _write_report(report, "complete")
    if _sha256(SOURCE_BLEND) != source_hash:
        raise RuntimeError(f"{ASSET_ID}: source blend changed while building its FBX")
    print(f"[model-budget] {ASSET_ID}: {report_path}")


if __name__ == "__main__":
    main()
