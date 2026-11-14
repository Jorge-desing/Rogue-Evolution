"""Blender-side regression checks for the Gravitas Hardflow detail recipe."""

from __future__ import annotations

import os
import sys
import unittest
from pathlib import Path

import bpy


REPO_ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPO_ROOT / "tools"))

from gravitas_breaker_hardflow_recipe import (  # noqa: E402
    apply_gravitas_breech_feed_details,
    apply_gravitas_trunnion_retainer_details,
)
from RogueModelKit import assign_rigid_mesh_weights  # noqa: E402

SOURCE_BLEND = Path(os.environ.get(
    "RCL_GRAVITAS_TEST_SOURCE_BLEND",
    REPO_ROOT / "content/source-art/gravitas-breaker.blend",
)).resolve()


class GravitasHardflowRecipeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        if not SOURCE_BLEND.is_file():
            raise FileNotFoundError(SOURCE_BLEND)
        bpy.ops.wm.open_mainfile(filepath=str(SOURCE_BLEND))

    def setUp(self) -> None:
        self.objects = list(bpy.context.scene.objects)

    def test_gravitas_hardware_names_follow_their_correct_rig_axis(self) -> None:
        expected = {
            "GravitasBreakerArmor_TurretRingGuard_Test": "Bone_Hull",
            "GravitasBreakerGravCannon_CoaxialPort_Test": "Bone_TurretYaw",
            "GravitasBreakerGravCannon_Gravity_BreechMount_Test": "Bone_TurretPitch",
        }
        temporary_objects = []
        armature = bpy.data.objects.get("GravitasBreaker_Armature")
        self.assertIsNotNone(armature)
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

            assign_rigid_mesh_weights(temporary_objects, "GravitasBreaker", armature)
            for obj in temporary_objects:
                with self.subTest(mesh=obj.name):
                    self.assertEqual([expected[obj.name]], [group.name for group in obj.vertex_groups])
        finally:
            for obj in temporary_objects:
                mesh = obj.data
                bpy.data.objects.remove(obj, do_unlink=True)
                if mesh.users == 0:
                    bpy.data.meshes.remove(mesh)

    def test_breech_feed_details_enter_builder_objects_and_replay_idempotently(self) -> None:
        expected_names = []
        for tag in ("L", "R"):
            expected_names.extend((
                f"GravitasBreakerHF_BreechFeedSeat_{tag}",
                f"GravitasBreakerHF_BreechFeedFlange_{tag}",
                f"GravitasBreakerHF_BreechFeedHub_{tag}",
                f"GravitasBreakerHF_BreechFeedPin_{tag}",
                *(f"GravitasBreakerHF_BreechFeedFastener_{tag}_{index}" for index in range(1, 5)),
            ))
        report = apply_gravitas_breech_feed_details(self.objects, None, None)
        if report["applied"]:
            self.assertEqual(16, report["addedDetailObjectCount"])
            added_names = list(report["addedDetailObjects"])
        else:
            self.assertTrue(report["idempotentNoOp"])
            added_names = expected_names
        scene_names = {obj.name for obj in self.objects}
        self.assertTrue(set(added_names).issubset(scene_names))
        self.assertTrue(all(bpy.data.objects.get(name) is not None for name in added_names))
        armature = bpy.data.objects.get("GravitasBreaker_Armature")
        self.assertIsNotNone(armature)
        breech_details = [bpy.data.objects[name] for name in added_names]
        assign_rigid_mesh_weights(breech_details, "GravitasBreaker", armature)
        self.assertTrue(all([group.name for group in obj.vertex_groups] == ["Bone_TurretPitch"]
                            for obj in breech_details))

        replay = apply_gravitas_breech_feed_details(self.objects, None, None)
        self.assertTrue(replay["idempotentNoOp"])

        missing_from_builder_list = bpy.data.objects[added_names[0]]
        self.objects.remove(missing_from_builder_list)
        try:
            with self.assertRaisesRegex(ValueError, "scene/object list"):
                apply_gravitas_breech_feed_details(self.objects, None, None)
        finally:
            self.objects.append(missing_from_builder_list)

    def test_trunnion_retainer_checks_scene_visibility_and_complete_marker(self) -> None:
        expected_names = [
            f"GravitasBreakerHF_TrunnionRetainerBolt_{tag}_{index}"
            for tag in ("L", "R")
            for index in range(1, 4)
        ]
        report = apply_gravitas_trunnion_retainer_details(self.objects, None)
        if report["applied"]:
            self.assertEqual(6, report["addedDetailObjectCount"])
            added_names = list(report["addedDetailObjects"])
        else:
            self.assertTrue(report["idempotentNoOp"])
            added_names = expected_names
        scene_names = {obj.name for obj in self.objects}
        self.assertTrue(set(added_names).issubset(scene_names))
        self.assertTrue(all(bpy.data.objects.get(name) is not None for name in added_names))
        armature = bpy.data.objects.get("GravitasBreaker_Armature")
        self.assertIsNotNone(armature)
        retainer_details = [bpy.data.objects[name] for name in added_names]
        assign_rigid_mesh_weights(retainer_details, "GravitasBreaker", armature)
        self.assertTrue(all([group.name for group in obj.vertex_groups] == ["Bone_TurretYaw"]
                            for obj in retainer_details))

        replay = apply_gravitas_trunnion_retainer_details(self.objects, None)
        self.assertTrue(replay["idempotentNoOp"])

        missing_from_builder_list = bpy.data.objects[added_names[0]]
        self.objects.remove(missing_from_builder_list)
        try:
            with self.assertRaisesRegex(ValueError, "scene/object list"):
                apply_gravitas_trunnion_retainer_details(self.objects, None)
        finally:
            self.objects.append(missing_from_builder_list)


if __name__ == "__main__":
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(GravitasHardflowRecipeTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    raise SystemExit(not result.wasSuccessful())
