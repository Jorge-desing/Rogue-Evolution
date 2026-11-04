"""Unit test for tools/apply_material_maker_to_blender.py in Blender 4.5."""

from __future__ import annotations

import shutil
import tempfile
import unittest
from pathlib import Path

from PIL import Image

from tests.python.helpers import load_tool_module

APPLY_TOOL = load_tool_module("apply_material_maker_to_blender")
BLENDER_CANDIDATES = [
    shutil.which("blender"),
    r"C:\Program Files\Blender Foundation\Blender 4.5\blender.exe",
]
BLENDER = next((c for c in BLENDER_CANDIDATES if c and Path(c).is_file()), None)


@unittest.skipIf(BLENDER is None, "Blender 4.5 is not installed")
class ApplyMaterialMakerToBlenderTests(unittest.TestCase):
    def test_apply_textures_binds_pbr_material(self) -> None:
        model_source = Path("content/source-art/skyguardsam-rigged.blend")
        if not model_source.is_file():
            self.skipTest(f"Source model not found: {model_source}")

        with tempfile.TemporaryDirectory() as tmp_dir:
            tmp_root = Path(tmp_dir)
            tex_dir = tmp_root / "textures"
            tex_dir.mkdir()
            prefix = "TestUnit"

            # Create dummy 4-map contract
            size = (64, 64)
            Image.new("RGB", size, (120, 140, 160)).save(tex_dir / f"{prefix}_Albedo.png")
            Image.new("RGBA", size, (220, 220, 220, 180)).save(tex_dir / f"{prefix}_MetallicGloss.png")
            Image.new("RGB", size, (128, 128, 255)).save(tex_dir / f"{prefix}_Normal.png")
            Image.new("RGB", size, (0, 0, 0)).save(tex_dir / f"{prefix}_Emission.png")

            output_blend = tmp_root / "output.blend"
            ok = APPLY_TOOL.apply_textures(
                model_path=model_source,
                texture_dir=tex_dir,
                prefix=prefix,
                output_blend=output_blend,
            )
            self.assertTrue(ok)
            self.assertTrue(output_blend.is_file())


if __name__ == "__main__":
    unittest.main()
