#!/usr/bin/env python3
"""Geometry v2 upgrade recipes and multi-frame/containment audits for Rogue Command
sample-native walkers, mechs, and ArachnidHexapodFactory.

Applies:
  - Exact Boolean (`solver="EXACT"`) armor/service/optic pockets and bay cuts
  - BVHTree + ray_cast physical seating and structural stay/bridge collars
  - Closed-solid containment auditing (preventing buried/invisible parts)
  - Multi-frame Action_Walk clearance & foot-floor checks on rigged mechs
  - Post-Boolean rigid 1.0 vertex-weight re-assignment & semantic bone binding
  - Single material per mesh, UVMap + Color CORNER BYTE_COLOR, Forsyth cache optimization
"""
from __future__ import annotations

import re
from typing import Sequence

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

import RogueModelKit as rmk
import mesh_geometry_algorithms as mga
from tactical_operations_v2_recipes import (
    RECIPE_PROP,
    add_raycast_stay_between,
    cleanup_mesh_topology,
    cut_recessed_service_pocket,
    cut_visor_pocket_around_housing,
    ensure_single_material,
    ensure_uv_and_color_layers,
    make_chamfered_box_mesh,
    make_faceted_cylinder,
    remove_objects_matching,
    resolve_unit_material,
    seat_cluster_downward,
    seat_object_downward_to_target,
    snap_object_to_nearest,
    world_bounds,
    world_center,
)


IGNORED_PREFIXES = ("LOD", "PreviewFloor", "RCL_Temp", "_Tmp")

# Mechanical telescoping / socketed / nested pairs intentionally allowed to overlap or telescope
ALLOWED_CONTAINMENT_TOKEN_PAIRS = (
    ("InnerRamRod", "OuterBarrel"),
    ("InnerRamRod", "WiperSealCollar"),
    ("InnerRamRod", "PressureRing"),
    ("ClevisPin", "ClevisEye"),
    ("ClevisEye", "ThighArmor"),
    ("ClevisPin", "ThighArmor"),
    ("ClevisEye", "PelvisBlock"),
    ("ClevisPin", "PelvisBlock"),
    ("ClevisPin", "HipActuator"),
    ("ClevisPin", "FemurBoom"),
    ("FeedThroatCollar", "GatlingBracer"),
    ("RecuperatorBaseStay", "RecoilRecuperator"),
    ("HydraulicRod", "LoadStrut"),
    ("Pin", "Saddle"),
    ("MuzzleBore", "MuzzleBrake"),
    ("MuzzleBore", "BarrelTube"),
    ("BarrelTube", "ThermalSleeve"),
    ("BarrelTube", "Mantlet"),
    ("BarrelTube", "Breech"),
    ("ThermalSleeve", "BoreEvacuator"),
    ("EvacuatorStripe", "BoreEvacuator"),
    ("Lens", "Well"),
    ("Well", "Housing"),
    ("DriverPeriscope", "DriverHood"),
    ("HydraulicRod", "HydraulicRam"),
    ("DarkBore", "MissileCell"),
    ("MissileCell", "DorsalMissileCradle"),
    ("KneeAxle", "Knee"),
    ("TrunnionAxle", "TrunnionCradle"),
    ("MidHip", "HipFairing"),
    ("HydraulicRam", "FemurBoom"),
    ("HydraulicRod", "FemurBoom"),
    ("Piston_Quad", "FemurBoom"),
    ("HipFlange", "Hull"),
    ("HipFlange", "Pelvis"),
    ("Pelvis_", "ArmoredCarapace"),
    ("ThoraxCollar", "ArmoredCarapace"),
    ("LugWeldPad", "LiftingLug"),
    ("SpringCoil", "Bracket"),
    ("ShutterRib", "SegmentedBlastShutter"),
    ("TrackGuide", "DeploymentApron"),
)


def _is_unit_mesh(obj: bpy.types.Object, prefix: str | None = None) -> bool:
    if obj.type != "MESH":
        return False
    if any(obj.name.startswith(p) for p in IGNORED_PREFIXES):
        return False
    if prefix and not obj.name.startswith(prefix):
        return False
    return True


def _collect_meshes(prefix: str | None = None) -> list[bpy.types.Object]:
    return [o for o in sorted(bpy.data.objects, key=lambda x: x.name) if _is_unit_mesh(o, prefix)]


def _remove_duplicate_suffix_objects() -> int:
    """Remove Blender `.001`-style duplicate mesh objects created by repeated passes."""
    pattern = re.compile(r"\.\d{3}$")
    removed = 0
    for obj in list(bpy.data.objects):
        if obj.type == "MESH" and pattern.search(obj.name):
            bpy.data.objects.remove(obj, do_unlink=True)
            removed += 1
    return removed


def _translate_cluster(objs: Sequence[bpy.types.Object | None], delta: Vector) -> None:
    for obj in objs:
        if obj is None:
            continue
        obj.location = obj.location + delta
    bpy.context.view_layer.update()


def _snap_cluster_to_surface(
    cluster: Sequence[bpy.types.Object],
    anchor: bpy.types.Object | None,
    targets: Sequence[bpy.types.Object],
    *,
    embed_fraction: float = 0.35,
    fixed_embed: float | None = None,
) -> None:
    """Translate `cluster` together so `anchor` contacts the closest surface on `targets` without burial."""
    if not cluster or anchor is None or not targets:
        return
    deps = bpy.context.evaluated_depsgraph_get()
    ac = world_center(anchor)
    best_dist = 1e9
    best_loc = None
    best_norm = None
    for t in targets:
        bvh, _, _ = _evaluated_world_bvh(t, deps)
        loc, norm, _, d = bvh.find_nearest(ac)
        if loc is not None and d < best_dist:
            best_dist = d
            best_loc = loc
            best_norm = norm
    if best_loc is None:
        return
    if fixed_embed is not None:
        offset = -fixed_embed
    else:
        x0, x1, y0, y1, z0, z1 = world_bounds(anchor)
        half_ext = 0.5 * min(max(1e-3, x1 - x0), max(1e-3, y1 - y0), max(1e-3, z1 - z0))
        offset = max(0.002, half_ext * (1.0 - embed_fraction))
    n = best_norm.normalized() if (best_norm is not None and best_norm.length > 1e-6) else Vector((0.0, 1.0, 0.0))
    target_pt = best_loc + n * offset
    _translate_cluster(cluster, target_pt - ac)


def _tag_object(obj: bpy.types.Object | None, tag: str) -> None:
    if obj is not None:
        obj[RECIPE_PROP] = tag


def _needs_tag(objs: Sequence[bpy.types.Object | None], tag: str) -> bool:
    return any(o is not None and o.get(RECIPE_PROP) != tag for o in objs)


def _stay_between(
    name: str,
    origin_obj: bpy.types.Object | None,
    target_obj: bpy.types.Object | None,
    unit_name: str,
    *,
    role: str = "Gunmetal",
    radius: float = 0.025,
    direction: Sequence[float] | None = None,
    anchor: Sequence[float] | None = None,
    embed_depth: float = 0.008,
    max_dist: float = 1.6,
    tag: str | None = None,
) -> bpy.types.Object | None:
    if origin_obj is None or target_obj is None:
        return None
    existing = bpy.data.objects.get(name)
    if existing is not None:
        bpy.data.objects.remove(existing, do_unlink=True)
    mat = resolve_unit_material(unit_name, role)
    origin_pt = Vector(anchor) if anchor is not None else world_center(origin_obj)
    ray_dir = Vector(direction) if direction is not None else (world_center(target_obj) - origin_pt)
    if ray_dir.length < 1e-6:
        ray_dir = Vector((0.0, 0.0, -1.0))
    stay = add_raycast_stay_between(
        name,
        origin_obj,
        [target_obj],
        origin_pt,
        ray_dir.normalized(),
        radius=radius,
        material=mat,
        embed_depth=embed_depth,
        max_dist=max_dist,
    )
    if stay is not None and tag:
        _tag_object(stay, tag)
    return stay


def _evaluated_world_bvh(obj: bpy.types.Object, depsgraph: bpy.types.Depsgraph) -> tuple[BVHTree, list[Vector], bool]:
    """Build world-space BVHTree for `obj` under current depsgraph and check closed manifold."""
    ev = obj.evaluated_get(depsgraph)
    me = ev.to_mesh()
    try:
        bm = bmesh.new()
        bm.from_mesh(me)
        is_closed = len(bm.edges) > 0 and all(len(e.link_faces) == 2 for e in bm.edges)
        bm.free()
        mw = obj.matrix_world
        verts = [mw @ v.co for v in me.vertices]
        polys = [list(p.vertices) for p in me.polygons]
        if not verts or not polys:
            return BVHTree.FromPolygons([], [], all_triangles=False), verts, False
        return BVHTree.FromPolygons(verts, polys, all_triangles=False), verts, is_closed
    finally:
        ev.to_mesh_clear()


def _min_distance_between_meshes(
    obj_a: bpy.types.Object,
    bvh_a: BVHTree,
    obj_b: bpy.types.Object,
    bvh_b: BVHTree,
    depsgraph: bpy.types.Depsgraph,
) -> float:
    if bvh_a.overlap(bvh_b):
        return 0.0
    best = 1e9
    for src, target_bvh in ((obj_a, bvh_b), (obj_b, bvh_a)):
        ev = src.evaluated_get(depsgraph)
        me = ev.to_mesh()
        try:
            mw = src.matrix_world
            for v in me.vertices:
                _, _, _, d = target_bvh.find_nearest(mw @ v.co)
                if d < best:
                    best = d
            for p in me.polygons:
                _, _, _, d = target_bvh.find_nearest(mw @ p.center)
                if d < best:
                    best = d
        finally:
            ev.to_mesh_clear()
    return float(best)


def _point_inside_closed_bvh(point: Vector, bvh: BVHTree) -> bool:
    """Parity ray test along 3 orthogonal directions for closed manifold BVHTrees."""
    dirs = (Vector((0.0, 0.0, 1.0)), Vector((1.0, 0.0, 0.0)), Vector((0.0, 1.0, 0.0)))
    odd_votes = 0
    for d in dirs:
        hits = 0
        origin = point.copy()
        for _ in range(32):
            loc, _, _, _ = bvh.ray_cast(origin, d, 50.0)
            if loc is None:
                break
            hits += 1
            origin = loc + d * 1e-4
        if hits % 2 == 1:
            odd_votes += 1
    return odd_votes >= 2


def _is_allowed_containment_pair(name_a: str, name_b: str) -> bool:
    for tok_a, tok_b in ALLOWED_CONTAINMENT_TOKEN_PAIRS:
        if (tok_a in name_a and tok_b in name_b) or (tok_b in name_a and tok_a in name_b):
            return True
    return False


def audit_closed_solid_containment(
    meshes: Sequence[bpy.types.Object],
    depsgraph: bpy.types.Depsgraph | None = None,
) -> list[dict]:
    """Detect closed meshes wholly buried inside another closed mesh (excluding allowed telescoping joints)."""
    if depsgraph is None:
        depsgraph = bpy.context.evaluated_depsgraph_get()
    records = {}
    bounds = {}
    for obj in meshes:
        bvh, verts, is_closed = _evaluated_world_bvh(obj, depsgraph)
        if not verts:
            continue
        mn = Vector((min(v.x for v in verts), min(v.y for v in verts), min(v.z for v in verts)))
        mx = Vector((max(v.x for v in verts), max(v.y for v in verts), max(v.z for v in verts)))
        records[obj.name] = (obj, bvh, verts, is_closed)
        bounds[obj.name] = (mn, mx)

    violations: list[dict] = []
    names = sorted(records.keys())
    for na in names:
        oa, bvha, verts_a, _ = records[na]
        mn_a, mx_a = bounds[na]
        center_a = (mn_a + mx_a) * 0.5
        for nb in names:
            if na == nb or _is_allowed_containment_pair(na, nb):
                continue
            ob, bvhb, _, closed_b = records[nb]
            if not closed_b:
                continue
            mn_b, mx_b = bounds[nb]
            eps = 2e-3
            if not (
                mn_a.x >= mn_b.x - eps
                and mn_a.y >= mn_b.y - eps
                and mn_a.z >= mn_b.z - eps
                and mx_a.x <= mx_b.x + eps
                and mx_a.y <= mx_b.y + eps
                and mx_a.z <= mx_b.z + eps
            ):
                continue
            if not _point_inside_closed_bvh(center_a, bvhb):
                continue
            sample_verts = verts_a if len(verts_a) <= 24 else verts_a[:: max(1, len(verts_a) // 24)]
            all_inside = True
            for v in sample_verts:
                inset_pt = v.lerp(center_a, 0.05)
                if not _point_inside_closed_bvh(inset_pt, bvhb):
                    all_inside = False
                    break
            if all_inside:
                violations.append({"inner": na, "outer": nb})
    return violations


def audit_connectivity(
    meshes: Sequence[bpy.types.Object],
    root_name: str,
    max_gap: float = 0.0085,
) -> list[str]:
    """BFS mesh connectivity from `root_name` using BVHTree overlap / surface distance."""
    deps = bpy.context.evaluated_depsgraph_get()
    bvhs = {}
    for obj in meshes:
        bvh, _, _ = _evaluated_world_bvh(obj, deps)
        bvhs[obj.name] = bvh

    adj: dict[str, list[str]] = {o.name: [] for o in meshes}
    for i, oa in enumerate(meshes):
        for j in range(i + 1, len(meshes)):
            ob = meshes[j]
            d = _min_distance_between_meshes(oa, bvhs[oa.name], ob, bvhs[ob.name], deps)
            if d <= max_gap:
                adj[oa.name].append(ob.name)
                adj[ob.name].append(oa.name)

    visited: set[str] = set()
    stack = [root_name] if root_name in adj else []
    while stack:
        cur = stack.pop()
        if cur in visited:
            continue
        visited.add(cur)
        for nxt in adj[cur]:
            if nxt not in visited:
                stack.append(nxt)
    return sorted(o.name for o in meshes if o.name not in visited)


def audit_mech_walk_frames(
    unit_name: str,
    armature: bpy.types.Object | None,
    action_name: str | None = None,
    clearance_pairs: Sequence[tuple[str, str, float]] = (),
    foot_prefixes: Sequence[str] = ("FootBase", "FootPad", "FootTread", "ToeCenter", "HeelClaw"),
) -> dict:
    """Sweep frames across Action_Walk (40 frames) and verify joint clearances and floor bounds."""
    if armature is None or not armature.animation_data:
        return {"checked_frames": 0, "min_foot_z": 0.0, "clearance_violations": [], "floor_violations": []}

    act = bpy.data.actions.get(action_name or f"{unit_name}_Action_Walk")
    if act is None:
        return {"checked_frames": 0, "min_foot_z": 0.0, "clearance_violations": [], "floor_violations": []}

    orig_action = armature.animation_data.action
    orig_frame = bpy.context.scene.frame_current
    armature.animation_data.action = act
    f_start = int(round(act.frame_range[0]))
    f_end = int(round(act.frame_range[1]))

    clearance_violations: list[dict] = []
    floor_violations: list[dict] = []
    min_foot_z = 999.0

    try:
        for frame in range(f_start, f_end + 1):
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            deps = bpy.context.evaluated_depsgraph_get()

            for obj in _collect_meshes(unit_name):
                if any(tok in obj.name for tok in foot_prefixes):
                    ev = obj.evaluated_get(deps)
                    me = ev.to_mesh()
                    try:
                        mw = obj.matrix_world
                        z_vals = [(mw @ v.co).z for v in me.vertices]
                        if z_vals:
                            lo_z = min(z_vals)
                            min_foot_z = min(min_foot_z, lo_z)
                            if lo_z < -0.35:
                                floor_violations.append({"frame": frame, "object": obj.name, "min_z": round(lo_z, 4)})
                    finally:
                        ev.to_mesh_clear()

            for name_a, name_b, min_req in clearance_pairs:
                oa = bpy.data.objects.get(name_a)
                ob = bpy.data.objects.get(name_b)
                if oa is None or ob is None:
                    continue
                bvha, _, _ = _evaluated_world_bvh(oa, deps)
                bvhb, _, _ = _evaluated_world_bvh(ob, deps)
                dist = _min_distance_between_meshes(oa, bvha, ob, bvhb, deps)
                if dist < min_req:
                    clearance_violations.append(
                        {"frame": frame, "pair": [name_a, name_b], "distance": round(dist, 5), "required": min_req}
                    )
    finally:
        armature.animation_data.action = orig_action
        bpy.context.scene.frame_set(orig_frame)
        bpy.context.view_layer.update()

    return {
        "checked_frames": max(0, f_end - f_start + 1),
        "min_foot_z": round(min_foot_z if min_foot_z < 900.0 else 0.0, 4),
        "clearance_violations": clearance_violations,
        "floor_violations": floor_violations,
    }


def _force_single_bone_weight(obj: bpy.types.Object, bone_name: str, armature: bpy.types.Object) -> None:
    if obj is None or obj.type != "MESH" or armature is None:
        return
    if bone_name not in armature.data.bones:
        return
    obj.vertex_groups.clear()
    vg = obj.vertex_groups.new(name=bone_name)
    v_indices = [v.index for v in obj.data.vertices]
    if v_indices:
        vg.add(v_indices, 1.0, "REPLACE")
    has_mod = any(m.type == "ARMATURE" and m.object == armature for m in obj.modifiers)
    if not has_mod:
        for m in list(obj.modifiers):
            if m.type == "ARMATURE":
                obj.modifiers.remove(m)
        mod = obj.modifiers.new(name="Armature", type="ARMATURE")
        mod.object = armature
    if obj.parent != armature:
        mw = obj.matrix_world.copy()
        obj.parent = armature
        obj.matrix_world = mw


def _apply_exact_bone_overrides(
    meshes: Sequence[bpy.types.Object],
    armature: bpy.types.Object,
    rules: Sequence[tuple[str, str]],
) -> None:
    """Apply substring-to-bone rigid 1.0 weight rules after generic proximity weighting."""
    for obj in meshes:
        for substr, bone in rules:
            if substr in obj.name:
                _force_single_bone_weight(obj, bone, armature)
                break


def _finalize_mesh_standards(
    meshes: Sequence[bpy.types.Object],
    unit_name: str,
    optimize_forsyth: bool = True,
) -> None:
    """Ensure cleanup, UVMap, Color RGBA, single material, and optional Forsyth ordering."""
    for obj in meshes:
        cleanup_mesh_topology(obj.data)
        ensure_uv_and_color_layers(obj.data, force_smart_uv=False)
        if len(obj.data.materials) != 1:
            role = "Gunmetal"
            cur_name = obj.data.materials[0].name if obj.data.materials else ""
            for r in ("PrimaryArmor", "LightArmor", "Warning", "Emissive", "Tread", "WheelRim"):
                if f"_{r}" in cur_name:
                    role = r
                    break
            ensure_single_material(obj, resolve_unit_material(unit_name, role))
        if optimize_forsyth and len(obj.data.polygons) > 0:
            mga.apply_forsyth_cache_optimize_bpy(obj, cache_size=32)
    rmk.bake_advanced_vertex_shading(
        meshes,
        wear_level="field_tested",
        structural_wear_mask=True,
    )


def _add_walker_armor_plate(
    name: str,
    size: tuple[float, float, float],
    center: tuple[float, float, float],
    host: bpy.types.Object | None,
    mat: bpy.types.Material,
    *,
    bevel: float = 0.012,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
    clearance: float = -0.008,
    seat_downward: bool = True,
    recipe_tag: str = "walker_l1_armor_plate",
) -> bpy.types.Object:
    """Create or rebuild a beveled walker/factory armor plate seated cleanly onto host."""
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
    _tag_object(obj, recipe_tag)
    if host is not None:
        if seat_downward:
            seat_object_downward_to_target(obj, [host], clearance=clearance, max_drop=0.6)
        else:
            snap_object_to_nearest(obj, [host], embed=abs(clearance), max_dist=0.4)
    return obj


def _validate_rigid_weights_1_0(meshes: Sequence[bpy.types.Object], armature: bpy.types.Object | None) -> list[str]:
    if armature is None:
        return []
    errors: list[str] = []
    valid_bones = {b.name for b in armature.data.bones}
    for obj in meshes:
        if not obj.vertex_groups:
            errors.append(f"{obj.name}: no vertex groups")
            continue
        vg_map = {vg.index: vg.name for vg in obj.vertex_groups}
        for v in obj.data.vertices:
            if len(v.groups) != 1:
                errors.append(f"{obj.name}: vertex {v.index} has {len(v.groups)} groups")
                break
            g = v.groups[0]
            bname = vg_map.get(g.group, "")
            if bname not in valid_bones:
                errors.append(f"{obj.name}: unknown bone {bname}")
                break
            if abs(g.weight - 1.0) > 1e-4:
                errors.append(f"{obj.name}: non-rigid weight {g.weight:.4f}")
                break
    return errors


# ==============================================================================
# 1. IroncladWalker v2
# ==============================================================================

def apply_ironclad_walker_v2(
    unit_name: str = "IroncladWalker",
    armature: bpy.types.Object | None = None,
) -> dict:
    """Apply Exact Boolean details, surface seating, containment fixes, and 40f clearance on IroncladWalker."""
    _remove_duplicate_suffix_objects()
    if armature is None:
        armature = bpy.data.objects.get(f"{unit_name}_Armature")

    from ironclad_damper_clearance_recipe import cut_ironclad_damper_clearance
    from ironclad_actuator_support_recipe import add_ironclad_actuator_supports
    from ironclad_hatch_hardflow_recipe import apply_ironclad_hatch_fit

    cut_ironclad_damper_clearance()
    remove_objects_matching(
        lambda name: name.startswith(
            (
                f"{unit_name}Leg_HipActuator_L_TipStay",
                f"{unit_name}Leg_HipActuator_R_TipStay",
            )
        )
    )
    gunmetal = resolve_unit_material(unit_name, "Gunmetal")
    supports = add_ironclad_actuator_supports(gunmetal)
    for s in supports:
        _tag_object(s, "ironclad_actuator_support")
    apply_ironclad_hatch_fit()

    chest = bpy.data.objects.get(f"{unit_name}Armor_ChestPlate")
    reactor = bpy.data.objects.get(f"{unit_name}Armor_RearPowerCell")
    sh_l = bpy.data.objects.get(f"{unit_name}Armor_ShoulderGuard_L")
    sh_r = bpy.data.objects.get(f"{unit_name}Armor_ShoulderGuard_R")
    hatch_plate = bpy.data.objects.get(f"{unit_name}Armor_AccessHatch_HatchPlate")
    dog_latch = bpy.data.objects.get(f"{unit_name}Armor_AccessHatch_DogLatch")
    visor_housing = bpy.data.objects.get(f"{unit_name}Sensor_Visor_Housing")

    # 1. Seat DogLatch cleanly onto HatchPlate
    if dog_latch is not None and hatch_plate is not None:
        seat_object_downward_to_target(dog_latch, [hatch_plate], clearance=-0.014, max_drop=0.4)
        _tag_object(dog_latch, "ironclad_latch_seat")

    # 2. Snap ShinAccent_L/R flush onto ShinArmor_L/R
    for side in ("L", "R"):
        accent = bpy.data.objects.get(f"{unit_name}Leg_ShinAccent_{side}")
        shin_arm = bpy.data.objects.get(f"{unit_name}Leg_ShinArmor_{side}")
        if accent is not None and shin_arm is not None:
            snap_object_to_nearest(accent, [shin_arm], embed=0.015, max_dist=0.5)
            _tag_object(accent, f"ironclad_shin_accent_{side}")

    # 3. Relocate buried GreebleHatch/Handle_L/R from Torso interior onto ShoulderGuard tops
    for side, sh in (("L", sh_l), ("R", sh_r)):
        ghatch = bpy.data.objects.get(f"{unit_name}_GreebleHatch_{side}")
        ghandle = bpy.data.objects.get(f"{unit_name}_GreebleHandle_{side}")
        cluster = [o for o in (ghatch, ghandle) if o is not None]
        if cluster and sh is not None:
            _, _, _, _, _, sh_z1 = world_bounds(sh)
            target_x = -0.68 if side == "L" else 0.68
            cur_c = world_center(ghatch)
            _translate_cluster(cluster, Vector((target_x - cur_c.x, -cur_c.y, (sh_z1 + 0.03) - cur_c.z)))
            seat_cluster_downward(cluster, [sh], clearance=-0.014, max_drop=0.5)
            for o in cluster:
                _tag_object(o, f"ironclad_shoulder_hatch_{side}")

    # 4. Relocate buried GreebleLouver cluster from Torso interior onto RearPowerCell top deck
    louver_frame = bpy.data.objects.get(f"{unit_name}_GreebleLouverFrame")
    slats = [bpy.data.objects.get(f"{unit_name}_GreebleLouverSlat_{i}") for i in range(1, 5)]
    louver_cluster = [o for o in [louver_frame, *slats] if o is not None]
    if louver_cluster and reactor is not None:
        _, _, r_y0, r_y1, _, r_z1 = world_bounds(reactor)
        lf_c = world_center(louver_frame)
        target_y = (r_y0 + r_y1) * 0.5
        _translate_cluster(louver_cluster, Vector((-lf_c.x, target_y - lf_c.y, (r_z1 + 0.03) - lf_c.z)))
        seat_cluster_downward(louver_cluster, [reactor], clearance=-0.014, max_drop=0.5)
        for o in louver_cluster:
            _tag_object(o, "ironclad_reactor_louver")

    # 5. Exact Boolean armor pockets on ChestPlate, ShoulderGuards, RearPowerCell, and Visor clearance
    if chest is not None and chest.get(RECIPE_PROP) != "ironclad_chest_recess":
        for side_s, sx in (("L", -0.25), ("R", 0.25)):
            cut_recessed_service_pocket(
                chest,
                (sx, 0.505, 1.94),
                (0.18, 0.07, 0.14),
                recipe_tag="ironclad_chest_recess",
            )
    if visor_housing is not None and chest is not None:
        cut_visor_pocket_around_housing(chest, visor_housing, margin=0.008, recipe_tag="ironclad_visor_pocket")

    for side, sh in (("L", sh_l), ("R", sh_r)):
        if sh is not None and sh.get(RECIPE_PROP) != f"ironclad_shoulder_recess_{side}":
            sh_x0, sh_x1, _, _, _, _ = world_bounds(sh)
            outer_x = sh_x0 + 0.025 if side == "L" else sh_x1 - 0.025
            cut_recessed_service_pocket(
                sh,
                (outer_x, 0.0, 2.21),
                (0.08, 0.34, 0.16),
                recipe_tag=f"ironclad_shoulder_recess_{side}",
            )
    if reactor is not None and reactor.get(RECIPE_PROP) != "ironclad_reactor_recess":
        _, _, r_y0, _, _, _ = world_bounds(reactor)
        cut_recessed_service_pocket(
            reactor,
            (0.0, r_y0 + 0.02, 1.98),
            (0.40, 0.07, 0.28),
            recipe_tag="ironclad_reactor_recess",
        )

    # 5b. Level-1 RTS readability: LightArmor chest/shoulder plates & Warning chest strip
    l_armor = resolve_unit_material(unit_name, "LightArmor")
    w_armor = resolve_unit_material(unit_name, "Warning")
    _add_walker_armor_plate(
        f"{unit_name}Armor_ChestLightShield", (0.56, 0.06, 0.24), (0.0, 0.34, 2.02),
        chest, l_armor, bevel=0.010, seat_downward=False, recipe_tag="ironclad_l1_chest_shield",
    )
    _add_walker_armor_plate(
        f"{unit_name}Warning_ChestStrip", (0.42, 0.035, 0.06), (0.0, 0.36, 1.82),
        chest, w_armor, bevel=0.006, seat_downward=False, recipe_tag="ironclad_l1_warning_strip",
    )
    for side, sh in (("L", sh_l), ("R", sh_r)):
        sx = -1.0 if side == "L" else 1.0
        _add_walker_armor_plate(
            f"{unit_name}Armor_ShoulderCrown_{side}", (0.24, 0.36, 0.045), (sx * 0.56, 0.0, 2.36),
            sh, l_armor, bevel=0.008, recipe_tag="ironclad_l1_shoulder_crown",
        )

    # 6. Re-assign rigid 1.0 weights after Booleans if rigged
    meshes = _collect_meshes(unit_name)
    if armature is not None:
        rmk.assign_rigid_mesh_weights(meshes, prefix=unit_name, armature_obj=armature, leg_type="biped")
        _apply_exact_bone_overrides(
            meshes,
            armature,
            [
                ("ChestLightShield", "Bone_TurretYaw"),
                ("ChestStrip", "Bone_TurretYaw"),
                ("ShoulderCrown_", "Bone_TurretYaw"),
                ("HipActuator_L", "Bone_Thigh_L"),
                ("HipActuator_R", "Bone_Thigh_R"),
                ("AnkleDamper_L", "Bone_Shin_L"),
                ("AnkleDamper_R", "Bone_Shin_R"),
                ("ShinAccent_L", "Bone_Shin_L"),
                ("ShinAccent_R", "Bone_Shin_R"),
                ("FootAnkleSupport_L", "Bone_Foot_L"),
                ("FootAnkleSupport_R", "Bone_Foot_R"),
                ("Gatling_L_Barrel_", "Bone_BarrelRecoil"),
                ("Gatling_R_Barrel_", "Bone_BarrelRecoil"),
                ("MuzzleClamp", "Bone_BarrelRecoil"),
                ("Gatling_L_", "Bone_TurretPitch"),
                ("Gatling_R_", "Bone_TurretPitch"),
                ("GatlingBracer_", "Bone_TurretPitch"),
                ("GatlingFeedChute_", "Bone_TurretPitch"),
                ("Pintle", "Bone_TurretPitch"),
                ("Sensor_Visor_", "Bone_TurretYaw"),
                ("TrunnionBracket_", "Bone_TurretYaw"),
                ("Greeble", "Bone_Pelvis"),
                ("WeldSeam_", "Bone_Pelvis"),
            ],
        )

    _finalize_mesh_standards(meshes, unit_name)
    return {"unit": unit_name, "meshes": len(meshes)}


# ==============================================================================
# 2. AtlasCraneMech v2
# ==============================================================================

def apply_atlas_crane_mech_v2(
    unit_name: str = "AtlasCraneMech",
    armature: bpy.types.Object | None = None,
) -> dict:
    """Apply Exact Boolean counterweight/boom sculpting, hatch/stay seating, and rigid reweighting on AtlasCraneMech."""
    _remove_duplicate_suffix_objects()
    if armature is None:
        armature = bpy.data.objects.get(f"{unit_name}_Armature")

    remove_objects_matching(
        lambda name: name.startswith(
            (
                f"{unit_name}Armor_LiftingLug_",
                f"{unit_name}Armor_LugWeldPad_",
                f"{unit_name}Armor_Gusset_",
            )
        )
    )

    torso = bpy.data.objects.get(f"{unit_name}Armor_IndustrialTorso")
    reactor = bpy.data.objects.get(f"{unit_name}Armor_DorsalReactorDrum")
    base_shell = bpy.data.objects.get(f"{unit_name}Turret_BaseShell")
    rear_bustle = bpy.data.objects.get(f"{unit_name}Turret_RearBustle")
    crane_jib = bpy.data.objects.get(f"{unit_name}Joint_ShoulderCraneJib")
    visor_housing = bpy.data.objects.get(f"{unit_name}Sensor_Visor_Housing")

    # 1. Integrate shortened-Y AccessHatch on turret roof Z=2.4825m clearing MantletHousing
    hatch_names = (
        f"{unit_name}Armor_AccessHatch_CoamingRim",
        f"{unit_name}Armor_AccessHatch_HatchPlate",
        f"{unit_name}Armor_AccessHatch_HingeBar",
        f"{unit_name}Armor_AccessHatch_DogLatch",
    )
    hatch_objs = [bpy.data.objects.get(n) for n in hatch_names if bpy.data.objects.get(n) is not None]
    if hatch_objs and base_shell is not None:
        mantlet = bpy.data.objects.get(f"{unit_name}Turret_MantletHousing")
        coaming = hatch_objs[0]
        _, _, _, h_y1, _, _ = world_bounds(coaming)
        if mantlet is not None:
            _, _, m_y0, _, _, _ = world_bounds(mantlet)
            desired_max_y = m_y0 - 0.028
            if h_y1 > desired_max_y:
                _translate_cluster(hatch_objs, Vector((0.0, desired_max_y - h_y1, 0.0)))
        seat_cluster_downward(hatch_objs, [base_shell], clearance=-0.014, max_drop=0.5)
        for o in hatch_objs:
            _tag_object(o, "atlas_hatch_fit")
        hatch_plate = bpy.data.objects.get(f"{unit_name}Armor_AccessHatch_HatchPlate")
        dog_latch = bpy.data.objects.get(f"{unit_name}Armor_AccessHatch_DogLatch")
        if dog_latch is not None and hatch_plate is not None:
            seat_object_downward_to_target(dog_latch, [hatch_plate], clearance=-0.012, max_drop=0.4)

    # 2. Connect ShoulderMount_L/R, ArmBoom_L/R, ClawPincerUpper/Lower_L/R cleanly to torso/arms
    for side in ("L", "R"):
        sm = bpy.data.objects.get(f"{unit_name}Joint_ShoulderMount_{side}")
        boom = bpy.data.objects.get(f"{unit_name}Armor_ArmBoom_{side}")
        cu = bpy.data.objects.get(f"{unit_name}Armor_ClawPincerUpper_{side}")
        cl = bpy.data.objects.get(f"{unit_name}Armor_ClawPincerLower_{side}")
        if sm is not None and torso is not None:
            snap_object_to_nearest(sm, [torso], embed=0.018, max_dist=0.35)
            _tag_object(sm, f"atlas_shoulder_snap_{side}")
            pylon_name = f"{unit_name}Joint_ShoulderPylon_{side}"
            existing_pylon = bpy.data.objects.get(pylon_name)
            if existing_pylon is not None:
                bpy.data.objects.remove(existing_pylon, do_unlink=True)
            t_x0, t_x1, _, _, _, _ = world_bounds(torso)
            sm_c = world_center(sm)
            p_start = (t_x0 + 0.08 if side == "L" else t_x1 - 0.08, sm_c.y, sm_c.z)
            p_end = (sm_c.x, sm_c.y, sm_c.z)
            pylon = make_faceted_cylinder(
                pylon_name,
                p_start,
                p_end,
                0.065,
                resolve_unit_material(unit_name, "Gunmetal"),
                segments=12,
            )
            _tag_object(pylon, f"atlas_shoulder_pylon_{side}")
        if boom is not None and sm is not None:
            snap_object_to_nearest(boom, [sm], embed=0.018, max_dist=0.35)
            _tag_object(boom, f"atlas_arm_snap_{side}")
        for cname, claw in (("Upper", cu), ("Lower", cl)):
            if claw is not None and boom is not None:
                _stay_between(
                    f"{unit_name}Joint_WristKnuckle_{cname}_{side}",
                    claw,
                    boom,
                    unit_name,
                    role="Gunmetal",
                    radius=0.042,
                    direction=(0.0, -1.0, 0.0),
                    tag=f"atlas_wrist_knuckle_{side}",
                )

    # 3. Snap buried WeldSeam_TorsoL/R, WeldSeam_Breastplate, and ShinAccent_L/R onto exterior surfaces
    for sname in (f"{unit_name}Armor_WeldSeam_TorsoL", f"{unit_name}Armor_WeldSeam_TorsoR"):
        seam = bpy.data.objects.get(sname)
        if seam is not None and torso is not None:
            t_x0, t_x1, _, _, _, _ = world_bounds(torso)
            s_c = world_center(seam)
            target_x = t_x0 - 0.004 if "TorsoL" in sname else t_x1 + 0.004
            _translate_cluster([seam], Vector((target_x - s_c.x, 0.0, 0.0)))
            snap_object_to_nearest(seam, [torso], embed=0.004, max_dist=0.25)
            _tag_object(seam, "atlas_weldseam_seat")

    bp_seam = bpy.data.objects.get(f"{unit_name}Armor_WeldSeam_Breastplate")
    if bp_seam is not None and torso is not None:
        canopy = bpy.data.objects.get(f"{unit_name}Armor_RollCageCanopy")
        _, _, _, t_y1, _, _ = world_bounds(torso)
        front_y = max(t_y1, world_bounds(canopy)[3]) if canopy is not None else t_y1
        bc = world_center(bp_seam)
        _translate_cluster([bp_seam], Vector((0.0, (front_y + 0.004) - bc.y, 0.0)))
        snap_object_to_nearest(bp_seam, [canopy or torso, torso], embed=0.004, max_dist=0.35)
        _tag_object(bp_seam, "atlas_breastplate_seam")

    for side in ("L", "R"):
        accent = bpy.data.objects.get(f"{unit_name}Leg_ShinAccent_{side}")
        shin = bpy.data.objects.get(f"{unit_name}Leg_ShinArmor_{side}")
        if accent is not None and shin is not None:
            _, _, _, s_y1, _, _ = world_bounds(shin)
            ac = world_center(accent)
            _translate_cluster([accent], Vector((0.0, (s_y1 + 0.015) - ac.y, 0.0)))
            _snap_cluster_to_surface([accent], accent, [shin], fixed_embed=0.0)
            _tag_object(accent, f"atlas_shin_accent_{side}")

    # Seat TrunnionBracket clusters cleanly onto Turret_BaseShell / MantletHousing
    for side_tok in ("_L", "_R"):
        tb_objs = [
            o
            for o in bpy.data.objects
            if o.type == "MESH"
            and o.name.startswith(f"{unit_name}Armor_TrunnionBracket_")
            and o.name.endswith(side_tok)
        ]
        if tb_objs and base_shell is not None:
            mantlet = bpy.data.objects.get(f"{unit_name}Turret_MantletHousing")
            targets = [o for o in (mantlet, base_shell) if o is not None]
            seat_cluster_downward(tb_objs, [base_shell], clearance=-0.015, max_drop=0.6)
            cheek = bpy.data.objects.get(f"{unit_name}Armor_TrunnionBracket_CheekBlock{side_tok}")
            if cheek is not None and targets:
                snap_object_to_nearest(cheek, targets, embed=0.014, max_dist=0.45)
            for o in tb_objs:
                _tag_object(o, f"atlas_trunnion_bracket{side_tok}")

    # Lift crane/boom hydraulics onto Turret_BaseShell exterior, shift leg hoses outward, and move WhipAntenna onto reactor rear
    for side in ("L", "R"):
        boom_cyls = [
            o
            for o in bpy.data.objects
            if o.type == "MESH" and o.name.startswith(unit_name) and f"BoomLiftCylinder_{side}" in o.name
        ]
        if boom_cyls and _needs_tag(boom_cyls, f"atlas_crane_hyd_{side}"):
            _translate_cluster(boom_cyls, Vector((-0.08 if side == "L" else 0.08, 0.0, 0.52)))
            for o in boom_cyls:
                _tag_object(o, f"atlas_crane_hyd_{side}")

        crane_hoses = [
            o
            for o in bpy.data.objects
            if o.type == "MESH" and o.name.startswith(unit_name) and f"CraneHydraulicHose_{side}" in o.name
        ]
        if crane_hoses and _needs_tag(crane_hoses, f"atlas_crane_hose_{side}"):
            _translate_cluster(crane_hoses, Vector((-0.14 if side == "L" else 0.14, 0.0, 0.56)))
            if base_shell is not None:
                c1 = next((o for o in crane_hoses if o.name.endswith("HoseCoupler_1")), None)
                if c1 is not None:
                    _snap_cluster_to_surface(crane_hoses, c1, [base_shell], fixed_embed=0.006)
            for o in crane_hoses:
                _tag_object(o, f"atlas_crane_hose_{side}")

        leg_hoses = [
            o
            for o in bpy.data.objects
            if o.type == "MESH" and o.name.startswith(f"{unit_name}Leg_{side}_HydraulicHose_")
        ]
        if leg_hoses and _needs_tag(leg_hoses, f"atlas_leg_hose_{side}"):
            dx = -0.22 if side == "L" else 0.22
            _translate_cluster(leg_hoses, Vector((dx, 0.0, 0.0)))
            c1 = bpy.data.objects.get(f"{unit_name}Leg_{side}_HydraulicHose_HoseCoupler_1")
            if c1 is not None and torso is not None:
                snap_object_to_nearest(c1, [torso], embed=0.008, max_dist=0.45)
            for o in leg_hoses:
                _tag_object(o, f"atlas_leg_hose_{side}")

    whip_objs = [
        o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith(f"{unit_name}Sensor_WhipAntenna_")
    ]
    if whip_objs and reactor is not None:
        _, rx1, _, _, _, rz1 = world_bounds(reactor)
        bracket = bpy.data.objects.get(f"{unit_name}Sensor_WhipAntenna_Bracket")
        ref_pt = world_center(bracket) if bracket is not None else world_center(whip_objs[0])
        _translate_cluster(whip_objs, Vector(((rx1 + 0.02) - ref_pt.x, 0.0, (rz1 - 0.06) - ref_pt.z)))
        if bracket is not None:
            snap_object_to_nearest(bracket, [reactor], embed=0.008, max_dist=0.35)
        for o in whip_objs:
            _tag_object(o, "atlas_whip_antenna")

    # 4. Relocate buried GreebleHatch/Handle_L/R onto exterior torso flanks & Louver onto ReactorDrum rear
    for side in ("L", "R"):
        gh = bpy.data.objects.get(f"{unit_name}_GreebleHatch_{side}")
        ghdl = bpy.data.objects.get(f"{unit_name}_GreebleHandle_{side}")
        cluster = [o for o in (gh, ghdl) if o is not None]
        if cluster and torso is not None:
            t_x0, t_x1, _, _, _, _ = world_bounds(torso)
            rot_angle = -1.5707963 if side == "L" else 1.5707963
            for o in cluster:
                o.rotation_euler = (0.0, rot_angle, 0.0)
            bpy.context.view_layer.update()
            cur_c = world_center(gh)
            target_x = (t_x0 - 0.015) if side == "L" else (t_x1 + 0.015)
            _translate_cluster(cluster, Vector((target_x - cur_c.x, -0.18 - cur_c.y, 1.72 - cur_c.z)))
            snap_object_to_nearest(gh, [torso], embed=0.012, max_dist=0.35)
            _tag_object(gh, f"atlas_flank_hatch_{side}")
            if ghdl is not None:
                snap_object_to_nearest(ghdl, [gh], embed=0.010, max_dist=0.25)
                _tag_object(ghdl, f"atlas_flank_handle_{side}")

    louver_frame = bpy.data.objects.get(f"{unit_name}_GreebleLouverFrame")
    slats = [bpy.data.objects.get(f"{unit_name}_GreebleLouverSlat_{i}") for i in range(1, 5)]
    louver_cluster = [o for o in [louver_frame, *slats] if o is not None]
    if louver_cluster and reactor is not None:
        for o in louver_cluster:
            o.rotation_euler = (1.5707963, 0.0, 0.0)
        bpy.context.view_layer.update()
        _, _, r_y0, _, _, _ = world_bounds(reactor)
        lf_c = world_center(louver_frame)
        _translate_cluster(louver_cluster, Vector((-lf_c.x, (r_y0 - 0.01) - lf_c.y, 1.96 - lf_c.z)))
        snap_object_to_nearest(louver_frame, [reactor], embed=0.012, max_dist=0.3)
        _tag_object(louver_frame, "atlas_reactor_louver")
        for slat in slats:
            if slat is not None:
                snap_object_to_nearest(slat, [louver_frame], embed=0.010, max_dist=0.2)
                _tag_object(slat, "atlas_reactor_louver_slat")

    # 5. Exact Boolean sculpting on counterweight (RearBustle), crane boom (ShoulderCraneJib), and visor
    if rear_bustle is not None and rear_bustle.get(RECIPE_PROP) != "atlas_counterweight_cuts":
        rb_x0, rb_x1, rb_y0, rb_y1, rb_z0, rb_z1 = world_bounds(rear_bustle)
        rb_cz = (rb_z0 + rb_z1) * 0.5
        rb_cy = (rb_y0 + rb_y1) * 0.5
        cut_recessed_service_pocket(
            rear_bustle,
            (0.0, rb_y0 + 0.02, rb_cz),
            (0.46, 0.08, 0.14),
            recipe_tag="atlas_counterweight_cuts",
        )
        for sx in (rb_x0 + 0.02, rb_x1 - 0.02):
            cut_recessed_service_pocket(
                rear_bustle,
                (sx, rb_cy, rb_cz),
                (0.07, 0.26, 0.12),
                recipe_tag="atlas_counterweight_cuts",
            )

    if crane_jib is not None and crane_jib.get(RECIPE_PROP) != "atlas_jib_lightening_cuts":
        jb_x0, jb_x1, _, _, jb_z0, jb_z1 = world_bounds(crane_jib)
        cx = (jb_x0 + jb_x1) * 0.5
        cz = (jb_z0 + jb_z1) * 0.5
        for py in (0.20, 0.56):
            cut_recessed_service_pocket(
                crane_jib,
                (cx, py, cz),
                ((jb_x1 - jb_x0) + 0.04, 0.18, 0.13),
                recipe_tag="atlas_jib_lightening_cuts",
            )

    if visor_housing is not None and torso is not None:
        cut_visor_pocket_around_housing(torso, visor_housing, margin=0.008, recipe_tag="atlas_visor_pocket")

    # 5b. Level-1 RTS readability: LightArmor torso crown, bustle shield & Warning boom trim
    l_armor = resolve_unit_material(unit_name, "LightArmor")
    w_armor = resolve_unit_material(unit_name, "Warning")
    _add_walker_armor_plate(
        f"{unit_name}Armor_TorsoCrownPlate", (0.68, 0.52, 0.045), (0.0, 0.05, 2.32),
        torso, l_armor, bevel=0.010, recipe_tag="atlas_l1_torso_crown",
    )
    if rear_bustle is not None:
        _, _, rb_y0, rb_y1, _, rb_z1 = world_bounds(rear_bustle)
        _add_walker_armor_plate(
            f"{unit_name}Armor_BustleShield", (0.56, 0.24, 0.045), (0.0, (rb_y0 + rb_y1) * 0.5, rb_z1 + 0.04),
            rear_bustle, l_armor, bevel=0.010, seat_downward=True, recipe_tag="atlas_l1_bustle_shield",
        )
    if crane_jib is not None:
        jb_c = world_center(crane_jib)
        _, _, _, _, _, jb_z1 = world_bounds(crane_jib)
        _add_walker_armor_plate(
            f"{unit_name}Warning_BoomTrim", (0.14, 0.44, 0.032), (jb_c.x, 0.38, jb_z1 + 0.01),
            crane_jib, w_armor, bevel=0.006, recipe_tag="atlas_l1_warning_boom",
        )

    # 6. Prepare/preserve Atlas ankle damper clearance + foot/thigh stays + rigid 1.0 reweight
    if armature is not None:
        from atlas_damper_clearance_recipe import prepare_atlas_damper_clearance
        prepare_atlas_damper_clearance(armature)
        meshes = _collect_meshes(unit_name)
        rmk.assign_rigid_mesh_weights(meshes, prefix=unit_name, armature_obj=armature, leg_type="biped")
        _apply_exact_bone_overrides(
            meshes,
            armature,
            [
                ("TorsoCrownPlate", "Bone_TurretYaw"),
                ("BustleShield", "Bone_TurretYaw"),
                ("BoomTrim", "Bone_Pelvis"),
                ("FootAnkleSupport_L", "Bone_Foot_L"),
                ("FootAnkleSupport_R", "Bone_Foot_R"),
                ("HeelClaw_L", "Bone_Foot_L"),
                ("HeelClaw_R", "Bone_Foot_R"),
                ("FootBase_L", "Bone_Foot_L"),
                ("FootBase_R", "Bone_Foot_R"),
                ("ToeCenter_L", "Bone_Foot_L"),
                ("ToeCenter_R", "Bone_Foot_R"),
                ("ToeOutrigger_L", "Bone_Foot_L"),
                ("ToeOutrigger_R", "Bone_Foot_R"),
                ("AnkleJoint_L", "Bone_Foot_L"),
                ("AnkleJoint_R", "Bone_Foot_R"),
                ("AnkleClevis_L", "Bone_Foot_L"),
                ("AnkleClevis_R", "Bone_Foot_R"),
                ("HipActuator_L", "Bone_Thigh_L"),
                ("HipActuator_R", "Bone_Thigh_R"),
                ("ThighPiston_L", "Bone_Thigh_L"),
                ("ThighPiston_R", "Bone_Thigh_R"),
                ("ThighCylinder_L", "Bone_Thigh_L"),
                ("ThighCylinder_R", "Bone_Thigh_R"),
                ("Piston_Leg_L_", "Bone_Thigh_L"),
                ("Piston_Leg_R_", "Bone_Thigh_R"),
                ("AnkleDamper_L_", "Bone_Shin_L"),
                ("AnkleDamper_R_", "Bone_Shin_R"),
                ("ShinAccent_L", "Bone_Shin_L"),
                ("ShinAccent_R", "Bone_Shin_R"),
                ("Leg_L_HydraulicHose_", "Bone_Shin_L"),
                ("Leg_R_HydraulicHose_", "Bone_Shin_R"),
                ("HipFlange_", "Bone_Hull"),
                ("DualGun_Breech_", "Bone_TurretPitch"),
                ("DualGun_MantletBlock", "Bone_TurretPitch"),
                ("Turret_MantletHousing", "Bone_TurretPitch"),
                ("DualGun_ThermalSleeve_", "Bone_BarrelRecoil"),
                ("DualGun_EvacuatorStripe_", "Bone_BarrelRecoil"),
                ("DualGun_BoreEvacuator_", "Bone_BarrelRecoil"),
                ("DualGun_BarrelTube_", "Bone_BarrelRecoil"),
                ("DualGun_Muzzle", "Bone_BarrelRecoil"),
                ("AccessHatch_", "Bone_TurretYaw"),
                ("TrunnionBracket_", "Bone_TurretYaw"),
                ("BoomLiftCylinder_", "Bone_TurretYaw"),
                ("CraneHydraulicHose_", "Bone_TurretYaw"),
                ("Sensor_Visor_", "Bone_TurretYaw"),
                ("Turret_", "Bone_TurretYaw"),
                ("ShoulderPylon_", "Bone_Pelvis"),
                ("ShoulderMount_", "Bone_Pelvis"),
                ("ArmBoom_", "Bone_Pelvis"),
                ("ClawPincer", "Bone_Pelvis"),
                ("WristKnuckle_", "Bone_Pelvis"),
                ("Greeble", "Bone_Hull"),
                ("WeldSeam_", "Bone_Hull"),
                ("WhipAntenna_", "Bone_Pelvis"),
            ],
        )
    else:
        meshes = _collect_meshes(unit_name)

    _finalize_mesh_standards(meshes, unit_name)
    return {"unit": unit_name, "meshes": len(meshes)}


# ==============================================================================
# 3. BastionSiegeMech v2
# ==============================================================================

def _fix_bastion_turret_pitch_pivot(armature: bpy.types.Object) -> None:
    """Align Bone_TurretPitch head to Socket_TurretPitch (0, 0.35, 1.51) and Bone_BarrelRecoil along +Y."""
    if armature is None or armature.type != "ARMATURE":
        return
    socket_pitch = bpy.data.objects.get("Socket_TurretPitch")
    pivot_world = socket_pitch.matrix_world.translation.copy() if socket_pitch else Vector((0.0, 0.35, 1.51))
    inv = armature.matrix_world.inverted()
    local_pivot = inv @ pivot_world

    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode="EDIT")
    try:
        eb = armature.data.edit_bones
        b_pitch = eb.get("Bone_TurretPitch")
        b_recoil = eb.get("Bone_BarrelRecoil")
        if b_pitch is not None:
            b_pitch.head = local_pivot
            b_pitch.tail = local_pivot + Vector((0.0, 0.30, 0.0))
        if b_recoil is not None:
            b_recoil.head = local_pivot + Vector((0.0, 0.30, 0.0))
            b_recoil.tail = local_pivot + Vector((0.0, 0.85, 0.0))
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.update()


def apply_bastion_siege_mech_v2(
    unit_name: str = "BastionSiegeMech",
    armature: bpy.types.Object | None = None,
) -> dict:
    """Resolve Bastion disconnected clusters, TurretPitch pivot, recuperators, carapace dampers, and Exact Boolean glacis/mantlet cuts."""
    _remove_duplicate_suffix_objects()
    if armature is None:
        armature = bpy.data.objects.get(f"{unit_name}_Armature")

    remove_objects_matching(
        lambda name: name.startswith(
            (
                "BastionArmor_TurretRingGuard_",
                "BastionArmor_LiftingLug_",
                "BastionArmor_LugWeldPad_",
                "BastionArmor_Gusset_",
                f"{unit_name}Armor_LiftingLug_",
                f"{unit_name}Armor_LugWeldPad_",
                f"{unit_name}Armor_Gusset_",
            )
        )
    )

    if armature is not None:
        _fix_bastion_turret_pitch_pivot(armature)

    hull = bpy.data.objects.get(f"{unit_name}Armor_Hull")
    upper_deck = bpy.data.objects.get(f"{unit_name}Armor_UpperDeck")
    base_shell = bpy.data.objects.get(f"{unit_name}Turret_BaseShell")
    rear_bustle = bpy.data.objects.get(f"{unit_name}Turret_RearBustle")
    mantlet = bpy.data.objects.get(f"{unit_name}Turret_MantletHousing")
    visor_housing = bpy.data.objects.get(f"{unit_name}Sensor_Visor_Housing")

    # 2. Seat BastionArmor_AccessHatch_* inside turret roof Z=1.60m (Y in [-0.54, -0.14])
    hatch_names = (
        "BastionArmor_AccessHatch_CoamingRim",
        "BastionArmor_AccessHatch_HatchPlate",
        "BastionArmor_AccessHatch_HingeBar",
        "BastionArmor_AccessHatch_DogLatch",
    )
    hatch_objs = [bpy.data.objects.get(n) for n in hatch_names if bpy.data.objects.get(n) is not None]
    if hatch_objs and base_shell is not None:
        _, _, _, _, _, bs_z1 = world_bounds(base_shell)
        coaming = hatch_objs[0]
        _, _, c_y0, c_y1, _, _ = world_bounds(coaming)
        cur_y_len = c_y1 - c_y0
        if cur_y_len > 0.40:
            scale_y = 0.38 / cur_y_len
            c_mid_y = (c_y0 + c_y1) * 0.5
            for ho in hatch_objs:
                for v in ho.data.vertices:
                    w = ho.matrix_world @ v.co
                    w.y = c_mid_y + (w.y - c_mid_y) * scale_y
                    v.co = ho.matrix_world.inverted() @ w
                ho.data.update()
            bpy.context.view_layer.update()
        c_c = world_center(coaming)
        _translate_cluster(hatch_objs, Vector((-c_c.x, -0.34 - c_c.y, (bs_z1 + 0.02) - c_c.z)))
        seat_cluster_downward(hatch_objs, [base_shell], clearance=-0.014, max_drop=1.5)
        for o in hatch_objs:
            _tag_object(o, "bastion_hatch_seat")
        hp = bpy.data.objects.get("BastionArmor_AccessHatch_HatchPlate")
        dl = bpy.data.objects.get("BastionArmor_AccessHatch_DogLatch")
        if dl is not None and hp is not None:
            seat_object_downward_to_target(dl, [hp], clearance=-0.012, max_drop=0.4)

    # 3. Seat BastionArmor_TrunnionBracket_L/R at turret trunnions (Y=0.35, Z=1.51), clearing MantletHousing
    for side in ("L", "R"):
        tb_objs = [
            o
            for o in (
                bpy.data.objects.get(f"BastionArmor_TrunnionBracket_{part}_{side}")
                for part in ("CheekBlock", "TrunnionPin", "RetentionCap", "MountFlange")
            )
            if o is not None
        ]
        if tb_objs and base_shell is not None:
            pin = bpy.data.objects.get(f"BastionArmor_TrunnionBracket_TrunnionPin_{side}")
            ref_c = world_center(pin or tb_objs[0])
            target_x = -0.575 if side == "L" else 0.575
            _translate_cluster(tb_objs, Vector((target_x - ref_c.x, 0.35 - ref_c.y, 1.51 - ref_c.z)))
            seat_cluster_downward(tb_objs, [base_shell], clearance=-0.014, max_drop=1.5)
            for o in tb_objs:
                _tag_object(o, f"bastion_trunnion_{side}")

    # 4. Reposition telescopic RecoilRecuperator_L/R in front of MantletHousing along barrels
    for side in ("L", "R"):
        recup_objs = [
            o
            for o in (
                bpy.data.objects.get(f"{unit_name}Turret_RecoilRecuperator_{side}_{part}")
                for part in (
                    "ClevisEye_Base",
                    "ClevisPin_Base",
                    "OuterBarrel",
                    "WiperSealCollar",
                    "PressureRing",
                    "InnerRamRod",
                    "ClevisEye_Tip",
                    "ClevisPin_Tip",
                )
            )
            if o is not None
        ]
        if recup_objs:
            base_eye = bpy.data.objects.get(f"{unit_name}Turret_RecoilRecuperator_{side}_ClevisEye_Base")
            if base_eye is not None:
                bc = world_center(base_eye)
                target_x = -0.35 if side == "L" else 0.35
                _translate_cluster(recup_objs, Vector((target_x - bc.x, 0.99 - bc.y, 1.48 - bc.z)))
                sleeve = bpy.data.objects.get(f"{unit_name}DualGun_ThermalSleeve_{side}")
                existing_base_stay = bpy.data.objects.get(f"{unit_name}Turret_RecuperatorBaseStay_{side}")
                if existing_base_stay is not None:
                    bpy.data.objects.remove(existing_base_stay, do_unlink=True)
                tip_eye = bpy.data.objects.get(f"{unit_name}Turret_RecoilRecuperator_{side}_ClevisEye_Tip")
                if tip_eye is not None and sleeve is not None:
                    _stay_between(
                        f"{unit_name}Turret_RecuperatorRodStay_{side}",
                        tip_eye,
                        sleeve,
                        unit_name,
                        role="Gunmetal",
                        radius=0.025,
                        direction=(1.0 if side == "L" else -1.0, 0.0, 0.0),
                        tag=f"bastion_recup_rod_stay_{side}",
                    )
                for o in recup_objs:
                    _tag_object(o, f"bastion_recup_{side}")

    # 5. Lower CarapaceDamper_L/R down onto UpperDeck/Hull and connect via hull stays (Bone_Hull)
    for side in ("L", "R"):
        d_objs = [
            o
            for o in (
                bpy.data.objects.get(f"{unit_name}Armor_CarapaceDamper_{side}_{part}")
                for part in (
                    "ClevisEye_Base",
                    "ClevisPin_Base",
                    "OuterBarrel",
                    "WiperSealCollar",
                    "PressureRing",
                    "InnerRamRod",
                    "ClevisEye_Tip",
                    "ClevisPin_Tip",
                )
            )
            if o is not None
        ]
        if d_objs and upper_deck is not None and hull is not None and _needs_tag(d_objs, f"bastion_carapace_damper_{side}"):
            dx = -0.05 if side == "L" else 0.05
            _translate_cluster(d_objs, Vector((dx, 0.0, -0.44)))
            base_eye = bpy.data.objects.get(f"{unit_name}Armor_CarapaceDamper_{side}_ClevisEye_Base")
            tip_eye = bpy.data.objects.get(f"{unit_name}Armor_CarapaceDamper_{side}_ClevisEye_Tip")
            _stay_between(
                f"{unit_name}Armor_CarapaceDamperStay_Base_{side}",
                base_eye,
                upper_deck,
                unit_name,
                role="Gunmetal",
                radius=0.032,
                direction=(1.0 if side == "L" else -1.0, 0.0, -0.3),
                tag=f"bastion_damper_base_stay_{side}",
            )
            _stay_between(
                f"{unit_name}Armor_CarapaceDamperStay_Tip_{side}",
                tip_eye,
                hull,
                unit_name,
                role="Gunmetal",
                radius=0.032,
                direction=(1.0 if side == "L" else -1.0, 0.0, 0.1),
                tag=f"bastion_damper_tip_stay_{side}",
            )
            for o in d_objs:
                _tag_object(o, f"bastion_carapace_damper_{side}")

    # 6. Connect all 4 CrawlerLegs to Hull via HipSponsonBridge
    if hull is not None:
        for side_code, x_dir in (("L", 1.0), ("R", -1.0)):
            for fb_code in ("F", "R"):
                hip_act = bpy.data.objects.get(f"{unit_name}CrawlerLeg_{side_code}_{fb_code}_HipActuator")
                _stay_between(
                    f"{unit_name}CrawlerLeg_{side_code}_{fb_code}_HipSponsonBridge",
                    hip_act,
                    hull,
                    unit_name,
                    role="Gunmetal",
                    radius=0.085,
                    direction=(x_dir, 0.0, -0.12),
                    tag=f"bastion_hip_bridge_{side_code}_{fb_code}",
                )

    # 7. Seat WeldSeam_Glacis & WeldSeam_CarapaceL/R onto UpperDeck
    if upper_deck is not None:
        ud_x0, ud_x1, _, ud_y1, _, ud_z1 = world_bounds(upper_deck)
        glacis_seam = bpy.data.objects.get(f"{unit_name}Armor_WeldSeam_Glacis")
        if glacis_seam is not None:
            gc = world_center(glacis_seam)
            _translate_cluster([glacis_seam], Vector((0.0, ud_y1 - 0.02 - gc.y, ud_z1 - 0.005 - gc.z)))
            _tag_object(glacis_seam, "bastion_glacis_seam")
        for side, sx in (("L", ud_x0 + 0.01), ("R", ud_x1 - 0.01)):
            c_seam = bpy.data.objects.get(f"{unit_name}Armor_WeldSeam_Carapace{side}")
            if c_seam is not None:
                sc = world_center(c_seam)
                _translate_cluster([c_seam], Vector((sx - sc.x, 0.0, ud_z1 - 0.005 - sc.z)))
                _tag_object(c_seam, f"bastion_carapace_seam_{side}")

    # 8. Lower SiegeFeederConduit_L/R, Antenna, and Greebles onto Turret_BaseShell / Hull
    if base_shell is not None:
        _, _, _, _, _, bs_z1 = world_bounds(base_shell)
        for side in ("L", "R"):
            c_objs = [
                o
                for o in (
                    bpy.data.objects.get(f"{unit_name}Turret_SiegeFeederConduit_{side}_{part}")
                    for part in ("Hose", "HoseCoupler_1", "HoseCoupler_2", "HoseClamp_1", "HoseClamp_2")
                )
                if o is not None
            ]
            if c_objs and _needs_tag(c_objs, f"bastion_conduit_{side}"):
                dx = -0.06 if side == "L" else 0.06
                _translate_cluster(c_objs, Vector((dx, 0.0, -0.36)))
                c1 = bpy.data.objects.get(f"{unit_name}Turret_SiegeFeederConduit_{side}_HoseCoupler_1")
                c2 = bpy.data.objects.get(f"{unit_name}Turret_SiegeFeederConduit_{side}_HoseCoupler_2")
                if c1 is not None:
                    snap_object_to_nearest(c1, [rear_bustle or base_shell], embed=0.014, max_dist=0.45)
                if c2 is not None:
                    snap_object_to_nearest(c2, [base_shell], embed=0.014, max_dist=0.45)
                for o in c_objs:
                    _tag_object(o, f"bastion_conduit_{side}")

            p_objs = [
                o
                for o in bpy.data.objects
                if o.type == "MESH" and o.name.startswith(f"{unit_name}Hull_{side}_PowerHose_")
            ]
            if p_objs and _needs_tag(p_objs, f"bastion_power_hose_{side}"):
                pdx = -0.08 if side == "L" else 0.08
                _translate_cluster(p_objs, Vector((pdx, 0.0, 0.0)))
                pc1 = bpy.data.objects.get(f"{unit_name}Hull_{side}_PowerHose_HoseCoupler_1")
                if pc1 is not None and (upper_deck is not None or hull is not None):
                    snap_object_to_nearest(pc1, [o for o in (upper_deck, hull) if o is not None], embed=0.006, max_dist=0.35)
                for o in p_objs:
                    _tag_object(o, f"bastion_power_hose_{side}")

        a_objs = [
            o
            for o in (bpy.data.objects.get(f"{unit_name}Sensor_Antenna{part}") for part in ("Base", "Mast", "Tip"))
            if o is not None
        ]
        if a_objs:
            seat_cluster_downward(a_objs, [base_shell], clearance=-0.015, max_drop=0.6)
            for o in a_objs:
                _tag_object(o, "bastion_antenna_seat")

        for side, sx in (("L", -0.44), ("R", 0.44)):
            gh = bpy.data.objects.get(f"{unit_name}_GreebleHatch_{side}")
            ghdl = bpy.data.objects.get(f"{unit_name}_GreebleHandle_{side}")
            cl = [o for o in (gh, ghdl) if o is not None]
            if cl:
                c = world_center(gh)
                _translate_cluster(cl, Vector((sx - c.x, -0.34 - c.y, (bs_z1 + 0.02) - c.z)))
                seat_cluster_downward(cl, [base_shell], clearance=-0.014, max_drop=0.6)
                for o in cl:
                    _tag_object(o, f"bastion_turret_hatch_{side}")

    louver_frame = bpy.data.objects.get(f"{unit_name}_GreebleLouverFrame")
    slats = [bpy.data.objects.get(f"{unit_name}_GreebleLouverSlat_{i}") for i in range(1, 5)]
    louver_cluster = [o for o in [louver_frame, *slats] if o is not None]
    if louver_cluster and hull is not None:
        _, _, _, _, _, h_z1 = world_bounds(hull)
        lc = world_center(louver_frame)
        _translate_cluster(louver_cluster, Vector((-lc.x, -1.20 - lc.y, (h_z1 + 0.02) - lc.z)))
        seat_cluster_downward(louver_cluster, [hull], clearance=-0.014, max_drop=1.2)
        for o in louver_cluster:
            _tag_object(o, "bastion_hull_louver")

    # 9. Exact Boolean cuts on Hull glacis, MantletHousing gun ports, and BaseShell visor pocket
    if hull is not None and hull.get(RECIPE_PROP) != "bastion_hull_glacis_cuts":
        _, _, h_y0, h_y1, _, _ = world_bounds(hull)
        for sx in (-0.40, 0.40):
            cut_recessed_service_pocket(
                hull,
                (sx, h_y1 - 0.02, 0.56),
                (0.24, 0.08, 0.22),
                recipe_tag="bastion_hull_glacis_cuts",
            )
        cut_recessed_service_pocket(
            hull,
            (0.0, h_y0 + 0.02, 0.56),
            (0.64, 0.08, 0.26),
            recipe_tag="bastion_hull_glacis_cuts",
        )
    if mantlet is not None and mantlet.get(RECIPE_PROP) != "bastion_mantlet_cuts":
        _, _, _, m_y1, _, _ = world_bounds(mantlet)
        for sx in (-0.19, 0.19):
            cut_recessed_service_pocket(
                mantlet,
                (sx, m_y1 - 0.02, 1.38),
                (0.14, 0.08, 0.14),
                recipe_tag="bastion_mantlet_cuts",
            )
    if visor_housing is not None and base_shell is not None:
        cut_visor_pocket_around_housing(base_shell, visor_housing, margin=0.008, recipe_tag="bastion_visor_pocket")

    # 9b. Level-1 RTS readability: LightArmor glacis/turret roof shields & Warning glacis trim
    l_armor = resolve_unit_material(unit_name, "LightArmor")
    w_armor = resolve_unit_material(unit_name, "Warning")
    _add_walker_armor_plate(
        f"{unit_name}Armor_GlacisLightPlate", (0.92, 0.40, 0.050), (0.0, 0.92, 0.88),
        hull, l_armor, bevel=0.012, rotation=(-0.18, 0.0, 0.0), recipe_tag="bastion_l1_glacis_plate",
    )
    _add_walker_armor_plate(
        f"{unit_name}Armor_TurretRoofShield", (0.84, 0.72, 0.045), (0.0, -0.08, 1.56),
        base_shell, l_armor, bevel=0.012, recipe_tag="bastion_l1_turret_roof",
    )
    _add_walker_armor_plate(
        f"{unit_name}Warning_GlacisTrim", (0.76, 0.12, 0.028), (0.0, 1.12, 0.78),
        hull, w_armor, bevel=0.006, recipe_tag="bastion_l1_warning_trim",
    )

    # 10. Re-assign rigid 1.0 weights after Booleans and bind subassemblies accurately
    meshes = _collect_meshes(unit_name) + _collect_meshes("BastionArmor")
    if armature is not None:
        rmk.assign_rigid_mesh_weights(meshes, prefix=unit_name, armature_obj=armature, leg_type="quad")
        _apply_exact_bone_overrides(
            meshes,
            armature,
            [
                ("GlacisLightPlate", "Bone_Hull"),
                ("GlacisTrim", "Bone_Hull"),
                ("TurretRoofShield", "Bone_TurretYaw"),
                ("CarapaceDamper", "Bone_Hull"),
                ("HipSponsonBridge", "Bone_Hull"),
                ("WeldSeam_", "Bone_Hull"),
                ("GreebleLouver", "Bone_Hull"),
                ("CrawlerLeg_L_F_AnkleSleeve", "Bone_Foot_FL"),
                ("CrawlerLeg_L_R_AnkleSleeve", "Bone_Foot_RL"),
                ("CrawlerLeg_R_F_AnkleSleeve", "Bone_Foot_FR"),
                ("CrawlerLeg_R_R_AnkleSleeve", "Bone_Foot_RR"),
                ("DualGun_ThermalSleeve_", "Bone_BarrelRecoil"),
                ("DualGun_EvacuatorStripe_", "Bone_BarrelRecoil"),
                ("DualGun_BoreEvacuator_", "Bone_BarrelRecoil"),
                ("DualGun_BarrelTube_", "Bone_BarrelRecoil"),
                ("DualGun_Muzzle", "Bone_BarrelRecoil"),
                ("RecoilRecuperator_L_InnerRamRod", "Bone_BarrelRecoil"),
                ("RecoilRecuperator_L_ClevisEye_Tip", "Bone_BarrelRecoil"),
                ("RecoilRecuperator_L_ClevisPin_Tip", "Bone_BarrelRecoil"),
                ("RecoilRecuperator_R_InnerRamRod", "Bone_BarrelRecoil"),
                ("RecoilRecuperator_R_ClevisEye_Tip", "Bone_BarrelRecoil"),
                ("RecoilRecuperator_R_ClevisPin_Tip", "Bone_BarrelRecoil"),
                ("RecuperatorRodStay_", "Bone_BarrelRecoil"),
                ("RecoilRecuperator_", "Bone_TurretPitch"),
                ("RecuperatorBaseStay_", "Bone_TurretPitch"),
                ("DualGun_MantletBlock", "Bone_TurretPitch"),
                ("DualGun_Breech_", "Bone_TurretPitch"),
                ("Turret_MantletHousing", "Bone_TurretPitch"),
                ("AccessHatch_", "Bone_TurretYaw"),
                ("TrunnionBracket_", "Bone_TurretYaw"),
                ("SiegeFeederConduit_", "Bone_TurretYaw"),
                ("Sensor_Antenna", "Bone_TurretYaw"),
                ("Sensor_Visor_", "Bone_TurretYaw"),
                ("GreebleHatch_", "Bone_TurretYaw"),
                ("GreebleHandle_", "Bone_TurretYaw"),
                ("Turret_", "Bone_TurretYaw"),
            ],
        )

    _finalize_mesh_standards(meshes, unit_name)
    return {"unit": unit_name, "meshes": len(meshes)}


# ==============================================================================
# 4. ArachnidHexapod v2
# ==============================================================================

def _ensure_arachnid_middle_leg_bones(armature: bpy.types.Object) -> None:
    """Create Bone_Thigh_ML/MR, Bone_Shin_ML/MR, Bone_Foot_ML/MR under Bone_Hull if missing."""
    needed = {
        f"Bone_{part}_{side}"
        for side in ("ML", "MR")
        for part in ("Thigh", "Shin", "Foot")
    }
    if all(n in armature.data.bones for n in needed):
        return
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode="EDIT")
    try:
        ebones = armature.data.edit_bones
        hull_eb = ebones.get("Bone_Hull") or (ebones[0] if len(ebones) > 0 else None)
        for side, sign in (("ML", -1.0), ("MR", 1.0)):
            hip = Vector((sign * 1.152, 0.0, 0.95))
            knee = Vector((sign * 1.913, 0.0, 0.6463))
            foot = Vector((sign * 2.452, 0.0, 0.04))
            foot_end = Vector((sign * 2.452, 0.20, 0.0))

            thigh_name = f"Bone_Thigh_{side}"
            shin_name = f"Bone_Shin_{side}"
            foot_name = f"Bone_Foot_{side}"

            tb = ebones.get(thigh_name) or ebones.new(thigh_name)
            tb.head = hip
            tb.tail = knee
            if hull_eb is not None:
                tb.parent = hull_eb
            tb.use_connect = False

            sb = ebones.get(shin_name) or ebones.new(shin_name)
            sb.head = knee
            sb.tail = foot
            sb.parent = tb
            sb.use_connect = True

            fb = ebones.get(foot_name) or ebones.new(foot_name)
            fb.head = foot
            fb.tail = foot_end
            fb.parent = sb
            fb.use_connect = True
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")


def apply_arachnid_hexapod_v2(
    unit_name: str = "ArachnidHexapod",
    armature: bpy.types.Object | None = None,
) -> dict:
    """Apply Exact Boolean optic/missile bays, middle-foot collars, fairing seating, and ML/MR normalization compatibility."""
    _remove_duplicate_suffix_objects()
    if armature is None:
        armature = bpy.data.objects.get(f"{unit_name}_Armature")

    remove_objects_matching(
        lambda name: name.startswith(
            (
                f"{unit_name}Armor_TurretRingGuard_",
                f"{unit_name}Armor_TrunnionBracket_",
                f"{unit_name}Armor_AccessHatch_",
            )
        )
    )

    carapace = bpy.data.objects.get(f"{unit_name}Armor_ArmoredCarapace")
    optic_crown = bpy.data.objects.get(f"{unit_name}Armor_OpticCrown")
    optic_glazing = bpy.data.objects.get(f"{unit_name}Sensor_OpticGlazing")
    missile_cradle = bpy.data.objects.get(f"{unit_name}Armor_DorsalMissileCradle")
    power_deck = bpy.data.objects.get(f"{unit_name}Armor_RearPowerDeck")

    # 1. Connect MidFootPad_ML/MR to MidShin_ML/MR with physical MidAnkleCollar_ML/MR
    gunmetal = resolve_unit_material(unit_name, "Gunmetal")
    for side, sign in (("ML", -1.0), ("MR", 1.0)):
        collar_name = f"{unit_name}Joint_MidAnkleCollar_{side}"
        existing = bpy.data.objects.get(collar_name)
        if existing is not None:
            bpy.data.objects.remove(existing, do_unlink=True)
        collar = make_faceted_cylinder(
            collar_name,
            (sign * 2.42, 0.0, 0.05),
            (sign * 2.18, 0.0, 0.16),
            0.055,
            gunmetal,
            segments=12,
        )
        _tag_object(collar, f"arachnid_mid_ankle_collar_{side}")

    # 2. Anchor CrawlerLeg_*_AnkleHydraulicShock & HydraulicRod_LF/LR/RF/RR cleanly
    for leg in ("L_F", "L_R", "R_F", "R_R"):
        shock = bpy.data.objects.get(f"{unit_name}CrawlerLeg_{leg}_AnkleHydraulicShock")
        tibia = bpy.data.objects.get(f"{unit_name}CrawlerLeg_{leg}_TibiaStrut")
        if shock is not None and tibia is not None:
            snap_object_to_nearest(shock, [tibia], embed=0.015, max_dist=0.25)
            _tag_object(shock, f"arachnid_shock_seat_{leg}")
    for side in ("LF", "LR", "RF", "RR"):
        rod = bpy.data.objects.get(f"{unit_name}HydraulicRod_{side}")
        ram = bpy.data.objects.get(f"{unit_name}HydraulicRam_{side}")
        if rod is not None and ram is not None:
            snap_object_to_nearest(rod, [ram], embed=0.015, max_dist=0.25)
            _tag_object(rod, f"arachnid_rod_seat_{side}")

    # 3. Lift carapace-roof/deck hardware out of ArmoredCarapace onto exterior & seat fairings/lugs/fins
    if carapace is not None:
        coaming_objs = [
            o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith(unit_name) and "TurretCoaming" in o.name
        ]
        if coaming_objs and _needs_tag(coaming_objs, "arachnid_turret_coaming"):
            _translate_cluster(coaming_objs, Vector((0.0, 0.0, 0.50)))
            seat_cluster_downward(coaming_objs, [carapace], clearance=-0.015, max_drop=0.4)
            for o in coaming_objs:
                _tag_object(o, "arachnid_turret_coaming")

        for side_tok, target_x in (("_L", -0.46), ("_R", 0.46)):
            trun_objs = [
                o
                for o in bpy.data.objects
                if o.type == "MESH"
                and o.name.startswith(f"{unit_name}WeaponTrunnions_")
                and o.name.endswith(side_tok)
            ]
            if trun_objs and _needs_tag(trun_objs, f"arachnid_weapon_trunnions{side_tok}"):
                avg_x = sum(world_center(o).x for o in trun_objs) / len(trun_objs)
                _translate_cluster(trun_objs, Vector((target_x - avg_x, 0.0, 0.48)))
                if missile_cradle is not None:
                    seat_cluster_downward(trun_objs, [carapace], clearance=-0.012, max_drop=0.5)
                for o in trun_objs:
                    _tag_object(o, f"arachnid_weapon_trunnions{side_tok}")

        mp_lift = [
            o
            for o in (
                bpy.data.objects.get(f"{unit_name}MissilePod_BlastCowl"),
                bpy.data.objects.get(f"{unit_name}MissilePod_TrunnionAxle"),
                bpy.data.objects.get(f"{unit_name}MissilePod_TrunnionCradle"),
            )
            if o is not None
        ]
        if mp_lift and _needs_tag(mp_lift, "arachnid_missile_pod_mount"):
            _translate_cluster(mp_lift, Vector((0.0, 0.0, 0.26)))
            for o in mp_lift:
                _tag_object(o, "arachnid_missile_pod_mount")

        coax_objs = [
            o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith(unit_name) and "CoaxPort_" in o.name
        ]
        if coax_objs and _needs_tag(coax_objs, "arachnid_coax_port"):
            _translate_cluster(coax_objs, Vector((0.52, 0.14, 0.28)))
            for o in coax_objs:
                _tag_object(o, "arachnid_coax_port")

        driver_objs = [
            o
            for o in bpy.data.objects
            if o.type == "MESH"
            and o.name.startswith(unit_name)
            and ("DriverHood" in o.name or "DriverPeriscope" in o.name)
        ]
        if driver_objs and _needs_tag(driver_objs, "arachnid_driver_hood"):
            hood = next((o for o in driver_objs if "DriverHood" in o.name), driver_objs[0])
            hc = world_center(hood)
            _translate_cluster(driver_objs, Vector((0.0, 0.38 - hc.y, 1.66 - hc.z)))
            seat_cluster_downward(driver_objs, [carapace], clearance=-0.015, max_drop=0.4)
            for o in driver_objs:
                _tag_object(o, "arachnid_driver_hood")

        hatch_objs = [
            o
            for o in bpy.data.objects
            if o.type == "MESH"
            and o.name.startswith(unit_name)
            and ("AccessHatch" in o.name or "GreebleHatch" in o.name or "GreebleHandle" in o.name)
        ]
        if hatch_objs and _needs_tag(hatch_objs, "arachnid_access_hatch"):
            _translate_cluster(hatch_objs, Vector((0.25, -0.12, 0.62)))
            seat_cluster_downward(hatch_objs, [carapace], clearance=-0.015, max_drop=0.5)
            for o in hatch_objs:
                _tag_object(o, "arachnid_access_hatch")

        deck_cluster = [
            o
            for o in bpy.data.objects
            if o.type == "MESH"
            and o.name.startswith(unit_name)
            and (
                "RearPowerDeck" in o.name
                or "Engine_CoolingFin_" in o.name
                or "AntennaPedestal" in o.name
                or "AntennaMast" in o.name
                or "AntennaTip" in o.name
                or "AntennaBase" in o.name
            )
        ]
        if deck_cluster and _needs_tag(deck_cluster, "arachnid_rear_deck"):
            _translate_cluster(deck_cluster, Vector((0.0, -0.15, 0.32)))
            seat_cluster_downward(deck_cluster, [carapace], clearance=-0.015, max_drop=0.5)
            for o in deck_cluster:
                _tag_object(o, "arachnid_rear_deck")

        for side in ("L", "R"):
            hf = bpy.data.objects.get(f"{unit_name}Armor_HipFairing_{side}")
            fp = bpy.data.objects.get(f"{unit_name}Armor_FlankPlate_{side}")
            if hf is not None and _needs_tag([hf], f"arachnid_fairing_{side}_out"):
                dx = -0.18 if side == "L" else 0.18
                _translate_cluster([hf], Vector((dx, 0.0, 0.0)))
                _tag_object(hf, f"arachnid_fairing_{side}_out")
            if fp is not None:
                seat_object_downward_to_target(fp, [carapace], clearance=-0.015, max_drop=0.5)
                _tag_object(fp, f"arachnid_flank_{side}")

        for side in ("L", "R"):
            hose_objs = [
                o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith(f"{unit_name}Hull_{side}_PowerHose_")
            ]
            if hose_objs and _needs_tag(hose_objs, f"arachnid_power_hose_{side}"):
                fp_target = bpy.data.objects.get(f"{unit_name}Armor_FlankPlate_{side}")
                targets = [o for o in (carapace, fp_target) if o is not None]
                dx = 0.06 if side == "L" else -0.06
                _translate_cluster(hose_objs, Vector((dx, 0.0, 1.08)))
                seat_cluster_downward(hose_objs, targets, clearance=-0.008, max_drop=0.8)
                for o in hose_objs:
                    _tag_object(o, f"arachnid_power_hose_{side}")
        for idx in (1, 2):
            gusset = bpy.data.objects.get(f"{unit_name}Armor_Gusset_{idx}")
            fp = bpy.data.objects.get(f"{unit_name}Armor_FlankPlate_{'L' if idx == 1 else 'R'}")
            target = fp or carapace
            if gusset is not None and target is not None:
                seat_object_downward_to_target(gusset, [target], clearance=-0.015, max_drop=0.5)
                _tag_object(gusset, f"arachnid_gusset_{idx}")
        for idx in range(1, 5):
            lug = bpy.data.objects.get(f"{unit_name}Armor_LiftingLug_{idx}")
            pad = bpy.data.objects.get(f"{unit_name}Armor_LugWeldPad_{idx}")
            cl = [o for o in (pad, lug) if o is not None]
            if cl:
                seat_cluster_downward(cl, [carapace], clearance=-0.015, max_drop=0.5)
                for o in cl:
                    _tag_object(o, f"arachnid_lug_{idx}")

    if power_deck is not None:
        for idx in range(1, 5):
            fin = bpy.data.objects.get(f"{unit_name}Engine_CoolingFin_{idx}")
            if fin is not None:
                seat_object_downward_to_target(fin, [power_deck], clearance=-0.012, max_drop=0.3)
                _tag_object(fin, f"arachnid_fin_{idx}")

    # 4. Exact Boolean optic bay, lateral carapace vents, and dorsal missile pod side recesses
    if optic_glazing is not None and optic_crown is not None:
        cut_visor_pocket_around_housing(optic_crown, optic_glazing, margin=0.008, recipe_tag="arachnid_optic_bay")

    if carapace is not None and carapace.get(RECIPE_PROP) != "arachnid_carapace_bays":
        for sx in (-1.24, 1.24):
            cut_recessed_service_pocket(
                carapace,
                (sx, -0.32, 1.16),
                (0.12, 0.62, 0.16),
                recipe_tag="arachnid_carapace_bays",
            )
    if missile_cradle is not None and missile_cradle.get(RECIPE_PROP) != "arachnid_missile_bays":
        for sx in (-0.50, 0.50):
            cut_recessed_service_pocket(
                missile_cradle,
                (sx, -0.52, 1.63),
                (0.08, 0.68, 0.14),
                recipe_tag="arachnid_missile_bays",
            )

    # 4b. Level-1 RTS readability: LightArmor carapace chevrons & Warning missile cradle trim
    l_armor = resolve_unit_material(unit_name, "LightArmor")
    w_armor = resolve_unit_material(unit_name, "Warning")
    if carapace is not None:
        _, _, _, _, _, c_z1 = world_bounds(carapace)
        for side, sx in (("L", -1.0), ("R", 1.0)):
            _add_walker_armor_plate(
                f"{unit_name}Armor_CarapaceChevron_{side}", (0.22, 0.44, 0.046), (sx * 0.50, 0.72, c_z1 + 0.04),
                carapace, l_armor, bevel=0.010, recipe_tag="arachnid_l1_carapace_chevron",
            )
    _add_walker_armor_plate(
        f"{unit_name}Warning_CradleTrim", (0.68, 0.14, 0.030), (0.0, -0.18, 1.80),
        missile_cradle, w_armor, bevel=0.006, recipe_tag="arachnid_l1_warning_trim",
    )

    # 5. Ensure ML/MR bones, preserve orientation normalization, and re-assign rigid 1.0 weights
    meshes = _collect_meshes(unit_name)
    if armature is not None:
        _ensure_arachnid_middle_leg_bones(armature)
        rmk.normalize_arachnid_middle_leg_orientation(armature)
        rmk.assign_rigid_mesh_weights(meshes, prefix=unit_name, armature_obj=armature, leg_type="quad")
        _apply_exact_bone_overrides(
            meshes,
            armature,
            [
                ("CarapaceChevron_", "Bone_Hull"),
                ("CradleTrim", "Bone_TurretPitch"),
                ("MidHip_ML", "Bone_Thigh_ML"),
                ("MidThigh_ML", "Bone_Thigh_ML"),
                ("HydraulicRam_ML", "Bone_Thigh_ML"),
                ("MidHip_MR", "Bone_Thigh_MR"),
                ("MidThigh_MR", "Bone_Thigh_MR"),
                ("HydraulicRam_MR", "Bone_Thigh_MR"),
                ("MidShin_ML", "Bone_Shin_ML"),
                ("MidKnee_L", "Bone_Shin_ML"),
                ("MidKneeAxle_L", "Bone_Shin_ML"),
                ("MidShin_MR", "Bone_Shin_MR"),
                ("MidKnee_R", "Bone_Shin_MR"),
                ("MidKneeAxle_R", "Bone_Shin_MR"),
                ("MidFootPad_ML", "Bone_Foot_ML"),
                ("MidAnkleCollar_ML", "Bone_Foot_ML"),
                ("MidFootPad_MR", "Bone_Foot_MR"),
                ("MidAnkleCollar_MR", "Bone_Foot_MR"),
                ("CrawlerLeg_L_F_AnkleHydraulicShock", "Bone_Shin_FL"),
                ("CrawlerLeg_L_R_AnkleHydraulicShock", "Bone_Shin_RL"),
                ("CrawlerLeg_R_F_AnkleHydraulicShock", "Bone_Shin_FR"),
                ("CrawlerLeg_R_R_AnkleHydraulicShock", "Bone_Shin_RR"),
                ("CrawlerLeg_L_F_AnkleSleeve", "Bone_Foot_FL"),
                ("CrawlerLeg_L_R_AnkleSleeve", "Bone_Foot_RL"),
                ("CrawlerLeg_R_F_AnkleSleeve", "Bone_Foot_FR"),
                ("CrawlerLeg_R_R_AnkleSleeve", "Bone_Foot_RR"),
                ("HydraulicRod_LF", "Bone_Thigh_FL"),
                ("HydraulicRod_LR", "Bone_Thigh_RL"),
                ("HydraulicRod_RF", "Bone_Thigh_FR"),
                ("HydraulicRod_RR", "Bone_Thigh_RR"),
                ("_DarkBore", "Bone_BarrelRecoil"),
                ("CoaxMuzzle", "Bone_BarrelRecoil"),
                ("MissileCell_", "Bone_TurretPitch"),
                ("MissilePod_", "Bone_TurretPitch"),
                ("DorsalMissileCradle", "Bone_TurretPitch"),
                ("TurretCoaming", "Bone_TurretYaw"),
            ],
        )

    _finalize_mesh_standards(meshes, unit_name)
    return {"unit": unit_name, "meshes": len(meshes)}


# ==============================================================================
# 5. ArachnidHexapodFactory v2
# ==============================================================================

def apply_arachnid_hexapod_factory_v2(
    asset_id: str = "ArachnidHexapodFactory",
) -> dict:
    """Apply Exact Boolean service bays/recesses and raycast seating for exhausts, lights, gantry, and reactor piping."""
    _remove_duplicate_suffix_objects()

    hall = bpy.data.objects.get(f"{asset_id}Armor_CarapaceHall")
    deck = bpy.data.objects.get(f"{asset_id}_FoundationDeck")
    header = bpy.data.objects.get(f"{asset_id}Armor_BayHeader")
    jamb_l = bpy.data.objects.get(f"{asset_id}Armor_BayJamb_L")
    jamb_r = bpy.data.objects.get(f"{asset_id}Armor_BayJamb_R")

    # 1. Seat Engine_Exhaust_L/R + Lip downward onto CarapaceHall dome and add wide plenum flanges
    if hall is not None:
        gunmetal = resolve_unit_material(asset_id, "Gunmetal")
        for side in ("L", "R"):
            ex = bpy.data.objects.get(f"{asset_id}Engine_Exhaust_{side}")
            lip = bpy.data.objects.get(f"{asset_id}Engine_ExhaustLip_{side}")
            cluster = [o for o in (ex, lip) if o is not None]
            if cluster:
                seat_cluster_downward(cluster, [hall], clearance=-0.04, max_drop=0.6)
                for o in cluster:
                    _tag_object(o, f"factory_exhaust_{side}")
                if ex is not None:
                    plenum_name = f"{asset_id}Engine_ExhaustPlenum_{side}"
                    existing = bpy.data.objects.get(plenum_name)
                    if existing is not None:
                        bpy.data.objects.remove(existing, do_unlink=True)
                    ex_c = world_center(ex)
                    plenum = make_faceted_cylinder(
                        plenum_name,
                        (ex_c.x, ex_c.y, 4.16),
                        (ex_c.x, ex_c.y, 3.86),
                        0.25,
                        gunmetal,
                        segments=14,
                    )
                    _tag_object(plenum, f"factory_exhaust_plenum_{side}")

        for side_code, dx in (("LM", -0.38), ("RM", 0.38)):
            hb = bpy.data.objects.get(f"{asset_id}Joint_{side_code}_HipBearing")
            if hb is not None and _needs_tag([hb], f"factory_hip_bearing_{side_code}"):
                _translate_cluster([hb], Vector((dx, 0.0, 0.0)))
                snap_object_to_nearest(hb, [hall], embed=0.015, max_dist=0.5)
                _tag_object(hb, f"factory_hip_bearing_{side_code}")

        for side in ("L", "R"):
            brace = bpy.data.objects.get(f"{asset_id}Gantry_Brace_{side}")
            if brace is not None and _needs_tag([brace], f"factory_gantry_brace_{side}"):
                _translate_cluster([brace], Vector((0.0, 0.0, 0.14)))
                seat_object_downward_to_target(brace, [hall], clearance=-0.012, max_drop=0.35)
                _tag_object(brace, f"factory_gantry_brace_{side}")

    # 2. Snap BayGuide lights and BayLintel flush into BayJambs and BayHeader
    for side, jamb in (("L", jamb_l), ("R", jamb_r)):
        guide = bpy.data.objects.get(f"{asset_id}Light_BayGuide_{side}")
        if guide is not None and jamb is not None:
            snap_object_to_nearest(guide, [jamb], embed=0.015, max_dist=0.35)
            _tag_object(guide, f"factory_guide_{side}")
    lintel = bpy.data.objects.get(f"{asset_id}Light_BayLintel")
    if lintel is not None and header is not None:
        snap_object_to_nearest(lintel, [header], embed=0.015, max_dist=0.35)
        _tag_object(lintel, "factory_lintel_seat")

    # 3. Exact Boolean service bays & recesses on CarapaceHall, BayHeader, FoundationDeck
    if hall is not None and hall.get(RECIPE_PROP) != "factory_hall_bays":
        h_x0, h_x1, h_y0, _, _, _ = world_bounds(hall)
        for sx in (h_x0 + 0.04, h_x1 - 0.04):
            cut_recessed_service_pocket(
                hall,
                (sx, 0.0, 1.85),
                (0.18, 1.35, 1.05),
                recipe_tag="factory_hall_bays",
            )
        cut_recessed_service_pocket(
            hall,
            (0.0, h_y0 + 0.04, 2.10),
            (1.55, 0.18, 0.85),
            recipe_tag="factory_hall_bays",
        )
    if header is not None and header.get(RECIPE_PROP) != "factory_header_recess":
        _, _, _, hd_y1, hd_z0, hd_z1 = world_bounds(header)
        cut_recessed_service_pocket(
            header,
            (0.0, hd_y1 - 0.02, (hd_z0 + hd_z1) * 0.5),
            (1.30, 0.14, 0.16),
            recipe_tag="factory_header_recess",
        )
    if deck is not None and deck.get(RECIPE_PROP) != "factory_deck_trenches":
        _, _, _, _, _, d_z1 = world_bounds(deck)
        for sx in (-2.35, 2.35):
            cut_recessed_service_pocket(
                deck,
                (sx, 3.60, d_z1 - 0.01),
                (0.50, 0.72, 0.08),
                recipe_tag="factory_deck_trenches",
            )

    # 4. Add raycast-seated rear coolant conduits bridging CarapaceHall -> FoundationDeck
    if hall is not None and deck is not None:
        gunmetal = resolve_unit_material(asset_id, "Gunmetal")
        for side, sx in (("L", -0.94), ("R", 0.94)):
            cname = f"{asset_id}_ReactorConduit_{side}"
            existing = bpy.data.objects.get(cname)
            if existing is not None:
                bpy.data.objects.remove(existing, do_unlink=True)
            conduit = make_faceted_cylinder(
                cname,
                (sx, -2.75, 2.40),
                (sx, -3.15, 0.28),
                0.11,
                gunmetal,
                segments=10,
            )
            _tag_object(conduit, f"factory_conduit_{side}")
            _stay_between(
                f"{asset_id}_ReactorConduitCollar_{side}",
                conduit,
                hall,
                asset_id,
                role="Gunmetal",
                radius=0.14,
                direction=(0.0, 1.0, 0.0),
                tag=f"factory_conduit_collar_{side}",
            )

    # 5. Level-1 RTS readability: LightArmor carapace ribs & Warning bay header trim
    l_armor = resolve_unit_material(asset_id, "LightArmor")
    w_armor = resolve_unit_material(asset_id, "Warning")
    if hall is not None:
        _, _, _, _, _, h_z1 = world_bounds(hall)
        for side, sx in (("L", -1.0), ("R", 1.0)):
            _add_walker_armor_plate(
                f"{asset_id}_CarapaceRib_{side}", (0.32, 2.60, 0.16), (sx * 1.15, -0.20, h_z1 + 0.08),
                hall, l_armor, bevel=0.025, clearance=-0.02, recipe_tag="factory_l1_carapace_rib",
            )
    if header is not None:
        _, _, _, hd_y1, _, hd_z1 = world_bounds(header)
        _add_walker_armor_plate(
            f"{asset_id}_HeaderWarningTrim", (1.80, 0.18, 0.08), (0.0, hd_y1 - 0.08, hd_z1 + 0.02),
            header, w_armor, bevel=0.014, clearance=-0.015, recipe_tag="factory_l1_warning_trim",
        )

    meshes = _collect_meshes(asset_id)
    _finalize_mesh_standards(meshes, asset_id)
    return {"unit": asset_id, "meshes": len(meshes)}


# ==============================================================================
# Unified v2 Audit Gate
# ==============================================================================

def audit_walker_v2_scene(
    unit_name: str,
    root_name: str,
    clearance_pairs: Sequence[tuple[str, str, float]] = (),
    run_walk_sweep: bool = True,
) -> dict:
    """Audit UVMap, Color RGBA, single material, .001 suffixes, recipe tags, connectivity, containment, rigid weights, and Action_Walk frames."""
    meshes = _collect_meshes(unit_name)
    if unit_name == "BastionSiegeMech":
        meshes.extend(_collect_meshes("BastionArmor"))
    armature = bpy.data.objects.get(f"{unit_name}_Armature")
    deps = bpy.context.evaluated_depsgraph_get()

    missing_uv = [o.name for o in meshes if "UVMap" not in o.data.uv_layers]
    missing_color = []
    for o in meshes:
        attr = o.data.color_attributes.get("Color")
        if attr is None or attr.domain != "CORNER" or attr.data_type != "BYTE_COLOR":
            missing_color.append(o.name)
    bad_mat = [o.name for o in meshes if len(o.data.materials) != 1]
    dup_names = [o.name for o in meshes if re.search(r"\.\d{3}$", o.name)]
    tagged = [o.name for o in meshes if RECIPE_PROP in o]

    disconnected = audit_connectivity(meshes, root_name=root_name, max_gap=0.0085)
    containment = audit_closed_solid_containment(meshes, depsgraph=deps)
    weight_errors = _validate_rigid_weights_1_0(meshes, armature)
    walk_report = (
        audit_mech_walk_frames(unit_name, armature, clearance_pairs=clearance_pairs)
        if run_walk_sweep
        else {"checked_frames": 0, "min_foot_z": 0.0, "clearance_violations": [], "floor_violations": []}
    )

    total_faces = sum(len(o.data.polygons) for o in meshes)
    return {
        "unit": unit_name,
        "mesh_count": len(meshes),
        "total_faces": total_faces,
        "missing_uv": missing_uv,
        "missing_color": missing_color,
        "bad_material_count": bad_mat,
        "duplicate_suffixes": dup_names,
        "tagged_v2_objects": len(tagged),
        "disconnected_from_hull": disconnected,
        "invalid_containment": containment,
        "weight_errors": weight_errors,
        "walk_sweep": walk_report,
        "passed": (
            not missing_uv
            and not missing_color
            and not bad_mat
            and not dup_names
            and len(tagged) >= 3
            and not disconnected
            and not containment
            and not weight_errors
            and not walk_report["clearance_violations"]
            and not walk_report["floor_violations"]
        ),
    }
