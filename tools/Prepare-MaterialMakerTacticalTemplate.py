#!/usr/bin/env python3
"""Create reproducible tactical-military Material Maker 1.7 graph templates.

Supports multiple faction/surface presets (`olive_armor`, `gunmetal_chassis`,
`desert_camo`, `hazard_industrial`) and wires a bimodal procedural Metallic mask
from the plate/seam height graph so that panel interiors and recessed seams have
distinct metallic and roughness responses (eliminating flat-channel exports).
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

EXAMPLE = Path(
    ".tools/MaterialMaker-1.7/material_maker_1_7_windows/examples/bricks.ptex"
)

PRESETS: dict[str, dict[str, object]] = {
    "olive_armor": {
        "label": "Tactical Olive Armor Hull",
        "name": "tactical_olive_armor_hull",
        "seam_tint": ((0.0, (0.09, 0.11, 0.07)), (1.0, (0.28, 0.33, 0.19))),
        "bevel_mask": ((0.0, (0.12, 0.14, 0.09)), (0.12, (0.46, 0.40, 0.22))),
        "plate_palette": (
            (0.0, (0.16, 0.21, 0.12)),
            (0.20, (0.32, 0.28, 0.15)),
            (0.35, (0.12, 0.16, 0.09)),
            (0.55, (0.40, 0.35, 0.20)),
            (0.75, (0.15, 0.19, 0.11)),
            (1.0, (0.30, 0.33, 0.20)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.08, (0.06, 0.06, 0.06)),
            (0.11, (0.86, 0.86, 0.86)),
            (1.0, (0.94, 0.94, 0.94)),
        ),
        "roughness_curve": (
            (0.0, (0.68, 0.68, 0.68)),
            (0.12, (0.30, 0.30, 0.30)),
            (1.0, (0.52, 0.52, 0.52)),
        ),
    },
    "gunmetal_chassis": {
        "label": "Tactical Gunmetal Chassis",
        "name": "tactical_gunmetal_chassis",
        "seam_tint": ((0.0, (0.08, 0.09, 0.11)), (1.0, (0.24, 0.27, 0.31))),
        "bevel_mask": ((0.0, (0.10, 0.11, 0.13)), (0.12, (0.42, 0.45, 0.50))),
        "plate_palette": (
            (0.0, (0.15, 0.17, 0.20)),
            (0.25, (0.26, 0.29, 0.34)),
            (0.50, (0.13, 0.15, 0.18)),
            (0.75, (0.31, 0.34, 0.39)),
            (1.0, (0.20, 0.23, 0.27)),
        ),
        "metallic_curve": (
            (0.0, (0.05, 0.05, 0.05)),
            (0.07, (0.08, 0.08, 0.08)),
            (0.10, (0.88, 0.88, 0.88)),
            (1.0, (0.96, 0.96, 0.96)),
        ),
        "roughness_curve": (
            (0.0, (0.64, 0.64, 0.64)),
            (0.12, (0.24, 0.24, 0.24)),
            (1.0, (0.44, 0.44, 0.44)),
        ),
    },
    "desert_camo": {
        "label": "Tactical Desert Armor",
        "name": "tactical_desert_armor",
        "seam_tint": ((0.0, (0.18, 0.14, 0.09)), (1.0, (0.48, 0.39, 0.26))),
        "bevel_mask": ((0.0, (0.20, 0.16, 0.11)), (0.12, (0.58, 0.48, 0.33))),
        "plate_palette": (
            (0.0, (0.34, 0.27, 0.18)),
            (0.25, (0.52, 0.43, 0.29)),
            (0.50, (0.28, 0.22, 0.14)),
            (0.75, (0.56, 0.46, 0.32)),
            (1.0, (0.42, 0.34, 0.23)),
        ),
        "metallic_curve": (
            (0.0, (0.03, 0.03, 0.03)),
            (0.09, (0.05, 0.05, 0.05)),
            (0.12, (0.84, 0.84, 0.84)),
            (1.0, (0.90, 0.90, 0.90)),
        ),
        "roughness_curve": (
            (0.0, (0.74, 0.74, 0.74)),
            (0.12, (0.36, 0.36, 0.36)),
            (1.0, (0.58, 0.58, 0.58)),
        ),
    },
    "hazard_industrial": {
        "label": "Hazard Industrial Plating",
        "name": "hazard_industrial_plating",
        "seam_tint": ((0.0, (0.10, 0.09, 0.07)), (1.0, (0.42, 0.32, 0.12))),
        "bevel_mask": ((0.0, (0.12, 0.11, 0.09)), (0.12, (0.55, 0.42, 0.16))),
        "plate_palette": (
            (0.0, (0.16, 0.16, 0.17)),
            (0.25, (0.54, 0.40, 0.11)),
            (0.50, (0.14, 0.14, 0.15)),
            (0.75, (0.58, 0.44, 0.13)),
            (1.0, (0.24, 0.22, 0.18)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.08, (0.06, 0.06, 0.06)),
            (0.11, (0.85, 0.85, 0.85)),
            (1.0, (0.93, 0.93, 0.93)),
        ),
        "roughness_curve": (
            (0.0, (0.70, 0.70, 0.70)),
            (0.12, (0.28, 0.28, 0.28)),
            (1.0, (0.50, 0.50, 0.50)),
        ),
    },
}


def color(r: float, g: float, b: float) -> dict[str, object]:
    return {"a": 1.0, "b": b, "g": g, "r": r, "type": "Color"}


def gradient(*points: tuple[float, tuple[float, float, float]]) -> dict[str, object]:
    return {
        "interpolation": 1,
        "points": [
            {**color(*rgb), "pos": position} for position, rgb in points
        ],
        "type": "Gradient",
    }


def build_template_project(
    preset_name: str = "olive_armor",
    size_power: int = 11,
    example_path: Path = EXAMPLE,
) -> dict[str, object]:
    if preset_name not in PRESETS:
        raise ValueError(f"Unknown Material Maker preset '{preset_name}'. Choices: {sorted(PRESETS)}")
    if not example_path.is_file():
        raise FileNotFoundError(f"Material Maker example is missing: {example_path}")

    preset = PRESETS[preset_name]
    project = json.loads(example_path.read_text(encoding="utf-8"))
    project["label"] = preset["label"]
    project["name"] = preset["name"]

    nodes = project.get("nodes", [])
    for node in nodes:
        name = node.get("name")
        parameters = node.setdefault("parameters", {})
        if name == "Material":
            parameters["size"] = int(size_power)
            parameters["metallic"] = 1
            parameters["roughness"] = 1
            parameters["normal"] = 1
        elif name == "uniform_0":
            parameters["color"] = color(0.88, 0.88, 0.88)
        elif name == "colorize_0":
            parameters["gradient"] = gradient(*preset["seam_tint"])
        elif name == "colorize_2":
            parameters["gradient"] = gradient(*preset["bevel_mask"])
        elif name == "colorize_1":
            parameters["gradient"] = gradient(*preset["plate_palette"])
        elif name == "colorize_4":
            parameters["gradient"] = gradient(*preset["roughness_curve"])

    # Replace the flat uniform_0 -> Material(port 1) connection with a bimodal
    # procedural metallic mask driven by Warp (recessed seams = dielectric dust,
    # armor plates = metallic steel).
    has_metal_node = any(n.get("name") == "colorize_metallic" for n in nodes)
    if not has_metal_node:
        nodes.append(
            {
                "name": "colorize_metallic",
                "node_position": {"x": 760.0, "y": 140.0},
                "parameters": {
                    "gradient": gradient(*preset["metallic_curve"]),
                },
                "type": "colorize",
            }
        )
    else:
        for n in nodes:
            if n.get("name") == "colorize_metallic":
                n.setdefault("parameters", {})["gradient"] = gradient(*preset["metallic_curve"])

    connections = [
        c
        for c in project.get("connections", [])
        if not (c.get("to") == "Material" and c.get("to_port") == 1)
    ]
    connections.append(
        {
            "from": "Warp",
            "from_port": 0,
            "to": "colorize_metallic",
            "to_port": 0,
        }
    )
    connections.append(
        {
            "from": "colorize_metallic",
            "from_port": 0,
            "to": "Material",
            "to_port": 1,
        }
    )
    project["connections"] = connections
    return project


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument(
        "--preset",
        choices=sorted(PRESETS),
        default="olive_armor",
        help="Tactical surface preset to author into the .ptex graph.",
    )
    parser.add_argument(
        "--size",
        type=int,
        default=11,
        choices=[9, 10, 11, 12],
        help="Material Maker power-of-two resolution exponent (10=1024, 11=2048).",
    )
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()

    if args.output.exists() and not args.force:
        print(f"Keeping existing Material Maker template: {args.output}")
        return 0

    project = build_template_project(preset_name=args.preset, size_power=args.size)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(project, indent="\t"), encoding="utf-8")
    print(f"Prepared Material Maker template ({args.preset}, 2^{args.size}): {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
