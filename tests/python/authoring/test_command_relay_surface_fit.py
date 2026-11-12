"""Blender-side seat and fastener preservation checks for Command Relay."""

from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree


REPO_ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPO_ROOT / "tools"))

import RogueModelKit as rmk  # noqa: E402

MODULE_PATH = REPO_ROOT / "tools/Build-CommandRelayModel.py"
SPEC = importlib.util.spec_from_file_location("build_command_relay", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
COMMAND_RELAY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(COMMAND_RELAY)

from VisualModelAuthoringSupport import (  # noqa: E402
    sample_superellipse_loft_side_x,
    simplify_all_terrain_wheels,
)


class CommandRelaySurfaceFitTests(unittest.TestCase):
    def setUp(self) -> None:
        self.created: list[bpy.types.Object] = []

    def tearDown(self) -> None:
        for obj in list(bpy.context.scene.objects):
            mesh = obj.data if obj.type == "MESH" else None
            bpy.data.objects.remove(obj, do_unlink=True)
            if mesh is not None and mesh.users == 0:
                bpy.data.meshes.remove(mesh)

    def _coamings(self):
        cab = rmk.make_transitional_coaming_collar(
            "CommandRelayArmor_CabCollar",
            (0.0, 1.05, 0.83),
            (1.35, 1.15),
            height=0.045,
            collar_mat=None,
        )
        shelter = rmk.make_transitional_coaming_collar(
            "CommandRelayArmor_ShelterCollar",
            (0.0, -0.65, 0.86),
            (1.48, 2.15),
            height=0.045,
            collar_mat=None,
        )
        self.created.extend((cab, shelter))
        return cab, shelter

    def _builder(self, *, include_coamings=True):
        primary = bpy.data.materials.new("CommandRelayFit_Primary")
        light = bpy.data.materials.new("CommandRelayFit_Light")
        warning = bpy.data.materials.new("CommandRelayFit_Warning")
        objects = list(self._coamings()) if include_coamings else []
        return SimpleNamespace(
            objects=objects,
            palette=SimpleNamespace(
                primary_armor=primary,
                light_armor=light,
                warning=warning,
                gunmetal=None,
            ),
        )

    @staticmethod
    def _overlaps(first, second) -> bool:
        def world_tree(obj):
            vertices = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
            polygons = [tuple(poly.vertices) for poly in obj.data.polygons]
            return BVHTree.FromPolygons(vertices, polygons, all_triangles=False)

        return bool(world_tree(first).overlap(world_tree(second)))

    def test_c2_panels_embed_to_faceted_host_profile(self) -> None:
        builder = self._builder()
        shell = COMMAND_RELAY.add_continuous_superstructure(builder)
        _cab_floor_z, shelter_floor_z = COMMAND_RELAY._command_relay_coaming_tops(builder)
        panels = COMMAND_RELAY.add_c2_side_panels(builder)
        self.created.extend(builder.objects)
        self.assertEqual(4, len(panels))
        self.assertTrue(all(panel in builder.objects for panel in panels))

        for panel in (obj for obj in panels if "_C2Panel_" in obj.name):
            side = -1 if panel.name.endswith("_L") else 1
            back_count = len(panel.data.vertices) // 2
            for vertex in list(panel.data.vertices)[:back_count]:
                expected = sample_superellipse_loft_side_x(
                    COMMAND_RELAY.COMMAND_RELAY_ELECTRONICS_SHELTER_STATIONS,
                    vertex.co.y,
                    vertex.co.z,
                    side,
                    exponent=COMMAND_RELAY.COMMAND_RELAY_SHELL_EXPONENT,
                    segments=16,
                    flat_floor_z=shelter_floor_z,
                    floor_width_ratio=COMMAND_RELAY.COMMAND_RELAY_FLOOR_WIDTH_RATIO,
                ) - side * 0.012
                self.assertAlmostEqual(expected, vertex.co.x, delta=1e-6)
                hit, location, _normal, _index = shell.ray_cast(
                    Vector((side * 2.0, vertex.co.y, vertex.co.z)),
                    Vector((-side, 0.0, 0.0)),
                    distance=4.0,
                )
                self.assertTrue(hit, (panel.name, tuple(vertex.co)))
                self.assertAlmostEqual(location.x - side * 0.012, vertex.co.x, delta=0.00025)

    def test_continuous_shell_seats_on_both_existing_coamings(self) -> None:
        builder = self._builder()
        cab, shelter = builder.objects
        shell = COMMAND_RELAY.add_continuous_superstructure(builder)
        self.created.append(shell)
        bpy.context.view_layer.update()

        def bounds(obj):
            points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
            return (
                min(point.x for point in points), max(point.x for point in points),
                min(point.y for point in points), max(point.y for point in points),
                min(point.z for point in points), max(point.z for point in points),
            )

        cab_top = bounds(cab)[5]
        shelter_top = bounds(shelter)[5]
        self.assertAlmostEqual(0.83225, cab_top, delta=1e-5)
        self.assertAlmostEqual(0.87825, shelter_top, delta=1e-5)
        self.assertAlmostEqual(
            cab_top + COMMAND_RELAY.COMMAND_RELAY_COAMING_JOINT_CLEARANCE,
            bounds(shelter)[4],
            delta=1e-6,
        )
        self.assertFalse(self._overlaps(cab, shelter))

        for coaming, expected_floor_z, samples in (
            (shelter, shelter_top, ((x, y) for y in (-1.45, 0.15) for x in (-0.60, -0.30, 0.0, 0.30, 0.60))),
            (cab, cab_top, ((x, y) for y in (0.66, 1.30) for x in (-0.48, -0.24, 0.0, 0.24, 0.48))),
        ):
            for x, y in samples:
                hit, shell_floor, _normal, _index = shell.ray_cast(
                    Vector((x, y, expected_floor_z - 0.05)), Vector((0.0, 0.0, 1.0)), distance=0.10
                )
                self.assertTrue(hit, (coaming.name, x, y))
                self.assertAlmostEqual(expected_floor_z, shell_floor.z, delta=1e-6)
                local_origin = coaming.matrix_world.inverted() @ Vector((x, y, shell_floor.z + 0.05))
                local_direction = coaming.matrix_world.to_3x3().inverted() @ Vector((0.0, 0.0, -1.0))
                hit, coaming_hit, _normal, _index = coaming.ray_cast(
                    local_origin, local_direction, distance=0.10
                )
                self.assertTrue(hit, (coaming.name, x, y))
                self.assertAlmostEqual(shell_floor.z, (coaming.matrix_world @ coaming_hit).z, delta=1e-6)

    def test_drone_dock_is_lifted_onto_contoured_leveler(self) -> None:
        builder = self._builder()
        shell = COMMAND_RELAY.add_continuous_superstructure(builder)
        self.created.append(shell)
        tray = rmk.make_box(
            "CommandRelayDroneBay_DroneDock_Tray",
            (0.95, 0.95, 0.08),
            (0.0, -0.72, 0.92),
            None,
            bevel=0.0,
        )
        drone = rmk.make_box(
            "CommandRelayDroneBay_ParasiteDrone_Fuselage",
            (0.24, 0.36, 0.09),
            (0.0, -0.72, 1.09),
            None,
            bevel=0.0,
        )
        builder.objects.extend((tray, drone))

        leveler, lift = COMMAND_RELAY.seat_command_relay_drone_dock(builder)
        self.created.extend((tray, drone, leveler))
        bpy.context.view_layer.update()
        tray_corners = [tray.matrix_world @ Vector(corner) for corner in tray.bound_box]
        expected_roof = sample_superellipse_loft_top_z_for_test(0.0, -0.72)
        self.assertGreater(lift, 0.60)
        self.assertAlmostEqual(expected_roof, min(point.z for point in tray_corners), delta=1e-6)
        self.assertAlmostEqual(lift, drone.location.z - 1.09, delta=1e-6)
        self.assertEqual("CommandRelayArmor_DroneDockLeveler", leveler.name)
        self.assertGreaterEqual(min(vertex.co.z for vertex in leveler.data.vertices), 1.45)
        self.assertTrue(all(abs(vertex.co.z - expected_roof) < 1e-6
                            for vertex in list(leveler.data.vertices)[len(leveler.data.vertices) // 2:]))

    def test_comms_mast_moves_aft_when_its_base_overlaps_drone_tray(self) -> None:
        tray = rmk.make_box(
            "CommandRelayDroneBay_DroneDock_Tray",
            (0.95, 0.95, 0.08),
            (0.0, -0.72, 1.63),
            None,
            bevel=0.0,
        )
        collar = rmk.make_cylinder(
            "CommandRelayCommsMast_CommsMast_BaseCollar",
            radius=0.18,
            depth=0.14,
            location=(-0.35, -1.25, 1.65),
            material=None,
            vertices=10,
            bevel=0.0,
        )
        mast_stage = rmk.make_cylinder(
            "CommandRelayCommsMast_CommsMast_Stage_1",
            radius=0.07,
            depth=0.25,
            location=(-0.35, -1.25, 1.90),
            material=None,
            vertices=8,
            bevel=0.0,
        )
        builder = SimpleNamespace(objects=[tray, collar, mast_stage])

        shift_y = COMMAND_RELAY.relocate_command_relay_mast_clear_of_drone_dock(builder)

        tray_bounds = [tray.matrix_world @ Vector(corner) for corner in tray.bound_box]
        collar_bounds = [collar.matrix_world @ Vector(corner) for corner in collar.bound_box]
        self.assertLess(shift_y, -0.12)
        self.assertLess(max(point.y for point in collar_bounds), min(point.y for point in tray_bounds) - 0.019)
        self.assertAlmostEqual(-1.25 + shift_y, mast_stage.location.y, delta=1e-6)

    def test_embedded_legacy_hatches_and_periscope_are_removed(self) -> None:
        names = (
            "CommandRelayDriverPeriscope_Hood",
            "CommandRelayAccessHatch_CoamingRim",
            "CommandRelayAccessHatch_HatchPlate",
            "CommandRelayArmor_AccessHatch_HatchPlate",
        )
        objects = [
            rmk.make_box(name, (0.1, 0.1, 0.1), (0.0, 0.0, 0.5), None, bevel=0.0)
            for name in names
        ]
        builder = SimpleNamespace(objects=objects)

        removed = COMMAND_RELAY.remove_embedded_command_relay_hardware(builder)

        self.assertEqual(list(names[:3]), removed)
        self.assertEqual([names[3]], [obj.name for obj in builder.objects])
        self.assertIsNotNone(bpy.data.objects.get(names[3]))

    def test_superstructure_is_one_continuous_material_banded_shell(self) -> None:
        builder = self._builder()
        shell = COMMAND_RELAY.add_continuous_superstructure(builder)
        self.created.append(shell)
        self.assertEqual("CommandRelayArmor_ContinuousSuperstructure", shell.name)
        self.assertEqual(3, len(builder.objects))
        self.assertEqual(2, len(shell.data.materials))
        self.assertAlmostEqual(-1.725, min((shell.matrix_world @ Vector(corner)).y for corner in shell.bound_box))
        self.assertAlmostEqual(1.625, max((shell.matrix_world @ Vector(corner)).y for corner in shell.bound_box))
        self.assertIn(0.45, {round(vertex.co.y, 3) for vertex in shell.data.vertices})
        self.assertTrue(any(face.material_index == 1 for face in shell.data.polygons))
        self.assertTrue(any(face.material_index == 0 for face in shell.data.polygons))
        cab_floor_z, shelter_floor_z = COMMAND_RELAY._command_relay_coaming_tops(builder)
        self.assertTrue(any(abs(vertex.co.z - cab_floor_z) < 1e-6 for vertex in shell.data.vertices))
        self.assertTrue(any(abs(vertex.co.z - shelter_floor_z) < 1e-6 for vertex in shell.data.vertices))

    def test_terrain_cleanup_preserves_hub_bolts(self) -> None:
        tire = rmk.make_box(
            "CommandRelayFit_Wheel_TireCarcass",
            (0.26, 0.70, 0.70),
            (0.96, 0.0, 0.50),
            None,
            bevel=0.0,
        )
        tread_lug = rmk.make_box(
            "CommandRelayFit_Wheel_TreadLug_1",
            (0.20, 0.05, 0.05),
            (0.96, 0.0, 0.90),
            None,
            bevel=0.0,
        )
        lug_bolt = rmk.make_box(
            "CommandRelayFit_Wheel_LugBolt_1",
            (0.02, 0.02, 0.02),
            (1.10, 0.0, 0.50),
            None,
            bevel=0.0,
        )
        palette = SimpleNamespace(tread=None)
        builder = SimpleNamespace(objects=[tire, tread_lug, lug_bolt], palette=palette)

        removed = simplify_all_terrain_wheels(builder)

        self.assertEqual(1, removed)
        self.assertNotIn(tread_lug, builder.objects)
        self.assertIn(lug_bolt, builder.objects)
        self.assertIsNotNone(bpy.data.objects.get(lug_bolt.name))
        self.assertTrue(any(obj.name.endswith("_TreadBlocks") for obj in builder.objects))

    def test_wheel_arches_clear_tread_and_receive_six_shell_stays(self) -> None:
        builder = self._builder()
        builder.palette.tread = None
        shell = COMMAND_RELAY.add_continuous_superstructure(builder)
        self.created.append(shell)
        for y in (1.45, -0.15, -1.55):
            for side in (-1, 1):
                prefix = f"CommandRelayFit_Wheel_{'L' if side < 0 else 'R'}_{y}"
                tire = rmk.make_box(
                    prefix + "_TireCarcass",
                    (0.28, 0.78, 0.78),
                    (side * 0.96, y, 0.495),
                    None,
                    bevel=0.0,
                )
                arch = rmk.make_box(
                    prefix + "_Fender_ArchTop",
                    (0.35, 0.62, 0.081),
                    (side * 0.96, y, 0.915),
                    None,
                    bevel=0.0,
                )
                builder.objects.extend((tire, arch))
                self.created.extend((tire, arch))

        simplify_all_terrain_wheels(builder)
        arches = COMMAND_RELAY.seat_command_relay_wheel_arches(builder)
        stays = [obj for obj in builder.objects if obj.name.startswith("CommandRelayFenderStay_")]
        self.created.extend(stays)
        self.assertEqual(6, len(arches))
        self.assertEqual(6, len(stays))

        def overlaps(first, second):
            def world_tree(obj):
                vertices = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
                polygons = [tuple(poly.vertices) for poly in obj.data.polygons]
                return BVHTree.FromPolygons(vertices, polygons, all_triangles=False)

            return bool(world_tree(first).overlap(world_tree(second)))

        for arch in arches:
            tire = min(
                (obj for obj in builder.objects if obj.name.endswith("_TireCarcass")),
                key=lambda item: (item.matrix_world.translation - arch.matrix_world.translation).length_squared,
            )
            tread_name = tire.name.replace("_TireCarcass", "_TreadBlocks")
            tread = next(obj for obj in builder.objects if obj.name == tread_name)
            tread_top = max((tread.matrix_world @ Vector(corner)).z for corner in tread.bound_box)
            arch_bottom = min((arch.matrix_world @ Vector(corner)).z for corner in arch.bound_box)
            self.assertGreaterEqual(arch_bottom + 1e-6, tread_top + 0.010)
            stay_prefix = "CommandRelayFenderStay_" + arch.name.removesuffix("_ArchTop")
            stay = next(obj for obj in stays if obj.name == stay_prefix)
            self.assertTrue(overlaps(shell, stay), stay.name)
            self.assertTrue(overlaps(arch, stay), stay.name)
            self.assertFalse(overlaps(tire, stay), stay.name)
            self.assertFalse(overlaps(tread, stay), stay.name)


def sample_superellipse_loft_top_z_for_test(x: float, y: float) -> float:
    from VisualModelAuthoringSupport import sample_superellipse_loft_top_z

    return sample_superellipse_loft_top_z(
        COMMAND_RELAY.COMMAND_RELAY_ELECTRONICS_SHELTER_STATIONS,
        x,
        y,
        exponent=COMMAND_RELAY.COMMAND_RELAY_SHELL_EXPONENT,
        segments=16,
    )


if __name__ == "__main__":
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(CommandRelaySurfaceFitTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    raise SystemExit(not result.wasSuccessful())
