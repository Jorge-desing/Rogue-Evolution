"""Unit tests for tools/mesh_geometry_algorithms.py."""

from __future__ import annotations

import unittest

import numpy as np

from tests.python.helpers import load_tool_module

mga = load_tool_module("mesh_geometry_algorithms")


def _make_uv_sphere(lat_steps: int = 8, lon_steps: int = 12) -> tuple[np.ndarray, np.ndarray]:
    verts = [[0.0, 0.0, 1.0]]
    for i in range(1, lat_steps):
        phi = np.pi * i / lat_steps
        for j in range(lon_steps):
            theta = 2.0 * np.pi * j / lon_steps
            verts.append([np.sin(phi) * np.cos(theta), np.sin(phi) * np.sin(theta), np.cos(phi)])
    verts.append([0.0, 0.0, -1.0])
    v_arr = np.array(verts, dtype=np.float64)

    faces = []
    # Top cap
    for j in range(lon_steps):
        jn = (j + 1) % lon_steps
        faces.append([0, 1 + j, 1 + jn])
    # Middle quads
    for i in range(lat_steps - 2):
        row0 = 1 + i * lon_steps
        row1 = 1 + (i + 1) * lon_steps
        for j in range(lon_steps):
            jn = (j + 1) % lon_steps
            faces.append([row0 + j, row1 + j, row1 + jn])
            faces.append([row0 + j, row1 + jn, row0 + jn])
    # Bottom cap
    bottom = len(v_arr) - 1
    last_row = 1 + (lat_steps - 2) * lon_steps
    for j in range(lon_steps):
        jn = (j + 1) % lon_steps
        faces.append([bottom, last_row + jn, last_row + j])
    return v_arr, np.array(faces, dtype=np.int64)


def _make_cube() -> tuple[np.ndarray, np.ndarray]:
    verts = np.array(
        [
            [-1, -1, 0],
            [1, -1, 0],
            [1, 1, 0],
            [-1, 1, 0],
            [-1, -1, 2],
            [1, -1, 2],
            [1, 1, 2],
            [-1, 1, 2],
        ],
        dtype=np.float64,
    )
    faces = np.array(
        [
            [0, 2, 1], [0, 3, 2],  # bottom
            [4, 5, 6], [4, 6, 7],  # top
            [0, 1, 5], [0, 5, 4],  # front
            [1, 2, 6], [1, 6, 5],  # right
            [2, 3, 7], [2, 7, 6],  # back
            [3, 0, 4], [3, 4, 7],  # left
        ],
        dtype=np.int64,
    )
    return verts, faces


class MeshGeometryAlgorithmsTests(unittest.TestCase):
    def test_taubin_smooth_preserves_sphere_volume_radius(self) -> None:
        verts, faces = _make_uv_sphere(8, 12)
        rng = np.random.default_rng(42)
        noisy = verts + rng.normal(0.0, 0.03, size=verts.shape)
        smoothed = mga.taubin_smooth(noisy, faces, iterations=8)
        mean_r_before = float(np.mean(np.linalg.norm(noisy, axis=1)))
        mean_r_after = float(np.mean(np.linalg.norm(smoothed, axis=1)))
        self.assertLess(abs(mean_r_after - mean_r_before), 0.06)

    def test_qem_simplify_reduces_triangles_to_target(self) -> None:
        verts, faces = _make_uv_sphere(10, 16)
        target = len(faces) // 2
        s_verts, s_faces = mga.qem_simplify(verts, faces, target_faces=target)
        self.assertLessEqual(len(s_faces), target)
        self.assertGreater(len(s_faces), 0)
        self.assertLess(len(s_verts), len(verts))

    def test_forsyth_vertex_cache_optimization_reduces_or_matches_acmr(self) -> None:
        verts, faces = _make_uv_sphere(10, 16)
        rng = np.random.default_rng(7)
        shuffled = faces[rng.permutation(len(faces))]
        acmr_shuffled = mga.compute_acmr(shuffled, cache_size=32)
        opt_faces, acmr_opt = mga.optimize_vertex_cache_forsyth(shuffled, len(verts), cache_size=32)
        self.assertEqual(len(opt_faces), len(faces))
        self.assertLessEqual(acmr_opt, acmr_shuffled)

    def test_compute_vertex_wear_rgba_contract(self) -> None:
        verts, faces = _make_cube()
        muzzle_anchor = np.array([[0.0, 1.0, 2.0]])
        rgba = mga.compute_vertex_wear_rgba(verts, faces, soot_anchors=muzzle_anchor, soot_radius=1.5)
        self.assertEqual(rgba.shape, (len(verts), 4))
        # Top vertices have higher Cavity AO (R) than bottom vertices
        self.assertGreater(float(rgba[4:8, 0].mean()), float(rgba[0:4, 0].mean()))
        # Sharp 90-degree cube edges have convex edge wear >= 0.7
        self.assertGreaterEqual(float(rgba[:, 1].min()), 0.7)
        # Soot near (0, 1, 2) is positive on top-back vertices and 0 on bottom-front
        self.assertGreater(float(rgba[6, 2]), 0.0)
        self.assertAlmostEqual(float(rgba[0, 2]), 0.0, places=5)
        # Default team mask is 1.0
        self.assertTrue(np.allclose(rgba[:, 3], 1.0))

    def test_compute_mesh_quality_metrics(self) -> None:
        verts, faces = _make_cube()
        metrics = mga.compute_mesh_quality_metrics(verts, faces)
        self.assertEqual(metrics["vertexCount"], 8)
        self.assertEqual(metrics["triangleCount"], 12)
        self.assertEqual(metrics["degenerateTriangles"], 0)
        self.assertEqual(metrics["nonManifoldEdges"], 0)
        self.assertEqual(metrics["boundaryEdges"], 0)


if __name__ == "__main__":
    unittest.main()
