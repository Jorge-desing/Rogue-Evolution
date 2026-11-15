"""Contract tests for the unified 6-phase Animation Validation Matrix."""

from __future__ import annotations

import ast
import unittest
from pathlib import Path

from tests.python.helpers import load_tool_module

ROOT = Path(__file__).resolve().parents[3]
vfap = load_tool_module("validate_fbx_animation_pipeline")
ram = load_tool_module("run_animation_validation_matrix")


class AnimationValidationMatrixTests(unittest.TestCase):
    def test_canonical_rigged_units_match_refresh_script_18_units(self) -> None:
        refresh_src = (ROOT / "tools/Refresh-UnitProceduralAnimations.py").read_text(
            encoding="utf-8"
        )
        tree = ast.parse(refresh_src)
        refresh_units: list[str] = []
        for node in ast.walk(tree):
            if isinstance(node, ast.AnnAssign) and isinstance(node.target, ast.Name):
                if node.target.id == "UNIT_SPECS" and isinstance(node.value, ast.List):
                    for elt in node.value.elts:
                        if isinstance(elt, ast.Tuple) and elt.elts:
                            first = elt.elts[0]
                            if isinstance(first, ast.Constant) and isinstance(first.value, str):
                                refresh_units.append(first.value)
        self.assertEqual(len(refresh_units), 18)
        self.assertEqual(sorted(vfap.CANONICAL_RIGGED_UNITS), sorted(refresh_units))

    def test_classify_action_role_avoids_walker_false_positive(self) -> None:
        self.assertEqual(vfap.classify_action_role("IroncladWalker_Action_Idle"), "idle")
        self.assertEqual(vfap.classify_action_role("IroncladWalker_Action_Walk"), "locomotion")
        self.assertEqual(vfap.classify_action_role("IroncladWalker_Action_Drive"), "locomotion")
        self.assertEqual(
            vfap.classify_action_role("IroncladWalker_Action_FireRecoil"), "fire_recoil"
        )
        self.assertEqual(vfap.classify_action_role("IroncladWalker_Action_Death"), "death")
        # Truncated 63-char FBX action name must not misclassify Death/FireRecoil as Walk
        self.assertIsNone(
            vfap.classify_action_role(
                "IroncladWalker_Armature|IroncladWalker_Armature|IroncladWalke"
            )
        )

    def test_unity_animation_qa_validator_exists_and_avoids_walker_false_positive(self) -> None:
        qa_path = ROOT / "unity/RogueCommandAssetAuthoring/Assets/Editor/AnimationQAValidator.cs"
        self.assertTrue(qa_path.is_file())
        cs = qa_path.read_text(encoding="utf-8")
        self.assertIn("AuditAllUnitAnimationsBatch", cs)
        self.assertIn("AuditAllUnitAnimationsStrictBatch", cs)
        self.assertIn("AnimationPreviewScrubberWindow", cs)
        self.assertIn("ExtractActionRoleToken", cs)
        self.assertTrue(hasattr(ram, "run_matrix"))


if __name__ == "__main__":
    unittest.main()
