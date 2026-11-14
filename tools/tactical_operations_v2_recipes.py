"""Shared Blender 4.5.14 v2 sculpting, Exact Boolean detailing, raycast seating,
and BVHTree clearance-auditing recipes for tactical-operations Level 1 units.

Targets the 6 tactical-operations units receiving v2 source blends:
- ArcLanceWalker
- TidebreakerHovercraft
- FieldMedicRover
- SkyguardSAM
- SalvageHauler
- SentinelRelay
"""

from __future__ import annotations

import math
import sys
from pathlib import Path
from typing import Any, Callable, Iterable, Mapping, Sequence

import bmesh
import bpy
import mathutils
from mathutils import Euler, Matrix, Vector
from mathutils.bvhtree import BVHTree

REPO_ROOT = Path(__file__).resolve().parent.parent
TOOLS_DIR = REPO_ROOT / "tools"
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

TARGET_UNITS_V2 = (
    "ArcLanceWalker",
    "TidebreakerHovercraft",
    "FieldMedicRover",
    "SkyguardSAM",
    "SalvageHauler",
    "SentinelRelay",
)

RECIPE_PROP = "rcl_v2_recipe"
CUTTER_CLEARANCE = 0.002  # 2 mm pocket wall clearance around nested inserts


# ---------------------------------------------------------------------------
# Basic scene & mesh utilities
# ---------------------------------------------------------------------------

def world_bounds(obj: bpy.types.Object) -> tuple[float, float, float, float, float, float]:
    pts = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return (
        min(p.x for p in pts),
        max(p.x for p in pts),
        min(p.y for p in pts),
        max(p.y for p in pts),
        min(p.z for p in pts),
        max(p.z for p in pts),
    )


def world_center(obj: bpy.types.Object) -> Vector:
    x0, x1, y0, y1, z0, z1 = world_bounds(obj)
    return Vector(((x0 + x1) * 0.5, (y0 + y1) * 0.5, (z0 + z1) * 0.5))


def resolve_unit_material(unit_prefix: str, suffix: str) -> bpy.types.Material:
    exact = bpy.data.materials.get(f"{unit_prefix}_{suffix}")
    if exact is not None:
        return exact
    token = suffix.lower()
    for mat in bpy.data.materials:
        name = mat.name.lower()
        if name.startswith("preview") or "detail" in name:
            continue
        if token in name:
            return mat
    mat = bpy.data.materials.new(name=f"{unit_prefix}_{suffix}")
    mat.use_nodes = True
    return mat


def ensure_single_material(obj: bpy.types.Object, material: bpy.types.Material) -> None:
    if obj.type != "MESH" or material is None:
        return
    obj.data.materials.clear()
    obj.data.materials.append(material)
    for poly in obj.data.polygons:
        poly.material_index = 0


def ensure_uv_and_color_layers(
    mesh: bpy.types.Mesh,
    *,
    force_smart_uv: bool = False,
) -> None:
    if not mesh.uv_layers:
        mesh.uv_layers.new(name="UVMap")
        force_smart_uv = True
    if "UVMap" not in mesh.uv_layers:
        mesh.uv_layers[0].name = "UVMap"
    while len(mesh.uv_layers) > 1:
        for layer in list(mesh.uv_layers):
            if layer.name != "UVMap":
                mesh.uv_layers.remove(layer)
                break

    color_attr = mesh.color_attributes.get("Color")
    if color_attr is None:
        mesh.color_attributes.new(name="Color", type="BYTE_COLOR", domain="CORNER")
    elif color_attr.domain != "CORNER" or color_attr.data_type != "BYTE_COLOR":
        mesh.color_attributes.remove(color_attr)
        mesh.color_attributes.new(name="Color", type="BYTE_COLOR", domain="CORNER")

    if force_smart_uv and len(mesh.polygons) > 0:
        bm = bmesh.new()
        bm.from_mesh(mesh)
        uv_layer = bm.loops.layers.uv.verify()
        # Deterministic box-projection fallback per face normal so smart UVs
        # never fail in headless mode without an active 3D viewport.
        for face in bm.faces:
            n = face.normal
            ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
            for loop in face.loops:
                co = loop.vert.co
                if az >= ax and az >= ay:
                    u, v = co.x * 0.35 + 0.5, co.y * 0.35 + 0.5
                elif ax >= ay and ax >= az:
                    u, v = co.y * 0.35 + 0.5, co.z * 0.35 + 0.5
                else:
                    u, v = co.x * 0.35 + 0.5, co.z * 0.35 + 0.5
                loop[uv_layer].uv = (max(0.01, min(0.99, u)), max(0.01, min(0.99, v)))
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()


def cleanup_mesh_topology(mesh: bpy.types.Mesh, *, merge_dist: float = 1e-4) -> None:
    if len(mesh.vertices) == 0:
        return
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=merge_dist)
    bmesh.ops.dissolve_degenerate(bm, dist=1e-5, edges=list(bm.edges))
    loose_verts = [v for v in bm.verts if not v.link_faces]
    if loose_verts:
        bmesh.ops.delete(bm, geom=loose_verts, context="VERTS")
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()


def create_mesh_object(
    name: str,
    vertices: Sequence[Sequence[float]],
    faces: Sequence[Sequence[int]],
    material: bpy.types.Material,
    *,
    smooth: bool = True,
    collection: bpy.types.Collection | None = None,
) -> bpy.types.Object:
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata([tuple(v) for v in vertices], [], [tuple(f) for f in faces])
    cleanup_mesh_topology(mesh)
    for poly in mesh.polygons:
        poly.use_smooth = smooth
    ensure_uv_and_color_layers(mesh, force_smart_uv=True)
    obj = bpy.data.objects.new(name, mesh)
    target_col = collection or bpy.context.scene.collection
    target_col.objects.link(obj)
    ensure_single_material(obj, material)
    return obj


def replace_object_geometry(
    obj: bpy.types.Object,
    new_mesh_source: bpy.types.Object,
) -> bpy.types.Object:
    """Replace `obj`'s mesh data & world transform with `new_mesh_source`, then remove temp source."""
    old_mesh = obj.data
    obj.matrix_world = Matrix.Identity(4)
    obj.data = new_mesh_source.data
    new_mesh_source.data = old_mesh
    ensure_uv_and_color_layers(obj.data, force_smart_uv=False)
    bpy.data.objects.remove(new_mesh_source, do_unlink=True)
    if old_mesh is not None and old_mesh.users == 0:
        bpy.data.meshes.remove(old_mesh)
    return obj


def remove_objects_matching(predicate: Callable[[str], bool]) -> list[str]:
    removed: list[str] = []
    for obj in list(bpy.data.objects):
        if obj.type == "MESH" and predicate(obj.name):
            removed.append(obj.name)
            mesh = obj.data
            bpy.data.objects.remove(obj, do_unlink=True)
            if mesh is not None and mesh.users == 0:
                bpy.data.meshes.remove(mesh)
    return removed


def apply_bevel_and_weighted_normal(
    obj: bpy.types.Object,
    *,
    width: float = 0.012,
    segments: int = 1,
    angle_limit_deg: float = 32.0,
) -> None:
    if obj.type != "MESH" or len(obj.data.polygons) == 0:
        return
    prev = bpy.context.view_layer.objects.active
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bev = obj.modifiers.new(name="V2_Bevel", type="BEVEL")
    bev.width = width
    bev.segments = segments
    bev.limit_method = "ANGLE"
    bev.angle_limit = math.radians(angle_limit_deg)
    bev.harden_normals = False
    try:
        bpy.ops.object.modifier_move_to_index(modifier=bev.name, index=0)
    except RuntimeError:
        pass
    try:
        bpy.ops.object.modifier_apply(modifier=bev.name)
    except RuntimeError:
        if bev.name in obj.modifiers:
            obj.modifiers.remove(bev)
    cleanup_mesh_topology(obj.data)
    ensure_uv_and_color_layers(obj.data, force_smart_uv=True)
    obj.select_set(False)
    if prev and prev.name in bpy.data.objects:
        bpy.context.view_layer.objects.active = prev


# ---------------------------------------------------------------------------
# Sculpting primitives (multi-station lofted shells, faceted cylinders, wedges)
# ---------------------------------------------------------------------------

def make_chamfered_y_loft(
    name: str,
    stations: Sequence[tuple[float, float, float, float, float]],
    material: bpy.types.Material,
    *,
    bevel_ratio: float = 0.22,
    taper_cap_ratio: float = 0.90,
) -> bpy.types.Object:
    """Create a sculpted 8-corner octagonal Y-loft hull/pod with beveled front/rear caps.

    Each station is ``(y, center_x, center_z, half_width, half_height)``.
    Produces crisp multi-station industrial hard-surface contours instead of 6-face cubes.
    """
    if len(stations) < 2:
        raise ValueError(f"{name}: need at least 2 stations")

    br = max(0.05, min(0.45, float(bevel_ratio)))
    # 8-vertex chamfered octagon ring in (x, z) unit coords, CCW around +Y
    unit_ring = (
        (-1.0 + br, -1.0),
        (1.0 - br, -1.0),
        (1.0, -1.0 + br),
        (1.0, 1.0 - br),
        (1.0 - br, 1.0),
        (-1.0 + br, 1.0),
        (-1.0, 1.0 - br),
        (-1.0, -1.0 + br),
    )

    # Add end-cap bevel inset rings if total Y span is large enough
    st = list(stations)
    dy0 = st[1][0] - st[0][0]
    dy1 = st[-1][0] - st[-2][0]
    ext_stations: list[tuple[float, float, float, float, float]] = []
    if dy0 > 0.04:
        y0, cx0, cz0, hw0, hh0 = st[0]
        cap_d = min(0.035, dy0 * 0.25)
        ext_stations.append((y0, cx0, cz0, hw0 * taper_cap_ratio, hh0 * taper_cap_ratio))
        ext_stations.append((y0 + cap_d, cx0, cz0, hw0, hh0))
        ext_stations.extend(st[1:-1])
    else:
        ext_stations.extend(st[:-1])
    if dy1 > 0.04:
        y1, cx1, cz1, hw1, hh1 = st[-1]
        cap_d = min(0.035, dy1 * 0.25)
        ext_stations.append((y1 - cap_d, cx1, cz1, hw1, hh1))
        ext_stations.append((y1, cx1, cz1, hw1 * taper_cap_ratio, hh1 * taper_cap_ratio))
    else:
        ext_stations.append(st[-1])

    ring_n = len(unit_ring)
    verts: list[tuple[float, float, float]] = []
    for y, cx, cz, hw, hh in ext_stations:
        for ux, uz in unit_ring:
            verts.append((cx + ux * hw, y, cz + uz * hh))

    faces: list[tuple[int, ...]] = []
    # Caps
    faces.append(tuple(range(ring_n - 1, -1, -1)))
    last_base = (len(ext_stations) - 1) * ring_n
    faces.append(tuple(last_base + i for i in range(ring_n)))
    # Side strips
    for r in range(len(ext_stations) - 1):
        a = r * ring_n
        b = (r + 1) * ring_n
        for i in range(ring_n):
            ni = (i + 1) % ring_n
            faces.append((a + i, a + ni, b + ni, b + i))

    return create_mesh_object(name, verts, faces, material, smooth=False)


def make_chamfered_box_mesh(
    name: str,
    size: tuple[float, float, float],
    center: tuple[float, float, float],
    material: bpy.types.Material,
    *,
    bevel: float = 0.015,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
) -> bpy.types.Object:
    sx, sy, sz = (max(1e-4, float(v)) for v in size)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(sx, sy, sz), verts=list(bm.verts))
    if bevel > 0.0:
        max_b = min(sx, sy, sz) * 0.35
        bmesh.ops.bevel(
            bm,
            geom=list(bm.edges),
            offset=min(bevel, max_b),
            segments=1,
            affect="EDGES",
            clamp_overlap=True,
        )
    rot_m = Euler(rotation, "XYZ").to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation(Vector(center)) @ rot_m, verts=list(bm.verts))
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))

    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    bm.to_mesh(mesh)
    bm.free()
    for poly in mesh.polygons:
        poly.use_smooth = False
    ensure_uv_and_color_layers(mesh, force_smart_uv=True)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    ensure_single_material(obj, material)
    return obj


def make_faceted_cylinder(
    name: str,
    start: Sequence[float],
    end: Sequence[float],
    radius: float,
    material: bpy.types.Material,
    *,
    segments: int = 16,
    bevel_ends: bool = True,
) -> bpy.types.Object:
    a, b = Vector(start), Vector(end)
    delta = b - a
    length = delta.length
    if length < 1e-5:
        raise ValueError(f"{name}: zero length cylinder")
    axis = delta / length
    up = Vector((0.0, 0.0, 1.0))
    if abs(axis.dot(up)) > 0.98:
        up = Vector((1.0, 0.0, 0.0))
    side = axis.cross(up).normalized()
    perp = axis.cross(side).normalized()

    r = float(radius)
    ring_spec: list[tuple[float, float]]
    if bevel_ends and length > 4.0 * min(0.015, r * 0.25):
        bd = min(0.012, length * 0.12)
        rb = r * 0.84
        ring_spec = [(0.0, rb), (bd, r), (length - bd, r), (length, rb)]
    else:
        ring_spec = [(0.0, r), (length, r)]

    verts: list[tuple[float, float, float]] = []
    for dist, rad in ring_spec:
        c = a + axis * dist
        for i in range(segments):
            ang = 2.0 * math.pi * i / segments
            pt = c + (side * math.cos(ang) + perp * math.sin(ang)) * rad
            verts.append(tuple(pt))

    faces: list[tuple[int, ...]] = [
        tuple(range(segments - 1, -1, -1)),
        tuple((len(ring_spec) - 1) * segments + i for i in range(segments)),
    ]
    for r_idx in range(len(ring_spec) - 1):
        base0 = r_idx * segments
        base1 = (r_idx + 1) * segments
        for i in range(segments):
            ni = (i + 1) % segments
            faces.append((base0 + i, base0 + ni, base1 + ni, base1 + i))

    return create_mesh_object(name, verts, faces, material, smooth=True)


# ---------------------------------------------------------------------------
# Exact Boolean cutters & recessed detail builders
# ---------------------------------------------------------------------------

def apply_exact_boolean(
    target: bpy.types.Object,
    cutter: bpy.types.Object,
    operation: str = "DIFFERENCE",
    *,
    recipe_tag: str | None = None,
) -> bool:
    """Apply Blender 4.5 EXACT Boolean while keeping target's single material & UVs."""
    if target is None or cutter is None or target.type != "MESH" or cutter.type != "MESH":
        return False

    cleanup_mesh_topology(target.data)
    cleanup_mesh_topology(cutter.data)

    host_mat = target.data.materials[0] if target.data.materials else None
    prev_active = bpy.context.view_layer.objects.active
    bpy.context.view_layer.objects.active = target
    target.select_set(True)

    mod = target.modifiers.new(name="V2_ExactBool", type="BOOLEAN")
    mod.operation = operation
    mod.solver = "EXACT"
    if hasattr(mod, "use_self"):
        mod.use_self = False
    if hasattr(mod, "use_hole_tolerant"):
        mod.use_hole_tolerant = True
    mod.object = cutter
    try:
        bpy.ops.object.modifier_move_to_index(modifier=mod.name, index=0)
    except RuntimeError:
        pass

    applied = True
    try:
        bpy.ops.object.modifier_apply(modifier=mod.name)
    except RuntimeError:
        applied = False
        if mod.name in target.modifiers:
            target.modifiers.remove(mod)

    cutter_mesh = cutter.data
    bpy.data.objects.remove(cutter, do_unlink=True)
    if cutter_mesh is not None and cutter_mesh.users == 0:
        bpy.data.meshes.remove(cutter_mesh)

    if applied:
        cleanup_mesh_topology(target.data)
        if host_mat is not None:
            ensure_single_material(target, host_mat)
        ensure_uv_and_color_layers(target.data, force_smart_uv=True)
        if recipe_tag:
            prev_tags = str(target.get(RECIPE_PROP, "")).strip()
            tags = [t for t in prev_tags.split(",") if t]
            if recipe_tag not in tags:
                tags.append(recipe_tag)
            target[RECIPE_PROP] = ",".join(tags)

    target.select_set(False)
    if prev_active and prev_active.name in bpy.data.objects:
        bpy.context.view_layer.objects.active = prev_active
    return applied


def cut_recessed_service_pocket(
    host: bpy.types.Object,
    pocket_center: Sequence[float],
    pocket_size: Sequence[float],
    *,
    louver_mat: bpy.types.Material | None = None,
    louver_name: str | None = None,
    louver_axis: str = "X",
    louver_count: int = 3,
    recipe_tag: str = "pocket",
    rotation: Sequence[float] = (0.0, 0.0, 0.0),
) -> list[bpy.types.Object]:
    """Cut an Exact Boolean recessed cavity in `host` and optionally seat non-overlapping louvers."""
    mat = host.data.materials[0] if host.data.materials else resolve_unit_material("Unit", "Gunmetal")
    cutter = make_chamfered_box_mesh(
        f"_TmpCutter_{host.name}_{recipe_tag}",
        (float(pocket_size[0]), float(pocket_size[1]), float(pocket_size[2])),
        tuple(float(v) for v in pocket_center),
        mat,
        bevel=min(0.012, min(pocket_size) * 0.18),
        rotation=tuple(float(r) for r in rotation),
    )
    ok = apply_exact_boolean(host, cutter, "DIFFERENCE", recipe_tag=recipe_tag)
    created: list[bpy.types.Object] = []
    if not ok or louver_mat is None or louver_name is None or louver_count <= 0:
        return created

    cx, cy, cz = (float(v) for v in pocket_center)
    sx, sy, sz = (float(v) for v in pocket_size)
    axis = louver_axis.upper()
    if axis == "X":
        # Intake on side +-X flank: stack slats along Z or Y
        vane_depth = max(0.016, sx * 0.45)
        vane_len = max(0.03, sy - CUTTER_CLEARANCE * 4.0)
        pitch = (sz - CUTTER_CLEARANCE * 4.0) / max(1, louver_count)
        vane_t = min(0.014, pitch * 0.34)
        for idx in range(louver_count):
            vz = cz - (sz - CUTTER_CLEARANCE * 4.0) * 0.5 + pitch * (idx + 0.5)
            created.append(
                make_chamfered_box_mesh(
                    f"{louver_name}_{idx + 1}",
                    (vane_depth, vane_len, vane_t),
                    (cx, cy, vz),
                    louver_mat,
                    bevel=0.002,
                    rotation=(0.0, math.radians(-20.0 if cx > 0 else 20.0), 0.0),
                )
            )
    elif axis == "Y":
        vane_depth = max(0.016, sy * 0.45)
        vane_len = max(0.03, sx - CUTTER_CLEARANCE * 4.0)
        pitch = (sz - CUTTER_CLEARANCE * 4.0) / max(1, louver_count)
        vane_t = min(0.014, pitch * 0.34)
        for idx in range(louver_count):
            vz = cz - (sz - CUTTER_CLEARANCE * 4.0) * 0.5 + pitch * (idx + 0.5)
            created.append(
                make_chamfered_box_mesh(
                    f"{louver_name}_{idx + 1}",
                    (vane_len, vane_depth, vane_t),
                    (cx, cy, vz),
                    louver_mat,
                    bevel=0.002,
                    rotation=(math.radians(18.0), 0.0, 0.0),
                )
            )
    else:  # "Z" deck pocket
        vane_depth = max(0.014, sz * 0.45)
        vane_len = max(0.03, sx - CUTTER_CLEARANCE * 4.0)
        pitch = (sy - CUTTER_CLEARANCE * 4.0) / max(1, louver_count)
        vane_t = min(0.014, pitch * 0.34)
        for idx in range(louver_count):
            vy = cy - (sy - CUTTER_CLEARANCE * 4.0) * 0.5 + pitch * (idx + 0.5)
            created.append(
                make_chamfered_box_mesh(
                    f"{louver_name}_{idx + 1}",
                    (vane_len, vane_t, vane_depth),
                    (cx, vy, cz),
                    louver_mat,
                    bevel=0.002,
                    rotation=(math.radians(-22.0), 0.0, 0.0),
                )
            )
    return created


def cut_visor_pocket_around_housing(
    host: bpy.types.Object,
    housing: bpy.types.Object,
    *,
    margin: float = 0.004,
    recipe_tag: str = "visor_pocket",
) -> bool:
    """Cut a matching pocket out of `host` so `housing` nests inside without mesh overlap."""
    if host is None or housing is None:
        return False
    x0, x1, y0, y1, z0, z1 = world_bounds(housing)
    hx0, hx1, hy0, hy1, hz0, hz1 = world_bounds(host)
    if x1 < hx0 or x0 > hx1 or y1 < hy0 or y0 > hy1 or z1 < hz0 or z0 > hz1:
        return False
    cx = (x0 + x1) * 0.5
    cy = (y0 + y1) * 0.5
    cz = (z0 + z1) * 0.5
    sx = (x1 - x0) + 2.0 * margin
    sy = (y1 - y0) + 2.0 * margin
    sz = (z1 - z0) + 2.0 * margin
    cutter = make_chamfered_box_mesh(
        f"_TmpVisorCut_{host.name}",
        (sx, sy, sz),
        (cx, cy, cz),
        host.data.materials[0],
        bevel=0.004,
    )
    return apply_exact_boolean(host, cutter, "DIFFERENCE", recipe_tag=recipe_tag)


# ---------------------------------------------------------------------------
# Raycast seating & BVHTree clearance helpers
# ---------------------------------------------------------------------------

def _mesh_bvh(obj: bpy.types.Object) -> tuple[BVHTree, float]:
    deps = bpy.context.evaluated_depsgraph_get()
    eval_obj = obj.evaluated_get(deps)
    mesh = eval_obj.to_mesh()
    mesh.transform(obj.matrix_world)
    verts = [v.co.copy() for v in mesh.vertices]
    polys = [tuple(p.vertices) for p in mesh.polygons]
    eval_obj.to_mesh_clear()
    bvh = BVHTree.FromPolygons(verts, polys, all_triangles=False)
    return bvh, len(verts)


def add_raycast_stay_between(
    name: str,
    origin_obj: bpy.types.Object,
    target_hosts: Sequence[bpy.types.Object],
    anchor_point: Sequence[float],
    ray_dir: Sequence[float],
    *,
    radius: float = 0.018,
    material: bpy.types.Material,
    avoid_obstacles: Sequence[bpy.types.Object] = (),
    embed_depth: float = 0.002,
    max_dist: float = 1.6,
) -> bpy.types.Object | None:
    """Cast a ray from `anchor_point` along `ray_dir` into `target_hosts` to seat a stay cleanly."""
    start = Vector(anchor_point)
    direction = Vector(ray_dir).normalized()
    best_hit: Vector | None = None
    best_dist = max_dist

    for host in target_hosts:
        if host is None or host.type != "MESH" or len(host.data.polygons) == 0:
            continue
        inv_m = host.matrix_world.inverted()
        local_origin = inv_m @ (start + direction * 0.002)
        local_dir = (inv_m.to_3x3() @ direction).normalized()
        ok, hit_loc, _norm, _idx = host.ray_cast(local_origin, local_dir, distance=max_dist)
        if not ok:
            continue
        world_hit = host.matrix_world @ hit_loc
        dist = (world_hit - start).length
        if 0.015 <= dist < best_dist:
            best_dist = dist
            best_hit = world_hit

    if best_hit is None:
        return None

    # Verify stay does not slice through wheel/tire/forbidden obstacles
    for obs in avoid_obstacles:
        if obs is None or obs.type != "MESH":
            continue
        inv_m = obs.matrix_world.inverted()
        local_origin = inv_m @ start
        local_dir = (inv_m.to_3x3() @ direction).normalized()
        ok, hit_loc, _norm, _idx = obs.ray_cast(local_origin, local_dir, distance=best_dist)
        if ok:
            obs_dist = ((obs.matrix_world @ hit_loc) - start).length
            if obs_dist < best_dist - 0.005:
                return None

    p0 = start - direction * embed_depth
    p1 = best_hit + direction * embed_depth
    stay = make_faceted_cylinder(name, p0, p1, radius, material, segments=12, bevel_ends=True)
    stay[RECIPE_PROP] = "raycast_stay"
    return stay


def seat_object_downward_to_target(
    obj: bpy.types.Object,
    targets: Sequence[bpy.types.Object],
    *,
    clearance: float = 0.0,
    max_drop: float = 1.5,
) -> bool:
    """Shift `obj` vertically so its bottom touches the highest hit surface in `targets`."""
    return seat_cluster_downward([obj], targets, clearance=clearance, max_drop=max_drop)


def _shift_objects(objs: Sequence[bpy.types.Object | None], dx: float = 0.0, dy: float = 0.0, dz: float = 0.0) -> None:
    """Translate all valid Blender objects rigidly by (dx, dy, dz) in world space."""
    for o in objs:
        if o is None:
            continue
        o.location.x += dx
        o.location.y += dy
        o.location.z += dz
    bpy.context.view_layer.update()


def seat_cluster_downward(
    objs: Sequence[bpy.types.Object | None],
    targets: Sequence[bpy.types.Object],
    *,
    clearance: float = -0.002,
    max_drop: float = 1.5,
) -> bool:
    """Translate an entire subassembly rigidly along Z so its lowest bounding Z seats on `targets`."""
    valid = [o for o in objs if o is not None and o.type == "MESH"]
    if not valid or not targets:
        return False
    bpy.context.view_layer.update()
    x0 = min(world_bounds(o)[0] for o in valid)
    x1 = max(world_bounds(o)[1] for o in valid)
    y0 = min(world_bounds(o)[2] for o in valid)
    y1 = max(world_bounds(o)[3] for o in valid)
    z0 = min(world_bounds(o)[4] for o in valid)
    z1 = max(world_bounds(o)[5] for o in valid)
    cx = (x0 + x1) * 0.5
    cy = (y0 + y1) * 0.5
    anchor = valid[0]
    ax0, ax1, ay0, ay1, _az0, az1 = world_bounds(anchor)
    acx = (ax0 + ax1) * 0.5
    acy = (ay0 + ay1) * 0.5
    sample_pts = (
        Vector((acx, acy, az1 + 0.08)),
        Vector((ax0 * 0.7 + acx * 0.3, acy, az1 + 0.08)),
        Vector((ax1 * 0.7 + acx * 0.3, acy, az1 + 0.08)),
        Vector((cx, cy, z1 + 0.08)),
    )
    highest_z: float | None = None
    down = Vector((0.0, 0.0, -1.0))
    for origin in sample_pts:
        for t in targets:
            if t is None or t in valid or t.type != "MESH" or len(t.data.polygons) == 0:
                continue
            inv = t.matrix_world.inverted()
            ok, loc, _norm, _idx = t.ray_cast(inv @ origin, (inv.to_3x3() @ down).normalized(), distance=4.0)
            if ok:
                wz = (t.matrix_world @ loc).z
                if wz <= z1 + 0.05 and (highest_z is None or wz > highest_z):
                    highest_z = wz
    if highest_z is None:
        return False
    dz = (highest_z + clearance) - z0
    if abs(dz) > max_drop:
        return False
    _shift_objects(valid, dz=dz)
    return True


def snap_object_to_nearest(
    obj: bpy.types.Object | None,
    targets: Sequence[bpy.types.Object | None],
    *,
    embed: float = 0.004,
    max_dist: float = 0.35,
) -> bool:
    """Shift `obj` along shortest vector toward nearest surface in `targets` so it embeds by `embed`."""
    valid_targets = [t for t in targets if t is not None and t is not obj and t.type == "MESH" and len(t.data.polygons) > 0]
    if obj is None or not valid_targets or obj.type != "MESH" or len(obj.data.vertices) == 0:
        return False
    bpy.context.view_layer.update()
    mw = obj.matrix_world
    pts = [mw @ v.co for v in obj.data.vertices]
    for p in obj.data.polygons:
        pts.append(mw @ p.center)
    best_d = 999.0
    best_delta: Vector | None = None
    for t in valid_targets:
        tbvh, _ = _mesh_bvh(t)
        if tbvh.overlap(_mesh_bvh(obj)[0]):
            return True
        for pt in pts:
            loc, _n, _idx, d = tbvh.find_nearest(pt)
            if loc is not None and d < best_d:
                best_d = d
                best_delta = loc - pt
    if best_delta is None or best_d > max_dist or best_d < 1e-6:
        return False
    dir_v = best_delta.normalized()
    step = best_delta + dir_v * embed
    _shift_objects([obj], dx=step.x, dy=step.y, dz=step.z)
    return True


def build_sculpted_wheel_fender_set(
    unit_prefix: str,
    fender_arch_obj: bpy.types.Object,
    tire_obj: bpy.types.Object,
    hosts: Sequence[bpy.types.Object],
    obstacles: Sequence[bpy.types.Object],
    fender_mat: bpy.types.Material,
    stay_mat: bpy.types.Material,
    skirt_mat: bpy.types.Material,
) -> list[bpy.types.Object]:
    """Upgrade a blockout wheel fender ArchTop/FlareLip/Mudflaps + add raycast stays."""
    if fender_arch_obj is None or tire_obj is None:
        return []

    tx0, tx1, ty0, ty1, tz0, tz1 = world_bounds(tire_obj)
    tcx = (tx0 + tx1) * 0.5
    tcy = (ty0 + ty1) * 0.5
    tcz = (tz0 + tz1) * 0.5
    tire_r = max((ty1 - ty0) * 0.5, (tz1 - tz0) * 0.5)
    tread_blocks = bpy.data.objects.get(tire_obj.name.replace("_TireCarcass", "_TreadBlocks"))
    if tread_blocks is not None and tread_blocks.type == "MESH":
        bx0, bx1, by0, by1, bz0, bz1 = world_bounds(tread_blocks)
        tx0, tx1 = min(tx0, bx0), max(tx1, bx1)
        tire_r = max(tire_r, (by1 - by0) * 0.5, (bz1 - bz0) * 0.5)
    inner_r = tire_r + 0.044
    outer_r = inner_r + 0.036

    fx0, fx1, _fy0, _fy1, _fz0, _fz1 = world_bounds(fender_arch_obj)
    side = -1 if tcx < 0.0 else 1
    arc_x0 = min(fx0, tx0) if side < 0 else min(fx0, tx0)
    arc_x1 = max(fx1, tx1) if side > 0 else max(fx1, tx1)
    # Keep inner edge tight to hull without pinching
    if side < 0:
        arc_x0 = min(tx0 - 0.02, fx0)
        arc_x1 = max(fx1, tx1 + 0.02)
    else:
        arc_x0 = min(fx0, tx0 - 0.02)
        arc_x1 = max(tx1 + 0.02, fx1)

    angles = [math.radians(a) for a in (-56, -34, -12, 12, 34, 56)]
    verts: list[tuple[float, float, float]] = []
    for ang in angles:
        sy = math.sin(ang)
        cz = math.cos(ang)
        for rx in (arc_x0, arc_x1):
            verts.append((rx, tcy + sy * inner_r, tcz + cz * inner_r))
            verts.append((rx, tcy + sy * outer_r, tcz + cz * outer_r))

    faces: list[tuple[int, ...]] = []
    n_arc = len(angles)
    # Quads per step: each angle has 4 vertices:
    # 0=(x0,in), 1=(x0,out), 2=(x1,in), 3=(x1,out)
    for k in range(n_arc - 1):
        a = k * 4
        b = (k + 1) * 4
        faces.append((a + 1, a + 3, b + 3, b + 1))  # outer skin
        faces.append((a + 0, b + 0, b + 2, a + 2))  # underside
        faces.append((a + 0, a + 1, b + 1, b + 0))  # -X side
        faces.append((a + 2, b + 2, b + 3, a + 3))  # +X side
    faces.append((0, 2, 3, 1))
    last = (n_arc - 1) * 4
    faces.append((last + 0, last + 1, last + 3, last + 2))

    sculpted = create_mesh_object(f"{fender_arch_obj.name}_V2Tmp", verts, faces, fender_mat, smooth=False)
    replace_object_geometry(fender_arch_obj, sculpted)
    apply_bevel_and_weighted_normal(fender_arch_obj, width=0.008, segments=1, angle_limit_deg=35.0)
    fender_arch_obj[RECIPE_PROP] = "sculpted_fender_arch"

    # Reshape sibling FlareLip & Mudflaps cleanly so they align with the curved arch
    base = fender_arch_obj.name.rsplit("_ArchTop", 1)[0]
    flare = bpy.data.objects.get(f"{base}_FlareLip")
    if flare is not None:
        flare_verts: list[tuple[float, float, float]] = []
        edge_x0 = (arc_x0 - 0.028) if side < 0 else (arc_x1 - 0.018)
        edge_x1 = (arc_x0 + 0.018) if side < 0 else (arc_x1 + 0.028)
        flare_in = outer_r - 0.022
        flare_out = outer_r + 0.016
        for ang in angles:
            sy = math.sin(ang)
            cz = math.cos(ang)
            for rx in (edge_x0, edge_x1):
                flare_verts.append((rx, tcy + sy * flare_in, tcz + cz * flare_in))
                flare_verts.append((rx, tcy + sy * flare_out, tcz + cz * flare_out))
        tmp_flare = create_mesh_object(f"{flare.name}_V2Tmp", flare_verts, faces, fender_mat, smooth=False)
        replace_object_geometry(flare, tmp_flare)
        apply_bevel_and_weighted_normal(flare, width=0.005, segments=1)
        flare[RECIPE_PROP] = "sculpted_flare_lip"

    for flap_suffix, sign_ang in (("FrontMudflap", 1.0), ("RearMudflap", -1.0)):
        flap = bpy.data.objects.get(f"{base}_{flap_suffix}")
        if flap is None:
            continue
        ang_top = math.radians(58.0 * sign_ang)
        py_top = tcy + math.sin(ang_top) * (outer_r - 0.008)
        pz_top = tcz + math.cos(ang_top) * (outer_r - 0.008)
        flap_h = max(0.15, pz_top - (tcz + math.cos(math.radians(76.0)) * outer_r)) + 0.035
        fy = py_top + sign_ang * 0.018
        fz = pz_top - flap_h * 0.36
        tmp_flap = make_chamfered_box_mesh(
            f"{flap.name}_V2Tmp",
            (abs(arc_x1 - arc_x0) * 0.94, 0.028, flap_h),
            ((arc_x0 + arc_x1) * 0.5, fy, fz),
            skirt_mat,
            bevel=0.006,
            rotation=(math.radians(-14.0 * sign_ang), 0.0, 0.0),
        )
        replace_object_geometry(flap, tmp_flap)
        snap_object_to_nearest(flap, [fender_arch_obj, flare], embed=0.006)
        flap[RECIPE_PROP] = "sculpted_mudflap"

    # Seat 2 raycast bracket stays from underside of arch toward inner hull hosts
    stays: list[bpy.types.Object] = []
    inner_x = arc_x1 - 0.03 if side < 0 else arc_x0 + 0.03
    for s_idx, ang_deg in enumerate((-28.0, 28.0), start=1):
        ang = math.radians(ang_deg)
        anchor = (inner_x, tcy + math.sin(ang) * (inner_r + 0.012), tcz + math.cos(ang) * (inner_r + 0.012))
        stay_dir = (-side * 1.0, 0.0, -0.22)
        stay = add_raycast_stay_between(
            f"{base}_Stay_{s_idx}",
            fender_arch_obj,
            hosts,
            anchor,
            stay_dir,
            radius=0.016,
            material=stay_mat,
            avoid_obstacles=obstacles,
        )
        if stay is not None:
            stays.append(stay)
    return stays


# ---------------------------------------------------------------------------
# Unit V2 Sculpting + Exact Boolean Recipes
# ---------------------------------------------------------------------------

def apply_arc_lance_walker_v2() -> list[str]:
    """Sculpt & detail ArcLanceWalker into v2: armored torso pockets, faceted greaves/claws, emitter cowl."""
    prefix = "ArcLanceWalker"
    p_armor = resolve_unit_material(prefix, "PrimaryArmor")
    l_armor = resolve_unit_material(prefix, "LightArmor")
    gunmetal = resolve_unit_material(prefix, "Gunmetal")
    warning = resolve_unit_material(prefix, "Warning")
    emissive = resolve_unit_material(prefix, "Emissive")
    notes: list[str] = []

    # 1. Replace Torso blockout with sculpted 5-station armored carapace + side intake pockets
    torso = bpy.data.objects.get("ArcLanceWalkerArmor_Torso")
    if torso is not None:
        stations = (
            (-0.34, 0.0, 1.94, 0.42, 0.30),
            (-0.18, 0.0, 1.97, 0.56, 0.38),
            (0.06, 0.0, 1.98, 0.58, 0.40),
            (0.26, 0.0, 1.97, 0.52, 0.37),
            (0.38, 0.0, 1.95, 0.40, 0.30),
        )
        new_torso = make_chamfered_y_loft("ArcLanceWalkerArmor_Torso_Tmp", stations, p_armor, bevel_ratio=0.25)
        replace_object_geometry(torso, new_torso)
        # Cut recessed side intake pockets (left/right) and seat angled cooling louvers
        for side, tag in ((-1, "L"), (1, "R")):
            cut_recessed_service_pocket(
                torso,
                (side * 0.54, 0.02, 1.98),
                (0.08, 0.28, 0.24),
                louver_mat=gunmetal,
                louver_name=f"ArcLanceWalkerArmor_TorsoIntakeLouver_{tag}",
                louver_axis="X",
                louver_count=3,
                recipe_tag=f"torso_intake_{tag}",
            )
        # Cut rear powercell socket collar rebate so PowerCell docks cleanly without raw box overlap
        cut_recessed_service_pocket(
            torso,
            (0.0, -0.34, 1.98),
            (0.56, 0.12, 0.44),
            recipe_tag="powercell_rebate",
        )
        visor_housing = bpy.data.objects.get("ArcLanceWalkerSensor_Visor_Housing")
        cut_visor_pocket_around_housing(torso, visor_housing, margin=0.008, recipe_tag="torso_visor_pocket")
        notes.append("Torso sculpted + 3 Exact Boolean cuts")

    # 2. ChestPlate sculpted frontal wedge + Exact Boolean visor embrasure
    chest = bpy.data.objects.get("ArcLanceWalkerArmor_ChestPlate")
    if chest is not None:
        stations = (
            (0.31, 0.0, 2.02, 0.49, 0.23),
            (0.42, 0.0, 2.03, 0.47, 0.21),
            (0.53, 0.0, 2.02, 0.36, 0.17),
        )
        new_chest = make_chamfered_y_loft("ArcLanceWalkerArmor_ChestPlate_Tmp", stations, l_armor, bevel_ratio=0.26)
        replace_object_geometry(chest, new_chest)
        visor_housing = bpy.data.objects.get("ArcLanceWalkerSensor_Visor_Housing")
        cut_visor_pocket_around_housing(chest, visor_housing, margin=0.008, recipe_tag="chest_visor_pocket")
        # Carve frontal sternum heat-relief slot
        cut_recessed_service_pocket(
            chest,
            (0.0, 0.515, 2.04),
            (0.24, 0.06, 0.14),
            louver_mat=warning,
            louver_name="ArcLanceWalkerArmor_ChestVentSlat",
            louver_axis="Y",
            louver_count=2,
            recipe_tag="chest_vent_pocket",
        )
        notes.append("ChestPlate sculpted + visor & vent Boolean cuts")

    # 3. PowerCell capacitor backpack with Exact Boolean cooling slots revealing plasma core ribs
    powercell = bpy.data.objects.get("ArcLanceWalkerArmor_PowerCell")
    if powercell is not None:
        stations = (
            (-0.72, 0.0, 2.02, 0.28, 0.25),
            (-0.56, 0.0, 2.03, 0.36, 0.30),
            (-0.32, 0.0, 2.03, 0.36, 0.30),
        )
        new_pc = make_chamfered_y_loft("ArcLanceWalkerArmor_PowerCell_Tmp", stations, gunmetal, bevel_ratio=0.25)
        replace_object_geometry(powercell, new_pc)
        for slot_i, sx in enumerate((-0.16, 0.0, 0.16), start=1):
            cut_recessed_service_pocket(
                powercell,
                (sx, -0.71, 2.03),
                (0.065, 0.07, 0.32),
                louver_mat=emissive if slot_i == 2 else warning,
                louver_name=f"ArcLanceWalkerArmor_PowerCellCellFin_{slot_i}",
                louver_axis="Y",
                louver_count=2,
                recipe_tag=f"powercell_slot_{slot_i}",
            )
        notes.append("PowerCell sculpted + 3 radiator Boolean slots")

    # 4. Remove internal buried torso gussets; upgrade Biped Legs & connect Foot/Ankle/ShinAccent/ArcEmitter Prongs
    remove_objects_matching(lambda n: n in ("ArcLanceWalkerArmor_Gusset_1", "ArcLanceWalkerArmor_Gusset_2"))

    for side_sign, tag in ((-1.0, "L"), (1.0, "R")):
        thigh = bpy.data.objects.get(f"ArcLanceWalkerLeg_ThighArmor_{tag}")
        if thigh is not None:
            cx = side_sign * 0.50
            stations = (
                (-0.32, cx, 1.15, 0.14, 0.22),
                (-0.09, cx, 1.15, 0.19, 0.26),
                (0.13, cx, 1.14, 0.14, 0.20),
            )
            replace_object_geometry(thigh, make_chamfered_y_loft(f"{thigh.name}_Tmp", stations, p_armor, bevel_ratio=0.24))
            cut_recessed_service_pocket(
                thigh,
                (cx + side_sign * 0.18, -0.09, 1.15),
                (0.04, 0.18, 0.20),
                recipe_tag=f"thigh_relief_{tag}",
            )

        shin = bpy.data.objects.get(f"ArcLanceWalkerLeg_ShinArmor_{tag}")
        if shin is not None:
            cx = side_sign * 0.54
            stations = (
                (-0.26, cx, 0.57, 0.10, 0.20),
                (-0.03, cx, 0.58, 0.135, 0.25),
                (0.20, cx, 0.56, 0.11, 0.20),
            )
            replace_object_geometry(shin, make_chamfered_y_loft(f"{shin.name}_Tmp", stations, p_armor, bevel_ratio=0.26))
            # Shift ShinAccent onto front shin face, carve Exact Boolean pocket around it, then seat back into pocket floor
            accent = bpy.data.objects.get(f"ArcLanceWalkerLeg_ShinAccent_{tag}")
            if accent is not None:
                sx0, sx1, sy0, sy1, sz0, sz1 = world_bounds(shin)
                ax0, ax1, ay0, ay1, az0, az1 = world_bounds(accent)
                accent.location.y += (sy1 - ay1) + 0.014
                bpy.context.view_layer.update()
                cut_visor_pocket_around_housing(shin, accent, margin=0.002, recipe_tag=f"shin_accent_{tag}")
                accent.location.y -= 0.004
                bpy.context.view_layer.update()

        knee = bpy.data.objects.get(f"ArcLanceWalkerLeg_KneeCapArmor_{tag}")
        if knee is not None:
            cx = side_sign * 0.56
            stations = (
                (-0.23, cx, 0.86, 0.08, 0.08),
                (-0.15, cx, 0.87, 0.11, 0.115),
                (-0.08, cx, 0.86, 0.075, 0.08),
            )
            replace_object_geometry(knee, make_chamfered_y_loft(f"{knee.name}_Tmp", stations, p_armor, bevel_ratio=0.28))

        calf = bpy.data.objects.get(f"ArcLanceWalkerLeg_CalfArmor_{tag}")
        if calf is not None:
            cx = side_sign * 0.54
            stations = (
                (-0.20, cx, 0.58, 0.075, 0.11),
                (-0.11, cx, 0.59, 0.095, 0.145),
                (-0.03, cx, 0.58, 0.075, 0.11),
            )
            replace_object_geometry(calf, make_chamfered_y_loft(f"{calf.name}_Tmp", stations, l_armor, bevel_ratio=0.25))

        foot = bpy.data.objects.get(f"ArcLanceWalkerLeg_FootBase_{tag}")
        ankle = bpy.data.objects.get(f"ArcLanceWalkerLeg_AnkleJoint_{tag}")
        if foot is not None:
            cx = side_sign * 0.52
            stations = (
                (0.015, cx, 0.06, 0.13, 0.06),
                (0.135, cx, 0.06, 0.135, 0.06),
                (0.255, cx, 0.055, 0.11, 0.055),
            )
            replace_object_geometry(foot, make_chamfered_y_loft(f"{foot.name}_Tmp", stations, gunmetal, bevel_ratio=0.22))
            if ankle is not None:
                acx, acy, acz = world_center(ankle)
                make_faceted_cylinder(
                    f"ArcLanceWalkerLeg_AnklePylon_{tag}",
                    (cx, 0.125, 0.105),
                    (acx, acy, acz),
                    0.048,
                    gunmetal,
                    segments=12,
                )

        for toe_name, cx_off, y0_t, y1_t, hw_t, hh_t in (
            (f"ArcLanceWalkerLeg_ToeCenter_{tag}", side_sign * 0.52, 0.25, 0.50, 0.08, 0.045),
            (f"ArcLanceWalkerLeg_ToeOutrigger_{tag}", side_sign * 0.67, 0.18, 0.42, 0.075, 0.04),
            (f"ArcLanceWalkerLeg_HeelClaw_{tag}", side_sign * 0.52, -0.115, 0.02, 0.085, 0.04),
        ):
            toe = bpy.data.objects.get(toe_name)
            if toe is not None:
                stations = (
                    (y0_t, cx_off, hh_t, hw_t, hh_t),
                    ((y0_t + y1_t) * 0.5, cx_off, hh_t * 0.92, hw_t * 0.85, hh_t * 0.9),
                    (y1_t, cx_off, hh_t * 0.55, hw_t * 0.55, hh_t * 0.55),
                )
                mat = p_armor if "Toe" in toe_name else gunmetal
                replace_object_geometry(toe, make_chamfered_y_loft(f"{toe_name}_Tmp", stations, mat, bevel_ratio=0.25))

    # Connect floating ArcEmitter Prongs back into InductionCoil_2/InsulatorRing with gunmetal rail mounts
    coil = bpy.data.objects.get("ArcLanceWalkerArcEmitter_InductionCoil_2")
    if coil is not None:
        _ccx, ccy, ccz = world_center(coil)
        for p_idx in range(1, 4):
            prong = bpy.data.objects.get(f"ArcLanceWalkerArcEmitter_Prong_{p_idx}")
            if prong is None:
                continue
            mw = prong.matrix_world
            wverts = [mw @ v.co for v in prong.data.vertices]
            py_min = min(v.y for v in wverts)
            rear_verts = [v for v in wverts if v.y <= py_min + 0.08] or wverts
            rc = sum(rear_verts, Vector((0.0, 0.0, 0.0))) / len(rear_verts)
            make_faceted_cylinder(
                f"ArcLanceWalkerArcEmitter_ProngBracket_{p_idx}",
                (0.0, ccy - 0.02, ccz),
                (rc.x, rc.y + 0.02, rc.z),
                0.024,
                gunmetal,
                segments=10,
            )

    # 5. ArcEmitter_Pedestal: chamfered cradle + Exact Boolean cooling vents
    pedestal = bpy.data.objects.get("ArcLanceWalkerArcEmitter_Pedestal")
    if pedestal is not None:
        stations = (
            (0.17, 0.0, 1.25, 0.22, 0.16),
            (0.40, 0.0, 1.25, 0.28, 0.20),
            (0.63, 0.0, 1.25, 0.24, 0.17),
        )
        replace_object_geometry(pedestal, make_chamfered_y_loft("ArcLanceWalkerArcEmitter_Pedestal_Tmp", stations, p_armor, bevel_ratio=0.25))
        for side, tag in ((-1, "L"), (1, "R")):
            cut_recessed_service_pocket(
                pedestal,
                (side * 0.27, 0.40, 1.25),
                (0.05, 0.18, 0.16),
                louver_mat=gunmetal,
                louver_name=f"ArcLanceWalkerArcEmitter_CoolingVane_{tag}",
                louver_axis="X",
                louver_count=2,
                recipe_tag=f"emitter_vent_{tag}",
            )
    notes.append("Legs, claws & emitter pedestal upgraded")
    return notes


def apply_tidebreaker_hovercraft_v2() -> list[str]:
    """Upgrade TidebreakerHovercraft with segmented skirt fingers, aerofoil rudders, hydro bow, and Exact Boolean intakes."""
    prefix = "TidebreakerHovercraft"
    p_armor = resolve_unit_material(prefix, "PrimaryArmor")
    l_armor = resolve_unit_material(prefix, "LightArmor")
    gunmetal = resolve_unit_material(prefix, "Gunmetal")
    warning = resolve_unit_material(prefix, "Warning")
    tread = resolve_unit_material(prefix, "Tread")
    notes: list[str] = []

    hull = bpy.data.objects.get("TidebreakerHovercraftArmor_Hull")
    deck = bpy.data.objects.get("TidebreakerHovercraftArmor_UpperDeck")
    # 1. BowRamp sculpted hydro-wedge
    bow = bpy.data.objects.get("TidebreakerHovercraftArmor_BowRamp")
    if bow is not None:
        stations = (
            (1.83, 0.0, 0.72, 0.78, 0.26),
            (2.04, 0.0, 0.78, 0.74, 0.21),
            (2.21, 0.0, 0.83, 0.64, 0.13),
        )
        replace_object_geometry(bow, make_chamfered_y_loft("TidebreakerHovercraftArmor_BowRamp_Tmp", stations, l_armor, bevel_ratio=0.22))
        cut_recessed_service_pocket(
            bow,
            (0.0, 2.15, 0.85),
            (0.56, 0.12, 0.08),
            recipe_tag="bow_ram_recess",
        )
        notes.append("BowRamp sculpted + centerline tow recess cut")

    # 2. Hull + UpperDeck Exact Boolean flank plenum intakes and engine deck louver pocket
    if hull is not None:
        for side, tag in ((-1, "L"), (1, "R")):
            cut_recessed_service_pocket(
                hull,
                (side * 0.80, -0.35, 0.62),
                (0.08, 0.42, 0.16),
                louver_mat=gunmetal,
                louver_name=f"TidebreakerHovercraftHull_FlankPlenumLouver_{tag}",
                louver_axis="X",
                louver_count=3,
                recipe_tag=f"flank_intake_{tag}",
            )
    if deck is not None:
        cut_recessed_service_pocket(
            deck,
            (0.0, -1.01, 0.99),
            (0.56, 0.40, 0.06),
            recipe_tag="engine_louver_well",
        )
        notes.append("Hull & UpperDeck Boolean intake/louver pockets cut")

    # 3. RepulsorSkirt sculpted beveled cushion skirt + segmented air-cushion finger ribs
    skirt = bpy.data.objects.get("TidebreakerHovercraftHover_RepulsorSkirt")
    if skirt is not None:
        stations = (
            (-1.72, 0.0, 0.42, 1.06, 0.11),
            (-1.28, 0.0, 0.42, 1.24, 0.11),
            (1.28, 0.0, 0.42, 1.24, 0.11),
            (1.72, 0.0, 0.42, 1.06, 0.11),
        )
        replace_object_geometry(skirt, make_chamfered_y_loft("TidebreakerHovercraftHover_RepulsorSkirt_Tmp", stations, p_armor, bevel_ratio=0.20))
        # Seat segmented rubber cushion fingers around flanks without exceeding bumper bounds
        for side, tag in ((-1, "L"), (1, "R")):
            for idx, fy in enumerate((-1.15, -0.70, -0.25, 0.20, 0.65, 1.10), start=1):
                make_chamfered_box_mesh(
                    f"TidebreakerHovercraftHover_SkirtFinger_{tag}_{idx}",
                    (0.06, 0.34, 0.16),
                    (side * 1.22, fy, 0.37),
                    tread,
                    bevel=0.014,
                    rotation=(0.0, math.radians(-12.0 if side > 0 else 12.0), 0.0),
                )
        notes.append("RepulsorSkirt + 12 segmented rubber cushion fingers")

    # 4. Rudder_L/R aerofoil vertical stabilizers + VectoringExhaust recessed thruster shrouds
    for side, tag in ((-1, "L"), (1, "R")):
        rudder = bpy.data.objects.get(f"TidebreakerHovercraftArmor_Rudder_{tag}")
        if rudder is not None:
            sx = side * 0.78
            stations = (
                (-2.39, sx, 1.55, 0.012, 0.30),
                (-2.25, sx, 1.55, 0.028, 0.35),
                (-2.11, sx, 1.55, 0.022, 0.35),
            )
            replace_object_geometry(rudder, make_chamfered_y_loft(f"{rudder.name}_Tmp", stations, warning, bevel_ratio=0.30))

        exh = bpy.data.objects.get(f"TidebreakerHovercraftHover_VectoringExhaust_{tag}")
        noz = bpy.data.objects.get(f"TidebreakerHovercraftHover_VectoringNozzle_{tag}")
        if exh is not None:
            sx = side * 0.70
            stations = (
                (-2.04, sx, 0.54, 0.13, 0.09),
                (-1.70, sx, 0.54, 0.145, 0.095),
            )
            replace_object_geometry(exh, make_chamfered_y_loft(f"{exh.name}_Tmp", stations, l_armor, bevel_ratio=0.25))
            cut_visor_pocket_around_housing(exh, noz, margin=0.005, recipe_tag=f"exhaust_noz_pocket_{tag}")

        strake = bpy.data.objects.get(f"TidebreakerHovercraftHover_StabilizerStrake_{tag}")
        if strake is not None:
            sx = side * 1.40
            stations = (
                (-1.46, sx, 0.50, 0.06, 0.045),
                (-0.40, sx, 0.50, 0.095, 0.05),
                (1.07, sx, 0.50, 0.07, 0.04),
            )
            replace_object_geometry(strake, make_chamfered_y_loft(f"{strake.name}_Tmp", stations, l_armor, bevel_ratio=0.25))
            if hull is not None:
                for k, sy in enumerate((-0.95, 0.25), start=1):
                    add_raycast_stay_between(
                        f"TidebreakerHovercraftHover_StrakePylon_{tag}_{k}",
                        strake,
                        [hull, skirt] if skirt else [hull],
                        (sx - side * 0.04, sy, 0.50),
                        (-side, 0.0, 0.0),
                        radius=0.022,
                        material=gunmetal,
                    )

    # 5. PilotHouse visor slot cut & connect floating stern Rudders, LiftLugs, Antenna, and Smoke_R
    pilot = bpy.data.objects.get("TidebreakerHovercraftArmor_PilotHouse")
    if pilot is not None:
        cut_recessed_service_pocket(
            pilot,
            (-0.70, 1.01, 1.38),
            (0.52, 0.06, 0.14),
            louver_mat=resolve_unit_material(prefix, "Emissive"),
            louver_name="TidebreakerHovercraftSensor_BridgeViewport",
            louver_axis="Y",
            louver_count=1,
            recipe_tag="pilothouse_viewport",
        )
        notes.append("PilotHouse Exact Boolean viewport cut + seated optic strip")

    prop_hosts = [o for o in bpy.data.objects if o.type == "MESH" and ("PropDuct" in o.name or "PropPylon" in o.name or o is hull)]
    for side, tag in ((-1, "L"), (1, "R")):
        rudder = bpy.data.objects.get(f"TidebreakerHovercraftArmor_Rudder_{tag}")
        if rudder is not None and prop_hosts:
            rx0, rx1, ry0, ry1, rz0, rz1 = world_bounds(rudder)
            rcx = (rx0 + rx1) * 0.5
            rcy = (ry0 + ry1) * 0.5
            for b_idx, bz in enumerate((1.30, 1.56), start=1):
                src_pt = Vector((rcx, ry1 - 0.04, bz))
                best_loc = None
                best_d = 999.0
                for h in prop_hosts:
                    hbvh, _ = _mesh_bvh(h)
                    loc, _n, _idx, d = hbvh.find_nearest(src_pt)
                    if loc is not None and d < best_d:
                        best_d = d
                        best_loc = loc
                if best_loc is not None and best_d < 0.8:
                    dir_v = (best_loc - src_pt).normalized()
                    make_faceted_cylinder(
                        f"TidebreakerHovercraftArmor_RudderBoom_{tag}_{b_idx}",
                        src_pt - dir_v * 0.03,
                        best_loc + dir_v * 0.03,
                        0.024,
                        gunmetal,
                        segments=10,
                    )
        lug = bpy.data.objects.get(f"TidebreakerHovercraftHull_Greeble_GreebleLiftLug_{tag}R")
        if lug is not None and prop_hosts:
            lc = Vector(world_center(lug))
            best_loc = None
            best_d = 999.0
            for h in prop_hosts:
                if h is lug:
                    continue
                hbvh, _ = _mesh_bvh(h)
                loc, _n, _idx, d = hbvh.find_nearest(lc)
                if loc is not None and d < best_d:
                    best_d = d
                    best_loc = loc
            if best_loc is not None and best_d < 0.5:
                delta = best_loc - lc
                _shift_objects([lug], dx=delta.x * 0.92, dy=delta.y * 0.92, dz=delta.z * 0.92)

    deck_hosts = [o for o in (hull, bpy.data.objects.get("TidebreakerHovercraftArmor_UpperDeck"), bpy.data.objects.get("TidebreakerHovercraftArmor_TurretRingGuard_YawRaceLip")) if o is not None]
    collar = bpy.data.objects.get("TidebreakerHovercraftSensor_AntennaDeckCollar")
    if collar is not None and deck_hosts:
        seat_cluster_downward([collar], deck_hosts, clearance=-0.004)

    whip_objs = [
        bpy.data.objects.get(n)
        for n in (
            "TidebreakerHovercraftSensor_WhipAntenna_Bracket",
            "TidebreakerHovercraftSensor_WhipAntenna_SpringCoil_0",
            "TidebreakerHovercraftSensor_WhipAntenna_SpringCoil_1",
            "TidebreakerHovercraftSensor_WhipAntenna_SpringCoil_2",
            "TidebreakerHovercraftSensor_WhipAntenna_Mast",
            "TidebreakerHovercraftSensor_WhipAntenna_Tip",
        )
    ]
    whip_objs = [o for o in whip_objs if o is not None]
    if whip_objs and deck_hosts:
        seat_cluster_downward(whip_objs, deck_hosts, clearance=-0.004)
        bx0, bx1, by0, by1, bz0, bz1 = world_bounds(whip_objs[0])
        mast = bpy.data.objects.get("TidebreakerHovercraftSensor_WhipAntenna_Mast")
        mz0 = world_bounds(mast)[4] if mast is not None else bz1 + 0.08
        make_faceted_cylinder(
            "TidebreakerHovercraftSensor_WhipAntenna_CoreRod",
            ((bx0 + bx1) * 0.5, (by0 + by1) * 0.5, bz0 + 0.01),
            ((bx0 + bx1) * 0.5, (by0 + by1) * 0.5, mz0 + 0.02),
            0.011,
            gunmetal,
            segments=8,
        )

    smoke_r = [
        o for o in bpy.data.objects
        if o.type == "MESH" and o.name.startswith("TidebreakerHovercraftSmoke_R_")
    ]
    smoke_base = bpy.data.objects.get("TidebreakerHovercraftSmoke_R_MountBase")
    if smoke_base is not None and hull is not None:
        ordered_smoke = [smoke_base] + [o for o in smoke_r if o is not smoke_base]
        seat_cluster_downward(ordered_smoke, [hull], clearance=-0.004, max_drop=1.5)

    return notes


def apply_field_medic_rover_v2() -> list[str]:
    """Upgrade FieldMedicRover: sculpted armored trauma cab, sloped nose grille, curved fenders & stays."""
    prefix = "FieldMedicRover"
    p_armor = resolve_unit_material(prefix, "PrimaryArmor")
    l_armor = resolve_unit_material(prefix, "LightArmor")
    gunmetal = resolve_unit_material(prefix, "Gunmetal")
    tread = resolve_unit_material(prefix, "Tread")
    emissive = resolve_unit_material(prefix, "Emissive")
    notes: list[str] = []

    spine = bpy.data.objects.get("FieldMedicRoverArmor_SpineChassis")
    nose = bpy.data.objects.get("FieldMedicRoverArmor_FrontNose")
    engine = bpy.data.objects.get("FieldMedicRoverArmor_EngineBlock")

    # 1. Sculpted armored trauma cab nested cleanly inside RollCage rails without blocking TurretRingGuard
    if not bpy.data.objects.get("FieldMedicRoverArmor_TraumaCabShell"):
        cab_stations = (
            (-0.72, 0.0, 0.99, 0.42, 0.34),
            (-0.10, 0.0, 1.00, 0.44, 0.35),
            (0.32, 0.0, 0.98, 0.405, 0.31),
            (0.76, 0.0, 0.80, 0.355, 0.20),
        )
        cab = make_chamfered_y_loft("FieldMedicRoverArmor_TraumaCabShell", cab_stations, p_armor, bevel_ratio=0.24)
        for side, tag in ((-1, "L"), (1, "R")):
            cut_recessed_service_pocket(
                cab,
                (side * 0.425, -0.25, 0.99),
                (0.06, 0.32, 0.24),
                louver_mat=l_armor,
                louver_name=f"FieldMedicRoverArmor_TraumaDoorSlat_{tag}",
                louver_axis="X",
                louver_count=3,
                recipe_tag=f"trauma_locker_{tag}",
            )
        # Windshield visor recess
        cut_recessed_service_pocket(
            cab,
            (0.0, 0.56, 0.96),
            (0.54, 0.12, 0.13),
            louver_mat=emissive,
            louver_name="FieldMedicRoverSensor_CabWindshieldStrip",
            louver_axis="Y",
            louver_count=1,
            recipe_tag="cab_windshield",
        )
        notes.append("TraumaCabShell sculpted + 3 Exact Boolean pockets")

    # 2. FrontNose sculpted hood with Exact Boolean radiator grille
    if nose is not None:
        nose_stations = (
            (0.61, 0.0, 0.65, 0.36, 0.22),
            (1.18, 0.0, 0.64, 0.34, 0.20),
            (1.49, 0.0, 0.61, 0.28, 0.16),
        )
        replace_object_geometry(nose, make_chamfered_y_loft("FieldMedicRoverArmor_FrontNose_Tmp", nose_stations, l_armor, bevel_ratio=0.25))
        cut_recessed_service_pocket(
            nose,
            (0.0, 1.48, 0.62),
            (0.40, 0.06, 0.18),
            louver_mat=gunmetal,
            louver_name="FieldMedicRoverArmor_GrilleLouver",
            louver_axis="Y",
            louver_count=3,
            recipe_tag="front_grille",
        )
        notes.append("FrontNose sculpted + radiator grille Boolean cut")

    # 3. Sculpted wheel arch fenders + raycast stays clearing tires
    hosts = [o for o in (spine, bpy.data.objects.get("FieldMedicRoverArmor_TraumaCabShell"), engine, nose) if o is not None]
    tires = [o for o in bpy.data.objects if o.type == "MESH" and ("TireCarcass" in o.name or "WheelRim" in o.name)]
    fender_map = (
        ("FieldMedicRoverFender_1_L_ArchTop", "FieldMedicRoverWheel_Rear_L_TireCarcass"),
        ("FieldMedicRoverFender_2_L_ArchTop", "FieldMedicRoverWheel_Front_L_TireCarcass"),
        ("FieldMedicRoverFender_3_R_ArchTop", "FieldMedicRoverWheel_Rear_R_TireCarcass"),
        ("FieldMedicRoverFender_4_R_ArchTop", "FieldMedicRoverWheel_Front_R_TireCarcass"),
    )
    stay_total = 0
    for arch_name, tire_name in fender_map:
        stays = build_sculpted_wheel_fender_set(
            prefix,
            bpy.data.objects.get(arch_name),
            bpy.data.objects.get(tire_name),
            hosts,
            tires,
            l_armor,
            gunmetal,
            tread,
        )
        stay_total += len(stays)

    # 4. DualGun_MantletBlock Exact Boolean breech collar pockets & connect RollCage / deck accessories
    mantlet = bpy.data.objects.get("FieldMedicRoverDualGun_MantletBlock")
    if mantlet is not None:
        for side, tag in ((-1, "L"), (1, "R")):
            cut_recessed_service_pocket(
                mantlet,
                (side * 0.19, 0.78, 1.50),
                (0.18, 0.12, 0.18),
                recipe_tag=f"dual_breech_port_{tag}",
            )

    cab_shell = bpy.data.objects.get("FieldMedicRoverArmor_TraumaCabShell")
    if cab_shell is not None and spine is not None:
        # Anchor RollCage pillars & RoofPlate solidly into TraumaCabShell and SpineChassis
        for side, tag in ((-1.0, "L"), (1.0, "R")):
            make_faceted_cylinder(
                f"FieldMedicRoverRollCage_AAnchor_{tag}",
                (side * 0.48, 0.56, 0.62),
                (side * 0.35, 0.56, 0.72),
                0.026,
                gunmetal,
                segments=10,
            )
            make_faceted_cylinder(
                f"FieldMedicRoverRollCage_BAnchor_{tag}",
                (side * 0.52, -0.09, 0.98),
                (side * 0.38, -0.09, 0.98),
                0.028,
                gunmetal,
                segments=10,
            )
            make_faceted_cylinder(
                f"FieldMedicRoverRollCage_RoofCabPad_{tag}",
                (side * 0.30, 0.10, 1.445),
                (side * 0.30, 0.10, 1.32),
                0.042,
                gunmetal,
                segments=10,
            )

        # Seat AccessHatch compactly on rear trauma cab roof behind TurretRingGuard (y <= -0.39)
        hatch_parts = [
            bpy.data.objects.get(f"FieldMedicRoverArmor_AccessHatch_{s}")
            for s in ("CoamingRim", "HatchPlate", "HingeBar", "DogLatch")
        ]
        hatch_parts = [o for o in hatch_parts if o is not None]
        if hatch_parts:
            for ho in hatch_parts:
                ho.scale.x *= 0.62
                ho.scale.y *= 0.62
                bpy.context.view_layer.update()
                bpy.context.view_layer.objects.active = ho
                ho.select_set(True)
                bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
                ho.select_set(False)
            bpy.context.view_layer.update()
            hx0 = min(world_bounds(o)[0] for o in hatch_parts)
            hx1 = max(world_bounds(o)[1] for o in hatch_parts)
            hy0 = min(world_bounds(o)[2] for o in hatch_parts)
            hy1 = max(world_bounds(o)[3] for o in hatch_parts)
            _shift_objects(hatch_parts, dx=-((hx0 + hx1) * 0.5), dy=-0.56 - ((hy0 + hy1) * 0.5))
            seat_cluster_downward(hatch_parts, [cab_shell], clearance=-0.005, max_drop=1.5)
            coaming = bpy.data.objects.get("FieldMedicRoverArmor_AccessHatch_CoamingRim")
            plate = bpy.data.objects.get("FieldMedicRoverArmor_AccessHatch_HatchPlate")
            for hw_name in ("FieldMedicRoverArmor_AccessHatch_HingeBar", "FieldMedicRoverArmor_AccessHatch_DogLatch"):
                snap_object_to_nearest(bpy.data.objects.get(hw_name), [plate, coaming], embed=0.004)

        # Seat Gusset_1/2, PioneerRack, AntennaDeckCollar onto TraumaCabShell; LiftingLugs onto chassis/cab corners; DriverPeriscope onto FrontNose
        for g_idx, g_side in ((1, -1.0), (2, 1.0)):
            g_obj = bpy.data.objects.get(f"FieldMedicRoverArmor_Gusset_{g_idx}")
            if g_obj is not None:
                _shift_objects([g_obj], dx=-g_side * 0.15, dz=-0.04)

        rack_objs = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("FieldMedicRoverArmor_PioneerRack_")]
        if rack_objs:
            _shift_objects(rack_objs, dx=0.47, dz=-0.46)

        ant_collar = bpy.data.objects.get("FieldMedicRoverSensor_AntennaDeckCollar")
        if ant_collar is not None:
            seat_cluster_downward([ant_collar], [cab_shell], clearance=-0.005)

        lug_targets = [
            (1, (-0.34, 0.88), [nose, spine]),
            (2, (0.34, 0.88), [nose, spine]),
            (3, (-0.34, -0.64), [cab_shell]),
            (4, (0.34, -0.64), [cab_shell]),
        ]
        for l_idx, (tx, ty), l_hosts in lug_targets:
            pad = bpy.data.objects.get(f"FieldMedicRoverArmor_LugWeldPad_{l_idx}")
            lug = bpy.data.objects.get(f"FieldMedicRoverArmor_LiftingLug_{l_idx}")
            pair = [o for o in (pad, lug) if o is not None]
            valid_hosts = [h for h in l_hosts if h is not None]
            if pair and valid_hosts:
                cx, cy, _cz = world_center(pair[0])
                _shift_objects(pair, dx=tx - cx, dy=ty - cy)
                seat_cluster_downward(pair, valid_hosts, clearance=-0.004, max_drop=1.5)

        periscope_objs = [
            bpy.data.objects.get(n)
            for n in (
                "FieldMedicRoverSensor_DriverPeriscope_MountPad",
                "FieldMedicRoverSensor_DriverPeriscope_PeriscopeHood",
                "FieldMedicRoverSensor_DriverPeriscope_OpticPrism",
            )
        ]
        periscope_objs = [o for o in periscope_objs if o is not None]
        if periscope_objs and nose is not None:
            _shift_objects(periscope_objs, dy=-0.12)
            seat_cluster_downward(periscope_objs, [nose], clearance=-0.005, max_drop=1.5)

    notes.append(f"4 curved fenders + {stay_total} raycast stays + DualGun breech cuts + RollCage/deck anchors")
    return notes


def apply_skyguard_sam_v2() -> list[str]:
    """Upgrade SkyguardSAM: sculpted cab & deck, 6-cell counterbored SAM pod, 6x6 fenders, and seated jacks."""
    prefix = "SkyguardSAM"
    p_armor = resolve_unit_material(prefix, "PrimaryArmor")
    l_armor = resolve_unit_material(prefix, "LightArmor")
    gunmetal = resolve_unit_material(prefix, "Gunmetal")
    tread = resolve_unit_material(prefix, "Tread")
    notes: list[str] = []

    # 1. ForwardCab: sloped armored cab clearing front tires (|x| <= 0.79 < 0.835 tire inner face) + CabVisor pocket
    cab = bpy.data.objects.get("SkyguardSAMArmor_ForwardCab")
    if cab is not None:
        cab_stations = (
            (0.755, 0.0, 1.02, 0.79, 0.37),
            (1.36, 0.0, 1.01, 0.79, 0.36),
            (1.68, 0.0, 0.95, 0.76, 0.30),
            (1.805, 0.0, 0.86, 0.70, 0.21),
        )
        replace_object_geometry(cab, make_chamfered_y_loft("SkyguardSAMArmor_ForwardCab_Tmp", cab_stations, p_armor, bevel_ratio=0.22))
        cut_visor_pocket_around_housing(cab, bpy.data.objects.get("SkyguardSAMSensor_CabVisor_Housing"), margin=0.008, recipe_tag="cab_visor_pocket")
        for side, tag in ((-1, "L"), (1, "R")):
            cut_recessed_service_pocket(
                cab,
                (side * 0.77, 1.24, 1.04),
                (0.08, 0.42, 0.26),
                louver_mat=l_armor,
                louver_name=f"SkyguardSAMArmor_CabDoorPanel_{tag}",
                louver_axis="X",
                louver_count=2,
                recipe_tag=f"cab_door_{tag}",
            )
        cut_recessed_service_pocket(
            cab,
            (0.0, 1.79, 0.84),
            (0.74, 0.08, 0.18),
            louver_mat=gunmetal,
            louver_name="SkyguardSAMArmor_CabIntakeLouver",
            louver_axis="Y",
            louver_count=3,
            recipe_tag="cab_grille",
        )
        notes.append("ForwardCab sculpted + visor/door/grille Exact Booleans")

    # 2. LauncherBed & ChassisFrame: multi-station beveled deck clearing 6x6 tires (|x| <= 0.79)
    bed = bpy.data.objects.get("SkyguardSAMArmor_LauncherBed")
    if bed is not None:
        bed_stations = (
            (-1.595, 0.0, 0.86, 0.79, 0.11),
            (-0.60, 0.0, 0.86, 0.79, 0.115),
            (0.355, 0.0, 0.86, 0.79, 0.11),
        )
        replace_object_geometry(bed, make_chamfered_y_loft("SkyguardSAMArmor_LauncherBed_Tmp", bed_stations, l_armor, bevel_ratio=0.24))
        for side, tag in ((-1, "L"), (1, "R")):
            cut_recessed_service_pocket(
                bed,
                (side * 0.77, -0.65, 0.86),
                (0.06, 0.54, 0.10),
                louver_mat=gunmetal,
                louver_name=f"SkyguardSAMArmor_DeckVentLouver_{tag}",
                louver_axis="X",
                louver_count=4,
                recipe_tag=f"bed_vent_{tag}",
            )

    chassis = bpy.data.objects.get("SkyguardSAMArmor_ChassisFrame")
    if chassis is not None:
        ch_stations = (
            (-1.775, 0.0, 0.56, 0.78, 0.17),
            (0.0, 0.0, 0.56, 0.81, 0.17),
            (1.775, 0.0, 0.56, 0.78, 0.17),
        )
        replace_object_geometry(chassis, make_chamfered_y_loft("SkyguardSAMArmor_ChassisFrame_Tmp", ch_stations, gunmetal, bevel_ratio=0.20))

    # 3. MissilePod_Chassis: chamfered housing + Exact Boolean counterbore cuts around all 6 Tubes + ExhaustVent
    pod = bpy.data.objects.get("SkyguardSAMMissilePod_Chassis")
    if pod is not None:
        x0, x1, y0, y1, z0, z1 = world_bounds(pod)
        tmp_pod = make_chamfered_box_mesh(
            "SkyguardSAMMissilePod_Chassis_Tmp",
            (x1 - x0, y1 - y0, z1 - z0),
            ((x0 + x1) * 0.5, (y0 + y1) * 0.5, (z0 + z1) * 0.5),
            p_armor,
            bevel=0.032,
        )
        replace_object_geometry(pod, tmp_pod)
        for t_idx in range(1, 7):
            tube = bpy.data.objects.get(f"SkyguardSAMMissilePod_Tube_{t_idx}")
            if tube is not None:
                cut_visor_pocket_around_housing(pod, tube, margin=0.004, recipe_tag=f"pod_tube_{t_idx}")
        exh_v = bpy.data.objects.get("SkyguardSAMMissilePod_ExhaustVent")
        if exh_v is not None:
            cut_visor_pocket_around_housing(pod, exh_v, margin=0.004, recipe_tag="pod_exhaust_pocket")
        notes.append("MissilePod_Chassis chamfered + 7 Exact Boolean launch/exhaust cuts")

    # 4. StabilizerJack_L/R: replace f=26 boxes with anchored outrigger struts + raycast chassis tie-in
    for side, tag in ((-1, "L"), (1, "R")):
        jack = bpy.data.objects.get(f"SkyguardSAMArmor_StabilizerJack_{tag}")
        if jack is not None:
            sx = side * 0.98
            post = make_faceted_cylinder(
                f"SkyguardSAMArmor_StabilizerJack_{tag}_Tmp",
                (sx, -1.55, 0.06),
                (sx, -1.55, 0.62),
                0.065,
                gunmetal,
                segments=12,
            )
            replace_object_geometry(jack, post)
            # Ground pad + raycast tie arm into ChassisFrame
            make_chamfered_box_mesh(
                f"SkyguardSAMArmor_StabilizerPad_{tag}",
                (0.18, 0.18, 0.04),
                (sx, -1.55, 0.02),
                l_armor,
                bevel=0.008,
            )
            if chassis is not None:
                add_raycast_stay_between(
                    f"SkyguardSAMArmor_StabilizerBeam_{tag}",
                    jack,
                    [chassis, bed] if bed else [chassis],
                    (sx - side * 0.04, -1.55, 0.54),
                    (-side, 0.0, 0.0),
                    radius=0.042,
                    material=l_armor,
                )

    # 5. Add sculpted 6x6 arch mudguards + raycast stays over all 6 Skyguard tires
    tires = [o for o in bpy.data.objects if o.type == "MESH" and ("TireCarcass" in o.name or "WheelRim" in o.name)]
    hosts = [o for o in (cab, bed, chassis) if o is not None]
    stay_count = 0
    for tag_pos, tire_name in (
        ("Front_L", "SkyguardSAMWheel_Front_L_TireCarcass"),
        ("Front_R", "SkyguardSAMWheel_Front_R_TireCarcass"),
        ("Mid_L", "SkyguardSAMWheel_Mid_L_TireCarcass"),
        ("Mid_R", "SkyguardSAMWheel_Mid_R_TireCarcass"),
        ("Rear_L", "SkyguardSAMWheel_Rear_L_TireCarcass"),
        ("Rear_R", "SkyguardSAMWheel_Rear_R_TireCarcass"),
    ):
        tire = bpy.data.objects.get(tire_name)
        if tire is None or bpy.data.objects.get(f"SkyguardSAMFender_{tag_pos}_ArchTop") is not None:
            continue
        tx0, tx1, ty0, ty1, _tz0, _tz1 = world_bounds(tire)
        cx = (tx0 + tx1) * 0.5
        cy = (ty0 + ty1) * 0.5
        stub = make_chamfered_box_mesh(
            f"SkyguardSAMFender_{tag_pos}_ArchTop",
            (0.32, 0.72, 0.04),
            (cx, cy, 0.94),
            l_armor,
            bevel=0.005,
        )
        stay_count += len(build_sculpted_wheel_fender_set(prefix, stub, tire, hosts, tires, l_armor, gunmetal, tread))

    # 6. Connect floating APS pods, APS radar arrays, LiftingLugs, PioneerRack, Gussets & Radar_DeckPylon onto sculpted cab/bed
    for side, tag in ((-1.0, "L"), (1.0, "R")):
        aps_cluster = [
            bpy.data.objects.get(f"SkyguardSAMAPS_APSTurntable_{tag}"),
            bpy.data.objects.get(f"SkyguardSAMAPS_APSLauncherPod_{tag}"),
            bpy.data.objects.get(f"SkyguardSAMAPS_APSTube_{tag}_1"),
            bpy.data.objects.get(f"SkyguardSAMAPS_APSTube_{tag}_2"),
        ]
        aps_cluster = [o for o in aps_cluster if o is not None]
        if aps_cluster and cab is not None:
            _shift_objects(aps_cluster, dx=side * 0.06, dy=0.14)
            seat_cluster_downward(aps_cluster, [cab], clearance=-0.005, max_drop=1.5)

        gus = bpy.data.objects.get(f"SkyguardSAMArmor_Gusset_{1 if side < 0 else 2}")
        if gus is not None and bed is not None:
            _shift_objects([gus], dx=-side * 0.04, dz=-0.16)

    for tag_corner, (tx, ty), r_hosts in (
        ("FL", (-0.64, 1.56), [cab]),
        ("FR", (0.64, 1.56), [cab]),
        ("RL", (-0.68, -1.42), [bed]),
        ("RR", (0.68, -1.42), [bed]),
    ):
        panel = bpy.data.objects.get(f"SkyguardSAMAPS_APSRadarPanel_{tag_corner}")
        diel = bpy.data.objects.get(f"SkyguardSAMAPS_APSRadarDielectric_{tag_corner}")
        pair = [o for o in (panel, diel) if o is not None]
        valid_hosts = [h for h in r_hosts if h is not None]
        if pair and valid_hosts:
            cx, cy, _cz = world_center(pair[0])
            _shift_objects(pair, dx=tx - cx, dy=ty - cy)
            seat_cluster_downward(pair, valid_hosts, clearance=-0.008, max_drop=1.5)

    for l_idx, (tx, ty), l_hosts in (
        (1, (-0.66, 0.96), [cab]),
        (2, (0.66, 0.96), [cab]),
        (3, (-0.68, -1.26), [bed]),
        (4, (0.68, -1.26), [bed]),
    ):
        pad = bpy.data.objects.get(f"SkyguardSAMArmor_LugWeldPad_{l_idx}")
        lug = bpy.data.objects.get(f"SkyguardSAMArmor_LiftingLug_{l_idx}")
        pair = [o for o in (pad, lug) if o is not None]
        valid_hosts = [h for h in l_hosts if h is not None]
        if pair and valid_hosts:
            cx, cy, _cz = world_center(pair[0])
            _shift_objects(pair, dx=tx - cx, dy=ty - cy)
            seat_cluster_downward(pair, valid_hosts, clearance=-0.005, max_drop=2.0)

    rack_objs = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("SkyguardSAMArmor_PioneerRack_")]
    if rack_objs:
        _shift_objects(rack_objs, dx=0.14, dy=-0.52, dz=-0.76)

    pylon = bpy.data.objects.get("SkyguardSAMRadar_DeckPylon")
    if pylon is not None:
        _shift_objects([pylon], dz=-0.015)

    notes.append(f"Stabilizer jacks seated + 6 sculpted 6x6 fenders + {stay_count} raycast stays + APS/lugs/rack seated")
    return notes


def apply_salvage_hauler_v2() -> list[str]:
    """Upgrade SalvageHauler: resolve Dozer/Spade overlap, carve engine bay & tool locker, sculpt track skirts + stays."""
    prefix = "SalvageHauler"
    p_armor = resolve_unit_material(prefix, "PrimaryArmor")
    l_armor = resolve_unit_material(prefix, "LightArmor")
    gunmetal = resolve_unit_material(prefix, "Gunmetal")
    warning = resolve_unit_material(prefix, "Warning")
    notes: list[str] = []

    hull = bpy.data.objects.get("SalvageHaulerArmor_Hull")
    deck = bpy.data.objects.get("SalvageHaulerArmor_UpperDeck")

    # 1. Hull & UpperDeck sculpted shells + Exact Boolean EngineDeck pocket + Winch fairlead pocket
    if hull is not None:
        hull_stations = (
            (-1.66, 0.0, 0.60, 0.76, 0.34),
            (-1.10, 0.0, 0.60, 0.79, 0.36),
            (1.10, 0.0, 0.60, 0.79, 0.36),
            (1.705, 0.0, 0.56, 0.74, 0.30),
        )
        replace_object_geometry(hull, make_chamfered_y_loft("SalvageHaulerArmor_Hull_Tmp", hull_stations, p_armor, bevel_ratio=0.22))
        # Carve recessed pocket behind WinchDrum so drum nests into bow bay cleanly
        cut_recessed_service_pocket(
            hull,
            (0.0, 1.48, 0.80),
            (1.24, 0.42, 0.28),
            recipe_tag="winch_bay_pocket",
        )

    if deck is not None:
        deck_stations = (
            (-1.274, 0.0, 1.06, 0.83, 0.115),
            (0.0, 0.0, 1.06, 0.845, 0.12),
            (1.174, 0.0, 1.06, 0.82, 0.115),
        )
        replace_object_geometry(deck, make_chamfered_y_loft("SalvageHaulerArmor_UpperDeck_Tmp", deck_stations, l_armor, bevel_ratio=0.24))
        # Carve true EngineDeck recessed vent bay under slats
        cut_recessed_service_pocket(
            deck,
            (0.0, -1.02, 1.13),
            (0.76, 0.62, 0.14),
            recipe_tag="engine_deck_cavity",
        )
        notes.append("Hull & UpperDeck sculpted + Winch bay & EngineDeck Boolean cavities")

    # 2. Separate overlapping Dozer_Blade vs EarthSpade cleanly!
    dozer = bpy.data.objects.get("SalvageHaulerDozer_Blade")
    spade = bpy.data.objects.get("SalvageHaulerArmor_EarthSpade")
    if dozer is not None:
        # Concave swept dozer moldboard at Z=[0.14, 0.54]
        dozer_stations = (
            (1.74, 0.0, 0.34, 1.365, 0.18),
            (1.88, 0.0, 0.34, 1.34, 0.19),
            (1.96, 0.0, 0.34, 1.28, 0.16),
        )
        replace_object_geometry(dozer, make_chamfered_y_loft("SalvageHaulerDozer_Blade_Tmp", dozer_stations, p_armor, bevel_ratio=0.22))
        # Relief cut for front shackles on rear face
        for sx in (-0.90, 0.90):
            cut_recessed_service_pocket(
                dozer,
                (sx, 1.72, 0.30),
                (0.18, 0.12, 0.18),
                recipe_tag=f"dozer_ram_pocket_{'L' if sx < 0 else 'R'}",
            )
    if spade is not None:
        # Clean lower bolted scraper cutting blade strictly below Dozer_Blade (Z=[0.02, 0.135]), ZERO overlap!
        spade_stations = (
            (1.77, 0.0, 0.075, 1.24, 0.052),
            (1.94, 0.0, 0.068, 1.22, 0.048),
            (2.04, 0.0, 0.055, 1.18, 0.036),
        )
        replace_object_geometry(spade, make_chamfered_y_loft("SalvageHaulerArmor_EarthSpade_Tmp", spade_stations, warning, bevel_ratio=0.22))
        notes.append("Dozer_Blade & EarthSpade separated cleanly with zero overlap")

    # 3. Sculpt ToolLocker + CraneSlewBase with Exact Boolean pockets
    locker = bpy.data.objects.get("SalvageHaulerArmor_ToolLocker")
    if locker is not None:
        lk_stations = (
            (-1.10, -0.70, 1.14, 0.36, 0.185),
            (-0.45, -0.70, 1.14, 0.375, 0.19),
            (0.20, -0.70, 1.14, 0.35, 0.18),
        )
        replace_object_geometry(locker, make_chamfered_y_loft("SalvageHaulerArmor_ToolLocker_Tmp", lk_stations, l_armor, bevel_ratio=0.25))
        for idx, ly in enumerate((-0.78, -0.14), start=1):
            cut_recessed_service_pocket(
                locker,
                (-1.055, ly, 1.14),
                (0.06, 0.44, 0.22),
                louver_mat=gunmetal,
                louver_name=f"SalvageHaulerArmor_LockerDoorLatch_{idx}",
                louver_axis="X",
                louver_count=2,
                recipe_tag=f"locker_bay_{idx}",
            )

    slew = bpy.data.objects.get("SalvageHaulerArmor_CraneSlewBase")
    if slew is not None:
        sl_stations = (
            (0.125, 0.62, 1.18, 0.38, 0.135),
            (0.55, 0.62, 1.18, 0.425, 0.14),
            (0.975, 0.62, 1.18, 0.38, 0.135),
        )
        replace_object_geometry(slew, make_chamfered_y_loft("SalvageHaulerArmor_CraneSlewBase_Tmp", sl_stations, l_armor, bevel_ratio=0.30))

    # 4. Contoured Track Skirts (ArmorSkirt_L/R) + FenderTop_L/R + FenderSplash_L/R + raycast stays
    tread_obstacles = [o for o in bpy.data.objects if o.type == "MESH" and ("Belt_" in o.name or "RoadWheel_" in o.name or "Sprocket" in o.name or "Idler" in o.name)]
    stay_hosts = [o for o in (hull, deck) if o is not None]
    stay_count = 0
    for side, tag in ((-1, "L"), (1, "R")):
        skirt = bpy.data.objects.get(f"SalvageHaulerTreads_ArmorSkirt_{tag}")
        if skirt is not None:
            sx = side * 1.385
            sk_stations = (
                (-1.40, sx, 0.41, 0.022, 0.17),
                (0.0, sx, 0.41, 0.024, 0.18),
                (1.40, sx, 0.41, 0.022, 0.17),
            )
            replace_object_geometry(skirt, make_chamfered_y_loft(f"{skirt.name}_Tmp", sk_stations, p_armor, bevel_ratio=0.25))
            # Cut 3 mud-relief inspection windows along skirt
            for w_idx, wy in enumerate((-0.78, 0.0, 0.78), start=1):
                cut_recessed_service_pocket(
                    skirt,
                    (sx, wy, 0.34),
                    (0.08, 0.36, 0.11),
                    recipe_tag=f"skirt_mud_relief_{tag}_{w_idx}",
                )
        ftop = bpy.data.objects.get(f"SalvageHaulerTreads_FenderTop_{tag}")
        if ftop is not None:
            fx = side * 1.18
            ft_stations = (
                (-1.536, fx, 0.745, 0.21, 0.032),
                (0.0, fx, 0.745, 0.21, 0.032),
                (1.536, fx, 0.745, 0.21, 0.032),
            )
            replace_object_geometry(ftop, make_chamfered_y_loft(f"{ftop.name}_Tmp", ft_stations, l_armor, bevel_ratio=0.22))
            for k_idx, fy in enumerate((-1.05, 0.0, 1.05), start=1):
                stay = add_raycast_stay_between(
                    f"SalvageHaulerTreads_FenderStay_{tag}_{k_idx}",
                    ftop,
                    stay_hosts,
                    (side * 0.98, fy, 0.73),
                    (-side, 0.0, -0.05),
                    radius=0.022,
                    material=gunmetal,
                    avoid_obstacles=tread_obstacles,
                )
                if stay is not None:
                    stay_count += 1
        fsplash = bpy.data.objects.get(f"SalvageHaulerTreads_FenderSplash_{tag}")
        if fsplash is not None:
            fx = side * 1.18
            sp_stations = (
                (1.48, fx, 0.72, 0.205, 0.06),
                (1.72, fx, 0.64, 0.20, 0.045),
                (1.88, fx, 0.56, 0.19, 0.035),
            )
            replace_object_geometry(fsplash, make_chamfered_y_loft(f"{fsplash.name}_Tmp", sp_stations, l_armor, bevel_ratio=0.22))

    # 5. Connect EarthSpade brackets, ArmorSkirts, RoadWheels/Suspension, Exhausts, Shackles, Lugs, PioneerRack & DriverPeriscope
    if dozer is not None and spade is not None:
        for side, tag in ((-1.0, "L"), (1.0, "R")):
            make_faceted_cylinder(
                f"SalvageHaulerDozer_SpadeBracket_{tag}",
                (side * 0.80, 1.82, 0.105),
                (side * 0.80, 1.82, 0.235),
                0.032,
                gunmetal,
                segments=10,
            )

    for side, tag in ((-1.0, "L"), (1.0, "R")):
        skirt = bpy.data.objects.get(f"SalvageHaulerTreads_ArmorSkirt_{tag}")
        stripe = bpy.data.objects.get(f"SalvageHaulerTreads_SkirtStripe_{tag}")
        if stripe is not None and skirt is not None:
            _shift_objects([stripe], dx=side * 0.020)
        ftop = bpy.data.objects.get(f"SalvageHaulerTreads_FenderTop_{tag}")
        if skirt is not None and ftop is not None:
            for s_idx, sy in enumerate((-1.05, 0.0, 1.05), start=1):
                make_faceted_cylinder(
                    f"SalvageHaulerTreads_SkirtBracket_{tag}_{s_idx}",
                    (side * 1.38, sy, 0.56),
                    (side * 1.28, sy, 0.74),
                    0.022,
                    gunmetal,
                    segments=8,
                )

        # Tie every TorsionBoss to Hull and every RoadWheel_2..5 Rim to SwingArm
        for w_idx in range(2, 6):
            boss = bpy.data.objects.get(f"SalvageHaulerSuspension_{tag}_TorsionBoss_{w_idx}")
            if boss is not None:
                bcx, bcy, bcz = world_center(boss)
                make_faceted_cylinder(
                    f"SalvageHaulerSuspension_{tag}_TorsionBar_{w_idx}",
                    (side * 0.74, bcy, bcz),
                    (bcx + side * 0.02, bcy, bcz),
                    0.030,
                    gunmetal,
                    segments=10,
                )
            rim = bpy.data.objects.get(f"SalvageHaulerTreads_RoadWheel_{tag}_{w_idx}_Rim")
            if rim is not None:
                rcx, rcy, rcz = world_center(rim)
                make_faceted_cylinder(
                    f"SalvageHaulerSuspension_{tag}_AxleStub_{w_idx}",
                    (side * 1.07, rcy, rcz),
                    (rcx, rcy, rcz),
                    0.032,
                    gunmetal,
                    segments=10,
                )

        exh_pair = [
            bpy.data.objects.get(f"SalvageHaulerArmor_Exhaust_{tag}"),
            bpy.data.objects.get(f"SalvageHaulerArmor_ExhaustShield_{tag}"),
        ]
        exh_pair = [o for o in exh_pair if o is not None]
        if exh_pair:
            _shift_objects(exh_pair, dy=0.06, dz=-0.04)

    for sh_idx, side in ((1, -1.0), (2, 1.0)):
        front_sh = [
            bpy.data.objects.get(f"SalvageHaulerArmor_FrontShackle_{p}_{sh_idx}")
            for p in ("ShackleBracket", "ShackleLoop", "ShacklePin")
        ]
        front_sh = [o for o in front_sh if o is not None]
        if front_sh:
            _shift_objects(front_sh, dx=-side * 0.20, dy=0.14)

        rear_sh = [
            bpy.data.objects.get(f"SalvageHaulerArmor_RearShackle_{p}_{sh_idx}")
            for p in ("ShackleBracket", "ShackleLoop", "ShacklePin")
        ]
        rear_sh = [o for o in rear_sh if o is not None]
        if rear_sh:
            _shift_objects(rear_sh, dx=-side * 0.20, dy=-0.12)

    if deck is not None:
        for l_idx, (tx, ty) in (
            (1, (-0.72, 0.96)),
            (2, (0.72, 0.96)),
            (3, (-0.72, -1.02)),
            (4, (0.72, -1.02)),
        ):
            pad = bpy.data.objects.get(f"SalvageHaulerArmor_LugWeldPad_{l_idx}")
            lug = bpy.data.objects.get(f"SalvageHaulerArmor_LiftingLug_{l_idx}")
            pair = [o for o in (pad, lug) if o is not None]
            if pair:
                cx, cy, _cz = world_center(pair[0])
                _shift_objects(pair, dx=tx - cx, dy=ty - cy)
                seat_cluster_downward(pair, [deck], clearance=-0.005, max_drop=1.5)

        periscope_objs = [
            bpy.data.objects.get(n)
            for n in (
                "SalvageHaulerSensor_DriverPeriscope_MountPad",
                "SalvageHaulerSensor_DriverPeriscope_PeriscopeHood",
                "SalvageHaulerSensor_DriverPeriscope_OpticPrism",
            )
        ]
        periscope_objs = [o for o in periscope_objs if o is not None]
        if periscope_objs:
            _shift_objects(periscope_objs, dy=-0.16)
            seat_cluster_downward(periscope_objs, [deck], clearance=-0.005, max_drop=1.5)

    rack_objs = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("SalvageHaulerArmor_PioneerRack_")]
    if rack_objs:
        _shift_objects(rack_objs, dx=0.05, dz=-0.22)

    notes.append(f"ToolLocker, CraneSlewBase, TrackSkirts carved + {stay_count} raycast stays + chassis/roadwheel anchors")
    return notes


def apply_sentinel_relay_v2() -> list[str]:
    """Final v2 polish for SentinelRelay: remove duplicate ring guard & hatches, cut Exact Boolean pockets, add curved fenders + stays."""
    prefix = "SentinelRelay"
    p_armor = resolve_unit_material(prefix, "PrimaryArmor")
    l_armor = resolve_unit_material(prefix, "LightArmor")
    gunmetal = resolve_unit_material(prefix, "Gunmetal")
    tread = resolve_unit_material(prefix, "Tread")
    notes: list[str] = []

    # 1. Remove stray duplicate TurretRingGuard & overlapping EquipmentBay AccessHatch
    removed = remove_objects_matching(
        lambda n: (
            n.startswith("SentinelRelayArmor_TurretRingGuard_")
            or n.startswith("SentinelRelayAccessHatch_")
        )
    )
    if removed:
        notes.append(f"Removed {len(removed)} duplicate SentinelRelay guard/hatch meshes")

    # 2. Exact Boolean pockets in EquipmentBay aft flank panels & CabCowl brow (preserving all test_sentinel_relay_surface_fit objects)
    bay = bpy.data.objects.get("SentinelRelayArmor_EquipmentBay")
    if bay is not None:
        for side, tag in ((-1, "L"), (1, "R")):
            cut_recessed_service_pocket(
                bay,
                (side * 0.60, -1.04, 1.06),
                (0.06, 0.28, 0.18),
                louver_mat=gunmetal,
                louver_name=f"SentinelRelayArmor_BayServiceLouver_{tag}",
                louver_axis="X",
                louver_count=3,
                recipe_tag=f"bay_service_{tag}",
            )
        notes.append("EquipmentBay flanked Exact Boolean service pockets")

    cowl = bpy.data.objects.get("SentinelRelayArmor_CabCowl")
    if cowl is not None:
        periscope = bpy.data.objects.get("SentinelRelayDriverPeriscope_MountPad")
        cut_visor_pocket_around_housing(cowl, periscope, margin=0.005, recipe_tag="cowl_periscope_pocket")
        cut_recessed_service_pocket(
            cowl,
            (0.0, 0.80, 1.02),
            (0.54, 0.06, 0.14),
            louver_mat=gunmetal,
            louver_name="SentinelRelayArmor_CowlFrontIntakeLouver",
            louver_axis="Y",
            louver_count=2,
            recipe_tag="cowl_front_intake",
        )
        notes.append("CabCowl Exact Boolean periscope & intake cuts")

    # 3. Seat Turret AccessHatch rigidly as ONE cluster onto turret roof & curved wheel-arch fenders + raycast stays
    turret_shell = bpy.data.objects.get("SentinelRelayTurret_BaseShell")
    hatch_cluster = [
        bpy.data.objects.get(f"SentinelRelayArmor_AccessHatch_{s}")
        for s in ("CoamingRim", "HatchPlate", "HingeBar", "DogLatch")
    ]
    hatch_cluster = [o for o in hatch_cluster if o is not None]
    if hatch_cluster and turret_shell is not None:
        _shift_objects(hatch_cluster, dx=-0.16, dy=0.38, dz=0.11)
        seat_cluster_downward(hatch_cluster, [turret_shell], clearance=-0.004, max_drop=1.0)
        coaming = bpy.data.objects.get("SentinelRelayArmor_AccessHatch_CoamingRim")
        plate = bpy.data.objects.get("SentinelRelayArmor_AccessHatch_HatchPlate")
        for hw_name in ("SentinelRelayArmor_AccessHatch_HingeBar", "SentinelRelayArmor_AccessHatch_DogLatch"):
            snap_object_to_nearest(bpy.data.objects.get(hw_name), [plate, coaming], embed=0.004)

    chassis = bpy.data.objects.get("SentinelRelayArmor_Chassis")
    hosts = [o for o in (chassis, cowl, bay) if o is not None]
    tires = [o for o in bpy.data.objects if o.type == "MESH" and ("TireCarcass" in o.name or "TreadBlocks" in o.name or "WheelRim" in o.name)]
    fender_map = (
        ("SentinelRelayFender_1_L_ArchTop", "SentinelRelayWheel_F_L_TireCarcass"),
        ("SentinelRelayFender_2_L_ArchTop", "SentinelRelayWheel_R_L_TireCarcass"),
        ("SentinelRelayFender_3_R_ArchTop", "SentinelRelayWheel_F_R_TireCarcass"),
        ("SentinelRelayFender_4_R_ArchTop", "SentinelRelayWheel_R_R_TireCarcass"),
    )
    stay_count = 0
    for arch_name, tire_name in fender_map:
        stays = build_sculpted_wheel_fender_set(
            prefix,
            bpy.data.objects.get(arch_name),
            bpy.data.objects.get(tire_name),
            hosts,
            tires,
            p_armor,
            gunmetal,
            tread,
        )
        stay_count += len(stays)
    notes.append(f"4 curved fenders + {stay_count} raycast stays seated")
    return notes


UNIT_V2_RECIPE_MAP: dict[str, Callable[[], list[str]]] = {
    "ArcLanceWalker": apply_arc_lance_walker_v2,
    "TidebreakerHovercraft": apply_tidebreaker_hovercraft_v2,
    "FieldMedicRover": apply_field_medic_rover_v2,
    "SkyguardSAM": apply_skyguard_sam_v2,
    "SalvageHauler": apply_salvage_hauler_v2,
    "SentinelRelay": apply_sentinel_relay_v2,
}


# ---------------------------------------------------------------------------
# Automated Surface-Fit, Attribute & Zero-Overlap Audit
# ---------------------------------------------------------------------------

def audit_v2_scene_clearances(unit_name: str) -> dict[str, Any]:
    """Verify Exact Boolean recipe tags, valid UVs/Colors, raycast stay gaps, and zero tire/blade overlaps."""
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

    # Check zero wheel/tire overlap against fenders, stays, chassis, cab, skirts
    wheel_objs = [o for o in render_objs if any(k in o.name for k in ("TireCarcass", "TreadBlocks"))]
    body_objs = [
        o for o in render_objs
        if any(k in o.name for k in ("Fender_", "_Stay_", "TraumaCabShell", "ForwardCab", "Dozer_", "EarthSpade", "ArmorSkirt"))
        and "Mudflap" not in o.name
    ]
    tire_overlaps: list[str] = []
    bvh_cache: dict[str, BVHTree] = {}
    for w in wheel_objs:
        wbvh, _ = _mesh_bvh(w)
        bvh_cache[w.name] = wbvh
    for b in body_objs:
        bbvh, _ = _mesh_bvh(b)
        bvh_cache[b.name] = bbvh
        for w in wheel_objs:
            if bvh_cache[w.name].overlap(bbvh):
                tire_overlaps.append(f"{b.name} <-> {w.name}")

    # Check Dozer_Blade vs EarthSpade separation on SalvageHauler
    dozer_spade_overlap = False
    dozer = bpy.data.objects.get("SalvageHaulerDozer_Blade")
    spade = bpy.data.objects.get("SalvageHaulerArmor_EarthSpade")
    if dozer is not None and spade is not None:
        dbvh, _ = _mesh_bvh(dozer)
        sbvh, _ = _mesh_bvh(spade)
        dozer_spade_overlap = bool(dbvh.overlap(sbvh))

    # Hull BFS connectivity audit: every renderable mesh must connect to the chassis/torso root within 8 mm
    disconnected_from_hull: list[str] = []
    root_obj = next(
        (
            o for o in render_objs
            if o.name.endswith(("Armor_Torso", "Armor_Hull", "Armor_SpineChassis", "Armor_ChassisFrame", "Armor_Chassis"))
        ),
        render_objs[0] if render_objs else None,
    )
    if root_obj is not None:
        from collections import deque

        pts_cache: dict[str, list[Vector]] = {}
        bounds_cache: dict[str, tuple[float, float, float, float, float, float]] = {}
        for o in render_objs:
            if o.name not in bvh_cache:
                bvh_o, _ = _mesh_bvh(o)
                bvh_cache[o.name] = bvh_o
            mw = o.matrix_world
            pts = [mw @ v.co for v in o.data.vertices]
            for p in o.data.polygons:
                pts.append(mw @ p.center)
            pts_cache[o.name] = pts
            bounds_cache[o.name] = world_bounds(o)

        def _pair_dist(a_name: str, b_name: str) -> float:
            if bvh_cache[a_name].overlap(bvh_cache[b_name]):
                return 0.0
            best = 999.0
            b_bvh = bvh_cache[b_name]
            for v in pts_cache[a_name]:
                loc, _n, _idx, d = b_bvh.find_nearest(v)
                if loc is not None and d < best:
                    best = d
            a_bvh = bvh_cache[a_name]
            for v in pts_cache[b_name]:
                loc, _n, _idx, d = a_bvh.find_nearest(v)
                if loc is not None and d < best:
                    best = d
            return best

        names = [o.name for o in render_objs]
        adj: dict[str, list[str]] = {n: [] for n in names}
        for i, a_name in enumerate(names):
            ax0, ax1, ay0, ay1, az0, az1 = bounds_cache[a_name]
            for b_name in names[i + 1 :]:
                bx0, bx1, by0, by1, bz0, bz1 = bounds_cache[b_name]
                if (
                    bx0 > ax1 + 0.02
                    or ax0 > bx1 + 0.02
                    or by0 > ay1 + 0.02
                    or ay0 > by1 + 0.02
                    or bz0 > az1 + 0.02
                    or az0 > bz1 + 0.02
                ):
                    continue
                if _pair_dist(a_name, b_name) <= 0.008:
                    adj[a_name].append(b_name)
                    adj[b_name].append(a_name)

        visited = {root_obj.name}
        queue = deque([root_obj.name])
        while queue:
            cur = queue.popleft()
            for nxt in adj[cur]:
                if nxt not in visited:
                    visited.add(nxt)
                    queue.append(nxt)
        disconnected_from_hull = sorted(n for n in names if n not in visited)

    return {
        "unit": unit_name,
        "mesh_count": len(render_objs),
        "total_faces": sum(len(o.data.polygons) for o in render_objs),
        "tagged_v2_objects": len(tagged_recipes),
        "missing_uv": missing_uv,
        "missing_color": missing_color,
        "bad_mat_count": bad_mat_count,
        "duplicate_suffixes": duplicate_suffixes,
        "tire_overlaps": tire_overlaps,
        "dozer_spade_overlap": dozer_spade_overlap,
        "disconnected_from_hull": disconnected_from_hull,
        "passed": (
            not missing_uv
            and not missing_color
            and not bad_mat_count
            and not duplicate_suffixes
            and not tire_overlaps
            and not dozer_spade_overlap
            and not disconnected_from_hull
            and len(tagged_recipes) >= 3
        ),
    }
