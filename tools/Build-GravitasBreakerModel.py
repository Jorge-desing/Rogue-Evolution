"""Build the Wave 7 Gravitas Breaker gravity siege breaker and its PBR atlas."""

from __future__ import annotations

import bpy
import bmesh
import hashlib
import json
from pathlib import Path
import sys

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))

from ActiveUnitVisualEnhancements import apply_wave34_details
from gravitas_breaker_hardflow_recipe import apply_gravitas_breaker_details
import RogueModelKit as rmk
from VisualModelAuthoringSupport import (
    add_box,
    add_link,
    remove_objects,
    run_preset_build,
    simplify_low_value_bevel_details,
)


def refine(builder) -> None:
    builder.locomotion_only = False  # Idle/FireRecoil/Death restaurados (ataque por RCL_Fire)
    p = builder.palette
    builder.objects.extend(apply_wave34_details(
        "GravitasBreaker",
        objects=builder.objects,
        materials={"armor": p.primary_armor, "gunmetal": p.gunmetal},
    ))
    remove_exact_duplicate_meshes(builder)

    remove_objects(
        builder,
        lambda name: name in {
            "GravitasBreakerArmor_CitadelLower",
            "GravitasBreakerArmor_CasemateCollar_CoamingCollar",
            "GravitasBreakerArmor_UpperCasemate",
            "GravitasBreakerTurret_BaseShell",
            "GravitasBreakerTurret_MantletHousing",
            "GravitasBreakerTurret_RearBustle",
            "GravitasBreakerTurret_CheekArmor_L",
            "GravitasBreakerTurret_CheekArmor_R",
            "GravitasBreakerTreads_ArmorSkirt_L",
            "GravitasBreakerTreads_ArmorSkirt_R",
            "GravitasBreakerTurretRingGuard_YawRaceLip",
        }
        or name.startswith("GravitasBreakerTreads_SpacedSkirt_")
        or name.startswith("GravitasBreakerTreads_FenderSplash_")
        or name.startswith("GravitasBreakerTreads_SkirtStripe_")
        or name.startswith("GravitasBreakerArmor_TurretRingGuard_")
        or name.startswith("GravitasBreakerArmor_CheekPlate_")
        or name.startswith("GravitasBreakerArmor_CheekPod_")
        or name.startswith("GravitasBreakerArmor_TrunnionBracket_CheekBlock_")
        or name.startswith("GravitasBreakerTurretRingGuard_FastenerBolt_")
        or name.startswith("GravitasBreakerWeapon_RecoilCylinder_")
        or name.startswith("GravitasBreakerWeaponTrunnions_CheekBlock_")
        or name.startswith("GravitasBreakerRamProw_")
        or name.startswith("GravitasBreakerGravCannon_Gravity_StabilizerRail_"),
    )

    # The ring guard seats on the hull deck; move it above the deck surface so
    # the turret shell rests on it instead of both solids crossing one another.
    for obj in builder.objects:
        if obj.name == "GravitasBreakerTurretRingGuard_DeflectorCollar":
            for vertex in obj.data.vertices:
                vertex.co.z += 0.06
            obj.data.update()

    builder.objects.append(make_sectioned_glacis(
        "GravitasBreakerArmor_CitadelLower",
        [
            (-1.78, 0.70, 0.24, 0.68),
            (-1.55, 0.94, 0.18, 0.88),
            (-1.10, 1.03, 0.16, 0.94),
            (-0.45, 1.03, 0.17, 0.94),
            (0.45, 1.03, 0.19, 0.88),
            (0.98, 0.94, 0.21, 0.74),
            (1.38, 0.76, 0.24, 0.58),
            (1.72, 0.43, 0.29, 0.42),
        ],
        p.primary_armor,
    ))
    builder.objects.append(make_sectioned_glacis(
        "GravitasBreakerTurret_BaseShell",
        [
            (-0.82, 0.45, 1.085, 1.34),
            (-0.68, 0.60, 1.085, 1.50),
            (-0.36, 0.70, 1.085, 1.57),
            (0.02, 0.73, 1.085, 1.55),
            (0.34, 0.68, 1.085, 1.47),
            (0.585, 0.51, 1.085, 1.37),
        ],
        p.primary_armor,
    ))

    hardflow_pilot = apply_gravitas_breaker_details(
        builder.objects,
        armor_material=p.primary_armor,
        mechanical_material=p.gunmetal,
    )
    print("Gravitas Hardflow pilot geometry replay: " + json.dumps(hardflow_pilot, sort_keys=True))

    for side, tag in ((-1.0, "L"), (1.0, "R")):
        add_link(
            builder,
            f"GravitasBreakerJoint_OverloadConduit_{tag}",
            (side * 0.78, -0.35, 1.29),
            (side * 0.78, 0.34, 1.39),
            0.055,
            0.042,
            p.warning,
        )
    builder.damage_smoke_loc = builder.damage_smoke_loc or builder.center_of_mass_loc
    builder.damage_fire_loc = builder.damage_fire_loc or builder.turret_center
    builder.engine_audio_loc = builder.engine_audio_loc or builder.center_of_mass_loc
    builder.weapon_audio_loc = builder.weapon_audio_loc or builder.gun_pivot
    simplify_low_value_bevel_details(
        builder, max_dimension_m=1.2, angle_degrees=48.0
    )


def remove_exact_duplicate_meshes(builder) -> list[str]:
    """Remove only coincident mesh copies with identical faces and materials."""
    seen: set[str] = set()
    removed: list[str] = []
    for obj in tuple(builder.objects):
        if obj is None or obj.type != "MESH" or "_COLLIDER_" in obj.name:
            continue
        vertices = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
        face_signatures = []
        for polygon in obj.data.polygons:
            coordinates = tuple(sorted(
                tuple(round(float(value), 6) for value in vertices[index])
                for index in polygon.vertices
            ))
            material = (
                obj.data.materials[polygon.material_index].name
                if polygon.material_index < len(obj.data.materials)
                and obj.data.materials[polygon.material_index]
                else ""
            )
            face_signatures.append((coordinates, material))
        signature = hashlib.sha256(
            json.dumps(sorted(face_signatures), separators=(",", ":")).encode()
        ).hexdigest()
        if signature in seen:
            removed.append(obj.name)
            builder.objects.remove(obj)
            bpy.data.objects.remove(obj, do_unlink=True)
        else:
            seen.add(signature)
    if removed:
        print("[Gravitas duplicate cleanup] " + json.dumps(removed))
    return removed


def make_sectioned_glacis(name: str, stations, material):
    """Build a closed faceted armor shell from longitudinal cross-sections."""
    vertices = []
    ring_size = 8
    for y, half_width, z_bottom, z_top in stations:
        edge_bevel = min(0.17, (z_top - z_bottom) * 0.26)
        lower_chamfer = min(0.085, (z_top - z_bottom) * 0.14)
        section = [
            (-0.72 * half_width, z_bottom),
            (0.72 * half_width, z_bottom),
            (half_width, z_bottom + lower_chamfer),
            (0.96 * half_width, z_top - edge_bevel),
            (0.72 * half_width, z_top),
            (-0.72 * half_width, z_top),
            (-0.96 * half_width, z_top - edge_bevel),
            (-half_width, z_bottom + lower_chamfer),
        ]
        vertices.extend((x, y, z) for x, z in section)

    faces = [tuple(reversed(range(ring_size)))]
    end = (len(stations) - 1) * ring_size
    faces.append(tuple(end + index for index in range(ring_size)))
    for ring in range(len(stations) - 1):
        base = ring * ring_size
        next_base = (ring + 1) * ring_size
        for index in range(ring_size):
            following = (index + 1) % ring_size
            faces.append((base + index, base + following,
                          next_base + following, next_base + index))

    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(vertices, [], faces)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    mesh.materials.append(material)
    for polygon in mesh.polygons:
        polygon.use_smooth = False
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def build() -> None:
    run_preset_build(
        [
            "--name", "GravitasBreaker",
            "--preset", "gravitas_breaker",
            "--camo", "splinter",
            "--structural-wear-mask",
            "--thermal-radiators",
            "--suspension-arms",
            "--mantlet-boot",
            "--rig", "--animate",
            "--colliders", "--gameplay-json",
            "--bake-atlas", "--single-material", "--atlas-resolution", "2048",
            "--multiview",
            "--blend-path", "content/source-art/gravitas-breaker.blend",
            "--fbx-path", "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models/GravitasBreaker.fbx",
            "--preview-path", "artifacts/previews/GravitasBreaker.png",
            "--report-json", "artifacts/previews/GravitasBreaker-report.json",
        ],
        refine,
    )


if __name__ == "__main__":
    build()
