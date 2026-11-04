"""Regression tests for Material Maker PBR channel packing."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from PIL import Image

from tests.python.helpers import load_tool_module

PACKER = load_tool_module("pack_material_maker_pbr")


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

    def test_flags_non_unit_normals(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            self._write_raw(root / "raw", (8, 8))
            Image.new("RGBA", (8, 8), (255, 255, 255, 255)).save(root / "raw" / "Hull_Normal.png")
            PACKER.main_for_test(root / "raw", root / "out", "Hull")
            report = json.loads((root / "out" / "Hull_pack-report.json").read_text(encoding="utf-8"))
            self.assertTrue(any("unit length" in w for w in report["warnings"]))


if __name__ == "__main__":
    unittest.main()
