"""Build the editable Ironclad Walker source and Unity FBX without rendering previews."""

from __future__ import annotations

import math
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))

import RogueModelKit as rmk
from ActiveUnitVisualEnhancements import apply_wave2_details, apply_wave34_details
from mathutils import Vector


UNIT_NAME = "IroncladWalker"
BLEND_PATH = REPO_ROOT / "content" / "source-art" / "ironclad-walker.blend"
FBX_PATH = (
    REPO_ROOT
    / "unity"
    / "RogueCommandAssetAuthoring"
    / "Assets"
    / "RogueCommandExpansionSample"
    / "Models"
    / f"{UNIT_NAME}.fbx"
)


def build_ironclad_walker_model() -> None:
    """Create a reinforced dual-Gatling biped within the native walker envelope."""
    rmk.clear_scene()

    builder = rmk.RogueVehicleBuilder(UNIT_NAME, faction="vanguard")
    builder.add_bipedal_legs(hip_height=1.45, hip_spacing_x=0.88)

    armor = builder.palette.primary_armor
    light_armor = builder.palette.light_armor
    gunmetal = builder.palette.gunmetal
    warning = builder.palette.warning
    sensor = builder.palette.emissive

    # Shape the main armor as integrated shells. Keep the waist, torso, chest
    # shield, and rear power cell distinct where their functions meet, but avoid
    # the stacked rectangular-block silhouette of the earlier generator pass.
    builder.objects.extend(
        [
            rmk.make_lofted_superellipse_shell(
                f"{UNIT_NAME}Armor_Pelvis",
                [(1.17, 0.0, -0.01, 0.34, 0.21),
                 (1.26, 0.0, -0.01, 0.46, 0.29),
                 (1.48, 0.0, -0.01, 0.48, 0.30),
                 (1.61, 0.0, -0.01, 0.38, 0.25)],
                gunmetal,
                axis="Z", segments=20, exponent=0.70, smooth=False,
            ),
            rmk.make_lofted_superellipse_shell(
                f"{UNIT_NAME}Armor_Torso",
                [(1.58, 0.0, 0.00, 0.43, 0.27),
                 (1.72, 0.0, 0.01, 0.55, 0.34),
                 (2.12, 0.0, 0.00, 0.59, 0.36),
                 (2.29, 0.0, -0.02, 0.49, 0.32),
                 (2.39, 0.0, -0.03, 0.34, 0.25)],
                armor,
                axis="Z", segments=20, exponent=0.70, smooth=False,
            ),
            rmk.make_lofted_superellipse_shell(
                f"{UNIT_NAME}Armor_ChestPlate",
                [(1.84, 0.0, 0.39, 0.34, 0.08),
                 (1.94, 0.0, 0.42, 0.45, 0.10),
                 (2.13, 0.0, 0.42, 0.44, 0.10),
                 (2.23, 0.0, 0.38, 0.34, 0.08)],
                light_armor,
                axis="Z", segments=16, exponent=0.62, smooth=False,
            ),
            rmk.make_lofted_superellipse_shell(
                f"{UNIT_NAME}Armor_RearPowerCell",
                [(1.72, 0.0, -0.48, 0.24, 0.13),
                 (1.81, 0.0, -0.51, 0.33, 0.17),
                 (2.15, 0.0, -0.51, 0.34, 0.17),
                 (2.24, 0.0, -0.49, 0.27, 0.13)],
                gunmetal,
                axis="Z", segments=16, exponent=0.76, smooth=True,
            ),
        ]
    )

    for side in (-1, 1):
        side_tag = "L" if side < 0 else "R"
        builder.objects.extend(
            [
                rmk.make_lofted_superellipse_shell(
                    f"{UNIT_NAME}Armor_ShoulderGuard_{side_tag}",
                    [(2.00, side * 0.64, 0.02, 0.22, 0.22),
                     (2.11, side * 0.67, 0.02, 0.31, 0.33),
                     (2.31, side * 0.67, 0.02, 0.34, 0.35),
                     (2.42, side * 0.64, 0.02, 0.24, 0.29)],
                    armor,
                    axis="Z", segments=16, exponent=0.70, smooth=False,
                ),
                rmk.make_lofted_superellipse_shell(
                    f"{UNIT_NAME}Armor_ShoulderInset_{side_tag}",
                    [(2.12, side * 0.67, 0.36, 0.11, 0.05),
                     (2.20, side * 0.67, 0.40, 0.18, 0.07),
                     (2.31, side * 0.67, 0.39, 0.16, 0.06),
                     (2.36, side * 0.67, 0.35, 0.10, 0.04)],
                    light_armor,
                    axis="Z", segments=12, exponent=0.62, smooth=False,
                ),
                rmk.make_lofted_superellipse_shell(
                    f"{UNIT_NAME}Armor_HipGuard_{side_tag}",
                    [(1.25, side * 0.46, 0.16, 0.13, 0.07),
                     (1.32, side * 0.47, 0.17, 0.21, 0.10),
                     (1.50, side * 0.47, 0.17, 0.21, 0.10),
                     (1.57, side * 0.46, 0.15, 0.14, 0.07)],
                    armor,
                    axis="Z", segments=16, exponent=0.76, smooth=True,
                ),
                rmk.make_lofted_superellipse_shell(
                    f"{UNIT_NAME}Armor_GatlingBracer_{side_tag}",
                    [(1.42, side * 0.72, 0.17, 0.14, 0.18),
                     (1.50, side * 0.72, 0.20, 0.18, 0.24),
                     (1.68, side * 0.72, 0.20, 0.18, 0.24),
                     (1.76, side * 0.72, 0.19, 0.14, 0.18)],
                    light_armor,
                    axis="Z", segments=16, exponent=0.76, smooth=True,
                ),
                rmk.make_box(
                    f"{UNIT_NAME}Warning_ShoulderMark_{side_tag}",
                    (0.18, 0.035, 0.09),
                    (side * 0.67, 0.478, 2.30),
                    warning,
                    bevel=0.008,
                ),
            ]
        )

    # Keep the two rotary gun mouths and all status/audio anchors in the model's
    # local envelope. Both firepoints sit under one canonical primary muzzle.
    builder.turret_center = Vector((0.0, 0.02, 1.98))
    builder.gun_pivot = Vector((0.0, 1.18, 1.58))
    builder.gun_dir = Vector((0.0, 1.0, 0.0))
    builder.primary_firepoint_locs = [
        Vector((side * 0.72, 1.18, 1.58)) for side in (-1, 1)
    ]
    builder.weapon_audio_loc = Vector((0.0, 1.18, 1.58))
    builder.engine_audio_loc = Vector((0.0, -0.51, 2.01))
    builder.damage_smoke_loc = Vector((0.0, -0.51, 2.10))
    builder.damage_fire_loc = Vector((0.0, -0.45, 1.86))

    # Recessed targeting optics sit above the chest and point in the unit's forward direction (+Y).
    builder.objects.extend(
        rmk.make_recessed_sensor_visor(
            f"{UNIT_NAME}Sensor_Visor",
            location=(0.0, 0.535, 2.18),
            size=(0.46, 0.14, 0.15),
            armor_mat=light_armor,
            lens_mat=sensor,
            cavity_mat=gunmetal,
        )
    )

    # Keep the paired guns at waist height and shorten the barrels to fit the native walker footprint.
    for side in (-1, 1):
        side_tag = "L" if side < 0 else "R"
        builder.objects.extend(
            rmk.make_rotary_gatling_cannon(
                f"{UNIT_NAME}Gatling_{side_tag}",
                pivot=(side * 0.72, 0.12, 1.58),
                direction=(0.0, 1.0, 0.0),
                barrel_count=6,
                barrel_length=0.88,
                barrel_radius=0.03,
                cluster_radius=0.085,
                housing_radius=0.17,
                housing_length=0.30,
                gunmetal_mat=gunmetal,
                housing_mat=light_armor,
                accent_mat=warning,
            )
        )

    builder.objects.extend(apply_wave2_details(
        UNIT_NAME,
        objects=builder.objects,
        materials={"armor": armor, "gunmetal": gunmetal, "warning": warning},
    ))

    builder.objects.extend(apply_wave34_details(
        UNIT_NAME,
        objects=builder.objects,
        materials={"armor": armor, "gunmetal": gunmetal, "warning": warning},
    ))

    diagnostics = rmk.validate_model_mesh_quality(builder.objects)
    bounds = diagnostics["bounds"]
    if not diagnostics["ground_contact_valid"] or diagnostics["missing_materials"]:
        raise ValueError("Ironclad Walker failed ground-contact or material validation")
    if bounds["width_x"] > 2.2 or bounds["height_z"] > 2.7 or bounds["length_y"] > 2.2:
        raise ValueError(
            "Ironclad Walker exceeds the approximate GatlingWalker envelope "
            f"(2.2 x 2.7 x 2.2 m): {bounds}"
        )
    if diagnostics["total_polygons"] > rmk.DEFAULT_FACE_BUDGET:
        raise ValueError(
            f"Ironclad Walker exceeds the {rmk.DEFAULT_FACE_BUDGET:,}-face LOD0 profiling ceiling"
        )

    result = builder.build_and_export(
        fbx_path=FBX_PATH,
        blend_path=BLEND_PATH,
        include_locators=True,
        generate_lods=False,
        include_rig=True,
        include_ik=True,
        include_animations=True,
        wear_level="factory_new",
        target_budget=rmk.DEFAULT_FACE_BUDGET,
        profile_budget=True,
        structural_wear_mask=True,
        unity_batch_names=True,
        bake_atlas=True,
        atlas_resolution=2048,
        single_material_atlas=True,
    )
    if not result.get("ground_contact_valid"):
        raise ValueError("Rigged Ironclad Walker failed the export ground-contact check")

    print(f"Editable source: {BLEND_PATH}")
    print(f"Unity FBX: {FBX_PATH}")
    print(f"Source mesh parts: {diagnostics['object_count']}")
    print(f"Exported material batches: {result['exported_batched_objects']}")
    print(f"Rigged: {result.get('has_armature', False)} | Clips: {', '.join(result.get('action_clips', []))}")
    print(f"Bounds (W x L x H): {bounds['width_x']} x {bounds['length_y']} x {bounds['height_z']} m")
    print("Preview rendering was skipped; existing previews and framing were left untouched.")


if __name__ == "__main__":
    build_ironclad_walker_model()
