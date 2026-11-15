"""Regression tests for the repository-wide Material Maker workflow contract."""

from __future__ import annotations

import unittest

from tests.python.helpers import REPO_ROOT, load_tool_module

VALIDATOR = load_tool_module("validate_material_maker_config")
TEMPLATE_BUILDER = load_tool_module(
    "prepare_mm_tactical_template", "Prepare-MaterialMakerTacticalTemplate.py"
)
PAINT_PREP = load_tool_module("prepare_material_maker_3d_paint")
CONFIG = REPO_ROOT / "tools" / "material-maker" / "rogue-command-material-maker.json"


class MaterialMakerWorkflowConfigTests(unittest.TestCase):
    def test_config_covers_urp_blender_3d_and_procedural_workflows(self) -> None:
        errors = VALIDATOR.validate_config(CONFIG)
        self.assertEqual([], errors)
        import json
        data = json.loads(CONFIG.read_text(encoding="utf-8"))
        self.assertEqual(
            {"Albedo", "MetallicGloss", "Normal", "Emission"},
            {entry["suffix"] for entry in data["textureContract"]["requiredMaps"]},
        )
        self.assertEqual("RogueCommand/VehiclePBR_VertexWear", data["textureContract"]["shader"])
        runner = (REPO_ROOT / "tools" / "Run-MaterialMakerBake.ps1").read_text(encoding="utf-8")
        self.assertIn("audit_material_maker_output.py", runner)

    def test_runner_clears_stale_raw_and_packed_outputs(self) -> None:
        runner = (REPO_ROOT / "tools" / "Run-MaterialMakerBake.ps1").read_text(encoding="utf-8")
        self.assertIn("Remove-Item -LiteralPath $rawDir -Recurse -Force", runner)
        self.assertIn("stale Material Maker output", runner)
        self.assertIn("--template", runner)
        self.assertIn("edge_detect_emission", runner)

    def test_runner_supports_blender_preview_for_3d_and_urp(self) -> None:
        runner = (REPO_ROOT / "tools" / "Run-MaterialMakerBake.ps1").read_text(encoding="utf-8")
        self.assertIn("BlenderPreviewModel", runner)
        self.assertIn("apply_material_maker_to_blender.py", runner)
        self.assertIn("blenderPreviewReport", runner)
        self.assertIn("resolutionActual", runner)

    def test_all_presets_wire_tactical_edge_emission(self) -> None:
        if not TEMPLATE_BUILDER.EXAMPLE.is_file():
            self.skipTest("Material Maker 1.7 example is not staged locally")
        self.assertEqual(set(TEMPLATE_BUILDER.PRESETS), set(TEMPLATE_BUILDER.EMISSION_PROFILES))
        for preset in sorted(TEMPLATE_BUILDER.PRESETS):
            project = TEMPLATE_BUILDER.build_template_project(preset_name=preset, size_power=10)
            node_names = {n.get("name") for n in project.get("nodes", [])}
            self.assertIn("colorize_emission", node_names, preset)
            self.assertIn("edge_detect_emission", node_names, preset)
            self.assertIn("micro_perlin", node_names, preset)
            self.assertIn("micro_detail_blend", node_names, preset)
            self.assertIn("micro_roughness_blend", node_names, preset)
            emission_connections = [
                c for c in project.get("connections", [])
                if c.get("to") == "Material" and c.get("to_port") == 3
            ]
            self.assertEqual(1, len(emission_connections), preset)
            self.assertEqual("colorize_emission", emission_connections[0].get("from"))
            material = next(n for n in project["nodes"] if n.get("name") == "Material")
            self.assertGreater(float(material["parameters"]["emission_energy"]), 0.0)
            self.assertEqual("tactical-hd-v2", project["rogueCommandQuality"]["profile"])

            detail_connections = [
                c for c in project.get("connections", [])
                if c.get("from") == "micro_detail_blend"
                and c.get("to") == "normal_map_2"
                and c.get("to_port") == 0
            ]
            roughness_connections = [
                c for c in project.get("connections", [])
                if c.get("from") == "micro_roughness_blend"
                and c.get("to") == "Material"
                and c.get("to_port") == 2
            ]
            material_roughness_connections = [
                c for c in project.get("connections", [])
                if c.get("to") == "Material" and c.get("to_port") == 2
            ]
            self.assertEqual(1, len(detail_connections), preset)
            self.assertEqual(1, len(roughness_connections), preset)
            self.assertEqual(1, len(material_roughness_connections), preset)
            normal_inputs = {
                (c.get("from"), c.get("to_port"))
                for c in project.get("connections", [])
                if c.get("to") == "micro_detail_blend"
            }
            self.assertEqual({("micro_perlin", 0), ("blend_2", 1),
                              ("micro_detail_opacity", 2)}, normal_inputs, preset)
            base_inputs = {(c.get("from"), c.get("to_port"))
                           for c in project.get("connections", []) if c.get("to") == "blend_2"}
            self.assertEqual({("Perlin", 0), ("Warp", 1),
                              ("base_detail_opacity", 2)}, base_inputs, preset)
            normal_opacity = next(n for n in project["nodes"] if n.get("name") == "micro_detail_opacity")
            self.assertAlmostEqual(float(project["rogueCommandQuality"]["microNormalBlend"]),
                                   float(normal_opacity["parameters"]["color"]), places=3, msg=preset)
            # Material Maker 1.7 blend ports are foreground, background, mask.
            blend_inputs = {
                (c.get("from"), c.get("to_port"))
                for c in project.get("connections", [])
                if c.get("to") == "micro_roughness_blend"
            }
            self.assertEqual({("micro_perlin", 0), ("colorize_4", 1),
                              ("micro_roughness_opacity", 2)}, blend_inputs, preset)
            opacity = next(n for n in project["nodes"] if n.get("name") == "micro_roughness_opacity")
            self.assertEqual("uniform_greyscale", opacity["type"], preset)
            self.assertAlmostEqual(float(project["rogueCommandQuality"]["microRoughnessBlend"]),
                                   float(opacity["parameters"]["color"]), places=3, msg=preset)

    def test_all_presets_wire_bimodal_metallic_node(self) -> None:
        if not TEMPLATE_BUILDER.EXAMPLE.is_file():
            self.skipTest("Material Maker 1.7 example is not staged locally")
        self.assertGreaterEqual(len(TEMPLATE_BUILDER.PRESETS), 44)
        for preset in sorted(TEMPLATE_BUILDER.PRESETS):
            project = TEMPLATE_BUILDER.build_template_project(preset_name=preset, size_power=10)
            node_names = {n.get("name") for n in project.get("nodes", [])}
            self.assertIn("colorize_metallic", node_names)
            metal_conns = [
                c
                for c in project.get("connections", [])
                if c.get("to") == "Material" and c.get("to_port") == 1
            ]
            self.assertEqual(1, len(metal_conns), f"Preset {preset} missing Material port 1 connection")
            self.assertEqual("colorize_metallic", metal_conns[0].get("from"))

    def test_presets_declare_space_military_pbr_contracts(self) -> None:
        for name, preset in TEMPLATE_BUILDER.PRESETS.items():
            self.assertTrue(preset.get("label"), f"Preset {name} missing label")
            self.assertTrue(preset.get("name"), f"Preset {name} missing name")
            bricks = preset.get("bricks", {})
            self.assertIn("pattern", bricks, f"Preset {name} missing pattern")
            self.assertIn("repeat", bricks, f"Preset {name} missing repeat")
            self.assertIn("rows", bricks, f"Preset {name} missing rows")
            self.assertIn("columns", bricks, f"Preset {name} missing columns")
            self.assertIn("mortar", bricks, f"Preset {name} missing mortar")
            self.assertIn("bevel", bricks, f"Preset {name} missing bevel")
            self.assertIn("corner", bricks, f"Preset {name} missing corner")

            # Check that bevel_mask is strictly grayscale to preserve downstream roughness stddev
            bevel_mask = preset.get("bevel_mask", ())
            for pos, rgb in bevel_mask:
                self.assertEqual(rgb[0], rgb[1], f"Preset {name} bevel_mask must be grayscale")
                self.assertEqual(rgb[1], rgb[2], f"Preset {name} bevel_mask must be grayscale")

            # Check that metallic curve has bimodal metal/dielectric separation
            metal_curve = preset.get("metallic_curve", ())
            self.assertGreaterEqual(len(metal_curve), 3, f"Preset {name} metallic_curve needs >= 3 points")
            metal_levels = [rgb[0] for _, rgb in metal_curve]
            self.assertTrue(any(v < 0.15 for v in metal_levels), f"Preset {name} needs dielectric baseline")
            self.assertTrue(any(v > 0.85 for v in metal_levels), f"Preset {name} needs metallic plateau")

    def test_3d_paint_workspace_creates_manifest_and_ptex(self) -> None:
        if not TEMPLATE_BUILDER.EXAMPLE.is_file():
            self.skipTest("Material Maker 1.7 example is not staged locally")
        import tempfile
        from pathlib import Path

        with tempfile.TemporaryDirectory() as tmp:
            ws = Path(tmp) / "MM"
            manifest = PAINT_PREP.prepare_3d_paint_workspace(
                unit_name="PilotUnit",
                workspace_root=ws,
                source_model=None,
                preset="hazard_industrial",
                size_power=10,
            )
            self.assertTrue((ws / "PilotUnit.ptex").is_file())
            self.assertTrue((ws / "paint-workspace.json").is_file())
            self.assertTrue(manifest["singleSubmeshForPaint"])
            self.assertTrue(manifest["productionMeshUntouched"])

    def test_all_presets_wire_plate_variation_and_calibrated_urp_palettes(self) -> None:
        if not TEMPLATE_BUILDER.EXAMPLE.is_file():
            self.skipTest("Material Maker 1.7 example is not staged locally")
        for preset_name, preset in sorted(TEMPLATE_BUILDER.PRESETS.items()):
            lumas = [
                0.2126 * rgb[0] + 0.7152 * rgb[1] + 0.0722 * rgb[2]
                for _, rgb in preset["plate_palette"]
            ]
            rgb_means = [sum(rgb) / 3.0 for _, rgb in preset["plate_palette"]]
            self.assertEqual(lumas, sorted(lumas), f"Preset {preset_name} plate_palette must be monotonic")
            mean_rgb = sum(rgb_means) / len(rgb_means)
            mean_luma = sum(lumas) / len(lumas)
            self.assertGreaterEqual(mean_rgb, 0.359, f"Preset {preset_name} palette RGB mean {mean_rgb:.3f} < 0.359")
            self.assertGreaterEqual(mean_luma, 0.30, f"Preset {preset_name} palette luma {mean_luma:.3f} < 0.30")
            if preset_name in TEMPLATE_BUILDER.FULL_METAL_PRESETS:
                self.assertGreaterEqual(mean_rgb, 0.439, f"Metal preset {preset_name} palette RGB mean < 0.439")

            project = TEMPLATE_BUILDER.build_template_project(preset_name=preset_name, size_power=10)
            qmeta = project.get("rogueCommandQuality", {})
            self.assertGreaterEqual(int(qmeta.get("schemaVersion", 0)), 3)
            self.assertTrue(qmeta.get("urpLinearCalibrated"))

            nodes = {n.get("name"): n for n in project.get("nodes", [])}
            self.assertIn("plate_variation_opacity", nodes, preset_name)
            self.assertEqual("uniform_greyscale", nodes["plate_variation_opacity"].get("type"))
            self.assertLessEqual(float(nodes["plate_variation_opacity"]["parameters"]["color"]), 0.12)
            self.assertEqual(0, nodes["blend_1"]["parameters"].get("blend_type"), preset_name)

            blend1_inputs = {
                (c.get("from"), c.get("to_port"))
                for c in project.get("connections", [])
                if c.get("to") == "blend_1"
            }
            self.assertEqual(
                {("Perlin", 0), ("graph", 1), ("plate_variation_opacity", 2)},
                blend1_inputs,
                preset_name,
            )


if __name__ == "__main__":
    unittest.main()
