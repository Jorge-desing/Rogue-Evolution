"""Blender-side regression checks for the authored Arachnid Hexapod rig."""

from __future__ import annotations

import unittest
from pathlib import Path

import bpy


REPO_ROOT = Path(__file__).resolve().parents[3]
SOURCE_BLEND = REPO_ROOT / "content/source-art/arachnidhexapod-rigged.blend"
LEG_TAGS = ("FL", "FR", "ML", "MR", "RL", "RR")


class ArachnidHexapodBlenderRigContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        if not SOURCE_BLEND.is_file():
            raise FileNotFoundError(SOURCE_BLEND)
        bpy.ops.wm.open_mainfile(filepath=str(SOURCE_BLEND))
        cls.armature = bpy.data.objects.get("ArachnidHexapod_Armature")
        if cls.armature is None:
            raise AssertionError("The rigged source must contain ArachnidHexapod_Armature")
        cls.meshes = {
            obj.name: obj
            for obj in bpy.context.scene.objects
            if obj.type == "MESH" and not obj.name.lower().startswith("preview")
        }
        cls.walk = bpy.data.actions.get("ArachnidHexapod_Action_Walk")
        cls.idle = bpy.data.actions.get("ArachnidHexapod_Action_Idle")

    def test_each_of_six_legs_has_a_deforming_bone_chain(self) -> None:
        bones = self.armature.data.bones
        for tag in LEG_TAGS:
            with self.subTest(leg=tag):
                for segment in ("Thigh", "Shin", "Foot"):
                    bone = bones.get(f"Bone_{segment}_{tag}")
                    self.assertIsNotNone(bone, f"missing Bone_{segment}_{tag}")
                    self.assertTrue(bone.use_deform, f"Bone_{segment}_{tag} must deform its rigid mesh parts")

    def test_each_leg_mesh_is_rigidly_bound_to_its_own_leg(self) -> None:
        expected_groups = {
            "ArachnidHexapodCrawlerLeg_L_F_FemurBoom": "Bone_Thigh_FL",
            "ArachnidHexapodCrawlerLeg_R_F_FemurBoom": "Bone_Thigh_FR",
            "ArachnidHexapodCrawlerLeg_L_R_FemurBoom": "Bone_Thigh_RL",
            "ArachnidHexapodCrawlerLeg_R_R_FemurBoom": "Bone_Thigh_RR",
            "ArachnidHexapodArmor_MidThigh_ML": "Bone_Thigh_ML",
            "ArachnidHexapodArmor_MidThigh_MR": "Bone_Thigh_MR",
            "ArachnidHexapodArmor_MidShin_ML": "Bone_Shin_ML",
            "ArachnidHexapodArmor_MidShin_MR": "Bone_Shin_MR",
            "ArachnidHexapodJoint_MidFootPad_ML": "Bone_Foot_ML",
            "ArachnidHexapodJoint_MidFootPad_MR": "Bone_Foot_MR",
            "ArachnidHexapodPiston_Quad_FL_OuterBarrel": "Bone_Thigh_FL",
            "ArachnidHexapodPiston_Quad_RR_OuterBarrel": "Bone_Thigh_RR",
            "ArachnidHexapodHydraulicRam_LF": "Bone_Thigh_FL",
            "ArachnidHexapodHydraulicRam_MR": "Bone_Thigh_MR",
        }
        for mesh_name, expected_group in expected_groups.items():
            with self.subTest(mesh=mesh_name):
                mesh = self.meshes.get(mesh_name)
                self.assertIsNotNone(mesh, f"missing authored leg mesh {mesh_name}")
                self.assertEqual(
                    [expected_group],
                    [group.name for group in mesh.vertex_groups],
                    f"{mesh_name} must be rigidly bound to its corresponding leg bone",
                )
                armature_modifiers = [modifier for modifier in mesh.modifiers if modifier.type == "ARMATURE"]
                self.assertEqual(1, len(armature_modifiers), f"{mesh_name} needs exactly one armature modifier")
                self.assertIs(self.armature, armature_modifiers[0].object)

    def test_all_render_meshes_have_complete_rigid_weights(self) -> None:
        """Every exported production mesh must be fully and rigidly bound."""
        for mesh_name, mesh in self.meshes.items():
            if mesh.name.endswith(("_COLLIDER", "_ShieldBubble")):
                continue
            with self.subTest(mesh=mesh_name):
                self.assertEqual(
                    1,
                    len(mesh.vertex_groups),
                    f"{mesh_name} must have exactly one rigid vertex group",
                )
                group = mesh.vertex_groups[0]
                self.assertEqual(1, len(mesh.modifiers), f"{mesh_name} must only carry its armature modifier")
                self.assertEqual("ARMATURE", mesh.modifiers[0].type)
                self.assertIs(self.armature, mesh.modifiers[0].object)
                for vertex in mesh.data.vertices:
                    assignments = [
                        assignment
                        for assignment in vertex.groups
                        if assignment.group == group.index and assignment.weight > 1e-6
                    ]
                    self.assertEqual(1, len(assignments), f"{mesh_name} vertex {vertex.index} is not assigned")
                    self.assertAlmostEqual(1.0, assignments[0].weight, places=5)

    def test_walk_animates_all_six_leg_chains_in_place(self) -> None:
        self.assertIsNotNone(self.walk)
        animated_bones = {
            curve.data_path.split('"')[1]
            for curve in self.walk.fcurves
            if curve.data_path.startswith('pose.bones["')
            and any(abs(float(point.co.y)) > 1e-6 for point in curve.keyframe_points)
        }
        for tag in LEG_TAGS:
            with self.subTest(leg=tag):
                for segment in ("Thigh", "Shin", "Foot"):
                    self.assertIn(f"Bone_{segment}_{tag}", animated_bones)
        root_location = [
            curve
            for curve in self.walk.fcurves
            if curve.data_path == 'pose.bones["Bone_Root"].location'
        ]
        for curve in root_location:
            self.assertTrue(all(abs(float(point.co.y)) <= 1e-6 for point in curve.keyframe_points))

    def test_idle_does_not_animate_any_leg_bones(self) -> None:
        self.assertIsNotNone(self.idle)
        animated_bones = {
            curve.data_path.split('"')[1]
            for curve in self.idle.fcurves
            if curve.data_path.startswith('pose.bones["')
            and any(abs(float(point.co.y)) > 1e-6 for point in curve.keyframe_points)
        }
        self.assertFalse(
            any(bone.startswith(("Bone_Thigh_", "Bone_Shin_", "Bone_Foot_")) for bone in animated_bones),
            "Idle may animate the hull, but must not keep cycling the leg gait while stopped",
        )


if __name__ == "__main__":
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(ArachnidHexapodBlenderRigContractTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    raise SystemExit(not result.wasSuccessful())
