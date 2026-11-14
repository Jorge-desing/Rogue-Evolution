"""Blender 4.5 bpy integration tests for tools/mesh_geometry_algorithms.py."""

from __future__ import annotations

import unittest

import bpy
import numpy as np

from tests.python.helpers import load_tool_module

mga = load_tool_module("mesh_geometry_algorithms")


class MeshGeometryAlgorithmsBpyTests(unittest.TestCase):
    def setUp(self) -> None:
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def test_bpy_smooth_simplify_cache_and_vertex_wear(self) -> None:
        bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, radius=1.0)
        obj = bpy.context.active_object
        self.assertIsNotNone(obj)

        # 1. Taubin smooth
        smoothed = mga.apply_taubin_smooth_bpy(obj, iterations=3)
        self.assertEqual(len(smoothed), len(obj.data.vertices))

        # 2. QEM simplify to 50%
        orig_tris, new_tris = mga.apply_qem_simplify_bpy(obj, ratio=0.5)
        self.assertGreater(orig_tris, new_tris)
        self.assertGreater(new_tris, 0)

        # 3. Forsyth cache optimization
        acmr = mga.apply_forsyth_cache_optimize_bpy(obj, cache_size=32)
        self.assertGreater(acmr, 0.0)

        # 4. Vertex-wear RGBA attribute write
        rgba = mga.apply_vertex_wear_rgba_bpy(
            obj, soot_anchors=np.array([[0.0, 1.0, 0.0]]), soot_radius=0.8
        )
        self.assertEqual(rgba.shape[1], 4)
        color_attr = obj.data.color_attributes.get("Color")
        self.assertIsNotNone(color_attr)
        self.assertGreater(len(color_attr.data), 0)


if __name__ == "__main__":
    unittest.main()
