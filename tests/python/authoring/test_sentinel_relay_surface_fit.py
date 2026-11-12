"""Blender-side fit checks for Sentinel Relay hard-surface repairs."""

from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

import bpy
from mathutils.bvhtree import BVHTree


REPO_ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPO_ROOT / "tools"))

import RogueModelKit as rmk  # noqa: E402

MODULE_PATH = REPO_ROOT / "tools/Build-SentinelRelayModel.py"
SPEC = importlib.util.spec_from_file_location("build_sentinel_relay", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
SENTINEL = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SENTINEL)

from VisualModelAuthoringSupport import add_z_axis_post  # noqa: E402


class SentinelRelaySurfaceFitTests(unittest.TestCase):
    def tearDown(self) -> None:
        for obj in list(bpy.context.scene.objects):
            mesh = obj.data if obj.type == "MESH" else None
            bpy.data.objects.remove(obj, do_unlink=True)
            if mesh is not None and mesh.users == 0:
                bpy.data.meshes.remove(mesh)

    def _builder(self):
        return SimpleNamespace(
            objects=[],
            palette=SimpleNamespace(
                primary_armor=bpy.data.materials.new("SentinelFit_Primary"),
                light_armor=bpy.data.materials.new("SentinelFit_Light"),
                warning=bpy.data.materials.new("SentinelFit_Warning"),
                gunmetal=bpy.data.materials.new("SentinelFit_Gunmetal"),
                emissive=bpy.data.materials.new("SentinelFit_Emissive"),
                tread=None,
            ),
        )

    @staticmethod
    def _overlaps(first, second) -> bool:
        def tree(obj):
            vertices = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
            polygons = [tuple(poly.vertices) for poly in obj.data.polygons]
            return BVHTree.FromPolygons(vertices, polygons, all_triangles=False)

        return bool(tree(first).overlap(tree(second)))

    def test_service_panels_stripes_and_fasteners_seat_on_cowl(self) -> None:
        builder = self._builder()
        cowl = rmk.make_lofted_superellipse_shell(
            "SentinelRelayArmor_CabCowl",
            SENTINEL.SENTINEL_RELAY_CAB_COWL_STATIONS,
            builder.palette.light_armor,
            axis="Y",
            segments=16,
            exponent=0.66,
            smooth=True,
        )
        builder.objects.append(cowl)

        added = SENTINEL.add_cowl_service_panels(builder)
        self.assertEqual(12, len(added))
        for side, tag in ((-1, "L"), (1, "R")):
            panel = bpy.data.objects[f"SentinelRelayArmor_CowlServicePanel_{tag}"]
            stripe = bpy.data.objects[f"SentinelRelayWarning_CowlServiceStripe_{tag}"]
            self.assertTrue(self._overlaps(panel, cowl), panel.name)
            self.assertTrue(self._overlaps(panel, stripe), stripe.name)
            for index in range(1, 5):
                bolt = bpy.data.objects[f"SentinelRelayFastener_CowlPanel_{tag}_{index}"]
                self.assertTrue(self._overlaps(panel, bolt), bolt.name)
            opposite = bpy.data.objects[
                f"SentinelRelayArmor_CowlServicePanel_{'R' if tag == 'L' else 'L'}"
            ]
            self.assertFalse(self._overlaps(panel, opposite))

    def test_relay_braces_connect_bay_and_mast(self) -> None:
        builder = self._builder()
        bay = rmk.make_lofted_superellipse_shell(
            "SentinelRelayArmor_EquipmentBay",
            SENTINEL.SENTINEL_RELAY_EQUIPMENT_BAY_STATIONS,
            builder.palette.primary_armor,
            axis="Y",
            segments=16,
            exponent=0.66,
            smooth=True,
        )
        builder.objects.append(bay)
        mast = add_z_axis_post(
            builder,
            "SentinelRelaySensor_RelayMast",
            (0.0, -0.72, 1.55),
            0.060,
            0.98,
            builder.palette.gunmetal,
        )

        braces = SENTINEL.replace_relay_braces(builder, builder.palette.gunmetal)
        self.assertEqual(2, len(braces))
        for brace in braces:
            self.assertTrue(self._overlaps(brace, bay), brace.name)
            self.assertTrue(self._overlaps(brace, mast), brace.name)
        self.assertFalse(self._overlaps(braces[0], braces[1]))

    def test_detached_gussets_and_both_rack_clusters_are_removed(self) -> None:
        builder = self._builder()
        names = (
            "SentinelRelayArmor_Gusset_1",
            "SentinelRelayPioneerRack_MountCleat_0",
            "SentinelRelayArmor_PioneerRack_MountCleat_0",
            "SentinelRelayArmor_HatchRim",
        )
        for name in names:
            builder.objects.append(rmk.make_box(name, (0.1, 0.1, 0.1), (0.0, 0.0, 0.5), None, bevel=0.0))

        removed = SENTINEL.remove_unseated_sentinel_clusters(builder)

        self.assertEqual(list(names[:3]), removed)
        self.assertEqual([names[3]], [obj.name for obj in builder.objects])
        self.assertIsNotNone(bpy.data.objects.get(names[3]))


if __name__ == "__main__":
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(SentinelRelaySurfaceFitTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    raise SystemExit(not result.wasSuccessful())
