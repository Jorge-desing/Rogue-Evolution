#!/usr/bin/env python3
"""Pack Material Maker scalar exports into Rogue Command's four-map PBR contract.

Besides channel packing, the packer sanitises the outputs for the URP
``RogueCommand/VehiclePBR_VertexWear`` shader and writes ``<prefix>_pack-report.json``
with per-map statistics and quality warnings (flat/blank exports, non power-of-two
sizes, non-unit normals, albedo outside the physically plausible range, ...).
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

_REPO_ROOT = Path(__file__).resolve().parents[1]
if str(_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPO_ROOT))

from PIL import Image, ImageFilter, ImageStat

# Roughness floor: a perfectly smooth (roughness 0) surface aliases badly at the
# game's RTS camera distance, so smoothness is capped at 1 - MIN_ROUGHNESS.
MIN_ROUGHNESS = 0.04
MAX_SMOOTHNESS = round(255 * (1.0 - MIN_ROUGHNESS))
SMOOTHNESS_LUT = [min(255 - i, MAX_SMOOTHNESS) for i in range(256)]
# Statistics are sampled on a downscaled copy so the report stays cheap on 4K atlases.
STATS_SAMPLE_SIZE = 64
# sRGB albedo plausibility window for non-metal surfaces (charcoal .. fresh snow).
ALBEDO_MIN_MEAN = 30
ALBEDO_MAX_MEAN = 240
NORMAL_LENGTH_TOLERANCE = 0.12
FLAT_STDDEV = 0.75


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--prefix", required=True)
    parser.add_argument(
        "--strict",
        action="store_true",
        help="Treat quality warnings as errors (exit code 2).",
    )
    parser.add_argument(
        "--dilate-px",
        type=int,
        default=0,
        help="Optional Voronoi UV island edge dilation radius in pixels (0 disables).",
    )
    parser.add_argument(
        "--detail-normal",
        type=Path,
        default=None,
        help="Optional tileable micro-detail tangent normal PNG composed via RNM.",
    )
    parser.add_argument(
        "--detail-strength",
        type=float,
        default=0.35,
        help="RNM blend strength for --detail-normal (default 0.35).",
    )
    return parser.parse_args()


def require_map(directory: Path, prefix: str, suffix: str) -> Path:
    path = directory / f"{prefix}_{suffix}.png"
    if not path.is_file():
        raise FileNotFoundError(f"Material Maker did not export {path}")
    return path


def save_png(image: Image.Image, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    image.save(destination, format="PNG", optimize=True)


def copy_opaque_rgb(source: Path, destination: Path) -> None:
    """Re-save as opaque RGB so a stray alpha channel never drives alpha clipping."""
    with Image.open(source) as image:
        save_png(image.convert("RGB"), destination)


def process_albedo(source: Path, destination: Path) -> None:
    """Re-save as opaque RGB with subtle PBR-safe micro-contrast for sharp panel lines."""
    with Image.open(source) as image:
        rgb = image.convert("RGB")
        # Gentle unsharp mask to crisp up panel separation at RTS isometric viewports
        # without introducing clipping or haloing.
        try:
            sharpened = rgb.filter(
                ImageFilter.UnsharpMask(radius=1.2, percent=35, threshold=2)
            )
        except Exception:
            sharpened = rgb
        save_png(sharpened, destination)


def normalize_normal(source: Path, destination: Path) -> None:
    """Re-save tangent normal map with exact per-pixel vector renormalization."""
    with Image.open(source) as image:
        rgb = image.convert("RGB")
        try:
            import numpy as np

            arr = np.asarray(rgb, dtype=np.float32) / 127.5 - 1.0
            # Ensure Z points outward in tangent space
            arr[..., 2] = np.maximum(arr[..., 2], 0.02)
            lens = np.sqrt(np.sum(arr * arr, axis=-1, keepdims=True))
            lens = np.maximum(lens, 1e-6)
            arr = arr / lens
            encoded = np.clip(np.round((arr + 1.0) * 127.5), 0, 255).astype(np.uint8)
            out_img = Image.fromarray(encoded, "RGB")
        except Exception:
            out_img = rgb
        save_png(out_img, destination)


def pack_metallic_gloss(metallic_path: Path, roughness_path: Path, output_path: Path) -> None:
    with Image.open(metallic_path) as metallic_image, Image.open(roughness_path) as roughness_image:
        metallic = metallic_image.convert("L")
        roughness = roughness_image.convert("L")
        if metallic.size != roughness.size:
            raise ValueError(
                f"Metallic/Roughness dimensions differ: {metallic.size} vs {roughness.size}"
            )
        smoothness = roughness.point(SMOOTHNESS_LUT)
        packed = Image.merge("RGBA", (metallic, metallic, metallic, smoothness))
        save_png(packed, output_path)


def sample(image: Image.Image) -> Image.Image:
    return image.resize((STATS_SAMPLE_SIZE, STATS_SAMPLE_SIZE), Image.BILINEAR)


def channel_stats(image: Image.Image) -> dict[str, list[float]]:
    stat = ImageStat.Stat(sample(image))
    return {
        "mean": [round(v, 2) for v in stat.mean],
        "stddev": [round(v, 2) for v in stat.stddev],
        "min": [lo for lo, _ in stat.extrema],
        "max": [hi for _, hi in stat.extrema],
    }


def _flat_pixels(image: Image.Image) -> list:
    getter = getattr(image, "get_flattened_data", None)
    if callable(getter):
        return list(getter())
    return list(image.getdata())


def normal_length_error(image: Image.Image) -> float:
    """Mean |length - 1| of decoded tangent-space normals (0 for a perfect map)."""
    # Bilinear filtering in encoded RGB space averages direction vectors and
    # creates an artificial under-length vector on detailed maps. Nearest
    # sampling keeps this audit about the authored texels, not mip filtering.
    sampled = image.convert("RGB").resize(
        (STATS_SAMPLE_SIZE, STATS_SAMPLE_SIZE), Image.Resampling.NEAREST
    )
    try:
        import numpy as np

        arr = np.asarray(sampled, dtype=np.float32) / 127.5 - 1.0
        lens = np.sqrt(np.sum(arr * arr, axis=-1))
        return float(np.mean(np.abs(lens - 1.0)))
    except Exception:
        pixels = _flat_pixels(sampled)
        total = 0.0
        for r, g, b in pixels:
            x, y, z = r / 127.5 - 1.0, g / 127.5 - 1.0, b / 127.5 - 1.0
            total += abs(math.sqrt(x * x + y * y + z * z) - 1.0)
        return total / max(1, len(pixels))


def metallic_midtone_ratio(metallic: Image.Image) -> float:
    """Fraction of pixels that are neither dielectric (<25%) nor metal (>75%)."""
    sampled = sample(metallic).convert("L")
    try:
        import numpy as np

        arr = np.asarray(sampled, dtype=np.uint8)
        mid = int(np.count_nonzero((arr >= 64) & (arr <= 191)))
        return float(mid / max(1, arr.size))
    except Exception:
        values = _flat_pixels(sampled)
        mid = sum(1 for v in values if 64 <= v <= 191)
        return mid / max(1, len(values))


def build_report(paths: dict[str, Path], dimensions: dict[str, tuple[int, int]]) -> dict[str, object]:
    warnings: list[str] = []
    stats: dict[str, object] = {}
    width, height = next(iter(dimensions.values()))

    if width & (width - 1) or height & (height - 1):
        warnings.append(f"non-power-of-two atlas {width}x{height}; mipmaps/compression are suboptimal")

    for label, path in paths.items():
        with Image.open(path) as image:
            gray_or_rgb = image.convert("RGB") if label in {"Albedo", "Normal", "Emission"} else image.convert("L")
            stats[label] = channel_stats(gray_or_rgb)
            if label in {"Albedo", "Metallic", "Roughness"}:
                if max(stats[label]["stddev"]) < FLAT_STDDEV:
                    warnings.append(f"{label} is flat (no variation); possible blank or failed export")
            if label == "Albedo":
                mean = sum(stats[label]["mean"]) / 3.0
                if not ALBEDO_MIN_MEAN <= mean <= ALBEDO_MAX_MEAN:
                    warnings.append(
                        f"Albedo mean {mean:.0f} outside plausible PBR range "
                        f"[{ALBEDO_MIN_MEAN}, {ALBEDO_MAX_MEAN}]"
                    )
            elif label == "Normal":
                error = normal_length_error(image)
                stats[label]["lengthError"] = round(error, 4)
                if error > NORMAL_LENGTH_TOLERANCE:
                    warnings.append(
                        f"Normal vectors deviate from unit length (mean error {error:.3f}); "
                        "check the export is a tangent-space (OpenGL) normal map"
                    )
            elif label == "Metallic":
                ratio = metallic_midtone_ratio(image.convert("L"))
                stats[label]["midtoneRatio"] = round(ratio, 3)
                if ratio > 0.35:
                    warnings.append(
                        f"Metallic has {ratio:.0%} mid-tone pixels; metal/dielectric masks should be near-binary"
                    )
            elif label == "Emission":
                if max(stats[label]["mean"]) > 96:
                    warnings.append("Emission is bright over most of the atlas; intentional emissive elements only")

    return {
        "size": [width, height],
        "maxSmoothness": MAX_SMOOTHNESS,
        "stats": stats,
        "warnings": warnings,
    }


def _apply_uv_dilation(source_albedo: Path, output_paths: dict[str, Path], dilate_px: int) -> None:
    if dilate_px <= 0:
        return
    import numpy as np
    from tools.pbr_texture_algorithms import dilate_uv_islands

    with Image.open(source_albedo) as src:
        rgba = src.convert("RGBA")
        alpha = np.asarray(rgba, dtype=np.uint8)[..., 3]
        if int(alpha.min()) < 255:
            mask = alpha > 127
        else:
            rgb = np.asarray(rgba, dtype=np.uint8)[..., :3]
            mask = np.any(rgb > 2, axis=-1)

    for key in ("Albedo", "MetallicGloss", "Normal", "Emission"):
        path = output_paths[key]
        with Image.open(path) as img:
            arr = np.asarray(img)
            dilated = dilate_uv_islands(arr, mask, max_distance=dilate_px)
            Image.fromarray(dilated, img.mode).save(path, format="PNG", optimize=True)


def pack_maps(
    input_dir: Path,
    output_dir: Path,
    prefix: str,
    dilate_px: int = 0,
    detail_normal: Path | None = None,
    detail_strength: float = 0.35,
) -> dict[str, object]:
    output_dir.mkdir(parents=True, exist_ok=True)

    paths = {
        "Albedo": require_map(input_dir, prefix, "Albedo"),
        "Metallic": require_map(input_dir, prefix, "Metallic"),
        "Roughness": require_map(input_dir, prefix, "Roughness"),
        "Normal": require_map(input_dir, prefix, "Normal"),
        "Emission": require_map(input_dir, prefix, "Emission"),
    }

    # Validate that all maps share the same atlas dimensions before writing anything.
    dimensions: dict[str, tuple[int, int]] = {}
    for label, path in paths.items():
        with Image.open(path) as image:
            dimensions[label] = image.size
    if len(set(dimensions.values())) != 1:
        raise ValueError(f"Material Maker maps have incompatible dimensions: {dimensions}")

    output_paths = {
        "Albedo": output_dir / f"{prefix}_Albedo.png",
        "MetallicGloss": output_dir / f"{prefix}_MetallicGloss.png",
        "Normal": output_dir / f"{prefix}_Normal.png",
        "Emission": output_dir / f"{prefix}_Emission.png",
    }
    process_albedo(paths["Albedo"], output_paths["Albedo"])
    pack_metallic_gloss(
        paths["Metallic"],
        paths["Roughness"],
        output_paths["MetallicGloss"],
    )
    normalize_normal(paths["Normal"], output_paths["Normal"])
    if detail_normal is not None:
        from tools.functional_engineering_integration import compose_canonical_pbr_detail

        compose_canonical_pbr_detail(
            output_paths["Normal"],
            detail_normal,
            output_paths["Normal"],
            detail_strength=detail_strength,
        )
    copy_opaque_rgb(paths["Emission"], output_paths["Emission"])
    _apply_uv_dilation(paths["Albedo"], output_paths, dilate_px)

    # Audit the final packed outputs. In particular, checking the raw Material
    # Maker normal before renormalization makes strict mode reject valid maps.
    report_paths = {
        "Albedo": output_paths["Albedo"],
        "Metallic": paths["Metallic"],
        "Roughness": paths["Roughness"],
        "Normal": output_paths["Normal"],
        "Emission": output_paths["Emission"],
    }
    report = build_report(report_paths, dimensions)
    (output_dir / f"{prefix}_pack-report.json").write_text(
        json.dumps(report, indent=2), encoding="utf-8"
    )

    width, height = dimensions["Albedo"]
    print(f"Packed {prefix}: {width}x{height}")
    for warning in report["warnings"]:
        print(f"WARNING: {warning}")
    return report


def main_for_test(
    input_dir: Path,
    output_dir: Path,
    prefix: str,
    strict: bool = False,
    dilate_px: int = 0,
    detail_normal: Path | None = None,
    detail_strength: float = 0.35,
) -> int:
    report = pack_maps(
        input_dir,
        output_dir,
        prefix,
        dilate_px=dilate_px,
        detail_normal=detail_normal,
        detail_strength=detail_strength,
    )
    return 2 if strict and report["warnings"] else 0


def main() -> int:
    args = parse_args()
    return main_for_test(
        args.input_dir,
        args.output_dir,
        args.prefix,
        args.strict,
        args.dilate_px,
        args.detail_normal,
        args.detail_strength,
    )


if __name__ == "__main__":
    raise SystemExit(main())
