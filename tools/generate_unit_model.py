"""generate_unit_model.py: CLI and scriptable vehicle generator for Rogue Command.

Usage with Blender 4.5.14 LTS:
    .\\.tools\\Blender-4.5.14\\...\\blender.exe --background --python tools\\generate_unit_model.py -- [options]

Options:
    --name <UnitName>          Vehicle class name (e.g., HeavySiegeTank)
    --archetype <type>         Archetype: 'tank', 'walker', 'bike', 'artillery', 'arc_tank'
    --faction <faction>        Color palette: 'vanguard', 'siege', 'syndicate', 'arctic'
    --weapon <type>            Primary weapon: 'cannon', 'gatling', 'missile', 'arc'
    --dozer                    Attach frontal heavy dozer ram blade (tanks/artillery)
    --skirts                   Attach armored side skirts over track run
    --multiview                Render 4-perspective orthographic sheet (ISO, TOP, FRONT, SIDE)
    --card                     Render 128x128 point-filtered tactical card sprite
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
import RogueModelKit as rmk


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
        choices=["tank", "walker", "bike", "artillery", "arc_tank"],
        help="Vehicle archetype",
    )
    parser.add_argument(
        "--faction",
        default="vanguard",
        choices=["vanguard", "siege", "syndicate", "arctic"],
        help="Visual 5-tone faction palette",
    )
    parser.add_argument(
        "--weapon",
        default="cannon",
        choices=["cannon", "gatling", "missile", "arc"],
        help="Primary weapon subsystem",
    )
    parser.add_argument("--dozer", action="store_true", help="Include frontal dozer ram blade")
    parser.add_argument("--skirts", action="store_true", default=True, help="Include armored side skirts")
    parser.add_argument("--multiview", action="store_true", help="Render 4 orthographic views")
    parser.add_argument("--card", action="store_true", default=True, help="Render 128x128 tactical card sprite")
    parser.add_argument("--out-dir", default=None, help="Base root directory for outputs")
    parser.add_argument("--blend-path", default=None, help="Explicit .blend output file path")
    parser.add_argument("--fbx-path", default=None, help="Explicit .fbx output file path")
    parser.add_argument("--preview-path", default=None, help="Explicit preview render output file path")
    parser.add_argument("--card-path", default=None, help="Explicit card sprite output file path")

    return parser.parse_args(script_args)


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
    if args.card:
        card_path = resolve_path(args.card_path, REPO_ROOT) or (
            out_root
            / "unity"
            / "RogueCommandAssetAuthoring"
            / "Assets"
            / "RogueCommandExpansionSample"
            / f"{args.name}Card.png"
        )
    else:
        card_path = None

    builder = rmk.RogueVehicleBuilder(args.name, faction=args.faction)

    # 1. Mobility and Chassis by Archetype
    if args.archetype == "walker":
        builder.add_bipedal_legs(hip_height=1.45, hip_spacing_x=0.88)
        # Torso chassis
        builder.objects.extend([
            rmk.make_box(f"{args.name}Armor_Pelvis", (0.92, 0.65, 0.42), (0.0, -0.02, 1.40), builder.palette.gunmetal, 0.08),
            rmk.make_box(f"{args.name}Armor_Torso", (1.14, 0.74, 0.78), (0.0, 0.02, 1.96), builder.palette.primary_armor, 0.09),
            rmk.make_box(f"{args.name}Armor_ChestPlate", (1.02, 0.22, 0.45), (0.0, 0.42, 2.04), builder.palette.light_armor, 0.065),
            rmk.make_box(f"{args.name}Armor_PowerCell", (0.72, 0.40, 0.60), (0.0, -0.52, 2.03), builder.palette.gunmetal, 0.07),
        ])
        builder.add_sensor_visor()

    elif args.archetype == "bike":
        builder.add_all_terrain_wheels(wheelbase_y=1.9, track_width_x=0.0, wheel_radius=0.36, wheel_width=0.22)
        # Spine frame and cockpit
        builder.objects.extend([
            rmk.make_box(f"{args.name}Armor_FrameSpine", (0.36, 1.95, 0.38), (0.0, 0.0, 0.72), builder.palette.primary_armor, 0.045),
            rmk.make_box(f"{args.name}Armor_EngineBlock", (0.48, 0.72, 0.44), (0.0, -0.15, 0.85), builder.palette.gunmetal, 0.04),
            rmk.make_box(f"{args.name}Armor_CowlVisor", (0.42, 0.52, 0.30), (0.0, 0.48, 1.05), builder.palette.light_armor, 0.035,
                         (math.radians(-15), 0.0, 0.0)),
        ])
        builder.add_sensor_visor()

    elif args.archetype == "artillery":
        builder.add_sloped_hull(length=3.4, width=2.6, height=0.96)
        builder.add_continuous_tracks(length=3.2, height=0.68, belt_width=0.36, roadwheels=6, with_skirts=args.skirts)
        builder.add_engine_deck(slat_count=4, with_exhausts=True)
        builder.add_turret(size=(1.5, 1.7, 0.48), center_y=0.12)
        # Rear outrigger recoil spades
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

    else:  # standard tank
        builder.add_sloped_hull(length=3.0, width=2.4, height=0.88)
        builder.add_continuous_tracks(length=2.85, height=0.62, belt_width=0.34, roadwheels=5, with_skirts=args.skirts)
        builder.add_engine_deck(slat_count=4, with_exhausts=True)
        builder.add_turret(size=(1.4, 1.6, 0.45), center_y=0.08)
        builder.add_sensor_visor()
        builder.add_spring_antenna()

    # 2. Weapons
    if args.archetype not in ("arc_tank",):
        if args.weapon == "gatling":
            if args.archetype == "walker":
                builder.add_rotary_gatling(side=-1, pivot_offset=(0.80, 0.45, 1.45))
                builder.add_rotary_gatling(side=1, pivot_offset=(0.80, 0.45, 1.45))
            else:
                builder.add_rotary_gatling(side=1)
        elif args.weapon == "missile":
            builder.add_missile_pod(rows=2, cols=3, elevation_deg=16.0)
        elif args.weapon == "arc":
            builder.add_arc_emitter(core_radius=0.20, prongs=3)
        else:  # cannon
            builder.add_cannon(caliber=0.095, length=1.70, elevation_deg=11.0)

    # 3. Optional Dozer Blade
    if args.dozer and args.archetype in ("tank", "artillery"):
        builder.add_dozer_blade(width=builder.hull_width * 1.05, height=0.44)

    # 4. Build, Validate, and Export
    res = builder.build_and_export(
        fbx_path=fbx_path,
        blend_path=blend_path,
        preview_path=preview_path,
        card_path=card_path,
        render_multiview=args.multiview,
        ortho_scale=6.2 if args.archetype == "artillery" else 5.6,
    )

    print("\n========================================================")
    print(f"Generated Rogue Command Vehicle: {args.name}")
    print(f"  Archetype: {args.archetype} | Faction: {args.faction} | Weapon: {args.weapon}")
    print(f"  Polygons: {res['total_polygons']} | Vertices: {res['total_vertices']}")
    print(f"  Batched Unity Meshes: {res['exported_batched_objects']}")
    print(f"  Ground Contact Valid: {res['ground_contact_valid']}")
    print(f"  Source Blend: {blend_path}")
    print(f"  Unity FBX: {fbx_path}")
    print(f"  Preview Image: {preview_path}")
    if card_path:
        print(f"  Tactical Card Sprite: {card_path}")
    print("========================================================\n")


if __name__ == "__main__":
    main()
