"""Regression for Arc Lance's pre-skinning damper clearance replay."""
import sys
import unittest
from pathlib import Path
import bpy
from mathutils.bvhtree import BVHTree
ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / 'tools'))
import RogueModelKit as rmk
from ironclad_damper_clearance_recipe import cut_biped_damper_clearance

class ArcLanceDamperClearanceTests(unittest.TestCase):
    def test_clearance_and_joint_contacts_through_walk(self):
        bpy.ops.wm.open_mainfile(filepath=str(ROOT / 'content/source-art/arclancewalker-v2.blend'))
        cut_biped_damper_clearance('ArcLanceWalker')
        meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and not o.name.lower().startswith('preview')]
        rig=rmk.generate_procedural_armature(meshes,prefix='ArcLanceWalker',leg_type='biped',hip_height=1.42,hip_loc_l=(-.45,-.04,1.42),hip_loc_r=(.45,-.04,1.42),knee_loc_l=(-.55,-.27,.84),knee_loc_r=(.55,-.27,.84),foot_loc_l=(-.52,.18,0),foot_loc_r=(.52,.18,0),with_ik=False)
        act=rmk.generate_procedural_action_clips(rig,prefix='ArcLanceWalker',leg_type='biped',locomotion_only=False)['Walk']
        rig.animation_data.action=act
        if act.slots: rig.animation_data.action_slot=act.slots[0]
        for track in rig.animation_data.nla_tracks: track.mute=True
        for frame in range(1,41):
            bpy.context.scene.frame_set(frame); dg=bpy.context.evaluated_depsgraph_get()
            for side in ('L','R'):
                trees={}
                names=['ShinArmor_'+side,'CalfArmor_'+side,'ShinBone_'+side,'AnkleDamper_'+side+'_L','AnkleDamper_'+side+'_R','AnkleJoint_'+side,'KneeJoint_'+side]
                for part in names:
                    obj=bpy.data.objects['ArcLanceWalkerLeg_'+part].evaluated_get(dg); mesh=obj.to_mesh()
                    trees[part]=BVHTree.FromPolygons([obj.matrix_world@v.co for v in mesh.vertices],[tuple(p.vertices) for p in mesh.polygons]); obj.to_mesh_clear()
                for tag in ('L','R'):
                    damper=trees['AnkleDamper_'+side+'_'+tag]
                    for part in names[:3]: self.assertFalse(damper.overlap(trees[part]),(frame,side,tag,part))
                    for part in names[-2:]: self.assertTrue(damper.overlap(trees[part]),(frame,side,tag,part))

if __name__=='__main__': unittest.main(argv=[sys.argv[0]])
