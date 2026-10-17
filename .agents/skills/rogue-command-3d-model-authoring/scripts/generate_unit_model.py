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
import json
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
    "sky_sweeper": {
        "archetype": "flak_anti_air",
        "weapon": "quad_flak",
        "faction": "vanguard",
        "radar": True,
        "outriggers": True,
    },
    "dreadnought_crawler": {
        "archetype": "siege_crawler",
        "weapon": "railgun",
        "faction": "siege",
        "outriggers": True,
        "sponsons": True,
    },
    "hive_carrier": {
        "archetype": "drone_carrier",
        "weapon": "missile",
        "faction": "syndicate",
        "radar": True,
        "flight_deck": True,
    },
    "titan_quad": {
        "archetype": "quad_mech",
        "weapon": "dual_cannon",
        "faction": "arctic",
        "smoke": True,
    },
    "resource_behemoth": {
        "archetype": "combat_harvester",
        "weapon": "gatling",
        "faction": "siege",
        "dozer": True,
        "sponsons": True,
        "excavator_drum": True,
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
        choices=[
            "tank", "walker", "bike", "artillery", "arc_tank", "hover", "spider",
            "vtol", "halftrack", "super_heavy", "buggy",
            "flak_anti_air", "siege_crawler", "drone_carrier", "quad_mech", "combat_harvester"
        ],
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
        choices=["cannon", "gatling", "missile", "arc", "howitzer", "dual_cannon", "railgun", "quad_flak", "plasma_mortar"],
        help="Primary weapon subsystem",
    )
    parser.add_argument(
        "--preset",
        default="none",
        choices=[
            "none", "siege_breaker", "recon_interceptor", "gunship_alpha", "apc_enforcer", "dune_raider",
            "sky_sweeper", "dreadnought_crawler", "hive_carrier", "titan_quad", "resource_behemoth"
        ],
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
    parser.add_argument("--outriggers", action="store_true", help="Include deployable hydraulic stabilizer outriggers")
    parser.add_argument("--flight-deck", action="store_true", help="Include flight / drone launch deck")
    parser.add_argument("--excavator-drum", action="store_true", help="Include front rotary cutting excavator drum")
    parser.add_argument("--bake-atlas", action="store_true", help="Bake unified PBR texture maps (Albedo, MetallicGloss, Normal, Emission)")
    parser.add_argument("--atlas-resolution", type=int, default=1024, choices=[512, 1024, 2048], help="Resolution for baked texture atlas")
    parser.add_argument("--single-material", action="store_true", help="Export unified 1-material mesh (<Name>_Atlas.fbx) for 1 draw call in Unity")
    parser.add_argument("--rig", action="store_true", help="Generate kinematic Armature bones and bind meshes with rigid vertex groups")
    parser.add_argument("--animate", action="store_true", help="Bake procedural action animation clips (Idle, Drive, FireRecoil, Death) into FBX")
    parser.add_argument("--vfx-flash", action="store_true", help="Attach star-spike muzzle flash mesh to weapon")
    parser.add_argument("--vfx-thruster", action="store_true", help="Attach glowing conical exhaust plumes to engines")
    parser.add_argument("--shield-bubble", action="store_true", help="Attach geodesic energy shield bubble envelope")
    parser.add_argument("--primary-color", default=None, help="Custom hex color for primary armor (e.g. #1f3a52)")
    parser.add_argument("--light-armor-color", default=None, help="Custom hex color for light armor (e.g. #3b5f78)")
    parser.add_argument("--emissive-color", default=None, help="Custom hex color for emissive glow (e.g. #00ffee)")
    parser.add_argument("--locators", action="store_true", default=True, help="Generate Unity locator sockets")
    parser.add_argument("--dirt", action="store_true", default=True, help="Bake procedural vertex ambient dirt/cavity shading")
    parser.add_argument("--wear", default="field_tested", choices=["pristine", "field_tested", "battle_hardened"], help="Procedural surface wear level")
    parser.add_argument("--lods", action="store_true", help="Generate multi-level LOD chain (LOD0, LOD1, LOD2) and export _LODs.fbx")
    parser.add_argument("--colliders", action="store_true", help="Generate low-poly physics collision hulls")
    parser.add_argument("--gameplay-json", action="store_true", help="Export companion .gameplay.json specification")
    parser.add_argument("--wreckage", action="store_true", help="Export burned / dislodged turret wreckage variant")
    parser.add_argument("--damaged-state", action="store_true", help="Export combat-damaged <Name>_Damaged.fbx variant with distorted plates and scorch marks")
    parser.add_argument("--structure", action="store_true", help="Generate a static building or factory instead of a mobile vehicle")
    parser.add_argument("--building-type", default="factory", choices=["factory", "defense_turret", "radar_station", "energy_silo"], help="Structure building type")
    parser.add_argument("--attachments", default=None, help="Comma-separated list of modular attachments to apply (e.g. 'era,aps,sponson,fuel_drums')")
    parser.add_argument("--export-detached-attachments", action="store_true", help="Export separate <Name>_Attachment_<Type>.fbx meshes for modular attachment in Unity")
    parser.add_argument("--render-icons", action="store_true", help="Render dual orthographic 2D tactical icons (Top-Down and Isometric) with transparent alpha")
    parser.add_argument("--icon-size", type=int, default=128, choices=[64, 128], help="Resolution for rendered tactical icons")
    parser.add_argument("--target-budget", type=int, default=4000, help="Target polygon budget for profiling")
    parser.add_argument("--lod", action="store_true", help="Calculate polygon budget and recommend LOD reductions")
    parser.add_argument("--smooth-angle", type=float, default=38.0, help="Smart angle-weighted auto-smooth threshold in degrees")
    parser.add_argument("--multiview", action="store_true", help="Render 4 orthographic views")
    parser.add_argument("--batch-all-archetypes", action="store_true", help="Batch generate all 16 archetypes into the output directory")
    parser.add_argument("--report-json", default=None, help="Path to write detailed structural and budget JSON report")
    parser.add_argument("--dry-run", action="store_true", help="Validate CLI parameters and print archetype plan without building Blender geometry")
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
        for flag in ("sponsons", "smoke", "radar", "rollcage", "dozer", "outriggers", "flight_deck", "excavator_drum"):
            if p.get(flag) and f"--{flag.replace('_', '-')}" not in script_args:
                setattr(args, flag, True)

    return args


def resolve_path(p: str | Path | None, base: Path) -> Path | None:
    if p is None:
        return None
    path_obj = Path(p)
    return path_obj if path_obj.is_absolute() else (base / path_obj).resolve()


ALL_ARCHETYPES = [
    "tank", "walker", "bike", "artillery", "arc_tank", "hover", "spider",
    "vtol", "halftrack", "super_heavy", "buggy",
    "flak_anti_air", "siege_crawler", "drone_carrier", "quad_mech", "combat_harvester"
]

ARCHETYPE_DEFAULT_WEAPONS = {
    "tank": "cannon",
    "walker": "gatling",
    "bike": "gatling",
    "artillery": "howitzer",
    "arc_tank": "arc",
    "hover": "cannon",
    "spider": "missile",
    "vtol": "missile",
    "halftrack": "gatling",
    "super_heavy": "dual_cannon",
    "buggy": "gatling",
    "flak_anti_air": "quad_flak",
    "siege_crawler": "railgun",
    "drone_carrier": "missile",
    "quad_mech": "dual_cannon",
    "combat_harvester": "gatling",
}


def build_vehicle_model(
    name: str,
    archetype: str,
    faction: str,
    camo: str,
    weapon: str,
    wear: str,
    out_root: Path,
    blend_path: Path | None = None,
    fbx_path: Path | None = None,
    preview_path: Path | None = None,
    skirts: bool = True,
    rollcage: bool = False,
    quad_tracks: bool = False,
    tilt_rotors: bool = False,
    dozer: bool = False,
    smoke: bool = False,
    radar: bool = False,
    sponsons: bool = False,
    outriggers: bool = False,
    flight_deck: bool = False,
    excavator_drum: bool = False,
    primary_color: str | None = None,
    light_armor_color: str | None = None,
    emissive_color: str | None = None,
    locators: bool = True,
    dirt: bool = True,
    lods: bool = False,
    colliders: bool = False,
    gameplay_json: bool = False,
    wreckage: bool = False,
    target_budget: int = 4000,
    lod_diagnostics: bool = False,
    smooth_angle: float = 38.0,
    multiview: bool = False,
    bake_atlas: bool = False,
    atlas_resolution: int = 1024,
    single_material: bool = False,
    rig: bool = False,
    animate: bool = False,
    vfx_flash: bool = False,
    vfx_thruster: bool = False,
    shield_bubble: bool = False,
    attachments: str | None = None,
    export_detached_attachments: bool = False,
    damaged_state: bool = False,
    render_icons: bool = False,
    icon_size: int = 128,
) -> dict:
    rmk.clear_scene()

    blend_file = blend_path or (out_root / "content" / "source-art" / f"{name.lower()}.blend")
    fbx_file = fbx_path or (
        out_root
        / "unity"
        / "RogueCommandAssetAuthoring"
        / "Assets"
        / "RogueCommandExpansionSample"
        / "Models"
        / f"{name}.fbx"
    )
    preview_file = preview_path or (out_root / "artifacts" / "previews" / f"{name.lower()}.png")
    card_path = None

    p_override = rmk.hex_to_rgb(primary_color) if primary_color else None
    l_override = rmk.hex_to_rgb(light_armor_color) if light_armor_color else None
    e_override = rmk.hex_to_rgb(emissive_color) if emissive_color else None

    builder = rmk.RogueVehicleBuilder(
        name,
        faction=faction,
        camo=camo,
        primary_override=p_override,
        light_override=l_override,
        emissive_override=e_override,
    )
    builder.set_archetype(archetype)
    builder.set_wear_level(wear)

    gameplay_meta_path = (fbx_file.parent / f"{name}.gameplay.json") if gameplay_json else None
    wreckage_fbx_path = (fbx_file.parent / f"{name}_Wreckage.fbx") if wreckage else None
    damaged_fbx_path = (fbx_file.parent / f"{name}_Damaged.fbx") if damaged_state else None

    # 1. Mobility and Chassis by Archetype
    if archetype == "walker":
        builder.add_bipedal_legs(hip_height=1.45, hip_spacing_x=0.88)
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_Pelvis", (0.92, 0.65, 0.42), (0.0, -0.02, 1.40), builder.palette.gunmetal, 0.08),
            rmk.make_box(f"{name}Armor_Torso", (1.14, 0.74, 0.78), (0.0, 0.02, 1.96), builder.palette.primary_armor, 0.09),
            rmk.make_box(f"{name}Armor_ChestPlate", (1.02, 0.22, 0.45), (0.0, 0.42, 2.04), builder.palette.light_armor, 0.065),
            rmk.make_box(f"{name}Armor_PowerCell", (0.72, 0.40, 0.60), (0.0, -0.52, 2.03), builder.palette.gunmetal, 0.07),
        ])
        builder.add_sensor_visor()

    elif archetype == "bike":
        builder.add_all_terrain_wheels(wheelbase_y=1.95, track_width_x=0.0, wheel_radius=0.38, wheel_width=0.22)
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_SpineFrame", (0.34, 1.85, 0.38), (0.0, -0.05, 0.72), builder.palette.primary_armor, 0.04),
            rmk.make_box(f"{name}Armor_EngineBlock", (0.46, 0.68, 0.42), (0.0, -0.18, 0.82), builder.palette.gunmetal, 0.035),
            rmk.make_box(f"{name}Armor_CockpitCowl", (0.42, 0.58, 0.32), (0.0, 0.45, 1.05), builder.palette.light_armor, 0.03,
                          (math.radians(-16), 0.0, 0.0)),
            rmk.make_rod(f"{name}Joint_Fork_L", Vector((-0.18, 0.45, 1.05)), Vector((-0.16, 0.98, 0.38)), 0.035, builder.palette.gunmetal),
            rmk.make_rod(f"{name}Joint_Fork_R", Vector((0.18, 0.45, 1.05)), Vector((0.16, 0.98, 0.38)), 0.035, builder.palette.gunmetal),
            rmk.make_box(f"{name}Joint_Handlebars", (0.64, 0.06, 0.05), (0.0, 0.38, 1.18), builder.palette.gunmetal, 0.01),
            rmk.make_rod(f"{name}Joint_Swingarm_L", Vector((-0.18, -0.18, 0.65)), Vector((-0.16, -0.98, 0.38)), 0.038, builder.palette.gunmetal),
            rmk.make_rod(f"{name}Joint_Swingarm_R", Vector((0.18, -0.18, 0.65)), Vector((0.16, -0.98, 0.38)), 0.038, builder.palette.gunmetal),
        ])
        builder.add_sensor_visor()
        for side in (-1, 1):
            side_tag = "L" if side < 0 else "R"
            builder.objects.append(rmk.make_box(
                f"{name}Armor_SidePod_{side_tag}",
                (0.18, 0.58, 0.28), (side * 0.36, -0.12, 0.72), builder.palette.primary_armor, 0.02
            ))

    elif archetype == "artillery":
        builder.add_sloped_hull(length=3.4, width=2.6, height=0.96)
        builder.add_continuous_tracks(length=3.2, height=0.68, belt_width=0.36, roadwheels=6, with_skirts=skirts)
        builder.add_engine_deck(slat_count=4, with_exhausts=True)
        builder.add_turret(size=(1.5, 1.7, 0.48), center_y=0.12)
        for side in (-1, 1):
            side_tag = "L" if side < 0 else "R"
            builder.objects.extend([
                rmk.make_box(f"{name}Armor_RecoilSpade_{side_tag}", (0.20, 0.24, 0.26),
                             (side * 0.62, -1.62, 0.56), builder.palette.primary_armor, 0.025),
                rmk.make_box(f"{name}Armor_RecoilBlade_{side_tag}", (0.26, 0.08, 0.32),
                             (side * 0.62, -1.78, 0.36), builder.palette.gunmetal, 0.02,
                             rotation=(math.radians(22), 0.0, 0.0)),
            ])
        builder.add_spring_antenna()

    elif archetype == "arc_tank":
        builder.add_sloped_hull(length=2.8, width=2.3, height=0.86)
        builder.add_continuous_tracks(length=2.7, height=0.58, belt_width=0.32, roadwheels=5, with_skirts=skirts)
        builder.add_engine_deck(slat_count=3, with_exhausts=False)
        builder.add_turret(size=(1.3, 1.4, 0.42), center_y=0.0)
        builder.add_arc_emitter(core_radius=0.22, prongs=3)
        builder.add_sensor_visor()

    elif archetype == "hover":
        builder.add_sloped_hull(length=2.9, width=2.5, height=0.78)
        builder.add_hover_skimmer(float_height=0.48)
        builder.add_turret(size=(1.35, 1.5, 0.42), center_y=0.06)
        builder.add_sensor_visor()
        builder.add_spring_antenna()

    elif archetype == "spider":
        builder.add_sloped_hull(length=2.6, width=2.4, height=0.82)
        builder.add_quad_crawler_legs(knee_height=0.98, reach_x=1.12, reach_y=0.88)
        builder.add_turret(size=(1.45, 1.55, 0.44), center_y=0.04)
        builder.add_sensor_visor()

    elif archetype == "vtol":
        builder.is_hover = True
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_Fuselage", (1.35, 2.60, 0.58), (0.0, 0.10, 0.72), builder.palette.primary_armor, 0.04),
            rmk.make_box(f"{name}Armor_CockpitNose", (1.05, 0.92, 0.42), (0.0, 1.45, 0.68), builder.palette.light_armor, 0.035,
                         rotation=(math.radians(-16), 0.0, 0.0)),
            rmk.make_box(f"{name}Armor_TailBoom", (0.34, 1.65, 0.26), (0.0, -1.85, 0.82), builder.palette.primary_armor, 0.03),
            rmk.make_box(f"{name}Armor_HorizStab", (1.15, 0.38, 0.06), (0.0, -2.50, 0.88), builder.palette.light_armor, 0.015),
            rmk.make_box(f"{name}Armor_VertFin_L", (0.05, 0.42, 0.48), (-0.42, -2.55, 1.05), builder.palette.primary_armor, 0.015,
                         rotation=(0.0, math.radians(-18), 0.0)),
            rmk.make_box(f"{name}Armor_VertFin_R", (0.05, 0.42, 0.48), (0.42, -2.55, 1.05), builder.palette.primary_armor, 0.015,
                         rotation=(0.0, math.radians(18), 0.0)),
            rmk.make_box(f"{name}Armor_WingSponson_L", (0.85, 0.52, 0.14), (-1.05, 0.15, 0.72), builder.palette.light_armor, 0.025),
            rmk.make_box(f"{name}Armor_WingSponson_R", (0.85, 0.52, 0.14), (1.05, 0.15, 0.72), builder.palette.light_armor, 0.025),
            rmk.make_box(f"{name}Joint_LandingSkid_L", (0.08, 1.85, 0.05), (-0.55, 0.05, 0.06), builder.palette.gunmetal, 0.01),
            rmk.make_box(f"{name}Joint_LandingSkid_R", (0.08, 1.85, 0.05), (0.55, 0.05, 0.06), builder.palette.gunmetal, 0.01),
            rmk.make_rod(f"{name}Joint_SkidStrut_FL", (-0.55, 0.75, 0.08), (-0.52, 0.65, 0.45), 0.032, builder.palette.gunmetal),
            rmk.make_rod(f"{name}Joint_SkidStrut_FR", (0.55, 0.75, 0.08), (0.52, 0.65, 0.45), 0.032, builder.palette.gunmetal),
            rmk.make_rod(f"{name}Joint_SkidStrut_RL", (-0.55, -0.65, 0.08), (-0.52, -0.55, 0.45), 0.032, builder.palette.gunmetal),
            rmk.make_rod(f"{name}Joint_SkidStrut_RR", (0.55, -0.65, 0.08), (0.52, -0.55, 0.45), 0.032, builder.palette.gunmetal),
        ])
        builder.add_sensor_visor()
        builder.add_tiltrotors(location_offset=(1.45, 0.15, 0.75), radius=0.44, tilt_deg=14.0)
        builder.turret_center = Vector((0.0, 1.05, 0.45))

    elif archetype == "halftrack":
        wheel_z = builder.ground_clearance + 0.38 + 0.035
        for side in (-1, 1):
            tag = "L" if side < 0 else "R"
            builder.objects.extend(rmk.make_all_terrain_wheel_assembly(
                f"{name}Wheel_Front_{tag}",
                location=(side * 0.88, 1.25, wheel_z),
                radius=0.38, width=0.26,
                tire_mat=builder.palette.tread, rim_mat=builder.palette.wheel_rim,
                hub_mat=builder.palette.gunmetal, accent_mat=builder.palette.warning
            ))
        builder.objects.append(rmk.make_box(
            f"{name}Joint_FrontAxle", (1.65, 0.14, 0.12), (0.0, 1.25, wheel_z), builder.palette.gunmetal, 0.02
        ))
        for side in (-1, 1):
            builder.objects.extend(rmk.make_continuous_track_assembly(
                f"{name}Treads",
                x_pos=side * 0.92,
                length=1.95, height=0.58, belt_width=0.30,
                roadwheel_count=4,
                tread_mat=builder.palette.tread,
                rim_mat=builder.palette.wheel_rim,
                hub_mat=builder.palette.wheel_hub,
                axle_mat=builder.palette.gunmetal,
                fender_mat=builder.palette.light_armor,
                skirt_mat=builder.palette.primary_armor if skirts else None,
                accent_mat=builder.palette.warning,
                ground_z=builder.ground_clearance,
                center_y=-0.55
            ))
        builder.objects.append(
            rmk.make_sloped_glacis_hull(
                f"{name}Armor_Hood",
                length_front=0.85, length_rear=0.65,
                width_front=1.20, width_rear=1.35,
                height_lower=builder.ground_clearance + 0.35, height_upper=0.92,
                material=builder.palette.primary_armor, bevel=0.035
            )
        )
        builder.objects.append(
            rmk.make_box(f"{name}Armor_Cab", (1.38, 0.88, 0.62), (0.0, 0.20, 1.05), builder.palette.primary_armor, 0.035)
        )
        builder.objects.extend(
            rmk.make_recessed_sensor_visor(
                f"{name}Sensor_CabVisor",
                location=(0.0, 0.62, 1.18),
                size=(0.58, 0.12, 0.15),
                armor_mat=builder.palette.light_armor, lens_mat=builder.palette.emissive, cavity_mat=builder.palette.gunmetal
            )
        )
        builder.add_troop_compartment(size=(1.42, 1.65, 0.74), location=(0.0, -0.95, 0.88))
        builder.add_turret(size=(1.05, 1.15, 0.35), center_y=0.20)
        builder.add_spring_antenna()

    elif archetype == "super_heavy":
        builder.add_sloped_hull(length=3.8, width=2.8, height=1.05, bevel=0.055)
        builder.objects.append(rmk.make_box(
            f"{name}Armor_Superstructure",
            (2.1, 2.7, 0.34), (0.0, -0.10, 1.25), builder.palette.primary_armor, 0.04
        ))
        builder.add_quad_track_pods(pod_length=1.65, pod_height=0.62, track_spacing_x=2.55, pod_spacing_y=2.0)
        builder.add_engine_deck(slat_count=5, with_exhausts=True)
        builder.add_turret(size=(1.85, 2.1, 0.58), center_y=0.15)
        builder.add_sensor_visor()
        builder.add_radar_dish()
        builder.add_armor_applique((-0.95, 1.95, 0.95), (0.95, 1.95, 0.95), count=6)
        builder.add_spring_antenna()

    elif archetype == "buggy":
        builder.add_all_terrain_wheels(wheelbase_y=2.2, track_width_x=1.85, wheel_radius=0.42, wheel_width=0.30)
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_SpineChassis", (0.85, 2.4, 0.28), (0.0, -0.05, 0.58), builder.palette.primary_armor, 0.035),
            rmk.make_box(f"{name}Armor_FrontNose", (0.72, 0.85, 0.26), (0.0, 1.05, 0.65), builder.palette.light_armor, 0.03,
                         rotation=(math.radians(-14), 0.0, 0.0)),
            rmk.make_box(f"{name}Armor_EngineBlock", (0.78, 0.88, 0.46), (0.0, -0.88, 0.78), builder.palette.gunmetal, 0.03),
            rmk.make_cylinder(f"{name}Armor_Exhaust_L", 0.055, 0.42, (-0.28, -1.25, 0.95), builder.palette.gunmetal,
                              rotation=(math.radians(-24), 0.0, 0.0), vertices=10, bevel=0.008),
            rmk.make_cylinder(f"{name}Armor_Exhaust_R", 0.055, 0.42, (0.28, -1.25, 0.95), builder.palette.gunmetal,
                              rotation=(math.radians(-24), 0.0, 0.0), vertices=10, bevel=0.008),
        ])
        builder.add_rollcage(size=(1.15, 1.5, 0.82), location=(0.0, 0.02, 1.02))
        builder.add_sensor_visor()
        builder.turret_center = Vector((0.0, 0.05, 1.45))

    elif archetype == "flak_anti_air":
        builder.add_sloped_hull(length=3.2, width=2.4, height=0.90)
        builder.add_continuous_tracks(length=3.0, height=0.64, belt_width=0.34, roadwheels=5, with_skirts=skirts)
        builder.add_engine_deck(slat_count=4, with_exhausts=True)
        builder.add_turret(size=(1.5, 1.65, 0.52), center_y=0.04)
        builder.add_sensor_visor()
        builder.add_radar_dish(is_rotodome=False)
        builder.add_outriggers(count=2, reach_x=0.55)
        builder.add_spring_antenna()

    elif archetype == "siege_crawler":
        builder.add_sloped_hull(length=4.2, width=3.0, height=1.15, bevel=0.055)
        builder.objects.append(rmk.make_box(
            f"{name}Armor_Citadel",
            (2.3, 2.9, 0.42), (0.0, -0.05, 1.35), builder.palette.primary_armor, 0.045
        ))
        builder.add_quad_track_pods(pod_length=1.85, pod_height=0.68, track_spacing_x=2.75, pod_spacing_y=2.3)
        builder.add_engine_deck(slat_count=6, with_exhausts=True)
        builder.add_turret(size=(2.0, 2.3, 0.62), center_y=0.18)
        builder.add_sensor_visor()
        builder.add_outriggers(count=4, reach_x=0.85)
        builder.add_side_sponsons()
        builder.add_spring_antenna()

    elif archetype == "drone_carrier":
        builder.add_sloped_hull(length=4.4, width=3.2, height=0.92, bevel=0.045)
        builder.add_continuous_tracks(length=4.1, height=0.68, belt_width=0.40, roadwheels=7, with_skirts=skirts)
        builder.add_engine_deck(slat_count=4, with_exhausts=True)
        builder.add_flight_deck(size=(2.10, 3.20, 0.22))
        builder.objects.extend([
            rmk.make_box(f"{name}Armor_BridgeTower", (0.55, 1.25, 0.68), (1.18, -0.15, 1.48), builder.palette.primary_armor, 0.035),
            rmk.make_box(f"{name}Armor_BridgeRoof", (0.62, 1.32, 0.12), (1.18, -0.15, 1.84), builder.palette.light_armor, 0.02),
        ])
        builder.objects.extend(rmk.make_recessed_sensor_visor(
            f"{name}Sensor_BridgeVisor",
            location=(1.18, 0.48, 1.62),
            size=(0.42, 0.10, 0.14),
            armor_mat=builder.palette.light_armor, lens_mat=builder.palette.emissive, cavity_mat=builder.palette.gunmetal
        ))
        builder.add_radar_dish(is_rotodome=True, location=(1.18, -0.42, 2.05))
        builder.turret_center = Vector((-0.88, 1.15, 1.15))

    elif archetype == "quad_mech":
        builder.add_sloped_hull(length=2.8, width=2.5, height=0.88, bevel=0.05)
        builder.objects.append(rmk.make_box(
            f"{name}Armor_MechPelvisCore",
            (1.4, 1.8, 0.48), (0.0, 0.0, 1.05), builder.palette.primary_armor, 0.04
        ))
        builder.add_quad_crawler_legs(knee_height=1.25, reach_x=1.35, reach_y=1.10)
        builder.add_turret(size=(1.55, 1.7, 0.48), center_y=0.10)
        builder.add_sensor_visor()
        builder.add_spring_antenna()

    elif archetype == "combat_harvester":
        builder.add_sloped_hull(length=3.6, width=2.7, height=1.05, bevel=0.045)
        builder.add_continuous_tracks(length=3.3, height=0.68, belt_width=0.38, roadwheels=6, with_skirts=skirts)
        builder.add_engine_deck(slat_count=5, with_exhausts=True)
        builder.add_excavator_drum(width=2.5, radius=0.34)
        for side in (-1, 1):
            side_tag = "L" if side < 0 else "R"
            builder.objects.append(rmk.make_box(
                f"{name}Armor_OreHopper_{side_tag}",
                (0.42, 1.65, 0.65), (side * 1.32, -0.42, 1.05), builder.palette.primary_armor, 0.035
            ))
        builder.add_turret(size=(1.15, 1.25, 0.38), center_y=0.25)
        builder.add_sensor_visor()
        builder.add_spring_antenna()

    else:  # standard tank
        builder.add_sloped_hull(length=3.0, width=2.4, height=0.88)
        builder.add_continuous_tracks(length=2.85, height=0.62, belt_width=0.34, roadwheels=5, with_skirts=skirts)
        builder.add_engine_deck(slat_count=4, with_exhausts=True)
        builder.add_turret(size=(1.4, 1.6, 0.45), center_y=0.08)
        builder.add_sensor_visor()
        builder.add_spring_antenna()

    # 2. Weapons
    if archetype not in ("arc_tank",):
        if weapon == "railgun":
            builder.add_railgun(rail_length=2.40, rail_height=0.28, rail_width=0.40, elevation_deg=8.0)
        elif weapon == "quad_flak":
            builder.add_quad_flak(barrel_length=1.65, caliber_radius=0.038, elevation_deg=34.0)
        elif weapon == "plasma_mortar":
            builder.add_plasma_mortar(caliber=0.175, length=0.95, elevation_deg=42.0)
        elif weapon == "howitzer":
            builder.add_howitzer(caliber=0.135, length=1.15, elevation_deg=26.0)
        elif weapon == "dual_cannon":
            builder.add_dual_cannon(caliber=0.082, length=1.65, spacing=0.38, elevation_deg=11.0)
        elif weapon == "gatling":
            if archetype in ("walker", "bike"):
                builder.add_rotary_gatling(side=-1, pivot_offset=(0.75, 0.35, 1.25))
                builder.add_rotary_gatling(side=1, pivot_offset=(0.75, 0.35, 1.25))
            elif archetype == "vtol":
                builder.add_rotary_gatling(side=0, pivot_offset=(0.0, 1.25, 0.42))
            elif archetype in ("buggy", "halftrack"):
                builder.add_rotary_gatling(side=0, pivot_offset=(0.0, 0.25, 1.45))
            else:
                builder.add_rotary_gatling(side=1)
        elif weapon == "missile":
            if archetype == "vtol":
                for side in (-1, 1):
                    side_tag = "L" if side < 0 else "R"
                    pod = rmk.make_missile_launcher_pod(
                        f"{name}MissilePod_{side_tag}",
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
        elif weapon == "arc":
            builder.add_arc_emitter(core_radius=0.20, prongs=3)
        else:  # cannon
            builder.add_cannon(caliber=0.095, length=1.70, elevation_deg=11.0)

    # 3. Optional Subsystems & Greebles
    if rollcage and archetype not in ("buggy",):
        builder.add_rollcage()

    if quad_tracks and archetype not in ("super_heavy", "siege_crawler"):
        builder.add_quad_track_pods()

    if tilt_rotors and archetype not in ("vtol",):
        builder.add_tiltrotors()

    if dozer and archetype in ("tank", "artillery", "halftrack", "super_heavy", "combat_harvester"):
        builder.add_dozer_blade(width=builder.hull_width * 1.05, height=0.44)

    if smoke and archetype in ("tank", "artillery", "hover", "spider", "halftrack", "super_heavy", "quad_mech", "flak_anti_air"):
        builder.add_smoke_launchers(tube_count=3)

    if radar:
        builder.add_radar_dish()

    if sponsons and archetype in ("tank", "artillery", "super_heavy", "siege_crawler", "combat_harvester"):
        builder.add_side_sponsons()

    if outriggers and archetype not in ("flak_anti_air", "siege_crawler"):
        builder.add_outriggers(count=4 if archetype in ("super_heavy", "siege_crawler") else 2)

    if flight_deck and archetype not in ("drone_carrier",):
        builder.add_flight_deck()

    if excavator_drum and archetype not in ("combat_harvester",):
        builder.add_excavator_drum()

    # 3. Modular Attachments
    if attachments:
        att_list = [a.strip() for a in attachments.split(",") if a.strip()]
        builder.add_attachments(att_list)

    # 4. Build, Validate, and Export
    res = builder.build_and_export(
        fbx_path=fbx_file,
        blend_path=blend_file,
        preview_path=preview_file,
        card_path=card_path,
        render_multiview=multiview,
        ortho_scale=(
            7.2 if archetype in ("super_heavy", "siege_crawler", "drone_carrier")
            else (6.2 if archetype in ("artillery", "spider", "vtol", "flak_anti_air", "quad_mech", "combat_harvester") else 5.6)
        ),
        smooth_by_angle=True,
        smooth_angle_deg=smooth_angle,
        bake_vertex_dirt=dirt,
        include_locators=locators,
        generate_lods=lods,
        generate_colliders=colliders,
        wear_level=wear,
        gameplay_meta_path=gameplay_meta_path,
        wreckage_fbx_path=wreckage_fbx_path,
        damaged_fbx_path=damaged_fbx_path,
        target_budget=target_budget,
        include_vfx_flash=vfx_flash,
        include_vfx_thruster=vfx_thruster,
        include_shield_bubble=shield_bubble,
        include_rig=rig,
        include_animations=animate,
        bake_atlas=bake_atlas,
        atlas_resolution=atlas_resolution,
        single_material_atlas=single_material,
        export_detached_attachments=export_detached_attachments,
        render_icons=render_icons,
        icon_resolutions=(icon_size,),
    )

    if lod_diagnostics:
        print("\n--- Low-Poly Geometry Budget & LOD Diagnostics ---")
        print(f"LOD0 (Current): {res['total_polygons']} tris | {res['total_vertices']} verts (RTS Combat Camera)")
        print(f"  Target Budget: 1,500 - 5,000 tris (Current: {round(res['total_polygons'] / max(1, target_budget) * 100, 1)}% of budget)")
        print("LOD Recommendations:")
        print(f"  LOD1 (~1,200 tris): Merge track cleats & road wheel rims into single cylinder silhouettes.")
        print(f"  LOD2 (~500 tris): Collapse greebles, antennas, and multi-barrel clusters into convex hulls.")
        print("---------------------------------------------------\n")

    print("\n========================================================")
    print(f"Generated Rogue Command Vehicle: {name}")
    print(f"  Archetype: {archetype} | Faction: {faction} | Camo: {camo} | Weapon: {weapon}")
    print(f"  Polygons: {res['total_polygons']} | Vertices: {res['total_vertices']}")
    print(f"  Batched Unity Meshes: {res['exported_batched_objects']}")
    print(f"  Ground Contact Valid: {res['ground_contact_valid']}")
    print(f"  Source Blend: {blend_file}")
    print(f"  Unity FBX: {fbx_file}")
    print(f"  Preview Image: {preview_file}")
    print("========================================================\n")

    return res


def build_structure_model(
    name: str,
    building_type: str = "factory",
    faction: str = "vanguard",
    camo: str = "none",
    out_root: Path = REPO_ROOT,
    blend_path: Path | None = None,
    fbx_path: Path | None = None,
    preview_path: Path | None = None,
    primary_color: str | None = None,
    light_armor_color: str | None = None,
    emissive_color: str | None = None,
    lods: bool = False,
    colliders: bool = False,
    gameplay_json: bool = False,
    render_icons: bool = False,
    icon_size: int = 128,
) -> dict:
    rmk.clear_scene()

    blend_file = blend_path or (out_root / "content" / "source-art" / f"{name.lower()}.blend")
    fbx_file = fbx_path or (
        out_root
        / "unity"
        / "RogueCommandAssetAuthoring"
        / "Assets"
        / "RogueCommandExpansionSample"
        / "Models"
        / f"{name}.fbx"
    )
    preview_file = preview_path or (out_root / "artifacts" / "previews" / f"{name.lower()}.png")

    p_override = rmk.hex_to_rgb(primary_color) if primary_color else None
    l_override = rmk.hex_to_rgb(light_armor_color) if light_armor_color else None
    e_override = rmk.hex_to_rgb(emissive_color) if emissive_color else None

    builder = rmk.RogueStructureBuilder(
        name,
        building_type=building_type,
        faction=faction,
        camo=camo,
        primary_override=p_override,
        light_override=l_override,
        emissive_override=e_override,
    )

    bt = building_type.lower()
    if bt == "factory":
        builder.build_factory()
    elif bt in ("defense_turret", "turret"):
        builder.build_defense_turret()
    elif bt in ("radar_station", "radar"):
        builder.build_radar_station()
    elif bt in ("energy_silo", "silo", "reactor"):
        builder.build_energy_silo()
    else:
        builder.build_factory()

    gameplay_meta_path = (fbx_file.parent / f"{name}.gameplay.json") if gameplay_json else None

    res = builder.build_and_export(
        fbx_path=fbx_file,
        blend_path=blend_file,
        preview_path=preview_file,
        generate_lods=lods,
        generate_colliders=colliders,
        gameplay_meta_path=gameplay_meta_path,
        render_icons=render_icons,
        icon_resolutions=(icon_size,),
    )

    print("\n========================================================")
    print(f"Generated Rogue Command Structure: {name}")
    print(f"  Building Type: {building_type} | Faction: {faction} | Camo: {camo}")
    print(f"  Polygons: {res['total_polygons']} | Vertices: {res['total_vertices']}")
    print(f"  Batched Unity Meshes: {res['exported_batched_objects']}")
    print(f"  Ground Contact Valid: {res['ground_contact_valid']}")
    print(f"  Source Blend: {blend_file}")
    print(f"  Unity FBX: {fbx_file}")
    print(f"  Preview Image: {preview_file}")
    print("========================================================\n")

    return res


def main():
    args = parse_args()
    out_root = resolve_path(args.out_dir, REPO_ROOT) or REPO_ROOT

    if args.dry_run:
        if args.structure:
            print("\n=== DRY RUN: Rogue Command Structure Planning ===")
            print(f"Structure Name: {args.name}")
            print(f"Building Type: {args.building_type}")
            print(f"Faction: {args.faction} | Camo: {args.camo}")
            print("Outputs Planned:")
            print(f"  FBX: {args.fbx_path or (out_root / 'unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models' / f'{args.name}.fbx')}")
            print(f"  Blend: {args.blend_path or (out_root / 'content/source-art' / f'{args.name.lower()}.blend')}")
            print(f"  Preview: {args.preview_path or (out_root / 'artifacts/previews' / f'{args.name.lower()}.png')}")
            if args.gameplay_json:
                print(f"  Gameplay JSON: {args.name}.gameplay.json (visualRootPath: 'Building')")
            if args.lods:
                print(f"  LOD FBX: {args.name}_LODs.fbx")
            if args.colliders:
                print(f"  Colliders: {args.name}_COLLIDER_Base, {args.name}_COLLIDER_Roof")
            if args.render_icons:
                print(f"  Tactical Icons: {args.icon_size}x{args.icon_size} Top-Down and Isometric")
            print("=== Dry run complete. No meshes created. ===\n")
            return

        print("\n=== DRY RUN: Rogue Command Vehicle Planning ===")
        print(f"Unit Name: {args.name}")
        print(f"Archetype: {args.archetype}")
        print(f"Faction: {args.faction} | Camo: {args.camo} | Wear: {args.wear}")
        print(f"Primary Weapon: {args.weapon}")
        print(f"Subsystems: Skirts={args.skirts}, Rollcage={args.rollcage}, QuadTracks={args.quad_tracks}, TiltRotors={args.tilt_rotors}, Dozer={args.dozer}, Smoke={args.smoke}, Radar={args.radar}, Sponsons={args.sponsons}, Outriggers={args.outriggers}, FlightDeck={args.flight_deck}, ExcavatorDrum={args.excavator_drum}")
        if args.attachments:
            print(f"Modular Attachments: {args.attachments} (Detached FBX: {args.export_detached_attachments})")
        if args.primary_color:
            print(f"Custom Primary Color: {args.primary_color}")
        if args.light_armor_color:
            print(f"Custom Light Armor Color: {args.light_armor_color}")
        if args.emissive_color:
            print(f"Custom Emissive Color: {args.emissive_color}")
        print("Outputs Planned:")
        print(f"  FBX: {args.fbx_path or (out_root / 'unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models' / f'{args.name}.fbx')}")
        print(f"  Blend: {args.blend_path or (out_root / 'content/source-art' / f'{args.name.lower()}.blend')}")
        print(f"  Preview: {args.preview_path or (out_root / 'artifacts/previews' / f'{args.name.lower()}.png')}")
        if args.gameplay_json:
            print(f"  Gameplay JSON: {args.name}.gameplay.json")
        if args.lods:
            print(f"  LOD FBX: {args.name}_LODs.fbx")
        if args.colliders:
            print(f"  Colliders: {args.name}_COLLIDER_Hull, {args.name}_COLLIDER_Turret")
        if args.wreckage:
            print(f"  Wreckage FBX: {args.name}_Wreckage.fbx")
        if args.damaged_state:
            print(f"  Damaged State FBX: {args.name}_Damaged.fbx")
        if args.bake_atlas:
            print(f"  Texture Atlas: {args.atlas_resolution}x{args.atlas_resolution} PBR (Albedo, MetallicGloss, Normal, Emission)")
        if args.single_material:
            print(f"  Unified Atlas FBX: {args.name}_Atlas.fbx (1 draw call)")
        if args.rig:
            print(f"  Armature Rig: {args.name}_Armature (Bone_Hull, Bone_TurretYaw, Bone_TurretPitch, Bone_BarrelRecoil)")
        if args.animate:
            print(f"  Baked Action Clips: Action_Idle, Action_Drive, Action_FireRecoil, Action_Death")
        if args.vfx_flash:
            print(f"  VFX: Muzzle flash star-spike")
        if args.vfx_thruster:
            print(f"  VFX: Exhaust thruster plume cones")
        if args.shield_bubble:
            print(f"  VFX: Geodesic shield bubble envelope")
        if args.render_icons:
            print(f"  Tactical Icons: {args.icon_size}x{args.icon_size} Top-Down and Isometric")
        print("=== Dry run complete. No meshes created. ===\n")
        return

    if args.batch_all_archetypes:
        print("\n========================================================")
        print(" BATCH GENERATING ALL 16 ROGUE COMMAND ARCHETYPES")
        print("========================================================\n")
        results = []
        for arch in ALL_ARCHETYPES:
            arch_title = arch.title().replace("_", "")
            unit_name = f"{args.name}_{arch_title}"
            def_weapon = ARCHETYPE_DEFAULT_WEAPONS.get(arch, "cannon") if args.weapon == "cannon" else args.weapon
            print(f"\n[Batch {len(results)+1}/{len(ALL_ARCHETYPES)}] Building archetype: {arch} -> {unit_name} (Weapon: {def_weapon})")
            res = build_vehicle_model(
                name=unit_name,
                archetype=arch,
                faction=args.faction,
                camo=args.camo,
                weapon=def_weapon,
                wear=args.wear,
                out_root=out_root,
                skirts=args.skirts,
                rollcage=args.rollcage,
                quad_tracks=args.quad_tracks,
                tilt_rotors=args.tilt_rotors,
                dozer=args.dozer,
                smoke=args.smoke,
                radar=args.radar,
                sponsons=args.sponsons,
                outriggers=args.outriggers,
                flight_deck=args.flight_deck,
                excavator_drum=args.excavator_drum,
                primary_color=args.primary_color,
                light_armor_color=args.light_armor_color,
                emissive_color=args.emissive_color,
                locators=args.locators,
                dirt=args.dirt,
                lods=args.lods,
                colliders=args.colliders,
                gameplay_json=args.gameplay_json,
                wreckage=args.wreckage,
                target_budget=args.target_budget,
                lod_diagnostics=args.lod,
                smooth_angle=args.smooth_angle,
                multiview=args.multiview,
            )
            results.append({
                "archetype": arch,
                "name": unit_name,
                "triangles": res["total_polygons"],
                "draw_calls": res["exported_batched_objects"],
                "valid": res["ground_contact_valid"],
            })

        print("\n========================================================")
        print(" BATCH GENERATION COMPLETE - 16 ARCHETYPES REPORT")
        print("========================================================")
        print(f"{'Archetype':<18} | {'Unit Name':<28} | {'Tris':<7} | {'DrawCalls':<9} | {'GroundValid'}")
        print("-" * 75)
        for r in results:
            print(f"{r['archetype']:<18} | {r['name']:<28} | {r['triangles']:<7} | {r['draw_calls']:<9} | {r['valid']}")
        print("========================================================\n")
        return

    # Single unit/structure generation
    blend_path = resolve_path(args.blend_path, REPO_ROOT)
    fbx_path = resolve_path(args.fbx_path, REPO_ROOT)
    preview_path = resolve_path(args.preview_path, REPO_ROOT)

    # Execution: Structure or Vehicle
    if args.structure:
        res = build_structure_model(
            name=args.name,
            building_type=args.building_type,
            faction=args.faction,
            camo=args.camo,
            out_root=out_root,
            blend_path=blend_path,
            fbx_path=fbx_path,
            preview_path=preview_path,
            primary_color=args.primary_color,
            light_armor_color=args.light_armor_color,
            emissive_color=args.emissive_color,
            lods=args.lods,
            colliders=args.colliders,
            gameplay_json=args.gameplay_json,
            render_icons=args.render_icons,
            icon_size=args.icon_size,
        )
    else:
        res = build_vehicle_model(
            name=args.name,
            archetype=args.archetype,
            faction=args.faction,
            camo=args.camo,
            weapon=args.weapon,
            wear=args.wear,
            out_root=out_root,
            blend_path=blend_path,
            fbx_path=fbx_path,
            preview_path=preview_path,
            skirts=args.skirts,
            rollcage=args.rollcage,
            quad_tracks=args.quad_tracks,
            tilt_rotors=args.tilt_rotors,
            dozer=args.dozer,
            smoke=args.smoke,
            radar=args.radar,
            sponsons=args.sponsons,
            outriggers=args.outriggers,
            flight_deck=args.flight_deck,
            excavator_drum=args.excavator_drum,
            primary_color=args.primary_color,
            light_armor_color=args.light_armor_color,
            emissive_color=args.emissive_color,
            locators=args.locators,
            dirt=args.dirt,
            lods=args.lods,
            colliders=args.colliders,
            gameplay_json=args.gameplay_json,
            wreckage=args.wreckage,
            target_budget=args.target_budget,
            lod_diagnostics=args.lod,
            smooth_angle=args.smooth_angle,
            multiview=args.multiview,
            bake_atlas=args.bake_atlas,
            atlas_resolution=args.atlas_resolution,
            single_material=args.single_material,
            rig=args.rig,
            animate=args.animate,
            vfx_flash=args.vfx_flash,
            vfx_thruster=args.vfx_thruster,
            shield_bubble=args.shield_bubble,
            attachments=args.attachments,
            export_detached_attachments=args.export_detached_attachments,
            damaged_state=args.damaged_state,
            render_icons=args.render_icons,
            icon_size=args.icon_size,
        )

    if args.report_json:
        report_path = resolve_path(args.report_json, REPO_ROOT)
        report_data = {
            "name": args.name,
            "archetype": args.archetype,
            "faction": args.faction,
            "camo": args.camo,
            "weapon": args.weapon,
            "wear_level": args.wear,
            "total_triangles": res["total_polygons"],
            "total_vertices": res["total_vertices"],
            "predicted_draw_calls": res["exported_batched_objects"],
            "ground_contact_valid": res["ground_contact_valid"],
            "subsystem_breakdown": res.get("profile_report", {}).get("subsystems", {}),
            "profile_report": res.get("profile_report", {}),
            "lod_levels": res.get("lod_levels", {}),
            "colliders": res.get("colliders", []),
            "budget_diagnostics": res.get("budget_diagnostics", {}),
        }
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(json.dumps(report_data, indent=2), encoding="utf-8")
        print(f"  Structural Report JSON: {report_path}")


if __name__ == "__main__":
    main()
