"""Blender integration tests for the FBX animation pipeline validator."""

from __future__ import annotations

import sys
import unittest
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[3]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from tools import RogueModelKit as rmk  # noqa: E402
from tools import validate_fbx_animation_pipeline as vfap  # noqa: E402


class FbxAnimationPipelineBlenderTests(unittest.TestCase):
    def test_exported_ironclad_and_bulwark_fbx_pass_contract_audit(self) -> None:
        models_dir = (
            ROOT
            / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models"
        )
        for fname in ("IroncladWalker.fbx", "BulwarkSiegeTank.fbx"):
            fbx_path = models_dir / fname
            self.assertTrue(fbx_path.is_file(), msg=f"Missing exported FBX: {fbx_path}")
            report = vfap.audit_fbx_file_bpy(fbx_path)
            self.assertTrue(
                report["passed"],
                msg=f"{fname} failed animation pipeline audit: {report.get('issues')}",
            )
            self.assertGreaterEqual(len(report["actions"]), 4)

    def test_synthetic_violations_are_detected(self) -> None:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        rig = rmk.generate_procedural_armature([], "TestMech", leg_type="biped", with_ik=True)
        actions = rmk.generate_procedural_action_clips(rig, prefix="TestMech", leg_type="biped")

        # 1. Clean procedural clips pass
        clean = vfap.audit_armature_actions_bpy(rig, list(actions.values()))
        self.assertTrue(clean["passed"], msg=f"Clean procedural rig failed: {clean.get('issues')}")

        # 2. Introduce Bone_Root drift in Walk
        walk_act = actions["Walk"]
        rig.animation_data.action = walk_act
        root_pb = rig.pose.bones["Bone_Root"]
        root_pb.location = (0.0, 0.25, 0.0)
        root_pb.keyframe_insert(data_path="location", frame=20)
        bad_root = vfap.audit_armature_actions_bpy(rig, [walk_act])
        self.assertFalse(bad_root["passed"])
        self.assertTrue(
            any("ROOT_MOTION_VIOLATION" in issue for issue in bad_root["issues"]),
            msg=bad_root["issues"],
        )


if __name__ == "__main__":
    unittest.main()
