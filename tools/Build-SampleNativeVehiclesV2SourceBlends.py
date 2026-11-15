"""Generate the 5 hardflow-grade v2 source .blend files for sample-native-expansion
Level-1 vehicles without modifying any v1 .blend file.

Run in Blender 4.5.14:
  .tools/Blender-4.5.14/blender-4.5.14-windows-x64/blender.exe --background --python-exit-code 1 --python tools/Build-SampleNativeVehiclesV2SourceBlends.py
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

import bpy

REPO_ROOT = Path(__file__).resolve().parents[1]
TOOLS_DIR = REPO_ROOT / "tools"
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

from sample_native_vehicles_v2_recipes import (
    UNIT_V2_RECIPE_MAP,
    audit_v2_scene_clearances,
)


V1_TO_V2_SOURCES: dict[str, tuple[str, str]] = {
    "SandlanceScout": ("sandlance-scout.blend", "sandlance-scout-v2.blend"),
    "ColossusSiegePlatform": ("colossus-siege.blend", "colossus-siege-v2.blend"),
    "SkirmishStrikeBike": ("skirmish-strike-bike.blend", "skirmish-strike-bike-v2.blend"),
    "SurgeArcTank": ("surge-arc-tank.blend", "surge-arc-tank-v2.blend"),
    "OverlordCommandHQ": ("overlord-command-hq.blend", "overlord-command-hq-v2.blend"),
}


def build_v2_source_blend(unit_name: str, *, strict: bool = True) -> dict:
    v1_rel, v2_rel = V1_TO_V2_SOURCES[unit_name]
    v1_path = REPO_ROOT / "content" / "source-art" / v1_rel
    v2_path = REPO_ROOT / "content" / "source-art" / v2_rel
    if not v1_path.is_file():
        raise FileNotFoundError(f"v1 source blend not found: {v1_path}")

    bpy.ops.wm.open_mainfile(filepath=str(v1_path))
    notes = UNIT_V2_RECIPE_MAP[unit_name]()
    bpy.context.view_layer.update()
    audit = audit_v2_scene_clearances(unit_name)
    audit["notes"] = notes
    audit["calibration_status"] = "UNCALIBRATED_USER_AUTHORIZED"
    audit["source_blend_v1"] = str(v1_path)
    audit["output_blend_v2"] = str(v2_path)

    bpy.ops.wm.save_as_mainfile(filepath=str(v2_path))
    print(f"[sample-native-v2] Saved {v2_path.name} (UNCALIBRATED_USER_AUTHORIZED): {json.dumps(audit)}")
    if strict and not audit["passed"]:
        raise RuntimeError(f"v2 audit failed for {unit_name}: {json.dumps(audit)}")
    return audit


def main(argv: list[str]) -> int:
    strict = "--no-strict" not in argv
    units = [a for a in argv if not a.startswith("--") and a in V1_TO_V2_SOURCES]
    if not units:
        units = list(V1_TO_V2_SOURCES.keys())

    results = []
    any_failed = False
    for unit_name in units:
        res = build_v2_source_blend(unit_name, strict=False)
        results.append(res)
        if not res["passed"]:
            any_failed = True

    if strict and any_failed:
        print("[sample-native-v2] FAILED audit on one or more vehicles.")
        return 1
    return 0


if __name__ == "__main__":
    raw_argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    raise SystemExit(main(raw_argv))
