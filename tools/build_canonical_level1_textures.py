#!/usr/bin/env python3
"""Hybrid Material Maker 1.7 + UV-region PBR composer for Level 1 canonical assets."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import subprocess
import sys
from typing import Dict, Mapping, Sequence, Tuple

import numpy as np
from PIL import Image, ImageDraw
try:
    from scipy import ndimage
except ImportError:
    ndimage = None  # type: ignore[assignment]

REPO_ROOT = Path(__file__).resolve().parent.parent
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from tools.UnitPbrPresetRegistry import UNIT_PBR_REGISTRY  # noqa: E402
from tools.audit_material_maker_output import build_quality_report  # noqa: E402
from tools.pack_material_maker_pbr import pack_maps  # noqa: E402
from tools.pbr_texture_algorithms import blend_normals_rnm, height_to_normal_scharr  # noqa: E402

REGION_GUTTER = 0
REGION_PRIMARY_ARMOR = 1
REGION_LIGHT_ARMOR = 2
REGION_GUNMETAL = 3
REGION_TREAD = 4
REGION_WHEEL_METAL = 5
REGION_WARNING = 6
REGION_EMISSIVE = 7

FACTORY_PRESET_MAP: Mapping[str, Tuple[str, str]] = {
    "BulwarkFoundry": ("dreadnought_adamantine_hull", "#32363b"),
    "ArachnidHexapodFactory": ("void_stalker_metamaterial", "#25292e"),
    "OverlordCommandHQFactory": ("fusion_reactor_core_plating", "#3a4149"),
}

DRAW_ORDER = (
    REGION_PRIMARY_ARMOR,
    REGION_GUNMETAL,
    REGION_LIGHT_ARMOR,
    REGION_TREAD,
    REGION_WHEEL_METAL,
    REGION_WARNING,
    REGION_EMISSIVE,
)

MODELS_DIR = REPO_ROOT / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Models"
TEXTURES_DIR = MODELS_DIR / "Textures"
DETAIL_NORMAL_PATH = (
    REPO_ROOT
    / "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Textures/Detail/RCL_SteelMicroDetail_Normal.png"
)
DEFAULT_MM_CACHE = REPO_ROOT / "artifacts/level1-mm-cache"
DEFAULT_UV_CACHE = REPO_ROOT / "artifacts/level1-uv-cache"
BLENDER_EXE = REPO_ROOT / ".tools/Blender-4.5.14/blender-4.5.14-windows-x64/blender.exe"

# Local finish tuning keeps the walker armor readable under grazing light while
# preserving Material Maker variation above each floor and RNM micro-detail.
ASSET_FINISH = {
    "IroncladWalker": {
        "roughnessFloors": {REGION_PRIMARY_ARMOR: 155, REGION_LIGHT_ARMOR: 145, REGION_GUNMETAL: 130},
        "detailStrength": 0.08,
    },
    "AtlasCraneMech": {
        "roughnessFloors": {REGION_PRIMARY_ARMOR: 165, REGION_LIGHT_ARMOR: 155, REGION_GUNMETAL: 145, REGION_WARNING: 140},
        "normalStrength": 0.25,
        "detailStrength": 0.08,
    },
    "ArcLanceWalker": {
        "roughnessFloors": {REGION_PRIMARY_ARMOR: 165, REGION_LIGHT_ARMOR: 155, REGION_GUNMETAL: 145},
        "normalStrength": 0.25,
        "detailStrength": 0.08,
    },
}


def _hex_to_rgb(hex_color: str) -> Tuple[int, int, int]:
    cleaned = hex_color.strip().lstrip("#")
    if len(cleaned) != 6:
        return (128, 128, 128)
    return (int(cleaned[0:2], 16), int(cleaned[2:4], 16), int(cleaned[4:6], 16))


def resolve_asset_preset(asset_name: str) -> Tuple[str, str]:
    for profile in UNIT_PBR_REGISTRY.values():
        if profile.model_name == asset_name and profile.pack_id != "sample-expansion":
            return profile.primary_preset, profile.primary_color_tint
    if asset_name in FACTORY_PRESET_MAP:
        return FACTORY_PRESET_MAP[asset_name]
    raise KeyError(f"No Level 1 Material Maker preset mapping exists for asset: {asset_name}")


def _template_prefix_for_preset(preset: str) -> str:
    return "tactical-military-template" if preset == "olive_armor" else f"tactical-{preset}-template"


def _load_mm_raw_tiles(preset: str, mm_cache_root: Path, resolution: int) -> dict[str, np.ndarray]:
    raw_dir = mm_cache_root / "texture-work" / preset / "raw"
    prefix = _template_prefix_for_preset(preset)
    out: dict[str, np.ndarray] = {}
    for suffix, mode in (
        ("Albedo", "RGB"),
        ("Metallic", "L"),
        ("Roughness", "L"),
        ("Normal", "RGB"),
        ("Emission", "RGB"),
    ):
        path = raw_dir / f"{prefix}_{suffix}.png"
        if not path.is_file():
            raise FileNotFoundError(f"Missing Material Maker 1.7 raw tile: {path}")
        with Image.open(path) as img:
            if img.size != (resolution, resolution):
                img = img.resize((resolution, resolution), Image.Resampling.BILINEAR)
            converted = img.convert(mode)
            out[suffix] = np.asarray(converted, dtype=np.uint8)
    return out


def rasterize_uv_region_mask(
    triangles: np.ndarray,
    regions: np.ndarray,
    resolution: int = 2048,
) -> np.ndarray:
    """Rasterize semantic material UV triangles into a single-channel region ID mask."""
    canvas = Image.new("L", (resolution, resolution), REGION_GUTTER)
    draw = ImageDraw.Draw(canvas)
    scale = float(resolution - 1)
    by_region: dict[int, list[int]] = {}
    for idx, reg_id in enumerate(regions.tolist()):
        by_region.setdefault(int(reg_id), []).append(idx)

    for reg_id in DRAW_ORDER:
        indices = by_region.get(reg_id)
        if not indices:
            continue
        sub = triangles[indices]
        # Convert Blender bottom-left UV (0,0) -> Pillow top-left pixel space.
        xs = np.clip(sub[:, 0::2], 0.0, 1.0) * scale
        ys = (1.0 - np.clip(sub[:, 1::2], 0.0, 1.0)) * scale
        for row_idx in range(sub.shape[0]):
            poly = [
                (float(xs[row_idx, 0]), float(ys[row_idx, 0])),
                (float(xs[row_idx, 1]), float(ys[row_idx, 1])),
                (float(xs[row_idx, 2]), float(ys[row_idx, 2])),
            ]
            draw.polygon(poly, fill=reg_id)
    return np.asarray(canvas, dtype=np.uint8)


def _apply_bimodal_contrast(gray: np.ndarray, *, high_floor: int = 215, low_ceil: int = 20) -> np.ndarray:
    """Snap soft transition values outside the midtone band to keep dielectric/metal separation crisp."""
    out = gray.copy()
    hi = gray >= 128
    out[hi] = np.maximum(gray[hi], high_floor)
    out[~hi] = np.minimum(gray[~hi], low_ceil)
    return out


def _attenuate_normal(normal_rgb: np.ndarray, factor: float) -> np.ndarray:
    vec = normal_rgb.astype(np.float32) / 127.5 - 1.0
    vec[..., 0] *= factor
    vec[..., 1] *= factor
    vec[..., 2] = np.maximum(vec[..., 2], 0.05)
    lens = np.sqrt(np.sum(vec * vec, axis=-1, keepdims=True))
    vec /= np.maximum(lens, 1e-6)
    return np.clip(np.round((vec + 1.0) * 127.5), 0, 255).astype(np.uint8)


def _compute_uv_island_bevel_fields(
    region_mask: np.ndarray,
) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    """Compute distance-to-boundary bevel, seam cavity, height, and small-island damping."""
    non_emissive = (region_mask != REGION_GUTTER) & (region_mask != REGION_EMISSIVE)
    edge_mask = np.zeros_like(region_mask, dtype=bool)
    edge_mask[:, 1:] |= region_mask[:, 1:] != region_mask[:, :-1]
    edge_mask[:, :-1] |= region_mask[:, :-1] != region_mask[:, 1:]
    edge_mask[1:, :] |= region_mask[1:, :] != region_mask[:-1, :]
    edge_mask[:-1, :] |= region_mask[:-1, :] != region_mask[1:, :]
    edge_mask |= region_mask == REGION_GUTTER

    if ndimage is not None:
        dist = ndimage.distance_transform_edt(~edge_mask).astype(np.float32)
    else:
        dist = np.where(edge_mask, 0.0, 8.0).astype(np.float32)
    bevel_ring = np.exp(-0.5 * ((dist - 2.2) / 1.4) ** 2).astype(np.float32) * non_emissive
    seam_cavity = np.exp(-0.5 * ((dist - 0.5) / 1.1) ** 2).astype(np.float32) * non_emissive
    bevel_height = np.clip((dist - 1.0) / 5.0, 0.0, 1.0).astype(np.float32) * non_emissive
    groove_weight = np.clip((dist - 1.5) / 8.0, 0.0, 1.0).astype(np.float32)
    return bevel_ring, seam_cavity, bevel_height, groove_weight


def _stabilize_tile_grooves(
    tile_rgb: np.ndarray,
    groove_weight: np.ndarray,
    *,
    floor_luma: float,
) -> np.ndarray:
    """Prevent dark global brick mortar from crushing small UV islands into near-black streaks."""
    luma = (
        0.2126 * tile_rgb[..., 0]
        + 0.7152 * tile_rgb[..., 1]
        + 0.0722 * tile_rgb[..., 2]
    )
    target_floor = floor_luma * (1.0 - 0.15 * groove_weight)
    needs_lift = luma < target_floor
    if not np.any(needs_lift):
        return tile_rgb
    out = tile_rgb.copy()
    scale = np.ones_like(luma, dtype=np.float32)
    scale[needs_lift] = target_floor[needs_lift] / np.maximum(luma[needs_lift], 8.0)
    out = np.clip(out * np.minimum(scale, 4.0)[..., None], 0.0, 255.0)
    return out


def _soften_tile_contrast(tile_rgb: np.ndarray, keep_detail: float = 0.50) -> np.ndarray:
    """Compress high-contrast blotches/mortar streaks while preserving micro-surface grain."""
    mean_rgb = np.mean(tile_rgb, axis=(0, 1), keepdims=True)
    return np.clip(mean_rgb * (1.0 - keep_detail) + tile_rgb * keep_detail, 0.0, 255.0)


def _lift_tint_for_urp(rgb: Tuple[int, int, int], min_mean: float) -> np.ndarray:
    """Hue-preserving luminance lift so dark role tints do not crush URP linear lighting."""
    arr = np.array(rgb, dtype=np.float32)
    mean_val = float(np.mean(arr))
    if mean_val < 1e-3:
        return np.full(3, min_mean, dtype=np.float32)
    if mean_val >= min_mean:
        return arr
    return np.clip(arr * (min_mean / mean_val), 0.0, 240.0)


def compose_hybrid_scalar_maps(
    region_mask: np.ndarray,
    primary_tiles: Mapping[str, np.ndarray],
    gunmetal_tiles: Mapping[str, np.ndarray],
    *,
    primary_tint_rgb: Tuple[int, int, int],
    warning_rgb: Tuple[int, int, int],
    emissive_rgb: Tuple[int, int, int],
) -> dict[str, np.ndarray]:
    """Compose 5 Material Maker-driven scalar maps modulated by per-material UV region masks."""
    p_alb = primary_tiles["Albedo"].astype(np.float32)
    p_met = _apply_bimodal_contrast(primary_tiles["Metallic"], high_floor=216, low_ceil=18)
    p_rgh = primary_tiles["Roughness"]
    p_nrm = primary_tiles["Normal"]

    g_alb = gunmetal_tiles["Albedo"].astype(np.float32)
    g_met = _apply_bimodal_contrast(gunmetal_tiles["Metallic"], high_floor=228, low_ceil=18)
    g_rgh = gunmetal_tiles["Roughness"]
    g_nrm = gunmetal_tiles["Normal"]

    bevel_ring, seam_cavity, bevel_height, groove_weight = _compute_uv_island_bevel_fields(region_mask)
    p_alb_stab = _soften_tile_contrast(_stabilize_tile_grooves(p_alb, groove_weight, floor_luma=126.0), keep_detail=0.45)
    g_alb_stab = _soften_tile_contrast(_stabilize_tile_grooves(g_alb, groove_weight, floor_luma=112.0), keep_detail=0.55)

    # Fill gutter background with muted Material Maker base tone while keeping alpha=0
    # outside UV islands so Voronoi UV dilation still bleeds exact edge texels out by
    # dilate_px without dark-void gutter pulling whole-canvas albedo below URP floor.
    h, w = region_mask.shape
    albedo = np.zeros((h, w, 4), dtype=np.uint8)
    albedo[..., :3] = np.clip(g_alb * 0.45 + 64.0, 88.0, 148.0).astype(np.uint8)
    metallic = np.zeros((h, w), dtype=np.uint8)
    roughness = np.full((h, w), 148, dtype=np.uint8)
    normal = np.empty((h, w, 3), dtype=np.uint8)
    normal[..., 0] = 128
    normal[..., 1] = 128
    normal[..., 2] = 255
    emission = np.zeros((h, w, 3), dtype=np.uint8)

    # subtle surface noise factor normalized around 1.0 from MM roughness grain
    grain = np.clip(0.88 + (p_rgh.astype(np.float32) / 255.0) * 0.24, 0.86, 1.12)[..., None]

    # 1: Primary armor (Material Maker primary preset + URP-calibrated role tint cohesion)
    m = region_mask == REGION_PRIMARY_ARMOR
    if np.any(m):
        tint = _lift_tint_for_urp(primary_tint_rgb, 108.0)
        blended = np.clip(p_alb_stab * 0.42 + tint * 0.58 * grain, 64.0, 238.0).astype(np.uint8)
        albedo[m, :3] = blended[m]
        metallic[m] = np.where(p_met[m] >= 128, 16, 4).astype(np.uint8)
        roughness[m] = np.clip(p_rgh[m], 64, 210)
        normal[m] = p_nrm[m]

    # 2: Light armor (lifted contrasting panel plating derived from MM primary preset)
    m = region_mask == REGION_LIGHT_ARMOR
    if np.any(m):
        tint = _lift_tint_for_urp(primary_tint_rgb, 146.0)
        lifted = np.clip(p_alb_stab * 0.42 + tint * 0.58 * grain + 28.0, 84.0, 244.0).astype(np.uint8)
        albedo[m, :3] = lifted[m]
        metallic[m] = np.where(p_met[m] >= 128, 16, 4).astype(np.uint8)
        roughness[m] = np.clip(p_rgh[m].astype(np.int16) - 10, 52, 196).astype(np.uint8)
        normal[m] = p_nrm[m]

    # 3: Gunmetal structural mechanical steel (MM gunmetal_chassis, URP F0 floor)
    m = region_mask == REGION_GUNMETAL
    if np.any(m):
        steel = np.clip(g_alb_stab * 0.94 + 14.0, 56.0, 212.0).astype(np.uint8)
        albedo[m, :3] = steel[m]
        metallic[m] = g_met[m]
        roughness[m] = np.clip(g_rgh[m], 40, 175)
        normal[m] = g_nrm[m]

    # 4: Treads / Tires (matte dielectric elastomer with subtle MM micro-grain)
    m = region_mask == REGION_TREAD
    if np.any(m):
        rubber = np.clip((g_alb * 0.20 + 36.0) * grain, 38.0, 88.0).astype(np.uint8)
        albedo[m, :3] = rubber[m]
        metallic[m] = 0
        roughness[m] = np.clip(p_rgh[m].astype(np.float32) * 0.38 + 168.0, 178, 232).astype(np.uint8)
        normal[m] = _attenuate_normal(g_nrm, 0.45)[m]

    # 5: Wheel hub / rim machined alloy
    m = region_mask == REGION_WHEEL_METAL
    if np.any(m):
        rim = np.clip(g_alb_stab * 1.04 + 16.0, 68.0, 218.0).astype(np.uint8)
        albedo[m, :3] = rim[m]
        metallic[m] = np.maximum(g_met[m], 232)
        roughness[m] = np.clip(g_rgh[m].astype(np.int16) - 12, 36, 165).astype(np.uint8)
        normal[m] = g_nrm[m]

    # 6: Warning / hazard industrial markings (painted enamel over MM seam normal)
    m = region_mask == REGION_WARNING
    if np.any(m):
        warn_base = np.array(warning_rgb, dtype=np.float32)
        if float(np.mean(warn_base)) < 90.0:
            warn_base = np.array([226.0, 148.0, 28.0], dtype=np.float32)
        painted = np.clip(warn_base * grain, 56.0, 240.0).astype(np.uint8)
        albedo[m, :3] = painted[m]
        metallic[m] = 12
        roughness[m] = np.clip(p_rgh[m].astype(np.float32) * 0.25 + 98.0, 96, 145).astype(np.uint8)
        normal[m] = _attenuate_normal(p_nrm, 0.35)[m]

    # 7: Emissive optics & reactor windows (dark glass base + selective HDR emission)
    m = region_mask == REGION_EMISSIVE
    if np.any(m):
        albedo[m, 0] = 28
        albedo[m, 1] = 36
        albedo[m, 2] = 44
        metallic[m] = 14
        roughness[m] = 36
        normal[m, 0] = 128
        normal[m, 1] = 128
        normal[m, 2] = 255
        e_col = np.clip(np.array(emissive_rgb, dtype=np.float32), 0.0, 255.0).astype(np.uint8)
        emission[m] = e_col

    # UV island-aware bevel edge gleam, seam cavity shading, and RNM chamfer normals
    non_emissive = (region_mask != REGION_GUTTER) & (region_mask != REGION_EMISSIVE)
    metal_bevel_regions = (
        (region_mask == REGION_PRIMARY_ARMOR)
        | (region_mask == REGION_LIGHT_ARMOR)
        | (region_mask == REGION_GUNMETAL)
        | (region_mask == REGION_WHEEL_METAL)
    )
    if np.any(non_emissive):
        alb_f = albedo[..., :3].astype(np.float32)
        rgh_f = roughness.astype(np.float32)
        alb_f[non_emissive] *= (1.0 - 0.14 * seam_cavity[non_emissive])[..., None]
        rgh_f[non_emissive] += 18.0 * seam_cavity[non_emissive]

        if np.any(metal_bevel_regions):
            alb_f[metal_bevel_regions] += (28.0 * bevel_ring[metal_bevel_regions])[..., None]
            rgh_f[metal_bevel_regions] -= 24.0 * bevel_ring[metal_bevel_regions]
            chamfer_hits = metal_bevel_regions & (bevel_ring > 0.45)
            metallic[chamfer_hits] = np.maximum(metallic[chamfer_hits], 224)

        albedo[..., :3] = np.clip(alb_f, 24.0, 244.0).astype(np.uint8)
        roughness = np.clip(rgh_f, 34.0, 236.0).astype(np.uint8)

        bevel_normal = height_to_normal_scharr(bevel_height, strength=1.75, tileable=False, as_uint8=True)
        blended_nrm = blend_normals_rnm(normal, bevel_normal, detail_strength=0.65)
        normal[non_emissive] = blended_nrm[non_emissive]

    # Alpha drives Voronoi UV island dilation inside pack_material_maker_pbr.pack_maps.
    covered = region_mask != REGION_GUTTER
    albedo[covered, 3] = 255
    albedo[~covered, 3] = 0

    return {
        "Albedo": albedo,
        "Metallic": metallic,
        "Roughness": roughness,
        "Normal": normal,
        "Emission": emission,
    }


def build_textures_for_assets(
    asset_names: Sequence[str],
    *,
    resolution: int = 2048,
    dilate_px: int = 8,
    detail_strength: float | None = None,
    mm_cache_root: Path = DEFAULT_MM_CACHE,
    uv_cache_root: Path = DEFAULT_UV_CACHE,
    output_dir: Path = TEXTURES_DIR,
    skip_mm_export: bool = False,
    skip_uv_extract: bool = False,
) -> list[dict[str, object]]:
    assets = list(asset_names)
    presets_needed = {"gunmetal_chassis"}
    resolved: dict[str, Tuple[str, str]] = {}
    for asset in assets:
        preset, tint = resolve_asset_preset(asset)
        resolved[asset] = (preset, tint)
        presets_needed.add(preset)

    if not skip_mm_export:
        ps_script = REPO_ROOT / "tools/Export-MaterialMakerRawPresets.ps1"
        size_power = int(round(np.log2(resolution)))
        cmd = [
            "powershell",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            str(ps_script),
            "-SizePower",
            str(size_power),
            "-OutputRoot",
            str(mm_cache_root),
            "-Presets",
            ",".join(sorted(presets_needed)),
        ]
        subprocess.run(cmd, cwd=str(REPO_ROOT), check=True)

    if not skip_uv_extract:
        extract_script = REPO_ROOT / "tools/extract_level1_uv_regions.py"
        cmd = [
            str(BLENDER_EXE),
            "--background",
            "--python",
            str(extract_script),
            "--",
            "--models-dir",
            str(MODELS_DIR),
            "--output-dir",
            str(uv_cache_root),
            *assets,
        ]
        subprocess.run(cmd, cwd=str(REPO_ROOT), check=True)

    raw_tile_cache: dict[str, dict[str, np.ndarray]] = {
        preset: _load_mm_raw_tiles(preset, mm_cache_root, resolution)
        for preset in sorted(presets_needed)
    }
    gunmetal_tiles = raw_tile_cache["gunmetal_chassis"]

    with Image.open(DETAIL_NORMAL_PATH) as det_im:
        detail_rgb = np.asarray(det_im.convert("RGB"), dtype=np.uint8)

    summaries: list[dict[str, object]] = []
    staged_root = mm_cache_root / "staged"
    output_dir.mkdir(parents=True, exist_ok=True)

    for asset in assets:
        preset, tint_hex = resolved[asset]
        npz_path = uv_cache_root / f"{asset}_uv_triangles.npz"
        meta_path = uv_cache_root / f"{asset}_uv_meta.json"
        if not npz_path.is_file() or not meta_path.is_file():
            raise FileNotFoundError(f"Missing extracted UV region data for {asset}: {npz_path}")

        data = np.load(npz_path)
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
        mask = rasterize_uv_region_mask(data["triangles"], data["regions"], resolution=resolution)
        warning_rgb = tuple(meta.get("regionColors", {}).get(str(REGION_WARNING), [226, 148, 28]))
        emissive_rgb = tuple(meta.get("emissiveRgb", [64, 210, 245]))
        scalars = compose_hybrid_scalar_maps(
            mask,
            raw_tile_cache[preset],
            gunmetal_tiles,
            primary_tint_rgb=_hex_to_rgb(tint_hex),
            warning_rgb=(int(warning_rgb[0]), int(warning_rgb[1]), int(warning_rgb[2])),
            emissive_rgb=(int(emissive_rgb[0]), int(emissive_rgb[1]), int(emissive_rgb[2])),
        )
        finish = ASSET_FINISH.get(asset, {})
        for region, floor in finish.get("roughnessFloors", {}).items():
            selected = mask == region
            scalars["Roughness"][selected] = np.maximum(scalars["Roughness"][selected], floor)
        if "normalStrength" in finish:
            scalars["Normal"] = _attenuate_normal(scalars["Normal"], finish["normalStrength"])
        effective_detail_strength = (detail_strength if detail_strength is not None
                                     else finish.get("detailStrength", 0.35))

        staged_dir = staged_root / asset
        staged_dir.mkdir(parents=True, exist_ok=True)
        for suffix, arr in scalars.items():
            mode = "RGBA" if arr.ndim == 3 and arr.shape[2] == 4 else ("RGB" if arr.ndim == 3 else "L")
            Image.fromarray(arr, mode=mode).save(staged_dir / f"{asset}_{suffix}.png", compress_level=1)

        pack_report = pack_maps(
            staged_dir,
            output_dir,
            asset,
            dilate_px=dilate_px,
            detail_strength=effective_detail_strength,
            detail_rgb=detail_rgb,
            compress_level=1,
        )
        if pack_report.get("warnings"):
            raise RuntimeError(f"PBR pack warnings for {asset}: {pack_report['warnings']}")

        tpl_path = REPO_ROOT / "artifacts/mm-templates" / f"{_template_prefix_for_preset(preset)}.ptex"
        mm_report = build_quality_report(
            output_dir,
            asset,
            template_path=tpl_path if tpl_path.is_file() else None,
            workflow_name="urp",
        )
        if mm_report.get("failures"):
            raise RuntimeError(f"Material Maker strict audit failures for {asset}: {mm_report['failures']}")

        summaries.append({
            "asset": asset,
            "preset": preset,
            "finish": {"roughnessFloors": finish.get("roughnessFloors", {}),
                       "normalStrength": finish.get("normalStrength", 1.0),
                       "detailStrength": effective_detail_strength},
            "packReport": pack_report,
            "auditFailures": mm_report.get("failures", []),
        })
        print(f"[level1-pbr] {asset} ({preset}): PASS | midtone={pack_report['stats']['Metallic']['midtoneRatio']} | lenErr={pack_report['stats']['Normal']['lengthError']}")

    return summaries


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("assets", nargs="+", help="Model names (e.g. BastionSiegeMech CommandRelay ...)")
    parser.add_argument("--resolution", type=int, default=2048)
    parser.add_argument("--dilate-px", type=int, default=8)
    parser.add_argument("--detail-strength", type=float, default=None,
                        help="Override the per-asset detail-normal strength (default 0.35).")
    parser.add_argument("--skip-mm-export", action="store_true")
    parser.add_argument("--skip-uv-extract", action="store_true")
    args = parser.parse_args()
    build_textures_for_assets(
        args.assets,
        resolution=args.resolution,
        dilate_px=args.dilate_px,
        detail_strength=args.detail_strength,
        skip_mm_export=args.skip_mm_export,
        skip_uv_extract=args.skip_uv_extract,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
