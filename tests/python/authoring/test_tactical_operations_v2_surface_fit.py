"""Blender-side Exact Boolean, BVHTree clearance, and source-versioning checks for v2 tactical-operations units."""

from __future__ import annotations

import sys
import unittest
from pathlib import Path

import bpy
from mathutils import Vector

REPO_ROOT = Path(__file__).resolve().parents[3]
TOOLS_DIR = REPO_ROOT / "tools"
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

import tactical_operations_v2_recipes as v2

V2_FILES = {
    "ArcLanceWalker": ("arclancewalker.blend", "arclancewalker-v2.blend", "Build-ArcLanceWalkerModel.py"),
    "TidebreakerHovercraft": ("tidebreaker-hovercraft.blend", "tidebreaker-hovercraft-v2.blend", "Build-TidebreakerHovercraftModel.py"),
    "FieldMedicRover": ("fieldmedicrover.blend", "fieldmedicrover-v2.blend", "Build-FieldMedicRoverModel.py"),
    "SkyguardSAM": ("skyguard-sam.blend", "skyguard-sam-v2.blend", "Build-SkyguardSAMModel.py"),
    "SalvageHauler": ("salvage-hauler.blend", "salvage-hauler-v2.blend", "Build-SalvageHaulerModel.py"),
    "SentinelRelay": ("sentinel-relay.blend", "sentinel-relay-v2.blend", "Build-SentinelRelayModel.py"),
}


class TacticalOperationsV2SurfaceFitTests(unittest.TestCase):
    @classmethod
    def tearDownClass(cls) -> None:
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def test_v2_blends_and_builders_preserve_v1_sources(self) -> None:
        source_dir = REPO_ROOT / "content" / "source-art"
        for unit, (v1_name, v2_name, builder_name) in V2_FILES.items():
            with self.subTest(unit=unit):
                self.assertTrue((source_dir / v1_name).is_file(), f"Missing preserved v1 source: {v1_name}")
                self.assertTrue((source_dir / v2_name).is_file(), f"Missing v2 source: {v2_name}")
                builder_text = (TOOLS_DIR / builder_name).read_text(encoding="utf-8-sig")
                self.assertIn(v2_name, builder_text, f"{builder_name} must point to {v2_name}")

    def test_all_six_v2_blends_pass_boolean_attribute_and_zero_overlap_audit(self) -> None:
        source_dir = REPO_ROOT / "content" / "source-art"
        for unit, (_v1_name, v2_name, _builder) in V2_FILES.items():
            with self.subTest(unit=unit):
                bpy.ops.wm.open_mainfile(filepath=str(source_dir / v2_name))
                audit = v2.audit_v2_scene_clearances(unit)
                self.assertEqual(audit["missing_uv"], [], f"{unit} missing UVMap")
                self.assertEqual(audit["missing_color"], [], f"{unit} missing CORNER BYTE_COLOR Color")
                self.assertEqual(audit["bad_mat_count"], [], f"{unit} multi/empty material meshes")
                self.assertEqual(audit["duplicate_suffixes"], [], f"{unit} .001 duplicates")
                self.assertEqual(audit["tire_overlaps"], [], f"{unit} tire/body overlaps")
                self.assertFalse(audit["dozer_spade_overlap"], f"{unit} dozer/spade overlap")
                self.assertEqual(audit["disconnected_from_hull"], [], f"{unit} disconnected parts from hull")
                self.assertGreaterEqual(audit["tagged_v2_objects"], 3, f"{unit} missing Exact Boolean/v2 recipe tags")
                self.assertTrue(audit["passed"], f"{unit} audit failed: {audit}")

    def test_sentinel_relay_v2_removes_duplicate_turret_ring_guard_and_bay_hatch(self) -> None:
        bpy.ops.wm.open_mainfile(filepath=str(REPO_ROOT / "content" / "source-art" / "sentinel-relay-v2.blend"))
        names = {o.name for o in bpy.data.objects if o.type == "MESH"}
        self.assertIn("SentinelRelayTurretRingGuard_DeflectorCollar", names)
        self.assertIn("SentinelRelayTurretRingGuard_YawRaceLip", names)
        self.assertFalse(any(n.startswith("SentinelRelayArmor_TurretRingGuard_") for n in names))
        self.assertFalse(any(n.startswith("SentinelRelayAccessHatch_") for n in names))
        # Preserve test_sentinel_relay_surface_fit parts
        for required in (
            "SentinelRelayArmor_CabCowl",
            "SentinelRelayArmor_EquipmentBay",
            "SentinelRelaySensor_RelayMast",
            "SentinelRelayArmor_CowlServicePanel_L",
            "SentinelRelayArmor_CowlServicePanel_R",
            "SentinelRelayJoint_RelayBrace_L",
            "SentinelRelayJoint_RelayBrace_R",
        ):
            self.assertIn(required, names)

    def test_raycast_stays_and_strut_anchors_contact_hosts_within_two_millimeters(self) -> None:
        stay_checks = (
            ("fieldmedicrover-v2.blend", ("FieldMedicRoverFender_", "_Stay_"), ("FieldMedicRoverArmor_TraumaCabShell", "FieldMedicRoverArmor_SpineChassis", "FieldMedicRoverArmor_EngineBlock", "FieldMedicRoverArmor_FrontNose")),
            ("skyguard-sam-v2.blend", ("SkyguardSAMFender_", "_Stay_"), ("SkyguardSAMArmor_ForwardCab", "SkyguardSAMArmor_LauncherBed", "SkyguardSAMArmor_ChassisFrame")),
            ("salvage-hauler-v2.blend", ("SalvageHaulerTreads_FenderStay_",), ("SalvageHaulerArmor_Hull", "SalvageHaulerArmor_UpperDeck")),
            ("sentinel-relay-v2.blend", ("SentinelRelayFender_", "_Stay_"), ("SentinelRelayArmor_Chassis", "SentinelRelayArmor_CabCowl", "SentinelRelayArmor_EquipmentBay")),
        )
        source_dir = REPO_ROOT / "content" / "source-art"
        for blend_name, stay_tokens, host_names in stay_checks:
            bpy.ops.wm.open_mainfile(filepath=str(source_dir / blend_name))
            hosts = [bpy.data.objects.get(h) for h in host_names if bpy.data.objects.get(h) is not None]
            host_bvhs = [v2._mesh_bvh(h)[0] for h in hosts]
            stays = [
                o for o in bpy.data.objects
                if o.type == "MESH" and all(tok in o.name for tok in stay_tokens)
            ]
            self.assertGreaterEqual(len(stays), 2, f"{blend_name}: expected raycast stays")
            for stay in stays:
                min_dist = min(
                    hbvh.find_nearest(stay.matrix_world @ Vector(v.co))[3]
                    for hbvh in host_bvhs
                    for v in stay.data.vertices
                    if hbvh.find_nearest(stay.matrix_world @ Vector(v.co))[3] is not None
                )
                self.assertLessEqual(min_dist, 0.0025, f"{blend_name} {stay.name} gap too large: {min_dist:.4f}m")


if __name__ == "__main__":
    unittest.main()
