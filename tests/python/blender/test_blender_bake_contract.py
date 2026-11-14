"""Blender 4.5 bake contract: data maps are Non-Color, albedo keeps the full base color."""

from __future__ import annotations

import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

from PIL import Image

BLENDER_CANDIDATES = [
    shutil.which("blender"),
    r"C:\Program Files\Blender Foundation\Blender 4.5\blender.exe",
]
BLENDER = next((c for c in BLENDER_CANDIDATES if c and Path(c).is_file()), None)
SCRIPT = Path(__file__).with_name("bake_smoke_blender.py")


@unittest.skipIf(BLENDER is None, "Blender 4.5 is not installed")
class BlenderBakeContractTests(unittest.TestCase):
    def test_bake_preserves_pbr_contract(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            result = subprocess.run(
                [BLENDER, "-b", "--factory-startup", "--python", str(SCRIPT), "--", temp_dir],
                capture_output=True, text=True, timeout=300,
            )
            self.assertIn("BAKED", result.stdout, result.stdout[-2000:] + result.stderr[-2000:])
            out = Path(temp_dir)
            with Image.open(out / "Smoke_MetallicGloss.png") as m:
                r, _, _, a = m.convert("RGBA").getpixel((128, 128))
            # Principled Metallic=0.5 / Roughness=0.5 must survive as linear data (not sRGB-encoded).
            self.assertAlmostEqual(128, r, delta=3)
            self.assertAlmostEqual(127, a, delta=3)
            with Image.open(out / "Smoke_Normal.png") as n:
                self.assertEqual((128, 128, 255), n.convert("RGB").getpixel((128, 128)))
            with Image.open(out / "Smoke_Albedo.png") as alb:
                px = alb.convert("RGB").getpixel((128, 128))
            # Base color (0.2, 0.4, 0.1) in sRGB, not multiplied by (1 - metallic).
            for got, want in zip(px, (124, 170, 89)):
                self.assertAlmostEqual(want, got, delta=4)


if __name__ == "__main__":
    unittest.main()
