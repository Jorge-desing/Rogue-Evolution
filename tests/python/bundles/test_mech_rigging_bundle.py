"""Regression coverage for the Unity AssetBundle rigging audit."""

from __future__ import annotations

import copy
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
    @classmethod
    def setUpClass(cls):
        cls._audits = {}

    @classmethod
    def tearDownClass(cls):
        cls._audits.clear()

    def _audit(self, path):
        if path not in self._audits:
            self._audits[path] = validator.MechRigValidator().validate_asset_bundle(path)
        return copy.deepcopy(self._audits[path])

    def test_tactical_operations_bundle_exposes_rig_animator_and_action_contract(self) -> None:
        audits = self._audit(TACTICAL_BUNDLE)
        audits_by_name = {audit.model_name: audit for audit in audits}
        expected = {
            "TidebreakerHovercraftVisual",
            "SalvageHaulerVisual",
            "SkyguardSAMVisual",
            "BulwarkSiegeTankVisual",
            "ArcLanceWalkerVisual",
            "FieldMedicRoverVisual",
            "SentinelRelayVisual",
            "GravitasBreakerVisual",
            "CommandRelayVisual",
        }

        self.assertEqual(set(audits_by_name), expected | {"BulwarkFoundryVisual", "RCLModule_arc_lance_walker_plasma_arc_containment"})
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
        audits = self._audit(SAMPLE_NATIVE_BUNDLE)
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

    def test_all_fire_recoil_clips_have_dynamic_serialized_samples(self) -> None:
        for bundle_path in (TACTICAL_BUNDLE, SAMPLE_NATIVE_BUNDLE):
            with self.subTest(bundle=bundle_path.name):
                env = validator.UnityPy.load(str(bundle_path))
                clips = (obj.read() for obj in env.objects if obj.type.name == "AnimationClip")
                fire_clips = [clip for clip in clips if "FireRecoil" in clip.m_Name]
                self.assertEqual(len(fire_clips), 9)
                for clip in fire_clips:
                    payload = clip.m_MuscleClip.m_Clip.data
                    dynamic_count = (
                        (payload.m_StreamedClip.curveCount or 0)
                        + (payload.m_DenseClip.m_CurveCount or 0)
                    )
                    self.assertGreater(dynamic_count, 0, clip.m_Name)


if __name__ == "__main__":
    unittest.main()
