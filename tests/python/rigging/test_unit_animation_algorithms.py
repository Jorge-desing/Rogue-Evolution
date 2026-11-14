"""Unit and static contract tests for layered unit animation tools."""

from __future__ import annotations

import unittest
from pathlib import Path

from tests.python.helpers import load_tool_module

ROOT = Path(__file__).resolve().parents[3]
uaa = load_tool_module("unit_animation_algorithms")


class UnitAnimationAlgorithmsTests(unittest.TestCase):
    def test_quadratic_bezier_endpoints_and_apex(self) -> None:
        p0 = (0.0, 0.0, 0.0)
        p1 = (0.0, 0.04, 0.18)
        p2 = (0.0, 0.0, 0.0)
        self.assertEqual(uaa.evaluate_quadratic_bezier_point(0.0, p0, p1, p2), p0)
        self.assertEqual(uaa.evaluate_quadratic_bezier_point(1.0, p0, p1, p2), p2)
        mid = uaa.evaluate_quadratic_bezier_point(0.5, p0, p1, p2)
        self.assertAlmostEqual(mid[1], 0.02, places=6)
        self.assertAlmostEqual(mid[2], 0.09, places=6)

    def test_gait_phase_offsets_cover_biped_and_multi_leg(self) -> None:
        self.assertEqual(uaa.get_gait_phase_offsets("biped"), {"L": 0.5, "R": 0.0})
        quad = uaa.get_gait_phase_offsets("quad")
        self.assertEqual(quad["FL"], 0.5)
        self.assertEqual(quad["FR"], 0.0)
        self.assertEqual(quad["RL"], 0.0)
        self.assertEqual(quad["RR"], 0.5)
        hexapod = uaa.get_gait_phase_offsets("hexapod")
        self.assertEqual(hexapod["ML"], 0.0)
        self.assertEqual(hexapod["MR"], 0.5)

    def test_sample_foot_step_arc_frames_grounding_lift_and_loop_closure(self) -> None:
        frames = (1, 5, 10, 15, 20, 25, 30, 35, 40)
        right = uaa.sample_foot_step_arc_frames(
            frames=frames,
            cycle_length=40,
            phase_offset=0.0,
            duty_factor=0.5,
            step_height=0.09,
            stride_forward=0.04,
        )
        left = uaa.sample_foot_step_arc_frames(
            frames=frames,
            cycle_length=40,
            phase_offset=0.5,
            duty_factor=0.5,
            step_height=0.09,
            stride_forward=0.04,
        )

        # Exact loop closure frame 1 == frame 40
        self.assertEqual(right[1], right[40])
        self.assertEqual(left[1], left[40])

        # Strict non-negative Z (zero ground penetration) and stance grounding at Z=0
        for f in frames:
            self.assertGreaterEqual(right[f][2], 0.0)
            self.assertGreaterEqual(left[f][2], 0.0)

        # Half-cycle alternation: when right is planted (frames 1..15), left lifts; peak lift ~0.09
        self.assertEqual(right[1], (0.0, 0.0, 0.0))
        self.assertEqual(right[10], (0.0, 0.0, 0.0))
        self.assertGreater(right[30][2], 0.07)
        self.assertGreater(left[10][2], 0.07)

    def test_second_order_response_converges_and_exhibits_anticipation_overshoot(self) -> None:
        targets = [1.0] * 120
        damped = uaa.evaluate_second_order_response(
            targets, dt=1.0 / 60.0, f=4.0, z=0.95, r=0.0, initial_value=0.0
        )
        bouncy = uaa.evaluate_second_order_response(
            targets, dt=1.0 / 60.0, f=4.0, z=0.35, r=1.5, initial_value=0.0
        )
        self.assertAlmostEqual(damped[-1], 1.0, places=2)
        self.assertAlmostEqual(bouncy[-1], 1.0, places=2)
        self.assertGreater(max(bouncy), 1.05)

    def test_micro_vibration_envelope_vanishes_at_boundaries_and_anchors(self) -> None:
        self.assertEqual(uaa.evaluate_micro_vibration_envelope(1, 1, 60, (1, 60)), 0.0)
        self.assertEqual(uaa.evaluate_micro_vibration_envelope(60, 1, 60, (1, 60)), 0.0)
        self.assertEqual(uaa.evaluate_micro_vibration_envelope(12, 1, 30, (1, 4, 12, 30)), 0.0)
        mid = uaa.evaluate_micro_vibration_envelope(21, 1, 30, (1, 4, 12, 30))
        self.assertAlmostEqual(mid, 1.0, places=5)

    def test_unity_animator_layer_and_addon_contracts(self) -> None:
        prefab_builder = (
            ROOT / "unity/RogueCommandAssetAuthoring/Assets/Editor/VehiclePrefabBuilder.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("RCL_SpeedMultiplier", prefab_builder)
        self.assertIn("speedParameterActive = true", prefab_builder)
        self.assertIn("UpperBodyFireRecoil", prefab_builder)
        self.assertIn("AnimatorLayerBlendingMode.Additive", prefab_builder)

        addon_script = (
            ROOT / "tools/Setup-BlenderAuthoringAddons.ps1"
        ).read_text(encoding="utf-8")
        self.assertIn("wiggle_2", addon_script)
        self.assertIn("SingleFile", addon_script)

    def test_all_18_unit_animator_controllers_serialize_layered_contract(self) -> None:
        assets_dir = ROOT / "unity/RogueCommandAssetAuthoring/Assets"
        controllers = sorted(assets_dir.rglob("*Controller.controller"))
        self.assertEqual(len(controllers), 18)
        for controller_path in controllers:
            yaml_text = controller_path.read_text(encoding="utf-8")
            self.assertIn("RCL_IsMoving", yaml_text, msg=str(controller_path))
            self.assertIn("RCL_SpeedMultiplier", yaml_text, msg=str(controller_path))
            self.assertIn("m_DefaultFloat: 1", yaml_text, msg=str(controller_path))
            self.assertIn("RCL_Fire", yaml_text, msg=str(controller_path))
            self.assertIn("RCL_Death", yaml_text, msg=str(controller_path))
            self.assertIn("UpperBodyFireRecoil", yaml_text, msg=str(controller_path))
            self.assertIn("m_BlendingMode: 1", yaml_text, msg=str(controller_path))

    def test_detect_foot_sliding_passes_bezier_walk_and_catches_slide_and_penetration(self) -> None:
        frames = tuple(range(1, 41))
        samples = uaa.sample_foot_step_arc_frames(
            frames=frames,
            cycle_length=40,
            phase_offset=0.0,
            duty_factor=0.5,
            step_height=0.09,
            stride_forward=0.04,
        )
        clean_track = [samples[f] for f in frames]
        clean_res = uaa.detect_foot_sliding(clean_track)
        self.assertTrue(clean_res["passed"])
        self.assertEqual(clean_res["sliding_frames"], [])
        self.assertEqual(clean_res["penetration_frames"], [])

        # Introduce horizontal foot slide during grounded stance
        sliding_track = list(clean_track)
        sliding_track[2] = (0.0, 0.025, 0.0)
        slide_res = uaa.detect_foot_sliding(sliding_track)
        self.assertFalse(slide_res["passed"])
        self.assertIn(2, slide_res["sliding_frames"])
        self.assertGreater(slide_res["total_slide_distance"], 0.02)

        # Introduce floor penetration (Z < -0.005)
        pen_track = list(clean_track)
        pen_track[4] = (0.0, 0.0, -0.02)
        pen_res = uaa.detect_foot_sliding(pen_track)
        self.assertFalse(pen_res["passed"])
        self.assertIn(4, pen_res["penetration_frames"])
        self.assertAlmostEqual(pen_res["max_penetration_depth"], 0.02, places=5)

    def test_audit_loop_closure_and_root_motion_zero(self) -> None:
        p1 = {"Bone_Hull": (0.0, 0.0, 0.5), "Bone_Foot_L": (0.1, 0.0, 0.0)}
        p2 = {"Bone_Hull": (0.0, 0.0, 0.5), "Bone_Foot_L": (0.1, 0.0, 0.0)}
        p_bad = {"Bone_Hull": (0.0, 0.0, 0.55), "Bone_Foot_L": (0.1, 0.0, 0.0)}
        self.assertTrue(uaa.audit_loop_closure(p1, p2)["passed"])
        bad_loop = uaa.audit_loop_closure(p1, p_bad)
        self.assertFalse(bad_loop["passed"])
        self.assertAlmostEqual(bad_loop["max_delta"], 0.05, places=5)

        self.assertTrue(uaa.audit_root_motion_zero([(0.0, 0.0, 0.0)] * 10)["passed"])
        drift = uaa.audit_root_motion_zero([(0.0, 0.0, 0.0), (0.0, 0.1, 0.0)])
        self.assertFalse(drift["passed"])
        self.assertEqual(drift["violating_frames"], [1])


if __name__ == "__main__":
    unittest.main()
