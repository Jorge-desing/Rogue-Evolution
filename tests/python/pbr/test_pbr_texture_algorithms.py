"""Unit tests for tools/pbr_texture_algorithms.py and UV dilation in pack_material_maker_pbr.py."""

from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

import numpy as np
from PIL import Image

from tests.python.helpers import load_tool_module

pbr_algo = load_tool_module("pbr_texture_algorithms")
packer = load_tool_module("pack_material_maker_pbr")


class PbrTextureAlgorithmsTests(unittest.TestCase):
    def test_dilate_uv_islands_extends_edge_without_overwriting_interior(self) -> None:
        img = np.zeros((16, 16, 3), dtype=np.uint8)
        mask = np.zeros((16, 16), dtype=bool)
        mask[6:10, 6:10] = True
        img[6:10, 6:10] = [200, 120, 40]

        dilated = pbr_algo.dilate_uv_islands(img, mask, max_distance=3)
        self.assertTrue(np.array_equal(dilated[6:10, 6:10], img[6:10, 6:10]))
        self.assertTrue(np.array_equal(dilated[4, 8], [200, 120, 40]))
        # Farther than max_distance stays 0
        self.assertTrue(np.array_equal(dilated[0, 0], [0, 0, 0]))

    def test_blend_normals_rnm_preserves_unit_length_and_outward_z(self) -> None:
        h = pbr_algo.synthesize_periodic_fbm(32, seed=7, octaves=3)
        base = pbr_algo.height_to_normal_scharr(h, strength=1.5, as_uint8=True)
        detail = pbr_algo.height_to_normal_scharr(
            pbr_algo.synthesize_periodic_fbm(32, seed=99, octaves=2, anisotropy=4.0),
            strength=2.0,
            as_uint8=True,
        )
        blended = pbr_algo.blend_normals_rnm(base, detail, detail_strength=0.8)
        self.assertEqual(blended.shape, (32, 32, 3))
        self.assertEqual(blended.dtype, np.uint8)

        vec = blended.astype(np.float32) / 127.5 - 1.0
        lengths = np.sqrt(np.sum(vec * vec, axis=-1))
        self.assertLess(float(np.max(np.abs(lengths - 1.0))), 0.03)
        self.assertGreater(float(np.min(vec[..., 2])), 0.0)

    def test_height_to_curvature_cavity_detects_ridge_and_valley(self) -> None:
        h = np.zeros((16, 16), dtype=np.float32)
        h[4, :] = 1.0  # ridge
        h[12, :] = -1.0  # valley
        wear, cavity = pbr_algo.height_to_curvature_cavity(h, tileable=False, gain=1.0)
        self.assertGreater(float(wear[4, 8]), 0.5)
        self.assertLess(float(cavity[12, 8]), 0.6)

    def test_worley_plates_and_reaction_diffusion_bounded(self) -> None:
        f1, seams = pbr_algo.synthesize_worley_plates(32, seed=11, cell_count=9)
        self.assertEqual(f1.shape, (32, 32))
        self.assertGreaterEqual(float(f1.min()), 0.0)
        self.assertLessEqual(float(f1.max()), 1.0)
        self.assertGreater(float(seams.max()), 0.5)

        rd = pbr_algo.synthesize_reaction_diffusion(size=32, seed=5, steps=30)
        self.assertEqual(rd.shape, (32, 32))
        self.assertGreaterEqual(float(rd.min()), 0.0)
        self.assertLessEqual(float(rd.max()), 1.0)

    def test_stochastic_tile_sample_shape_and_range(self) -> None:
        tex = pbr_algo.synthesize_periodic_fbm(32, seed=3)
        sampled = pbr_algo.stochastic_tile_sample(tex, out_size=48, seed=12, cell_scale=3.0)
        self.assertEqual(sampled.shape, (48, 48))
        self.assertGreaterEqual(float(sampled.min()), 0.0)
        self.assertLessEqual(float(sampled.max()), 1.0)

    def test_normal_to_curvature_and_triplanar_blend(self) -> None:
        h = pbr_algo.synthesize_periodic_fbm(32, seed=21, octaves=3)
        nrm = pbr_algo.height_to_normal_scharr(h, strength=2.0, as_uint8=True)
        wear, cavity = pbr_algo.normal_to_curvature_cavity(nrm, tileable=True, gain=2.5)
        self.assertEqual(wear.shape, (32, 32))
        self.assertEqual(cavity.shape, (32, 32))
        self.assertGreater(float(wear.max()), 0.05)
        self.assertLess(float(cavity.min()), 0.95)

        pos = np.random.default_rng(1).uniform(-2.0, 2.0, size=(64, 3)).astype(np.float32)
        norms = np.tile([0.0, 0.0, 1.0], (64, 1)).astype(np.float32)
        projected = pbr_algo.triplanar_project_blend(h, pos, norms, scale=0.5, sharpness=4.0)
        self.assertEqual(projected.shape, (64,))
        self.assertGreaterEqual(float(projected.min()), 0.0)
        self.assertLessEqual(float(projected.max()), 1.0)

    def test_pack_maps_with_optional_uv_dilation(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            in_dir = tmp_path / "in"
            out_dir = tmp_path / "out"
            in_dir.mkdir()

            # Albedo with alpha island
            albedo = np.zeros((32, 32, 4), dtype=np.uint8)
            albedo[8:24, 8:24] = [140, 150, 160, 255]
            Image.fromarray(albedo, "RGBA").save(in_dir / "Unit_Albedo.png")

            gray = np.full((32, 32), 128, dtype=np.uint8)
            gray[8:16, 8:16] = 220
            Image.fromarray(gray, "L").save(in_dir / "Unit_Metallic.png")
            Image.fromarray(255 - gray, "L").save(in_dir / "Unit_Roughness.png")

            normal = np.zeros((32, 32, 3), dtype=np.uint8)
            normal[..., 0] = 128
            normal[..., 1] = 128
            normal[..., 2] = 255
            Image.fromarray(normal, "RGB").save(in_dir / "Unit_Normal.png")

            emission = np.zeros((32, 32, 3), dtype=np.uint8)
            Image.fromarray(emission, "RGB").save(in_dir / "Unit_Emission.png")

            rc = packer.main_for_test(in_dir, out_dir, "Unit", strict=False, dilate_px=4)
            self.assertEqual(rc, 0)
            packed_albedo = np.asarray(Image.open(out_dir / "Unit_Albedo.png"))
            # Border pixel 2px outside island should be dilated from island color
            self.assertGreater(int(packed_albedo[6, 16, 0]), 100)


if __name__ == "__main__":
    unittest.main()
