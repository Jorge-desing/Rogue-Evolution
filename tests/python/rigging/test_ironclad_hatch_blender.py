"""Verify the Ironclad hatch sits on its authored roof and remains rigid."""

import sys
import unittest
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[3]


class IroncladHatchBlenderTests(unittest.TestCase):
    def test_hatch_seat_and_rig(self):
        bpy.ops.wm.open_mainfile(filepath=str(ROOT / "content/source-art/ironclad-walker.blend"))
        arm = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
        arm.data.pose_position = "REST"
        bpy.context.view_layer.update()
        roof = bpy.data.objects["IroncladWalkerArmor_Torso"]
        mesh = roof.to_mesh()
        try:
            tree = BVHTree.FromPolygons([roof.matrix_world @ v.co for v in mesh.vertices],
                                        [list(p.vertices) for p in mesh.polygons])
        finally:
            roof.to_mesh_clear()
        for suffix in ("CoamingRim", "HatchPlate", "HingeBar", "DogLatch"):
            obj = bpy.data.objects["IroncladWalkerArmor_AccessHatch_" + suffix]
            self.assertEqual([g.name for g in obj.vertex_groups], ["Bone_Pelvis"])
            bm = bmesh.new()
            try:
                bm.from_mesh(obj.data)
                self.assertFalse([e for e in bm.edges if not e.is_manifold], suffix)
            finally:
                bm.free()
        rim = bpy.data.objects["IroncladWalkerArmor_AccessHatch_CoamingRim"]
        rim_bottom = min((rim.matrix_world @ Vector(corner)).z for corner in rim.bound_box)
        for x in (-0.2, 0.0, 0.2):
            for y in (-0.19, 0.0, 0.19):
                point, _, _, _ = tree.ray_cast(Vector((x, y, 3.0)), Vector((0, 0, -1)), 3.0)
                self.assertIsNotNone(point)
                self.assertAlmostEqual(point.z, rim_bottom, delta=0.001)
        self.assertEqual(bpy.data.objects["IroncladWalkerArmor_AccessHatch_HatchPlate"].get("RCL_IroncladHatchFit"), 1)


if __name__ == "__main__":
    unittest.main(argv=[sys.argv[0]])
