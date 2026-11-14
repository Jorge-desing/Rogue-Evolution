#!/usr/bin/env python3
"""Pack Material Maker scalar exports into Rogue Command's four-map PBR contract.

Besides channel packing, the packer sanitises the outputs for the URP
``RogueCommand/VehiclePBR_VertexWear`` shader and writes ``<prefix>_pack-report.json``
with per-map statistics and quality warnings (flat/blank exports, non power-of-two
sizes, non-unit normals, albedo outside the physically plausible range, ...).
"""

from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor
import json
import math
import numbers
import re
import sys
import traceback
from pathlib import Path

_REPO_ROOT = Path(__file__).resolve().parents[1]
if str(_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPO_ROOT))

from PIL import Image, ImageFilter, ImageStat
import numpy as np

# Roughness floor: a perfectly smooth (roughness 0) surface aliases badly at the
# game's RTS camera distance, so smoothness is capped at 1 - MIN_ROUGHNESS.
MIN_ROUGHNESS = 0.04
MAX_SMOOTHNESS = round(255 * (1.0 - MIN_ROUGHNESS))
SMOOTHNESS_LUT = [min(255 - i, MAX_SMOOTHNESS) for i in range(256)]
SMOOTHNESS_LUT_U8 = np.array(SMOOTHNESS_LUT, dtype=np.uint8)
# Statistics are sampled on a downscaled copy so the report stays cheap on 4K atlases.
STATS_SAMPLE_SIZE = 64
# sRGB albedo plausibility window for non-metal surfaces (charcoal .. fresh snow).
ALBEDO_MIN_MEAN = 30
ALBEDO_MAX_MEAN = 240
NORMAL_LENGTH_TOLERANCE = 0.12
FLAT_STDDEV = 0.75
SAFE_PREFIX = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]*$")


def _raise_task_failures(stage: str, failures: list[tuple[str, Exception]]) -> None:
    if not failures:
        return
    details = []
    for label, exc in failures:
        rendered = "".join(traceback.format_exception(exc)).rstrip()
        details.append(f"--- {label} ---\n{rendered}")
    message = f"PBR {stage} failed for {len(failures)} task(s):\n" + "\n".join(details)
    raise RuntimeError(message) from failures[0][1]


def _validate_pack_options(
    prefix: str,
    dilate_px: int,
    detail_strength: float,
    compress_level: int,
) -> None:
    if (
        not isinstance(prefix, str)
        or not SAFE_PREFIX.fullmatch(prefix)
        or prefix in {".", ".."}
    ):
        raise ValueError(f"PBR output prefix must be a simple file-name component; got {prefix!r}.")
    if isinstance(dilate_px, bool) or not isinstance(dilate_px, int):
        raise TypeError(f"UV dilation radius must be an integer >= 0; got {dilate_px!r}.")
    if dilate_px < 0:
        raise ValueError(f"UV dilation radius must be >= 0; got {dilate_px}.")
    if isinstance(detail_strength, bool) or not isinstance(detail_strength, numbers.Real):
        raise TypeError(
            f"detail_strength must be a real number from 0 through 1; got {detail_strength!r}."
        )
    if not math.isfinite(float(detail_strength)) or not 0.0 <= float(detail_strength) <= 1.0:
        raise ValueError(f"detail_strength must be finite and from 0 through 1; got {detail_strength!r}.")
    if isinstance(compress_level, bool) or not isinstance(compress_level, int):
        raise TypeError(
            f"PNG compression level must be an integer from 0 through 9; got {compress_level!r}."
        )
    if not 0 <= compress_level <= 9:
        raise ValueError(f"PNG compression level must be from 0 through 9; got {compress_level}.")


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
    parser.add_argument(
        "--compress-level",
        type=int,
        default=1,
        help="Lossless PNG zlib compression level (0-9, default 1 for fast builds).",
    )
    parser.add_argument(
        "--optimize-png",
        action="store_true",
        help="Run slower maximum-size PNG optimization passes.",
    )
    return parser.parse_args()


def require_map(directory: Path, prefix: str, suffix: str) -> Path:
    path = directory / f"{prefix}_{suffix}.png"
    if not path.is_file():
        raise FileNotFoundError(f"Material Maker did not export {path}")
    return path


def save_png(
    image: Image.Image,
    destination: Path,
    *,
    compress_level: int = 1,
    optimize_png: bool = False,
) -> None:
    if isinstance(compress_level, bool) or not isinstance(compress_level, int):
        raise TypeError(
            f"PNG compression level must be an integer from 0 through 9; got {compress_level!r}."
        )
    if compress_level < 0 or compress_level > 9:
        raise ValueError(f"PNG compression level must be from 0 through 9; got {compress_level}.")
    destination.parent.mkdir(parents=True, exist_ok=True)
    if optimize_png:
        image.save(destination, format="PNG", optimize=True)
    else:
        image.save(destination, format="PNG", compress_level=compress_level)


def _build_emission_image(source: Path) -> Image.Image:
    with Image.open(source) as image:
        rgb = image.convert("RGB")
        rgb.load()
        return rgb


def copy_opaque_rgb(source: Path, destination: Path) -> None:
    """Re-save as opaque RGB so a stray alpha channel never drives alpha clipping."""
    save_png(_build_emission_image(source), destination)


def _build_albedo_image(source: Path) -> Image.Image:
    with Image.open(source) as image:
        rgb = image.convert("RGB")
        sharpened = rgb.filter(
            ImageFilter.UnsharpMask(radius=1.2, percent=35, threshold=2)
        )
        sharpened.load()
        return sharpened


def process_albedo(source: Path, destination: Path) -> None:
    """Re-save as opaque RGB with subtle PBR-safe micro-contrast for sharp panel lines."""
    save_png(_build_albedo_image(source), destination)


def _build_normal_image(
    source: Path,
    detail_rgb: object | None = None,
    detail_strength: float = 0.35,
) -> Image.Image:
    if not isinstance(detail_strength, numbers.Real) or isinstance(detail_strength, bool):
        raise TypeError(
            f"detail_strength must be a real number from 0 through 1; got {detail_strength!r}."
        )
    if not math.isfinite(float(detail_strength)) or not 0.0 <= float(detail_strength) <= 1.0:
        raise ValueError(f"detail_strength must be finite and from 0 through 1; got {detail_strength!r}.")
    with Image.open(source) as image:
        rgb = image.convert("RGB")
        arr = np.asarray(rgb, dtype=np.float32) / 127.5 - 1.0
        arr[..., 2] = np.maximum(arr[..., 2], 0.02)
        lens = np.sqrt(np.sum(arr * arr, axis=-1, keepdims=True))
        arr /= np.maximum(lens, 1e-6)
        encoded = np.clip(np.round((arr + 1.0) * 127.5), 0, 255).astype(np.uint8)
        if detail_rgb is not None:
            from tools.functional_engineering_integration import compose_normals_rnm_rgb

            detail = np.asarray(detail_rgb)
            if detail.dtype != np.uint8:
                raise TypeError(f"detail_rgb must contain uint8 PNG texels; got {detail.dtype}.")
            encoded = compose_normals_rnm_rgb(
                encoded,
                detail,
                detail_strength=detail_strength,
            )
        return Image.fromarray(encoded, "RGB")


def normalize_normal(source: Path, destination: Path) -> None:
    """Re-save tangent normal map with exact per-pixel vector renormalization."""
    save_png(_build_normal_image(source), destination)


def _build_metallic_gloss_images(
    metallic_path: Path,
    roughness_path: Path,
) -> tuple[Image.Image, Image.Image, Image.Image]:
    with Image.open(metallic_path) as metallic_image, Image.open(roughness_path) as roughness_image:
        metallic = metallic_image.convert("L")
        roughness = roughness_image.convert("L")
        if metallic.size != roughness.size:
            raise ValueError(
                f"Metallic/Roughness dimensions differ: {metallic.size} vs {roughness.size}"
            )
        m_arr = np.asarray(metallic, dtype=np.uint8)
        r_arr = np.asarray(roughness, dtype=np.uint8)
        out_arr = np.empty((m_arr.shape[0], m_arr.shape[1], 4), dtype=np.uint8)
        out_arr[..., 0] = m_arr
        out_arr[..., 1] = m_arr
        out_arr[..., 2] = m_arr
        out_arr[..., 3] = SMOOTHNESS_LUT_U8[r_arr]
        metallic.load()
        roughness.load()
        return Image.fromarray(out_arr, "RGBA"), metallic, roughness


def pack_metallic_gloss(metallic_path: Path, roughness_path: Path, output_path: Path) -> None:
    packed, _, _ = _build_metallic_gloss_images(metallic_path, roughness_path)
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
    arr = np.asarray(sampled, dtype=np.float32) / 127.5 - 1.0
    lens = np.sqrt(np.sum(arr * arr, axis=-1))
    return float(np.mean(np.abs(lens - 1.0)))


def metallic_midtone_ratio(metallic: Image.Image) -> float:
    """Fraction of pixels that are neither dielectric (<25%) nor metal (>75%)."""
    # Bilinear resampling manufactures gray texels along binary material-mask
    # boundaries. Nearest sampling measures authored texels without changing
    # their metallic class.
    sampled = metallic.convert("L").resize(
        (STATS_SAMPLE_SIZE, STATS_SAMPLE_SIZE), Image.Resampling.NEAREST
    )
    arr = np.asarray(sampled, dtype=np.uint8)
    mid = int(np.count_nonzero((arr >= 64) & (arr <= 191)))
    return float(mid / max(1, arr.size))


def _evaluate_report_image(label: str, image: Image.Image, stats: dict[str, object], warnings: list[str]) -> None:
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


def build_report(
    paths: dict[str, Path | Image.Image],
    dimensions: dict[str, tuple[int, int]],
) -> dict[str, object]:
    warnings: list[str] = []
    stats: dict[str, object] = {}
    width, height = next(iter(dimensions.values()))

    if width & (width - 1) or height & (height - 1):
        warnings.append(f"non-power-of-two atlas {width}x{height}; mipmaps/compression are suboptimal")

    for label, source in paths.items():
        if isinstance(source, Image.Image):
            _evaluate_report_image(label, source, stats, warnings)
        else:
            with Image.open(source) as image:
                _evaluate_report_image(label, image, stats, warnings)

    return {
        "size": [width, height],
        "maxSmoothness": MAX_SMOOTHNESS,
        "stats": stats,
        "warnings": warnings,
    }


def _apply_uv_dilation_in_memory(
    source_albedo: Path,
    images: dict[str, Image.Image],
    dilate_px: int,
) -> dict[str, Image.Image]:
    if dilate_px <= 0:
        return images
    import numpy as np
    from tools.pbr_texture_algorithms import apply_uv_dilation_map, compute_uv_dilation_map

    with Image.open(source_albedo) as src:
        rgba = src.convert("RGBA")
        alpha = np.asarray(rgba, dtype=np.uint8)[..., 3]
        if int(alpha.min()) < 255:
            mask = alpha > 127
        else:
            rgb = np.asarray(rgba, dtype=np.uint8)[..., :3]
            mask = np.any(rgb > 2, axis=-1)

    dilation_map = compute_uv_dilation_map(mask, max_distance=dilate_px)
    if dilation_map is None:
        return images
    fill_mask, nearest_y, nearest_x = dilation_map

    out: dict[str, Image.Image] = {}
    for key in ("Albedo", "MetallicGloss", "Normal", "Emission"):
        img = images[key]
        dilated = apply_uv_dilation_map(np.asarray(img), fill_mask, nearest_y, nearest_x)
        out[key] = Image.fromarray(dilated, img.mode)
    return out


def pack_maps(
    input_dir: Path,
    output_dir: Path,
    prefix: str,
    dilate_px: int = 0,
    detail_normal: Path | None = None,
    detail_strength: float = 0.35,
    *,
    compress_level: int = 1,
    optimize_png: bool = False,
    detail_rgb: object | None = None,
) -> dict[str, object]:
    _validate_pack_options(prefix, dilate_px, detail_strength, compress_level)
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

    if detail_rgb is None and detail_normal is not None:
        import numpy as np

        with Image.open(detail_normal) as det_im:
            detail_rgb = np.asarray(det_im.convert("RGB"), dtype=np.uint8)

    output_paths = {
        "Albedo": output_dir / f"{prefix}_Albedo.png",
        "MetallicGloss": output_dir / f"{prefix}_MetallicGloss.png",
        "Normal": output_dir / f"{prefix}_Normal.png",
        "Emission": output_dir / f"{prefix}_Emission.png",
    }

    with ThreadPoolExecutor(max_workers=4) as pool:
        futures = {
            "Albedo": pool.submit(_build_albedo_image, paths["Albedo"]),
            "MetallicGloss": pool.submit(
                _build_metallic_gloss_images, paths["Metallic"], paths["Roughness"]
            ),
            "Normal": pool.submit(_build_normal_image, paths["Normal"], detail_rgb, detail_strength),
            "Emission": pool.submit(_build_emission_image, paths["Emission"]),
        }
        built: dict[str, object] = {}
        build_failures: list[tuple[str, Exception]] = []
        for label, future in futures.items():
            try:
                built[label] = future.result()
            except Exception as exc:
                build_failures.append((label, exc))
    _raise_task_failures("image processing", build_failures)

    albedo_img = built["Albedo"]
    mg_img, metallic_gray, roughness_gray = built["MetallicGloss"]  # type: ignore[misc]
    normal_img = built["Normal"]
    emission_img = built["Emission"]

    out_images = {
        "Albedo": albedo_img,
        "MetallicGloss": mg_img,
        "Normal": normal_img,
        "Emission": emission_img,
    }
    if dilate_px > 0:
        out_images = _apply_uv_dilation_in_memory(paths["Albedo"], out_images, dilate_px)

    report_sources: dict[str, Path | Image.Image] = {
        "Albedo": out_images["Albedo"],
        "Metallic": metallic_gray,
        "Roughness": roughness_gray,
        "Normal": out_images["Normal"],
        "Emission": out_images["Emission"],
    }

    with ThreadPoolExecutor(max_workers=4) as pool:
        save_futures = {
            key: pool.submit(
                save_png,
                out_images[key],
                output_paths[key],
                compress_level=compress_level,
                optimize_png=optimize_png,
            )
            for key in ("Albedo", "MetallicGloss", "Normal", "Emission")
        }
        report: dict[str, object] | None = None
        save_failures: list[tuple[str, Exception]] = []
        try:
            report = build_report(report_sources, dimensions)
        except Exception as exc:
            save_failures.append(("quality report", exc))
        for label, future in save_futures.items():
            try:
                future.result()
            except Exception as exc:
                save_failures.append((f"{label} PNG write", exc))
    _raise_task_failures("output generation", save_failures)
    if report is None:
        raise RuntimeError("PBR output generation completed without producing a quality report.")

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
    compress_level: int = 1,
    optimize_png: bool = False,
) -> int:
    report = pack_maps(
        input_dir,
        output_dir,
        prefix,
        dilate_px=dilate_px,
        detail_normal=detail_normal,
        detail_strength=detail_strength,
        compress_level=compress_level,
        optimize_png=optimize_png,
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
        args.compress_level,
        args.optimize_png,
    )


if __name__ == "__main__":
    raise SystemExit(main())
