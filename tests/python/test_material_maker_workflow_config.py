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

    def test_config_preserves_the_four_map_contract(self) -> None:
        import json

        data = json.loads(CONFIG.read_text(encoding="utf-8"))
        self.assertEqual(
            {"Albedo", "MetallicGloss", "Normal", "Emission"},
            {entry["suffix"] for entry in data["textureContract"]["requiredMaps"]},
        )
        self.assertEqual("RogueCommand/VehiclePBR_VertexWear", data["textureContract"]["shader"])

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

    def test_all_presets_wire_bimodal_metallic_node(self) -> None:
        if not TEMPLATE_BUILDER.EXAMPLE.is_file():
            self.skipTest("Material Maker 1.7 example is not staged locally")
        self.assertGreaterEqual(len(TEMPLATE_BUILDER.PRESETS), 36)
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


if __name__ == "__main__":
    unittest.main()
