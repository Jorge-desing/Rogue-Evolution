#!/usr/bin/env python3
"""Add manifest-derived labels to the Blender Level 2 contact sheet."""

from __future__ import annotations

import json
import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_IMAGE_PATH = ROOT / "artifacts/canonical-two-level/modules-contact-sheet.png"
MANIFEST_PATH = ROOT / "content/source-art/canonical-visual-modules.json"
COLUMNS = 5
TILE_SIZE = 480
LABEL_HEIGHT = 34


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", type=Path, default=DEFAULT_IMAGE_PATH)
    image_path = parser.parse_args().image
    if not image_path.is_absolute():
        image_path = ROOT / image_path
    entries = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))["modules"]
    image = Image.open(image_path).convert("RGBA")
    expected_size = (COLUMNS * TILE_SIZE, ((len(entries) + COLUMNS - 1) // COLUMNS) * TILE_SIZE)
    if image.size != expected_size:
        raise ValueError(f"Contact sheet dimensions {image.size} do not match {expected_size}")

    draw = ImageDraw.Draw(image, "RGBA")
    font_path = Path("C:/Windows/Fonts/arial.ttf")
    font = ImageFont.truetype(str(font_path), 16) if font_path.exists() else ImageFont.load_default()
    for index, entry in enumerate(entries):
        column = index % COLUMNS
        row = index // COLUMNS
        left = column * TILE_SIZE
        top = row * TILE_SIZE
        label = "{:02d} {} | {} | {}".format(
            index + 1,
            entry["slotId"],
            entry["unitId"].split(":", 1)[-1],
            entry["preset"].replace("_", "-"),
        )
        draw.rectangle((left + 2, top + TILE_SIZE - LABEL_HEIGHT, left + TILE_SIZE - 2, top + TILE_SIZE - 2),
                       fill=(8, 13, 21, 218))
        draw.text((left + 10, top + TILE_SIZE - LABEL_HEIGHT + 8), label, font=font,
                  fill=(239, 244, 250, 255), stroke_width=0)
    image.save(image_path)
    print(f"Annotated {len(entries)} module tiles in {image_path}")


if __name__ == "__main__":
    main()
