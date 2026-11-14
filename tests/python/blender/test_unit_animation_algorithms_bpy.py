"""Blender 4.5 bpy integration tests for procedural Bezier gaits and micro-vibration F-Curves."""

from __future__ import annotations

import unittest

import bpy

from tests.python.helpers import load_tool_module

rkit = load_tool_module("RogueModelKit")
uaa = load_tool_module("unit_animation_algorithms")


def _build_test_armature(name: str, leg_bones: list[str]):
    arm_data = bpy.data.armatures.new(name + "_Data")
    arm_obj = bpy.data.objects.new(name, arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode="EDIT")

    root = arm_data.edit_bones.new("Bone_Root")
    root.head = (0.0, 0.0, 0.0)
    root.tail = (0.0, 0.0, 0.4)

    chassis = arm_data.edit_bones.new("Bone_Chassis")
    chassis.head = (0.0, 0.0, 0.4)
    chassis.tail = (0.0, 0.0, 1.2)
    chassis.parent = root

    turret = arm_data.edit_bones.new("Bone_Turret")
    turret.head = (0.0, 0.0, 1.2)
    turret.tail = (0.0, 0.4, 1.2)
    turret.parent = chassis

    barrel = arm_data.edit_bones.new("Bone_Barrel")
    barrel.head = (0.0, 0.4, 1.2)
    barrel.tail = (0.0, 1.1, 1.2)
    barrel.parent = turret

    radar = arm_data.edit_bones.new("Bone_Radar")
    radar.head = (0.0, -0.3, 1.3)
    radar.tail = (0.0, -0.3, 1.6)
    radar.parent = chassis

    for idx, bone_name in enumerate(leg_bones):
        foot = arm_data.edit_bones.new(bone_name)
        side = -0.5 if idx % 2 == 0 else 0.5
        foot.head = (side, 0.0, 0.0)
        foot.tail = (side, 0.2, 0.0)
        foot.parent = root

    bpy.ops.object.mode_set(mode="OBJECT")
    return arm_obj


class UnitAnimationAlgorithmsBpyTests(unittest.TestCase):
    def setUp(self) -> None:
        bpy.ops.wm.read_factory_settings(use_empty=True)

    def test_biped_procedural_clips_bezier_and_root_invariant(self) -> None:
        arm_obj = _build_test_armature("TestBiped", ["Bone_Foot_L", "Bone_Foot_R"])
        rkit.generate_procedural_action_clips(arm_obj, "TestBiped", leg_type="biped")

        actions = {a.name: a for a in bpy.data.actions}
        for expected in (
            "TestBiped_Action_Idle",
            "TestBiped_Action_Walk",
            "TestBiped_Action_FireRecoil",
            "TestBiped_Action_Death",
        ):
            self.assertIn(expected, actions)

        # Verify zero Bone_Root translation across all actions
        for action in actions.values():
            for fc in action.fcurves:
                if fc.data_path == 'pose.bones["Bone_Root"].location':
                    for kp in fc.keyframe_points:
                        self.assertAlmostEqual(kp.co[1], 0.0, places=5)

        # Verify Bone_Foot_L Bezier Z >= 0 across Action_Walk and positive lift apex
        walk_action = actions["TestBiped_Action_Walk"]
        z_curve = next(
            (
                fc
                for fc in walk_action.fcurves
                if fc.data_path == 'pose.bones["Bone_Foot_L"].location' and fc.array_index == 2
            ),
            None,
        )
        self.assertIsNotNone(z_curve)
        z_values = [kp.co[1] for kp in z_curve.keyframe_points]
        self.assertTrue(all(v >= -1e-5 for v in z_values))
        self.assertGreater(max(z_values), 0.05)

    def test_quad_procedural_clips_and_root_exclusion(self) -> None:
        arm_obj = _build_test_armature(
            "TestQuad", ["Bone_Foot_FL", "Bone_Foot_FR", "Bone_Foot_RL", "Bone_Foot_RR"]
        )
        rkit.generate_procedural_action_clips(arm_obj, "TestQuad", leg_type="quad")

        idle_action = bpy.data.actions["TestQuad_Action_Idle"]
        before_root_curves = [
            len(fc.keyframe_points)
            for fc in idle_action.fcurves
            if 'pose.bones["Bone_Root"]' in fc.data_path
        ]
        # Passing Bone_Root explicitly must still skip Bone_Root completely
        inserted = uaa.apply_baked_fcurve_micro_vibration_bpy(
            arm_obj,
            idle_action,
            bone_names=("Bone_Root", "Bone_Radar"),
            frame_start=1,
            frame_end=30,
            amplitude_rad=0.006,
            frequency_cycles=2.0,
            anchor_frames=(1, 30),
        )
        self.assertGreater(inserted, 0)
        after_root_curves = [
            len(fc.keyframe_points)
            for fc in idle_action.fcurves
            if 'pose.bones["Bone_Root"]' in fc.data_path
        ]
        self.assertEqual(before_root_curves, after_root_curves)


if __name__ == "__main__":
    unittest.main()
