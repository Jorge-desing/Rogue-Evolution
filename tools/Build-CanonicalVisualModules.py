#!/usr/bin/env python3
"""Build the 35 Level 2 hardpoint modules from the canonical pack declarations.

Run with the repository's pinned Blender 4.5.14 executable. The source chassis
blend files are read only; this script writes one new module source blend, its
35 visual-only FBXs, and a manifest used by the Unity prefab/AssetBundle builder.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
import sys
from pathlib import Path
from typing import Any

import bpy
from mathutils import Euler, Vector


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import RogueModelKit as rmk  # noqa: E402
from UnitPbrPresetRegistry import UNIT_PBR_REGISTRY  # noqa: E402


PRESET_SOURCE = ROOT / "tools" / "Prepare-MaterialMakerTacticalTemplate.py"
SOURCE_BLEND = ROOT / "content" / "source-art" / "canonical-visual-modules.blend"
MODULE_ASSET_ROOT = "Assets/RogueCommandExpansionSample/VisualModules"
MODULE_FBX_ROOT = MODULE_ASSET_ROOT + "/Source"
MODULE_PREFAB_ROOT = MODULE_ASSET_ROOT + "/Prefabs"
MANIFEST_PATH = ROOT / "content" / "source-art" / "canonical-visual-modules.json"
ACTIVE_PACKS = (
    "sample-native-expansion",
    "tactical-operations",
)


def load_presets() -> dict[str, dict[str, Any]]:
    spec = importlib.util.spec_from_file_location("rcl_material_maker_presets", PRESET_SOURCE)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load Material Maker preset source: {PRESET_SOURCE}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.PRESETS


PRESETS = load_presets()


def normalized(value: str) -> str:
    return value.strip().lower().replace("_", "-")


def resolve_preset(variant_id: str, unit_profile: Any) -> str:
    local_id = variant_id.split(":", 1)[-1].lower()
    candidates = [unit_profile.primary_preset]
    candidates.extend(unit_profile.tactical_variant_presets.values())
    for preset in candidates:
        if local_id.endswith(normalized(preset)):
            return preset
    for alias, preset in unit_profile.tactical_variant_presets.items():
        if local_id.endswith(normalized(alias)):
            return preset
    raise ValueError(f"No registered Material Maker preset matches Level 2 variant {variant_id!r}")


def choose_slot(preset: str) -> str:
    name = normalized(preset)
    if any(token in name for token in ("minefield", "ordnance-carrier", "fuel-shroud")):
        return "FuelDrums"
    if any(token in name for token in (
        "radar", "phased-array", "magnetic-shield", "void-composite", "nanoweave",
        "sniper-camo", "radiation-shield",
    )):
        return "APS"
    if any(token in name for token in (
        "radiator", "reactor", "conduit", "thruster", "hyperdrive", "containment",
        "singularity", "borer", "repair", "industrial", "docking", "heavy-flak",
        "salvage-reclamation",
    )):
        return "Sponsons"
    return "ERA"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--force", action="store_true", help="Allow rebuilding only the generated canonical module outputs.")
    return parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])


def read_module_entries() -> list[dict[str, Any]]:
    entries: list[dict[str, Any]] = []
    for pack_name in ACTIVE_PACKS:
        pack_path = ROOT / "content" / "packs" / pack_name / "content.json"
        pack = json.loads(pack_path.read_text(encoding="utf-8"))
        pack_id = pack["packId"]
        bundle_map = {item["id"]: item["path"].split("/")[-1] for item in pack.get("assetBundles", [])}
        for unit in pack.get("units", []):
            unit_id = unit["id"]
            profile = UNIT_PBR_REGISTRY.get(unit_id)
            if profile is None:
                raise ValueError(f"No PBR profile is registered for active unit {unit_id!r}")
            variants = unit.get("visualVariants") or []
            for index, variant in enumerate(variants):
                if index == 0:
                    if variant.get("modules"):
                        raise ValueError(f"Level 1 primary variant cannot author modules: {variant['id']}")
                    continue

                preset = resolve_preset(variant["id"], profile)
                if preset not in PRESETS:
                    raise ValueError(f"Unknown Material Maker preset {preset!r} for {variant['id']}")
                unit_slug = unit_id.split(":", 1)[1].replace("-", "_")
                module_name = f"RCLModule_{unit_slug}_{preset}"
                fbx_path = f"{MODULE_FBX_ROOT}/{module_name}.fbx"
                prefab_path = f"{MODULE_PREFAB_ROOT}/{module_name}.prefab"
                fbx_file_path = f"unity/RogueCommandAssetAuthoring/{fbx_path}"
                bundle_id = unit.get("assetBundleId")
                bundle_name = bundle_map.get(bundle_id)
                if not bundle_name:
                    raise ValueError(f"Unit {unit_id} lacks a declared bundle for module {variant['id']}")
                entries.append({
                    "moduleName": module_name,
                    "packName": pack_name,
                    "packId": pack_id,
                    "unitId": unit_id,
                    "variantId": variant["id"],
                    "level": 2,
                    "preset": preset,
                    "slotId": choose_slot(preset),
                    "assetBundleId": bundle_id,
                    "assetBundleName": bundle_name,
                    "fbxAssetPath": fbx_path,
                    "fbxFilePath": fbx_file_path,
                    "prefabAssetPath": prefab_path,
                    # Unity serializes AssetBundle container keys in lowercase;
                    # runtime LoadAsset and bundle-reference validation use that exact key.
                    "assetName": prefab_path.lower(),
                    "texturePrefix": f"RCLModule_{preset}",
                })

    names = [entry["moduleName"].casefold() for entry in entries]
    if len(entries) != 35:
        raise ValueError(f"Expected 35 Level 2 visual modules for 53 variants, found {len(entries)}")
    if len(names) != len(set(names)):
        raise ValueError("Canonical visual module names must be unique across both active packs")
    return entries


def make_materials(entry: dict[str, Any]) -> tuple[bpy.types.Material, bpy.types.Material, bpy.types.Material]:
    preset_data = PRESETS[entry["preset"]]
    palette = preset_data.get("plate_palette") or ((0.0, (0.24, 0.28, 0.31)), (1.0, (0.38, 0.42, 0.46)))
    armor_color = tuple(float(channel) for channel in palette[-1][1])
    seam = preset_data.get("seam_tint") or ((0.0, (0.12, 0.15, 0.18)), (1.0, (0.20, 0.25, 0.30)))
    accent_color = tuple(float(channel) for channel in seam[-1][1])
    prefix = "RCLModuleMaterial__" + entry["preset"] + "__"
    armor = rmk.make_material(prefix + "PrimaryArmor", armor_color, metallic=0.74, roughness=0.34)
    gunmetal = rmk.make_material(prefix + "GunMetal", (0.13, 0.16, 0.19), metallic=0.91, roughness=0.27)
    accent = rmk.make_material(prefix + "Accent", accent_color, metallic=0.52, roughness=0.28,
                               emission_color=accent_color, emission_strength=0.18)
    return armor, gunmetal, accent


def build_armor_module(entry: dict[str, Any], armor: Any, gunmetal: Any, accent: Any, seed: int) -> list[bpy.types.Object]:
    prefix = entry["moduleName"]
    rows = 2 + seed % 2
    columns = 2 + (seed // 3) % 2
    width = 0.19 + (seed % 3) * 0.025
    depth = 0.13 + (seed % 2) * 0.02
    objects = rmk.make_era_armor_tiles(
        prefix,
        (0.0, 0.0, 0.015),
        rows=rows,
        cols=columns,
        tile_size=(width, depth, 0.065),
        spacing=0.025,
        slope_angle_deg=20.0 + (seed % 4) * 3.0,
        tile_mat=armor,
        bolt_mat=gunmetal,
    )
    rail_width = columns * width + (columns - 1) * 0.025 + 0.10
    rail_length = rows * depth + (rows - 1) * 0.025 + 0.06
    objects.append(rmk.make_box(prefix + "_CassetteBackplate", (rail_width, rail_length, 0.055),
                                (0.0, 0.0, -0.055), gunmetal, bevel=0.018))
    for x_sign in (-1.0, 1.0):
        objects.append(rmk.make_rod(prefix + f"_RetentionRail_{'L' if x_sign < 0 else 'R'}",
                                    (x_sign * rail_width * 0.47, -rail_length * 0.38, 0.005),
                                    (x_sign * rail_width * 0.47, rail_length * 0.38, 0.005),
                                    0.018, accent if seed % 5 == 0 else gunmetal, vertices=8))
    return objects


def build_aps_module(entry: dict[str, Any], armor: Any, gunmetal: Any, accent: Any, seed: int) -> list[bpy.types.Object]:
    prefix = entry["moduleName"]
    side = -1.0 if seed % 2 else 1.0
    objects = rmk.make_aps_hardkill_system(prefix, (0.0, 0.0, 0.0), side=side,
                                           pod_mat=armor, radar_face_mat=accent, barrel_mat=gunmetal)
    objects.append(rmk.make_box(prefix + "_SensorArmorHood", (0.34, 0.24, 0.07),
                                (0.0, 0.015, 0.22), armor, bevel=0.022))
    objects.append(rmk.make_torus(prefix + "_SensorGimbalRing", (0.0, 0.02, 0.20),
                                  major_radius=0.19 + (seed % 3) * 0.012,
                                  minor_radius=0.018, material=gunmetal,
                                  rotation=(0.0, 0.0, 0.0), major_segments=16, minor_segments=6))
    if "magnetic-shield" in normalized(entry["preset"]) or "phased-array" in normalized(entry["preset"]):
        for y in (-0.17, 0.17):
            objects.append(rmk.make_box(prefix + f"_ArrayFacet_{'A' if y < 0 else 'B'}",
                                        (0.11, 0.06, 0.13), (side * 0.25, y, 0.08), accent, bevel=0.008))
    return objects


def build_fuel_or_ordnance_module(entry: dict[str, Any], armor: Any, gunmetal: Any, accent: Any, seed: int) -> list[bpy.types.Object]:
    prefix = entry["moduleName"]
    preset = normalized(entry["preset"])
    radius = 0.16 + (seed % 3) * 0.018
    objects = rmk.make_aux_fuel_drums(prefix, (0.0, 0.0, 0.0),
                                      rear_width=0.78 + (seed % 2) * 0.12,
                                      radius=radius,
                                      length=0.58 + (seed % 3) * 0.08,
                                      drum_mat=armor, mount_mat=gunmetal, strap_mat=accent)
    objects.append(rmk.make_box(prefix + "_TwinDrumCarrier", (0.92, 0.24, 0.30),
                                (0.0, 0.0, -radius * 0.80), gunmetal, bevel=0.035))
    if "ordnance-carrier" in preset or "minefield" in preset:
        for index in range(3):
            y = -0.22 + index * 0.22
            objects.append(rmk.make_cylinder(prefix + f"_SealedOrdnanceCanister_{index + 1}",
                                             radius=0.07, depth=0.38,
                                             location=(0.0, y, radius + 0.14),
                                             material=armor, rotation=(math.pi / 2.0, 0.0, 0.0),
                                             vertices=12, bevel=0.009))
        objects.append(rmk.make_rod(prefix + "_ReleaseManifold_L", (-0.40, -0.27, radius + 0.15),
                                    (-0.40, 0.27, radius + 0.15), 0.018, accent))
        objects.append(rmk.make_rod(prefix + "_ReleaseManifold_R", (0.40, -0.27, radius + 0.15),
                                    (0.40, 0.27, radius + 0.15), 0.018, accent))
    else:
        for x in (-0.24, 0.24):
            objects.append(rmk.make_box(prefix + f"_ThermalShroud_{'L' if x < 0 else 'R'}",
                                        (0.08, 0.60, radius * 1.70),
                                        (x, 0.0, 0.0), armor, bevel=0.012))
    return objects


def build_drill_module(entry: dict[str, Any], armor: Any, gunmetal: Any, accent: Any, seed: int) -> list[bpy.types.Object]:
    prefix = entry["moduleName"]
    objects = [
        rmk.make_box(prefix + "_TorqueHousing", (0.62, 0.50, 0.48), (0.0, -0.05, 0.0), armor, bevel=0.045),
        rmk.make_box(prefix + "_ReducerCover", (0.44, 0.28, 0.34), (0.0, -0.22, 0.0), gunmetal, bevel=0.028),
        rmk.make_tapered_cylinder(prefix + "_CarbidePilotBit", 0.14, 0.045, 0.72,
                                  (0.0, 0.43, 0.0), gunmetal,
                                  rotation=(-math.pi / 2.0, 0.0, 0.0), vertices=12, bevel=0.008),
    ]
    for index, y in enumerate((0.16, 0.30, 0.44)):
        objects.append(rmk.make_torus(prefix + f"_AugerFlight_{index + 1}", (0.0, y, 0.0),
                                      major_radius=0.19 - index * 0.035,
                                      minor_radius=0.035, material=armor if index != 1 else accent,
                                      rotation=(math.pi / 2.0, 0.0, 0.0), major_segments=12, minor_segments=6))
    for x in (-0.23, 0.23):
        objects.append(rmk.make_rod(prefix + f"_VibrationDamper_{'L' if x < 0 else 'R'}",
                                    (x, -0.25, -0.16), (x, 0.08, -0.16), 0.035, gunmetal))
    return objects


def build_power_or_radiator_module(entry: dict[str, Any], armor: Any, gunmetal: Any, accent: Any, seed: int) -> list[bpy.types.Object]:
    prefix = entry["moduleName"]
    preset = normalized(entry["preset"])
    if "repair" in preset or "chitin" in preset:
        return rmk.make_nanite_repair_dispenser_assembly(
            prefix, (0.0, 0.0, 0.0), scale=0.62,
            canister_radius=0.14, canister_height=0.40 + (seed % 2) * 0.05,
            spacing_x=0.60, canister_mat=armor, frame_mat=gunmetal,
            glow_mat=accent, hose_mat=gunmetal,
        )
    if "radiator" in preset or "thruster" in preset or "solar" in preset:
        return rmk.make_thermal_radiator_assembly(
            prefix, hull_length=0.92, hull_width=0.82,
            deck_z=0.0, fin_count=5 + seed % 3,
            housing_mat=armor, fin_mat=gunmetal, glow_mat=accent, pipe_mat=gunmetal,
        )
    if "heavy-flak" in preset:
        return rmk.make_quad_flak_autocannon_assembly(
            prefix, (0.0, 0.0, 0.02), direction=(0.0, 1.0, 0.08),
            barrel_length=0.62, caliber_radius=0.032,
            spacing_x=0.22, spacing_z=0.18,
            cradle_mat=armor, barrel_mat=gunmetal, mag_mat=gunmetal, accent_mat=accent,
        )

    base = rmk.make_box(prefix + "_PowerCellArmorCase", (0.68, 0.62, 0.46),
                        (0.0, -0.02, 0.0), armor, bevel=0.05)
    objects = [base]
    objects.append(rmk.make_box(prefix + "_ServicePanel", (0.46, 0.08, 0.26),
                                (0.0, 0.31, 0.0), gunmetal, bevel=0.014))
    ring_count = 2 + seed % 2
    for index in range(ring_count):
        y = -0.20 + index * 0.20
        objects.append(rmk.make_torus(prefix + f"_InductionCoil_{index + 1}", (0.0, y, 0.05),
                                      major_radius=0.24, minor_radius=0.032,
                                      material=accent if index == ring_count - 1 else gunmetal,
                                      rotation=(math.pi / 2.0, 0.0, 0.0), major_segments=16, minor_segments=8))
    for x_sign in (-1.0, 1.0):
        objects.append(rmk.make_rod(prefix + f"_CoolantFeed_{'L' if x_sign < 0 else 'R'}",
                                    (x_sign * 0.26, 0.31, -0.13),
                                    (x_sign * 0.34, -0.32, -0.13),
                                    0.028, gunmetal, vertices=8))
        objects.append(rmk.make_box(prefix + f"_ContainmentBrace_{'L' if x_sign < 0 else 'R'}",
                                    (0.075, 0.48, 0.38), (x_sign * 0.35, -0.02, 0.0), armor, bevel=0.014))
    return objects


def build_utility_sponson_module(entry: dict[str, Any], armor: Any, gunmetal: Any, accent: Any, seed: int) -> list[bpy.types.Object]:
    prefix = entry["moduleName"]
    preset = normalized(entry["preset"])
    if "docking" in preset:
        objects = [rmk.make_box(prefix + "_ClampRoot", (0.62, 0.48, 0.36), (0.0, 0.0, 0.0), armor, bevel=0.035)]
        for side in (-1.0, 1.0):
            tag = "L" if side < 0 else "R"
            objects.append(rmk.make_rod(prefix + f"_DockingActuator_{tag}",
                                        (side * 0.24, -0.12, 0.0), (side * 0.44, 0.28, 0.08), 0.055, gunmetal))
            objects.append(rmk.make_box(prefix + f"_CaptureJaw_{tag}", (0.18, 0.20, 0.30),
                                        (side * 0.45, 0.34, 0.08), armor, bevel=0.025))
            objects.append(rmk.make_cylinder(prefix + f"_LatchPin_{tag}", 0.045, 0.20,
                                             (side * 0.45, 0.34, 0.08), accent,
                                             rotation=(0.0, math.pi / 2.0, 0.0), vertices=10, bevel=0.004))
        return objects

    objects = rmk.make_side_sponson_assembly(
        prefix, (0.0, 0.0, 0.0), side=1,
        size=(0.38 + (seed % 2) * 0.06, 0.74, 0.42 + (seed % 3) * 0.04),
        armor_mat=armor, strap_mat=gunmetal, accent_mat=accent,
    )
    objects.append(rmk.make_box(prefix + "_ServiceAccessHatch", (0.24, 0.25, 0.06),
                                (0.0, 0.0, 0.24), armor, bevel=0.014))
    for side in (-1.0, 1.0):
        objects.append(rmk.make_rod(prefix + f"_MountingStrut_{'L' if side < 0 else 'R'}",
                                    (side * 0.15, -0.27, -0.18), (side * 0.20, 0.27, -0.18),
                                    0.025, gunmetal))
    return objects


def build_module_geometry(entry: dict[str, Any], seed: int) -> tuple[list[bpy.types.Object], list[bpy.types.Material]]:
    armor, gunmetal, accent = make_materials(entry)
    slot = entry["slotId"]
    preset = normalized(entry["preset"])
    if slot == "ERA":
        objects = build_armor_module(entry, armor, gunmetal, accent, seed)
    elif slot == "APS":
        objects = build_aps_module(entry, armor, gunmetal, accent, seed)
    elif slot == "FuelDrums":
        objects = build_fuel_or_ordnance_module(entry, armor, gunmetal, accent, seed)
    elif "borer" in preset:
        objects = build_drill_module(entry, armor, gunmetal, accent, seed)
    elif any(token in preset for token in (
        "radiator", "thruster", "solar", "repair", "chitin", "heavy-flak",
        "conduit", "reactor", "hyperdrive", "containment", "singularity", "arc-discharge",
    )):
        objects = build_power_or_radiator_module(entry, armor, gunmetal, accent, seed)
    else:
        objects = build_utility_sponson_module(entry, armor, gunmetal, accent, seed)

    return objects, [armor]


def ensure_uv(obj: bpy.types.Object) -> None:
    if obj.type != "MESH" or obj.data.uv_layers:
        return
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.018)
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.select_set(False)


def place_in_collection(objects: list[bpy.types.Object], collection: bpy.types.Collection) -> None:
    for obj in objects:
        for previous in list(obj.users_collection):
            previous.objects.unlink(obj)
        collection.objects.link(obj)


def build_all(entries: list[dict[str, Any]], force: bool) -> None:
    outputs = [SOURCE_BLEND]
    outputs.extend(ROOT / entry["fbxFilePath"] for entry in entries)
    if not force:
        existing = [str(path) for path in outputs if path.exists()]
        if existing:
            raise FileExistsError("Generated canonical module outputs already exist; rerun with --force: " + ", ".join(existing[:8]))

    rmk.clear_scene()
    root_collection = bpy.context.scene.collection
    module_rows: list[tuple[dict[str, Any], list[bpy.types.Object]]] = []
    for index, entry in enumerate(entries):
        seed = int(hashlib.sha256(entry["variantId"].encode("utf-8")).hexdigest()[:8], 16)
        module_collection = bpy.data.collections.new(entry["moduleName"])
        root_collection.children.link(module_collection)
        created, armor_materials = build_module_geometry(entry, seed)
        if not created:
            raise RuntimeError(f"No geometry generated for {entry['variantId']}")
        for obj in created:
            ensure_uv(obj)
            rmk.apply_weighted_normals(obj, weight=60, keep_sharp=True, apply_modifier=True)
        rmk.bake_advanced_vertex_shading(
            created,
            wear_level="field_tested",
            soot_intensity=0.0,
            team_mask_materials=armor_materials,
            structural_wear_mask=True,
        )
        combined = rmk.batch_and_join_by_material(
            created,
            prefix=entry["moduleName"],
            smooth_by_angle=True,
            weighted_normals=True,
            unity_batch_names=True,
        )
        if not combined:
            raise RuntimeError(f"Material batching produced no meshes for {entry['variantId']}")
        place_in_collection(combined, module_collection)
        module_rows.append((entry, combined))
        print(f"[{index + 1:02d}/{len(entries):02d}] {entry['variantId']} -> {entry['slotId']} ({len(combined)} material batch(es))", flush=True)

    SOURCE_BLEND.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE_BLEND))

    for entry, objects in module_rows:
        fbx_path = ROOT / entry["fbxFilePath"]
        fbx_path.parent.mkdir(parents=True, exist_ok=True)
        rmk.export_unity_fbx(fbx_path, objects, include_empties=False, include_armature=False, bake_anim=False)
        if not fbx_path.is_file() or fbx_path.stat().st_size <= 0:
            raise RuntimeError(f"Blender did not export a non-empty FBX: {fbx_path}")

    manifest = {
        "schemaVersion": 1,
        "sourceBlend": SOURCE_BLEND.relative_to(ROOT).as_posix(),
        "level1Authority": "Existing unit chassis geometry, functional hierarchy, rig, pivots, canonical sockets, LOD and PBR atlas.",
        "level2Contract": "Each listed GameObject is visual-only geometry attached to exactly one canonical hardpoint group; no gameplay components or balance changes.",
        "modules": entries,
    }
    MANIFEST_PATH.parent.mkdir(parents=True, exist_ok=True)
    MANIFEST_PATH.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Saved {len(entries)} modules, source blend, and manifest: {MANIFEST_PATH}", flush=True)


def main() -> None:
    args = parse_args()
    entries = read_module_entries()
    build_all(entries, args.force)


if __name__ == "__main__":
    main()
