"""Regression contract for readable, body-driven attack clips."""

from __future__ import annotations

import ast
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[3]
MODEL_KIT = ROOT / "tools" / "RogueModelKit.py"
VEHICLE_BUILDER = (
    ROOT
    / "unity/RogueCommandAssetAuthoring/Assets/Editor/VehiclePrefabBuilder.cs"
)


class AttackClipAuthoringContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.model_kit_text = MODEL_KIT.read_text(encoding="utf-8")
        cls.model_kit_tree = ast.parse(cls.model_kit_text, filename=str(MODEL_KIT))
        cls.action_function = next(
            node
            for node in cls.model_kit_tree.body
            if isinstance(node, ast.FunctionDef)
            and node.name == "generate_procedural_action_clips"
        )
        cls.action_source = ast.get_source_segment(cls.model_kit_text, cls.action_function) or ""
        fire_start = cls.action_source.index("# 3. Action_FireRecoil")
        fire_end = cls.action_source.index("# 4. Action_Death", fire_start)
        cls.fire_source = cls.action_source[fire_start:fire_end]
        cls.builder_text = VEHICLE_BUILDER.read_text(encoding="utf-8")

    def test_fire_recoil_contains_body_anticipation_and_recovery_keys(self) -> None:
        """FireRecoil must move the chassis/pelvis, not only the barrel."""
        self.assertRegex(
            self.fire_source,
            r'set_bone_key\(body_bone,\s*"location"',
            "FireRecoil must key body translation for a readable recoil impulse",
        )
        self.assertIn("anticipation", self.action_source.lower())
        self.assertIn("follow-through", self.action_source.lower())
        self.assertIn("recovery", self.action_source.lower())

    def test_fire_recoil_has_secondary_turret_motion(self) -> None:
        """The attack pose must include pitch/settling motion beside barrel slide."""
        self.assertRegex(
            self.fire_source,
            r'set_bone_key\("Bone_TurretPitch",\s*"rotation_euler"',
        )
        self.assertRegex(
            self.fire_source,
            r'set_bone_key\("Bone_BarrelRecoil",\s*"location"',
        )

    def test_fire_recoil_does_not_key_gameplay_root_motion(self) -> None:
        """Recoil may compress the body but must not move the gameplay root."""
        self.assertNotRegex(self.fire_source, r'set_bone_key\("Bone_Root"')

    def test_controller_rebuilds_a_canonical_state_graph(self) -> None:
        """Rebuilds must discard stale duplicate imported states/transitions."""
        self.assertIn("stateMachine.RemoveState", self.builder_text)
        self.assertIn("stateMachine.RemoveAnyStateTransition", self.builder_text)
        self.assertIn("FindAnimationClipByPriority", self.builder_text)
        self.assertIn("HasBodyDrivenFireRecoilClip", self.builder_text)

    def test_builder_replaces_constant_imported_fire_clip_with_runtime_body_clip(self) -> None:
        """Static bindings with constant values must not be accepted as working recoil."""
        self.assertIn("CreateBodyDrivenFireRecoilClip", self.builder_text)
        self.assertIn("AnimationUtility.GetEditorCurve", self.builder_text)
        self.assertIn("Mathf.Abs", self.builder_text)
        self.assertIn("AnimationClip generatedClip", self.builder_text)

    def test_runtime_recoil_fallback_uses_monotonic_key_times(self) -> None:
        """Synthesized fallback keys must end at frame 14 after frame 11."""
        self.assertIn(
            "new[] { 0f, 1f, 2f, 4f, 8f, 11f, 14f }[index] / frameRate",
            self.builder_text,
        )
        self.assertNotIn(
            "(offsets.Length - 1) / frameRate",
            self.builder_text,
        )


if __name__ == "__main__":
    unittest.main()
