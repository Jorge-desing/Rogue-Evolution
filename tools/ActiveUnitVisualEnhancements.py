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
        "features": {"missile_mounts", "gussets"},
        "missile_location": (0.0, -0.06, 1.58),
        "gusset_locations": [(-0.86, -0.45, 1.58), (0.86, -0.45, 1.58)],
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
        "features": {"access_hatches", "final_drive", "track_tensioners", "pioneer_racks", "turret_ring_guard", "coaxial_port", "driver_periscope", "weapon_trunnions"},
        "hull_width": 2.35, "hull_length": 3.25, "deck_z": 1.45, "ground_clearance": 0.06,
        "hatch_location": (0.45, -0.65, 1.48),
        "periscope_location": (0.0, 1.35, 1.38),
        "tool_rack_location": (-1.02, -0.25, 1.45),
        "turret_ring_location": (0.0, 0.25, 1.45), "turret_ring_radius": 0.70,
        "coaxial_location": (0.32, 0.95, 1.62),
        "weapon_trunnion_center": (0.0, 1.05, 1.62),
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



def _object_exists(name: str) -> bool:
    return bpy.data.objects.get(name) is not None


def _object_with_tokens(prefix: str, tokens: tuple[str, ...]):
    lowered = tuple(token.lower() for token in tokens)
    for obj in bpy.context.scene.objects:
        if obj.name.startswith(prefix) and all(token in obj.name.lower() for token in lowered):
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


def _add_wave34_leg_mounts(prefix: str, armor, gunmetal) -> list:
    result = []
    for side, tag in ((-1, "L"), (1, "R")):
        hip = bpy.data.objects.get(f"{prefix}Leg_HipJoint_{tag}")
        if hip is not None and not _object_exists(f"{prefix}Leg_HipFlange_{tag}"):
            result.append(rmk.make_cylinder(
                f"{prefix}Leg_HipFlange_{tag}", 0.22, 0.06,
                hip.matrix_world.translation - Vector((side * 0.06, 0.0, 0.0)),
                armor, rotation=(0.0, math.radians(90.0), 0.0), vertices=10, bevel=0.012,
            ))
        shin = bpy.data.objects.get(f"{prefix}Leg_ShinArmor_{tag}")
        if shin is not None and not _object_exists(f"{prefix}Leg_CalfArmor_{tag}"):
            result.append(rmk.make_box(
                f"{prefix}Leg_CalfArmor_{tag}", (0.18, 0.16, 0.28),
                shin.matrix_world.translation - Vector((0.0, 0.08, 0.0)),
                armor, bevel=0.02, rotation=tuple(shin.rotation_euler),
            ))
        ankle = bpy.data.objects.get(f"{prefix}Leg_AnkleJoint_{tag}")
        if ankle is not None and not _object_exists(f"{prefix}Leg_AnkleClevis_{tag}"):
            result.append(rmk.make_box(
                f"{prefix}Leg_AnkleClevis_{tag}", (0.16, 0.18, 0.10),
                ankle.matrix_world.translation - Vector((0.0, 0.0, 0.04)),
                gunmetal, bevel=0.012,
            ))
    return result


def _add_wave34_gussets(prefix: str, profile: Mapping[str, object], armor) -> list:
    locations = profile.get("gusset_locations", [])
    if not locations or any(obj.name.startswith(f"{prefix}Armor_Gusset_") for obj in bpy.context.scene.objects):
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
    return rmk.make_driver_periscope_hood(
        f"{prefix}Sensor_DriverPeriscope",
        location=loc,
        armor_mat=armor,
        optic_mat=gunmetal,
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
    return rmk.make_weapon_trunnion_bracket_assembly(
        f"{prefix}Armor_TrunnionBracket",
        pivot_center=loc,
        spacing_x=spacing,
        armor_mat=armor,
        gunmetal_mat=gunmetal,
    )


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
    return result


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
