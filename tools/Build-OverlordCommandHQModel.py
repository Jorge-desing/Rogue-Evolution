"""Build the refined Overlord Command HQ from its canonical preset."""

import math
import sys
from pathlib import Path

import bpy
sys.path.insert(0, str(Path(__file__).resolve().parent))

from mathutils import Vector

from VisualModelAuthoringSupport import (
    add_box, add_link, add_loft_shell, add_uv_ellipsoid, hex_color,
    remove_objects, run_preset_build, set_material_color,
    simplify_all_terrain_wheels,
    simplify_low_value_bevel_details,
    _make_mesh_object,
)
from ActiveUnitVisualEnhancements import apply_wave2_details, apply_wave34_details


OVERLORD_COMMAND_SHELTER_STATIONS = (
    (0.95, 0.84, 0.54, -1.98),
    (1.25, 0.95, 0.54, -1.94),
    (1.72, 0.92, 0.48, -1.82),
    (1.96, 0.83, 0.39, -1.70),
    (2.02, 0.70, 0.28, -1.57),
)


def add_armored_profile_shell(builder, name, stations, material):
    """Build an armor shell with broad planes and two-step clipped corners."""
    corner_sine = math.sqrt(0.5)
    profile_segments = 12
    vertices = []
    for z, half_width, front_y, rear_y in stations:
        if half_width <= 0 or front_y <= rear_y:
            raise ValueError(f"Invalid armor shell station for {name}")
        corner_x = min(half_width * 0.18, 0.22)
        half_length = (front_y - rear_y) * 0.5
        corner_y = min(half_length * 0.24, 0.24)
        points = (
            (-half_width + corner_x, front_y),
            (half_width - corner_x, front_y),
            (half_width - corner_x + corner_x * corner_sine,
             front_y - corner_y + corner_y * corner_sine),
            (half_width, front_y - corner_y),
            (half_width, rear_y + corner_y),
            (half_width - corner_x + corner_x * corner_sine,
             rear_y + corner_y - corner_y * corner_sine),
            (half_width - corner_x, rear_y),
            (-half_width + corner_x, rear_y),
            (-half_width + corner_x - corner_x * corner_sine,
             rear_y + corner_y - corner_y * corner_sine),
            (-half_width, rear_y + corner_y),
            (-half_width, front_y - corner_y),
            (-half_width + corner_x - corner_x * corner_sine,
             front_y - corner_y + corner_y * corner_sine),
        )
        vertices.extend((x, y, z) for x, y in points)

    faces = [tuple(reversed(range(profile_segments)))]
    faces.append(tuple((len(stations) - 1) * profile_segments + i
                       for i in range(profile_segments)))
    for ring in range(len(stations) - 1):
        a = ring * profile_segments
        b = (ring + 1) * profile_segments
        for i in range(profile_segments):
            nxt = (i + 1) % profile_segments
            faces.append((a + i, a + nxt, b + nxt, b + i))

    obj = _make_mesh_object(name, vertices, faces, material, smooth=False)
    builder.objects.append(obj)
    return obj


def _profile_section_at_z(stations, z):
    """Interpolate the shelter half-width and fore/aft extents at height z."""
    ordered = sorted(stations, key=lambda station: station[0])
    if z < ordered[0][0] or z > ordered[-1][0]:
        raise ValueError(f"Height {z:.4f} m lies outside the shelter profile")
    for lower, upper in zip(ordered, ordered[1:]):
        if lower[0] <= z <= upper[0]:
            t = (z - lower[0]) / (upper[0] - lower[0])
            return tuple(float(a + (b - a) * t) for a, b in zip(lower[1:], upper[1:]))
    return tuple(map(float, ordered[-1][1:]))


def add_profiled_service_door(
    builder,
    name,
    side,
    stations,
    center_y,
    width_y,
    z_bottom,
    z_top,
    depth=0.020,
    embed=0.012,
    material=None,
):
    """Build a door whose back face follows every tapered shelter station."""
    if side not in (-1, 1):
        raise ValueError("Service-door side must be -1 or 1")
    if width_y <= 0 or depth <= embed or z_bottom >= z_top:
        raise ValueError(f"Invalid dimensions for {name}")
    y_rear = center_y - width_y * 0.5
    y_front = center_y + width_y * 0.5
    heights = sorted({
        float(z_bottom),
        *(float(station[0]) for station in stations if z_bottom < station[0] < z_top),
        float(z_top),
    })
    vertices = []
    for z in heights:
        half_width, front_y, rear_y = _profile_section_at_z(stations, z)
        corner_y = min((front_y - rear_y) * 0.12, 0.24)
        if y_front > front_y - corner_y + 1e-5 or y_rear < rear_y + corner_y - 1e-5:
            raise ValueError(f"Service door {name} extends past the flat shelter side at z={z:.3f}")
        inner_x = side * (half_width - embed)
        outer_x = side * (half_width + depth - embed)
        vertices.extend((
            (inner_x, y_rear, z), (inner_x, y_front, z),
            (outer_x, y_front, z), (outer_x, y_rear, z),
        ))
    faces = []
    for ring in range(len(heights) - 1):
        lower = ring * 4
        upper = (ring + 1) * 4
        faces.extend((
            (lower, lower + 1, upper + 1, upper),
            (lower + 2, lower + 3, upper + 3, upper + 2),
            (lower + 1, lower + 2, upper + 2, upper + 1),
            (lower + 3, lower, upper, upper + 3),
        ))
    faces.extend((tuple(reversed(range(4))), tuple((len(heights) - 1) * 4 + index for index in range(4))))
    panel = _make_mesh_object(name, vertices, faces, material, smooth=False)
    builder.objects.append(panel)
    return panel


def replace_fenders_with_arches(builder, material):
    """Replace repeated fender boxes with thin wheel-following armor arches."""
    removed = remove_objects(builder, lambda name: "Fender_" in name)
    tires = sorted(
        (obj for obj in builder.objects if obj.type == "MESH" and obj.name.endswith("_TireCarcass")),
        key=lambda obj: obj.name,
    )
    if len(tires) < 4:
        raise ValueError(f"Expected multiple tire carcasses for fender arches, got {len(tires)}")

    bpy.context.view_layer.update()
    arch_segments = 14
    start_angle = math.radians(40.0)
    end_angle = math.radians(140.0)
    arch_count = 0
    for index, tire in enumerate(tires, start=1):
        local_bounds = [Vector(corner) for corner in tire.bound_box]
        minimum = Vector(tuple(min(point[axis] for point in local_bounds) for axis in range(3)))
        maximum = Vector(tuple(max(point[axis] for point in local_bounds) for axis in range(3)))
        local_center = (minimum + maximum) * 0.5
        half_x = (maximum.x - minimum.x) * 0.5
        radius_y = (maximum.y - minimum.y) * 0.5
        radius_z = (maximum.z - minimum.z) * 0.5
        world_basis = tire.matrix_world.to_3x3()
        world_scale = tuple((world_basis @ axis).length for axis in (
            Vector((1.0, 0.0, 0.0)), Vector((0.0, 1.0, 0.0)), Vector((0.0, 0.0, 1.0)),
        ))
        if min(world_scale) <= 1e-8 or min(half_x, radius_y, radius_z) <= 1e-8:
            raise ValueError(f"Tire {tire.name} has degenerate bounds or transform")
        half_width = half_x + 0.035 / world_scale[0]
        inner_radius_y = radius_y + 0.035 / world_scale[1]
        inner_radius_z = radius_z + 0.035 / world_scale[2]
        outer_radius_y = radius_y + 0.085 / world_scale[1]
        outer_radius_z = radius_z + 0.085 / world_scale[2]
        cross_section = (
            (-half_width, outer_radius_y, outer_radius_z),
            (half_width, outer_radius_y, outer_radius_z),
            (half_width, inner_radius_y, inner_radius_z),
            (-half_width, inner_radius_y, inner_radius_z),
        )
        vertices = []
        for segment in range(arch_segments + 1):
            angle = start_angle + (end_angle - start_angle) * segment / arch_segments
            for x, radial_y, radial_z in cross_section:
                local_point = local_center + Vector((
                    x,
                    math.cos(angle) * radial_y,
                    math.sin(angle) * radial_z,
                ))
                vertices.append(tire.matrix_world @ local_point)

        faces = []
        for segment in range(arch_segments):
            a = segment * 4
            b = (segment + 1) * 4
            for side in range(4):
                nxt = (side + 1) % 4
                faces.append((a + side, b + side, b + nxt, a + nxt))
        faces.extend((tuple(reversed(range(4))), tuple(arch_segments * 4 + i for i in range(4))))

        arch = _make_mesh_object(
            f"OverlordCommandHQFender_Arch_{index:02d}",
            vertices, faces, material, smooth=True,
        )
        builder.objects.append(arch)
        arch_count += 1
    return removed, arch_count


def refine(builder):
    p = builder.palette
    set_material_color(p.warning, hex_color("#D4A64C"), metallic=0.38, roughness=0.45)

    # Remove optional arrays of repeated accessories and replace the three
    # stacked body boxes with a joined chassis, armored cab, and command pod.
    remove_objects(builder, lambda n: any(token in n for token in (
        "Armor_ChassisSpine", "Armor_ArmoredCab", "Armor_CommandShelter",
        "Armor_SlideOutBay_", "Sensor_SatcomRadome", "Sensor_TelescopicMast",
        "Sensor_UplinkCrossbar", "Sensor_WhipAntenna", "Radar_", "Hull_Greeble",
        "Sensor_CabVisor_", "APS_", "Stowage_", "CamoNet_", "RWS_", "DroneBay_", "Decal_",
    )))

    add_armored_profile_shell(builder, "OverlordCommandHQArmor_ContinuousChassis", [
        (0.42, 0.84, 2.10, -2.10),
        (0.61, 0.98, 2.18, -2.16),
        (0.90, 1.02, 2.14, -2.08),
        (1.17, 0.93, 1.98, -1.94),
    ], p.gunmetal)

    # Forward armored cab: the windshield line slopes into the roof instead
    # of reading as a square cab box.
    add_armored_profile_shell(builder, "OverlordCommandHQArmor_ArmoredCabShell", [
        (0.84, 0.72, 2.02, 0.34),
        (1.12, 0.87, 2.18, 0.28),
        (1.52, 0.82, 1.96, 0.40),
        (1.74, 0.75, 1.82, 0.56),
        (1.84, 0.65, 1.68, 0.68),
    ], p.primary_armor)

    # Rear C2 shelter sits into the chassis and meets the cab at its shoulder.
    add_armored_profile_shell(
        builder,
        "OverlordCommandHQArmor_CommandShelterShell",
        OVERLORD_COMMAND_SHELTER_STATIONS,
        p.light_armor,
    )

    # A wide armored glazing slit replaces the blockout's tiny paired optics.
    add_box(builder, "OverlordCommandHQSensor_CabGlazingFrame",
            (0.0, 2.025, 1.39), (1.24, 0.085, 0.20), p.gunmetal)
    for index, x in enumerate((-0.38, 0.0, 0.38), start=1):
        add_box(builder, f"OverlordCommandHQSensor_CabGlazing_{index}",
                (x, 2.073, 1.39), (0.29, 0.025, 0.12), p.emissive)
    add_loft_shell(builder, "OverlordCommandHQArmor_GlazingBrow", [
        (1.48, 0.67, 2.00, 1.82),
        (1.57, 0.69, 1.96, 1.80),
        (1.62, 0.60, 1.90, 1.82),
    ], p.primary_armor, exponent=0.42, smooth=False)

    # Reinforced front bumper is attached to the chassis nose and protects the
    # glazing line without adding a floating add-on block.
    add_armored_profile_shell(builder, "OverlordCommandHQArmor_NoseBumper", [
        (0.52, 0.84, 2.26, 2.00),
        (0.66, 0.90, 2.31, 1.98),
        (0.82, 0.84, 2.24, 1.99),
    ], p.primary_armor)

    # Flush service doors, vent bank, and protected comms cabling read as
    # maintenance access on the shelter sides, not as extra cargo blocks.
    for side, tag in ((-1, "L"), (1, "R")):
        add_profiled_service_door(
            builder,
            f"OverlordCommandHQArmor_ServiceDoor_{tag}",
            side,
            OVERLORD_COMMAND_SHELTER_STATIONS,
            center_y=-0.72,
            width_y=0.58,
            z_bottom=1.24,
            z_top=1.72,
            depth=0.020,
            embed=0.012,
            material=p.primary_armor,
        )
        for index in range(3):
            add_box(builder, f"OverlordCommandHQEngine_Vent_{tag}_{index + 1}",
                    (side * 0.78, -1.86, 1.30 + index * 0.085),
                    (0.20, 0.06, 0.035), p.gunmetal)

    # Purposeful 8x8 tire tread blocks retain silhouette while dropping the
    # repeated bolt and lug micro-geometry that dominated the blockout budget.
    removed_wheel_bits = simplify_all_terrain_wheels(builder)
    removed_fenders, fender_arches = replace_fenders_with_arches(builder, p.primary_armor)

    # Integrated C4ISR mast assembly: pedestal, braced telescoping mast,
    # paired directional arrays, and a separate low-profile radome.
    mast_x, mast_y = 0.61, -1.04
    add_loft_shell(builder, "OverlordCommandHQSensor_MastPedestal", [
        (2.00, 0.13, mast_y + 0.16, mast_y - 0.16),
        (2.13, 0.17, mast_y + 0.19, mast_y - 0.19),
        (2.22, 0.12, mast_y + 0.13, mast_y - 0.13),
    ], p.gunmetal, center_x=mast_x, exponent=0.72)
    add_loft_shell(builder, "OverlordCommandHQSensor_TelescopingMast", [
        (2.12, 0.055, mast_y + 0.055, mast_y - 0.055),
        (2.42, 0.050, mast_y + 0.050, mast_y - 0.050),
        (2.76, 0.036, mast_y + 0.036, mast_y - 0.036),
        (2.82, 0.030, mast_y + 0.030, mast_y - 0.030),
    ], p.wheel_rim, center_x=mast_x, exponent=0.72)
    add_link(builder, "OverlordCommandHQSensor_MastBrace_L",
             (mast_x - 0.27, mast_y, 2.09), (mast_x - 0.06, mast_y, 2.64),
             0.035, 0.026, p.gunmetal)
    add_link(builder, "OverlordCommandHQSensor_MastBrace_R",
             (mast_x + 0.27, mast_y, 2.09), (mast_x + 0.06, mast_y, 2.64),
             0.035, 0.026, p.gunmetal)
    add_box(builder, "OverlordCommandHQSensor_ArrayCrossbar",
            (mast_x, mast_y, 2.66), (0.74, 0.10, 0.075), p.gunmetal)
    add_box(builder, "OverlordCommandHQSensor_ArrayPanel_L",
            (mast_x - 0.22, mast_y, 2.79), (0.27, 0.09, 0.23), p.light_armor,
            rotation_z=-0.08)
    add_box(builder, "OverlordCommandHQSensor_ArrayPanel_R",
            (mast_x + 0.22, mast_y, 2.79), (0.27, 0.09, 0.23), p.light_armor,
            rotation_z=0.08)
    add_uv_ellipsoid(builder, "OverlordCommandHQSensor_SatcomRadome",
                     (-0.40, -0.76, 2.14), (0.32, 0.38, 0.17), p.light_armor)
    add_box(builder, "OverlordCommandHQSensor_RadomeMount",
            (-0.40, -0.76, 2.06), (0.45, 0.50, 0.07), p.gunmetal)
    for side, tag in ((-1, "L"), (1, "R")):
        add_link(builder, f"OverlordCommandHQSensor_CommsConduit_{tag}",
                 (side * 0.60, -0.95, 2.06), (mast_x, mast_y, 2.18),
                 0.025, 0.025, p.wheel_rim)

    builder.hull_length = 4.30
    builder.hull_width = 2.35
    builder.hull_deck_z = 1.24
    builder.sensor_loc = Vector((0.0, 1.98, 1.35))
    builder.antenna_loc = Vector((mast_x, mast_y, 2.18))
    builder.damage_smoke_loc = Vector((0.0, -1.55, 1.50))
    builder.damage_fire_loc = Vector((0.0, -1.24, 1.18))
    builder.engine_audio_loc = Vector((0.0, -1.58, 0.86))
    builder.weapon_audio_loc = Vector(builder.gun_pivot)
    builder.center_of_mass_loc = Vector((0.0, -0.05, 0.92))

    builder.objects.extend(apply_wave2_details(
        "OverlordCommandHQ",
        objects=builder.objects,
        materials={"armor": p.primary_armor, "gunmetal": p.gunmetal, "warning": p.warning, "tread": p.tread},
    ))

    builder.objects.extend(apply_wave34_details(
        "OverlordCommandHQ",
        objects=builder.objects,
        materials={"armor": p.primary_armor, "gunmetal": p.gunmetal, "warning": p.warning, "tread": p.tread},
    ))

    bevel_reduction = simplify_low_value_bevel_details(
        builder, max_dimension_m=0.62, angle_degrees=32.0
    )

    builder.add_unity_locators()
    for socket in builder.locator_sockets:
        if socket.name == "Socket_RadarMast":
            socket.location = (mast_x, mast_y, 2.82)
        elif socket.name == "Socket_Antenna":
            socket.location = builder.antenna_loc
        elif socket.name == "Socket_DamageSmoke":
            socket.location = builder.damage_smoke_loc
        elif socket.name == "Socket_DamageFire":
            socket.location = builder.damage_fire_loc
        elif socket.name == "Socket_EngineAudio":
            socket.location = builder.engine_audio_loc
        elif socket.name == "Socket_WeaponAudio":
            socket.location = builder.weapon_audio_loc

    print(f"Overlord refinements: removed {removed_wheel_bits} individual wheel lugs/bolts; "
          f"replaced {len(removed_fenders)} box fender pieces with {fender_arches} curved arches; "
          "kept the tire carcasses and added low-profile C2 arrays; "
          f"simplified {bevel_reduction['faces_removed']} low-value bevel faces.")


def build():
    args = [
        "--name", "OverlordCommandHQ", "--preset", "overlord_command_hq",
        "--primary-color", "#59634E", "--light-armor-color", "#A2A28A",
        "--emissive-color", "#76CFC1", "--camo", "none",
        "--wear", "factory_new", "--no-panel-lines", "--normal-intensity", "0.30",
        "--bake-atlas", "--single-material",
        "--atlas-resolution", "2048", "--structural-wear-mask", "--multiview",
        "--blend-path", "content/source-art/overlord-command-hq.blend",
        "--fbx-path", "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/OverlordCommandHQ.fbx",
        "--preview-path", "artifacts/previews/overlord-command-hq.png",
        "--report-json", "artifacts/previews/overlord-command-hq-report.json",
    ]
    run_preset_build(args, refine)


if __name__ == "__main__":
    build()
