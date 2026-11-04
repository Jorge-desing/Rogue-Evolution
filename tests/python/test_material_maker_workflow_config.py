"""Regression tests for the repository-wide Material Maker workflow contract."""

from __future__ import annotations

import unittest

from tests.python.helpers import REPO_ROOT, load_tool_module

VALIDATOR = load_tool_module("validate_material_maker_config")
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


if __name__ == "__main__":
    unittest.main()
