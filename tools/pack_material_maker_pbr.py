#!/usr/bin/env python3
"""Pack Material Maker scalar exports into Rogue Command's four-map PBR contract."""

from __future__ import annotations

import argparse
import shutil
from pathlib import Path

from PIL import Image, ImageChops


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--prefix", required=True)
    return parser.parse_args()


def require_map(directory: Path, prefix: str, suffix: str) -> Path:
    path = directory / f"{prefix}_{suffix}.png"
    if not path.is_file():
        raise FileNotFoundError(f"Material Maker did not export {path}")
    return path


def copy_color_map(source: Path, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)


def pack_metallic_gloss(metallic_path: Path, roughness_path: Path, output_path: Path) -> None:
    with Image.open(metallic_path) as metallic_image, Image.open(roughness_path) as roughness_image:
        metallic = metallic_image.convert("L")
        roughness = roughness_image.convert("L")
        if metallic.size != roughness.size:
            raise ValueError(
                f"Metallic/Roughness dimensions differ: {metallic.size} vs {roughness.size}"
            )
        smoothness = ImageChops.invert(roughness)
        packed = Image.merge("RGBA", (metallic, metallic, metallic, smoothness))
        output_path.parent.mkdir(parents=True, exist_ok=True)
        packed.save(output_path, format="PNG", optimize=True)


def pack_maps(input_dir: Path, output_dir: Path, prefix: str) -> None:
    output_dir.mkdir(parents=True, exist_ok=True)

    albedo = require_map(input_dir, prefix, "Albedo")
    metallic = require_map(input_dir, prefix, "Metallic")
    roughness = require_map(input_dir, prefix, "Roughness")
    normal = require_map(input_dir, prefix, "Normal")
    emission = require_map(input_dir, prefix, "Emission")

    # Validate that all maps share the same atlas dimensions before copying.
    dimensions = {}
    for label, path in {
        "Albedo": albedo,
        "Metallic": metallic,
        "Roughness": roughness,
        "Normal": normal,
        "Emission": emission,
    }.items():
        with Image.open(path) as image:
            dimensions[label] = image.size
    if len(set(dimensions.values())) != 1:
        raise ValueError(f"Material Maker maps have incompatible dimensions: {dimensions}")

    copy_color_map(albedo, output_dir / f"{prefix}_Albedo.png")
    pack_metallic_gloss(
        metallic,
        roughness,
        output_dir / f"{prefix}_MetallicGloss.png",
    )
    copy_color_map(normal, output_dir / f"{prefix}_Normal.png")
    copy_color_map(emission, output_dir / f"{prefix}_Emission.png")

    print(f"Packed {prefix}: {dimensions['Albedo'][0]}x{dimensions['Albedo'][1]}")


def main_for_test(input_dir: Path, output_dir: Path, prefix: str) -> int:
    pack_maps(input_dir, output_dir, prefix)
    return 0


def main() -> int:
    args = parse_args()
    return main_for_test(args.input_dir, args.output_dir, args.prefix)


if __name__ == "__main__":
    raise SystemExit(main())
