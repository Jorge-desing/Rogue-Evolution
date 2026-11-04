#!/usr/bin/env python3
"""Create reproducible space-military Material Maker 1.7 graph templates.

Configures the Material Maker 1.7 modular panel generator for hard-surface
military-spacecraft and tactical armor plating:
- Laser-cut expansion seams (`mortar = 0.012`), CNC 45-degree chamfers
  (`bevel = 0.035`), and corner miters (`corner = 0.02`).
- Minimal edge warp (`amount = 0.0025`) so panel lines stay crisp and machined
  instead of looking like wavy masonry bricks.
- Anisotropic cold-rolled brushed alloy grain (`scale_x = 32, scale_y = 8`) with
  controlled normal relief (`blend_2.amount = 0.08`, `normal_map_2.param1 = 0.45`).
- Bimodal procedural `colorize_metallic` node wired from the panel height graph.
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
        "label": "Exo-Atmospheric Tactical Olive Armor",
        "name": "tactical_olive_armor_hull",
        "bricks": {"pattern": 4, "repeat": 3, "rows": 4, "columns": 2, "mortar": 0.012, "bevel": 0.035, "corner": 0.02},
        "seam_tint": ((0.0, (0.05, 0.07, 0.04)), (1.0, (0.16, 0.20, 0.12))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.17, 0.22, 0.14)),
            (0.20, (0.28, 0.33, 0.21)),
            (0.40, (0.14, 0.18, 0.12)),
            (0.60, (0.35, 0.40, 0.26)),
            (0.80, (0.20, 0.25, 0.16)),
            (1.0, (0.31, 0.36, 0.23)),
        ),
        "metallic_curve": (
            (0.0, (0.03, 0.03, 0.03)),
            (0.04, (0.05, 0.05, 0.05)),
            (0.07, (0.94, 0.94, 0.94)),
            (1.0, (0.88, 0.88, 0.88)),
        ),
        "roughness_curve": (
            (0.0, (0.72, 0.72, 0.72)),
            (0.08, (0.20, 0.20, 0.20)),
            (0.25, (0.36, 0.36, 0.36)),
            (1.0, (0.46, 0.46, 0.46)),
        ),
    },
    "gunmetal_chassis": {
        "label": "Orbital Dreadnought Gunmetal Hull",
        "name": "tactical_gunmetal_chassis",
        "bricks": {"pattern": 4, "repeat": 3, "rows": 4, "columns": 2, "mortar": 0.011, "bevel": 0.032, "corner": 0.025},
        "seam_tint": ((0.0, (0.05, 0.06, 0.08)), (1.0, (0.15, 0.18, 0.23))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.16, 0.19, 0.24)),
            (0.25, (0.27, 0.31, 0.38)),
            (0.50, (0.14, 0.17, 0.22)),
            (0.75, (0.33, 0.38, 0.46)),
            (1.0, (0.22, 0.26, 0.32)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.07, 0.07, 0.07)),
            (0.07, (0.96, 0.96, 0.96)),
            (1.0, (0.90, 0.90, 0.90)),
        ),
        "roughness_curve": (
            (0.0, (0.68, 0.68, 0.68)),
            (0.08, (0.16, 0.16, 0.16)),
            (0.25, (0.30, 0.30, 0.30)),
            (1.0, (0.40, 0.40, 0.40)),
        ),
    },
    "desert_camo": {
        "label": "Lunar/Martian Thermal Regolith Plating",
        "name": "tactical_desert_armor",
        "bricks": {"pattern": 1, "repeat": 2, "rows": 6, "columns": 3, "mortar": 0.012, "bevel": 0.035, "corner": 0.018},
        "seam_tint": ((0.0, (0.12, 0.09, 0.06)), (1.0, (0.28, 0.22, 0.15))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.34, 0.28, 0.19)),
            (0.25, (0.52, 0.43, 0.30)),
            (0.50, (0.28, 0.23, 0.16)),
            (0.75, (0.58, 0.48, 0.34)),
            (1.0, (0.42, 0.35, 0.24)),
        ),
        "metallic_curve": (
            (0.0, (0.03, 0.03, 0.03)),
            (0.04, (0.05, 0.05, 0.05)),
            (0.07, (0.92, 0.92, 0.92)),
            (1.0, (0.85, 0.85, 0.85)),
        ),
        "roughness_curve": (
            (0.0, (0.76, 0.76, 0.76)),
            (0.08, (0.22, 0.22, 0.22)),
            (0.25, (0.38, 0.38, 0.38)),
            (1.0, (0.50, 0.50, 0.50)),
        ),
    },
    "hazard_industrial": {
        "label": "Orbital Docking Bay EVA Hazard Plating",
        "name": "hazard_industrial_plating",
        "bricks": {"pattern": 4, "repeat": 2, "rows": 4, "columns": 2, "mortar": 0.013, "bevel": 0.036, "corner": 0.022},
        "seam_tint": ((0.0, (0.07, 0.07, 0.08)), (1.0, (0.24, 0.20, 0.08))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.17, 0.18, 0.20)),
            (0.25, (0.58, 0.43, 0.12)),
            (0.50, (0.15, 0.16, 0.18)),
            (0.75, (0.64, 0.48, 0.14)),
            (1.0, (0.24, 0.25, 0.27)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.06, 0.06, 0.06)),
            (0.07, (0.95, 0.95, 0.95)),
            (1.0, (0.88, 0.88, 0.88)),
        ),
        "roughness_curve": (
            (0.0, (0.72, 0.72, 0.72)),
            (0.08, (0.18, 0.18, 0.18)),
            (0.25, (0.34, 0.34, 0.34)),
            (1.0, (0.45, 0.45, 0.45)),
        ),
    },
    "orbital_titanium": {
        "label": "Cryo-Titanium Spacecraft Armor",
        "name": "orbital_titanium_armor",
        "bricks": {"pattern": 4, "repeat": 3, "rows": 4, "columns": 2, "mortar": 0.010, "bevel": 0.030, "corner": 0.025},
        "seam_tint": ((0.0, (0.08, 0.10, 0.13)), (1.0, (0.26, 0.32, 0.38))),
        "bevel_mask": ((0.0, (0.0, 0.0, 0.0)), (0.05, (1.0, 1.0, 1.0))),
        "plate_palette": (
            (0.0, (0.38, 0.43, 0.50)),
            (0.25, (0.54, 0.60, 0.68)),
            (0.50, (0.32, 0.37, 0.44)),
            (0.75, (0.62, 0.68, 0.75)),
            (1.0, (0.46, 0.52, 0.60)),
        ),
        "metallic_curve": (
            (0.0, (0.04, 0.04, 0.04)),
            (0.04, (0.08, 0.08, 0.08)),
            (0.07, (0.97, 0.97, 0.97)),
            (1.0, (0.92, 0.92, 0.92)),
        ),
        "roughness_curve": (
            (0.0, (0.64, 0.64, 0.64)),
            (0.08, (0.14, 0.14, 0.14)),
            (0.25, (0.26, 0.26, 0.26)),
            (1.0, (0.36, 0.36, 0.36)),
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


def _configure_subgraph_bricks(subgraph_node: dict[str, object], bricks_cfg: dict[str, object]) -> None:
    for child in subgraph_node.get("nodes", []):
        cname = child.get("name")
        cparams = child.setdefault("parameters", {})
        if cname == "Bricks":
            cparams["pattern"] = bricks_cfg["pattern"]
            cparams["repeat"] = bricks_cfg["repeat"]
            cparams["rows"] = bricks_cfg["rows"]
            cparams["columns"] = bricks_cfg["columns"]
            cparams["mortar"] = bricks_cfg["mortar"]
            cparams["bevel"] = bricks_cfg["bevel"]
            cparams["corner"] = bricks_cfg["corner"]
            cparams["round"] = 0.0
        elif cname == "switch_0":
            cparams["source"] = 0


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
        elif name == "graph":
            _configure_subgraph_bricks(node, preset["bricks"])
        elif name == "Warp":
            # Crisp CNC-machined spacecraft seams with micro edge wear only.
            parameters["amount"] = 0.0025
            parameters["eps"] = 0.015
        elif name == "Perlin":
            # Balanced isotropic aerospace micro-grain (smooth CNC finish without streaky banding).
            parameters["scale_x"] = 12
            parameters["scale_y"] = 12
            parameters["iterations"] = 5
            parameters["persistence"] = 0.50
        elif name == "blend_2":
            # 96.5% crisp panel bevel geometry and 3.5% satin metal micro-relief.
            parameters["amount"] = 0.035
        elif name == "normal_map_2":
            parameters["param0"] = int(size_power)
            parameters["param1"] = 0.38
        elif name == "uniform_0":
            parameters["color"] = color(0.90, 0.90, 0.90)
        elif name == "colorize_0":
            parameters["gradient"] = gradient(*preset["seam_tint"])
        elif name == "colorize_2":
            parameters["gradient"] = gradient(*preset["bevel_mask"])
        elif name == "colorize_1":
            parameters["gradient"] = gradient(*preset["plate_palette"])
        elif name == "colorize_4":
            parameters["gradient"] = gradient(*preset["roughness_curve"])

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
        help="Space-military surface preset to author into the .ptex graph.",
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
