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

    def test_config_preserves_the_four_map_contract(self) -> None:
        import json

        data = json.loads(CONFIG.read_text(encoding="utf-8"))
        self.assertEqual(
            {"Albedo", "MetallicGloss", "Normal", "Emission"},
            {entry["suffix"] for entry in data["textureContract"]["requiredMaps"]},
        )
        self.assertEqual("RogueCommand/VehiclePBR_VertexWear", data["textureContract"]["shader"])

    def test_all_presets_wire_bimodal_metallic_node(self) -> None:
        if not TEMPLATE_BUILDER.EXAMPLE.is_file():
            self.skipTest("Material Maker 1.7 example is not staged locally")
        for preset in (
            "olive_armor",
            "gunmetal_chassis",
            "desert_camo",
            "hazard_industrial",
            "orbital_titanium",
        ):
            project = TEMPLATE_BUILDER.build_template_project(preset_name=preset, size_power=10)
            node_names = {n.get("name") for n in project.get("nodes", [])}
            self.assertIn("colorize_metallic", node_names)
            metal_conns = [
                c
                for c in project.get("connections", [])
                if c.get("to") == "Material" and c.get("to_port") == 1
            ]
            self.assertEqual(1, len(metal_conns))
            self.assertEqual("colorize_metallic", metal_conns[0].get("from"))

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
