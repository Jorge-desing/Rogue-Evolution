"""Blender 4.5 bpy integration tests for Real Functional Engineering, Form Hierarchy,
and the Canonical Two-Level Visual System.
"""

from __future__ import annotations

import sys
import unittest
from pathlib import Path

import bpy

from tests.python.helpers import TOOLS_DIR, load_tool_module

if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

mga = load_tool_module("mesh_geometry_algorithms")
fei = load_tool_module("functional_engineering_integration")
vmas = load_tool_module("VisualModelAuthoringSupport")
cvm = load_tool_module("Build-CanonicalVisualModules")


class CanonicalTwoLevelIntegrationBpyTests(unittest.TestCase):
    def setUp(self) -> None:
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def test_forsyth_preserves_uv_color_rgba_and_materials(self) -> None:
        bpy.ops.mesh.primitive_cube_add(size=1.2)
        obj = bpy.context.active_object
        mat = bpy.data.materials.new("TestArmorMat")
        obj.data.materials.append(mat)
        uv_layer = obj.data.uv_layers.active or obj.data.uv_layers.new(name="UVMap")
        for idx, loop in enumerate(uv_layer.data):
            loop.uv = (0.1 + 0.01 * idx, 0.2 + 0.01 * idx)
        mga.apply_vertex_wear_rgba_bpy(obj, soot_anchors=[[0.6, 0.6, 0.6]], soot_radius=0.9)

        acmr = mga.apply_forsyth_cache_optimize_bpy(obj, cache_size=32)
        self.assertGreater(acmr, 0.0)
        self.assertEqual(len(obj.data.uv_layers), 1)
        self.assertGreater(obj.data.uv_layers[0].data[0].uv[0], 0.09)
        self.assertIsNotNone(obj.data.color_attributes.get("Color"))
        self.assertEqual(len(obj.data.materials), 1)
        audit = mga.audit_blender_object_metrics_bpy(obj)
        self.assertTrue(audit["hasUvMap"])
        self.assertIsNotNone(audit["vertexWearRgba"])
        self.assertGreater(audit["vertexWearRgba"]["gMax"], 0.65)
        self.assertGreater(audit["vertexWearRgba"]["bMax"], 0.1)

    def test_level2_canonical_module_functional_soot_and_cache_optimization(self) -> None:
        entries = cvm.read_module_entries()
        flak_entry = next(e for e in entries if e["visualEngineeringRole"] == "heavy_flak_battery")
        armor_entry = next(e for e in entries if e["visualEngineeringRole"] == "armor_cassette")

        # 1. Heavy flak battery should receive muzzle soot (B > 0) and preserve UVs + Color
        created_flak, mats_flak = cvm.build_module_geometry(flak_entry, seed=101)
        combined_flak = cvm.prepare_module_export_geometry(
            flak_entry, created_flak, mats_flak, allow_uncalibrated_geometry=True
        )
        flak_quality = fei.apply_canonical_two_level_mesh_pass_bpy(
            combined_flak, role=flak_entry["visualEngineeringRole"], optimize_vertex_cache=False
        )
        self.assertGreater(flak_quality["totalTriangles"], 0)
        self.assertEqual(flak_quality["maxNonManifoldEdges"], 0)
        self.assertTrue(all(o["hasUvMap"] for o in flak_quality["objects"]))
        self.assertGreater(max(o["vertexWearRgba"]["bMax"] for o in flak_quality["objects"]), 0.05)

        # 2. Clean armor cassette should keep thermal soot at 0.0
        bpy.ops.wm.read_factory_settings(use_empty=True)
        created_armor, mats_armor = cvm.build_module_geometry(armor_entry, seed=202)
        combined_armor = cvm.prepare_module_export_geometry(
            armor_entry, created_armor, mats_armor, allow_uncalibrated_geometry=True
        )
        armor_quality = fei.apply_canonical_two_level_mesh_pass_bpy(
            combined_armor, role=armor_entry["visualEngineeringRole"], optimize_vertex_cache=False
        )
        self.assertEqual(max(o["vertexWearRgba"]["bMax"] for o in armor_quality["objects"]), 0.0)

    def test_level1_functional_engineering_pass_on_cast_and_faceted_parts(self) -> None:
        bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=0.8)
        cast_obj = bpy.context.active_object
        cast_obj.name = "UnitCast_HullPod"
        cast_obj.data.uv_layers.new(name="UVMap")
        mga.apply_vertex_wear_rgba_bpy(cast_obj)

        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(2.0, 0.0, 0.0))
        armor_obj = bpy.context.active_object
        armor_obj.name = "UnitArmor_GlacisPlate"
        armor_obj.data.uv_layers.new(name="UVMap")
        mga.apply_vertex_wear_rgba_bpy(armor_obj)

        class _DummyBuilder:
            objects = [cast_obj, armor_obj]

        report = vmas.apply_level1_functional_engineering_pass(
            _DummyBuilder(), role="utility_sponson", enable_taubin_fairing=True, optimize_vertex_cache=True
        )
        self.assertEqual(report["taubinFairingObjects"], ["UnitCast_HullPod"])
        self.assertEqual(report["objectCount"], 2)
        self.assertTrue(all(o["hasUvMap"] for o in report["objects"]))


if __name__ == "__main__":
    unittest.main()
