"""Build-UnitModelTemplate.py: Production-ready vehicle builder template for Rogue Command.

Demonstrates all core visual principles:
- Sloped glacis chassis with ground clearance
- Continuous articulated track loops (sloped approach angle)
- Stepped road wheels (outer rim, recessed dish, center hex cap)
- Armored engine deck with angled cooling louvers
- Recessed glowing optic sensors (armored visor with glowing lenses)
- 5-tone military/sci-fi PBR material assignment
- 1-segment machined chamfers for RTS specular catch-lights
- 3-point AgX preview studio and Unity FBX export
"""

from __future__ import annotations
import math
import sys
from pathlib import Path

# Add script directory and repo tools to sys.path so RogueModelKit is found
SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parents[3]
sys.path.insert(0, str(SCRIPT_DIR))
sys.path.insert(0, str(REPO_ROOT / "tools"))

import bpy
from mathutils import Vector
import RogueModelKit as rmk


# Target paths (customize for your unit)
UNIT_NAME = "ColossusSiegePlatform"
BLEND_PATH = REPO_ROOT / "content" / "source-art" / "colossus-siege.blend"
FBX_PATH = (REPO_ROOT / "unity" / "RogueCommandAssetAuthoring" / "Assets" /
            "RogueCommandExpansionSample" / "Models" / f"{UNIT_NAME}.fbx")
PREVIEW_PATH = REPO_ROOT / "artifacts" / "previews" / "colossus-siege.png"


def build_colossus_siege_model():
    rmk.clear_scene()
    BLEND_PATH.parent.mkdir(parents=True, exist_ok=True)
    FBX_PATH.parent.mkdir(parents=True, exist_ok=True)
    PREVIEW_PATH.parent.mkdir(parents=True, exist_ok=True)

    # 1. Disciplined 5-Tone Material Palette (Heavy Industrial / Siege Set)
    armor_primary = rmk.make_material("ColossusArmor", (0.12, 0.19, 0.24), metallic=0.42, roughness=0.58)
    armor_light = rmk.make_material("ColossusLightArmor", (0.22, 0.32, 0.38), metallic=0.34, roughness=0.60)
    gunmetal = rmk.make_material("ColossusGunmetal", (0.07, 0.10, 0.12), metallic=0.78, roughness=0.30)
    tread_mat = rmk.make_material("ColossusTread", (0.045, 0.055, 0.06), metallic=0.18, roughness=0.74)
    wheel_rim = rmk.make_material("ColossusWheelRim", (0.16, 0.22, 0.26), metallic=0.62, roughness=0.40)
    wheel_hub = rmk.make_material("ColossusWheelHub", (0.10, 0.15, 0.18), metallic=0.55, roughness=0.50)
    warning_amber = rmk.make_material("ColossusSignalAmber", (0.95, 0.32, 0.05), metallic=0.18, roughness=0.36)
    sensor_cyan = rmk.make_material(
        "ColossusSensorCyan", (0.04, 0.72, 0.88), metallic=0.20, roughness=0.22,
        emission_color=(0.04, 0.72, 0.88), emission_strength=1.8
    )

    model_objects = []

    # 2. Macro Silhouette: Sloped Armored Hull (Wide stance, low center of gravity)
    # Dimensions: Length ~3.2m, Width ~2.5m, Height ~0.9m
    hull = rmk.make_sloped_glacis_hull(
        "ColossusArmor_Hull",
        length_front=1.55, length_rear=1.50,
        width_front=1.40, width_rear=1.48,
        height_lower=0.24, height_upper=0.78,
        material=armor_primary, bevel=0.045
    )
    model_objects.append(hull)

    # Upper hull deck and side armor sponsons
    model_objects += [
        rmk.make_box("ColossusArmor_Deck", (1.36, 1.95, 0.30), (0.0, -0.05, 0.88), armor_light, bevel=0.04),
        rmk.make_box("ColossusArmor_Sponson_L", (0.24, 2.70, 0.38), (-1.08, -0.02, 0.68), armor_primary, bevel=0.035),
        rmk.make_box("ColossusArmor_Sponson_R", (0.24, 2.70, 0.38), ( 1.08, -0.02, 0.68), armor_primary, bevel=0.035),
        # Side armor skirt plates
        rmk.make_box("ColossusArmor_Skirt_L", (0.06, 2.50, 0.32), (-1.24, -0.02, 0.46), armor_light, bevel=0.02),
        rmk.make_box("ColossusArmor_Skirt_R", (0.06, 2.50, 0.32), ( 1.24, -0.02, 0.46), armor_light, bevel=0.02),
    ]

    # 3. Meso Geometry: Continuous Articulated Track Belts (Left & Right)
    for side in (-1, 1):
        side_tag = "L" if side < 0 else "R"
        x_pos = side * 1.08
        track_belt = rmk.make_continuous_track_belt(
            f"ColossusTreads_Belt_{side_tag}",
            x_pos=x_pos, length=2.85, height=0.62, width=0.34,
            tread_mat=tread_mat, ground_z=0.06
        )
        model_objects.append(track_belt)

        # Stepped road wheels (5 per side)
        for idx, y in enumerate((-1.05, -0.52, 0.0, 0.52, 1.05), start=1):
            wheels = rmk.make_stepped_roadwheel(
                f"ColossusWheel_Road_{side_tag}_{idx}",
                location=(side * 1.20, y, 0.34),
                radius=0.18, width=0.11,
                rim_mat=wheel_rim, hub_mat=wheel_hub, axle_mat=gunmetal,
                vertices=12
            )
            model_objects.extend(wheels)

        # Drive sprocket & front idler
        for y_pos, name_tag, radius in [(-1.32, "DriveSprocket", 0.16), (1.32, "FrontIdler", 0.16)]:
            wheels = rmk.make_stepped_roadwheel(
                f"ColossusWheel_{name_tag}_{side_tag}",
                location=(side * 1.20, y_pos, 0.36),
                radius=radius, width=0.12,
                rim_mat=gunmetal, hub_mat=wheel_rim, axle_mat=gunmetal,
                vertices=12
            )
            model_objects.extend(wheels)

        # Tread cleats along top run for mechanical surface rhythm
        for cleat_idx, y in enumerate((-1.0, -0.75, -0.5, -0.25, 0.0, 0.25, 0.5, 0.75, 1.0), start=1):
            model_objects.append(rmk.make_box(
                f"ColossusTreads_Cleat_{side_tag}_{cleat_idx}",
                (0.30, 0.14, 0.06), (side * 1.08, y, 0.64), tread_mat, bevel=0.012
            ))

        # Flank identification warning stripes
        model_objects.append(rmk.make_box(
            f"ColossusAccent_Stripe_{side_tag}",
            (0.02, 1.20, 0.08), (side * 1.272, -0.05, 0.52), warning_amber, bevel=0.01
        ))

    # 4. Engine Deck with Angled Cooling Louvers
    grille_parts = rmk.make_engine_deck_grille(
        "ColossusArmor_EngineDeck",
        location=(0.0, -0.92, 1.03),
        size=(0.74, 0.65, 0.08),
        slat_count=4,
        slat_mat=gunmetal,
        frame_mat=armor_primary
    )
    model_objects.extend(grille_parts)

    # Dual Heavy Exhaust Stacks
    for side in (-1, 1):
        side_tag = "L" if side < 0 else "R"
        model_objects.append(rmk.make_cylinder(
            f"ColossusArmor_Exhaust_{side_tag}",
            radius=0.065, depth=0.35,
            location=(side * 0.48, -1.22, 1.18),
            material=gunmetal,
            rotation=(math.radians(-18), 0.0, 0.0),
            vertices=10
        ))
        # Exhaust heat shield collar
        model_objects.append(rmk.make_cylinder(
            f"ColossusArmor_ExhaustShield_{side_tag}",
            radius=0.082, depth=0.18,
            location=(side * 0.48, -1.20, 1.12),
            material=armor_light,
            rotation=(math.radians(-18), 0.0, 0.0),
            vertices=10
        ))

    # 5. Heavy Siege Turret & Ballistic Cannon Assembly
    # Octagonal low-profile armored turret base with rear bustle ammo compartment
    turret_center = Vector((0.0, 0.05, 1.15))
    model_objects.append(rmk.make_cylinder(
        "ColossusArmor_TurretBase",
        radius=0.74, depth=0.34,
        location=turret_center,
        material=armor_primary,
        vertices=8, bevel=0.04
    ))
    # Rear bustle ammo compartment (counterweights the heavy cannon)
    model_objects.append(rmk.make_box(
        "ColossusArmor_TurretBustle",
        (0.92, 0.55, 0.28), (0.0, -0.48, 1.18),
        armor_primary, bevel=0.035
    ))
    # Sloped front mantlet cradle housing
    model_objects.append(rmk.make_box(
        "ColossusArmor_TurretMantlet",
        (0.88, 0.48, 0.30), (0.0, 0.42, 1.17),
        armor_light, bevel=0.035,
        rotation=(math.radians(-6), 0.0, 0.0)
    ))

    # Coaxial Cannon Parameterization:
    # All barrel components stem precisely along gun_dir from gun_pivot!
    elev_angle = math.radians(4.5)  # Sleek, grounded, purposeful siege elevation
    gun_pivot = Vector((0.0, 0.52, 1.18))
    gun_dir = Vector((0.0, math.cos(elev_angle), math.sin(elev_angle))).normalized()
    gun_rot = gun_dir.to_track_quat("Z", "Y").to_euler()

    # Heavy breech collar
    breech_loc = gun_pivot + gun_dir * 0.22
    model_objects.append(rmk.make_cylinder(
        "ColossusGun_BreechCollar",
        radius=0.17, depth=0.42,
        location=breech_loc,
        material=gunmetal,
        rotation=gun_rot,
        vertices=10, bevel=0.02
    ))

    # Reinforced thermal shroud / barrel sleeve
    sleeve_loc = gun_pivot + gun_dir * 0.68
    model_objects.append(rmk.make_cylinder(
        "ColossusGun_BarrelSleeve",
        radius=0.13, depth=0.54,
        location=sleeve_loc,
        material=armor_light,
        rotation=gun_rot,
        vertices=8, bevel=0.025
    ))

    # Main rifled cannon barrel (Length: 1.55m)
    barrel_loc = gun_pivot + gun_dir * 1.52
    model_objects.append(rmk.make_cylinder(
        "ColossusGun_MainBarrel",
        radius=0.095, depth=1.55,
        location=barrel_loc,
        material=gunmetal,
        rotation=gun_rot,
        vertices=12, bevel=0.012
    ))

    # Double-baffle muzzle brake (Mounted snugly on the barrel tip)
    muzzle_loc = gun_pivot + gun_dir * 2.32
    model_objects.append(rmk.make_box(
        "ColossusGun_MuzzleBrake",
        (0.24, 0.30, 0.22), muzzle_loc,
        armor_light, bevel=0.02,
        rotation=gun_rot
    ))
    # Muzzle bore hole opening
    bore_loc = gun_pivot + gun_dir * 2.47
    model_objects.append(rmk.make_cylinder(
        "ColossusGun_MuzzleBore",
        radius=0.065, depth=0.10,
        location=bore_loc,
        material=gunmetal,
        rotation=gun_rot,
        vertices=10
    ))

    # Twin hydraulic recoil recuperators (Snugly flanking the mantlet, parallel to gun_dir)
    recoil_offset_x = 0.22
    for side in (-1, 1):
        side_tag = "L" if side < 0 else "R"
        recoil_loc = gun_pivot + gun_dir * 0.28 + Vector((side * recoil_offset_x, 0.0, 0.0))
        model_objects.append(rmk.make_cylinder(
            f"ColossusGun_RecoilCylinder_{side_tag}",
            radius=0.05, depth=0.52,
            location=recoil_loc,
            material=gunmetal,
            rotation=gun_rot,
            vertices=8, bevel=0.008
        ))
        # Mounting bracket attaching cylinder to mantlet
        model_objects.append(rmk.make_box(
            f"ColossusGun_RecoilBracket_{side_tag}",
            (0.06, 0.16, 0.12), recoil_loc - gun_dir * 0.18,
            armor_primary, bevel=0.01
        ))

    # 6. Integrated Optics, Commander Cupola & Clean Antenna Mast
    # Commander's low-profile observation cupola on rear-left of turret
    cupola_loc = Vector((-0.34, -0.08, 1.35))
    model_objects.append(rmk.make_cylinder(
        "ColossusArmor_CommanderCupola",
        radius=0.22, depth=0.14,
        location=cupola_loc,
        material=armor_light,
        vertices=8, bevel=0.02
    ))
    # Armored periscope visor slit on cupola
    model_objects.append(rmk.make_box(
        "ColossusSensor_CupolaVisionSlit",
        (0.26, 0.04, 0.05), cupola_loc + Vector((0.0, 0.15, 0.03)),
        sensor_cyan, bevel=0.008
    ))

    # Integrated Transverse Targeting Rangefinder (Mounted on turret roof, flush and armored)
    rangefinder_loc = Vector((0.0, 0.24, 1.34))
    model_objects.append(rmk.make_box(
        "ColossusSensor_RangefinderBar",
        (0.86, 0.14, 0.12), rangefinder_loc,
        armor_primary, bevel=0.018
    ))
    # Twin recessed rangefinder optical lenses on the bar tips
    for side in (-1, 1):
        side_tag = "L" if side < 0 else "R"
        lens_loc = rangefinder_loc + Vector((side * 0.38, 0.075, 0.0))
        model_objects.append(rmk.make_box(
            f"ColossusSensor_RangefinderLens_{side_tag}",
            (0.08, 0.02, 0.07), lens_loc,
            sensor_cyan, bevel=0.005
        ))

    # Clean Military Spring-Base Antenna on rear-right corner
    antenna_base = Vector((0.36, -0.48, 1.32))
    model_objects += [
        rmk.make_cylinder("ColossusSensor_AntennaBase", 0.04, 0.08, antenna_base, gunmetal, vertices=8, bevel=0.01),
        rmk.make_rod("ColossusSensor_AntennaMast", antenna_base, antenna_base + Vector((0.0, -0.04, 0.75)), 0.012, gunmetal, vertices=6),
    ]

    # Frontal hull headlights
    for side in (-1, 1):
        side_tag = "L" if side < 0 else "R"
        model_objects.append(rmk.make_box(
            f"ColossusAccent_FrontLight_{side_tag}",
            (0.14, 0.08, 0.08), (side * 0.52, 1.44, 0.72), sensor_cyan, bevel=0.012
        ))

    # 7. Rear Outrigger Recoil Spades (Planted ground stabilizers)
    for side in (-1, 1):
        side_tag = "L" if side < 0 else "R"
        model_objects += [
            rmk.make_box(f"ColossusArmor_RecoilSpadeMount_{side_tag}", (0.18, 0.22, 0.24),
                         (side * 0.55, -1.48, 0.54), armor_primary, bevel=0.025),
            rmk.make_box(f"ColossusArmor_RecoilSpadeBlade_{side_tag}", (0.24, 0.08, 0.28),
                         (side * 0.55, -1.62, 0.36), gunmetal, bevel=0.018,
                         rotation=(math.radians(22), 0.0, 0.0)),
        ]

    # 8. Studio Preview Setup, AgX Rendering, and FBX Export
    rmk.setup_preview_studio(model_objects, PREVIEW_PATH, ortho_scale=6.4)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))
    bpy.ops.render.render(write_still=True)

    rmk.export_unity_fbx(FBX_PATH, model_objects)

    print(f"Created source model: {BLEND_PATH}")
    print(f"Created Unity model: {FBX_PATH}")
    print(f"Created preview render: {PREVIEW_PATH}")
    print(f"Exported mesh objects: {len(model_objects)}")


if __name__ == "__main__":
    build_colossus_siege_model()
