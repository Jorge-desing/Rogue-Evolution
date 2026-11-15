"""Regression for vertex-group preservation during mesh cache optimization."""

import sys
import unittest
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools"))
import mesh_geometry_algorithms as mga


class ForsythWeightsBlenderTests(unittest.TestCase):
    def test_rebuild_preserves_named_weights(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.mesh.primitive_cube_add()
        obj = bpy.context.object
        left = obj.vertex_groups.new(name="Bone_Foot_L")
        right = obj.vertex_groups.new(name="Bone_Foot_R")
        for vertex in obj.data.vertices:
            (left if vertex.index % 2 == 0 else right).add([vertex.index], 1.0, "REPLACE")
        mga.apply_forsyth_cache_optimize_bpy(obj)
        self.assertEqual([group.name for group in obj.vertex_groups], ["Bone_Foot_L", "Bone_Foot_R"])
        for vertex in obj.data.vertices:
            expected = "Bone_Foot_L" if vertex.index % 2 == 0 else "Bone_Foot_R"
            self.assertEqual(obj.vertex_groups[expected].weight(vertex.index), 1.0)


if __name__ == "__main__":
    unittest.main(argv=[sys.argv[0]])
