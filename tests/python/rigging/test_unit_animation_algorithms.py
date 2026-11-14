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


if __name__ == "__main__":
    unittest.main()
