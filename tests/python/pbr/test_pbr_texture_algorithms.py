"""Unit tests for tools/pbr_texture_algorithms.py and UV dilation in pack_material_maker_pbr.py."""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

import numpy as np
from PIL import Image

from tests.python.helpers import load_tool_module

pbr_algo = load_tool_module("pbr_texture_algorithms")
packer = load_tool_module("pack_material_maker_pbr")
batch_mm = load_tool_module("batch_material_maker_textures")


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

    def test_compute_and_apply_uv_dilation_map_matches_single_call(self) -> None:
        rgba = np.zeros((32, 32, 4), dtype=np.uint8)
        rgb = np.zeros((32, 32, 3), dtype=np.uint8)
        mask = np.zeros((32, 32), dtype=bool)
        mask[10:22, 10:22] = True
        rgba[10:22, 10:22] = [180, 90, 45, 255]
        rgb[10:22, 10:22] = [30, 210, 120]

        expected_rgba = pbr_algo.dilate_uv_islands(rgba, mask, max_distance=4)
        expected_rgb = pbr_algo.dilate_uv_islands(rgb, mask, max_distance=4)

        dilation_map = pbr_algo.compute_uv_dilation_map(mask, max_distance=4)
        self.assertIsNotNone(dilation_map)
        fill_mask, nearest_y, nearest_x = dilation_map  # type: ignore[misc]
        actual_rgba = pbr_algo.apply_uv_dilation_map(rgba, fill_mask, nearest_y, nearest_x)
        actual_rgb = pbr_algo.apply_uv_dilation_map(rgb, fill_mask, nearest_y, nearest_x)

        self.assertTrue(np.array_equal(expected_rgba, actual_rgba))
        self.assertTrue(np.array_equal(expected_rgb, actual_rgb))

    def test_pack_maps_fast_png_and_in_memory_detail_normal_pixel_identical(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            in_dir = tmp_path / "in"
            out_opt = tmp_path / "out_opt"
            out_fast = tmp_path / "out_fast"
            in_dir.mkdir()

            rng = np.random.default_rng(42)
            albedo = rng.integers(60, 180, size=(32, 32, 4), dtype=np.uint8)
            albedo[..., 3] = 255
            albedo[:6, :6] = 0
            Image.fromarray(albedo, "RGBA").save(in_dir / "Unit_Albedo.png")

            metallic = rng.choice(np.array([0, 255], dtype=np.uint8), size=(32, 32))
            roughness = rng.integers(40, 220, size=(32, 32), dtype=np.uint8)
            Image.fromarray(metallic, "L").save(in_dir / "Unit_Metallic.png")
            Image.fromarray(roughness, "L").save(in_dir / "Unit_Roughness.png")

            base_h = pbr_algo.synthesize_periodic_fbm(32, seed=7, octaves=2)
            base_n = pbr_algo.height_to_normal_scharr(base_h, strength=1.5, as_uint8=True)
            Image.fromarray(base_n, "RGB").save(in_dir / "Unit_Normal.png")

            det_h = pbr_algo.synthesize_periodic_fbm(16, seed=99, octaves=2)
            det_n = pbr_algo.height_to_normal_scharr(det_h, strength=1.2, as_uint8=True)
            det_path = tmp_path / "detail_n.png"
            Image.fromarray(det_n, "RGB").save(det_path)

            emission = np.zeros((32, 32, 3), dtype=np.uint8)
            Image.fromarray(emission, "RGB").save(in_dir / "Unit_Emission.png")

            packer.pack_maps(
                in_dir,
                out_opt,
                "Unit",
                dilate_px=3,
                detail_normal=det_path,
                detail_strength=0.25,
                optimize_png=True,
            )
            packer.pack_maps(
                in_dir,
                out_fast,
                "Unit",
                dilate_px=3,
                detail_strength=0.25,
                compress_level=1,
                optimize_png=False,
                detail_rgb=det_n,
            )

            for suffix in ("Albedo", "MetallicGloss", "Normal", "Emission"):
                a = np.asarray(Image.open(out_opt / f"Unit_{suffix}.png"))
                b = np.asarray(Image.open(out_fast / f"Unit_{suffix}.png"))
                self.assertTrue(np.array_equal(a, b), f"Mismatch in {suffix}")

    def test_batch_material_maker_prepare_and_pack_audit(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            manifest_path = tmp_path / "batch.json"
            summary_path = tmp_path / "summary.json"
            entries = []
            for preset in ("orbital_titanium", "battleship_tungsten"):
                out_dir = tmp_path / preset
                raw_dir = out_dir / "raw"
                template = tmp_path / f"tactical-{preset}-template.ptex"
                entries.append(
                    {
                        "preset": preset,
                        "unitName": f"RCLModule_{preset}",
                        "template": str(template),
                        "outputDir": str(out_dir),
                        "rawDir": str(raw_dir),
                    }
                )
            manifest_path.write_text(json.dumps(entries), encoding="utf-8")

            old_argv = sys.argv
            try:
                sys.argv = [
                    "batch_material_maker_textures.py",
                    "prepare",
                    "--batch-manifest",
                    str(manifest_path),
                    "--size",
                    "5",
                    "--force",
                ]
                self.assertEqual(batch_mm.main(), 0)

                det_h = pbr_algo.synthesize_periodic_fbm(16, seed=13, octaves=2)
                det_n = pbr_algo.height_to_normal_scharr(det_h, strength=1.0, as_uint8=True)
                det_path = tmp_path / "detail.png"
                Image.fromarray(det_n, "RGB").save(det_path)

                for entry in entries:
                    template_path = Path(entry["template"])
                    self.assertTrue(template_path.is_file())
                    prefix = template_path.stem
                    raw_dir = Path(entry["rawDir"])
                    rng = np.random.default_rng(7)
                    albedo = rng.integers(80, 160, size=(32, 32, 3), dtype=np.uint8)
                    Image.fromarray(albedo, "RGB").save(raw_dir / f"{prefix}_Albedo.png")
                    metallic = rng.choice(np.array([0, 255], dtype=np.uint8), size=(32, 32))
                    roughness = rng.integers(50, 200, size=(32, 32), dtype=np.uint8)
                    Image.fromarray(metallic, "L").save(raw_dir / f"{prefix}_Metallic.png")
                    Image.fromarray(roughness, "L").save(raw_dir / f"{prefix}_Roughness.png")
                    base_h = pbr_algo.synthesize_periodic_fbm(32, seed=5, octaves=2)
                    base_n = pbr_algo.height_to_normal_scharr(base_h, strength=1.5, as_uint8=True)
                    Image.fromarray(base_n, "RGB").save(raw_dir / f"{prefix}_Normal.png")
                    emission = np.zeros((32, 32, 3), dtype=np.uint8)
                    Image.fromarray(emission, "RGB").save(raw_dir / f"{prefix}_Emission.png")

                sys.argv = [
                    "batch_material_maker_textures.py",
                    "pack-audit",
                    "--batch-manifest",
                    str(manifest_path),
                    "--workflow",
                    "procedural",
                    "--size",
                    "5",
                    "--detail-normal",
                    str(det_path),
                    "--detail-strength",
                    "0.25",
                    "--workers",
                    "2",
                    "--strict",
                    "--summary-output",
                    str(summary_path),
                ]
                self.assertEqual(batch_mm.main(), 0)
            finally:
                sys.argv = old_argv

            summary = json.loads(summary_path.read_text(encoding="utf-8"))
            self.assertEqual(summary["presetCount"], 2)
            self.assertEqual(summary["resolution"], 32)
            for entry in entries:
                out_dir = Path(entry["outputDir"])
                prefix = Path(entry["template"]).stem
                qr = json.loads((out_dir / f"{prefix}_quality-report.json").read_text(encoding="utf-8"))
                self.assertTrue(qr["qualityGate"]["passed"], qr)
                self.assertTrue((out_dir / "material-maker-workflow.json").is_file())


if __name__ == "__main__":
    unittest.main()
