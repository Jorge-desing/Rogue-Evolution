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
        self.assertEqual(fei.classify_form_hierarchy("Deck_FastenerSeat", "fuel_drum_pair"), "primary_faceted")
        self.assertEqual(fei.classify_form_hierarchy("Rear_GussetHinge", "aps_phase_shield"), "primary_faceted")

    def test_vectorized_taubin_smooth_preserves_shape_and_bounds(self) -> None:
        mga = load_tool_module("mesh_geometry_algorithms")
        phi = np.linspace(0.2, np.pi - 0.2, 6)
        theta = np.linspace(0.0, 2.0 * np.pi, 8, endpoint=False)
        pts = []
        for p in phi:
            for t in theta:
                r = 1.0 + (0.04 if (int(round(t * 2)) % 2 == 0) else -0.04)
                pts.append([r * np.sin(p) * np.cos(t), r * np.sin(p) * np.sin(t), r * np.cos(p)])
        verts = np.array(pts, dtype=np.float64)
        tris = []
        n_t = len(theta)
        for i in range(len(phi) - 1):
            for j in range(n_t):
                a = i * n_t + j
                b = i * n_t + ((j + 1) % n_t)
                c = (i + 1) * n_t + j
                d = (i + 1) * n_t + ((j + 1) % n_t)
                tris.extend([[a, b, c], [b, d, c]])
        faces = np.array(tris, dtype=np.int64)
        smoothed = mga.taubin_smooth(verts, faces, iterations=4, lam=0.4, mu=-0.42, preserve_boundary=True)
        self.assertEqual(smoothed.shape, verts.shape)
        self.assertTrue(np.all(np.isfinite(smoothed)))
        # Boundary rings (i == 0 and i == last) must remain fixed when preserve_boundary=True
        self.assertTrue(np.allclose(smoothed[:n_t], verts[:n_t]))

    def test_resolve_level1_soot_anchors_and_postprocess_atlas(self) -> None:
        class _Pos:
            def __init__(self, xyz):
                self.x, self.y, self.z = xyz

        class _Mat:
            def __init__(self, xyz):
                self.translation = _Pos(xyz)

        class _DummySocket:
            def __init__(self, name, xyz):
                self.name = name
                self.matrix_world = _Mat(xyz)

        anchors = fei.resolve_level1_soot_anchors(
            [],
            role="heavy_flak_battery",
            sockets=[
                _DummySocket("Socket_MuzzlePrimary", (0.0, 2.5, 1.1)),
                _DummySocket("Socket_EngineAudio", (0.0, -1.4, 0.7)),
            ],
        )
        self.assertEqual(anchors["muzzle_loc"], (0.0, 2.5, 1.1))
        self.assertEqual(anchors["exhaust_locs"], [(0.0, -1.4, 0.7)])
        self.assertGreater(anchors["soot_intensity"], 0.0)

        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            alb = np.zeros((32, 32, 4), dtype=np.float32)
            alb[8:24, 8:24] = (0.7, 0.2, 0.1, 1.0)
            alb_rgb = (alb[..., :3] * 255.0).astype(np.uint8)
            Image.fromarray(alb_rgb, "RGB").save(tmp_path / "T_Albedo.png")

            nrm = np.full((32, 32, 3), (128, 128, 255), dtype=np.uint8)
            Image.fromarray(nrm, "RGB").save(tmp_path / "T_Normal.png")

            maps = {"albedo": tmp_path / "T_Albedo.png", "normal": tmp_path / "T_Normal.png"}
            res = fei.postprocess_level1_atlas_maps(
                maps,
                albedo_alpha_mask=alb[..., 3],
                dilate_px=4,
                detail_strength=0.35,
            )
            self.assertTrue(res["dilationApplied"])
            self.assertTrue(res["rnmComposed"])
            dilated_alb = np.asarray(Image.open(tmp_path / "T_Albedo.png"))
            # Border gutter within 4px should have dilated island color instead of black
            self.assertGreater(int(dilated_alb[6, 12, 0]), 100)
            composed_nrm = np.asarray(Image.open(tmp_path / "T_Normal.png"))
            self.assertGreater(float(composed_nrm[8:24, 8:24, :2].std()), 0.5)

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
