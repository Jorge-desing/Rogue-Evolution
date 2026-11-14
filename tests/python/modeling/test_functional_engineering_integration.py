"""Unit tests for tools/functional_engineering_integration.py and --detail-normal packing."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

import numpy as np
from PIL import Image

from tests.python.helpers import CONTENT_DIR, load_tool_module

fei = load_tool_module("functional_engineering_integration")
pbr_algo = load_tool_module("pbr_texture_algorithms")
packer = load_tool_module("pack_material_maker_pbr")


class FunctionalEngineeringIntegrationTests(unittest.TestCase):
    def test_role_engineering_specs_cover_all_canonical_roles(self) -> None:
        roles_path = CONTENT_DIR / "source-art" / "canonical-visual-module-roles.json"
        registry = json.loads(roles_path.read_text(encoding="utf-8"))
        declared_roles = set(registry["roleDefinitions"].keys())
        self.assertTrue(declared_roles.issubset(set(fei.ROLE_ENGINEERING_SPECS.keys())))

    def test_classify_form_hierarchy(self) -> None:
        self.assertEqual(fei.classify_form_hierarchy("UnitArmor_UpperPlate", "armor_cassette"), "primary_faceted")
        self.assertEqual(fei.classify_form_hierarchy("UnitCast_RadomeShell", "aps_phase_shield"), "primary_cast")
        self.assertEqual(fei.classify_form_hierarchy("Unit_AuxDrum", "fuel_drum_pair"), "primary_cast")

    def test_compose_canonical_pbr_detail_and_packer_integration(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            in_dir = tmp_path / "in"
            out_dir = tmp_path / "out"
            in_dir.mkdir()

            albedo = np.full((32, 32, 3), 120, dtype=np.uint8)
            albedo[:16, :16] = 180
            Image.fromarray(albedo, "RGB").save(in_dir / "Unit_Albedo.png")

            gray = np.full((32, 32), 60, dtype=np.uint8)
            gray[:16, :16] = 220
            Image.fromarray(gray, "L").save(in_dir / "Unit_Metallic.png")
            Image.fromarray(255 - gray, "L").save(in_dir / "Unit_Roughness.png")

            base_n = np.zeros((32, 32, 3), dtype=np.uint8)
            base_n[..., 0] = 128
            base_n[..., 1] = 128
            base_n[..., 2] = 255
            Image.fromarray(base_n, "RGB").save(in_dir / "Unit_Normal.png")
            Image.fromarray(np.zeros((32, 32, 3), dtype=np.uint8), "RGB").save(in_dir / "Unit_Emission.png")

            detail_h = pbr_algo.synthesize_periodic_fbm(16, seed=17, octaves=2, anisotropy=2.0)
            detail_n = pbr_algo.height_to_normal_scharr(detail_h, strength=2.5, as_uint8=True)
            detail_path = tmp_path / "detail_normal.png"
            Image.fromarray(detail_n, "RGB").save(detail_path)

            rc = packer.main_for_test(
                in_dir, out_dir, "Unit", strict=False, detail_normal=detail_path, detail_strength=0.6
            )
            self.assertEqual(rc, 0)
            out_n = np.asarray(Image.open(out_dir / "Unit_Normal.png"))
            self.assertEqual(out_n.shape, (32, 32, 3))
            # Detail normal composition must introduce non-flat X/Y variation
            self.assertGreater(float(out_n[..., :2].std()), 1.0)


if __name__ == "__main__":
    unittest.main()
