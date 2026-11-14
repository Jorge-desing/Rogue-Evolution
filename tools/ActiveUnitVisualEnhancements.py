"""Deterministic Wave 2/3/4 visual integration for authored active units.

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
        "features": {"coaming", "lifting"},
        "hull_width": 0.96, "hull_length": 1.60, "deck_z": 1.22, "ground_clearance": 0.03,
        "coamings": [((0.0, -0.13, 0.78), (0.69, 0.65), "BatteryCollar")],
        "lift_corners": [(-0.22, 0.28, 0.88), (0.22, 0.28, 0.88), (-0.22, -0.32, 0.84), (0.22, -0.32, 0.84)],
    },
    "SurgeArcTank": {
        "features": {"towing", "lifting", "suspension"},
        "hull_width": 1.78, "hull_length": 2.54, "deck_z": 1.18, "ground_clearance": 0.06,
        "lift_corners": [(-0.62, 0.66, 1.31), (0.62, 0.66, 1.31), (-0.62, -0.66, 1.31), (0.62, -0.66, 1.31)],
        "wheel_centers": [(-0.91, -0.82, 0.34), (-0.91, -0.41, 0.34), (-0.91, 0.0, 0.34), (-0.91, 0.41, 0.34), (-0.91, 0.82, 0.34), (0.91, -0.82, 0.34), (0.91, -0.41, 0.34), (0.91, 0.0, 0.34), (0.91, 0.41, 0.34), (0.91, 0.82, 0.34)],
    },
    "TidebreakerHovercraft": {
        "features": {"hover", "lifting", "towing"},
        "hull_width": 2.60, "hull_length": 3.40, "deck_z": 1.25, "ground_clearance": 0.42,
        "hover": {"prefix": "TidebreakerHovercraftHover", "width": 2.60, "length": 3.40, "float_height": 0.42},
        "lift_corners": [(-0.88, 1.05, 1.02), (0.88, 1.05, 1.02), (-0.88, -1.05, 1.02), (0.88, -1.05, 1.02)],
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


ACTIVE_UNIT_WAVE34_PROFILES = {
    "SandlanceScout": {
        "features": {"sponson_bridges", "engine_cavity", "antenna_collar", "gussets"},
        "hull_width": 2.10, "hull_length": 2.60, "deck_z": 0.76, "ground_clearance": 0.055,
        "gusset_locations": [(-0.46, -0.34, 0.82), (0.46, -0.34, 0.82)],
        "antenna_location": (0.0, -0.64, 1.25),
    },
    "ColossusSiegePlatform": {
        "features": {"sponson_bridges", "engine_cavity", "antenna_collar", "gussets"},
        "hull_width": 2.50, "hull_length": 2.90, "deck_z": 0.88, "ground_clearance": 0.06,
        "gusset_locations": [(-0.72, -0.38, 0.98), (0.72, -0.38, 0.98)],
        "antenna_location": (0.36, -0.48, 1.32),
    },
    "IroncladWalker": {
        "features": {"leg_mounts", "gatling_mounts"},
        "gusset_locations": [(-0.42, -0.08, 1.42), (0.42, -0.08, 1.42)],
        "gatling_locations": [(-0.72, 0.12, 1.58), (0.72, 0.12, 1.58)],
    },
    "AtlasCraneMech": {
        "features": {"leg_mounts", "gussets"},
        "gusset_locations": [(-0.70, -0.20, 2.35), (0.70, -0.20, 2.35)],
    },
    "BastionSiegeMech": {
        "features": {"gussets"},
        "gusset_locations": [(-0.82, -0.48, 2.28), (0.82, -0.48, 2.28)],
    },
    "ArachnidHexapod": {
        # The authored six-cell cradle provides the launcher support; omit the
        # generic cross-axle and unsupported side gussets for this silhouette.
        "features": set(),
    },
    "OverlordCommandHQ": {
        "features": {"drive_axles", "gatling_mounts", "radar_mount", "antenna_collar", "smoke_brackets", "gussets"},
        "axle_locations": [("Rear", -1.43, 0.47, 0.86), ("Mid", -0.08, 0.47, 0.86), ("Front", 1.43, 0.47, 0.86)],
        "gatling_locations": [(0.0, 0.52, 1.95)],
        "radar_location": (0.45, -1.25, 2.05),
        "antenna_location": (0.61, -1.04, 2.18),
        "smoke_locations": [(-0.78, 0.42, 1.72), (0.78, 0.42, 1.72)],
        "gusset_locations": [(-0.82, -0.70, 1.48), (0.82, -0.70, 1.48)],
    },
    "SkirmishStrikeBike": {
        "features": {"drive_axles", "engine_cavity", "gussets"},
        "hull_width": 0.96, "hull_length": 1.60, "deck_z": 1.22, "ground_clearance": 0.03,
        "axle_locations": [("Rear", -0.78, 0.39, 0.16), ("Front", 0.75, 0.39, 0.16)],
        "gusset_locations": [(-0.30, -0.30, 1.02), (0.30, -0.30, 1.02)],
    },
    "SurgeArcTank": {
        "features": {"sponson_bridges", "arc_mount", "engine_cavity", "gussets"},
        "hull_width": 1.78, "hull_length": 2.54, "deck_z": 1.18, "ground_clearance": 0.06,
        "arc_location": (0.0, -0.10, 1.20),
        "gusset_locations": [(-0.58, -0.34, 1.10), (0.58, -0.34, 1.10)],
    },
    "TidebreakerHovercraft": {
        "features": {"gatling_mounts", "antenna_collar"},
        "gatling_locations": [(0.72, 0.40, 1.20)],
        "antenna_location": (0.38, -0.42, 1.44),
    },
    "SalvageHauler": {
        "features": {"gatling_mounts", "antenna_collar"},
        "gatling_locations": [(-0.55, 0.40, 1.30)],
        "antenna_location": (0.20, -0.60, 1.46),
    },
    "SkyguardSAM": {
        "features": {"drive_axles", "missile_mounts", "radar_mount", "antenna_collar", "gussets"},
        "axle_locations": [("Rear", -1.27, 0.435, 0.88), ("Mid", -0.08, 0.435, 0.88), ("Front", 1.27, 0.435, 0.88)],
        "missile_location": (0.0, -0.78, 1.72),
        "radar_location": (0.0, 1.20, 1.92),
        "antenna_location": (0.38, 0.78, 1.78),
        "gusset_locations": [(-0.70, -0.62, 1.10), (0.70, -0.62, 1.10)],
    },
    "BulwarkSiegeTank": {
        "features": {"sponson_bridges", "engine_cavity", "gussets"},
        "hull_width": 2.35, "hull_length": 3.25, "deck_z": 1.45, "ground_clearance": 0.06,
        "gusset_locations": [(-0.72, -0.52, 1.26), (0.72, -0.52, 1.26)],
    },
    "ArcLanceWalker": {
        "features": {"leg_mounts", "arc_mount", "gussets"},
        "arc_location": (0.0, 0.42, 1.60),
        "gusset_locations": [(-0.42, -0.20, 1.88), (0.42, -0.20, 1.88)],
    },
    "FieldMedicRover": {
        "features": {"drive_axles", "antenna_collar", "gussets"},
        "axle_locations": [("Rear", -1.05, 0.435, 0.86), ("Front", 1.05, 0.435, 0.86)],
        "antenna_location": (0.38, -0.70, 1.55),
        "gusset_locations": [(-0.58, -0.52, 1.18), (0.58, -0.52, 1.18)],
    },
    "SentinelRelay": {
        "features": {"drive_axles", "antenna_collar"},
        "axle_locations": [("Rear", -1.05, 0.47, 0.88), ("Front", 1.05, 0.47, 0.88)],
        "antenna_location": (0.0, -0.72, 1.72),
    },
    "GravitasBreaker": {
        "features": {"sponson_bridges", "engine_cavity", "gussets"},
        "hull_width": 2.40, "hull_length": 3.20, "deck_z": 1.35, "ground_clearance": 0.06,
        "gusset_locations": [(-0.82, -0.48, 1.24), (0.82, -0.48, 1.24)],
    },
    "CommandRelay": {
        "features": {"drive_axles", "antenna_collar"},
        "axle_locations": [("Rear", -1.45, 0.475, 0.92), ("Mid", -0.15, 0.475, 0.92), ("Front", 1.45, 0.475, 0.92)],
        "antenna_location": (-0.35, -1.25, 1.72),
    },
}


ACTIVE_UNIT_REALISM_PROFILES = {
    "SandlanceScout": {
        "features": {"access_hatches", "final_drive", "track_tensioners", "pioneer_racks", "turret_ring_guard", "coaxial_port", "driver_periscope"},
        "hull_width": 2.10, "hull_length": 2.60, "deck_z": 0.76, "ground_clearance": 0.055,
        "hatch_location": (0.35, -0.42, 0.78),
        "periscope_location": (0.0, 0.95, 0.72),
        "tool_rack_location": (-0.88, -0.15, 0.76),
        "turret_ring_location": (0.0, 0.28, 0.76), "turret_ring_radius": 0.52,
        "coaxial_location": (0.22, 0.78, 0.92),
        "drive_center": (0.0, -1.05, 0.35),
        "idler_center": (0.0, 1.05, 0.35),
    },
    "ColossusSiegePlatform": {
        "features": {"access_hatches", "final_drive", "track_tensioners", "pioneer_racks", "turret_ring_guard", "coaxial_port", "driver_periscope", "weapon_trunnions"},
        "hull_width": 2.50, "hull_length": 2.90, "deck_z": 0.88, "ground_clearance": 0.06,
        "hatch_location": (0.48, -0.55, 0.92),
        "periscope_location": (0.0, 1.15, 0.84),
        "tool_rack_location": (-1.05, -0.25, 0.88),
        "turret_ring_location": (0.0, 0.35, 0.92), "turret_ring_radius": 0.74,
        "coaxial_location": (0.34, 0.95, 1.15),
        "weapon_trunnion_center": (0.0, 1.10, 1.15),
        "drive_center": (0.0, -1.25, 0.38),
        "idler_center": (0.0, 1.25, 0.38),
    },
    "IroncladWalker": {
        "features": {"access_hatches", "weapon_trunnions"},
        "hatch_location": (0.0, -0.22, 2.45),
        "weapon_trunnion_center": (0.0, 0.38, 2.25), "trunnion_spacing": 0.62,
    },
    "AtlasCraneMech": {
        "features": {"access_hatches", "weapon_trunnions"},
        "hatch_location": (0.0, -0.35, 2.65),
        "weapon_trunnion_center": (0.0, 0.45, 2.45), "trunnion_spacing": 0.75,
    },
    "BastionSiegeMech": {
        "features": {"access_hatches", "weapon_trunnions", "turret_ring_guard"},
        "hatch_location": (0.0, -0.42, 2.55),
        "turret_ring_location": (0.0, 0.15, 2.45), "turret_ring_radius": 0.72,
        "weapon_trunnion_center": (0.0, 0.65, 2.48), "trunnion_spacing": 0.68,
    },
    "ArachnidHexapod": {
        # The launcher is fixed to the carapace; this unit has no turret ring
        # or articulated cannon trunnions. Keep only its service hatch.
        "features": {"access_hatches"},
        "hatch_location": (0.0, -0.45, 1.62),
    },
    "OverlordCommandHQ": {
        "features": {"access_hatches", "driver_periscope", "pioneer_racks", "turret_ring_guard"},
        "hull_width": 2.55, "hull_length": 3.55, "deck_z": 1.38, "ground_clearance": 0.06,
        "hatch_location": (0.55, -0.45, 1.42),
        "periscope_location": (0.0, 1.65, 1.35),
        "tool_rack_location": (-1.10, -0.30, 1.38),
        "turret_ring_location": (0.0, 0.52, 1.42), "turret_ring_radius": 0.55,
    },
    "SkirmishStrikeBike": {
        "features": {"access_hatches", "pioneer_racks"},
        "hull_width": 0.96, "hull_length": 1.60, "deck_z": 1.22, "ground_clearance": 0.03,
        "hatch_location": (0.0, -0.35, 1.15),
        "tool_rack_location": (-0.32, -0.15, 0.85),
    },
    "SurgeArcTank": {
        "features": {"access_hatches", "final_drive", "track_tensioners", "pioneer_racks", "turret_ring_guard", "driver_periscope"},
        "hull_width": 1.78, "hull_length": 2.54, "deck_z": 1.18, "ground_clearance": 0.06,
        "hatch_location": (0.38, -0.38, 1.22),
        "periscope_location": (0.0, 0.92, 1.15),
        "tool_rack_location": (-0.78, -0.10, 1.18),
        "turret_ring_location": (0.0, 0.18, 1.18), "turret_ring_radius": 0.58,
        "drive_center": (0.0, -1.05, 0.34),
        "idler_center": (0.0, 1.05, 0.34),
    },
    "TidebreakerHovercraft": {
        "features": {"access_hatches", "driver_periscope", "turret_ring_guard", "coaxial_port"},
        "hull_width": 2.60, "hull_length": 3.40, "deck_z": 1.25, "ground_clearance": 0.42,
        "hatch_location": (0.42, -0.45, 1.28),
        "periscope_location": (0.0, 1.35, 1.22),
        "turret_ring_location": (0.0, 0.35, 1.25), "turret_ring_radius": 0.62,
        "coaxial_location": (0.28, 0.85, 1.38),
    },
    "SalvageHauler": {
        "features": {"access_hatches", "driver_periscope", "pioneer_racks", "turret_ring_guard"},
        "hull_width": 2.37, "hull_length": 3.20, "deck_z": 1.40, "ground_clearance": 0.06,
        "hatch_location": (0.45, -0.35, 1.44),
        "periscope_location": (0.0, 1.25, 1.36),
        "tool_rack_location": (-0.98, -0.20, 1.40),
        "turret_ring_location": (0.0, 0.35, 1.40), "turret_ring_radius": 0.54,
    },
    "SkyguardSAM": {
        "features": {"access_hatches", "driver_periscope", "pioneer_racks", "turret_ring_guard"},
        "hull_width": 2.05, "hull_length": 3.10, "deck_z": 1.65, "ground_clearance": 0.06,
        "hatch_location": (0.38, 0.35, 1.68),
        "periscope_location": (0.0, 1.35, 1.58),
        "tool_rack_location": (-0.88, 0.15, 1.65),
        "turret_ring_location": (0.0, -0.55, 1.68), "turret_ring_radius": 0.62,
    },
    "BulwarkSiegeTank": {
        "features": {"access_hatches", "final_drive", "track_tensioners", "pioneer_racks", "coaxial_port", "driver_periscope", "weapon_trunnions"},
        "hull_width": 2.35, "hull_length": 3.25, "deck_z": 1.45, "ground_clearance": 0.06,
        "hatch_location": (0.45, -0.65, 1.48),
        "periscope_location": (-0.55, -1.00, 1.08),
        "periscope_size": (0.18, 0.20, 0.14),
        "tool_rack_location": (-1.02, -0.25, 1.45),
        "coaxial_location": (0.32, 0.95, 1.495),
        "weapon_trunnion_center": (0.0, 0.33, 1.495),
        "trunnion_spacing": 1.35,
        "trunnion_bracket_height": 0.16,
        "drive_center": (0.0, -1.35, 0.36),
        "idler_center": (0.0, 1.35, 0.36),
    },
    "ArcLanceWalker": {
        "features": {"access_hatches", "weapon_trunnions"},
        "hatch_location": (0.0, -0.25, 2.15),
        "weapon_trunnion_center": (0.0, 0.45, 1.95), "trunnion_spacing": 0.58,
    },
    "FieldMedicRover": {
        "features": {"access_hatches", "driver_periscope", "pioneer_racks", "turret_ring_guard"},
        "hull_width": 2.10, "hull_length": 2.70, "deck_z": 1.45, "ground_clearance": 0.06,
        "hatch_location": (0.40, -0.32, 1.48),
        "periscope_location": (0.0, 1.10, 1.40),
        "tool_rack_location": (-0.88, -0.15, 1.45),
        "turret_ring_location": (0.0, 0.20, 1.45), "turret_ring_radius": 0.52,
    },
    "SentinelRelay": {
        "features": {"access_hatches", "driver_periscope", "turret_ring_guard"},
        "hull_width": 2.20, "hull_length": 2.80, "deck_z": 1.30, "ground_clearance": 0.06,
        "hatch_location": (0.42, -0.38, 1.35),
        "periscope_location": (0.0, 1.15, 1.28),
        "turret_ring_location": (0.0, 0.25, 1.32), "turret_ring_radius": 0.55,
    },
    "GravitasBreaker": {
        "features": {"access_hatches", "final_drive", "track_tensioners", "pioneer_racks", "turret_ring_guard", "coaxial_port", "driver_periscope", "weapon_trunnions"},
        "hull_width": 2.40, "hull_length": 3.20, "deck_z": 1.35, "ground_clearance": 0.06,
        "hatch_location": (0.48, -0.58, 1.38),
        "periscope_location": (0.0, 1.30, 1.28),
        "tool_rack_location": (-1.05, -0.20, 1.35),
        "turret_ring_location": (0.0, 0.22, 1.35), "turret_ring_radius": 0.68,
        "coaxial_location": (0.30, 0.88, 1.52),
        "weapon_trunnion_center": (0.0, 1.02, 1.52),
        "drive_center": (0.0, -1.30, 0.36),
        "idler_center": (0.0, 1.30, 0.36),
    },
    "CommandRelay": {
        "features": {"access_hatches", "driver_periscope", "turret_ring_guard"},
        "hull_width": 2.20, "hull_length": 2.90, "deck_z": 1.30, "ground_clearance": 0.06,
        "hatch_location": (0.42, -0.42, 1.35),
        "periscope_location": (0.0, 1.20, 1.28),
        "turret_ring_location": (0.0, 0.28, 1.32), "turret_ring_radius": 0.55,
    },
}



_PREFIX_ALIASES: dict[str, tuple[str, ...]] = {
    "SandlanceScout": ("SandlanceScout", "Scout"),
    "Scout": ("SandlanceScout", "Scout"),
    "SkirmishStrikeBike": ("SkirmishStrikeBike", "StrikeBike"),
    "StrikeBike": ("SkirmishStrikeBike", "StrikeBike"),
    "ColossusSiegePlatform": ("ColossusSiegePlatform", "Colossus"),
    "Colossus": ("ColossusSiegePlatform", "Colossus"),
    "AtlasCraneMech": ("AtlasCraneMech", "Atlas"),
    "Atlas": ("AtlasCraneMech", "Atlas"),
    "BastionSiegeMech": ("BastionSiegeMech", "Bastion"),
    "Bastion": ("BastionSiegeMech", "Bastion"),
}


def _candidate_prefixes(prefix: str) -> tuple[str, ...]:
    return _PREFIX_ALIASES.get(prefix, (prefix,))


def _object_exists(name: str) -> bool:
    if bpy.data.objects.get(name) is not None:
        return True
    for base, aliases in _PREFIX_ALIASES.items():
        if name.startswith(base):
            suffix = name[len(base):]
            return any(bpy.data.objects.get(cand + suffix) is not None for cand in aliases)
    return False


def _object_with_tokens(prefix: str, tokens: tuple[str, ...]):
    lowered = tuple(token.lower() for token in tokens)
    candidates = _candidate_prefixes(prefix)
    for obj in bpy.context.scene.objects:
        if any(obj.name.startswith(cand) for cand in candidates) and all(token in obj.name.lower() for token in lowered):
            return obj
    return None


def _add_wave34_drive_axles(prefix: str, profile: Mapping[str, object], gunmetal, armor) -> list:
    result = []
    locations = profile.get("axle_locations", [])
    for label, y_pos, z_pos, half_span in locations:
        axle_name = f"{prefix}Chassis_AxleBeam_{label}"
        if not _object_exists(axle_name):
            result.append(rmk.make_rod(
                axle_name,
                (-float(half_span), float(y_pos), float(z_pos)),
                (float(half_span), float(y_pos), float(z_pos)),
                0.058, gunmetal, vertices=8,
            ))
        differential_name = f"{prefix}Chassis_Differential_{label}"
        if not _object_exists(differential_name):
            result.append(rmk.make_cylinder(
                differential_name, 0.13, 0.20,
                (0.0, float(y_pos), float(z_pos)), gunmetal,
                rotation=(0.0, math.radians(90.0), 0.0), vertices=10, bevel=0.012,
            ))
        for side in (-1, 1):
            tag = "L" if side < 0 else "R"
            shock_name = f"{prefix}Chassis_ShockStrut_{label}_{tag}"
            if not _object_exists(shock_name):
                deck_z = float(profile.get("deck_z", float(z_pos) + 0.45))
                result.append(rmk.make_rod(
                    shock_name,
                    (side * float(half_span) * 0.95, float(y_pos), float(z_pos)),
                    (side * float(half_span) * 0.80, float(y_pos), deck_z),
                    0.032, armor, vertices=6,
                ))
    return result


def _add_wave34_sponson_bridges(prefix: str, profile: Mapping[str, object], armor) -> list:
    if _has_object(prefix, "Armor_SponsonBridge"):
        return []
    width = float(profile["hull_width"])
    length = float(profile["hull_length"])
    ground = float(profile.get("ground_clearance", 0.06))
    deck_z = float(profile.get("deck_z", 0.9))
    bridge_width = max(0.08, width * 0.13)
    bridge_length = length * 0.76
    bridge_z = ground + max(0.34, (deck_z - ground) * 0.46)
    result = []
    for side in (-1, 1):
        tag = "L" if side < 0 else "R"
        result.append(rmk.make_box(
            f"{prefix}Armor_SponsonBridge_{tag}",
            (bridge_width, bridge_length, 0.06),
            (side * width * 0.37, 0.0, bridge_z), armor, bevel=0.012,
        ))
    return result


def _add_wave34_engine_cavity(prefix: str, profile: Mapping[str, object], gunmetal) -> list:
    name = f"{prefix}Engine_RadiatorCavity"
    if _object_exists(name):
        return []
    width = float(profile["hull_width"])
    length = float(profile["hull_length"])
    deck_z = float(profile.get("deck_z", 0.9))
    size = (width * 0.30, length * 0.20, 0.07)
    return [rmk.make_box(
        name, (size[0] * 0.90, size[1] * 0.88, size[2] * 0.45),
        (0.0, -length * 0.30, deck_z + 0.10 - size[2] * 0.15),
        gunmetal, bevel=0.008,
    )]


def _add_wave34_antenna_collar(prefix: str, profile: Mapping[str, object], armor) -> list:
    name = f"{prefix}Sensor_AntennaDeckCollar"
    if _object_exists(name):
        return []
    location = profile.get("antenna_location")
    if location is None:
        base = _object_with_tokens(prefix, ("Antenna", "Base"))
        if base is None:
            base = _object_with_tokens(prefix, ("WhipAntenna", "Bracket"))
        if base is None:
            return []
        location = tuple(base.matrix_world.translation)
    return [rmk.make_cylinder(
        name, 0.062, 0.06,
        Vector(location) - Vector((0.0, 0.0, 0.03)), armor,
        vertices=10, bevel=0.008,
    )]


def _add_wave34_gatling_mounts(prefix: str, profile: Mapping[str, object], armor, gunmetal) -> list:
    locations = profile.get("gatling_locations", [])
    result = []
    for index, location in enumerate(locations):
        side = -1 if float(location[0]) < 0.0 else 1
        tag = "L" if side < 0 else "R"
        saddle_name = f"{prefix}Gatling_PintleSaddle_{tag}"
        if _object_exists(saddle_name):
            continue
        pivot = Vector(location) - Vector((0.0, 0.06, 0.12))
        result.extend([
            rmk.make_box(saddle_name, (0.18, 0.26, 0.16), pivot, armor, bevel=0.012),
            rmk.make_cylinder(
                f"{prefix}Gatling_PintlePin_{tag}", 0.055, 0.22, pivot, gunmetal,
                rotation=(0.0, math.radians(90.0), 0.0), vertices=8, bevel=0.006,
            ),
        ])
    return result


def _add_wave34_missile_mount(prefix: str, profile: Mapping[str, object], armor, gunmetal) -> list:
    name = f"{prefix}MissilePod_TrunnionCradle"
    if _object_exists(name):
        return []
    location = profile.get("missile_location")
    if location is None:
        pod = _object_with_tokens(prefix, ("Missile", "Pod"))
        if pod is None:
            pod = _object_with_tokens(prefix, ("Missile", "Canister"))
        if pod is None:
            return []
        location = tuple(pod.matrix_world.translation)
    pod_loc = Vector(location)
    return [
        rmk.make_box(name, (0.52, 0.62, 0.18), pod_loc - Vector((0.0, 0.0, 0.18)), armor, bevel=0.015),
        rmk.make_cylinder(
            f"{prefix}MissilePod_TrunnionAxle", 0.085, 0.68,
            pod_loc - Vector((0.0, 0.0, 0.08)), gunmetal,
            rotation=(0.0, math.radians(90.0), 0.0), vertices=10, bevel=0.008,
        ),
    ]


def _add_wave34_arc_mount(prefix: str, profile: Mapping[str, object], armor) -> list:
    name = f"{prefix}Arc_PedestalCollar"
    if _object_exists(name):
        return []
    location = profile.get("arc_location")
    if location is None:
        emitter = _object_with_tokens(prefix, ("Arc",))
        if emitter is None:
            return []
        location = tuple(emitter.matrix_world.translation)
    return [rmk.make_cylinder(
        name, 0.34, 0.12,
        Vector(location) - Vector((0.0, 0.05, 0.04)), armor,
        rotation=(math.radians(90.0), 0.0, 0.0), vertices=12, bevel=0.012,
    )]


def _add_wave34_radar_mount(prefix: str, profile: Mapping[str, object], armor) -> list:
    name = f"{prefix}Radar_DeckPylon"
    if _object_exists(name):
        return []
    location = profile.get("radar_location")
    if location is None:
        radar = _object_with_tokens(prefix, ("Radar",))
        if radar is None:
            return []
        location = tuple(radar.matrix_world.translation)
    pylon_h = 0.16
    return [rmk.make_cylinder(
        name, 0.12, pylon_h,
        Vector(location) - Vector((0.0, 0.0, pylon_h * 0.5)), armor,
        vertices=10, bevel=0.015,
    )]


def _add_wave34_smoke_brackets(prefix: str, profile: Mapping[str, object], armor) -> list:
    locations = profile.get("smoke_locations", [])
    result = []
    for index, location in enumerate(locations):
        tag = "L" if index == 0 else "R"
        name = f"{prefix}Smoke_MountBracket_{tag}"
        if not _object_exists(name):
            side = -1 if float(location[0]) < 0.0 else 1
            result.append(rmk.make_box(
                name, (0.16, 0.18, 0.08),
                Vector(location) - Vector((side * 0.06, -0.04, 0.04)), armor,
                bevel=0.008, rotation=(0.0, side * math.radians(20.0), 0.0),
            ))
    return result


def _find_aliased_object(prefix: str, suffix: str):
    for cand in _candidate_prefixes(prefix):
        obj = bpy.data.objects.get(cand + suffix)
        if obj is not None:
            return obj
    return None


def _add_wave34_leg_mounts(prefix: str, armor, gunmetal) -> list:
    result = []
    for side, tag in ((-1, "L"), (1, "R")):
        hip = _find_aliased_object(prefix, f"Leg_HipJoint_{tag}")
        if hip is not None and not _object_exists(f"{prefix}Leg_HipFlange_{tag}"):
            result.append(rmk.make_cylinder(
                f"{prefix}Leg_HipFlange_{tag}", 0.22, 0.06,
                hip.matrix_world.translation - Vector((side * 0.06, 0.0, 0.0)),
                armor, rotation=(0.0, math.radians(90.0), 0.0), vertices=10, bevel=0.012,
            ))
        shin = _find_aliased_object(prefix, f"Leg_ShinArmor_{tag}")
        if shin is not None and not _object_exists(f"{prefix}Leg_CalfArmor_{tag}"):
            result.append(rmk.make_box(
                f"{prefix}Leg_CalfArmor_{tag}", (0.18, 0.16, 0.28),
                shin.matrix_world.translation - Vector((0.0, 0.08, 0.0)),
                armor, bevel=0.02, rotation=tuple(shin.rotation_euler),
            ))
        ankle = _find_aliased_object(prefix, f"Leg_AnkleJoint_{tag}")
        if ankle is not None and not _object_exists(f"{prefix}Leg_AnkleClevis_{tag}"):
            result.append(rmk.make_box(
                f"{prefix}Leg_AnkleClevis_{tag}", (0.16, 0.18, 0.10),
                ankle.matrix_world.translation - Vector((0.0, 0.0, 0.04)),
                gunmetal, bevel=0.012,
            ))
    return result


def _add_wave34_gussets(prefix: str, profile: Mapping[str, object], armor) -> list:
    locations = profile.get("gusset_locations", [])
    if not locations or _has_object(prefix, "Armor_Gusset_"):
        return []
    return rmk.make_interlocking_armor_gusset(
        f"{prefix}Armor", locations=locations,
        size=(0.05, 0.22, 0.22), material=armor,
    )



def _add_realism_access_hatches(prefix: str, profile: Mapping[str, object], armor, gunmetal) -> list:
    if _has_object(prefix, "Armor_AccessHatch") or _has_object(prefix, "Armor_Hatch"):
        return []
    loc = profile.get("hatch_location")
    if not loc:
        return []
    return rmk.make_armored_access_hatch_assembly(
        f"{prefix}Armor_AccessHatch",
        location=loc,
        armor_mat=armor,
        gunmetal_mat=gunmetal,
    )


def _add_realism_driver_periscope(prefix: str, profile: Mapping[str, object], armor, gunmetal) -> list:
    if _has_object(prefix, "Sensor_DriverPeriscope") or _has_object(prefix, "Periscope"):
        return []
    loc = profile.get("periscope_location")
    if not loc:
        return []
    size_value = profile.get("periscope_size")
    size = tuple(float(value) for value in size_value) if size_value is not None else (0.24, 0.28, 0.18)
    return rmk.make_driver_periscope_hood(
        f"{prefix}Sensor_DriverPeriscope",
        location=loc,
        armor_mat=armor,
        optic_mat=gunmetal,
        size=size,
    )


def _add_realism_pioneer_racks(prefix: str, profile: Mapping[str, object], armor, gunmetal) -> list:
    if _has_object(prefix, "Armor_PioneerRack") or _has_object(prefix, "PioneerTool"):
        return []
    loc = profile.get("tool_rack_location")
    if not loc:
        return []
    return rmk.make_pioneer_tool_rack_assembly(
        f"{prefix}Armor_PioneerRack",
        location=loc,
        armor_mat=armor,
        gunmetal_mat=gunmetal,
    )


def _add_realism_turret_ring_guard(prefix: str, profile: Mapping[str, object], armor, gunmetal) -> list:
    if _has_object(prefix, "Armor_TurretRingGuard") or _has_object(prefix, "RingGuard"):
        return []
    loc = profile.get("turret_ring_location")
    if not loc:
        return []
    radius = float(profile.get("turret_ring_radius", 0.65))
    return rmk.make_turret_ring_guard(
        f"{prefix}Armor_TurretRingGuard",
        location=loc,
        radius=radius,
        armor_mat=armor,
        gunmetal_mat=gunmetal,
    )


def _add_realism_coaxial_port(prefix: str, profile: Mapping[str, object], armor, gunmetal) -> list:
    if _has_object(prefix, "Weapon_CoaxialPort") or _has_object(prefix, "Coaxial"):
        return []
    loc = profile.get("coaxial_location")
    if not loc:
        return []
    return rmk.make_coaxial_gun_port_assembly(
        f"{prefix}Weapon_CoaxialPort",
        location=loc,
        armor_mat=armor,
        gunmetal_mat=gunmetal,
    )


def _add_realism_weapon_trunnions(prefix: str, profile: Mapping[str, object], armor, gunmetal) -> list:
    if _has_object(prefix, "Armor_TrunnionBracket") or _has_object(prefix, "Trunnion"):
        return []
    loc = profile.get("weapon_trunnion_center")
    if not loc:
        return []
    spacing = float(profile.get("trunnion_spacing", 0.60))
    objects = rmk.make_weapon_trunnion_bracket_assembly(
        f"{prefix}Armor_TrunnionBracket",
        pivot_center=loc,
        spacing_x=spacing,
        bracket_height=float(profile.get("trunnion_bracket_height", 0.22)),
        bracket_width=float(profile.get("trunnion_bracket_width", 0.12)),
        bracket_depth=float(profile.get("trunnion_bracket_depth", 0.28)),
        armor_mat=armor,
        gunmetal_mat=gunmetal,
    )
    if prefix == "BulwarkSiegeTank":
        # The generic bottom flange sits below the Bulwark's tapered bearing
        # cheek and is not seated to the turret shell. The cheek itself spans
        # the trunnion socket and is the functional mount, so omit the floating
        # redundant flange on this chassis.
        kept = []
        for obj in objects:
            if "_MountFlange_" in obj.name:
                mesh = obj.data if obj.type == "MESH" else None
                bpy.data.objects.remove(obj, do_unlink=True)
                if mesh is not None and mesh.users == 0:
                    bpy.data.meshes.remove(mesh)
                continue
            kept.append(obj)
        objects = kept
    return objects


def _add_realism_final_drive(prefix: str, profile: Mapping[str, object], armor, gunmetal) -> list:
    if _has_object(prefix, "Chassis_FinalDrive") or _has_object(prefix, "FinalDrive"):
        return []
    loc = profile.get("drive_center")
    if not loc:
        return []
    width = float(profile.get("hull_width", 2.4))
    return rmk.make_final_drive_housing_assembly(
        f"{prefix}Chassis_FinalDrive",
        hub_center=loc,
        track_width=width,
        armor_mat=armor,
        gunmetal_mat=gunmetal,
    )


def _add_realism_track_tensioners(prefix: str, profile: Mapping[str, object], armor, gunmetal) -> list:
    if _has_object(prefix, "Chassis_TrackTensioner") or _has_object(prefix, "TrackTensioner"):
        return []
    loc = profile.get("idler_center")
    if not loc:
        return []
    width = float(profile.get("hull_width", 2.4))
    return rmk.make_track_tensioner_idler_yoke_assembly(
        f"{prefix}Chassis_TrackTensioner",
        idler_center=loc,
        track_width=width,
        armor_mat=armor,
        gunmetal_mat=gunmetal,
    )


def apply_realism_details(
    unit_name: str,
    objects: list | None = None,
    materials: Mapping[str, object] | None = None,
    object_prefix: str | None = None,
) -> list:
    """Add Wave 11-13 hard-surface realism assemblies to active pack unit."""
    if unit_name not in ACTIVE_UNIT_REALISM_PROFILES:
        return []
    profile = ACTIVE_UNIT_REALISM_PROFILES[unit_name]
    prefix = object_prefix or unit_name
    features = set(profile["features"])
    if unit_name == "BulwarkSiegeTank":
        # These assemblies were previously baked into the promoted source
        # replay. Reconcile them against the corrected Bulwark-specific mounts:
        # the old ring collar had no deck seat, and trunnions/periscope must be
        # regenerated at their validated hardpoints rather than silently kept
        # at their obsolete transforms.
        stale_clusters = ["Armor_TurretRingGuard_"]
        if "driver_periscope" in features:
            stale_clusters.append("Sensor_DriverPeriscope_")
        if "weapon_trunnions" in features:
            stale_clusters.append("Armor_TrunnionBracket_")
        if "coaxial_port" in features:
            stale_clusters.append("Weapon_CoaxialPort_")
        for suffix in stale_clusters:
            for obj in list(bpy.context.scene.objects):
                if obj.name.startswith(prefix + suffix):
                    mesh = obj.data if obj.type == "MESH" else None
                    bpy.data.objects.remove(obj, do_unlink=True)
                    if mesh is not None and mesh.users == 0:
                        bpy.data.meshes.remove(mesh)
    armor = _resolve_material(materials, "armor", (0.18, 0.24, 0.28))
    gunmetal = _resolve_material(materials, "gunmetal", (0.07, 0.09, 0.11))
    result = []
    if "access_hatches" in features:
        result.extend(_add_realism_access_hatches(prefix, profile, armor, gunmetal))
    if "driver_periscope" in features:
        result.extend(_add_realism_driver_periscope(prefix, profile, armor, gunmetal))
    if "pioneer_racks" in features:
        result.extend(_add_realism_pioneer_racks(prefix, profile, armor, gunmetal))
    if "turret_ring_guard" in features:
        result.extend(_add_realism_turret_ring_guard(prefix, profile, armor, gunmetal))
    if "coaxial_port" in features:
        result.extend(_add_realism_coaxial_port(prefix, profile, armor, gunmetal))
    if "weapon_trunnions" in features:
        result.extend(_add_realism_weapon_trunnions(prefix, profile, armor, gunmetal))
    if "final_drive" in features:
        result.extend(_add_realism_final_drive(prefix, profile, armor, gunmetal))
    if "track_tensioners" in features:
        result.extend(_add_realism_track_tensioners(prefix, profile, armor, gunmetal))
    return result


ACTIVE_UNIT_WAVE14_ARCHETYPE_PROFILES: dict[str, dict[str, object]] = {
    "ArcLanceWalker": {
        "pistons": [
            ("Leg_HipActuator_L", (-0.42, -0.08, 1.32), (-0.54, 0.08, 0.88), 0.038, 0.024),
            ("Leg_HipActuator_R", (0.42, -0.08, 1.32), (0.54, 0.08, 0.88), 0.038, 0.024),
            ("Leg_AnkleDamper_L", (-0.52, -0.06, 0.72), (-0.52, -0.10, 0.28), 0.032, 0.020),
            ("Leg_AnkleDamper_R", (0.52, -0.06, 0.72), (0.52, -0.10, 0.28), 0.032, 0.020),
        ],
        "cables": [
            ("Weapon_LancePowerCable_L", (-0.36, -0.24, 1.72), (-0.52, 0.22, 1.56), 0.06, 0.020),
            ("Weapon_LancePowerCable_R", (0.36, -0.24, 1.72), (0.52, 0.22, 1.56), 0.06, 0.020),
        ],
        "greeble_vents": (1.10, 1.00, 1.74, (0.0, -0.18, 0.0)),
        "weld_seams": [
            ("Armor_WeldSeam_ChestL", (-0.44, 0.34, 1.52), (-0.44, -0.28, 1.52)),
            ("Armor_WeldSeam_ChestR", (0.44, 0.34, 1.52), (0.44, -0.28, 1.52)),
            ("Armor_WeldSeam_Sternum", (-0.32, 0.42, 1.62), (0.32, 0.42, 1.62)),
        ],
    },
    "IroncladWalker": {
        "pistons": [
            ("Leg_HipActuator_L", (-0.44, -0.06, 1.24), (-0.52, 0.08, 0.82), 0.038, 0.024),
            ("Leg_HipActuator_R", (0.44, -0.06, 1.24), (0.52, 0.08, 0.82), 0.038, 0.024),
        ],
        "ammo_chutes": [
            ("Weapon_GatlingFeedChute_L", (-0.44, -0.24, 1.80), (-0.64, 0.06, 1.58), 0.11, 0.038, 0.06, -0.04),
            ("Weapon_GatlingFeedChute_R", (0.44, -0.24, 1.80), (0.64, 0.06, 1.58), 0.11, 0.038, 0.06, 0.04),
        ],
        "greeble_vents": (0.86, 0.94, 1.98, (0.0, -0.12, 0.0)),
        "weld_seams": [
            ("Armor_WeldSeam_TorsoL", (-0.46, 0.36, 1.72), (-0.46, -0.28, 1.72)),
            ("Armor_WeldSeam_TorsoR", (0.46, 0.36, 1.72), (0.46, -0.28, 1.72)),
            ("Armor_WeldSeam_Glacis", (-0.36, 0.46, 1.84), (0.36, 0.46, 1.84)),
        ],
    },
    "AtlasCraneMech": {
        "pistons": [
            ("Leg_HipActuator_L", (-0.48, -0.08, 1.38), (-0.58, 0.10, 0.92), 0.042, 0.026),
            ("Leg_HipActuator_R", (0.48, -0.08, 1.38), (0.58, 0.10, 0.92), 0.042, 0.026),
            ("Turret_BoomLiftCylinder_L", (-0.28, -0.18, 1.92), (-0.28, 0.28, 2.28), 0.044, 0.028),
            ("Turret_BoomLiftCylinder_R", (0.28, -0.18, 1.92), (0.28, 0.28, 2.28), 0.044, 0.028),
        ],
        "cables": [
            ("Turret_CraneHydraulicHose_L", (-0.42, -0.26, 1.78), (-0.48, 0.24, 2.02), 0.07, 0.022),
            ("Turret_CraneHydraulicHose_R", (0.42, -0.26, 1.78), (0.48, 0.24, 2.02), 0.07, 0.022),
        ],
        "greeble_vents": (1.05, 1.10, 1.86, (0.0, -0.15, 0.0)),
        "weld_seams": [
            ("Armor_WeldSeam_TorsoL", (-0.52, 0.38, 1.68), (-0.52, -0.34, 1.68)),
            ("Armor_WeldSeam_TorsoR", (0.52, 0.38, 1.68), (0.52, -0.34, 1.68)),
            ("Armor_WeldSeam_Breastplate", (-0.38, 0.44, 1.78), (0.38, 0.44, 1.78)),
        ],
    },
    "BastionSiegeMech": {
        "pistons": [
            ("Turret_RecoilRecuperator_L", (-0.48, -0.12, 2.18), (-0.48, 0.62, 2.22), 0.052, 0.034),
            ("Turret_RecoilRecuperator_R", (0.48, -0.12, 2.18), (0.48, 0.62, 2.22), 0.052, 0.034),
            ("Armor_CarapaceDamper_L", (-0.82, -0.45, 1.54), (-1.18, -0.45, 1.18), 0.046, 0.030),
            ("Armor_CarapaceDamper_R", (0.82, -0.45, 1.54), (1.18, -0.45, 1.18), 0.046, 0.030),
        ],
        "cables": [
            ("Turret_SiegeFeederConduit_L", (-0.64, -0.58, 1.76), (-0.56, 0.14, 2.04), 0.08, 0.026),
            ("Turret_SiegeFeederConduit_R", (0.64, -0.58, 1.76), (0.56, 0.14, 2.04), 0.08, 0.026),
        ],
        "greeble_vents": (1.80, 1.70, 1.72, (0.0, -0.25, 0.0)),
        "weld_seams": [
            ("Armor_WeldSeam_CarapaceL", (-0.88, 0.65, 1.62), (-0.88, -0.65, 1.62)),
            ("Armor_WeldSeam_CarapaceR", (0.88, 0.65, 1.62), (0.88, -0.65, 1.62)),
            ("Armor_WeldSeam_Glacis", (-0.68, 0.86, 1.56), (0.68, 0.86, 1.56)),
        ],
    },
    "SkirmishStrikeBike": {
        "pistons": [
            ("Chassis_ForkDamper_L", (-0.16, 0.56, 0.86), (-0.16, 0.78, 0.38), 0.030, 0.020),
            ("Chassis_ForkDamper_R", (0.16, 0.56, 0.86), (0.16, 0.78, 0.38), 0.030, 0.020),
            ("Chassis_RearMonoShock", (0.0, -0.22, 0.72), (0.0, -0.62, 0.42), 0.036, 0.022),
        ],
        "ammo_chutes": [
            ("Weapon_TwinGunChute_L", (-0.22, 0.12, 0.78), (-0.26, 0.52, 0.74), 0.075, 0.028, 0.04, -0.02),
            ("Weapon_TwinGunChute_R", (0.22, 0.12, 0.78), (0.26, 0.52, 0.74), 0.075, 0.028, 0.04, 0.02),
        ],
        "radiators": (1.40, 0.68, 0.64, 4),
        "bike_hardware": True,
        "weld_seams": [
            ("Armor_WeldSeam_CowlingL", (-0.24, 0.58, 0.84), (-0.24, -0.32, 0.76)),
            ("Armor_WeldSeam_CowlingR", (0.24, 0.58, 0.84), (0.24, -0.32, 0.76)),
            ("Armor_WeldSeam_NoseSpine", (0.0, 0.82, 0.78), (0.0, 0.24, 0.94)),
        ],
    },
    "TidebreakerHovercraft": {
        "pistons": [
            ("Hover_VaneActuator_L", (-0.68, -1.52, 1.18), (-0.56, -1.96, 1.12), 0.034, 0.022),
            ("Hover_VaneActuator_R", (0.68, -1.52, 1.18), (0.56, -1.96, 1.12), 0.034, 0.022),
        ],
        "cables": [
            ("Hover_PlenumFeedHose_L", (-0.86, 0.45, 0.72), (-0.86, -0.85, 0.76), 0.06, 0.024),
            ("Hover_PlenumFeedHose_R", (0.86, 0.45, 0.72), (0.86, -0.85, 0.76), 0.06, 0.024),
        ],
        "greeble_vents": (2.80, 2.00, 1.06, (0.0, -0.30, 0.0)),
        "glacis_rib": ((0.0, 1.72, 0.88), 1.45, 0.07, 0.06, 22.0),
        "hover_thrusters": True,
        "weld_seams": [
            ("Armor_WeldSeam_DeckL", (-0.98, 1.45, 0.92), (-0.98, -1.45, 0.92)),
            ("Armor_WeldSeam_DeckR", (0.98, 1.45, 0.92), (0.98, -1.45, 0.92)),
            ("Armor_WeldSeam_BowGlacis", (-0.72, 1.82, 0.82), (0.72, 1.82, 0.82)),
        ],
    },
    "ColossusSiegePlatform": {
        "pistons": [
            ("Turret_MortarRecuperator_L", (-0.42, -0.25, 1.48), (-0.42, 0.85, 1.56), 0.050, 0.032),
            ("Turret_MortarRecuperator_R", (0.42, -0.25, 1.48), (0.42, 0.85, 1.56), 0.050, 0.032),
        ],
        "earth_spade": ((0.0, -1.38, 0.46), 1.38, 0.34),
        "commander_sight": ((0.38, -0.35, 1.62), (0.24, 0.24, 0.26)),
        "glacis_rib": ((0.0, 1.32, 0.84), 1.32, 0.07, 0.06, 20.0),
        "weld_seams": [
            ("Armor_WeldSeam_CasemateL", (-0.82, 0.95, 1.18), (-0.82, -1.05, 1.18)),
            ("Armor_WeldSeam_CasemateR", (0.82, 0.95, 1.18), (0.82, -1.05, 1.18)),
            ("Armor_WeldSeam_Glacis", (-0.68, 1.38, 0.88), (0.68, 1.38, 0.88)),
        ],
    },
    "SurgeArcTank": {
        "pistons": [
            ("Turret_ArcCoilStabilizer_L", (-0.38, -0.22, 1.18), (-0.32, 0.48, 1.26), 0.038, 0.024),
            ("Turret_ArcCoilStabilizer_R", (0.38, -0.22, 1.18), (0.32, 0.48, 1.26), 0.038, 0.024),
        ],
        "cables": [
            ("Turret_CapacitorBusCable_L", (-0.56, -0.68, 0.96), (-0.42, 0.12, 1.18), 0.07, 0.024),
            ("Turret_CapacitorBusCable_R", (0.56, -0.68, 0.96), (0.42, 0.12, 1.18), 0.07, 0.024),
        ],
        "radiators": (2.20, 1.56, 0.88, 5),
        "glacis_rib": ((0.0, 1.12, 0.78), 1.08, 0.06, 0.05, 22.0),
        "weld_seams": [
            ("Armor_WeldSeam_SponsonL", (-0.68, 0.92, 0.88), (-0.68, -0.92, 0.88)),
            ("Armor_WeldSeam_SponsonR", (0.68, 0.92, 0.88), (0.68, -0.92, 0.88)),
            ("Armor_WeldSeam_Glacis", (-0.54, 1.16, 0.76), (0.54, 1.16, 0.76)),
        ],
    },
    "SandlanceScout": {
        "commander_sight": ((0.26, -0.18, 1.38), (0.22, 0.22, 0.24)),
        "stowage_basket": ((0.0, -0.86, 1.14), (0.86, 0.30, 0.18)),
        "glacis_rib": ((0.0, 0.98, 0.74), 1.04, 0.06, 0.05, 24.0),
        "greeble_vents": (2.10, 1.56, 0.86, (0.0, -0.22, 0.0)),
        "weld_seams": [
            ("Armor_WeldSeam_HullL", (-0.66, 0.84, 0.84), (-0.66, -0.84, 0.84)),
            ("Armor_WeldSeam_HullR", (0.66, 0.84, 0.84), (0.66, -0.84, 0.84)),
            ("Armor_WeldSeam_Glacis", (-0.52, 1.04, 0.72), (0.52, 1.04, 0.72)),
        ],
    },
    "FieldMedicRover": {
        "nanite_dispenser": ((0.0, -0.48, 1.18), 0.55, 0.10, 0.32, 1.12),
        "commander_sight": ((0.0, 0.42, 1.48), (0.22, 0.22, 0.24)),
        "glacis_rib": ((0.0, 1.24, 0.82), 1.08, 0.06, 0.05, 20.0),
        "greeble_vents": (2.20, 1.62, 0.98, (0.0, -0.20, 0.0)),
        "weld_seams": [
            ("Armor_WeldSeam_CabinL", (-0.68, 0.88, 0.98), (-0.68, -0.96, 0.98)),
            ("Armor_WeldSeam_CabinR", (0.68, 0.88, 0.98), (0.68, -0.96, 0.98)),
            ("Armor_WeldSeam_Nose", (-0.52, 1.28, 0.84), (0.52, 1.28, 0.84)),
        ],
    },
    "OverlordCommandHQ": {
        "commander_sight": ((-0.34, 0.28, 1.88), (0.26, 0.26, 0.28)),
        "stowage_basket": ((0.0, -1.62, 1.34), (1.06, 0.36, 0.22)),
        "radiators": (3.20, 1.92, 1.14, 6),
        "cables": [
            ("Armor_CommandUmbilical_L", (-0.72, -0.88, 1.28), (-0.48, 0.12, 1.64), 0.08, 0.024),
            ("Armor_CommandUmbilical_R", (0.72, -0.88, 1.28), (0.48, 0.12, 1.64), 0.08, 0.024),
        ],
        "weld_seams": [
            ("Armor_WeldSeam_CitadelL", (-0.82, 1.25, 1.18), (-0.82, -1.35, 1.18)),
            ("Armor_WeldSeam_CitadelR", (0.82, 1.25, 1.18), (0.82, -1.35, 1.18)),
            ("Armor_WeldSeam_Prow", (-0.66, 1.58, 1.04), (0.66, 1.58, 1.04)),
        ],
    },
    "SkyguardSAM": {
        "pistons": [
            ("Turret_LauncherElevCylinder_L", (-0.38, -0.42, 1.18), (-0.38, 0.14, 1.68), 0.042, 0.026),
            ("Turret_LauncherElevCylinder_R", (0.38, -0.42, 1.18), (0.38, 0.14, 1.68), 0.042, 0.026),
        ],
        "stowage_basket": ((0.0, -1.38, 1.12), (0.92, 0.32, 0.18)),
        "greeble_vents": (2.40, 1.68, 0.96, (0.0, -0.25, 0.0)),
        "weld_seams": [
            ("Armor_WeldSeam_DeckL", (-0.72, 1.05, 0.94), (-0.72, -1.15, 0.94)),
            ("Armor_WeldSeam_DeckR", (0.72, 1.05, 0.94), (0.72, -1.15, 0.94)),
            ("Armor_WeldSeam_Glacis", (-0.56, 1.32, 0.84), (0.56, 1.32, 0.84)),
        ],
    },
    "SalvageHauler": {
        "pistons": [
            ("Turret_CraneLiftCylinder_L", (-0.42, -0.18, 1.16), (-0.42, 0.42, 1.58), 0.044, 0.028),
            ("Turret_CraneLiftCylinder_R", (0.42, -0.18, 1.16), (0.42, 0.42, 1.58), 0.044, 0.028),
        ],
        "cables": [
            ("Turret_WinchHydraulicLine_L", (-0.58, -0.72, 1.12), (-0.46, 0.22, 1.44), 0.07, 0.024),
            ("Turret_WinchHydraulicLine_R", (0.58, -0.72, 1.12), (0.46, 0.22, 1.44), 0.07, 0.024),
        ],
        "stowage_basket": ((0.0, -1.42, 1.14), (1.02, 0.34, 0.20)),
        "weld_seams": [
            ("Armor_WeldSeam_BedL", (-0.84, 1.10, 0.96), (-0.84, -1.20, 0.96)),
            ("Armor_WeldSeam_BedR", (0.84, 1.10, 0.96), (0.84, -1.20, 0.96)),
            ("Armor_WeldSeam_Bumper", (-0.64, 1.48, 0.82), (0.64, 1.48, 0.82)),
        ],
    },
    "SentinelRelay": {
        "pistons": [
            ("Sensor_MastStayCylinder_L", (-0.42, -0.72, 1.14), (-0.12, -0.72, 1.68), 0.032, 0.020),
            ("Sensor_MastStayCylinder_R", (0.42, -0.72, 1.14), (0.12, -0.72, 1.68), 0.032, 0.020),
        ],
        "cables": [
            ("Sensor_WaveguideFeeder_L", (-0.36, -0.32, 1.16), (-0.10, -0.68, 1.58), 0.06, 0.020),
            ("Sensor_WaveguideFeeder_R", (0.36, -0.32, 1.16), (0.10, -0.68, 1.58), 0.06, 0.020),
        ],
        "radiators": (2.10, 1.52, 0.96, 4),
        "weld_seams": [
            ("Armor_WeldSeam_BayL", (-0.56, 0.65, 0.96), (-0.56, -1.05, 0.96)),
            ("Armor_WeldSeam_BayR", (0.56, 0.65, 0.96), (0.56, -1.05, 0.96)),
            ("Armor_WeldSeam_Cowl", (-0.44, 0.78, 1.02), (0.44, 0.78, 1.02)),
        ],
    },
    "CommandRelay": {
        "pistons": [
            ("CommsMast_MastElevStrut_L", (-0.44, -1.35, 1.24), (-0.18, -1.35, 1.78), 0.034, 0.022),
            ("CommsMast_MastElevStrut_R", (0.44, -1.35, 1.24), (0.18, -1.35, 1.78), 0.034, 0.022),
        ],
        "cables": [
            ("CommsMast_C2FeederConduit_L", (-0.48, -0.45, 1.28), (-0.24, -1.28, 1.64), 0.07, 0.022),
            ("CommsMast_C2FeederConduit_R", (0.48, -0.45, 1.28), (0.24, -1.28, 1.64), 0.07, 0.022),
        ],
        "radiators": (2.50, 1.68, 1.04, 5),
        "weld_seams": [
            ("Armor_WeldSeam_ShelterL", (-0.64, 1.10, 1.02), (-0.64, -1.40, 1.02)),
            ("Armor_WeldSeam_ShelterR", (0.64, 1.10, 1.02), (0.64, -1.40, 1.02)),
            ("Armor_WeldSeam_CabFront", (-0.48, 1.48, 0.98), (0.48, 1.48, 0.98)),
        ],
    },
    "ArachnidHexapod": {
        "pistons": [
            ("Piston_Quad_Coxa_FL", (-0.82, 0.72, 1.14), (-1.28, 1.02, 0.96), 0.036, 0.022),
            ("Piston_Quad_Coxa_FR", (0.82, 0.72, 1.14), (1.28, 1.02, 0.96), 0.036, 0.022),
            ("Piston_Quad_Coxa_ML", (-0.88, 0.00, 1.12), (-1.36, 0.00, 0.92), 0.036, 0.022),
            ("Piston_Quad_Coxa_MR", (0.88, 0.00, 1.12), (1.36, 0.00, 0.92), 0.036, 0.022),
            ("Piston_Quad_Coxa_RL", (-0.82, -0.72, 1.14), (-1.28, -1.02, 0.96), 0.036, 0.022),
            ("Piston_Quad_Coxa_RR", (0.82, -0.72, 1.14), (1.28, -1.02, 0.96), 0.036, 0.022),
        ],
        "cables": [
            ("Armor_BioPlasmaConduit_L", (-0.54, -0.68, 1.32), (-0.42, 0.62, 1.24), 0.07, 0.024),
            ("Armor_BioPlasmaConduit_R", (0.54, -0.68, 1.32), (0.42, 0.62, 1.24), 0.07, 0.024),
        ],
        "weld_seams": [
            ("Armor_WeldSeam_CarapaceL", (-0.68, 0.82, 1.26), (-0.68, -0.82, 1.26)),
            ("Armor_WeldSeam_CarapaceR", (0.68, 0.82, 1.26), (0.68, -0.82, 1.26)),
            ("Armor_WeldSeam_MandibleBridge", (-0.46, 0.96, 1.14), (0.46, 0.96, 1.14)),
        ],
    },
}


def apply_wave14_archetype_details(
    unit_name: str,
    objects: list | None = None,
    materials: Mapping[str, object] | None = None,
    object_prefix: str | None = None,
) -> list:
    """Add Wave 14 archetype-specific mechanical engineering assemblies to non-flagship units."""
    if unit_name not in ACTIVE_UNIT_WAVE14_ARCHETYPE_PROFILES:
        return []
    profile = ACTIVE_UNIT_WAVE14_ARCHETYPE_PROFILES[unit_name]
    prefix = object_prefix or unit_name
    armor = _resolve_material(materials, "armor", (0.18, 0.24, 0.28))
    gunmetal = _resolve_material(materials, "gunmetal", (0.07, 0.09, 0.11))
    warning = _resolve_material(materials, "warning", (0.85, 0.28, 0.04))
    tread = _resolve_material(materials, "tread", (0.035, 0.04, 0.045))
    result = []

    for suffix, start_pt, end_pt, outer_r, inner_r in profile.get("pistons", []):
        if not _has_object(prefix, suffix):
            result.extend(rmk.make_telescopic_piston_assembly(
                f"{prefix}{suffix}",
                start_point=start_pt,
                end_point=end_pt,
                outer_radius=float(outer_r),
                inner_radius=float(inner_r),
                stroke_ratio=0.55,
                housing_mat=gunmetal,
                rod_mat=armor,
                seal_mat=tread,
                accent_mat=warning,
            ))

    for suffix, start_pt, end_pt, width, thickness, sag, bow in profile.get("ammo_chutes", []):
        if not _has_object(prefix, suffix):
            result.extend(rmk.make_ammo_belt_chute_assembly(
                f"{prefix}{suffix}",
                start_point=start_pt,
                end_point=end_pt,
                chute_width=float(width),
                chute_thickness=float(thickness),
                sag=float(sag),
                lateral_bow=float(bow),
                link_count=7,
                hopper_mat=armor,
                chute_mat=gunmetal,
                brass_mat=warning,
            ))

    for suffix, start_pt, end_pt, sag, radius in profile.get("cables", []):
        if not _has_object(prefix, suffix):
            result.extend(rmk.make_catenary_cable_assembly(
                f"{prefix}{suffix}",
                start_point=start_pt,
                end_point=end_pt,
                sag=float(sag),
                radius=float(radius),
                segments=8,
                sides=8,
                clamp_count=2,
                hose_mat=tread,
                coupler_mat=gunmetal,
                accent_mat=warning,
            ))

    greeble_cfg = profile.get("greeble_vents")
    if greeble_cfg is not None and not _has_object(prefix, "Greeble_LouverVent_"):
        h_len, h_wid, d_z, d_center = greeble_cfg
        result.extend(rmk.make_bmesh_hull_greebles(
            prefix,
            hull_length=float(h_len),
            hull_width=float(h_wid),
            deck_z=float(d_z),
            deck_center=d_center,
            armor_mat=armor,
            metal_mat=gunmetal,
            accent_mat=warning,
            include_lugs=False,
            include_vents=True,
        ))

    rad_cfg = profile.get("radiators")
    if rad_cfg is not None and not _has_object(prefix, "Thermal_RadiatorShroud_"):
        h_len, h_wid, d_z, f_cnt = rad_cfg
        result.extend(rmk.make_thermal_radiator_assembly(
            f"{prefix}Thermal",
            hull_length=float(h_len),
            hull_width=float(h_wid),
            deck_z=float(d_z),
            fin_count=int(f_cnt),
            housing_mat=armor,
            fin_mat=gunmetal,
            glow_mat=warning,
            pipe_mat=gunmetal,
        ))

    spade_cfg = profile.get("earth_spade")
    if spade_cfg is not None and not _has_object(prefix, "Chassis_RecoilSpade_"):
        loc, sp_w, sp_d = spade_cfg
        result.extend(rmk.make_recoil_earth_spade_assembly(
            f"{prefix}Chassis_RecoilSpade",
            location=loc,
            width=float(sp_w),
            depth=float(sp_d),
            deployed=False,
            blade_mat=armor,
            strut_mat=gunmetal,
            ram_mat=gunmetal,
            rod_mat=warning,
        ))

    sight_cfg = profile.get("commander_sight")
    if sight_cfg is not None and not _has_object(prefix, "Sensor_PanoramicSight_"):
        loc, sz = sight_cfg
        result.extend(rmk.make_panoramic_commander_sight(
            f"{prefix}Sensor_PanoramicSight",
            location=loc,
            size=sz,
            armor_mat=armor,
            lens_mat=warning,
            metal_mat=gunmetal,
        ))

    basket_cfg = profile.get("stowage_basket")
    if basket_cfg is not None and not _has_object(prefix, "Armor_StowageBasket_"):
        loc, sz = basket_cfg
        result.extend(rmk.make_bustle_stowage_basket(
            f"{prefix}Armor_StowageBasket",
            location=loc,
            size=sz,
            rail_radius=0.016,
            frame_mat=gunmetal,
            mesh_mat=armor,
        ))

    rib_cfg = profile.get("glacis_rib")
    if rib_cfg is not None and not _has_object(prefix, "Armor_SplashRib_"):
        loc, r_w, r_d, r_h, r_ang = rib_cfg
        result.extend(rmk.make_glacis_splash_rib(
            f"{prefix}Armor_SplashRib",
            location=loc,
            width=float(r_w),
            depth=float(r_d),
            height=float(r_h),
            sweep_angle_deg=float(r_ang),
            material=armor,
        ))

    nanite_cfg = profile.get("nanite_dispenser")
    if nanite_cfg is not None and not _has_object(prefix, "Support_NaniteDispenser_"):
        loc, sc, c_rad, c_h, sp_x = nanite_cfg
        result.extend(rmk.make_nanite_repair_dispenser_assembly(
            f"{prefix}Support_NaniteDispenser",
            location=loc,
            scale=float(sc),
            canister_radius=float(c_rad),
            canister_height=float(c_h),
            spacing_x=float(sp_x),
            canister_mat=armor,
            frame_mat=gunmetal,
            glow_mat=warning,
            hose_mat=tread,
        ))

    if profile.get("bike_hardware") and not _has_object(prefix, "Chassis_BrakeRotor_"):
        result.extend([
            rmk.make_cylinder(
                f"{prefix}Chassis_BrakeRotor_F", 0.16, 0.018,
                (-0.08, 0.78, 0.36), gunmetal,
                rotation=(0.0, math.radians(90.0), 0.0), vertices=14, bevel=0.003,
            ),
            rmk.make_cylinder(
                f"{prefix}Chassis_BrakeRotor_R", 0.16, 0.018,
                (0.08, -0.78, 0.36), gunmetal,
                rotation=(0.0, math.radians(90.0), 0.0), vertices=14, bevel=0.003,
            ),
            rmk.make_box(
                f"{prefix}Chassis_BrakeCaliper_F", (0.042, 0.085, 0.065),
                (-0.09, 0.68, 0.44), warning, bevel=0.006,
            ),
            rmk.make_box(
                f"{prefix}Chassis_BrakeCaliper_R", (0.042, 0.085, 0.065),
                (0.09, -0.68, 0.44), warning, bevel=0.006,
            ),
            rmk.make_box(
                f"{prefix}Chassis_ForkClamp_Upper", (0.36, 0.09, 0.036),
                (0.0, 0.54, 0.84), armor, bevel=0.006,
            ),
            rmk.make_box(
                f"{prefix}Chassis_ForkClamp_Lower", (0.36, 0.09, 0.036),
                (0.0, 0.62, 0.68), armor, bevel=0.006,
            ),
        ])

    if profile.get("hover_thrusters") and not _has_object(prefix, "Hover_ThrustShroud_"):
        for side, tag in ((-1, "L"), (1, "R")):
            sx = side * 0.58
            result.extend([
                rmk.make_box(
                    f"{prefix}Hover_ThrustShroud_{tag}", (0.42, 0.32, 0.38),
                    (sx, -1.92, 1.14), armor, bevel=0.018,
                ),
                rmk.make_box(
                    f"{prefix}Hover_RudderVane_{tag}_Inner", (0.026, 0.28, 0.32),
                    (sx - side * 0.08, -2.04, 1.14), gunmetal, bevel=0.004,
                ),
                rmk.make_box(
                    f"{prefix}Hover_RudderVane_{tag}_Outer", (0.026, 0.28, 0.32),
                    (sx + side * 0.08, -2.04, 1.14), gunmetal, bevel=0.004,
                ),
            ])

    for seam_suffix, start_pt, end_pt in profile.get("weld_seams", []):
        if not _has_object(prefix, seam_suffix):
            result.append(rmk.make_weld_seam_bead(
                f"{prefix}{seam_suffix}",
                start=start_pt,
                end=end_pt,
                radius=0.015,
                bead_spacing=0.048,
                material=gunmetal,
            ))

    return result


def apply_wave34_details(
    unit_name: str,
    objects: list | None = None,
    materials: Mapping[str, object] | None = None,
    object_prefix: str | None = None,
) -> list:
    """Add 55442dd Waves 3/4 anchoring details without replacing authored geometry."""
    if unit_name not in ACTIVE_UNIT_WAVE34_PROFILES:
        raise KeyError(f"No Wave 3/4 profile for active unit {unit_name}")
    profile = ACTIVE_UNIT_WAVE34_PROFILES[unit_name]
    prefix = object_prefix or unit_name
    features = set(profile["features"])
    armor = _resolve_material(materials, "armor", (0.18, 0.24, 0.28))
    gunmetal = _resolve_material(materials, "gunmetal", (0.07, 0.09, 0.11))
    result = []
    if "drive_axles" in features:
        result.extend(_add_wave34_drive_axles(prefix, profile, gunmetal, armor))
    if "sponson_bridges" in features:
        result.extend(_add_wave34_sponson_bridges(prefix, profile, armor))
    if "engine_cavity" in features:
        result.extend(_add_wave34_engine_cavity(prefix, profile, gunmetal))
    if "antenna_collar" in features:
        result.extend(_add_wave34_antenna_collar(prefix, profile, armor))
    if "gatling_mounts" in features:
        result.extend(_add_wave34_gatling_mounts(prefix, profile, armor, gunmetal))
    if "missile_mounts" in features:
        result.extend(_add_wave34_missile_mount(prefix, profile, armor, gunmetal))
    if "arc_mount" in features:
        result.extend(_add_wave34_arc_mount(prefix, profile, armor))
    if "radar_mount" in features:
        result.extend(_add_wave34_radar_mount(prefix, profile, armor))
    if "smoke_brackets" in features:
        result.extend(_add_wave34_smoke_brackets(prefix, profile, armor))
    if "leg_mounts" in features:
        result.extend(_add_wave34_leg_mounts(prefix, armor, gunmetal))
    if "gussets" in features:
        result.extend(_add_wave34_gussets(prefix, profile, armor))
    if unit_name in ACTIVE_UNIT_REALISM_PROFILES:
        result.extend(apply_realism_details(unit_name, objects=objects, materials=materials, object_prefix=object_prefix))
    if unit_name in ACTIVE_UNIT_WAVE14_ARCHETYPE_PROFILES:
        result.extend(apply_wave14_archetype_details(unit_name, objects=objects, materials=materials, object_prefix=object_prefix))
    return result


def _has_object(prefix: str, suffix: str) -> bool:
    candidates = _candidate_prefixes(prefix)
    return any(
        obj.name.startswith(cand + suffix)
        for cand in candidates
        for obj in bpy.context.scene.objects
    )


def _resolve_material(materials: Mapping[str, object] | None, key: str, fallback_color: tuple[float, float, float]):
    if materials and materials.get(key) is not None:
        return materials[key]
    tokens = {
        "armor": ("primaryarmor", "armor", "primary"),
        "gunmetal": ("gunmetal", "metal", "wheelrim", "rim"),
        "warning": ("warning", "amber", "accent", "signal", "orange", "hazard", "emissive"),
        "tread": ("tread", "rubber", "tire", "gunmetal", "metal"),
    }[key]
    scene_mats = [m for m in bpy.data.materials if not m.name.endswith("AuthoringDetail") and not m.name.lower().startswith("preview")]
    for token in tokens:
        for material in scene_mats:
            name = material.name.lower().replace("_", "")
            if token in name:
                return material
    if scene_mats:
        return scene_mats[0]
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
