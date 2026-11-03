"""Deterministic Wave 2 visual integration for authored active units.

This module augments existing authored geometry without replacing hulls, rigs,
sockets, materials, or gameplay-facing asset names. It is intentionally
idempotent: rerunning a builder does not duplicate an already-present feature.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path
from typing import Mapping

import bpy
from mathutils import Vector

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))
import RogueModelKit as rmk


ACTIVE_UNIT_WAVE2_PROFILES = {
    "SandlanceScout": {
        "features": {"towing", "lifting", "suspension"},
        "hull_width": 2.10, "hull_length": 2.60, "deck_z": 0.76, "ground_clearance": 0.055,
        "lift_corners": [(-0.48, 0.62, 0.93), (0.48, 0.62, 0.93), (-0.48, -0.62, 0.93), (0.48, -0.62, 0.93)],
        "wheel_centers": [(-1.035, -0.89, 0.35), (-1.035, -0.39, 0.35), (-1.035, 0.15, 0.35), (-1.035, 0.68, 0.35), (1.035, -0.89, 0.35), (1.035, -0.39, 0.35), (1.035, 0.15, 0.35), (1.035, 0.68, 0.35)],
    },
    "ColossusSiegePlatform": {
        "features": {"towing", "lifting", "suspension"},
        "hull_width": 2.50, "hull_length": 2.90, "deck_z": 0.88, "ground_clearance": 0.06,
        "lift_corners": [(-0.75, 0.66, 1.08), (0.75, 0.66, 1.08), (-0.75, -0.66, 1.08), (0.75, -0.66, 1.08)],
        "wheel_centers": [(-1.20, -1.05, 0.34), (-1.20, -0.52, 0.34), (-1.20, 0.0, 0.34), (-1.20, 0.52, 0.34), (-1.20, 1.05, 0.34), (1.20, -1.05, 0.34), (1.20, -0.52, 0.34), (1.20, 0.0, 0.34), (1.20, 0.52, 0.34), (1.20, 1.05, 0.34)],
    },
    "IroncladWalker": {
        "features": {"lifting"},
        "hull_width": 1.18, "hull_length": 0.90, "deck_z": 2.32, "ground_clearance": 0.0,
        "lift_corners": [(-0.48, 0.32, 2.42), (0.48, 0.32, 2.42), (-0.48, -0.32, 2.42), (0.48, -0.32, 2.42)],
    },
    "AtlasCraneMech": {
        "features": {"lifting"},
        "hull_width": 1.85, "hull_length": 1.20, "deck_z": 2.50, "ground_clearance": 0.0,
        "lift_corners": [(-0.82, 0.48, 2.80), (0.82, 0.48, 2.80), (-0.82, -0.48, 2.80), (0.82, -0.48, 2.80)],
    },
    "BastionSiegeMech": {
        "features": {"lifting", "crawler"},
        "hull_width": 2.30, "hull_length": 1.80, "deck_z": 2.45, "ground_clearance": 0.0,
        "lift_corners": [(-0.98, 0.72, 2.76), (0.98, 0.72, 2.76), (-0.98, -0.72, 2.76), (0.98, -0.72, 2.76)],
    },
    "ArachnidHexapod": {
        "features": {"lifting", "crawler", "missile"},
        "hull_width": 2.60, "hull_length": 3.35, "deck_z": 1.58, "ground_clearance": 0.0,
        "lift_corners": [(-0.92, 0.78, 1.72), (0.92, 0.78, 1.72), (-0.92, -0.78, 1.72), (0.92, -0.78, 1.72)],
        "missile_anchor": ((0.0, 0.0, 1.67), (1.08, 0.52, 0.38), (0.0, 1.0, 0.0)),
    },
    "OverlordCommandHQ": {
        "features": {"towing", "lifting", "suspension", "fenders", "coaming"},
        "hull_width": 2.55, "hull_length": 3.55, "deck_z": 1.38, "ground_clearance": 0.06,
    },
    "SkirmishStrikeBike": {
        "features": {"coaming"},
        "hull_width": 0.96, "hull_length": 1.60, "deck_z": 1.22, "ground_clearance": 0.03,
        "coamings": [((0.0, -0.13, 0.78), (0.69, 0.65), "BatteryCollar")],
    },
    "SurgeArcTank": {
        "features": {"towing", "lifting", "suspension"},
        "hull_width": 1.78, "hull_length": 2.54, "deck_z": 1.18, "ground_clearance": 0.06,
        "lift_corners": [(-0.62, 0.66, 1.31), (0.62, 0.66, 1.31), (-0.62, -0.66, 1.31), (0.62, -0.66, 1.31)],
        "wheel_centers": [(-0.91, -0.82, 0.34), (-0.91, -0.41, 0.34), (-0.91, 0.0, 0.34), (-0.91, 0.41, 0.34), (-0.91, 0.82, 0.34), (0.91, -0.82, 0.34), (0.91, -0.41, 0.34), (0.91, 0.0, 0.34), (0.91, 0.41, 0.34), (0.91, 0.82, 0.34)],
    },
    "TidebreakerHovercraft": {
        "features": {"hover"},
        "hull_width": 2.60, "hull_length": 3.40, "deck_z": 1.25, "ground_clearance": 0.42,
        "hover": {"prefix": "TidebreakerHovercraftHover", "width": 2.60, "length": 3.40, "float_height": 0.42},
    },
    "SalvageHauler": {
        "features": {"towing", "lifting", "suspension"},
        "hull_width": 2.37, "hull_length": 3.20, "deck_z": 1.40, "ground_clearance": 0.06,
        "lift_corners": [(-0.82, 0.82, 1.62), (0.82, 0.82, 1.62), (-0.82, -0.82, 1.62), (0.82, -0.82, 1.62)],
    },
    "SkyguardSAM": {
        "features": {"towing", "lifting", "missile"},
        "hull_width": 2.05, "hull_length": 3.10, "deck_z": 1.65, "ground_clearance": 0.06,
        "lift_corners": [(-0.72, 0.88, 2.10), (0.72, 0.88, 2.10), (-0.72, -0.88, 2.10), (0.72, -0.88, 2.10)],
        "missile_anchor": ((0.0, 1.30, 1.90), (0.68, 0.66, 0.97), (0.0, 1.0, 0.0)),
    },
    "BulwarkSiegeTank": {
        "features": {"towing", "lifting", "suspension"},
        "hull_width": 2.35, "hull_length": 3.25, "deck_z": 1.45, "ground_clearance": 0.06,
        "lift_corners": [(-0.78, 0.78, 1.68), (0.78, 0.78, 1.68), (-0.78, -0.78, 1.68), (0.78, -0.78, 1.68)],
    },
    "ArcLanceWalker": {
        "features": {"lifting"},
        "hull_width": 1.10, "hull_length": 0.90, "deck_z": 2.05, "ground_clearance": 0.0,
        "lift_corners": [(-0.46, 0.30, 2.28), (0.46, 0.30, 2.28), (-0.46, -0.30, 2.28), (0.46, -0.30, 2.28)],
    },
    "FieldMedicRover": {
        "features": {"towing", "lifting", "fenders"},
        "hull_width": 2.10, "hull_length": 2.70, "deck_z": 1.45, "ground_clearance": 0.06,
        "lift_corners": [(-0.72, 0.74, 1.67), (0.72, 0.74, 1.67), (-0.72, -0.74, 1.67), (0.72, -0.74, 1.67)],
    },
    "SentinelRelay": {
        "features": {"towing", "lifting"},
        "hull_width": 2.20, "hull_length": 2.80, "deck_z": 1.30, "ground_clearance": 0.06,
    },
    "GravitasBreaker": {
        "features": {"towing", "lifting"},
        "hull_width": 2.40, "hull_length": 3.20, "deck_z": 1.35, "ground_clearance": 0.06,
    },
    "CommandRelay": {
        "features": {"towing", "lifting"},
        "hull_width": 2.20, "hull_length": 2.90, "deck_z": 1.30, "ground_clearance": 0.06,
    },
}


def _has_object(prefix: str, suffix: str) -> bool:
    return any(obj.name.startswith(prefix + suffix) for obj in bpy.context.scene.objects)


def _resolve_material(materials: Mapping[str, object] | None, key: str, fallback_color: tuple[float, float, float]):
    if materials and materials.get(key) is not None:
        return materials[key]
    tokens = {
        "armor": ("primaryarmor", "armor", "primary"),
        "gunmetal": ("gunmetal", "metal", "wheelrim", "rim"),
        "warning": ("warning", "amber", "accent", "signal"),
        "tread": ("tread", "rubber"),
    }[key]
    for material in bpy.data.materials:
        name = material.name.lower().replace("_", "")
        if any(token in name for token in tokens):
            return material
    return rmk.make_material(f"{key.title()}AuthoringDetail", fallback_color, metallic=0.55, roughness=0.42)


def _add_missile_cowl_and_umbilical(prefix: str, anchor, armor, cavity) -> list:
    center, size, direction = anchor
    if _has_object(prefix, "_BlastCowl") and _has_object(prefix, "_UmbilicalConduit"):
        return []
    loc_v = Vector(center)
    dir_v = Vector(direction).normalized()
    _, v_vec, forward_dir = rmk.get_orthonormal_basis(dir_v)
    rot = forward_dir.to_track_quat("Z", "Y").to_euler()
    result = []
    cowl_loc = loc_v + forward_dir * (size[1] * 0.44) + v_vec * (size[2] * 0.52)
    result.append(rmk.make_box(f"{prefix}_BlastCowl", (size[0] * 0.95, size[1] * 0.22, 0.04), cowl_loc, armor, bevel=0.008, rotation=rot))
    conduit_start = loc_v - forward_dir * (size[1] * 0.35) - v_vec * (size[2] * 0.45)
    result.append(rmk.make_rod(f"{prefix}_UmbilicalConduit", conduit_start, conduit_start - Vector((0.0, 0.0, 0.18)), 0.028, cavity, vertices=6))
    return result


def _add_crawler_leg_hardware(unit_name: str, armor, gunmetal) -> list:
    result = []
    prefix = unit_name + "CrawlerLeg_"
    for obj in list(bpy.context.scene.objects):
        if obj.type != "MESH" or not obj.name.startswith(prefix) or not obj.name.endswith("_KneeKnuckle"):
            continue
        tag = obj.name[len(prefix):-len("_KneeKnuckle")]
        shield_name = f"{prefix}{tag}_KneeCapShield"
        if not bpy.data.objects.get(shield_name):
            side_y = -1.0 if tag.endswith("R") else 1.0
            knee = obj.matrix_world.translation.copy()
            result.append(rmk.make_box(shield_name, (0.24, 0.12, 0.22), knee + Vector((0.0, side_y * 0.10, 0.04)), armor, bevel=0.02, rotation=(math.radians(-side_y * 18.0), 0.0, 0.0)))
        shock_name = f"{prefix}{tag}_TibiaHydraulicShock"
        if not bpy.data.objects.get(shock_name):
            foot = bpy.data.objects.get(f"{prefix}{tag}_FootPad") or bpy.data.objects.get(f"{prefix}{tag}_FootBase")
            if foot is not None:
                side_x = -1.0 if tag.startswith("L") else 1.0
                side_y = -1.0 if tag.endswith("R") else 1.0
                knee = obj.matrix_world.translation.copy()
                foot_pos = foot.matrix_world.translation.copy()
                offset = Vector((side_x * 0.08, -side_y * 0.06, 0.05))
                result.append(rmk.make_rod(shock_name, knee + offset, knee.lerp(foot_pos, 0.5) + offset, 0.032, gunmetal, vertices=6))
    return result


def apply_wave2_details(
    unit_name: str,
    objects: list | None = None,
    materials: Mapping[str, object] | None = None,
    object_prefix: str | None = None,
) -> list:
    """Add the missing Wave 2 details to one authored active-unit scene.

    The returned objects are the only new objects. Callers that maintain a
    builder list must extend that list before validation/rigging/export.
    """
    if unit_name not in ACTIVE_UNIT_WAVE2_PROFILES:
        raise KeyError(f"No Wave 2 profile for active unit {unit_name}")
    profile = ACTIVE_UNIT_WAVE2_PROFILES[unit_name]
    prefix = object_prefix or unit_name
    features = set(profile["features"])
    objects = objects if objects is not None else []
    armor = _resolve_material(materials, "armor", (0.18, 0.24, 0.28))
    gunmetal = _resolve_material(materials, "gunmetal", (0.07, 0.09, 0.11))
    warning = _resolve_material(materials, "warning", (0.85, 0.28, 0.04))
    tread = _resolve_material(materials, "tread", (0.035, 0.04, 0.045))
    result = []
    width = float(profile["hull_width"])
    length = float(profile["hull_length"])
    deck_z = float(profile["deck_z"])
    ground = float(profile["ground_clearance"])

    if "towing" in features and not _has_object(prefix, "Armor_FrontShackle"):
        sx = width * 0.38
        z = ground + 0.24
        result.extend(rmk.make_towing_shackle_assembly(f"{prefix}Armor_FrontShackle", [(-sx, length * 0.48, z), (sx, length * 0.48, z)], material=gunmetal, pin_material=warning, direction=(0.0, 1.0, 0.0)))
    if "towing" in features and not _has_object(prefix, "Armor_RearShackle"):
        sx = width * 0.38
        z = ground + 0.24
        result.extend(rmk.make_towing_shackle_assembly(f"{prefix}Armor_RearShackle", [(-sx, -length * 0.48, z), (sx, -length * 0.48, z)], material=gunmetal, pin_material=warning, direction=(0.0, -1.0, 0.0)))
    if "lifting" in features and not _has_object(prefix, "Armor_LiftingLug"):
        corners = profile.get("lift_corners")
        if corners is None:
            hw, hl, hz = width * 0.36, length * 0.32, deck_z + 0.12
            corners = [(-hw, hl, hz), (hw, hl, hz), (-hw, -hl, hz), (hw, -hl, hz)]
        result.extend(rmk.make_crane_lifting_lugs(f"{prefix}Armor", corners, material=gunmetal))
    if "suspension" in features and not any(
        obj.type == "MESH" and obj.name.startswith(prefix) and ("Suspension" in obj.name or "SwingArm" in obj.name)
        for obj in bpy.context.scene.objects
    ):
        centers = profile.get("wheel_centers")
        if not centers:
            centers = [tuple(obj.matrix_world.translation) for obj in bpy.context.scene.objects if obj.type == "MESH" and ("TireCarcass" in obj.name or ("RoadWheel" in obj.name and obj.name.endswith("_Rim")))]
        by_side = {"L": [loc for loc in centers if float(loc[0]) < 0.0], "R": [loc for loc in centers if float(loc[0]) >= 0.0]}
        for side_tag, side_centers in by_side.items():
            if side_centers:
                result.extend(rmk.make_swing_arm_suspension_linkage(f"{prefix}Suspension_{side_tag}", side_centers, side=-1 if side_tag == "L" else 1, arm_length=0.22, material=gunmetal, boss_mat=warning, with_coil_spring=True))
    if "fenders" in features and not _has_object(prefix, "Fender_"):
        wheel_locations = [obj.matrix_world.translation.copy() for obj in bpy.context.scene.objects if obj.type == "MESH" and "TireCarcass" in obj.name]
        for idx, location in enumerate(wheel_locations, start=1):
            side = 1 if location.x >= 0.0 else -1
            result.extend(rmk.make_sculpted_wheel_fender_assembly(f"{prefix}Fender_{idx}_{'R' if side > 0 else 'L'}", location, radius=max(0.30, min(location.z, 0.48)), width=0.28, side=side, fender_mat=armor, skirt_mat=tread))
    if "coaming" in features:
        for location, size, tag in profile.get("coamings", []):
            if not bpy.data.objects.get(f"{prefix}Armor_{tag}_CoamingCollar"):
                result.append(rmk.make_transitional_coaming_collar(f"{prefix}Armor_{tag}", location, size, height=0.035, collar_mat=gunmetal))
    if "hover" in features and not _has_object(prefix, "Hover_PlenumPleat_"):
        hover = profile["hover"]
        hp = hover["prefix"]
        hw, hl, fh = hover["width"], hover["length"], hover["float_height"]
        result.extend([
            rmk.make_box(f"{hp}_PlenumPleat_F", (hw * 0.86, 0.22, 0.16), (0.0, hl * 0.42, fh - 0.02), tread, bevel=0.02, rotation=(math.radians(-22.0), 0.0, 0.0)),
            rmk.make_box(f"{hp}_PlenumPleat_R", (hw * 0.86, 0.22, 0.16), (0.0, -hl * 0.42, fh - 0.02), tread, bevel=0.02, rotation=(math.radians(22.0), 0.0, 0.0)),
            rmk.make_box(f"{hp}_PerimeterBumper", (hw * 0.94, hl * 0.90, 0.06), (0.0, 0.0, fh + 0.10), gunmetal, bevel=0.015),
        ])
    if "missile" in features and profile.get("missile_anchor"):
        result.extend(_add_missile_cowl_and_umbilical(prefix + "MissilePod", profile["missile_anchor"], armor, gunmetal))
    if "crawler" in features:
        result.extend(_add_crawler_leg_hardware(unit_name, armor, gunmetal))

    return result
