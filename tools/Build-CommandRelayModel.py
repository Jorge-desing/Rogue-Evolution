"""Build the Wave 7 Command Relay tactical relay carrier and its PBR atlas."""

from __future__ import annotations

from pathlib import Path
import math
import sys

import bpy
from mathutils import Vector

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))

from ActiveUnitVisualEnhancements import apply_wave34_details
import RogueModelKit as rmk
from VisualModelAuthoringSupport import (
    add_superellipse_side_panel,
    add_superellipse_roof_leveler,
    make_flat_bottom_superellipse_loft_shell,
    remove_objects,
    run_preset_build,
    sample_superellipse_loft_top_z,
    simplify_all_terrain_wheels,
    simplify_low_value_bevel_details,
)


COMMAND_RELAY_FORWARD_CAB_STATIONS = (
    (0.475, 0.0, 1.10, 0.55, 0.24),
    (0.66, 0.0, 1.14, 0.67, 0.29),
    (1.30, 0.0, 1.14, 0.675, 0.29),
    (1.625, 0.0, 1.07, 0.58, 0.23),
)
COMMAND_RELAY_ELECTRONICS_SHELTER_STATIONS = (
    (-1.725, 0.0, 1.20, 0.60, 0.31),
    (-1.45, 0.0, 1.23, 0.73, 0.36),
    (0.15, 0.0, 1.23, 0.74, 0.36),
    (0.425, 0.0, 1.17, 0.62, 0.28),
)
COMMAND_RELAY_SHELL_EXPONENT = 0.66
COMMAND_RELAY_FLOOR_WIDTH_RATIO = 0.94
COMMAND_RELAY_COAMING_JOINT_CLEARANCE = 0.001


def _world_bounds(obj):
    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return (
        min(point.x for point in points), max(point.x for point in points),
        min(point.y for point in points), max(point.y for point in points),
        min(point.z for point in points), max(point.z for point in points),
    )


def _command_relay_coamings(builder):
    by_name = {obj.name: obj for obj in builder.objects if obj.type == "MESH"}
    cab = by_name.get("CommandRelayArmor_CabCollar_CoamingCollar")
    shelter = by_name.get("CommandRelayArmor_ShelterCollar_CoamingCollar")
    if cab is None or shelter is None:
        raise ValueError(
            "Command Relay needs both existing cab and shelter coaming collars before seating its shell"
        )
    return cab, shelter


def _command_relay_coaming_tops(builder):
    """Read the existing, intentionally staggered cab and shelter seat heights."""
    bpy.context.view_layer.update()
    cab, shelter = _command_relay_coamings(builder)
    return _world_bounds(cab)[5], _world_bounds(shelter)[5]


def seat_command_relay_coamings(builder):
    """Lift the shelter collar clear of the cab collar with a 1 mm joint seam."""
    bpy.context.view_layer.update()
    cab, shelter = _command_relay_coamings(builder)
    cab_top = _world_bounds(cab)[5]
    shelter_bottom = _world_bounds(shelter)[4]
    target_shelter_bottom = cab_top + COMMAND_RELAY_COAMING_JOINT_CLEARANCE
    if shelter_bottom < target_shelter_bottom - 1e-5:
        shelter.location.z += target_shelter_bottom - shelter_bottom
        bpy.context.view_layer.update()
    seated_bottom = _world_bounds(shelter)[4]
    if abs(seated_bottom - target_shelter_bottom) > 1e-5:
        raise ValueError(
            f"Command Relay coaming seam is not clear: cab top={cab_top:.6f}, "
            f"shelter bottom={seated_bottom:.6f}, expected={target_shelter_bottom:.6f}"
        )
    return cab_top, _world_bounds(shelter)[5]


def _section_at_y(stations, y):
    for lower, upper in zip(stations, stations[1:]):
        if lower[0] <= y <= upper[0]:
            t = (y - lower[0]) / (upper[0] - lower[0])
            return (float(y), *(a + (b - a) * t for a, b in zip(lower[1:], upper[1:])))
    raise ValueError(f"Command Relay loft station Y={y:.4f} is outside its profile")


def add_continuous_superstructure(builder):
    """Replace split cabins with one loft whose flat base seats on both coamings."""
    p = builder.palette
    seat_command_relay_coamings(builder)
    cab_floor_z, shelter_floor_z = _command_relay_coaming_tops(builder)
    cab, shelter = _command_relay_coamings(builder)
    base_stations = [*COMMAND_RELAY_ELECTRONICS_SHELTER_STATIONS]
    shelter_end = base_stations[-1]
    cab_start = COMMAND_RELAY_FORWARD_CAB_STATIONS[0]
    transition_y = (shelter_end[0] + cab_start[0]) * 0.5
    t = (transition_y - shelter_end[0]) / (cab_start[0] - shelter_end[0])
    transition = (transition_y, *(a + (b - a) * t for a, b in zip(shelter_end[1:], cab_start[1:])))
    shape_stations = [*base_stations, transition, *COMMAND_RELAY_FORWARD_CAB_STATIONS]
    coaming_end_y = _world_bounds(shelter)[3]
    cab_after_start = COMMAND_RELAY_FORWARD_CAB_STATIONS[1][0]
    if not cab_start[0] < coaming_end_y < cab_after_start - 0.002:
        raise ValueError(
            f"Shelter coaming end Y={coaming_end_y:.4f} is outside the cab transition span"
        )
    stepped_floor_end_y = coaming_end_y + 0.001
    if stepped_floor_end_y >= cab_after_start:
        raise ValueError("Command Relay coaming transition leaves no room for its floor step")
    coaming_end_section = _section_at_y(shape_stations, coaming_end_y)
    stepped_floor_end = _section_at_y(shape_stations, stepped_floor_end_y)
    stations = [
        *base_stations,
        transition,
        cab_start,
        coaming_end_section,
        stepped_floor_end,
        *COMMAND_RELAY_FORWARD_CAB_STATIONS[1:],
    ]
    floor_levels = (
        [shelter_floor_z] * (len(base_stations) + 3)
        + [cab_floor_z] * (1 + len(COMMAND_RELAY_FORWARD_CAB_STATIONS[1:]))
    )

    remove_objects(builder, lambda name: name in {
        "CommandRelayArmor_ForwardCab",
        "CommandRelayArmor_ElectronicsShelter",
        "CommandRelayArmor_ContinuousSuperstructure",
    })
    shell = make_flat_bottom_superellipse_loft_shell(
        "CommandRelayArmor_ContinuousSuperstructure",
        stations,
        floor_levels,
        (p.primary_armor, p.light_armor),
        band_start_y=cab_start[0],
        segments=16,
        exponent=COMMAND_RELAY_SHELL_EXPONENT,
        floor_width_ratio=COMMAND_RELAY_FLOOR_WIDTH_RATIO,
    )
    builder.objects.append(shell)
    return shell


def relocate_command_relay_mast_clear_of_drone_dock(builder):
    """Slide the existing mast assembly aft only when its foot overlaps the dock."""
    bpy.context.view_layer.update()
    tray = next((obj for obj in builder.objects
                 if obj.type == "MESH" and obj.name == "CommandRelayDroneBay_DroneDock_Tray"), None)
    collar = next((obj for obj in builder.objects
                   if obj.type == "MESH" and obj.name == "CommandRelayCommsMast_CommsMast_BaseCollar"), None)
    if tray is None or collar is None:
        return 0.0
    tray_bounds = _world_bounds(tray)
    collar_bounds = _world_bounds(collar)
    overlaps_x = tray_bounds[0] < collar_bounds[1] and collar_bounds[0] < tray_bounds[1]
    overlaps_y = tray_bounds[2] < collar_bounds[3] and collar_bounds[2] < tray_bounds[3]
    if not (overlaps_x and overlaps_y):
        return 0.0
    shift_y = tray_bounds[2] - collar_bounds[3] - 0.020
    for obj in builder.objects:
        if obj.name.startswith("CommandRelayCommsMast_"):
            obj.location.y += shift_y
    bpy.context.view_layer.update()
    moved_collar = next(obj for obj in builder.objects if obj.name == collar.name)
    moved_bounds = _world_bounds(moved_collar)
    if moved_bounds[3] >= tray_bounds[2] - 0.019:
        raise ValueError("Command Relay communications mast collar still overlaps the drone dock")
    return shift_y


def remove_embedded_command_relay_hardware(builder):
    """Drop the superseded hidden periscope and hatch set under the new shell."""
    return remove_objects(builder, lambda name: (
        name.startswith("CommandRelayDriverPeriscope_")
        or name.startswith("CommandRelayAccessHatch_")
    ))


def add_c2_side_panels(builder):
    """Fit the C2 access covers and warning stripes to the shelter hull."""
    p = builder.palette
    _cab_floor_z, floor_z = _command_relay_coaming_tops(builder)
    added = []
    for side, tag in ((-1, "L"), (1, "R")):
        added.append(add_superellipse_side_panel(
            builder,
            f"CommandRelayArmor_C2Panel_{tag}",
            COMMAND_RELAY_ELECTRONICS_SHELTER_STATIONS,
            side,
            y_start=-1.22,
            y_end=-0.02,
            z_bottom=1.03,
            z_top=1.43,
            material=p.light_armor,
            exponent=COMMAND_RELAY_SHELL_EXPONENT,
            thickness=0.035,
            embed=0.012,
            y_segments=8,
            z_segments=6,
            host_floor_z=floor_z,
            floor_width_ratio=COMMAND_RELAY_FLOOR_WIDTH_RATIO,
        ))
        added.append(add_superellipse_side_panel(
            builder,
            f"CommandRelayWarning_C2Stripe_{tag}",
            COMMAND_RELAY_ELECTRONICS_SHELTER_STATIONS,
            side,
            y_start=-0.98,
            y_end=-0.26,
            z_bottom=1.18,
            z_top=1.24,
            material=p.warning,
            exponent=COMMAND_RELAY_SHELL_EXPONENT,
            thickness=0.010,
            embed=0.004,
            surface_offset=0.023,
            y_segments=6,
            z_segments=2,
            host_floor_z=floor_z,
            floor_width_ratio=COMMAND_RELAY_FLOOR_WIDTH_RATIO,
        ))
    return added


def seat_command_relay_drone_dock(builder):
    """Lift the dock onto a contoured shoe that follows the shelter roof."""
    bpy.context.view_layer.update()
    tray = next((
        obj for obj in builder.objects
        if obj.type == "MESH" and obj.name == "CommandRelayDroneBay_DroneDock_Tray"
    ), None)
    if tray is None:
        raise ValueError("Command Relay drone dock tray is missing from the builder object set")

    for obj in list(builder.objects):
        if obj.name == "CommandRelayArmor_DroneDockLeveler":
            remove_objects(builder, lambda name: name == obj.name)

    corners = [tray.matrix_world @ Vector(corner) for corner in tray.bound_box]
    bounds = (
        min(point.x for point in corners), max(point.x for point in corners),
        min(point.y for point in corners), max(point.y for point in corners),
    )
    tray_bottom = min(point.z for point in corners)
    dock_center = tray.matrix_world.translation
    roof_z = sample_superellipse_loft_top_z(
        COMMAND_RELAY_ELECTRONICS_SHELTER_STATIONS,
        dock_center.x,
        dock_center.y,
        exponent=COMMAND_RELAY_SHELL_EXPONENT,
        segments=16,
    )
    leveler = add_superellipse_roof_leveler(
        builder,
        "CommandRelayArmor_DroneDockLeveler",
        COMMAND_RELAY_ELECTRONICS_SHELTER_STATIONS,
        bounds,
        roof_z,
        builder.palette.gunmetal,
        exponent=COMMAND_RELAY_SHELL_EXPONENT,
        embed=0.002,
        segments=16,
        x_segments=8,
        y_segments=10,
    )

    lift = roof_z - tray_bottom
    for obj in builder.objects:
        if obj.name.startswith("CommandRelayDroneBay_"):
            obj.location.z += lift
    bpy.context.view_layer.update()
    return leveler, lift


def seat_command_relay_wheel_arches(builder):
    """Clear tire tread and weld each existing fender arch to its host shell."""
    bpy.context.view_layer.update()
    shell = next((
        obj for obj in builder.objects
        if obj.type == "MESH" and obj.name == "CommandRelayArmor_ContinuousSuperstructure"
    ), None)
    tires = [
        obj for obj in builder.objects
        if obj.type == "MESH" and obj.name.endswith("_TireCarcass")
    ]
    arches = [
        obj for obj in builder.objects
        if obj.type == "MESH" and obj.name.endswith("_ArchTop")
    ]
    if shell is None or len(tires) != 6 or len(arches) != 6:
        raise ValueError(
            f"Expected the Command Relay shell, six tires and six arch tops; "
            f"got shell={shell is not None}, tires={len(tires)}, arches={len(arches)}"
        )

    for obj in list(builder.objects):
        if obj.name.startswith("CommandRelayFenderStay_"):
            remove_objects(builder, lambda name, target=obj.name: name == target)

    stations = ((1.45, 1.0), (-0.40, 1.0), (-1.55, 1.0))
    used_tires = set()
    for arch in sorted(arches, key=lambda item: (item.matrix_world.translation.y, item.matrix_world.translation.x)):
        arch_center = arch.matrix_world.translation
        tire = min(
            (item for item in tires if item.name not in used_tires),
            key=lambda item: (item.matrix_world.translation - arch_center).length_squared,
        )
        used_tires.add(tire.name)
        tread_name = tire.name.replace("_TireCarcass", "_TreadBlocks")
        tread = next((obj for obj in builder.objects if obj.name == tread_name), None)
        if tread is None:
            raise ValueError(f"Missing simplified tread blocks for {tire.name}")

        tread_top = max((tread.matrix_world @ Vector(corner)).z for corner in tread.bound_box)
        arch_corners = [arch.matrix_world @ Vector(corner) for corner in arch.bound_box]
        arch_bottom = min(point.z for point in arch_corners)
        target_bottom = tread_top + 0.010
        if arch_bottom < target_bottom:
            arch.location.z += target_bottom - arch_bottom
            bpy.context.view_layer.update()
            arch_corners = [arch.matrix_world @ Vector(corner) for corner in arch.bound_box]
        side = -1 if arch_center.x < 0.0 else 1
        inner_x = min(point.x for point in arch_corners) if side > 0 else max(point.x for point in arch_corners)
        anchor_y, anchor_z = min(stations, key=lambda value: abs(value[0] - arch_center.y))
        hit, shell_surface, shell_normal, _face_index = shell.ray_cast(
            Vector((side * 2.0, anchor_y, anchor_z)),
            Vector((-side, 0.0, 0.0)),
            distance=4.0,
        )
        if not hit:
            raise ValueError(f"No shell surface found for {arch.name} support at Y={anchor_y:.3f}")
        shell_end = shell_surface - shell_normal.normalized() * 0.002
        arch_end = Vector((inner_x + side * 0.006, anchor_y, target_bottom + 0.006))
        delta = arch_end - shell_end
        run = math.hypot(delta.x, delta.z)
        if run <= 1e-5 or abs(delta.y) > 0.01:
            raise ValueError(f"Invalid support direction for {arch.name}: {tuple(delta)}")
        center = (shell_end + arch_end) * 0.5
        yaw = math.atan2(-delta.z, delta.x)
        stay = rmk.make_box(
            f"CommandRelayFenderStay_{arch.name.removesuffix('_ArchTop')}",
            (run + 0.008, 0.080, 0.020),
            center,
            builder.palette.gunmetal,
            bevel=0.004,
            rotation=(0.0, yaw, 0.0),
        )
        builder.objects.append(stay)
    bpy.context.view_layer.update()
    return arches


def refine(builder) -> None:
    """Add protected C2 shelter panels to the Wave 7 relay carrier."""
    builder.locomotion_only = False  # Idle/FireRecoil/Death restored (attack uses RCL_Fire)
    p = builder.palette
    add_continuous_superstructure(builder)
    add_c2_side_panels(builder)
    remove_embedded_command_relay_hardware(builder)
    relocate_command_relay_mast_clear_of_drone_dock(builder)
    seat_command_relay_drone_dock(builder)
    builder.objects.extend(apply_wave34_details(
        "CommandRelay",
        objects=builder.objects,
        materials={"armor": p.primary_armor, "gunmetal": p.gunmetal},
    ))
    remove_objects(builder, lambda name: (
        name.startswith("CommandRelayArmor_Gusset_")
        or name.startswith("CommandRelayPioneerRack_")
        or name.startswith("CommandRelayArmor_PioneerRack_")
    ))
    builder.engine_audio_loc = builder.engine_audio_loc or builder.center_of_mass_loc
    builder.weapon_audio_loc = builder.weapon_audio_loc or builder.gun_pivot
    simplify_all_terrain_wheels(builder)
    seat_command_relay_wheel_arches(builder)
    simplify_low_value_bevel_details(builder, angle_degrees=29.0)


def build() -> None:
    run_preset_build(
        [
            "--name", "CommandRelay",
            "--preset", "command_relay",
            "--camo", "digital",
            "--structural-wear-mask",
            "--rig", "--animate",
            "--colliders", "--gameplay-json",
            "--bake-atlas", "--single-material", "--atlas-resolution", "2048",
            "--multiview",
            "--blend-path", "content/source-art/command-relay.blend",
            "--fbx-path", "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/CommandRelay.fbx",
            "--preview-path", "artifacts/previews/CommandRelay.png",
            "--report-json", "artifacts/previews/CommandRelay-report.json",
        ],
        refine,
    )


if __name__ == "__main__":
    build()
