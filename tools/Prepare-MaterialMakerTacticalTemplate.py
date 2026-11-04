#!/usr/bin/env python3
"""Create the reproducible tactical-military Material Maker graph template."""

from __future__ import annotations

import argparse
import json
from pathlib import Path


EXAMPLE = Path(
    ".tools/MaterialMaker-1.7/material_maker_1_7_windows/examples/bricks.ptex"
)


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


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()

    if args.output.exists() and not args.force:
        print(f"Keeping existing Material Maker template: {args.output}")
        return 0
    if not EXAMPLE.is_file():
        raise FileNotFoundError(f"Material Maker example is missing: {EXAMPLE}")

    project = json.loads(EXAMPLE.read_text(encoding="utf-8"))
    project["label"] = "Tactical Military Hull"
    project["name"] = "tactical_military_hull"

    for node in project.get("nodes", []):
        name = node.get("name")
        parameters = node.setdefault("parameters", {})
        if name == "uniform_0":
            # Metallic channel: authored steel, not a global black plastic default.
            parameters["color"] = color(0.78, 0.78, 0.78)
        elif name == "colorize_0":
            parameters["gradient"] = gradient(
                (0.0, (0.055, 0.075, 0.040)),
                (1.0, (0.25, 0.30, 0.16)),
            )
        elif name == "colorize_2":
            parameters["gradient"] = gradient(
                (0.0, (0.10, 0.12, 0.07)),
                (0.10, (0.42, 0.35, 0.18)),
            )
        elif name == "colorize_1":
            parameters["gradient"] = gradient(
                (0.0, (0.12, 0.16, 0.08)),
                (0.20, (0.30, 0.24, 0.10)),
                (0.35, (0.07, 0.09, 0.05)),
                (0.55, (0.38, 0.32, 0.16)),
                (0.75, (0.10, 0.13, 0.07)),
                (1.0, (0.28, 0.30, 0.17)),
            )
        elif name == "colorize_4":
            # Roughness: matte painted armor with localized worn steel.
            parameters["gradient"] = gradient(
                (0.0, (0.28, 0.28, 0.28)),
                (1.0, (0.58, 0.58, 0.58)),
            )

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(project, indent="\t"), encoding="utf-8")
    print(f"Prepared Material Maker template: {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
