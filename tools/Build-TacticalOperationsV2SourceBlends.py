"""Generate editable v2 source .blend files for the 6 tactical-operations units.

Reads each v1 source .blend, applies sculpted shells, Exact Boolean detail cuts,
raycast-seated stays, and duplicate/overlap cleanup from ``tactical_operations_v2_recipes``,
verifies zero invalid overlaps and UV/Vertex-Color contracts, and writes a new
``content/source-art/<unit>-v2.blend`` file without modifying any v1 .blend.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import bpy

REPO_ROOT = Path(__file__).resolve().parent.parent
TOOLS_DIR = REPO_ROOT / "tools"
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

import tactical_operations_v2_recipes as v2

V1_TO_V2_BLENDS: dict[str, tuple[str, str]] = {
    "ArcLanceWalker": ("arclancewalker.blend", "arclancewalker-v2.blend"),
    "TidebreakerHovercraft": ("tidebreaker-hovercraft.blend", "tidebreaker-hovercraft-v2.blend"),
    "FieldMedicRover": ("fieldmedicrover.blend", "fieldmedicrover-v2.blend"),
    "SkyguardSAM": ("skyguard-sam.blend", "skyguard-sam-v2.blend"),
    "SalvageHauler": ("salvage-hauler.blend", "salvage-hauler-v2.blend"),
    "SentinelRelay": ("sentinel-relay.blend", "sentinel-relay-v2.blend"),
}


def build_v2_source_blend(unit_name: str) -> dict:
    v1_name, v2_name = V1_TO_V2_BLENDS[unit_name]
    source_dir = REPO_ROOT / "content" / "source-art"
    v1_path = source_dir / v1_name
    v2_path = source_dir / v2_name
    if not v1_path.exists():
        raise FileNotFoundError(f"Missing v1 source blend: {v1_path}")

    bpy.ops.wm.open_mainfile(filepath=str(v1_path))
    notes = v2.UNIT_V2_RECIPE_MAP[unit_name]()

    # Ensure every mesh has valid UVMap & CORNER BYTE_COLOR before saving v2 blend
    for obj in bpy.data.objects:
        if obj.type == "MESH" and "_COLLIDER" not in obj.name and not obj.name.endswith("_ShieldBubble"):
            v2.ensure_uv_and_color_layers(obj.data, force_smart_uv=False)

    audit = v2.audit_v2_scene_clearances(unit_name)
    audit["notes"] = notes
    audit["v2_blend"] = str(v2_path.relative_to(REPO_ROOT))
    if not audit["passed"]:
        raise RuntimeError(f"Surface-fit / clearance audit failed for {unit_name}: {json.dumps(audit, indent=2)}")

    bpy.ops.wm.save_as_mainfile(filepath=str(v2_path))
    return audit


def main() -> int:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description="Build tactical-operations v2 source blends.")
    parser.add_argument("units", nargs="*", help="Subset of units to build (default: all 6).")
    args = parser.parse_args(argv)

    selected = args.units or list(V1_TO_V2_BLENDS.keys())
    reports: list[dict] = []
    for unit_name in selected:
        if unit_name not in V1_TO_V2_BLENDS:
            raise KeyError(f"Unknown v2 target unit: {unit_name}")
        reports.append(build_v2_source_blend(unit_name))

    print("=== TACTICAL-OPERATIONS V2 SOURCE BLENDS SUMMARY ===")
    for rep in reports:
        print(
            f"[OK] {rep['unit']}: meshes={rep['mesh_count']}, faces={rep['total_faces']}, "
            f"v2_tagged={rep['tagged_v2_objects']}, tire_overlaps={len(rep['tire_overlaps'])}, "
            f"out={rep['v2_blend']}"
        )
        for note in rep["notes"]:
            print(f"     - {note}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
