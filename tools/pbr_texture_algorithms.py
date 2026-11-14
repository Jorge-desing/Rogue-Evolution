#!/usr/bin/env python3
"""Open-source algorithmic PBR texture synthesis, normal composition, and UV padding.

Provides deterministic, zero-external-service NumPy/SciPy/Pillow implementations
compatible with Rogue Command's ``VehiclePBR_VertexWear`` four-map contract:
  - UV island Voronoi edge dilation (mipmap bleed prevention)
  - Reoriented Normal Mapping (RNM) tangent-space normal composition
  - Isotropic Scharr height-to-normal & Laplacian curvature/cavity extraction
  - FFT-periodic anisotropic fBm, Worley/Voronoi cellular plates, Gray-Scott
    reaction-diffusion weathering, and stochastic tile-free sampling
"""

from __future__ import annotations

import argparse
import json
import math
import numbers
from pathlib import Path
from typing import Iterable

import numpy as np
from PIL import Image
from scipy import ndimage

MIN_ROUGHNESS = 0.04
MAX_SMOOTHNESS_F = 1.0 - MIN_ROUGHNESS


def validate_detail_strength(value: object) -> float:
    """Return a finite RNM detail weight in the supported [0, 1] range."""
    if isinstance(value, (bool, np.bool_)) or not isinstance(value, numbers.Real):
        raise TypeError(f"detail_strength must be a real number from 0 through 1; got {value!r}.")
    strength = float(value)
    if not math.isfinite(strength) or not 0.0 <= strength <= 1.0:
        raise ValueError(f"detail_strength must be finite and from 0 through 1; got {value!r}.")
    return strength


def compute_uv_dilation_map(
    mask: np.ndarray,
    max_distance: int = 16,
) -> tuple[np.ndarray, np.ndarray, np.ndarray] | None:
    """Compute reusable Voronoi dilation coordinates `(fill_mask, nearest_y, nearest_x)`."""
    if max_distance <= 0:
        return None
    valid = np.asarray(mask, dtype=bool)
    if np.all(valid) or not np.any(valid):
        return None
    distances, indices = ndimage.distance_transform_edt(~valid, return_indices=True)
    fill_mask = (~valid) & (distances <= float(max_distance))
    return fill_mask, indices[0], indices[1]


def apply_uv_dilation_map(
    image: np.ndarray,
    fill_mask: np.ndarray,
    nearest_y: np.ndarray,
    nearest_x: np.ndarray,
) -> np.ndarray:
    """Apply a precomputed Voronoi dilation map to ``image``."""
    out = image.copy()
    out[fill_mask] = image[nearest_y[fill_mask], nearest_x[fill_mask]]
    return out


def dilate_uv_islands(
    image: np.ndarray,
    mask: np.ndarray,
    max_distance: int = 16,
) -> np.ndarray:
    """Extend valid UV island texels outward using exact Euclidean Voronoi dilation.

    Args:
        image: HxWxC (or HxW) uint8 or float32 array.
        mask: HxW boolean array where True marks valid UV island texels.
        max_distance: Maximum dilation radius in pixels (<= 0 returns a copy).

    Returns:
        Dilated array with the same shape and dtype as ``image``.
    """
    if max_distance <= 0:
        return image.copy()

    valid = np.asarray(mask, dtype=bool)
    if valid.shape != image.shape[:2]:
        raise ValueError(f"Mask shape {valid.shape} does not match image {image.shape[:2]}")
    dilation_map = compute_uv_dilation_map(valid, max_distance=max_distance)
    if dilation_map is None:
        return image.copy()
    fill_mask, nearest_y, nearest_x = dilation_map
    return apply_uv_dilation_map(image, fill_mask, nearest_y, nearest_x)


def _decode_normal_rgb(rgb: np.ndarray) -> np.ndarray:
    raw = np.asarray(rgb)
    if raw.ndim != 3 or raw.shape[-1] != 3 or raw.shape[0] == 0 or raw.shape[1] == 0:
        raise ValueError(f"Tangent normal data must have a non-empty HxWx3 shape; got {raw.shape}.")
    if not np.issubdtype(raw.dtype, np.number) or np.issubdtype(raw.dtype, np.complexfloating):
        raise TypeError(f"Tangent normal data must be real numeric RGB; got {raw.dtype}.")
    if not np.all(np.isfinite(raw)):
        raise ValueError("Tangent normal data contains NaN or infinite values.")

    arr = raw.astype(np.float32, copy=False)
    minimum = float(np.min(arr))
    maximum = float(np.max(arr))
    if minimum < 0.0 or maximum > 255.0:
        raise ValueError(
            f"Encoded tangent normal channels must be in [0, 1] or [0, 255]; "
            f"observed [{minimum:g}, {maximum:g}]."
        )
    if np.issubdtype(raw.dtype, np.integer):
        return arr / 127.5 - 1.0
    if maximum <= 1.0:
        return arr * 2.0 - 1.0
    return arr / 127.5 - 1.0


def _encode_normal_rgb(normals: np.ndarray, as_uint8: bool = True) -> np.ndarray:
    n = np.asarray(normals, dtype=np.float32).copy()
    n[..., 2] = np.maximum(n[..., 2], 0.02)
    length = np.sqrt(np.sum(n * n, axis=-1, keepdims=True))
    n = n / np.maximum(length, 1e-6)
    encoded = np.clip(n * 0.5 + 0.5, 0.0, 1.0)
    if as_uint8:
        return np.clip(np.round(encoded * 255.0), 0, 255).astype(np.uint8)
    return encoded


def blend_normals_rnm(
    base_rgb: np.ndarray,
    detail_rgb: np.ndarray,
    detail_strength: float = 1.0,
) -> np.ndarray:
    """Combine two tangent-space normal maps via Reoriented Normal Mapping (RNM).

    Preserves base macro-curvature orientation while adding micro-detail relief
    without the flattening artifact of linear/overlay normal blending.
    """
    base = np.asarray(base_rgb)
    detail = np.asarray(detail_rgb)
    if base.shape != detail.shape:
        raise ValueError(f"Base/detail normal shapes must match; got {base.shape} and {detail.shape}.")
    strength_f = validate_detail_strength(detail_strength)
    n1 = _decode_normal_rgb(base).copy()
    n2 = _decode_normal_rgb(detail).copy()

    if strength_f != 1.0:
        n2[..., 0] *= strength_f
        n2[..., 1] *= strength_f
        l2 = np.sqrt(np.sum(n2 * n2, axis=-1, keepdims=True))
        n2 /= np.maximum(l2, 1e-6)

    l1 = np.sqrt(np.sum(n1 * n1, axis=-1, keepdims=True))
    n1 /= np.maximum(l1, 1e-6)

    # Compute RNM in-place on n1 (t) and n2 (u) to reduce 2K/4K temporary buffers.
    n1[..., 2] += 1.0
    n2[..., 0] = -n2[..., 0]
    n2[..., 1] = -n2[..., 1]

    dot_tu = np.sum(n1 * n2, axis=-1, keepdims=True)
    scale = dot_tu / np.maximum(n1[..., 2:3], 1e-5)
    n1 *= scale
    n1 -= n2
    return _encode_normal_rgb(n1, as_uint8=(base.dtype == np.uint8))


def height_to_normal_scharr(
    height: np.ndarray,
    strength: float = 2.0,
    tileable: bool = True,
    as_uint8: bool = False,
) -> np.ndarray:
    """Convert a 2D height field in [0, 1] to an OpenGL tangent-space normal map.

    Uses 3x3 Scharr kernels for superior rotational isotropy over Sobel/central diff.
    """
    h = np.asarray(height, dtype=np.float32)
    mode = "wrap" if tileable else "nearest"
    # Scharr weights (normalized by 32)
    kx = np.array([[-3, 0, 3], [-10, 0, 10], [-3, 0, 3]], dtype=np.float32) / 32.0
    ky = np.array([[-3, -10, -3], [0, 0, 0], [3, 10, 3]], dtype=np.float32) / 32.0

    dx = ndimage.convolve(h, kx, mode=mode) * float(strength) * (h.shape[1] / 64.0)
    dy = ndimage.convolve(h, ky, mode=mode) * float(strength) * (h.shape[0] / 64.0)

    normals = np.stack([-dx, dy, np.ones_like(dx)], axis=-1)
    return _encode_normal_rgb(normals, as_uint8=as_uint8)


def height_to_curvature_cavity(
    height: np.ndarray,
    tileable: bool = True,
    gain: float = 4.0,
) -> tuple[np.ndarray, np.ndarray]:
    """Extract (edge_wear, cavity_ao) masks in [0, 1] from a height field."""
    h = np.asarray(height, dtype=np.float32)
    mode = "wrap" if tileable else "nearest"
    lap_kernel = np.array([[0.25, 0.5, 0.25], [0.5, -3.0, 0.5], [0.25, 0.5, 0.25]], dtype=np.float32)
    # Negative Laplacian at peaks/convex ridges -> positive convexity
    convexity = -ndimage.convolve(h, lap_kernel, mode=mode) * float(gain)
    edge_wear = np.clip(convexity, 0.0, 1.0)
    cavity_ao = np.clip(1.0 + np.minimum(convexity, 0.0), 0.0, 1.0)
    return edge_wear.astype(np.float32), cavity_ao.astype(np.float32)


def synthesize_periodic_fbm(
    size: int,
    seed: int = 0,
    octaves: int = 4,
    power: float = 1.5,
    anisotropy: float = 1.0,
) -> np.ndarray:
    """Generate a seamlessly tileable multi-octave FFT-periodic fBm field in [0, 1]."""
    rng = np.random.default_rng(seed)
    fx = np.fft.fftfreq(size)[None, :] * float(anisotropy)
    fy = np.fft.fftfreq(size)[:, None]
    radius = np.sqrt(fx * fx + fy * fy)
    radius[0, 0] = 1.0

    accum = np.zeros((size, size), dtype=np.float32)
    amp = 1.0
    for oct_idx in range(max(1, octaves)):
        freq_power = power + 0.15 * oct_idx
        spectrum = (rng.normal(size=(size, size)) + 1j * rng.normal(size=(size, size))) / (
            radius**freq_power
        )
        spectrum[0, 0] = 0.0
        layer = np.real(np.fft.ifft2(spectrum)).astype(np.float32)
        layer -= layer.mean()
        std = float(layer.std())
        if std > 1e-8:
            layer /= std
        accum += amp * layer
        amp *= 0.5

    lo, hi = float(accum.min()), float(accum.max())
    if hi - lo < 1e-8:
        return np.zeros((size, size), dtype=np.float32)
    return ((accum - lo) / (hi - lo)).astype(np.float32)


def synthesize_worley_plates(
    size: int,
    seed: int = 0,
    cell_count: int = 16,
    jitter: float = 0.85,
    edge_width: float = 0.08,
) -> tuple[np.ndarray, np.ndarray]:
    """Generate toroidally tileable Worley cellular (f1, edge_seam_mask) in [0, 1]."""
    rng = np.random.default_rng(seed)
    grid_n = max(2, int(round(np.sqrt(cell_count))))
    pts_x = (np.arange(grid_n)[:, None] + 0.5 + (rng.random((grid_n, grid_n)) - 0.5) * jitter) / grid_n
    pts_y = (np.arange(grid_n)[None, :] + 0.5 + (rng.random((grid_n, grid_n)) - 0.5) * jitter) / grid_n
    points = np.stack([pts_x.ravel(), pts_y.ravel()], axis=-1)  # (K, 2)

    coords = (np.arange(size, dtype=np.float32) + 0.5) / float(size)
    gx, gy = np.meshgrid(coords, coords, indexing="ij")
    grid = np.stack([gx, gy], axis=-1)[..., None, :]  # (H, W, 1, 2)

    # Toroidal minimum distance across periodic [0, 1)^2
    delta = np.abs(grid - points[None, None, :, :])
    delta = np.minimum(delta, 1.0 - delta)
    dist_sq = np.sum(delta * delta, axis=-1)
    # Partition to get 2 smallest distances
    part = np.partition(dist_sq, 1, axis=-1)[..., :2]
    d1 = np.sqrt(np.maximum(part[..., 0], 0.0))
    d2 = np.sqrt(np.maximum(part[..., 1], 0.0))
    # Sort the 2 smallest
    f1 = np.minimum(d1, d2)
    f2 = np.maximum(d1, d2)

    f1_norm = np.clip(f1 * grid_n, 0.0, 1.0).astype(np.float32)
    seam_dist = (f2 - f1) * grid_n
    seam_mask = np.clip(1.0 - seam_dist / max(float(edge_width), 1e-4), 0.0, 1.0).astype(np.float32)
    return f1_norm, seam_mask


def synthesize_reaction_diffusion(
    size: int = 128,
    seed: int = 42,
    steps: int = 250,
    feed: float = 0.055,
    kill: float = 0.062,
) -> np.ndarray:
    """Periodic Gray-Scott reaction-diffusion solver returning chemical V in [0, 1]."""
    rng = np.random.default_rng(seed)
    u = np.ones((size, size), dtype=np.float32)
    v = np.zeros((size, size), dtype=np.float32)

    num_seeds = max(4, size // 16)
    for _ in range(num_seeds):
        cx = int(rng.integers(0, size))
        cy = int(rng.integers(0, size))
        r = max(2, size // 32)
        xs = np.arange(cx - r, cx + r) % size
        ys = np.arange(cy - r, cy + r) % size
        u[np.ix_(xs, ys)] = 0.50
        v[np.ix_(xs, ys)] = 0.25 + 0.05 * rng.random((len(xs), len(ys))).astype(np.float32)

    du, dv, dt = 0.16, 0.08, 1.0
    for _ in range(max(1, steps)):
        lu = (
            np.roll(u, 1, 0) + np.roll(u, -1, 0) + np.roll(u, 1, 1) + np.roll(u, -1, 1) - 4.0 * u
        )
        lv = (
            np.roll(v, 1, 0) + np.roll(v, -1, 0) + np.roll(v, 1, 1) + np.roll(v, -1, 1) - 4.0 * v
        )
        uvv = u * v * v
        u = np.clip(u + (du * lu - uvv + feed * (1.0 - u)) * dt, 0.0, 1.0)
        v = np.clip(v + (dv * lv + uvv - (feed + kill) * v) * dt, 0.0, 1.0)

    lo, hi = float(v.min()), float(v.max())
    if hi - lo < 1e-8:
        return np.zeros((size, size), dtype=np.float32)
    return ((v - lo) / (hi - lo)).astype(np.float32)


def stochastic_tile_sample(
    texture: np.ndarray,
    out_size: int,
    seed: int = 0,
    cell_scale: float = 4.0,
) -> np.ndarray:
    """Stochastic grid-offset texture bombing to break up periodic tiling artifacts."""
    src = np.asarray(texture, dtype=np.float32)
    if src.ndim == 2:
        src = src[..., None]
    sh, sw, sc = src.shape
    rng = np.random.default_rng(seed)

    y = np.linspace(0.0, cell_scale, out_size, endpoint=False, dtype=np.float32)
    x = np.linspace(0.0, cell_scale, out_size, endpoint=False, dtype=np.float32)
    gy, gx = np.meshgrid(y, x, indexing="ij")

    ix = np.floor(gx).astype(np.int32)
    iy = np.floor(gy).astype(np.int32)
    fx = gx - ix
    fy = gy - iy

    # Deterministic per-cell random UV offsets
    table_size = 64
    offsets = rng.random((table_size, table_size, 2), dtype=np.float32)

    def _sample_cell(cx: np.ndarray, cy: np.ndarray) -> np.ndarray:
        off = offsets[cy % table_size, cx % table_size]
        u = ((gx / cell_scale + off[..., 0]) * sw).astype(np.int32) % sw
        v = ((gy / cell_scale + off[..., 1]) * sh).astype(np.int32) % sh
        return src[v, u]

    c00 = _sample_cell(ix, iy)
    c10 = _sample_cell(ix + 1, iy)
    c01 = _sample_cell(ix, iy + 1)
    c11 = _sample_cell(ix + 1, iy + 1)

    wx = (fx * fx * (3.0 - 2.0 * fx))[..., None]
    wy = (fy * fy * (3.0 - 2.0 * fy))[..., None]

    blended = (c00 * (1.0 - wx) + c10 * wx) * (1.0 - wy) + (c01 * (1.0 - wx) + c11 * wx) * wy
    return blended[..., 0] if texture.ndim == 2 else blended


def normal_to_curvature_cavity(
    normal_rgb: np.ndarray,
    tileable: bool = True,
    gain: float = 2.5,
) -> tuple[np.ndarray, np.ndarray]:
    """Derive (edge_wear, cavity_ao) masks in [0, 1] directly from a tangent-space normal map."""
    n = _decode_normal_rgb(normal_rgb)
    mode = "wrap" if tileable else "nearest"
    kx = np.array([[-3, 0, 3], [-10, 0, 10], [-3, 0, 3]], dtype=np.float32) / 32.0
    ky = np.array([[-3, -10, -3], [0, 0, 0], [3, 10, 3]], dtype=np.float32) / 32.0
    dnx_dx = ndimage.convolve(n[..., 0], kx, mode=mode)
    dny_dy = ndimage.convolve(n[..., 1], ky, mode=mode)
    # In OpenGL tangent normal maps, +nx tilts right and +ny tilts up; positive divergence = convex ridge
    div = (dnx_dx - dny_dy) * float(gain)
    edge_wear = np.clip(div, 0.0, 1.0).astype(np.float32)
    cavity_ao = np.clip(1.0 + np.minimum(div, 0.0), 0.0, 1.0).astype(np.float32)
    return edge_wear, cavity_ao


def triplanar_project_blend(
    texture: np.ndarray,
    positions: np.ndarray,
    normals: np.ndarray,
    scale: float = 1.0,
    sharpness: float = 4.0,
) -> np.ndarray:
    """Sample a 2D tileable texture across 3D positions/normals using triplanar blending."""
    src = np.asarray(texture, dtype=np.float32)
    if src.ndim == 2:
        src = src[..., None]
    sh, sw, _ = src.shape
    pos = np.asarray(positions, dtype=np.float32) * float(scale)
    nrm = np.abs(np.asarray(normals, dtype=np.float32))
    weights = np.power(np.maximum(nrm, 1e-4), float(sharpness))
    weights /= np.sum(weights, axis=-1, keepdims=True)

    def _sample(uv: np.ndarray) -> np.ndarray:
        u = np.mod(uv[..., 0] * sw, sw).astype(np.int32) % sw
        v = np.mod(uv[..., 1] * sh, sh).astype(np.int32) % sh
        return src[v, u]

    tx = _sample(pos[..., [1, 2]])
    ty = _sample(pos[..., [0, 2]])
    tz = _sample(pos[..., [0, 1]])
    blended = tx * weights[..., 0:1] + ty * weights[..., 1:2] + tz * weights[..., 2:3]
    return blended[..., 0] if texture.ndim == 2 else blended


def pack_metallic_gloss_array(metallic: np.ndarray, roughness: np.ndarray) -> np.ndarray:
    """Pack grayscale metallic & roughness [0..255 or 0..1] into Rogue Command MetallicGloss RGBA."""
    m = np.asarray(metallic, dtype=np.float32)
    r = np.asarray(roughness, dtype=np.float32)
    if m.max() > 1.001 or m.dtype == np.uint8:
        m = m / 255.0
    if r.max() > 1.001 or r.dtype == np.uint8:
        r = r / 255.0
    m_u8 = np.clip(np.round(m * 255.0), 0, 255).astype(np.uint8)
    s_u8 = np.clip(np.round(np.minimum(1.0 - r, MAX_SMOOTHNESS_F) * 255.0), 0, 255).astype(np.uint8)
    return np.stack([m_u8, m_u8, m_u8, s_u8], axis=-1)


def _load_rgb(path: Path) -> np.ndarray:
    with Image.open(path) as img:
        return np.asarray(img.convert("RGB"), dtype=np.uint8)


def _save_png(arr: np.ndarray, path: Path, compress_level: int = 1) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    mode = "RGBA" if arr.ndim == 3 and arr.shape[2] == 4 else "RGB"
    Image.fromarray(arr, mode).save(path, format="PNG", compress_level=compress_level)


def parse_args(argv: Iterable[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)

    rnm = sub.add_parser("compose-normal", help="Blend base and detail tangent-space normal maps with RNM.")
    rnm.add_argument("--base", type=Path, required=True)
    rnm.add_argument("--detail", type=Path, required=True)
    rnm.add_argument("--strength", type=float, default=0.75)
    rnm.add_argument("--output", type=Path, required=True)

    synth = sub.add_parser("synthesize-armor", help="Generate tileable procedural armor/detail maps.")
    synth.add_argument("--size", type=int, default=512)
    synth.add_argument("--seed", type=int, default=20261113)
    synth.add_argument("--output-dir", type=Path, required=True)
    synth.add_argument("--prefix", default="ProceduralArmor")

    curv = sub.add_parser("normal-to-curvature", help="Extract edge wear and cavity AO masks from a normal map.")
    curv.add_argument("--normal", type=Path, required=True)
    curv.add_argument("--gain", type=float, default=2.5)
    curv.add_argument("--output-dir", type=Path, required=True)
    curv.add_argument("--prefix", default="Curvature")

    return parser.parse_args(list(argv) if argv is not None else None)


def main(argv: Iterable[str] | None = None) -> int:
    args = parse_args(argv)
    if args.command == "compose-normal":
        base = _load_rgb(args.base)
        detail = _load_rgb(args.detail)
        if detail.shape[:2] != base.shape[:2]:
            detail_img = Image.fromarray(detail, "RGB").resize((base.shape[1], base.shape[0]), Image.Resampling.BILINEAR)
            detail = np.asarray(detail_img, dtype=np.uint8)
        blended = blend_normals_rnm(base, detail, detail_strength=args.strength)
        _save_png(blended, args.output)
        print(json.dumps({"command": "compose-normal", "output": str(args.output), "shape": list(blended.shape)}))
        return 0

    if args.command == "synthesize-armor":
        fbm = synthesize_periodic_fbm(args.size, seed=args.seed, octaves=4, anisotropy=2.0)
        f1, seams = synthesize_worley_plates(args.size, seed=args.seed + 1, cell_count=16)
        height = np.clip(0.65 * fbm + 0.35 * (1.0 - seams * 0.6) - 0.1 * f1, 0.0, 1.0)
        normal = height_to_normal_scharr(height, strength=2.2, tileable=True, as_uint8=True)
        wear, cavity = height_to_curvature_cavity(height, tileable=True, gain=4.0)

        base_rgb = np.stack([
            np.clip(np.round((0.18 + 0.14 * wear - 0.08 * (1.0 - cavity)) * 255.0), 0, 255),
            np.clip(np.round((0.22 + 0.14 * wear - 0.08 * (1.0 - cavity)) * 255.0), 0, 255),
            np.clip(np.round((0.25 + 0.14 * wear - 0.08 * (1.0 - cavity)) * 255.0), 0, 255),
        ], axis=-1).astype(np.uint8)
        metallic = np.clip(0.35 + 0.55 * wear, 0.0, 1.0)
        roughness = np.clip(0.55 - 0.25 * wear + 0.15 * (1.0 - cavity), MIN_ROUGHNESS, 1.0)
        mg = pack_metallic_gloss_array(metallic, roughness)
        emission = np.zeros_like(base_rgb)

        out_dir = args.output_dir
        _save_png(base_rgb, out_dir / f"{args.prefix}_Albedo.png")
        _save_png(mg, out_dir / f"{args.prefix}_MetallicGloss.png")
        _save_png(normal, out_dir / f"{args.prefix}_Normal.png")
        _save_png(emission, out_dir / f"{args.prefix}_Emission.png")
        print(json.dumps({"command": "synthesize-armor", "outputDir": str(out_dir), "prefix": args.prefix, "size": args.size}))
        return 0

    if args.command == "normal-to-curvature":
        normal_rgb = _load_rgb(args.normal)
        wear, cavity = normal_to_curvature_cavity(normal_rgb, tileable=True, gain=args.gain)
        out_dir = args.output_dir
        out_dir.mkdir(parents=True, exist_ok=True)
        Image.fromarray(np.clip(np.round(wear * 255.0), 0, 255).astype(np.uint8), "L").save(
            out_dir / f"{args.prefix}_EdgeWear.png", format="PNG", compress_level=1
        )
        Image.fromarray(np.clip(np.round(cavity * 255.0), 0, 255).astype(np.uint8), "L").save(
            out_dir / f"{args.prefix}_CavityAO.png", format="PNG", compress_level=1
        )
        print(json.dumps({"command": "normal-to-curvature", "outputDir": str(out_dir), "prefix": args.prefix}))
        return 0

    return 1


if __name__ == "__main__":
    raise SystemExit(main())
