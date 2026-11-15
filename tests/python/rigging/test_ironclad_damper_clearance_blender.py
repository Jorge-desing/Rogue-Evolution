"""Validate Ironclad damper clearances and rigid weights in Walk poses."""
import sys
import unittest
from pathlib import Path
import bpy
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[3]

class IroncladDamperClearanceTests(unittest.TestCase):
    def test_dampers_clear_shins_and_reach_both_joints(self):
        bpy.ops.wm.open_mainfile(filepath=str(ROOT / 'content/source-art/ironclad-walker.blend'))
        arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
        act = bpy.data.actions['IroncladWalker_Action_Walk']
        arm.animation_data.action = act
        if act.slots:
            arm.animation_data.action_slot = act.slots[0]
        for track in arm.animation_data.nla_tracks:
            track.mute = True
        for side in ('L', 'R'):
            for part in ('ShinArmor', 'CalfArmor', 'ShinBone'):
                obj = bpy.data.objects[f'IroncladWalkerLeg_{part}_{side}']
                group = obj.vertex_groups[f'Bone_Shin_{side}']
                self.assertTrue(all(abs(group.weight(v.index) - 1.0) < 1e-6 for v in obj.data.vertices))
        for frame in range(1, 41):
            bpy.context.scene.frame_set(frame)
            dg = bpy.context.evaluated_depsgraph_get()
            for side in ('L', 'R'):
                trees = {}
                parts = ('ShinArmor', 'CalfArmor', 'ShinBone', 'AnkleDamper_'+side+'_L', 'AnkleDamper_'+side+'_R', 'AnkleJoint_'+side, 'KneeJoint_'+side)
                for part in parts:
                    name = f'IroncladWalkerLeg_{part}' + (f'_{side}' if part in parts[:3] else '')
                    obj = bpy.data.objects[name].evaluated_get(dg)
                    mesh = obj.to_mesh()
                    vertices = [obj.matrix_world @ v.co for v in mesh.vertices]
                    trees[part] = BVHTree.FromPolygons(vertices, [tuple(p.vertices) for p in mesh.polygons])
                    obj.to_mesh_clear()
                for tag in ('L', 'R'):
                    damper = trees['AnkleDamper_'+side+'_'+tag]
                    for part in parts[:3]:
                        self.assertFalse(damper.overlap(trees[part]), (frame, side, tag, part))
                    self.assertTrue(damper.overlap(trees['AnkleJoint_'+side]), (frame, side, tag, 'ankle'))
                    self.assertTrue(damper.overlap(trees['KneeJoint_'+side]), (frame, side, tag, 'knee'))

if __name__ == '__main__':
    unittest.main(argv=[sys.argv[0]])
