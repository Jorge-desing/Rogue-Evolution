"""Canonical authored hardpoint transforms for the Level 1 source vehicles."""

from __future__ import annotations

from typing import Dict, Tuple

import bpy


HARDPOINT_POSITIONS_BY_UNIT: Dict[str, Dict[str, Tuple[float, float, float]]] = {
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
}


def get_canonical_hardpoint_socket_positions(
    unit_name: str,
) -> Dict[str, Tuple[float, float, float]]:
    """Return the unit's single source of truth for its four Level 1 hardpoints."""
    try:
        positions = HARDPOINT_POSITIONS_BY_UNIT[unit_name]
    except KeyError as exc:
        raise ValueError(f"No canonical hardpoint positions are authored for {unit_name}") from exc
    return dict(positions)


def create_canonical_hardpoint_sockets(unit_name: str) -> list[bpy.types.Object]:
    """Create or position the four unique hardpoint empties in the current scene."""
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
