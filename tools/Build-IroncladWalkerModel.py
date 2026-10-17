"""Build the editable Ironclad Walker source and Unity FBX without rendering previews."""

from __future__ import annotations

import math
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))

import bpy
import RogueModelKit as rmk


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
    BLEND_PATH.parent.mkdir(parents=True, exist_ok=True)
    FBX_PATH.parent.mkdir(parents=True, exist_ok=True)

    builder = rmk.RogueVehicleBuilder(UNIT_NAME, faction="vanguard")
    builder.add_bipedal_legs(hip_height=1.45, hip_spacing_x=0.88)

    armor = builder.palette.primary_armor
    light_armor = builder.palette.light_armor
    gunmetal = builder.palette.gunmetal
    warning = builder.palette.warning
    sensor = builder.palette.emissive

    # Reinforced waist, torso, and chest plate define a heavier silhouette than the native frame.
    builder.objects.extend(
        [
            rmk.make_box(
                f"{UNIT_NAME}Armor_Pelvis",
                (0.96, 0.62, 0.42),
                (0.0, -0.02, 1.40),
                gunmetal,
                bevel=0.065,
            ),
            rmk.make_box(
                f"{UNIT_NAME}Armor_Torso",
                (1.18, 0.74, 0.78),
                (0.0, 0.02, 1.98),
                armor,
                bevel=0.085,
            ),
            rmk.make_box(
                f"{UNIT_NAME}Armor_ChestPlate",
                (1.03, 0.20, 0.42),
                (0.0, 0.43, 2.08),
                light_armor,
                bevel=0.045,
                rotation=(-math.radians(8), 0.0, 0.0),
            ),
            rmk.make_box(
                f"{UNIT_NAME}Armor_RearPowerCell",
                (0.72, 0.34, 0.56),
                (0.0, -0.51, 2.01),
                gunmetal,
                bevel=0.055,
            ),
        ]
    )

    for side in (-1, 1):
        side_tag = "L" if side < 0 else "R"
        builder.objects.extend(
            [
                rmk.make_box(
                    f"{UNIT_NAME}Armor_ShoulderGuard_{side_tag}",
                    (0.48, 0.70, 0.40),
                    (side * 0.67, 0.02, 2.25),
                    armor,
                    bevel=0.055,
                ),
                rmk.make_box(
                    f"{UNIT_NAME}Armor_ShoulderInset_{side_tag}",
                    (0.28, 0.14, 0.24),
                    (side * 0.67, 0.40, 2.23),
                    light_armor,
                    bevel=0.025,
                ),
                rmk.make_box(
                    f"{UNIT_NAME}Armor_HipGuard_{side_tag}",
                    (0.34, 0.18, 0.34),
                    (side * 0.47, 0.17, 1.40),
                    armor,
                    bevel=0.028,
                ),
                rmk.make_box(
                    f"{UNIT_NAME}Armor_GatlingBracer_{side_tag}",
                    (0.36, 0.48, 0.30),
                    (side * 0.72, 0.20, 1.59),
                    light_armor,
                    bevel=0.035,
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

    diagnostics = rmk.validate_model_mesh_quality(builder.objects)
    bounds = diagnostics["bounds"]
    if not diagnostics["ground_contact_valid"] or diagnostics["missing_materials"]:
        raise ValueError("Ironclad Walker failed ground-contact or material validation")
    if bounds["width_x"] > 2.2 or bounds["height_z"] > 2.7 or bounds["length_y"] > 2.2:
        raise ValueError(
            "Ironclad Walker exceeds the approximate GatlingWalker envelope "
            f"(2.2 x 2.7 x 2.2 m): {bounds}"
        )
    if diagnostics["total_polygons"] > 5000:
        raise ValueError("Ironclad Walker exceeds the 5,000-face low-poly authoring budget")

    rmk.generate_lightmap_uvs(builder.objects)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))

    export_batches = rmk.merge_meshes_by_material(builder.objects, UNIT_NAME, {
        builder.palette.primary_armor: "armor",
        builder.palette.light_armor: "lightArmor",
        builder.palette.gunmetal: "gunmetal",
        builder.palette.warning: "warning",
        builder.palette.emissive: "sensor",
    })
    rmk.export_unity_fbx(FBX_PATH, export_batches)

    print(f"Editable source: {BLEND_PATH}")
    print(f"Unity FBX: {FBX_PATH}")
    print(f"Source mesh parts: {diagnostics['object_count']}")
    print(f"Exported material batches: {len(export_batches)}")
    print(f"Bounds (W x L x H): {bounds['width_x']} x {bounds['length_y']} x {bounds['height_z']} m")
    print("Preview rendering was skipped; existing previews and framing were left untouched.")


if __name__ == "__main__":
    build_ironclad_walker_model()
