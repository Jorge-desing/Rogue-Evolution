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

    def test_qem_lod_preserves_attributes_and_materials(self) -> None:
        rmk = load_tool_module("RogueModelKit")
        bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, radius=1.0)
        obj = bpy.context.active_object
        obj.name = "TestUnit_Armor"
        mat = bpy.data.materials.new("ArmorMat")
        obj.data.materials.append(mat)
        uv_layer = obj.data.uv_layers.new(name="UVMap")
        for idx, loop in enumerate(uv_layer.data):
            loop.uv = ((idx % 17) / 17.0, (idx // 17) / 40.0)
        mga.apply_vertex_wear_rgba_bpy(obj)

        orig_tris = rmk.count_mesh_triangles([obj])
        lods = rmk.generate_lod_chain(
            [obj],
            prefix="TestUnit",
            lod_levels=[("LOD0", 1.0), ("LOD1", 0.5), ("LOD2", 0.25)],
            simplify_backend="qem",
        )
        lod1 = lods["LOD1"][0]
        lod2 = lods["LOD2"][0]
        self.assertLess(rmk.count_mesh_triangles([lod1]), orig_tris)
        self.assertLess(rmk.count_mesh_triangles([lod2]), rmk.count_mesh_triangles([lod1]))
        self.assertGreater(len(lod1.data.uv_layers), 0)
        self.assertIsNotNone(lod1.data.color_attributes.get("Color"))
        self.assertEqual(len(lod1.data.materials), 1)
        audit = mga.audit_blender_object_metrics_bpy(lod1)
        self.assertEqual(audit["nonManifoldEdges"], 0)
        self.assertTrue(audit["hasUvMap"])

    def test_level2_prepass_fairs_cast_role_before_batching(self) -> None:
        entries = cvm.read_module_entries()
        drum_entry = next(e for e in entries if e["visualEngineeringRole"] == "fuel_drum_pair")
        created, mats = cvm.build_module_geometry(drum_entry, seed=303)
        prepass_info: dict = {}
        combined = cvm.prepare_module_export_geometry(
            drum_entry,
            created,
            mats,
            allow_uncalibrated_geometry=True,
            prepass_out=prepass_info,
        )
        self.assertTrue(prepass_info)
        self.assertGreater(prepass_info["formHierarchyCounts"].get("primary_cast", 0), 0)
        self.assertGreater(len(prepass_info["taubinFairingObjects"]), 0)
        quality = fei.apply_canonical_two_level_mesh_pass_bpy(
            combined,
            role=drum_entry["visualEngineeringRole"],
            enable_taubin_fairing=False,
            optimize_vertex_cache=False,
            assume_cache_optimized=True,
        )
        self.assertEqual(quality["maxNonManifoldEdges"], 0)
        self.assertTrue(all(o["hasUvMap"] for o in quality["objects"]))

    def test_level1_vehicle_builder_runs_fe_qem_and_atlas_algorithms(self) -> None:
        import tempfile
        import numpy as np

        rmk = load_tool_module("RogueModelKit")
        builder = rmk.RogueVehicleBuilder("TestFeTank", faction="vanguard")
        bpy.ops.mesh.primitive_cube_add(size=1.2, location=(0.0, 0.0, 0.6))
        hull = bpy.context.active_object
        hull.name = "TestFeTankArmor_ChassisHull"
        hull.data.materials.append(builder.palette.primary_armor)
        builder.objects.append(hull)

        bpy.ops.mesh.primitive_uv_sphere_add(segments=14, ring_count=10, radius=0.4, location=(0.0, 0.2, 1.3))
        pod = bpy.context.active_object
        pod.name = "UnitCast_Pod"
        pod.data.materials.append(builder.palette.primary_armor)
        builder.objects.append(pod)

        bpy.ops.mesh.primitive_cube_add(size=0.3, location=(-0.5, -0.7, 0.7))
        exhaust = bpy.context.active_object
        exhaust.name = "Exhaust_L"
        exhaust.data.materials.append(builder.palette.gunmetal)
        builder.objects.append(exhaust)
        builder.exhaust_l = (-0.5, -0.7, 0.7)

        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            fbx_path = tmp_path / "TestFeTank.fbx"
            blend_path = tmp_path / "TestFeTank.blend"
            diag = builder.build_and_export(
                fbx_path=fbx_path,
                blend_path=blend_path,
                generate_lods=True,
                lod_ratios=[("LOD0", 1.0), ("LOD1", 0.6), ("LOD2", 0.35)],
                lod_simplify_backend="qem",
                bake_atlas=True,
                atlas_resolution=256,
                atlas_detail_strength=0.25,
                functional_role="fuel_drum_pair",
            )
            fe_summary = diag.get("functional_engineering", {})
            self.assertIn("UnitCast_Pod", fe_summary.get("taubinFairingObjects", []))
            self.assertEqual(fe_summary.get("maxNonManifoldEdges"), 0)
            self.assertIn("normal", diag.get("texture_maps", {}))
            n_img = bpy.data.images.load(str(diag["texture_maps"]["normal"]))
            try:
                w, h = int(n_img.size[0]), int(n_img.size[1])
                buf = np.empty(w * h * 4, dtype=np.float32)
                n_img.pixels.foreach_get(buf)
                normal_rgb = buf.reshape(h, w, 4)[..., :3]
                self.assertGreater(float(normal_rgb[..., :2].std()), 0.002)
            finally:
                bpy.data.images.remove(n_img)


if __name__ == "__main__":
    unittest.main()
