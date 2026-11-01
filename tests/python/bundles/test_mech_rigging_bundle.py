"""Regression coverage for the Unity AssetBundle rigging audit."""

from __future__ import annotations

import unittest
from pathlib import Path

from tests.python.helpers import load_tool_module


validator = load_tool_module("validate_mech_rigging")
REPO_ROOT = Path(__file__).resolve().parents[3]
TACTICAL_BUNDLE = (
    REPO_ROOT
    / "content"
    / "packs"
    / "tactical-operations"
    / "bundles"
    / "tactical-operations.bundle"
)
SAMPLE_NATIVE_BUNDLE = (
    REPO_ROOT
    / "content"
    / "packs"
    / "sample-native-expansion"
    / "bundles"
    / "sample-native-expansion.bundle"
)


@unittest.skipUnless(validator.HAS_UNITYPY, "UnityPy is required for AssetBundle rigging audits")
class MechRiggingBundleTests(unittest.TestCase):
    def test_tactical_operations_bundle_exposes_rig_animator_and_action_contract(self) -> None:
        audits = validator.MechRigValidator().validate_asset_bundle(TACTICAL_BUNDLE)
        audits_by_name = {audit.model_name: audit for audit in audits}
        expected = {
            "TidebreakerHovercraftVisual",
            "SalvageHaulerVisual",
            "SkyguardSAMVisual",
            "BulwarkSiegeTankVisual",
            "ArcLanceWalkerVisual",
            "FieldMedicRoverVisual",
        }

        self.assertEqual(set(audits_by_name), expected)
        for model_name in sorted(expected):
            audit = audits_by_name[model_name]
            with self.subTest(model=model_name):
                self.assertTrue(audit.has_armature)
                self.assertTrue(audit.has_animator)
                self.assertEqual(audit.missing_parameters, [])
                self.assertTrue(audit.animation_clips)
                self.assertTrue(any("Action_Idle" in clip for clip in audit.animation_clips))
                self.assertTrue(
                    any(
                        ("Action_Walk" in clip or "Action_Drive" in clip)
                        for clip in audit.animation_clips
                    )
                )
                self.assertTrue(any("Action_FireRecoil" in clip for clip in audit.animation_clips))
                self.assertTrue(any("Action_Death" in clip for clip in audit.animation_clips))

    def test_sample_native_remaining_vehicle_overlays_expose_rig_animation_contract(self) -> None:
        audits = validator.MechRigValidator().validate_asset_bundle(SAMPLE_NATIVE_BUNDLE)
        audits_by_name = {audit.model_name: audit for audit in audits}
        expected = {
            "ScoutTankVisual",
            "SkirmishBikeVisual",
            "ColossusSiegeVisual",
            "SurgeArcTankVisual",
        }

        self.assertTrue(expected.issubset(audits_by_name))
        for model_name in sorted(expected):
            audit = audits_by_name[model_name]
            with self.subTest(model=model_name):
                self.assertTrue(audit.has_animator)
                self.assertEqual(audit.missing_parameters, [])
                self.assertTrue(any("Action_Idle" in clip for clip in audit.animation_clips))
                self.assertTrue(
                    any(
                        ("Action_Walk" in clip or "Action_Drive" in clip)
                        for clip in audit.animation_clips
                    )
                )
                self.assertTrue(any("Action_FireRecoil" in clip for clip in audit.animation_clips))
                self.assertTrue(any("Action_Death" in clip for clip in audit.animation_clips))


if __name__ == "__main__":
    unittest.main()
