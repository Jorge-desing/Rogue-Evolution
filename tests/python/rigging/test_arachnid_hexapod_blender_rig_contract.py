"""Blender-side regression checks for the authored Arachnid Hexapod rig."""

from __future__ import annotations

import unittest
from pathlib import Path
import sys

import bpy


REPO_ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPO_ROOT / "tools"))
import RogueModelKit as rmk  # noqa: E402

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

    def test_middle_leg_bones_remain_connected_at_ground_contact(self) -> None:
        """Middle shin and foot rest matrices must share a joint and keep the pad above Z=0."""
        bones = self.armature.data.bones
        for tag in ("ML", "MR"):
            with self.subTest(leg=tag):
                shin = bones[f"Bone_Shin_{tag}"]
                foot = bones[f"Bone_Foot_{tag}"]
                for axis in range(3):
                    self.assertAlmostEqual(
                        shin.tail_local[axis],
                        foot.head_local[axis],
                        places=5,
                        msg=f"Bone_Foot_{tag} must start at Bone_Shin_{tag} tail on axis {axis}",
                    )
                self.assertGreaterEqual(foot.head_local.z, 0.0)
                self.assertLess(foot.tail_local.z, foot.head_local.z)

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

    def test_new_middle_knee_and_axle_names_bind_to_middle_shins(self) -> None:
        expected = {
            "ArachnidHexapodJoint_MidKnee_L": "Bone_Shin_ML",
            "ArachnidHexapodJoint_MidKneeAxle_L": "Bone_Shin_ML",
            "ArachnidHexapodJoint_MidKnee_R": "Bone_Shin_MR",
            "ArachnidHexapodJoint_MidKneeAxle_R": "Bone_Shin_MR",
        }
        temporary_objects = []
        try:
            for name in expected:
                mesh = bpy.data.meshes.new(name + "_Mesh")
                mesh.from_pydata(
                    ((0.0, 0.0, 0.0), (0.05, 0.0, 0.0), (0.0, 0.05, 0.0)),
                    (),
                    ((0, 1, 2),),
                )
                obj = bpy.data.objects.new(name, mesh)
                bpy.context.scene.collection.objects.link(obj)
                temporary_objects.append(obj)

            rmk.assign_rigid_mesh_weights(
                temporary_objects,
                "ArachnidHexapod",
                self.armature,
                leg_type="quad",
            )

            for obj in temporary_objects:
                with self.subTest(mesh=obj.name):
                    self.assertEqual([expected[obj.name]], [group.name for group in obj.vertex_groups])
                    modifiers = [modifier for modifier in obj.modifiers if modifier.type == "ARMATURE"]
                    self.assertEqual(1, len(modifiers))
                    self.assertIs(self.armature, modifiers[0].object)
        finally:
            for obj in temporary_objects:
                mesh = obj.data
                bpy.data.objects.remove(obj, do_unlink=True)
                if mesh.users == 0:
                    bpy.data.meshes.remove(mesh)

    def test_leg_attachments_do_not_bind_to_idle_hull(self) -> None:
        """Leg hardware must not inherit the hull's stationary breathing curve."""
        leg_prefixes = (
            "ArachnidHexapodCrawlerLeg_",
            "ArachnidHexapodPiston_Quad_",
            "ArachnidHexapodHydraulicRam_",
            "ArachnidHexapodHydraulicRod_",
        )
        for mesh_name, mesh in self.meshes.items():
            if not mesh_name.startswith(leg_prefixes):
                continue
            with self.subTest(mesh=mesh_name):
                self.assertNotEqual(
                    "Bone_Hull",
                    mesh.vertex_groups[0].name,
                    f"{mesh_name} must not inherit Bone_Hull idle motion",
                )

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

    def test_export_does_not_mix_animation_actions_through_nla(self) -> None:
        self.assertIsNotNone(self.armature.animation_data)
        self.assertEqual(
            len(self.armature.animation_data.nla_tracks),
            0,
            "Arachnid clips must export independently; overlapping NLA strips reanimate legs in Idle",
        )

    def test_leg_chains_do_not_use_runtime_ik_in_idle(self) -> None:
        """Authored gait clips, not a live IK solver, own the six leg poses."""
        pose_bones = self.armature.pose.bones
        for tag in LEG_TAGS:
            with self.subTest(leg=tag):
                shin = pose_bones.get(f"Bone_Shin_{tag}")
                self.assertIsNotNone(shin)
                self.assertFalse(
                    any(constraint.type == "IK" for constraint in shin.constraints),
                    f"Bone_Shin_{tag} must not solve an IK pose while Idle is active",
                )

    def test_idle_keys_neutral_pose_for_all_leg_chains(self) -> None:
        """Idle must author a rest pose, not inherit the last generated gait pose."""
        self.assertIsNotNone(self.idle)
        for tag in LEG_TAGS:
            for segment in ("Thigh", "Shin", "Foot"):
                bone_name = f"Bone_{segment}_{tag}"
                for channel in ("location", "rotation_euler"):
                    with self.subTest(bone=bone_name, channel=channel):
                        curves = [
                            curve
                            for curve in self.idle.fcurves
                            if curve.data_path == f'pose.bones["{bone_name}"].{channel}'
                        ]
                        self.assertEqual(3, len(curves), f"Idle must key all {channel} channels for {bone_name}")
                        self.assertTrue(
                            all(abs(float(point.co.y)) <= 1e-6 for curve in curves for point in curve.keyframe_points),
                            f"Idle must keep {bone_name} at its authored rest pose",
                        )

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

    def test_idle_does_not_animate_any_leg_ancestors(self) -> None:
        """Idle body breathing must not be inherited by the six leg chains."""
        self.assertIsNotNone(self.idle)
        animated_bones = {
            curve.data_path.split('"')[1]
            for curve in self.idle.fcurves
            if curve.data_path.startswith('pose.bones["')
            and any(abs(float(point.co.y)) > 1e-6 for point in curve.keyframe_points)
        }
        leg_ancestors = set()
        for tag in LEG_TAGS:
            for segment in ("Thigh", "Shin", "Foot"):
                bone = self.armature.data.bones.get(f"Bone_{segment}_{tag}")
                while bone is not None:
                    bone = bone.parent
                    if bone is not None:
                        leg_ancestors.add(bone.name)
        self.assertFalse(
            animated_bones.intersection(leg_ancestors),
            "Idle body curves must not move an ancestor of any leg chain",
        )


if __name__ == "__main__":
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(ArachnidHexapodBlenderRigContractTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    raise SystemExit(not result.wasSuccessful())
