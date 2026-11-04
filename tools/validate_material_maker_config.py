#!/usr/bin/env python3
"""Validate the repository-wide Material Maker workflow contract."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

REQUIRED_MAPS = {"Albedo", "MetallicGloss", "Normal", "Emission"}
REQUIRED_WORKFLOWS = {"urp", "blender", "three_d", "procedural"}
REQUIRED_RAW_OUTPUTS = {
    "Albedo": 0,
    "Metallic": 12,
    "Roughness": 13,
    "Normal": 7,
    "Emission": 2,
}


def validate_config(path: Path) -> list[str]:
    errors: list[str] = []
    data = json.loads(path.read_text(encoding="utf-8"))

    if data.get("schemaVersion") != 1:
        errors.append("schemaVersion must be 1")
    tool = data.get("tool", {})
    if tool.get("name") != "Material Maker" or tool.get("version") != "1.7":
        errors.append("tool must pin Material Maker 1.7")

    maps = data.get("textureContract", {}).get("requiredMaps", [])
    map_suffixes = {entry.get("suffix") for entry in maps}
    if map_suffixes != REQUIRED_MAPS:
        errors.append(f"textureContract maps must be {sorted(REQUIRED_MAPS)}")
    if data.get("textureContract", {}).get("shader") != "RogueCommand/VehiclePBR_VertexWear":
        errors.append("textureContract shader must be VehiclePBR_VertexWear")

    raw_files = data.get("exportProfile", {}).get("rawFiles", [])
    raw_outputs = {entry.get("suffix"): entry.get("output") for entry in raw_files}
    if raw_outputs != REQUIRED_RAW_OUTPUTS:
        errors.append(f"exportProfile raw outputs must be {REQUIRED_RAW_OUTPUTS}")

    workflows = data.get("workflows", {})
    missing_workflows = REQUIRED_WORKFLOWS - set(workflows)
    if missing_workflows:
        errors.append(f"missing workflows: {sorted(missing_workflows)}")
    if workflows.get("three_d", {}).get("requiresUv") is not True:
        errors.append("three_d workflow must require UVs")
    if workflows.get("three_d", {}).get("singleSubmeshForPaint") is not True:
        errors.append("three_d workflow must declare the single-submesh paint constraint")

    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--config",
        type=Path,
        default=Path("tools/material-maker/rogue-command-material-maker.json"),
    )
    args = parser.parse_args()
    errors = validate_config(args.config)
    if errors:
        for error in errors:
            print(f"ERROR: {error}")
        return 1
    print(f"Material Maker workflow config OK: {args.config}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
