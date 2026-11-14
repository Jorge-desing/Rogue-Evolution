"""Contract tests for full Canonical Level 1 parity across all active units and factories."""

from __future__ import annotations

import json
import unittest
from pathlib import Path

from PIL import Image

from tests.python.helpers import load_tool_module

REPO_ROOT = Path(__file__).resolve().parents[3]
HARDPOINTS = load_tool_module("CanonicalUnitHardpoints")
LEVEL1_TEX = load_tool_module("build_canonical_level1_textures")


class CanonicalLevelOneParityTests(unittest.TestCase):
    def test_all_18_active_units_registered_with_four_unique_hardpoints(self) -> None:
        unit_names = HARDPOINTS.list_canonical_unit_names()
        self.assertEqual(18, len(unit_names))

        for pack_id in ("sample-native-expansion", "tactical-operations"):
            manifest = json.loads((REPO_ROOT / "content/packs" / pack_id / "content.json").read_text(encoding="utf-8"))
            for unit in manifest["units"]:
                visual_name = Path(unit["visualAssetName"]).stem
                model_name = visual_name[:-6] if visual_name.lower().endswith("visual") else visual_name
                if model_name.lower() == "scouttank":
                    model_name = "SandlanceScout"
                elif model_name.lower() == "skirmishbike":
                    model_name = "SkirmishStrikeBike"
                elif model_name.lower() == "colossussiege":
                    model_name = "ColossusSiegePlatform"
                resolved = next((u for u in unit_names if u.lower() == model_name.lower()), None)
                self.assertIsNotNone(resolved, f"Missing active unit in CanonicalUnitHardpoints: {model_name}")

        for unit_name in unit_names:
            positions = HARDPOINTS.get_canonical_hardpoint_socket_positions(unit_name)
            self.assertEqual(set(HARDPOINTS.CANONICAL_HARDPOINT_NAMES), set(positions.keys()))
            unique_coords = {tuple(round(float(v), 4) for v in pos) for pos in positions.values()}
            self.assertEqual(4, len(unique_coords), f"{unit_name} must have 4 distinct hardpoint coordinates")

        # Preserve the critical Bulwark L2 side-skirt host-origin invariant.
        self.assertEqual((0.0, 0.0, 0.0), HARDPOINTS.get_canonical_hardpoint_socket_positions("BulwarkSiegeTank")["Socket_Hardpoint_ERA"])

    def test_every_active_unit_and_factory_maps_to_mm_preset(self) -> None:
        all_assets = list(HARDPOINTS.list_canonical_unit_names()) + list(HARDPOINTS.CANONICAL_FACTORY_NAMES)
        self.assertEqual(21, len(all_assets))
        for asset in all_assets:
            preset, tint = LEVEL1_TEX.resolve_asset_preset(asset)
            self.assertTrue(preset)
            self.assertTrue(tint.startswith("#") and len(tint) == 7)

    def test_all_21_lods_fbx_and_2048_mm_pbr_textures_present(self) -> None:
        models_dir = REPO_ROOT / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models"
        textures_dir = models_dir / "Textures"
        all_assets = list(HARDPOINTS.list_canonical_unit_names()) + list(HARDPOINTS.CANONICAL_FACTORY_NAMES)
        missing_lods = [asset for asset in all_assets if not (models_dir / f"{asset}_LODs.fbx").is_file()]
        self.assertEqual([], missing_lods, f"Missing *_LODs.fbx files: {missing_lods}")

        for asset in all_assets:
            for suffix in ("Albedo", "MetallicGloss", "Normal", "Emission"):
                tex_path = textures_dir / f"{asset}_{suffix}.png"
                self.assertTrue(tex_path.is_file(), f"Missing Level 1 MM texture: {tex_path}")
                with Image.open(tex_path) as im:
                    self.assertEqual((2048, 2048), im.size, f"Expected 2048x2048 texture: {tex_path}")

    def test_unity_scaffolders_and_build_script_enforce_21_asset_parity(self) -> None:
        vpb_src = (REPO_ROOT / "unity/RogueCommandAssetAuthoring/Assets/Editor/VehiclePrefabBuilder.cs").read_text(encoding="utf-8")
        self.assertIn("SetupCompanionLodGroup(", vpb_src)

        tactical_src = (REPO_ROOT / "unity/RogueCommandAssetAuthoring/Assets/Editor/TacticalOperationsPackScaffolder.cs").read_text(encoding="utf-8")
        self.assertIn("SetupCompanionLodGroup(", tactical_src)
        self.assertIn("BulwarkFoundryBuilder.BuildPrefab(", tactical_src)

        sample_src = (REPO_ROOT / "unity/RogueCommandAssetAuthoring/Assets/Editor/RogueCommandVisualSampleScaffolder.cs").read_text(encoding="utf-8")
        self.assertIn("Validated \" + unitPrefabPaths.Length + \" unit prefabs", sample_src)
        self.assertIn("ValidateLevelOnePbrAndLodContract", sample_src)

        ps_src = (REPO_ROOT / "tools/Build-CanonicalVisualModules.ps1").read_text(encoding="utf-8")
        self.assertIn("Validated (?<units>\\d+) unit prefabs and (?<factories>\\d+) factory prefabs", ps_src)


if __name__ == "__main__":
    unittest.main()
