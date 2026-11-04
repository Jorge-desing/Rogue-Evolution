"""Regression tests for Material Maker PBR channel packing."""

from __future__ import annotations

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


if __name__ == "__main__":
    unittest.main()
