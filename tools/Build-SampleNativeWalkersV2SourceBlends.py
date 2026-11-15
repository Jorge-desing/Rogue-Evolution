#!/usr/bin/env python3
"""Generate v2 source `.blend` files for the 4 sample-native walkers/mechs and ArachnidHexapodFactory
without modifying a single byte of the v1 `.blend` files.
"""
from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

import bpy

REPO_ROOT = Path(__file__).resolve().parent.parent
TOOLS_DIR = REPO_ROOT / "tools"
SOURCE_ART = REPO_ROOT / "content" / "source-art"
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

import RogueModelKit as rmk
from sample_native_walkers_v2_recipes import (
    _collect_meshes,
    apply_arachnid_hexapod_factory_v2,
    apply_arachnid_hexapod_v2,
    apply_atlas_crane_mech_v2,
    apply_bastion_siege_mech_v2,
    apply_ironclad_walker_v2,
    audit_walker_v2_scene,
)

SPECS = [
    {
        "unit_name": "IroncladWalker",
        "v1": SOURCE_ART / "ironclad-walker.blend",
        "v2": SOURCE_ART / "ironclad-walker-v2.blend",
        "root": "IroncladWalkerArmor_Torso",
        "apply": apply_ironclad_walker_v2,
        "walk_sweep": True,
        "clearance_pairs": [
            ("IroncladWalkerLeg_AnkleDamper_L_L", "IroncladWalkerLeg_ShinArmor_L", 0.002),
            ("IroncladWalkerLeg_AnkleDamper_R_R", "IroncladWalkerLeg_ShinArmor_R", 0.002),
        ],
    },
    {
        "unit_name": "AtlasCraneMech",
        "v1": SOURCE_ART / "atlas-crane-mech.blend",
        "v2": SOURCE_ART / "atlas-crane-mech-v2.blend",
        "root": "AtlasCraneMechArmor_IndustrialTorso",
        "apply": apply_atlas_crane_mech_v2,
        "walk_sweep": True,
        "clearance_pairs": [
            ("AtlasCraneMechArmor_AccessHatch_CoamingRim", "AtlasCraneMechTurret_MantletHousing", 0.015),
        ],
    },
    {
        "unit_name": "BastionSiegeMech",
        "v1": SOURCE_ART / "bastion-siege-mech.blend",
        "v2": SOURCE_ART / "bastion-siege-mech-v2.blend",
        "root": "BastionSiegeMechArmor_Hull",
        "apply": apply_bastion_siege_mech_v2,
        "walk_sweep": True,
        "clearance_pairs": [
            ("BastionArmor_TrunnionBracket_CheekBlock_L", "BastionSiegeMechTurret_MantletHousing", 0.010),
            ("BastionArmor_TrunnionBracket_CheekBlock_R", "BastionSiegeMechTurret_MantletHousing", 0.010),
            ("BastionArmor_AccessHatch_CoamingRim", "BastionSiegeMechSensor_Visor_Housing", 0.020),
        ],
    },
    {
        "unit_name": "ArachnidHexapod",
        "v1": SOURCE_ART / "arachnid-hexapod.blend",
        "v2": SOURCE_ART / "arachnid-hexapod-v2.blend",
        "root": "ArachnidHexapodArmor_ArmoredCarapace",
        "apply": apply_arachnid_hexapod_v2,
        "walk_sweep": False,
        "clearance_pairs": [],
    },
    {
        "unit_name": "ArachnidHexapodFactory",
        "v1": SOURCE_ART / "arachnidhexapodfactory.blend",
        "v2": SOURCE_ART / "arachnidhexapodfactory-v2.blend",
        "root": "ArachnidHexapodFactory_FoundationOctagon",
        "apply": apply_arachnid_hexapod_factory_v2,
        "walk_sweep": False,
        "clearance_pairs": [],
    },
]


def _sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def main() -> int:
    pre_hashes = {str(spec["v1"]): _sha256(spec["v1"]) for spec in SPECS}
    reports = []
    all_ok = True

    for spec in SPECS:
        unit_name = spec["unit_name"]
        v1_path = spec["v1"]
        v2_path = spec["v2"]
        bpy.ops.wm.open_mainfile(filepath=str(v1_path), load_ui=False)
        bpy.context.view_layer.update()

        spec["apply"](unit_name)
        meshes = _collect_meshes(unit_name)
        if unit_name == "BastionSiegeMech":
            meshes.extend(_collect_meshes("BastionArmor"))
        rmk.bake_advanced_vertex_shading(
            meshes,
            wear_level="field_tested",
            soot_intensity=1.0,
            structural_wear_mask=True,
        )
        bpy.context.view_layer.update()

        audit = audit_walker_v2_scene(
            unit_name,
            root_name=spec["root"],
            clearance_pairs=spec["clearance_pairs"],
            run_walk_sweep=spec["walk_sweep"],
        )
        reports.append(audit)
        if not audit["passed"]:
            all_ok = False

        bpy.ops.wm.save_as_mainfile(filepath=str(v2_path))

    post_hashes = {str(spec["v1"]): _sha256(spec["v1"]) for spec in SPECS}
    hash_preserved = pre_hashes == post_hashes
    summary = {
        "status": "UNCALIBRATED_USER_AUTHORIZED",
        "v1_hashes_preserved": hash_preserved,
        "all_passed": all_ok and hash_preserved,
        "reports": reports,
    }
    print(json.dumps(summary, indent=2))
    return 0 if summary["all_passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
