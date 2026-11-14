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
)


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


def apply_canonical_two_level_mesh_pass_bpy(
    objects: Sequence[Any],
    *,
    role: str = "",
    enable_taubin_fairing: bool = False,
    optimize_vertex_cache: bool = True,
    cache_size: int = 32,
    assume_cache_optimized: bool = False,
) -> dict[str, Any]:
    """Run Form Hierarchy fairing (optional cast shells), Forsyth vertex-cache reordering,
    and topology + Vertex-Wear RGBA auditing across a list of Blender mesh objects.
    """
    per_object: list[dict[str, Any]] = []
    taubin_applied: list[str] = []
    for obj in objects:
        if obj is None or getattr(obj, "type", None) != "MESH" or "_COLLIDER" in obj.name:
            continue
        if len(obj.data.polygons) == 0:
            continue

        form_class = classify_form_hierarchy(obj.name, role=role)
        if enable_taubin_fairing and form_class == "primary_cast":
            mga.apply_taubin_smooth_bpy(
                obj, iterations=2, lam=0.35, mu=-0.37, preserve_boundary=True
            )
            taubin_applied.append(obj.name)

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
        "objectCount": len(per_object),
        "totalTriangles": sum(r["triangleCount"] for r in per_object),
        "maxNonManifoldEdges": max((r["nonManifoldEdges"] for r in per_object), default=0),
        "meanAcmrBefore": round(float(np.mean([r["acmrBefore"] for r in per_object])), 4) if per_object else 0.0,
        "meanAcmrAfter": round(float(np.mean([r["acmrAfter"] for r in per_object])), 4) if per_object else 0.0,
        "objects": per_object,
    }


def compose_normals_rnm_rgb(
    base_rgb: np.ndarray,
    detail_rgb: np.ndarray,
    *,
    detail_strength: float = 0.35,
) -> np.ndarray:
    """Tile/fit ``detail_rgb`` in memory and blend onto ``base_rgb`` via RNM."""
    from PIL import Image
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
        else:
            det_arr = np.asarray(
                Image.fromarray(det_arr, "RGB").resize((bw, bh), Image.Resampling.BILINEAR),
                dtype=np.uint8,
            )

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
