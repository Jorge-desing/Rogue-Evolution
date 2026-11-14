"""Regression tests for Material Maker PBR channel packing."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from PIL import Image
import numpy as np

from tests.python.helpers import load_tool_module

PACKER = load_tool_module("pack_material_maker_pbr")
HYBRID = load_tool_module("build_canonical_level1_textures")


class MaterialMakerPbrPackingTests(unittest.TestCase):
    def test_packs_metallic_rgb_and_inverted_roughness_alpha(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            raw = root / "raw"
            output = root / "output"
            raw.mkdir()
            size = (2, 2)
            Image.new("RGBA", size, (12, 34, 56, 255)).save(raw / "Hull_Albedo.png")
            Image.new("L", size, 180).save(raw / "Hull_Metallic.png")
            Image.new("L", size, 64).save(raw / "Hull_Roughness.png")
            Image.new("RGBA", size, (128, 128, 255, 255)).save(raw / "Hull_Normal.png")
            Image.new("RGBA", size, (0, 0, 0, 255)).save(raw / "Hull_Emission.png")

            result = PACKER.main_for_test(raw, output, "Hull")

            self.assertEqual(0, result)
            with Image.open(output / "Hull_MetallicGloss.png") as packed:
                self.assertEqual("RGBA", packed.mode)
                self.assertEqual((180, 180, 180, 191), packed.getpixel((0, 0)))
            self.assertTrue((output / "Hull_Albedo.png").is_file())
            self.assertTrue((output / "Hull_Normal.png").is_file())
            self.assertTrue((output / "Hull_Emission.png").is_file())

    def _write_raw(self, raw: Path, size: tuple[int, int], roughness: int = 64) -> None:
        raw.mkdir(exist_ok=True)
        Image.new("RGBA", size, (90, 100, 70, 128)).save(raw / "Hull_Albedo.png")
        Image.new("L", size, 255).save(raw / "Hull_Metallic.png")
        Image.new("L", size, roughness).save(raw / "Hull_Roughness.png")
        Image.new("RGBA", size, (128, 128, 255, 255)).save(raw / "Hull_Normal.png")
        Image.new("RGBA", size, (0, 0, 0, 255)).save(raw / "Hull_Emission.png")

    def test_caps_smoothness_and_strips_albedo_alpha(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            self._write_raw(root / "raw", (4, 4), roughness=0)
            PACKER.main_for_test(root / "raw", root / "out", "Hull")
            with Image.open(root / "out" / "Hull_MetallicGloss.png") as packed:
                self.assertEqual(PACKER.MAX_SMOOTHNESS, packed.getpixel((0, 0))[3])
            with Image.open(root / "out" / "Hull_Albedo.png") as albedo:
                self.assertEqual("RGB", albedo.mode)

    def test_report_flags_flat_and_non_power_of_two_maps(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            self._write_raw(root / "raw", (6, 6))
            result = PACKER.main_for_test(root / "raw", root / "out", "Hull", strict=True)
            self.assertEqual(2, result)
            report = json.loads((root / "out" / "Hull_pack-report.json").read_text(encoding="utf-8"))
            joined = " ".join(report["warnings"])
            self.assertIn("non-power-of-two", joined)
            self.assertIn("flat", joined)

    def test_renormalizes_non_unit_normals_before_quality_report(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            self._write_raw(root / "raw", (8, 8))
            Image.new("RGBA", (8, 8), (255, 255, 255, 255)).save(root / "raw" / "Hull_Normal.png")
            PACKER.main_for_test(root / "raw", root / "out", "Hull")
            report = json.loads((root / "out" / "Hull_pack-report.json").read_text(encoding="utf-8"))
            self.assertLess(report["stats"]["Normal"]["lengthError"], 0.02)
            self.assertFalse(any("unit length" in w for w in report["warnings"]))

    def test_hybrid_uv_region_composer_preserves_semantic_pbr_zones(self) -> None:
        triangles = np.array(
            [
                [0.05, 0.55, 0.45, 0.55, 0.25, 0.95],  # primary armor (top-left)
                [0.55, 0.55, 0.95, 0.55, 0.75, 0.95],  # gunmetal (top-right)
                [0.05, 0.05, 0.45, 0.05, 0.25, 0.45],  # tread (bottom-left)
                [0.55, 0.05, 0.95, 0.05, 0.75, 0.45],  # emissive (bottom-right)
            ],
            dtype=np.float32,
        )
        regions = np.array(
            [
                HYBRID.REGION_PRIMARY_ARMOR,
                HYBRID.REGION_GUNMETAL,
                HYBRID.REGION_TREAD,
                HYBRID.REGION_EMISSIVE,
            ],
            dtype=np.uint8,
        )
        mask = HYBRID.rasterize_uv_region_mask(triangles, regions, resolution=64)
        self.assertEqual(mask.shape, (64, 64))
        self.assertGreater(int(np.count_nonzero(mask == HYBRID.REGION_PRIMARY_ARMOR)), 100)
        self.assertGreater(int(np.count_nonzero(mask == HYBRID.REGION_GUNMETAL)), 100)
        self.assertGreater(int(np.count_nonzero(mask == HYBRID.REGION_TREAD)), 100)
        self.assertGreater(int(np.count_nonzero(mask == HYBRID.REGION_EMISSIVE)), 100)

        primary_tiles = {
            "Albedo": np.full((64, 64, 3), 120, dtype=np.uint8),
            "Metallic": np.full((64, 64), 240, dtype=np.uint8),
            "Roughness": np.full((64, 64), 80, dtype=np.uint8),
            "Normal": np.tile(np.array([128, 128, 255], dtype=np.uint8), (64, 64, 1)),
            "Emission": np.zeros((64, 64, 3), dtype=np.uint8),
        }
        gunmetal_tiles = {
            "Albedo": np.full((64, 64, 3), 75, dtype=np.uint8),
            "Metallic": np.full((64, 64), 245, dtype=np.uint8),
            "Roughness": np.full((64, 64), 90, dtype=np.uint8),
            "Normal": np.tile(np.array([128, 128, 255], dtype=np.uint8), (64, 64, 1)),
            "Emission": np.zeros((64, 64, 3), dtype=np.uint8),
        }
        composed = HYBRID.compose_hybrid_scalar_maps(
            mask,
            primary_tiles,
            gunmetal_tiles,
            primary_tint_rgb=(72, 110, 145),
            warning_rgb=(226, 148, 28),
            emissive_rgb=(30, 220, 255),
        )
        tread_mask = mask == HYBRID.REGION_TREAD
        emissive_mask = mask == HYBRID.REGION_EMISSIVE
        self.assertEqual(int(np.max(composed["Metallic"][tread_mask])), 0)
        self.assertGreater(int(np.min(composed["Roughness"][tread_mask])), 170)
        self.assertGreater(int(np.mean(composed["Emission"][emissive_mask, 2])), 200)
        self.assertEqual(int(np.max(composed["Emission"][tread_mask])), 0)


if __name__ == "__main__":
    unittest.main()
