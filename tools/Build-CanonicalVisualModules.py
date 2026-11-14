#!/usr/bin/env python3
"""Build declared Level 2 hardpoint modules from the canonical pack declarations.

Run with the repository's pinned Blender 4.5.14 executable. The source chassis
blend files are read only; this script writes one new module source blend, its
visual-only FBXs, and a manifest used by the Unity prefab/AssetBundle builder.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import inspect
import json
import math
import sys
from pathlib import Path
from typing import Any, Callable, get_type_hints

import bpy
from mathutils import Euler, Vector


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / "tools"))
import RogueModelKit as rmk  # noqa: E402
import functional_engineering_integration as fei  # noqa: E402
import inspect_fbx_geometry as fbx_inspector  # noqa: E402
from canonical_module_inventory_staging import (  # noqa: E402
    InventoryCapacityGate,
    InventoryStageContext,
    stage_candidate_inventory,
    validate_candidate_export_path,
    validate_candidate_triangle_count,
    verify_gate_unchanged,
)
from UnitPbrPresetRegistry import UNIT_PBR_REGISTRY  # noqa: E402
from hard_surface_geometry_presets import (  # noqa: E402
    COAXIAL_SERVICE_CONNECTOR_PRESET_ID,
    ERA_BRICK_PRESET_ID,
    GEOMETRY_PRESETS,
    PRESET_METADATA,
    SERVICE_HATCH_PRESET_ID,
    EraBrickConfig,
    CoaxialServiceConnectorConfig,
    ServiceHatchConfig,
)


PRESET_SOURCE = ROOT / "tools" / "Prepare-MaterialMakerTacticalTemplate.py"
SOURCE_BLEND = ROOT / "content" / "source-art" / "canonical-visual-modules.blend"
MODULE_ASSET_ROOT = "Assets/RogueCommandExpansionSample/VisualModules"
MODULE_FBX_ROOT = MODULE_ASSET_ROOT + "/Source"
MODULE_PREFAB_ROOT = MODULE_ASSET_ROOT + "/Prefabs"
MANIFEST_PATH = ROOT / "content" / "source-art" / "canonical-visual-modules.json"
ROLE_REGISTRY_PATH = ROOT / "content" / "source-art" / "canonical-visual-module-roles.json"
BUDGET_PROFILE_PATH = ROOT / "content" / "source-art" / "model-budget-profile.json"
GEOMETRY_BUILD_POLICY_PATH = ROOT / "content" / "source-art" / "canonical-visual-module-build-policy.json"
ACTIVE_PACKS = (
    "sample-native-expansion",
    "tactical-operations",
)
CANONICAL_SLOTS = {"ERA", "APS", "Sponsons", "FuelDrums"}


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


def load_visual_engineering_roles() -> tuple[
    dict[str, dict[str, Any]],
    dict[tuple[str, str], dict[str, Any]],
    dict[str, dict[str, Any]],
    dict[str, dict[str, Any]],
    dict[str, Any],
]:
    registry = json.loads(ROLE_REGISTRY_PATH.read_text(encoding="utf-8"))
    definitions = registry.get("roleDefinitions")
    rows = registry.get("variantRoles")
    preset_rows = registry.get("geometryPresetBindings")
    layout_rows = registry.get("geometryLayouts")
    if (registry.get("schemaVersion") != 2 or not isinstance(definitions, dict) or
            not isinstance(rows, list) or not isinstance(preset_rows, list) or not isinstance(layout_rows, list)):
        raise ValueError(f"Visual engineering role registry is incomplete: {ROLE_REGISTRY_PATH}")

    preset_bindings: dict[str, dict[str, Any]] = {}
    for binding in preset_rows:
        preset_id = binding.get("geometryPresetId")
        slot_id = binding.get("slotId")
        engineering_role = binding.get("geometryEngineeringRole")
        visual_roles = binding.get("visualEngineeringRoles")
        metadata = PRESET_METADATA.get(preset_id)
        if (not preset_id or preset_id in preset_bindings or preset_id not in GEOMETRY_PRESETS or
                not metadata or slot_id != metadata.get("slot") or
                engineering_role != metadata.get("engineeringRole") or
                not isinstance(visual_roles, list) or not visual_roles):
            raise ValueError(f"Invalid or stale hard-surface geometry preset binding: {binding}")
        allowed_variants = binding.get("allowedVariantIds", [])
        if not isinstance(allowed_variants, list) or any(not isinstance(value, str) or not value for value in allowed_variants):
            raise ValueError(f"Invalid allowedVariantIds for geometry preset {preset_id!r}")
        for visual_role in visual_roles:
            definition = definitions.get(visual_role)
            if (definition is None or slot_id not in (definition.get("allowedSlots") or [])):
                raise ValueError(
                    f"Geometry preset {preset_id!r} cannot be assigned to role {visual_role!r} at {slot_id!r}"
                )
        preset_bindings[preset_id] = binding
    missing_presets = sorted(set(GEOMETRY_PRESETS) - set(preset_bindings))
    unknown_presets = sorted(set(preset_bindings) - set(GEOMETRY_PRESETS))
    if missing_presets or unknown_presets:
        raise ValueError(
            "Geometry preset contract must cover the complete factory catalog; "
            f"missing={missing_presets}, unknown={unknown_presets}"
        )

    layout_bindings: dict[str, dict[str, Any]] = {}
    for layout in layout_rows:
        layout_id = layout.get("geometryLayoutId")
        required_preset = layout.get("requiredGeometryPresetId")
        role = layout.get("visualEngineeringRole")
        slot = layout.get("slotId")
        if (not layout_id or layout_id in layout_bindings or required_preset not in preset_bindings or
                role not in definitions or slot not in definitions[role].get("allowedSlots", [])):
            raise ValueError(f"Invalid canonical geometry layout binding: {layout}")
        preset_binding = preset_bindings[required_preset]
        if slot != preset_binding["slotId"] or role not in preset_binding["visualEngineeringRoles"]:
            raise ValueError(f"Geometry layout {layout_id!r} is incompatible with {required_preset!r}")
        layout_bindings[layout_id] = layout

    roles_by_variant_slot: dict[tuple[str, str], dict[str, Any]] = {}
    for row in rows:
        variant_id = row.get("variantId")
        slot_id = row.get("slotId")
        role = row.get("visualEngineeringRole")
        if not variant_id or slot_id not in CANONICAL_SLOTS or role not in definitions:
            raise ValueError(f"Invalid visual engineering role declaration: {row}")
        key = (variant_id, slot_id)
        if key in roles_by_variant_slot:
            raise ValueError(f"Duplicate visual engineering role declaration: {variant_id} at {slot_id}")
        allowed_slots = definitions[role].get("allowedSlots") or []
        if slot_id not in allowed_slots:
            raise ValueError(f"Role {role!r} cannot mount at {slot_id!r}: {variant_id}")
        preset_id = row.get("geometryPreset")
        binding = preset_bindings.get(preset_id)
        if (binding is None or slot_id != binding["slotId"] or role not in binding["visualEngineeringRoles"]):
            raise ValueError(f"Level 2 module has no compatible reusable geometry preset: {variant_id} at {slot_id}")
        allowed_variants = binding.get("allowedVariantIds") or []
        if allowed_variants and variant_id not in allowed_variants:
            raise ValueError(f"Geometry preset {preset_id!r} is not authorized for {variant_id}")
        layout = row.get("geometryLayout", "")
        layout_binding = layout_bindings.get(layout) if layout else None
        if layout and (layout_binding is None or layout_binding.get("variantId") != variant_id or
                       layout_binding.get("slotId") != slot_id or
                       layout_binding.get("visualEngineeringRole") != role or
                       layout_binding.get("requiredGeometryPresetId") != preset_id):
            raise ValueError(f"Geometry layout {layout!r} is not compatible with {variant_id} at {slot_id}")
        expected_layout = next((item for item in layout_rows
                                if item.get("variantId") == variant_id and item.get("slotId") == slot_id), None)
        if expected_layout and layout != expected_layout.get("geometryLayoutId"):
            raise ValueError(f"Variant {variant_id} at {slot_id} must use layout {expected_layout['geometryLayoutId']!r}")
        roles_by_variant_slot[key] = row

    return definitions, roles_by_variant_slot, preset_bindings, layout_bindings, registry


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--force", action="store_true", help="Allow rebuilding only the generated canonical module outputs.")
    parser.add_argument(
        "--inventory-staging",
        type=Path,
        help="Write candidate Level 2 source FBXs and inventory only under a new artifacts/ directory.",
    )
    parser.add_argument(
        "--capacity-evidence",
        type=Path,
        help="Legacy argument; accepted but not used as a build gate or calibration evidence.",
    )
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    if args.capacity_evidence and not args.inventory_staging:
        parser.error("--capacity-evidence requires --inventory-staging")
    if args.inventory_staging and args.force:
        parser.error("--force is not available for staging-only candidate inventory")
    return args


def read_module_entries() -> list[dict[str, Any]]:
    entries: list[dict[str, Any]] = []
    role_definitions, roles_by_variant_slot, _, _, _ = load_visual_engineering_roles()
    seen_roles: set[tuple[str, str]] = set()
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
                module_declarations = variant.get("modules") or []
                if not isinstance(module_declarations, list):
                    raise ValueError(f"Level 2 modules must be an array: {variant['id']}")
                preset = resolve_preset(variant["id"], profile)
                if preset not in PRESETS:
                    raise ValueError(f"Unknown Material Maker preset {preset!r} for {variant['id']}")

                for module_index, declaration in enumerate(module_declarations):
                    if not isinstance(declaration, dict):
                        raise ValueError(f"Invalid module declaration {module_index} on {variant['id']}")
                    slot_id = declaration.get("slotId")
                    asset_name = declaration.get("assetName")
                    if slot_id not in CANONICAL_SLOTS or not asset_name:
                        raise ValueError(f"Level 2 module requires a canonical slot and assetName: {variant['id']}")
                    role_key = (variant["id"], slot_id)
                    role_row = roles_by_variant_slot.get(role_key)
                    if role_row is None:
                        raise ValueError(f"No explicit visual engineering role for {variant['id']} at {slot_id}")
                    visual_role = role_row["visualEngineeringRole"]
                    seen_roles.add(role_key)
                    geometry_preset = role_row["geometryPreset"]
                    geometry_layout = role_row.get("geometryLayout", "")

                    unit_slug = unit_id.split(":", 1)[1].replace("-", "_")
                    module_suffix = f"_{slot_id.lower()}" if len(module_declarations) > 1 else ""
                    module_name = f"RCLModule_{unit_slug}_{preset}{module_suffix}"
                    fbx_path = f"{MODULE_FBX_ROOT}/{module_name}.fbx"
                    lod_fbx_path = f"{MODULE_FBX_ROOT}/{module_name}_LODs.fbx"
                    prefab_path = f"{MODULE_PREFAB_ROOT}/{module_name}.prefab"
                    fbx_file_path = f"unity/RogueCommandAssetAuthoring/{fbx_path}"
                    lod_fbx_file_path = f"unity/RogueCommandAssetAuthoring/{lod_fbx_path}"
                    expected_asset_name = prefab_path.lower()
                    if asset_name.casefold() != expected_asset_name.casefold():
                        raise ValueError(
                            f"Declared Level 2 assetName must match the generated module path for "
                            f"{variant['id']} at {slot_id}: {asset_name!r} != {expected_asset_name!r}"
                        )

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
                        "geometryPreset": geometry_preset,
                        "geometryLayout": geometry_layout,
                        "slotId": slot_id,
                        "visualEngineeringRole": visual_role,
                        "assetBundleId": bundle_id,
                        "assetBundleName": bundle_name,
                        "fbxAssetPath": fbx_path,
                        "fbxFilePath": fbx_file_path,
                        "lodFbxAssetPath": lod_fbx_path,
                        "lodFbxFilePath": lod_fbx_file_path,
                        "prefabAssetPath": prefab_path,
                        # Unity serializes AssetBundle container keys in lowercase;
                        # runtime LoadAsset and bundle-reference validation use that exact key.
                        "assetName": expected_asset_name,
                        "texturePrefix": f"RCLModule_{preset}",
                    })

    names = [entry["moduleName"].casefold() for entry in entries]
    missing_roles = sorted(set(roles_by_variant_slot) - seen_roles)
    if missing_roles:
        raise ValueError(f"Visual engineering roles do not match declared pack modules; unused roles: {missing_roles}")
    if not entries:
        raise ValueError("The active packs declare no Level 2 visual modules")
    if len(names) != len(set(names)):
        raise ValueError("Canonical visual module names must be unique across both active packs")
    for entry in entries:
        role = entry["visualEngineeringRole"]
        if not role_definitions[role].get("visibleAssembly"):
            raise ValueError(f"Visual engineering role lacks a form description: {role}")
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


HardflowBoolean = Callable[[bpy.types.Object, bpy.types.Object], str]
HardflowBevel = Callable[[bpy.types.Object], None]


def build_armor_module(
    entry: dict[str, Any], armor: Any, gunmetal: Any, accent: Any, seed: int,
    *, hardflow_boolean_apply: HardflowBoolean | None = None,
    hardflow_bevel: HardflowBevel | None = None,
) -> list[bpy.types.Object]:
    prefix = entry["moduleName"]
    if entry.get("geometryPreset") != ERA_BRICK_PRESET_ID:
        raise ValueError(f"ERA role requires {ERA_BRICK_PRESET_ID}: {entry['variantId']}")
    spacing = 0.025
    if entry.get("geometryLayout") == "bulwark_side_skirt_array_v2":
        # Anchor to the flat support face in the current Level 1 Unity FBX.
        # Five cassettes per side span the supported skirt from front to rear.
        # The current Level 1 FBX support face is x=+/-1.312 m; the moving
        # tread reaches x=+/-1.250 m. The full cassette stack is separately
        # regression-checked for at least 35 mm of belt clearance.
        side_positions_y = (-1.10, -0.55, 0.0, 0.55, 1.10)
        positions = [(x_sign * 1.312, y, 0.40)
                     for x_sign in (-1.0, 1.0) for y in side_positions_y]
        dimensions = (0.24, 0.12, 0.045)
        normals = [(x_sign, 0.0, 0.0)
                   for x_sign in (-1.0, 1.0) for _ in side_positions_y]
    else:
        rows = 2 + seed % 2
        columns = 2 + (seed // 3) % 2
        width = 0.19 + (seed % 3) * 0.020
        height = 0.125 + (seed % 2) * 0.015
        step_x = width + spacing
        step_y = height + spacing
        positions = [((column - (columns - 1) * 0.5) * step_x,
                      (row - (rows - 1) * 0.5) * step_y, 0.0)
                     for row in range(rows) for column in range(columns)]
        dimensions = (width, height, 0.052)
        normals = [(0.0, 0.0, 1.0) for _ in positions]
    objects: list[bpy.types.Object] = []
    brick_family = hashlib.sha256(entry["variantId"].encode("utf-8")).hexdigest()[:8]
    for index, (center, normal) in enumerate(zip(positions, normals), 1):
        objects.extend(GEOMETRY_PRESETS[ERA_BRICK_PRESET_ID](
            prefix=f"ERABrick_{brick_family}_B{index:02d}",
            config=EraBrickConfig(center=center, normal=normal, size=dimensions),
            armor_material=armor,
            hardware_material=gunmetal,
            hardflow_boolean_apply=hardflow_boolean_apply,
            hardflow_bevel=hardflow_bevel,
        ))
    return objects


def build_generic_geometry_preset(
    entry: dict[str, Any], armor: Any, gunmetal: Any, accent: Any,
    *, hardflow_boolean_apply: HardflowBoolean | None = None,
    hardflow_bevel: HardflowBevel | None = None,
) -> list[bpy.types.Object]:
    """Dispatch a catalogued preset through its own config dataclass and factory."""
    preset_id = entry["geometryPreset"]
    creator = GEOMETRY_PRESETS.get(preset_id)
    if creator is None:
        raise ValueError(f"No hard-surface factory is registered for {preset_id!r}")
    config_type = get_type_hints(creator).get("config")
    if config_type is None:
        raise TypeError(f"Hard-surface factory {creator.__name__} has no typed config contract")
    try:
        config = config_type()
    except TypeError as exc:
        raise TypeError(
            f"Hard-surface preset {preset_id!r} needs an explicit mount recipe before it can be assigned"
        ) from exc

    parameters = inspect.signature(creator).parameters
    kwargs: dict[str, Any] = {
        "prefix": entry["moduleName"],
        "config": config,
        "armor_material": armor,
        "hardware_material": gunmetal,
    }
    optional_values = {
        "accent_material": accent,
        "hardflow_boolean_apply": hardflow_boolean_apply,
        "hardflow_bevel": hardflow_bevel,
    }
    kwargs.update({key: value for key, value in optional_values.items() if key in parameters})
    objects = creator(**kwargs)
    if not objects:
        raise RuntimeError(f"Hard-surface preset {preset_id!r} produced no geometry")
    for obj in objects:
        obj["rcl_hard_surface_geometry_preset"] = preset_id
        obj["rcl_canonical_visual_engineering_role"] = entry["visualEngineeringRole"]
        obj["rcl_canonical_hardpoint_slot"] = entry["slotId"]
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
    if entry["visualEngineeringRole"] == "aps_magnetic_shield":
        for y in (-0.17, 0.17):
            objects.append(rmk.make_box(prefix + f"_ArrayFacet_{'A' if y < 0 else 'B'}",
                                        (0.11, 0.06, 0.13), (side * 0.25, y, 0.08), accent, bevel=0.008))
    return objects


def build_fuel_or_ordnance_module(entry: dict[str, Any], armor: Any, gunmetal: Any, accent: Any, seed: int) -> list[bpy.types.Object]:
    prefix = entry["moduleName"]
    role = entry["visualEngineeringRole"]
    radius = 0.16 + (seed % 3) * 0.018
    objects = rmk.make_aux_fuel_drums(prefix, (0.0, 0.0, 0.0),
                                      rear_width=0.78 + (seed % 2) * 0.12,
                                      radius=radius,
                                      length=0.58 + (seed % 3) * 0.08,
                                      drum_mat=armor, mount_mat=gunmetal, strap_mat=accent)
    objects.append(rmk.make_box(prefix + "_TwinDrumCarrier", (0.92, 0.24, 0.30),
                                (0.0, 0.0, -radius * 0.80), gunmetal, bevel=0.035))
    if role == "ordnance_carrier":
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


def build_engineering_sponson_module(
    entry: dict[str, Any], armor: Any, gunmetal: Any, accent: Any, seed: int,
    *, hardflow_boolean_apply: HardflowBoolean | None = None,
    hardflow_bevel: HardflowBevel | None = None,
) -> list[bpy.types.Object]:
    prefix = entry["moduleName"]
    role = entry["visualEngineeringRole"]
    if role == "repair_dispenser":
        return rmk.make_nanite_repair_dispenser_assembly(
            prefix, (0.0, 0.0, 0.0), scale=0.62,
            canister_radius=0.14, canister_height=0.40 + (seed % 2) * 0.05,
            spacing_x=0.60, canister_mat=armor, frame_mat=gunmetal,
            glow_mat=accent, hose_mat=gunmetal,
        )
    if role == "thermal_radiator":
        return rmk.make_thermal_radiator_assembly(
            prefix, hull_length=0.92, hull_width=0.82,
            deck_z=0.0, fin_count=5 + seed % 3,
            housing_mat=armor, fin_mat=gunmetal, glow_mat=accent, pipe_mat=gunmetal,
        )
    if role == "heavy_flak_battery":
        return rmk.make_quad_flak_autocannon_assembly(
            prefix, (0.0, 0.0, 0.02), direction=(0.0, 1.0, 0.08),
            barrel_length=0.62, caliber_radius=0.032,
            spacing_x=0.22, spacing_z=0.18,
            cradle_mat=armor, barrel_mat=gunmetal, mag_mat=gunmetal, accent_mat=accent,
        )

    if role != "power_containment":
        raise ValueError(f"Unsupported engineering sponson role {role!r} for {entry['variantId']}")

    base = rmk.make_box(prefix + "_PowerCellArmorCase", (0.68, 0.62, 0.46),
                        (0.0, -0.02, 0.0), armor, bevel=0.05)
    base["rcl_canonical_component_role"] = "gravitas_power_containment_case"
    objects = [base]
    service_panel = rmk.make_box(prefix + "_ServicePanel", (0.46, 0.08, 0.26),
                                 (0.0, 0.31, 0.0), gunmetal, bevel=0.014)
    service_panel["rcl_canonical_component_role"] = "gravitas_power_containment_service_panel"
    objects.append(service_panel)
    ring_count = 2 + seed % 2
    for index in range(ring_count):
        y = -0.20 + index * 0.20
        objects.append(rmk.make_torus(prefix + f"_InductionCoil_{index + 1}", (0.0, y, 0.05),
                                      major_radius=0.24, minor_radius=0.032,
                                      material=accent if index == ring_count - 1 else gunmetal,
                                      rotation=(math.pi / 2.0, 0.0, 0.0), major_segments=16, minor_segments=8))
    for x_sign in (-1.0, 1.0):
        coolant_feed = rmk.make_rod(prefix + f"_CoolantFeed_{'L' if x_sign < 0 else 'R'}",
                                    (x_sign * 0.26, 0.31, -0.13),
                                    (x_sign * 0.34, -0.32, -0.13),
                                    0.028, gunmetal, vertices=8)
        coolant_feed["rcl_canonical_component_role"] = "gravitas_power_containment_coolant_feed"
        objects.append(coolant_feed)
        containment_brace = rmk.make_box(prefix + f"_ContainmentBrace_{'L' if x_sign < 0 else 'R'}",
                                         (0.075, 0.48, 0.38), (x_sign * 0.35, -0.02, 0.0), armor, bevel=0.014)
        containment_brace["rcl_canonical_component_role"] = "gravitas_power_containment_brace"
        objects.append(containment_brace)
    if entry.get("geometryPreset") == COAXIAL_SERVICE_CONNECTOR_PRESET_ID:
        if (entry.get("variantId") != "tactical-operations:gravitas-breaker-quantum-hyperdrive-alloy" or
                role != "power_containment" or entry.get("slotId") != "Sponsons"):
            raise ValueError(f"{COAXIAL_SERVICE_CONNECTOR_PRESET_ID} is reserved for the Gravitas power module")
        connector_objects = GEOMETRY_PRESETS[COAXIAL_SERVICE_CONNECTOR_PRESET_ID](
            prefix=prefix + "_ServicePort",
            config=CoaxialServiceConnectorConfig(center=(0.0, 0.31, 0.0)),
            armor_material=gunmetal,
            hardware_material=gunmetal,
            accent_material=accent,
            backing_panel=service_panel,
            hardflow_boolean_apply=hardflow_boolean_apply,
            hardflow_bevel=hardflow_bevel,
        )
        objects.extend(connector_objects)
    return objects


def build_utility_sponson_module(
    entry: dict[str, Any], armor: Any, gunmetal: Any, accent: Any, seed: int,
    *, hardflow_boolean_apply: HardflowBoolean | None = None,
    hardflow_bevel: HardflowBevel | None = None,
) -> list[bpy.types.Object]:
    prefix = entry["moduleName"]
    role = entry["visualEngineeringRole"]
    if role == "docking_clamp":
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

    if role != "utility_sponson":
        raise ValueError(f"Unsupported utility sponson role {role!r} for {entry['variantId']}")

    size = (0.38 + (seed % 2) * 0.06, 0.74, 0.42 + (seed % 3) * 0.04)
    location = Vector((0.0, 0.0, 0.0))
    objects = rmk.make_side_sponson_assembly(
        prefix, (0.0, 0.0, 0.0), side=1,
        size=size,
        armor_mat=armor, strap_mat=gunmetal, accent_mat=accent,
    )
    if entry.get("geometryPreset") != SERVICE_HATCH_PRESET_ID:
        raise ValueError(f"Utility sponson requires the registered service hatch geometry preset: {entry['variantId']}")
    pannier = next(obj for obj in objects if obj.name == prefix + "_PannierBody")
    fuel_cap = next(obj for obj in objects if obj.name == prefix + "_FuelCap")
    # Move the existing cap clear of the hatch while keeping it seated on the
    # same pannier top; its radius is 40 mm and the left body margin stays >=20 mm.
    fuel_cap.location.x = location.x - size[0] * 0.34
    hatch_config = ServiceHatchConfig(
        center_xy=(location.x + 0.05, location.y),
        outer_size=(0.24, 0.28),
        support_top_z=location.z + size[2] * 0.5,
    )
    objects.extend(GEOMETRY_PRESETS[SERVICE_HATCH_PRESET_ID](
        prefix=prefix + "_ServiceHatch",
        config=hatch_config,
        armor_material=armor,
        hardware_material=gunmetal,
        cut_target=pannier,
    ))
    for side in (-1.0, 1.0):
        objects.append(rmk.make_rod(prefix + f"_MountingStrut_{'L' if side < 0 else 'R'}",
                                    (side * 0.15, -0.27, -0.18), (side * 0.20, 0.27, -0.18),
                                    0.025, gunmetal))
    return objects


def build_module_geometry(
    entry: dict[str, Any], seed: int, *,
    hardflow_boolean_apply: HardflowBoolean | None = None,
    hardflow_bevel: HardflowBevel | None = None,
) -> tuple[list[bpy.types.Object], list[bpy.types.Material]]:
    armor, gunmetal, accent = make_materials(entry)
    role = entry["visualEngineeringRole"]
    geometry_preset = entry.get("geometryPreset", "")
    if geometry_preset == ERA_BRICK_PRESET_ID:
        objects = build_armor_module(
            entry, armor, gunmetal, accent, seed,
            hardflow_boolean_apply=hardflow_boolean_apply,
            hardflow_bevel=hardflow_bevel,
        )
    elif geometry_preset == SERVICE_HATCH_PRESET_ID:
        objects = build_utility_sponson_module(
            entry, armor, gunmetal, accent, seed,
            hardflow_boolean_apply=hardflow_boolean_apply,
            hardflow_bevel=hardflow_bevel,
        )
    elif geometry_preset == COAXIAL_SERVICE_CONNECTOR_PRESET_ID:
        objects = build_engineering_sponson_module(
            entry, armor, gunmetal, accent, seed,
            hardflow_boolean_apply=hardflow_boolean_apply,
            hardflow_bevel=hardflow_bevel,
        )
    elif geometry_preset:
        objects = build_generic_geometry_preset(
            entry, armor, gunmetal, accent,
            hardflow_boolean_apply=hardflow_boolean_apply,
            hardflow_bevel=hardflow_bevel,
        )
    elif role == "armor_cassette":
        raise ValueError(f"Armor geometry preset is required for {entry['variantId']}")
    elif role in ("aps_sensor_suite", "aps_magnetic_shield"):
        objects = build_aps_module(entry, armor, gunmetal, accent, seed)
    elif role in ("fuel_drum_pair", "ordnance_carrier"):
        objects = build_fuel_or_ordnance_module(entry, armor, gunmetal, accent, seed)
    elif role == "excavation_drill":
        objects = build_drill_module(entry, armor, gunmetal, accent, seed)
    elif role in ("thermal_radiator", "heavy_flak_battery", "repair_dispenser", "power_containment"):
        objects = build_engineering_sponson_module(
            entry, armor, gunmetal, accent, seed,
            hardflow_boolean_apply=hardflow_boolean_apply,
            hardflow_bevel=hardflow_bevel,
        )
    elif role in ("docking_clamp", "utility_sponson"):
        objects = build_utility_sponson_module(
            entry, armor, gunmetal, accent, seed,
            hardflow_boolean_apply=hardflow_boolean_apply,
            hardflow_bevel=hardflow_bevel,
        )
    else:
        raise ValueError(f"No geometry builder registered for visual engineering role {role!r}")

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


def prepare_module_export_geometry(
    entry: dict[str, Any],
    created: list[bpy.types.Object],
    armor_materials: list[bpy.types.Material],
    *,
    allow_uncalibrated_geometry: bool = False,
    prepass_out: dict[str, Any] | None = None,
) -> list[bpy.types.Object]:
    """Apply the shared in-memory export preparation for production and staging."""
    for obj in created:
        ensure_uv(obj)
    role = entry.get("visualEngineeringRole", "")
    prepass = fei.apply_form_hierarchy_prepass_bpy(
        created,
        role=role,
        entry_or_role=entry,
        enable_taubin_fairing=None,
    )
    if prepass_out is not None:
        prepass_out.update(prepass)
    soot_profile = prepass["sootProfile"]
    rmk.bake_advanced_vertex_shading(
        created,
        wear_level="field_tested",
        soot_intensity=float(soot_profile["soot_intensity"]),
        team_mask_materials=armor_materials,
        exhaust_locs=soot_profile["exhaust_locs"],
        muzzle_loc=soot_profile["muzzle_loc"],
        structural_wear_mask=True,
    )
    combined = rmk.batch_and_join_by_material(
        created,
        prefix=entry["moduleName"],
        smooth_by_angle=True,
        weighted_normals=True,
        unity_batch_names=True,
        asset_id=entry["moduleName"],
        allow_uncalibrated_geometry=allow_uncalibrated_geometry,
        apply_form_hierarchy=False,
    )
    if not combined:
        raise RuntimeError(f"Material batching produced no meshes for {entry['variantId']}")
    fei.apply_canonical_two_level_mesh_pass_bpy(
        combined,
        role=role,
        enable_taubin_fairing=False,
        optimize_vertex_cache=True,
    )
    return combined


def export_inventory_candidate_fbx(
    fbx_path: Path,
    model_objects: list[bpy.types.Object],
    gate: InventoryCapacityGate | InventoryStageContext,
    *,
    asset_id: str,
) -> None:
    """Export one LOD0 candidate only to the measured-capacity staging area."""
    verify_gate_unchanged(gate)
    output = validate_candidate_export_path(fbx_path, gate)
    if not model_objects:
        raise ValueError(f"{asset_id}: candidate FBX export requires mesh objects")
    if any(obj is None or obj.type != "MESH" or "_COLLIDER_" in obj.name for obj in model_objects):
        raise ValueError(f"{asset_id}: candidate inventory export accepts renderable meshes only")
    output.parent.mkdir(parents=True, exist_ok=True)
    output = validate_candidate_export_path(output, gate)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in model_objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = model_objects[0]
    bpy.ops.export_scene.fbx(
        filepath=str(output),
        use_selection=True,
        object_types={"MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_NONE",
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        path_mode="AUTO",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=False,
        add_leaf_bones=False,
        use_custom_props=False,
        bake_anim=False,
        bake_anim_use_all_actions=False,
        bake_anim_use_nla_strips=False,
        bake_anim_step=1.0,
    )
    if not output.is_file() or output.stat().st_size <= 0:
        raise RuntimeError(f"Blender did not export a non-empty candidate FBX: {output}")


def build_candidate_module_fbx(
    entry: dict[str, Any],
    fbx_path: Path,
    gate: InventoryCapacityGate | InventoryStageContext,
) -> dict[str, Any]:
    """Replay one module in memory, export to staging, then inspect its actual FBX."""
    rmk.clear_scene()
    collection = bpy.data.collections.new(entry["moduleName"])
    bpy.context.scene.collection.children.link(collection)
    seed = int(hashlib.sha256(entry["variantId"].encode("utf-8")).hexdigest()[:8], 16)
    created, armor_materials = build_module_geometry(entry, seed)
    if not created:
        raise RuntimeError(f"No geometry generated for {entry['variantId']}")
    combined = prepare_module_export_geometry(entry, created, armor_materials)
    place_in_collection(combined, collection)
    in_memory_triangles = rmk.count_mesh_triangles(combined)
    validate_candidate_triangle_count(in_memory_triangles, gate, entry["moduleName"])
    export_inventory_candidate_fbx(fbx_path, combined, gate, asset_id=entry["moduleName"])
    return fbx_inspector.inspect_fbx(fbx_path)


def build_all(entries: list[dict[str, Any]], force: bool) -> None:
    build_policy = json.loads(GEOMETRY_BUILD_POLICY_PATH.read_text(encoding="utf-8"))
    if build_policy.get("schemaVersion") != 1:
        raise ValueError(f"Unsupported canonical visual module build policy: {GEOMETRY_BUILD_POLICY_PATH}")
    retention_ratios = build_policy.get("lodTriangleRetentionRatios")
    transition_heights = build_policy.get("lodScreenRelativeTransitionHeights")
    if (not isinstance(retention_ratios, list) or len(retention_ratios) != 3 or
            not isinstance(transition_heights, list) or len(transition_heights) != 3):
        raise ValueError("Canonical Level 2 build policy must declare three LOD retention ratios and screen thresholds")
    retention_ratios = [float(value) for value in retention_ratios]
    transition_heights = [float(value) for value in transition_heights]
    if (retention_ratios[0] != 1.0 or not 0.0 < retention_ratios[2] < retention_ratios[1] < 1.0 or
            not 0.0 < transition_heights[2] < transition_heights[1] < transition_heights[0] <= 1.0):
        raise ValueError("Canonical Level 2 LOD ratios and thresholds must descend from LOD0 to LOD2")
    # Calibration is diagnostic, never an authoring/build gate. Do not label
    # outputs calibrated even if a profile happens to be present.
    uncalibrated_build = True

    outputs = [SOURCE_BLEND]
    outputs.extend(ROOT / entry["fbxFilePath"] for entry in entries)
    outputs.extend(ROOT / entry["lodFbxFilePath"] for entry in entries)
    if not force:
        existing = [str(path) for path in outputs if path.exists()]
        if existing:
            raise FileExistsError("Generated canonical module outputs already exist; rerun with --force: " + ", ".join(existing[:8]))

    rmk.clear_scene()
    archive_scene = bpy.context.scene
    archive_collection = archive_scene.collection
    work_scene = bpy.data.scenes.new("CanonicalModuleWorkScene")
    work_collection = work_scene.collection
    window = getattr(bpy.context, "window", None)
    if window is None:
        raise RuntimeError("Blender context has no active window for isolated module generation.")

    try:
        window.scene = work_scene
        for index, entry in enumerate(entries):
            seed = int(hashlib.sha256(entry["variantId"].encode("utf-8")).hexdigest()[:8], 16)
            module_collection = bpy.data.collections.new(entry["moduleName"])
            work_collection.children.link(module_collection)
            created, armor_materials = build_module_geometry(entry, seed)
            if not created:
                raise RuntimeError(f"No geometry generated for {entry['variantId']}")
            asset_budget = None
            if not uncalibrated_build:
                # Module demand is charged as its own visible asset demand in the
                # calibrated scene profile, separate from its owning chassis.
                asset_budget = rmk.resolve_geometry_budget(entry["moduleName"])
                rmk.enforce_face_budget_before_export(
                    created,
                    entry["moduleName"],
                    asset_id=entry["moduleName"],
                )
            prepass_info: dict[str, Any] = {}
            combined = prepare_module_export_geometry(
                entry,
                created,
                armor_materials,
                allow_uncalibrated_geometry=uncalibrated_build,
                prepass_out=prepass_info,
            )
            place_in_collection(combined, module_collection)
            source_triangles = rmk.count_mesh_triangles(combined)
            lod_ratios = (
                [("LOD0", retention_ratios[0]), ("LOD1", retention_ratios[1]), ("LOD2", retention_ratios[2])]
                if asset_budget is None else rmk.lod_ratios_from_budget(asset_budget, source_triangles)
            )
            lod_dict = rmk.generate_lod_chain(
                combined,
                prefix=entry["moduleName"],
                lod_levels=lod_ratios,
                lod_name_first=True,
            )
            lod_stats = {}
            for lod_name, lod_objects in lod_dict.items():
                if lod_name != "LOD0":
                    fei.apply_canonical_two_level_mesh_pass_bpy(
                        lod_objects,
                        role=entry.get("visualEngineeringRole", ""),
                        enable_taubin_fairing=False,
                        optimize_vertex_cache=True,
                    )
                lod_collection = bpy.data.collections.new(entry["moduleName"] + "_" + lod_name)
                module_collection.children.link(lod_collection)
                place_in_collection(lod_objects, lod_collection)
                faces = sum(len(obj.data.polygons) for obj in lod_objects if obj.type == "MESH")
                triangles = rmk.count_mesh_triangles(lod_objects)
                lod_stats[lod_name] = {"blenderFaces": faces, "unityTriangles": triangles}
            mesh_quality = fei.apply_canonical_two_level_mesh_pass_bpy(
                combined,
                role=entry.get("visualEngineeringRole", ""),
                enable_taubin_fairing=False,
                optimize_vertex_cache=False,
                assume_cache_optimized=True,
            )
            if prepass_info:
                mesh_quality["taubinFairingObjects"] = list(prepass_info.get("taubinFairingObjects", []))
                mesh_quality["formHierarchyCounts"] = dict(prepass_info.get("formHierarchyCounts", {}))
                mesh_quality["sootProfile"] = prepass_info.get("sootProfile")
            entry["meshQuality"] = mesh_quality
            entry["lodScreenHeights"] = (
                transition_heights if asset_budget is None else
                [row["screenRelativeTransitionHeight"] for row in asset_budget["lods"]]
            )
            entry["geometryBudgetStatus"] = (
                "UNCALIBRATED_USER_AUTHORIZED" if uncalibrated_build else "CALIBRATED"
            )
            entry["geometryStats"] = lod_stats

            # Export within the isolated 1-module work scene so FBX selection/depsgraph
            # updates stay O(1) instead of scanning all 57 modules.
            fbx_path = ROOT / entry["fbxFilePath"]
            fbx_path.parent.mkdir(parents=True, exist_ok=True)
            rmk.export_unity_fbx(
                fbx_path,
                combined,
                include_empties=False,
                include_armature=False,
                bake_anim=False,
                asset_id=entry["moduleName"],
                prevalidated_budget=asset_budget,
                allow_uncalibrated_export=asset_budget is None,
                optimize_vertex_cache=False,
            )
            if not fbx_path.is_file() or fbx_path.stat().st_size <= 0:
                raise RuntimeError(f"Blender did not export a non-empty FBX: {fbx_path}")
            lod_fbx_path = ROOT / entry["lodFbxFilePath"]
            all_lod_objects = [
                obj for lod_name in ("LOD1", "LOD2") for obj in lod_dict[lod_name]
            ]
            rmk.export_unity_fbx(
                lod_fbx_path,
                all_lod_objects,
                include_empties=False,
                include_armature=False,
                bake_anim=False,
                asset_id=entry["moduleName"],
                prevalidated_budget=asset_budget,
                allow_uncalibrated_export=asset_budget is None,
                optimize_vertex_cache=False,
            )
            if not lod_fbx_path.is_file() or lod_fbx_path.stat().st_size <= 0:
                raise RuntimeError(f"Blender did not export a non-empty LOD FBX: {lod_fbx_path}")

            archive_collection.children.link(module_collection)
            work_collection.children.unlink(module_collection)
            print(f"[{index + 1:02d}/{len(entries):02d}] {entry['variantId']} -> {entry['slotId']} ({len(combined)} material batch(es))", flush=True)
    finally:
        window.scene = archive_scene
        if work_scene.name in bpy.data.scenes:
            bpy.data.scenes.remove(work_scene)

    SOURCE_BLEND.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE_BLEND))

    role_registry = json.loads(ROLE_REGISTRY_PATH.read_text(encoding="utf-8"))
    manifest = {
        "schemaVersion": 3,
        "sourceBlend": SOURCE_BLEND.relative_to(ROOT).as_posix(),
        "level1Authority": "Existing unit chassis geometry, functional hierarchy, rig, pivots, canonical sockets, LOD and PBR atlas.",
        "geometryBudgetPolicy": (
            "uncalibrated-relative-lod-no-face-or-triangle-cap" if uncalibrated_build else
            "scene-calibrated-per-asset-lod"
        ),
        "geometryBudgetProfile": "content/source-art/model-budget-profile.json",
        "geometryBudgetStatus": (
            "UNCALIBRATED_USER_AUTHORIZED" if uncalibrated_build else "CALIBRATED"
        ),
        "geometryLodPolicy": (
            (
                "relative triangle retention " + "/".join(f"{value:g}" for value in retention_ratios) +
                "; screen transitions " + "/".join(f"{value:g}" for value in transition_heights) +
                "; no face or triangle ceiling"
            ) if uncalibrated_build else "scene-calibrated per-asset LOD allocation"
        ),
        "geometryBuildPolicy": GEOMETRY_BUILD_POLICY_PATH.relative_to(ROOT).as_posix(),
        "geometryBuildPolicySha256": hashlib.sha256(GEOMETRY_BUILD_POLICY_PATH.read_bytes()).hexdigest(),
        "level2Contract": "Each listed GameObject is visual-only geometry attached to exactly one canonical hardpoint group; no gameplay components or balance changes.",
        "visualEngineeringRoleContract": role_registry.get("scope", ""),
        "geometryPresetRegistrySha256": hashlib.sha256(ROLE_REGISTRY_PATH.read_bytes()).hexdigest(),
        "geometryPresetPolicy": "Every Level 2 module resolves one validated hard-surface factory from the complete preset catalog; geometry presets remain separate from Material Maker PBR presets.",
        "modules": entries,
    }
    MANIFEST_PATH.parent.mkdir(parents=True, exist_ok=True)
    MANIFEST_PATH.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Saved {len(entries)} modules, source blend, and manifest: {MANIFEST_PATH}", flush=True)
    if uncalibrated_build:
        print(
            "WARNING: Level 2 geometry was exported without a calibrated face/triangle ceiling under explicit user authorization; "
            "the model-budget profile remains uncalibrated.", flush=True
        )


def main() -> None:
    args = parse_args()
    entries = read_module_entries()
    if args.inventory_staging:
        manifest = stage_candidate_inventory(
            repo_root=ROOT,
            staging_root=args.inventory_staging,
            profile_path=BUDGET_PROFILE_PATH,
            evidence_bundle_path=args.capacity_evidence,
            entries=entries,
            build_candidate=build_candidate_module_fbx,
        )
        output = Path(args.inventory_staging)
        if not output.is_absolute():
            output = ROOT / output
        print(
            f"Staged {manifest['assetInventory']['fileCount']} candidate Level 2 FBXs under "
            f"{output.resolve()} (candidate inventory only; no calibration limits applied).",
            flush=True,
        )
        return
    build_all(entries, args.force)


if __name__ == "__main__":
    main()
