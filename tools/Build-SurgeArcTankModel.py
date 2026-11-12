"""Build the editable Surge Arc Tank source and its Unity FBX without rendering previews."""

from __future__ import annotations

import math
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))

import bpy
import RogueModelKit as rmk
from ActiveUnitVisualEnhancements import apply_wave2_details, apply_wave34_details
from CanonicalUnitHardpoints import create_canonical_hardpoint_sockets


UNIT_NAME = "SurgeArcTank"
BLEND_PATH = REPO_ROOT / "content" / "source-art" / "surge-arc-tank.blend"
FBX_PATH = (
    REPO_ROOT
    / "unity"
    / "RogueCommandAssetAuthoring"
    / "Assets"
    / "RogueCommandExpansionSample"
    / "Models"
    / f"{UNIT_NAME}.fbx"
)


def build_surge_arc_tank_model() -> None:
    """Build a tracked electromagnetic tank around a vertical induction accelerator."""
    rmk.clear_scene()
    builder = rmk.RogueVehicleBuilder(UNIT_NAME, faction="syndicate", ground_clearance=0.06)
    builder.add_sloped_hull(length=2.35, width=1.78, height=0.78, bevel=0.055)
    builder.add_continuous_tracks(
        length=2.54,
        height=0.59,
        belt_width=0.30,
        roadwheels=5,
        with_skirts=True,
    )

    armor = builder.palette.primary_armor
    light_armor = builder.palette.light_armor
    gunmetal = builder.palette.gunmetal
    tread = builder.palette.tread
    warning = builder.palette.warning
    arc = builder.palette.emissive

    # Rear generator pack feeds the exposed accelerator without changing the native tank rig.
    builder.objects.extend(
        [
            rmk.make_box(
                f"{UNIT_NAME}Armor_GeneratorPack",
                (1.12, 0.58, 0.36),
                (0.0, -0.73, 0.92),
                gunmetal,
                bevel=0.045,
            ),
            rmk.make_box(
                f"{UNIT_NAME}Armor_GeneratorCover",
                (0.88, 0.42, 0.20),
                (0.0, -0.70, 1.18),
                light_armor,
                bevel=0.035,
            ),
        ]
    )

    # Twin insulated capacitor housings make the silhouette read as an electrical unit.
    for side in (-1, 1):
        side_tag = "L" if side < 0 else "R"
        x = side * 0.66
        builder.objects.extend(
            [
                rmk.make_box(
                    f"{UNIT_NAME}Armor_Capacitor_{side_tag}",
                    (0.34, 0.78, 0.48),
                    (x, -0.02, 0.94),
                    armor,
                    bevel=0.045,
                ),
                rmk.make_box(
                    f"{UNIT_NAME}Armor_CapacitorCap_{side_tag}",
                    (0.36, 0.24, 0.36),
                    (x, 0.36, 0.96),
                    light_armor,
                    bevel=0.03,
                ),
                rmk.make_box(
                    f"{UNIT_NAME}Warning_CapacitorMark_{side_tag}",
                    (0.018, 0.28, 0.07),
                    (x + side * 0.167, -0.01, 1.02),
                    warning,
                    bevel=0.006,
                ),
                rmk.make_rod(
                    f"{UNIT_NAME}Gunmetal_CapacitorConduit_{side_tag}",
                    (side * 0.51, -0.48, 1.04),
                    (side * 0.42, -0.27, 1.27),
                    0.045,
                    gunmetal,
                    vertices=8,
                ),
            ]
        )

    # The LightningTank's stock weapon is a toroidal coil; center the replacement on its
    # documented pivot so the overlay remains aligned with the native attack hardpoint.
    core_center = (0.0, -0.10, 1.20)
    ring_rotation = (math.radians(90.0), 0.0, 0.0)  # Default torus normal (+Z) -> forward (+Y).
    builder.objects.extend(
        [
            rmk.make_cylinder(
                f"{UNIT_NAME}Gunmetal_AcceleratorMount",
                0.46,
                0.17,
                (0.0, -0.08, 0.92),
                gunmetal,
                vertices=10,
                bevel=0.025,
            ),
            rmk.make_box(
                f"{UNIT_NAME}Armor_AcceleratorMantlet",
                (0.84, 0.34, 0.26),
                (0.0, 0.12, 1.00),
                light_armor,
                bevel=0.04,
            ),
            rmk.make_torus(
                f"{UNIT_NAME}Gunmetal_InductionFrame",
                core_center,
                major_radius=0.320,
                minor_radius=0.060,
                material=gunmetal,
                rotation=ring_rotation,
                major_segments=16,
                minor_segments=6,
            ),
            rmk.make_torus(
                f"{UNIT_NAME}Arc_InductionCoil",
                core_center,
                major_radius=0.275,
                minor_radius=0.047,
                material=arc,
                rotation=ring_rotation,
                major_segments=16,
                minor_segments=6,
            ),
            rmk.make_torus(
                f"{UNIT_NAME}Gunmetal_InsulatorRim",
                (0.0, -0.13, 1.20),
                major_radius=0.350,
                minor_radius=0.022,
                material=light_armor,
                rotation=ring_rotation,
                major_segments=16,
                minor_segments=5,
            ),
            rmk.make_cylinder(
                f"{UNIT_NAME}Arc_PlasmaCore",
                0.16,
                0.58,
                (0.0, -0.02, 1.20),
                arc,
                rotation=ring_rotation,
                vertices=12,
                bevel=0.012,
            ),
        ]
    )

    # Forward prongs frame the native discharge direction and stay inside the tank envelope.
    for side in (-1, 1):
        side_tag = "L" if side < 0 else "R"
        builder.objects.extend(
            [
                rmk.make_rod(
                    f"{UNIT_NAME}Gunmetal_DischargeProng_{side_tag}",
                    (side * 0.29, 0.13, 1.20),
                    (side * 0.13, 1.05, 1.20),
                    0.062,
                    gunmetal,
                    vertices=8,
                    bevel=0.012,
                ),
                rmk.make_cylinder(
                    f"{UNIT_NAME}Arc_DischargeTip_{side_tag}",
                    0.095,
                    0.13,
                    (side * 0.13, 1.02, 1.20),
                    arc,
                    rotation=ring_rotation,
                    vertices=10,
                    bevel=0.012,
                ),
                rmk.make_box(
                    f"{UNIT_NAME}Warning_ProngsMark_{side_tag}",
                    (0.10, 0.10, 0.018),
                    (side * 0.66, 0.38, 1.149),
                    warning,
                    bevel=0.006,
                ),
            ]
        )

    # Small recessed forward optics provide a clear nose without adding a separate card image.
    builder.objects.extend(
        rmk.make_recessed_sensor_visor(
            f"{UNIT_NAME}Sensor_Visor",
            location=(0.0, 0.646, 0.88),
            size=(0.34, 0.10, 0.12),
            armor_mat=light_armor,
            lens_mat=arc,
            cavity_mat=gunmetal,
        )
    )

    builder.objects.extend(apply_wave2_details(
        UNIT_NAME,
        objects=builder.objects,
        materials={"armor": armor, "gunmetal": gunmetal, "warning": warning, "tread": tread},
    ))

    builder.objects.extend(apply_wave34_details(
        UNIT_NAME,
        objects=builder.objects,
        materials={"armor": armor, "gunmetal": gunmetal, "warning": warning, "tread": tread},
    ))

    rmk.enforce_face_budget_before_export(builder.objects, UNIT_NAME)
    BLEND_PATH.parent.mkdir(parents=True, exist_ok=True)
    FBX_PATH.parent.mkdir(parents=True, exist_ok=True)
    diagnostics = rmk.validate_model_mesh_quality(builder.objects)
    bounds = diagnostics["bounds"]
    if not diagnostics["ground_contact_valid"] or diagnostics["missing_materials"]:
        raise ValueError("Surge Arc Tank failed ground-contact or material validation")
    if (
        bounds["width_x"] > 2.3
        or bounds["height_z"] > 1.6
        or bounds["length_y"] > 3.0
    ):
        raise ValueError(
            "Surge Arc Tank exceeds the approximate LightningTank envelope "
            f"(2.3 x 1.6 x 3.0 m): {bounds}"
        )
    rmk.bake_advanced_vertex_shading(
        builder.objects,
        wear_level="field_tested",
        structural_wear_mask=True,
    )
    rmk.generate_lightmap_uvs(builder.objects)
    hardpoint_sockets = create_canonical_hardpoint_sockets(UNIT_NAME)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))

    export_batches = rmk.merge_meshes_by_material(
        builder.objects,
        UNIT_NAME,
        {
            builder.palette.primary_armor: "armor",
            builder.palette.light_armor: "lightArmor",
            builder.palette.gunmetal: "gunmetal",
            builder.palette.tread: "tread",
            builder.palette.wheel_rim: "lightArmor",
            builder.palette.wheel_hub: "armor",
            builder.palette.warning: "magenta",
            builder.palette.emissive: "arc",
        },
    )
    rmk.export_unity_fbx(FBX_PATH, export_batches + hardpoint_sockets, include_empties=True, generate_profiled_lods=True, asset_id=UNIT_NAME)

    print(f"Editable source: {BLEND_PATH}")
    print(f"Unity FBX: {FBX_PATH}")
    print(f"Source mesh parts: {diagnostics['object_count']}")
    print(f"Exported material batches: {len(export_batches)}")
    print(f"Bounds (W x L x H): {bounds['width_x']:.3f} x {bounds['length_y']:.3f} x {bounds['height_z']:.3f} m")
    print(f"Vertices / polygons: {diagnostics['total_vertices']} / {diagnostics['total_polygons']}")


if __name__ == "__main__":
    build_surge_arc_tank_model()
