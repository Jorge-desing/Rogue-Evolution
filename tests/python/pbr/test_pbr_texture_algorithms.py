"""Unit tests for tools/pbr_texture_algorithms.py and UV dilation in pack_material_maker_pbr.py."""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import numpy as np
from PIL import Image

from tests.python.helpers import load_tool_module

pbr_algo = load_tool_module("pbr_texture_algorithms")
packer = load_tool_module("pack_material_maker_pbr")
batch_mm = load_tool_module("batch_material_maker_textures")
functional = load_tool_module("functional_engineering_integration")


class PbrTextureAlgorithmsTests(unittest.TestCase):
    def test_decode_normal_respects_uint8_encoding_even_when_channels_are_zero_or_one(self) -> None:
        byte_pixels = np.array([[[0, 1, 1]]], dtype=np.uint8)
        byte_decoded = pbr_algo._decode_normal_rgb(byte_pixels)
        expected = byte_pixels.astype(np.float32) / 127.5 - 1.0
        self.assertTrue(np.array_equal(byte_decoded, expected))

        normalized_pixels = byte_pixels.astype(np.float32) / 255.0
        normalized_decoded = pbr_algo._decode_normal_rgb(normalized_pixels)
        self.assertTrue(np.allclose(normalized_decoded, byte_decoded, atol=1e-7))

    def test_rnm_rejects_shape_and_strength_errors(self) -> None:
        base = np.full((4, 4, 3), [128, 128, 255], dtype=np.uint8)
        detail = base.copy()
        with self.assertRaisesRegex(ValueError, "shapes must match"):
            pbr_algo.blend_normals_rnm(base, detail[:2], detail_strength=0.5)
        with self.assertRaisesRegex(ValueError, "0 through 1"):
            pbr_algo.blend_normals_rnm(base, detail, detail_strength=1.01)
        with self.assertRaisesRegex(TypeError, "real number"):
            pbr_algo.blend_normals_rnm(base, detail, detail_strength="0.5")  # type: ignore[arg-type]
        with self.assertRaisesRegex(ValueError, "finite"):
            pbr_algo.blend_normals_rnm(base, detail, detail_strength=float("nan"))

    def test_rnm_integration_rejects_implicit_dtype_conversion(self) -> None:
        base = np.full((4, 4, 3), 128, dtype=np.uint8)
        with self.assertRaisesRegex(TypeError, "detail_rgb must be a uint8"):
            functional.compose_normals_rnm_rgb(base, base.astype(np.float32))

    def test_failed_rnm_is_reported_instead_of_returning_unprocessed_normal(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            source = Path(tmp) / "normal.png"
            Image.fromarray(np.full((4, 4, 3), [128, 128, 255], dtype=np.uint8), "RGB").save(source)
            with patch(
                "tools.functional_engineering_integration.compose_normals_rnm_rgb",
                side_effect=RuntimeError("RNM implementation failed"),
            ):
                with self.assertRaisesRegex(RuntimeError, "RNM implementation failed"):
                    packer._build_normal_image(
                        source,
                        detail_rgb=np.full((2, 2, 3), 128, dtype=np.uint8),
                        detail_strength=0.5,
                    )

    def test_invalid_pack_options_fail_before_creating_output_directory(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            out_dir = Path(tmp) / "not-created"
            with self.assertRaisesRegex(ValueError, "dilation radius must be >= 0"):
                packer.pack_maps(Path(tmp) / "missing-input", out_dir, "Unit", dilate_px=-1)
            self.assertFalse(out_dir.exists())
            with self.assertRaisesRegex(ValueError, "simple file-name component"):
                packer.pack_maps(Path(tmp) / "missing-input", out_dir, "../Unit")
            self.assertFalse(out_dir.exists())

    def test_binary_metallic_mask_sampling_does_not_invent_midtones(self) -> None:
        rng = np.random.default_rng(314)
        mask = rng.choice(np.array([0, 255], dtype=np.uint8), size=(32, 32))
        self.assertEqual(packer.metallic_midtone_ratio(Image.fromarray(mask, "L")), 0.0)

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

    def test_batch_preflight_rejects_external_raw_dir_before_cleaning_any_entry(self) -> None:
        with tempfile.TemporaryDirectory() as tmp, tempfile.TemporaryDirectory() as outside:
            root = Path(tmp)
            first_output = root / "preset_a"
            first_raw = first_output / "raw"
            first_raw.mkdir(parents=True)
            sentinel = first_raw / "keep.txt"
            sentinel.write_text("preserve", encoding="utf-8")
            entries = [
                {
                    "preset": "preset_a",
                    "template": str(root / "a.ptex"),
                    "outputDir": str(first_output),
                    "rawDir": str(first_raw),
                },
                {
                    "preset": "preset_b",
                    "template": str(root / "b.ptex"),
                    "outputDir": str(root / "preset_b"),
                    "rawDir": str(Path(outside) / "raw"),
                },
            ]
            manifest = root / "batch.json"
            manifest.write_text(json.dumps(entries), encoding="utf-8")
            args = type("Args", (), {
                "config": Path(batch_mm.REPO_ROOT) / "tools/material-maker/rogue-command-material-maker.json",
                "batch_manifest": manifest,
                "artifact_root": root,
                "template_root": root,
                "force": True,
                "size": 5,
            })()
            with self.assertRaisesRegex(ValueError, "rawDir must be"):
                batch_mm.cmd_prepare(args)
            self.assertEqual(sentinel.read_text(encoding="utf-8"), "preserve")

    def test_batch_preflight_rejects_template_inside_cleaned_raw_directory(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            output = root / "preset"
            raw = output / "raw"
            raw.mkdir(parents=True)
            sentinel = raw / "template.ptex"
            sentinel.write_text("user data", encoding="utf-8")
            manifest = root / "batch.json"
            manifest.write_text(
                json.dumps(
                    [{
                        "preset": "preset",
                        "template": str(sentinel),
                        "outputDir": str(output),
                        "rawDir": str(raw),
                    }]
                ),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(ValueError, "cannot be inside an output directory"):
                batch_mm._load_batch_entries(
                    manifest,
                    artifact_root=root,
                    template_root=root,
                )
            self.assertEqual(sentinel.read_text(encoding="utf-8"), "user data")

    def test_batch_preflight_rejects_manifest_inside_output_directory(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            output = root / "preset"
            output.mkdir()
            manifest = output / "batch.json"
            manifest.write_text(
                json.dumps(
                    [{
                        "preset": "preset",
                        "template": str(root / "preset.ptex"),
                        "outputDir": str(output),
                        "rawDir": str(output / "raw"),
                    }]
                ),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(ValueError, "manifest cannot be inside"):
                batch_mm._load_batch_entries(
                    manifest,
                    artifact_root=root,
                    template_root=root,
                )

    def test_strict_batch_audit_fails_on_pack_warnings(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            output = root / "output"
            raw = output / "raw"
            output.mkdir()
            raw.mkdir()
            template = root / "strict.ptex"
            template.write_text("fixture", encoding="utf-8")
            for suffix in ("Albedo", "MetallicGloss", "Normal", "Emission"):
                (output / f"strict_{suffix}.png").write_bytes(b"fixture")
            config = {
                "workflows": {"procedural": {"sourceAuthority": "graph", "postProcess": "pack"}},
                "exportProfile": {"name": "RogueCommand"},
                "textureContract": {
                    "pipeline": "test",
                    "shader": "test",
                    "requiredMaps": [{"suffix": name} for name in ("Albedo", "MetallicGloss", "Normal", "Emission")],
                },
            }
            entry = {
                "preset": "test",
                "template": template,
                "outputDir": output,
                "rawDir": raw,
            }
            quality = {"dimensions": [[4, 4]], "qualityGate": {"passed": False, "failures": ["quality failure"]}}
            with patch.object(batch_mm, "pack_maps", return_value={"warnings": ["preserved pack warning"]}), patch.object(
                batch_mm, "build_quality_report", return_value=quality
            ):
                with self.assertRaises(RuntimeError) as raised:
                    batch_mm._process_single_pack_audit(
                        entry,
                        config=config,
                        workflow_name="procedural",
                        requested_resolution=4,
                        dilate_px=0,
                        detail_rgb=None,
                        detail_strength=0.5,
                        strict=True,
                        optimize_png=False,
                    )
                self.assertIn("quality failure", str(raised.exception))
                self.assertIn("preserved pack warning", str(raised.exception))

    def test_batch_audit_reports_failures_from_every_parallel_preset(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            entries = []
            for preset in ("broken_a", "broken_b"):
                output = root / preset
                (output / "raw").mkdir(parents=True)
                template = root / f"{preset}.ptex"
                template.write_text("fixture", encoding="utf-8")
                entries.append(
                    {
                        "preset": preset,
                        "template": str(template),
                        "outputDir": str(output),
                        "rawDir": str(output / "raw"),
                    }
                )
            manifest = root / "batch.json"
            manifest.write_text(json.dumps(entries), encoding="utf-8")
            args = type("Args", (), {
                "size": 5,
                "dilate_px": 0,
                "detail_strength": 0.35,
                "workers": 2,
                "config": Path(batch_mm.REPO_ROOT) / "tools/material-maker/rogue-command-material-maker.json",
                "batch_manifest": manifest,
                "artifact_root": root,
                "template_root": root,
                "workflow": "procedural",
                "detail_normal": None,
                "strict": False,
                "optimize_png": False,
                "summary_output": None,
            })()
            with self.assertRaises(RuntimeError) as raised:
                batch_mm.cmd_pack_audit(args)
            message = str(raised.exception)
            self.assertIn("broken_a", message)
            self.assertIn("broken_b", message)
            self.assertIn("Material Maker did not export", message)


if __name__ == "__main__":
    unittest.main()
