"""Build the refined Arachnid Hexapod from its canonical preset."""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from mathutils import Vector
from VisualModelAuthoringSupport import (
    add_box, add_link, add_loft_shell, add_uv_ellipsoid, add_y_axis_tube, hex_color,
    remove_objects, replace_link_mesh, run_preset_build,
    set_material_color, simplify_low_value_bevel_details,
)
from ActiveUnitVisualEnhancements import apply_wave2_details, apply_wave34_details


def refine(builder):
    p = builder.palette
    set_material_color(p.warning, hex_color("#D09B43"), metallic=0.36, roughness=0.42)
    remove_objects(builder, lambda n: any(token in n for token in (
        "Armor_Hull", "Armor_UpperDeck", "Armor_ThoraxCore", "Turret_", "MissilePod_",
        "Sensor_Visor_", "Sensor_Antenna", "Hull_Greeble", "APS_",
        "DeckEdgeWeld_", "TurretRingGuard_", "Armor_TurretRingGuard_",
    )))
    # The preset-era hatch sits inside the replacement carapace; Wave 3/4 adds
    # the correctly seated top hatch later in this refinement.
    remove_objects(builder, lambda name: name.startswith("ArachnidHexapodAccessHatch_"))

    # The carapace uses more profile samples for a cleaner outline while its
    # broad facets remain visible as fabricated armor planes.
    add_loft_shell(builder, "ArachnidHexapodArmor_ArmoredCarapace", [
        (0.64, 0.98, 1.54, -1.56),
        (0.79, 1.22, 1.72, -1.62),
        (1.02, 1.30, 1.78, -1.53),
        (1.30, 1.18, 1.59, -1.28),
        (1.52, 0.95, 1.26, -1.00),
        (1.62, 0.74, 0.92, -0.72),
    ], p.primary_armor, exponent=0.74, smooth=True, segments=32,
       smooth_caps=False, smooth_side_ends=True)

    # Hip armor overlaps the torso and actuator roots so each limb appears mounted.
    for side, tag in ((-1, "L"), (1, "R")):
        add_loft_shell(builder, f"ArachnidHexapodArmor_HipFairing_{tag}", [
            (0.78, 0.26, 0.98, -0.98),
            (0.91, 0.34, 1.04, -1.00),
            (1.15, 0.29, 0.90, -0.88),
            (1.27, 0.21, 0.76, -0.74),
        ], p.light_armor, center_x=side * 0.98, exponent=0.70, smooth=True,
           segments=20, smooth_caps=False, smooth_side_ends=True)

    # Raised, tapered flank plates sit proud of the carapace and catch light at
    # their edges, while the dark shell beneath remains a single continuous part.
    for side, tag in ((-1, "L"), (1, "R")):
        add_loft_shell(builder, f"ArachnidHexapodArmor_FlankPlate_{tag}", [
            (1.45, 0.22, 0.66, -1.04),
            (1.50, 0.31, 0.82, -1.15),
            (1.60, 0.27, 0.72, -1.04),
            (1.64, 0.20, 0.55, -0.88),
        ], p.light_armor, center_x=side * 0.70, exponent=0.42, smooth=False)

    # Integrated forward sensor brow and recessed lens band.
    add_loft_shell(builder, "ArachnidHexapodArmor_OpticCrown", [
        (1.43, 0.40, 1.53, 0.80),
        (1.50, 0.50, 1.59, 0.68),
        (1.62, 0.42, 1.51, 0.80),
        (1.70, 0.30, 1.38, 0.90),
    ], p.light_armor, exponent=0.74, smooth=True, segments=20,
       smooth_caps=False, smooth_side_ends=True)
    add_box(builder, "ArachnidHexapodSensor_OpticGlazing",
            (0.0, 1.50, 1.59), (0.86, 0.055, 0.15), p.gunmetal)
    for index, x in enumerate((-0.28, -0.09, 0.09, 0.28), start=1):
        add_box(builder, f"ArachnidHexapodSensor_Optic_{index}",
                (x, 1.535, 1.60), (0.072, 0.025, 0.067), p.emissive)
    add_box(builder, "ArachnidHexapodArmor_OpticBrow",
            (0.0, 1.47, 1.70), (0.78, 0.09, 0.08), p.primary_armor)

    # Six forward-facing missile cells sit inside a low dorsal launcher cradle.
    add_loft_shell(builder, "ArachnidHexapodArmor_DorsalMissileCradle", [
        (1.48, 0.45, 0.06, -1.13),
        (1.57, 0.53, 0.12, -1.18),
        (1.75, 0.48, 0.00, -1.06),
        (1.79, 0.39, -0.07, -0.93),
    ], p.primary_armor, exponent=0.56, smooth=False)
    for row, z in enumerate((1.60, 1.74), start=1):
        for col, x in enumerate((-0.25, 0.0, 0.25), start=1):
            tube = f"ArachnidHexapodMissileCell_R{row}C{col}"
            add_y_axis_tube(builder, tube, (x, -0.06, z), 0.074, 0.46, p.gunmetal, segments=12)
            add_y_axis_tube(builder, tube + "_DarkBore", (x, 0.174, z), 0.043, 0.012,
                            p.tread, segments=12)
    add_box(builder, "ArachnidHexapodArmor_RearPowerDeck",
            (0.0, -1.20, 1.08), (0.96, 0.35, 0.10), p.gunmetal)
    for index, x in enumerate((-0.32, -0.11, 0.11, 0.32), start=1):
        add_box(builder, f"ArachnidHexapodEngine_CoolingFin_{index}",
                (x, -1.20, 1.15), (0.075, 0.29, 0.045), p.wheel_rim)

    # Exposed rams connect the hip actuators to the plated femurs. The center
    # pair stays static in the source rig but has its own visible drive rods.
    for side, sx in (("L", -1.0), ("R", 1.0)):
        for fore, fy in (("F", 1.0), ("R", -1.0)):
            add_link(builder, f"ArachnidHexapodHydraulicRam_{side}{fore}",
                     (sx * 1.14, fy * 1.21, 0.79), (sx * 1.69, fy * 1.63, 1.04),
                     0.060, 0.045, p.gunmetal)
            add_link(builder, f"ArachnidHexapodHydraulicRod_{side}{fore}",
                     (sx * 1.30, fy * 1.34, 0.84), (sx * 1.72, fy * 1.66, 1.05),
                     0.022, 0.020, p.wheel_rim)
    for side, sx in (("L", -1.0), ("R", 1.0)):
        add_link(builder, f"ArachnidHexapodHydraulicRam_M{side}",
                 (sx * 1.18, 0.0, 0.91), (sx * 1.65, 0.0, 1.08),
                 0.052, 0.040, p.gunmetal)

    # Replace round stick-like links with broad, tapered armored limbs while
    # preserving canonical names so the four driven legs remain rig-bindable.
    for obj in builder.objects:
        name = obj.name
        if "CrawlerLeg_" in name and name.endswith("_FemurBoom"):
            is_left = "_L_" in name
            is_front = "_F_" in name
            sx, fy = (-1.0 if is_left else 1.0), (1.0 if is_front else -1.0)
            replace_link_mesh(obj, (sx * 1.10, fy * 1.20, 0.74),
                              (sx * 1.92, fy * 1.83, 1.05), 0.29, 0.21,
                              p.light_armor, ring_segments=10, exponent=0.82)
        elif "CrawlerLeg_" in name and name.endswith("_TibiaStrut"):
            is_left = "_L_" in name
            is_front = "_F_" in name
            sx, fy = (-1.0 if is_left else 1.0), (1.0 if is_front else -1.0)
            replace_link_mesh(obj, (sx * 1.92, fy * 1.83, 1.05),
                              (sx * 2.35, fy * 2.25, 0.18), 0.23, 0.17,
                              p.primary_armor, ring_segments=10, exponent=0.82)
        elif "Armor_MidThigh_" in name:
            is_left = name.endswith("_ML")
            sx = -1.0 if is_left else 1.0
            replace_link_mesh(obj, (sx * 1.15, 0.0, 0.95),
                              (sx * 1.62, 0.0, 0.72), 0.23, 0.17,
                              p.light_armor, ring_segments=10, exponent=0.82)
        elif "Armor_MidShin_" in name:
            is_left = name.endswith("_ML")
            sx = -1.0 if is_left else 1.0
            replace_link_mesh(obj, (sx * 1.62, 0.0, 0.72),
                              (sx * 2.23, 0.0, 0.17), 0.18, 0.14,
                              p.primary_armor, ring_segments=10, exponent=0.82)
        elif "KneeKnuckle" in name:
            obj.scale = tuple(float(value) * 1.12 for value in obj.scale)
        elif "FootPad" in name:
            obj.data.materials.clear()
            obj.data.materials.append(p.wheel_rim)
        elif "FootTread" in name or "MidFootPad" in name:
            obj.data.materials.clear()
            obj.data.materials.append(p.tread)

    # The lateral middle pair now has a visible knee joint and a two-link bend
    # instead of a single straight support under the thorax.
    for side, tag in ((-1, "L"), (1, "R")):
        knee = (side * 1.62, 0.0, 0.72)
        add_uv_ellipsoid(builder, f"ArachnidHexapodJoint_MidKnee_{tag}", knee,
                         (0.22, 0.20, 0.19), p.gunmetal, segments=12, rings=6)
        add_y_axis_tube(builder, f"ArachnidHexapodJoint_MidKneeAxle_{tag}",
                        knee, 0.085, 0.42, p.wheel_rim, segments=12)

    # The antenna stays low and attached to the rear deck rather than floating
    # through the old turret stack.
    add_box(builder, "ArachnidHexapodSensor_AntennaPedestal",
            (0.0, -1.31, 1.13), (0.22, 0.20, 0.12), p.gunmetal)
    add_box(builder, "ArachnidHexapodSensor_AntennaMast",
            (0.0, -1.31, 1.34), (0.035, 0.035, 0.34), p.warning)

    builder.hull_width = 2.60
    builder.hull_length = 3.35
    builder.hull_deck_z = 1.58
    builder.turret_center = None
    builder.turret_pitch_loc = None
    builder.gun_pivot = Vector((0.0, 0.18, 1.67))
    builder.gun_dir = Vector((0.0, 1.0, 0.0))
    builder.primary_firepoint_locs = [Vector((-0.25, 0.18, 1.60)),
                                      Vector((0.25, 0.18, 1.60))]
    builder.sensor_loc = Vector((0.0, 1.52, 1.60))
    builder.antenna_loc = Vector((0.0, -1.31, 1.18))
    builder.damage_smoke_loc = Vector((0.0, -1.08, 1.38))
    builder.damage_fire_loc = Vector((0.0, -0.76, 1.24))
    builder.engine_audio_loc = Vector((0.0, -1.20, 0.90))
    builder.weapon_audio_loc = Vector((0.0, 0.18, 1.67))
    builder.center_of_mass_loc = Vector((0.0, 0.0, 0.83))
    builder.foot_loc_l = None
    builder.foot_loc_r = None
    builder.quad_feet = {
        "FL": Vector((-2.35, 2.25, 0.0)), "FR": Vector((2.35, 2.25, 0.0)),
        "ML": Vector((-2.45, 0.0, 0.0)), "MR": Vector((2.45, 0.0, 0.0)),
        "RL": Vector((-2.35, -2.25, 0.0)), "RR": Vector((2.35, -2.25, 0.0)),
    }

    builder.objects.extend(apply_wave2_details(
        "ArachnidHexapod",
        objects=builder.objects,
        materials={"armor": p.primary_armor, "gunmetal": p.gunmetal, "warning": p.warning, "tread": p.tread},
    ))

    builder.objects.extend(apply_wave34_details(
        "ArachnidHexapod",
        objects=builder.objects,
        materials={"armor": p.primary_armor, "gunmetal": p.gunmetal, "warning": p.warning, "tread": p.tread},
    ))

    # Keep later shared detail waves from reintroducing hardware that has no
    # host or motion system on this fixed-launcher chassis.
    remove_objects(builder, lambda name: any(token in name for token in (
        "ArachnidHexapodAccessHatch_",
        "ArachnidHexapodArmor_TurretRingGuard_",
        "ArachnidHexapodArmor_TrunnionBracket_",
    )))

    simplify_low_value_bevel_details(builder, max_dimension_m=0.60, angle_degrees=29.0)

    # Create canonical sockets now so the two mission-specific anchors can be
    # moved without duplicating the generator's authoritative hierarchy.
    builder.add_unity_locators()
    for socket in builder.locator_sockets:
        if socket.name == "Socket_Antenna":
            socket.location = builder.antenna_loc
        elif socket.name == "Socket_DamageSmoke":
            socket.location = builder.damage_smoke_loc
        elif socket.name == "Socket_DamageFire":
            socket.location = builder.damage_fire_loc
        elif socket.name == "Socket_EngineAudio":
            socket.location = builder.engine_audio_loc
        elif socket.name == "Socket_WeaponAudio":
            socket.location = builder.weapon_audio_loc


def build():
    args = [
        "--name", "ArachnidHexapod", "--preset", "arachnid_hexapod",
        "--primary-color", "#35495A", "--light-armor-color", "#788793",
        "--emissive-color", "#25B5C4", "--wear", "factory_new",
        "--no-panel-lines", "--normal-intensity", "0.35",
        "--bake-atlas", "--single-material", "--atlas-resolution", "2048",
        "--structural-wear-mask", "--multiview",
        "--blend-path", "content/source-art/arachnid-hexapod.blend",
        "--fbx-path", "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/ArachnidHexapod.fbx",
        "--preview-path", "artifacts/previews/arachnid-hexapod.png",
        "--report-json", "artifacts/previews/arachnid-hexapod-report.json",
    ]
    run_preset_build(args, refine)


if __name__ == "__main__":
    build()
