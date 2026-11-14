#!/usr/bin/env python3
"""Integration layer tying open-source geometry & PBR algorithms into Rogue Command's
Real Functional Engineering, Form Hierarchy, and Canonical Two-Level System.

Bridges:
  - ``tools/mesh_geometry_algorithms.py`` (Taubin fairing, Forsyth GPU cache optimization,
    QEM simplification, geometry-driven Vertex-Wear RGBA, topology/ACMR audit)
  - ``tools/pbr_texture_algorithms.py`` (Voronoi UV island dilation, RNM normal blending)
  - Level 1 primary chassis builders (``VisualModelAuthoringSupport.py``)
  - Level 2 canonical hardpoint modules (``Build-CanonicalVisualModules.py``)
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any, Mapping, Sequence

import numpy as np

try:
    from tools import mesh_geometry_algorithms as mga
except ImportError:
    import mesh_geometry_algorithms as mga


@dataclass(frozen=True)
class FunctionalRoleEngineeringSpec:
    """Visual engineering profile for a canonical Level 2 role or Level 1 assembly."""

    role: str
    form_tier: str  # "primary_faceted", "primary_cast", "secondary_mechanism"
    soot_intensity: float
    soot_anchor_mode: str  # "none", "muzzle_plus_y", "exhaust_minus_y", "dorsal_plus_z"
    allow_taubin_fairing: bool


ROLE_ENGINEERING_SPECS: dict[str, FunctionalRoleEngineeringSpec] = {
    "armor_cassette": FunctionalRoleEngineeringSpec("armor_cassette", "primary_faceted", 0.0, "none", False),
    "aps_sensor_suite": FunctionalRoleEngineeringSpec("aps_sensor_suite", "secondary_mechanism", 0.0, "none", False),
    "aps_magnetic_shield": FunctionalRoleEngineeringSpec("aps_magnetic_shield", "secondary_mechanism", 0.0, "none", False),
    "aps_phase_shield": FunctionalRoleEngineeringSpec("aps_phase_shield", "primary_cast", 0.0, "none", True),
    "aps_countermeasure_suite": FunctionalRoleEngineeringSpec("aps_countermeasure_suite", "secondary_mechanism", 0.32, "muzzle_plus_y", False),
    "fuel_drum_pair": FunctionalRoleEngineeringSpec("fuel_drum_pair", "primary_cast", 0.0, "none", True),
    "ordnance_carrier": FunctionalRoleEngineeringSpec("ordnance_carrier", "secondary_mechanism", 0.45, "exhaust_minus_y", False),
    "fuel_jerrycan_carrier": FunctionalRoleEngineeringSpec("fuel_jerrycan_carrier", "primary_faceted", 0.0, "none", False),
    "excavation_drill": FunctionalRoleEngineeringSpec("excavation_drill", "secondary_mechanism", 0.35, "muzzle_plus_y", False),
    "thermal_radiator": FunctionalRoleEngineeringSpec("thermal_radiator", "secondary_mechanism", 0.38, "dorsal_plus_z", False),
    "heavy_flak_battery": FunctionalRoleEngineeringSpec("heavy_flak_battery", "secondary_mechanism", 0.62, "muzzle_plus_y", False),
    "repair_dispenser": FunctionalRoleEngineeringSpec("repair_dispenser", "primary_cast", 0.0, "none", True),
    "power_containment": FunctionalRoleEngineeringSpec("power_containment", "primary_faceted", 0.14, "dorsal_plus_z", False),
    "plasma_containment": FunctionalRoleEngineeringSpec("plasma_containment", "primary_faceted", 0.18, "dorsal_plus_z", False),
    "auxiliary_power_generator": FunctionalRoleEngineeringSpec("auxiliary_power_generator", "secondary_mechanism", 0.50, "exhaust_minus_y", False),
    "field_engineering_crane": FunctionalRoleEngineeringSpec("field_engineering_crane", "secondary_mechanism", 0.0, "none", False),
    "recoil_anchor": FunctionalRoleEngineeringSpec("recoil_anchor", "secondary_mechanism", 0.0, "none", False),
    "docking_clamp": FunctionalRoleEngineeringSpec("docking_clamp", "secondary_mechanism", 0.0, "none", False),
    "utility_sponson": FunctionalRoleEngineeringSpec("utility_sponson", "primary_faceted", 0.0, "none", False),
}

CAST_FORM_NAME_TOKENS = (
    "Cast", "Cowl", "Radome", "Dome", "Canopy", "Drum", "Tank", "Flask", "Pod", "Fairing",
)
FACETED_PROTECTED_TOKENS = (
    "Armor", "Plate", "Bracket", "Turret", "Trunnion", "Hatch", "ERA", "Bolt", "Rivet",
    "Weld", "Flange", "Track", "Wheel", "Barrel", "Muzzle", "Louver", "Grille", "COLLIDER",
    "Fastener", "Seat", "Stay", "Brace", "Gusset", "Strap", "Latch", "Hinge", "Pin",
    "Keepers", "Collar",
)


def _object_supports_taubin_fairing(obj: Any, role: str, *, min_vertices: int = 24) -> bool:
    if obj is None or getattr(obj, "type", None) != "MESH" or "_COLLIDER" in obj.name:
        return False
    mesh = getattr(obj, "data", None)
    if mesh is None or len(mesh.polygons) == 0 or len(mesh.vertices) < min_vertices:
        return False
    if getattr(mesh, "shape_keys", None) is not None:
        return False
    if any(getattr(m, "type", None) == "ARMATURE" for m in getattr(obj, "modifiers", ())):
        return False
    try:
        if obj.get("_rcl_fe_taubin_applied"):
            return False
    except Exception:
        pass
    return classify_form_hierarchy(obj.name, role=role) == "primary_cast"


def get_role_engineering_spec(role: str) -> FunctionalRoleEngineeringSpec:
    """Resolve the engineering specification for a canonical visual engineering role."""
    return ROLE_ENGINEERING_SPECS.get(
        role,
        FunctionalRoleEngineeringSpec(role or "generic", "primary_faceted", 0.0, "none", False),
    )


def classify_form_hierarchy(obj_name: str, role: str = "") -> str:
    """Classify a Blender mesh into the Form Hierarchy (cast vs. faceted vs. tertiary)."""
    if any(tok in obj_name for tok in FACETED_PROTECTED_TOKENS):
        return "primary_faceted"
    if any(tok in obj_name for tok in CAST_FORM_NAME_TOKENS):
        return "primary_cast"
    return get_role_engineering_spec(role).form_tier


def _compute_objects_world_bounds(objects: Sequence[Any]) -> tuple[np.ndarray, np.ndarray] | None:
    mins: list[np.ndarray] = []
    maxs: list[np.ndarray] = []
    for obj in objects:
        if obj is None or getattr(obj, "type", None) != "MESH" or not obj.data.vertices:
            continue
        mat = np.array(obj.matrix_world, dtype=np.float64)
        verts = np.array([v.co[:] for v in obj.data.vertices], dtype=np.float64)
        hom = np.concatenate([verts, np.ones((len(verts), 1), dtype=np.float64)], axis=1)
        world = (hom @ mat.T)[:, :3]
        mins.append(world.min(axis=0))
        maxs.append(world.max(axis=0))
    if not mins:
        return None
    return np.min(np.vstack(mins), axis=0), np.max(np.vstack(maxs), axis=0)


def resolve_functional_soot_profile(
    entry_or_role: Mapping[str, Any] | str,
    objects: Sequence[Any],
) -> dict[str, Any]:
    """Derive physically grounded thermal/muzzle soot anchors from engineering role & bounds.

    Keeps clean armor, sensor, and medical roles at ``soot_intensity = 0.0`` while
    anchoring thermal or ordnance soot at the active muzzle/exhaust/louver extremum.
    """
    role = entry_or_role.get("visualEngineeringRole", "") if isinstance(entry_or_role, Mapping) else str(entry_or_role)
    spec = get_role_engineering_spec(role)
    if spec.soot_intensity <= 0.0 or spec.soot_anchor_mode == "none":
        return {"soot_intensity": 0.0, "exhaust_locs": [], "muzzle_loc": None, "role": role}

    bounds = _compute_objects_world_bounds(objects)
    if bounds is None:
        return {"soot_intensity": 0.0, "exhaust_locs": [], "muzzle_loc": None, "role": role}

    bmin, bmax = bounds
    center = 0.5 * (bmin + bmax)
    if spec.soot_anchor_mode == "muzzle_plus_y":
        return {
            "soot_intensity": spec.soot_intensity,
            "exhaust_locs": [],
            "muzzle_loc": (float(center[0]), float(bmax[1]), float(center[2])),
            "role": role,
        }
    if spec.soot_anchor_mode == "exhaust_minus_y":
        return {
            "soot_intensity": spec.soot_intensity,
            "exhaust_locs": [(float(center[0]), float(bmin[1]), float(center[2]))],
            "muzzle_loc": None,
            "role": role,
        }
    if spec.soot_anchor_mode == "dorsal_plus_z":
        return {
            "soot_intensity": spec.soot_intensity,
            "exhaust_locs": [(float(center[0]), float(center[1]), float(bmax[2]))],
            "muzzle_loc": None,
            "role": role,
        }
    return {"soot_intensity": 0.0, "exhaust_locs": [], "muzzle_loc": None, "role": role}


def apply_form_hierarchy_prepass_bpy(
    objects: Sequence[Any],
    *,
    role: str = "",
    entry_or_role: Mapping[str, Any] | str | None = None,
    enable_taubin_fairing: bool | None = None,
    min_vertices: int = 24,
) -> dict[str, Any]:
    """Run Form Hierarchy classification, volume-preserving cast Taubin fairing, and soot
    resolution BEFORE vertex-wear baking and material batching merge part names away.
    """
    resolved_role = (
        entry_or_role.get("visualEngineeringRole", role)
        if isinstance(entry_or_role, Mapping)
        else (str(entry_or_role) if entry_or_role is not None else role)
    )
    spec = get_role_engineering_spec(resolved_role)
    taubin_applied: list[str] = []
    counts = {"primary_cast": 0, "primary_faceted": 0, "secondary_mechanism": 0}

    for obj in objects:
        if obj is None or getattr(obj, "type", None) != "MESH" or "_COLLIDER" in obj.name:
            continue
        mesh = getattr(obj, "data", None)
        if mesh is None or len(mesh.polygons) == 0:
            continue
        form_class = classify_form_hierarchy(obj.name, role=resolved_role)
        counts[form_class] = counts.get(form_class, 0) + 1

        if form_class != "primary_cast":
            continue
        try:
            already_applied = bool(obj.get("_rcl_fe_taubin_applied"))
        except Exception:
            already_applied = False
        if already_applied:
            continue

        should_smooth = (
            _object_supports_taubin_fairing(obj, resolved_role, min_vertices=min_vertices)
            if enable_taubin_fairing is None
            else (bool(enable_taubin_fairing) and _object_supports_taubin_fairing(obj, resolved_role, min_vertices=4))
        )
        if should_smooth:
            mga.apply_taubin_smooth_bpy(
                obj, iterations=2, lam=0.35, mu=-0.37, preserve_boundary=True
            )
            taubin_applied.append(obj.name)
            try:
                obj["_rcl_fe_taubin_applied"] = True
            except Exception:
                pass

    soot_profile = resolve_functional_soot_profile(entry_or_role or resolved_role, objects)
    return {
        "role": resolved_role,
        "formTier": spec.form_tier,
        "taubinFairingObjects": taubin_applied,
        "formHierarchyCounts": counts,
        "sootProfile": soot_profile,
    }


def _extract_world_point(item: Any) -> tuple[float, float, float] | None:
    if item is None:
        return None
    if hasattr(item, "matrix_world"):
        pos = item.matrix_world.translation
        return (float(pos.x), float(pos.y), float(pos.z))
    if isinstance(item, (tuple, list)) and len(item) >= 3:
        return (float(item[0]), float(item[1]), float(item[2]))
    return None


def resolve_level1_soot_anchors(
    objects: Sequence[Any],
    *,
    role: str = "",
    sockets: Sequence[Any] | Mapping[str, Any] | None = None,
    exhaust_locs: Sequence[Any] | None = None,
    muzzle_loc: Any = None,
    soot_intensity: float | None = None,
) -> dict[str, Any]:
    """Discover Level 1 functional thermal/muzzle soot anchors from canonical sockets and part names."""
    resolved_exhaust: list[tuple[float, float, float]] = []
    for loc in exhaust_locs or ():
        pt = _extract_world_point(loc)
        if pt is not None:
            resolved_exhaust.append(pt)
    resolved_muzzle = _extract_world_point(muzzle_loc)
    discovered_from: list[str] = []

    socket_items: list[tuple[str, Any]] = []
    if isinstance(sockets, Mapping):
        socket_items = [(str(k), v) for k, v in sockets.items()]
    elif sockets is not None:
        socket_items = [(str(getattr(s, "name", "")), s) for s in sockets]

    muzzle_tokens = ("Socket_Muzzle", "FirePoint", "Socket_WeaponAudio", "Muzzle", "Barrel")
    exhaust_tokens = ("Socket_EngineAudio", "Socket_DamageSmoke", "Exhaust", "Chimney", "Vent", "Smokestack", "Thruster")

    def _add_unique_exhaust(pt: tuple[float, float, float]) -> bool:
        for ex in resolved_exhaust:
            if (ex[0] - pt[0]) ** 2 + (ex[1] - pt[1]) ** 2 + (ex[2] - pt[2]) ** 2 < 1e-6:
                return False
        resolved_exhaust.append(pt)
        return True

    for sname, sval in socket_items:
        pt = _extract_world_point(sval)
        if pt is None:
            continue
        if any(tok in sname for tok in muzzle_tokens) and resolved_muzzle is None:
            resolved_muzzle = pt
            discovered_from.append(f"socket:{sname}")
        elif any(tok in sname for tok in exhaust_tokens):
            if _add_unique_exhaust(pt):
                discovered_from.append(f"socket:{sname}")

    if resolved_muzzle is None or not resolved_exhaust:
        for obj in objects:
            if obj is None or getattr(obj, "type", None) != "MESH" or "_COLLIDER" in obj.name:
                continue
            b = _compute_objects_world_bounds([obj])
            if b is None:
                continue
            bmin, bmax = b
            center = 0.5 * (bmin + bmax)
            if resolved_muzzle is None and any(tok in obj.name for tok in ("Muzzle", "Barrel")):
                resolved_muzzle = (float(center[0]), float(bmax[1]), float(center[2]))
                discovered_from.append(f"mesh:{obj.name}")
            elif not resolved_exhaust and any(tok in obj.name for tok in exhaust_tokens):
                pt = (float(center[0]), float(center[1]), float(center[2]))
                if _add_unique_exhaust(pt):
                    discovered_from.append(f"mesh:{obj.name}")

    spec = get_role_engineering_spec(role)
    resolved_intensity = float(soot_intensity) if soot_intensity is not None else float(spec.soot_intensity)
    if soot_intensity is None and resolved_intensity <= 0.0 and (resolved_muzzle is not None or resolved_exhaust):
        resolved_intensity = 0.30

    return {
        "role": role,
        "soot_intensity": resolved_intensity,
        "exhaust_locs": resolved_exhaust,
        "muzzle_loc": resolved_muzzle,
        "discoveredFrom": discovered_from,
    }


def apply_canonical_two_level_mesh_pass_bpy(
    objects: Sequence[Any],
    *,
    role: str = "",
    enable_taubin_fairing: bool | None = None,
    optimize_vertex_cache: bool = True,
    cache_size: int = 32,
    assume_cache_optimized: bool = False,
) -> dict[str, Any]:
    """Run Form Hierarchy fairing (auto on primary_cast when enable_taubin_fairing=None),
    Forsyth vertex-cache reordering, and topology + Vertex-Wear RGBA auditing.
    """
    per_object: list[dict[str, Any]] = []
    taubin_applied: list[str] = []
    counts = {"primary_cast": 0, "primary_faceted": 0, "secondary_mechanism": 0}
    for obj in objects:
        if obj is None or getattr(obj, "type", None) != "MESH" or "_COLLIDER" in obj.name:
            continue
        if len(obj.data.polygons) == 0:
            continue

        form_class = classify_form_hierarchy(obj.name, role=role)
        counts[form_class] = counts.get(form_class, 0) + 1
        should_taubin = (
            _object_supports_taubin_fairing(obj, role, min_vertices=24)
            if enable_taubin_fairing is None
            else (bool(enable_taubin_fairing) and _object_supports_taubin_fairing(obj, role, min_vertices=4))
        )
        if should_taubin:
            mga.apply_taubin_smooth_bpy(
                obj, iterations=2, lam=0.35, mu=-0.37, preserve_boundary=True
            )
            taubin_applied.append(obj.name)
            try:
                obj["_rcl_fe_taubin_applied"] = True
            except Exception:
                pass

        metrics_before = mga.audit_blender_object_metrics_bpy(
            obj,
            cache_size=cache_size,
            compute_forsyth=not optimize_vertex_cache and not assume_cache_optimized,
        )
        acmr_optimized = metrics_before["acmrBefore"]
        if optimize_vertex_cache:
            acmr_optimized = mga.apply_forsyth_cache_optimize_bpy(obj, cache_size=cache_size)

        per_object.append({
            "objectName": obj.name,
            "formClass": form_class,
            "triangleCount": metrics_before["triangleCount"],
            "degenerateTriangles": metrics_before["degenerateTriangles"],
            "nonManifoldEdges": metrics_before["nonManifoldEdges"],
            "acmrBefore": metrics_before["acmrBefore"],
            "acmrAfter": round(float(acmr_optimized), 4),
            "hasUvMap": metrics_before["hasUvMap"],
            "vertexWearRgba": metrics_before["vertexWearRgba"],
        })

    return {
        "role": role,
        "taubinFairingObjects": taubin_applied,
        "formHierarchyCounts": counts,
        "objectCount": len(per_object),
        "totalTriangles": sum(r["triangleCount"] for r in per_object),
        "maxNonManifoldEdges": max((r["nonManifoldEdges"] for r in per_object), default=0),
        "meanAcmrBefore": round(float(np.mean([r["acmrBefore"] for r in per_object])), 4) if per_object else 0.0,
        "meanAcmrAfter": round(float(np.mean([r["acmrAfter"] for r in per_object])), 4) if per_object else 0.0,
        "objects": per_object,
    }


def _bilinear_resize_rgb_numpy(arr: np.ndarray, out_h: int, out_w: int) -> np.ndarray:
    sh, sw = arr.shape[:2]
    y = np.linspace(0.0, max(sh - 1, 0), out_h, dtype=np.float32)
    x = np.linspace(0.0, max(sw - 1, 0), out_w, dtype=np.float32)
    y0 = np.floor(y).astype(np.int32)
    x0 = np.floor(x).astype(np.int32)
    y1 = np.minimum(y0 + 1, sh - 1)
    x1 = np.minimum(x0 + 1, sw - 1)
    wy = (y - y0)[:, None, None]
    wx = (x - x0)[None, :, None]
    src = arr.astype(np.float32)
    top = src[y0[:, None], x0[None, :]] * (1.0 - wx) + src[y0[:, None], x1[None, :]] * wx
    bot = src[y1[:, None], x0[None, :]] * (1.0 - wx) + src[y1[:, None], x1[None, :]] * wx
    return np.clip(np.round(top * (1.0 - wy) + bot * wy), 0, 255).astype(np.uint8)


def compose_normals_rnm_rgb(
    base_rgb: np.ndarray,
    detail_rgb: np.ndarray,
    *,
    detail_strength: float = 0.35,
) -> np.ndarray:
    """Tile/fit ``detail_rgb`` in memory and blend onto ``base_rgb`` via RNM."""
    try:
        from PIL import Image
    except ImportError:
        Image = None
    try:
        from tools import pbr_texture_algorithms as pbr_algo
    except ImportError:
        import pbr_texture_algorithms as pbr_algo

    base_arr = np.asarray(base_rgb)
    det_arr = np.asarray(detail_rgb)
    if base_arr.dtype != np.uint8 or base_arr.ndim != 3 or base_arr.shape[-1] != 3:
        raise TypeError(
            f"base_rgb must be a uint8 HxWx3 image; got dtype={base_arr.dtype}, shape={base_arr.shape}."
        )
    if det_arr.dtype != np.uint8 or det_arr.ndim != 3 or det_arr.shape[-1] != 3:
        raise TypeError(
            f"detail_rgb must be a uint8 HxWx3 image; got dtype={det_arr.dtype}, shape={det_arr.shape}."
        )
    if base_arr.shape[0] == 0 or base_arr.shape[1] == 0 or det_arr.shape[0] == 0 or det_arr.shape[1] == 0:
        raise ValueError("Base and detail normal images must have non-empty width and height.")
    strength = pbr_algo.validate_detail_strength(detail_strength)
    if det_arr.shape[:2] != base_arr.shape[:2]:
        bh, bw = base_arr.shape[:2]
        dh, dw = det_arr.shape[:2]
        if dh <= bh and dw <= bw and (bh % dh == 0) and (bw % dw == 0):
            det_arr = np.tile(det_arr, (bh // dh, bw // dw, 1))
        elif Image is not None:
            det_arr = np.asarray(
                Image.fromarray(det_arr, "RGB").resize((bw, bh), Image.Resampling.BILINEAR),
                dtype=np.uint8,
            )
        else:
            det_arr = _bilinear_resize_rgb_numpy(det_arr, bh, bw)

    return pbr_algo.blend_normals_rnm(base_arr, det_arr, detail_strength=strength)


def compose_canonical_pbr_detail(
    normal_path: Path,
    detail_normal_path: Path,
    output_normal_path: Path,
    *,
    detail_strength: float = 0.35,
    compress_level: int = 1,
) -> dict[str, Any]:
    """Compose a tileable micro-detail normal onto a canonical Normal map using RNM."""
    from PIL import Image

    with Image.open(normal_path) as base_im, Image.open(detail_normal_path) as det_im:
        base_rgb = np.asarray(base_im.convert("RGB"), dtype=np.uint8)
        det_rgb = np.asarray(det_im.convert("RGB"), dtype=np.uint8)

    blended = compose_normals_rnm_rgb(base_rgb, det_rgb, detail_strength=detail_strength)
    output_normal_path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(blended, "RGB").save(output_normal_path, format="PNG", compress_level=compress_level)
    return {
        "baseNormal": str(normal_path),
        "detailNormal": str(detail_normal_path),
        "outputNormal": str(output_normal_path),
        "detailStrength": float(detail_strength),
        "resolution": [int(blended.shape[1]), int(blended.shape[0])],
    }


def _load_detail_normal_rgb(
    candidate_paths: Sequence[Path],
    base_h: int,
    base_w: int,
    pbr_algo: Any,
    *,
    use_bpy: bool,
) -> tuple[np.ndarray | None, str]:
    try:
        from PIL import Image
    except ImportError:
        Image = None

    for cand in candidate_paths:
        if not cand.exists():
            continue
        if Image is not None:
            with Image.open(cand) as det_im:
                return np.asarray(det_im.convert("RGB"), dtype=np.uint8), str(cand)
        if use_bpy:
            import bpy

            existing = {id(im) for im in bpy.data.images}
            img = bpy.data.images.load(str(cand), check_existing=True)
            is_new = id(img) not in existing
            try:
                try:
                    img.colorspace_settings.name = "Non-Color"
                except Exception:
                    pass
                w, h = int(img.size[0]), int(img.size[1])
                buf = np.empty(w * h * 4, dtype=np.float32)
                img.pixels.foreach_get(buf)
                arr_top_down = np.flipud(buf.reshape(h, w, 4))[..., :3]
                return np.clip(np.round(arr_top_down * 255.0), 0, 255).astype(np.uint8), str(cand)
            finally:
                if is_new and img.users == 0:
                    bpy.data.images.remove(img)

    detail_size = min(256, int(base_h), int(base_w))
    if detail_size >= 16:
        h_field = pbr_algo.synthesize_periodic_fbm(detail_size, seed=42, octaves=4)
        return (
            pbr_algo.height_to_normal_scharr(h_field, strength=1.5, tileable=True, as_uint8=True),
            "synthetic_periodic_fbm_scharr",
        )
    return None, "none"


def postprocess_level1_atlas_maps(
    maps: Mapping[str, Path | str],
    *,
    albedo_alpha_mask: np.ndarray | None = None,
    flip_mask_y: bool = False,
    dilate_px: int = 8,
    detail_normal_path: Path | str | None = None,
    detail_strength: float = 0.25,
    compress_level: int = 1,
    bpy_images: Mapping[str, Any] | None = None,
) -> dict[str, Any]:
    """Post-process Level 1 Cycles atlas maps using Euclidean Voronoi UV-island dilation
    and Reoriented Normal Mapping (RNM) micro-detail composition. Works with PIL on PNGs or
    directly in memory on Blender ``bpy.data.images``.
    """
    try:
        from PIL import Image
    except ImportError:
        Image = None
    try:
        from tools import pbr_texture_algorithms as pbr_algo
    except ImportError:
        import pbr_texture_algorithms as pbr_algo

    dilated_maps: list[str] = []
    dilation_applied = False
    modified_bpy_keys: set[str] = set()

    mask: np.ndarray | None = None
    dilation_map = None
    if int(dilate_px) > 0 and albedo_alpha_mask is not None:
        mask = np.asarray(albedo_alpha_mask) > 0.05
        if flip_mask_y:
            mask = np.flipud(mask)
        if mask.ndim == 2 and 0 < int(mask.sum()) < mask.size:
            dilation_map = pbr_algo.compute_uv_dilation_map(mask, max_distance=int(dilate_px))

    if dilation_map is not None:
        fill_mask, nearest_y, nearest_x = dilation_map
        dilation_applied = True
        if bpy_images:
            for key, img in bpy_images.items():
                if img is None:
                    continue
                w, h = int(img.size[0]), int(img.size[1])
                if (h, w) != mask.shape:
                    continue
                buf = np.empty(w * h * 4, dtype=np.float32)
                img.pixels.foreach_get(buf)
                arr_td = np.flipud(buf.reshape(h, w, 4))
                dilated_td = pbr_algo.apply_uv_dilation_map(arr_td, fill_mask, nearest_y, nearest_x)
                img.pixels.foreach_set(np.flipud(dilated_td).ravel())
                modified_bpy_keys.add(str(key))
                dilated_maps.append(str(key))
        elif Image is not None:
            for key, raw_path in maps.items():
                p = Path(raw_path)
                if not p.exists():
                    continue
                with Image.open(p) as im:
                    mode = im.mode if im.mode in ("RGB", "RGBA") else "RGB"
                    arr = np.asarray(im.convert(mode), dtype=np.uint8)
                if arr.shape[:2] != mask.shape:
                    continue
                dilated = pbr_algo.apply_uv_dilation_map(arr, fill_mask, nearest_y, nearest_x)
                Image.fromarray(dilated, mode).save(p, format="PNG", compress_level=compress_level)
                dilated_maps.append(str(key))

    rnm_composed = False
    detail_source = "none"
    strength = pbr_algo.validate_detail_strength(float(detail_strength)) if detail_strength > 0.0 else 0.0
    candidate_paths: list[Path] = []
    if detail_normal_path is not None:
        candidate_paths.append(Path(detail_normal_path))
    candidate_paths.append(
        Path(__file__).resolve().parents[1]
        / "unity"
        / "RogueCommandAssetAuthoring"
        / "Assets"
        / "RogueCommandExpansionSample"
        / "Textures"
        / "Detail"
        / "RCL_SteelMicroDetail_Normal.png"
    )

    normal_bpy = bpy_images.get("normal") if bpy_images else None
    if normal_bpy is not None and strength > 0.0:
        nw, nh = int(normal_bpy.size[0]), int(normal_bpy.size[1])
        buf = np.empty(nw * nh * 4, dtype=np.float32)
        normal_bpy.pixels.foreach_get(buf)
        arr4_td = np.flipud(buf.reshape(nh, nw, 4)).copy()
        base_rgb = np.clip(np.round(arr4_td[..., :3] * 255.0), 0, 255).astype(np.uint8)
        resolved_detail, detail_source = _load_detail_normal_rgb(
            candidate_paths, nh, nw, pbr_algo, use_bpy=True
        )
        if resolved_detail is not None:
            blended = compose_normals_rnm_rgb(base_rgb, resolved_detail, detail_strength=strength)
            arr4_td[..., :3] = blended.astype(np.float32) / 255.0
            normal_bpy.pixels.foreach_set(np.flipud(arr4_td).ravel())
            modified_bpy_keys.add("normal")
            rnm_composed = True
    elif Image is not None and strength > 0.0 and maps.get("normal") is not None:
        normal_path = Path(maps["normal"])
        if normal_path.exists():
            with Image.open(normal_path) as base_im:
                base_rgb = np.asarray(base_im.convert("RGB"), dtype=np.uint8)
            resolved_detail, detail_source = _load_detail_normal_rgb(
                candidate_paths, int(base_rgb.shape[0]), int(base_rgb.shape[1]), pbr_algo, use_bpy=False
            )
            if resolved_detail is not None:
                blended = compose_normals_rnm_rgb(base_rgb, resolved_detail, detail_strength=strength)
                Image.fromarray(blended, "RGB").save(normal_path, format="PNG", compress_level=compress_level)
                rnm_composed = True

    if bpy_images:
        for key in sorted(modified_bpy_keys):
            img = bpy_images.get(key)
            if img is not None:
                img.save()

    return {
        "dilationApplied": dilation_applied,
        "dilatedMaps": dilated_maps,
        "dilatePx": int(dilate_px),
        "rnmComposed": rnm_composed,
        "detailSource": detail_source,
        "detailStrength": float(strength),
    }
