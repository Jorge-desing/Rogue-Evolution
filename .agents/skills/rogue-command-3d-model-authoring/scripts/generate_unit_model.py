"""generate_unit_model.py: CLI and scriptable vehicle generator for Rogue Command.

Usage with Blender 4.5.14 LTS:
    .\\.tools\\Blender-4.5.14\\...\\blender.exe --background --python tools\\generate_unit_model.py -- [options]

Archetypes:
    tank, walker, bike, artillery, arc_tank, hover, spider, vtol, halftrack, super_heavy, buggy

Weapons:
    cannon, gatling, missile, arc, howitzer, dual_cannon

Presets:
    siege_breaker, recon_interceptor, gunship_alpha, apc_enforcer, dune_raider

Options:
    --name <UnitName>          Vehicle class name (e.g., ColossusSuperHeavy)
    --archetype <type>         Vehicle archetype (default: tank)
    --faction <faction>        Color palette: 'vanguard', 'siege', 'syndicate', 'arctic'
    --camo <pattern>           Camouflage variant: 'none', 'desert', 'urban', 'arctic', 'woodland'
    --weapon <type>            Primary weapon: 'cannon', 'gatling', 'missile', 'arc', 'howitzer', 'dual_cannon'
    --preset <preset>          Quick archetype and weapon preset
    --rollcage                 Attach tubular roll cage structure
    --quad-tracks              Attach 4 independent heavy track pods
    --tilt-rotors              Attach dual aerodynamic ducted tilt-rotors
    --dozer                    Attach frontal heavy dozer ram blade
    --skirts                   Attach armored side skirts over track run (default: True)
    --smoke                    Attach multi-tube smoke discharger banks
    --radar                    Attach command/recon radar dish
    --sponsons                 Attach armored external side sponsons
    --dirt                     Bake procedural vertex ambient dirt/cavity shading (default: True)
    --locators                 Generate Unity locator sockets (default: True)
    --lod                      Print low-poly triangle budget diagnostics and LOD recommendations
    --multiview                Render 4-perspective orthographic sheet (ISO, TOP, FRONT, SIDE)
    --out-dir <path>           Base output directory (default: repo root)
"""

from __future__ import annotations
import argparse
import math
import sys
from pathlib import Path

# Add script directory and repo tools to sys.path so RogueModelKit is found
SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parent if (SCRIPT_DIR.parent / "content").exists() else SCRIPT_DIR.parents[3]
sys.path.insert(0, str(SCRIPT_DIR))
sys.path.insert(0, str(REPO_ROOT / "tools"))

import bpy
from mathutils import Vector
import RogueModelKit as rmk


PRESETS = {
    "siege_breaker": {
        "archetype": "super_heavy",
        "weapon": "dual_cannon",
        "faction": "siege",
        "sponsons": True,
        "smoke": True,
        "radar": True,
    },
    "recon_interceptor": {
        "archetype": "buggy",
        "weapon": "gatling",
        "faction": "vanguard",
        "rollcage": True,
        "radar": True,
    },
    "gunship_alpha": {
        "archetype": "vtol",
        "weapon": "missile",
        "faction": "syndicate",
    },
    "apc_enforcer": {
        "archetype": "halftrack",
        "weapon": "gatling",
        "faction": "vanguard",
        "smoke": True,
    },
    "dune_raider": {
        "archetype": "bike",
        "weapon": "gatling",
        "faction": "siege",
    },
}


def parse_args():
    # Blender passes all args after '--' to Python script
    argv = sys.argv
    if "--" in argv:
        script_args = argv[argv.index("--") + 1 :]
    else:
        script_args = []

    parser = argparse.ArgumentParser(description="Generate Rogue Command 3D vehicle models.")
    parser.add_argument("--name", default="CustomRogueVehicle", help="Unit model name")
    parser.add_argument(
        "--archetype",
        default="tank",
        choices=["tank", "walker", "bike", "artillery", "arc_tank", "hover", "spider", "vtol", "halftrack", "super_heavy", "buggy"],
        help="Vehicle archetype",
    )
    parser.add_argument(
        "--faction",
        default="vanguard",
        choices=["vanguard", "siege", "syndicate", "arctic"],
        help="Visual 5-tone faction palette",
    )
    parser.add_argument(
        "--camo",
        default="none",
        choices=["none", "desert", "urban", "arctic", "woodland"],
        help="Camouflage variant",
    )
    parser.add_argument(
        "--weapon",
        default="cannon",
        choices=["cannon", "gatling", "missile", "arc", "howitzer", "dual_cannon"],
        help="Primary weapon subsystem",
    )
    parser.add_argument(
        "--preset",
        default="none",
        choices=["none", "siege_breaker", "recon_interceptor", "gunship_alpha", "apc_enforcer", "dune_raider"],
        help="Preconfigured vehicle loadout",
    )
    parser.add_argument("--rollcage", action="store_true", help="Include tubular roll cage")
    parser.add_argument("--quad-tracks", action="store_true", help="Include 4 independent track pods")
    parser.add_argument("--tilt-rotors", action="store_true", help="Include ducted tilt-rotors")
    parser.add_argument("--dozer", action="store_true", help="Include frontal dozer ram blade")
    parser.add_argument("--skirts", action="store_true", default=True, help="Include armored side skirts")
    parser.add_argument("--smoke", action="store_true", help="Include multi-tube smoke discharger banks")
    parser.add_argument("--radar", action="store_true", help="Include command/recon radar dish")
    parser.add_argument("--sponsons", action="store_true", help="Include armored external side sponsons")
    parser.add_argument("--locators", action="store_true", default=True, help="Generate Unity locator sockets")
    parser.add_argument("--dirt", action="store_true", default=True, help="Bake procedural vertex ambient dirt/cavity shading")
    parser.add_argument("--wear", default="field_tested", choices=["pristine", "field_tested", "battle_hardened"], help="Procedural surface wear level")
    parser.add_argument("--lods", action="store_true", help="Generate multi-level LOD chain (LOD0, LOD1, LOD2) and export _LODs.fbx")
    parser.add_argument("--colliders", action="store_true", help="Generate low-poly physics collision hulls")
    parser.add_argument("--gameplay-json", action="store_true", help="Export companion .gameplay.json specification")
    parser.add_argument("--wreckage", action="store_true", help="Export burned / dislodged turret wreckage variant")
    parser.add_argument("--target-budget", type=int, default=4000, help="Target polygon budget for profiling")
    parser.add_argument("--lod", action="store_true", help="Calculate polygon budget and recommend LOD reductions")
    parser.add_argument("--smooth-angle", type=float, default=38.0, help="Smart angle-weighted auto-smooth threshold in degrees")
    parser.add_argument("--multiview", action="store_true", help="Render 4 orthographic views")
    parser.add_argument("--out-dir", default=None, help="Base root directory for outputs")
    parser.add_argument("--blend-path", default=None, help="Explicit .blend output file path")
    parser.add_argument("--fbx-path", default=None, help="Explicit .fbx output file path")
    parser.add_argument("--preview-path", default=None, help="Explicit preview render output file path")

    args = parser.parse_args(script_args)

    # Apply preset overrides for unspecified parameters
    if args.preset and args.preset != "none" and args.preset in PRESETS:
        p = PRESETS[args.preset]
        if "--archetype" not in script_args:
            args.archetype = p.get("archetype", args.archetype)
        if "--weapon" not in script_args:
            args.weapon = p.get("weapon", args.weapon)
        if "--faction" not in script_args:
            args.faction = p.get("faction", args.faction)
        for flag in ("sponsons", "smoke", "radar", "rollcage", "dozer"):
            if p.get(flag) and f"--{flag}" not in script_args:
                setattr(args, flag, True)

    return args


def resolve_path(p: str | Path | None, base: Path) -> Path | None:
    if p is None:
        return None
    path_obj = Path(p)
    return path_obj if path_obj.is_absolute() else (base / path_obj).resolve()


def main():
    args = parse_args()
    rmk.clear_scene()

    out_root = resolve_path(args.out_dir, REPO_ROOT) or REPO_ROOT
    blend_path = resolve_path(args.blend_path, REPO_ROOT) or (out_root / "content" / "source-art" / f"{args.name.lower()}.blend")
    fbx_path = resolve_path(args.fbx_path, REPO_ROOT) or (
        out_root
        / "unity"
        / "RogueCommandAssetAuthoring"
        / "Assets"
        / "RogueCommandExpansionSample"
        / "Models"
        / f"{args.name}.fbx"
    )
    preview_path = resolve_path(args.preview_path, REPO_ROOT) or (out_root / "artifacts" / "previews" / f"{args.name.lower()}.png")
    card_path = None

    builder = rmk.RogueVehicleBuilder(args.name, faction=args.faction, camo=args.camo)
    builder.set_archetype(args.archetype)
    builder.set_wear_level(args.wear)

    gameplay_meta_path = (fbx_path.parent / f"{args.name}.gameplay.json") if args.gameplay_json else None
    wreckage_fbx_path = (fbx_path.parent / f"{args.name}_Wreckage.fbx") if args.wreckage else None

    # 1. Mobility and Chassis by Archetype
    if args.archetype == "walker":
        builder.add_bipedal_legs(hip_height=1.45, hip_spacing_x=0.88)
        builder.objects.extend([
            rmk.make_box(f"{args.name}Armor_Pelvis", (0.92, 0.65, 0.42), (0.0, -0.02, 1.40), builder.palette.gunmetal, 0.08),
            rmk.make_box(f"{args.name}Armor_Torso", (1.14, 0.74, 0.78), (0.0, 0.02, 1.96), builder.palette.primary_armor, 0.09),
            rmk.make_box(f"{args.name}Armor_ChestPlate", (1.02, 0.22, 0.45), (0.0, 0.42, 2.04), builder.palette.light_armor, 0.065),
            rmk.make_box(f"{args.name}Armor_PowerCell", (0.72, 0.40, 0.60), (0.0, -0.52, 2.03), builder.palette.gunmetal, 0.07),
        ])
        builder.add_sensor_visor()

    elif args.archetype == "bike":
        builder.add_all_terrain_wheels(wheelbase_y=1.95, track_width_x=0.0, wheel_radius=0.38, wheel_width=0.22)
        builder.objects.extend([
            rmk.make_box(f"{args.name}Armor_SpineFrame", (0.34, 1.85, 0.38), (0.0, -0.05, 0.72), builder.palette.primary_armor, 0.04),
            rmk.make_box(f"{args.name}Armor_EngineBlock", (0.46, 0.68, 0.42), (0.0, -0.18, 0.82), builder.palette.gunmetal, 0.035),
            rmk.make_box(f"{args.name}Armor_CockpitCowl", (0.42, 0.58, 0.32), (0.0, 0.45, 1.05), builder.palette.light_armor, 0.03,
                          (math.radians(-16), 0.0, 0.0)),
            rmk.make_rod(f"{args.name}Joint_Fork_L", Vector((-0.18, 0.45, 1.05)), Vector((-0.16, 0.98, 0.38)), 0.035, builder.palette.gunmetal),
            rmk.make_rod(f"{args.name}Joint_Fork_R", Vector((0.18, 0.45, 1.05)), Vector((0.16, 0.98, 0.38)), 0.035, builder.palette.gunmetal),
            rmk.make_box(f"{args.name}Joint_Handlebars", (0.64, 0.06, 0.05), (0.0, 0.38, 1.18), builder.palette.gunmetal, 0.01),
            rmk.make_rod(f"{args.name}Joint_Swingarm_L", Vector((-0.18, -0.18, 0.65)), Vector((-0.16, -0.98, 0.38)), 0.038, builder.palette.gunmetal),
            rmk.make_rod(f"{args.name}Joint_Swingarm_R", Vector((0.18, -0.18, 0.65)), Vector((0.16, -0.98, 0.38)), 0.038, builder.palette.gunmetal),
        ])
        builder.add_sensor_visor()
        for side in (-1, 1):
            side_tag = "L" if side < 0 else "R"
            builder.objects.append(rmk.make_box(
                f"{args.name}Armor_SidePod_{side_tag}",
                (0.18, 0.58, 0.28), (side * 0.36, -0.12, 0.72), builder.palette.primary_armor, 0.02
            ))

    elif args.archetype == "artillery":
        builder.add_sloped_hull(length=3.4, width=2.6, height=0.96)
        builder.add_continuous_tracks(length=3.2, height=0.68, belt_width=0.36, roadwheels=6, with_skirts=args.skirts)
        builder.add_engine_deck(slat_count=4, with_exhausts=True)
        builder.add_turret(size=(1.5, 1.7, 0.48), center_y=0.12)
        for side in (-1, 1):
            side_tag = "L" if side < 0 else "R"
            builder.objects.extend([
                rmk.make_box(f"{args.name}Armor_RecoilSpade_{side_tag}", (0.20, 0.24, 0.26),
                             (side * 0.62, -1.62, 0.56), builder.palette.primary_armor, 0.025),
                rmk.make_box(f"{args.name}Armor_RecoilBlade_{side_tag}", (0.26, 0.08, 0.32),
                             (side * 0.62, -1.78, 0.36), builder.palette.gunmetal, 0.02,
                             rotation=(math.radians(22), 0.0, 0.0)),
            ])
        builder.add_spring_antenna()

    elif args.archetype == "arc_tank":
        builder.add_sloped_hull(length=2.8, width=2.3, height=0.86)
        builder.add_continuous_tracks(length=2.7, height=0.58, belt_width=0.32, roadwheels=5, with_skirts=args.skirts)
        builder.add_engine_deck(slat_count=3, with_exhausts=False)
        builder.add_turret(size=(1.3, 1.4, 0.42), center_y=0.0)
        builder.add_arc_emitter(core_radius=0.22, prongs=3)
        builder.add_sensor_visor()

    elif args.archetype == "hover":
        builder.add_sloped_hull(length=2.9, width=2.5, height=0.78)
        builder.add_hover_skimmer(float_height=0.48)
        builder.add_turret(size=(1.35, 1.5, 0.42), center_y=0.06)
        builder.add_sensor_visor()
        builder.add_spring_antenna()

    elif args.archetype == "spider":
        builder.add_sloped_hull(length=2.6, width=2.4, height=0.82)
        builder.add_quad_crawler_legs(knee_height=0.98, reach_x=1.12, reach_y=0.88)
        builder.add_turret(size=(1.45, 1.55, 0.44), center_y=0.04)
        builder.add_sensor_visor()

    elif args.archetype == "vtol":
        builder.is_hover = True
        builder.objects.extend([
            rmk.make_box(f"{args.name}Armor_Fuselage", (1.35, 2.60, 0.58), (0.0, 0.10, 0.72), builder.palette.primary_armor, 0.04),
            rmk.make_box(f"{args.name}Armor_CockpitNose", (1.05, 0.92, 0.42), (0.0, 1.45, 0.68), builder.palette.light_armor, 0.035,
                         rotation=(math.radians(-16), 0.0, 0.0)),
            rmk.make_box(f"{args.name}Armor_TailBoom", (0.34, 1.65, 0.26), (0.0, -1.85, 0.82), builder.palette.primary_armor, 0.03),
            rmk.make_box(f"{args.name}Armor_HorizStab", (1.15, 0.38, 0.06), (0.0, -2.50, 0.88), builder.palette.light_armor, 0.015),
            rmk.make_box(f"{args.name}Armor_VertFin_L", (0.05, 0.42, 0.48), (-0.42, -2.55, 1.05), builder.palette.primary_armor, 0.015,
                         rotation=(0.0, math.radians(-18), 0.0)),
            rmk.make_box(f"{args.name}Armor_VertFin_R", (0.05, 0.42, 0.48), (0.42, -2.55, 1.05), builder.palette.primary_armor, 0.015,
                         rotation=(0.0, math.radians(18), 0.0)),
            rmk.make_box(f"{args.name}Armor_WingSponson_L", (0.85, 0.52, 0.14), (-1.05, 0.15, 0.72), builder.palette.light_armor, 0.025),
            rmk.make_box(f"{args.name}Armor_WingSponson_R", (0.85, 0.52, 0.14), (1.05, 0.15, 0.72), builder.palette.light_armor, 0.025),
            # Landing skids resting at Z >= 0.06m
            rmk.make_box(f"{args.name}Joint_LandingSkid_L", (0.08, 1.85, 0.05), (-0.55, 0.05, 0.06), builder.palette.gunmetal, 0.01),
            rmk.make_box(f"{args.name}Joint_LandingSkid_R", (0.08, 1.85, 0.05), (0.55, 0.05, 0.06), builder.palette.gunmetal, 0.01),
            rmk.make_rod(f"{args.name}Joint_SkidStrut_FL", (-0.55, 0.75, 0.08), (-0.52, 0.65, 0.45), 0.032, builder.palette.gunmetal),
            rmk.make_rod(f"{args.name}Joint_SkidStrut_FR", (0.55, 0.75, 0.08), (0.52, 0.65, 0.45), 0.032, builder.palette.gunmetal),
            rmk.make_rod(f"{args.name}Joint_SkidStrut_RL", (-0.55, -0.65, 0.08), (-0.52, -0.55, 0.45), 0.032, builder.palette.gunmetal),
            rmk.make_rod(f"{args.name}Joint_SkidStrut_RR", (0.55, -0.65, 0.08), (0.52, -0.55, 0.45), 0.032, builder.palette.gunmetal),
        ])
        builder.add_sensor_visor()
        builder.add_tiltrotors(location_offset=(1.45, 0.15, 0.75), radius=0.44, tilt_deg=14.0)
        builder.turret_center = Vector((0.0, 1.05, 0.45))

    elif args.archetype == "halftrack":
        # Front steerable all-terrain wheels
        wheel_z = builder.ground_clearance + 0.38 + 0.035
        for side in (-1, 1):
            tag = "L" if side < 0 else "R"
            builder.objects.extend(rmk.make_all_terrain_wheel_assembly(
                f"{args.name}Wheel_Front_{tag}",
                location=(side * 0.88, 1.25, wheel_z),
                radius=0.38, width=0.26,
                tire_mat=builder.palette.tread, rim_mat=builder.palette.wheel_rim,
                hub_mat=builder.palette.gunmetal, accent_mat=builder.palette.warning
            ))
        builder.objects.append(rmk.make_box(
            f"{args.name}Joint_FrontAxle", (1.65, 0.14, 0.12), (0.0, 1.25, wheel_z), builder.palette.gunmetal, 0.02
        ))
        # Rear continuous track bogie
        for side in (-1, 1):
            builder.objects.extend(rmk.make_continuous_track_assembly(
                f"{args.name}Treads",
                x_pos=side * 0.92,
                length=1.95, height=0.58, belt_width=0.30,
                roadwheel_count=4,
                tread_mat=builder.palette.tread,
                rim_mat=builder.palette.wheel_rim,
                hub_mat=builder.palette.wheel_hub,
                axle_mat=builder.palette.gunmetal,
                fender_mat=builder.palette.light_armor,
                skirt_mat=builder.palette.primary_armor if args.skirts else None,
                accent_mat=builder.palette.warning,
                ground_z=builder.ground_clearance,
                center_y=-0.55
            ))
        # Armored engine hood, cab, and rear troop compartment
        builder.objects.append(
            rmk.make_sloped_glacis_hull(
                f"{args.name}Armor_Hood",
                length_front=0.85, length_rear=0.65,
                width_front=1.20, width_rear=1.35,
                height_lower=builder.ground_clearance + 0.35, height_upper=0.92,
                material=builder.palette.primary_armor, bevel=0.035
            )
        )
        builder.objects.append(
            rmk.make_box(f"{args.name}Armor_Cab", (1.38, 0.88, 0.62), (0.0, 0.20, 1.05), builder.palette.primary_armor, 0.035)
        )
        builder.objects.extend(
            rmk.make_recessed_sensor_visor(
                f"{args.name}Sensor_CabVisor",
                location=(0.0, 0.62, 1.18),
                size=(0.58, 0.12, 0.15),
                armor_mat=builder.palette.light_armor, lens_mat=builder.palette.emissive, cavity_mat=builder.palette.gunmetal
            )
        )
        builder.add_troop_compartment(size=(1.42, 1.65, 0.74), location=(0.0, -0.95, 0.88))
        builder.add_turret(size=(1.05, 1.15, 0.35), center_y=0.20)
        builder.add_spring_antenna()

    elif args.archetype == "super_heavy":
        builder.add_sloped_hull(length=3.8, width=2.8, height=1.05, bevel=0.055)
        builder.objects.append(rmk.make_box(
            f"{args.name}Armor_Superstructure",
            (2.1, 2.7, 0.34), (0.0, -0.10, 1.25), builder.palette.primary_armor, 0.04
        ))
        builder.add_quad_track_pods(pod_length=1.65, pod_height=0.62, track_spacing_x=2.55, pod_spacing_y=2.0)
        builder.add_engine_deck(slat_count=5, with_exhausts=True)
        builder.add_turret(size=(1.85, 2.1, 0.58), center_y=0.15)
        builder.add_sensor_visor()
        builder.add_radar_dish()
        builder.add_armor_applique((-0.95, 1.95, 0.95), (0.95, 1.95, 0.95), count=6)
        builder.add_spring_antenna()

    elif args.archetype == "buggy":
        builder.add_all_terrain_wheels(wheelbase_y=2.2, track_width_x=1.85, wheel_radius=0.42, wheel_width=0.30)
        builder.objects.extend([
            rmk.make_box(f"{args.name}Armor_SpineChassis", (0.85, 2.4, 0.28), (0.0, -0.05, 0.58), builder.palette.primary_armor, 0.035),
            rmk.make_box(f"{args.name}Armor_FrontNose", (0.72, 0.85, 0.26), (0.0, 1.05, 0.65), builder.palette.light_armor, 0.03,
                         rotation=(math.radians(-14), 0.0, 0.0)),
            rmk.make_box(f"{args.name}Armor_EngineBlock", (0.78, 0.88, 0.46), (0.0, -0.88, 0.78), builder.palette.gunmetal, 0.03),
            rmk.make_cylinder(f"{args.name}Armor_Exhaust_L", 0.055, 0.42, (-0.28, -1.25, 0.95), builder.palette.gunmetal,
                              rotation=(math.radians(-24), 0.0, 0.0), vertices=10, bevel=0.008),
            rmk.make_cylinder(f"{args.name}Armor_Exhaust_R", 0.055, 0.42, (0.28, -1.25, 0.95), builder.palette.gunmetal,
                              rotation=(math.radians(-24), 0.0, 0.0), vertices=10, bevel=0.008),
        ])
        builder.add_rollcage(size=(1.15, 1.5, 0.82), location=(0.0, 0.02, 1.02))
        builder.add_sensor_visor()
        builder.turret_center = Vector((0.0, 0.05, 1.45))

    else:  # standard tank
        builder.add_sloped_hull(length=3.0, width=2.4, height=0.88)
        builder.add_continuous_tracks(length=2.85, height=0.62, belt_width=0.34, roadwheels=5, with_skirts=args.skirts)
        builder.add_engine_deck(slat_count=4, with_exhausts=True)
        builder.add_turret(size=(1.4, 1.6, 0.45), center_y=0.08)
        builder.add_sensor_visor()
        builder.add_spring_antenna()

    # 2. Weapons
    if args.archetype not in ("arc_tank",):
        if args.weapon == "howitzer":
            builder.add_howitzer(caliber=0.135, length=1.15, elevation_deg=26.0)
        elif args.weapon == "dual_cannon":
            builder.add_dual_cannon(caliber=0.082, length=1.65, spacing=0.38, elevation_deg=11.0)
        elif args.weapon == "gatling":
            if args.archetype in ("walker", "bike"):
                builder.add_rotary_gatling(side=-1, pivot_offset=(0.75, 0.35, 1.25))
                builder.add_rotary_gatling(side=1, pivot_offset=(0.75, 0.35, 1.25))
            elif args.archetype == "vtol":
                builder.add_rotary_gatling(side=0, pivot_offset=(0.0, 1.25, 0.42))
            elif args.archetype in ("buggy", "halftrack"):
                builder.add_rotary_gatling(side=0, pivot_offset=(0.0, 0.25, 1.45))
            else:
                builder.add_rotary_gatling(side=1)
        elif args.weapon == "missile":
            if args.archetype == "vtol":
                for side in (-1, 1):
                    side_tag = "L" if side < 0 else "R"
                    pod = rmk.make_missile_launcher_pod(
                        f"{args.name}MissilePod_{side_tag}",
                        location=(side * 0.95, 0.35, 0.58),
                        size=(0.42, 0.75, 0.35),
                        rows=2, cols=2, elevation_deg=8.0,
                        armor_mat=builder.palette.primary_armor, cavity_mat=builder.palette.gunmetal,
                        warhead_mat=builder.palette.warning, accent_mat=builder.palette.warning
                    )
                    builder.objects.extend(pod)
                builder.gun_pivot = Vector((0.0, 1.10, 0.58))
                builder.gun_dir = Vector((0.0, 1.0, 0.0))
            else:
                builder.add_missile_pod(rows=2, cols=3, elevation_deg=16.0)
        elif args.weapon == "arc":
            builder.add_arc_emitter(core_radius=0.20, prongs=3)
        else:  # cannon
            builder.add_cannon(caliber=0.095, length=1.70, elevation_deg=11.0)

    # 3. Optional Subsystems & Greebles
    if args.rollcage and args.archetype not in ("buggy",):
        builder.add_rollcage()

    if args.quad_tracks and args.archetype not in ("super_heavy",):
        builder.add_quad_track_pods()

    if args.tilt_rotors and args.archetype not in ("vtol",):
        builder.add_tiltrotors()

    if args.dozer and args.archetype in ("tank", "artillery", "halftrack", "super_heavy"):
        builder.add_dozer_blade(width=builder.hull_width * 1.05, height=0.44)

    if args.smoke and args.archetype in ("tank", "artillery", "hover", "spider", "halftrack", "super_heavy"):
        builder.add_smoke_launchers(tube_count=3)

    if args.radar:
        builder.add_radar_dish()

    if args.sponsons and args.archetype in ("tank", "artillery", "super_heavy"):
        builder.add_side_sponsons()

    # 4. Build, Validate, and Export
    res = builder.build_and_export(
        fbx_path=fbx_path,
        blend_path=blend_path,
        preview_path=preview_path,
        card_path=card_path,
        render_multiview=args.multiview,
        ortho_scale=6.8 if args.archetype == "super_heavy" else (6.2 if args.archetype in ("artillery", "spider", "vtol") else 5.6),
        smooth_by_angle=True,
        smooth_angle_deg=args.smooth_angle,
        bake_vertex_dirt=args.dirt,
        include_locators=args.locators,
        generate_lods=args.lods,
        generate_colliders=args.colliders,
        wear_level=args.wear,
        gameplay_meta_path=gameplay_meta_path,
        wreckage_fbx_path=wreckage_fbx_path,
        target_budget=args.target_budget,
    )

    if args.lod:
        print("\n--- Low-Poly Geometry Budget & LOD Diagnostics ---")
        print(f"LOD0 (Current): {res['total_polygons']} tris | {res['total_vertices']} verts (RTS Combat Camera)")
        print(f"  Target Budget: 1,500 - 5,000 tris (Current: {round(res['total_polygons'] / 4000.0 * 100, 1)}% of budget)")
        print("LOD Recommendations:")
        print(f"  LOD1 (~1,200 tris): Merge track cleats & road wheel rims into single cylinder silhouettes.")
        print(f"  LOD2 (~500 tris): Collapse greebles, antennas, and multi-barrel clusters into convex hulls.")
        print("---------------------------------------------------\n")

    print("\n========================================================")
    print(f"Generated Rogue Command Vehicle: {args.name}")
    print(f"  Archetype: {args.archetype} | Faction: {args.faction} | Camo: {args.camo} | Weapon: {args.weapon}")
    print(f"  Polygons: {res['total_polygons']} | Vertices: {res['total_vertices']}")
    print(f"  Batched Unity Meshes: {res['exported_batched_objects']}")
    print(f"  Ground Contact Valid: {res['ground_contact_valid']}")
    print(f"  Source Blend: {blend_path}")
    print(f"  Unity FBX: {fbx_path}")
    print(f"  Preview Image: {preview_path}")
    print("========================================================\n")


if __name__ == "__main__":
    main()
