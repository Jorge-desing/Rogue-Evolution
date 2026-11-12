"""Blender-side contact checks for Overlord HQ armor and tire fenders."""

from __future__ import annotations

import importlib.util
import math
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

import bpy
from mathutils import Vector


REPO_ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPO_ROOT / "tools"))
MODULE_PATH = REPO_ROOT / "tools/Build-OverlordCommandHQModel.py"
SPEC = importlib.util.spec_from_file_location("build_overlord_command_hq", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
OVERLORD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(OVERLORD)


class OverlordHQSurfaceFitTests(unittest.TestCase):
    def setUp(self) -> None:
        self.created_names: list[str] = []

    def tearDown(self) -> None:
        for name in reversed(self.created_names):
            obj = bpy.data.objects.get(name)
            if obj is not None:
                mesh = obj.data if obj.type == "MESH" else None
                bpy.data.objects.remove(obj, do_unlink=True)
                if mesh is not None and mesh.users == 0:
                    bpy.data.meshes.remove(mesh)

    def _mesh_object(self, name: str, vertices, faces=()) -> bpy.types.Object:
        mesh = bpy.data.meshes.new(name + "_Mesh")
        mesh.from_pydata(vertices, (), faces)
        mesh.update()
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        self.created_names.append(obj.name)
        return obj

    def _tire(self, name: str, parent: bpy.types.Object, location) -> bpy.types.Object:
        vertices = []
        ring_segments = 24
        for x in (-0.09, 0.09):
            for segment in range(ring_segments):
                angle = 2.0 * math.pi * segment / ring_segments
                vertices.append((x, 0.32 * math.cos(angle), 0.30 * math.sin(angle)))
        faces = []
        for segment in range(ring_segments):
            next_segment = (segment + 1) % ring_segments
            faces.append((segment, next_segment, ring_segments + next_segment, ring_segments + segment))
        tire = self._mesh_object(name, vertices, faces)
        tire.parent = parent
        tire.location = location
        return tire

    def test_service_door_back_face_tracks_tapered_shelter_side(self) -> None:
        builder = SimpleNamespace(objects=[])
        for side, tag in ((-1, "L"), (1, "R")):
            panel = OVERLORD.add_profiled_service_door(
                builder,
                f"OverlordHQFitTest_ServiceDoor_{tag}",
                side,
                OVERLORD.OVERLORD_COMMAND_SHELTER_STATIONS,
                center_y=-0.72,
                width_y=0.58,
                z_bottom=1.24,
                z_top=1.72,
                material=None,
            )
            self.created_names.append(panel.name)
            self.assertIn(panel, builder.objects)
            for ring in range(len(panel.data.vertices) // 4):
                base = ring * 4
                z = float(panel.data.vertices[base].co.z)
                half_width, _front_y, _rear_y = OVERLORD._profile_section_at_z(
                    OVERLORD.OVERLORD_COMMAND_SHELTER_STATIONS, z
                )
                inner = panel.data.vertices[base].co
                outer = panel.data.vertices[base + 2].co
                self.assertAlmostEqual(side * (half_width - 0.012), inner.x, delta=1e-6)
                self.assertAlmostEqual(side * (half_width + 0.020 - 0.012), outer.x, delta=1e-6)

    def test_service_door_rejects_placement_past_the_shelter_corner(self) -> None:
        with self.assertRaisesRegex(ValueError, "flat shelter side"):
            OVERLORD.add_profiled_service_door(
                SimpleNamespace(objects=[]),
                "OverlordHQFitTest_OutsideCorner",
                1,
                OVERLORD.OVERLORD_COMMAND_SHELTER_STATIONS,
                center_y=0.5,
                width_y=0.58,
                z_bottom=1.24,
                z_top=1.72,
            )

    def test_fender_arches_follow_parented_tire_world_transforms(self) -> None:
        parent = bpy.data.objects.new("OverlordHQFitTest_WheelCarrier", None)
        bpy.context.scene.collection.objects.link(parent)
        parent.location = (2.3, -1.1, 0.7)
        parent.rotation_euler = (0.21, -0.34, 0.17)
        parent.scale = (1.25, 0.82, 1.10)
        self.created_names.append(parent.name)

        tires = [
            self._tire(f"OverlordHQFitTest_Tire_{index:02d}_TireCarcass", parent, location)
            for index, location in enumerate((
                (-1.025, 1.425, 0.505), (1.025, 1.425, 0.505),
                (-1.025, 0.520, 0.505), (1.025, 0.520, 0.505),
                (-1.025, -0.171, 0.505), (1.025, -0.171, 0.505),
                (-1.025, -1.425, 0.505), (1.025, -1.425, 0.505),
            ), start=1)
        ]
        fender = self._mesh_object(
            "OverlordHQFitTest_Fender_Old",
            ((0.0, 0.0, 0.0), (0.1, 0.0, 0.0), (0.0, 0.1, 0.0)),
            ((0, 1, 2),),
        )
        fender_name = fender.name
        builder = SimpleNamespace(objects=[fender, *tires])
        bpy.context.view_layer.update()

        removed, count = OVERLORD.replace_fenders_with_arches(builder, None)
        arches = sorted(
            (obj for obj in builder.objects if "Fender_Arch_" in obj.name),
            key=lambda obj: obj.name,
        )
        self.created_names.extend(arch.name for arch in arches)
        self.assertEqual([fender_name], removed)
        self.assertEqual(len(tires), count)
        self.assertEqual(len(tires), len(arches))

        for tire, arch in zip(sorted(tires, key=lambda obj: obj.name), arches):
            inverse = tire.matrix_world.inverted()
            local_bounds = [Vector(corner) for corner in tire.bound_box]
            center = Vector(tuple(
                (min(point[axis] for point in local_bounds) + max(point[axis] for point in local_bounds)) * 0.5
                for axis in range(3)
            ))
            half_x = (max(point.x for point in local_bounds) - min(point.x for point in local_bounds)) * 0.5
            radius_y = (max(point.y for point in local_bounds) - min(point.y for point in local_bounds)) * 0.5
            radius_z = (max(point.z for point in local_bounds) - min(point.z for point in local_bounds)) * 0.5
            basis = tire.matrix_world.to_3x3()
            scales = (
                (basis @ Vector((1.0, 0.0, 0.0))).length,
                (basis @ Vector((0.0, 1.0, 0.0))).length,
                (basis @ Vector((0.0, 0.0, 1.0))).length,
            )
            for segment in range(15):
                angle = math.radians(40.0 + 100.0 * segment / 14.0)
                local_inner = inverse @ (arch.matrix_world @ arch.data.vertices[segment * 4 + 2].co)
                expected_y = center.y + math.cos(angle) * (radius_y + 0.035 / scales[1])
                expected_z = center.z + math.sin(angle) * (radius_z + 0.035 / scales[2])
                self.assertAlmostEqual(expected_y, local_inner.y, delta=1e-5)
                self.assertAlmostEqual(expected_z, local_inner.z, delta=1e-5)
                self.assertAlmostEqual(center.x + half_x + 0.035 / scales[0], local_inner.x, delta=1e-5)


if __name__ == "__main__":
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(OverlordHQSurfaceFitTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    raise SystemExit(not result.wasSuccessful())
