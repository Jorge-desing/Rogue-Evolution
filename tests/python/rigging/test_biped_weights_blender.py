"""Regression checks for rigid binding of authored biped mechanisms."""

import sys
import unittest
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools"))
import RogueModelKit as rmk


class BipedWeightsBlenderTests(unittest.TestCase):
    def test_ik_knees_bend_in_the_same_direction(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        rig = rmk.generate_procedural_armature([], "Walker", leg_type="biped", with_ik=True)
        bpy.context.view_layer.update()
        posed = rig.evaluated_get(bpy.context.evaluated_depsgraph_get())
        left = posed.pose.bones["Bone_Shin_L"].head
        right = posed.pose.bones["Bone_Shin_R"].head
        self.assertAlmostEqual(left.y, right.y, places=4)
        self.assertAlmostEqual(left.z, right.z, places=4)
        self.assertAlmostEqual(left.x, -right.x, places=4)

    def test_leg_side_and_role_win_over_mechanism_words(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        armature = bpy.data.armatures.new("TestArmature")
        rig = bpy.data.objects.new("TestRig", armature)
        bpy.context.scene.collection.objects.link(rig)
        cases = {
            "WalkerLeg_AnkleDamper_R_L": "Bone_Shin_R",
            "WalkerLeg_AnkleDamper_L_R": "Bone_Shin_L",
            "WalkerLeg_HeelClaw_R": "Bone_Foot_R",
            "WalkerLeg_CalfArmor_L": "Bone_Shin_L",
            "WalkerLeg_ThighPiston_L": "Bone_Thigh_L",
            "WalkerLeg_ThighCylinder_L": "Bone_Thigh_L",
            "WalkerLeg_HipActuator_R_OuterBarrel": "Bone_Thigh_R",
        }
        objects = []
        for name in cases:
            mesh = bpy.data.meshes.new(name + "Mesh")
            mesh.from_pydata([(0, 0, 0)], [], [])
            obj = bpy.data.objects.new(name, mesh)
            bpy.context.scene.collection.objects.link(obj)
            objects.append(obj)
        rmk.assign_rigid_mesh_weights(objects, "Walker", rig, leg_type="biped")
        for obj in objects:
            with self.subTest(name=obj.name):
                self.assertEqual([group.name for group in obj.vertex_groups], [cases[obj.name]])
                self.assertEqual(obj.vertex_groups[0].weight(0), 1.0)
                self.assertEqual([mod.object for mod in obj.modifiers if mod.type == "ARMATURE"], [rig])


if __name__ == "__main__":
    unittest.main(argv=[sys.argv[0]])
