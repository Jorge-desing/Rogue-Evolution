"""Hardflow-grade v2 geometry, Exact Boolean, raycast/BVH seating, and audit recipes
for the 5 Level-1 armored vehicles in sample-native-expansion:
  - SandlanceScout
  - ColossusSiegePlatform
  - SkirmishStrikeBike
  - SurgeArcTank
  - OverlordCommandHQ

Never modifies v1 .blend files directly; invoked by
tools/Build-SampleNativeVehiclesV2SourceBlends.py to generate *-v2.blend.
"""

from __future__ import annotations

from collections import deque
import math
from typing import Any, Callable

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector
from mathutils.bvhtree import BVHTree

from ActiveUnitVisualEnhancements import apply_wave2_details, apply_wave34_details
from tactical_operations_v2_recipes import (
    RECIPE_PROP,
    _mesh_bvh,
    _shift_objects as _raw_shift_objects,
    add_raycast_stay_between,
    apply_bevel_and_weighted_normal,
    apply_exact_boolean,
    build_sculpted_wheel_fender_set,
    cleanup_mesh_topology,
    create_mesh_object,
    cut_recessed_service_pocket,
    cut_visor_pocket_around_housing,
    ensure_single_material,
    ensure_uv_and_color_layers,
    make_chamfered_box_mesh,
    make_chamfered_y_loft,
    make_faceted_cylinder,
    remove_objects_matching,
    replace_object_geometry,
    seat_cluster_downward as _raw_seat_cluster,
    seat_object_downward_to_target as _raw_seat_object,
    snap_object_to_nearest as _raw_snap_object,
    world_bounds,
    world_center,
)


def _shift_objects(objs: list[bpy.types.Object], delta: Vector) -> None:
    _raw_shift_objects(objs, dx=float(delta.x), dy=float(delta.y), dz=float(delta.z))


def _group_world_bounds(objs: list[bpy.types.Object]) -> tuple[float, float, float, float, float, float]:
    xs: list[float] = []
    ys: list[float] = []
    zs: list[float] = []
    for o in objs:
        x0, x1, y0, y1, z0, z1 = world_bounds(o)
        xs.extend((x0, x1))
        ys.extend((y0, y1))
        zs.extend((z0, z1))
    return min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)


def _shift_group_center_to(objs: list[bpy.types.Object], target: Vector) -> None:
    """Translate a multi-part assembly rigidly so its combined AABB center moves to target."""
    if not objs:
        return
    x0, x1, y0, y1, z0, z1 = _group_world_bounds(objs)
    cur = Vector(((x0 + x1) * 0.5, (y0 + y1) * 0.5, (z0 + z1) * 0.5))
    _shift_objects(objs, target - cur)


def _seat_shackle_groups(
    keyword: str,
    host: bpy.types.Object,
    *,
    y_target: float,
    z_target: float,
    x_clamp: float | None = None,
    embed: float = 0.004,
    snap: bool = True,
) -> None:
    """Translate each shackle assembly rigidly (preserving Bracket/Loop/Pin spacing) and snap to host."""
    parts = [o for o in bpy.data.objects if o.type == "MESH" and keyword in o.name]
    if not parts:
        return
    buckets: dict[str, list[bpy.types.Object]] = {}
    for o in parts:
        if o.name.endswith("_L") or "_L_" in o.name:
            tag = "_L"
        elif o.name.endswith("_R") or "_R_" in o.name:
            tag = "_R"
        elif o.name.endswith("_1") or "_1_" in o.name:
            tag = "_1"
        elif o.name.endswith("_2") or "_2_" in o.name:
            tag = "_2"
        else:
            tag = "C"
        buckets.setdefault(tag, []).append(o)
    for grp in buckets.values():
        x0, x1, y0, y1, z0, z1 = _group_world_bounds(grp)
        cx = (x0 + x1) * 0.5
        if x_clamp is not None:
            cx = (-x_clamp if cx < 0.0 else x_clamp)
        _shift_group_center_to(grp, Vector((cx, y_target, z_target)))
        if snap:
            anchor = next((o for o in grp if "Bracket" in o.name), grp[0])
            before = world_center(anchor)
            snap_object_to_nearest(anchor, host, embed=embed)
            delta = world_center(anchor) - before
            rest = [o for o in grp if o != anchor]
            if rest and delta.length > 1e-6:
                _shift_objects(rest, delta)


def snap_object_to_nearest(obj: bpy.types.Object, host: bpy.types.Object, *, embed: float = 0.004) -> bool:
    return _raw_snap_object(obj, [host], embed=embed)


def seat_object_downward_to_target(obj: bpy.types.Object, host: bpy.types.Object, *, embed: float = 0.004) -> bool:
    return _raw_seat_object(obj, [host], clearance=-embed)


def seat_cluster_downward(objs: list[bpy.types.Object], host: bpy.types.Object, *, embed: float = 0.004) -> bool:
    return _raw_seat_cluster(objs, [host], clearance=-embed)


def tag_v2_recipe(obj: bpy.types.Object, tag: str) -> None:
    """Attach the v2 recipe identifier custom property to an authored object."""
    obj[RECIPE_PROP] = tag


# ---------------------------------------------------------------------------
# Vehicle-Specific Material & Topology Helpers
# ---------------------------------------------------------------------------

_ROLE_TOKENS: dict[str, tuple[str, ...]] = {
    "primary": ("primaryarmor", "armor", "primary"),
    "light": ("lightarmor", "light armor", "light"),
    "gunmetal": ("gunmetal", "metal"),
    "warning": ("warning", "amber", "orange"),
    "emissive": ("emissive", "cyan", "glass", "sensor", "arc"),
    "tread": ("tread", "tire"),
    "wheelrim": ("wheelrim", "wheel metal", "rim"),
    "wheelhub": ("wheelhub", "wheel metal", "hub"),
}


def resolve_vehicle_material(unit_prefix: str, role: str) -> bpy.types.Material:
    """Resolve existing scene material across spacing/naming conventions of sample-native units."""
    role_key = role.lower().replace(" ", "").replace("_", "")
    tokens = _ROLE_TOKENS.get(role_key, (role.lower(),))

    candidates = [
        m for m in bpy.data.materials
        if not m.name.lower().startswith("preview") and "detail" not in m.name.lower()
    ]
    # Prefer exact role matches (avoid matching "light armor" when requesting "armor").
    for tok in tokens:
        for mat in candidates:
            norm = mat.name.lower().replace(" ", "").replace("_", "")
            if role_key == "primary" and "light" in norm:
                continue
            if norm.endswith(tok) or tok in norm:
                return mat
    mat = bpy.data.materials.new(name=f"{unit_prefix}_{role}")
    mat.use_nodes = True
    return mat


def _set_obj_world_center(obj: bpy.types.Object, target_center: Vector) -> None:
    cur = world_center(obj)
    _shift_objects([obj], target_center - cur)


def _snap_name_prefix_to_host(
    prefixes: tuple[str, ...],
    host: bpy.types.Object,
    *,
    embed: float = 0.004,
) -> int:
    """Snap every mesh whose name starts with any prefix onto host surface via BVH."""
    snapped = 0
    for obj in list(bpy.data.objects):
        if obj.type != "MESH" or obj == host:
            continue
        if any(obj.name.startswith(p) for p in prefixes):
            snap_object_to_nearest(obj, host, embed=embed)
            snapped += 1
    return snapped


def _seat_prefix_downward(
    prefixes: tuple[str, ...],
    host: bpy.types.Object,
    *,
    embed: float = 0.004,
) -> int:
    """Seat matching objects downward onto host top surface using ray_cast."""
    seated = 0
    for obj in list(bpy.data.objects):
        if obj.type != "MESH" or obj == host:
            continue
        if any(obj.name.startswith(p) for p in prefixes):
            if seat_object_downward_to_target(obj, host, embed=embed):
                seated += 1
    return seated


def _apply_wave_details_cleanly(unit_name: str, legacy_dup_prefixes: tuple[str, ...] = ()) -> None:
    """Bake Wave 2 + Wave 3/4 details into the v2 blend and strip known legacy prefix duplicates."""
    apply_wave2_details(unit_name)
    apply_wave34_details(unit_name)
    if legacy_dup_prefixes:
        remove_objects_matching(lambda name: any(name.startswith(p) for p in legacy_dup_prefixes))


def _finalize_all_mesh_attributes(unit_name: str, recipe_prefix: str) -> None:
    """Ensure every renderable mesh has a single material, valid UVMap, and Color RGBA layer."""
    default_mat = resolve_vehicle_material(unit_name, "primary")
    for obj in bpy.data.objects:
        if obj.type != "MESH" or obj.name.lower().startswith("preview"):
            continue
        if len(obj.data.materials) != 1 or obj.data.materials[0] is None:
            mat = obj.data.materials[0] if obj.data.materials and obj.data.materials[0] is not None else default_mat
            ensure_single_material(obj, mat)
        ensure_uv_and_color_layers(obj.data)
        if not obj.name.startswith(unit_name) and recipe_prefix and RECIPE_PROP not in obj:
            pass


def make_hollow_track_belt(
    name: str,
    x_center: float,
    width: float,
    *,
    y_rear: float,
    y_front: float,
    z_bot: float,
    z_top: float,
    wall: float = 0.036,
    end_bevel: float = 0.14,
    material: bpy.types.Material,
    recipe_tag: str = "hollow_track_belt",
) -> bpy.types.Object:
    """Build a closed manifold hollow loop track belt with open inner bay for roadwheels/idlers."""
    hw = width * 0.5
    eb = min(end_bevel, (z_top - z_bot) * 0.38, (y_front - y_rear) * 0.25)
    zm_low = z_bot + eb
    zm_high = z_top - eb

    # 8-point chamfered stadium outer loop in YZ (CCW when viewed from +X)
    outer_yz = [
        (y_rear + eb, z_bot),
        (y_front - eb, z_bot),
        (y_front, zm_low),
        (y_front, zm_high),
        (y_front - eb, z_top),
        (y_rear + eb, z_top),
        (y_rear, zm_high),
        (y_rear, zm_low),
    ]
    eb_in = max(0.03, eb - wall * 0.45)
    inner_yz = [
        (y_rear + wall + eb_in, z_bot + wall),
        (y_front - wall - eb_in, z_bot + wall),
        (y_front - wall, zm_low + wall * 0.35),
        (y_front - wall, zm_high - wall * 0.35),
        (y_front - wall - eb_in, z_top - wall),
        (y_rear + wall + eb_in, z_top - wall),
        (y_rear + wall, zm_high - wall * 0.35),
        (y_rear + wall, zm_low + wall * 0.35),
    ]

    verts: list[tuple[float, float, float]] = []
    for (y, z) in outer_yz:
        verts.append((x_center - hw, y, z))
    for (y, z) in outer_yz:
        verts.append((x_center + hw, y, z))
    for (y, z) in inner_yz:
        verts.append((x_center - hw, y, z))
    for (y, z) in inner_yz:
        verts.append((x_center + hw, y, z))

    n = len(outer_yz)
    oL, oR, iL, iR = 0, n, 2 * n, 3 * n
    faces: list[tuple[int, int, int, int]] = []
    for idx in range(n):
        nxt = (idx + 1) % n
        faces.append((oL + idx, oL + nxt, oR + nxt, oR + idx))
        faces.append((iL + nxt, iL + idx, iR + idx, iR + nxt))
        faces.append((oL + nxt, oL + idx, iL + idx, iL + nxt))
        faces.append((oR + idx, oR + nxt, iR + nxt, iR + idx))

    obj = create_mesh_object(name, verts, faces, material, smooth=True)
    apply_bevel_and_weighted_normal(obj, width=0.008, segments=1, angle_limit_deg=32.0)
    tag_v2_recipe(obj, recipe_tag)
    return obj


def _rebuild_or_create_hollow_belt(
    name: str,
    x_center: float,
    width: float,
    *,
    y_rear: float,
    y_front: float,
    z_bot: float,
    z_top: float,
    wall: float,
    end_bevel: float,
    material: bpy.types.Material,
    recipe_tag: str,
) -> bpy.types.Object:
    tmp = make_hollow_track_belt(
        f"__tmp_{name}",
        x_center,
        width,
        y_rear=y_rear,
        y_front=y_front,
        z_bot=z_bot,
        z_top=z_top,
        wall=wall,
        end_bevel=end_bevel,
        material=material,
        recipe_tag=recipe_tag,
    )
    target = bpy.data.objects.get(name)
    if target is not None and target.type == "MESH":
        replace_object_geometry(target, tmp)
        ensure_single_material(target, material)
        tag_v2_recipe(target, recipe_tag)
        return target
    tmp.name = name
    return tmp


def _rebuild_loft_in_place(
    name: str,
    stations: list[tuple[float, float, float, float, float]],
    material: bpy.types.Material,
    *,
    bevel_ratio: float = 0.22,
    taper_cap_ratio: float = 0.88,
    recipe_tag: str,
) -> bpy.types.Object | None:
    existing = bpy.data.objects.get(name)
    if existing is None or existing.type != "MESH":
        return None
    loft_stations = [
        (y, 0.0, (z0 + z1) * 0.5, (hw1 + hw2) * 0.5, max(0.02, (z1 - z0) * 0.5))
        for (y, hw1, hw2, z0, z1) in stations
    ]
    src_obj = make_chamfered_y_loft(
        f"__tmp_{name}",
        loft_stations,
        material,
        bevel_ratio=bevel_ratio,
        taper_cap_ratio=taper_cap_ratio,
    )
    replace_object_geometry(existing, src_obj)
    ensure_single_material(existing, material)
    tag_v2_recipe(existing, recipe_tag)
    return existing


def _rebuild_box_in_place(
    name: str,
    size: tuple[float, float, float],
    center: tuple[float, float, float],
    material: bpy.types.Material,
    *,
    bevel: float = 0.015,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
    recipe_tag: str,
) -> bpy.types.Object | None:
    existing = bpy.data.objects.get(name)
    if existing is None or existing.type != "MESH":
        return None
    src_obj = make_chamfered_box_mesh(
        f"__tmp_{name}",
        size,
        center,
        material,
        bevel=bevel,
        rotation=rotation,
    )
    replace_object_geometry(existing, src_obj)
    ensure_single_material(existing, material)
    tag_v2_recipe(existing, recipe_tag)
    return existing


def _add_symmetric_stays(
    unit_prefix: str,
    stem: str,
    start_r: tuple[float, float, float],
    end_r: tuple[float, float, float],
    radius: float,
    material: bpy.types.Material,
    *,
    recipe_tag: str,
) -> None:
    for side, sx in (("L", -1.0), ("R", 1.0)):
        s_pt = Vector((start_r[0] * sx, start_r[1], start_r[2]))
        e_pt = Vector((end_r[0] * sx, end_r[1], end_r[2]))
        name = f"{unit_prefix}_{stem}_{side}"
        existing = bpy.data.objects.get(name)
        if existing is not None:
            bpy.data.objects.remove(existing, do_unlink=True)
        stay = make_faceted_cylinder(name, s_pt, e_pt, radius, material, segments=10, bevel_ends=False)
        tag_v2_recipe(stay, recipe_tag)


def _trim_box_parts_outside_belt(
    part_names: tuple[str, ...],
    x_inner_max: float,
    x_outer_min: float | None = None,
) -> None:
    """Shift or scale side-mounted parts cleanly outside the track/tire envelope if needed."""
    for name in part_names:
        obj = bpy.data.objects.get(name)
        if obj is None or obj.type != "MESH":
            continue
        x0, x1, _, _, _, _ = world_bounds(obj)
        sign = -1.0 if (x0 + x1) * 0.5 < 0.0 else 1.0
        if sign < 0 and x0 < -x_inner_max:
            _shift_objects([obj], Vector((-x_inner_max - x0, 0.0, 0.0)))
        elif sign > 0 and x1 > x_inner_max:
            _shift_objects([obj], Vector((x_inner_max - x1, 0.0, 0.0)))
        if x_outer_min is not None:
            x0, x1, _, _, _, _ = world_bounds(obj)
            if sign < 0 and x1 > -x_outer_min:
                _shift_objects([obj], Vector((-x_outer_min - x1, 0.0, 0.0)))
            elif sign > 0 and x0 < x_outer_min:
                _shift_objects([obj], Vector((x_outer_min - x0, 0.0, 0.0)))


def _add_beveled_armor_plate(
    name: str,
    size: tuple[float, float, float],
    center: tuple[float, float, float],
    host: bpy.types.Object | None,
    mat: bpy.types.Material,
    *,
    bevel: float = 0.014,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
    embed: float = 0.005,
    seat_downward: bool = True,
    recipe_tag: str = "level1_armor_plate",
) -> bpy.types.Object:
    """Create or rebuild a beveled armor/warning plate seated cleanly onto host."""
    existing = bpy.data.objects.get(name)
    if existing is not None:
        bpy.data.objects.remove(existing, do_unlink=True)
    obj = make_chamfered_box_mesh(
        name,
        size,
        center,
        mat,
        bevel=bevel,
        rotation=rotation,
    )
    tag_v2_recipe(obj, recipe_tag)
    if host is not None:
        if seat_downward:
            seat_object_downward_to_target(obj, host, embed=embed)
        else:
            snap_object_to_nearest(obj, host, embed=embed)
    return obj


# ---------------------------------------------------------------------------
# 1. SandlanceScout v2
# ---------------------------------------------------------------------------

def apply_sandlance_scout_v2() -> list[str]:
    unit = "SandlanceScout"
    _apply_wave_details_cleanly(unit, legacy_dup_prefixes=("ScoutSuspension_",))

    p_armor = resolve_vehicle_material(unit, "primary")
    l_armor = resolve_vehicle_material(unit, "light")
    gunmetal = resolve_vehicle_material(unit, "gunmetal")
    tread = resolve_vehicle_material(unit, "tread")

    notes: list[str] = []

    # 1. Multi-station faceted hull + upper deck + low turret
    hull = _rebuild_loft_in_place(
        "ScoutArmor_Hull",
        [
            (-1.26, 0.52, 0.52, 0.24, 0.72),
            (-0.96, 0.65, 0.65, 0.18, 0.78),
            (0.00, 0.66, 0.66, 0.18, 0.80),
            (0.86, 0.65, 0.65, 0.19, 0.78),
            (1.24, 0.58, 0.54, 0.30, 0.62),
        ],
        p_armor,
        bevel_ratio=0.18,
        taper_cap_ratio=0.90,
        recipe_tag="sandlance_hull_loft",
    )
    deck = _rebuild_loft_in_place(
        "ScoutArmor_Deck",
        [
            (-1.08, 0.44, 0.44, 0.70, 0.90),
            (-0.78, 0.46, 0.46, 0.70, 0.92),
            (-0.20, 0.48, 0.48, 0.71, 0.93),
            (0.24, 0.47, 0.46, 0.71, 0.92),
            (0.45, 0.40, 0.38, 0.72, 0.86),
        ],
        l_armor,
        bevel_ratio=0.22,
        taper_cap_ratio=0.90,
        recipe_tag="sandlance_deck_loft",
    )
    low_turret = _rebuild_loft_in_place(
        "ScoutArmor_LowTurret",
        [
            (-0.70, 0.42, 0.36, 0.960, 1.15),
            (-0.18, 0.48, 0.42, 0.960, 1.16),
            (0.20, 0.46, 0.40, 0.960, 1.15),
            (0.35, 0.34, 0.30, 0.960, 1.10),
        ],
        l_armor,
        bevel_ratio=0.22,
        taper_cap_ratio=0.88,
        recipe_tag="sandlance_turret_loft",
    )
    _rebuild_loft_in_place(
        "ScoutArmor_TurretNose",
        [
            (0.14, 0.33, 0.31, 0.962, 1.12),
            (0.36, 0.31, 0.28, 0.962, 1.10),
            (0.50, 0.23, 0.20, 0.965, 1.06),
        ],
        p_armor,
        bevel_ratio=0.22,
        taper_cap_ratio=0.88,
        recipe_tag="sandlance_turret_nose",
    )
    notes.append("multi-station ScoutArmor Hull/Deck/LowTurret/TurretNose sculpted")

    # 2. Hollow continuous track belts (open bay so roadwheels/idlers/final drive are never swallowed)
    for side, sx in (("L", -0.91), ("R", 0.91)):
        _rebuild_or_create_hollow_belt(
            f"ScoutTreads_Belt_{side}",
            sx,
            0.24,
            y_rear=-1.31,
            y_front=1.21,
            z_bot=0.055,
            z_top=0.610,
            wall=0.032,
            end_bevel=0.105,
            material=tread,
            recipe_tag="sandlance_hollow_belt",
        )
    # Keep track shoes cleanly riding on outer surface of hollow belts (not inside!)
    for side in ("L", "R"):
        fc = bpy.data.objects.get(f"ScoutTreads_ShoeFrontClimb_{side}")
        if fc is not None:
            _shift_objects([fc], Vector((0.0, 0.17, 0.0)))
        rc = bpy.data.objects.get(f"ScoutTreads_ShoeRearClimb_{side}")
        if rc is not None:
            _shift_objects([rc], Vector((0.0, -0.26, 0.0)))
        sb1 = bpy.data.objects.get(f"ScoutTreads_ShoeBottom_{side}_1")
        if sb1 is not None:
            _shift_objects([sb1], Vector((0.0, 0.0, 0.005)))
    notes.append("hollow track belts + climb shoes aligned")

    # 3. Reshape SidePods, IdentificationStripes, and SponsonBridges clear of tracks/belts
    for side, sx in (("L", -1.0), ("R", 1.0)):
        _rebuild_box_in_place(
            f"ScoutArmor_SidePod_{side}",
            (0.14, 1.18, 0.17),
            (sx * 0.71, -0.02, 0.705),
            p_armor,
            bevel=0.014,
            recipe_tag="sandlance_sidepod_clear",
        )
        stripe = bpy.data.objects.get(f"ScoutAccent_IdentificationStripe_{side}")
        if stripe is not None:
            _set_obj_world_center(stripe, Vector((sx * 0.785, -0.02, 0.71)))
        # Sponson bridge runs horizontally BETWEEN hull (|x|=0.65) and inner face of belt (|x|=0.79)
        _rebuild_box_in_place(
            f"ScoutArmor_SponsonBridge_{side}",
            (0.145, 1.72, 0.055),
            (sx * 0.715, -0.04, 0.42),
            p_armor,
            bevel=0.008,
            recipe_tag="sandlance_sponson_bridge",
        )
    # Seat PioneerRack cleanly onto SidePod_L / Deck flank
    sidepod_l = bpy.data.objects.get("ScoutArmor_SidePod_L")
    pioneer_objs = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("ScoutArmor_PioneerRack_")]
    if pioneer_objs and sidepod_l is not None:
        _shift_objects(pioneer_objs, Vector((0.12, 0.0, 0.02)))
        seat_cluster_downward(pioneer_objs, sidepod_l, embed=0.006)
    notes.append("SidePod, Stripe, SponsonBridge, PioneerRack cleared from tracks")

    # 4. Exact Boolean recessed pockets in Deck / LowTurret / Hull
    if deck is not None:
        rad_cov = bpy.data.objects.get("ScoutEngine_RadiatorCavity")
        if rad_cov is not None:
            _set_obj_world_center(rad_cov, Vector((0.0, -0.88, 0.905)))
        cut_recessed_service_pocket(
            deck,
            Vector((0.0, -0.88, 0.905)),
            Vector((0.36, 0.30, 0.045)),
            louver_mat=gunmetal,
            louver_name="SandlanceScout_DeckVent",
            louver_axis="Y",
            louver_count=3,
            recipe_tag="sandlance_deck_vent_pocket",
        )
        # Position greeble louvers on rear engine deck behind LowTurret
        louver_frame = bpy.data.objects.get("SandlanceScout_GreebleLouverFrame")
        if louver_frame is not None:
            _set_obj_world_center(louver_frame, Vector((0.0, -0.88, 0.922)))
        for idx in range(1, 5):
            slat = bpy.data.objects.get(f"SandlanceScout_GreebleLouverSlat_{idx}")
            if slat is not None:
                _set_obj_world_center(slat, Vector((0.0, -0.99 + idx * 0.065, 0.935)))
    if hull is not None:
        # Twin bow intake / tow pockets on glacis + raise SplashRib onto top of glacis
        for side, sx in (("L", -1.0), ("R", 1.0)):
            cut_recessed_service_pocket(
                hull,
                Vector((sx * 0.37, 1.08, 0.66)),
                Vector((0.14, 0.08, 0.08)),
                recipe_tag="sandlance_glacis_light_pocket",
                rotation=(-0.45, 0.0, 0.0),
            )
            fl = bpy.data.objects.get(f"ScoutAccent_FrontLight_{side}")
            if fl is not None:
                _set_obj_world_center(fl, Vector((sx * 0.37, 1.09, 0.65)))
                snap_object_to_nearest(fl, hull, embed=0.005)
        for sr_name in ("SandlanceScoutArmor_SplashRib_SplashRib_Apex", "SandlanceScoutArmor_SplashRib_SplashRib_L", "SandlanceScoutArmor_SplashRib_SplashRib_R"):
            sr = bpy.data.objects.get(sr_name)
            if sr is not None:
                _shift_objects([sr], Vector((0.0, 0.14, 0.06)))
                snap_object_to_nearest(sr, hull, embed=0.003)
    # Seat StowageBasket onto LowTurret rear, PanoramicSight on LowTurret roof, and external hardware
    if low_turret is not None:
        basket_objs = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("SandlanceScoutArmor_StowageBasket_")]
        if basket_objs:
            _shift_objects(basket_objs, Vector((0.0, 0.05, 0.0)))
            seat_cluster_downward(basket_objs, low_turret, embed=0.005)
            _add_symmetric_stays(
                unit,
                "BasketStay",
                (0.28, -0.71, 1.07),
                (0.28, -0.58, 1.02),
                0.016,
                gunmetal,
                recipe_tag="sandlance_basket_stay",
            )
        sight_ring = bpy.data.objects.get("SandlanceScoutSensor_PanoramicSight_RotaryRing")
        sight_head = [
            o for o in bpy.data.objects
            if o.type == "MESH" and o.name.startswith("SandlanceScoutSensor_PanoramicSight_") and o != sight_ring
        ]
        if sight_ring is not None:
            seat_object_downward_to_target(sight_ring, low_turret, embed=0.006)
            if sight_head:
                seat_cluster_downward(sight_head, sight_ring, embed=0.010)
        for gh in ("SandlanceScout_GreebleHatch_L", "SandlanceScout_GreebleHatch_R"):
            obj = bpy.data.objects.get(gh)
            if obj is not None and deck is not None:
                sx = -0.32 if gh.endswith("_L") else 0.32
                _set_obj_world_center(obj, Vector((sx, -0.88, 0.94)))
                seat_object_downward_to_target(obj, deck, embed=0.004)
        for gh in ("SandlanceScout_GreebleHandle_L", "SandlanceScout_GreebleHandle_R"):
            obj = bpy.data.objects.get(gh)
            if obj is not None:
                sx = -0.46 if gh.endswith("_L") else 0.46
                _set_obj_world_center(obj, Vector((sx, 0.05, 1.00)))
                snap_object_to_nearest(obj, low_turret, embed=0.002)

    # Seat shackles, weld seams, access hatch dog latch, ring-guard bolts, torsion bosses, final drives, idlers
    if hull is not None:
        _snap_name_prefix_to_host(
            ("ScoutArmor_FrontShackle_", "SandlanceScoutArmor_WeldSeam_", "ScoutChassis_FinalDrive_", "ScoutChassis_TrackTensioner_"),
            hull,
            embed=0.005,
        )
        # Connect suspension & roadwheels to hull with horizontal axle stubs
        for i, yy in enumerate((-0.89, -0.39, 0.15, 0.68), start=1):
            _add_symmetric_stays(
                unit,
                f"AxleStay_{i}",
                (0.60, yy, 0.35),
                (0.99, yy, 0.35),
                0.024,
                gunmetal,
                recipe_tag="sandlance_axle_stay",
            )
        for i, yy in enumerate((-1.10, 0.94), start=1):
            _add_symmetric_stays(
                unit,
                f"IdlerAxle_{i}",
                (0.60, yy, 0.35),
                (0.99, yy, 0.35),
                0.022,
                gunmetal,
                recipe_tag="sandlance_idler_stay",
            )
        # Bridge SponsonBridge to Belt inner tunnel ceiling/floor via TrackFrame mount
        for side, sx in (("L", -1.0), ("R", 1.0)):
            tm_name = f"SandlanceScout_TrackMount_{side}"
            if _rebuild_box_in_place(
                tm_name,
                (0.06, 1.50, 0.08),
                (sx * 0.775, -0.04, 0.42),
                gunmetal,
                bevel=0.006,
                recipe_tag="sandlance_track_mount",
            ) is None:
                tm = make_chamfered_box_mesh(
                    tm_name,
                    (0.06, 1.50, 0.08),
                    (sx * 0.775, -0.04, 0.42),
                    gunmetal,
                    bevel=0.006,
                )
                tag_v2_recipe(tm, "sandlance_track_mount")
        hatch_grp = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("ScoutArmor_AccessHatch_")]
        if hatch_grp and low_turret is not None:
            _shift_objects(hatch_grp, Vector((0.0, 0.0, 0.35)))
            seat_cluster_downward(hatch_grp, low_turret, embed=0.004)
            hatch = bpy.data.objects.get("ScoutArmor_AccessHatch_HatchPlate")
            dog = bpy.data.objects.get("ScoutArmor_AccessHatch_DogLatch")
            if hatch is not None and dog is not None:
                _shift_objects([dog], Vector((0.0, 0.0, 0.025)))
                seat_object_downward_to_target(dog, hatch, embed=0.002)
        for obj in bpy.data.objects:
            if obj.type != "MESH" or not obj.name.startswith("ScoutArmor_TurretRingGuard_"):
                continue
            if "DeflectorCollar" in obj.name:
                _shift_objects([obj], Vector((0.0, 0.0, 0.036)))
            elif "YawRaceLip" in obj.name:
                _shift_objects([obj], Vector((0.0, 0.0, 0.056)))
            elif "FastenerBolt_" in obj.name:
                _, _, _, _, bz0, _ = world_bounds(obj)
                _shift_objects([obj], Vector((0.0, 0.0, 0.933 - bz0)))
        for obj in bpy.data.objects:
            if obj.type == "MESH" and "FinalDrive_Bolt_" in obj.name:
                c = world_center(obj)
                if c.y < -1.16:
                    _shift_objects([obj], Vector((0.0, 0.04, 0.0)))
        # Ensure TorsionBoss sits on hull side cleanly outside Belt
        for obj in bpy.data.objects:
            if obj.type == "MESH" and "TorsionBoss" in obj.name:
                x0, x1, _, _, _, _ = world_bounds(obj)
                sx = -1.0 if (x0 + x1) < 0 else 1.0
                c = world_center(obj)
                _set_obj_world_center(obj, Vector((sx * 0.70, c.y, c.z)))
                snap_object_to_nearest(obj, hull, embed=0.005)

    # 5. Level-1 RTS readability: LightArmor glacis/turret/skirt plates & Warning deck chevrons
    w_armor = resolve_vehicle_material(unit, "warning")
    _add_beveled_armor_plate(
        "ScoutArmor_GlacisLightPlate", (0.76, 0.38, 0.040), (0.0, 0.82, 0.78),
        hull, l_armor, bevel=0.012, rotation=(-0.18, 0.0, 0.0), seat_downward=True,
        recipe_tag="sandlance_l1_glacis_plate",
    )
    _add_beveled_armor_plate(
        "ScoutArmor_TurretRoofPlate", (0.54, 0.56, 0.036), (0.0, -0.18, 1.16),
        low_turret, l_armor, bevel=0.010, recipe_tag="sandlance_l1_turret_roof",
    )
    for side, sx in (("L", -1.0), ("R", 1.0)):
        _add_beveled_armor_plate(
            f"ScoutArmor_TurretCheek_{side}", (0.11, 0.44, 0.14), (sx * 0.46, -0.12, 1.07),
            low_turret, l_armor, bevel=0.010, seat_downward=False,
            recipe_tag="sandlance_l1_turret_cheek",
        )
        _add_beveled_armor_plate(
            f"ScoutWarning_DeckChevron_{side}", (0.10, 0.36, 0.024), (sx * 0.36, -0.52, 0.92),
            deck, w_armor, bevel=0.006, recipe_tag="sandlance_l1_warning_chevron",
        )
        belt = bpy.data.objects.get(f"ScoutTreads_Belt_{side}")
        _add_beveled_armor_plate(
            f"ScoutArmor_SideSkirtPlate_{side}", (0.045, 1.56, 0.16), (sx * 1.04, -0.04, 0.46),
            belt, l_armor, bevel=0.008, seat_downward=False,
            recipe_tag="sandlance_l1_side_skirt",
        )

    notes.append("Boolean pockets + external hardware + axle stays + L1 armor plates seated")
    _finalize_all_mesh_attributes(unit, unit)
    return notes


# ---------------------------------------------------------------------------
# 2. ColossusSiegePlatform v2
# ---------------------------------------------------------------------------

def apply_colossus_siege_v2() -> list[str]:
    unit = "ColossusSiegePlatform"
    _apply_wave_details_cleanly(unit, legacy_dup_prefixes=("ColossusSuspension_",))

    p_armor = resolve_vehicle_material(unit, "primary")
    l_armor = resolve_vehicle_material(unit, "light")
    gunmetal = resolve_vehicle_material(unit, "gunmetal")
    tread = resolve_vehicle_material(unit, "tread")

    notes: list[str] = []

    # 1. Multi-station heavy artillery chassis, deck, turret base/bustle/mantlet
    hull = _rebuild_loft_in_place(
        "ColossusArmor_Hull",
        [
            (-1.46, 0.64, 0.64, 0.24, 0.74),
            (-1.12, 0.74, 0.74, 0.22, 0.78),
            (0.00, 0.74, 0.74, 0.22, 0.78),
            (1.14, 0.74, 0.74, 0.22, 0.78),
            (1.52, 0.68, 0.62, 0.34, 0.64),
        ],
        p_armor,
        bevel_ratio=0.18,
        taper_cap_ratio=0.90,
        recipe_tag="colossus_hull_loft",
    )
    deck = _rebuild_loft_in_place(
        "ColossusArmor_Deck",
        [
            (-1.02, 0.62, 0.62, 0.72, 1.00),
            (-0.30, 0.68, 0.68, 0.73, 1.02),
            (0.45, 0.68, 0.68, 0.73, 1.02),
            (0.90, 0.60, 0.56, 0.74, 0.96),
        ],
        l_armor,
        bevel_ratio=0.20,
        taper_cap_ratio=0.90,
        recipe_tag="colossus_deck_loft",
    )
    turret_base = _rebuild_loft_in_place(
        "ColossusArmor_TurretBase",
        [
            (-0.66, 0.60, 0.54, 1.095, 1.33),
            (0.05, 0.72, 0.64, 1.095, 1.34),
            (0.55, 0.66, 0.58, 1.095, 1.33),
            (0.76, 0.54, 0.48, 1.095, 1.30),
        ],
        p_armor,
        bevel_ratio=0.22,
        taper_cap_ratio=0.88,
        recipe_tag="colossus_turret_base_loft",
    )
    turret_bustle = _rebuild_loft_in_place(
        "ColossusArmor_TurretBustle",
        [
            (-0.74, 0.38, 0.34, 1.095, 1.32),
            (-0.46, 0.45, 0.41, 1.095, 1.34),
            (-0.22, 0.44, 0.40, 1.095, 1.34),
        ],
        p_armor,
        bevel_ratio=0.20,
        taper_cap_ratio=0.90,
        recipe_tag="colossus_turret_bustle_loft",
    )
    notes.append("multi-station Colossus Hull/Deck/TurretBase/TurretBustle sculpted")

    # 2. Hollow track belts wrapping around DriveSprocket (-1.32) .. FrontIdler (+1.32)
    for side, sx in (("L", -1.08), ("R", 1.08)):
        _rebuild_or_create_hollow_belt(
            f"ColossusTreads_Belt_{side}",
            sx,
            0.32,
            y_rear=-1.555,
            y_front=1.555,
            z_bot=0.055,
            z_top=0.615,
            wall=0.034,
            end_bevel=0.115,
            material=tread,
            recipe_tag="colossus_hollow_belt",
        )
        # Sponson sits cleanly ABOVE track cleats (cleats top z=0.67 -> sponson z=[0.69, 0.90])
        _rebuild_box_in_place(
            f"ColossusArmor_Sponson_{side}",
            (0.24, 2.58, 0.20),
            (sx, -0.02, 0.79),
            p_armor,
            bevel=0.015,
            recipe_tag="colossus_sponson_clear",
        )
        # Armored skirt outside the wheel/track envelope (|x|=1.295..1.345, clear of wheel rim |x|=1.26)
        _rebuild_box_in_place(
            f"ColossusArmor_Skirt_{side}",
            (0.05, 2.48, 0.28),
            ((-1.325 if side == "L" else 1.325), -0.02, 0.50),
            l_armor,
            bevel=0.008,
            recipe_tag="colossus_skirt_clear",
        )
        # Horizontal SponsonBridge linking Hull (|x|=0.73) to inner Sponson/Belt face (|x|=0.93)
        _rebuild_box_in_place(
            f"ColossusArmor_SponsonBridge_{side}",
            (0.22, 2.10, 0.06),
            ((-0.83 if side == "L" else 0.83), 0.0, 0.69),
            p_armor,
            bevel=0.008,
            recipe_tag="colossus_sponson_bridge",
        )
        # Connect skirt to sponson with diagonal armored brackets
        for idx, yy in enumerate((-1.05, -0.35, 0.35, 1.05), start=1):
            b_name = f"ColossusSkirtBracket_{side}_{idx}"
            if bpy.data.objects.get(b_name) is not None:
                bpy.data.objects.remove(bpy.data.objects[b_name], do_unlink=True)
            sx_s = -1.0 if side == "L" else 1.0
            stay = make_faceted_cylinder(
                b_name,
                Vector((sx_s * 1.18, yy, 0.70)),
                Vector((sx_s * 1.31, yy, 0.61)),
                0.020,
                gunmetal,
                segments=8,
                bevel_ends=False,
            )
            tag_v2_recipe(stay, "colossus_skirt_bracket")
    notes.append("hollow belts, raised sponsons, cleared outer skirts + brackets")

    # 3. Exact Boolean recessed pockets in Deck, Hull, and TurretBustle
    if deck is not None:
        cut_recessed_service_pocket(
            deck,
            Vector((0.0, -0.84, 1.005)),
            Vector((0.52, 0.44, 0.05)),
            louver_mat=gunmetal,
            louver_name="Colossus_EngineDeckVent",
            louver_axis="Y",
            louver_count=4,
            recipe_tag="colossus_engine_pocket",
        )
    if hull is not None:
        for side, sx in (("L", -1.0), ("R", 1.0)):
            cut_recessed_service_pocket(
                hull,
                Vector((sx * 0.52, 1.42, 0.66)),
                Vector((0.15, 0.09, 0.09)),
                recipe_tag="colossus_glacis_light_pocket",
                rotation=(-0.45, 0.0, 0.0),
            )
            fl = bpy.data.objects.get(f"ColossusAccent_FrontLight_{side}")
            if fl is not None:
                _set_obj_world_center(fl, Vector((sx * 0.52, 1.43, 0.66)))
                snap_object_to_nearest(fl, hull, embed=0.004)
    if turret_bustle is not None:
        cut_recessed_service_pocket(
            turret_bustle,
            Vector((0.0, -0.71, 1.18)),
            Vector((0.46, 0.08, 0.16)),
            louver_mat=gunmetal,
            louver_name="Colossus_BustleCooler",
            louver_axis="X",
            louver_count=3,
            recipe_tag="colossus_bustle_pocket",
        )

    # 4. Seat all disconnected/contained subassemblies to their hosts + wire axles
    if hull is not None:
        _snap_name_prefix_to_host(
            (
                "ColossusChassis_FinalDrive_",
                "ColossusChassis_TrackTensioner_",
                "ColossusSiegePlatformChassis_TrackTensioner_",
            ),
            hull,
            embed=0.005,
        )
        # Keep shackles rigidly aligned and recoil spade assembly flush on outer bow/transom
        _seat_shackle_groups("FrontShackle", hull, y_target=1.60, z_target=0.46, x_clamp=0.50, embed=0.003)
        _seat_shackle_groups("RearShackle", hull, y_target=-1.52, z_target=0.42, x_clamp=0.25, embed=0.003)
        for side in ("L", "R"):
            blade = bpy.data.objects.get(f"ColossusArmor_RecoilSpadeBlade_{side}")
            if blade is not None:
                snap_object_to_nearest(blade, hull, embed=0.006)
        spade_grp = [
            o for o in bpy.data.objects
            if o.type == "MESH" and "RecoilSpade_" in o.name
        ]
        if spade_grp:
            _shift_objects(spade_grp, Vector((0.0, -0.22, 0.0)))
            for o in spade_grp:
                if "Cylinder_" in o.name:
                    _shift_objects([o], Vector((0.0, -0.16, 0.16)))
                elif "PistonRod" in o.name:
                    _shift_objects([o], Vector((0.0, -0.04, 0.04)))
                elif "Tooth_" in o.name:
                    _shift_objects([o], Vector((0.0, 0.0, 0.025)))
        _snap_name_prefix_to_host(("ColossusSiegePlatformArmor_SplashRib_", "ColossusSiegePlatformArmor_WeldSeam_Glacis"), hull, embed=0.004)
        periscope_grp = [o for o in bpy.data.objects if o.type == "MESH" and "DriverPeriscope_" in o.name]
        if periscope_grp:
            seat_cluster_downward(periscope_grp, hull, embed=0.006)
        for obj in bpy.data.objects:
            if obj.type != "MESH":
                continue
            if "LiftingLug_" in obj.name or "LugWeldPad_" in obj.name:
                c = world_center(obj)
                sx = -1.0 if c.x < 0 else 1.0
                _set_obj_world_center(obj, Vector((sx * 0.72, c.y, 0.78)))
                snap_object_to_nearest(obj, hull, embed=0.002)
        # Connect Road wheels, DriveSprockets, FrontIdlers to Hull with real axle shafts
        for yy in (-1.32, -1.05, -0.52, 0.0, 0.52, 1.05, 1.32):
            _add_symmetric_stays(
                unit,
                f"TrackAxle_{abs(int(round(yy * 100)))}_{'F' if yy >= 0 else 'R'}",
                (0.72, yy, 0.36),
                (1.16, yy, 0.36),
                0.026,
                gunmetal,
                recipe_tag="colossus_axle_stay",
            )
    if deck is not None:
        _snap_name_prefix_to_host(("ColossusSiegePlatformArmor_WeldSeam_", "ColossusArmor_PioneerRack_"), deck, embed=0.003)
        for side in ("L", "R"):
            exh = bpy.data.objects.get(f"ColossusArmor_Exhaust_{side}")
            shield = bpy.data.objects.get(f"ColossusArmor_ExhaustShield_{side}")
            if exh is not None:
                before_e = world_center(exh)
                _shift_objects([exh], Vector((0.0, 0.22, -0.04)))
                snap_object_to_nearest(exh, deck, embed=0.008)
                delta_e = world_center(exh) - before_e
                if shield is not None:
                    _shift_objects([shield], delta_e)
        for g_name in ("ColossusArmor_Gusset_1", "ColossusArmor_Gusset_2"):
            g = bpy.data.objects.get(g_name)
            if g is not None:
                c = world_center(g)
                sx = -1.0 if c.x < 0 else 1.0
                _set_obj_world_center(g, Vector((sx * 0.67, c.y, 0.96)))
                snap_object_to_nearest(g, deck, embed=0.002)
        hatch_grp = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("ColossusSiegePlatformArmor_AccessHatch_")]
        if hatch_grp and (turret_base is not None or deck is not None):
            hatch_host = turret_base if turret_base is not None else deck
            _shift_objects(hatch_grp, Vector((0.0, 0.0, 0.52)))
            seat_cluster_downward(hatch_grp, hatch_host, embed=0.003)
            hatch = bpy.data.objects.get("ColossusSiegePlatformArmor_AccessHatch_HatchPlate")
            dog = bpy.data.objects.get("ColossusSiegePlatformArmor_AccessHatch_DogLatch")
            if hatch is not None and dog is not None:
                _shift_objects([dog], Vector((0.0, 0.0, 0.024)))
                seat_object_downward_to_target(dog, hatch, embed=0.002)
    if turret_base is not None:
        sight_ring = bpy.data.objects.get("ColossusSiegePlatformSensor_PanoramicSight_RotaryRing")
        sight_head = [
            o for o in bpy.data.objects
            if o.type == "MESH" and o.name.startswith("ColossusSiegePlatformSensor_PanoramicSight_") and o != sight_ring
        ]
        if sight_ring is not None:
            _shift_objects([sight_ring], Vector((-0.18, -0.10, 0.10)))
            seat_object_downward_to_target(sight_ring, turret_base, embed=0.005)
            if sight_head:
                _shift_objects(sight_head, Vector((-0.18, -0.10, 0.10)))
                seat_cluster_downward(sight_head, sight_ring, embed=0.008)
        for obj in bpy.data.objects:
            if obj.type != "MESH":
                continue
            if "MortarRecuperator_" in obj.name:
                if "ClevisPin_Base" in obj.name:
                    _shift_objects([obj], Vector((0.0, -0.04, 0.04)))
                snap_object_to_nearest(obj, turret_base, embed=0.002)
            elif obj.name.startswith("ColossusSiegePlatformArmor_TurretRingGuard_"):
                if "DeflectorCollar" in obj.name:
                    _shift_objects([obj], Vector((0.0, -0.14, 0.008)))
                elif "YawRaceLip" in obj.name:
                    _shift_objects([obj], Vector((0.0, -0.14, 0.030)))
                elif "FastenerBolt_" in obj.name:
                    _, _, _, _, bz0, _ = world_bounds(obj)
                    _shift_objects([obj], Vector((0.0, -0.14, 1.064 - bz0)))
            elif "RecoilBracket_" in obj.name:
                c = world_center(obj)
                sx = -1.0 if c.x < 0 else 1.0
                _set_obj_world_center(obj, Vector((sx * max(0.54, abs(c.x)), c.y, max(1.32, c.z))))
                snap_object_to_nearest(obj, turret_base, embed=0.002)
            elif obj.name == "ColossusSensor_AntennaDeckCollar":
                _shift_objects([obj], Vector((0.0, 0.0, 0.04)))
                seat_object_downward_to_target(obj, turret_base, embed=0.002)
            elif obj.name == "ColossusSensor_CupolaVisionSlit":
                _shift_objects([obj], Vector((0.0, 0.06, 0.0)))
    # Seat Sponson PioneerRack cleanly on Sponson_L
    sponson_l = bpy.data.objects.get("ColossusArmor_Sponson_L")
    if sponson_l is not None:
        pioneer = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("ColossusArmor_PioneerRack_")]
        if pioneer:
            for o in pioneer:
                c = world_center(o)
                _set_obj_world_center(o, Vector((-1.08, c.y, 0.91)))
            seat_cluster_downward(pioneer, sponson_l, embed=0.004)
    # TorsionBosses outside belts on hull
    for obj in bpy.data.objects:
        if obj.type == "MESH" and "TorsionBoss" in obj.name and hull is not None:
            c = world_center(obj)
            sx = -1.0 if c.x < 0 else 1.0
            _set_obj_world_center(obj, Vector((sx * 0.77, c.y, c.z)))
            snap_object_to_nearest(obj, hull, embed=0.005)
    # Track tensioners & final drives bridged to hull
    for obj in bpy.data.objects:
        if obj.type == "MESH" and ("TrackTensioner" in obj.name or "FinalDrive" in obj.name) and hull is not None:
            if "FinalDrive_Bolt_" in obj.name:
                continue
            c = world_center(obj)
            if abs(c.x) > 0.85:
                sx = -1.0 if c.x < 0 else 1.0
                _set_obj_world_center(obj, Vector((sx * 0.80, c.y, c.z)))
                snap_object_to_nearest(obj, hull, embed=0.005)
    # Seat FinalDrive bolts visibly on the exterior flange face (|X|=[0.762, 0.786] around r=0.211)
    for side, sx in (("L", -1.0), ("R", 1.0)):
        flange = bpy.data.objects.get(f"ColossusSiegePlatformChassis_FinalDrive_Flange_{side}")
        if flange is None:
            continue
        fx0, fx1, fy0, fy1, fz0, fz1 = world_bounds(flange)
        cy = 0.5 * (fy0 + fy1)
        cz = 0.5 * (fz0 + fz1)
        x_target = (fx0 - 0.005) if sx < 0 else (fx1 + 0.005)
        for idx in range(8):
            bolt = bpy.data.objects.get(f"ColossusSiegePlatformChassis_FinalDrive_Bolt_{side}_{idx}")
            if bolt is None:
                continue
            ang = idx * (2.0 * math.pi / 8.0)
            _set_obj_world_center(
                bolt,
                Vector((x_target, cy + 0.211 * math.cos(ang), cz + 0.211 * math.sin(ang))),
            )

    # 4. Level-1 RTS readability: LightArmor glacis/turret/bustle shields & Warning sponson trim
    w_armor = resolve_vehicle_material(unit, "warning")
    _add_beveled_armor_plate(
        "ColossusArmor_GlacisReactivePlate", (1.04, 0.42, 0.050), (0.0, 1.04, 0.80),
        hull, l_armor, bevel=0.014, rotation=(-0.14, 0.0, 0.0), seat_downward=True,
        recipe_tag="colossus_l1_glacis_plate",
    )
    _add_beveled_armor_plate(
        "ColossusArmor_TurretRoofShield", (0.92, 0.84, 0.045), (0.0, 0.05, 1.35),
        turret_base, l_armor, bevel=0.012, recipe_tag="colossus_l1_turret_roof",
    )
    for side, sx in (("L", -1.0), ("R", 1.0)):
        _add_beveled_armor_plate(
            f"ColossusArmor_BustleSidePlate_{side}", (0.08, 0.50, 0.18), (sx * 0.44, -0.46, 1.22),
            turret_bustle, l_armor, bevel=0.010, seat_downward=False,
            recipe_tag="colossus_l1_bustle_plate",
        )
        sponson = bpy.data.objects.get(f"ColossusArmor_Sponson_{side}")
        _add_beveled_armor_plate(
            f"ColossusWarning_SponsonTrim_{side}", (0.14, 0.64, 0.028), (sx * 1.08, 0.88, 0.90),
            sponson, w_armor, bevel=0.006, recipe_tag="colossus_l1_warning_trim",
        )

    notes.append("Pockets, hardware seating, axle connections, and L1 armor plates complete")
    _finalize_all_mesh_attributes(unit, unit)
    return notes


# ---------------------------------------------------------------------------
# 3. SkirmishStrikeBike v2
# ---------------------------------------------------------------------------

def apply_skirmish_strike_bike_v2() -> list[str]:
    unit = "SkirmishStrikeBike"
    _apply_wave_details_cleanly(unit)

    p_armor = resolve_vehicle_material(unit, "primary")
    l_armor = resolve_vehicle_material(unit, "light")
    gunmetal = resolve_vehicle_material(unit, "gunmetal")
    tread = resolve_vehicle_material(unit, "tread")

    notes: list[str] = []

    # 1. Sculpt central chassis LowerFrame CLEAR of Front Tire (y=[0.365, 1.085]) and Rear Tire (y=[-1.114, -0.394])
    # Center frame spans y=[-0.36, 0.34], z=[0.50, 0.76], with twin swingarm beams flanking outside |x|=[0.145, 0.235]!
    lower_frame = _rebuild_loft_in_place(
        "StrikeBikeArmor_LowerFrame",
        [
            (-0.34, 0.22, 0.20, 0.50, 0.74),
            (-0.10, 0.25, 0.24, 0.48, 0.76),
            (0.18, 0.24, 0.22, 0.50, 0.75),
            (0.34, 0.18, 0.16, 0.53, 0.74),
        ],
        gunmetal,
        bevel_ratio=0.22,
        taper_cap_ratio=0.90,
        recipe_tag="strikebike_lower_frame_loft",
    )
    # Sculpted BatteryPod & FrontFairing clear of both tires
    battery_pod = _rebuild_loft_in_place(
        "StrikeBikeArmor_BatteryPod",
        [
            (-0.46, 0.24, 0.22, 0.76, 1.10),
            (-0.14, 0.27, 0.25, 0.75, 1.14),
            (0.16, 0.26, 0.24, 0.75, 1.13),
            (0.28, 0.22, 0.20, 0.76, 1.08),
        ],
        l_armor,
        bevel_ratio=0.22,
        taper_cap_ratio=0.88,
        recipe_tag="strikebike_battery_pod_loft",
    )
    front_fairing = _rebuild_loft_in_place(
        "StrikeBikeArmor_FrontFairing",
        [
            (0.22, 0.28, 0.24, 0.78, 1.18),
            (0.52, 0.32, 0.26, 0.79, 1.19),
            (0.76, 0.24, 0.18, 0.81, 1.14),
            (0.89, 0.14, 0.10, 0.83, 1.06),
        ],
        p_armor,
        bevel_ratio=0.24,
        taper_cap_ratio=0.88,
        recipe_tag="strikebike_fairing_loft",
    )
    notes.append("sculpted StrikeBike LowerFrame/BatteryPod/FrontFairing clear of tires")

    # 2. Rear & Front swingarms flanking outside Tire width (|x|=0.10) so zero tire overlap
    for side, sx in (("L", -1.0), ("R", 1.0)):
        # SidePods cleared outward slightly & tied to BatteryPod
        sp = bpy.data.objects.get(f"StrikeBikeArmor_SidePod_{'-1' if side == 'L' else '1'}")
        if sp is not None and battery_pod is not None:
            _set_obj_world_center(sp, Vector((sx * 0.36, -0.10, 0.78)))
            snap_object_to_nearest(sp, battery_pod, embed=0.006)
        stripe = bpy.data.objects.get(f"StrikeBikeAccent_SideStripe_{'-1' if side == 'L' else '1'}")
        if stripe is not None and sp is not None:
            _set_obj_world_center(stripe, Vector((sx * 0.45, -0.10, 0.78)))
            snap_object_to_nearest(stripe, sp, embed=0.004)
    _add_symmetric_stays(unit, "RearSwingarm", (0.175, -0.31, 0.56), (0.155, -0.75, 0.39), 0.022, gunmetal, recipe_tag="strikebike_rear_swingarm")
    _add_symmetric_stays(unit, "FrontForkLeg", (0.165, 0.32, 0.82), (0.155, 0.72, 0.39), 0.022, gunmetal, recipe_tag="strikebike_front_fork")

    # 3. Reposition chassis/axle/differential/shock/radiator parts clear of Front/Rear tires & LowerFrame core
    for name in ("StrikeBikeChassis_AxleBeam_Rear", "StrikeBikeChassis_Differential_Rear", "StrikeBikeChassis_AxleBeam_Front", "StrikeBikeChassis_Differential_Front"):
        obj = bpy.data.objects.get(name)
        if obj is not None:
            yy = -0.24 if "Rear" in name else 0.22
            zz = 0.48 if "AxleBeam" in name else 0.45
            _set_obj_world_center(obj, Vector((0.0, yy, zz)))

    # Move RearShock & RearMonoShock rigidly to right flank outside BatteryPod/RearChargeCage & Rear Tire
    rear_shocks = [
        o for o in bpy.data.objects
        if o.type == "MESH" and ("RearShock" in o.name or "RearMonoShock" in o.name)
    ]
    if rear_shocks:
        _shift_group_center_to(rear_shocks, Vector((0.295, -0.66, 0.86)))

    # Shift Thermal Radiators onto BatteryPod flanks (z >= 0.84, |x| >= 0.26) clear of Rear Tire
    for obj in bpy.data.objects:
        if obj.type != "MESH":
            continue
        if "Thermal_Radiator" in obj.name:
            c = world_center(obj)
            sx = -1.0 if c.x < 0 else 1.0
            _set_obj_world_center(obj, Vector((sx * max(0.26, abs(c.x)), max(-0.34, c.y + 0.08), max(0.86, c.z + 0.16))))
            if battery_pod is not None:
                snap_object_to_nearest(obj, battery_pod, embed=0.003)

    # 4. Exact Boolean recessed pockets in FrontFairing (for Headlamp) and BatteryPod
    if front_fairing is not None:
        headlamp_objs = [o for o in bpy.data.objects if o.type == "MESH" and "Headlamp" in o.name]
        if headlamp_objs:
            _shift_group_center_to(headlamp_objs, Vector((0.0, 0.885, 0.96)))
            cut_recessed_service_pocket(
                front_fairing,
                Vector((0.0, 0.84, 0.96)),
                Vector((0.20, 0.09, 0.12)),
                recipe_tag="strikebike_headlamp_pocket",
            )
            anchor_hl = next((o for o in headlamp_objs if "Housing" in o.name), headlamp_objs[0])
            before_hl = world_center(anchor_hl)
            snap_object_to_nearest(anchor_hl, front_fairing, embed=0.004)
            delta_hl = world_center(anchor_hl) - before_hl
            rest_hl = [o for o in headlamp_objs if o != anchor_hl]
            if rest_hl and delta_hl.length > 1e-6:
                _shift_objects(rest_hl, delta_hl)
        for side, sx in (("L", -1.0), ("R", 1.0)):
            damper_grp = [
                o for o in bpy.data.objects
                if o.type == "MESH" and f"ForkDamper_{side}" in o.name
            ]
            if damper_grp:
                _shift_group_center_to(damper_grp, Vector((sx * 0.335, 0.62, 0.92)))
        for obj in bpy.data.objects:
            if obj.type != "MESH":
                continue
            if "FrontCrashBar" in obj.name or "Handlebar" in obj.name or "FrontMarker" in obj.name:
                snap_object_to_nearest(obj, front_fairing, embed=0.004)
            elif "ForkClamp_Lower" in obj.name or "ForkClamp_Upper" in obj.name:
                _set_obj_world_center(obj, Vector((0.0, 0.56, 1.18 if "Upper" in obj.name else 0.77)))
                snap_object_to_nearest(obj, front_fairing, embed=0.002)
        _add_symmetric_stays(unit, "CrashStay", (0.24, 0.62, 0.84), (0.34, 0.72, 0.82), 0.014, gunmetal, recipe_tag="strikebike_crash_stay")
        _add_symmetric_stays(unit, "BarRiser", (0.09, 0.42, 1.15), (0.18, 0.40, 1.24), 0.015, gunmetal, recipe_tag="strikebike_bar_riser")

    if battery_pod is not None:
        cut_recessed_service_pocket(
            battery_pod,
            Vector((0.0, -0.04, 1.12)),
            Vector((0.28, 0.30, 0.04)),
            louver_mat=gunmetal,
            louver_name="SkirmishStrikeBike_BatteryVent",
            louver_axis="Y",
            louver_count=3,
            recipe_tag="strikebike_battery_pocket",
        )
        pioneer_grp = [o for o in bpy.data.objects if o.type == "MESH" and "PioneerRack_" in o.name]
        if pioneer_grp:
            _shift_objects(pioneer_grp, Vector((-0.115, 0.0, 0.0)))
        for obj in bpy.data.objects:
            if obj.type != "MESH":
                continue
            if any(k in obj.name for k in ("StrikeBikeArmor_Gusset_", "StrikeBikeArmor_LiftingLug_", "StrikeBikeArmor_LugWeldPad_", "SkirmishStrikeBikeArmor_WeldSeam_", "InductionSupport_")):
                c = world_center(obj)
                if "LiftingLug" in obj.name or "LugWeldPad" in obj.name:
                    sx = -1.0 if c.x < 0 else 1.0
                    _set_obj_world_center(obj, Vector((sx * 0.31, c.y, max(0.95, c.z))))
                snap_object_to_nearest(obj, battery_pod, embed=0.002)
            elif obj.name == "StrikeBikeEngine_RadiatorCavity":
                _set_obj_world_center(obj, Vector((0.0, -0.46, 1.35)))
                saddle = bpy.data.objects.get("StrikeBikeArmor_Saddle")
                if saddle is not None:
                    snap_object_to_nearest(obj, saddle, embed=0.002)
        hatch_grp = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("StrikeBikeArmor_AccessHatch_")]
        if hatch_grp:
            _shift_objects(hatch_grp, Vector((0.0, 0.24, 0.16)))
            seat_cluster_downward(hatch_grp, battery_pod, embed=0.003)
            hatch = bpy.data.objects.get("StrikeBikeArmor_AccessHatch_HatchPlate")
            dog = bpy.data.objects.get("StrikeBikeArmor_AccessHatch_DogLatch")
            if hatch is not None and dog is not None:
                _shift_objects([dog], Vector((0.0, 0.0, 0.025)))
                seat_object_downward_to_target(dog, hatch, embed=0.002)

    # 5. Level-1 RTS readability: LightArmor fairing crest, battery shrouds & Warning nose fin
    w_armor = resolve_vehicle_material(unit, "warning")
    _add_beveled_armor_plate(
        "StrikeBikeArmor_FairingCrest", (0.32, 0.42, 0.040), (0.0, 0.48, 1.19),
        front_fairing, l_armor, bevel=0.010, rotation=(-0.18, 0.0, 0.0),
        recipe_tag="strikebike_l1_fairing_crest",
    )
    _add_beveled_armor_plate(
        "StrikeBikeWarning_NoseFin", (0.18, 0.22, 0.024), (0.0, 0.72, 1.14),
        front_fairing, w_armor, bevel=0.006, rotation=(-0.25, 0.0, 0.0),
        recipe_tag="strikebike_l1_warning_fin",
    )
    for side, sx in (("L", -1.0), ("R", 1.0)):
        _add_beveled_armor_plate(
            f"StrikeBikeArmor_BatteryShroud_{side}", (0.05, 0.56, 0.16), (sx * 0.27, -0.10, 0.98),
            battery_pod, l_armor, bevel=0.008, seat_downward=False,
            recipe_tag="strikebike_l1_battery_shroud",
        )

    notes.append("StrikeBike pockets, fork/swingarm clearances, hardware, and L1 armor plates seated")
    _finalize_all_mesh_attributes(unit, unit)
    return notes


# ---------------------------------------------------------------------------
# 4. SurgeArcTank v2
# ---------------------------------------------------------------------------

def apply_surge_arc_tank_v2() -> list[str]:
    unit = "SurgeArcTank"
    _apply_wave_details_cleanly(unit)

    p_armor = resolve_vehicle_material(unit, "primary")
    l_armor = resolve_vehicle_material(unit, "light")
    gunmetal = resolve_vehicle_material(unit, "gunmetal")
    tread = resolve_vehicle_material(unit, "tread")

    notes: list[str] = []

    # 1. Multi-station faceted Hull & UpperDeck
    hull = _rebuild_loft_in_place(
        "SurgeArcTankArmor_Hull",
        [
            (-1.14, 0.46, 0.46, 0.25, 0.72),
            (-0.85, 0.54, 0.54, 0.22, 0.78),
            (0.00, 0.54, 0.54, 0.22, 0.78),
            (0.84, 0.54, 0.54, 0.22, 0.78),
            (1.18, 0.48, 0.44, 0.34, 0.62),
        ],
        p_armor,
        bevel_ratio=0.18,
        taper_cap_ratio=0.90,
        recipe_tag="surgearc_hull_loft",
    )
    upper_deck = _rebuild_loft_in_place(
        "SurgeArcTankArmor_UpperDeck",
        [
            (-0.94, 0.54, 0.52, 0.78, 0.96),
            (-0.20, 0.63, 0.61, 0.78, 0.98),
            (0.42, 0.62, 0.60, 0.78, 0.98),
            (0.84, 0.52, 0.48, 0.79, 0.92),
        ],
        l_armor,
        bevel_ratio=0.20,
        taper_cap_ratio=0.90,
        recipe_tag="surgearc_deck_loft",
    )
    notes.append("multi-station SurgeArc Hull + UpperDeck sculpted")

    # 2. Hollow track belts (expanded bay so DriveSprocket_Rim & FrontIdler_Rim clear corners cleanly)
    for side, sx in (("L", -0.801), ("R", 0.801)):
        _rebuild_or_create_hollow_belt(
            f"SurgeArcTankTreads_Belt_{side}",
            sx,
            0.29,
            y_rear=-1.37,
            y_front=1.37,
            z_bot=0.032,
            z_top=0.610,
            wall=0.030,
            end_bevel=0.095,
            material=tread,
            recipe_tag="surgearc_hollow_belt",
        )
        # SponsonBridge strictly inside gap between Hull (|x|=0.54) and Belt inner edge (|x|=0.656), clear of FinalDrive
        _rebuild_box_in_place(
            f"SurgeArcTankArmor_SponsonBridge_{side}",
            (0.12, 1.56, 0.055),
            ((-0.595 if side == "L" else 0.595), 0.0, 0.48),
            p_armor,
            bevel=0.008,
            recipe_tag="surgearc_sponson_bridge",
        )
        # FenderTop at z=[0.63, 0.675] so Capacitor_L/R (lifted to z_min=0.715) has clean 40mm gap
        _rebuild_box_in_place(
            f"SurgeArcTankTreads_FenderTop_{side}",
            (0.34, 2.40, 0.045),
            (sx, 0.0, 0.652),
            l_armor,
            bevel=0.008,
            recipe_tag="surgearc_fender_top",
        )
        f_splash = bpy.data.objects.get(f"SurgeArcTankTreads_FenderSplash_{side}")
        if f_splash is not None:
            _shift_objects([f_splash], Vector((0.0, 0.0, -0.02)))
        s_dir = -1.0 if side == "L" else 1.0
        cap_parts = [
            o for o in bpy.data.objects
            if o.type == "MESH" and any(o.name.startswith(p) for p in (
                f"SurgeArcTankArmor_Capacitor_{side}",
                f"SurgeArcTankArmor_CapacitorCap_{side}",
                f"SurgeArcTankWarning_CapacitorMark_{side}",
            ))
        ]
        if cap_parts:
            _shift_objects(cap_parts, Vector((s_dir * 0.17, 0.0, 0.045)))
        pm = bpy.data.objects.get(f"SurgeArcTankWarning_ProngsMark_{side}")
        if pm is not None:
            _shift_objects([pm], Vector((s_dir * 0.17, 0.0, 0.045)))
    # Pylon bridges UpperDeck (|x|=0.63) to Capacitor inner wall (|x|=0.66) without being inside either!
    _add_symmetric_stays(unit, "CapacitorPylon", (0.60, -0.04, 0.87), (0.68, -0.04, 0.87), 0.040, gunmetal, recipe_tag="surgearc_cap_pylon")
    _add_symmetric_stays(unit, "FenderStay", (0.54, 0.90, 0.65), (0.76, 0.90, 0.65), 0.020, gunmetal, recipe_tag="surgearc_fender_stay")
    _add_symmetric_stays(unit, "FenderStayR", (0.54, -0.90, 0.65), (0.76, -0.90, 0.65), 0.020, gunmetal, recipe_tag="surgearc_fender_stay")
    for idx, yy in enumerate((-0.80, 0.0, 0.80), start=1):
        _add_symmetric_stays(unit, f"SkirtStay_{idx}", (0.80, yy, 0.64), (0.96, yy, 0.48), 0.018, gunmetal, recipe_tag="surgearc_skirt_stay")
    notes.append("hollow belts, cleared SponsonBridge, raised Capacitors + pylons + skirt stays")

    # 3. Exact Boolean visor pocket on UpperDeck & DriverHood + GeneratorPack vent + lugs
    driver_hood = bpy.data.objects.get("SurgeArcTankArmor_DriverHood")
    gen_pack = bpy.data.objects.get("SurgeArcTankArmor_GeneratorPack")
    if upper_deck is not None:
        visor_housing = bpy.data.objects.get("SurgeArcTankSensor_Visor_Housing")
        if visor_housing is not None:
            _set_obj_world_center(visor_housing, Vector((0.0, 0.78, 0.91)))
            cut_visor_pocket_around_housing(upper_deck, visor_housing, margin=0.005, recipe_tag="surgearc_visor_pocket")
            if driver_hood is not None:
                cut_visor_pocket_around_housing(driver_hood, visor_housing, margin=0.005, recipe_tag="surgearc_hood_visor_pocket")
            for vp in bpy.data.objects:
                if vp.type == "MESH" and vp.name.startswith("SurgeArcTankSensor_Visor_") and vp != visor_housing:
                    _shift_objects([vp], Vector((0.0, 0.145, 0.03)))
        periscope_armor = bpy.data.objects.get("SurgeArcTankArmor_DriverPeriscope")
        if periscope_armor is not None and driver_hood is not None:
            seat_object_downward_to_target(periscope_armor, driver_hood, embed=0.003)
        pocket_host = gen_pack if gen_pack is not None else upper_deck
        pocket_z = world_bounds(pocket_host)[5] - 0.015
        cut_recessed_service_pocket(
            pocket_host,
            Vector((0.0, -0.78, pocket_z)),
            Vector((0.46, 0.28, 0.045)),
            louver_mat=gunmetal,
            louver_name="SurgeArcTank_DeckIntake",
            louver_axis="Y",
            louver_count=3,
            recipe_tag="surgearc_deck_intake",
        )
        _snap_name_prefix_to_host(("SurgeArcTankArmor_LiftingLug_", "SurgeArcTankArmor_LugWeldPad_", "SurgeArcTankArmor_WeldSeam_"), upper_deck, embed=0.003)

    # 4. Connect RoadWheels, DriveSprockets, FrontIdlers, Shackles, Tensioners, FinalDrives, SplashRib, ArcCoilStabilizers
    if hull is not None:
        _seat_shackle_groups("FrontShackle", hull, y_target=1.18, z_target=0.40, embed=0.003)
        _seat_shackle_groups("RearShackle", hull, y_target=-1.14, z_target=0.40, embed=0.003)
        for obj in bpy.data.objects:
            if obj.type != "MESH":
                continue
            if "FinalDrive_Bolt_" in obj.name:
                c = world_center(obj)
                sx = -1.0 if c.x < 0 else 1.0
                _set_obj_world_center(obj, Vector((sx * 0.642, c.y, c.z)))
            elif "FinalDrive" in obj.name or "TrackTensioner" in obj.name:
                c = world_center(obj)
                if abs(c.x) > 0.62:
                    sx = -1.0 if c.x < 0 else 1.0
                    _set_obj_world_center(obj, Vector((sx * 0.58, c.y, c.z)))
                    snap_object_to_nearest(obj, hull, embed=0.004)
            elif "SplashRib_Apex" in obj.name:
                _shift_objects([obj], Vector((0.0, -0.05, 0.04)))
                snap_object_to_nearest(obj, hull, embed=0.004)
        for obj in bpy.data.objects:
            if obj.type != "MESH":
                continue
            if "TrackTensioner_TensionerCyl_" in obj.name:
                _shift_objects([obj], Vector((0.0, -0.24, 0.0)))
            elif "TrackTensioner_TensionerPiston_" in obj.name:
                _shift_objects([obj], Vector((0.0, -0.15, 0.0)))
        for idx, yy in enumerate((-1.10, -0.915, -0.457, 0.02, 0.48, 0.93, 1.10), start=1):
            _add_symmetric_stays(
                unit,
                f"RoadAxle_{idx}",
                (0.50, yy, 0.26),
                (0.82, yy, 0.26),
                0.024,
                gunmetal,
                recipe_tag="surgearc_road_axle",
            )
    hatch_grp = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("SurgeArcTankArmor_AccessHatch_")]
    if hatch_grp and upper_deck is not None:
        _shift_objects(hatch_grp, Vector((0.18, -0.10, 0.12)))
        seat_cluster_downward(hatch_grp, upper_deck, embed=0.003)
        hatch = bpy.data.objects.get("SurgeArcTankArmor_AccessHatch_HatchPlate")
        dog = bpy.data.objects.get("SurgeArcTankArmor_AccessHatch_DogLatch")
        if hatch is not None and dog is not None:
            _shift_objects([dog], Vector((0.0, 0.0, 0.025)))
            seat_object_downward_to_target(dog, hatch, embed=0.002)
    for obj in bpy.data.objects:
        if obj.type == "MESH" and "TurretRingGuard_FastenerBolt_" in obj.name:
            _, _, _, _, bz0, _ = world_bounds(obj)
            _shift_objects([obj], Vector((0.0, 0.0, 1.318 - bz0)))
    for side, sx in (("L", -1.0), ("R", 1.0)):
        stab_grp = [o for o in bpy.data.objects if o.type == "MESH" and f"ArcCoilStabilizer_{side}" in o.name]
        if stab_grp:
            _shift_objects(stab_grp, Vector((sx * 0.14, 0.0, 0.13)))
        bus_grp = [o for o in bpy.data.objects if o.type == "MESH" and f"CapacitorBusCable_{side}" in o.name]
        if bus_grp:
            _shift_objects(bus_grp, Vector((sx * 0.10, 0.0, 0.195)))

    # 5. Level-1 RTS readability: LightArmor glacis/deck/sponson plates & Warning deck stripes
    w_armor = resolve_vehicle_material(unit, "warning")
    _add_beveled_armor_plate(
        "SurgeArcTankArmor_GlacisLightPlate", (0.84, 0.38, 0.044), (0.0, 0.96, 0.78),
        hull, l_armor, bevel=0.012, rotation=(-0.14, 0.0, 0.0), seat_downward=True,
        recipe_tag="surgearc_l1_glacis_plate",
    )
    for side, sx in (("L", -1.0), ("R", 1.0)):
        _add_beveled_armor_plate(
            f"SurgeArcTankArmor_DeckCheekPlate_{side}", (0.14, 1.12, 0.042), (sx * 0.46, -0.05, 1.01),
            upper_deck, l_armor, bevel=0.010, recipe_tag="surgearc_l1_deck_cheek",
        )
        _add_beveled_armor_plate(
            f"SurgeArcTankWarning_DeckStripe_{side}", (0.12, 0.34, 0.024), (sx * 0.46, 0.36, 1.02),
            upper_deck, w_armor, bevel=0.006, recipe_tag="surgearc_l1_warning_stripe",
        )
        _add_beveled_armor_plate(
            f"SurgeArcTankArmor_SponsonShield_{side}", (0.06, 1.50, 0.14), (sx * 0.63, -0.05, 0.88),
            upper_deck, l_armor, bevel=0.008, seat_downward=False,
            recipe_tag="surgearc_l1_sponson_shield",
        )

    notes.append("Visor pocket, road axles, shackles, stabilizer clearances, and L1 armor plates applied")
    _finalize_all_mesh_attributes(unit, unit)
    return notes


# ---------------------------------------------------------------------------
# 5. OverlordCommandHQ v2
# ---------------------------------------------------------------------------

def apply_overlord_command_hq_v2() -> list[str]:
    unit = "OverlordCommandHQ"
    _apply_wave_details_cleanly(unit)

    p_armor = resolve_vehicle_material(unit, "primary")
    l_armor = resolve_vehicle_material(unit, "light")
    gunmetal = resolve_vehicle_material(unit, "gunmetal")
    tread = resolve_vehicle_material(unit, "tread")

    notes: list[str] = []

    # 1. Re-space the 4 wheel axles cleanly so Mid & MidFront never overlap!
    # Target Y stations: Rear=-1.48, Mid=-0.50, MidFront=0.48, Front=1.45 (min spacing 0.97m > 0.854m tire dia!)
    axle_targets = {
        "Rear": (-1.425, -1.48),
        "Mid": (-0.171, -0.50),
        "MidFront": (0.520, 0.48),
        "Front": (1.425, 1.45),
    }
    for axle_key, (old_y, new_y) in axle_targets.items():
        dy = new_y - old_y
        if abs(dy) > 1e-4:
            for obj in bpy.data.objects:
                if obj.type == "MESH" and (
                    f"Wheel_{axle_key}_" in obj.name
                    or f"AxleBeam_{axle_key}" in obj.name
                    or f"Differential_{axle_key}" in obj.name
                    or f"ShockStrut_{axle_key}_" in obj.name
                ):
                    _shift_objects([obj], Vector((0.0, dy, 0.0)))

    # Keep ShockCoils & AxleBeams cleanly positioned below ContinuousChassis bottom (z <= 0.64)
    # 2. Multi-station ContinuousChassis narrowed to |x|=0.70 (inside inner tire face |x|=0.875) & raised z_bot=0.66
    chassis = _rebuild_loft_in_place(
        "OverlordCommandHQArmor_ContinuousChassis",
        [
            (-2.14, 0.66, 0.66, 0.66, 0.96),
            (-1.60, 0.72, 0.72, 0.65, 1.00),
            (0.00, 0.72, 0.72, 0.65, 1.00),
            (1.60, 0.72, 0.72, 0.65, 1.00),
            (2.14, 0.66, 0.64, 0.68, 0.94),
        ],
        gunmetal,
        bevel_ratio=0.16,
        taper_cap_ratio=0.90,
        recipe_tag="overlord_chassis_loft",
    )
    # Keep existing 3 fender sets per side + clone Mid fender set for MidFront so all 4 axles get fenders
    for src_tag, dst_tag in (("2_L", "7_L"), ("5_R", "8_R")):
        for suffix in ("ArchTop", "FlareLip", "FrontMudflap", "RearMudflap"):
            src = bpy.data.objects.get(f"OverlordCommandHQFender_{src_tag}_{suffix}")
            dst_name = f"OverlordCommandHQFender_{dst_tag}_{suffix}"
            if src is not None and bpy.data.objects.get(dst_name) is None:
                dup = src.copy()
                dup.data = src.data.copy()
                dup.name = dst_name
                bpy.context.scene.collection.objects.link(dup)
    cab_collar = bpy.data.objects.get("OverlordCommandHQArmor_CabCollar_CoamingCollar")
    if cab_collar is not None:
        # Narrow CabCollar inside chassis & cab deck so it doesn't cross tires
        _rebuild_box_in_place(
            "OverlordCommandHQArmor_CabCollar_CoamingCollar",
            (1.42, 1.24, 0.045),
            (0.0, 1.32, 0.98),
            gunmetal,
            bevel=0.008,
            recipe_tag="overlord_cab_collar_trim",
        )

    # 3. Sculpt stepped ArmoredCabShell (roof z=1.64 so roof-mounted Turret & Gatling sit cleanly ABOVE cab!)
    # Cab glazing sits at y=2.04, z=[1.34, 1.54], so cab top z=1.64 preserves glazing & brow cleanly!
    cab = _rebuild_loft_in_place(
        "OverlordCommandHQArmor_ArmoredCabShell",
        [
            (0.46, 0.84, 0.78, 0.96, 1.64),
            (1.30, 0.84, 0.76, 0.96, 1.64),
            (1.88, 0.80, 0.68, 0.98, 1.62),
            (2.16, 0.72, 0.60, 1.00, 1.48),
        ],
        p_armor,
        bevel_ratio=0.20,
        taper_cap_ratio=0.90,
        recipe_tag="overlord_cab_loft",
    )
    shelter = _rebuild_loft_in_place(
        "OverlordCommandHQArmor_CommandShelterShell",
        [
            (-1.98, 0.84, 0.74, 0.96, 2.04),
            (-1.10, 0.92, 0.82, 0.96, 2.06),
            (-0.10, 0.92, 0.82, 0.96, 2.06),
            (0.44, 0.86, 0.78, 0.96, 1.98),
        ],
        l_armor,
        bevel_ratio=0.20,
        taper_cap_ratio=0.90,
        recipe_tag="overlord_shelter_loft",
    )
    notes.append("8x8 axles respaced + sculpted ContinuousChassis, CabShell, ShelterShell")

    # 4. Elevate Turret & TurretRingGuard onto CabShell roof (y=1.32, dz=+0.57) and Gatling CROWs onto ShelterShell roof!
    turret_dz = 0.57
    for obj in bpy.data.objects:
        if obj.type != "MESH":
            continue
        if obj.name.startswith("OverlordCommandHQTurret_"):
            _shift_objects([obj], Vector((0.0, 0.0, turret_dz)))
        elif obj.name.startswith("OverlordCommandHQArmor_TurretRingGuard_"):
            if "DeflectorCollar" in obj.name or "YawRaceLip" in obj.name:
                _shift_objects([obj], Vector((0.0, 0.80, 0.115)))
            elif "FastenerBolt_" in obj.name:
                _, _, _, _, bz0, _ = world_bounds(obj)
                _shift_objects([obj], Vector((0.0, 0.80, 1.674 - bz0)))
        elif obj.name.startswith("OverlordCommandHQSmoke_"):
            _shift_objects([obj], Vector((0.0, 0.0, -0.14)))
    gatling_grp = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("OverlordCommandHQGatling_")]
    if gatling_grp:
        _shift_objects(gatling_grp, Vector((0.0, 0.0, 0.90)))
        if shelter is not None:
            seat_cluster_downward(gatling_grp, shelter, embed=0.008)
    # Adjust glazing brow & frames cleanly into carved bow pocket
    if cab is not None:
        cut_recessed_service_pocket(
            cab,
            Vector((0.0, 2.04, 1.43)),
            Vector((1.24, 0.14, 0.24)),
            recipe_tag="overlord_cab_glazing_pocket",
        )
        for obj in bpy.data.objects:
            if obj.type == "MESH" and ("CabGlazing" in obj.name or "GlazingBrow" in obj.name):
                _shift_objects([obj], Vector((0.0, 0.0, -0.06)))
                snap_object_to_nearest(obj, cab, embed=0.004)
        for obj in bpy.data.objects:
            if obj.type == "MESH" and "DriverPeriscope" in obj.name:
                _shift_objects([obj], Vector((-0.36, 0.34, 0.26)))
                seat_object_downward_to_target(obj, cab, embed=0.002)

    # 5. Exact Boolean pockets in Chassis (winch) and Shelter (radiator bays & service doors)
    if chassis is not None:
        cut_recessed_service_pocket(
            chassis,
            Vector((0.0, 2.04, 0.72)),
            Vector((0.56, 0.18, 0.22)),
            recipe_tag="overlord_winch_pocket",
        )
        bumper = bpy.data.objects.get("OverlordCommandHQArmor_NoseBumper")
        if bumper is not None:
            _set_obj_world_center(bumper, Vector((0.0, 2.15, 0.68)))
            snap_object_to_nearest(bumper, chassis, embed=0.005)
        winch_grp = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("OverlordCommandHQWinch_")]
        if winch_grp:
            for o in winch_grp:
                if "FairleadFrame" in o.name or "Roller" in o.name:
                    _shift_objects([o], Vector((0.0, -0.035, 0.0)))
            _shift_group_center_to(winch_grp, Vector((0.0, 2.13, 0.85)))
            if bumper is not None:
                seat_cluster_downward(winch_grp, bumper, embed=0.005)
            else:
                seat_cluster_downward(winch_grp, chassis, embed=0.005)
        shackle_front_host = bumper if bumper is not None else chassis
        _seat_shackle_groups("FrontShackle", shackle_front_host, y_target=2.335, z_target=0.68, x_clamp=0.55, snap=False)
        _seat_shackle_groups("RearShackle", chassis, y_target=-2.18, z_target=0.70, x_clamp=0.55, embed=0.003)

    if shelter is not None:
        # Seat roof-mounted equipment ON TOP of Shelter roof (z=2.04..2.06) and flanks
        for prefix in (
            "OverlordCommandHQArmor_AccessHatch_",
            "OverlordCommandHQSensor_MastPedestal",
            "OverlordCommandHQSensor_RadomeMount",
            "OverlordCommandHQSensor_SatcomRadome",
            "OverlordCommandHQRadar_DeckPylon",
        ):
            grp = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith(prefix)]
            if not grp:
                continue
            if "AccessHatch" in prefix:
                _shift_objects(grp, Vector((0.0, 0.0, 0.60)))
                seat_cluster_downward(grp, shelter, embed=0.005)
            elif "DeckPylon" in prefix:
                _shift_objects(grp, Vector((0.0, 0.0, 0.10)))
                seat_cluster_downward(grp, shelter, embed=0.004)
            else:
                for o in grp:
                    seat_object_downward_to_target(o, shelter, embed=0.005)

        collar = bpy.data.objects.get("OverlordCommandHQSensor_AntennaDeckCollar")
        pedestal = bpy.data.objects.get("OverlordCommandHQSensor_MastPedestal")
        if collar is not None and pedestal is not None:
            _shift_objects([collar], Vector((0.0, 0.0, 0.25)))
            seat_object_downward_to_target(collar, pedestal, embed=0.004)

        sight_ring = bpy.data.objects.get("OverlordCommandHQSensor_PanoramicSight_RotaryRing")
        sight_head = [
            o for o in bpy.data.objects
            if o.type == "MESH" and o.name.startswith("OverlordCommandHQSensor_PanoramicSight_") and o != sight_ring
        ]
        if sight_ring is not None:
            _shift_objects([sight_ring], Vector((0.0, -0.10, 0.14)))
            seat_object_downward_to_target(sight_ring, shelter, embed=0.006)
            if sight_head:
                _shift_objects(sight_head, Vector((0.0, -0.10, 0.14)))
                seat_cluster_downward(sight_head, sight_ring, embed=0.008)

        basket_grp = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("OverlordCommandHQArmor_StowageBasket_")]
        if basket_grp:
            # Place StowageBasket cleanly mounted against rear transom of CommandShelterShell (y < -1.98)
            _shift_objects(basket_grp, Vector((0.0, -0.45, 0.14)))
            for bo in basket_grp:
                snap_object_to_nearest(bo, shelter, embed=0.002)

        # Shift Engine_Vents to rear face, Radiators & CoolantManifolds outward to flank mounts
        for obj in bpy.data.objects:
            if obj.type != "MESH":
                continue
            if obj.name.startswith("OverlordCommandHQEngine_Vent_"):
                c = world_center(obj)
                _set_obj_world_center(obj, Vector((c.x * 0.85, -1.98, c.z)))
                snap_object_to_nearest(obj, shelter, embed=0.002)
            elif obj.name.startswith("OverlordCommandHQThermal_Radiator"):
                c = world_center(obj)
                sx = -1.0 if c.x < 0 else 1.0
                _set_obj_world_center(obj, Vector((sx * 0.90, c.y, c.z + 0.12)))
                snap_object_to_nearest(obj, shelter, embed=0.003)
            elif "CoolantManifold_" in obj.name:
                c = world_center(obj)
                sx = -1.0 if c.x < 0 else 1.0
                _set_obj_world_center(obj, Vector((sx * 0.96, c.y, c.z + 0.12)))
                snap_object_to_nearest(obj, shelter, embed=0.003)
            elif "ServiceDoor_" in obj.name or "Gusset_" in obj.name:
                snap_object_to_nearest(obj, shelter, embed=0.003)
            elif "LiftingLug_" in obj.name or "LugWeldPad_" in obj.name:
                c = world_center(obj)
                sx = -1.0 if c.x < 0 else 1.0
                _set_obj_world_center(obj, Vector((sx * (0.94 if c.y < 0.4 else 0.86), c.y, 1.05)))
                snap_object_to_nearest(obj, shelter if c.y < 0.4 else cab, embed=0.002)
            elif "WeldSeam_" in obj.name:
                c = world_center(obj)
                target = cab if c.y > 0.5 else shelter
                if "Prow" in obj.name:
                    _set_obj_world_center(obj, Vector((0.0, 1.70, 1.65)))
                    seat_object_downward_to_target(obj, cab, embed=0.002)
                else:
                    snap_object_to_nearest(obj, target, embed=0.002)
        bvh_shelter, _ = _mesh_bvh(shelter)
        for stem in ("CommandUmbilical_", "PowerHose_"):
            for side, sx in (("L", -1.0), ("R", 1.0)):
                grp = [o for o in bpy.data.objects if o.type == "MESH" and stem in o.name and f"_{side}" in o.name]
                if not grp:
                    continue
                dz = 0.24 if stem == "CommandUmbilical_" else 0.12
                _shift_objects(grp, Vector((sx * 0.28, 0.0, dz)))
                coupler = next((o for o in grp if "Coupler_2" in o.name), grp[-1])
                c_pt = world_center(coupler)
                cx0, cx1, _, _, _, _ = world_bounds(coupler)
                half_w = (cx1 - cx0) * 0.5
                loc, _, _, _ = bvh_shelter.find_nearest(c_pt)
                if loc is not None:
                    # Place Coupler_2 outer-adjacent face just overlapping ShelterShell wall by 6 mm
                    target_x = float(loc.x) + sx * max(0.015, half_w - 0.006)
                    _shift_objects(grp, Vector((target_x - c_pt.x, 0.0, 0.0)))

    # 6. Upgrade all 8 wheel-arch fenders + raycast stays clear of all 8 tires
    tires = [o for o in bpy.data.objects if o.type == "MESH" and ("TireCarcass" in o.name or "TreadBlocks" in o.name)]
    hosts = [h for h in (shelter, cab, chassis) if h is not None]
    fender_pairs = [
        ("1_L", "Rear_L"), ("2_L", "Mid_L"), ("7_L", "MidFront_L"), ("3_L", "Front_L"),
        ("4_R", "Rear_R"), ("5_R", "Mid_R"), ("8_R", "MidFront_R"), ("6_R", "Front_R"),
    ]
    stay_total = 0
    for f_tag, w_tag in fender_pairs:
        arch = bpy.data.objects.get(f"OverlordCommandHQFender_{f_tag}_ArchTop")
        tire = bpy.data.objects.get(f"OverlordCommandHQWheel_{w_tag}_TireCarcass")
        if arch is None or tire is None:
            continue
        stays = build_sculpted_wheel_fender_set(
            unit,
            arch,
            tire,
            hosts,
            tires,
            p_armor,
            gunmetal,
            l_armor,
        )
        stay_total += len(stays)

    # Connect ShockCoils & SuspensionArms cleanly to ContinuousChassis & Wheel hubs
    if chassis is not None:
        for obj in bpy.data.objects:
            if obj.type == "MESH" and "ShockCoil" in obj.name:
                c = world_center(obj)
                sx = -1.0 if c.x < 0 else 1.0
                _set_obj_world_center(obj, Vector((sx * 0.78, c.y, 0.58)))
                snap_object_to_nearest(obj, chassis, embed=0.005)

    # 7. Level-1 RTS readability: LightArmor cab/shelter roof shields, side ribs & Warning brow trim
    w_armor = resolve_vehicle_material(unit, "warning")
    _add_beveled_armor_plate(
        "OverlordCommandHQArmor_CabRoofShield", (1.26, 0.96, 0.046), (0.0, 1.18, 1.65),
        cab, l_armor, bevel=0.012, recipe_tag="overlord_l1_cab_roof",
    )
    _add_beveled_armor_plate(
        "OverlordCommandHQArmor_ShelterRoofDeck", (1.42, 1.84, 0.052), (0.0, -0.78, 2.07),
        shelter, l_armor, bevel=0.014, recipe_tag="overlord_l1_shelter_roof",
    )
    _add_beveled_armor_plate(
        "OverlordCommandHQWarning_CabBrowTrim", (1.18, 0.14, 0.028), (0.0, 1.96, 1.56),
        cab, w_armor, bevel=0.006, recipe_tag="overlord_l1_warning_brow",
    )
    for side, sx in (("L", -1.0), ("R", 1.0)):
        _add_beveled_armor_plate(
            f"OverlordCommandHQArmor_ShelterSideRib_{side}", (0.06, 1.86, 0.22), (sx * 0.91, -0.78, 1.62),
            shelter, p_armor, bevel=0.010, seat_downward=False,
            recipe_tag="overlord_l1_shelter_rib",
        )

    notes.append(f"8 sculpted wheel-arch fenders + {stay_total} stays; Turret/Gatling elevated; L1 armor plates seated")
    _finalize_all_mesh_attributes(unit, unit)
    return notes


UNIT_V2_RECIPE_MAP: dict[str, Callable[[], list[str]]] = {
    "SandlanceScout": apply_sandlance_scout_v2,
    "ColossusSiegePlatform": apply_colossus_siege_v2,
    "SkirmishStrikeBike": apply_skirmish_strike_bike_v2,
    "SurgeArcTank": apply_surge_arc_tank_v2,
    "OverlordCommandHQ": apply_overlord_command_hq_v2,
}


# ---------------------------------------------------------------------------
# Comprehensive Audit: Topology, UVs, Color, Overlaps, Closed-Solid Containment, BFS Connectivity
# ---------------------------------------------------------------------------

def _is_intentional_mechanical_joint(small_name: str, big_name: str) -> bool:
    """Allow genuine coaxial mechanical sleeve/piston/clevis/clamp/optic-inset joints."""
    if ("TensionerPiston" in small_name and ("TensionerCyl" in big_name or "IdlerYokeArm" in big_name)):
        return True
    if ("ClevisPin" in small_name and ("ClevisEye" in big_name or "WiperSealCollar" in big_name)):
        return True
    if ("ForkDamper" in small_name and "ForkDamper" in big_name and any(k in big_name for k in ("OuterBarrel", "WiperSealCollar", "PressureRing", "ClevisEye"))):
        return True
    if ("HoseClamp" in small_name and "Hose" in big_name):
        return True
    if ("BrakeCaliper" in small_name and ("ForkLeg" in big_name or "RearSwingarm" in big_name)):
        return True
    if ("Fork_Clamp" in small_name and "StrikeBikeFork" in big_name):
        return True
    if ("OpticPrism" in small_name and "PeriscopeHood" in big_name):
        return True
    if (("Lens_" in small_name or "_Well" in small_name) and ("Headlamp_Housing" in big_name or "Headlamp_Well" in big_name or "Visor_Housing" in big_name)):
        return True
    if ("CupolaVisionSlit" in small_name and "CommanderCupola" in big_name):
        return True
    if ("RadomeMount" in small_name and "SatcomRadome" in big_name):
        return True
    return False


def point_inside_bvh(bvh: BVHTree, point: Vector) -> bool:
    """3-axis odd-parity raycast test for closed-solid point containment."""
    votes = 0
    for axis in (Vector((1.0, 0.0, 0.0)), Vector((0.0, 1.0, 0.0)), Vector((0.0, 0.0, 1.0))):
        hits = 0
        origin = point.copy()
        for _ in range(16):
            loc, _, _, _ = bvh.ray_cast(origin, axis, 50.0)
            if loc is None:
                break
            hits += 1
            origin = loc + axis * 1e-4
        if (hits % 2) == 1:
            votes += 1
    return votes >= 2


def audit_v2_scene_clearances(unit_name: str) -> dict[str, Any]:
    """Audit v2 vehicle scene for UVs, Color RGBA, materials, duplicates, wheel/belt overlaps,
    closed-solid containment, and hull BFS connectivity (<= 8 mm).
    """
    render_objs = [
        o for o in bpy.data.objects
        if o.type == "MESH"
        and "_COLLIDER" not in o.name
        and not o.name.endswith("_ShieldBubble")
        and not o.name.lower().startswith("preview")
    ]
    missing_uv: list[str] = []
    missing_color: list[str] = []
    bad_mat_count: list[str] = []
    duplicate_suffixes: list[str] = []
    tagged_recipes: dict[str, str] = {}

    for o in render_objs:
        if o.name.endswith((".001", ".002", ".003")):
            duplicate_suffixes.append(o.name)
        if not o.data.uv_layers or "UVMap" not in o.data.uv_layers:
            missing_uv.append(o.name)
        ca = o.data.color_attributes.get("Color")
        if ca is None or ca.domain != "CORNER" or ca.data_type != "BYTE_COLOR":
            missing_color.append(o.name)
        if len(o.data.materials) != 1 or o.data.materials[0] is None:
            bad_mat_count.append(f"{o.name}({len(o.data.materials)})")
        if RECIPE_PROP in o:
            tagged_recipes[o.name] = str(o[RECIPE_PROP])

    bvh_cache: dict[str, BVHTree] = {}
    bounds_cache: dict[str, tuple[float, float, float, float, float, float]] = {}
    for o in render_objs:
        bvh_cache[o.name], _ = _mesh_bvh(o)
        bounds_cache[o.name] = world_bounds(o)

    # 1. Wheel / tire / belt overlap checks against armor, fenders, stays, chassis, pods, skirts, sponsons, capacitors
    wheel_objs = [
        o for o in render_objs
        if any(k in o.name for k in ("TireCarcass", "TreadBlocks", "StrikeBikeTire_", "Treads_Belt_"))
    ]
    clear_body_objs = [
        o for o in render_objs
        if any(k in o.name for k in (
            "Fender_", "_Stay_", "Hull", "LowerFrame", "FrontFairing", "ContinuousChassis",
            "SidePod", "IdentificationStripe", "SponsonBridge", "Skirt_", "Sponson_",
            "Capacitor_", "FenderTop_", "CabCollar", "RearShock", "RearMonoShock",
            "Thermal_Radiator", "AxleBeam", "Differential",
        ))
        and "Mudflap" not in o.name
        and "Belt" not in o.name
        and "SkirtBracket" not in o.name
    ]
    wheel_overlaps: list[str] = []
    for w in wheel_objs:
        wb = bvh_cache[w.name]
        for b in clear_body_objs:
            if w == b:
                continue
            if wb.overlap(bvh_cache[b.name]):
                wheel_overlaps.append(f"{b.name} <-> {w.name}")

    # Also check track belts vs roadwheels / idlers / sprockets / tensioners / final drives
    belt_objs = [o for o in render_objs if "Treads_Belt_" in o.name]
    running_gear = [
        o for o in render_objs
        if any(k in o.name for k in ("RoadWheel", "Wheel_Road_", "DriveSprocket", "FrontIdler", "Wheel_Idler_", "Tensioner", "FinalDrive"))
    ]
    for belt in belt_objs:
        bb = bvh_cache[belt.name]
        for rg in running_gear:
            if bb.overlap(bvh_cache[rg.name]):
                wheel_overlaps.append(f"{belt.name} <-> {rg.name}")

    # Check adjacent tire pairs on OverlordCommandHQ & SkirmishStrikeBike do not collide with each other
    tires_only = [o for o in render_objs if "TireCarcass" in o.name or "StrikeBikeTire_" in o.name]
    for i in range(len(tires_only)):
        for j in range(i + 1, len(tires_only)):
            if bvh_cache[tires_only[i].name].overlap(bvh_cache[tires_only[j].name]):
                wheel_overlaps.append(f"{tires_only[i].name} <-> {tires_only[j].name}")

    # 2. Closed-solid ray-parity containment check (detect small meshes swallowed inside larger solid hulls/belts)
    contained_solids: list[str] = []
    for small in render_objs:
        sx0, sx1, sy0, sy1, sz0, sz1 = bounds_cache[small.name]
        sc = Vector(((sx0 + sx1) * 0.5, (sy0 + sy1) * 0.5, (sz0 + sz1) * 0.5))
        s_vol = max(1e-9, (sx1 - sx0) * (sy1 - sy0) * (sz1 - sz0))
        for big in render_objs:
            if small == big:
                continue
            bx0, bx1, by0, by1, bz0, bz1 = bounds_cache[big.name]
            b_vol = max(1e-9, (bx1 - bx0) * (by1 - by0) * (bz1 - bz0))
            if b_vol <= s_vol * 1.25:
                continue
            if not (bx0 <= sx0 + 1e-4 and sx1 <= bx1 + 1e-4 and by0 <= sy0 + 1e-4 and sy1 <= by1 + 1e-4 and bz0 <= sz0 + 1e-4 and sz1 <= bz1 + 1e-4):
                continue
            if _is_intentional_mechanical_joint(small.name, big.name):
                continue
            if point_inside_bvh(bvh_cache[big.name], sc):
                contained_solids.append(f"{small.name} INSIDE {big.name}")
                break

    # 3. Hull BFS connectivity audit: every renderable mesh must connect within 8 mm to main hull/frame
    disconnected_from_hull: list[str] = []
    root_obj = next(
        (
            o for o in render_objs
            if o.name.endswith(("Armor_Hull", "Armor_LowerFrame", "Armor_ContinuousChassis"))
        ),
        render_objs[0] if render_objs else None,
    )
    if root_obj is not None:
        pts_cache: dict[str, list[Vector]] = {}
        for o in render_objs:
            mw = o.matrix_world
            pts_cache[o.name] = [mw @ v.co for v in o.data.vertices]
        visited = {root_obj.name}
        queue = deque([root_obj.name])
        tol = 0.008
        while queue:
            cur = queue.popleft()
            cx0, cx1, cy0, cy1, cz0, cz1 = bounds_cache[cur]
            cbvh = bvh_cache[cur]
            cpts = pts_cache[cur]
            for nxt in render_objs:
                nn = nxt.name
                if nn in visited:
                    continue
                nx0, nx1, ny0, ny1, nz0, nz1 = bounds_cache[nn]
                if (
                    cx1 + tol < nx0 or nx1 + tol < cx0
                    or cy1 + tol < ny0 or ny1 + tol < cy0
                    or cz1 + tol < nz0 or nz1 + tol < cz0
                ):
                    continue
                nbvh = bvh_cache[nn]
                if cbvh.overlap(nbvh):
                    visited.add(nn)
                    queue.append(nn)
                    continue
                connected = False
                for p in pts_cache[nn][::max(1, len(pts_cache[nn]) // 24)]:
                    loc, _, _, dist = cbvh.find_nearest(p, tol)
                    if loc is not None and dist <= tol:
                        connected = True
                        break
                if not connected:
                    for p in cpts[::max(1, len(cpts) // 24)]:
                        loc, _, _, dist = nbvh.find_nearest(p, tol)
                        if loc is not None and dist <= tol:
                            connected = True
                            break
                if connected:
                    visited.add(nn)
                    queue.append(nn)
        disconnected_from_hull = sorted(o.name for o in render_objs if o.name not in visited)

    return {
        "unit_name": unit_name,
        "render_mesh_count": len(render_objs),
        "total_faces": sum(len(o.data.polygons) for o in render_objs),
        "tagged_v2_objects": len(tagged_recipes),
        "missing_uv": missing_uv,
        "missing_color": missing_color,
        "bad_mat_count": bad_mat_count,
        "duplicate_suffixes": duplicate_suffixes,
        "wheel_overlaps": wheel_overlaps,
        "contained_solids": contained_solids,
        "disconnected_from_hull": disconnected_from_hull,
        "passed": (
            not missing_uv
            and not missing_color
            and not bad_mat_count
            and not duplicate_suffixes
            and not wheel_overlaps
            and not contained_solids
            and not disconnected_from_hull
            and len(tagged_recipes) >= 3
        ),
    }
