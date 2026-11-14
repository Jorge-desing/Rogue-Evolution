"""Canonical authored hardpoint transforms for all Level 1 source vehicles."""

from __future__ import annotations

from typing import Dict, Tuple

try:
    import bpy
except ImportError:  # Allow pure-Python validation scripts/tests outside Blender.
    bpy = None  # type: ignore[assignment]

CANONICAL_HARDPOINT_NAMES: Tuple[str, ...] = (
    "Socket_Hardpoint_ERA",
    "Socket_Hardpoint_APS",
    "Socket_Hardpoint_Sponsons",
    "Socket_Hardpoint_FuelDrums",
)

CANONICAL_FACTORY_NAMES: Tuple[str, ...] = (
    "BulwarkFoundry",
    "ArachnidHexapodFactory",
    "OverlordCommandHQFactory",
)


HARDPOINT_POSITIONS_BY_UNIT: Dict[str, Dict[str, Tuple[float, float, float]]] = {
    # -------------------------------------------------------------------------
    # Pack: sample-native-expansion (9 units)
    # -------------------------------------------------------------------------
    "SandlanceScout": {
        # ERA mounts on the forward upper hull; APS sits on the turret rear quarter.
        "Socket_Hardpoint_ERA": (0.0, 0.92, 0.76),
        "Socket_Hardpoint_APS": (0.48, -0.18, 1.10),
        "Socket_Hardpoint_Sponsons": (0.96, -0.02, 0.55),
        "Socket_Hardpoint_FuelDrums": (0.88, -0.82, 0.48),
    },
    "ColossusSiegePlatform": {
        # ERA mounts on the glacis; the sponson point clears the built-in side armor.
        "Socket_Hardpoint_ERA": (0.0, 0.62, 0.86),
        "Socket_Hardpoint_APS": (-0.55, 0.82, 1.58),
        "Socket_Hardpoint_Sponsons": (1.25, -0.02, 0.68),
        "Socket_Hardpoint_FuelDrums": (0.72, -1.30, 0.52),
    },
    "SkirmishStrikeBike": {
        # ERA anchors on the upper body; APS occupies the high rear turret deck.
        "Socket_Hardpoint_ERA": (0.42, -0.24, 0.86),
        "Socket_Hardpoint_APS": (-0.32, -0.46, 1.16),
        "Socket_Hardpoint_Sponsons": (0.57, -0.02, 0.55),
        "Socket_Hardpoint_FuelDrums": (0.38, -0.82, 0.50),
    },
    "SurgeArcTank": {
        # The sponson point sits on the exposed right flank, clear of the accelerator.
        "Socket_Hardpoint_ERA": (-0.62, 0.58, 0.78),
        "Socket_Hardpoint_APS": (0.60, -0.22, 1.25),
        "Socket_Hardpoint_Sponsons": (0.98, 0.02, 0.70),
        "Socket_Hardpoint_FuelDrums": (0.72, -1.02, 0.54),
    },
    "IroncladWalker": {
        # Seated within chest/torso envelope [-1.46..1.01, -0.68..1.30, -0.25..2.55].
        "Socket_Hardpoint_ERA": (0.0, 0.48, 1.98),
        "Socket_Hardpoint_APS": (0.36, -0.32, 2.38),
        "Socket_Hardpoint_Sponsons": (0.76, 0.05, 1.72),
        "Socket_Hardpoint_FuelDrums": (-0.42, -0.52, 1.92),
    },
    "AtlasCraneMech": {
        # Seated within crane mech torso envelope [-1.56..1.01, -0.91..1.07, -0.20..2.86].
        "Socket_Hardpoint_ERA": (0.0, 0.52, 1.82),
        "Socket_Hardpoint_APS": (0.35, -0.60, 2.52),
        "Socket_Hardpoint_Sponsons": (0.78, 0.02, 1.65),
        "Socket_Hardpoint_FuelDrums": (0.0, -0.78, 1.85),
    },
    "BastionSiegeMech": {
        # Anchored on the quad-walker glacis, turret deck, cheek, and rear bustle.
        "Socket_Hardpoint_ERA": (0.0, 0.72, 1.46),
        "Socket_Hardpoint_APS": (0.42, -0.25, 2.15),
        "Socket_Hardpoint_Sponsons": (1.05, 0.05, 1.32),
        "Socket_Hardpoint_FuelDrums": (0.0, -1.35, 1.28),
    },
    "ArachnidHexapod": {
        # Seated on the central carapace inner ring well inside the six articulated legs.
        "Socket_Hardpoint_ERA": (0.0, 0.85, 0.92),
        "Socket_Hardpoint_APS": (0.42, -0.45, 1.35),
        "Socket_Hardpoint_Sponsons": (0.92, 0.02, 0.82),
        "Socket_Hardpoint_FuelDrums": (-0.48, -0.95, 0.78),
    },
    "OverlordCommandHQ": {
        # Seated on heavy command hull [-1.25..1.25, -2.16..2.31, 0.07..2.91].
        "Socket_Hardpoint_ERA": (0.0, 1.72, 1.15),
        "Socket_Hardpoint_APS": (0.45, -0.15, 2.18),
        "Socket_Hardpoint_Sponsons": (1.08, 0.08, 0.92),
        "Socket_Hardpoint_FuelDrums": (0.74, -1.45, 0.82),
    },
    # -------------------------------------------------------------------------
    # Pack: tactical-operations (9 units)
    # -------------------------------------------------------------------------
    "TidebreakerHovercraft": {
        # Seated on hover bow, superstructure roof, skirt flank, and aft thruster deck.
        "Socket_Hardpoint_ERA": (0.0, 1.32, 1.12),
        "Socket_Hardpoint_APS": (0.38, -0.15, 1.72),
        "Socket_Hardpoint_Sponsons": (1.18, 0.05, 0.86),
        "Socket_Hardpoint_FuelDrums": (0.76, -1.62, 0.82),
    },
    "SalvageHauler": {
        # Seated on forward utility armor, cab sensor roof, right rail, and aft bed.
        "Socket_Hardpoint_ERA": (-0.45, 0.95, 1.32),
        "Socket_Hardpoint_APS": (0.32, 0.05, 1.88),
        "Socket_Hardpoint_Sponsons": (1.15, -0.05, 0.82),
        "Socket_Hardpoint_FuelDrums": (0.78, -1.35, 0.76),
    },
    "SkyguardSAM": {
        # Seated on forward cab glacis, launcher shoulder, skirt, and rear chassis bay.
        "Socket_Hardpoint_ERA": (0.0, 1.48, 1.18),
        "Socket_Hardpoint_APS": (0.42, -0.35, 2.02),
        "Socket_Hardpoint_Sponsons": (1.02, 0.02, 0.78),
        "Socket_Hardpoint_FuelDrums": (0.72, -1.32, 0.72),
    },
    "ArcLanceWalker": {
        # Seated within compact biped torso [-0.75..0.75, -0.72..1.40, 0.0..2.35].
        "Socket_Hardpoint_ERA": (0.0, 0.56, 1.32),
        "Socket_Hardpoint_APS": (0.28, -0.25, 1.88),
        "Socket_Hardpoint_Sponsons": (0.58, 0.05, 1.38),
        "Socket_Hardpoint_FuelDrums": (-0.26, -0.54, 1.42),
    },
    "FieldMedicRover": {
        # Seated on forward hood bumper, medical bay roof, flank skirt, and rear bay.
        "Socket_Hardpoint_ERA": (0.0, 1.25, 1.18),
        "Socket_Hardpoint_APS": (0.36, -0.22, 1.82),
        "Socket_Hardpoint_Sponsons": (0.98, 0.02, 0.76),
        "Socket_Hardpoint_FuelDrums": (0.68, -1.28, 0.72),
    },
    "BulwarkSiegeTank": {
        # Preserve unit-local origin (0,0,0) on Socket_Hardpoint_ERA for the bilateral Level 2 array.
        "Socket_Hardpoint_ERA": (0.0, 0.0, 0.0),
        "Socket_Hardpoint_APS": (0.42, -0.28, 1.82),
        "Socket_Hardpoint_Sponsons": (1.24, 0.02, 0.76),
        "Socket_Hardpoint_FuelDrums": (0.84, -1.28, 0.68),
    },
    "CommandRelay": {
        # Seated on forward cab glacis, shelter roof, fender flank, and aft rear deck.
        "Socket_Hardpoint_ERA": (0.0, 1.15, 1.22),
        "Socket_Hardpoint_APS": (0.42, -0.25, 2.02),
        "Socket_Hardpoint_Sponsons": (1.06, -0.15, 0.82),
        "Socket_Hardpoint_FuelDrums": (0.76, -1.58, 0.78),
    },
    "SentinelRelay": {
        # Seated on forward hull nose, sensor roof deck, side skirt, and rear bay.
        "Socket_Hardpoint_ERA": (0.0, 1.12, 1.22),
        "Socket_Hardpoint_APS": (0.40, -0.22, 1.78),
        "Socket_Hardpoint_Sponsons": (1.06, -0.05, 0.78),
        "Socket_Hardpoint_FuelDrums": (0.76, -1.26, 0.72),
    },
    "GravitasBreaker": {
        # Seated on reinforced bow shield, singularity roof deck, skirt, and aft bay.
        "Socket_Hardpoint_ERA": (0.0, 1.28, 1.18),
        "Socket_Hardpoint_APS": (0.42, -0.25, 1.74),
        "Socket_Hardpoint_Sponsons": (1.18, -0.05, 0.76),
        "Socket_Hardpoint_FuelDrums": (0.82, -1.85, 0.72),
    },
}


def list_canonical_unit_names() -> Tuple[str, ...]:
    """Return all Level 1 mobile unit names registered for canonical hardpoints."""
    return tuple(HARDPOINT_POSITIONS_BY_UNIT.keys())


def get_canonical_hardpoint_socket_positions(
    unit_name: str,
) -> Dict[str, Tuple[float, float, float]]:
    """Return the unit's single source of truth for its four Level 1 hardpoints."""
    try:
        positions = HARDPOINT_POSITIONS_BY_UNIT[unit_name]
    except KeyError as exc:
        raise ValueError(f"No canonical hardpoint positions are authored for {unit_name}") from exc
    return dict(positions)


def create_canonical_hardpoint_sockets(unit_name: str) -> list[object]:
    """Create or position the four unique hardpoint empties in the current scene."""
    if bpy is None:
        raise RuntimeError("create_canonical_hardpoint_sockets must run inside Blender")
    positions = get_canonical_hardpoint_socket_positions(unit_name)
    unexpected = sorted(
        obj.name
        for obj in bpy.context.scene.objects
        if obj.name.startswith("Socket_Hardpoint_") and obj.name not in positions
    )
    if unexpected:
        raise ValueError(f"{unit_name} has noncanonical hardpoint objects: {unexpected}")

    result = []
    for name, location in positions.items():
        matches = [obj for obj in bpy.context.scene.objects if obj.name == name]
        if len(matches) > 1:
            raise ValueError(f"{unit_name} has duplicate {name} objects")
        if matches:
            socket = matches[0]
            if socket.type != "EMPTY":
                raise ValueError(f"{unit_name} {name} must be an empty socket")
        else:
            socket = bpy.data.objects.new(name, None)
            socket.empty_display_size = 0.1
            bpy.context.scene.collection.objects.link(socket)
        socket.location = location
        result.append(socket)
    return result
