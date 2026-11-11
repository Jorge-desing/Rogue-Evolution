"""Build the refined Overlord Command HQ from its canonical preset."""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from mathutils import Vector

from VisualModelAuthoringSupport import (
    add_box, add_link, add_loft_shell, add_uv_ellipsoid, hex_color,
    remove_objects, run_preset_build, set_material_color,
    simplify_command_wheels,
)
from ActiveUnitVisualEnhancements import apply_wave2_details, apply_wave34_details


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

    add_loft_shell(builder, "OverlordCommandHQArmor_ContinuousChassis", [
        (0.42, 0.84, 2.10, -2.10),
        (0.61, 0.98, 2.18, -2.16),
        (0.90, 1.02, 2.14, -2.08),
        (1.17, 0.93, 1.98, -1.94),
    ], p.gunmetal, exponent=0.54, smooth=False)

    # Forward armored cab: the windshield line slopes into the roof instead
    # of reading as a square cab box.
    add_loft_shell(builder, "OverlordCommandHQArmor_ArmoredCabShell", [
        (0.84, 0.72, 2.02, 0.34),
        (1.12, 0.87, 2.18, 0.28),
        (1.60, 0.84, 1.98, 0.40),
        (1.91, 0.77, 1.78, 0.57),
        (1.99, 0.66, 1.65, 0.68),
    ], p.primary_armor, exponent=0.58, smooth=False)

    # Rear C2 shelter sits into the chassis and meets the cab at its shoulder.
    add_loft_shell(builder, "OverlordCommandHQArmor_CommandShelterShell", [
        (0.95, 0.91, 0.54, -1.98),
        (1.28, 1.04, 0.54, -1.94),
        (1.83, 1.00, 0.48, -1.82),
        (2.03, 0.90, 0.39, -1.70),
        (2.08, 0.78, 0.28, -1.57),
    ], p.light_armor, exponent=0.60, smooth=False)

    # A wide armored glazing slit replaces the blockout's tiny paired optics.
    add_box(builder, "OverlordCommandHQSensor_CabGlazingFrame",
            (0.0, 2.025, 1.47), (1.24, 0.085, 0.23), p.gunmetal)
    for index, x in enumerate((-0.38, 0.0, 0.38), start=1):
        add_box(builder, f"OverlordCommandHQSensor_CabGlazing_{index}",
                (x, 2.073, 1.48), (0.29, 0.025, 0.13), p.emissive)
    add_loft_shell(builder, "OverlordCommandHQArmor_GlazingBrow", [
        (1.58, 0.67, 2.00, 1.82),
        (1.68, 0.69, 1.96, 1.80),
        (1.73, 0.60, 1.90, 1.82),
    ], p.primary_armor, exponent=0.42, smooth=False)

    # Reinforced front bumper is attached to the chassis nose and protects the
    # glazing line without adding a floating add-on block.
    add_loft_shell(builder, "OverlordCommandHQArmor_NoseBumper", [
        (0.52, 0.84, 2.26, 2.00),
        (0.66, 0.90, 2.31, 1.98),
        (0.82, 0.84, 2.24, 1.99),
    ], p.primary_armor, exponent=0.46, smooth=False)

    # Flush service doors, vent bank, and protected comms cabling read as
    # maintenance access on the shelter sides, not as extra cargo blocks.
    for side, tag in ((-1, "L"), (1, "R")):
        add_box(builder, f"OverlordCommandHQArmor_ServiceDoor_{tag}",
                (side * 1.005, -0.72, 1.48), (0.045, 0.58, 0.48), p.primary_armor)
        for index in range(3):
            add_box(builder, f"OverlordCommandHQEngine_Vent_{tag}_{index + 1}",
                    (side * 0.78, -1.86, 1.30 + index * 0.085),
                    (0.20, 0.06, 0.035), p.gunmetal)

    # Purposeful 8x8 tire tread blocks retain silhouette while dropping the
    # repeated bolt and lug micro-geometry that dominated the blockout budget.
    removed_wheel_bits = simplify_command_wheels(builder)

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
          "kept eight wheel carcasses and added low-profile C2 arrays.")


def build():
    args = [
        "--name", "OverlordCommandHQ", "--preset", "overlord_command_hq",
        "--primary-color", "#59634E", "--light-armor-color", "#A2A28A",
        "--emissive-color", "#76CFC1", "--camo", "none",
        "--wear", "factory_new", "--no-panel-lines", "--normal-intensity", "0.30",
        "--target-budget", str(rmk.DEFAULT_FACE_BUDGET), "--bake-atlas", "--single-material",
        "--atlas-resolution", "2048", "--structural-wear-mask", "--multiview",
        "--blend-path", "content/source-art/overlord-command-hq.blend",
        "--fbx-path", "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/OverlordCommandHQ.fbx",
        "--preview-path", "artifacts/previews/overlord-command-hq.png",
        "--report-json", "artifacts/previews/overlord-command-hq-report.json",
    ]
    run_preset_build(args, refine)


if __name__ == "__main__":
    build()
