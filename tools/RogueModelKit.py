"""RogueModelKit: Reusable procedural 3D modeling toolkit for Rogue Command vehicles.

Provides parametric geometric primitives, scale-aware hard-surface builders,
continuous track loops, stepped wheels, recessed optics, advanced rotary weapons,
missile pods, energy emitters, bipedal legs, draw-call batching, multi-perspective
studio rendering, and automated quality diagnostics designed for Blender 4.5.14 LTS
and Unity 2021.3.45f2 FBX export.
"""

from __future__ import annotations
import json
import math
import re
from pathlib import Path
from typing import Any, Dict, Sequence, Tuple

import bpy, bmesh
from mathutils import Vector, Euler, Quaternion, Matrix

DEFAULT_FACE_BUDGET = 7500


# ---------------------------------------------------------------------------
# 1. Scene Management & Materials
# ---------------------------------------------------------------------------

def clear_scene() -> None:
    """Clear all objects, meshes, materials, and lights from the current scene."""
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def make_material(
    name: str,
    color: Tuple[float, float, float],
    metallic: float = 0.0,
    roughness: float = 0.55,
    emission_color: Tuple[float, float, float] | None = None,
    emission_strength: float = 1.0,
) -> bpy.types.Material:
    """Creates a Principled BSDF material with PBR properties and optional emission."""
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True
    tree = material.node_tree
    shader = tree.nodes.get("Principled BSDF")

    shader.inputs["Base Color"].default_value = (*color, 1.0)
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness

    if emission_color is not None:
        if "Emission Color" in shader.inputs:
            shader.inputs["Emission Color"].default_value = (*emission_color, 1.0)
        elif "Emission" in shader.inputs:
            shader.inputs["Emission"].default_value = (*emission_color, 1.0)
        if "Emission Strength" in shader.inputs:
            shader.inputs["Emission Strength"].default_value = emission_strength

    return material


def finish_mesh(
    obj: bpy.types.Object,
    material: bpy.types.Material | None,
    bevel: float = 0.0,
    smooth: bool = False,
    bevel_segments: int = 1,
) -> bpy.types.Object:
    """Assigns material and applies an optional scale-appropriate edge bevel."""
    if material is not None:
        obj.data.materials.append(material)

    for polygon in obj.data.polygons:
        polygon.use_smooth = smooth

    if bevel > 0.0:
        modifier = obj.modifiers.new("machined chamfers", "BEVEL")
        modifier.width = bevel
        modifier.segments = max(1, int(bevel_segments))
        modifier.limit_method = "ANGLE"
        modifier.angle_limit = math.radians(35)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        for polygon in obj.data.polygons:
            polygon.use_smooth = smooth

    obj.select_set(False)
    return obj


def make_lofted_superellipse_shell(
    name: str,
    stations: Sequence[Sequence[float]],
    material: bpy.types.Material | None,
    axis: str = "Y",
    segments: int = 24,
    exponent: float = 0.78,
    smooth: bool = True,
) -> bpy.types.Object:
    """Build a shaped armor shell from cross-section stations.

    Each station is ``(axis_position, center_a, center_b, half_a, half_b)``.
    The cross-section axes are X/Z for a Y loft, X/Y for a Z loft, and Y/Z
    for an X loft. ``exponent`` controls the transition from an ellipse toward
    a softly squared armor section; use profile stations to form shoulders,
    tapers, and sloped noses instead of stacking primitive boxes.
    """
    axis = axis.upper()
    if axis not in {"X", "Y", "Z"}:
        raise ValueError("axis must be X, Y, or Z")
    if len(stations) < 2:
        raise ValueError("a lofted shell requires at least two stations")
    if segments < 8 or exponent <= 0.0:
        raise ValueError("segments must be >= 8 and exponent must be positive")

    normalized = []
    for index, station in enumerate(stations):
        if len(station) != 5:
            raise ValueError("each station must contain five values")
        position, center_a, center_b, half_a, half_b = map(float, station)
        if half_a <= 0.0 or half_b <= 0.0:
            raise ValueError(f"station {index} must have positive cross-section radii")
        normalized.append((position, center_a, center_b, half_a, half_b))
    if any(b[0] <= a[0] for a, b in zip(normalized, normalized[1:])):
        raise ValueError("station axis positions must increase monotonically")

    vertices = []
    for position, center_a, center_b, half_a, half_b in normalized:
        for segment in range(segments):
            angle = 2.0 * math.pi * segment / segments
            unit_a = math.copysign(abs(math.cos(angle)) ** exponent, math.cos(angle))
            unit_b = math.copysign(abs(math.sin(angle)) ** exponent, math.sin(angle))
            a = center_a + unit_a * half_a
            b = center_b + unit_b * half_b
            if axis == "Y":
                point = (a, position, b)
            elif axis == "Z":
                point = (a, b, position)
            else:
                point = (position, a, b)
            vertices.append(point)

    faces = [tuple(reversed(range(segments)))]
    last_ring = (len(normalized) - 1) * segments
    faces.append(tuple(last_ring + segment for segment in range(segments)))
    for ring in range(len(normalized) - 1):
        lower = ring * segments
        upper = (ring + 1) * segments
        for segment in range(segments):
            following = (segment + 1) % segments
            faces.append((lower + segment, lower + following,
                          upper + following, upper + segment))

    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    if material is not None:
        mesh.materials.append(material)
    for polygon in mesh.polygons:
        polygon.use_smooth = smooth and polygon.index not in {0, 1}
    obj.select_set(False)
    return obj


def _auto_box_bevel(size: Sequence[float], bevel: float) -> float:
    """Widens bevels on major armor volumes while leaving small hardware crisp."""
    dimensions = [abs(float(component)) for component in size]
    if not dimensions or bevel <= 0.0:
        return bevel
    largest = max(dimensions)
    smallest = max(min(dimensions), 1e-5)
    if largest >= 1.10 and smallest >= 0.30 and bevel >= 0.018:
        return max(bevel, min(smallest * 0.16, 0.12))
    if largest >= 0.68 and smallest >= 0.16 and bevel >= 0.012:
        return max(bevel, min(smallest * 0.12, 0.075))
    return bevel


def _auto_bevel_segments(size: Sequence[float], bevel: float) -> int:
    """Adds rounded transitions to major forms without densifying small details."""
    dimensions = [abs(float(component)) for component in size]
    if not dimensions or bevel <= 0.0:
        return 1
    largest = max(dimensions)
    smallest = max(min(dimensions), 1e-5)
    bevel_ratio = bevel / smallest
    if largest >= 1.00 and smallest >= 0.30 and bevel_ratio <= 0.20:
        return 4
    if largest >= 0.62 and smallest >= 0.14 and bevel_ratio <= 0.22:
        return 3
    return 1


def _auto_armor_taper(name: str, size: Sequence[float], bevel: float) -> Tuple[float, float] | None:
    """Returns a restrained formed-plate taper for large named vehicle body masses."""
    dimensions = [abs(float(component)) for component in size]
    if not dimensions or bevel <= 0.0:
        return None
    lower_name = name.casefold()
    body_terms = ("armor_", "hull", "cab", "cabin", "fuselage", "torso", "cockpit", "cowl", "superstructure")
    if not any(term in lower_name for term in body_terms):
        return None

    largest = max(dimensions)
    smallest = max(min(dimensions), 1e-5)
    aspect_ratio = largest / smallest
    if largest >= 1.20 and smallest >= 0.28 and aspect_ratio <= 4.5:
        return (0.90, 0.94)
    if largest >= 0.78 and smallest >= 0.22 and aspect_ratio <= 3.5:
        return (0.95, 0.97)
    return None


def get_orthonormal_basis(direction: Vector) -> Tuple[Vector, Vector, Vector]:
    """Constructs a right-handed orthonormal basis (u_vec, v_vec, dir_vec) around direction."""
    dir_vec = direction.normalized()
    if abs(dir_vec.z) < 0.92:
        u_vec = dir_vec.cross(Vector((0.0, 0.0, 1.0))).normalized()
    else:
        u_vec = dir_vec.cross(Vector((1.0, 0.0, 0.0))).normalized()
    v_vec = dir_vec.cross(u_vec).normalized()
    return u_vec, v_vec, dir_vec


# ---------------------------------------------------------------------------
# 2. Geometric Primitives
# ---------------------------------------------------------------------------

def make_box(
    name: str,
    size: Tuple[float, float, float],
    location: Tuple[float, float, float] | Vector,
    material: bpy.types.Material | None,
    bevel: float = 0.025,
    rotation: Tuple[float, float, float] = (0.0, 0.0, 0.0),
    bevel_segments: int | None = None,
    smooth: bool | None = None,
) -> bpy.types.Object:
    """Creates a box with wider rounded transitions on major armor volumes."""
    if rotation is None:
        rotation = (0.0, 0.0, 0.0)
    resolved_bevel = _auto_box_bevel(size, bevel)
    resolved_segments = bevel_segments if bevel_segments is not None else _auto_bevel_segments(size, resolved_bevel)
    smooth_faces = resolved_segments > 1 if smooth is None else smooth
    top_scale = _auto_armor_taper(name, size, resolved_bevel)
    if top_scale is not None:
        obj = _make_tapered_shell(
            name,
            size,
            location,
            material,
            top_scale=top_scale,
            bevel=resolved_bevel,
            bevel_segments=resolved_segments,
            smooth=smooth_faces,
        )
        loc_v = Vector(location)
        for vertex in obj.data.vertices:
            vertex.co -= loc_v
        obj.location = loc_v
        obj.rotation_euler = rotation
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        return obj

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    obj.rotation_euler = rotation
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return finish_mesh(obj, material, resolved_bevel, smooth=smooth_faces, bevel_segments=resolved_segments)


def _make_tapered_shell(
    name: str,
    size: Tuple[float, float, float],
    location: Tuple[float, float, float] | Vector,
    material: bpy.types.Material | None,
    top_scale: Tuple[float, float] = (0.92, 0.92),
    bevel: float = 0.025,
    bevel_segments: int = 2,
    smooth: bool = True,
) -> bpy.types.Object:
    """Creates a tapered armor shell with a narrower roof and softened edge transitions."""
    sx, sy, sz = size
    scale_x, scale_y = top_scale
    if min(sx, sy, sz) <= 0.0:
        raise ValueError("shell dimensions must be positive")
    if not (0.0 < scale_x <= 1.0 and 0.0 < scale_y <= 1.0):
        raise ValueError("top_scale values must be in the range (0, 1]")

    loc = Vector(location)
    hx, hy = sx * 0.5, sy * 0.5
    z_bottom, z_top = loc.z - sz * 0.5, loc.z + sz * 0.5
    vertices = [
        (loc.x - hx, loc.y - hy, z_bottom), (loc.x + hx, loc.y - hy, z_bottom),
        (loc.x + hx, loc.y + hy, z_bottom), (loc.x - hx, loc.y + hy, z_bottom),
        (loc.x - hx * scale_x, loc.y - hy * scale_y, z_top),
        (loc.x + hx * scale_x, loc.y - hy * scale_y, z_top),
        (loc.x + hx * scale_x, loc.y + hy * scale_y, z_top),
        (loc.x - hx * scale_x, loc.y + hy * scale_y, z_top),
    ]
    faces = [
        (3, 2, 1, 0), (4, 5, 6, 7),
        (0, 1, 5, 4), (1, 2, 6, 5),
        (2, 3, 7, 6), (3, 0, 4, 7),
    ]
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    return finish_mesh(obj, material, bevel, smooth=smooth, bevel_segments=bevel_segments)


def make_cylinder(
    name: str,
    radius: float,
    depth: float,
    location: Tuple[float, float, float] | Vector,
    material: bpy.types.Material | None,
    rotation: Tuple[float, float, float] = (0.0, 0.0, 0.0),
    vertices: int = 12,
    bevel: float | None = None,
    bevel_segments: int = 1,
) -> bpy.types.Object:
    """Creates a cylinder with baked scale and proportional chamfers."""
    if rotation is None:
        rotation = (0.0, 0.0, 0.0)
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth, location=location, rotation=rotation
    )
    obj = bpy.context.object
    obj.name = name
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if bevel is None:
        bevel = min(radius * 0.16, depth * 0.14)
    obj = finish_mesh(obj, material, bevel, bevel_segments=bevel_segments)
    axis = Euler(rotation).to_matrix() @ Vector((0.0, 0.0, 1.0))
    for polygon in obj.data.polygons:
        polygon.use_smooth = abs(polygon.normal.dot(axis)) < 0.999
    return obj




def make_tapered_cylinder(
    name: str,
    radius_base: float,
    radius_tip: float,
    depth: float,
    location: Tuple[float, float, float] | Vector,
    material: bpy.types.Material | None,
    rotation: Tuple[float, float, float] = (0.0, 0.0, 0.0),
    vertices: int = 12,
    bevel: float | None = None,
    bevel_segments: int = 1,
) -> bpy.types.Object:
    """Creates a conical tapered cylinder (truncated cone) with baked scale and proportional chamfers.

    radius_base sits at local -Z; radius_tip sits at local +Z. When aligned with
    a forward direction vector tracking +Z, radius_base is at the rear/breech
    and radius_tip is at the front/muzzle.
    """
    if rotation is None:
        rotation = (0.0, 0.0, 0.0)
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices,
        radius1=radius_base,
        radius2=radius_tip,
        depth=depth,
        location=location,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if bevel is None:
        bevel = min(radius_tip * 0.16, depth * 0.14)
    obj = finish_mesh(obj, material, bevel, bevel_segments=bevel_segments)
    axis = Euler(rotation).to_matrix() @ Vector((0.0, 0.0, 1.0))
    for polygon in obj.data.polygons:
        polygon.use_smooth = abs(polygon.normal.dot(axis)) < 0.999
    return obj

def make_rod(
    name: str,
    start: Sequence[float] | Vector,
    end: Sequence[float] | Vector,
    radius: float,
    material: bpy.types.Material | None,
    vertices: int = 8,
    bevel: float | None = None,
    bevel_segments: int = 1,
) -> bpy.types.Object:
    """Creates a rod/pipe between two arbitrary 3D endpoints."""
    start_v = Vector(start)
    end_v = Vector(end)
    direction = end_v - start_v
    length = direction.length
    if length < 1e-6:
        length = 1e-6

    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices,
        radius=radius,
        depth=length,
        location=(start_v + end_v) * 0.5,
    )
    obj = bpy.context.object
    obj.name = name
    obj.rotation_euler = direction.to_track_quat("Z", "Y").to_euler()
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if bevel is None:
        bevel = min(radius * 0.18, length * 0.03)
    obj = finish_mesh(obj, material, bevel, bevel_segments=bevel_segments)
    axis = direction.normalized() if direction.length > 1e-6 else Vector((0.0, 0.0, 1.0))
    for polygon in obj.data.polygons:
        polygon.use_smooth = abs(polygon.normal.dot(axis)) < 0.999
    return obj


def make_torus(
    name: str,
    location: Tuple[float, float, float] | Vector,
    major_radius: float,
    minor_radius: float,
    material: bpy.types.Material | None,
    rotation: Tuple[float, float, float] = (0.0, 0.0, 0.0),
    major_segments: int = 12,
    minor_segments: int = 6,
) -> bpy.types.Object:
    """Creates a low-poly torus for energy rings, conduits, and rim trims."""
    bpy.ops.mesh.primitive_torus_add(
        major_segments=major_segments,
        minor_segments=minor_segments,
        major_radius=major_radius,
        minor_radius=minor_radius,
        location=location,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    obj = finish_mesh(obj, material, 0.0, smooth=True)
    return obj


# ---------------------------------------------------------------------------
# 3. Macro Chassis & Hull Assemblies
# ---------------------------------------------------------------------------

def make_sloped_glacis_hull(
    name: str,
    length_front: float,
    length_rear: float,
    width_front: float,
    width_rear: float,
    height_lower: float,
    height_upper: float,
    material: bpy.types.Material | None,
    bevel: float = 0.045,
    bevel_segments: int = 2,
) -> bpy.types.Object:
    """Creates a swept hull with rounded shoulders, shaped lower sides, and a sloped glacis."""
    if min(length_front, length_rear, width_front, width_rear) <= 0.0:
        raise ValueError("hull lengths and widths must be positive")
    if height_upper <= height_lower:
        raise ValueError("height_upper must be greater than height_lower")

    # A rounded-rectangle cross-section keeps the deck broad while rolling the
    # shoulders and narrowing the lower hull above the track line.
    cross_section = (
        (-0.84, 1.00), (-0.48, 1.00), (0.00, 1.00), (0.48, 1.00), (0.84, 1.00),
        (0.92, 0.985), (0.97, 0.95), (0.995, 0.90), (1.00, 0.84),
        (1.00, 0.70), (0.995, 0.57), (0.985, 0.44), (0.965, 0.32),
        (0.94, 0.22), (0.91, 0.13), (0.88, 0.06), (0.84, 0.00),
        (-0.84, 0.00), (-0.88, 0.06), (-0.91, 0.13), (-0.94, 0.22),
        (-0.965, 0.32), (-0.985, 0.44), (-0.995, 0.57), (-1.00, 0.70),
        (-1.00, 0.84), (-0.995, 0.90), (-0.97, 0.95), (-0.92, 0.985),
    )
    height_span = height_upper - height_lower
    middle_width = (width_front + width_rear) * 0.5
    stations = (
        (-length_rear, -length_rear * 0.95, width_rear * 0.90, height_lower, height_upper - height_span * 0.02),
        (-length_rear * 0.72, -length_rear * 0.68, width_rear, height_lower, height_upper),
        (-length_rear * 0.24, -length_rear * 0.16, middle_width, height_lower, height_upper),
        (length_front * 0.36, length_front * 0.48, width_front, height_lower, height_upper),
        (length_front * 0.58, length_front * 0.76, width_front * 0.98, height_lower, height_upper - height_span * 0.06),
        (length_front * 0.65, length_front, width_front * 0.72, height_lower, height_lower + height_span * 0.56),
    )
    ring_size = len(cross_section)
    vertices = []
    for bottom_y, top_y, width, bottom_z, top_z in stations:
        for x_ratio, height_ratio in cross_section:
            vertices.append((
                x_ratio * width * 0.5,
                bottom_y + (top_y - bottom_y) * height_ratio,
                bottom_z + (top_z - bottom_z) * height_ratio,
            ))

    faces = [tuple(reversed(range(ring_size)))]
    last_ring_start = (len(stations) - 1) * ring_size
    faces.append(tuple(last_ring_start + i for i in range(ring_size)))
    for station_index in range(len(stations) - 1):
        lower_start = station_index * ring_size
        upper_start = (station_index + 1) * ring_size
        for profile_index in range(ring_size):
            next_index = (profile_index + 1) % ring_size
            faces.append((
                lower_start + profile_index,
                lower_start + next_index,
                upper_start + next_index,
                upper_start + profile_index,
            ))

    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    return finish_mesh(obj, material, bevel, smooth=True, bevel_segments=bevel_segments)


def make_faceted_turret_housing(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    size: Tuple[float, float, float] = (1.4, 1.6, 0.45),
    armor_mat: bpy.types.Material | None = None,
    roof_mat: bpy.types.Material | None = None,
    bevel: float = 0.075,
) -> list[bpy.types.Object]:
    """Generates an authentic compound faceted ballistic turret housing with armored bearing race collar, sloped kinetic deflector cheeks, undercut rear bustle, and cast mantlet cradle."""
    objects = []
    loc_v = Vector(location)
    sx, sy, sz = size
    hx, hy, hz = sx * 0.5, sy * 0.5, sz * 0.5
    cr_mat = armor_mat
    rf_mat = roof_mat if roof_mat else armor_mat

    # 1. Armored Turret Bearing Ring / Race Collar
    # Physically seats the turret onto the hull deck, eliminating the floating-box appearance
    ring_r = min(hx, hy) * 0.88
    ring_h = 0.08
    ring_loc = Vector((loc_v.x, loc_v.y, loc_v.z - hz - ring_h * 0.5))
    objects.append(make_cylinder(
        f"{prefix}_TurretRing",
        radius=ring_r,
        depth=ring_h,
        location=ring_loc,
        material=cr_mat,
        vertices=16,
        bevel=0.012,
    ))

    # 2. Compound 16-Vertex Faceted Monocoque Turret Shell
    z_bot = loc_v.z - hz
    z_bustle_bot = loc_v.z - hz + sz * 0.28  # Undercut rear bustle allows 360° engine deck clearance!
    z_top = loc_v.z + hz
    z_brow_top = loc_v.z + hz * 0.82  # Sloped front roof brow for ballistic deflection

    # Bottom 8 vertices (front mantlet notch, wedge cheeks, mid flanks, undercut bustle)
    v_bottom = [
        Vector((loc_v.x - hx * 0.44, loc_v.y + hy * 0.76, z_bot)),
        Vector((loc_v.x - hx * 1.00, loc_v.y + hy * 0.28, z_bot)),
        Vector((loc_v.x - hx * 0.94, loc_v.y - hy * 0.28, z_bot + (z_bustle_bot - z_bot) * 0.3)),
        Vector((loc_v.x - hx * 0.72, loc_v.y - hy * 1.00, z_bustle_bot)),
        Vector((loc_v.x + hx * 0.72, loc_v.y - hy * 1.00, z_bustle_bot)),
        Vector((loc_v.x + hx * 0.94, loc_v.y - hy * 0.28, z_bot + (z_bustle_bot - z_bot) * 0.3)),
        Vector((loc_v.x + hx * 1.00, loc_v.y + hy * 0.28, z_bot)),
        Vector((loc_v.x + hx * 0.44, loc_v.y + hy * 0.76, z_bot)),
    ]
    # Top 8 vertices (tumblehome inward slope & raked brow)
    v_top = [
        Vector((loc_v.x - hx * 0.36, loc_v.y + hy * 0.65, z_brow_top)),
        Vector((loc_v.x - hx * 0.84, loc_v.y + hy * 0.22, z_top)),
        Vector((loc_v.x - hx * 0.78, loc_v.y - hy * 0.26, z_top)),
        Vector((loc_v.x - hx * 0.62, loc_v.y - hy * 0.92, z_top)),
        Vector((loc_v.x + hx * 0.62, loc_v.y - hy * 0.92, z_top)),
        Vector((loc_v.x + hx * 0.78, loc_v.y - hy * 0.26, z_top)),
        Vector((loc_v.x + hx * 0.84, loc_v.y + hy * 0.22, z_top)),
        Vector((loc_v.x + hx * 0.36, loc_v.y + hy * 0.65, z_brow_top)),
    ]
    all_verts = [v[:] for v in v_bottom + v_top]
    faces = [tuple(range(7, -1, -1)), tuple(range(8, 16))]
    for i in range(8):
        ni = (i + 1) % 8
        faces.append((i, ni, ni + 8, i + 8))

    mesh = bpy.data.meshes.new(f"{prefix}_BaseShell_Mesh")
    mesh.from_pydata(all_verts, [], faces)
    mesh.update()
    shell_obj = bpy.data.objects.new(f"{prefix}_BaseShell", mesh)
    bpy.context.collection.objects.link(shell_obj)
    bpy.context.view_layer.objects.active = shell_obj
    shell_obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    shell_bevel = min(bevel, sz * 0.25, sx * 0.08) if bevel > 0.0 else 0.03
    shell_obj = finish_mesh(shell_obj, cr_mat, bevel=shell_bevel, smooth=True, bevel_segments=2)
    objects.append(shell_obj)

    # 3. Recessed Cast Gun Mantlet Cradle
    mantlet_w = sx * 0.52
    mantlet_l = sy * 0.32
    mantlet_h = sz * 0.82
    mantlet_loc = loc_v + Vector((0.0, hy * 0.68, 0.0))
    objects.append(make_box(
        f"{prefix}_MantletHousing",
        (mantlet_w, mantlet_l, mantlet_h),
        mantlet_loc,
        rf_mat,
        bevel=min(bevel * 0.82, sz * 0.22),
        rotation=(math.radians(-6), 0.0, 0.0),
        bevel_segments=2,
        smooth=True,
    ))

    # 4. Rear Bustle Counterweight / Electronics Compartment with Undercut Chamfer
    bustle_w = sx * 0.72
    bustle_l = sy * 0.38
    bustle_h = sz * 0.72
    bustle_loc = loc_v + Vector((0.0, -hy * 0.74, sz * 0.10))
    objects.append(make_box(
        f"{prefix}_RearBustle",
        (bustle_w, bustle_l, bustle_h),
        bustle_loc,
        cr_mat,
        bevel=min(bevel * 0.75, sz * 0.20),
        bevel_segments=2,
        smooth=True,
    ))

    # 5. Bilateral Angled Composite Cheek Armor Panels
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        cheek_pos = loc_v + Vector((side * (hx * 0.72), hy * 0.38, hz * 0.05))
        objects.append(make_box(
            f"{prefix}_CheekArmor_{stag}",
            (hx * 0.38, hy * 0.48, sz * 0.65),
            cheek_pos,
            rf_mat,
            bevel=0.018,
            rotation=(math.radians(6), -side * math.radians(12), side * math.radians(22)),
            bevel_segments=2,
            smooth=True,
        ))

    return objects


def apply_procedural_armor_plating(
    obj: bpy.types.Object,
    thickness: float = 0.035,
    inset_depth: float = 0.045,
    bevel_width: float = 0.012,
) -> bpy.types.Object:
    """Applies procedural BMesh armor plating by creating recessed panel gutters and extruding armor facets."""
    if obj is None or obj.type != "MESH" or not obj.data.polygons:
        return obj

    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.faces.ensure_lookup_table()

    # Identify candidate upward or outward facing planar faces suitable for modular plating
    candidate_faces = [
        f for f in bm.faces
        if f.calc_area() > 0.08 and (f.normal.z > -0.2)
    ]

    if candidate_faces:
        try:
            # 1. Inset candidate faces to establish panel gutters/seams
            res_inset = bmesh.ops.inset_individual(
                bm, faces=candidate_faces, thickness=inset_depth, depth=0.0
            )
            inset_faces = [f for f in res_inset.get("faces", []) if f.is_valid]
            if inset_faces:
                # 2. Extrude the inset plates outward along their face normal
                res_extrude = bmesh.ops.extrude_face_region(bm, geom=inset_faces)
                extruded_verts = [v for v in res_extrude.get("geom", []) if isinstance(v, bmesh.types.BMVert)]
                for f in inset_faces:
                    for v in f.verts:
                        v.co += f.normal * thickness
        except Exception:
            pass

    bm.normal_update()
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()

    if bevel_width > 0.0:
        finish_mesh(obj, None, bevel=bevel_width)

    return obj


# ---------------------------------------------------------------------------
# 4. Mobility Subsystems (Tracks, Wheels & Bipedal Legs)
# ---------------------------------------------------------------------------

def make_continuous_track_belt(
    name: str,
    x_pos: float,
    length: float,
    height: float,
    width: float,
    tread_mat: bpy.types.Material | None,
    ground_z: float = 0.08,
    y_pos: float = 0.0,
    track_sag: float = 0.0,
) -> bpy.types.Object:
    """Creates a continuous articulated track belt with sloped front climb, rounded return idlers, and optional catenary sag."""
    hw = width * 0.5
    half_l = length * 0.5
    approach_y = half_l * 0.68
    front_apex_y = half_l * 0.98
    front_apex_z = ground_z + (height - ground_z) * 0.45

    rear_apex_y = -half_l * 0.98
    rear_apex_z = ground_z + (height - ground_z) * 0.45
    rear_approach_y = -half_l * 0.72

    if track_sag <= 0.001:
        vertices = [
            # Ground contact run
            (-hw, rear_approach_y + y_pos, ground_z),   # 0
            ( hw, rear_approach_y + y_pos, ground_z),   # 1
            ( hw, approach_y + y_pos, ground_z),        # 2
            (-hw, approach_y + y_pos, ground_z),        # 3
            # Front climb
            ( hw, front_apex_y + y_pos, front_apex_z),  # 4
            (-hw, front_apex_y + y_pos, front_apex_z),  # 5
            # Front top curve
            ( hw, half_l * 0.82 + y_pos, height),       # 6
            (-hw, half_l * 0.82 + y_pos, height),       # 7
            # Top return run
            ( hw, -half_l * 0.80 + y_pos, height),      # 8
            (-hw, -half_l * 0.80 + y_pos, height),      # 9
            # Rear over curve
            ( hw, rear_apex_y + y_pos, rear_apex_z),    # 10
            (-hw, rear_apex_y + y_pos, rear_apex_z),    # 11
        ]

        faces = [
            (3, 2, 1, 0),        # Bottom ground run
            (5, 4, 2, 3),        # Front approach climb
            (7, 6, 4, 5),        # Front over-curve
            (9, 8, 6, 7),        # Top return run
            (11, 10, 8, 9),      # Rear over-curve
            (0, 1, 10, 11),      # Rear under-curve
            (0, 11, 9, 7, 5, 3), # Left side wall
            (1, 2, 4, 6, 8, 10), # Right side wall
        ]
    else:
        # Catenary Sag Track: 4 stations across top return run
        y_top_front = half_l * 0.82 + y_pos
        y_top_rear = -half_l * 0.80 + y_pos
        top_span = y_top_front - y_top_rear

        sag_1 = height - track_sag * math.sin(math.pi * 0.67)
        y_sag_1 = y_top_rear + top_span * 0.67

        sag_2 = height - track_sag * math.sin(math.pi * 0.33)
        y_sag_2 = y_top_rear + top_span * 0.33

        vertices = [
            # Ground contact run
            (-hw, rear_approach_y + y_pos, ground_z),   # 0
            ( hw, rear_approach_y + y_pos, ground_z),   # 1
            ( hw, approach_y + y_pos, ground_z),        # 2
            (-hw, approach_y + y_pos, ground_z),        # 3
            # Front climb
            ( hw, front_apex_y + y_pos, front_apex_z),  # 4
            (-hw, front_apex_y + y_pos, front_apex_z),  # 5
            # Front top curve
            ( hw, y_top_front, height),                 # 6
            (-hw, y_top_front, height),                 # 7
            # Sag station 1
            ( hw, y_sag_1, sag_1),                      # 8
            (-hw, y_sag_1, sag_1),                      # 9
            # Sag station 2
            ( hw, y_sag_2, sag_2),                      # 10
            (-hw, y_sag_2, sag_2),                      # 11
            # Top return rear point
            ( hw, y_top_rear, height),                  # 12
            (-hw, y_top_rear, height),                  # 13
            # Rear over curve
            ( hw, rear_apex_y + y_pos, rear_apex_z),    # 14
            (-hw, rear_apex_y + y_pos, rear_apex_z),    # 15
        ]

        faces = [
            (3, 2, 1, 0),                # Bottom ground run
            (5, 4, 2, 3),                # Front approach climb
            (7, 6, 4, 5),                # Front over-curve
            (9, 8, 6, 7),                # Top return run front segment
            (11, 10, 8, 9),              # Top return run mid catenary segment
            (13, 12, 10, 11),            # Top return run rear segment
            (15, 14, 12, 13),            # Rear over-curve
            (0, 1, 14, 15),              # Rear under-curve
            (0, 15, 13, 11, 9, 7, 5, 3), # Left side wall
            (1, 2, 4, 6, 8, 10, 12, 14), # Right side wall
        ]

    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    obj.location = (x_pos, 0.0, 0.0)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=True)
    return finish_mesh(obj, tread_mat, 0.02)


def make_stepped_roadwheel(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    radius: float,
    width: float,
    rim_mat: bpy.types.Material | None,
    hub_mat: bpy.types.Material | None,
    axle_mat: bpy.types.Material | None,
    vertices: int = 12,
) -> list[bpy.types.Object]:
    """Creates a 3-tier road wheel: outer tread rim, recessed hub dish, and center axle cap."""
    objects = []
    loc_v = Vector(location)
    wheel_rot = (0.0, math.radians(90), 0.0)
    # 1. Main outer rim
    objects.append(make_cylinder(
        f"{prefix}_Rim", radius, width, loc_v, rim_mat,
        rotation=wheel_rot, vertices=vertices, bevel=radius * 0.14
    ))
    # 2. Recessed inner dish on outer face
    sign = 1.0 if loc_v.x > 0 else -1.0
    dish_offset = sign * (width * 0.5 + 0.004)
    objects.append(make_cylinder(
        f"{prefix}_Dish", radius * 0.74, 0.012,
        (loc_v.x + dish_offset, loc_v.y, loc_v.z), hub_mat,
        rotation=wheel_rot, vertices=max(8, vertices - 2), bevel=0.008
    ))
    # 3. Center hex axle cap
    cap_offset = sign * (width * 0.5 + 0.018)
    objects.append(make_cylinder(
        f"{prefix}_AxleCap", radius * 0.32, 0.025,
        (loc_v.x + cap_offset, loc_v.y, loc_v.z), axle_mat,
        rotation=wheel_rot, vertices=8, bevel=0.006
    ))
    return objects


def make_continuous_track_assembly(
    prefix: str,
    x_pos: float,
    length: float = 2.85,
    height: float = 0.62,
    belt_width: float = 0.34,
    roadwheel_count: int = 5,
    tread_mat: bpy.types.Material | None = None,
    rim_mat: bpy.types.Material | None = None,
    hub_mat: bpy.types.Material | None = None,
    axle_mat: bpy.types.Material | None = None,
    fender_mat: bpy.types.Material | None = None,
    skirt_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    ground_z: float = 0.06,
    center_y: float = 0.0,
    track_sag: float = 0.0,
    with_suspension_arms: bool = False,
    with_spaced_skirts: bool = False,
) -> list[bpy.types.Object]:
    """Generates a complete parametric continuous track assembly with wheels, cleats, and fenders."""
    objects = []
    side_tag = "L" if x_pos < 0 else "R"
    sign = 1.0 if x_pos >= 0 else -1.0

    # 1. Articulated continuous track belt
    belt = make_continuous_track_belt(
        f"{prefix}_Belt_{side_tag}",
        x_pos=x_pos, length=length, height=height, width=belt_width,
        tread_mat=tread_mat, ground_z=ground_z, y_pos=center_y,
        track_sag=track_sag,
    )
    objects.append(belt)

    # 2. Stepped road wheels array along ground run
    half_l = length * 0.5
    usable_l = half_l * 0.72
    step_y = (usable_l * 2.0) / (roadwheel_count - 1) if roadwheel_count > 1 else 0.0
    wheel_radius = (height - ground_z) * 0.34
    wheel_center_z = ground_z + wheel_radius + 0.02
    roadwheel_centers = []

    for idx in range(roadwheel_count):
        y = center_y - usable_l + idx * step_y
        wheel_loc = (x_pos + sign * 0.015, y, wheel_center_z)
        roadwheel_centers.append(wheel_loc)
        wheels = make_stepped_roadwheel(
            f"{prefix}_RoadWheel_{side_tag}_{idx+1}",
            location=wheel_loc, radius=wheel_radius, width=belt_width * 0.38,
            rim_mat=rim_mat, hub_mat=hub_mat, axle_mat=axle_mat, vertices=12
        )
        objects.extend(wheels)

    # 2b. Optional trailing-arm suspension linkages with torsion bosses and dampers
    if with_suspension_arms and axle_mat is not None:
        susp_objs = make_swing_arm_suspension_linkage(
            f"{prefix}_Suspension_{side_tag}",
            roadwheel_centers=roadwheel_centers,
            side=int(sign),
            arm_length=wheel_radius * 1.08,
            material=axle_mat,
            boss_mat=hub_mat or axle_mat,
            with_coil_spring=True,
        )
        objects.extend(susp_objs)

    # 3. Drive sprocket (rear) & Front idler wheel
    for y_rel, name_tag in [(-half_l * 0.88, "DriveSprocket"), (half_l * 0.88, "FrontIdler")]:
        idler_wheels = make_stepped_roadwheel(
            f"{prefix}_{name_tag}_{side_tag}",
            location=(x_pos + sign * 0.015, center_y + y_rel, ground_z + (height - ground_z) * 0.48),
            radius=wheel_radius * 0.95, width=belt_width * 0.36,
            rim_mat=axle_mat, hub_mat=rim_mat, axle_mat=axle_mat, vertices=12
        )
        objects.extend(idler_wheels)

    # 4. Top run track cleats for mechanical rhythm
    cleat_count = max(6, int(length * 3.0))
    cleat_step = (length * 0.72) / (cleat_count - 1)
    cleat_start_y = center_y - length * 0.36
    top_y_rear = center_y - half_l * 0.80
    top_y_front = center_y + half_l * 0.82
    top_span = top_y_front - top_y_rear

    for c_idx in range(cleat_count):
        cy = cleat_start_y + c_idx * cleat_step
        if track_sag > 0.001 and top_span > 1e-4:
            t_cleat = max(0.0, min(1.0, (cy - top_y_rear) / top_span))
            cz = height + 0.02 - track_sag * math.sin(math.pi * t_cleat)
        else:
            cz = height + 0.02
        objects.append(make_box(
            f"{prefix}_Cleat_{side_tag}_{c_idx+1}",
            (belt_width * 0.88, cleat_step * 0.55, 0.045),
            (x_pos, cy, cz), tread_mat, bevel=0.008
        ))

    # 5. Optional upper mudguard / fender and armored side skirts
    if fender_mat is not None:
        fender_w = belt_width * 1.15
        objects.append(make_box(
            f"{prefix}_FenderTop_{side_tag}",
            (fender_w, length * 0.96, 0.06),
            (x_pos, center_y, height + 0.08), fender_mat, bevel=0.02
        ))
        # Front angled splash guard
        objects.append(make_box(
            f"{prefix}_FenderSplash_{side_tag}",
            (fender_w, length * 0.16, 0.05),
            (x_pos, center_y + length * 0.52, height + 0.02), fender_mat, bevel=0.015,
            rotation=(math.radians(35), 0.0, 0.0)
        ))
        # Rear angled splash guard / rubber mudflap
        objects.append(make_box(
            f"{prefix}_FenderRearMudflap_{side_tag}",
            (fender_w, length * 0.14, 0.04),
            (x_pos, center_y - length * 0.52, height + 0.02),
            tread_mat or fender_mat,
            bevel=0.012,
            rotation=(math.radians(-32), 0.0, 0.0),
        ))

    if with_spaced_skirts and skirt_mat is not None:
        skirt_objs = make_spaced_armor_side_skirts(
            f"{prefix}_SpacedSkirt_{side_tag}",
            x_pos=x_pos + sign * (belt_width * 0.58),
            length=length * 0.90,
            height=height * 0.58,
            z_center=ground_z + height * 0.52,
            center_y=center_y,
            armor_mat=skirt_mat,
            trim_mat=axle_mat,
            rubber_mat=tread_mat,
            with_mudflap=True,
        )
        objects.extend(skirt_objs)
    elif skirt_mat is not None:
        skirt_x = x_pos + sign * (belt_width * 0.55)
        objects.append(make_box(
            f"{prefix}_ArmorSkirt_{side_tag}",
            (0.04, length * 0.88, height * 0.55),
            (skirt_x, center_y, ground_z + height * 0.52), skirt_mat, bevel=0.015
        ))
        if accent_mat is not None:
            objects.append(make_box(
                f"{prefix}_SkirtStripe_{side_tag}",
                (0.015, length * 0.55, 0.065),
                (skirt_x + sign * 0.022, center_y, ground_z + height * 0.58),
                accent_mat, bevel=0.006
            ))

    return objects


def make_swing_arm_suspension_linkage(
    prefix: str,
    roadwheel_centers: Sequence[Tuple[float, float, float] | Vector],
    side: int = 1,
    arm_length: float = 0.26,
    arm_width: float = 0.045,
    arm_thickness: float = 0.035,
    boss_radius: float = 0.048,
    material: bpy.types.Material | None = None,
    boss_mat: bpy.types.Material | None = None,
    with_coil_spring: bool = True,
) -> list[bpy.types.Object]:
    """Generates trailing-arm suspension linkages with torsion bar pivot bosses linking roadwheels to hull."""
    objects = []
    sign = 1.0 if side >= 0 else -1.0
    boss_rot = (0.0, math.radians(90.0), 0.0)

    for idx, center in enumerate(roadwheel_centers, start=1):
        c_v = Vector(center)
        # Torsion bar boss pivot on the hull side (inboard and slightly forward/upward)
        pivot_y = c_v.y + arm_length * 0.65
        pivot_z = c_v.z + arm_length * 0.60
        pivot_x = c_v.x - sign * 0.10
        pivot_loc = Vector((pivot_x, pivot_y, pivot_z))

        # 1. Torsion bar boss housing
        objects.append(make_cylinder(
            f"{prefix}_TorsionBoss_{idx}",
            radius=boss_radius,
            depth=0.08,
            location=pivot_loc,
            material=boss_mat or material,
            rotation=boss_rot,
            vertices=8,
            bevel=0.008,
        ))

        # 2. Diagonal trailing swing arm connecting boss to wheel axle
        arm_vec = c_v - pivot_loc
        arm_mid = (pivot_loc + c_v) * 0.5
        arm_dist = arm_vec.length
        if arm_dist > 1e-4:
            arm_dir = arm_vec.normalized()
            arm_rot = arm_dir.to_track_quat("Z", "Y").to_euler()
            objects.append(make_box(
                f"{prefix}_SwingArm_{idx}",
                (arm_width, arm_thickness, arm_dist * 0.95),
                arm_mid,
                material,
                bevel=0.008,
                rotation=arm_rot,
            ))

        # 3. Optional coilover / hydraulic damper connecting arm midpoint to upper hull sponson
        if with_coil_spring:
            shock_top = Vector((pivot_x, arm_mid.y, pivot_z + 0.16))
            shock_vec = shock_top - arm_mid
            shock_len = shock_vec.length
            if shock_len > 1e-4:
                shock_rot = shock_vec.normalized().to_track_quat("Z", "Y").to_euler()
                objects.append(make_cylinder(
                    f"{prefix}_ShockDamper_{idx}",
                    radius=boss_radius * 0.55,
                    depth=shock_len * 0.90,
                    location=(arm_mid + shock_top) * 0.5,
                    material=boss_mat or material,
                    rotation=shock_rot,
                    vertices=6,
                    bevel=0.005,
                ))

    return objects


def make_spaced_armor_side_skirts(
    prefix: str,
    x_pos: float,
    length: float = 2.7,
    height: float = 0.42,
    thickness: float = 0.028,
    z_center: float = 0.52,
    center_y: float = 0.0,
    panel_count: int = 4,
    gap: float = 0.035,
    standoff: float = 0.08,
    armor_mat: bpy.types.Material | None = None,
    trim_mat: bpy.types.Material | None = None,
    rubber_mat: bpy.types.Material | None = None,
    with_mudflap: bool = True,
) -> list[bpy.types.Object]:
    """Generates multi-panel spaced composite armor side skirts with mounting standoffs and rear rubber mudflaps."""
    objects = []
    sign = 1.0 if x_pos >= 0 else -1.0
    usable_len = length - (panel_count - 1) * gap
    panel_len = usable_len / max(1, panel_count)
    start_y = center_y - length * 0.5 + panel_len * 0.5

    # Top longitudinal mounting rail
    objects.append(make_box(
        f"{prefix}_MountRail",
        (0.045, length * 1.02, 0.035),
        (x_pos - sign * (standoff * 0.35), center_y, z_center + height * 0.50),
        trim_mat or armor_mat,
        bevel=0.006,
    ))

    for idx in range(panel_count):
        py = start_y + idx * (panel_len + gap)
        # Segmented armor plate
        objects.append(make_box(
            f"{prefix}_SkirtPanel_{idx+1}",
            (thickness, panel_len, height),
            (x_pos, py, z_center),
            armor_mat,
            bevel=0.012,
        ))

        # Standoff brackets connecting plate to hull sponson
        for sy_offset in (-panel_len * 0.32, panel_len * 0.32):
            bracket_loc = Vector((
                x_pos - sign * (standoff * 0.5),
                py + sy_offset,
                z_center + height * 0.28
            ))
            objects.append(make_box(
                f"{prefix}_Standoff_{idx+1}_{1 if sy_offset < 0 else 2}",
                (standoff * 0.95, 0.035, 0.035),
                bracket_loc,
                trim_mat or armor_mat,
                bevel=0.004,
            ))

    # Rear rubber mudflap
    if with_mudflap and rubber_mat is not None:
        flap_y = center_y - length * 0.5 - 0.08
        flap_z = z_center - height * 0.15
        objects.append(make_box(
            f"{prefix}_MudFlap",
            (thickness * 0.8, 0.018, height * 0.65),
            (x_pos, flap_y, flap_z),
            rubber_mat,
            bevel=0.004,
            rotation=(math.radians(14.0), 0.0, 0.0),
        ))

    return objects


def make_thrown_track_debris(
    prefix: str,
    origin: Tuple[float, float, float] | Vector,
    length: float = 1.8,
    width: float = 0.32,
    cleat_count: int = 8,
    material: bpy.types.Material | None = None,
    cleat_mat: bpy.types.Material | None = None,
    undulation: float = 0.035,
) -> list[bpy.types.Object]:
    """Generates an uncoiled, crumpled section of thrown track shoes lying on terrain for wreckage."""
    objects = []
    orig_v = Vector(origin)
    step_y = length / max(1, cleat_count - 1)

    # Base track rubber/steel band strip on ground
    objects.append(make_box(
        f"{prefix}_BandStrip",
        (width * 0.92, length * 1.02, 0.025),
        (orig_v.x, orig_v.y + length * 0.5, orig_v.z + 0.015),
        material,
        bevel=0.005,
    ))

    # Individual cleats along the thrown band with slight height wave/crumple
    for i in range(cleat_count):
        y_pos = orig_v.y + i * step_y
        z_wave = orig_v.z + 0.025 + undulation * math.sin(i * 1.2)
        tilt_x = math.radians(math.sin(i * 0.9) * 8.0)
        objects.append(make_box(
            f"{prefix}_Cleat_{i+1}",
            (width * 0.98, step_y * 0.50, 0.035),
            (orig_v.x, y_pos, z_wave),
            cleat_mat or material,
            bevel=0.006,
            rotation=(tilt_x, 0.0, 0.0),
        ))

    return objects


def make_all_terrain_wheel_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    radius: float = 0.35,
    width: float = 0.26,
    tire_mat: bpy.types.Material | None = None,
    rim_mat: bpy.types.Material | None = None,
    hub_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    lug_count: int = 10,
    lug_depth: float = 0.035,
    bolt_count: int = 6,
) -> list[bpy.types.Object]:
    """Generates a heavy tactical off-road wheel with chevron tread lugs and planetary gear hub."""
    objects = []
    loc_v = Vector(location)
    sign = 1.0 if loc_v.x >= 0 else -1.0
    wheel_rot = (0.0, math.radians(90), 0.0)

    # 1. Main outer tire carcass
    objects.append(make_cylinder(
        f"{prefix}_TireCarcass", radius, width,
        loc_v, tire_mat, rotation=wheel_rot, vertices=14, bevel=radius * 0.12
    ))

    # 2. Chevron directional tread lugs arrayed around circumference
    lug_angle_step = (2.0 * math.pi) / lug_count
    for i in range(lug_count):
        angle = i * lug_angle_step
        lug_y = loc_v.y + math.cos(angle) * (radius * 0.96)
        lug_z = loc_v.z + math.sin(angle) * (radius * 0.96)
        lug_loc = (loc_v.x, lug_y, lug_z)
        lug_rot = (angle, math.radians(90), 0.0)

        objects.append(make_box(
            f"{prefix}_TreadLug_{i+1}",
            (width * 0.82, lug_depth * 1.5, lug_depth * 1.8),
            lug_loc, tire_mat, bevel=0.006, rotation=lug_rot
        ))

    # 3. Deep-dish steel rim (recessed inside tire)
    rim_offset = sign * (width * 0.35)
    rim_loc = (loc_v.x + rim_offset, loc_v.y, loc_v.z)
    objects.append(make_cylinder(
        f"{prefix}_WheelRim", radius * 0.65, width * 0.32,
        rim_loc, rim_mat, rotation=wheel_rot, vertices=12, bevel=0.015
    ))

    # 4. Planetary gear hub with center hex cap
    hub_offset = sign * (width * 0.52)
    hub_loc = (loc_v.x + hub_offset, loc_v.y, loc_v.z)
    objects.append(make_cylinder(
        f"{prefix}_PlanetaryHub", radius * 0.32, 0.045,
        hub_loc, hub_mat, rotation=wheel_rot, vertices=8, bevel=0.008
    ))
    objects.append(make_cylinder(
        f"{prefix}_GreaseCap", radius * 0.14, 0.035,
        (loc_v.x + hub_offset + sign * 0.025, loc_v.y, loc_v.z),
        rim_mat, rotation=wheel_rot, vertices=6, bevel=0.005
    ))

    # 5. Hexagonal lug bolts circle
    bolt_radius = radius * 0.22
    bolt_angle_step = (2.0 * math.pi) / max(1, bolt_count)
    for b in range(bolt_count):
        b_angle = b * bolt_angle_step
        b_y = loc_v.y + math.cos(b_angle) * bolt_radius
        b_z = loc_v.z + math.sin(b_angle) * bolt_radius
        b_loc = (loc_v.x + hub_offset + sign * 0.015, b_y, b_z)
        objects.append(make_cylinder(
            f"{prefix}_LugBolt_{b+1}", 0.015, 0.02,
            b_loc, rim_mat, rotation=wheel_rot, vertices=6, bevel=0.003
        ))

    # 6. Upper suspension wishbone & coilover shock strut connecting to hull
    inboard_x = loc_v.x - sign * (width * 0.65)
    objects.append(make_rod(
        f"{prefix}_SuspensionArm",
        (inboard_x, loc_v.y, loc_v.z + radius * 0.55),
        (loc_v.x, loc_v.y, loc_v.z),
        0.035, hub_mat, vertices=8
    ))
    objects.append(make_cylinder(
        f"{prefix}_ShockCoil",
        0.055, radius * 0.5,
        (inboard_x * 0.7 + loc_v.x * 0.3, loc_v.y, loc_v.z + radius * 0.35),
        accent_mat if accent_mat else hub_mat, rotation=(0.0, sign * math.radians(25), 0.0),
        vertices=8, bevel=0.006
    ))

    return objects


def make_bipedal_digitigrade_leg(
    prefix: str,
    hip_location: Tuple[float, float, float] | Vector,
    side: int = 1,  # -1 for Left, 1 for Right
    total_height: float = 1.60,
    armor_mat: bpy.types.Material | None = None,
    joint_mat: bpy.types.Material | None = None,
    piston_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an articulated digitigrade bipedal walker leg with hydraulic pistons and 3-toed foot."""
    objects = []
    hip_v = Vector(hip_location)
    side_name = "L" if side < 0 else "R"

    hip_z = hip_v.z
    knee_z = hip_z * 0.58
    ankle_z = hip_z * 0.22

    knee_v = Vector((hip_v.x + side * 0.12, hip_v.y - hip_z * 0.14, knee_z))
    ankle_v = Vector((hip_v.x + side * 0.08, hip_v.y + hip_z * 0.10, ankle_z))
    foot_v = Vector((ankle_v.x, ankle_v.y + 0.12, 0.08))

    # 1. Hip rotary actuator & flanged mounting collar boxing joint into pelvis
    objects.append(make_cylinder(
        f"{prefix}_HipJoint_{side_name}", 0.18, 0.18,
        hip_v, joint_mat, rotation=(0.0, math.radians(90), 0.0), vertices=10, bevel=0.015
    ))
    objects.append(make_cylinder(
        f"{prefix}_HipFlange_{side_name}", 0.22, 0.06,
        hip_v - Vector((side * 0.06, 0.0, 0.0)),
        armor_mat or joint_mat, rotation=(0.0, math.radians(90), 0.0), vertices=10, bevel=0.012
    ))

    # 2. Thigh bone & front armor plate
    objects.append(make_rod(
        f"{prefix}_ThighBone_{side_name}", hip_v, knee_v,
        0.085, joint_mat, vertices=8
    ))
    thigh_mid = (hip_v + knee_v) * 0.5
    thigh_dir = (knee_v - hip_v).normalized()
    thigh_rot = thigh_dir.to_track_quat("Z", "Y").to_euler()
    objects.append(make_box(
        f"{prefix}_ThighArmor_{side_name}",
        (0.24, 0.28, (hip_v - knee_v).length * 0.72),
        thigh_mid + Vector((0.0, 0.06, 0.0)), armor_mat, bevel=0.03, rotation=thigh_rot
    ))

    # Hydraulic compression piston behind thigh
    piston_start = hip_v - Vector((0.0, 0.10, 0.05))
    piston_end = knee_v - Vector((0.0, 0.08, -0.05))
    objects.append(make_rod(
        f"{prefix}_ThighCylinder_{side_name}", piston_start, (piston_start + piston_end) * 0.5,
        0.045, joint_mat, vertices=8
    ))
    objects.append(make_rod(
        f"{prefix}_ThighPiston_{side_name}", (piston_start + piston_end) * 0.5, piston_end,
        0.03, piston_mat, vertices=8
    ))

    # 3. Knee hinge joint
    objects.append(make_cylinder(
        f"{prefix}_KneeJoint_{side_name}", 0.14, 0.20,
        knee_v, joint_mat, rotation=(0.0, math.radians(90), 0.0), vertices=10, bevel=0.012
    ))
    # Flexible rubber knee joint dust gaiter
    objects.append(make_cylinder(
        f"{prefix}_KneeGaiter_{side_name}", 0.16, 0.12,
        knee_v - Vector((0.0, 0.04, 0.0)),
        piston_mat or joint_mat, rotation=(0.0, math.radians(90), 0.0), vertices=10, bevel=0.01
    ))
    objects.append(make_box(
        f"{prefix}_KneeCapArmor_{side_name}", (0.22, 0.14, 0.22),
        knee_v + Vector((0.0, 0.10, 0.02)), armor_mat, bevel=0.025
    ))

    # 4. Shin bone & sloped shin armor plate
    objects.append(make_rod(
        f"{prefix}_ShinBone_{side_name}", knee_v, ankle_v,
        0.075, joint_mat, vertices=8
    ))
    shin_mid = (knee_v + ankle_v) * 0.5
    shin_dir = (ankle_v - knee_v).normalized()
    shin_rot = shin_dir.to_track_quat("Z", "Y").to_euler()
    objects.append(make_box(
        f"{prefix}_ShinArmor_{side_name}",
        (0.22, 0.26, (knee_v - ankle_v).length * 0.75),
        shin_mid + Vector((0.0, 0.05, 0.0)), armor_mat, bevel=0.028, rotation=shin_rot
    ))
    # Armored calf deflector plate protecting rear ankle dampers
    objects.append(make_box(
        f"{prefix}_CalfArmor_{side_name}",
        (0.18, 0.16, (knee_v - ankle_v).length * 0.55),
        shin_mid - Vector((0.0, 0.08, 0.0)),
        armor_mat, bevel=0.02, rotation=shin_rot
    ))

    # Twin ankle shock struts
    for strut_offset in (-0.08, 0.08):
        s_start = knee_v + Vector((strut_offset, -0.06, 0.0))
        s_end = ankle_v + Vector((strut_offset, -0.04, 0.04))
        objects.append(make_rod(
            f"{prefix}_AnkleDamper_{side_name}_{'L' if strut_offset < 0 else 'R'}",
            s_start, s_end, 0.022, joint_mat, vertices=6
        ))

    # 5. Articulated 3-toed stabilizer foot resting exactly on Z >= 0.0
    objects.append(make_cylinder(
        f"{prefix}_AnkleJoint_{side_name}", 0.10, 0.16,
        ankle_v, joint_mat, rotation=(0.0, math.radians(90), 0.0), vertices=8
    ))
    # Ankle clevis yoke bracket locking joint to foot
    objects.append(make_box(
        f"{prefix}_AnkleClevis_{side_name}",
        (0.16, 0.18, 0.10),
        ankle_v - Vector((0.0, 0.0, 0.04)),
        joint_mat, bevel=0.012
    ))
    objects.append(make_box(
        f"{prefix}_FootBase_{side_name}", (0.26, 0.24, 0.12),
        (foot_v.x, foot_v.y - 0.08, 0.06), joint_mat, bevel=0.015
    ))
    objects.append(make_box(
        f"{prefix}_ToeCenter_{side_name}", (0.16, 0.28, 0.09),
        (foot_v.x, foot_v.y + 0.14, 0.045), armor_mat, bevel=0.015
    ))
    objects.append(make_box(
        f"{prefix}_ToeOutrigger_{side_name}", (0.13, 0.22, 0.08),
        (foot_v.x + side * 0.14, foot_v.y + 0.08, 0.04), armor_mat, bevel=0.012,
        rotation=(0.0, 0.0, side * math.radians(18))
    ))
    objects.append(make_box(
        f"{prefix}_HeelClaw_{side_name}", (0.18, 0.18, 0.08),
        (foot_v.x, foot_v.y - 0.24, 0.04), joint_mat, bevel=0.012
    ))

    if accent_mat is not None:
        objects.append(make_box(
            f"{prefix}_ShinAccent_{side_name}", (0.08, 0.02, 0.16),
            shin_mid + Vector((0.0, 0.185, 0.0)), accent_mat, bevel=0.005, rotation=shin_rot
        ))

    return objects


# ---------------------------------------------------------------------------
# 5. Advanced Weapon Systems & Turrets
# ---------------------------------------------------------------------------

def make_rotary_gatling_cannon(
    prefix: str,
    pivot: Tuple[float, float, float] | Vector,
    direction: Tuple[float, float, float] | Vector = (0.0, 1.0, 0.0),
    barrel_count: int = 6,
    barrel_length: float = 1.15,
    barrel_radius: float = 0.032,
    cluster_radius: float = 0.10,
    housing_radius: float = 0.18,
    housing_length: float = 0.35,
    gunmetal_mat: bpy.types.Material | None = None,
    housing_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an articulated multi-barrel rotary Gatling cannon assembly."""
    objects = []
    pivot_v = Vector(pivot)
    u_vec, v_vec, dir_vec = get_orthonormal_basis(Vector(direction))
    rot_euler = dir_vec.to_track_quat("Z", "Y").to_euler()

    # 1. Rotary breech rotor / motor housing
    housing_loc = pivot_v + dir_vec * (housing_length * 0.5)
    objects.append(make_cylinder(
        f"{prefix}_Housing", housing_radius, housing_length,
        housing_loc, housing_mat, rotation=rot_euler, vertices=10, bevel=0.02
    ))

    # Ammo feed chute collar / bracket
    chute_loc = pivot_v + dir_vec * (housing_length * 0.35) - u_vec * (housing_radius * 0.85)
    objects.append(make_box(
        f"{prefix}_FeedChute",
        (housing_radius * 0.6, housing_length * 0.5, housing_radius * 0.5),
        chute_loc, housing_mat, bevel=0.015, rotation=rot_euler
    ))

    # 2. Central drive axle shaft
    axle_start = pivot_v + dir_vec * housing_length
    axle_end = pivot_v + dir_vec * (housing_length + barrel_length)
    objects.append(make_rod(
        f"{prefix}_DriveAxle", axle_start, axle_end,
        barrel_radius * 0.85, gunmetal_mat, vertices=8
    ))

    # 3. Circularly arrayed rifled barrels
    for i in range(barrel_count):
        theta = 2.0 * math.pi * i / barrel_count
        radial_vec = (u_vec * math.cos(theta) + v_vec * math.sin(theta)) * cluster_radius
        b_start = axle_start + radial_vec
        b_end = axle_end + radial_vec
        objects.append(make_rod(
            f"{prefix}_Barrel_{i+1}", b_start, b_end,
            barrel_radius, gunmetal_mat, vertices=8, bevel=barrel_radius * 0.15
        ))

    # 4. Retaining clamp rings (Mid-barrel and Muzzle tip)
    for frac, ring_name, use_accent in [(0.45, "MidClamp", False), (0.95, "MuzzleClamp", True)]:
        ring_loc = axle_start + dir_vec * (barrel_length * frac)
        mat = accent_mat if (use_accent and accent_mat is not None) else gunmetal_mat
        objects.append(make_cylinder(
            f"{prefix}_{ring_name}",
            cluster_radius + barrel_radius * 1.35,
            0.045, ring_loc, mat, rotation=rot_euler, vertices=12, bevel=0.008
        ))

    return objects


def make_missile_launcher_pod(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    size: Tuple[float, float, float] = (0.64, 0.92, 0.46),
    rows: int = 2,
    cols: int = 3,
    armor_mat: bpy.types.Material | None = None,
    cavity_mat: bpy.types.Material | None = None,
    warhead_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    elevation_deg: float = 12.0,
    azimuth_deg: float = 0.0,
) -> list[bpy.types.Object]:
    """Generates an armored missile launcher pod with recessed rocket tubes and warheads."""
    objects = []
    loc_v = Vector(location)
    elev_rad = math.radians(elevation_deg)
    azim_rad = math.radians(azimuth_deg)

    dir_vec = Vector((
        math.sin(azim_rad) * math.cos(elev_rad),
        math.cos(azim_rad) * math.cos(elev_rad),
        math.sin(elev_rad)
    )).normalized()
    u_vec, v_vec, forward_dir = get_orthonormal_basis(dir_vec)
    rot_euler = (forward_dir.to_track_quat("Z", "Y")).to_euler()

    # 1. Main armored pod chassis
    objects.append(make_box(
        f"{prefix}_Chassis", size, loc_v, armor_mat, bevel=0.035, rotation=rot_euler
    ))

    # 2. Recessed front launch cell well
    front_face_loc = loc_v + forward_dir * (size[1] * 0.48)
    well_w = size[0] * 0.84
    well_h = size[2] * 0.78
    well_depth = size[1] * 0.18
    objects.append(make_box(
        f"{prefix}_RecessedWell",
        (well_w, well_depth, well_h),
        front_face_loc - forward_dir * (well_depth * 0.4),
        cavity_mat, bevel=0.012, rotation=rot_euler
    ))

    # 3. Grid array of launch tubes + warhead nose cones
    spacing_x = well_w / (cols + 0.5)
    spacing_z = well_h / (rows + 0.5)
    tube_radius = min(spacing_x, spacing_z) * 0.38
    tube_depth = well_depth * 0.85

    start_x = -(cols - 1) * 0.5 * spacing_x
    start_z = -(rows - 1) * 0.5 * spacing_z

    tube_idx = 1
    for r in range(rows):
        for c in range(cols):
            grid_offset = u_vec * (start_x + c * spacing_x) + v_vec * (start_z + r * spacing_z)
            tube_center = front_face_loc - forward_dir * (tube_depth * 0.5) + grid_offset

            # Launch tube sleeve
            objects.append(make_cylinder(
                f"{prefix}_Tube_{tube_idx}",
                tube_radius, tube_depth, tube_center,
                cavity_mat, rotation=rot_euler, vertices=8, bevel=0.005
            ))

            # Conical warhead tip peeking forward
            warhead_loc = tube_center + forward_dir * (tube_depth * 0.42)
            bpy.ops.mesh.primitive_cone_add(
                vertices=8, radius1=tube_radius * 0.78, depth=0.07,
                location=warhead_loc, rotation=rot_euler
            )
            cone_obj = bpy.context.object
            cone_obj.name = f"{prefix}_Warhead_{tube_idx}"
            bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
            objects.append(finish_mesh(cone_obj, warhead_mat, 0.004))

            tube_idx += 1

    # 4. Rear backblast louvers / exhaust vent
    rear_loc = loc_v - forward_dir * (size[1] * 0.48)
    objects.append(make_box(
        f"{prefix}_ExhaustVent",
        (size[0] * 0.75, 0.04, size[2] * 0.65),
        rear_loc, cavity_mat, bevel=0.01, rotation=rot_euler
    ))

    # 5. Accent stripes
    if accent_mat is not None:
        for side in (-1, 1):
            stripe_loc = loc_v + u_vec * (side * (size[0] * 0.51)) + v_vec * (size[2] * 0.15)
            objects.append(make_box(
                f"{prefix}_AccentStripe_{'L' if side < 0 else 'R'}",
                (0.015, size[1] * 0.6, 0.06),
                stripe_loc, accent_mat, bevel=0.005, rotation=rot_euler
            ))

    # 6. Upper blast deflector cowl & umbilical conduit
    cowl_loc = loc_v + forward_dir * (size[1] * 0.44) + v_vec * (size[2] * 0.52)
    objects.append(make_box(
        f"{prefix}_BlastCowl",
        (size[0] * 0.95, size[1] * 0.22, 0.04),
        cowl_loc,
        armor_mat,
        bevel=0.008,
        rotation=rot_euler,
    ))
    conduit_start = loc_v - forward_dir * (size[1] * 0.35) - v_vec * (size[2] * 0.45)
    conduit_end = conduit_start - Vector((0.0, 0.0, 0.18))
    objects.append(make_rod(
        f"{prefix}_UmbilicalConduit",
        conduit_start,
        conduit_end,
        0.028,
        cavity_mat,
        vertices=6,
    ))

    return objects


def make_arc_energy_emitter(
    prefix: str,
    base_location: Tuple[float, float, float] | Vector,
    forward_dir: Tuple[float, float, float] | Vector = (0.0, 1.0, 0.0),
    core_radius: float = 0.20,
    core_length: float = 0.45,
    prong_count: int = 3,
    prong_length: float = 0.50,
    housing_mat: bpy.types.Material | None = None,
    gunmetal_mat: bpy.types.Material | None = None,
    arc_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an electromagnetic arc weapon with faceted dielectric housing and tapered convergent discharge prongs."""
    objects = []
    base_v = Vector(base_location)
    u_vec, v_vec, dir_vec = get_orthonormal_basis(Vector(forward_dir))
    rot_euler = dir_vec.to_track_quat("Z", "Y").to_euler()

    # 1. Armored generator base pedestal
    objects.append(make_box(
        f"{prefix}_Pedestal",
        (core_radius * 2.8, core_length * 0.8, core_radius * 2.2),
        base_v, housing_mat, bevel=0.025, rotation=rot_euler
    ))

    # 2. Glowing plasma core chamber (tapered)
    core_loc = base_v + dir_vec * (core_length * 0.45)
    objects.append(make_tapered_cylinder(
        f"{prefix}_PlasmaCore",
        core_radius * 0.78, core_radius * 0.55, core_length,
        core_loc, arc_mat, rotation=rot_euler, vertices=12, bevel=0.01
    ))

    # 3. Faceted Dielectric Ceramic Insulator Housing (replaces concentric toroid donut rings)
    objects.append(make_box(
        f"{prefix}_DielectricHousing",
        (core_radius * 2.35, core_length * 0.75, core_radius * 2.35),
        core_loc, housing_mat, bevel=0.03, rotation=rot_euler
    ))
    for idx, t in enumerate((0.25, 0.65), start=1):
        slot_loc = base_v + dir_vec * (core_length * t)
        objects.append(make_box(
            f"{prefix}_InsulatorVent_{idx}",
            (core_radius * 2.45, 0.04, core_radius * 2.45),
            slot_loc, gunmetal_mat, bevel=0.006, rotation=rot_euler
        ))

    # 4. Forward convergent arc discharge prongs with organic taper
    focus_point = base_v + dir_vec * (core_length + prong_length * 1.3)
    start_plane = base_v + dir_vec * (core_length * 0.85)

    for i in range(prong_count):
        theta = 2.0 * math.pi * i / prong_count
        prong_radial = (u_vec * math.cos(theta) + v_vec * math.sin(theta)) * (core_radius * 1.15)
        p_base = start_plane + prong_radial
        p_tip = p_base + dir_vec * prong_length + (focus_point - (p_base + dir_vec * prong_length)) * 0.35

        p_dir = (p_tip - p_base).normalized()
        p_dist = (p_tip - p_base).length
        p_rot = p_dir.to_track_quat("Z", "Y").to_euler()

        objects.append(make_tapered_cylinder(
            f"{prefix}_Prong_{i+1}",
            core_radius * 0.18, core_radius * 0.09, p_dist,
            p_base + p_dir * (p_dist * 0.5), gunmetal_mat, rotation=p_rot, vertices=8, bevel=0.005
        ))
        objects.append(make_cylinder(
            f"{prefix}_ProngTip_{i+1}",
            core_radius * 0.12, 0.05, p_tip,
            arc_mat, rotation=rot_euler, vertices=8, bevel=0.004
        ))

    return objects

def make_collimated_beam_laser_assembly(
    prefix: str,
    gun_pivot: Tuple[float, float, float] | Vector,
    direction: Tuple[float, float, float] | Vector = (0.0, 1.0, 0.08),
    barrel_length: float = 2.10,
    caliber_radius: float = 0.08,
    housing_mat: bpy.types.Material | None = None,
    gunmetal_mat: bpy.types.Material | None = None,
    lens_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    ring_count: int = 3,
) -> list[bpy.types.Object]:
    """Generates a heavy collimated beam laser assembly with faceted optical housing, cooling fins, and focus lens."""
    objects = []
    pivot_v = Vector(gun_pivot)
    gun_dir = Vector(direction).normalized()
    u_vec, v_vec, forward_dir = get_orthonormal_basis(gun_dir)
    gun_rot = gun_dir.to_track_quat("Z", "Y").to_euler()

    # 1. Rear power generator housing & accumulator block
    gen_loc = pivot_v - gun_dir * 0.15
    objects.append(make_box(
        f"{prefix}_LaserGenerator",
        (caliber_radius * 4.2, 0.55, caliber_radius * 3.8),
        gen_loc, housing_mat, bevel=0.025, rotation=gun_rot
    ))

    # Cooling radiator fins on generator top/rear
    fin_count = 4
    fin_step = 0.35 / max(1, fin_count - 1)
    for i in range(fin_count):
        fin_loc = gen_loc - gun_dir * (0.15 - i * fin_step) + v_vec * (caliber_radius * 2.1)
        objects.append(make_box(
            f"{prefix}_RadiatorFin_{i+1}",
            (caliber_radius * 3.6, 0.025, caliber_radius * 1.2),
            fin_loc, gunmetal_mat, bevel=0.005, rotation=gun_rot
        ))

    # Dual high-voltage conduit tubes
    for s in (-1, 1):
        c_start = gen_loc + u_vec * (s * caliber_radius * 2.2)
        c_end = pivot_v + gun_dir * 0.45 + u_vec * (s * caliber_radius * 1.3)
        objects.append(make_rod(
            f"{prefix}_PowerConduit_{'L' if s < 0 else 'R'}",
            c_start, c_end, caliber_radius * 0.28, gunmetal_mat, vertices=8
        ))

    # 2. Main collimation barrel core with continuous taper
    barrel_center = pivot_v + gun_dir * (barrel_length * 0.5 + 0.15)
    objects.append(make_tapered_cylinder(
        f"{prefix}_EmitterCore",
        caliber_radius * 1.20, caliber_radius * 0.88, barrel_length,
        barrel_center, gunmetal_mat, rotation=gun_rot, vertices=12, bevel=0.01
    ))

    # 3. Faceted Hexagonal Collimation Shroud with longitudinal heat dissipators (replaces concentric rings)
    shroud_len = barrel_length * 0.75
    shroud_center = pivot_v + gun_dir * (0.30 + shroud_len * 0.5)
    objects.append(make_tapered_cylinder(
        f"{prefix}_HexCollimationShroud",
        caliber_radius * 1.85, caliber_radius * 1.45, shroud_len,
        shroud_center, housing_mat, rotation=gun_rot, vertices=6, bevel=0.018
    ))
    # Longitudinal heat sink rails along the shroud flanks
    for side in (-1, 1):
        s_tag = "L" if side < 0 else "R"
        objects.append(make_box(
            f"{prefix}_HeatSinkRail_{s_tag}",
            (0.025, shroud_len * 0.90, caliber_radius * 0.45),
            shroud_center + u_vec * (side * caliber_radius * 1.55),
            gunmetal_mat, bevel=0.004, rotation=gun_rot
        ))

    # 4. Front emitter muzzle shroud & recessed optical lens
    shroud_loc = pivot_v + gun_dir * (barrel_length + 0.12)
    objects.append(make_tapered_cylinder(
        f"{prefix}_MuzzleShroud",
        caliber_radius * 1.45, caliber_radius * 1.70, 0.24,
        shroud_loc, housing_mat, rotation=gun_rot, vertices=8, bevel=0.015
    ))
    lens_loc = pivot_v + gun_dir * (barrel_length + 0.22)
    objects.append(make_cylinder(
        f"{prefix}_FocusLens", caliber_radius * 0.95, 0.04,
        lens_loc, lens_mat or accent_mat, rotation=gun_rot, vertices=12, bevel=0.005
    ))

    return objects

def make_vls_vertical_missile_array(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    size: Tuple[float, float, float] = (0.95, 1.35, 0.38),
    rows: int = 2,
    cols: int = 4,
    armor_mat: bpy.types.Material | None = None,
    cavity_mat: bpy.types.Material | None = None,
    hatch_mat: bpy.types.Material | None = None,
    missile_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an armored Vertical Launch System (VLS) missile cell matrix."""
    objects = []
    loc_v = Vector(location)

    # 1. Main armored silo deck module
    objects.append(make_box(
        f"{prefix}_SiloDeck", size, loc_v, armor_mat, bevel=0.03
    ))

    # Ventral exhaust gas deflector trough
    exhaust_w = size[0] * 0.90
    exhaust_l = size[1] * 0.12
    objects.append(make_box(
        f"{prefix}_ExhaustVent_Rear",
        (exhaust_w, exhaust_l, 0.06),
        loc_v + Vector((0.0, -size[1] * 0.44, size[2] * 0.5 + 0.015)),
        cavity_mat, bevel=0.008
    ))

    # 2. Silo cell grid with individual split blast hatches and recessed warheads
    cell_w = (size[0] * 0.82) / cols
    cell_l = (size[1] * 0.72) / rows
    cell_size = min(cell_w, cell_l) * 0.85
    start_x = -(cols - 1) * 0.5 * cell_w
    start_y = -(rows - 1) * 0.5 * cell_l + 0.04

    cell_idx = 1
    for r in range(rows):
        for c in range(cols):
            cx = loc_v.x + start_x + c * cell_w
            cy = loc_v.y + start_y + r * cell_l
            cz = loc_v.z + size[2] * 0.5

            objects.append(make_box(
                f"{prefix}_CellCavity_{cell_idx}",
                (cell_size, cell_size, 0.12),
                (cx, cy, cz - 0.04), cavity_mat, bevel=0.005
            ))
            hatch_w = cell_size * 0.46
            objects.append(make_box(
                f"{prefix}_HatchFlap_L_{cell_idx}",
                (hatch_w, cell_size * 0.94, 0.025),
                (cx - hatch_w * 0.52, cy, cz + 0.01), hatch_mat or armor_mat, bevel=0.004
            ))
            objects.append(make_box(
                f"{prefix}_HatchFlap_R_{cell_idx}",
                (hatch_w, cell_size * 0.94, 0.025),
                (cx + hatch_w * 0.52, cy, cz + 0.01), hatch_mat or armor_mat, bevel=0.004
            ))
            objects.append(make_cylinder(
                f"{prefix}_MissileTip_{cell_idx}",
                cell_size * 0.28, 0.06,
                (cx, cy, cz - 0.02), missile_mat or accent_mat, vertices=8, bevel=0.006
            ))
            cell_idx += 1

    return objects


def make_mantlet_dust_boot(
    prefix: str,
    pivot: Tuple[float, float, float] | Vector,
    direction: Tuple[float, float, float] | Vector = (0.0, 1.0, 0.0),
    collar_radius: float = 0.17,
    taper_radius: float = 0.11,
    length: float = 0.22,
    stages: int = 3,
    material: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an authentic continuous pleated fabric/rubber accordion dust bellows at the mantlet interface."""
    objects = []
    pivot_v = Vector(pivot)
    dir_v = Vector(direction).normalized()
    rot_euler = dir_v.to_track_quat("Z", "Y").to_euler()

    # Flanged base collar clamping to the turret mantlet face
    objects.append(make_cylinder(
        f"{prefix}_BaseClamp",
        collar_radius * 1.15, 0.035,
        pivot_v + dir_v * 0.02,
        accent_mat or material, rotation=rot_euler, vertices=12, bevel=0.006
    ))

    # Continuous pleated bellows stages using tapered conical pleats (no floating toroid donuts)
    stage_len = length / max(1, stages)
    for i in range(stages):
        t0 = i / stages
        t1 = (i + 1) / stages
        r0 = collar_radius * (1.0 - t0) + taper_radius * t0
        r1 = collar_radius * (1.0 - t1) + taper_radius * t1
        r_mid = max(r0, r1) * 1.12
        half_len = stage_len * 0.5
        c_loc1 = pivot_v + dir_v * (i * stage_len + half_len * 0.5)
        c_loc2 = pivot_v + dir_v * (i * stage_len + half_len * 1.5)

        objects.append(make_tapered_cylinder(
            f"{prefix}_PleatConeA_{i+1}",
            r0, r_mid, half_len,
            c_loc1, material, rotation=rot_euler, vertices=12, bevel=0.004
        ))
        objects.append(make_tapered_cylinder(
            f"{prefix}_PleatConeB_{i+1}",
            r_mid, r1, half_len,
            c_loc2, material, rotation=rot_euler, vertices=12, bevel=0.004
        ))

    return objects

def make_coaxial_cannon_assembly(
    prefix: str,
    gun_pivot: Tuple[float, float, float] | Vector,
    direction: Tuple[float, float, float] | Vector = (0.0, 1.0, 0.12),
    caliber_radius: float = 0.095,
    barrel_length: float = 1.65,
    armor_mat: bpy.types.Material | None = None,
    barrel_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    has_thermal_sleeve: bool = True,
    has_bore_evacuator: bool = True,
    has_muzzle_brake: bool = True,
    has_recoil_cylinders: bool = True,
    has_dust_boot: bool = False,
    dust_boot_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an authentic tapered tank gun barrel with faceted thermal sleeve and aerodynamic fume extractor."""
    objects = []
    pivot_v = Vector(gun_pivot)
    gun_dir = Vector(direction).normalized()
    gun_rot = gun_dir.to_track_quat("Z", "Y").to_euler()

    # 0. Pleated conical accordion dust boot at mantlet junction
    if has_dust_boot:
        boot_objs = make_mantlet_dust_boot(
            f"{prefix}_DustBoot",
            pivot=pivot_v - gun_dir * 0.04,
            direction=gun_dir,
            collar_radius=caliber_radius * 2.2,
            taper_radius=caliber_radius * 1.4,
            length=0.22,
            stages=3,
            material=dust_boot_mat or barrel_mat,
            accent_mat=accent_mat,
        )
        objects.extend(boot_objs)

    # 1. Reinforced breech collar with tapered transition
    breech_loc = pivot_v + gun_dir * 0.22
    objects.append(make_tapered_cylinder(
        f"{prefix}_BreechCollar",
        caliber_radius * 1.95, caliber_radius * 1.65, 0.42,
        breech_loc, barrel_mat, rotation=gun_rot, vertices=12, bevel=0.02
    ))

    # 2. Faceted octagonal thermal sleeve / heat shroud with clamping flanges
    if has_thermal_sleeve:
        sleeve_loc = pivot_v + gun_dir * 0.65
        objects.append(make_tapered_cylinder(
            f"{prefix}_ThermalSleeve",
            caliber_radius * 1.48, caliber_radius * 1.32, 0.52,
            sleeve_loc, armor_mat, rotation=gun_rot, vertices=8, bevel=0.015
        ))
        # Longitudinal clamping spine rib
        u_up = gun_dir.cross(Vector((1.0, 0.0, 0.0))).normalized()
        objects.append(make_box(
            f"{prefix}_SleeveClampSpine",
            (0.025, 0.48, caliber_radius * 0.22),
            sleeve_loc + u_up * (caliber_radius * 1.38),
            barrel_mat, bevel=0.005, rotation=gun_rot
        ))

    # 3. Aerodynamic double-tapered bore evacuator / fume extractor (no redundant donut rings)
    if has_bore_evacuator:
        evac_dist = barrel_length * 0.62
        evac_len = 0.36
        evac_center = pivot_v + gun_dir * evac_dist
        evac_r = caliber_radius * 1.52
        b_r_at_evac = caliber_radius * (1.25 - 0.33 * (evac_dist / barrel_length))

        # Rear entry taper cone
        objects.append(make_tapered_cylinder(
            f"{prefix}_BoreEvacuator_ConeRear",
            b_r_at_evac * 1.08, evac_r, evac_len * 0.25,
            evac_center - gun_dir * (evac_len * 0.375),
            armor_mat, rotation=gun_rot, vertices=12, bevel=0.008
        ))
        # Central pressure body
        objects.append(make_cylinder(
            f"{prefix}_BoreEvacuator",
            evac_r, evac_len * 0.50,
            evac_center,
            armor_mat, rotation=gun_rot, vertices=12, bevel=0.012
        ))
        # Forward exit taper cone
        objects.append(make_tapered_cylinder(
            f"{prefix}_BoreEvacuator_ConeFront",
            evac_r, b_r_at_evac * 0.98, evac_len * 0.25,
            evac_center + gun_dir * (evac_len * 0.375),
            armor_mat, rotation=gun_rot, vertices=12, bevel=0.008
        ))

    # 4. Main continuous tapered rifled barrel (narrowing from breech to muzzle)
    barrel_center = pivot_v + gun_dir * (barrel_length * 0.5 + 0.3)
    objects.append(make_tapered_cylinder(
        f"{prefix}_MainBarrel",
        caliber_radius * 1.25, caliber_radius * 0.92, barrel_length,
        barrel_center, barrel_mat, rotation=gun_rot, vertices=14, bevel=0.008
    ))

    # 5. Ported multi-slot muzzle brake & bore opening
    if has_muzzle_brake:
        brake_loc = pivot_v + gun_dir * (barrel_length + 0.22)
        objects.append(make_box(
            f"{prefix}_MuzzleBrake",
            (caliber_radius * 2.5, caliber_radius * 3.2, caliber_radius * 2.3),
            brake_loc, armor_mat, bevel=0.018, rotation=gun_rot
        ))
        # Lateral gas evacuation baffle ports
        u_lat = gun_dir.cross(Vector((0.0, 0.0, 1.0))).normalized()
        for side in (-1, 1):
            s_tag = "L" if side < 0 else "R"
            objects.append(make_box(
                f"{prefix}_MuzzlePort_{s_tag}",
                (caliber_radius * 0.35, caliber_radius * 1.8, caliber_radius * 1.2),
                brake_loc + u_lat * (side * caliber_radius * 1.15),
                barrel_mat, bevel=0.005, rotation=gun_rot
            ))
        bore_loc = pivot_v + gun_dir * (barrel_length + 0.36)
        objects.append(make_cylinder(
            f"{prefix}_MuzzleBore", caliber_radius * 0.68, 0.10,
            bore_loc, barrel_mat, rotation=gun_rot, vertices=12
        ))

    # 6. Twin hydraulic recoil recuperators
    if has_recoil_cylinders:
        recoil_offset_x = caliber_radius * 2.3
        u_vec = gun_dir.cross(Vector((0.0, 0.0, 1.0))).normalized()
        for side in (-1, 1):
            side_tag = "L" if side < 0 else "R"
            recoil_loc = pivot_v + gun_dir * 0.28 + u_vec * (side * recoil_offset_x)
            objects.append(make_tapered_cylinder(
                f"{prefix}_RecoilCylinder_{side_tag}",
                caliber_radius * 0.58, caliber_radius * 0.48, 0.50,
                recoil_loc, barrel_mat, rotation=gun_rot, vertices=10, bevel=0.008
            ))

    return objects

# ---------------------------------------------------------------------------
# 6. Micro-Greebles & Optical Sensors
# ---------------------------------------------------------------------------

def make_engine_deck_grille(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    size: Tuple[float, float, float],
    slat_count: int,
    slat_mat: bpy.types.Material | None,
    frame_mat: bpy.types.Material | None,
) -> list[bpy.types.Object]:
    """Creates an armored engine intake deck with angled ventilation louvers."""
    objects = []
    loc_v = Vector(location)
    objects.append(make_box(f"{prefix}_Frame", size, loc_v, frame_mat, bevel=0.018))

    slat_spacing = (size[1] * 0.82) / slat_count
    slat_width = size[0] * 0.84
    slat_thickness = size[2] * 0.55
    start_y = loc_v.y - (size[1] * 0.41) + (slat_spacing * 0.5)

    for i in range(slat_count):
        y = start_y + i * slat_spacing
        objects.append(make_box(
            f"{prefix}_Slat_{i+1}",
            (slat_width, slat_spacing * 0.65, slat_thickness),
            (loc_v.x, y, loc_v.z + size[2] * 0.35),
            slat_mat, bevel=0.006,
            rotation=(math.radians(-24), 0.0, 0.0),
        ))
    return objects


def make_recessed_sensor_visor(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    size: Tuple[float, float, float],
    armor_mat: bpy.types.Material | None,
    lens_mat: bpy.types.Material | None,
    cavity_mat: bpy.types.Material | None,
) -> list[bpy.types.Object]:
    """Creates a protective armored visor housing with recessed glowing optical slits."""
    objects = []
    loc_v = Vector(location)
    objects.append(make_box(
        f"{prefix}_Housing", size, loc_v, armor_mat, bevel=0.025
    ))
    well_size = (size[0] * 0.80, size[1] * 0.35, size[2] * 0.58)
    well_loc = (loc_v.x, loc_v.y + size[1] * 0.35, loc_v.z)
    objects.append(make_box(
        f"{prefix}_Well", well_size, well_loc, cavity_mat, bevel=0.008
    ))
    lens_w = well_size[0] * 0.36
    for idx, side in enumerate((-1, 1), start=1):
        lens_x = loc_v.x + side * (lens_w * 0.68)
        objects.append(make_box(
            f"{prefix}_Lens_{idx}",
            (lens_w, 0.025, well_size[2] * 0.72),
            (lens_x, loc_v.y + size[1] * 0.48, loc_v.z),
            lens_mat, bevel=0.004
        ))
    return objects


def make_armor_applique_array(
    prefix: str,
    start: Tuple[float, float, float] | Vector,
    end: Tuple[float, float, float] | Vector,
    count: int = 4,
    size: Tuple[float, float, float] = (0.18, 0.22, 0.05),
    armor_mat: bpy.types.Material | None = None,
    standoff_mat: bpy.types.Material | None = None,
    rotation: Tuple[float, float, float] = (0.0, 0.0, 0.0),
) -> list[bpy.types.Object]:
    """Generates an array of Explosive Reactive Armor (ERA) tiles or spaced modular plates."""
    objects = []
    start_v = Vector(start)
    end_v = Vector(end)
    spacing_step = (end_v - start_v) / (count - 1) if count > 1 else Vector((0.0, 0.0, 0.0))

    for i in range(count):
        tile_loc = start_v + spacing_step * i
        objects.append(make_box(
            f"{prefix}_Tile_{i+1}", size, tile_loc, armor_mat,
            bevel=0.015, rotation=rotation
        ))
        if standoff_mat is not None:
            for sx in (-1, 1):
                for sy in (-1, 1):
                    bolt_loc = tile_loc + Vector((
                        sx * size[0] * 0.38,
                        sy * size[1] * 0.38,
                        -size[2] * 0.45
                    ))
                    objects.append(make_cylinder(
                        f"{prefix}_Standoff_{i+1}_{sx}_{sy}",
                        0.015, size[2] * 0.8, bolt_loc, standoff_mat,
                        vertices=6, bevel=0.003
                    ))

    return objects


def make_hex_bolt_array(
    prefix: str,
    points: Sequence[Tuple[float, float, float] | Vector],
    radius: float = 0.02,
    depth: float = 0.015,
    material: bpy.types.Material | None = None,
    normal_dir: Tuple[float, float, float] | Vector = (0.0, 0.0, 1.0),
) -> list[bpy.types.Object]:
    """Places low-poly 6-sided hexagonal bolt heads at specified coordinates."""
    objects = []
    norm_v = Vector(normal_dir).normalized()
    rot_euler = norm_v.to_track_quat("Z", "Y").to_euler()

    for idx, pt in enumerate(points, start=1):
        pt_v = Vector(pt)
        objects.append(make_cylinder(
            f"{prefix}_Bolt_{idx}", radius, depth,
            pt_v + norm_v * (depth * 0.5), material,
            rotation=rot_euler, vertices=6, bevel=0.003
        ))
    return objects


def make_gimbal_sensor_turret(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    radius: float = 0.18,
    look_dir: Tuple[float, float, float] | Vector = (0.0, 1.0, 0.0),
    armor_mat: bpy.types.Material | None = None,
    lens_mat: bpy.types.Material | None = None,
    gunmetal_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a 2-axis gimballed FLIR sensor ball with dual optic apertures and mounting yoke."""
    objects = []
    loc_v = Vector(location)
    dir_v = Vector(look_dir).normalized()
    u_vec, v_vec, forward_v = get_orthonormal_basis(dir_v)
    rot_euler = forward_v.to_track_quat("Z", "Y").to_euler()

    # Base pedestal
    objects.append(make_cylinder(
        f"{prefix}_AzimuthBase", radius * 0.85, 0.06,
        loc_v - Vector((0.0, 0.0, radius * 0.95)),
        gunmetal_mat, vertices=10, bevel=0.008
    ))
    # U-shaped mounting yoke arms
    for side in (-1, 1):
        arm_loc = loc_v + u_vec * (side * radius * 1.05) - Vector((0.0, 0.0, radius * 0.45))
        objects.append(make_box(
            f"{prefix}_YokeArm_{'L' if side < 0 else 'R'}",
            (0.05, radius * 0.7, radius * 0.9),
            arm_loc, armor_mat, bevel=0.012, rotation=rot_euler
        ))
    # Spherical sensor ball
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=12, ring_count=8, radius=radius, location=loc_v
    )
    ball_obj = bpy.context.object
    ball_obj.name = f"{prefix}_Ball"
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    objects.append(finish_mesh(ball_obj, armor_mat, 0.008))

    # Thermal aperture
    lens1_loc = loc_v + forward_v * (radius * 0.88) - u_vec * (radius * 0.28)
    objects.append(make_cylinder(
        f"{prefix}_ThermalLens", radius * 0.38, 0.04,
        lens1_loc, lens_mat, rotation=rot_euler, vertices=10, bevel=0.004
    ))
    objects.append(make_cylinder(
        f"{prefix}_ThermalBezel", radius * 0.45, 0.02,
        lens1_loc - forward_v * 0.01, gunmetal_mat, rotation=rot_euler, vertices=10
    ))
    # Laser designator slit
    lens2_loc = loc_v + forward_v * (radius * 0.90) + u_vec * (radius * 0.32)
    objects.append(make_box(
        f"{prefix}_DesignatorOptic",
        (radius * 0.30, 0.03, radius * 0.22),
        lens2_loc, lens_mat, bevel=0.004, rotation=rot_euler
    ))
    return objects


def make_smoke_launcher_rack(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    tube_count: int = 3,
    direction: Tuple[float, float, float] | Vector = (0.5, 0.86, 0.38),
    armor_mat: bpy.types.Material | None = None,
    tube_mat: bpy.types.Material | None = None,
    cap_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an angled multi-tube defensive smoke grenade discharger bank."""
    objects = []
    loc_v = Vector(location)
    dir_v = Vector(direction).normalized()
    u_vec, v_vec, forward_v = get_orthonormal_basis(dir_v)
    rot_euler = forward_v.to_track_quat("Z", "Y").to_euler()

    spacing = 0.075
    base_w = tube_count * spacing + 0.04
    base_h = 0.08
    base_d = 0.04
    objects.append(make_box(
        f"{prefix}_MountBase", (base_w, base_d, base_h), loc_v, armor_mat,
        bevel=0.006, rotation=rot_euler
    ))

    start_u = -(tube_count - 1) * 0.5 * spacing
    for i in range(tube_count):
        offset = start_u + i * spacing
        tube_center = loc_v + u_vec * offset + forward_v * 0.07 + v_vec * 0.02
        objects.append(make_cylinder(
            f"{prefix}_Tube_{i+1}", 0.026, 0.13, tube_center, tube_mat,
            rotation=rot_euler, vertices=8, bevel=0.004
        ))
        cap_center = tube_center + forward_v * 0.07
        objects.append(make_cylinder(
            f"{prefix}_Cap_{i+1}", 0.022, 0.02, cap_center, cap_mat,
            rotation=rot_euler, vertices=8
        ))
    return objects


def make_radar_dish_or_rotodome(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    dish_radius: float = 0.42,
    tilt_deg: float = 18.0,
    is_rotodome: bool = False,
    armor_mat: bpy.types.Material | None = None,
    antenna_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a command/recon radar antenna: parabolic dish or elliptical rotodome."""
    objects = []
    loc_v = Vector(location)
    objects.append(make_cylinder(
        f"{prefix}_TurntableBase", 0.16, 0.08, loc_v + Vector((0.0, 0.0, 0.04)),
        antenna_mat, vertices=10, bevel=0.008
    ))
    objects.append(make_cylinder(
        f"{prefix}_PedestalMast", 0.065, 0.22, loc_v + Vector((0.0, 0.0, 0.18)),
        antenna_mat, vertices=8, bevel=0.005
    ))

    dish_center = loc_v + Vector((0.0, 0.0, 0.32))
    tilt_rad = math.radians(tilt_deg)
    rot_euler = (tilt_rad, 0.0, 0.0)

    if is_rotodome:
        objects.append(make_cylinder(
            f"{prefix}_RotodomeBody", dish_radius, 0.12, dish_center, armor_mat,
            rotation=rot_euler, vertices=12, bevel=0.02
        ))
        objects.append(make_cylinder(
            f"{prefix}_RadomeCap", dish_radius * 0.45, 0.16, dish_center, accent_mat,
            rotation=rot_euler, vertices=10, bevel=0.01
        ))
    else:
        objects.append(make_cylinder(
            f"{prefix}_DishBacking", dish_radius, 0.04, dish_center, armor_mat,
            rotation=rot_euler, vertices=12, bevel=0.008
        ))
        horn_pos = dish_center + Vector((0.0, math.cos(tilt_rad) * 0.24, math.sin(tilt_rad) * 0.24))
        objects.append(make_rod(
            f"{prefix}_FeedHornStrut", dish_center, horn_pos, 0.015,
            antenna_mat, vertices=6
        ))
        objects.append(make_cylinder(
            f"{prefix}_FeedTransceiver", 0.045, 0.06, horn_pos, accent_mat,
            rotation=rot_euler, vertices=8
        ))

    return objects


def make_quad_crawler_leg(
    prefix: str,
    hip_location: Tuple[float, float, float] | Vector,
    side_x: int = 1,
    side_y: int = 1,
    reach_x: float = 0.95,
    reach_y: float = 0.75,
    knee_height: float = 0.95,
    foot_radius: float = 0.22,
    armor_mat: bpy.types.Material | None = None,
    joint_mat: bpy.types.Material | None = None,
    damper_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an articulated heavy spider/crawler leg resting securely on Z >= 0.0m."""
    objects = []
    hip_v = Vector(hip_location)
    foot_target = Vector((hip_v.x + side_x * reach_x, hip_v.y + side_y * reach_y, 0.0))
    knee_v = Vector((
        hip_v.x + side_x * (reach_x * 0.65),
        hip_v.y + side_y * (reach_y * 0.60),
        knee_height
    ))

    objects.append(make_cylinder(
        f"{prefix}_HipActuator", 0.18, 0.22, hip_v, joint_mat,
        rotation=(0.0, math.radians(90), 0.0), vertices=10, bevel=0.012
    ))
    objects.append(make_rod(
        f"{prefix}_FemurBoom", hip_v, knee_v, 0.085, armor_mat, vertices=8
    ))
    damper_start = hip_v - Vector((0.0, 0.0, 0.10))
    damper_mid = (damper_start + knee_v) * 0.5
    objects.append(make_rod(
        f"{prefix}_FemurRam", damper_start, damper_mid, 0.045, damper_mat, vertices=6
    ))
    objects.append(make_cylinder(
        f"{prefix}_KneeKnuckle", 0.15, 0.20, knee_v, joint_mat,
        rotation=(math.radians(90), 0.0, 0.0), vertices=10, bevel=0.01
    ))
    # Ballistic chevron knee-cap deflector shield
    objects.append(make_box(
        f"{prefix}_KneeCapShield",
        (0.24, 0.12, 0.22),
        knee_v + Vector((0.0, side_y * 0.10, 0.04)),
        armor_mat,
        bevel=0.02,
        rotation=(math.radians(-side_y * 18), 0.0, 0.0),
    ))
    objects.append(make_rod(
        f"{prefix}_TibiaStrut", knee_v, foot_target + Vector((0.0, 0.0, 0.10)),
        0.075, joint_mat, vertices=8
    ))
    # Parallel ankle hydraulic shock damper
    tibia_mid = (knee_v + foot_target) * 0.5
    damper_offset = Vector((side_x * 0.08, -side_y * 0.06, 0.05))
    objects.append(make_rod(
        f"{prefix}_AnkleHydraulicShock",
        knee_v + damper_offset,
        tibia_mid + damper_offset,
        0.032,
        joint_mat,
        vertices=6,
    ))
    objects.append(make_cylinder(
        f"{prefix}_FootPad", foot_radius, 0.08, foot_target + Vector((0.0, 0.0, 0.04)),
        armor_mat, vertices=10, bevel=0.015
    ))
    objects.append(make_cylinder(
        f"{prefix}_FootTread", foot_radius * 1.08, 0.03, foot_target + Vector((0.0, 0.0, 0.015)),
        damper_mat, vertices=10
    ))
    objects.append(make_cylinder(
        f"{prefix}_AnkleSleeve", 0.10, 0.16, foot_target + Vector((0.0, 0.0, 0.14)),
        accent_mat, vertices=8, bevel=0.008
    ))
    return objects


def make_hover_skimmer_assembly(
    prefix: str,
    hull_length: float = 3.0,
    hull_width: float = 2.4,
    float_height: float = 0.55,
    skirt_mat: bpy.types.Material | None = None,
    thruster_mat: bpy.types.Material | None = None,
    core_mat: bpy.types.Material | None = None,
    glow_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an anti-grav repulsor cushion, ventral thruster bells, and stabilizer strakes."""
    objects = []
    objects.append(make_box(
        f"{prefix}_RepulsorSkirt",
        (hull_width * 0.92, hull_length * 0.88, 0.22),
        (0.0, 0.0, float_height),
        skirt_mat, bevel=0.04
    ))
    # Segmented rubber plenum pleats at bow and stern
    objects.append(make_box(
        f"{prefix}_PlenumPleat_F",
        (hull_width * 0.86, 0.22, 0.16),
        (0.0, hull_length * 0.42, float_height - 0.02),
        skirt_mat, bevel=0.02, rotation=(math.radians(-22), 0.0, 0.0)
    ))
    objects.append(make_box(
        f"{prefix}_PlenumPleat_R",
        (hull_width * 0.86, 0.22, 0.16),
        (0.0, -hull_length * 0.42, float_height - 0.02),
        skirt_mat, bevel=0.02, rotation=(math.radians(22), 0.0, 0.0)
    ))
    # Flexible perimeter bumper welt collar
    objects.append(make_box(
        f"{prefix}_PerimeterBumper",
        (hull_width * 0.94, hull_length * 0.90, 0.06),
        (0.0, 0.0, float_height + 0.10),
        thruster_mat, bevel=0.015
    ))
    bell_rx = hull_width * 0.32
    bell_ry = hull_length * 0.28
    for sx in (-1, 1):
        for sy in (-1, 1):
            tag = f"{'L' if sx < 0 else 'R'}_{'F' if sy > 0 else 'R'}"
            bell_loc = Vector((sx * bell_rx, sy * bell_ry, float_height - 0.04))
            objects.append(make_cylinder(
                f"{prefix}_ThrusterBell_{tag}", 0.24, 0.12, bell_loc,
                thruster_mat, vertices=10, bevel=0.015
            ))
            objects.append(make_cylinder(
                f"{prefix}_ThrusterCore_{tag}", 0.18, 0.03, bell_loc - Vector((0.0, 0.0, 0.05)),
                glow_mat, vertices=10
            ))

    for side in (-1, 1):
        side_tag = "L" if side < 0 else "R"
        objects.append(make_box(
            f"{prefix}_StabilizerStrake_{side_tag}",
            (0.18, hull_length * 0.65, 0.06),
            (side * (hull_width * 0.52), -hull_length * 0.05, float_height + 0.08),
            core_mat, bevel=0.012, rotation=(0.0, math.radians(side * -14), 0.0)
        ))

    for side in (-1, 1):
        side_tag = "L" if side < 0 else "R"
        ex_loc = Vector((side * (hull_width * 0.26), -hull_length * 0.48, float_height + 0.12))
        objects.append(make_box(
            f"{prefix}_VectoringExhaust_{side_tag}",
            (0.28, 0.35, 0.18), ex_loc, core_mat, bevel=0.018
        ))
        objects.append(make_box(
            f"{prefix}_VectoringNozzle_{side_tag}",
            (0.20, 0.04, 0.12), ex_loc - Vector((0.0, 0.16, 0.0)), glow_mat
        ))
    return objects


def make_side_sponson_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    side: int = 1,
    size: Tuple[float, float, float] = (0.28, 0.85, 0.42),
    armor_mat: bpy.types.Material | None = None,
    strap_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an armored external sponson, ammo pannier, or auxiliary fuel cell."""
    objects = []
    loc_v = Vector(location)
    objects.append(make_box(
        f"{prefix}_PannierBody", size, loc_v, armor_mat, bevel=0.02
    ))
    for sy in (-1, 1):
        strap_y = loc_v.y + sy * (size[1] * 0.34)
        objects.append(make_box(
            f"{prefix}_Strap_{'F' if sy > 0 else 'R'}",
            (size[0] * 1.05, 0.05, size[2] * 1.05),
            (loc_v.x, strap_y, loc_v.z),
            strap_mat, bevel=0.005
        ))
    objects.append(make_cylinder(
        f"{prefix}_FuelCap", 0.04, 0.03,
        loc_v + Vector((0.0, 0.0, size[2] * 0.51)),
        accent_mat, vertices=8
    ))
    return objects


def make_whip_antenna(
    name: str,
    base_pos: Tuple[float, float, float] | Vector,
    length: float = 0.85,
    lean_deg: float = 7.0,
    base_mat: bpy.types.Material | None = None,
    mast_mat: bpy.types.Material | None = None,
    tip_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a tactical whip antenna with stepped helical spring coils and beacon tip."""
    objects = []
    base_v = Vector(base_pos)
    objects.append(make_cylinder(f"{name}_Bracket", 0.045, 0.06, base_v, base_mat, vertices=8, bevel=0.008))
    for i in range(3):
        coil_z = base_v.z + 0.03 + i * 0.025
        objects.append(make_cylinder(f"{name}_SpringCoil_{i}", 0.032 - i * 0.003, 0.02, Vector((base_v.x, base_v.y, coil_z)), base_mat, vertices=8))
    lean_rad = math.radians(lean_deg)
    mast_start = base_v + Vector((0.0, 0.0, 0.10))
    mast_end = mast_start + Vector((0.0, -math.sin(lean_rad) * length, math.cos(lean_rad) * length))
    objects.append(make_rod(f"{name}_Mast", mast_start, mast_end, 0.010, mast_mat, vertices=6))
    objects.append(make_cylinder(f"{name}_Tip", 0.018, 0.035, mast_end, tip_mat, vertices=8))
    return objects


def make_crew_cupola_hatch(
    name: str,
    location: Tuple[float, float, float] | Vector,
    radius: float = 0.30,
    height: float = 0.18,
    cupola_mat: bpy.types.Material | None = None,
    lens_mat: bpy.types.Material | None = None,
    metal_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a commander's periscope cupola and armored split hatch."""
    objects = []
    loc_v = Vector(location)
    objects.append(make_cylinder(f"{name}_Ring", radius, height, loc_v, cupola_mat, vertices=12, bevel=0.015))
    slit_count = 6
    for i in range(slit_count):
        angle = (2.0 * math.pi / slit_count) * i
        sx = loc_v.x + math.cos(angle) * (radius * 0.90)
        sy = loc_v.y + math.sin(angle) * (radius * 0.90)
        sz = loc_v.z + height * 0.15
        objects.append(make_box(f"{name}_Periscope_{i}", (0.06, 0.08, 0.05), (sx, sy, sz), lens_mat,
                                rotation=(0.0, 0.0, angle)))
    hatch_z = loc_v.z + height * 0.52
    objects.append(make_cylinder(f"{name}_HatchPlate", radius * 0.88, 0.04, Vector((loc_v.x, loc_v.y, hatch_z)), cupola_mat, vertices=12, bevel=0.008))
    objects.append(make_box(f"{name}_Hinge", (0.08, 0.06, 0.04), (loc_v.x, loc_v.y - radius * 0.75, hatch_z + 0.02), metal_mat, bevel=0.006))
    return objects


def make_rivets_and_weld_seams(
    name: str,
    start_point: Tuple[float, float, float] | Vector,
    end_point: Tuple[float, float, float] | Vector,
    count: int = 5,
    rivet_radius: float = 0.016,
    metal_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a row of hex/dome rivets and continuous beveled weld seam strip."""
    objects = []
    p1 = Vector(start_point)
    p2 = Vector(end_point)
    diff = p2 - p1
    length = diff.length
    if length > 0.05:
        objects.append(make_rod(f"{name}_WeldSeam", p1, p2, rivet_radius * 0.65, metal_mat, vertices=6))
        for i in range(count):
            t = (i + 0.5) / float(count)
            pos = p1 + diff * t
            objects.append(make_cylinder(f"{name}_Rivet_{i}", rivet_radius, 0.015, pos, metal_mat, vertices=6, bevel=0.004))
    return objects


def make_articulated_track_shoes(
    name: str,
    center: Tuple[float, float, float] | Vector,
    length: float = 3.0,
    width: float = 0.36,
    ground_z: float = 0.08,
    shoe_count: int = 8,
    shoe_mat: bpy.types.Material | None = None,
    pin_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates discrete geometric track shoe pads and guide pins along the ground contact run."""
    objects = []
    c = Vector(center)
    span = length * 0.75
    spacing = span / max(1, shoe_count - 1)
    start_y = c.y - span * 0.5
    for i in range(shoe_count):
        sy = start_y + i * spacing
        objects.append(make_box(f"{name}_Shoe_{i}", (width * 0.95, spacing * 0.78, 0.035), (c.x, sy, ground_z - 0.015), shoe_mat, bevel=0.006))
        objects.append(make_cylinder(f"{name}_GuideHorn_{i}", 0.025, 0.05, Vector((c.x, sy, ground_z + 0.03)), pin_mat, vertices=6))
    return objects


def make_catenary_cable_assembly(
    prefix: str,
    start_point: Tuple[float, float, float] | Vector,
    end_point: Tuple[float, float, float] | Vector,
    sag: float = 0.14,
    radius: float = 0.024,
    segments: int = 10,
    sides: int = 8,
    clamp_count: int = 2,
    hose_mat: bpy.types.Material | None = None,
    coupler_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    corrugated: bool = True,
) -> list[bpy.types.Object]:
    """Generates a catenary-sagging hydraulic hose / power conduit with BMesh sweep, end couplers, and retaining collars."""
    objects: list[bpy.types.Object] = []
    p1 = Vector(start_point)
    p2 = Vector(end_point)
    span = (p2 - p1).length
    if span < 0.05:
        return objects

    num_segs = max(4, int(segments))
    n_sides = max(6, int(sides))

    # Evaluate catenary points along curve with hyperbolic cosine / parabolic sag profile
    points: list[Vector] = []
    for i in range(num_segs + 1):
        t = i / float(num_segs)
        pt = p1.lerp(p2, t)
        # Hyperbolic cosine catenary approximation normalized to [0, 1]
        u = 2.0 * t - 1.0
        cosh_drop = (math.cosh(1.5) - math.cosh(1.5 * u)) / (math.cosh(1.5) - 1.0)
        pt.z -= sag * cosh_drop
        points.append(pt)

    # Construct continuous BMesh tube along the catenary curve
    mesh = bpy.data.meshes.new(f"{prefix}_Hose_Mesh")
    hose_obj = bpy.data.objects.new(f"{prefix}_Hose", mesh)
    bpy.context.collection.objects.link(hose_obj)
    bm = bmesh.new()

    rings: list[list[bmesh.types.BMVert]] = []
    for i, pt in enumerate(points):
        if i == 0:
            tangent = (points[1] - points[0]).normalized()
        elif i == len(points) - 1:
            tangent = (points[-1] - points[-2]).normalized()
        else:
            tangent = (points[i + 1] - points[i - 1]).normalized()

        u_vec, v_vec, _ = get_orthonormal_basis(tangent)
        ring_verts = []
        # Subtle corrugated rib radius modulation along hose length
        rib_scale = 1.0 + (0.08 if (corrugated and i % 2 == 1 and 0 < i < num_segs) else 0.0)
        r_eff = radius * rib_scale
        for s in range(n_sides):
            theta = (2.0 * math.pi * s) / n_sides
            offset = (u_vec * math.cos(theta) + v_vec * math.sin(theta)) * r_eff
            ring_verts.append(bm.verts.new(pt + offset))
        rings.append(ring_verts)

    for i in range(len(rings) - 1):
        r_curr = rings[i]
        r_next = rings[i + 1]
        for s in range(n_sides):
            s_next = (s + 1) % n_sides
            bm.faces.new((r_curr[s], r_curr[s_next], r_next[s_next], r_next[s]))

    # Cap ends
    bm.faces.new(reversed(rings[0]))
    bm.faces.new(rings[-1])
    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()

    finish_mesh(hose_obj, hose_mat, bevel=0.0, smooth=True)
    objects.append(hose_obj)

    # Hexagonal end bulkhead couplers
    c_mat = coupler_mat or hose_mat
    for idx, (end_pt, adj_pt) in enumerate(((points[0], points[1]), (points[-1], points[-2])), start=1):
        t_dir = (adj_pt - end_pt).normalized()
        rot_euler = t_dir.to_track_quat("Z", "Y").to_euler()
        objects.append(make_cylinder(
            f"{prefix}_HoseCoupler_{idx}",
            radius=radius * 1.55,
            depth=radius * 2.2,
            location=end_pt + t_dir * (radius * 0.8),
            material=c_mat,
            rotation=rot_euler,
            vertices=6,
            bevel=0.004,
        ))

    # Intermediate retaining collar clamps
    if clamp_count > 0:
        for c_idx in range(1, clamp_count + 1):
            t_c = c_idx / float(clamp_count + 1)
            f_idx = min(num_segs - 1, max(1, int(round(t_c * num_segs))))
            c_pt = points[f_idx]
            c_tan = (points[f_idx + 1] - points[f_idx - 1]).normalized()
            rot_c = c_tan.to_track_quat("Z", "Y").to_euler()
            col_mat = accent_mat if (accent_mat and c_idx == 1) else c_mat
            objects.append(make_cylinder(
                f"{prefix}_HoseClamp_{c_idx}",
                radius=radius * 1.42,
                depth=radius * 1.4,
                location=c_pt,
                material=col_mat,
                rotation=rot_c,
                vertices=8,
                bevel=0.003,
            ))

    return objects


def make_bmesh_hull_greebles(
    prefix: str,
    hull_length: float = 3.0,
    hull_width: float = 2.4,
    deck_z: float = 0.90,
    deck_center: Tuple[float, float, float] | Vector = (0.0, 0.0, 0.0),
    armor_mat: bpy.types.Material | None = None,
    metal_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    include_lugs: bool = True,
    include_vents: bool = True,
) -> list[bpy.types.Object]:
    """Generates low-poly BMesh surface greebles: recessed maintenance hatches, louver vents, and forged lifting lugs."""
    objects: list[bpy.types.Object] = []
    a_mat = armor_mat or metal_mat
    m_mat = metal_mat or armor_mat
    center = Vector(deck_center)

    # 1. Bilateral BMesh-inset maintenance access panels with raised handle bars
    panel_w = min(0.36, hull_width * 0.16)
    panel_l = min(0.46, hull_length * 0.16)
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        px = center.x + side * (hull_width * 0.26)
        py = center.y + hull_length * 0.22
        pz = center.z + deck_z + 0.015

        mesh = bpy.data.meshes.new(f"{prefix}_GreebleHatch_{stag}_Mesh")
        hatch_obj = bpy.data.objects.new(f"{prefix}_GreebleHatch_{stag}", mesh)
        hatch_obj.location = (px, py, pz)
        bpy.context.collection.objects.link(hatch_obj)

        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        for v in bm.verts:
            v.co.x *= panel_w
            v.co.y *= panel_l
            v.co.z *= 0.032
        bm.faces.ensure_lookup_table()
        top_faces = [f for f in bm.faces if f.normal.z > 0.8]
        if top_faces:
            res = bmesh.ops.inset_individual(bm, faces=top_faces, thickness=0.035, depth=-0.010)
            inner_faces = [f for f in bm.faces if f.normal.z > 0.8 and f not in res.get("faces", [])]
            if inner_faces:
                bmesh.ops.inset_individual(bm, faces=inner_faces, thickness=0.018, depth=0.014)
        bm.normal_update()
        bm.to_mesh(mesh)
        bm.free()
        finish_mesh(hatch_obj, a_mat, bevel=0.006, smooth=False)
        objects.append(hatch_obj)

        # Recessed grab handle bar on hatch
        objects.append(make_box(
            f"{prefix}_GreebleHandle_{stag}",
            (0.025, panel_l * 0.38, 0.022),
            (px, py, pz + 0.024),
            m_mat, bevel=0.004
        ))

    # 2. Slotted micro-louver heat dissipation cassette on rear/mid deck
    if include_vents:
        louver_loc = Vector((center.x, center.y - hull_length * 0.14, center.z + deck_z + 0.02))
        louver_w = min(0.58, hull_width * 0.26)
        louver_l = min(0.42, hull_length * 0.14)
        objects.append(make_box(
            f"{prefix}_GreebleLouverFrame",
            (louver_w, louver_l, 0.028),
            louver_loc, m_mat, bevel=0.006
        ))
        slat_count = 4
        for idx in range(slat_count):
            sy = louver_loc.y - louver_l * 0.34 + idx * (louver_l * 0.68 / max(1, slat_count - 1))
            objects.append(make_box(
                f"{prefix}_GreebleLouverSlat_{idx+1}",
                (louver_w * 0.86, 0.032, 0.024),
                (louver_loc.x, sy, louver_loc.z + 0.015),
                a_mat, bevel=0.004, rotation=(math.radians(22.0), 0.0, 0.0)
            ))

    # 3. Four corner forged crane lifting lugs / tow shackles
    if include_lugs:
        for sx in (-1, 1):
            for sy in (-1, 1):
                tag = f"{'L' if sx < 0 else 'R'}{'F' if sy > 0 else 'R'}"
                lx = center.x + sx * (hull_width * 0.34)
                ly = center.y + sy * (hull_length * 0.38)
                lz = center.z + deck_z + 0.025
                objects.append(make_box(
                    f"{prefix}_GreebleLiftLug_{tag}",
                    (0.036, 0.095, 0.055),
                    (lx, ly, lz),
                    accent_mat if (accent_mat and sy > 0) else m_mat,
                    bevel=0.008,
                ))

    return objects


def make_tactical_decal_plates(
    prefix: str,
    target_objects: Sequence[bpy.types.Object],
    hull_length: float = 3.0,
    hull_width: float = 2.4,
    deck_z: float = 0.90,
    decal_mat: bpy.types.Material | None = None,
    secondary_mat: bpy.types.Material | None = None,
    emissive_mat: bpy.types.Material | None = None,
    stripe_count: int = 2,
    include_zenith_id: bool = True,
) -> list[bpy.types.Object]:
    """Projects low-poly tactical chevron stripes and faction ID stencil plates onto armor surfaces via BVHTree raycasting."""
    from mathutils.bvhtree import BVHTree

    objects: list[bpy.types.Object] = []
    w_mat = decal_mat or secondary_mat
    l_mat = secondary_mat or decal_mat
    stripe_count = max(0, min(8, int(stripe_count)))

    # Filter candidate armor meshes for BVHTree surface snapping
    armor_meshes = [
        o for o in target_objects
        if o.type == "MESH"
        and any(k in o.name for k in ("Hull", "Torso", "Citadel", "Cab", "Shelter", "Hall", "Bunker", "Foundation"))
        and not o.name.endswith(("_COLLIDER", "_ShieldBubble"))
    ]

    bpy.context.view_layer.update()
    bvh_trees: list[BVHTree] = []
    for o in armor_meshes:
        try:
            bm = bmesh.new()
            bm.from_mesh(o.data)
            bm.transform(o.matrix_world)
            bvh_trees.append(BVHTree.FromBMesh(bm))
            bm.free()
        except Exception:
            pass

    def _raycast_armor(origin: Vector, direction: Vector, max_dist: float = 3.5) -> Tuple[Vector, Vector] | None:
        best_loc = None
        best_norm = None
        best_dist = max_dist
        dir_n = direction.normalized()
        for tree in bvh_trees:
            loc, norm, _, dist = tree.ray_cast(origin, dir_n, best_dist)
            if loc is not None and norm is not None and dist < best_dist:
                best_dist = dist
                best_loc = loc
                best_norm = norm.normalized()
        if best_loc is not None and best_norm is not None:
            return best_loc, best_norm
        return None

    # 1. Bilateral flank tactical chevron stripes (snapped to side hull/torso armor)
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        stripe_y = hull_length * 0.08
        ray_origin = Vector((side * (hull_width * 1.35), stripe_y, deck_z * 0.78))
        ray_dir = Vector((-side, 0.0, 0.0))
        hit = _raycast_armor(ray_origin, ray_dir)
        if hit is not None:
            hit_loc, hit_norm = hit
            # Ensure outward normal points toward the side
            if hit_norm.dot(Vector((side, 0.0, 0.0))) < 0.0:
                hit_norm = -hit_norm
            plate_center = hit_loc + hit_norm * 0.012
            rot_euler = hit_norm.to_track_quat("Z", "Y").to_euler()
        else:
            plate_center = Vector((side * (hull_width * 0.48 + 0.012), stripe_y, deck_z * 0.75))
            rot_euler = Vector((side, 0.0, 0.0)).to_track_quat("Z", "Y").to_euler()

        # Primary warning stripes and a secondary ID badge on each flank.
        for stripe_index in range(stripe_count):
            stripe_offset = (stripe_index - (stripe_count - 1) * 0.5) * 0.18
            stripe_pos = plate_center + Vector((0.0, stripe_offset, 0.0))
            objects.append(make_box(
                f"{prefix}_DecalStripe_{stag}_{stripe_index + 1}",
                (0.42, 0.11, 0.012),
                stripe_pos,
                w_mat, bevel=0.003, rotation=rot_euler
            ))
        objects.append(make_box(
            f"{prefix}_DecalBadge_{stag}",
            (0.16, 0.16, 0.014),
            plate_center + Vector((0.0, -0.32, 0.0)),
            l_mat, bevel=0.003, rotation=rot_euler
        ))

    # 2. Forward glacis / upper deck zenith identification chevron plate (visible from 45° RTS camera)
    if include_zenith_id:
        top_origin = Vector((0.0, hull_length * 0.28, deck_z + 2.2))
        top_hit = _raycast_armor(top_origin, Vector((0.0, 0.0, -1.0)))
        if top_hit is not None:
            t_loc, t_norm = top_hit
            if t_norm.z < 0.0:
                t_norm = -t_norm
            front_plate_pos = t_loc + t_norm * 0.012
            front_rot = t_norm.to_track_quat("Z", "Y").to_euler()
        else:
            front_plate_pos = Vector((0.0, hull_length * 0.26, deck_z + 0.015))
            front_rot = (0.0, 0.0, 0.0)

        objects.append(make_box(
            f"{prefix}_DecalChevron_Front",
            (min(0.52, hull_width * 0.28), 0.14, 0.014),
            front_plate_pos,
            w_mat, bevel=0.003, rotation=front_rot
        ))
        if emissive_mat is not None:
            objects.append(make_box(
                f"{prefix}_DecalBeacon_Front",
                (min(0.22, hull_width * 0.12), 0.05, 0.016),
                front_plate_pos + Vector((0.0, -0.11, 0.002)),
                emissive_mat, bevel=0.002, rotation=front_rot
            ))

    return objects


def make_telescopic_piston_assembly(
    prefix: str,
    start_point: Tuple[float, float, float] | Vector,
    end_point: Tuple[float, float, float] | Vector,
    outer_radius: float = 0.046,
    inner_radius: float = 0.028,
    stroke_ratio: float = 0.55,
    housing_mat: bpy.types.Material | None = None,
    rod_mat: bpy.types.Material | None = None,
    seal_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a dual-stage telescopic hydraulic actuator cylinder with outer housing, wiper seal collar, inner ram rod, and clevis pin eye-mounts."""
    objects: list[bpy.types.Object] = []
    p1 = Vector(start_point)
    p2 = Vector(end_point)
    vec = p2 - p1
    total_len = vec.length
    if total_len < 0.06:
        return objects

    axis = vec.normalized()
    u_vec, v_vec, _ = get_orthonormal_basis(axis)
    rot_euler = axis.to_track_quat("Z", "Y").to_euler()
    clevis_rot = u_vec.to_track_quat("Z", "Y").to_euler()

    h_mat = housing_mat or rod_mat
    r_mat = rod_mat or housing_mat
    s_mat = seal_mat or h_mat
    a_mat = accent_mat or s_mat

    ratio = max(0.25, min(0.75, float(stroke_ratio)))
    housing_len = total_len * ratio

    # 1. Start and end clevis eye-mount knuckles + cross pins
    for tag, pt in (("Base", p1), ("Tip", p2)):
        objects.append(make_cylinder(
            f"{prefix}_ClevisEye_{tag}",
            radius=outer_radius * 1.18,
            depth=outer_radius * 1.85,
            location=pt,
            material=h_mat if tag == "Base" else r_mat,
            rotation=clevis_rot,
            vertices=10,
            bevel=0.005,
        ))
        objects.append(make_cylinder(
            f"{prefix}_ClevisPin_{tag}",
            radius=outer_radius * 0.52,
            depth=outer_radius * 2.25,
            location=pt,
            material=a_mat if tag == "Base" else s_mat,
            rotation=clevis_rot,
            vertices=8,
            bevel=0.003,
        ))

    # 2. Outer hydraulic barrel housing
    housing_start = p1 + axis * (outer_radius * 0.7)
    housing_end = p1 + axis * housing_len
    if (housing_end - housing_start).length > 0.02:
        objects.append(make_rod(
            f"{prefix}_OuterBarrel",
            housing_start,
            housing_end,
            radius=outer_radius,
            material=h_mat,
            vertices=10,
            bevel=0.006,
        ))

    # 3. Wiper seal gland collar at barrel mouth
    objects.append(make_cylinder(
        f"{prefix}_WiperSealCollar",
        radius=outer_radius * 1.24,
        depth=outer_radius * 1.15,
        location=housing_end,
        material=s_mat,
        rotation=rot_euler,
        vertices=10,
        bevel=0.004,
    ))
    if accent_mat is not None:
        objects.append(make_cylinder(
            f"{prefix}_PressureRing",
            radius=outer_radius * 1.28,
            depth=outer_radius * 0.35,
            location=housing_end - axis * (outer_radius * 0.45),
            material=a_mat,
            rotation=rot_euler,
            vertices=10,
            bevel=0.002,
        ))

    # 4. Inner telescopic chrome/steel ram rod
    rod_start = housing_end - axis * (outer_radius * 0.5)
    rod_end = p2 - axis * (outer_radius * 0.65)
    if (rod_end - rod_start).length > 0.02:
        objects.append(make_rod(
            f"{prefix}_InnerRamRod",
            rod_start,
            rod_end,
            radius=inner_radius,
            material=r_mat,
            vertices=10,
            bevel=0.004,
        ))

    return objects


def make_ammo_belt_chute_assembly(
    prefix: str,
    start_point: Tuple[float, float, float] | Vector,
    end_point: Tuple[float, float, float] | Vector,
    chute_width: float = 0.14,
    chute_thickness: float = 0.048,
    sag: float = 0.12,
    lateral_bow: float = 0.08,
    link_count: int = 9,
    hopper_mat: bpy.types.Material | None = None,
    chute_mat: bpy.types.Material | None = None,
    brass_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an articulated metallic ammunition feed chute with feed hopper box, receiver adapter, and linked guide ribs."""
    objects: list[bpy.types.Object] = []
    p1 = Vector(start_point)
    p2 = Vector(end_point)
    span = (p2 - p1).length
    if span < 0.08:
        return objects

    h_mat = hopper_mat or chute_mat
    c_mat = chute_mat or hopper_mat
    b_mat = brass_mat or c_mat
    n_links = max(5, int(link_count))

    # 1. Evaluate 3D curve points with catenary sag and outward lateral bow
    outward_x = 1.0 if ((p1.x + p2.x) * 0.5 >= 0.0) else -1.0
    points: list[Vector] = []
    for i in range(n_links + 1):
        t = i / float(n_links)
        pt = p1.lerp(p2, t)
        arch = math.sin(math.pi * t)
        pt.z -= sag * arch
        pt.x += outward_x * lateral_bow * arch
        points.append(pt)

    # 2. Construct continuous rectangular BMesh ribbon chute with stepped expansion ribs
    mesh = bpy.data.meshes.new(f"{prefix}_FeedChute_Mesh")
    chute_obj = bpy.data.objects.new(f"{prefix}_FeedChute", mesh)
    bpy.context.collection.objects.link(chute_obj)
    bm = bmesh.new()

    rings: list[list[bmesh.types.BMVert]] = []
    for i, pt in enumerate(points):
        if i == 0:
            tangent = (points[1] - points[0]).normalized()
        elif i == len(points) - 1:
            tangent = (points[-1] - points[-2]).normalized()
        else:
            tangent = (points[i + 1] - points[i - 1]).normalized()

        u_vec, v_vec, _ = get_orthonormal_basis(tangent)
        rib_flare = 1.12 if (i % 2 == 1 and 0 < i < n_links) else 1.0
        hw = chute_width * 0.5 * rib_flare
        ht = chute_thickness * 0.5 * rib_flare
        corners = [
            pt - u_vec * hw - v_vec * ht,
            pt + u_vec * hw - v_vec * ht,
            pt + u_vec * hw + v_vec * ht,
            pt - u_vec * hw + v_vec * ht,
        ]
        rings.append([bm.verts.new(c) for c in corners])

    for i in range(len(rings) - 1):
        r0 = rings[i]
        r1 = rings[i + 1]
        for s in range(4):
            sn = (s + 1) % 4
            bm.faces.new((r0[s], r0[sn], r1[sn], r1[s]))

    bm.faces.new(reversed(rings[0]))
    bm.faces.new(rings[-1])
    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    finish_mesh(chute_obj, c_mat, bevel=0.004, smooth=False)
    objects.append(chute_obj)

    # 3. Exposed brass cartridge glimpse strips at alternate chute links
    for i in range(1, n_links, 2):
        pt = points[i]
        tan = (points[i + 1] - points[i - 1]).normalized()
        rot_e = tan.to_track_quat("Z", "Y").to_euler()
        objects.append(make_box(
            f"{prefix}_CartridgeLink_{i}",
            (chute_width * 1.16, chute_thickness * 1.14, 0.022),
            pt,
            b_mat,
            bevel=0.003,
            rotation=rot_e,
        ))

    # 4. Source Ammo Hopper Box & Breech Feed Throat Collar
    objects.append(make_box(
        f"{prefix}_AmmoHopperBox",
        (chute_width * 1.55, chute_width * 1.65, chute_width * 1.35),
        p1,
        h_mat,
        bevel=0.012,
    ))
    end_tan = (points[-1] - points[-2]).normalized()
    objects.append(make_box(
        f"{prefix}_FeedThroatCollar",
        (chute_width * 1.32, chute_thickness * 1.45, 0.065),
        p2,
        b_mat,
        bevel=0.006,
        rotation=end_tan.to_track_quat("Z", "Y").to_euler(),
    ))

    return objects


def make_aps_hardkill_suite(
    prefix: str,
    turret_center: Tuple[float, float, float] | Vector,
    turret_width: float = 1.45,
    turret_length: float = 1.65,
    turret_height: float = 0.45,
    armor_mat: bpy.types.Material | None = None,
    metal_mat: bpy.types.Material | None = None,
    radar_mat: bpy.types.Material | None = None,
    emissive_mat: bpy.types.Material | None = None,
    include_lwr: bool = True,
) -> list[bpy.types.Object]:
    """Generates a modern Active Protection System (APS) suite: bilateral hard-kill interceptor launchers, 4 corner millimeter-wave radar panels, and roof Laser Warning Receiver (LWR) prism."""
    objects: list[bpy.types.Object] = []
    tc = Vector(turret_center)
    a_mat = armor_mat or metal_mat
    m_mat = metal_mat or armor_mat
    r_mat = radar_mat or a_mat
    e_mat = emissive_mat or r_mat

    roof_z = tc.z + turret_height * 0.5

    # 1. Bilateral swiveling hard-kill interceptor launcher cassettes on turret roof flanks
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        base_loc = Vector((tc.x + side * (turret_width * 0.34), tc.y - turret_length * 0.14, roof_z + 0.04))
        objects.append(make_cylinder(
            f"{prefix}_APSTurntable_{stag}",
            radius=0.11,
            depth=0.06,
            location=base_loc,
            material=m_mat,
            vertices=10,
            bevel=0.006,
        ))
        pod_loc = base_loc + Vector((0.0, 0.02, 0.09))
        yaw_rad = math.radians(side * -22.0)
        pitch_rad = math.radians(-14.0)
        pod_rot = (pitch_rad, 0.0, yaw_rad)
        objects.append(make_box(
            f"{prefix}_APSLauncherPod_{stag}",
            (0.22, 0.28, 0.13),
            pod_loc,
            a_mat,
            bevel=0.012,
            rotation=pod_rot,
        ))
        # Dual countermeasure mortar tubes inside each APS pod
        for tube_idx, tx in enumerate((-0.052, 0.052), start=1):
            tube_pos = pod_loc + Vector((tx * math.cos(yaw_rad), 0.13, 0.015))
            objects.append(make_cylinder(
                f"{prefix}_APSTube_{stag}_{tube_idx}",
                radius=0.036,
                depth=0.08,
                location=tube_pos,
                material=r_mat if tube_idx == 1 else m_mat,
                rotation=(math.radians(76.0), 0.0, yaw_rad),
                vertices=8,
                bevel=0.004,
            ))

    # 2. Four corner faceted millimeter-wave AESA radar transceiver panels
    for sx in (-1, 1):
        for sy in (-1, 1):
            qtag = f"{'F' if sy > 0 else 'R'}{'L' if sx < 0 else 'R'}"
            rx = tc.x + sx * (turret_width * 0.46)
            ry = tc.y + sy * (turret_length * 0.38)
            rz = tc.z + turret_height * 0.08
            panel_yaw = math.atan2(-sx, sy)
            objects.append(make_box(
                f"{prefix}_APSRadarPanel_{qtag}",
                (0.18, 0.055, 0.14),
                (rx, ry, rz),
                r_mat,
                bevel=0.008,
                rotation=(math.radians(-10.0), 0.0, panel_yaw),
            ))
            objects.append(make_box(
                f"{prefix}_APSRadarDielectric_{qtag}",
                (0.13, 0.025, 0.095),
                (rx + sx * 0.015, ry + sy * 0.015, rz),
                e_mat if sy > 0 else a_mat,
                bevel=0.004,
                rotation=(math.radians(-10.0), 0.0, panel_yaw),
            ))

    # 3. Hemispherical Laser Warning Receiver (LWR) optical mast on turret roof
    if include_lwr:
        lwr_base = Vector((tc.x, tc.y + turret_length * 0.16, roof_z + 0.04))
        objects.append(make_cylinder(
            f"{prefix}_LWRPedestal",
            radius=0.065,
            depth=0.08,
            location=lwr_base,
            material=m_mat,
            vertices=8,
            bevel=0.005,
        ))
        objects.append(make_cylinder(
            f"{prefix}_LWRPrismHead",
            radius=0.078,
            depth=0.075,
            location=lwr_base + Vector((0.0, 0.0, 0.075)),
            material=e_mat,
            vertices=8,
            bevel=0.012,
        ))
        objects.append(make_cylinder(
            f"{prefix}_LWRCap",
            radius=0.082,
            depth=0.022,
            location=lwr_base + Vector((0.0, 0.0, 0.12)),
            material=a_mat,
            vertices=8,
            bevel=0.004,
        ))

    return objects


def make_tactical_stowage_kit(
    prefix: str,
    hull_length: float = 3.0,
    hull_width: float = 2.4,
    deck_z: float = 0.88,
    turret_center: Tuple[float, float, float] | Vector | None = None,
    turret_length: float = 1.60,
    turret_width: float = 1.40,
    armor_mat: bpy.types.Material | None = None,
    metal_mat: bpy.types.Material | None = None,
    tarp_mat: bpy.types.Material | None = None,
    tread_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates external military stowage: rear turret bustle rack, rolled camo tarp with retention straps, auxiliary Jerry cans, and glacis spare track-link brackets."""
    objects: list[bpy.types.Object] = []
    a_mat = armor_mat or metal_mat
    m_mat = metal_mat or armor_mat
    tp_mat = tarp_mat or a_mat
    tr_mat = tread_mat or m_mat
    ac_mat = accent_mat or a_mat

    anchor = Vector(turret_center) if turret_center is not None else Vector((0.0, -hull_length * 0.15, deck_z + 0.22))
    rack_y = anchor.y - turret_length * 0.56
    rack_z = anchor.z + 0.02
    rack_w = min(1.25, turret_width * 0.78)

    # 1. Rear turret bustle tubular cargo rack frame
    rail_pts = [
        Vector((-rack_w * 0.5, rack_y + 0.12, rack_z)),
        Vector((-rack_w * 0.5, rack_y - 0.22, rack_z)),
        Vector((rack_w * 0.5, rack_y - 0.22, rack_z)),
        Vector((rack_w * 0.5, rack_y + 0.12, rack_z)),
    ]
    for idx in range(len(rail_pts) - 1):
        objects.append(make_rod(
            f"{prefix}_BustleRackBar_{idx+1}",
            rail_pts[idx],
            rail_pts[idx + 1],
            radius=0.018,
            material=m_mat,
            vertices=6,
        ))
        objects.append(make_rod(
            f"{prefix}_BustleRackLower_{idx+1}",
            rail_pts[idx] - Vector((0.0, 0.0, 0.14)),
            rail_pts[idx + 1] - Vector((0.0, 0.0, 0.14)),
            radius=0.016,
            material=m_mat,
            vertices=6,
        ))

    # 2. Rolled canvas camouflage tarp bundle with dual retention straps
    tarp_loc = Vector((0.0, rack_y - 0.10, rack_z + 0.08))
    objects.append(make_cylinder(
        f"{prefix}_CamoTarpRoll",
        radius=0.105,
        depth=rack_w * 0.76,
        location=tarp_loc,
        material=tp_mat,
        rotation=(0.0, math.radians(90.0), 0.0),
        vertices=10,
        bevel=0.025,
    ))
    for s_idx, sx in enumerate((-rack_w * 0.22, rack_w * 0.22), start=1):
        objects.append(make_cylinder(
            f"{prefix}_TarpStrap_{s_idx}",
            radius=0.114,
            depth=0.035,
            location=(sx, tarp_loc.y, tarp_loc.z),
            material=tr_mat,
            rotation=(0.0, math.radians(90.0), 0.0),
            vertices=10,
            bevel=0.004,
        ))

    # 3. Bilateral auxiliary Jerry fuel cans in side racks
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        can_loc = Vector((side * (rack_w * 0.56), rack_y - 0.04, rack_z - 0.02))
        objects.append(make_box(
            f"{prefix}_JerryCan_{stag}",
            (0.12, 0.22, 0.28),
            can_loc,
            ac_mat if side > 0 else a_mat,
            bevel=0.012,
        ))
        objects.append(make_box(
            f"{prefix}_JerryCanBracket_{stag}",
            (0.14, 0.24, 0.04),
            can_loc - Vector((0.0, 0.0, 0.08)),
            m_mat,
            bevel=0.004,
        ))

    # 4. Forward glacis spare track-link armor blocks & retention bar
    glacis_y = hull_length * 0.42
    glacis_z = deck_z * 0.76
    link_count = 4
    span_x = min(1.10, hull_width * 0.48)
    for i in range(link_count):
        lx = -span_x * 0.5 + i * (span_x / max(1, link_count - 1))
        objects.append(make_box(
            f"{prefix}_SpareTrackLink_{i+1}",
            (0.18, 0.065, 0.22),
            (lx, glacis_y, glacis_z),
            tr_mat,
            bevel=0.008,
            rotation=(math.radians(-24.0), 0.0, 0.0),
        ))
    objects.append(make_box(
        f"{prefix}_SpareTrackClampBar",
        (span_x + 0.22, 0.04, 0.035),
        (0.0, glacis_y + 0.025, glacis_z),
        m_mat,
        bevel=0.005,
    ))

    return objects


def make_slat_cage_armor_assembly(
    prefix: str,
    hull_length: float = 3.0,
    hull_width: float = 2.4,
    deck_z: float = 0.88,
    turret_center: Tuple[float, float, float] | Vector | None = None,
    turret_length: float = 1.60,
    turret_width: float = 1.40,
    slat_count: int = 5,
    standoff_dist: float = 0.14,
    metal_mat: bpy.types.Material | None = None,
    armor_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates anti-HEAT standoff slat/cage bar armor grids around rear hull flanks and turret bustle cheeks."""
    objects: list[bpy.types.Object] = []
    m_mat = metal_mat or armor_mat
    a_mat = armor_mat or metal_mat
    ac_mat = accent_mat or m_mat
    n_slats = max(3, min(8, int(slat_count)))

    # 1. Bilateral rear hull flank anti-HEAT slat cage grids (consolidated into single BMesh per side)
    cage_len = max(0.85, hull_length * 0.42)
    cage_h = max(0.32, min(0.56, deck_z * 0.48))
    cage_y = -hull_length * 0.20
    cage_z = max(0.32, deck_z - cage_h * 0.55)

    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        base_x = side * (hull_width * 0.50)
        outer_x = base_x + side * standoff_dist

        mesh = bpy.data.meshes.new(f"{prefix}_HullSlatCage_{stag}_Mesh")
        cage_obj = bpy.data.objects.new(f"{prefix}_HullSlatCage_{stag}", mesh)
        bpy.context.collection.objects.link(cage_obj)
        bm = bmesh.new()

        def _append_box_to_bmesh(center_v: Vector, dims: Tuple[float, float, float]) -> None:
            res = bmesh.ops.create_cube(bm, size=1.0)
            verts = res["verts"]
            for v in verts:
                v.co.x = v.co.x * dims[0] + center_v.x
                v.co.y = v.co.y * dims[1] + center_v.y
                v.co.z = v.co.z * dims[2] + center_v.z

        # Horizontal slat blades
        for i in range(n_slats):
            t = i / float(max(1, n_slats - 1))
            sz = cage_z - cage_h * 0.45 + t * (cage_h * 0.90)
            _append_box_to_bmesh(Vector((outer_x, cage_y, sz)), (0.032, cage_len, 0.022))

        # Vertical framing ribs + standoff struts
        for idx, fy in enumerate((cage_y - cage_len * 0.44, cage_y, cage_y + cage_len * 0.44)):
            _append_box_to_bmesh(Vector((outer_x, fy, cage_z)), (0.038, 0.036, cage_h))
            if idx != 1:
                mid_x = (base_x + outer_x) * 0.5
                _append_box_to_bmesh(Vector((mid_x, fy, cage_z + cage_h * 0.30)), (standoff_dist, 0.042, 0.042))
                _append_box_to_bmesh(Vector((mid_x, fy, cage_z - cage_h * 0.30)), (standoff_dist, 0.042, 0.042))

        bm.normal_update()
        bm.to_mesh(mesh)
        bm.free()
        finish_mesh(cage_obj, m_mat, bevel=0.004, smooth=False)
        objects.append(cage_obj)

        # Corner hazard tie plates
        objects.append(make_box(
            f"{prefix}_SlatTiePlate_{stag}",
            (0.045, 0.14, cage_h * 0.82),
            (outer_x + side * 0.006, cage_y - cage_len * 0.38, cage_z),
            ac_mat if side > 0 else a_mat,
            bevel=0.005,
        ))

    # 2. Turret bustle rear wrap-around slat screen
    if turret_center is not None:
        tc = Vector(turret_center)
        rear_y = tc.y - turret_length * 0.56 - standoff_dist * 0.65
        rear_w = max(0.75, turret_width * 0.84)
        rear_h = 0.28
        mesh_t = bpy.data.meshes.new(f"{prefix}_TurretSlatScreen_Mesh")
        t_obj = bpy.data.objects.new(f"{prefix}_TurretSlatScreen", mesh_t)
        bpy.context.collection.objects.link(t_obj)
        bm_t = bmesh.new()
        for i in range(4):
            tz = tc.z - 0.10 + i * (rear_h / 3.0)
            res = bmesh.ops.create_cube(bm_t, size=1.0)
            for v in res["verts"]:
                v.co.x = v.co.x * rear_w + tc.x
                v.co.y = v.co.y * 0.032 + rear_y
                v.co.z = v.co.z * 0.020 + tz
        for sx in (-1, 1):
            res = bmesh.ops.create_cube(bm_t, size=1.0)
            for v in res["verts"]:
                v.co.x = v.co.x * 0.038 + (tc.x + sx * rear_w * 0.46)
                v.co.y = v.co.y * 0.14 + (rear_y + 0.06)
                v.co.z = v.co.z * (rear_h + 0.04) + (tc.z + 0.04)
        bm_t.normal_update()
        bm_t.to_mesh(mesh_t)
        bm_t.free()
        finish_mesh(t_obj, m_mat, bevel=0.004, smooth=False)
        objects.append(t_obj)

    return objects


def make_era_reactive_bricks_assembly(
    prefix: str,
    hull_length: float = 3.0,
    hull_width: float = 2.4,
    deck_z: float = 0.88,
    turret_center: Tuple[float, float, float] | Vector | None = None,
    turret_length: float = 1.60,
    turret_width: float = 1.40,
    cols: int = 5,
    rows: int = 2,
    brick_mat: bpy.types.Material | None = None,
    bolt_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates staggered Explosive Reactive Armor (ERA) Kontakt/Relikt wedge bricks across the front glacis and turret cheeks."""
    objects: list[bpy.types.Object] = []
    b_mat = brick_mat or bolt_mat
    m_mat = bolt_mat or brick_mat
    ac_mat = accent_mat or b_mat

    n_cols = max(3, min(8, int(cols)))
    n_rows = max(1, min(3, int(rows)))
    brick_w = min(0.28, (hull_width * 0.68) / n_cols)
    brick_l = 0.18
    brick_h = 0.075

    # 1. Forward sloped glacis staggered ERA brick array (single BMesh)
    mesh = bpy.data.meshes.new(f"{prefix}_GlacisERABricks_Mesh")
    era_obj = bpy.data.objects.new(f"{prefix}_GlacisERABricks", mesh)
    bpy.context.collection.objects.link(era_obj)
    bm = bmesh.new()

    span_x = n_cols * (brick_w + 0.025)
    base_y = hull_length * 0.36
    base_z = deck_z * 0.84
    pitch_rad = math.radians(-22.0)
    cos_p, sin_p = math.cos(pitch_rad), math.sin(pitch_rad)

    for r in range(n_rows):
        stagger_x = (0.04 if (r % 2 == 1) else -0.04) if n_rows > 1 else 0.0
        ry = base_y + r * (brick_l + 0.025) * cos_p
        rz = base_z - r * (brick_l + 0.025) * abs(sin_p)
        for c in range(n_cols):
            cx = -span_x * 0.5 + (c + 0.5) * (span_x / n_cols) + stagger_x
            res = bmesh.ops.create_cube(bm, size=1.0)
            for v in res["verts"]:
                lx = v.co.x * brick_w
                ly = v.co.y * brick_l
                lz = v.co.z * brick_h
                # Wedge profile: taper top front edge slightly
                if v.co.z > 0 and v.co.y > 0:
                    lz *= 0.68
                rot_y = ly * cos_p - lz * sin_p
                rot_z = ly * sin_p + lz * cos_p
                v.co = Vector((cx + lx, ry + rot_y, rz + rot_z))

    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    finish_mesh(era_obj, b_mat, bevel=0.008, smooth=False)
    objects.append(era_obj)

    # Retaining rails and corner warning tiles
    objects.append(make_box(
        f"{prefix}_GlacisERARail",
        (span_x * 0.96, 0.042, 0.036),
        (0.0, base_y - 0.06, base_z + 0.04),
        m_mat,
        bevel=0.005,
    ))

    # 2. Bilateral angled turret cheek ERA wedge blocks
    if turret_center is not None:
        tc = Vector(turret_center)
        for side in (-1, 1):
            stag = "L" if side < 0 else "R"
            for idx in range(3):
                by = tc.y + turret_length * 0.24 - idx * 0.21
                bx = tc.x + side * (turret_width * 0.48 + 0.04)
                bz = tc.z + 0.02
                objects.append(make_box(
                    f"{prefix}_TurretERABrick_{stag}_{idx+1}",
                    (0.085, 0.19, 0.16),
                    (bx, by, bz),
                    ac_mat if idx == 0 else b_mat,
                    bevel=0.010,
                    rotation=(0.0, math.radians(side * 16.0), math.radians(side * -14.0)),
                ))

    return objects


def make_camo_netting_assembly(
    prefix: str,
    hull_length: float = 3.0,
    hull_width: float = 2.4,
    deck_z: float = 0.88,
    turret_center: Tuple[float, float, float] | Vector | None = None,
    turret_length: float = 1.60,
    turret_width: float = 1.40,
    gun_pivot: Tuple[float, float, float] | Vector | None = None,
    gun_dir: Tuple[float, float, float] | Vector | None = None,
    net_mat: bpy.types.Material | None = None,
    strap_mat: bpy.types.Material | None = None,
    pole_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates procedural 3D noise-displaced draped camouflage netting canopy, support poles, and barrel ghillie wrap."""
    from mathutils import noise

    objects: list[bpy.types.Object] = []
    n_mat = net_mat or strap_mat
    s_mat = strap_mat or net_mat
    p_mat = pole_mat or s_mat

    anchor = Vector(turret_center) if turret_center is not None else Vector((0.0, 0.0, deck_z + 0.22))
    canopy_w = max(1.10, min(hull_width * 0.78, turret_width * 1.18))
    canopy_l = max(1.20, min(hull_length * 0.65, turret_length * 1.15))
    canopy_z = anchor.z + 0.28

    # 1. Noise-displaced BMesh camouflage netting canopy with perimeter droop
    mesh = bpy.data.meshes.new(f"{prefix}_CamoNetCanopy_Mesh")
    net_obj = bpy.data.objects.new(f"{prefix}_CamoNetCanopy", mesh)
    bpy.context.collection.objects.link(net_obj)
    bm = bmesh.new()

    seg_x, seg_y = 8, 8
    bmesh.ops.create_grid(bm, x_segments=seg_x, y_segments=seg_y, size=0.5)
    for v in bm.verts:
        nx = v.co.x * 2.0  # [-1, 1]
        ny = v.co.y * 2.0  # [-1, 1]
        wx = anchor.x + nx * (canopy_w * 0.5)
        wy = anchor.y + ny * (canopy_l * 0.5)
        edge_dist = max(abs(nx), abs(ny))
        # Natural perimeter drape + 3D Perlin noise folds
        n_val = noise.noise(Vector((wx * 1.85 + 1.3, wy * 1.85 + 2.7, canopy_z)))
        droop = (edge_dist ** 2) * 0.20 + abs(n_val) * 0.075
        wz = max(0.18, canopy_z - droop)
        v.co = Vector((wx, wy, wz))

    # Solidify canopy slightly so both top & underside render cleanly in URP without backface culling gaps
    ext = bmesh.ops.extrude_face_region(bm, geom=bm.faces[:])
    extruded_verts = [ele for ele in ext["geom"] if isinstance(ele, bmesh.types.BMVert)]
    for v in extruded_verts:
        v.co.z += 0.028

    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    finish_mesh(net_obj, n_mat, bevel=0.004, smooth=False)
    objects.append(net_obj)

    # 2. Corner telescopic support spreader poles
    for sx in (-1, 1):
        for sy in (-1, 1):
            qtag = f"{'F' if sy > 0 else 'R'}{'L' if sx < 0 else 'R'}"
            px = anchor.x + sx * (canopy_w * 0.44)
            py = anchor.y + sy * (canopy_l * 0.42)
            p_base = Vector((px * 0.88, py * 0.88, max(0.25, anchor.z - 0.04)))
            p_tip = Vector((px, py, canopy_z - 0.12))
            objects.append(make_rod(
                f"{prefix}_NetSupportPole_{qtag}",
                p_base,
                p_tip,
                radius=0.016,
                material=p_mat,
                vertices=6,
            ))

    # 3. Barrel ghillie wrap rings & retention straps along primary weapon tube
    if gun_pivot is not None and gun_dir is not None:
        gp = Vector(gun_pivot)
        gd = Vector(gun_dir).normalized()
        rot_e = gd.to_track_quat("Z", "Y").to_euler()
        for idx, dist_f in enumerate((0.28, 0.55, 0.82), start=1):
            ring_pos = gp - gd * dist_f
            objects.append(make_cylinder(
                f"{prefix}_BarrelGhillieWrap_{idx}",
                radius=0.115 - idx * 0.008,
                depth=0.16,
                location=ring_pos,
                material=n_mat if idx % 2 == 1 else s_mat,
                rotation=rot_e,
                vertices=8,
                bevel=0.012,
            ))

    return objects


def make_thermal_radiator_assembly(
    prefix: str,
    hull_length: float = 3.0,
    hull_width: float = 2.4,
    deck_z: float = 0.88,
    fin_count: int = 6,
    housing_mat: bpy.types.Material | None = None,
    fin_mat: bpy.types.Material | None = None,
    glow_mat: bpy.types.Material | None = None,
    pipe_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates armored heat-exchanger radiator banks with glowing emissive plasma/coolant fins and manifolds."""
    objects: list[bpy.types.Object] = []
    h_mat = housing_mat or pipe_mat
    f_mat = fin_mat or housing_mat
    g_mat = glow_mat or f_mat
    p_mat = pipe_mat or h_mat
    n_fins = max(4, min(9, int(fin_count)))

    bank_y = -hull_length * 0.26
    bank_z = deck_z + 0.08
    bank_l = max(0.56, min(1.05, hull_length * 0.28))
    bank_w = max(0.24, min(0.42, hull_width * 0.16))
    bank_h = 0.22

    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        bx = side * (hull_width * 0.30)
        tilt_rad = math.radians(side * -18.0)

        # 1. Armored radiator shroud frame
        objects.append(make_box(
            f"{prefix}_RadiatorShroud_{stag}",
            (bank_w, bank_l, 0.065),
            (bx, bank_y, bank_z - 0.04),
            h_mat,
            bevel=0.010,
            rotation=(0.0, tilt_rad, 0.0),
        ))

        # 2. Glowing emissive core bed inside shroud
        objects.append(make_box(
            f"{prefix}_RadiatorGlowCore_{stag}",
            (bank_w * 0.82, bank_l * 0.88, 0.045),
            (bx, bank_y, bank_z + 0.01),
            g_mat,
            bevel=0.006,
            rotation=(0.0, tilt_rad, 0.0),
        ))

        # 3. Array of heat-sink cooling fins (single BMesh per bank)
        mesh = bpy.data.meshes.new(f"{prefix}_RadiatorFins_{stag}_Mesh")
        fin_obj = bpy.data.objects.new(f"{prefix}_RadiatorFins_{stag}", mesh)
        bpy.context.collection.objects.link(fin_obj)
        bm = bmesh.new()

        for i in range(n_fins):
            fy = bank_y - bank_l * 0.40 + i * (bank_l * 0.80 / max(1, n_fins - 1))
            res = bmesh.ops.create_cube(bm, size=1.0)
            for v in res["verts"]:
                v.co.x = v.co.x * (bank_w * 0.88) + bx
                v.co.y = v.co.y * 0.024 + fy
                v.co.z = v.co.z * bank_h + (bank_z + bank_h * 0.35)

        bm.normal_update()
        bm.to_mesh(mesh)
        bm.free()
        finish_mesh(fin_obj, f_mat, bevel=0.003, smooth=False)
        objects.append(fin_obj)

        # 4. Longitudinal coolant manifold pipe along inboard edge
        inboard_x = bx - side * (bank_w * 0.48)
        objects.append(make_rod(
            f"{prefix}_CoolantManifold_{stag}",
            (inboard_x, bank_y - bank_l * 0.46, bank_z + 0.02),
            (inboard_x, bank_y + bank_l * 0.46, bank_z + 0.02),
            radius=0.028,
            material=p_mat,
            vertices=8,
            bevel=0.004,
        ))

    return objects


def make_remote_weapon_station_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector = (0.0, 0.0, 1.2),
    scale: float = 1.0,
    yaw_deg: float = 0.0,
    pitch_deg: float = 0.0,
    weapon_type: str = "autocannon",
    housing_mat: bpy.types.Material | None = None,
    gun_mat: bpy.types.Material | None = None,
    optic_mat: bpy.types.Material | None = None,
    ammo_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a roof-mounted independent Remote Weapon Station (RWS/CROWS) with stabilized EO/IR optical sensor ball, traversing weapon yoke, armored ammo box, and rapid-fire autocannon/HMG."""
    objects: list[bpy.types.Object] = []
    loc = Vector(location)
    h_mat = housing_mat or gun_mat
    g_mat = gun_mat or housing_mat
    o_mat = optic_mat or h_mat
    a_mat = ammo_mat or h_mat

    s = max(0.4, scale)
    yaw_rad = math.radians(yaw_deg)
    pitch_rad = math.radians(pitch_deg)

    # 1. Base rotatable pintle / azimuth plinth
    objects.append(make_cylinder(
        f"{prefix}_RWS_Plinth",
        radius=0.18 * s,
        depth=0.10 * s,
        location=loc + Vector((0.0, 0.0, 0.05 * s)),
        material=g_mat,
        vertices=10,
        bevel=0.012 * s,
    ))

    # Azimuth coordinate frame
    fwd = Vector((-math.sin(yaw_rad), math.cos(yaw_rad), 0.0)).normalized()
    right = Vector((math.cos(yaw_rad), math.sin(yaw_rad), 0.0)).normalized()
    up = Vector((0.0, 0.0, 1.0))
    cradle_center = loc + up * (0.16 * s)

    # 2. U-shaped traversing weapon yoke / trunnion housing
    yoke_rot = (0.0, 0.0, yaw_rad)
    objects.append(make_box(
        f"{prefix}_RWS_YokeBase",
        (0.32 * s, 0.28 * s, 0.08 * s),
        cradle_center,
        h_mat,
        bevel=0.012 * s,
        rotation=yoke_rot,
    ))
    # Bilateral vertical trunnion uprights
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        arm_pos = cradle_center + right * (side * 0.14 * s) + up * (0.12 * s)
        objects.append(make_box(
            f"{prefix}_RWS_Trunnion_{stag}",
            (0.06 * s, 0.20 * s, 0.18 * s),
            arm_pos,
            h_mat,
            bevel=0.010 * s,
            rotation=yoke_rot,
        ))

    # Upper bridge closes the yoke over the elevating weapon receiver.
    objects.append(make_box(
        f"{prefix}_RWS_YokeTop",
        (0.34 * s, 0.06 * s, 0.06 * s),
        cradle_center + up * (0.25 * s),
        h_mat,
        bevel=0.010 * s,
        rotation=yoke_rot,
    ))

    # 3. Armored ammunition magazine box on left flank
    ammo_pos = cradle_center - right * (0.24 * s) + up * (0.10 * s) - fwd * (0.02 * s)
    objects.append(make_box(
        f"{prefix}_RWS_AmmoBox",
        (0.14 * s, 0.26 * s, 0.20 * s),
        ammo_pos,
        a_mat,
        bevel=0.012 * s,
        rotation=yoke_rot,
    ))

    # 4. Stabilized Electro-Optical / Infrared (EO/IR) sensor ball on right flank
    optic_pos = cradle_center + right * (0.23 * s) + up * (0.14 * s) + fwd * (0.04 * s)
    objects.append(make_box(
        f"{prefix}_RWS_OpticHousing",
        (0.13 * s, 0.16 * s, 0.16 * s),
        optic_pos,
        h_mat,
        bevel=0.014 * s,
        rotation=yoke_rot,
    ))
    # Dual aperture sensor lenses: large FLIR thermal lens & small laser rangefinder
    flir_pos = optic_pos + fwd * (0.085 * s) + up * (0.02 * s)
    objects.append(make_cylinder(
        f"{prefix}_RWS_FLIR_Lens",
        radius=0.045 * s,
        depth=0.03 * s,
        location=flir_pos,
        material=o_mat,
        vertices=10,
        bevel=0.005 * s,
        rotation=(math.radians(90.0), 0.0, yaw_rad),
    ))
    lrf_pos = optic_pos + fwd * (0.085 * s) - up * (0.038 * s) + right * (0.025 * s)
    objects.append(make_cylinder(
        f"{prefix}_RWS_LRF_Aperture",
        radius=0.022 * s,
        depth=0.025 * s,
        location=lrf_pos,
        material=o_mat,
        vertices=8,
        bevel=0.003 * s,
        rotation=(math.radians(90.0), 0.0, yaw_rad),
    ))

    # 5. Elevated weapon receiver & barrel assembly
    elev_pivot = cradle_center + up * (0.14 * s)
    gun_dir = (fwd * math.cos(pitch_rad) + up * math.sin(pitch_rad)).normalized()
    gun_rot = (pitch_rad, 0.0, yaw_rad)

    # Receiver body
    objects.append(make_box(
        f"{prefix}_RWS_Receiver",
        (0.12 * s, 0.30 * s, 0.14 * s),
        elev_pivot,
        g_mat,
        bevel=0.010 * s,
        rotation=gun_rot,
    ))
    # Main autocannon / machine gun barrel
    b_len = 0.72 * s
    b_rad = 0.028 * s
    b_center = elev_pivot + gun_dir * (0.16 * s + b_len * 0.5)
    barrel_rotation = (pitch_rad - math.radians(90.0), 0.0, yaw_rad)
    objects.append(make_cylinder(
        f"{prefix}_RWS_Barrel",
        radius=b_rad,
        depth=b_len,
        location=b_center,
        material=g_mat,
        vertices=8,
        bevel=0.004 * s,
        rotation=barrel_rotation,
    ))
    # Slotted flash hider / muzzle brake
    muzzle_pos = elev_pivot + gun_dir * (0.16 * s + b_len + 0.04 * s)
    objects.append(make_cylinder(
        f"{prefix}_RWS_FlashHider",
        radius=b_rad * 1.45,
        depth=0.10 * s,
        location=muzzle_pos,
        material=g_mat,
        vertices=8,
        bevel=0.006 * s,
        rotation=barrel_rotation,
    ))

    return objects


def make_drone_docking_bay_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector = (0.0, 0.0, 1.0),
    scale: float = 1.0,
    dock_type: str = "quadcopter",
    bay_mat: bpy.types.Material | None = None,
    frame_mat: bpy.types.Material | None = None,
    drone_mat: bpy.types.Material | None = None,
    glow_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a recessed roof/deck landing cradle with magnetic charging guide rails, alignment runway glow LEDs, and a docked parasitic reconnaissance/escort drone."""
    objects: list[bpy.types.Object] = []
    loc = Vector(location)
    b_mat = bay_mat or frame_mat
    f_mat = frame_mat or bay_mat
    d_mat = drone_mat or b_mat
    g_mat = glow_mat or f_mat

    s = max(0.4, scale)

    # 1. Recessed docking tray cradle base
    tray_w = 0.95 * s
    tray_l = 0.95 * s
    tray_h = 0.08 * s
    objects.append(make_box(
        f"{prefix}_DroneDock_Tray",
        (tray_w, tray_l, tray_h),
        loc + Vector((0.0, 0.0, tray_h * 0.5)),
        b_mat,
        bevel=0.014 * s,
    ))

    # 2. Perimeter guide bevel lip / chamfered bumper curbs
    for sx in (-1, 1):
        objects.append(make_box(
            f"{prefix}_DroneDock_Curb_X_{'L' if sx < 0 else 'R'}",
            (0.06 * s, tray_l * 0.92, 0.06 * s),
            loc + Vector((sx * (tray_w * 0.46), 0.0, tray_h + 0.02 * s)),
            f_mat,
            bevel=0.008 * s,
        ))
    for sy in (-1, 1):
        objects.append(make_box(
            f"{prefix}_DroneDock_Curb_Y_{'F' if sy > 0 else 'R'}",
            (tray_w * 0.82, 0.06 * s, 0.06 * s),
            loc + Vector((0.0, sy * (tray_l * 0.46), tray_h + 0.02 * s)),
            f_mat,
            bevel=0.008 * s,
        ))

    # 3. Glowing LED alignment landing markers
    for sx in (-1, 1):
        for sy in (-1, 1):
            qtag = f"{'F' if sy > 0 else 'R'}{'L' if sx < 0 else 'R'}"
            objects.append(make_box(
                f"{prefix}_DroneDock_LED_{qtag}",
                (0.08 * s, 0.08 * s, 0.015 * s),
                loc + Vector((sx * (tray_w * 0.32), sy * (tray_l * 0.32), tray_h + 0.008 * s)),
                g_mat,
                bevel=0.003 * s,
            ))

    # Center magnetic charging contact puck
    objects.append(make_cylinder(
        f"{prefix}_DroneDock_ChargePuck",
        radius=0.10 * s,
        depth=0.025 * s,
        location=loc + Vector((0.0, 0.0, tray_h + 0.012 * s)),
        material=f_mat,
        vertices=10,
        bevel=0.004 * s,
    ))

    # 4. Docked Parasitic Recon Drone
    drone_z = loc.z + tray_h + 0.06 * s
    # Fuselage pod
    objects.append(make_box(
        f"{prefix}_ParasiteDrone_Fuselage",
        (0.24 * s, 0.36 * s, 0.09 * s),
        Vector((loc.x, loc.y, drone_z)),
        d_mat,
        bevel=0.016 * s,
    ))
    # Forward sensor optic dome
    objects.append(make_cylinder(
        f"{prefix}_ParasiteDrone_SensorEye",
        radius=0.045 * s,
        depth=0.03 * s,
        location=Vector((loc.x, loc.y + 0.18 * s, drone_z - 0.01 * s)),
        material=g_mat,
        vertices=8,
        bevel=0.004 * s,
        rotation=(math.radians(90.0), 0.0, 0.0),
    ))

    # 4 Diagonal motor outrigger booms & rotor cowlings
    boom_reach_x = 0.28 * s
    boom_reach_y = 0.28 * s
    for bx in (-1, 1):
        for by in (-1, 1):
            rtag = f"{'F' if by > 0 else 'R'}{'L' if bx < 0 else 'R'}"
            boom_start = Vector((loc.x + bx * 0.08 * s, loc.y + by * 0.10 * s, drone_z))
            boom_end = Vector((loc.x + bx * boom_reach_x, loc.y + by * boom_reach_y, drone_z + 0.02 * s))
            objects.append(make_rod(
                f"{prefix}_ParasiteDrone_Boom_{rtag}",
                boom_start,
                boom_end,
                radius=0.016 * s,
                material=f_mat,
                vertices=6,
            ))
            # Rotor duct cowling
            objects.append(make_cylinder(
                f"{prefix}_ParasiteDrone_Cowl_{rtag}",
                radius=0.085 * s,
                depth=0.045 * s,
                location=boom_end,
                material=f_mat,
                vertices=10,
                bevel=0.006 * s,
            ))
            # Rotor hub spinner
            objects.append(make_cylinder(
                f"{prefix}_ParasiteDrone_Hub_{rtag}",
                radius=0.025 * s,
                depth=0.035 * s,
                location=boom_end + Vector((0.0, 0.0, 0.015 * s)),
                material=d_mat,
                vertices=6,
                bevel=0.003 * s,
            ))

    return objects


def make_recoil_earth_spade_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector = (0.0, -1.8, 0.45),
    width: float = 1.8,
    depth: float = 0.85,
    deployed: bool = True,
    blade_mat: bpy.types.Material | None = None,
    strut_mat: bpy.types.Material | None = None,
    ram_mat: bpy.types.Material | None = None,
    rod_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates rear-mounted heavy hydraulic recoil spades / earth anchors that bite into the terrain plane (Z ≈ 0.02) to absorb massive kinetic artillery recoil."""
    objects: list[bpy.types.Object] = []
    loc = Vector(location)
    b_mat = blade_mat or strut_mat
    s_mat = strut_mat or blade_mat
    rm_mat = ram_mat or b_mat
    rd_mat = rod_mat or s_mat

    w = max(0.8, width)
    d = max(0.4, depth)

    # 1. Transverse pivot hinge trunnion crossbar
    hinge_y = loc.y
    hinge_z = loc.z
    objects.append(make_cylinder(
        f"{prefix}_RecoilSpade_HingeBar",
        radius=0.055,
        depth=w * 0.90,
        location=Vector((loc.x, hinge_y, hinge_z)),
        material=s_mat,
        vertices=10,
        bevel=0.008,
        rotation=(0.0, math.radians(90.0), 0.0),
    ))

    # Chassis anchor mounting knuckles
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        kx = loc.x + side * (w * 0.42)
        objects.append(make_box(
            f"{prefix}_RecoilSpade_MountBracket_{stag}",
            (0.08, 0.16, 0.18),
            Vector((kx, hinge_y + 0.06, hinge_z)),
            b_mat,
            bevel=0.012,
        ))

    # 2. Deployed vs Folded swing arms and spade blade coordinates
    if deployed:
        arm_end_y = hinge_y - d
        blade_z = 0.010  # Teeth end at Z=0.02, just above the terrain plane.
    else:
        arm_end_y = hinge_y - d * 0.25
        blade_z = hinge_z + 0.55  # Folded up against rear hull

    # Bilateral heavy swing arms
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        ax = loc.x + side * (w * 0.36)
        arm_start = Vector((ax, hinge_y, hinge_z))
        arm_end = Vector((ax, arm_end_y, blade_z + 0.12))
        objects.append(make_rod(
            f"{prefix}_RecoilSpade_SwingArm_{stag}",
            arm_start,
            arm_end,
            radius=0.055,
            material=b_mat,
            vertices=8,
            bevel=0.008,
        ))

    # 3. Main wedge spade anchor blade across the rear
    blade_w = w * 0.92
    blade_pos = Vector((loc.x, arm_end_y - 0.06, blade_z + 0.12))
    blade_rot = (math.radians(-24.0 if deployed else 75.0), 0.0, 0.0)
    objects.append(make_box(
        f"{prefix}_RecoilSpade_BladePlate",
        (blade_w, 0.08, 0.20),
        blade_pos,
        b_mat,
        bevel=0.012,
        rotation=blade_rot,
    ))

    # 4. Triangular ground-penetrating teeth along the bottom lip
    n_teeth = 5
    for i in range(n_teeth):
        tx = loc.x - blade_w * 0.40 + i * (blade_w * 0.80 / max(1, n_teeth - 1))
        tooth_pos = Vector((tx, arm_end_y - 0.08, blade_z + 0.04))
        objects.append(make_box(
            f"{prefix}_RecoilSpade_Tooth_{i+1}",
            (0.08, 0.08, 0.06),
            tooth_pos,
            s_mat,
            bevel=0.006,
            rotation=blade_rot,
        ))

    # 5. Dual angled hydraulic actuator rams spanning chassis to spade blade
    ram_mount_y = hinge_y + 0.30
    ram_mount_z = hinge_z + 0.36
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        rx = loc.x + side * (w * 0.28)
        upper_point = Vector((rx, ram_mount_y, ram_mount_z))
        lower_point = Vector((rx, arm_end_y + 0.04, blade_z + 0.18))
        mid_point = upper_point.lerp(lower_point, 0.55)

        # Upper cylinder barrel
        objects.append(make_rod(
            f"{prefix}_RecoilSpade_Cylinder_{stag}",
            upper_point,
            mid_point,
            radius=0.042,
            material=rm_mat,
            vertices=8,
            bevel=0.006,
        ))
        # Telescopic chrome piston rod
        objects.append(make_rod(
            f"{prefix}_RecoilSpade_PistonRod_{stag}",
            mid_point,
            lower_point,
            radius=0.026,
            material=rd_mat,
            vertices=8,
        ))

    return objects


def make_perforated_barrel_shroud_assembly(
    prefix: str,
    gun_pivot: Tuple[float, float, float] | Vector = (0.0, 1.2, 0.9),
    gun_dir: Tuple[float, float, float] | Vector = (0.0, 1.0, 0.0),
    barrel_length: float = 2.4,
    barrel_radius: float = 0.10,
    shroud_length: float = 1.4,
    shroud_radius: float = 0.16,
    shroud_mat: bpy.types.Material | None = None,
    collar_mat: bpy.types.Material | None = None,
    brake_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a ventilated thermal barrel jacket with consolidated BMesh cooling perforation slats, reinforced clamping collars, and an aggressive multi-baffle muzzle brake."""
    objects: list[bpy.types.Object] = []
    gp = Vector(gun_pivot)
    gd = Vector(gun_dir)
    if gd.length_squared <= 1e-8:
        raise ValueError("gun_dir must be a non-zero direction vector.")
    gd.normalize()
    s_mat = shroud_mat or collar_mat
    c_mat = collar_mat or shroud_mat
    b_mat = brake_mat or s_mat

    # Compute orthonormal basis (gd, right_vec, up_vec)
    world_up = Vector((0.0, 0.0, 1.0))
    if abs(gd.dot(world_up)) > 0.95:
        world_up = Vector((1.0, 0.0, 0.0))
    right_vec = gd.cross(world_up).normalized()
    up_vec = right_vec.cross(gd).normalized()

    b_len = max(0.8, barrel_length)
    s_len = min(b_len * 0.75, max(0.5, shroud_length))
    s_rad = max(barrel_radius * 1.25, shroud_radius)
    offset_dist = b_len * 0.14

    shroud_start = gp + gd * offset_dist
    shroud_end = shroud_start + gd * s_len

    # 1. Consolidated BMesh ventilated thermal shroud jacket with radial perforation slots
    mesh = bpy.data.meshes.new(f"{prefix}_BarrelShroud_Mesh")
    shroud_obj = bpy.data.objects.new(f"{prefix}_BarrelShroud", mesh)
    bpy.context.collection.objects.link(shroud_obj)
    bm = bmesh.new()

    # 8-sided polygonal jacket with alternating vented slats
    n_facets = 8
    n_slots = 5
    slot_span = s_len / max(1, n_slots)
    bar_thick = 0.022

    for f_idx in range(n_facets):
        theta = f_idx * (2.0 * math.pi / n_facets)
        rad_dir = (right_vec * math.cos(theta) + up_vec * math.sin(theta)).normalized()
        tan_dir = (-right_vec * math.sin(theta) + up_vec * math.cos(theta)).normalized()

        for s_idx in range(n_slots):
            # Perforated window: leave alternate gaps open along barrel length
            slot_mid_d = offset_dist + (s_idx + 0.5) * slot_span
            slot_center = gp + gd * slot_mid_d + rad_dir * s_rad

            # Create ventilated slat bar segment
            res = bmesh.ops.create_cube(bm, size=1.0)
            slat_w = (2.0 * math.pi * s_rad / n_facets) * 0.72
            slat_l = slot_span * 0.65  # Leaves 35% open venting slot
            for v in res["verts"]:
                p = (
                    slot_center
                    + tan_dir * (v.co.x * slat_w)
                    + gd * (v.co.y * slat_l)
                    + rad_dir * (v.co.z * bar_thick)
                )
                v.co = p

    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    finish_mesh(shroud_obj, s_mat, bevel=0.003, smooth=False)
    objects.append(shroud_obj)

    # 2. Clamping collar rings at jacket base and tip
    for c_pos in (shroud_start, shroud_end):
        rot_e = gd.to_track_quat("Z", "Y").to_euler()
        objects.append(make_cylinder(
            f"{prefix}_ShroudCollar",
            radius=s_rad * 1.08,
            depth=0.065,
            location=c_pos,
            material=c_mat,
            vertices=10,
            bevel=0.006,
            rotation=rot_e,
        ))

    # 3. High-efficiency multi-baffle aggressive muzzle brake at the barrel tip
    muzzle_tip = gp + gd * b_len
    mb_len = max(0.24, barrel_radius * 2.8)
    mb_rad = max(0.14, barrel_radius * 1.65)
    mb_pos = muzzle_tip + gd * (mb_len * 0.5)
    rot_mb = gd.to_track_quat("Z", "Y").to_euler()

    objects.append(make_cylinder(
        f"{prefix}_MuzzleBrake_Housing",
        radius=mb_rad,
        depth=mb_len,
        location=mb_pos,
        material=b_mat,
        vertices=10,
        bevel=0.008,
        rotation=rot_mb,
    ))

    # Bilateral horizontal gas-deflector baffles
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        baffle_pos = mb_pos + right_vec * (side * mb_rad * 0.55)
        objects.append(make_box(
            f"{prefix}_MuzzleBrake_Baffle_{stag}",
            (mb_rad * 0.45, mb_len * 0.60, mb_rad * 0.55),
            baffle_pos,
            c_mat,
            bevel=0.005,
        ))

    # Stepped crown ring at the muzzle exit
    objects.append(make_cylinder(
        f"{prefix}_MuzzleBrake_Crown",
        radius=mb_rad * 0.88,
        depth=0.04,
        location=muzzle_tip + gd * (mb_len + 0.02),
        material=c_mat,
        vertices=10,
        bevel=0.004,
        rotation=rot_mb,
    ))

    return objects



def make_nanite_projector_assembly(
    prefix: str,
    gun_pivot: Tuple[float, float, float] | Vector = (0.0, 1.2, 0.9),
    direction: Tuple[float, float, float] | Vector = (0.0, 1.0, 0.0),
    head_radius: float = 0.16,
    length: float = 1.2,
    cradle_mat: bpy.types.Material | None = None,
    nozzle_mat: bpy.types.Material | None = None,
    glow_mat: bpy.types.Material | None = None,
    canister_mat: bpy.types.Material | None = None,
    conduit_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an articulated medical/repair nanite projector assembly with pressurized storage canisters, elevation cradle, collimating lens ring, and multi-aperture dispersal nozzle."""
    objects: list[bpy.types.Object] = []
    gp = Vector(gun_pivot)
    gd = Vector(direction)
    if gd.length_squared <= 1e-8:
        raise ValueError("direction must be a non-zero vector.")
    gd.normalize()
    u_vec, v_vec, _ = get_orthonormal_basis(gd)
    rot_euler = gd.to_track_quat("Z", "Y").to_euler()

    cr_mat = cradle_mat or nozzle_mat
    nz_mat = nozzle_mat or cradle_mat
    gl_mat = glow_mat or cr_mat
    ca_mat = canister_mat or cr_mat
    cd_mat = conduit_mat or nz_mat

    hr = max(0.08, head_radius)
    bl = max(0.6, length)

    # 1. Armored elevation trunnion yoke / cradle
    cradle_w = hr * 3.8
    cradle_h = hr * 2.6
    cradle_loc = gp + gd * (bl * 0.12)
    objects.append(make_box(
        f"{prefix}_Nanite_Cradle",
        (cradle_w, bl * 0.28, cradle_h),
        cradle_loc,
        cr_mat,
        bevel=0.02,
        rotation=rot_euler,
    ))

    # 2. Bilateral pressurized nanite chemical canisters flanking cradle
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        can_loc = cradle_loc + u_vec * (side * (cradle_w * 0.5 + hr * 0.95))
        objects.append(make_cylinder(
            f"{prefix}_Nanite_Canister_{stag}",
            radius=hr * 0.85,
            depth=bl * 0.55,
            location=can_loc,
            material=ca_mat,
            vertices=12,
            bevel=0.02,
            rotation=rot_euler,
        ))
        # Glowing vertical level-indicator sight glass slot on each canister
        objects.append(make_box(
            f"{prefix}_Nanite_SightGlass_{stag}",
            (hr * 0.22, bl * 0.38, hr * 0.22),
            can_loc + u_vec * (side * hr * 0.75),
            gl_mat,
            bevel=0.005,
            rotation=rot_euler,
        ))
        # Flexible armored feeder conduit curving into the main barrel receiver
        tube_start = can_loc + gd * (bl * 0.22)
        tube_end = cradle_loc + gd * (bl * 0.38) + u_vec * (side * hr * 0.8)
        objects.append(make_rod(
            f"{prefix}_Nanite_Conduit_{stag}",
            tube_start,
            tube_end,
            radius=hr * 0.22,
            material=cd_mat,
            vertices=8,
        ))

    # 3. Main projection projector barrel & magnetic focusing rings
    barrel_center = gp + gd * (bl * 0.52)
    objects.append(make_cylinder(
        f"{prefix}_Nanite_ProjectorTube",
        radius=hr * 1.15,
        depth=bl * 0.70,
        location=barrel_center,
        material=nz_mat,
        vertices=12,
        bevel=0.015,
        rotation=rot_euler,
    ))
    # Stepped magnetic accelerator focusing collars
    for f_idx in range(3):
        f_pos = gp + gd * (bl * (0.32 + f_idx * 0.18))
        objects.append(make_cylinder(
            f"{prefix}_Nanite_FocusRing_{f_idx+1}",
            radius=hr * 1.35,
            depth=bl * 0.08,
            location=f_pos,
            material=ca_mat,
            vertices=12,
            bevel=0.01,
            rotation=rot_euler,
        ))

    # 4. Multi-aperture dispersal nozzle & glowing collimating lens ring
    muzzle_pos = gp + gd * (bl * 0.92)
    objects.append(make_cylinder(
        f"{prefix}_Nanite_DispersalBell",
        radius=hr * 1.55,
        depth=bl * 0.20,
        location=muzzle_pos,
        material=cr_mat,
        vertices=12,
        bevel=0.018,
        rotation=rot_euler,
    ))
    # Glowing emitter ring at aperture mouth
    objects.append(make_cylinder(
        f"{prefix}_Nanite_CollimatorGlow",
        radius=hr * 1.35,
        depth=bl * 0.05,
        location=muzzle_pos + gd * (bl * 0.10),
        material=gl_mat,
        vertices=12,
        bevel=0.005,
        rotation=rot_euler,
    ))

    return objects


def make_gravity_cannon_assembly(
    prefix: str,
    gun_pivot: Tuple[float, float, float] | Vector = (0.0, 1.2, 0.9),
    direction: Tuple[float, float, float] | Vector = (0.0, 1.0, 0.0),
    barrel_length: float = 2.2,
    bore_radius: float = 0.14,
    ring_count: int = 4,
    mount_mat: bpy.types.Material | None = None,
    barrel_mat: bpy.types.Material | None = None,
    core_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a super-heavy gravitational singularity cannon / kinetic mass driver with magnetic accelerator ring stages, glowing grav-core chamber, and longitudinal stabilization rails."""
    objects: list[bpy.types.Object] = []
    gp = Vector(gun_pivot)
    gd = Vector(direction)
    if gd.length_squared <= 1e-8:
        raise ValueError("direction must be a non-zero vector.")
    gd.normalize()
    u_vec, v_vec, _ = get_orthonormal_basis(gd)
    rot_euler = gd.to_track_quat("Z", "Y").to_euler()

    m_mat = mount_mat or barrel_mat
    b_mat = barrel_mat or mount_mat
    c_mat = core_mat or b_mat
    a_mat = accent_mat or b_mat

    br = max(0.08, bore_radius)
    bl = max(1.2, barrel_length)
    n_rings = max(2, ring_count)

    # 1. Heavy reinforced octagonal breech & hydraulic trunnion mount
    breech_w = br * 4.6
    breech_h = br * 3.8
    breech_l = bl * 0.28
    breech_pos = gp + gd * (breech_l * 0.45)
    objects.append(make_box(
        f"{prefix}_Gravity_BreechMount",
        (breech_w, breech_l, breech_h),
        breech_pos,
        m_mat,
        bevel=0.035,
        rotation=rot_euler,
    ))

    # 2. Central glowing gravitational containment core cylinder
    core_len = bl * 0.32
    core_pos = breech_pos + gd * (breech_l * 0.5 + core_len * 0.5)
    objects.append(make_cylinder(
        f"{prefix}_Gravity_SingularityCore",
        radius=br * 1.55,
        depth=core_len,
        location=core_pos,
        material=c_mat,
        vertices=12,
        bevel=0.015,
        rotation=rot_euler,
    ))

    # 3. Main bore tube
    tube_len = bl * 0.65
    tube_pos = core_pos + gd * (core_len * 0.5 + tube_len * 0.5)
    objects.append(make_cylinder(
        f"{prefix}_Gravity_BoreTube",
        radius=br,
        depth=tube_len,
        location=tube_pos,
        material=b_mat,
        vertices=12,
        bevel=0.01,
        rotation=rot_euler,
    ))

    # 4. Stepped concentric magnetic accelerator ring stages
    step_d = tube_len / max(1, n_rings)
    for r_idx in range(n_rings):
        ring_d = core_pos + gd * (core_len * 0.5 + (r_idx + 0.5) * step_d)
        ring_rad = br * (2.10 - 0.12 * r_idx)
        objects.append(make_cylinder(
            f"{prefix}_Gravity_AccelRing_{r_idx+1}",
            radius=ring_rad,
            depth=step_d * 0.48,
            location=ring_d,
            material=m_mat,
            vertices=12,
            bevel=0.018,
            rotation=rot_euler,
        ))
        # High-voltage dielectric warning collar
        objects.append(make_cylinder(
            f"{prefix}_Gravity_RingCollar_{r_idx+1}",
            radius=ring_rad * 1.08,
            depth=step_d * 0.16,
            location=ring_d,
            material=a_mat,
            vertices=12,
            bevel=0.008,
            rotation=rot_euler,
        ))

    # 5. Four perimeter longitudinal stabilization guide rails
    rail_start = breech_pos + gd * (breech_l * 0.48)
    rail_end = tube_pos + gd * (tube_len * 0.52)
    rail_offsets = [
        (u_vec * (br * 2.2), "Right"),
        (-u_vec * (br * 2.2), "Left"),
        (v_vec * (br * 2.2), "Top"),
        (-v_vec * (br * 2.2), "Bottom"),
    ]
    for r_off, rtag in rail_offsets:
        objects.append(make_rod(
            f"{prefix}_Gravity_StabilizerRail_{rtag}",
            rail_start + r_off,
            rail_end + r_off,
            radius=br * 0.22,
            material=b_mat,
            vertices=8,
        ))

    # 6. Muzzle crown field projector ring
    muzzle_pos = tube_pos + gd * (tube_len * 0.5 + 0.04)
    objects.append(make_cylinder(
        f"{prefix}_Gravity_MuzzleCrown",
        radius=br * 1.85,
        depth=0.12,
        location=muzzle_pos,
        material=m_mat,
        vertices=12,
        bevel=0.015,
        rotation=rot_euler,
    ))
    objects.append(make_cylinder(
        f"{prefix}_Gravity_MuzzleEmitterGlow",
        radius=br * 1.45,
        depth=0.04,
        location=muzzle_pos + gd * 0.06,
        material=c_mat,
        vertices=12,
        bevel=0.005,
        rotation=rot_euler,
    ))

    return objects


def make_nanite_repair_dispenser_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector = (0.0, 0.0, 1.0),
    scale: float = 1.0,
    canister_radius: float = 0.18,
    canister_height: float = 0.55,
    spacing_x: float = 1.8,
    canister_mat: bpy.types.Material | None = None,
    frame_mat: bpy.types.Material | None = None,
    glow_mat: bpy.types.Material | None = None,
    hose_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates bilateral armored field nanite repair dispenser pods with glowing inspection sight glasses, pressure manifolds, atomizing spray heads, and braided fluid hoses."""
    objects: list[bpy.types.Object] = []
    loc = Vector(location)
    c_mat = canister_mat or frame_mat
    f_mat = frame_mat or canister_mat
    g_mat = glow_mat or f_mat
    h_mat = hose_mat or f_mat

    s = max(0.4, scale)
    cr = canister_radius * s
    ch = canister_height * s
    sx = spacing_x * s

    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        can_x = loc.x + side * (sx * 0.5)
        can_pos = Vector((can_x, loc.y, loc.z))

        # 1. Armored mounting bracket knuckle on chassis flank
        objects.append(make_box(
            f"{prefix}_NaniteDisp_Bracket_{stag}",
            (0.12 * s, 0.22 * s, ch * 0.65),
            can_pos - Vector((side * (cr * 0.9), 0.0, 0.0)),
            f_mat,
            bevel=0.012 * s,
        ))

        # 2. Main cylindrical nanite storage vessel
        objects.append(make_cylinder(
            f"{prefix}_NaniteDisp_Tank_{stag}",
            radius=cr,
            depth=ch,
            location=can_pos,
            material=c_mat,
            vertices=12,
            bevel=0.02 * s,
        ))

        # 3. Vertical glowing fluid level sight glass
        objects.append(make_box(
            f"{prefix}_NaniteDisp_LevelGlass_{stag}",
            (cr * 0.25, cr * 0.25, ch * 0.68),
            can_pos + Vector((side * (cr * 0.85), 0.0, 0.0)),
            g_mat,
            bevel=0.005 * s,
        ))

        # 4. Top atomizing distribution manifold block & spray nozzle
        manifold_z = loc.z + ch * 0.5 + 0.06 * s
        objects.append(make_box(
            f"{prefix}_NaniteDisp_Manifold_{stag}",
            (cr * 1.5, cr * 1.5, 0.10 * s),
            Vector((can_x, loc.y, manifold_z)),
            f_mat,
            bevel=0.010 * s,
        ))
        # Forward/outward angled spray dispersal horn
        spray_dir = Vector((side * 0.5, 0.8, 0.3)).normalized()
        spray_rot = spray_dir.to_track_quat("Z", "Y").to_euler()
        objects.append(make_cylinder(
            f"{prefix}_NaniteDisp_SprayNozzle_{stag}",
            radius=cr * 0.45,
            depth=0.12 * s,
            location=Vector((can_x + side * (cr * 0.4), loc.y + cr * 0.5, manifold_z + 0.04 * s)),
            material=c_mat,
            vertices=10,
            bevel=0.008 * s,
            rotation=spray_rot,
        ))

        # 5. Flexible braided armored supply hose into hull
        hose_start = Vector((can_x, loc.y - cr * 0.5, loc.z - ch * 0.25))
        hose_end = Vector((can_x - side * (cr * 1.4), loc.y - cr * 0.8, loc.z - ch * 0.25))
        objects.append(make_rod(
            f"{prefix}_NaniteDisp_Hose_{stag}",
            hose_start,
            hose_end,
            radius=cr * 0.28,
            material=h_mat,
            vertices=8,
        ))

    return objects


def make_grav_dampener_hub_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector = (0.0, 0.0, 1.2),
    radius: float = 0.42,
    height: float = 0.28,
    housing_mat: bpy.types.Material | None = None,
    rotor_mat: bpy.types.Material | None = None,
    core_mat: bpy.types.Material | None = None,
    brace_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a chassis-mounted gyroscopic gravitational anchor and inertial dampener hub with counter-rotating magnetic rings, glowing singularity sphere, and structural tie-down braces."""
    objects: list[bpy.types.Object] = []
    loc = Vector(location)
    h_mat = housing_mat or rotor_mat
    r_mat = rotor_mat or housing_mat
    c_mat = core_mat or h_mat
    b_mat = brace_mat or h_mat

    rad = max(0.25, radius)
    h = max(0.15, height)

    # 1. Octagonal gyro base pedestal
    objects.append(make_cylinder(
        f"{prefix}_GravHub_BasePedestal",
        radius=rad * 1.15,
        depth=h * 0.35,
        location=loc + Vector((0.0, 0.0, h * 0.175)),
        material=h_mat,
        vertices=8,
        bevel=0.015,
    ))

    # 2. Dual concentric counter-rotating magnetic rotor rings
    objects.append(make_torus(
        f"{prefix}_GravHub_OuterRotor",
        location=loc + Vector((0.0, 0.0, h * 0.55)),
        major_radius=rad * 0.85,
        minor_radius=rad * 0.12,
        material=r_mat,
        major_segments=16,
        minor_segments=6,
    ))
    objects.append(make_torus(
        f"{prefix}_GravHub_InnerRotor",
        location=loc + Vector((0.0, 0.0, h * 0.55)),
        major_radius=rad * 0.55,
        minor_radius=rad * 0.10,
        material=r_mat,
        major_segments=14,
        minor_segments=6,
    ))

    # 3. Central glowing singularity containment core sphere
    objects.append(make_cylinder(
        f"{prefix}_GravHub_SingularityCore",
        radius=rad * 0.35,
        depth=h * 0.65,
        location=loc + Vector((0.0, 0.0, h * 0.55)),
        material=c_mat,
        vertices=12,
        bevel=0.02,
    ))

    # 4. Four angled stabilizing tie-down anchor struts
    corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    for idx, (cx, cy) in enumerate(corners, start=1):
        p_top = loc + Vector((cx * rad * 0.65, cy * rad * 0.65, h * 0.70))
        p_bot = loc + Vector((cx * rad * 1.25, cy * rad * 1.25, 0.02))
        objects.append(make_rod(
            f"{prefix}_GravHub_AnchorStrut_{idx}",
            p_top,
            p_bot,
            radius=rad * 0.075,
            material=b_mat,
            vertices=6,
        ))

    return objects


def make_comms_ew_mast_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector = (0.0, 0.0, 1.2),
    height: float = 1.6,
    base_mat: bpy.types.Material | None = None,
    mast_mat: bpy.types.Material | None = None,
    radome_mat: bpy.types.Material | None = None,
    glow_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a heavy-duty telescopic communications & EW jamming mast with planar high-frequency blade antenna fins, cross-dipole jamming horns, satcom dome, and glowing apex beacon LEDs."""
    objects: list[bpy.types.Object] = []
    loc = Vector(location)
    b_mat = base_mat or mast_mat
    m_mat = mast_mat or base_mat
    r_mat = radome_mat or b_mat
    g_mat = glow_mat or m_mat

    ht = max(0.8, height)

    # 1. Heavy stepped base collar on roof
    objects.append(make_cylinder(
        f"{prefix}_CommsMast_BaseCollar",
        radius=0.18,
        depth=0.14,
        location=loc + Vector((0.0, 0.0, 0.07)),
        material=b_mat,
        vertices=10,
        bevel=0.015,
    ))

    # 2. Telescopic mast tube stages (3 stages)
    stages = 3
    stage_h = (ht * 0.75) / stages
    for s_idx in range(stages):
        stage_rad = 0.09 - s_idx * 0.02
        s_z = loc.z + 0.14 + (s_idx + 0.5) * stage_h
        objects.append(make_cylinder(
            f"{prefix}_CommsMast_Stage_{s_idx+1}",
            radius=stage_rad,
            depth=stage_h,
            location=Vector((loc.x, loc.y, s_z)),
            material=m_mat,
            vertices=8,
            bevel=0.005,
        ))

    # 3. Bilateral high-frequency planar blade antenna fins
    blade_z = loc.z + 0.14 + stage_h * 1.5
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        objects.append(make_box(
            f"{prefix}_CommsMast_BladeAntenna_{stag}",
            (0.02, 0.22, 0.32),
            Vector((loc.x + side * 0.12, loc.y, blade_z)),
            b_mat,
            bevel=0.006,
        ))

    # 4. Cross-dipole electronic warfare jamming horns (mid-high)
    ew_z = loc.z + 0.14 + stage_h * 2.3
    for angle_deg in (45.0, 135.0):
        rad = math.radians(angle_deg)
        objects.append(make_box(
            f"{prefix}_CommsMast_EWHorn_{int(angle_deg)}",
            (0.38, 0.04, 0.04),
            Vector((loc.x, loc.y, ew_z)),
            m_mat,
            bevel=0.005,
            rotation=(0.0, 0.0, rad),
        ))

    # 5. Apex satcom teardrop radome & omni beacon LEDs
    apex_z = loc.z + 0.14 + ht * 0.75 + 0.12
    objects.append(make_cylinder(
        f"{prefix}_CommsMast_SatcomDome",
        radius=0.22,
        depth=0.18,
        location=Vector((loc.x, loc.y, apex_z)),
        material=r_mat,
        vertices=12,
        bevel=0.025,
    ))
    # Glowing apex beacon LEDs
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        objects.append(make_cylinder(
            f"{prefix}_CommsMast_BeaconLED_{stag}",
            radius=0.032,
            depth=0.04,
            location=Vector((loc.x + side * 0.14, loc.y, apex_z + 0.08)),
            material=g_mat,
            vertices=8,
            bevel=0.004,
        ))

    return objects


def make_reinforced_ram_prow_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector = (0.0, 1.8, 0.4),
    width: float = 2.4,
    height: float = 0.55,
    forward_reach: float = 0.45,
    prow_mat: bpy.types.Material | None = None,
    bracket_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a heavy angled V-shaped kinetic battering ram prow / cowcatcher bumper with structural reinforcing gusset ribs, forged towing shackles, and diagonal hazard chevron plates."""
    objects: list[bpy.types.Object] = []
    loc = Vector(location)
    p_mat = prow_mat or bracket_mat
    b_mat = bracket_mat or prow_mat
    a_mat = accent_mat or p_mat

    w = max(1.2, width)
    h = max(0.3, height)
    reach = max(0.25, forward_reach)

    # 1. Bilateral angled V-prow kinetic blade wings
    wing_w = w * 0.55
    v_angle = math.radians(24.0)
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        wing_center = loc + Vector((side * (wing_w * 0.42), reach * 0.45, 0.0))
        wing_rot = (0.0, 0.0, -side * v_angle)
        objects.append(make_box(
            f"{prefix}_RamProw_Blade_{stag}",
            (wing_w, 0.14, h),
            wing_center,
            p_mat,
            bevel=0.02,
            rotation=wing_rot,
        ))
        # High-contrast hazard chevron accent plate along blade top
        objects.append(make_box(
            f"{prefix}_RamProw_HazardStripe_{stag}",
            (wing_w * 0.85, 0.04, h * 0.28),
            wing_center + Vector((0.0, 0.06, h * 0.22)),
            a_mat,
            bevel=0.005,
            rotation=wing_rot,
        ))

    # 2. Central vertical prow wedge apex nose
    apex_pos = loc + Vector((0.0, reach * 0.85, 0.0))
    objects.append(make_box(
        f"{prefix}_RamProw_ApexWedge",
        (0.18, 0.24, h * 1.08),
        apex_pos,
        b_mat,
        bevel=0.025,
    ))

    # 3. Four structural gusset ribs anchoring prow to hull glacis
    gusset_x_offsets = [-w * 0.35, -w * 0.12, w * 0.12, w * 0.35]
    for idx, gx in enumerate(gusset_x_offsets, start=1):
        g_start = Vector((loc.x + gx, loc.y, loc.z))
        g_end = Vector((loc.x + gx, loc.y - 0.35, loc.z + h * 0.35))
        objects.append(make_rod(
            f"{prefix}_RamProw_GussetRib_{idx}",
            g_start,
            g_end,
            radius=0.042,
            material=b_mat,
            vertices=6,
        ))

    # 4. Bilateral heavy forged towing shackles / D-rings on lower prow
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        shackle_pos = loc + Vector((side * (w * 0.32), reach * 0.25, -h * 0.32))
        objects.append(make_torus(
            f"{prefix}_RamProw_TowingShackle_{stag}",
            location=shackle_pos,
            major_radius=0.085,
            minor_radius=0.028,
            material=b_mat,
            major_segments=12,
            minor_segments=6,
        ))

    return objects


def make_cast_pelvis_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector = (0.0, 0.0, 1.40),
    width: float = 0.95,
    depth: float = 0.68,
    height: float = 0.45,
    core_mat: bpy.types.Material | None = None,
    armor_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an articulated heavy cast pelvis gimbal assembly with bilateral hip clevis actuator knuckles and lower groin ballistic deflector plate."""
    objects = []
    loc = Vector(location)
    c_mat = core_mat or armor_mat
    a_mat = armor_mat or core_mat
    ac_mat = accent_mat or a_mat

    w = max(0.6, width)
    d = max(0.4, depth)
    h = max(0.3, height)

    # 1. Heavy central waist gimbal housing
    waist_rad = min(w, d) * 0.42
    objects.append(make_cylinder(
        f"{prefix}_WaistGimbal",
        radius=waist_rad,
        depth=h * 0.65,
        location=loc,
        material=c_mat,
        vertices=12,
        bevel=0.02,
    ))

    # 2. Bilateral heavy forged hip clevis pivot brackets
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        hip_x = side * (w * 0.46)
        objects.append(make_box(
            f"{prefix}_HipClevis_{stag}",
            (w * 0.26, d * 0.58, h * 0.72),
            loc + Vector((hip_x, -d * 0.04, -h * 0.18)),
            a_mat,
            bevel=0.025,
        ))
        # Cross-pin axle boss
        objects.append(make_cylinder(
            f"{prefix}_HipAxle_{stag}",
            radius=h * 0.18,
            depth=w * 0.32,
            location=loc + Vector((hip_x, -d * 0.04, -h * 0.18)),
            material=c_mat,
            vertices=10,
            bevel=0.01,
            rotation=(0.0, math.radians(90), 0.0),
        ))

    # 3. Lower groin chevron deflection plate
    objects.append(make_box(
        f"{prefix}_GroinPlate",
        (w * 0.48, d * 0.42, h * 0.40),
        loc + Vector((0.0, d * 0.32, -h * 0.35)),
        a_mat,
        bevel=0.02,
        rotation=(math.radians(35), 0.0, 0.0),
    ))

    # 4. Rear chassis hitch / towing anchor block
    objects.append(make_box(
        f"{prefix}_RearTowingBlock",
        (w * 0.52, d * 0.28, h * 0.38),
        loc + Vector((0.0, -d * 0.45, -h * 0.15)),
        c_mat,
        bevel=0.018,
    ))

    return objects


def make_articulated_mech_torso(
    prefix: str,
    location: Tuple[float, float, float] | Vector = (0.0, 0.0, 1.95),
    width: float = 1.15,
    length: float = 0.85,
    height: float = 0.82,
    primary_mat: bpy.types.Material | None = None,
    secondary_mat: bpy.types.Material | None = None,
    gunmetal_mat: bpy.types.Material | None = None,
    emissive_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an authentic faceted monocoque walker/mech torso with sloped chest glacis, armored visor cowl, shoulder pauldron cuffs, and dorsal reactor bay."""
    objects = []
    loc = Vector(location)
    p_mat = primary_mat or secondary_mat
    s_mat = secondary_mat or primary_mat
    g_mat = gunmetal_mat or p_mat
    e_mat = emissive_mat or s_mat

    w = max(0.8, width)
    l = max(0.6, length)
    h = max(0.6, height)
    hw, hl, hh = w * 0.5, l * 0.5, h * 0.5

    # 1. Monocoque Tapered Torso Shell (12-vertex faceted hull)
    v_bot = [
        Vector((loc.x - hw * 0.55, loc.y + hl * 0.65, loc.z - hh)),
        Vector((loc.x - hw * 0.88, loc.y - hl * 0.10, loc.z - hh)),
        Vector((loc.x - hw * 0.72, loc.y - hl * 0.85, loc.z - hh)),
        Vector((loc.x + hw * 0.72, loc.y - hl * 0.85, loc.z - hh)),
        Vector((loc.x + hw * 0.88, loc.y - hl * 0.10, loc.z - hh)),
        Vector((loc.x + hw * 0.55, loc.y + hl * 0.65, loc.z - hh)),
    ]
    v_top = [
        Vector((loc.x - hw * 0.42, loc.y + hl * 0.45, loc.z + hh * 0.75)),
        Vector((loc.x - hw * 0.78, loc.y - hl * 0.15, loc.z + hh)),
        Vector((loc.x - hw * 0.60, loc.y - hl * 0.75, loc.z + hh * 0.88)),
        Vector((loc.x + hw * 0.60, loc.y - hl * 0.75, loc.z + hh * 0.88)),
        Vector((loc.x + hw * 0.78, loc.y - hl * 0.15, loc.z + hh)),
        Vector((loc.x + hw * 0.42, loc.y + hl * 0.45, loc.z + hh * 0.75)),
    ]
    all_verts = [v[:] for v in v_bot + v_top]
    faces = [(5, 4, 3, 2, 1, 0), (6, 7, 8, 9, 10, 11)]
    for i in range(6):
        ni = (i + 1) % 6
        faces.append((i, ni, ni + 6, i + 6))

    mesh = bpy.data.meshes.new(f"{prefix}_TorsoMonocoque_Mesh")
    mesh.from_pydata(all_verts, [], faces)
    mesh.update()
    torso_obj = bpy.data.objects.new(f"{prefix}_TorsoMonocoque", mesh)
    bpy.context.collection.objects.link(torso_obj)
    bpy.context.view_layer.objects.active = torso_obj
    torso_obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    torso_obj = finish_mesh(torso_obj, p_mat, bevel=0.035, smooth=True, bevel_segments=2)
    objects.append(torso_obj)

    # 2. Sloped Forward Chest Glacis Plate
    chest_pos = loc + Vector((0.0, hl * 0.55, hh * 0.15))
    objects.append(make_box(
        f"{prefix}_ChestGlacis",
        (w * 0.82, l * 0.28, h * 0.55),
        chest_pos,
        s_mat,
        bevel=0.025,
        rotation=(math.radians(-20), 0.0, 0.0),
        bevel_segments=2,
        smooth=True,
    ))

    # 3. Armored Visor Cowl with Optical Sight Bar
    cowl_pos = loc + Vector((0.0, hl * 0.38, hh * 0.65))
    objects.append(make_box(
        f"{prefix}_VisorCowl",
        (w * 0.65, l * 0.35, h * 0.22),
        cowl_pos,
        p_mat,
        bevel=0.018,
        rotation=(math.radians(-14), 0.0, 0.0),
    ))
    objects.append(make_box(
        f"{prefix}_VisorOpticGlass",
        (w * 0.52, 0.05, 0.07),
        cowl_pos + Vector((0.0, l * 0.14, -0.02)),
        e_mat,
        bevel=0.005,
        rotation=(math.radians(-14), 0.0, 0.0),
    ))

    # 4. Bilateral Shoulder Pauldron Mounting Hardpoints
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        sh_pos = loc + Vector((side * (hw * 0.95), -hl * 0.08, hh * 0.45))
        objects.append(make_cylinder(
            f"{prefix}_ShoulderJoint_{stag}",
            radius=h * 0.22,
            depth=w * 0.28,
            location=sh_pos,
            material=g_mat,
            vertices=12,
            bevel=0.015,
            rotation=(0.0, math.radians(90), 0.0),
        ))
        # Armored Pauldron Guard
        objects.append(make_box(
            f"{prefix}_ShoulderPauldron_{stag}",
            (w * 0.32, l * 0.68, h * 0.48),
            sh_pos + Vector((side * (w * 0.10), 0.0, hh * 0.18)),
            p_mat,
            bevel=0.028,
            rotation=(0.0, side * math.radians(14), 0.0),
        ))

    # 5. Dorsal Power Reactor Core Bay with Cooling Stacks
    core_pos = loc + Vector((0.0, -hl * 0.72, hh * 0.10))
    objects.append(make_box(
        f"{prefix}_ReactorCoreBay",
        (w * 0.78, l * 0.48, h * 0.78),
        core_pos,
        g_mat,
        bevel=0.025,
    ))
    # Angled cooling exhaust stacks
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        stack_pos = core_pos + Vector((side * (w * 0.24), -l * 0.18, hh * 0.45))
        objects.append(make_cylinder(
            f"{prefix}_CoolingStack_{stag}",
            radius=0.09,
            depth=h * 0.55,
            location=stack_pos,
            material=g_mat,
            vertices=10,
            bevel=0.01,
            rotation=(math.radians(22), 0.0, 0.0),
        ))

    return objects


def make_sculpted_wheel_fender_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    radius: float = 0.40,
    width: float = 0.28,
    side: int = 1,
    fender_mat: bpy.types.Material | None = None,
    skirt_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a contoured hard-surface wheel arch fender that hugs all-terrain tires with mudguard deflector lips."""
    objects = []
    loc = Vector(location)
    f_mat = fender_mat or skirt_mat
    s_mat = skirt_mat or fender_mat
    r = max(0.20, radius)
    w = max(0.15, width)

    # 1. Contoured Top Fender Arch (covers upper half of wheel)
    arch_r = r * 1.15
    fender_w = w * 1.25
    fender_h = r * 0.45
    objects.append(make_box(
        f"{prefix}_ArchTop",
        (fender_w, r * 1.55, fender_h * 0.45),
        loc + Vector((0.0, 0.0, r * 1.05)),
        f_mat,
        bevel=0.018,
    ))

    # 2. Sloped Forward Deflector Lip
    objects.append(make_box(
        f"{prefix}_FrontMudflap",
        (fender_w, r * 0.55, fender_h * 0.35),
        loc + Vector((0.0, r * 0.95, r * 0.75)),
        s_mat,
        bevel=0.012,
        rotation=(math.radians(35), 0.0, 0.0),
    ))

    # 3. Rear Splash Guard Flap
    objects.append(make_box(
        f"{prefix}_RearMudflap",
        (fender_w, r * 0.55, fender_h * 0.35),
        loc + Vector((0.0, -r * 0.95, r * 0.75)),
        s_mat,
        bevel=0.012,
        rotation=(math.radians(-35), 0.0, 0.0),
    ))

    # 4. Outer Chamfered Flare Skirt Lip
    flare_x = side * (fender_w * 0.52)
    objects.append(make_box(
        f"{prefix}_FlareLip",
        (0.04, r * 1.95, fender_h * 0.55),
        loc + Vector((flare_x, 0.0, r * 0.88)),
        f_mat,
        bevel=0.01,
        rotation=(0.0, side * math.radians(12), 0.0),
    ))

    return objects


def make_weld_seam_bead(
    name: str,
    start: Tuple[float, float, float] | Vector,
    end: Tuple[float, float, float] | Vector,
    radius: float = 0.016,
    bead_spacing: float = 0.032,
    material: bpy.types.Material | None = None,
) -> bpy.types.Object | None:
    """Generates an authentic scalloped military weld seam bead chain between start and end coordinates."""
    p1 = Vector(start)
    p2 = Vector(end)
    diff = p2 - p1
    length = diff.length
    if length < 0.001:
        return None

    direction = diff.normalized()
    bead_count = max(3, int(length / bead_spacing))
    actual_spacing = length / bead_count
    rot_quat = direction.to_track_quat("Z", "Y")

    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    bm = bmesh.new()

    for i in range(bead_count + 1):
        pos = p1 + direction * (i * actual_spacing)
        matrix = rot_quat.to_matrix().to_4x4()
        matrix.translation = pos
        tilt = Euler((0.14 * (1.0 if i % 2 == 0 else -0.14), 0.0, 0.0)).to_matrix().to_4x4()
        full_mat = matrix @ tilt
        bmesh.ops.create_cone(
            bm,
            cap_ends=True,
            cap_tris=False,
            segments=6,
            radius1=radius,
            radius2=radius * 0.45,
            depth=actual_spacing * 0.90,
            matrix=full_mat,
        )

    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    if material:
        obj.data.materials.append(material)

    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    if hasattr(bpy.ops.object, "shade_smooth_by_angle"):
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(45.0))
    return obj


def make_glacis_splash_rib(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    width: float = 1.20,
    depth: float = 0.08,
    height: float = 0.07,
    sweep_angle_deg: float = 24.0,
    material: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an authentic chevron / V-shaped ballistic splash rib welded to the glacis plate."""
    objects = []
    loc_v = Vector(location)
    sweep_rad = math.radians(sweep_angle_deg)
    half_w = width * 0.5
    for side in (-1, 1):
        side_tag = "L" if side < 0 else "R"
        arm_len = half_w / math.cos(sweep_rad)
        arm_rot = (0.0, 0.0, -side * sweep_rad)
        arm_mid = loc_v + Vector((side * half_w * 0.5, -math.sin(sweep_rad) * half_w * 0.25, 0.0))
        objects.append(make_box(
            f"{prefix}_SplashRib_{side_tag}",
            (depth, arm_len * 0.95, height),
            arm_mid,
            material,
            bevel=0.012,
            rotation=arm_rot,
        ))
    objects.append(make_box(
        f"{prefix}_SplashRib_Apex",
        (depth * 1.5, depth * 1.5, height * 1.15),
        loc_v + Vector((0.0, 0.02, 0.0)),
        material,
        bevel=0.012,
    ))
    return objects


def make_bustle_stowage_basket(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    size: Tuple[float, float, float] = (1.10, 0.42, 0.24),
    rail_radius: float = 0.018,
    frame_mat: bpy.types.Material | None = None,
    mesh_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a welded tubular steel equipment stowage basket mounted to the rear turret bustle."""
    objects = []
    loc_v = Vector(location)
    w, l, h = size
    hw = w * 0.5
    hl = l * 0.5
    hh = h * 0.5

    objects.append(make_box(
        f"{prefix}_BasketFloor",
        (w * 0.95, l * 0.95, 0.025),
        loc_v - Vector((0.0, 0.0, hh - 0.015)),
        mesh_mat or frame_mat,
        bevel=0.005,
    ))

    for z_off, z_tag in [(-hh + 0.02, "Lower"), (hh, "Upper")]:
        p_z = loc_v.z + z_off
        objects.append(make_rod(
            f"{prefix}_Rail_{z_tag}_Rear",
            Vector((loc_v.x - hw, loc_v.y - hl, p_z)),
            Vector((loc_v.x + hw, loc_v.y - hl, p_z)),
            rail_radius, frame_mat, vertices=8
        ))
        for side, s_tag in [(-1, "L"), (1, "R")]:
            objects.append(make_rod(
                f"{prefix}_Rail_{z_tag}_{s_tag}",
                Vector((loc_v.x + side * hw, loc_v.y + hl, p_z)),
                Vector((loc_v.x + side * hw, loc_v.y - hl, p_z)),
                rail_radius, frame_mat, vertices=8
            ))

    for x_sign, x_tag in [(-1, "L"), (1, "R")]:
        for y_sign, y_tag in [(-1, "Rear"), (1, "Front")]:
            objects.append(make_rod(
                f"{prefix}_Post_{x_tag}_{y_tag}",
                Vector((loc_v.x + x_sign * hw, loc_v.y + y_sign * hl, loc_v.z - hh + 0.02)),
                Vector((loc_v.x + x_sign * hw, loc_v.y + y_sign * hl, loc_v.z + hh)),
                rail_radius, frame_mat, vertices=8
            ))

    for side in (-1, 1):
        s_tag = "L" if side < 0 else "R"
        objects.append(make_box(
            f"{prefix}_MountBracket_{s_tag}",
            (0.04, 0.12, 0.08),
            loc_v + Vector((side * hw * 0.65, hl + 0.05, 0.0)),
            frame_mat,
            bevel=0.006,
        ))
    return objects


def make_panoramic_commander_sight(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    size: Tuple[float, float, float] = (0.28, 0.26, 0.32),
    armor_mat: bpy.types.Material | None = None,
    lens_mat: bpy.types.Material | None = None,
    metal_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a 360-degree rotating panoramic commander independent thermal viewer (CITV)."""
    objects = []
    loc_v = Vector(location)
    w, l, h = size

    objects.append(make_cylinder(
        f"{prefix}_RotaryRing",
        w * 0.45, 0.06,
        loc_v + Vector((0.0, 0.0, 0.03)),
        metal_mat or armor_mat,
        vertices=12, bevel=0.008
    ))

    head_z = loc_v.z + 0.06 + h * 0.5
    objects.append(make_box(
        f"{prefix}_OpticHead",
        (w, l, h * 0.85),
        Vector((loc_v.x, loc_v.y, head_z)),
        armor_mat,
        bevel=0.02,
    ))

    objects.append(make_box(
        f"{prefix}_HoodBrow",
        (w * 0.88, l * 0.35, 0.03),
        Vector((loc_v.x, loc_v.y + l * 0.35, head_z + h * 0.38)),
        armor_mat,
        bevel=0.005,
        rotation=(math.radians(15.0), 0.0, 0.0),
    ))

    objects.append(make_cylinder(
        f"{prefix}_ThermalLens",
        w * 0.22, 0.04,
        Vector((loc_v.x - w * 0.18, loc_v.y + l * 0.48, head_z)),
        lens_mat,
        rotation=(math.radians(90.0), 0.0, 0.0),
        vertices=10, bevel=0.004
    ))
    objects.append(make_cylinder(
        f"{prefix}_LaserAperture",
        w * 0.12, 0.04,
        Vector((loc_v.x + w * 0.22, loc_v.y + l * 0.48, head_z + 0.03)),
        lens_mat,
        rotation=(math.radians(90.0), 0.0, 0.0),
        vertices=8, bevel=0.003
    ))
    return objects


def make_recovery_winch_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    size: Tuple[float, float, float] = (0.55, 0.32, 0.24),
    housing_mat: bpy.types.Material | None = None,
    drum_mat: bpy.types.Material | None = None,
    roller_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a heavy front recovery winch with fairlead guide rollers and steel cable drum."""
    objects = []
    loc_v = Vector(location)
    w, l, h = size

    objects.append(make_box(
        f"{prefix}_Housing",
        (w, l * 0.65, h),
        loc_v - Vector((0.0, l * 0.15, 0.0)),
        housing_mat,
        bevel=0.015,
    ))

    objects.append(make_cylinder(
        f"{prefix}_CableDrum",
        h * 0.38, w * 0.68,
        loc_v - Vector((0.0, l * 0.10, 0.0)),
        drum_mat or housing_mat,
        rotation=(0.0, math.radians(90.0), 0.0),
        vertices=12, bevel=0.008
    ))

    fl_y = loc_v.y + l * 0.32
    objects.append(make_box(
        f"{prefix}_FairleadFrame",
        (w * 0.72, 0.04, h * 0.68),
        Vector((loc_v.x, fl_y, loc_v.z)),
        housing_mat,
        bevel=0.008,
    ))
    for z_side in (-1, 1):
        objects.append(make_rod(
            f"{prefix}_RollerH_{'B' if z_side < 0 else 'T'}",
            Vector((loc_v.x - w * 0.28, fl_y + 0.03, loc_v.z + z_side * h * 0.25)),
            Vector((loc_v.x + w * 0.28, fl_y + 0.03, loc_v.z + z_side * h * 0.25)),
            0.018, roller_mat or housing_mat, vertices=8
        ))
    for x_side in (-1, 1):
        objects.append(make_rod(
            f"{prefix}_RollerV_{'L' if x_side < 0 else 'R'}",
            Vector((loc_v.x + x_side * w * 0.28, fl_y + 0.03, loc_v.z - h * 0.25)),
            Vector((loc_v.x + x_side * w * 0.28, fl_y + 0.03, loc_v.z + h * 0.25)),
            0.018, roller_mat or housing_mat, vertices=8
        ))
    return objects


def make_auxiliary_fuel_drum_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    drum_radius: float = 0.18,
    drum_length: float = 0.72,
    spacing_x: float = 0.85,
    drum_mat: bpy.types.Material | None = None,
    saddle_mat: bpy.types.Material | None = None,
    strap_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates dual cylindrical auxiliary fuel drums with stamped steel mounting saddles."""
    objects = []
    loc_v = Vector(location)

    for side in (-1, 1):
        side_tag = "L" if side < 0 else "R"
        d_x = side * spacing_x * 0.5
        drum_center = loc_v + Vector((d_x, 0.0, 0.0))

        # Stamped mounting cradle saddle
        objects.append(make_box(
            f"{prefix}_FuelDrumSaddle_{side_tag}",
            (0.12, drum_radius * 2.2, drum_radius * 1.4),
            drum_center + Vector((0.0, drum_radius * 0.35, -drum_radius * 0.45)),
            saddle_mat,
            bevel=0.012,
            rotation=(math.radians(22.0), 0.0, 0.0),
        ))

        # Cylindrical fuel tank barrel (transverse X-oriented)
        objects.append(make_cylinder(
            f"{prefix}_FuelDrum_{side_tag}",
            drum_radius, drum_length,
            drum_center,
            drum_mat,
            rotation=(0.0, math.radians(90.0), 0.0),
            vertices=14, bevel=0.015,
        ))

        # Stamped end caps with recessed rim
        for cap_side in (-1, 1):
            cap_tag = "Out" if (cap_side * side) > 0 else "In"
            objects.append(make_cylinder(
                f"{prefix}_FuelDrumCap_{side_tag}_{cap_tag}",
                drum_radius * 0.88, 0.03,
                drum_center + Vector((cap_side * drum_length * 0.51, 0.0, 0.0)),
                saddle_mat,
                rotation=(0.0, math.radians(90.0), 0.0),
                vertices=12, bevel=0.006,
            ))

        # Retention steel tension straps
        for strap_x in (-drum_length * 0.28, drum_length * 0.28):
            objects.append(make_cylinder(
                f"{prefix}_FuelDrumStrap_{side_tag}_{'1' if strap_x < 0 else '2'}",
                drum_radius * 1.04, 0.035,
                drum_center + Vector((strap_x, 0.0, 0.0)),
                strap_mat or saddle_mat,
                rotation=(0.0, math.radians(90.0), 0.0),
                vertices=14, bevel=0.005,
            ))

    # Central armored fuel manifold umbilical conduit
    objects.append(make_rod(
        f"{prefix}_FuelManifold",
        loc_v + Vector((-spacing_x * 0.25, drum_radius * 0.6, 0.0)),
        loc_v + Vector((spacing_x * 0.25, drum_radius * 0.6, 0.0)),
        0.022, saddle_mat, vertices=8,
    ))
    return objects


def make_reactive_armor_array(
    prefix: str,
    locations: list[Tuple[float, float, float] | Vector],
    size: Tuple[float, float, float] = (0.28, 0.16, 0.06),
    tilt_deg: float = 18.0,
    brick_mat: bpy.types.Material | None = None,
    bracket_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an array of Explosive Reactive Armor (ERA) tiles on welded mounting standoffs."""
    objects = []
    w, l, h = size
    tilt_rad = math.radians(tilt_deg)
    for idx, loc in enumerate(locations, start=1):
        loc_v = Vector(loc)
        # ERA cassette brick
        objects.append(make_box(
            f"{prefix}_ERA_Brick_{idx:02d}",
            (w, l, h),
            loc_v,
            brick_mat,
            bevel=0.008,
            rotation=(tilt_rad, 0.0, 0.0),
        ))
        # Welded standoff mounting plate
        objects.append(make_box(
            f"{prefix}_ERA_Standoff_{idx:02d}",
            (w * 0.75, l * 0.75, 0.02),
            loc_v - Vector((0.0, 0.0, h * 0.65)),
            bracket_mat or brick_mat,
            bevel=0.004,
        ))
    return objects


def make_exhaust_deflector_cowl(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    size: Tuple[float, float, float] = (0.42, 0.32, 0.14),
    slat_count: int = 4,
    frame_mat: bpy.types.Material | None = None,
    slat_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a heavy armored exhaust deflector cowl with directional heat louvers."""
    objects = []
    loc_v = Vector(location)
    w, l, h = size

    # Shroud hood frame
    objects.append(make_box(
        f"{prefix}_DeflectorHood",
        (w, l, h * 0.5),
        loc_v + Vector((0.0, 0.0, h * 0.25)),
        frame_mat,
        bevel=0.012,
        rotation=(math.radians(-12.0), 0.0, 0.0),
    ))

    # Angled directional diffuser slats
    slat_spacing = l / (slat_count + 1)
    for idx in range(slat_count):
        s_y = -l * 0.5 + (idx + 1) * slat_spacing
        objects.append(make_box(
            f"{prefix}_DeflectorSlat_{idx + 1}",
            (w * 0.88, 0.025, h * 0.75),
            loc_v + Vector((0.0, s_y, 0.0)),
            slat_mat or frame_mat,
            bevel=0.004,
            rotation=(math.radians(35.0), 0.0, 0.0),
        ))
    return objects

def make_interlocking_armor_gusset(
    prefix: str,
    locations: Sequence[Tuple[float, float, float] | Vector],
    size: Tuple[float, float, float] = (0.05, 0.22, 0.22),
    material: bpy.types.Material | None = None,
    rotation: Tuple[float, float, float] = (0.0, 0.0, 0.0),
) -> list[bpy.types.Object]:
    """Generates welded triangular structural web gussets reinforcing 90-degree hull/superstructure joints."""
    objects = []
    w, l, h = size
    hw = w * 0.5
    for idx, loc in enumerate(locations, start=1):
        mesh = bpy.data.meshes.new(f"{prefix}_Gusset_{idx}_Mesh")
        bm = bmesh.new()
        v0 = bm.verts.new((-hw, 0.0, 0.0))
        v1 = bm.verts.new((hw, 0.0, 0.0))
        v2 = bm.verts.new((hw, l, 0.0))
        v3 = bm.verts.new((-hw, l, 0.0))
        v4 = bm.verts.new((-hw, 0.0, h))
        v5 = bm.verts.new((hw, 0.0, h))
        bm.faces.new((v0, v1, v2, v3))
        bm.faces.new((v0, v4, v5, v1))
        bm.faces.new((v1, v5, v2))
        bm.faces.new((v0, v3, v4))
        bm.faces.new((v4, v3, v2, v5))
        bm.to_mesh(mesh)
        bm.free()
        obj = bpy.data.objects.new(f"{prefix}_Gusset_{idx}", mesh)
        if material is not None:
            obj.data.materials.append(material)
        obj.location = Vector(loc)
        obj.rotation_euler = rotation
        bpy.context.collection.objects.link(obj)
        objects.append(obj)
    return objects


def make_towing_shackle_assembly(
    prefix: str,
    locations: Sequence[Tuple[float, float, float] | Vector],
    material: bpy.types.Material | None = None,
    pin_material: bpy.types.Material | None = None,
    shackle_size: float = 0.12,
    direction: Tuple[float, float, float] | Vector = (0.0, 1.0, 0.0),
) -> list[bpy.types.Object]:
    """Generates heavy forged towing clevis shackles with cross-pins mounted on vehicle armor."""
    objects = []
    dir_v = Vector(direction).normalized()
    for idx, loc in enumerate(locations, start=1):
        loc_v = Vector(loc)
        # 1. Welded mounting boss
        objects.append(make_box(
            f"{prefix}_ShackleBracket_{idx}",
            (shackle_size * 0.85, 0.05, shackle_size * 0.85),
            loc_v,
            material,
            bevel=0.012,
        ))
        # 2. Transverse clevis pin
        pin_rot = (0.0, math.radians(90.0), 0.0)
        objects.append(make_cylinder(
            f"{prefix}_ShacklePin_{idx}",
            radius=shackle_size * 0.18,
            depth=shackle_size * 1.15,
            location=loc_v + dir_v * 0.04,
            material=pin_material or material,
            rotation=pin_rot,
            vertices=8,
            bevel=0.005,
        ))
        # 3. Hanging D-ring shackle loop
        objects.append(make_box(
            f"{prefix}_ShackleLoop_{idx}",
            (shackle_size * 0.92, 0.035, shackle_size * 0.95),
            loc_v + dir_v * 0.07 - Vector((0.0, 0.0, shackle_size * 0.35)),
            material,
            bevel=0.015,
            rotation=(math.radians(18.0), 0.0, 0.0),
        ))
    return objects


def make_crane_lifting_lugs(
    prefix: str,
    corner_locations: Sequence[Tuple[float, float, float] | Vector],
    material: bpy.types.Material | None = None,
    lug_radius: float = 0.065,
    thickness: float = 0.035,
) -> list[bpy.types.Object]:
    """Generates forged circular crane lifting eye lugs welded to upper hull or turret corners."""
    objects = []
    for idx, loc in enumerate(corner_locations, start=1):
        loc_v = Vector(loc)
        lug = make_cylinder(
            f"{prefix}_LiftingLug_{idx}",
            radius=lug_radius,
            depth=thickness,
            location=loc_v,
            material=material,
            rotation=(0.0, math.radians(90.0), 0.0),
            vertices=10,
            bevel=0.008,
        )
        objects.append(lug)
        objects.append(make_box(
            f"{prefix}_LugWeldPad_{idx}",
            (thickness * 1.5, lug_radius * 1.6, 0.03),
            loc_v - Vector((0.0, 0.0, lug_radius * 0.7)),
            material,
            bevel=0.005,
        ))
    return objects


def make_aerodynamic_fuselage_assembly(
    prefix: str,
    length: float = 2.6,
    width: float = 1.35,
    height: float = 0.58,
    location: Tuple[float, float, float] | Vector = (0.0, 0.10, 0.72),
    primary_mat: bpy.types.Material | None = None,
    canopy_mat: bpy.types.Material | None = None,
    intake_mat: bpy.types.Material | None = None,
    exhaust_mat: bpy.types.Material | None = None,
    with_wing_fairings: bool = True,
    wing_span_x: float = 1.5,
) -> list[bpy.types.Object]:
    """Generates an aerodynamic compound fuselage monocoque with faceted cockpit, intakes, and wing root fillets."""
    objects = []
    loc = Vector(location)

    # 1. Main fuselage monocoque with tumblehome upper shoulder chamfers
    fuselage = make_box(
        f"{prefix}_Fuselage",
        (width, length, height),
        (loc.x, loc.y, loc.z),
        primary_mat,
        bevel=0.045,
    )
    objects.append(fuselage)

    # 2. Sloped aerodynamic cockpit nose
    nose_len = length * 0.35
    nose_w = width * 0.78
    nose_h = height * 0.72
    nose_y = loc.y + length * 0.5 + nose_len * 0.42
    nose_z = loc.z - height * 0.08
    cockpit_nose = make_box(
        f"{prefix}_CockpitNose",
        (nose_w, nose_len, nose_h),
        (loc.x, nose_y, nose_z),
        canopy_mat or primary_mat,
        bevel=0.035,
        rotation=(math.radians(-16), 0.0, 0.0),
    )
    objects.append(cockpit_nose)

    # 3. Faceted cockpit canopy glass
    canopy_w = nose_w * 0.72
    canopy_len = nose_len * 0.65
    canopy_h = nose_h * 0.45
    objects.append(make_box(
        f"{prefix}_CanopyGlass",
        (canopy_w, canopy_len, canopy_h),
        (loc.x, nose_y - nose_len * 0.05, nose_z + nose_h * 0.48),
        canopy_mat or primary_mat,
        bevel=0.02,
        rotation=(math.radians(-12), 0.0, 0.0),
    ))

    # 4. Bilateral jet/lift-fan intake scoops with splitter plates
    intake_w = width * 0.22
    intake_len = length * 0.38
    intake_h = height * 0.55
    for side in (-1, 1):
        side_tag = "L" if side < 0 else "R"
        ix = loc.x + side * (width * 0.5 + intake_w * 0.35)
        iy = loc.y + length * 0.08
        iz = loc.z
        objects.append(make_box(
            f"{prefix}_IntakeScoop_{side_tag}",
            (intake_w, intake_len, intake_h),
            (ix, iy, iz),
            intake_mat or primary_mat,
            bevel=0.025,
            rotation=(0.0, 0.0, math.radians(side * -5)),
        ))
        objects.append(make_box(
            f"{prefix}_IntakeSplitter_{side_tag}",
            (0.02, intake_len * 0.95, intake_h * 0.85),
            (ix + side * intake_w * 0.25, iy, iz),
            exhaust_mat or intake_mat or primary_mat,
            bevel=0.008,
        ))

    # 5. Wing root fairing fillets (bridging fuselage to wings)
    if with_wing_fairings:
        fairing_w = width * 0.28
        fairing_len = length * 0.48
        fairing_h = height * 0.25
        for side in (-1, 1):
            side_tag = "L" if side < 0 else "R"
            fx = loc.x + side * (width * 0.5 + fairing_w * 0.35)
            fy = loc.y - length * 0.05
            fz = loc.z + height * 0.22
            objects.append(make_box(
                f"{prefix}_WingRootFairing_{side_tag}",
                (fairing_w, fairing_len, fairing_h),
                (fx, fy, fz),
                primary_mat,
                bevel=0.02,
                rotation=(0.0, math.radians(side * 12), 0.0),
            ))

    return objects


def make_transitional_coaming_collar(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    size: Tuple[float, float],
    height: float = 0.045,
    collar_mat: bpy.types.Material | None = None,
) -> bpy.types.Object:
    """Generates a beveled perimeter mounting coaming / weld collar along the base of a cabin, cowl, or equipment bay where it mounts to the chassis, bridging sharp 90° intersections."""
    loc = Vector(location)
    sx, sy = size
    h = max(0.02, height)
    return make_box(
        f"{prefix}_CoamingCollar",
        (sx * 1.08, sy * 1.08, h),
        loc - Vector((0.0, 0.0, h * 0.45)),
        collar_mat,
        bevel=h * 0.75,
        bevel_segments=2,
        smooth=True,
    )



def make_turret_ring_guard(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    radius: float = 0.70,
    height: float = 0.14,
    thickness: float = 0.075,
    armor_mat: bpy.types.Material | None = None,
    gunmetal_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an authentic circular armored shot-trap deflector collar / yaw-race coaming ring around the turret base.

    Features an outer beveled kinetic deflector collar, stepped inner yaw race lip,
    and flanged perimeter hex bolt fasteners to physically seat and shield the turret into the hull deck.
    """
    objects = []
    loc_v = Vector(location)

    # 1. Armored Circular Deflector Ring Collar
    outer_r = radius + thickness
    collar_loc = Vector((loc_v.x, loc_v.y, loc_v.z + height * 0.5))
    collar_obj = make_cylinder(
        f"{prefix}_DeflectorCollar",
        radius=outer_r,
        depth=height,
        location=collar_loc,
        material=armor_mat,
        vertices=16,
        bevel=thickness * 0.40,
    )
    objects.append(collar_obj)

    # 2. Stepped Inner Yaw Bearing Lip
    inner_lip_obj = make_cylinder(
        f"{prefix}_YawRaceLip",
        radius=radius + thickness * 0.45,
        depth=height * 1.15,
        location=collar_loc,
        material=gunmetal_mat or armor_mat,
        vertices=16,
        bevel=0.008,
    )
    objects.append(inner_lip_obj)

    # 3. Perimeter Hex Bolt Studs (8 radial studs around the collar)
    bolt_count = 8
    bolt_r = radius + thickness * 0.72
    bolt_z = loc_v.z + height * 0.94
    fastener_mat = gunmetal_mat or armor_mat
    for b_idx in range(bolt_count):
        b_angle = 2.0 * math.pi * b_idx / bolt_count
        bx = loc_v.x + bolt_r * math.cos(b_angle)
        by = loc_v.y + bolt_r * math.sin(b_angle)
        objects.append(make_cylinder(
            f"{prefix}_FastenerBolt_{b_idx}",
            radius=0.018,
            depth=0.026,
            location=Vector((bx, by, bolt_z)),
            material=fastener_mat,
            vertices=6,
            bevel=0.003,
        ))

    return objects


def make_driver_periscope_hood(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    armor_mat: bpy.types.Material | None = None,
    optic_mat: bpy.types.Material | None = None,
    size: Tuple[float, float, float] = (0.24, 0.28, 0.18),
    brow_angle_deg: float = 18.0,
) -> list[bpy.types.Object]:
    """Generates an authentic cast armored driver's vision periscope hood with recessed optical prism block."""
    objects = []
    loc_v = Vector(location)
    sx, sy, sz = size

    # 1. Armored Cast Hood with sloped ballistic brow
    hood_loc = loc_v + Vector((0.0, 0.0, sz * 0.5))
    objects.append(make_box(
        f"{prefix}_PeriscopeHood",
        size,
        hood_loc,
        armor_mat,
        bevel=sz * 0.22,
        rotation=(math.radians(brow_angle_deg), 0.0, 0.0),
    ))

    # 2. Recessed Optical Prism Glass Block (Cyan anti-laser coating)
    glass_loc = loc_v + Vector((0.0, sy * 0.38, sz * 0.44))
    objects.append(make_box(
        f"{prefix}_OpticPrism",
        (sx * 0.72, sy * 0.22, sz * 0.38),
        glass_loc,
        optic_mat,
        bevel=0.005,
        rotation=(math.radians(brow_angle_deg + 14.0), 0.0, 0.0),
    ))

    # 3. Base Mounting Collar Pad
    objects.append(make_box(
        f"{prefix}_MountPad",
        (sx * 1.25, sy * 1.20, sz * 0.22),
        loc_v + Vector((0.0, 0.0, sz * 0.11)),
        armor_mat,
        bevel=0.012,
    ))
    return objects


def make_perforated_exhaust_heat_shield(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    pipe_radius: float = 0.065,
    length: float = 0.55,
    direction: Tuple[float, float, float] | Vector = (0.0, 0.0, 1.0),
    armor_mat: bpy.types.Material | None = None,
    gunmetal_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a military perforated exhaust heat shield sleeve with clamp tension bands surrounding an exhaust duct."""
    objects = []
    loc_v = Vector(location)
    dir_v = Vector(direction).normalized()
    shield_r = pipe_radius * 1.35

    rot_quat = dir_v.to_track_quat("Z", "Y")
    rot_euler = rot_quat.to_euler()

    # 1. Stamped tubular heat shield sleeve
    shield_obj = make_cylinder(
        f"{prefix}_ShieldSleeve",
        radius=shield_r,
        depth=length * 0.88,
        location=loc_v,
        material=armor_mat,
        vertices=10,
        bevel=0.008,
        rotation=rot_euler,
    )
    objects.append(shield_obj)

    # 2. Tension clamp bands at both ends
    for side, tag in ((-1.0, "Start"), (1.0, "End")):
        band_loc = loc_v + dir_v * (length * 0.35 * side)
        objects.append(make_cylinder(
            f"{prefix}_ClampBand_{tag}",
            radius=shield_r * 1.15,
            depth=length * 0.065,
            location=band_loc,
            material=gunmetal_mat or armor_mat,
            vertices=8,
            bevel=0.004,
            rotation=rot_euler,
        ))
    return objects



def make_weapon_trunnion_bracket_assembly(
    prefix: str,
    pivot_center: Tuple[float, float, float] | Vector,
    spacing_x: float = 0.52,
    bracket_width: float = 0.12,
    bracket_height: float = 0.22,
    bracket_depth: float = 0.28,
    pin_radius: float = 0.045,
    armor_mat: bpy.types.Material | None = None,
    gunmetal_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates heavy cast/machined elevation trunnion bearing cheek brackets flanking the weapon mantlet.

    Provides physical load-bearing support for elevating gun tubes, featuring left and right
    trunnion cheek blocks, horizontal bearing pins with end retention plates, and flanged base mount pads.
    """
    objects = []
    center_v = Vector(pivot_center)
    half_spacing = spacing_x * 0.5
    rot_pin = (0.0, math.radians(90.0), 0.0)

    for side, stag in ((-1, "L"), (1, "R")):
        cx = center_v.x + side * half_spacing
        cy = center_v.y
        cz = center_v.z

        # 1. Armored Trunnion Cheek Housing Block
        cheek_obj = make_box(
            f"{prefix}_CheekBlock_{stag}",
            (bracket_width, bracket_depth, bracket_height),
            (cx, cy, cz),
            armor_mat,
            bevel=min(bracket_width * 0.25, bracket_height * 0.20),
        )
        objects.append(cheek_obj)

        # 2. Horizontal Bearing Pivot Pin
        pin_obj = make_cylinder(
            f"{prefix}_TrunnionPin_{stag}",
            radius=pin_radius,
            depth=bracket_width * 1.35,
            location=(cx + side * (bracket_width * 0.12), cy, cz),
            material=gunmetal_mat or armor_mat,
            vertices=12,
            bevel=0.005,
            rotation=rot_pin,
        )
        objects.append(pin_obj)

        # 3. Flanged Retention End Cap / Hub Nut
        cap_obj = make_cylinder(
            f"{prefix}_RetentionCap_{stag}",
            radius=pin_radius * 1.35,
            depth=bracket_width * 0.18,
            location=(cx + side * (bracket_width * 0.65), cy, cz),
            material=gunmetal_mat or armor_mat,
            vertices=8,
            bevel=0.003,
            rotation=rot_pin,
        )
        objects.append(cap_obj)

        # 4. Lower Mounting Saddle Flange
        flange_obj = make_box(
            f"{prefix}_MountFlange_{stag}",
            (bracket_width * 1.30, bracket_depth * 1.15, bracket_height * 0.20),
            (cx, cy, cz - bracket_height * 0.50),
            armor_mat,
            bevel=0.008,
        )
        objects.append(flange_obj)

    return objects


def make_final_drive_housing_assembly(
    prefix: str,
    hub_center: Tuple[float, float, float] | Vector,
    track_width: float = 2.4,
    housing_radius: float = 0.20,
    housing_depth: float = 0.12,
    is_rear: bool = True,
    armor_mat: bpy.types.Material | None = None,
    gunmetal_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates heavy planetary final-drive transmission gearbox housings connecting lower hull to drive sprockets.

    Features stepped cylindrical cast transmission housings protruding laterally from the hull sponson,
    with an 8-bolt circular flange ring and protruding center grease/bearing hub cap.
    """
    objects = []
    hub_v = Vector(hub_center)
    rot_trans = (0.0, math.radians(90.0), 0.0)

    for side, stag in ((-1, "L"), (1, "R")):
        hx = hub_v.x + side * (track_width * 0.42)
        hy = hub_v.y
        hz = hub_v.z

        # 1. Stepped Cylindrical Cast Transmission Housing
        housing_obj = make_cylinder(
            f"{prefix}_Housing_{stag}",
            radius=housing_radius,
            depth=housing_depth,
            location=(hx, hy, hz),
            material=armor_mat,
            vertices=14,
            bevel=housing_depth * 0.20,
            rotation=rot_trans,
        )
        objects.append(housing_obj)

        # 2. Bolt Retaining Flange Ring
        flange_x = hx - side * (housing_depth * 0.25)
        flange_obj = make_cylinder(
            f"{prefix}_Flange_{stag}",
            radius=housing_radius * 1.14,
            depth=housing_depth * 0.28,
            location=(flange_x, hy, hz),
            material=gunmetal_mat or armor_mat,
            vertices=14,
            bevel=0.006,
            rotation=rot_trans,
        )
        objects.append(flange_obj)

        # 3. Perimeter Retention Studs (8 radial hex bolts)
        bolt_r = housing_radius * 0.95
        bolt_x = hx + side * (housing_depth * 0.45)
        fastener_mat = gunmetal_mat or armor_mat
        for b_idx in range(8):
            b_angle = 2.0 * math.pi * b_idx / 8
            by = hy + bolt_r * math.cos(b_angle)
            bz = hz + bolt_r * math.sin(b_angle)
            objects.append(make_cylinder(
                f"{prefix}_Bolt_{stag}_{b_idx}",
                radius=0.016,
                depth=0.024,
                location=(bolt_x, by, bz),
                material=fastener_mat,
                vertices=6,
                bevel=0.003,
                rotation=rot_trans,
            ))

        # 4. Protruding Center Bearing Hub Cap
        cap_x = hx + side * (housing_depth * 0.55)
        cap_obj = make_cylinder(
            f"{prefix}_HubCap_{stag}",
            radius=housing_radius * 0.42,
            depth=housing_depth * 0.32,
            location=(cap_x, hy, hz),
            material=gunmetal_mat or armor_mat,
            vertices=12,
            bevel=0.005,
            rotation=rot_trans,
        )
        objects.append(cap_obj)

    return objects


def make_armored_access_hatch_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    size: Tuple[float, float] = (0.42, 0.48),
    thickness: float = 0.045,
    coaming_height: float = 0.025,
    rotation: Tuple[float, float, float] = (0.0, 0.0, 0.0),
    armor_mat: bpy.types.Material | None = None,
    gunmetal_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a raised armored crew access or maintenance inspection hatch with hinge knuckle and dogging latch.

    Eliminates flat bare armor slabs on decks and superstructures with authentic raised coaming rim,
    beveled hatch cover plate, heavy cylindrical hinge bar, and rotating dog-clamp handle.
    """
    objects = []
    loc_v = Vector(location)
    sx, sy = size
    rot_euler = Euler(rotation)

    # 1. Raised Coaming Rim
    coaming_z = loc_v.z + coaming_height * 0.5
    coaming_obj = make_box(
        f"{prefix}_CoamingRim",
        (sx * 1.10, sy * 1.08, coaming_height),
        Vector((loc_v.x, loc_v.y, coaming_z)),
        armor_mat,
        bevel=0.008,
        rotation=rotation,
    )
    objects.append(coaming_obj)

    # 2. Beveled Armor Hatch Plate
    plate_z = loc_v.z + coaming_height + thickness * 0.5
    plate_obj = make_box(
        f"{prefix}_HatchPlate",
        (sx, sy, thickness),
        Vector((loc_v.x, loc_v.y, plate_z)),
        armor_mat,
        bevel=thickness * 0.35,
        rotation=rotation,
    )
    objects.append(plate_obj)

    # 3. Heavy Transverse Hinge Bar Knuckle
    hinge_offset = rot_euler.to_matrix() @ Vector((0.0, -sy * 0.46, coaming_height + thickness * 0.70))
    hinge_loc = loc_v + hinge_offset
    hinge_obj = make_cylinder(
        f"{prefix}_HingeBar",
        radius=thickness * 0.55,
        depth=sx * 0.75,
        location=hinge_loc,
        material=gunmetal_mat or armor_mat,
        vertices=10,
        bevel=0.004,
        rotation=(rotation[0], rotation[1] + math.radians(90.0), rotation[2]),
    )
    objects.append(hinge_obj)

    # 4. Dogging Clamp Locking Latch Handle
    latch_offset = rot_euler.to_matrix() @ Vector((0.0, sy * 0.38, coaming_height + thickness + 0.015))
    latch_loc = loc_v + latch_offset
    latch_obj = make_box(
        f"{prefix}_DogLatch",
        (sx * 0.28, sy * 0.12, 0.022),
        latch_loc,
        gunmetal_mat or armor_mat,
        bevel=0.003,
        rotation=rotation,
    )
    objects.append(latch_obj)

    return objects


def make_track_tensioner_idler_yoke_assembly(
    prefix: str,
    idler_center: Tuple[float, float, float] | Vector,
    track_width: float = 2.4,
    arm_radius: float = 0.14,
    arm_length: float = 0.28,
    cylinder_radius: float = 0.055,
    cylinder_length: float = 0.32,
    armor_mat: bpy.types.Material | None = None,
    gunmetal_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates hydraulic track tensioner cylinders, swing yokes and mud scrapers for front idler wheels.

    Provides authentic mechanical mounting for front idlers with longitudinal hydraulic tensioner
    cylinders recessed along the front sponson wall, swinging trailing yoke arms connecting to the
    idler spindle, and heavy angled mud scraper blades behind the idler wheel.
    """
    objects = []
    idler_v = Vector(idler_center)
    rot_trans = (0.0, math.radians(90.0), 0.0)

    for side, stag in ((-1, "L"), (1, "R")):
        ix = idler_v.x + side * (track_width * 0.42)
        iy = idler_v.y
        iz = idler_v.z

        # 1. Longitudinal Hydraulic Tensioner Cylinder Barrel
        cyl_x = ix - side * 0.08
        cyl_y = iy - arm_length * 0.70
        cyl_z = iz
        cyl_obj = make_cylinder(
            f"{prefix}_TensionerCyl_{stag}",
            radius=cylinder_radius,
            depth=cylinder_length,
            location=(cyl_x, cyl_y, cyl_z),
            material=armor_mat,
            vertices=12,
            bevel=0.006,
            rotation=(math.radians(90.0), 0.0, 0.0),
        )
        objects.append(cyl_obj)

        # 2. Chrome Extension Piston Shaft
        piston_obj = make_cylinder(
            f"{prefix}_TensionerPiston_{stag}",
            radius=cylinder_radius * 0.65,
            depth=cylinder_length * 0.55,
            location=(cyl_x, cyl_y + cylinder_length * 0.35, cyl_z),
            material=gunmetal_mat or armor_mat,
            vertices=12,
            bevel=0.003,
            rotation=(math.radians(90.0), 0.0, 0.0),
        )
        objects.append(piston_obj)

        # 3. Heavy Cast Idler Swing-Arm Trailing Yoke
        arm_obj = make_box(
            f"{prefix}_IdlerYokeArm_{stag}",
            (arm_radius * 0.90, arm_length, arm_radius * 1.30),
            (ix, iy - arm_length * 0.30, iz),
            armor_mat,
            bevel=arm_radius * 0.20,
        )
        objects.append(arm_obj)

        # 4. Spindle Hub Collar
        hub_obj = make_cylinder(
            f"{prefix}_IdlerSpindleHub_{stag}",
            radius=arm_radius * 0.85,
            depth=arm_radius * 0.75,
            location=(ix + side * 0.04, iy, iz),
            material=gunmetal_mat or armor_mat,
            vertices=12,
            bevel=0.005,
            rotation=rot_trans,
        )
        objects.append(hub_obj)

        # 5. Heavy Angled Mud / Ice Scraper Blade
        scraper_obj = make_box(
            f"{prefix}_TrackScraper_{stag}",
            (arm_radius * 1.40, 0.04, arm_radius * 0.90),
            (ix, iy - arm_length * 0.65, iz - arm_radius * 0.45),
            gunmetal_mat or armor_mat,
            bevel=0.004,
            rotation=(math.radians(22.0), 0.0, 0.0),
        )
        objects.append(scraper_obj)

    return objects


def make_coaxial_gun_port_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    caliber: float = 0.032,
    barrel_length: float = 0.38,
    housing_size: Tuple[float, float, float] = (0.16, 0.24, 0.16),
    direction: Tuple[float, float, float] = (0.0, 1.0, 0.0),
    armor_mat: bpy.types.Material | None = None,
    gunmetal_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an armored coaxial secondary weapon port, blast deflector cowl, and barrel sleeve.

    Integrates into turret cheeks or hull mantlet bezels beside main ordnance, featuring a heavy
    cast port housing, recessed secondary barrel sleeve with perforated muzzle flash hider, and an
    angled ballistic blast deflector cowl.
    """
    objects = []
    loc_v = Vector(location)
    hx, hy, hz = housing_size
    dir_v = Vector(direction).normalized()
    rot_barrel = (math.radians(90.0), 0.0, 0.0)

    # 1. Cast Armored Coaxial Port Housing Block
    housing_obj = make_box(
        f"{prefix}_PortHousing",
        housing_size,
        loc_v,
        armor_mat,
        bevel=min(hx, hz) * 0.22,
    )
    objects.append(housing_obj)

    # 2. Armored Ballistic Blast Deflector Brow / Cowl
    brow_obj = make_box(
        f"{prefix}_BlastCowl",
        (hx * 1.15, hy * 0.55, hz * 0.35),
        loc_v + Vector((0.0, hy * 0.22, hz * 0.48)),
        armor_mat,
        bevel=0.006,
        rotation=(math.radians(-14.0), 0.0, 0.0),
    )
    objects.append(brow_obj)

    # 3. Recessed Secondary Barrel Sleeve
    sleeve_center = loc_v + dir_v * (barrel_length * 0.45)
    sleeve_obj = make_cylinder(
        f"{prefix}_CoaxSleeve",
        radius=caliber * 1.55,
        depth=barrel_length * 0.65,
        location=sleeve_center,
        material=gunmetal_mat or armor_mat,
        vertices=10,
        bevel=0.003,
        rotation=rot_barrel,
    )
    objects.append(sleeve_obj)

    # 4. Perforated Flash Hider / Barrel Tip
    muzzle_center = loc_v + dir_v * (barrel_length * 0.90)
    muzzle_obj = make_cylinder(
        f"{prefix}_CoaxMuzzle",
        radius=caliber * 1.20,
        depth=barrel_length * 0.35,
        location=muzzle_center,
        material=gunmetal_mat or armor_mat,
        vertices=10,
        bevel=0.002,
        rotation=rot_barrel,
    )
    objects.append(muzzle_obj)

    return objects


def make_pioneer_tool_rack_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    length: float = 0.95,
    width: float = 0.20,
    rotation: Tuple[float, float, float] = (0.0, 0.0, 0.0),
    armor_mat: bpy.types.Material | None = None,
    gunmetal_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates exterior pioneer entrenching tool racks (spade, track crowbar, retention cleats).

    Eliminates flat featureless sponson and hull side plates by mounting field pioneer entrenching
    gear: dual stamped steel C-brackets with dogging clamp clasps, an entrenching shovel with
    spade blade and handle shaft, and an octagonal steel track breaker pry bar.
    """
    objects = []
    loc_v = Vector(location)
    rot_euler = Euler(rotation)
    mat_rot = rot_euler.to_matrix()
    fastener_mat = gunmetal_mat or armor_mat

    # 1. Dual Stamped Steel Mounting Cleats / Clamps
    half_len = length * 0.40
    for c_idx, y_off in enumerate((-half_len, half_len)):
        c_pos = loc_v + mat_rot @ Vector((0.0, y_off, 0.02))
        cleat_obj = make_box(
            f"{prefix}_MountCleat_{c_idx}",
            (width * 1.15, 0.05, 0.06),
            c_pos,
            fastener_mat,
            bevel=0.004,
            rotation=rotation,
        )
        objects.append(cleat_obj)

        # Dogging Clamp Clasp
        clasp_pos = loc_v + mat_rot @ Vector((0.0, y_off, 0.06))
        clasp_obj = make_box(
            f"{prefix}_ClampClasp_{c_idx}",
            (width * 0.75, 0.035, 0.035),
            clasp_pos,
            armor_mat or fastener_mat,
            bevel=0.003,
            rotation=rotation,
        )
        objects.append(clasp_obj)

    # 2. Entrenching Shovel (Handle Shaft + Spade Head)
    shaft_pos = loc_v + mat_rot @ Vector((-width * 0.22, 0.0, 0.045))
    shaft_obj = make_cylinder(
        f"{prefix}_SpadeShaft",
        radius=0.016,
        depth=length * 0.88,
        location=shaft_pos,
        material=fastener_mat,
        vertices=8,
        bevel=0.002,
        rotation=(rotation[0] + math.radians(90.0), rotation[1], rotation[2]),
    )
    objects.append(shaft_obj)

    spade_pos = loc_v + mat_rot @ Vector((-width * 0.22, length * 0.38, 0.045))
    spade_obj = make_box(
        f"{prefix}_SpadeBlade",
        (width * 0.55, length * 0.22, 0.018),
        spade_pos,
        fastener_mat,
        bevel=0.003,
        rotation=rotation,
    )
    objects.append(spade_obj)

    # 3. Track-Pin Breaker Octagonal Pry Bar
    crowbar_pos = loc_v + mat_rot @ Vector((width * 0.24, -length * 0.05, 0.045))
    crowbar_obj = make_cylinder(
        f"{prefix}_PryBar",
        radius=0.018,
        depth=length * 0.92,
        location=crowbar_pos,
        material=fastener_mat,
        vertices=8,
        bevel=0.002,
        rotation=(rotation[0] + math.radians(90.0), rotation[1], rotation[2]),
    )
    objects.append(crowbar_obj)

    # Chisel Claw Tip
    claw_pos = loc_v + mat_rot @ Vector((width * 0.24, length * 0.42, 0.045))
    claw_obj = make_box(
        f"{prefix}_PryBarClaw",
        (0.032, 0.06, 0.024),
        claw_pos,
        fastener_mat,
        bevel=0.003,
        rotation=(rotation[0] + math.radians(18.0), rotation[1], rotation[2]),
    )
    objects.append(claw_obj)

    return objects


def make_process_piping_manifold(
    prefix: str,
    start_loc: Tuple[float, float, float] | Vector,
    length: float = 3.5,
    pipe_radius: float = 0.08,
    flange_radius: float = 0.125,
    pipe_mat: bpy.types.Material | None = None,
    flange_mat: bpy.types.Material | None = None,
    valve_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    direction: Tuple[float, float, float] | Vector = (0.0, 1.0, 0.0),
    include_gate_valve: bool = True,
    include_color_bands: bool = True,
) -> list[bpy.types.Object]:
    """Generates an ASME B31.3 compliant process piping manifold with weld-neck flanges,
    90-degree long-radius elbows, manual handwheel gate valves, and ASME A13.1 fluid bands."""
    objects = []
    p0 = Vector(start_loc)
    dir_v = Vector(direction).normalized()
    p_mat = pipe_mat or flange_mat
    f_mat = flange_mat or pipe_mat
    v_mat = valve_mat or flange_mat

    rot = dir_v.to_track_quat("Z", "Y").to_euler()

    # 1. Main header pipe
    pipe_center = p0 + dir_v * (length * 0.5)
    main_pipe = make_cylinder(
        f"{prefix}_HeaderPipe",
        radius=pipe_radius,
        depth=length,
        location=pipe_center,
        material=p_mat,
        rotation=rot,
        vertices=12,
        bevel=0.006,
    )
    objects.append(main_pipe)

    # 2. Spaced weld-neck flanges with bolt circles
    flange_spacing = length / 4.0
    for f_idx in range(1, 4):
        f_pos = p0 + dir_v * (f_idx * flange_spacing)
        flange = make_cylinder(
            f"{prefix}_WeldNeckFlange_{f_idx}",
            radius=flange_radius,
            depth=0.035,
            location=f_pos,
            material=f_mat,
            rotation=rot,
            vertices=12,
            bevel=0.004,
        )
        objects.append(flange)

    # 3. Manual handwheel gate valve (ASME body, bonnet, spindle, circular handwheel)
    if include_gate_valve:
        valve_pos = p0 + dir_v * (length * 0.5)
        objects.append(make_box(
            f"{prefix}_GateValveBody",
            (pipe_radius * 2.6, pipe_radius * 2.2, pipe_radius * 2.4),
            valve_pos,
            v_mat,
            bevel=0.008,
            rotation=rot,
        ))
        stem_dir = Vector((0.0, 0.0, 1.0))
        stem_pos = valve_pos + stem_dir * (pipe_radius * 1.8)
        objects.append(make_cylinder(
            f"{prefix}_ValveStem",
            radius=0.016,
            depth=pipe_radius * 1.6,
            location=stem_pos,
            material=f_mat,
            rotation=None,
            vertices=8,
            bevel=0.002,
        ))
        wheel_pos = stem_pos + stem_dir * (pipe_radius * 0.8)
        objects.append(make_cylinder(
            f"{prefix}_ValveHandwheel",
            radius=pipe_radius * 1.5,
            depth=0.025,
            location=wheel_pos,
            material=accent_mat or v_mat,
            rotation=None,
            vertices=12,
            bevel=0.004,
        ))

    # 4. ASME A13.1 fluid identification color bands
    if include_color_bands and accent_mat:
        for b_idx in (1, 3):
            b_pos = p0 + dir_v * (b_idx * flange_spacing + 0.12)
            band = make_cylinder(
                f"{prefix}_FluidColorBand_{b_idx}",
                radius=pipe_radius * 1.05,
                depth=0.08,
                location=b_pos,
                material=accent_mat,
                rotation=rot,
                vertices=12,
                bevel=0.002,
            )
            objects.append(band)

    return objects


def make_dished_head_pressure_vessel(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    radius: float = 0.70,
    height: float = 2.4,
    vessel_mat: bpy.types.Material | None = None,
    metal_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    include_manway: bool = True,
    include_stiffener_rings: bool = True,
) -> list[bpy.types.Object]:
    """Generates an industrial vertical pressure vessel / chemical storage silo with an authentic
    ASME 2:1 ellipsoidal / toriespherical dished head, circumferential stiffener rings,
    inspection manway hatch with davit arm, and anchored skirt support."""
    objects = []
    loc = Vector(location)
    v_mat = vessel_mat or metal_mat
    m_mat = metal_mat or vessel_mat

    # 1. Main vertical cylindrical shell
    body_z = loc.z + height * 0.5
    body = make_cylinder(
        f"{prefix}_VesselShell",
        radius=radius,
        depth=height,
        location=Vector((loc.x, loc.y, body_z)),
        material=v_mat,
        rotation=None,
        vertices=16,
        bevel=0.02,
    )
    objects.append(body)

    # 2. ASME 2:1 Ellipsoidal Dished Head top cap (domed crown)
    dome_z = loc.z + height + 0.12
    dished_head = make_cylinder(
        f"{prefix}_DishedHead",
        radius=radius,
        depth=0.25,
        location=Vector((loc.x, loc.y, dome_z)),
        material=v_mat,
        rotation=None,
        vertices=16,
        bevel=min(radius * 0.45, 0.22),
    )
    objects.append(dished_head)

    # 3. Flared skirt support base resting on foundation
    skirt_z = loc.z + 0.18
    skirt = make_cylinder(
        f"{prefix}_SkirtBase",
        radius=radius * 1.10,
        depth=0.36,
        location=Vector((loc.x, loc.y, skirt_z)),
        material=m_mat,
        rotation=None,
        vertices=16,
        bevel=0.03,
    )
    objects.append(skirt)

    # 4. Circumferential external stiffener rings (vacuum / pressure reinforcement)
    if include_stiffener_rings:
        for r_idx in range(1, 3):
            ring_z = loc.z + height * (r_idx * 0.33)
            ring = make_cylinder(
                f"{prefix}_StiffenerRing_{r_idx}",
                radius=radius * 1.05,
                depth=0.045,
                location=Vector((loc.x, loc.y, ring_z)),
                material=m_mat,
                rotation=None,
                vertices=16,
                bevel=0.005,
            )
            objects.append(ring)

    # 5. Inspection manway hatch with davit arm hinge
    if include_manway:
        manway_y = loc.y + radius * 0.96
        manway_z = loc.z + height * 0.30
        objects.append(make_cylinder(
            f"{prefix}_ManwayFlange",
            radius=0.22,
            depth=0.08,
            location=Vector((loc.x, manway_y, manway_z)),
            material=m_mat,
            rotation=(math.radians(90.0), 0.0, 0.0),
            vertices=12,
            bevel=0.008,
        ))
        objects.append(make_cylinder(
            f"{prefix}_ManwayCover",
            radius=0.24,
            depth=0.03,
            location=Vector((loc.x, manway_y + 0.05, manway_z)),
            material=accent_mat or m_mat,
            rotation=(math.radians(90.0), 0.0, 0.0),
            vertices=12,
            bevel=0.004,
        ))
        objects.append(make_box(
            f"{prefix}_DavitArm",
            (0.04, 0.14, 0.35),
            Vector((loc.x + 0.22, manway_y + 0.02, manway_z)),
            m_mat,
            bevel=0.005,
        ))

    return objects


def make_gantry_crane_trolley_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    span: float = 7.0,
    beam_mat: bpy.types.Material | None = None,
    hoist_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates heavy overhead gantry crane runway beams with triangular gusset plates,
    polyurethane end-stops, hoist trolley with wire-rope drum, sheave hook block, and cable festoons."""
    objects = []
    loc = Vector(location)
    b_mat = beam_mat or hoist_mat
    h_mat = hoist_mat or beam_mat

    # 1. Runway I-beam rails along the top crossbeam
    for side in (-1.0, 1.0):
        gx = side * (span * 0.5 - 0.20)
        objects.append(make_box(
            f"{prefix}_EndStop_{'R' if side > 0 else 'L'}",
            (0.18, 0.35, 0.22),
            Vector((gx, loc.y, loc.z + 0.12)),
            accent_mat or h_mat,
            bevel=0.008,
        ))
        objects.append(make_box(
            f"{prefix}_GussetPlate_{'R' if side > 0 else 'L'}",
            (0.04, 0.38, 0.45),
            Vector((gx - side * 0.15, loc.y, loc.z - 0.28)),
            b_mat,
            bevel=0.005,
        ))

    # 2. Central Hoist Trolley Chassis
    trolley_w = 1.10
    trolley_l = 0.70
    trolley_h = 0.28
    trolley_z = loc.z - 0.18
    trolley = make_box(
        f"{prefix}_HoistTrolley",
        (trolley_w, trolley_l, trolley_h),
        Vector((loc.x, loc.y, trolley_z)),
        b_mat,
        bevel=0.015,
    )
    objects.append(trolley)

    # 3. Wire-rope grooved hoist drum
    drum_z = trolley_z - 0.20
    drum = make_cylinder(
        f"{prefix}_HoistDrum",
        radius=0.18,
        depth=0.55,
        location=Vector((loc.x, loc.y, drum_z)),
        material=h_mat,
        rotation=(0.0, math.radians(90.0), 0.0),
        vertices=12,
        bevel=0.008,
    )
    objects.append(drum)

    # 4. Sheave block & forged crane hook
    hook_z = drum_z - 0.42
    sheave_block = make_box(
        f"{prefix}_SheaveBlock",
        (0.24, 0.26, 0.22),
        Vector((loc.x, loc.y, hook_z)),
        accent_mat or b_mat,
        bevel=0.01,
    )
    objects.append(sheave_block)
    hook_horn = make_cylinder(
        f"{prefix}_CraneHook",
        radius=0.08,
        depth=0.18,
        location=Vector((loc.x, loc.y, hook_z - 0.14)),
        material=h_mat,
        rotation=None,
        vertices=8,
        bevel=0.02,
    )
    objects.append(hook_horn)

    # 5. C-rail cable festoon support track
    festoon_track = make_box(
        f"{prefix}_FestoonTrack",
        (span * 0.75, 0.06, 0.06),
        Vector((loc.x, loc.y - 0.28, loc.z + 0.10)),
        h_mat,
        bevel=0.003,
    )
    objects.append(festoon_track)

    return objects


def make_caged_safety_ladder(
    prefix: str,
    base_location: Tuple[float, float, float] | Vector,
    height: float = 3.8,
    ladder_mat: bpy.types.Material | None = None,
    cage_mat: bpy.types.Material | None = None,
    width: float = 0.45,
) -> list[bpy.types.Object]:
    """Generates an OSHA-compliant industrial caged safety ladder with vertical stringers,
    rungs, safety hoop cages, and top walk-through handrails."""
    objects = []
    base = Vector(base_location)
    l_mat = ladder_mat or cage_mat
    c_mat = cage_mat or ladder_mat

    # 1. Vertical side stringers
    for side in (-1.0, 1.0):
        sx = base.x + side * (width * 0.5)
        stringer = make_box(
            f"{prefix}_Stringer_{'R' if side > 0 else 'L'}",
            (0.035, 0.06, height),
            Vector((sx, base.y, base.z + height * 0.5)),
            l_mat,
            bevel=0.004,
        )
        objects.append(stringer)

    # 2. Spaced horizontal rungs (300mm standard spacing)
    rung_spacing = 0.30
    rung_count = max(2, int(height / rung_spacing))
    for r_idx in range(1, rung_count):
        rz = base.z + r_idx * rung_spacing
        rung = make_cylinder(
            f"{prefix}_Rung_{r_idx}",
            radius=0.014,
            depth=width - 0.02,
            location=Vector((base.x, base.y, rz)),
            material=l_mat,
            rotation=(0.0, math.radians(90.0), 0.0),
            vertices=8,
            bevel=0.002,
        )
        objects.append(rung)

    # 3. Safety hoop cage (starting above 2.1m clearance)
    cage_start_z = base.z + 2.1
    cage_height = max(0.5, height - 2.1)
    hoop_count = max(2, int(cage_height / 0.65))
    for h_idx in range(hoop_count):
        hz = cage_start_z + h_idx * 0.65
        hoop = make_box(
            f"{prefix}_CageHoop_{h_idx}",
            (width * 1.35, width * 0.95, 0.03),
            Vector((base.x, base.y + width * 0.45, hz)),
            c_mat,
            bevel=0.004,
        )
        objects.append(hoop)

    # 4. Vertical cage strap bars
    for strap_side in (-1.0, 0.0, 1.0):
        strap_x = base.x + strap_side * (width * 0.45)
        strap_y = base.y + width * 0.85
        strap = make_box(
            f"{prefix}_CageStrap_{int(strap_side+1)}",
            (0.025, 0.025, cage_height),
            Vector((strap_x, strap_y, cage_start_z + cage_height * 0.5)),
            c_mat,
            bevel=0.002,
        )
        objects.append(strap)

    return objects

def create_unity_empty_locator(
    name: str,
    location: Tuple[float, float, float] | Vector,
    forward: Tuple[float, float, float] | Vector = (0.0, 1.0, 0.0),
    radius: float = 0.2,
) -> bpy.types.Object:
    """Creates a Unity-compatible empty locator with specified name, position and orientation."""
    bpy.ops.object.empty_add(type="PLAIN_AXES", radius=radius, location=location)
    emp = bpy.context.object
    emp.name = name
    fwd_vec = Vector(forward).normalized()
    if fwd_vec.length > 0.001:
        up_vec = Vector((0.0, 0.0, 1.0))
        if abs(fwd_vec.dot(up_vec)) > 0.99:
            up_vec = Vector((1.0, 0.0, 0.0))
        emp.rotation_euler = fwd_vec.to_track_quat("Y", "Z").to_euler()
    return emp


def create_unity_locator_sockets(
    name_prefix: str,
    muzzle_loc: Tuple[float, float, float] | Vector | None = None,
    muzzle_dir: Tuple[float, float, float] | Vector | None = None,
    muzzle_secondary_loc: Tuple[float, float, float] | Vector | None = None,
    muzzle_secondary_dir: Tuple[float, float, float] | Vector | None = None,
    turret_loc: Tuple[float, float, float] | Vector | None = None,
    turret_pitch_loc: Tuple[float, float, float] | Vector | None = None,
    recoil_loc: Tuple[float, float, float] | Vector | None = None,
    recoil_dir: Tuple[float, float, float] | Vector | None = None,
    sensor_loc: Tuple[float, float, float] | Vector | None = None,
    exhaust_left: Tuple[float, float, float] | Vector | None = None,
    exhaust_right: Tuple[float, float, float] | Vector | None = None,
    damage_smoke_loc: Tuple[float, float, float] | Vector | None = None,
    damage_fire_loc: Tuple[float, float, float] | Vector | None = None,
    center_of_mass_loc: Tuple[float, float, float] | Vector | None = None,
    ground_contact_loc: Tuple[float, float, float] | Vector | None = None,
    radar_loc: Tuple[float, float, float] | Vector | None = None,
    hardpoints: Dict[str, Tuple[float, float, float] | Vector] | None = None,
    include_default_hardpoints: bool = True,
    antenna_loc: Tuple[float, float, float] | Vector | None = None,
    thruster_vector_left: Tuple[float, float, float] | Vector | None = None,
    thruster_vector_right: Tuple[float, float, float] | Vector | None = None,
    foot_contact_left: Tuple[float, float, float] | Vector | None = None,
    foot_contact_right: Tuple[float, float, float] | Vector | None = None,
    quad_foot_contacts: Dict[str, Tuple[float, float, float] | Vector] | None = None,
    engine_audio_loc: Tuple[float, float, float] | Vector | None = None,
    weapon_audio_loc: Tuple[float, float, float] | Vector | None = None,
    nested_firepoints: bool = True,
    antenna_parts: Sequence[bpy.types.Object] | None = None,
    primary_firepoint_locs: Sequence[Tuple[float, float, float] | Vector] | None = None,
) -> list[bpy.types.Object]:
    """Generates standard Unity empty sockets matching Rogue Command vehicle & weapon mechanics:

    1. Socket_MuzzlePrimary: Main weapon firing point and projectile exit vector.
       If nested_firepoints is True, creates child empty FirePoint_0 for ShootProjectile.Init().
    2. Socket_MuzzleSecondary: Secondary weapon (coax/pintle/missile) firing point.
       If nested_firepoints is True, creates child empty FirePoint_1.
    3. Socket_TurretPivot: Horizontal azimuth rotation axis. Do not emit its legacy
       Socket_TurretYaw alias in the same hierarchy; both names bind the same runtime role.
    4. Socket_TurretPitch: Vertical elevation pivot axis.
    5. Socket_RecoilPrimary: Linear impulse slide axis for barrel recoil animation.
    6. Socket_SensorOptics / Socket_SensorEye: Targeting visor and sensor beam origin.
    7. Socket_Exhaust_L / Socket_Exhaust_R: Engine exhaust particle emitters.
    8. Socket_DamageSmoke: Engine deck damage particle spawner (50% HP threshold).
    9. Socket_DamageFire: Catastrophic damage flame particle spawner (25% HP threshold).
    10. Socket_CenterOfMass: Rigidbody physics center of mass.
    11. Socket_GroundContact / Socket_GroundCenter: Ground contact projection (Z=0) for selection rings.
    12. Socket_RadarMast: 360-degree radar mast sweep pivot. Do not also emit the
        legacy Socket_RadarDish alias in the same hierarchy.
    13. Socket_Hardpoint_<Type>: Modular equipment mounts (ERA, APS, Sponsons, FuelDrums).
    14. Socket_Antenna: Whip antenna mounting root for dynamic inertial wobble simulation.
    15. Socket_ThrusterVector_L / Socket_ThrusterVector_R: Articulated VTOL/dropship vector thrusters.
    16. Socket_FootContact_L / Socket_FootContact_R / Socket_FootContact_<Tag>: Foot ground contact anchors.
    17. Socket_EngineAudio: Looping engine/movement SFX anchor for EntityFlockingMovement.
    18. Socket_WeaponAudio: Weapon firing 3D SFX anchor for ShootProjectile.
    """
    empties = []
    def _add_empty(socket_name: str, loc: Vector, rot: Tuple[float, float, float] = (0.0, 0.0, 0.0)) -> bpy.types.Object:
        bpy.ops.object.empty_add(type="PLAIN_AXES", radius=0.15, location=loc)
        e = bpy.context.object
        e.name = socket_name
        e.rotation_euler = rot
        empties.append(e)
        return e

    def _add_child_empty(
        child_name: str,
        parent: bpy.types.Object,
        world_loc: Vector,
        rot: Tuple[float, float, float],
    ) -> bpy.types.Object:
        bpy.ops.object.empty_add(type="PLAIN_AXES", radius=0.08, location=world_loc)
        child = bpy.context.object
        child.name = child_name
        child.rotation_euler = rot
        world_matrix = child.matrix_world.copy()
        child.parent = parent
        child.matrix_world = world_matrix
        empties.append(child)
        return child

    # 1. Primary Muzzle
    if muzzle_loc is not None:
        m_loc = Vector(muzzle_loc)
        m_dir = Vector(muzzle_dir if muzzle_dir is not None else (0.0, 1.0, 0.0)).normalized()
        rot = m_dir.to_track_quat("Z", "Y").to_euler()
        muz_obj = _add_empty("Socket_MuzzlePrimary", m_loc, rot)
        if nested_firepoints:
            if primary_firepoint_locs:
                if len(primary_firepoint_locs) > 2:
                    raise ValueError("Socket_MuzzlePrimary supports at most FirePoint_0 and FirePoint_1.")
                for index, firepoint_loc in enumerate(primary_firepoint_locs):
                    _add_child_empty("FirePoint_" + str(index), muz_obj, Vector(firepoint_loc), rot)
            else:
                _add_child_empty("FirePoint_0", muz_obj, m_loc, rot)

    # 2. Secondary Muzzle
    if muzzle_secondary_loc is not None:
        m2_loc = Vector(muzzle_secondary_loc)
        m2_dir = Vector(muzzle_secondary_dir if muzzle_secondary_dir is not None else (0.0, 1.0, 0.0)).normalized()
        rot2 = m2_dir.to_track_quat("Z", "Y").to_euler()
        muz2_obj = _add_empty("Socket_MuzzleSecondary", m2_loc, rot2)
        if nested_firepoints:
            _add_child_empty("FirePoint_1", muz2_obj, m2_loc, rot2)

    # 3. Turret Yaw
    if turret_loc is not None:
        t_loc = Vector(turret_loc)
        _add_empty("Socket_TurretPivot", t_loc)

    # 4. Turret Pitch
    if turret_pitch_loc is not None:
        _add_empty("Socket_TurretPitch", Vector(turret_pitch_loc))
    elif turret_loc is not None:
        _add_empty("Socket_TurretPitch", Vector(turret_loc) + Vector((0.0, 0.25, 0.15)))

    # 5. Recoil
    if recoil_loc is not None:
        r_loc = Vector(recoil_loc)
        r_dir = Vector(recoil_dir if recoil_dir is not None else (0.0, -1.0, 0.0)).normalized()
        r_rot = r_dir.to_track_quat("Z", "Y").to_euler()
        _add_empty("Socket_RecoilPrimary", r_loc, r_rot)
    elif muzzle_loc is not None:
        _add_empty("Socket_RecoilPrimary", Vector(muzzle_loc) - Vector((0.0, 0.45, 0.0)))

    # 6. Sensor Optics
    if sensor_loc is not None:
        s_loc = Vector(sensor_loc)
        _add_empty("Socket_SensorOptics", s_loc)
        _add_empty("Socket_SensorEye", s_loc)

    # 7. Exhausts
    if exhaust_left is not None:
        _add_empty("Socket_Exhaust_L", Vector(exhaust_left))
    if exhaust_right is not None:
        _add_empty("Socket_Exhaust_R", Vector(exhaust_right))

    # 8. Damage Threshold Sockets (Smoke & Fire)
    if damage_smoke_loc is not None:
        _add_empty("Socket_DamageSmoke", Vector(damage_smoke_loc))
    elif exhaust_left is not None:
        _add_empty("Socket_DamageSmoke", Vector(exhaust_left) + Vector((0.0, 0.15, 0.25)))
    elif turret_loc is not None:
        _add_empty("Socket_DamageSmoke", Vector(turret_loc) + Vector((-0.20, -0.20, 0.30)))
    else:
        _add_empty("Socket_DamageSmoke", Vector((0.0, -0.45, 0.65)))

    if damage_fire_loc is not None:
        _add_empty("Socket_DamageFire", Vector(damage_fire_loc))
    elif turret_loc is not None:
        _add_empty("Socket_DamageFire", Vector(turret_loc) + Vector((0.20, -0.20, 0.30)))

    # 9. Radar Mast & Dish
    if radar_loc is not None:
        r_loc = Vector(radar_loc)
        _add_empty("Socket_RadarMast", r_loc)
    elif turret_loc is not None:
        r_loc = Vector(turret_loc) + Vector((-0.25, -0.30, 0.45))
        _add_empty("Socket_RadarMast", r_loc)

    # 10. Modular Hardpoints
    hp_dict = dict(hardpoints) if hardpoints else {}
    if include_default_hardpoints:
        if "ERA" not in hp_dict:
            hp_dict["ERA"] = (Vector(turret_loc) + Vector((0.0, 0.40, 0.10))) if turret_loc is not None else Vector((0.0, 0.80, 0.50))
        if "APS" not in hp_dict:
            hp_dict["APS"] = (Vector(turret_loc) + Vector((0.35, -0.35, 0.40))) if turret_loc is not None else Vector((0.40, -0.60, 0.70))
        if "Sponsons" not in hp_dict:
            hp_dict["Sponsons"] = Vector((0.95, 0.0, 0.45))
        if "FuelDrums" not in hp_dict:
            hp_dict["FuelDrums"] = Vector((0.0, -1.25, 0.40))

    for hp_name, hp_pos in hp_dict.items():
        _add_empty(f"Socket_Hardpoint_{hp_name}", Vector(hp_pos))

    # 11. Center of Mass
    if center_of_mass_loc is not None:
        _add_empty("Socket_CenterOfMass", Vector(center_of_mass_loc))
    else:
        _add_empty("Socket_CenterOfMass", Vector((0.0, 0.0, 0.45)))

    # 12. Ground Contact / Selection Ring
    if ground_contact_loc is not None:
        _add_empty("Socket_GroundContact", Vector(ground_contact_loc))
        _add_empty("Socket_GroundCenter", Vector(ground_contact_loc))
    else:
        _add_empty("Socket_GroundContact", Vector((0.0, 0.0, 0.0)))
        _add_empty("Socket_GroundCenter", Vector((0.0, 0.0, 0.0)))

    # 13. Antenna Socket (for dynamic inertial wobble)
    if antenna_loc is not None:
        _add_empty("Socket_Antenna", Vector(antenna_loc))
    elif turret_loc is not None:
        _add_empty("Socket_Antenna", Vector(turret_loc) + Vector((0.35, -0.40, 0.25)))

    antenna_socket = next((obj for obj in empties if obj.name == "Socket_Antenna"), None)
    if antenna_socket is not None:
        for part in antenna_parts or ():
            try:
                if part is None or part.type != "MESH":
                    continue
                if "Sensor_Antenna" not in part.name and "Sensor_WhipAntenna" not in part.name:
                    continue
                world_matrix = part.matrix_world.copy()
                part.parent = antenna_socket
                part.matrix_world = world_matrix
            except ReferenceError:
                continue

    # 14. Vector Thruster Sockets (VTOL / Dropships)
    if thruster_vector_left is not None:
        _add_empty("Socket_ThrusterVector_L", Vector(thruster_vector_left))
    if thruster_vector_right is not None:
        _add_empty("Socket_ThrusterVector_R", Vector(thruster_vector_right))

    # 15. Foot Contact Sockets (for Walkers, Mechs, and Step SFX / Dust)
    if foot_contact_left is not None:
        _add_empty("Socket_FootContact_L", Vector(foot_contact_left))
    if foot_contact_right is not None:
        _add_empty("Socket_FootContact_R", Vector(foot_contact_right))
    if quad_foot_contacts:
        for q_name, q_loc in quad_foot_contacts.items():
            _add_empty(f"Socket_FootContact_{q_name}", Vector(q_loc))

    # 16. Positional 3D Audio Anchors
    # Socket_EngineAudio: Looping engine/movement SFX anchor (EntityFlockingMovement._movementSoundAudioSource)
    if engine_audio_loc is not None:
        _add_empty("Socket_EngineAudio", Vector(engine_audio_loc))
    elif exhaust_left is not None and exhaust_right is not None:
        mid_ex = (Vector(exhaust_left) + Vector(exhaust_right)) * 0.5
        _add_empty("Socket_EngineAudio", mid_ex)
    elif exhaust_left is not None:
        _add_empty("Socket_EngineAudio", Vector(exhaust_left))
    else:
        _add_empty("Socket_EngineAudio", Vector((0.0, -0.65, 0.40)))

    # Socket_WeaponAudio: Weapon firing SFX anchor (ShootProjectile.shotSoundAudioSource)
    if weapon_audio_loc is not None:
        _add_empty("Socket_WeaponAudio", Vector(weapon_audio_loc))
    elif muzzle_loc is not None:
        _add_empty("Socket_WeaponAudio", Vector(muzzle_loc))
    elif turret_pitch_loc is not None:
        _add_empty("Socket_WeaponAudio", Vector(turret_pitch_loc))

    return empties


# ---------------------------------------------------------------------------
# 7. Optimization, Batching & Mesh Quality
# ---------------------------------------------------------------------------

def apply_weighted_normals(
    obj: bpy.types.Object,
    weight: int = 50,
    mode: str = "FACE_AREA",
    keep_sharp: bool = True,
    apply_modifier: bool = True,
) -> bpy.types.Object:
    """Applies a Face-Area Weighted Normal modifier to eliminate low-poly shading pinch on chamfered hard-surface plates."""
    if obj is None or obj.type != "MESH" or not obj.data.polygons:
        return obj

    try:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj

        mod = obj.modifiers.new(name="RCL_WeightedNormal", type="WEIGHTED_NORMAL")
        mod.mode = mode
        mod.weight = max(1, min(100, int(weight)))
        mod.keep_sharp = keep_sharp

        has_armature = any(m.type == "ARMATURE" for m in obj.modifiers)
        if apply_modifier and not has_armature:
            bpy.ops.object.modifier_apply(modifier=mod.name)
    except Exception as ex:
        print(f"Warning: Weighted normal pass skipped for {obj.name}: {ex}")

    return obj


def batch_and_join_by_material(
    objects: Sequence[bpy.types.Object],
    prefix: str = "Unit",
    preserve_names: Sequence[str] | None = None,
    smooth_by_angle: bool = True,
    smooth_angle_deg: float = 38.0,
    weighted_normals: bool = True,
    normal_weight: int = 50,
    unity_batch_names: bool = False,
) -> list[bpy.types.Object]:
    """Combines modular kitbashed objects by material into unified meshes.

    Reduces the number of Blender mesh objects/material groups exported. Final
    Unity draw-call counts depend on the selected prefab source, passes, and settings.
    Objects whose names match or contain strings in `preserve_names` remain separate.
    When `smooth_by_angle` is True, applies Blender 4.5 angle-weighted auto-smoothing,
    giving perfectly rounded cylinders/wheels/barrels while preserving crisp chamfers.
    When `weighted_normals` is True, applies face-area weighted normals (`WEIGHTED_NORMAL`)
    so broad armor plates remain flat without shading pinch across 1-segment bevels.
    """
    material_groups: Dict[bpy.types.Material | None, list[bpy.types.Object]] = {}
    preserved_objects: list[bpy.types.Object] = []
    # Rigged sample overlays are consumed as material batches under the
    # Generic armature; every rendered object must use the canonical batch
    # naming contract even when a source mech part was ordinarily preserved
    # for a runtime transform pivot.
    preserve_patterns = [] if unity_batch_names else (list(preserve_names) if preserve_names else [])

    for obj in objects:
        if obj.type != "MESH":
            continue
        should_preserve = any(p in obj.name for p in preserve_patterns)
        if should_preserve:
            preserved_objects.append(obj)
            continue

        mat = obj.data.materials[0] if obj.data.materials else None
        material_groups.setdefault(mat, []).append(obj)

    joined_objects: list[bpy.types.Object] = list(preserved_objects)

    for mat, group in material_groups.items():
        if not group:
            continue
        if len(group) == 1:
            joined_obj = group[0]
            joined_objects.append(joined_obj)
        else:
            bpy.ops.object.select_all(action="DESELECT")
            for obj in group:
                obj.select_set(True)
            bpy.context.view_layer.objects.active = group[0]

            bpy.ops.object.join()
            joined_obj = bpy.context.view_layer.objects.active
            mat_name = mat.name.replace(" ", "_") if mat else "Unassigned"
            if unity_batch_names:
                prefix_marker = f"{prefix}_"
                if mat_name.lower().startswith(prefix_marker.lower()):
                    mat_name = mat_name[len(prefix_marker):]
                joined_obj.name = f"{prefix}_Batch_{mat_name}"
            else:
                joined_obj.name = f"{prefix}_{mat_name}"

            # Clean up mesh normals
            bpy.ops.object.mode_set(mode="EDIT")
            bpy.ops.mesh.select_all(action="SELECT")
            bpy.ops.mesh.normals_make_consistent(inside=False)
            bpy.ops.object.mode_set(mode="OBJECT")
            joined_objects.append(joined_obj)

        if unity_batch_names and len(group) == 1:
            mat_name = mat.name.replace(" ", "_") if mat else "Unassigned"
            prefix_marker = f"{prefix}_"
            if mat_name.lower().startswith(prefix_marker.lower()):
                mat_name = mat_name[len(prefix_marker):]
            joined_obj.name = f"{prefix}_Batch_{mat_name}"

        if smooth_by_angle and hasattr(bpy.ops.object, "shade_smooth_by_angle"):
            bpy.ops.object.select_all(action="DESELECT")
            joined_obj.select_set(True)
            bpy.context.view_layer.objects.active = joined_obj
            bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth_angle_deg))
        else:
            for poly in joined_obj.data.polygons:
                poly.use_smooth = False

        if weighted_normals and smooth_by_angle:
            apply_weighted_normals(joined_obj, weight=normal_weight, keep_sharp=True, apply_modifier=True)

    # Also smooth preserved objects if requested
    if smooth_by_angle and hasattr(bpy.ops.object, "shade_smooth_by_angle"):
        for p_obj in preserved_objects:
            if p_obj.type == "MESH":
                bpy.ops.object.select_all(action="DESELECT")
                p_obj.select_set(True)
                bpy.context.view_layer.objects.active = p_obj
                bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth_angle_deg))
                if weighted_normals:
                    apply_weighted_normals(p_obj, weight=normal_weight, keep_sharp=True, apply_modifier=True)

    return joined_objects


def bake_vertex_ambient_dirt(
    objects: Sequence[bpy.types.Object],
    blur_strength: float = 1.0,
    dirt_angle_deg: float = 90.0,
    clean_angle_deg: float = 110.0,
    dirt_only: bool = False,
    attribute_name: str = "Color",
) -> None:
    """Calculates procedural vertex ambient occlusion and crevice darkening (cavity shading).

    Generates subtle contact shadows in wheel wells, armor recesses, and undersides.
    Stores the result in a standard vertex color attribute named `attribute_name` (default 'Color')
    for direct ingestion into Unity Mesh.colors32.
    """
    for obj in objects:
        if obj.type != "MESH" or not obj.data.polygons:
            continue
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj

        mesh = obj.data
        color_attr = mesh.color_attributes.get(attribute_name)
        if color_attr is None:
            color_attr = mesh.color_attributes.new(name=attribute_name, type="BYTE_COLOR", domain="CORNER")
        mesh.color_attributes.active_color = color_attr

        try:
            bpy.ops.object.mode_set(mode="VERTEX_PAINT")
            bpy.ops.paint.vertex_color_dirt(
                blur_strength=blur_strength,
                dirt_angle=math.radians(dirt_angle_deg),
                clean_angle=math.radians(clean_angle_deg),
                dirt_only=dirt_only,
            )
            bpy.ops.object.mode_set(mode="OBJECT")
        except Exception as ex:
            print(f"Warning: vertex dirt bake skipped for {obj.name}: {ex}")
            bpy.ops.object.mode_set(mode="OBJECT")


def bake_advanced_vertex_shading(
    objects: Sequence[bpy.types.Object],
    attribute_name: str = "Color",
    wear_level: str = "field_tested",
    soot_intensity: float = 1.0,
    team_mask_materials: Sequence[bpy.types.Material] | None = None,
    exhaust_locs: Sequence[Vector | Tuple[float, float, float]] | None = None,
    muzzle_loc: Vector | Tuple[float, float, float] | None = None,
    structural_wear_mask: bool = False,
) -> None:
    """Calculates multi-channel procedural vertex shading targeting URP VehiclePBR_VertexWear:

    Channel R: Ambient Occlusion / Cavity Dirt (contact shadows in wheel wells, louvers, undersides).
               1.0 = zenith exposed; 0.25 - 0.40 = deep crevices and chassis underbelly.
    Channel G: Curvature / Edge Wear (bright glints on sharp chamfers simulating chipped paint).
               0.0 = flat faces and concave corners; 0.7 - 1.0 = sharp convex edges (>= 35 deg).
    Channel B: Thermal Soot / Weathering (darkening gradient around exhausts and muzzles).
               0.0 = clean base metal; 1.0 = heavy carbon soot with quadratic dropoff (1 - d/R)^2.
    Channel A: Team Color Mask (1.0 for tintable primary/light armor, 0.0 for fixed gunmetal/rubber).
    """
    if wear_level in ("none", "pristine"):
        wear_mult = 0.0
    elif wear_level == "factory_new":
        wear_mult = 0.25
    elif wear_level == "battle_hardened":
        wear_mult = 1.35
    else:  # field_tested
        wear_mult = 0.85

    team_mat_names = set()
    if team_mask_materials:
        team_mat_names = {m.name for m in team_mask_materials if m}

    ex_v = [Vector(e) for e in (exhaust_locs or [])]
    muz_v = Vector(muzzle_loc) if muzzle_loc is not None else None
    min_convex_angle = math.radians(35.0)

    for obj in objects:
        if obj.type != "MESH" or not obj.data.polygons:
            continue

        mesh = obj.data
        color_attr = mesh.color_attributes.get(attribute_name)
        if color_attr is None:
            color_attr = mesh.color_attributes.new(name=attribute_name, type="BYTE_COLOR", domain="CORNER")
        mesh.color_attributes.active_color = color_attr

        world_mat = obj.matrix_world
        normal_mat = world_mat.to_3x3()

        # Build BMesh for topological curvature and convexity analysis
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bm.normal_update()
        bm.verts.ensure_lookup_table()
        bm.edges.ensure_lookup_table()
        bm.faces.ensure_lookup_table()
        bm.verts.index_update()
        bm.faces.index_update()

        vert_wear = [0.0] * len(bm.verts)
        vert_concavity = [0.0] * len(bm.verts)
        corner_wear: dict[tuple[int, int], float] = {}

        for edge in bm.edges:
            if len(edge.link_faces) == 2:
                f1, f2 = edge.link_faces
                angle = edge.calc_face_angle()
                v_other = next((v for v in f2.verts if v != edge.verts[0] and v != edge.verts[1]), None)
                if v_other is not None:
                    # dot < 0 -> convex (exterior angle > 180), dot > 0 -> concave (interior angle < 180)
                    dot = (v_other.co - edge.verts[0].co).dot(f1.normal)
                    if dot < -1e-4 and angle >= min_convex_angle:
                        sharpness = min(1.0, (angle - min_convex_angle) / (math.pi * 0.5 - min_convex_angle))
                        edge_w = (0.55 + 0.45 * sharpness) * wear_mult
                        if structural_wear_mask and wear_level not in ("none", "pristine"):
                            edge_w = 0.70 + 0.30 * sharpness
                        if structural_wear_mask:
                            for face in edge.link_faces:
                                for v in edge.verts:
                                    key = (face.index, v.index)
                                    corner_wear[key] = max(corner_wear.get(key, 0.0), edge_w)
                        else:
                            for v in edge.verts:
                                vert_wear[v.index] = max(vert_wear[v.index], edge_w)
                    elif dot > 1e-4 and angle >= math.radians(25.0):
                        crease = min(1.0, angle / (math.pi * 0.5))
                        for v in edge.verts:
                            vert_concavity[v.index] = max(vert_concavity[v.index], crease)
            elif len(edge.link_faces) == 1:
                # Open border edge: exposed to scraping
                edge_w = 0.75 * wear_mult
                if structural_wear_mask and wear_level not in ("none", "pristine"):
                    edge_w = 0.70
                if structural_wear_mask:
                    for face in edge.link_faces:
                        for v in edge.verts:
                            key = (face.index, v.index)
                            corner_wear[key] = max(corner_wear.get(key, 0.0), edge_w)
                else:
                    for v in edge.verts:
                        vert_wear[v.index] = max(vert_wear[v.index], edge_w)

        vert_ao = [1.0] * len(bm.verts)
        vert_soot = [0.0] * len(bm.verts)

        for v in bm.verts:
            p_world = world_mat @ v.co
            n_world = (normal_mat @ v.normal).normalized() if v.normal.length > 1e-4 else Vector((0.0, 0.0, 1.0))
            up_factor = 0.5 * (n_world.z + 1.0)
            base_ao = 0.35 + 0.65 * up_factor
            if p_world.z < 0.35:
                ground_shadow = max(0.0, p_world.z / 0.35)
                base_ao = min(base_ao, 0.25 + 0.55 * ground_shadow)
            if vert_concavity[v.index] > 0.0:
                base_ao *= (1.0 - 0.35 * vert_concavity[v.index])
            vert_ao[v.index] = max(0.20, min(1.0, base_ao))

            soot_val = 0.0
            if soot_intensity > 0.0:
                for e_pos in ex_v:
                    d_ex = (p_world - e_pos).length
                    if d_ex < 0.45:
                        soot_val = max(soot_val, (1.0 - (d_ex / 0.45)) ** 2 * 0.95)
                if muz_v is not None:
                    d_muz = (p_world - muz_v).length
                    if d_muz < 0.60:
                        soot_val = max(soot_val, (1.0 - (d_muz / 0.60)) ** 2 * 1.0)
            vert_soot[v.index] = max(0.0, min(1.0, soot_val * soot_intensity))

        for poly in mesh.polygons:
            mat_idx = poly.material_index
            mat = obj.data.materials[mat_idx] if mat_idx < len(obj.data.materials) else None
            mat_name = mat.name if mat else ""

            if team_mat_names and mat_name in team_mat_names:
                team_alpha = 1.0
            elif any(k in mat_name for k in ("PrimaryArmor", "LightArmor", "TurretArmor", "ChassisTop", "HullTop")):
                team_alpha = 1.0
            elif any(k in mat_name for k in ("GunMetal", "Tracks", "Tread", "Wheel", "Exhaust", "Optics", "Grille", "Rubber", "Glass", "Decal")):
                team_alpha = 0.0
            else:
                team_alpha = 1.0 if "Armor" in mat_name else 0.0

            for loop_idx in poly.loop_indices:
                v_idx = mesh.loops[loop_idx].vertex_index
                edge_wear = corner_wear.get((poly.index, v_idx), 0.0) if structural_wear_mask else vert_wear[v_idx]
                color_attr.data[loop_idx].color = (vert_ao[v_idx], edge_wear, vert_soot[v_idx], team_alpha)

        bm.free()


def generate_lod_chain(
    batched_objects: Sequence[bpy.types.Object],
    prefix: str = "Unit",
    lod_levels: Sequence[Tuple[str, float]] = (("LOD0", 1.0), ("LOD1", 0.50), ("LOD2", 0.22)),
    smooth_by_angle: bool = True,
    smooth_angle_deg: float = 38.0,
    weighted_normals: bool = True,
) -> Dict[str, list[bpy.types.Object]]:
    """Generates an automated multi-level LOD hierarchy (LOD0, LOD1, LOD2) for Unity LODGroup.

    LOD0 retains 100% geometry and detail.
    LOD1 decimate target ~50% polygons (collapsing fine chamfers while preserving silhouette).
    LOD2 decimate target ~22% polygons (coarse blockout silhouette for far RTS distance).
    All levels preserve material slot assignments and angle-weighted normal smoothing.
    """
    lod_dict: Dict[str, list[bpy.types.Object]] = {}

    for lod_name, ratio in lod_levels:
        lod_objects: list[bpy.types.Object] = []
        for obj in batched_objects:
            if obj.type != "MESH":
                continue

            dup = obj.copy()
            dup.data = obj.data.copy()
            mat_suffix = obj.name.replace(f"{prefix}_", "")
            dup.name = f"{prefix}_{lod_name}_{mat_suffix}"
            dup.data.name = f"{prefix}_{lod_name}_{mat_suffix}_Mesh"
            bpy.context.collection.objects.link(dup)

            if ratio < 0.99:
                bpy.context.view_layer.objects.active = dup
                bpy.ops.object.select_all(action="DESELECT")
                dup.select_set(True)
                mod = dup.modifiers.new(f"Decimate_{lod_name}", "DECIMATE")
                mod.ratio = ratio
                mod.delimit = {"NORMAL", "MATERIAL"}
                bpy.ops.object.modifier_apply(modifier=mod.name)

            if smooth_by_angle and hasattr(bpy.ops.object, "shade_smooth_by_angle"):
                bpy.context.view_layer.objects.active = dup
                bpy.ops.object.select_all(action="DESELECT")
                dup.select_set(True)
                bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth_angle_deg))
                if weighted_normals:
                    apply_weighted_normals(dup, weight=50, keep_sharp=True, apply_modifier=True)

            lod_objects.append(dup)
        lod_dict[lod_name] = lod_objects

    return lod_dict


def generate_collision_hulls(
    model_objects: Sequence[bpy.types.Object],
    prefix: str = "Unit",
    include_turret_collider: bool = True,
    ground_offset_z: float = 0.04,
) -> list[bpy.types.Object]:
    """Generates low-poly physics collision hulls tagged for Unity BoxCollider / MeshCollider ingestion.

    Produces simplified, optimized bounding meshes:
    - `<Prefix>_COLLIDER_Hull`: Box/convex hull encompassing chassis and mobility components.
    - `<Prefix>_COLLIDER_Turret`: Box encompassing turret and weapon housings.
    Tagged with custom property `is_collider = True` and flat shading.
    """
    collider_objects: list[bpy.types.Object] = []
    mesh_objects = [o for o in model_objects if o.type == "MESH"]
    if not mesh_objects:
        return collider_objects

    turret_keywords = ("Turret", "Cannon", "Barrel", "Howitzer", "Gatling", "Missile", "Gun", "Visor")
    hull_objs = [o for o in mesh_objects if not any(t in o.name for t in turret_keywords)]
    turret_objs = [o for o in mesh_objects if any(t in o.name for t in turret_keywords)]

    if not hull_objs:
        hull_objs = mesh_objects

    def _compute_bounds(objs: list[bpy.types.Object]):
        min_v = Vector((float("inf"), float("inf"), float("inf")))
        max_v = Vector((float("-inf"), float("-inf"), float("-inf")))
        for o in objs:
            for corner in o.bound_box:
                w_co = o.matrix_world @ Vector(corner)
                min_v.x = min(min_v.x, w_co.x)
                min_v.y = min(min_v.y, w_co.y)
                min_v.z = min(min_v.z, w_co.z)
                max_v.x = max(max_v.x, w_co.x)
                max_v.y = max(max_v.y, w_co.y)
                max_v.z = max(max_v.z, w_co.z)
        center = (min_v + max_v) * 0.5
        size = (max_v - min_v)
        return center, size

    # 1. Hull Collider Box
    h_center, h_size = _compute_bounds(hull_objs)
    adj_h_center = Vector((h_center.x, h_center.y, h_center.z + ground_offset_z * 0.5))
    adj_h_size = Vector((max(0.4, h_size.x * 0.96), max(0.4, h_size.y * 0.96), max(0.2, h_size.z - ground_offset_z)))

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=adj_h_center)
    hull_col = bpy.context.object
    hull_col.name = f"{prefix}_COLLIDER_Hull"
    hull_col.scale = adj_h_size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    hull_col["is_collider"] = True
    hull_col["collider_type"] = "Box"
    for poly in hull_col.data.polygons:
        poly.use_smooth = False
    collider_objects.append(hull_col)

    # 2. Turret Collider Box
    if include_turret_collider and turret_objs:
        t_center, t_size = _compute_bounds(turret_objs)
        if t_size.length > 0.3:
            bpy.ops.mesh.primitive_cube_add(size=1.0, location=t_center)
            turret_col = bpy.context.object
            turret_col.name = f"{prefix}_COLLIDER_Turret"
            turret_col.scale = Vector((max(0.3, t_size.x * 0.94), max(0.3, t_size.y * 0.94), max(0.2, t_size.z * 0.94)))
            bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
            turret_col["is_collider"] = True
            turret_col["collider_type"] = "Box"
            for poly in turret_col.data.polygons:
                poly.use_smooth = False
            collider_objects.append(turret_col)

    return collider_objects


def profile_model_budget_and_drawcalls(
    objects: Sequence[bpy.types.Object],
    prefix: str = "Unit",
    target_budget: int = DEFAULT_FACE_BUDGET,
    lod_dict: Dict[str, list[bpy.types.Object]] | None = None,
) -> Dict[str, Any]:
    """Provides face-budget, true triangulation, and draw-call diagnostics by subsystem."""
    subsystems = {
        "Chassis & Hull": 0,
        "Turret & Cabin": 0,
        "Mobility & Wheels": 0,
        "Weapons": 0,
        "Optics & Greebles": 0,
    }
    total_verts = 0
    total_faces = 0
    total_triangles = 0
    materials_used = set()

    for obj in objects:
        if obj.type != "MESH":
            continue
        mesh = obj.data
        faces = len(mesh.polygons)
        mesh.calc_loop_triangles()
        triangles = len(mesh.loop_triangles)
        verts = len(obj.data.vertices)
        total_faces += faces
        total_triangles += triangles
        total_verts += verts
        for m in obj.data.materials:
            if m:
                materials_used.add(m.name)

        name = obj.name
        if prefix and name.startswith(prefix):
            name = name[len(prefix):]

        if any(k in name for k in ("Track", "Wheel", "Tire", "Leg", "Skimmer", "Thruster", "TiltRotor", "Outrigger")):
            subsystems["Mobility & Wheels"] += faces
        elif any(k in name for k in ("Cannon", "Barrel", "Howitzer", "Gatling", "Missile", "Gun", "Emitter", "Muzzle", "Railgun", "Flak", "Plasma", "Mortar", "Flamer", "EMP", "Laser", "VLS")):
            subsystems["Weapons"] += faces
        elif any(k in name for k in ("Turret", "Troop", "RollCage", "Cab", "FlightDeck", "Bridge")):
            subsystems["Turret & Cabin"] += faces
        elif any(k in name for k in ("Sensor", "Visor", "Antenna", "Radar", "Smoke", "Sponson", "Applique", "Grille", "Bolt", "Excavator", "Drum", "Hose", "Greeble", "Decal", "Marking")):
            subsystems["Optics & Greebles"] += faces
        else:
            subsystems["Chassis & Hull"] += faces

    pct_of_budget = round((total_faces / max(1, target_budget)) * 100, 1)

    lod_summary = {}
    lod_triangle_summary = {}
    if lod_dict:
        for lod_name, lod_objs in lod_dict.items():
            lod_meshes = [o.data for o in lod_objs if o.type == "MESH"]
            lod_summary[lod_name] = sum(len(mesh.polygons) for mesh in lod_meshes)
            lod_triangle_summary[lod_name] = 0
            for mesh in lod_meshes:
                mesh.calc_loop_triangles()
                lod_triangle_summary[lod_name] += len(mesh.loop_triangles)

    report = {
        "prefix": prefix,
        "total_faces": total_faces,
        "total_triangles": total_triangles,
        "total_vertices": total_verts,
        "materials_count": len(materials_used),
        "predicted_draw_calls": len(materials_used),
        "target_budget": target_budget,
        "budget_percentage": pct_of_budget,
        "subsystems": subsystems,
        "subsystem_faces": subsystems.copy(),
        "lod_levels": lod_summary,
        "lod_triangle_levels": lod_triangle_summary,
    }

    print(f"\n================ [ {prefix} Subsystem Budget & Profiling ] ================")
    print(f"Total Faces: {total_faces} | True Triangles: {total_triangles} | Vertices: {total_verts} | Materials: {len(materials_used)}")
    print(f"Face Budget Health: {total_faces}/{target_budget} faces ({pct_of_budget}%)")
    print("Subsystem Breakdown:")
    for sub, count in subsystems.items():
        sub_pct = round((count / max(1, total_faces)) * 100, 1)
        print(f"  - {sub:20s}: {count:5d} faces ({sub_pct:4.1f}%)")
    if lod_summary:
        print("LOD Chain Hierarchy:")
        for lod_name, lod_faces in lod_summary.items():
            print(f"  - {lod_name}: {lod_faces} faces / {lod_triangle_summary[lod_name]} triangles")
    print("========================================================================\n")
    return report



def validate_model_mesh_quality(
    objects: Sequence[bpy.types.Object],
    allow_hover: bool = False,
) -> Dict[str, Any]:
    """Inspects geometric quality, ground clearance, polycount, and material slots."""
    total_verts = 0
    total_faces = 0
    min_x, max_x = float("inf"), float("-inf")
    min_y, max_y = float("inf"), float("-inf")
    min_z, max_z = float("inf"), float("-inf")
    missing_material_objs = []

    for obj in objects:
        if obj.type != "MESH" or obj.name.endswith("_ShieldBubble"):
            continue
        mesh = obj.data
        total_verts += len(mesh.vertices)
        total_faces += len(mesh.polygons)
        if not obj.data.materials:
            missing_material_objs.append(obj.name)

        matrix = obj.matrix_world
        for vert in mesh.vertices:
            w_co = matrix @ vert.co
            min_x = min(min_x, w_co.x)
            max_x = max(max_x, w_co.x)
            min_y = min(min_y, w_co.y)
            max_y = max(max_y, w_co.y)
            min_z = min(min_z, w_co.z)
            max_z = max(max_z, w_co.z)

    dim_w = max_x - min_x if max_x >= min_x else 0.0
    dim_l = max_y - min_y if max_y >= min_y else 0.0
    dim_h = max_z - min_z if max_z >= min_z else 0.0
    max_ground = 0.55 if allow_hover else 0.08
    ground_ok = (-0.02 <= min_z <= max_ground)

    results = {
        "object_count": len(objects),
        "total_vertices": total_verts,
        "total_polygons": total_faces,
        "bounds": {
            "width_x": round(dim_w, 3),
            "length_y": round(dim_l, 3),
            "height_z": round(dim_h, 3),
            "min_z": round(min_z, 4),
        },
        "ground_contact_valid": ground_ok,
        "missing_materials": missing_material_objs,
    }

    print("\n--- Model Quality Assessment ---")
    print(f"Objects: {results['object_count']}")
    print(f"Polygons: {results['total_polygons']}, Vertices: {results['total_vertices']}")
    print(f"Dimensions: {dim_w:.2f}m (W) x {dim_l:.2f}m (L) x {dim_h:.2f}m (H)")
    print(f"Lowest point (Z min): {min_z:.4f}m (Ground OK: {ground_ok})")
    if missing_material_objs:
        print(f"WARNING: Objects missing materials: {missing_material_objs}")
    else:
        print("Materials: All objects assigned valid PBR materials.")
    print("--------------------------------\n")
    return results


# ---------------------------------------------------------------------------
# 8. Rendering & Multi-View Studio
# ---------------------------------------------------------------------------

def frame_camera_to_model(
    camera: bpy.types.Object,
    model_objects: Sequence[bpy.types.Object],
    reference_target: Vector,
    margin: float = 1.15,
) -> Vector:
    """Aim an orthographic camera at the model bounds and expand it if needed."""
    mesh_objects = [obj for obj in model_objects if obj.type == "MESH"]
    if not mesh_objects:
        raise ValueError("Cannot frame a model without mesh objects")

    corners = [
        obj.matrix_world @ Vector(corner)
        for obj in mesh_objects
        for corner in obj.bound_box
    ]
    minimum = Vector(tuple(min(point[axis] for point in corners) for axis in range(3)))
    maximum = Vector(tuple(max(point[axis] for point in corners) for axis in range(3)))
    target = (minimum + maximum) * 0.5

    camera_offset = camera.location - reference_target
    camera.location = target + camera_offset
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    bpy.context.view_layer.update()

    camera_space_corners = [camera.matrix_world.inverted() @ point for point in corners]
    projected_width = max(point.x for point in camera_space_corners) - min(
        point.x for point in camera_space_corners
    )
    projected_height = max(point.y for point in camera_space_corners) - min(
        point.y for point in camera_space_corners
    )
    scene = bpy.context.scene
    aspect = scene.render.resolution_x / max(1, scene.render.resolution_y)
    required_scale = max(projected_width, projected_height * aspect) * margin
    camera.data.ortho_scale = max(camera.data.ortho_scale, required_scale)
    return target


def _setup_studio_lighting_and_floor() -> bpy.types.Object:
    """Configures floor shadow plane, 3-point area lights, and AgX EEVEE color management."""
    # Floor shadow plane
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0.0, 0.0, -0.015))
    floor = bpy.context.object
    floor.name = "PreviewFloor"
    floor_mat = make_material("PreviewFloorMat", (0.035, 0.052, 0.065), 0.0, 0.92)
    finish_mesh(floor, floor_mat)

    # 3-Point Area Lights
    lights = [
        ("Key", (3.5, 4.5, 6.0), 850, 4.0),
        ("Fill", (-4.2, 1.0, 3.5), 520, 3.5),
        ("Rim", (-1.5, -4.2, 4.5), 980, 3.0),
    ]
    for name, pos, energy, size in lights:
        bpy.ops.object.light_add(type="AREA", location=pos)
        l = bpy.context.object
        l.name = f"PreviewLight_{name}"
        l.data.energy = energy
        l.data.shape = "DISK"
        l.data.size = size
        direction = Vector((0.0, 0.0, 0.6)) - l.location
        l.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 900
    scene.render.resolution_y = 760
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "AgX"
    scene.world.color = (0.035, 0.045, 0.065)
    return floor


def setup_preview_studio(
    model_objects: Sequence[bpy.types.Object],
    preview_path: Path,
    ortho_scale: float = 5.8,
) -> None:
    """Configures 3-point studio lighting, floor shadow plane, and AgX isometric RTS camera."""
    preview_path.parent.mkdir(parents=True, exist_ok=True)
    _setup_studio_lighting_and_floor()

    # Camera: Standard RTS orthographic view
    bpy.ops.object.camera_add(location=(4.8, 5.8, 3.6))
    camera = bpy.context.object
    camera.name = "PreviewCamera"
    target = Vector((0.0, 0.0, 0.72))
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = ortho_scale
    frame_camera_to_model(camera, model_objects, target)
    scene = bpy.context.scene
    scene.camera = camera
    scene.render.filepath = str(preview_path)


def setup_multi_view_studio(
    model_objects: Sequence[bpy.types.Object],
    preview_dir: Path,
    base_name: str,
    ortho_scale: float = 5.8,
    views: Sequence[str] = ("ISOMETRIC", "TOP_DOWN", "FRONT", "SIDE"),
    render_all: bool = False,
) -> dict[str, bpy.types.Object]:
    """Sets up AgX studio lighting, floor shadow plane, and calibrated cameras for multiple perspectives."""
    preview_dir.mkdir(parents=True, exist_ok=True)
    _setup_studio_lighting_and_floor()

    # View Cameras
    camera_configs = {
        "ISOMETRIC": ((4.8, 5.8, 3.6), Vector((0.0, 0.0, 0.72)), ortho_scale),
        "TOP_DOWN": ((0.0, 0.001, 8.5), Vector((0.0, 0.0, 0.6)), ortho_scale * 1.1),
        "FRONT": ((0.0, 7.5, 1.2), Vector((0.0, 0.0, 0.72)), ortho_scale * 0.92),
        "SIDE": ((-7.5, 0.0, 1.2), Vector((0.0, 0.0, 0.72)), ortho_scale * 0.92),
    }

    cameras = {}
    for view_name in views:
        if view_name not in camera_configs:
            continue
        cam_pos, target, scale = camera_configs[view_name]
        bpy.ops.object.camera_add(location=cam_pos)
        cam = bpy.context.object
        cam.name = f"PreviewCamera_{view_name}"
        cam.data.type = "ORTHO"
        cam.data.ortho_scale = scale
        direction = target - cam.location
        cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        frame_camera_to_model(cam, model_objects, target)
        cameras[view_name] = cam

    scene = bpy.context.scene
    if "ISOMETRIC" in cameras:
        scene.camera = cameras["ISOMETRIC"]
    elif cameras:
        scene.camera = next(iter(cameras.values()))

    if render_all:
        for v_name, cam in cameras.items():
            scene.camera = cam
            out_file = preview_dir / f"{base_name}_{v_name.lower()}.png"
            scene.render.filepath = str(out_file)
            bpy.ops.render.render(write_still=True)
            print(f"Rendered {v_name} perspective to {out_file}")

    return cameras


# ---------------------------------------------------------------------------
# 9. Unity FBX Export
# ---------------------------------------------------------------------------

def merge_meshes_by_material(
    model_objects: Sequence[bpy.types.Object],
    model_prefix: str,
    material_keys: dict[bpy.types.Material, str] | None = None,
) -> list[bpy.types.Object]:
    """Create export-only mesh batches per material while preserving the editable source objects."""
    batches: dict[str, tuple[bpy.types.Material, list[bpy.types.Object]]] = {}
    for source in model_objects:
        if source.type != "MESH":
            continue
        if len(source.data.materials) != 1 or source.data.materials[0] is None:
            raise ValueError(f"{source.name} must have exactly one material before FBX batching")

        material = source.data.materials[0]
        material_key = material_keys.get(material) if material_keys is not None else material.name.replace(" ", "_")
        if not material_key:
            raise ValueError(f"No export batch material key was provided for {material.name}")

        duplicate = source.copy()
        duplicate.data = source.data.copy()
        bpy.context.collection.objects.link(duplicate)
        if material_key not in batches:
            batches[material_key] = (material, [])
        batches[material_key][1].append(duplicate)

    if not batches:
        raise ValueError(f"No mesh objects were provided for {model_prefix} export")

    exported_objects = []
    for material_key, (material, duplicates) in sorted(batches.items()):
        bpy.ops.object.select_all(action="DESELECT")
        for duplicate in duplicates:
            duplicate.select_set(True)
        bpy.context.view_layer.objects.active = duplicates[0]
        bpy.ops.object.join()

        combined = duplicates[0]
        combined.name = f"{model_prefix}_Batch_{material_key}"
        combined.data.name = f"{model_prefix}_Mesh_{material_key}"
        for polygon in combined.data.polygons:
            polygon.material_index = 0
        combined.data.materials.clear()
        combined.data.materials.append(material)

        if hasattr(bpy.ops.object, "shade_smooth_by_angle"):
            bpy.ops.object.shade_smooth_by_angle(angle=math.radians(38.0))
        apply_weighted_normals(combined, weight=60, keep_sharp=True, apply_modifier=True)
        exported_objects.append(combined)

    return exported_objects


def export_unity_fbx(
    fbx_path: Path,
    model_objects: Sequence[bpy.types.Object],
    include_empties: bool = False,
    include_armature: bool = False,
    bake_anim: bool = False,
) -> None:
    """Exports model objects to FBX with optimal scale and axis mappings for Unity."""
    fbx_path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in model_objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = model_objects[0]

    obj_types = {"MESH"}
    if include_empties:
        obj_types.add("EMPTY")
    if include_armature:
        obj_types.add("ARMATURE")

    bpy.ops.export_scene.fbx(
        filepath=str(fbx_path),
        use_selection=True,
        object_types=obj_types,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_NONE",
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        path_mode="AUTO",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=False,
        add_leaf_bones=False,
        use_custom_props=False,
        bake_anim=bake_anim,
        bake_anim_use_all_actions=bake_anim,
        bake_anim_use_nla_strips=bake_anim,
        bake_anim_step=1.0,
    )


def export_gameplay_metadata(
    output_path: Path,
    unit_name: str,
    archetype: str,
    bounds: Dict[str, Any],
    sockets: Sequence[bpy.types.Object],
    turret_center: Vector | Tuple[float, float, float] | None = None,
    gun_pivot: Vector | Tuple[float, float, float] | None = None,
    recoil_distance: float = 0.22,
    elevation_range: Tuple[float, float] = (-10.0, 35.0),
    yaw_speed: float = 45.0,
    visual_root_path: str = "Vehicle",
) -> Dict[str, Any]:
    """Generates a companion JSON specification with exact Unity engine bindings and kinematics."""
    output_path.parent.mkdir(parents=True, exist_ok=True)

    socket_dict = {}
    for s in sockets:
        clean_name = s.name.replace(f"{unit_name}_", "")
        fwd = Vector((0.0, 1.0, 0.0))
        fwd.rotate(s.rotation_euler)
        socket_dict[clean_name] = {
            "position": [round(s.location.x, 3), round(s.location.y, 3), round(s.location.z, 3)],
            "forward": [round(fwd.x, 3), round(fwd.y, 3), round(fwd.z, 3)],
        }

    tc = Vector(turret_center) if turret_center is not None else Vector((0.0, 0.0, 1.0))
    gp = Vector(gun_pivot) if gun_pivot is not None else Vector((0.0, 1.5, 1.2))

    dim_w = bounds.get("width_x", 2.4)
    dim_l = bounds.get("length_y", 3.0)
    dim_h = bounds.get("height_z", 1.6)

    template_map = {
        "tank": ["PCXBaseTank"],
        "walker": ["GatlingWalker"],
        "bike": ["BombBike"],
        "artillery": ["ArtilleryTank"],
        "arc_tank": ["LightningTank"],
        "hover": ["PCXBaseTank"],
        "spider": ["GatlingWalker"],
        "vtol": ["PCXBaseTank"],
        "halftrack": ["PCXBaseTank"],
        "super_heavy": ["ArtilleryTank"],
        "buggy": ["BombBike"],
        "flak_anti_air": ["PCXBaseTank"],
        "siege_crawler": ["ArtilleryTank", "GatlingWalker"],
        "drone_carrier": ["PCXBaseTank"],
        "quad_mech": ["GatlingWalker"],
        "combat_harvester": ["PCXBaseTank"],
        "colossus_titan": ["GatlingWalker"],
        "hover_cruiser": ["PCXBaseTank"],
        "heavy_dropship": ["PCXBaseTank"],
        "mobile_sam_launcher": ["PCXBaseTank"],
        "recovery_crawler": ["PCXBaseTank"],
        "mine_layer": ["PCXBaseTank"],
        "sniper_tankette": ["PCXBaseTank"],
        "hexapod_assault": ["GatlingWalker"],
        "assault_hovercraft": ["PCXBaseTank"],
        "bridge_layer": ["PCXBaseTank"],
        "command_hq_rig": ["PCXBaseTank"],
        "flying_wing_bomber": ["PCXBaseTank"],
        "mortar_carrier": ["ArtilleryTank"],
        "siege_drill_mole": ["PCXBaseTank"],
        "heavy_crane_mech": ["GatlingWalker"],
        "nanite_support_rover": ["PCXBaseTank"],
        "gravity_siege_breaker": ["PCXBaseTank", "ArtilleryTank"],
        "tactical_relay_carrier": ["PCXBaseTank"],
        "amphibious_assault_tank": ["PCXBaseTank"],
        "plasma_mortar_strider": ["GatlingWalker"],
        "stealth_recon_skimmer": ["PCXBaseTank"],
    }

    if archetype in ("bike", "buggy"):
        resolved_root_path = "GameObject/BombBike"
    else:
        resolved_root_path = visual_root_path

    meta = {
        "unitName": unit_name,
        "archetype": archetype,
        "visualRootPath": resolved_root_path,
        "compatibleNativeTemplates": template_map.get(archetype, ["PCXBaseTank"]),
        "boxCollider": {
            "center": [0.0, 0.0, round(dim_h * 0.5, 3)],
            "size": [round(dim_w, 3), round(dim_l, 3), round(dim_h, 3)],
        },
        "turretKinematics": {
            "yawCenter": [round(tc.x, 3), round(tc.y, 3), round(tc.z, 3)],
            "pitchCenter": [round(gp.x, 3), round(gp.y, 3), round(gp.z, 3)],
            "minElevationDeg": elevation_range[0],
            "maxElevationDeg": elevation_range[1],
            "yawSpeedDegPerSec": yaw_speed,
            "recoilDistanceMeters": recoil_distance,
        },
        "damageThresholds": {
            "smokeHpFraction": 0.50,
            "fireHpFraction": 0.25,
        },
        "rigging": {
            "rigType": "skeletal_armature" if archetype in ("walker", "colossus_titan", "quad_mech", "spider", "siege_crawler", "hexapod_assault", "heavy_crane_mech") else "chassis_kinematic",
            "hasArmature": True,
            "animatorParameters": ["RCL_IsMoving", "RCL_Fire", "RCL_Death"],
            "actionClips": ["Action_Idle", "Action_Walk", "Action_Drive", "Action_FireRecoil", "Action_Death"],
        },
        "sockets": socket_dict,
    }

    output_path.write_text(json.dumps(meta, indent=2), encoding="utf-8")
    return meta


def make_wreckage_variant(
    source_objects: Sequence[bpy.types.Object],
    prefix: str,
    output_fbx_path: Path,
    output_blend_path: Path | None = None,
    archetype: str = "tank",
    severity: str = "catastrophic",
    turret_keywords: Sequence[str] = (
        "Turret", "Cannon", "Barrel", "Howitzer", "Gatling", "Missile", "Gun", "Visor", "BeamLaser", "VLS", "Flamer", "EMP", "Nanite", "Gravity", "Prow", "Dispenser", "Mast"
    ),
    smooth_by_angle: bool = True,
    smooth_angle_deg: float = 38.0,
) -> list[bpy.types.Object]:
    """Generates an archetype-aware battle-destroyed / burned wreckage model variant with severity scaling."""
    # Material tuning by severity
    if severity == "light":
        charred_armor = make_material(f"{prefix}_Wreck_Charred", (0.16, 0.16, 0.18), metallic=0.15, roughness=0.72)
        burned_metal = make_material(f"{prefix}_Wreck_Metal", (0.22, 0.18, 0.16), metallic=0.45, roughness=0.65)
        soot_val = 0.65
    elif severity == "medium":
        charred_armor = make_material(f"{prefix}_Wreck_Charred", (0.11, 0.11, 0.12), metallic=0.08, roughness=0.82)
        burned_metal = make_material(f"{prefix}_Wreck_Metal", (0.16, 0.13, 0.11), metallic=0.35, roughness=0.76)
        soot_val = 0.85
    else:  # catastrophic
        charred_armor = make_material(f"{prefix}_Wreck_Charred", (0.05, 0.05, 0.06), metallic=0.04, roughness=0.92)
        burned_metal = make_material(f"{prefix}_Wreck_Metal", (0.11, 0.09, 0.08), metallic=0.25, roughness=0.85)
        soot_val = 1.0

    wreck_objs = []
    turret_objs = []
    hull_objs = []

    for obj in source_objects:
        if obj.type != "MESH":
            continue
        dup = obj.copy()
        dup.data = obj.data.copy()
        dup.name = f"{prefix}_Wreck_{obj.name}"
        bpy.context.collection.objects.link(dup)

        orig_mat = obj.data.materials[0] if obj.data.materials else None
        dup.data.materials.clear()
        if orig_mat and any(m in orig_mat.name for m in ("PrimaryArmor", "LightArmor", "Warning", "Emissive")):
            dup.data.materials.append(charred_armor)
        else:
            dup.data.materials.append(burned_metal)

        part_name = obj.name
        if prefix and part_name.startswith(prefix):
            part_name = part_name[len(prefix):]
        is_turret_part = any(k in part_name for k in turret_keywords)
        if is_turret_part:
            turret_objs.append(dup)
        else:
            hull_objs.append(dup)

    # Apply kinematic transformations based on severity and archetype
    if severity == "light":
        for t_obj in turret_objs:
            t_obj.rotation_euler = Vector((math.radians(4.5), math.radians(-3.0), math.radians(6.5)))
            t_obj.location += Vector((0.04, -0.05, 0.02))
            wreck_objs.append(t_obj)
        wreck_objs.extend(hull_objs)

    elif severity == "medium":
        for t_obj in turret_objs:
            t_obj.rotation_euler = Vector((math.radians(12.5), math.radians(-9.0), math.radians(17.0)))
            t_obj.location += Vector((0.14, -0.18, 0.05))
            wreck_objs.append(t_obj)
        roll = math.radians(3.2)
        for h_obj in hull_objs:
            h_obj.rotation_euler.y += roll
            wreck_objs.append(h_obj)

    else:  # catastrophic
        if archetype in ("tank", "artillery", "flak_anti_air", "arc_tank", "beam_destroyer", "halftrack", "thermobaric_tank", "demolition_breacher", "recovery_crawler", "mine_layer", "sniper_tankette", "bridge_layer", "mortar_carrier", "siege_drill_mole", "gravity_siege_breaker", "amphibious_assault_tank"):
            roll = math.radians(5.5)
            for h_obj in hull_objs:
                h_obj.rotation_euler.y += roll
                wreck_objs.append(h_obj)

            turret_offset = Vector((1.70, -0.25, -0.55))
            turret_rot = Vector((math.radians(24.0), math.radians(-16.0), math.radians(55.0)))
            for t_obj in turret_objs:
                t_obj.location += turret_offset
                t_obj.rotation_euler = turret_rot
                if t_obj.location.z < 0.05:
                    t_obj.location.z = 0.05
                wreck_objs.append(t_obj)

            debris = make_thrown_track_debris(
                f"{prefix}_ThrownTrack",
                origin=(-1.40, -0.80, 0.0),
                length=1.95,
                width=0.34,
                cleat_count=8,
                material=burned_metal,
                cleat_mat=charred_armor,
            )
            wreck_objs.extend(debris)

        elif archetype in ("walker", "colossus_titan", "artillery_strider", "spider", "quad_mech", "siege_crawler", "hexapod_assault", "heavy_crane_mech", "plasma_mortar_strider"):
            drop_z = 0.55 if archetype in ("colossus_titan", "quad_mech") else 0.35
            for h_obj in hull_objs:
                h_obj.location.z = max(0.04, h_obj.location.z - drop_z)
                h_obj.rotation_euler.x += math.radians(-8.0)
                wreck_objs.append(h_obj)
            for t_obj in turret_objs:
                t_obj.location.z = max(0.08, t_obj.location.z - drop_z)
                t_obj.rotation_euler.x += math.radians(26.0)
                wreck_objs.append(t_obj)

        elif archetype in ("hover", "hover_cruiser", "vtol", "heavy_dropship", "assault_hovercraft", "flying_wing_bomber", "stealth_recon_skimmer"):
            for h_obj in hull_objs:
                h_obj.location.z = max(0.03, h_obj.location.z - 0.38)
                h_obj.rotation_euler.x += math.radians(7.5)
                h_obj.rotation_euler.y += math.radians(-5.5)
                wreck_objs.append(h_obj)
            for t_obj in turret_objs:
                t_obj.location.z = max(0.05, t_obj.location.z - 0.38)
                t_obj.rotation_euler = Vector((math.radians(18.0), math.radians(-12.0), math.radians(35.0)))
                wreck_objs.append(t_obj)

        else:
            for h_obj in hull_objs:
                h_obj.location.z = max(0.04, h_obj.location.z - 0.18)
                h_obj.rotation_euler.y += math.radians(4.2)
                wreck_objs.append(h_obj)
            for t_obj in turret_objs:
                t_obj.location += Vector((0.25, -0.22, 0.05))
                t_obj.rotation_euler = Vector((math.radians(16.0), math.radians(-14.0), math.radians(22.0)))
                wreck_objs.append(t_obj)

    bake_advanced_vertex_shading(
        wreck_objs,
        wear_level="battle_hardened",
        soot_intensity=soot_val,
    )

    batched_wreckage = batch_and_join_by_material(
        wreck_objs,
        prefix=f"{prefix}_Wreckage",
        smooth_by_angle=smooth_by_angle,
        smooth_angle_deg=smooth_angle_deg,
    )

    if output_blend_path is not None:
        output_blend_path.parent.mkdir(parents=True, exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=str(output_blend_path))

    export_unity_fbx(output_fbx_path, batched_wreckage, include_empties=False)
    return batched_wreckage


# ---------------------------------------------------------------------------
# 10. Lightmap UVs, Card Sprites & Fluent Vehicle Builder
# ---------------------------------------------------------------------------

def generate_lightmap_uvs(
    objects: Sequence[bpy.types.Object],
    angle_limit_deg: float = 66.0,
    island_margin: float = 0.02,
) -> None:
    """Generates clean, non-overlapping Smart UV coordinates across all mesh objects for Unity lightmapping."""
    for obj in objects:
        if obj.type != "MESH":
            continue
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(
            angle_limit=math.radians(angle_limit_deg),
            island_margin=island_margin,
            area_weight=0.0,
            correct_aspect=True,
            scale_to_bounds=False,
        )
        bpy.ops.object.mode_set(mode="OBJECT")
        obj.select_set(False)


def render_tactical_card_sprite(
    model_objects: Sequence[bpy.types.Object],
    output_path: Path,
    resolution: Tuple[int, int] = (128, 128),
    ortho_scale: float = 4.2,
) -> None:
    """Renders a 128x128 point-filtered tactical card sprite with a stylized dark gradient backdrop."""
    output_path.parent.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene

    # Card camera: 35° elevation angled toward front 3/4 profile
    bpy.ops.object.camera_add(location=(3.8, -4.5, 3.2))
    card_cam = bpy.context.object
    card_cam.name = "TacticalCardCamera"
    card_cam.data.type = "ORTHO"
    card_cam.data.ortho_scale = ortho_scale
    target = Vector((0.0, 0.0, 0.75))
    card_cam.rotation_euler = (target - card_cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = card_cam

    # Dark tactical vignette background plane
    bpy.ops.mesh.primitive_plane_add(size=30, location=(0.0, 0.0, -0.05))
    card_floor = bpy.context.object
    card_floor.name = "CardBackdrop"
    card_mat = make_material("CardBackdropMat", (0.04, 0.07, 0.09), 0.0, 0.95)
    finish_mesh(card_floor, card_mat)

    # Dramatic rim lighting for card drafting clarity
    lights = [
        ("CardKey", (3.0, -3.5, 4.5), 900, 3.0),
        ("CardRim", (-3.5, 3.5, 4.0), 1200, 2.5),
        ("CardFill", (-2.5, -2.0, 2.0), 400, 3.0),
    ]
    for name, pos, energy, size in lights:
        bpy.ops.object.light_add(type="AREA", location=pos)
        l = bpy.context.object
        l.name = name
        l.data.energy = energy
        l.data.shape = "DISK"
        l.data.size = size
        l.rotation_euler = (target - l.location).to_track_quat("-Z", "Y").to_euler()

    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = resolution[0]
    scene.render.resolution_y = resolution[1]
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "AgX"
    scene.render.filepath = str(output_path)
    scene.world.color = (0.04, 0.06, 0.08)

    bpy.ops.render.render(write_still=True)


def make_dozer_blade(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    width: float = 2.4,
    height: float = 0.45,
    armor_mat: bpy.types.Material | None = None,
    ram_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a heavy armored dozer ram blade with hydraulic push rams."""
    objects = []
    loc_v = Vector(location)
    # Main angled blade shield
    objects.append(make_box(
        f"{prefix}_Blade",
        (width, 0.12, height),
        loc_v, armor_mat, bevel=0.035,
        rotation=(math.radians(-16), 0.0, 0.0)
    ))
    # Push rams (Left & Right)
    for side in (-1, 1):
        ram_x = loc_v.x + side * (width * 0.38)
        objects.append(make_rod(
            f"{prefix}_PushRam_{'L' if side < 0 else 'R'}",
            (ram_x, loc_v.y - 0.48, loc_v.z + 0.12),
            (ram_x, loc_v.y, loc_v.z),
            0.045, ram_mat, vertices=8
        ))
    return objects


def make_ducted_tiltrotor_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    radius: float = 0.44,
    depth: float = 0.26,
    tilt_deg: float = 14.0,
    duct_mat: bpy.types.Material | None = None,
    rotor_mat: bpy.types.Material | None = None,
    hub_mat: bpy.types.Material | None = None,
    pylon_mat: bpy.types.Material | None = None,
    glow_mat: bpy.types.Material | None = None,
    blade_count: int = 3,
    pylon_inboard_x: float = -0.32,
) -> list[bpy.types.Object]:
    """Generates an aerodynamic ducted tilt-rotor pod with shroud, spinner hub, blades, and swivel mount."""
    objects = []
    loc_v = Vector(location)
    rot_tilt = (math.radians(tilt_deg), 0.0, 0.0)

    # 1. Main outer aerodynamic cowl shroud
    objects.append(make_cylinder(
        f"{prefix}_DuctCowl",
        radius=radius, depth=depth,
        location=loc_v, material=duct_mat,
        rotation=rot_tilt, vertices=14, bevel=0.025
    ))

    # 2. Intake lip ring
    tilt_r = math.radians(tilt_deg)
    lip_offset = Vector((0.0, -math.sin(tilt_r) * (depth * 0.45), math.cos(tilt_r) * (depth * 0.45)))
    objects.append(make_cylinder(
        f"{prefix}_IntakeLip",
        radius=radius * 1.05, depth=0.04,
        location=loc_v + lip_offset, material=hub_mat,
        rotation=rot_tilt, vertices=14, bevel=0.012
    ))

    # 3. Inner recessed cavity duct wall
    objects.append(make_cylinder(
        f"{prefix}_InnerDuct",
        radius=radius * 0.82, depth=depth * 0.95,
        location=loc_v, material=hub_mat,
        rotation=rot_tilt, vertices=14, bevel=0.015
    ))

    # 4. Central aerodynamic spinner hub
    objects.append(make_cylinder(
        f"{prefix}_SpinnerHub",
        radius=radius * 0.28, depth=depth * 1.25,
        location=loc_v, material=hub_mat,
        rotation=rot_tilt, vertices=10, bevel=0.02
    ))

    # 5. Rotor blades radiating from spinner hub
    inner_span = radius * 0.54
    blade_width = radius * 0.22
    for b_idx in range(blade_count):
        angle = (2.0 * math.pi * b_idx) / blade_count
        rx = math.cos(angle)
        rz = math.sin(angle)
        ry_tilted = -rz * math.sin(tilt_r)
        rz_tilted = rz * math.cos(tilt_r)
        blade_center = loc_v + Vector((rx * inner_span * 0.5, ry_tilted * inner_span * 0.5, rz_tilted * inner_span * 0.5))

        objects.append(make_box(
            f"{prefix}_Blade_{b_idx+1}",
            (inner_span, blade_width, 0.025),
            blade_center, rotor_mat, bevel=0.006,
            rotation=(rot_tilt[0], math.radians(18), angle)
        ))

    # 6. Swivel pivot bracket / inboard pylon
    pylon_start = loc_v + Vector((pylon_inboard_x, 0.0, 0.0))
    objects.append(make_rod(
        f"{prefix}_MountPylon",
        pylon_start, loc_v,
        radius * 0.12, pylon_mat, vertices=8
    ))
    objects.append(make_cylinder(
        f"{prefix}_SwivelBearing",
        radius * 0.22, 0.08,
        loc_v + Vector((pylon_inboard_x * 0.35, 0.0, 0.0)),
        pylon_mat, rotation=(0.0, math.radians(90), 0.0), vertices=10, bevel=0.01
    ))

    # 7. Optional glowing exhaust core ring
    if glow_mat is not None:
        exhaust_offset = Vector((0.0, math.sin(tilt_r) * (depth * 0.45), -math.cos(tilt_r) * (depth * 0.45)))
        objects.append(make_cylinder(
            f"{prefix}_GlowCore",
            radius=radius * 0.52, depth=0.03,
            location=loc_v + exhaust_offset, material=glow_mat,
            rotation=rot_tilt, vertices=12, bevel=0.005
        ))

    return objects


def make_tubular_rollcage_assembly(
    prefix: str,
    size: Tuple[float, float, float] = (1.15, 1.45, 0.78),
    location: Tuple[float, float, float] | Vector = (0.0, 0.0, 1.0),
    bar_radius: float = 0.034,
    tube_mat: bpy.types.Material | None = None,
    roof_mat: bpy.types.Material | None = None,
    visor_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an off-road tubular roll cage with A/B pillars, roof crossbars, and armored sun-visor plate."""
    objects = []
    loc_v = Vector(location)
    hx = size[0] * 0.5
    hy = size[1] * 0.5
    hz = size[2] * 0.5

    # 1. Main B-pillar roll hoop (central arch)
    hoop_y = loc_v.y - hy * 0.15
    for sx in (-1, 1):
        tag = "L" if sx < 0 else "R"
        objects.append(make_rod(
            f"{prefix}_BPillar_{tag}",
            (loc_v.x + sx * hx, hoop_y, loc_v.z - hz),
            (loc_v.x + sx * (hx * 0.85), hoop_y, loc_v.z + hz),
            bar_radius, tube_mat, vertices=8
        ))
    objects.append(make_rod(
        f"{prefix}_BRoofBar",
        (loc_v.x - hx * 0.85, hoop_y, loc_v.z + hz),
        (loc_v.x + hx * 0.85, hoop_y, loc_v.z + hz),
        bar_radius, tube_mat, vertices=8
    ))

    # 2. Sloped front A-pillars (windshield posts)
    front_y = loc_v.y + hy
    for sx in (-1, 1):
        tag = "L" if sx < 0 else "R"
        objects.append(make_rod(
            f"{prefix}_APillar_{tag}",
            (loc_v.x + sx * (hx * 0.92), front_y, loc_v.z - hz),
            (loc_v.x + sx * (hx * 0.78), loc_v.y + hy * 0.40, loc_v.z + hz),
            bar_radius, tube_mat, vertices=8
        ))
        objects.append(make_rod(
            f"{prefix}_RoofRail_{tag}",
            (loc_v.x + sx * (hx * 0.78), loc_v.y + hy * 0.40, loc_v.z + hz),
            (loc_v.x + sx * (hx * 0.85), hoop_y, loc_v.z + hz),
            bar_radius, tube_mat, vertices=8
        ))

    # 3. Windshield cross header bar
    objects.append(make_rod(
        f"{prefix}_WindshieldHeader",
        (loc_v.x - hx * 0.78, loc_v.y + hy * 0.40, loc_v.z + hz),
        (loc_v.x + hx * 0.78, loc_v.y + hy * 0.40, loc_v.z + hz),
        bar_radius, tube_mat, vertices=8
    ))

    # 4. Rear diagonal stays (C-pillars down to rear deck)
    rear_y = loc_v.y - hy
    for sx in (-1, 1):
        tag = "L" if sx < 0 else "R"
        objects.append(make_rod(
            f"{prefix}_CStay_{tag}",
            (loc_v.x + sx * (hx * 0.85), hoop_y, loc_v.z + hz),
            (loc_v.x + sx * (hx * 0.88), rear_y, loc_v.z - hz),
            bar_radius * 0.92, tube_mat, vertices=8
        ))

    # 5. Armored roof sun-visor plate
    if roof_mat is not None:
        roof_l = (loc_v.y + hy * 0.40) - hoop_y
        roof_cy = hoop_y + roof_l * 0.5
        objects.append(make_box(
            f"{prefix}_RoofPlate",
            (size[0] * 0.80, roof_l * 1.08, 0.035),
            (loc_v.x, roof_cy, loc_v.z + hz + 0.02),
            roof_mat, bevel=0.012
        ))

    # 6. Windshield sun-visor brow shield
    if visor_mat is not None:
        objects.append(make_box(
            f"{prefix}_SunVisor",
            (size[0] * 0.76, 0.14, 0.05),
            (loc_v.x, loc_v.y + hy * 0.44, loc_v.z + hz - 0.02),
            visor_mat, bevel=0.01,
            rotation=(math.radians(24), 0.0, 0.0)
        ))

    return objects


def make_heavy_howitzer_assembly(
    prefix: str,
    gun_pivot: Tuple[float, float, float] | Vector,
    direction: Tuple[float, float, float] | Vector,
    caliber_radius: float = 0.135,
    barrel_length: float = 1.15,
    armor_mat: bpy.types.Material | None = None,
    barrel_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a heavy siege mortar / howitzer with tapered barrel, reinforced breech, and recoil cylinders."""
    objects = []
    p_vec = Vector(gun_pivot)
    d_vec = Vector(direction).normalized()
    u_vec, v_vec, _ = get_orthonormal_basis(d_vec)
    rot_euler = d_vec.to_track_quat("Z", "Y").to_euler()

    # 1. Heavy cylindrical breech housing collar with tapered reinforcement
    breech_loc = p_vec + d_vec * 0.14
    objects.append(make_tapered_cylinder(
        f"{prefix}_BreechCollar",
        caliber_radius * 2.45, caliber_radius * 2.15, 0.42,
        breech_loc, armor_mat, rotation=rot_euler, vertices=14, bevel=0.03
    ))

    # 2. Main heavy-caliber tapered barrel tube (narrowing from breech to muzzle)
    barrel_center = p_vec + d_vec * (barrel_length * 0.5 + 0.22)
    objects.append(make_tapered_cylinder(
        f"{prefix}_BarrelTube",
        caliber_radius * 1.62, caliber_radius * 1.22, barrel_length,
        barrel_center, barrel_mat, rotation=rot_euler, vertices=14, bevel=0.015
    ))

    # 3. Flared conical muzzle bell (no thin warning band rings)
    muzzle_loc = p_vec + d_vec * (barrel_length + 0.22)
    objects.append(make_tapered_cylinder(
        f"{prefix}_MuzzleCollar",
        caliber_radius * 1.50, caliber_radius * 1.95, 0.18,
        muzzle_loc, armor_mat, rotation=rot_euler, vertices=14, bevel=0.02
    ))

    # 4. Massive hollow bore opening (dark cavity)
    objects.append(make_cylinder(
        f"{prefix}_BoreCavity",
        caliber_radius * 1.05, 0.12,
        muzzle_loc + d_vec * 0.05, barrel_mat, rotation=rot_euler, vertices=12, bevel=0.005
    ))

    # 5. Dual hydraulic recoil recuperator cylinders flanking the breech
    recoil_spacing = caliber_radius * 2.6
    for side in (-1, 1):
        cyl_center = p_vec + u_vec * (side * recoil_spacing) + d_vec * 0.28
        objects.append(make_tapered_cylinder(
            f"{prefix}_RecoilCylinder_{'L' if side < 0 else 'R'}",
            caliber_radius * 0.58, caliber_radius * 0.48, 0.55,
            cyl_center, barrel_mat, rotation=rot_euler, vertices=10, bevel=0.012
        ))
        objects.append(make_cylinder(
            f"{prefix}_RecoilPiston_{'L' if side < 0 else 'R'}",
            caliber_radius * 0.35, 0.35,
            cyl_center + d_vec * 0.25, armor_mat, rotation=rot_euler, vertices=8, bevel=0.01
        ))

    # 6. Sector elevation quadrant plates
    for side in (-1, 1):
        quad_loc = p_vec + u_vec * (side * (recoil_spacing + 0.12)) - v_vec * 0.12
        objects.append(make_box(
            f"{prefix}_ElevationQuadrant_{'L' if side < 0 else 'R'}",
            (0.04, 0.38, 0.24), quad_loc, barrel_mat, bevel=0.01, rotation=rot_euler
        ))

    return objects

def make_dual_cannon_assembly(
    prefix: str,
    gun_pivot: Tuple[float, float, float] | Vector,
    direction: Tuple[float, float, float] | Vector,
    caliber_radius: float = 0.082,
    barrel_length: float = 1.65,
    barrel_spacing: float = 0.38,
    armor_mat: bpy.types.Material | None = None,
    barrel_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    has_dust_boot: bool = False,
    dust_boot_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a twin-cannon mantlet assembly with dual tapered synchronized barrels and aerodynamic evacuators."""
    objects = []
    p_vec = Vector(gun_pivot)
    d_vec = Vector(direction).normalized()
    u_vec, v_vec, _ = get_orthonormal_basis(d_vec)
    rot_euler = d_vec.to_track_quat("Z", "Y").to_euler()

    # 1. Common armored mantlet block
    mantlet_width = barrel_spacing + caliber_radius * 4.4
    objects.append(make_box(
        f"{prefix}_MantletBlock",
        (mantlet_width, 0.44, 0.36),
        p_vec + d_vec * 0.15, armor_mat, bevel=0.035, rotation=rot_euler
    ))

    # 2. Dual gun barrels
    for side in (-1, 1):
        side_tag = "L" if side < 0 else "R"
        barrel_pivot = p_vec + u_vec * (side * barrel_spacing * 0.5)
        if has_dust_boot:
            objects.extend(make_mantlet_dust_boot(
                f"{prefix}_DustBoot_{side_tag}",
                pivot=barrel_pivot - d_vec * 0.04,
                direction=d_vec,
                collar_radius=caliber_radius * 1.65,
                taper_radius=caliber_radius * 1.20,
                length=0.22,
                stages=3,
                material=dust_boot_mat or barrel_mat,
                accent_mat=accent_mat,
            ))
        # Tapered breech collar
        objects.append(make_tapered_cylinder(
            f"{prefix}_Breech_{side_tag}",
            caliber_radius * 1.95, caliber_radius * 1.65, 0.38,
            barrel_pivot + d_vec * 0.32, armor_mat, rotation=rot_euler, vertices=12, bevel=0.015
        ))
        # Faceted thermal sleeve
        objects.append(make_tapered_cylinder(
            f"{prefix}_ThermalSleeve_{side_tag}",
            caliber_radius * 1.50, caliber_radius * 1.35, barrel_length * 0.42,
            barrel_pivot + d_vec * (barrel_length * 0.28 + 0.38), armor_mat, rotation=rot_euler, vertices=8, bevel=0.012
        ))
        # Aerodynamic bore evacuator with tapered transitions (NO evacuator stripe ring!)
        evac_loc = barrel_pivot + d_vec * (barrel_length * 0.58 + 0.38)
        objects.append(make_cylinder(
            f"{prefix}_BoreEvacuator_{side_tag}",
            caliber_radius * 1.55, 0.24,
            evac_loc, armor_mat, rotation=rot_euler, vertices=12, bevel=0.015
        ))
        # Main tapered barrel tube
        objects.append(make_tapered_cylinder(
            f"{prefix}_BarrelTube_{side_tag}",
            caliber_radius * 1.25, caliber_radius * 0.90, barrel_length,
            barrel_pivot + d_vec * (barrel_length * 0.5 + 0.38), barrel_mat, rotation=rot_euler, vertices=14, bevel=0.008
        ))
        # Muzzle brake
        muzzle_loc = barrel_pivot + d_vec * (barrel_length + 0.38)
        objects.append(make_box(
            f"{prefix}_MuzzleBrake_{side_tag}",
            (caliber_radius * 2.8, caliber_radius * 2.8, caliber_radius * 2.2),
            muzzle_loc, barrel_mat, bevel=0.015, rotation=rot_euler
        ))
        objects.append(make_cylinder(
            f"{prefix}_MuzzleBore_{side_tag}",
            caliber_radius * 0.85, 0.08,
            muzzle_loc + d_vec * (caliber_radius * 1.2), barrel_mat, rotation=rot_euler, vertices=10, bevel=0.005
        ))

    return objects

def make_quad_track_pod_assembly(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    length: float = 1.55,
    height: float = 0.56,
    belt_width: float = 0.32,
    roadwheel_count: int = 3,
    tread_mat: bpy.types.Material | None = None,
    rim_mat: bpy.types.Material | None = None,
    hub_mat: bpy.types.Material | None = None,
    axle_mat: bpy.types.Material | None = None,
    fender_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    ground_z: float = 0.06,
    inboard_side: int = -1,
    track_sag: float = 0.0,
) -> list[bpy.types.Object]:
    """Generates an independent continuous track pod for 4-track super-heavy crawlers."""
    objects = []
    loc_v = Vector(location)

    # 1. Continuous track belt
    belt = make_continuous_track_belt(
        f"{prefix}_Belt",
        x_pos=loc_v.x, length=length, height=height, width=belt_width,
        tread_mat=tread_mat, ground_z=ground_z, y_pos=loc_v.y,
        track_sag=track_sag,
    )
    objects.append(belt)

    # 2. Stepped road wheels along ground run
    half_l = length * 0.5
    usable_l = half_l * 0.65
    step_y = (usable_l * 2.0) / (roadwheel_count - 1) if roadwheel_count > 1 else 0.0
    wheel_radius = (height - ground_z) * 0.34
    wheel_center_z = ground_z + wheel_radius + 0.02
    sign = 1.0 if inboard_side < 0 else -1.0

    for idx in range(roadwheel_count):
        y = loc_v.y - usable_l + idx * step_y
        wheel_loc = (loc_v.x + sign * 0.015, y, wheel_center_z)
        wheels = make_stepped_roadwheel(
            f"{prefix}_RoadWheel_{idx+1}",
            location=wheel_loc, radius=wheel_radius, width=belt_width * 0.38,
            rim_mat=rim_mat, hub_mat=hub_mat, axle_mat=axle_mat, vertices=12
        )
        objects.extend(wheels)

    # 3. Drive sprocket & idler wheel
    for y_offset, name_tag in [(-half_l * 0.85, "DriveSprocket"), (half_l * 0.85, "FrontIdler")]:
        idlers = make_stepped_roadwheel(
            f"{prefix}_{name_tag}",
            location=(loc_v.x + sign * 0.015, loc_v.y + y_offset, ground_z + (height - ground_z) * 0.48),
            radius=wheel_radius * 0.95, width=belt_width * 0.36,
            rim_mat=axle_mat, hub_mat=rim_mat, axle_mat=axle_mat, vertices=12
        )
        objects.extend(idlers)

    # 4. Armored upper pod fender
    if fender_mat is not None:
        objects.append(make_box(
            f"{prefix}_Fender",
            (belt_width * 1.15, length * 0.92, 0.08),
            (loc_v.x, loc_v.y, ground_z + height + 0.04),
            fender_mat, bevel=0.02
        ))

    # 5. Inboard swivel mounting sponson arm (connecting pod to vehicle main hull)
    arm_x_span = 0.32
    arm_x = loc_v.x + (inboard_side * arm_x_span * 0.5)
    objects.append(make_box(
        f"{prefix}_MountArm",
        (arm_x_span, length * 0.45, 0.24),
        (arm_x, loc_v.y, ground_z + height * 0.65),
        fender_mat, bevel=0.03
    ))
    objects.append(make_cylinder(
        f"{prefix}_PivotKnuckle",
        radius=0.14, depth=0.18,
        location=(arm_x, loc_v.y, ground_z + height * 0.65),
        material=axle_mat, rotation=(math.radians(90), 0.0, 0.0), vertices=10, bevel=0.015
    ))

    return objects


def make_troop_compartment(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    size: Tuple[float, float, float] = (1.45, 1.65, 0.76),
    armor_mat: bpy.types.Material | None = None,
    door_mat: bpy.types.Material | None = None,
    detail_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an armored troop compartment / rear cabin for APCs and halftracks."""
    objects = []
    loc_v = Vector(location)
    sx, sy, sz = size

    # 1. Main cabin shell with a slight roof taper and rolled edge transitions.
    objects.append(_make_tapered_shell(
        f"{prefix}_CabinShell",
        size, loc_v, armor_mat,
        top_scale=(0.92, 0.94),
        bevel=min(0.045, sz * 0.16),
        bevel_segments=3,
        smooth=True,
    ))

    # 2. Roof observation cupola / hatch
    cupola_z = loc_v.z + sz * 0.5 + 0.05
    objects.append(make_cylinder(
        f"{prefix}_RoofCupola",
        radius=sx * 0.22, depth=0.10,
        location=(loc_v.x, loc_v.y + sy * 0.15, cupola_z),
        material=detail_mat, vertices=16, bevel=0.012, bevel_segments=2
    ))
    objects.append(make_cylinder(
        f"{prefix}_HatchCover",
        radius=sx * 0.18, depth=0.035,
        location=(loc_v.x, loc_v.y + sy * 0.15, cupola_z + 0.06),
        material=door_mat, vertices=16, bevel=0.006, bevel_segments=2
    ))

    # 3. Rear clamshell access doors
    door_w = sx * 0.36
    door_h = sz * 0.68
    rear_y = loc_v.y - sy * 0.5 - 0.01
    door_z = loc_v.z - sz * 0.08
    for side in (-1, 1):
        door_x = loc_v.x + side * (door_w * 0.52)
        objects.append(make_box(
            f"{prefix}_RearDoor_{'L' if side < 0 else 'R'}",
            (door_w, 0.04, door_h),
            (door_x, rear_y, door_z),
            door_mat, bevel=0.010, bevel_segments=2
        ))
        # Grab handles
        handle_x = loc_v.x + side * 0.08
        objects.append(make_rod(
            f"{prefix}_DoorHandle_{'L' if side < 0 else 'R'}",
            (handle_x, rear_y - 0.02, door_z - 0.08),
            (handle_x, rear_y - 0.02, door_z + 0.08),
            0.012, detail_mat, vertices=6
        ))

    # 4. Lateral armored vision slits / firing ports
    for side in (-1, 1):
        slit_x = loc_v.x + side * (sx * 0.5 + 0.01)
        for y_offset in (-sy * 0.22, sy * 0.22):
            tag = "F" if y_offset > 0 else "R"
            objects.append(make_box(
                f"{prefix}_VisionSlit_{'L' if side < 0 else 'R'}_{tag}",
                (0.035, 0.22, 0.06),
                (slit_x, loc_v.y + y_offset, loc_v.z + sz * 0.12),
                detail_mat, bevel=0.008
            ))

    return objects


def make_railgun_assembly(
    prefix: str,
    gun_pivot: Tuple[float, float, float] | Vector,
    direction: Tuple[float, float, float] | Vector = (0.0, 1.0, 0.0),
    rail_length: float = 2.40,
    rail_height: float = 0.28,
    rail_width: float = 0.40,
    bore_gap: float = 0.09,
    armor_mat: bpy.types.Material | None = None,
    rail_mat: bpy.types.Material | None = None,
    glow_mat: bpy.types.Material | None = None,
    capacitor_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an electromagnetic railgun assembly with dual parallel guide rails, central accelerator slit, capacitor bank, and magnetic coil clamps."""
    objects = []
    p_vec = Vector(gun_pivot)
    d_vec = Vector(direction).normalized()
    u_vec, v_vec, _ = get_orthonormal_basis(d_vec)
    rot_euler = d_vec.to_track_quat("Z", "Y").to_euler()

    # 1. Armored breech housing block
    breech_loc = p_vec + d_vec * 0.20
    objects.append(make_box(
        f"{prefix}_BreechHousing",
        (rail_width * 1.25, 0.52, rail_height * 1.35),
        breech_loc, armor_mat, bevel=0.035, rotation=rot_euler
    ))

    # 2. Capacitor energy bank on top of breech
    cap_mat = capacitor_mat or rail_mat
    for side in (-1, 1):
        cap_loc = breech_loc + u_vec * (side * rail_width * 0.32) + v_vec * (rail_height * 0.72)
        objects.append(make_cylinder(
            f"{prefix}_CapacitorCell_{'L' if side < 0 else 'R'}",
            radius=0.075, depth=0.42,
            location=cap_loc, material=cap_mat,
            rotation=rot_euler, vertices=10, bevel=0.015
        ))

    # 3. Dual parallel kinetic rails
    single_rail_w = (rail_width - bore_gap) * 0.5
    rail_center_y = p_vec + d_vec * (rail_length * 0.5 + 0.42)
    for side in (-1, 1):
        tag = "L" if side < 0 else "R"
        rail_offset = u_vec * (side * (bore_gap * 0.5 + single_rail_w * 0.5))
        objects.append(make_box(
            f"{prefix}_Rail_{tag}",
            (single_rail_w, rail_length, rail_height),
            rail_center_y + rail_offset, rail_mat,
            bevel=0.02, rotation=rot_euler
        ))
        # Outer armor shield / reinforcement spine
        objects.append(make_box(
            f"{prefix}_RailShield_{tag}",
            (single_rail_w * 0.45, rail_length * 0.75, rail_height * 0.65),
            rail_center_y + rail_offset + u_vec * (side * single_rail_w * 0.48),
            armor_mat, bevel=0.018, rotation=rot_euler
        ))

    # 4. Central glowing accelerator channel / coil core
    if glow_mat is not None:
        glow_core_loc = p_vec + d_vec * (rail_length * 0.5 + 0.42)
        objects.append(make_box(
            f"{prefix}_AcceleratorCore",
            (bore_gap * 0.75, rail_length * 0.95, rail_height * 0.45),
            glow_core_loc, glow_mat, rotation=rot_euler
        ))

    # 5. Magnetic containment sleeve clamps
    clamp_count = 4
    clamp_step = (rail_length - 0.40) / max(1, clamp_count - 1)
    for i in range(clamp_count):
        clamp_dist = 0.55 + i * clamp_step
        clamp_loc = p_vec + d_vec * clamp_dist
        objects.append(make_box(
            f"{prefix}_CoilClamp_{i+1}",
            (rail_width * 1.14, 0.12, rail_height * 1.18),
            clamp_loc, armor_mat, bevel=0.015, rotation=rot_euler
        ))
        if accent_mat is not None:
            objects.append(make_box(
                f"{prefix}_CoilAccent_{i+1}",
                (rail_width * 1.16, 0.04, rail_height * 0.35),
                clamp_loc + v_vec * (rail_height * 0.45),
                accent_mat, bevel=0.008, rotation=rot_euler
            ))

    # 6. Muzzle guide prongs
    muzzle_loc = p_vec + d_vec * (rail_length + 0.46)
    for side in (-1, 1):
        tag = "L" if side < 0 else "R"
        prong_loc = muzzle_loc + u_vec * (side * (bore_gap * 0.5 + single_rail_w * 0.5))
        objects.append(make_box(
            f"{prefix}_MuzzleProng_{tag}",
            (single_rail_w * 0.82, 0.14, rail_height * 0.88),
            prong_loc, rail_mat, bevel=0.015, rotation=rot_euler
        ))

    return objects


def make_quad_flak_autocannon_assembly(
    prefix: str,
    gun_pivot: Tuple[float, float, float] | Vector,
    direction: Tuple[float, float, float] | Vector = (0.0, 0.85, 0.52),  # ~32 deg elevation
    barrel_length: float = 1.65,
    caliber_radius: float = 0.038,
    spacing_x: float = 0.24,
    spacing_z: float = 0.20,
    cradle_mat: bpy.types.Material | None = None,
    barrel_mat: bpy.types.Material | None = None,
    mag_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    has_dust_boot: bool = False,
    dust_boot_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an anti-air quad flak autocannon assembly in a 2x2 cluster with flash hiders, ammo feed boxes, and elevation cradle."""
    objects = []
    p_vec = Vector(gun_pivot)
    d_vec = Vector(direction).normalized()
    u_vec, v_vec, _ = get_orthonormal_basis(d_vec)
    rot_euler = d_vec.to_track_quat("Z", "Y").to_euler()

    # 1. Armored elevation cradle trunnion block
    cradle_w = spacing_x + caliber_radius * 5.5
    cradle_h = spacing_z + caliber_radius * 5.0
    cradle_loc = p_vec + d_vec * 0.18
    objects.append(make_box(
        f"{prefix}_ElevationCradle",
        (cradle_w, 0.48, cradle_h),
        cradle_loc, cradle_mat, bevel=0.035, rotation=rot_euler
    ))

    # 2. Dual high-capacity ammo drums / magazines flanking cradle
    m_mat = mag_mat or barrel_mat
    for side in (-1, 1):
        mag_loc = cradle_loc + u_vec * (side * (cradle_w * 0.5 + 0.16))
        objects.append(make_cylinder(
            f"{prefix}_AmmoDrum_{'L' if side < 0 else 'R'}",
            radius=0.18, depth=0.36,
            location=mag_loc, material=m_mat,
            rotation=rot_euler, vertices=12, bevel=0.02
        ))
        if accent_mat is not None:
            objects.append(make_box(
                f"{prefix}_FeedChute_{'L' if side < 0 else 'R'}",
                (0.14, 0.24, 0.10),
                mag_loc - u_vec * (side * 0.10) + d_vec * 0.12,
                accent_mat, bevel=0.01, rotation=rot_euler
            ))

    # 3. 2x2 Synchronized Autocannon Barrels
    offsets = [
        (-1, 1, "TopL"), (1, 1, "TopR"),
        (-1, -1, "BotL"), (1, -1, "BotR"),
    ]
    for sx, sz, tag in offsets:
        b_pivot = p_vec + u_vec * (sx * spacing_x * 0.5) + v_vec * (sz * spacing_z * 0.5)
        if has_dust_boot:
            objects.extend(make_mantlet_dust_boot(
                f"{prefix}_DustBoot_{tag}",
                pivot=b_pivot - d_vec * 0.04,
                direction=d_vec,
                collar_radius=caliber_radius * 1.75,
                taper_radius=caliber_radius * 1.20,
                length=0.18,
                stages=3,
                material=dust_boot_mat or barrel_mat,
                accent_mat=accent_mat,
            ))
        # Breech sleeve
        objects.append(make_cylinder(
            f"{prefix}_Breech_{tag}",
            radius=caliber_radius * 1.85, depth=0.35,
            location=b_pivot + d_vec * 0.35, material=cradle_mat,
            rotation=rot_euler, vertices=10, bevel=0.015
        ))
        # Cooling jacket / perforated shroud
        objects.append(make_cylinder(
            f"{prefix}_CoolingJacket_{tag}",
            radius=caliber_radius * 1.40, depth=barrel_length * 0.45,
            location=b_pivot + d_vec * (barrel_length * 0.28 + 0.38), material=barrel_mat,
            rotation=rot_euler, vertices=10, bevel=0.012
        ))
        # Main barrel tube
        objects.append(make_cylinder(
            f"{prefix}_BarrelTube_{tag}",
            radius=caliber_radius, depth=barrel_length,
            location=b_pivot + d_vec * (barrel_length * 0.5 + 0.38), material=barrel_mat,
            rotation=rot_euler, vertices=10, bevel=0.008
        ))
        # Conical flash hider muzzle
        muzzle_loc = b_pivot + d_vec * (barrel_length + 0.44)
        objects.append(make_cylinder(
            f"{prefix}_FlashHider_{tag}",
            radius=caliber_radius * 1.65, depth=0.18,
            location=muzzle_loc, material=barrel_mat,
            rotation=rot_euler, vertices=10, bevel=0.015
        ))
        if accent_mat is not None and sz > 0:
            objects.append(make_cylinder(
                f"{prefix}_MuzzleRing_{tag}",
                radius=caliber_radius * 1.70, depth=0.04,
                location=muzzle_loc - d_vec * 0.05, material=accent_mat,
                rotation=rot_euler, vertices=10
            ))

    return objects


def make_plasma_mortar_assembly(
    prefix: str,
    gun_pivot: Tuple[float, float, float] | Vector,
    direction: Tuple[float, float, float] | Vector = (0.0, 0.70, 0.70),  # 45 deg elevation
    caliber_radius: float = 0.175,
    barrel_length: float = 0.95,
    ring_count: int = 3,
    armor_mat: bpy.types.Material | None = None,
    barrel_mat: bpy.types.Material | None = None,
    glow_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a heavy plasma siege mortar with magnetic stator rail housing and tapered chamber."""
    objects = []
    p_vec = Vector(gun_pivot)
    d_vec = Vector(direction).normalized()
    u_vec, v_vec, _ = get_orthonormal_basis(d_vec)
    rot_euler = d_vec.to_track_quat("Z", "Y").to_euler()

    # 1. Heavy reinforced trunnion base & sector gear
    trunnion_loc = p_vec + d_vec * 0.14
    objects.append(make_box(
        f"{prefix}_MortarTrunnion",
        (caliber_radius * 4.6, 0.44, caliber_radius * 3.8),
        trunnion_loc, armor_mat, bevel=0.04, rotation=rot_euler
    ))
    # Sector elevation gear arc
    gear_loc = trunnion_loc - v_vec * (caliber_radius * 1.7)
    objects.append(make_box(
        f"{prefix}_SectorGearArc",
        (caliber_radius * 1.8, 0.32, 0.14),
        gear_loc, barrel_mat, bevel=0.015, rotation=rot_euler
    ))

    # 2. Main tapered mortar barrel (no straight tube)
    barrel_loc = p_vec + d_vec * (barrel_length * 0.5 + 0.22)
    objects.append(make_tapered_cylinder(
        f"{prefix}_MortarBarrel",
        caliber_radius * 1.55, caliber_radius * 1.25, barrel_length,
        barrel_loc, barrel_mat, rotation=rot_euler, vertices=14, bevel=0.015
    ))
    # Bore hole cavity
    bore_loc = p_vec + d_vec * (barrel_length + 0.24)
    objects.append(make_cylinder(
        f"{prefix}_MortarBoreHole",
        caliber_radius * 1.05, 0.12,
        bore_loc, barrel_mat, rotation=rot_euler, vertices=14
    ))

    # 3. Longitudinal Magnetic Stator Rail Cages (eliminates floating concentric donut rings)
    stator_len = barrel_length * 0.72
    stator_center = p_vec + d_vec * (0.28 + stator_len * 0.5)
    rail_w = caliber_radius * 0.42
    rail_h = caliber_radius * 0.38
    r_dist = caliber_radius * 1.48

    for side in (-1, 1):
        s_tag = "L" if side < 0 else "R"
        # Lateral stator rails
        objects.append(make_box(
            f"{prefix}_StatorRail_{s_tag}",
            (rail_w, stator_len, rail_h),
            stator_center + u_vec * (side * r_dist),
            armor_mat, bevel=0.015, rotation=rot_euler
        ))
        if glow_mat is not None:
            # Linear recessed magnetic induction channel
            objects.append(make_box(
                f"{prefix}_StatorGlow_{s_tag}",
                (rail_w * 0.45, stator_len * 0.85, rail_h * 0.35),
                stator_center + u_vec * (side * (r_dist + rail_w * 0.35)),
                glow_mat, bevel=0.005, rotation=rot_euler
            ))

    # Dorsal and ventral stator spine beams
    for v_sign, v_tag in ((-1, "Btm"), (1, "Top")):
        objects.append(make_box(
            f"{prefix}_StatorSpine_{v_tag}",
            (rail_h, stator_len, rail_w),
            stator_center + v_vec * (v_sign * r_dist),
            armor_mat, bevel=0.015, rotation=rot_euler
        ))

    # 4. Flare muzzle bell with conical taper
    muzzle_loc = p_vec + d_vec * (barrel_length + 0.18)
    objects.append(make_tapered_cylinder(
        f"{prefix}_MuzzleBell",
        caliber_radius * 1.35, caliber_radius * 1.85, 0.16,
        muzzle_loc, armor_mat, rotation=rot_euler, vertices=14, bevel=0.02
    ))

    return objects

def make_flamethrower_assembly(
    prefix: str,
    gun_pivot: Tuple[float, float, float] | Vector,
    direction: Tuple[float, float, float] | Vector = (0.0, 1.0, 0.05),
    barrel_length: float = 1.45,
    caliber_radius: float = 0.088,
    armor_mat: bpy.types.Material | None = None,
    barrel_mat: bpy.types.Material | None = None,
    tank_mat: bpy.types.Material | None = None,
    glow_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a heavy thermobaric flamethrower projector with under-slung fuel tanks, perforated shroud, and pilot igniter."""
    objects = []
    p_vec = Vector(gun_pivot)
    d_vec = Vector(direction).normalized()
    u_vec, v_vec, _ = get_orthonormal_basis(d_vec)
    rot_euler = d_vec.to_track_quat("Z", "Y").to_euler()

    # 1. Armored breech manifold block
    manifold_loc = p_vec + d_vec * 0.16
    objects.append(make_box(
        f"{prefix}_FlamerManifold",
        (caliber_radius * 4.2, 0.44, caliber_radius * 3.4),
        manifold_loc, armor_mat, bevel=0.035, rotation=rot_euler
    ))

    # 2. Dual pressurized under-slung fuel canisters
    t_mat = tank_mat or accent_mat or armor_mat
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        tank_loc = p_vec + d_vec * (barrel_length * 0.38) + u_vec * (side * caliber_radius * 1.55) - v_vec * (caliber_radius * 1.15)
        objects.append(make_cylinder(
            f"{prefix}_FuelCanister_{stag}",
            radius=caliber_radius * 0.78, depth=barrel_length * 0.52,
            location=tank_loc, material=t_mat,
            rotation=rot_euler, vertices=12, bevel=0.02
        ))
        objects.append(make_cylinder(
            f"{prefix}_CanisterStrap_{stag}",
            radius=caliber_radius * 0.84, depth=0.06,
            location=tank_loc, material=barrel_mat,
            rotation=rot_euler, vertices=12, bevel=0.008
        ))

    # 3. Main propellant delivery tube & perforated heat shroud
    tube_loc = p_vec + d_vec * (barrel_length * 0.5 + 0.22)
    objects.append(make_cylinder(
        f"{prefix}_PropellantBarrel",
        radius=caliber_radius * 0.82, depth=barrel_length,
        location=tube_loc, material=barrel_mat,
        rotation=rot_euler, vertices=12, bevel=0.012
    ))
    shroud_loc = p_vec + d_vec * (barrel_length * 0.56 + 0.22)
    objects.append(make_cylinder(
        f"{prefix}_ThermalShroud",
        radius=caliber_radius * 1.28, depth=barrel_length * 0.68,
        location=shroud_loc, material=armor_mat,
        rotation=rot_euler, vertices=10, bevel=0.02
    ))
    for r_idx in range(3):
        ring_loc = p_vec + d_vec * (0.42 + r_idx * (barrel_length * 0.22))
        objects.append(make_cylinder(
            f"{prefix}_CoolingBand_{r_idx+1}",
            radius=caliber_radius * 1.38, depth=0.05,
            location=ring_loc, material=barrel_mat,
            rotation=rot_euler, vertices=10, bevel=0.008
        ))

    # 4. Muzzle nozzle cone & pilot igniter bracket
    nozzle_loc = p_vec + d_vec * (barrel_length + 0.24)
    objects.append(make_cylinder(
        f"{prefix}_NozzleCone",
        radius=caliber_radius * 1.45, depth=0.18,
        location=nozzle_loc, material=barrel_mat,
        rotation=rot_euler, vertices=12, bevel=0.02
    ))
    igniter_start = nozzle_loc - d_vec * 0.12 - v_vec * (caliber_radius * 1.35)
    igniter_tip = nozzle_loc + d_vec * 0.16 - v_vec * (caliber_radius * 0.65)
    objects.append(make_rod(
        f"{prefix}_PilotIgniterArm",
        igniter_start, igniter_tip,
        caliber_radius * 0.18, barrel_mat, vertices=8
    ))
    if glow_mat is not None:
        objects.append(make_cylinder(
            f"{prefix}_PilotFlameTip",
            radius=caliber_radius * 0.28, depth=0.07,
            location=igniter_tip + d_vec * 0.03, material=glow_mat,
            rotation=rot_euler, vertices=8, bevel=0.008
        ))

    return objects


def make_emp_emitter_assembly(
    prefix: str,
    gun_pivot: Tuple[float, float, float] | Vector,
    direction: Tuple[float, float, float] | Vector = (0.0, 1.0, 0.06),
    emitter_length: float = 1.55,
    core_radius: float = 0.14,
    armor_mat: bpy.types.Material | None = None,
    metal_mat: bpy.types.Material | None = None,
    glow_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates a directional EMP wave disruptor with resonator housing, concentric phase rings, and dipole array."""
    objects = []
    p_vec = Vector(gun_pivot)
    d_vec = Vector(direction).normalized()
    u_vec, v_vec, _ = get_orthonormal_basis(d_vec)
    rot_euler = d_vec.to_track_quat("Z", "Y").to_euler()

    # 1. Armored wave-guide resonator housing
    res_loc = p_vec + d_vec * 0.20
    objects.append(make_box(
        f"{prefix}_EMPResonator",
        (core_radius * 3.6, 0.48, core_radius * 2.8),
        res_loc, armor_mat, bevel=0.035, rotation=rot_euler
    ))

    # 2. Central dielectric wave-guide boom
    boom_loc = p_vec + d_vec * (emitter_length * 0.5 + 0.22)
    objects.append(make_cylinder(
        f"{prefix}_WaveguideBoom",
        radius=core_radius * 0.55, depth=emitter_length,
        location=boom_loc, material=metal_mat,
        rotation=rot_euler, vertices=12, bevel=0.012
    ))
    if glow_mat is not None:
        objects.append(make_cylinder(
            f"{prefix}_PulseCore",
            radius=core_radius * 0.64, depth=emitter_length * 0.72,
            location=boom_loc, material=glow_mat,
            rotation=rot_euler, vertices=12, bevel=0.008
        ))

    # 3. Concentric stepped toroidal phase rings (decreasing radius forward)
    for idx, t in enumerate((0.28, 0.55, 0.82), start=1):
        ring_loc = p_vec + d_vec * (0.22 + emitter_length * t)
        r_scale = 1.65 - idx * 0.22
        objects.append(make_torus(
            f"{prefix}_EMPPhaseRing_{idx}",
            location=ring_loc,
            major_radius=core_radius * r_scale,
            minor_radius=core_radius * 0.16,
            material=glow_mat if (glow_mat and idx == 2) else (accent_mat or armor_mat),
            rotation=rot_euler, major_segments=14, minor_segments=6
        ))
        for side in (-1, 1):
            strut_end = ring_loc + u_vec * (side * core_radius * r_scale)
            objects.append(make_rod(
                f"{prefix}_PhaseStrut_{idx}_{'L' if side < 0 else 'R'}",
                ring_loc, strut_end, core_radius * 0.10, metal_mat, vertices=6
            ))

    # 4. Forward cross-dipole emitter array head
    head_loc = p_vec + d_vec * (emitter_length + 0.24)
    objects.append(make_cylinder(
        f"{prefix}_EmitterHub",
        radius=core_radius * 0.82, depth=0.14,
        location=head_loc, material=armor_mat,
        rotation=rot_euler, vertices=10, bevel=0.015
    ))
    for side in (-1, 1):
        objects.append(make_box(
            f"{prefix}_DipoleWing_{'L' if side < 0 else 'R'}",
            (core_radius * 1.85, 0.08, 0.14),
            head_loc + u_vec * (side * core_radius * 1.15),
            glow_mat or accent_mat or armor_mat, bevel=0.012, rotation=rot_euler
        ))

    return objects


def make_flight_deck_assembly(
    prefix: str,
    size: Tuple[float, float, float] = (2.10, 3.20, 0.22),
    location: Tuple[float, float, float] | Vector = (0.0, 0.0, 1.10),
    deck_mat: bpy.types.Material | None = None,
    guideline_mat: bpy.types.Material | None = None,
    glow_mat: bpy.types.Material | None = None,
    rail_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an aircraft/drone flight and launch deck with guide light rails, blast deflector ramp, and perimeter safety catwalks."""
    objects = []
    loc = Vector(location)
    sx, sy, sz = size

    # 1. Main flat flight deck slab
    objects.append(make_box(
        f"{prefix}_DeckSlab",
        (sx, sy, sz),
        loc, deck_mat, bevel=0.025
    ))

    # 2. Angled frontal blast deflector / ski-jump ramp
    ramp_l = sy * 0.18
    ramp_loc = loc + Vector((0.0, sy * 0.5 - ramp_l * 0.45, sz * 0.40))
    objects.append(make_box(
        f"{prefix}_BlastRamp",
        (sx * 0.88, ramp_l, 0.08),
        ramp_loc, deck_mat, bevel=0.015,
        rotation=(math.radians(-14), 0.0, 0.0)
    ))

    # 3. Centerline runway landing guide strip
    g_mat = guideline_mat or deck_mat
    objects.append(make_box(
        f"{prefix}_RunwayStrip",
        (0.18, sy * 0.72, 0.02),
        loc + Vector((0.0, -sy * 0.08, sz * 0.5 + 0.01)),
        g_mat, bevel=0.005
    ))

    # 4. Runway guidance light dots (emissive)
    if glow_mat is not None:
        dot_count = 5
        step_y = (sy * 0.70) / max(1, dot_count - 1)
        for i in range(dot_count):
            dot_y = loc.y - (sy * 0.35) + i * step_y
            for side in (-1, 1):
                dot_x = loc.x + side * (sx * 0.38)
                objects.append(make_cylinder(
                    f"{prefix}_GuideLight_{'L' if side < 0 else 'R'}_{i+1}",
                    radius=0.025, depth=0.03,
                    location=(dot_x, dot_y, loc.z + sz * 0.5 + 0.015),
                    material=glow_mat, vertices=8
                ))

    # 5. Side perimeter catwalk railings
    r_mat = rail_mat or deck_mat
    for side in (-1, 1):
        catwalk_x = loc.x + side * (sx * 0.5 + 0.08)
        objects.append(make_box(
            f"{prefix}_Catwalk_{'L' if side < 0 else 'R'}",
            (0.14, sy * 0.90, 0.05),
            (catwalk_x, loc.y, loc.z + sz * 0.20),
            r_mat, bevel=0.01
        ))
        for post_y in (-sy * 0.35, 0.0, sy * 0.35):
            objects.append(make_rod(
                f"{prefix}_RailPost_{'L' if side < 0 else 'R'}_{int(post_y*10)}",
                (catwalk_x + side * 0.05, loc.y + post_y, loc.z + sz * 0.20),
                (catwalk_x + side * 0.05, loc.y + post_y, loc.z + sz * 0.5 + 0.12),
                0.012, r_mat, vertices=6
            ))

    return objects


def make_outrigger_stabilizer_legs(
    prefix: str,
    hull_length: float = 3.2,
    hull_width: float = 2.4,
    ground_z: float = 0.06,
    reach_x: float = 0.65,
    count: int = 4,  # 2 or 4
    armor_mat: bpy.types.Material | None = None,
    piston_mat: bpy.types.Material | None = None,
    pad_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates deployable hydraulic stabilizer outrigger jacks with wide ground foot pads for firing stability."""
    objects = []
    pad_m = pad_mat or armor_mat
    pis_m = piston_mat or armor_mat

    y_positions = [-hull_length * 0.35, hull_length * 0.35] if count == 4 else [-hull_length * 0.38]
    for side in (-1, 1):
        side_tag = "L" if side < 0 else "R"
        for idx, y_pos in enumerate(y_positions):
            tag = f"{side_tag}_{idx+1}"
            hinge_loc = Vector((side * (hull_width * 0.50), y_pos, ground_z + 0.42))
            foot_loc = Vector((side * (hull_width * 0.50 + reach_x), y_pos, ground_z + 0.04))

            # 1. Hinge housing on hull flank
            objects.append(make_box(
                f"{prefix}_OutriggerHinge_{tag}",
                (0.18, 0.22, 0.22),
                hinge_loc, armor_mat, bevel=0.02
            ))
            # 2. Diagonal hydraulic ram arm
            objects.append(make_rod(
                f"{prefix}_OutriggerArm_{tag}",
                hinge_loc, foot_loc,
                0.042, pis_m, vertices=8
            ))
            # 3. Heavy circular ground stabilizer pad
            objects.append(make_cylinder(
                f"{prefix}_OutriggerPad_{tag}",
                radius=0.18, depth=0.06,
                location=foot_loc, material=pad_m,
                vertices=10, bevel=0.012
            ))

    return objects


def make_heavy_excavator_drum(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    width: float = 2.2,
    radius: float = 0.32,
    drum_mat: bpy.types.Material | None = None,
    tooth_mat: bpy.types.Material | None = None,
    arm_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an industrial rotary cutting excavator drum with carbide teeth, hydraulic push arms, and overhead rock guard."""
    objects = []
    loc = Vector(location)
    d_mat = drum_mat or arm_mat
    t_mat = tooth_mat or d_mat

    # 1. Central rotary cutting drum (oriented along X axis)
    drum_rot = (0.0, math.radians(90), 0.0)
    objects.append(make_cylinder(
        f"{prefix}_ExcavatorDrumCore",
        radius=radius, depth=width,
        location=loc, material=d_mat,
        rotation=drum_rot, vertices=16, bevel=0.02
    ))

    # 2. Rows of helical cutting teeth
    tooth_rows = 6
    teeth_per_row = 7
    tooth_step_x = (width * 0.85) / max(1, teeth_per_row - 1)
    for r in range(tooth_rows):
        angle = (2.0 * math.pi / tooth_rows) * r
        for c in range(teeth_per_row):
            t_x = loc.x - width * 0.425 + c * tooth_step_x
            t_y = loc.y + math.cos(angle) * (radius + 0.05)
            t_z = loc.z + math.sin(angle) * (radius + 0.05)
            objects.append(make_box(
                f"{prefix}_CutterTooth_{r+1}_{c+1}",
                (0.045, 0.07, 0.07),
                (t_x, t_y, t_z), t_mat, bevel=0.008,
                rotation=(angle, 0.0, 0.0)
            ))

    # 3. Left and right heavy hydraulic lift/push arms
    a_mat = arm_mat or d_mat
    for side in (-1, 1):
        arm_x = loc.x + side * (width * 0.52)
        arm_start = Vector((arm_x, loc.y - 0.75, loc.z + 0.30))
        arm_end = Vector((arm_x, loc.y, loc.z))
        objects.append(make_rod(
            f"{prefix}_LiftArm_{'L' if side < 0 else 'R'}",
            arm_start, arm_end,
            0.055, a_mat, vertices=8
        ))
        objects.append(make_cylinder(
            f"{prefix}_ArmHubCap_{'L' if side < 0 else 'R'}",
            radius=0.12, depth=0.08,
            location=(arm_x, loc.y, loc.z), material=d_mat,
            rotation=drum_rot, vertices=10, bevel=0.015
        ))

    # 4. Overhead protective debris deflector cowl
    objects.append(make_box(
        f"{prefix}_DebrisDeflectorCowl",
        (width * 1.08, 0.45, 0.08),
        loc + Vector((0.0, -radius * 0.35, radius + 0.12)),
        a_mat, bevel=0.02,
        rotation=(math.radians(18), 0.0, 0.0)
    ))

    return objects


# ---------------------------------------------------------------------------
# Procedural VFX Meshes & Shield Generators
# ---------------------------------------------------------------------------

def make_muzzle_flash_mesh(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    direction: Tuple[float, float, float] | Vector = (0.0, 1.0, 0.0),
    size: float = 0.45,
    flash_mat: bpy.types.Material | None = None,
    core_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an angular star-spike muzzle flash mesh with high-emission core and lateral flame blades."""
    objects = []
    p_loc = Vector(location)
    d_vec = Vector(direction).normalized()
    u_vec, v_vec, _ = get_orthonormal_basis(d_vec)
    rot_euler = d_vec.to_track_quat("Z", "Y").to_euler()

    # 1. Central high-intensity core diamond
    objects.append(make_cylinder(
        f"{prefix}_MuzzleFlashCore",
        radius=size * 0.22, depth=size * 0.85,
        location=p_loc + d_vec * (size * 0.38),
        material=core_mat if core_mat else flash_mat,
        rotation=rot_euler, vertices=6, bevel=0.01
    ))

    # 2. Angular star blades along lateral and vertical axes
    blade_length = size * 0.95
    blade_thickness = size * 0.08
    for angle_deg in (0, 45, 90, 135):
        rad = math.radians(angle_deg)
        blade_center = p_loc + d_vec * (size * 0.42)
        objects.append(make_box(
            f"{prefix}_FlashBlade_{angle_deg}",
            (blade_thickness, blade_length, blade_thickness),
            blade_center, flash_mat, bevel=0.005,
            rotation=(rot_euler.x + rad, rot_euler.y, rot_euler.z)
        ))

    return objects


def make_thruster_plume_mesh(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    direction: Tuple[float, float, float] | Vector = (0.0, -1.0, 0.0),
    length: float = 0.65,
    radius: float = 0.18,
    segments: int = 8,
    plume_mat: bpy.types.Material | None = None,
    core_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an aerodynamic glowing conical thruster plume and inner high-temperature exhaust core."""
    objects = []
    loc = Vector(location)
    d_vec = Vector(direction).normalized()
    rot_euler = d_vec.to_track_quat("-Z", "Y").to_euler()

    # Outer plume cone
    bpy.ops.mesh.primitive_cone_add(
        vertices=segments, radius1=radius, radius2=radius * 0.12,
        depth=length, location=loc + d_vec * (length * 0.5),
        rotation=rot_euler
    )
    outer = bpy.context.active_object
    outer.name = f"{prefix}_ThrusterPlume"
    finish_mesh(outer, plume_mat, bevel=0.0)
    objects.append(outer)

    # Inner high-intensity core cone
    bpy.ops.mesh.primitive_cone_add(
        vertices=segments, radius1=radius * 0.62, radius2=0.02,
        depth=length * 0.60, location=loc + d_vec * (length * 0.30),
        rotation=rot_euler
    )
    inner = bpy.context.active_object
    inner.name = f"{prefix}_ThrusterCore"
    finish_mesh(inner, core_mat if core_mat else plume_mat, bevel=0.0)
    objects.append(inner)

    return objects


def make_shield_bubble_mesh(
    prefix: str,
    center: Tuple[float, float, float] | Vector = (0.0, 0.0, 0.8),
    radius: float = 2.4,
    scale_z: float = 0.78,
    shield_mat: bpy.types.Material | None = None,
    subdivisions: int = 2,
) -> bpy.types.Object:
    """Generates a streamlined aerodynamic geodesic shield bubble enclosure."""
    c_loc = Vector(center)
    bpy.ops.mesh.primitive_ico_sphere_add(
        subdivisions=subdivisions, radius=radius, location=c_loc
    )
    bubble = bpy.context.active_object
    bubble.name = f"{prefix}_ShieldBubble"
    bubble.scale = (1.0, 1.12, scale_z)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    finish_mesh(bubble, shield_mat, bevel=0.0)
    for poly in bubble.data.polygons:
        poly.use_smooth = True
    return bubble


# ---------------------------------------------------------------------------
# Modular Attachments & Hardpoint Customization System
# ---------------------------------------------------------------------------

def make_era_armor_tiles(
    prefix: str,
    base_location: Tuple[float, float, float] | Vector,
    rows: int = 2,
    cols: int = 3,
    tile_size: Tuple[float, float, float] = (0.28, 0.16, 0.06),
    spacing: float = 0.03,
    slope_angle_deg: float = 25.0,
    tile_mat: bpy.types.Material | None = None,
    bolt_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an array of Explosive Reactive Armor (ERA) cassette tiles with mounting bolts."""
    objects = []
    base_loc = Vector(base_location)
    w, l, h = tile_size
    step_x = w + spacing
    step_y = l + spacing
    rot_rad = math.radians(slope_angle_deg)
    rot_euler = Euler((rot_rad, 0.0, 0.0), "XYZ")

    x_start = -((cols - 1) * step_x) * 0.5
    for r in range(rows):
        y_offset = (r - (rows - 1) * 0.5) * step_y
        for c in range(cols):
            x_offset = x_start + c * step_x
            loc = base_loc + Vector((x_offset, y_offset * math.cos(rot_rad), y_offset * math.sin(rot_rad)))
            tile = make_box(
                f"{prefix}_ERATile_R{r}C{c}",
                (w, l, h),
                loc,
                tile_mat,
                bevel=0.008,
                rotation=rot_euler,
            )
            objects.append(tile)

            if bolt_mat:
                for bx, by in [(-w * 0.38, -l * 0.38), (w * 0.38, -l * 0.38), (-w * 0.38, l * 0.38), (w * 0.38, l * 0.38)]:
                    bolt_local = Vector((bx, by, h * 0.52))
                    bolt_local.rotate(rot_euler)
                    bolt = make_cylinder(
                        f"{prefix}_ERABolt_R{r}C{c}_{bx > 0}_{by > 0}",
                        radius=0.012,
                        depth=0.02,
                        location=loc + bolt_local,
                        material=bolt_mat,
                        rotation=rot_euler,
                        vertices=6,
                        bevel=0.0,
                    )
                    objects.append(bolt)

    return objects


def make_aps_hardkill_system(
    prefix: str,
    mount_location: Tuple[float, float, float] | Vector,
    side: float = 1.0,
    pod_mat: bpy.types.Material | None = None,
    radar_face_mat: bpy.types.Material | None = None,
    barrel_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an Active Protection System (APS / Trophy) hardkill sensor pod & launcher."""
    objects = []
    base_loc = Vector(mount_location)

    # 1. Armored radar sensor pod
    pod = make_box(
        f"{prefix}_APSPod_{'R' if side > 0 else 'L'}",
        (0.24, 0.32, 0.28),
        base_loc,
        pod_mat,
        bevel=0.015,
    )
    objects.append(pod)

    # 2. Recessed phased-array radar sensor face
    face_loc = base_loc + Vector((side * 0.12, 0.05, 0.02))
    sensor_face = make_box(
        f"{prefix}_APSSensor_{'R' if side > 0 else 'L'}",
        (0.02, 0.22, 0.18),
        face_loc,
        radar_face_mat if radar_face_mat else pod_mat,
        bevel=0.005,
    )
    objects.append(sensor_face)

    # 3. Countermeasure mortar launcher (2 angled launcher tubes)
    for tube_idx in range(2):
        tube_loc = base_loc + Vector((side * 0.06, -0.06 + tube_idx * 0.12, 0.20))
        tube_rot = Euler((math.radians(35.0), math.radians(side * 20.0), 0.0), "XYZ")
        tube = make_cylinder(
            f"{prefix}_APSTube_{'R' if side > 0 else 'L'}_{tube_idx}",
            radius=0.045,
            depth=0.18,
            location=tube_loc,
            material=barrel_mat if barrel_mat else pod_mat,
            rotation=tube_rot,
            vertices=8,
            bevel=0.005,
        )
        objects.append(tube)

    return objects


def make_sponson_pod(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    side: float = 1.0,
    size: Tuple[float, float, float] = (0.35, 0.95, 0.50),
    weapon_type: str = "autocannon",
    armor_mat: bpy.types.Material | None = None,
    gunmetal_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates an armored flank sponson pod housing auxiliary weapons."""
    objects = []
    base_loc = Vector(location)
    w, l, h = size

    # Sponson armored casing with chamfered angular profile
    casing = make_box(
        f"{prefix}_SponsonCasing_{'R' if side > 0 else 'L'}",
        (w, l, h),
        base_loc,
        armor_mat,
        bevel=0.02,
    )
    objects.append(casing)

    # Armored pivot trunnion
    trunnion_loc = base_loc + Vector((side * (w * 0.55), 0.15, 0.0))
    trunnion = make_cylinder(
        f"{prefix}_SponsonPivot_{'R' if side > 0 else 'L'}",
        radius=h * 0.30,
        depth=0.12,
        location=trunnion_loc,
        material=gunmetal_mat,
        rotation=Euler((0.0, math.radians(90.0), 0.0), "XYZ"),
        vertices=10,
        bevel=0.008,
    )
    objects.append(trunnion)

    # Auxiliary weapon barrel
    barrel_loc = trunnion_loc + Vector((0.0, l * 0.45, 0.0))
    barrel = make_cylinder(
        f"{prefix}_SponsonBarrel_{'R' if side > 0 else 'L'}",
        radius=0.042,
        depth=0.55,
        location=barrel_loc,
        material=gunmetal_mat,
        rotation=Euler((math.radians(90.0), 0.0, 0.0), "XYZ"),
        vertices=8,
        bevel=0.005,
    )
    objects.append(barrel)

    # Muzzle flash collar
    muzzle_loc = barrel_loc + Vector((0.0, 0.28, 0.0))
    flash_collar = make_cylinder(
        f"{prefix}_SponsonMuzzle_{'R' if side > 0 else 'L'}",
        radius=0.062,
        depth=0.08,
        location=muzzle_loc,
        material=gunmetal_mat,
        rotation=Euler((math.radians(90.0), 0.0, 0.0), "XYZ"),
        vertices=8,
        bevel=0.005,
    )
    objects.append(flash_collar)

    return objects


def make_aux_fuel_drums(
    prefix: str,
    rear_location: Tuple[float, float, float] | Vector,
    rear_width: float = 1.8,
    radius: float = 0.22,
    length: float = 0.75,
    drum_mat: bpy.types.Material | None = None,
    mount_mat: bpy.types.Material | None = None,
    strap_mat: bpy.types.Material | None = None,
) -> list[bpy.types.Object]:
    """Generates dual cylindrical external fuel/battery drums on the rear chassis."""
    objects = []
    base_loc = Vector(rear_location)
    x_offset = (rear_width * 0.5) * 0.55

    for idx, side in enumerate([-1.0, 1.0]):
        d_loc = base_loc + Vector((side * x_offset, 0.0, 0.0))
        rot_cyl = Euler((0.0, math.radians(90.0), 0.0), "XYZ")

        # 1. Main cylindrical drum
        drum = make_cylinder(
            f"{prefix}_AuxDrum_{'L' if side < 0 else 'R'}",
            radius=radius,
            depth=length,
            location=d_loc,
            material=drum_mat,
            rotation=rot_cyl,
            vertices=14,
            bevel=0.015,
        )
        objects.append(drum)

        # 2. End caps
        for end in [-1.0, 1.0]:
            cap_loc = d_loc + Vector((end * (length * 0.5 + 0.01), 0.0, 0.0))
            cap = make_cylinder(
                f"{prefix}_AuxDrumCap_{'L' if side < 0 else 'R'}_{'1' if end < 0 else '2'}",
                radius=radius * 0.88,
                depth=0.03,
                location=cap_loc,
                material=mount_mat if mount_mat else drum_mat,
                rotation=rot_cyl,
                vertices=12,
                bevel=0.005,
            )
            objects.append(cap)

        # 3. Mounting cradle brackets
        for b_side in [-0.25, 0.25]:
            bracket_loc = d_loc + Vector((b_side * length, 0.08, -radius * 0.65))
            bracket = make_box(
                f"{prefix}_AuxDrumBracket_{'L' if side < 0 else 'R'}_{'F' if b_side > 0 else 'B'}",
                (0.05, 0.18, radius * 1.1),
                bracket_loc,
                mount_mat if mount_mat else drum_mat,
                bevel=0.008,
            )
            objects.append(bracket)

        # 4. Retaining straps
        if strap_mat:
            for s_pos in [-0.22, 0.22]:
                strap_loc = d_loc + Vector((s_pos * length, 0.0, 0.0))
                strap = make_cylinder(
                    f"{prefix}_AuxDrumStrap_{'L' if side < 0 else 'R'}_{'F' if s_pos > 0 else 'B'}",
                    radius=radius * 1.04,
                    depth=0.03,
                    location=strap_loc,
                    material=strap_mat,
                    rotation=rot_cyl,
                    vertices=14,
                    bevel=0.002,
                )
                objects.append(strap)

    return objects


# ---------------------------------------------------------------------------
# Procedural Structures & Tactical Factories
# ---------------------------------------------------------------------------

def make_factory_assembly(
    prefix: str,
    width: float = 6.0,
    length: float = 8.0,
    height: float = 4.5,
    foundation_mat: bpy.types.Material | None = None,
    wall_mat: bpy.types.Material | None = None,
    roof_mat: bpy.types.Material | None = None,
    door_mat: bpy.types.Material | None = None,
    hazard_mat: bpy.types.Material | None = None,
    emissive_mat: bpy.types.Material | None = None,
    metal_mat: bpy.types.Material | None = None,
) -> tuple[list[bpy.types.Object], dict[str, Vector]]:
    """Generates an industrial production factory with roll-up blast doors, crane gantry, and vents."""
    objects = []
    sockets = {}

    slab_h = 0.35
    # 1. Base foundation slab resting exactly at Z=0.0
    slab = make_box(
        f"{prefix}_FoundationSlab",
        (width * 1.12, length * 1.10, slab_h),
        location=(0.0, 0.0, slab_h * 0.5),
        material=foundation_mat,
        bevel=0.03,
    )
    objects.append(slab)
    sockets["Socket_GroundCenter"] = Vector((0.0, 0.0, 0.0))

    # 2. Main industrial production hall
    hall_z = slab_h + height * 0.5
    hall = make_box(
        f"{prefix}_MainHall",
        (width, length, height),
        location=(0.0, 0.0, hall_z),
        material=wall_mat,
        bevel=0.04,
    )
    objects.append(hall)

    # 2b. Structural corner buttress pilasters with angled footing taluds
    buttress_w = width * 0.14
    buttress_d = length * 0.12
    buttress_h = height * 0.75
    for sx in (-1, 1):
        for sy in (-1, 1):
            btag = f"{'F' if sy > 0 else 'R'}{'L' if sx < 0 else 'R'}"
            bx = sx * (width * 0.5 + buttress_w * 0.25)
            by = sy * (length * 0.5 + buttress_d * 0.25)
            bz = slab_h + buttress_h * 0.5
            objects.append(make_box(
                f"{prefix}_CornerButtress_{btag}",
                (buttress_w, buttress_d, buttress_h),
                (bx, by, bz),
                foundation_mat,
                bevel=0.035,
            ))

    # 2c. Electrical substation transformer and heavy cable raceway
    substation_loc = Vector((width * 0.55, -length * 0.15, slab_h + 0.65))
    objects.append(make_box(
        f"{prefix}_SubstationTransformer",
        (width * 0.16, length * 0.22, 1.30),
        substation_loc,
        metal_mat or foundation_mat,
        bevel=0.03,
    ))
    objects.append(make_box(
        f"{prefix}_CableRaceway",
        (0.08, length * 0.65, 0.08),
        Vector((width * 0.52, 0.0, slab_h + 0.12)),
        hazard_mat or metal_mat,
        bevel=0.01,
    ))

    # 3. Chamfered / Gabled roof cap with industrial skylights
    roof_h = 0.85
    roof_z = slab_h + height + roof_h * 0.5
    roof = make_box(
        f"{prefix}_RoofCap",
        (width * 1.04, length * 1.02, roof_h),
        location=(0.0, 0.0, roof_z),
        material=roof_mat if roof_mat else wall_mat,
        bevel=0.05,
    )
    objects.append(roof)

    # Skylights on roof
    if emissive_mat:
        for sky_idx in range(3):
            sky_y = -length * 0.30 + sky_idx * (length * 0.30)
            skylight = make_box(
                f"{prefix}_Skylight_{sky_idx}",
                (width * 0.50, length * 0.18, 0.12),
                location=(0.0, sky_y, roof_z + roof_h * 0.5 + 0.05),
                material=emissive_mat,
                bevel=0.01,
            )
            objects.append(skylight)

    # 4. Roll-up armored blast door at front (+Y)
    door_w = width * 0.55
    door_h = height * 0.65
    door_y = length * 0.5 + 0.02
    door_z = slab_h + door_h * 0.5
    door = make_box(
        f"{prefix}_BlastDoor",
        (door_w, 0.18, door_h),
        location=(0.0, door_y, door_z),
        material=door_mat if door_mat else metal_mat,
        bevel=0.02,
    )
    objects.append(door)
    sockets["Socket_DeployRamp"] = Vector((0.0, length * 0.55, slab_h))
    sockets["Socket_Deploy_Unit"] = Vector((0.0, length * 0.55, slab_h))

    # Hazard chevron stripe bar above blast door
    if hazard_mat:
        hazard_bar = make_box(
            f"{prefix}_DoorHazardBar",
            (door_w * 1.15, 0.22, 0.25),
            location=(0.0, door_y, door_z + door_h * 0.5 + 0.15),
            material=hazard_mat,
            bevel=0.01,
        )
        objects.append(hazard_bar)

    # 5. Exterior crane gantry frame straddling deployment apron
    gantry_w = width * 1.25
    gantry_h = height * 1.22
    gantry_y = length * 0.52
    gantry_z = gantry_h * 0.5
    for side in [-1.0, 1.0]:
        pylon = make_box(
            f"{prefix}_GantryPylon_{'R' if side > 0 else 'L'}",
            (0.35, 0.45, gantry_h),
            location=(side * (gantry_w * 0.5), gantry_y, gantry_z),
            material=metal_mat,
            bevel=0.02,
        )
        objects.append(pylon)

    crossbeam = make_box(
        f"{prefix}_GantryCrossbeam",
        (gantry_w + 0.4, 0.45, 0.40),
        location=(0.0, gantry_y, gantry_h + 0.15),
        material=metal_mat,
        bevel=0.02,
    )
    objects.append(crossbeam)

    # 6. Dual ventilation / exhaust stacks on roof
    for s_idx, s_x in enumerate([-width * 0.32, width * 0.32]):
        stack_loc = Vector((s_x, -length * 0.25, roof_z + roof_h * 0.5 + 0.75))
        stack = make_cylinder(
            f"{prefix}_VentStack_{s_idx}",
            radius=0.38,
            depth=1.5,
            location=stack_loc,
            material=metal_mat,
            rotation=None,
            vertices=12,
            bevel=0.02,
        )
        objects.append(stack)
        cap = make_cylinder(
            f"{prefix}_VentCap_{s_idx}",
            radius=0.52,
            depth=0.15,
            location=stack_loc + Vector((0.0, 0.0, 0.80)),
            material=metal_mat,
            rotation=None,
            vertices=12,
            bevel=0.01,
        )
        objects.append(cap)
        sockets[f"Socket_Exhaust_{s_idx}"] = stack_loc + Vector((0.0, 0.0, 0.90))

    # 7. Flank substation / power annexe module
    annexe_w = width * 0.32
    annexe_l = length * 0.45
    annexe_h = height * 0.48
    annexe = make_box(
        f"{prefix}_SubstationAnnexe",
        (annexe_w, annexe_l, annexe_h),
        location=(-width * 0.5 - annexe_w * 0.5, 0.0, slab_h + annexe_h * 0.5),
        material=roof_mat if roof_mat else wall_mat,
        bevel=0.02,
    )
    objects.append(annexe)

    # 8. ASME B31.3 Process piping manifold along annexe wall
    piping_start = Vector((-width * 0.5 - annexe_w * 0.88, -length * 0.30, slab_h + annexe_h * 0.45))
    objects.extend(make_process_piping_manifold(
        f"{prefix}_ProcessPiping",
        start_loc=piping_start,
        length=length * 0.65,
        pipe_radius=0.075,
        flange_radius=0.12,
        pipe_mat=metal_mat,
        flange_mat=foundation_mat,
        valve_mat=door_mat,
        accent_mat=hazard_mat,
        direction=(0.0, 1.0, 0.0),
        include_gate_valve=True,
        include_color_bands=True,
    ))

    # 9. Chemical storage pressure vessel with ASME 2:1 dished head
    silo_loc = Vector((width * 0.54, length * 0.16, slab_h))
    objects.extend(make_dished_head_pressure_vessel(
        f"{prefix}_ChemicalSilo",
        location=silo_loc,
        radius=0.68,
        height=height * 0.58,
        vessel_mat=wall_mat,
        metal_mat=metal_mat,
        accent_mat=hazard_mat,
        include_manway=True,
        include_stiffener_rings=True,
    ))

    # 10. Overhead gantry crane trolley with hoist drum and triangular gussets
    gantry_cross_loc = Vector((0.0, gantry_y, gantry_h + 0.15))
    objects.extend(make_gantry_crane_trolley_assembly(
        f"{prefix}_Gantry",
        location=gantry_cross_loc,
        span=gantry_w,
        beam_mat=metal_mat,
        hoist_mat=foundation_mat,
        accent_mat=hazard_mat,
    ))

    # 11. Industrial caged safety ladder on rear wall leading to roof
    ladder_base = Vector((-width * 0.38, -length * 0.51, slab_h))
    objects.extend(make_caged_safety_ladder(
        f"{prefix}_Ladder",
        base_location=ladder_base,
        height=height + roof_h * 0.45,
        ladder_mat=metal_mat,
        cage_mat=hazard_mat or metal_mat,
        width=0.42,
    ))

    # 12. Heavy forged crane lifting lugs on 4 roof corners
    roof_corners = [
        (-width * 0.46, length * 0.45, roof_z + roof_h * 0.48),
        (width * 0.46, length * 0.45, roof_z + roof_h * 0.48),
        (-width * 0.46, -length * 0.45, roof_z + roof_h * 0.48),
        (width * 0.46, -length * 0.45, roof_z + roof_h * 0.48),
    ]
    objects.extend(make_crane_lifting_lugs(
        f"{prefix}_Roof",
        corner_locations=roof_corners,
        material=metal_mat,
    ))

    sockets["Socket_RoofMount"] = Vector((0.0, 0.0, roof_z + roof_h * 0.5))
    sockets["Socket_DamageSmoke"] = Vector((0.0, -length * 0.25, roof_z + roof_h * 0.5))

    return objects, sockets


def make_turret_bunker_assembly(
    prefix: str,
    radius: float = 3.2,
    height: float = 2.2,
    weapon_type: str = "cannon",
    foundation_mat: bpy.types.Material | None = None,
    bunker_mat: bpy.types.Material | None = None,
    metal_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    emissive_mat: bpy.types.Material | None = None,
) -> tuple[list[bpy.types.Object], dict[str, Vector], Vector]:
    """Generates an octagonal reinforced defense turret bunker with rotatable weapon mount."""
    objects = []
    sockets = {}

    slab_h = 0.30
    # 1. Foundation octagonal slab
    slab = make_cylinder(
        f"{prefix}_BunkerFoundation",
        radius=radius * 1.15,
        depth=slab_h,
        location=(0.0, 0.0, slab_h * 0.5),
        material=foundation_mat,
        vertices=8,
        bevel=0.03,
    )
    objects.append(slab)
    sockets["Socket_GroundCenter"] = Vector((0.0, 0.0, 0.0))

    # 2. Octagonal reinforced bunker body
    body_z = slab_h + height * 0.5
    body = make_cylinder(
        f"{prefix}_BunkerBody",
        radius=radius,
        depth=height,
        location=(0.0, 0.0, body_z),
        material=bunker_mat,
        vertices=8,
        bevel=0.04,
    )
    objects.append(body)

    # 2b. Sloped armor glacis plinth ring bridging slab to bunker wall
    plinth_h = 0.45
    objects.append(make_cylinder(
        f"{prefix}_GlacisPlinth",
        radius=radius * 1.08,
        depth=plinth_h,
        location=(0.0, 0.0, slab_h + plinth_h * 0.5),
        material=foundation_mat,
        vertices=8,
        bevel=0.04,
    ))

    # 3. Turret bearing ring collar on roof
    collar_z = slab_h + height + 0.15
    collar = make_cylinder(
        f"{prefix}_BearingCollar",
        radius=radius * 0.62,
        depth=0.30,
        location=(0.0, 0.0, collar_z),
        material=metal_mat,
        vertices=16,
        bevel=0.02,
    )
    objects.append(collar)

    # 4. Faceted weapon turret housing
    turret_z = collar_z + 0.40
    turret = make_box(
        f"{prefix}_TurretHousing",
        (radius * 0.95, radius * 1.10, 0.80),
        location=(0.0, 0.10, turret_z),
        material=bunker_mat,
        bevel=0.04,
    )
    objects.append(turret)

    # 5. Sensor visor
    if emissive_mat:
        visor = make_box(
            f"{prefix}_SensorVisor",
            (radius * 0.45, 0.15, 0.18),
            location=(0.0, 0.10 + radius * 0.55 + 0.02, turret_z + 0.20),
            material=emissive_mat,
            bevel=0.01,
        )
        objects.append(visor)
        sockets["Socket_SensorOptics"] = Vector((0.0, 0.10 + radius * 0.55 + 0.08, turret_z + 0.20))

    # 6. Weapon assembly attached to turret
    weapon_pivot = Vector((0.0, 0.10 + radius * 0.55, turret_z))
    barrel_objs = []
    if weapon_type in ("dual_cannon", "cannon"):
        barrel_objs = make_coaxial_cannon_assembly(
            prefix,
            gun_pivot=weapon_pivot,
            barrel_mat=metal_mat,
            armor_mat=bunker_mat,
            accent_mat=accent_mat,
        )
    elif weapon_type == "quad_flak":
        barrel_objs = make_quad_flak_autocannon_assembly(
            prefix,
            gun_pivot=weapon_pivot,
            barrel_mat=metal_mat,
            cradle_mat=bunker_mat,
            accent_mat=accent_mat,
        )
    elif weapon_type == "railgun":
        barrel_objs = make_railgun_assembly(
            prefix,
            gun_pivot=weapon_pivot,
            rail_mat=metal_mat,
            armor_mat=bunker_mat,
            glow_mat=emissive_mat,
            accent_mat=accent_mat,
        )
    else:
        barrel_objs = make_coaxial_cannon_assembly(
            prefix,
            gun_pivot=weapon_pivot,
            barrel_mat=metal_mat,
            armor_mat=bunker_mat,
            accent_mat=accent_mat,
        )
    objects.extend(barrel_objs)

    sockets["Socket_TurretPivot"] = Vector((0.0, 0.0, collar_z))
    sockets["Socket_MuzzlePrimary"] = weapon_pivot + Vector((0.0, 2.2, 0.0))
    sockets["Socket_DamageSmoke"] = Vector((0.0, 0.0, collar_z + 0.9))

    return objects, sockets, weapon_pivot


def make_radar_station_assembly(
    prefix: str,
    base_radius: float = 2.8,
    height: float = 3.5,
    foundation_mat: bpy.types.Material | None = None,
    bunker_mat: bpy.types.Material | None = None,
    metal_mat: bpy.types.Material | None = None,
    dish_mat: bpy.types.Material | None = None,
    emissive_mat: bpy.types.Material | None = None,
) -> tuple[list[bpy.types.Object], dict[str, Vector]]:
    """Generates an armored radar communications bunker with rotating lattice mast and antenna dish."""
    objects = []
    sockets = {}

    slab_h = 0.30
    # 1. Foundation slab
    slab = make_cylinder(
        f"{prefix}_RadarFoundation",
        radius=base_radius * 1.15,
        depth=slab_h,
        location=(0.0, 0.0, slab_h * 0.5),
        material=foundation_mat,
        vertices=6,
        bevel=0.03,
    )
    objects.append(slab)
    sockets["Socket_GroundCenter"] = Vector((0.0, 0.0, 0.0))

    # 2. Hexagonal command bunker core
    bunker_z = slab_h + height * 0.40
    bunker = make_cylinder(
        f"{prefix}_CommandBunker",
        radius=base_radius,
        depth=height * 0.80,
        location=(0.0, 0.0, bunker_z),
        material=bunker_mat,
        vertices=6,
        bevel=0.04,
    )
    objects.append(bunker)

    # 3. Communications lattice mast extending upward
    mast_base_z = slab_h + height * 0.80
    mast_h = height * 0.90
    mast = make_cylinder(
        f"{prefix}_CentralMast",
        radius=0.45,
        depth=mast_h,
        location=(0.0, 0.0, mast_base_z + mast_h * 0.5),
        material=metal_mat,
        vertices=8,
        bevel=0.02,
    )
    objects.append(mast)

    # 4. Rotating radar dish assembly at mast summit
    dish_z = mast_base_z + mast_h + 0.25
    dish_pivot = make_box(
        f"{prefix}_DishPivot",
        (0.60, 0.60, 0.45),
        location=(0.0, 0.0, dish_z),
        material=metal_mat,
        bevel=0.02,
    )
    objects.append(dish_pivot)

    # Elliptical parabolic reflector dish
    dish = make_box(
        f"{prefix}_RadarDish",
        (base_radius * 1.25, 0.18, base_radius * 0.75),
        location=(0.0, 0.25, dish_z + 0.45),
        material=dish_mat if dish_mat else bunker_mat,
        bevel=0.04,
        rotation=Euler((math.radians(-18.0), 0.0, 0.0), "XYZ"),
    )
    objects.append(dish)

    # Sensor feed horn extending forward from dish
    feed_horn = make_cylinder(
        f"{prefix}_FeedHorn",
        radius=0.08,
        depth=0.75,
        location=(0.0, 0.75, dish_z + 0.45),
        material=metal_mat,
        rotation=Euler((math.radians(90.0), 0.0, 0.0), "XYZ"),
        vertices=8,
        bevel=0.01,
    )
    objects.append(feed_horn)

    # Glowing sensor emitter tip
    if emissive_mat:
        tip = make_cylinder(
            f"{prefix}_EmitterTip",
            radius=0.12,
            depth=0.15,
            location=(0.0, 1.15, dish_z + 0.45),
            material=emissive_mat,
            rotation=Euler((math.radians(90.0), 0.0, 0.0), "XYZ"),
            vertices=8,
            bevel=0.01,
        )
        objects.append(tip)
        sockets["Socket_SensorOptics"] = Vector((0.0, 1.25, dish_z + 0.45))

    sockets["Socket_TurretPivot"] = Vector((0.0, 0.0, dish_z))
    sockets["Socket_DamageSmoke"] = Vector((0.0, 0.0, mast_base_z))

    return objects, sockets


def make_energy_silo_assembly(
    prefix: str,
    radius: float = 2.5,
    height: float = 5.0,
    foundation_mat: bpy.types.Material | None = None,
    silo_mat: bpy.types.Material | None = None,
    metal_mat: bpy.types.Material | None = None,
    containment_mat: bpy.types.Material | None = None,
    emissive_mat: bpy.types.Material | None = None,
) -> tuple[list[bpy.types.Object], dict[str, Vector]]:
    """Generates a heavy plasma reactor / energy silo with magnetic containment rings."""
    objects = []
    sockets = {}

    slab_h = 0.35
    # 1. Heavy circular foundation slab
    slab = make_cylinder(
        f"{prefix}_SiloFoundation",
        radius=radius * 1.30,
        depth=slab_h,
        location=(0.0, 0.0, slab_h * 0.5),
        material=foundation_mat,
        vertices=16,
        bevel=0.04,
    )
    objects.append(slab)
    sockets["Socket_GroundCenter"] = Vector((0.0, 0.0, 0.0))

    # 2. Main cylindrical reactor vessel
    vessel_z = slab_h + height * 0.5
    vessel = make_cylinder(
        f"{prefix}_ReactorVessel",
        radius=radius,
        depth=height,
        location=(0.0, 0.0, vessel_z),
        material=silo_mat,
        vertices=16,
        bevel=0.05,
    )
    objects.append(vessel)

    # 3. 4 Heavy buttress support legs anchoring the silo
    for idx, angle in enumerate([0.0, 90.0, 180.0, 270.0]):
        rad = math.radians(angle)
        buttress_x = math.cos(rad) * (radius * 1.15)
        buttress_y = math.sin(rad) * (radius * 1.15)
        buttress = make_box(
            f"{prefix}_ButtressLeg_{idx}",
            (0.40, 0.60, height * 0.85),
            location=(buttress_x, buttress_y, slab_h + height * 0.42),
            material=metal_mat,
            bevel=0.03,
            rotation=Euler((0.0, 0.0, rad), "XYZ"),
        )
        objects.append(buttress)

    # 4. Triple magnetic containment rings with emissive plasma channels
    for ring_idx in range(3):
        ring_z = slab_h + height * 0.28 + ring_idx * (height * 0.25)
        ring = make_cylinder(
            f"{prefix}_ContainmentRing_{ring_idx}",
            radius=radius * 1.12,
            depth=0.35,
            location=(0.0, 0.0, ring_z),
            material=containment_mat if containment_mat else metal_mat,
            vertices=16,
            bevel=0.02,
        )
        objects.append(ring)

        if emissive_mat:
            plasma_band = make_cylinder(
                f"{prefix}_PlasmaBand_{ring_idx}",
                radius=radius * 1.14,
                depth=0.12,
                location=(0.0, 0.0, ring_z),
                material=emissive_mat,
                vertices=16,
                bevel=0.005,
            )
            objects.append(plasma_band)

    # 5. Top cryogenic dome / ventilation cap
    dome_z = slab_h + height + 0.35
    dome = make_cylinder(
        f"{prefix}_CryoDome",
        radius=radius * 0.82,
        depth=0.70,
        location=(0.0, 0.0, dome_z),
        material=metal_mat,
        vertices=16,
        bevel=0.05,
    )
    objects.append(dome)

    sockets["Socket_RoofMount"] = Vector((0.0, 0.0, dome_z + 0.35))
    sockets["Socket_DamageSmoke"] = Vector((0.0, 0.0, dome_z))

    return objects, sockets


def make_orbital_silo_assembly(
    prefix: str,
    radius: float = 3.6,
    height: float = 4.5,
    foundation_mat: bpy.types.Material | None = None,
    bunker_mat: bpy.types.Material | None = None,
    metal_mat: bpy.types.Material | None = None,
    door_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    emissive_mat: bpy.types.Material | None = None,
) -> tuple[list[bpy.types.Object], dict[str, Vector]]:
    """Generates a heavy strategic orbital missile silo and tactical launch installation."""
    objects = []
    sockets = {}

    # 1. Octagonal foundation pit & perimeter blast berm
    objects.append(make_cylinder(f"{prefix}_FoundationSlab", radius * 1.15, 0.55, (0.0, 0.0, 0.275), foundation_mat, vertices=8, bevel=0.06))
    objects.append(make_cylinder(f"{prefix}_LaunchRing", radius * 0.85, 0.35, (0.0, 0.0, 0.65), bunker_mat, vertices=8, bevel=0.04))

    # 2. Silo launch pit aperture (inner well)
    objects.append(make_cylinder(f"{prefix}_SiloAperture", radius * 0.52, 0.15, (0.0, 0.0, 0.78), metal_mat, vertices=12, bevel=0.02))

    # 3. Dual book-opening heavy armored blast door leaves
    door_w = radius * 0.56
    door_l = radius * 0.95
    door_h = 0.18
    for side in (-1, 1):
        stag = "L" if side < 0 else "R"
        objects.append(make_box(
            f"{prefix}_BlastDoor_{stag}",
            (door_w, door_l, door_h),
            (side * (door_w * 0.48), 0.0, 0.88),
            door_mat, bevel=0.025
        ))
        # Hydraulic actuator cylinders on door flanks
        objects.append(make_cylinder(
            f"{prefix}_DoorRam_{stag}", 0.065, 0.45,
            (side * (radius * 0.62), 0.0, 0.92), metal_mat, vertices=8,
            rotation=(0.0, math.radians(side * 28), 0.0)
        ))

    # 4. Vertical gantry crane tower & umbilical service boom
    tower_x = -radius * 0.78
    tower_y = radius * 0.45
    objects.append(make_box(f"{prefix}_GantryColumn", (0.55, 0.55, height), (tower_x, tower_y, height * 0.5), bunker_mat, bevel=0.03))
    objects.append(make_box(f"{prefix}_UmbilicalBoom", (radius * 0.65, 0.28, 0.25), (tower_x + (radius * 0.32), tower_y, height * 0.85), metal_mat, bevel=0.02))
    objects.append(make_cylinder(f"{prefix}_WarningBeacon", 0.045, 0.08, (tower_x, tower_y, height + 0.04), emissive_mat, vertices=8))

    # 5. Exhaust deflector trench duct
    objects.append(make_box(f"{prefix}_ExhaustFlume", (radius * 0.65, radius * 0.82, 0.45), (0.0, -radius * 0.68, 0.32), foundation_mat, bevel=0.035))

    # Sockets
    sockets["Socket_MuzzlePrimary"] = Vector((0.0, 0.0, 0.90))
    sockets["Socket_Deploy_Unit"] = Vector((0.0, 0.0, 0.85))
    sockets["Socket_DeployRamp"] = Vector((0.0, 0.0, 0.85))
    sockets["Socket_RadarMast"] = Vector((tower_x, tower_y, height + 0.10))
    sockets["Socket_DamageSmoke"] = Vector((0.0, -radius * 0.68, 0.55))
    sockets["Socket_DamageFire"] = Vector((0.0, 0.0, 0.92))
    sockets["Socket_Antenna"] = Vector((tower_x, tower_y, height + 0.15))
    sockets["Socket_GroundCenter"] = Vector((0.0, 0.0, 0.0))

    return objects, sockets


def make_shield_dome_assembly(
    prefix: str,
    radius: float = 3.2,
    height: float = 3.8,
    foundation_mat: bpy.types.Material | None = None,
    bunker_mat: bpy.types.Material | None = None,
    metal_mat: bpy.types.Material | None = None,
    coil_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    emissive_mat: bpy.types.Material | None = None,
) -> tuple[list[bpy.types.Object], dict[str, Vector]]:
    """Generates a stationary tactical shield dome generator with converging emitter pylons, toroidal field coils, and glowing projector core."""
    objects = []
    sockets = {}

    # 1. Octagonal armored foundation slab resting at Z=0.0
    slab_h = 0.35
    objects.append(make_cylinder(
        f"{prefix}_FoundationSlab", radius * 1.15, slab_h,
        (0.0, 0.0, slab_h * 0.5), foundation_mat, vertices=8, bevel=0.05
    ))
    objects.append(make_cylinder(
        f"{prefix}_GeneratorBaseRing", radius * 0.88, 0.55,
        (0.0, 0.0, slab_h + 0.275), bunker_mat, vertices=8, bevel=0.04
    ))

    # 2. Central glowing plasma core column & induction rings
    core_h = height * 0.72
    core_z = slab_h + 0.55 + core_h * 0.5
    objects.append(make_cylinder(
        f"{prefix}_PlasmaCoreColumn", radius * 0.26, core_h,
        (0.0, 0.0, core_z), emissive_mat or coil_mat, vertices=12, bevel=0.02
    ))
    for idx, t in enumerate((0.25, 0.55, 0.85), start=1):
        ring_z = slab_h + 0.55 + core_h * t
        objects.append(make_torus(
            f"{prefix}_FieldCoil_{idx}",
            location=(0.0, 0.0, ring_z),
            major_radius=radius * 0.42,
            minor_radius=0.08,
            material=coil_mat or metal_mat,
            major_segments=16, minor_segments=8
        ))

    # 3. Four converging diagonal emitter pylons & flank capacitor banks
    for i in range(4):
        angle = math.radians(45.0 + i * 90.0)
        cos_a = math.cos(angle)
        sin_a = math.sin(angle)
        base_pt = Vector((cos_a * radius * 0.78, sin_a * radius * 0.78, slab_h + 0.50))
        apex_pt = Vector((cos_a * radius * 0.32, sin_a * radius * 0.32, height * 0.88))
        objects.append(make_rod(
            f"{prefix}_EmitterPylon_{i+1}", base_pt, apex_pt, 0.16, bunker_mat, vertices=8, bevel=0.015
        ))
        objects.append(make_box(
            f"{prefix}_CapacitorBank_{i+1}",
            (0.48, 0.65, 0.52),
            (cos_a * radius * 0.85, sin_a * radius * 0.85, slab_h + 0.48),
            coil_mat or metal_mat, bevel=0.025, rotation=(0.0, 0.0, angle)
        ))

    # 4. Upper toroidal shield projector crown & geodesic emitter dome cap
    crown_z = height * 0.90
    objects.append(make_torus(
        f"{prefix}_ProjectorCrownRing",
        location=(0.0, 0.0, crown_z),
        major_radius=radius * 0.46,
        minor_radius=0.11,
        material=accent_mat or bunker_mat,
        major_segments=16, minor_segments=8
    ))
    objects.append(make_cylinder(
        f"{prefix}_EmitterDomeCap", radius * 0.36, 0.28,
        (0.0, 0.0, crown_z + 0.12), emissive_mat, vertices=12, bevel=0.06
    ))

    sockets["Socket_GroundCenter"] = Vector((0.0, 0.0, 0.0))
    sockets["Socket_MuzzlePrimary"] = Vector((0.0, 0.0, crown_z + 0.28))
    sockets["Socket_RoofMount"] = Vector((0.0, 0.0, crown_z + 0.26))
    sockets["Socket_RadarMast"] = Vector((0.0, 0.0, crown_z + 0.30))
    sockets["Socket_DamageSmoke"] = Vector((-0.45, -0.45, slab_h + 0.95))
    sockets["Socket_DamageFire"] = Vector((0.35, 0.35, slab_h + 1.10))

    return objects, sockets


def make_emp_obelisk_assembly(
    prefix: str,
    radius: float = 2.8,
    height: float = 5.2,
    foundation_mat: bpy.types.Material | None = None,
    bunker_mat: bpy.types.Material | None = None,
    metal_mat: bpy.types.Material | None = None,
    coil_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    emissive_mat: bpy.types.Material | None = None,
) -> tuple[list[bpy.types.Object], dict[str, Vector]]:
    """Generates a fortified EMP & electronic suppression obelisk with segmented conductive column, discharge rings, and high-voltage crown."""
    objects = []
    sockets = {}

    # 1. Octagonal bunker base resting at Z=0.0
    slab_h = 0.35
    objects.append(make_cylinder(
        f"{prefix}_FoundationSlab", radius * 1.15, slab_h,
        (0.0, 0.0, slab_h * 0.5), foundation_mat, vertices=8, bevel=0.05
    ))
    bunker_h = 1.10
    objects.append(make_cylinder(
        f"{prefix}_TransformerBunker", radius * 0.85, bunker_h,
        (0.0, 0.0, slab_h + bunker_h * 0.5), bunker_mat, vertices=8, bevel=0.04
    ))

    # 2. Four radial high-voltage transformer fins
    for i in range(4):
        angle = math.radians(i * 90.0)
        fx = math.cos(angle) * (radius * 0.72)
        fy = math.sin(angle) * (radius * 0.72)
        objects.append(make_box(
            f"{prefix}_TransformerButtress_{i+1}",
            (0.42, 0.88, 1.35),
            (fx, fy, slab_h + 0.68),
            coil_mat or bunker_mat, bevel=0.03, rotation=(0.0, 0.0, angle)
        ))

    # 3. Tall segmented hexagonal conductive obelisk spire
    spire_base_z = slab_h + bunker_h
    spire_h = height - spire_base_z - 0.55
    objects.append(make_cylinder(
        f"{prefix}_ObeliskSpireLower", radius * 0.38, spire_h * 0.55,
        (0.0, 0.0, spire_base_z + spire_h * 0.275), bunker_mat, vertices=6, bevel=0.03
    ))
    objects.append(make_cylinder(
        f"{prefix}_ObeliskSpireUpper", radius * 0.26, spire_h * 0.52,
        (0.0, 0.0, spire_base_z + spire_h * 0.74), metal_mat, vertices=6, bevel=0.025
    ))

    # 4. Three toroidal EMP discharge accelerator rings along spire
    for idx, t in enumerate((0.28, 0.58, 0.86), start=1):
        rz = spire_base_z + spire_h * t
        r_maj = radius * (0.56 - idx * 0.07)
        objects.append(make_torus(
            f"{prefix}_DischargeRing_{idx}",
            location=(0.0, 0.0, rz),
            major_radius=r_maj,
            minor_radius=0.075,
            material=emissive_mat if idx == 3 else (accent_mat or metal_mat),
            major_segments=14, minor_segments=6
        ))

    # 5. High-voltage emitter crown & arc focus sphere/prism
    crown_z = height - 0.25
    objects.append(make_cylinder(
        f"{prefix}_EmitterCrown", radius * 0.34, 0.38,
        (0.0, 0.0, crown_z), emissive_mat, vertices=8, bevel=0.04
    ))
    for i in range(3):
        theta = math.radians(i * 120.0)
        p_base = Vector((math.cos(theta) * radius * 0.32, math.sin(theta) * radius * 0.32, crown_z - 0.10))
        p_tip = Vector((math.cos(theta) * radius * 0.14, math.sin(theta) * radius * 0.14, height + 0.22))
        objects.append(make_rod(
            f"{prefix}_CrownElectrode_{i+1}", p_base, p_tip, 0.045, metal_mat, vertices=6
        ))

    sockets["Socket_GroundCenter"] = Vector((0.0, 0.0, 0.0))
    sockets["Socket_MuzzlePrimary"] = Vector((0.0, 0.0, height + 0.18))
    sockets["Socket_SensorOptics"] = Vector((0.0, 0.0, crown_z))
    sockets["Socket_Antenna"] = Vector((0.0, 0.0, height + 0.22))
    sockets["Socket_DamageSmoke"] = Vector((0.0, -radius * 0.45, spire_base_z + 0.25))
    sockets["Socket_DamageFire"] = Vector((0.30, 0.25, spire_base_z + 0.40))

    return objects, sockets


def make_bunker_pillbox_assembly(
    prefix: str,
    radius: float = 3.0,
    height: float = 2.0,
    foundation_mat: bpy.types.Material | None = None,
    bunker_mat: bpy.types.Material | None = None,
    metal_mat: bpy.types.Material | None = None,
    roof_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    emissive_mat: bpy.types.Material | None = None,
) -> tuple[list[bpy.types.Object], dict[str, Vector]]:
    """Generates a fortified octagonal casemate pillbox with recessed firing embrasure slits, blast overhang roof, and twin coax cannons."""
    objects = []
    sockets = {}

    slab_h = 0.32
    objects.append(make_cylinder(
        f"{prefix}_FoundationSlab", radius * 1.15, slab_h,
        (0.0, 0.0, slab_h * 0.5), foundation_mat, vertices=8, bevel=0.05
    ))

    wall_h = height - slab_h - 0.35
    objects.append(make_cylinder(
        f"{prefix}_CasemateWall", radius * 0.88, wall_h,
        (0.0, 0.0, slab_h + wall_h * 0.5), bunker_mat, vertices=8, bevel=0.04
    ))

    # Overhanging sloped blast roof cap
    roof_z = slab_h + wall_h + 0.18
    objects.append(make_cylinder(
        f"{prefix}_BlastRoofCap", radius * 0.96, 0.36,
        (0.0, 0.0, roof_z), roof_mat or bunker_mat, vertices=8, bevel=0.045
    ))

    # 4 corner armor glacis buttresses
    for i in range(4):
        ang = math.radians(45.0 + i * 90.0)
        bx = math.cos(ang) * (radius * 0.86)
        by = math.sin(ang) * (radius * 0.86)
        objects.append(make_box(
            f"{prefix}_CornerButtress_{i+1}", (0.48, 0.65, wall_h * 0.92),
            (bx, by, slab_h + wall_h * 0.46), roof_mat or bunker_mat,
            bevel=0.03, rotation=(0.0, 0.0, ang)
        ))

    # Forward recessed firing embrasure slit + twin heavy machine-gun barrels
    slit_z = slab_h + wall_h * 0.58
    objects.append(make_box(
        f"{prefix}_EmbrasureMantlet", (1.45, 0.36, 0.42),
        (0.0, radius * 0.82, slit_z), metal_mat, bevel=0.025
    ))
    objects.append(make_box(
        f"{prefix}_OpticRangefinderStrip", (0.95, 0.12, 0.10),
        (0.0, radius * 0.96, slit_z + 0.14), emissive_mat, bevel=0.01
    ))
    for side, stag in ((-1, "L"), (1, "R")):
        objects.extend([
            make_cylinder(
                f"{prefix}_CasemateBarrel_{stag}", 0.065, 1.35,
                (side * 0.32, radius * 0.88 + 0.60, slit_z - 0.04),
                metal_mat, rotation=(math.radians(90.0), 0.0, 0.0), vertices=10, bevel=0.01
            ),
            make_cylinder(
                f"{prefix}_MuzzleBrake_{stag}", 0.09, 0.22,
                (side * 0.32, radius * 0.88 + 1.25, slit_z - 0.04),
                accent_mat or metal_mat, rotation=(math.radians(90.0), 0.0, 0.0), vertices=10, bevel=0.01
            ),
        ])

    # Commander armored observation cupola on roof
    objects.append(make_cylinder(
        f"{prefix}_ObservationCupola", 0.55, 0.28,
        (0.0, -0.25, roof_z + 0.26), metal_mat, vertices=8, bevel=0.025
    ))

    sockets["Socket_GroundCenter"] = Vector((0.0, 0.0, 0.0))
    sockets["Socket_MuzzlePrimary"] = Vector((0.0, radius * 0.88 + 1.36, slit_z - 0.04))
    sockets["Socket_SensorOptics"] = Vector((0.0, radius * 0.96, slit_z + 0.14))
    sockets["Socket_RoofMount"] = Vector((0.0, -0.25, roof_z + 0.40))
    sockets["Socket_DamageSmoke"] = Vector((-0.45, -0.55, roof_z + 0.20))
    sockets["Socket_DamageFire"] = Vector((0.35, -0.35, slit_z + 0.20))

    return objects, sockets


def make_anti_air_battery_assembly(
    prefix: str,
    radius: float = 3.2,
    height: float = 3.6,
    foundation_mat: bpy.types.Material | None = None,
    bunker_mat: bpy.types.Material | None = None,
    metal_mat: bpy.types.Material | None = None,
    turret_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    emissive_mat: bpy.types.Material | None = None,
) -> tuple[list[bpy.types.Object], dict[str, Vector]]:
    """Generates a fortified SAM & CIWS air-defense battery with rotatable pedestal, twin quad-SAM pods, and fire-control radar."""
    objects = []
    sockets = {}

    slab_h = 0.35
    objects.append(make_cylinder(
        f"{prefix}_FoundationSlab", radius * 1.12, slab_h,
        (0.0, 0.0, slab_h * 0.5), foundation_mat, vertices=8, bevel=0.05
    ))

    ped_h = 1.25
    objects.append(make_cylinder(
        f"{prefix}_PedestalBunker", radius * 0.78, ped_h,
        (0.0, 0.0, slab_h + ped_h * 0.5), bunker_mat, vertices=8, bevel=0.04
    ))

    turret_z = slab_h + ped_h + 0.42
    objects.append(make_box(
        f"{prefix}_AzimuthTurretHouse", (1.65, 1.85, 0.82),
        (0.0, 0.0, turret_z), turret_mat or bunker_mat, bevel=0.04
    ))

    # Twin elevated 4-cell SAM launcher pods on trunnion arms
    pod_elev = math.radians(-28.0)
    for side, stag in ((-1, "L"), (1, "R")):
        px = side * 1.32
        objects.append(make_box(
            f"{prefix}_SAMPodHousing_{stag}", (0.72, 1.75, 0.68),
            (px, 0.15, turret_z + 0.22), bunker_mat, bevel=0.03,
            rotation=(pod_elev, 0.0, 0.0)
        ))
        for r_idx, rz_off in enumerate((-0.16, 0.16)):
            for c_idx, cx_off in enumerate((-0.18, 0.18)):
                objects.append(make_cylinder(
                    f"{prefix}_SAMTube_{stag}_{r_idx}{c_idx}", 0.11, 1.82,
                    (px + cx_off, 0.20, turret_z + 0.22 + rz_off),
                    accent_mat or metal_mat, rotation=(math.radians(62.0), 0.0, 0.0),
                    vertices=10, bevel=0.01
                ))

    # Central planar fire-control phased-array radar & optical tracker above turret
    mast_z = turret_z + 0.72
    objects.extend([
        make_cylinder(f"{prefix}_RadarMast", 0.22, 0.58, (0.0, -0.25, mast_z), metal_mat, vertices=10, bevel=0.02),
        make_box(
            f"{prefix}_FireControlRadarDish", (1.45, 0.18, 0.62),
            (0.0, -0.25, mast_z + 0.45), emissive_mat, bevel=0.02,
            rotation=(math.radians(-16.0), 0.0, 0.0)
        ),
    ])

    sockets["Socket_GroundCenter"] = Vector((0.0, 0.0, 0.0))
    sockets["Socket_MuzzlePrimary"] = Vector((0.0, 1.15, turret_z + 0.65))
    sockets["Socket_RadarMast"] = Vector((0.0, -0.25, mast_z + 0.45))
    sockets["Socket_SensorOptics"] = Vector((0.0, 0.92, turret_z + 0.25))
    sockets["Socket_DamageSmoke"] = Vector((-0.55, -0.55, slab_h + ped_h))
    sockets["Socket_DamageFire"] = Vector((0.45, 0.20, turret_z))

    return objects, sockets


def make_comms_relay_tower_assembly(
    prefix: str,
    radius: float = 2.8,
    height: float = 5.6,
    foundation_mat: bpy.types.Material | None = None,
    bunker_mat: bpy.types.Material | None = None,
    metal_mat: bpy.types.Material | None = None,
    dish_mat: bpy.types.Material | None = None,
    accent_mat: bpy.types.Material | None = None,
    emissive_mat: bpy.types.Material | None = None,
) -> tuple[list[bpy.types.Object], dict[str, Vector]]:
    """Generates a strategic C4ISR communications relay tower with lattice truss mast, microwave drums, and high-gain uplink array."""
    objects = []
    sockets = {}

    slab_h = 0.32
    objects.append(make_cylinder(
        f"{prefix}_FoundationSlab", radius * 1.12, slab_h,
        (0.0, 0.0, slab_h * 0.5), foundation_mat, vertices=8, bevel=0.05
    ))

    # Armored server & transceiver shelter base
    shelter_h = 1.15
    objects.append(make_box(
        f"{prefix}_TransceiverShelter", (radius * 1.35, radius * 1.35, shelter_h),
        (0.0, 0.0, slab_h + shelter_h * 0.5), bunker_mat, bevel=0.04
    ))

    # 4 tapered lattice truss corner legs rising to crown platform
    base_z = slab_h + shelter_h
    top_z = height - 0.55
    r_bot = 0.78
    r_top = 0.36
    corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    for idx, (sx, sy) in enumerate(corners, start=1):
        p0 = Vector((sx * r_bot, sy * r_bot, base_z))
        p1 = Vector((sx * r_top, sy * r_top, top_z))
        objects.append(make_rod(
            f"{prefix}_LatticeChord_{idx}", p0, p1, 0.065, metal_mat, vertices=8
        ))
        # Diagonal cross-bracing struts
        nx, ny = corners[idx % 4]
        p_mid = Vector((nx * (r_bot + r_top) * 0.5, ny * (r_bot + r_top) * 0.5, (base_z + top_z) * 0.5))
        objects.append(make_rod(
            f"{prefix}_LatticeBraceA_{idx}", p0, p_mid, 0.035, metal_mat, vertices=6
        ))
        objects.append(make_rod(
            f"{prefix}_LatticeBraceB_{idx}", p_mid, p1, 0.035, metal_mat, vertices=6
        ))

    # Bilateral microwave relay drum dishes mounted mid-mast
    mid_z = (base_z + top_z) * 0.55
    for side, stag in ((-1, "L"), (1, "R")):
        objects.extend([
            make_cylinder(
                f"{prefix}_MicrowaveDrum_{stag}", 0.44, 0.28,
                (side * 0.82, 0.18, mid_z), dish_mat or bunker_mat,
                rotation=(math.radians(90.0), 0.0, math.radians(side * -18.0)),
                vertices=12, bevel=0.025
            ),
            make_cylinder(
                f"{prefix}_MicrowaveFeed_{stag}", 0.14, 0.34,
                (side * 0.82, 0.24, mid_z), emissive_mat,
                rotation=(math.radians(90.0), 0.0, math.radians(side * -18.0)),
                vertices=10, bevel=0.01
            ),
        ])

    # Crown transceiver ring & vertical omni-directional whip array
    objects.append(make_torus(
        f"{prefix}_TelemetryCrownRing", (0.0, 0.0, top_z),
        major_radius=0.62, minor_radius=0.065, material=accent_mat or metal_mat,
        major_segments=14, minor_segments=6
    ))
    objects.append(make_cylinder(
        f"{prefix}_OmniBeaconSpire", 0.08, 0.85,
        (0.0, 0.0, top_z + 0.42), emissive_mat, vertices=8, bevel=0.015
    ))

    sockets["Socket_GroundCenter"] = Vector((0.0, 0.0, 0.0))
    sockets["Socket_Antenna"] = Vector((0.0, 0.0, top_z + 0.85))
    sockets["Socket_RadarMast"] = Vector((0.0, 0.0, mid_z))
    sockets["Socket_SensorOptics"] = Vector((0.0, 0.85, mid_z))
    sockets["Socket_DamageSmoke"] = Vector((-0.55, -0.55, base_z))
    sockets["Socket_DamageFire"] = Vector((0.40, 0.35, base_z * 0.75))

    return objects, sockets


# ---------------------------------------------------------------------------
# Procedural Armature Rigging & Animation Actions
# ---------------------------------------------------------------------------

def normalize_arachnid_middle_leg_orientation(armature: bpy.types.Object) -> None:
    """Keep ML/MR shin and foot bones connected at the authored ground contact.

    The middle-leg chains are synthesized by the Arachnid rig builder.  Older
    normalization logic mirrored a positive shin tail to negative Z without
    moving the child foot head, which detached the pad and deformed the FBX.
    Repair the joint in edit space and retain the authored downward foot length.
    """
    if armature is None or armature.type != "ARMATURE":
        return
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode="EDIT")
    try:
        edit_bones = armature.data.edit_bones
        for tag in ("ML", "MR"):
            shin = edit_bones.get(f"Bone_Shin_{tag}")
            foot = edit_bones.get(f"Bone_Foot_{tag}")
            if shin is None or foot is None:
                continue
            foot_length = max((foot.tail - foot.head).length, 0.20)
            if shin.tail.z < 0.0:
                shin.tail.z = abs(shin.tail.z)
            foot.head = shin.tail
            foot.tail = foot.head + Vector((0.0, 0.0, -foot_length))
            foot.parent = shin
            foot.use_connect = False
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")


def assign_rigid_mesh_weights(
    objects: Sequence[bpy.types.Object],
    prefix: str,
    armature_obj: bpy.types.Object,
    leg_type: str | None = None,
) -> None:
    """Assigns rigid vertex groups (weight 1.0) and Armature modifiers to hard-surface model components."""
    for obj in objects:
        if obj.type != "MESH" or obj.name.endswith(("_COLLIDER", "_ShieldBubble")):
            continue

        # Rigid rebinding must not inherit stale groups/modifiers from authored
        # or previously generated versions of the same mesh.
        for vertex_group in list(obj.vertex_groups):
            obj.vertex_groups.remove(vertex_group)
        for modifier in [m for m in obj.modifiers if m.type == "ARMATURE"]:
            obj.modifiers.remove(modifier)

        clean_name = obj.name
        if clean_name.startswith(prefix):
            clean_name = clean_name[len(prefix):]

        target_bone = None
        if leg_type == "biped":
            side_str = "L" if ("_L" in clean_name or "Left" in clean_name) else ("R" if ("_R" in clean_name or "Right" in clean_name) else None)
            if side_str:
                if any(k in clean_name for k in ("Foot", "Toe", "Ankle")):
                    target_bone = f"Bone_Foot_{side_str}"
                elif any(k in clean_name for k in ("Shin", "Knee", "Hydraulic", "Piston")):
                    target_bone = f"Bone_Shin_{side_str}"
                elif any(k in clean_name for k in ("Thigh", "HipJoint", "Femur")):
                    target_bone = f"Bone_Thigh_{side_str}"

            if target_bone is None and any(k in clean_name for k in ("Pelvis", "HipHub")):
                target_bone = "Bone_Pelvis"

        elif leg_type == "quad":
            tag_found = None
            # Extract the authored leg tag from "CrawlerLeg_<tag>_" so names such
            # as "..._FemurBoom" cannot masquerade as a different leg tag.
            crawler_match = re.search(r"CrawlerLeg_([A-Z](?:_[A-Z])?)_", clean_name)
            if crawler_match:
                raw_tag = crawler_match.group(1)
                tag_found = {
                    "FL": "FL", "FR": "FR", "RL": "RL", "RR": "RR",
                    "L_F": "FL", "R_F": "FR", "L_R": "RL", "R_R": "RR",
                }.get(raw_tag)
            if tag_found is None:
                if "_ML" in clean_name or clean_name.endswith("ML"):
                    tag_found = "ML"
                elif "_MR" in clean_name or clean_name.endswith("MR"):
                    tag_found = "MR"
            if tag_found in ("FL", "FR", "RL", "RR"):
                if any(k in clean_name for k in ("Foot", "Talon")):
                    target_bone = f"Bone_Foot_{tag_found}"
                elif any(k in clean_name for k in ("Tibia", "Knee")):
                    target_bone = f"Bone_Shin_{tag_found}"
                elif any(k in clean_name for k in ("Femur", "Hip")):
                    target_bone = f"Bone_Thigh_{tag_found}"
            elif tag_found in ("ML", "MR"):
                if any(k in clean_name for k in ("Foot", "Talon", "Pad")):
                    target_bone = f"Bone_Foot_{tag_found}"
                elif any(k in clean_name for k in ("Tibia", "Knee", "Shin")):
                    target_bone = f"Bone_Shin_{tag_found}"
                elif any(k in clean_name for k in ("Thigh", "Femur", "Hip")):
                    target_bone = f"Bone_Thigh_{tag_found}"

            if target_bone is None and ("HydraulicRam" in clean_name or "HipActuator" in clean_name or "Piston" in clean_name):
                if tag_found is None:
                    for cand, norm in [
                        ("FL", "FL"), ("FR", "FR"), ("RL", "RL"), ("RR", "RR"),
                        ("LF", "FL"), ("RF", "FR"), ("LR", "RL"), ("RR", "RR"),
                        ("ML", "ML"), ("MR", "MR"),
                    ]:
                        if cand in clean_name:
                            tag_found = norm
                            break
                if tag_found in ("ML", "MR", "FL", "FR", "RL", "RR"):
                    target_bone = f"Bone_Thigh_{tag_found}"

            if target_bone is None and any(k in clean_name for k in ("Pelvis", "MechPelvis")):
                target_bone = "Bone_Pelvis"

        if target_bone is None:
            if any(k in clean_name for k in ("Barrel", "Muzzle", "Bore", "Recoil", "Nozzle")):
                target_bone = "Bone_BarrelRecoil"
            elif any(k in clean_name for k in ("Mantlet", "Cradle", "Howitzer", "Rail", "Flak", "Mortar", "Gatling", "Pod", "Emitter", "Flamer", "EMP", "TurretPitch")):
                target_bone = "Bone_TurretPitch"
            elif any(k in clean_name for k in ("Turret", "Bustle", "Visor", "Radar", "Rotodome", "TurretPivot")):
                target_bone = "Bone_TurretYaw"
            elif leg_type == "biped":
                target_bone = "Bone_Pelvis"
            else:
                target_bone = "Bone_Hull"

        vg = obj.vertex_groups.get(target_bone) or obj.vertex_groups.new(name=target_bone)
        all_v_indices = list(range(len(obj.data.vertices)))
        if all_v_indices:
            vg.add(all_v_indices, 1.0, "REPLACE")

        arm_mod = next((m for m in obj.modifiers if m.type == "ARMATURE"), None)
        if not arm_mod:
            arm_mod = obj.modifiers.new(name="Armature", type="ARMATURE")
        arm_mod.object = armature_obj


def setup_walker_ik_armature(
    armature_obj: bpy.types.Object,
    leg_type: str | None = None,
) -> bpy.types.Object:
    """Configures 2-bone Inverse Kinematics (IK) constraints on walker leg chains."""
    if armature_obj is None or armature_obj.type != "ARMATURE" or leg_type not in ("biped", "quad"):
        return armature_obj

    prev_active = bpy.context.view_layer.objects.active
    bpy.context.view_layer.objects.active = armature_obj
    armature_obj.select_set(True)
    bpy.ops.object.mode_set(mode="POSE")
    pb = armature_obj.pose.bones

    if leg_type == "biped":
        for side in ("L", "R"):
            shin_bone = pb.get(f"Bone_Shin_{side}")
            target_bone = f"IK_Target_Foot_{side}"
            pole_bone = f"IK_Pole_Knee_{side}"
            if shin_bone and target_bone in pb:
                ik_con = next((c for c in shin_bone.constraints if c.type == "IK"), None)
                if not ik_con:
                    ik_con = shin_bone.constraints.new(type="IK")
                ik_con.target = armature_obj
                ik_con.subtarget = target_bone
                if pole_bone in pb:
                    ik_con.pole_target = armature_obj
                    ik_con.pole_subtarget = pole_bone
                    ik_con.pole_angle = math.radians(-90.0 if side == "L" else 90.0)
                ik_con.chain_count = 2
    elif leg_type == "quad":
        for tag in ("FL", "FR", "RL", "RR"):
            shin_bone = pb.get(f"Bone_Shin_{tag}")
            target_bone = f"IK_Target_Foot_{tag}"
            pole_bone = f"IK_Pole_Knee_{tag}"
            if shin_bone and target_bone in pb:
                ik_con = next((c for c in shin_bone.constraints if c.type == "IK"), None)
                if not ik_con:
                    ik_con = shin_bone.constraints.new(type="IK")
                ik_con.target = armature_obj
                ik_con.subtarget = target_bone
                if pole_bone in pb:
                    ik_con.pole_target = armature_obj
                    ik_con.pole_subtarget = pole_bone
                    ik_con.pole_angle = math.radians(-90.0 if "L" in tag else 90.0)
                ik_con.chain_count = 2

    bpy.ops.object.mode_set(mode="OBJECT")
    if prev_active and prev_active.name in bpy.data.objects:
        bpy.context.view_layer.objects.active = prev_active
    return armature_obj


def generate_procedural_armature(
    objects: Sequence[bpy.types.Object],
    prefix: str,
    turret_center: Vector | Tuple[float, float, float] | None = None,
    gun_pivot: Vector | Tuple[float, float, float] | None = None,
    hull_deck_z: float = 0.88,
    leg_type: str | None = None,
    hip_height: float = 1.45,
    hip_loc_l: Vector | Tuple[float, float, float] | None = None,
    hip_loc_r: Vector | Tuple[float, float, float] | None = None,
    knee_loc_l: Vector | Tuple[float, float, float] | None = None,
    knee_loc_r: Vector | Tuple[float, float, float] | None = None,
    foot_loc_l: Vector | Tuple[float, float, float] | None = None,
    foot_loc_r: Vector | Tuple[float, float, float] | None = None,
    quad_hips: Dict[str, Vector | Tuple[float, float, float]] | None = None,
    quad_knees: Dict[str, Vector | Tuple[float, float, float]] | None = None,
    quad_feet: Dict[str, Vector | Tuple[float, float, float]] | None = None,
    with_ik: bool = False,
) -> bpy.types.Object:
    """Creates a kinematic vehicle or articulated mech armature and binds mesh components to rigid bones."""
    tc = Vector(turret_center) if turret_center is not None else Vector((0.0, 0.05, 1.15))
    gp = Vector(gun_pivot) if gun_pivot is not None else Vector((0.0, 1.45, 1.25))

    armature_data = bpy.data.armatures.new(f"{prefix}_ArmatureData")
    armature_obj = bpy.data.objects.new(f"{prefix}_Armature", armature_data)
    bpy.context.collection.objects.link(armature_obj)
    bpy.context.view_layer.objects.active = armature_obj
    armature_obj.select_set(True)

    bpy.ops.object.mode_set(mode="EDIT")
    eb = armature_data.edit_bones

    # 1. Root bone at ground contact
    b_root = eb.new("Bone_Root")
    b_root.head = (0.0, 0.0, 0.0)
    b_root.tail = (0.0, 0.0, 0.22)

    # 2. Pelvis / Hull bones & leg chains
    if leg_type == "biped":
        b_pelvis = eb.new("Bone_Pelvis")
        b_pelvis.head = (0.0, 0.0, 0.22)
        b_pelvis.tail = (0.0, 0.0, max(0.65, hip_height))
        b_pelvis.parent = b_root

        b_hull = eb.new("Bone_Hull")
        b_hull.head = (0.0, 0.0, max(0.65, hip_height))
        b_hull.tail = (0.0, 0.0, max(0.65, hip_height) + 0.55)
        b_hull.parent = b_pelvis

        for side, hip_p, knee_p, foot_p in [
            ("L", hip_loc_l, knee_loc_l, foot_loc_l),
            ("R", hip_loc_r, knee_loc_r, foot_loc_r),
        ]:
            hip_v = Vector(hip_p) if hip_p is not None else Vector((-0.42 if side == "L" else 0.42, -0.05, hip_height))
            knee_v = Vector(knee_p) if knee_p is not None else Vector((hip_v.x + (-0.12 if side == "L" else 0.12), hip_v.y - hip_height * 0.14, hip_height * 0.58))
            foot_v = Vector(foot_p) if foot_p is not None else Vector((hip_v.x + (-0.08 if side == "L" else 0.08), hip_v.y + hip_height * 0.10 + 0.12, 0.0))

            b_thigh = eb.new(f"Bone_Thigh_{side}")
            b_thigh.head = hip_v
            b_thigh.tail = knee_v
            b_thigh.parent = b_pelvis

            b_shin = eb.new(f"Bone_Shin_{side}")
            b_shin.head = knee_v
            b_shin.tail = Vector((foot_v.x, foot_v.y, max(0.12, foot_v.z + 0.15)))
            b_shin.parent = b_thigh

            b_foot = eb.new(f"Bone_Foot_{side}")
            b_foot.head = Vector((foot_v.x, foot_v.y, max(0.12, foot_v.z + 0.15)))
            b_foot.tail = Vector((foot_v.x, foot_v.y + 0.22, 0.0))
            b_foot.parent = b_shin

            if with_ik:
                b_ik_target = eb.new(f"IK_Target_Foot_{side}")
                b_ik_target.head = Vector((foot_v.x, foot_v.y, max(0.04, foot_v.z)))
                b_ik_target.tail = Vector((foot_v.x, foot_v.y + 0.24, max(0.04, foot_v.z)))
                b_ik_target.parent = b_root
                b_ik_target.use_deform = False

                b_ik_pole = eb.new(f"IK_Pole_Knee_{side}")
                b_ik_pole.head = knee_v + Vector((0.0, 0.45, 0.0))
                b_ik_pole.tail = knee_v + Vector((0.0, 0.60, 0.0))
                b_ik_pole.parent = b_root
                b_ik_pole.use_deform = False

        turret_parent = b_pelvis

    elif leg_type == "quad":
        b_hull = eb.new("Bone_Hull")
        b_hull.head = (0.0, 0.0, 0.22)
        b_hull.tail = (0.0, 0.0, max(0.5, hull_deck_z * 0.85))
        b_hull.parent = b_root

        b_pelvis = eb.new("Bone_Pelvis")
        b_pelvis.head = (0.0, 0.0, 0.35)
        b_pelvis.tail = (0.0, 0.0, 0.75)
        b_pelvis.parent = b_hull

        q_hips = quad_hips or {}
        q_knees = quad_knees or {}
        q_feet = quad_feet or {}
        for tag in ("FL", "FR", "RL", "RR"):
            h_v = Vector(q_hips[tag]) if tag in q_hips else Vector((-0.8 if "L" in tag else 0.8, 0.8 if "F" in tag else -0.8, 0.85))
            k_v = Vector(q_knees[tag]) if tag in q_knees else Vector((h_v.x * 1.3, h_v.y * 1.2, 0.95))
            f_v = Vector(q_feet[tag]) if tag in q_feet else Vector((h_v.x * 1.6, h_v.y * 1.4, 0.0))

            b_thigh = eb.new(f"Bone_Thigh_{tag}")
            b_thigh.head = h_v
            b_thigh.tail = k_v
            b_thigh.parent = b_hull

            b_shin = eb.new(f"Bone_Shin_{tag}")
            b_shin.head = k_v
            b_shin.tail = Vector((f_v.x, f_v.y, f_v.z + 0.12))
            b_shin.parent = b_thigh

            b_foot = eb.new(f"Bone_Foot_{tag}")
            b_foot.head = Vector((f_v.x, f_v.y, f_v.z + 0.12))
            b_foot.tail = f_v
            b_foot.parent = b_shin

            if with_ik:
                b_ik_target = eb.new(f"IK_Target_Foot_{tag}")
                b_ik_target.head = Vector((f_v.x, f_v.y, max(0.04, f_v.z)))
                b_ik_target.tail = Vector((f_v.x, f_v.y + 0.22, max(0.04, f_v.z)))
                b_ik_target.parent = b_root
                b_ik_target.use_deform = False

                b_ik_pole = eb.new(f"IK_Pole_Knee_{tag}")
                pole_offset_y = 0.45 if "F" in tag else -0.45
                b_ik_pole.head = k_v + Vector((0.0, pole_offset_y, 0.0))
                b_ik_pole.tail = k_v + Vector((0.0, pole_offset_y * 1.3, 0.0))
                b_ik_pole.parent = b_root
                b_ik_pole.use_deform = False

        turret_parent = b_hull

    else:
        # Standard vehicle chassis
        b_hull = eb.new("Bone_Hull")
        b_hull.head = (0.0, 0.0, 0.22)
        b_hull.tail = (0.0, 0.0, max(0.5, hull_deck_z * 0.85))
        b_hull.parent = b_root

        b_pelvis = eb.new("Bone_Pelvis")
        b_pelvis.head = (0.0, 0.0, 0.22)
        b_pelvis.tail = (0.0, 0.0, max(0.4, hull_deck_z * 0.65))
        b_pelvis.parent = b_hull

        turret_parent = b_hull

    # 3. Turret Yaw bone (horizontal azimuth traverse)
    b_turret = eb.new("Bone_TurretYaw")
    b_turret.head = (tc.x, tc.y, tc.z)
    b_turret.tail = (tc.x, tc.y, tc.z + 0.32)
    b_turret.parent = turret_parent

    # 4. Turret Pitch bone (vertical gun elevation)
    b_pitch = eb.new("Bone_TurretPitch")
    b_pitch.head = (gp.x, gp.y, gp.z)
    b_pitch.tail = (gp.x, gp.y + 0.30, gp.z)
    b_pitch.parent = b_turret

    # 5. Barrel Recoil bone (linear backward stroke)
    b_recoil = eb.new("Bone_BarrelRecoil")
    b_recoil.head = (gp.x, gp.y + 0.30, gp.z)
    b_recoil.tail = (gp.x, gp.y + 0.85, gp.z)
    b_recoil.parent = b_pitch

    bpy.ops.object.mode_set(mode="OBJECT")

    # Bind meshes to armature with rigid vertex groups
    assign_rigid_mesh_weights(objects, prefix, armature_obj, leg_type=leg_type)

    if with_ik:
        setup_walker_ik_armature(armature_obj, leg_type=leg_type)

    return armature_obj


def generate_procedural_action_clips(
    armature_obj: bpy.types.Object,
    prefix: str,
    leg_type: str | None = None,
    fire_profile: str | None = None,
    hover: bool | None = None,
    fire_end_frame: int | None = None,
    death_end_frame: int | None = None,
    locomotion_only: bool = False,
) -> dict[str, bpy.types.Action]:
    """Generates baked NLA animation clips (Idle, Walk/Drive, FireRecoil, Death) on the armature.

    Enforces the In-Place Locomotion Invariant: Bone_Root position delta is strictly ZERO
    across all animation frames, ensuring tactical flocking translations work without double-stepping.

    ``fire_profile`` differentiates the FireRecoil impulse by mass/role for the
    tactical-operations pack (and any re-rigged unit): ``light`` (hover/medic/
    sentinel/scout) is subtle, ``medium`` is the legacy baseline, ``heavy``
    (siege/bulwark/gravitas) compresses further with a longer recovery, ``sam``
    (skyguard) pitches the launcher harder, and ``walker`` (biped) additionally
    braces the legs. ``None`` auto-resolves from ``prefix``/``leg_type`` so all
    nine tactical builders keep working without a call-site change. ``hover``
    raises the Drive bob for hovercraft (auto when the prefix suggests it).
    ``fire_end_frame``/``death_end_frame`` override the authored clip end for
    units with a verified frame contract (BastionSiegeMech validates
    FireRecoil 15 frames and Death 45 frames in Unity); ``None`` keeps the
    profile default (FireRecoil 15/18/21, Death 35). ``locomotion_only=True``
    deletes Idle/FireRecoil/Death so the rig exports only Walk/Drive;
    attack feedback then comes from the runtime turret director + muzzle VFX.
    """
    if not armature_obj.animation_data:
        armature_obj.animation_data_create()

    # Resolve the recoil profile/hover from prefix when the caller does not
    # pass one explicitly (tactical-operations builders pass neither).
    _prefix_lower = prefix.lower()
    if fire_profile is None:
        if leg_type == "biped":
            _resolved_fire_profile = "walker"
        elif any(tok in _prefix_lower for tok in ("bulwark", "gravitas", "colossus", "overlord")):
            _resolved_fire_profile = "heavy"
        elif "skyguard" in _prefix_lower:
            _resolved_fire_profile = "sam"
        elif any(tok in _prefix_lower for tok in ("tidebreaker", "fieldmedic", "sentinel", "sandlance", "skirmish")):
            _resolved_fire_profile = "light"
        else:
            _resolved_fire_profile = "medium"
    else:
        _resolved_fire_profile = fire_profile.strip().lower()
    if _resolved_fire_profile not in ("light", "medium", "heavy", "sam", "walker"):
        raise ValueError(
            f"Unknown fire_profile '{fire_profile}'; expected light|medium|heavy|sam|walker"
        )
    if hover is None:
        hover = "hovercraft" in _prefix_lower or "tidebreaker" in _prefix_lower
    # Frame-contract overrides (Bastion 15/45); defaults keep current behavior.
    if fire_end_frame is not None:
        if not 12 <= fire_end_frame <= 24:
            raise ValueError(
                f"fire_end_frame must be 12..24, got {fire_end_frame}"
            )
    if death_end_frame is not None:
        if not 30 <= death_end_frame <= 60:
            raise ValueError(
                f"death_end_frame must be 30..60, got {death_end_frame}"
            )

    actions = {}

    def set_bone_key(bone_name: str, data_path: str, frame: int, value: Sequence[float]) -> None:
        pbone = armature_obj.pose.bones.get(bone_name)
        if not pbone:
            return
        if data_path == "location":
            pbone.location = Vector(value)
            pbone.keyframe_insert(data_path="location", frame=frame)
        elif data_path == "rotation_euler":
            pbone.rotation_mode = "XYZ"
            pbone.rotation_euler = Euler(value, "XYZ")
            pbone.keyframe_insert(data_path="rotation_euler", frame=frame)

    # 1. Action_Idle (Engine vibration & stance breathing: 30 frames)
    act_idle = bpy.data.actions.new(name=f"{prefix}_Action_Idle")
    armature_obj.animation_data.action = act_idle
    base_body = "Bone_Pelvis" if leg_type == "biped" else "Bone_Hull"
    for f in (1, 30):
        set_bone_key(base_body, "location", f, (0.0, 0.0, 0.0))
        set_bone_key(base_body, "rotation_euler", f, (0.0, 0.0, 0.0))
    # RTS-legible breathing: a touch deeper than the legacy 0.006/0.35°, still subtle.
    # The f8/f22 shoulders form a slow in/out envelope around the central f15
    # breath instead of a single trapezoidal key, so the stance reads as a
    # settled mechanical idle at RTS camera distance.
    if hover:
        set_bone_key(base_body, "location", 8, (0.0, 0.0, -0.005))
        set_bone_key(base_body, "location", 15, (0.0, 0.0, -0.014))
        set_bone_key(base_body, "location", 22, (0.0, 0.0, -0.005))
    else:
        set_bone_key(base_body, "location", 8, (0.0, 0.0, -0.004))
        set_bone_key(base_body, "location", 15, (0.0, 0.0, -0.010))
        set_bone_key(base_body, "location", 22, (0.0, 0.0, -0.004))
    set_bone_key(base_body, "rotation_euler", 8, (math.radians(0.20), 0.0, 0.0))
    set_bone_key(base_body, "rotation_euler", 15, (math.radians(0.55), 0.0, 0.0))
    set_bone_key(base_body, "rotation_euler", 22, (math.radians(0.20), 0.0, 0.0))
    act_idle.use_fake_user = True
    actions["Idle"] = act_idle

    # 2. Locomotion: Action_Walk and Action_Drive (In-Place Invariant!)
    if leg_type == "biped":
        act_walk = bpy.data.actions.new(name=f"{prefix}_Action_Walk")
        armature_obj.animation_data.action = act_walk
        for f in (1, 10, 20, 30, 40):
            set_bone_key("Bone_Root", "location", f, (0.0, 0.0, 0.0))
            set_bone_key("Bone_Root", "rotation_euler", f, (0.0, 0.0, 0.0))

        # Pelvis bob, lateral weight transfer and yaw (RTS-legible gait)
        set_bone_key("Bone_Pelvis", "location", 1, (0.0, 0.0, 0.0))
        set_bone_key("Bone_Pelvis", "rotation_euler", 1, (0.0, 0.0, 0.0))
        set_bone_key("Bone_Pelvis", "location", 10, (0.035, 0.0, -0.04))
        set_bone_key("Bone_Pelvis", "rotation_euler", 10, (0.0, math.radians(0.8), math.radians(3.5)))
        set_bone_key("Bone_Pelvis", "location", 20, (0.0, 0.0, 0.015))
        set_bone_key("Bone_Pelvis", "rotation_euler", 20, (0.0, 0.0, 0.0))
        set_bone_key("Bone_Pelvis", "location", 30, (-0.035, 0.0, -0.04))
        set_bone_key("Bone_Pelvis", "rotation_euler", 30, (0.0, math.radians(-0.8), math.radians(-3.5)))
        set_bone_key("Bone_Pelvis", "location", 40, (0.0, 0.0, 0.0))
        set_bone_key("Bone_Pelvis", "rotation_euler", 40, (0.0, 0.0, 0.0))

        # Left Leg Schedule: Contact -> Down -> Passing -> Push-Off -> Contact
        biped_gait_left = {
            1: (18.0, -10.0, -8.0),
            5: (13.0, -16.0, -4.0),
            10: (8.0, -22.0, 0.0),
            15: (-1.0, -13.5, 6.0),
            20: (-10.0, -5.0, 12.0),
            25: (-15.0, 5.0, 16.0),
            30: (-20.0, 15.0, 20.0),
            35: (-1.0, 2.5, 6.0),
            40: (18.0, -10.0, -8.0),
        }
        for f, (thigh_rot, shin_rot, foot_rot) in biped_gait_left.items():
            set_bone_key("Bone_Thigh_L", "rotation_euler", f, (math.radians(thigh_rot), 0.0, 0.0))
            set_bone_key("Bone_Shin_L", "rotation_euler", f, (math.radians(shin_rot), 0.0, 0.0))
            set_bone_key("Bone_Foot_L", "rotation_euler", f, (math.radians(foot_rot), 0.0, 0.0))

        # Right Leg Schedule: 180 degrees out of phase (shifted 20 frames)
        biped_gait_right = {
            1: (-10.0, -5.0, 12.0),
            5: (-15.0, 5.0, 16.0),
            10: (-20.0, 15.0, 20.0),
            15: (-1.0, 2.5, 6.0),
            20: (18.0, -10.0, -8.0),
            25: (13.0, -16.0, -4.0),
            30: (8.0, -22.0, 0.0),
            35: (-1.0, -13.5, 6.0),
            40: (-10.0, -5.0, 12.0),
        }
        for f, (thigh_rot, shin_rot, foot_rot) in biped_gait_right.items():
            set_bone_key("Bone_Thigh_R", "rotation_euler", f, (math.radians(thigh_rot), 0.0, 0.0))
            set_bone_key("Bone_Shin_R", "rotation_euler", f, (math.radians(shin_rot), 0.0, 0.0))
            set_bone_key("Bone_Foot_R", "rotation_euler", f, (math.radians(foot_rot), 0.0, 0.0))

        # Foot lift (contact -> passing -> push-off): the swing foot rises and
        # returns to the ground plane; stance feet stay at Z=0 so the gait does
        # not slide or penetrate. Mirrors the thigh passing poses above.
        set_bone_key("Bone_Foot_L", "location", 1, (0.0, 0.0, 0.0))
        set_bone_key("Bone_Foot_L", "location", 5, (0.0, 0.01, 0.02))
        set_bone_key("Bone_Foot_L", "location", 10, (0.0, 0.0, 0.0))
        set_bone_key("Bone_Foot_L", "location", 15, (0.0, 0.02, 0.045))
        set_bone_key("Bone_Foot_L", "location", 20, (0.0, 0.04, 0.09))
        set_bone_key("Bone_Foot_L", "location", 25, (0.0, 0.03, 0.07))
        set_bone_key("Bone_Foot_L", "location", 30, (0.0, 0.02, 0.05))
        set_bone_key("Bone_Foot_L", "location", 35, (0.0, 0.01, 0.025))
        set_bone_key("Bone_Foot_L", "location", 40, (0.0, 0.0, 0.0))
        set_bone_key("Bone_Foot_R", "location", 1, (0.0, 0.02, 0.05))
        set_bone_key("Bone_Foot_R", "location", 5, (0.0, 0.03, 0.07))
        set_bone_key("Bone_Foot_R", "location", 10, (0.0, 0.04, 0.09))
        set_bone_key("Bone_Foot_R", "location", 15, (0.0, 0.02, 0.045))
        set_bone_key("Bone_Foot_R", "location", 20, (0.0, 0.0, 0.0))
        set_bone_key("Bone_Foot_R", "location", 25, (0.0, 0.0, 0.0))
        set_bone_key("Bone_Foot_R", "location", 30, (0.0, 0.0, 0.0))
        set_bone_key("Bone_Foot_R", "location", 35, (0.0, 0.01, 0.025))
        set_bone_key("Bone_Foot_R", "location", 40, (0.0, 0.02, 0.05))

        act_walk.use_fake_user = True
        actions["Walk"] = act_walk
        actions["Drive"] = act_walk

    elif leg_type == "quad":
        act_walk = bpy.data.actions.new(name=f"{prefix}_Action_Walk")
        armature_obj.animation_data.action = act_walk
        for f in (1, 10, 20, 30, 40):
            set_bone_key("Bone_Root", "location", f, (0.0, 0.0, 0.0))
            set_bone_key("Bone_Root", "rotation_euler", f, (0.0, 0.0, 0.0))

        # Hull diagonal pitch/roll oscillation
        for f, (pz, rx, ry) in [
            (1, (0.0, 0.0, 0.0)),
            (10, (-0.025, -0.5, 0.4)),
            (20, (0.012, 0.0, 0.0)),
            (30, (-0.025, 0.5, -0.4)),
            (40, (0.0, 0.0, 0.0)),
        ]:
            set_bone_key("Bone_Hull", "location", f, (0.0, 0.0, pz))
            set_bone_key("Bone_Hull", "rotation_euler", f, (math.radians(rx), math.radians(ry), 0.0))

        quad_gait_diag1 = {
            1: (15.0, -12.0, -6.0),
            10: (5.0, -18.0, 0.0),
            20: (-8.0, -4.0, 8.0),
            30: (-16.0, 12.0, 14.0),
            40: (15.0, -12.0, -6.0),
        }
        quad_gait_diag2 = {
            1: (-8.0, -4.0, 8.0),
            10: (-16.0, 12.0, 14.0),
            20: (15.0, -12.0, -6.0),
            30: (5.0, -18.0, 0.0),
            40: (-8.0, -4.0, 8.0),
        }
        for tag in ("FL", "RR"):
            for f, (thigh_rot, shin_rot, foot_rot) in quad_gait_diag1.items():
                set_bone_key(f"Bone_Thigh_{tag}", "rotation_euler", f, (math.radians(thigh_rot), 0.0, 0.0))
                set_bone_key(f"Bone_Shin_{tag}", "rotation_euler", f, (math.radians(shin_rot), 0.0, 0.0))
                set_bone_key(f"Bone_Foot_{tag}", "rotation_euler", f, (math.radians(foot_rot), 0.0, 0.0))
        for tag in ("FR", "RL"):
            for f, (thigh_rot, shin_rot, foot_rot) in quad_gait_diag2.items():
                set_bone_key(f"Bone_Thigh_{tag}", "rotation_euler", f, (math.radians(thigh_rot), 0.0, 0.0))
                set_bone_key(f"Bone_Shin_{tag}", "rotation_euler", f, (math.radians(shin_rot), 0.0, 0.0))
                set_bone_key(f"Bone_Foot_{tag}", "rotation_euler", f, (math.radians(foot_rot), 0.0, 0.0))

        # Middle legs complete the hexapod tripod gait.
        for tag, gait in (("ML", quad_gait_diag2), ("MR", quad_gait_diag1)):
            for f, (thigh_rot, shin_rot, foot_rot) in gait.items():
                set_bone_key(f"Bone_Thigh_{tag}", "rotation_euler", f, (math.radians(thigh_rot), 0.0, 0.0))
                set_bone_key(f"Bone_Shin_{tag}", "rotation_euler", f, (math.radians(shin_rot), 0.0, 0.0))
                set_bone_key(f"Bone_Foot_{tag}", "rotation_euler", f, (math.radians(foot_rot), 0.0, 0.0))

        act_walk.use_fake_user = True
        actions["Walk"] = act_walk
        actions["Drive"] = act_walk

    else:
        # Wheeled or Tracked vehicle: Action_Drive and alias Action_Walk.
        # Hull bob + pitch/roll differential (front vs rear) so the motion reads
        # as a vehicle at RTS distance; hovercraft float higher with a softer tilt.
        act_drive = bpy.data.actions.new(name=f"{prefix}_Action_Drive")
        armature_obj.animation_data.action = act_drive
        # In-place invariant: Bone_Root stays at (0,0,0) (validator requires it)
        for _root_f in (1, 10, 20, 30, 40):
            set_bone_key("Bone_Root", "location", _root_f, (0.0, 0.0, 0.0))
        if hover:
            # Hover: 0.040 m float with lateral sway -- still an in-place hull motion.
            set_bone_key("Bone_Hull", "location", 1, (0.0, 0.0, 0.0))
            set_bone_key("Bone_Hull", "rotation_euler", 1, (0.0, 0.0, 0.0))
            set_bone_key("Bone_Hull", "location", 10, (0.010, 0.0, 0.040))
            set_bone_key("Bone_Hull", "rotation_euler", 10, (math.radians(-1.3), math.radians(1.1), 0.0))
            set_bone_key("Bone_Hull", "location", 20, (0.0, 0.0, -0.010))
            set_bone_key("Bone_Hull", "rotation_euler", 20, (math.radians(1.0), math.radians(-0.8), 0.0))
            set_bone_key("Bone_Hull", "location", 30, (-0.010, 0.0, 0.032))
            set_bone_key("Bone_Hull", "rotation_euler", 30, (math.radians(-0.9), math.radians(0.8), 0.0))
            set_bone_key("Bone_Hull", "location", 40, (0.0, 0.0, 0.0))
            set_bone_key("Bone_Hull", "rotation_euler", 40, (0.0, 0.0, 0.0))
        else:
            for f in (1, 40):
                set_bone_key("Bone_Hull", "location", f, (0.0, 0.0, 0.0))
                set_bone_key("Bone_Hull", "rotation_euler", f, (0.0, 0.0, 0.0))
            set_bone_key("Bone_Hull", "location", 10, (0.008, 0.0, 0.025))
            set_bone_key("Bone_Hull", "rotation_euler", 10, (math.radians(-1.6), math.radians(1.4), 0.0))
            set_bone_key("Bone_Hull", "location", 20, (0.0, 0.0, -0.012))
            set_bone_key("Bone_Hull", "rotation_euler", 20, (math.radians(1.1), math.radians(-0.9), 0.0))
            set_bone_key("Bone_Hull", "location", 30, (-0.008, 0.0, 0.022))
            set_bone_key("Bone_Hull", "rotation_euler", 30, (math.radians(-0.9), math.radians(0.7), 0.0))
        act_drive.use_fake_user = True
        actions["Drive"] = act_drive
        actions["Walk"] = act_drive

    # 3. Action_FireRecoil: body-driven anticipation, impact, follow-through and recovery.
    # The chassis/pelvis must not remain static while only the barrel moves: at RTS
    # distance the body impulse is what makes a shot read as mass rather than a
    # floating weapon. All translations remain local to the rig; gameplay still owns
    # world movement and Bone_Root remains untouched.
    #
    # Role profiles (tactical-operations): the impulse scales with mass --
    # light hover/medic/sentinel is subtle, medium is the legacy baseline, heavy
    # siege (bulwark/gravitas) compresses further with a longer recovery, sam
    # pitches the launcher harder, and walker additionally braces the legs.
    # The phase map is explicit and strictly monotonic: neutral (1) -> anticipation
    # brace (3) -> impact (5) -> follow-through (7) -> recovery (11) -> neutral.
    act_fire = bpy.data.actions.new(name=f"{prefix}_Action_FireRecoil")
    armature_obj.animation_data.action = act_fire
    body_bone = "Bone_Pelvis" if leg_type == "biped" else "Bone_Hull"
    # (body_y_impact, body_pitch_deg, barrel_y_impact, pitch_deg, end_frame)
    _fire_impulse = {
        "light": (-0.016, -0.9, -0.14, -0.6, 15),
        "medium": (-0.024, -1.4, -0.22, -0.9, 15),
        "heavy": (-0.038, -2.2, -0.34, -1.6, 21),
        "sam": (-0.028, -1.8, -0.26, -2.0, 18),
        "walker": (-0.030, -1.8, -0.22, -1.2, 18),
    }
    _body_impulse_y, _body_pitch_deg, _barrel_impulse_y, _pitch_deg, _fire_end_frame = _fire_impulse[
        _resolved_fire_profile
    ]
    # Verified frame contract override (Bastion 15); otherwise keep profile end.
    if fire_end_frame is not None:
        _fire_end_frame = fire_end_frame
    # Anticipation brace is proportional to the impact (forward + up), so light
    # guns brace less and heavy siege braces more -- anticipation precedes impact.
    _brace_body_y = round(-_body_impulse_y * 0.33, 4)
    _brace_pitch_deg = round(-_body_pitch_deg * 0.18, 3)
    _brace_barrel_y = round(-_barrel_impulse_y * 0.16, 4)
    _brace_pitch = round(-_pitch_deg * 0.39, 3)
    _follow_body_y = round(_body_impulse_y * 0.42, 4)
    _follow_pitch_deg = round(-_body_pitch_deg * 0.32, 3)
    _follow_barrel_y = round(_barrel_impulse_y * 0.36, 4)
    _follow_pitch = round(-_pitch_deg * 0.28, 3)
    fire_poses = {
        1: ((0.0, 0.0, 0.0), (0.0, 0.0, 0.0), (0.0, 0.0, 0.0), (0.0, 0.0, 0.0)),
        # Anticipation: a visible forward brace (3 frames) before the impulse.
        3: (
            (0.0, _brace_body_y, 0.0),
            (math.radians(_brace_pitch_deg), 0.0, 0.0),
            (0.0, _brace_barrel_y, 0.0),
            (math.radians(_brace_pitch), 0.0, 0.0),
        ),
        # Impact: body mass compresses backward while the barrel reaches peak recoil.
        5: (
            (0.0, _body_impulse_y, 0.0),
            (math.radians(_body_pitch_deg), 0.0, 0.0),
            (0.0, _barrel_impulse_y, 0.0),
            (math.radians(_pitch_deg), 0.0, 0.0),
        ),
        # Follow-through: the mount overshoots briefly before settling.
        7: (
            (0.0, _follow_body_y, 0.0),
            (math.radians(_follow_pitch_deg), 0.0, 0.0),
            (0.0, _follow_barrel_y, 0.0),
            (math.radians(_follow_pitch), 0.0, 0.0),
        ),
        11: ((0.0, -0.002, 0.0), (math.radians(0.12), 0.0, 0.0), (0.0, -0.018, 0.0), (math.radians(0.08), 0.0, 0.0)),
        # Recovery: every authored recoil channel returns to its neutral pose.
        15: ((0.0, 0.0, 0.0), (0.0, 0.0, 0.0), (0.0, 0.0, 0.0), (0.0, 0.0, 0.0)),
    }
    for frame, (body_location, body_rotation, barrel_location, pitch_rotation) in fire_poses.items():
        set_bone_key(body_bone, "location", frame, body_location)
        set_bone_key(body_bone, "rotation_euler", frame, body_rotation)
        set_bone_key("Bone_TurretPitch", "rotation_euler", frame, pitch_rotation)
        set_bone_key("Bone_BarrelRecoil", "location", frame, barrel_location)
    # Heavy/sam/walker clips hold neutral to their later end frame so the
    # recovery reads as mass, not a snap back.
    if _fire_end_frame != 15:
        set_bone_key(body_bone, "location", _fire_end_frame, (0.0, 0.0, 0.0))
        set_bone_key(body_bone, "rotation_euler", _fire_end_frame, (0.0, 0.0, 0.0))
        set_bone_key("Bone_TurretPitch", "rotation_euler", _fire_end_frame, (0.0, 0.0, 0.0))
        set_bone_key("Bone_BarrelRecoil", "location", _fire_end_frame, (0.0, 0.0, 0.0))
    # Walker brace: legs absorb the impulse (secondary action) before recovery.
    if _resolved_fire_profile == "walker" and leg_type == "biped":
        set_bone_key("Bone_Thigh_L", "rotation_euler", 1, (0.0, 0.0, 0.0))
        set_bone_key("Bone_Thigh_R", "rotation_euler", 1, (0.0, 0.0, 0.0))
        set_bone_key("Bone_Thigh_L", "rotation_euler", 5, (math.radians(6.0), 0.0, math.radians(-4.0)))
        set_bone_key("Bone_Thigh_R", "rotation_euler", 5, (math.radians(6.0), 0.0, math.radians(4.0)))
        set_bone_key("Bone_Shin_L", "rotation_euler", 5, (math.radians(-14.0), 0.0, 0.0))
        set_bone_key("Bone_Shin_R", "rotation_euler", 5, (math.radians(-14.0), 0.0, 0.0))
        set_bone_key("Bone_Thigh_L", "rotation_euler", 13, (math.radians(2.0), 0.0, math.radians(-1.0)))
        set_bone_key("Bone_Thigh_R", "rotation_euler", 13, (math.radians(2.0), 0.0, math.radians(1.0)))
        set_bone_key("Bone_Shin_L", "rotation_euler", 13, (math.radians(-4.0), 0.0, 0.0))
        set_bone_key("Bone_Shin_R", "rotation_euler", 13, (math.radians(-4.0), 0.0, 0.0))
        set_bone_key("Bone_Thigh_L", "rotation_euler", _fire_end_frame, (0.0, 0.0, 0.0))
        set_bone_key("Bone_Thigh_R", "rotation_euler", _fire_end_frame, (0.0, 0.0, 0.0))
        set_bone_key("Bone_Shin_L", "rotation_euler", _fire_end_frame, (0.0, 0.0, 0.0))
        set_bone_key("Bone_Shin_R", "rotation_euler", _fire_end_frame, (0.0, 0.0, 0.0))
    act_fire.use_fake_user = True
    actions["FireRecoil"] = act_fire

    # 4. Action_Death (Chassis collapse & barrel droop: 35 frames)
    act_death = bpy.data.actions.new(name=f"{prefix}_Action_Death")
    armature_obj.animation_data.action = act_death
    set_bone_key(body_bone, "location", 1, (0.0, 0.0, 0.0))
    set_bone_key(body_bone, "rotation_euler", 1, (0.0, 0.0, 0.0))
    set_bone_key("Bone_TurretPitch", "rotation_euler", 1, (0.0, 0.0, 0.0))
    # Vehicle collapse must read at RTS distance: -0.22 m (legacy -0.12 was a
    # near-invisible settle). Biped/quad keep their deeper drops.
    collapse_z = -0.55 if leg_type == "biped" else (-0.28 if leg_type == "quad" else -0.22)
    # Slow in/out: frame 8 is an early 12% settle, frame 16 is half-collapse,
    # then the rest pose holds to the end (35 by default; Bastion validates
    # Death at 45 frames in Unity).
    _death_end_frame = death_end_frame if death_end_frame is not None else 35
    for f in (8, 16, 28, _death_end_frame):
        _collapse_factor = 0.12 if f == 8 else (0.5 if f == 16 else 1.0)
        set_bone_key(body_bone, "location", f, (0.0, 0.0, collapse_z * _collapse_factor))
        set_bone_key(body_bone, "rotation_euler", f, (math.radians(7.0 * _collapse_factor), math.radians(-9.0 * _collapse_factor), math.radians(3.5 * _collapse_factor)))
        set_bone_key("Bone_TurretPitch", "rotation_euler", f, (math.radians(-34.0 * _collapse_factor), 0.0, 0.0))
        if leg_type == "biped":
            set_bone_key("Bone_Thigh_L", "rotation_euler", f, (math.radians(12.0 * _collapse_factor), 0.0, math.radians(-18.0 * _collapse_factor)))
            set_bone_key("Bone_Thigh_R", "rotation_euler", f, (math.radians(12.0 * _collapse_factor), 0.0, math.radians(18.0 * _collapse_factor)))
            set_bone_key("Bone_Shin_L", "rotation_euler", f, (math.radians(-35.0 * _collapse_factor), 0.0, 0.0))
            set_bone_key("Bone_Shin_R", "rotation_euler", f, (math.radians(-35.0 * _collapse_factor), 0.0, 0.0))

    act_death.use_fake_user = True
    actions["Death"] = act_death

    # Tactical locomotion-only: remove non-movement clips; the turret director
    # plus muzzle VFX provide attack feedback trained on attack cooldown.
    if locomotion_only:
        for _key in ("Idle", "FireRecoil", "Death"):
            _act = actions.pop(_key, None)
            if _act is not None:
                try:
                    bpy.data.actions.remove(_act)
                except ReferenceError:
                    pass
        _locomotion = actions.get("Walk") or actions.get("Drive")
        if _locomotion is not None:
            armature_obj.animation_data.action = _locomotion

    # Push actions to NLA tracks for complete FBX export
    for name, act in actions.items():
        track = armature_obj.animation_data.nla_tracks.new()
        track.name = f"Track_{name}"
        track.strips.new(name, 1, act)

    return actions


# ---------------------------------------------------------------------------
# Texture Atlas Baking & Single-Material Optimization
# ---------------------------------------------------------------------------

def unwrap_atlas_uvs(
    objects: Sequence[bpy.types.Object],
    island_margin: float = 0.01,
) -> None:
    """Generates a unified, non-overlapping UV layout across all mesh objects into 'UVMap_Atlas'.

    Smart Project gives per-island unwrapping; a follow-up `pack_islands` pass rotates and
    scales the islands to fill the atlas tightly (higher, more uniform texel density) while
    keeping `island_margin` (fraction of the atlas) of gutter for mip/bake bleed.
    """
    bpy.ops.object.select_all(action="DESELECT")
    valid_mesh_objs = [o for o in objects if o.type == "MESH" and not o.name.endswith(("_COLLIDER", "_ShieldBubble"))]
    if not valid_mesh_objs:
        return

    for obj in valid_mesh_objs:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = valid_mesh_objs[0]

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(
        angle_limit=66.0,
        island_margin=island_margin,
        correct_aspect=True,
        scale_to_bounds=True
    )
    try:
        bpy.ops.uv.pack_islands(
            rotate=True,
            scale=True,
            margin_method="FRACTION",
            margin=island_margin,
        )
    except (TypeError, RuntimeError) as exc:
        print(f"Warning: UV island packing skipped: {exc}")
    bpy.ops.object.mode_set(mode="OBJECT")


def bake_pbr_texture_atlas(
    objects: Sequence[bpy.types.Object],
    prefix: str,
    output_dir: Path,
    resolution: int = 1024,
    palette: FactionPalette | None = None,
) -> dict[str, Path]:
    """Bakes unified PBR texture maps (Albedo, MetallicGloss, Normal, Emission) for atlas meshes."""
    output_dir.mkdir(parents=True, exist_ok=True)
    maps = {}

    unwrap_atlas_uvs(objects)

    valid_mesh_objs = [o for o in objects if o.type == "MESH" and not o.name.endswith(("_COLLIDER", "_ShieldBubble"))]
    if not valid_mesh_objs:
        return maps

    scene = bpy.context.scene
    prev_engine = scene.render.engine
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 1
    scene.cycles.bake_type = "DIFFUSE"

    # Unity expects OpenGL tangent-space normals (+X +Y +Z). Make that explicit and give the
    # bake a pixel gutter that is wide enough for mipmaps but scales with atlas resolution.
    bake_settings = scene.render.bake
    bake_settings.normal_space = "TANGENT"
    bake_settings.normal_r, bake_settings.normal_g, bake_settings.normal_b = "POS_X", "POS_Y", "POS_Z"
    bake_settings.margin_type = "EXTEND"
    bake_settings.margin = max(4, resolution // 128)
    bake_settings.use_clear = True

    def mark_data_image(image):
        # Normal, roughness and metallic are data, not color: without Non-Color the sRGB
        # transform would gamma-encode them when the PNG is written.
        image.colorspace_settings.name = "Non-Color"
        return image

    img_albedo = bpy.data.images.new(f"{prefix}_Albedo", resolution, resolution, alpha=True)
    img_emission = bpy.data.images.new(f"{prefix}_Emission", resolution, resolution, alpha=False)
    img_normal = mark_data_image(bpy.data.images.new(f"{prefix}_Normal", resolution, resolution, alpha=False))
    img_metallic = mark_data_image(bpy.data.images.new(f"{prefix}_MetallicGloss", resolution, resolution, alpha=True))

    def set_bake_target(target_image):
        for obj in valid_mesh_objs:
            for mat in obj.data.materials:
                if mat and mat.use_nodes:
                    node = mat.node_tree.nodes.get("BakeTarget") or mat.node_tree.nodes.new("ShaderNodeTexImage")
                    node.name = "BakeTarget"
                    node.image = target_image
                    mat.node_tree.nodes.active = node

    bpy.ops.object.select_all(action="DESELECT")
    for obj in valid_mesh_objs:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = valid_mesh_objs[0]

    # 1. Bake Albedo. The DIFFUSE/COLOR pass of a Principled BSDF is Base Color * (1 - Metallic),
    # which would turn every metal plate near-black in Unity's metallic workflow. Route the
    # Base Color input through a temporary Emission shader so the full base color is baked.
    def bake_base_color_via_emission():
        rerouted = []
        for obj in valid_mesh_objs:
            for mat in obj.data.materials:
                if not (mat and mat.use_nodes):
                    continue
                out_node = next((n for n in mat.node_tree.nodes if n.type == "OUTPUT_MATERIAL"), None)
                bsdf_node = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
                if not (out_node and bsdf_node and "Base Color" in bsdf_node.inputs):
                    continue
                saved_from = None
                for link in mat.node_tree.links:
                    if link.to_node == out_node and link.to_socket.name == "Surface":
                        saved_from = link.from_socket
                        break
                emit_node = mat.node_tree.nodes.new("ShaderNodeEmission")
                base_input = bsdf_node.inputs["Base Color"]
                if base_input.is_linked:
                    mat.node_tree.links.new(base_input.links[0].from_socket, emit_node.inputs["Color"])
                else:
                    emit_node.inputs["Color"].default_value = tuple(base_input.default_value)
                mat.node_tree.links.new(emit_node.outputs["Emission"], out_node.inputs["Surface"])
                rerouted.append((mat, out_node, emit_node, saved_from))
        try:
            bpy.ops.object.bake(type="EMIT")
        finally:
            for mat, out_node, emit_node, saved_from in rerouted:
                if saved_from is not None:
                    mat.node_tree.links.new(saved_from, out_node.inputs["Surface"])
                mat.node_tree.nodes.remove(emit_node)

    set_bake_target(img_albedo)
    try:
        bake_base_color_via_emission()
    except Exception as e:
        print(f"Warning: Base color bake failed ({e}); falling back to DIFFUSE color pass")
        try:
            bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"})
        except Exception as e2:
            print(f"Warning: Diffuse bake: {e2}")
    albedo_path = output_dir / f"{prefix}_Albedo.png"
    img_albedo.filepath_raw = str(albedo_path)
    img_albedo.file_format = "PNG"
    img_albedo.save()
    maps["albedo"] = albedo_path

    # 2. Bake Emission
    set_bake_target(img_emission)
    try:
        bpy.ops.object.bake(type="EMIT")
    except Exception as e:
        print(f"Warning: Emission bake: {e}")
    emission_path = output_dir / f"{prefix}_Emission.png"
    img_emission.filepath_raw = str(emission_path)
    img_emission.file_format = "PNG"
    img_emission.save()
    maps["emission"] = emission_path

    # 3. Bake Normal Map
    set_bake_target(img_normal)
    try:
        bpy.ops.object.bake(type="NORMAL")
    except Exception as e:
        print(f"Warning: Normal bake: {e}")
    normal_path = output_dir / f"{prefix}_Normal.png"
    img_normal.filepath_raw = str(normal_path)
    img_normal.file_format = "PNG"
    img_normal.save()
    maps["normal"] = normal_path

    # 4. Bake Roughness (Grayscale)
    img_roughness = mark_data_image(bpy.data.images.new(f"{prefix}_RoughnessTemp", resolution, resolution, alpha=False))
    set_bake_target(img_roughness)
    try:
        bpy.ops.object.bake(type="ROUGHNESS")
    except Exception as e:
        print(f"Warning: Roughness bake: {e}")

    # 5. Bake Metallic (Grayscale via temporary Emission shader reroute)
    img_metallic_temp = mark_data_image(bpy.data.images.new(f"{prefix}_MetallicTemp", resolution, resolution, alpha=False))
    set_bake_target(img_metallic_temp)
    saved_surface_links = {}
    temp_emit_nodes = []

    for obj in valid_mesh_objs:
        for mat in obj.data.materials:
            if mat and mat.use_nodes:
                out_node = next((n for n in mat.node_tree.nodes if n.type == "OUTPUT_MATERIAL"), None)
                bsdf_node = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
                if out_node and bsdf_node and "Metallic" in bsdf_node.inputs:
                    # Save existing surface link
                    for link in mat.node_tree.links:
                        if link.to_node == out_node and link.to_socket.name == "Surface":
                            saved_surface_links[mat] = link.from_socket
                            break
                    # Reroute metallic through emission
                    emit_node = mat.node_tree.nodes.new("ShaderNodeEmission")
                    temp_emit_nodes.append((mat, emit_node))
                    if bsdf_node.inputs["Metallic"].is_linked:
                        mat.node_tree.links.new(bsdf_node.inputs["Metallic"].links[0].from_socket, emit_node.inputs["Color"])
                    else:
                        m_val = float(bsdf_node.inputs["Metallic"].default_value)
                        emit_node.inputs["Color"].default_value = (m_val, m_val, m_val, 1.0)
                    mat.node_tree.links.new(emit_node.outputs["Emission"], out_node.inputs["Surface"])

    try:
        bpy.ops.object.bake(type="EMIT")
    except Exception as e:
        print(f"Warning: Metallic emit bake: {e}")

    # Restore original surface links and remove temporary emission nodes
    for mat, emit_node in temp_emit_nodes:
        out_node = next((n for n in mat.node_tree.nodes if n.type == "OUTPUT_MATERIAL"), None)
        if mat in saved_surface_links and out_node:
            mat.node_tree.links.new(saved_surface_links[mat], out_node.inputs["Surface"])
        mat.node_tree.nodes.remove(emit_node)

    # 6. Pack metallic and smoothness into the atlas consumed by the Rogue Command URP shader:
    # Red = Metallic, Green = Metallic, Blue = Metallic, Alpha = Smoothness (1.0 - Roughness)
    try:
        import numpy as np
        num_pixels = resolution * resolution
        rough_arr = np.empty(num_pixels * 4, dtype=np.float32)
        metal_arr = np.empty(num_pixels * 4, dtype=np.float32)
        img_roughness.pixels.foreach_get(rough_arr)
        img_metallic_temp.pixels.foreach_get(metal_arr)

        metallic_vals = metal_arr[0::4]
        roughness_vals = rough_arr[0::4]
        smoothness_vals = np.clip(1.0 - roughness_vals, 0.0, 0.96)  # roughness floor 0.04, same as pack_material_maker_pbr.py

        packed_arr = np.empty(num_pixels * 4, dtype=np.float32)
        packed_arr[0::4] = metallic_vals
        packed_arr[1::4] = metallic_vals
        packed_arr[2::4] = metallic_vals
        packed_arr[3::4] = smoothness_vals

        img_metallic.pixels.foreach_set(packed_arr)
    except Exception as e:
        print(f"Fallback to pure python pixel pack: {e}")
        r_pix = list(img_roughness.pixels)
        m_pix = list(img_metallic_temp.pixels)
        packed = []
        for i in range(0, len(r_pix), 4):
            m = m_pix[i]
            s = max(0.0, min(0.96, 1.0 - r_pix[i]))
            packed.extend([m, m, m, s])
        img_metallic.pixels = packed

    metallic_path = output_dir / f"{prefix}_MetallicGloss.png"
    img_metallic.filepath_raw = str(metallic_path)
    img_metallic.file_format = "PNG"
    img_metallic.save()
    maps["metallic_gloss"] = metallic_path

    # Clean up temporary images
    bpy.data.images.remove(img_roughness)
    bpy.data.images.remove(img_metallic_temp)

    for obj in valid_mesh_objs:
        for mat in obj.data.materials:
            if mat and mat.use_nodes:
                b_node = mat.node_tree.nodes.get("BakeTarget")
                if b_node:
                    mat.node_tree.nodes.remove(b_node)

    scene.render.engine = prev_engine
    return maps


def create_single_material_atlas_model(
    source_objects: Sequence[bpy.types.Object],
    prefix: str,
    texture_maps: dict[str, Path],
    output_fbx_path: Path,
) -> bpy.types.Object:
    """Combines source meshes into one object with one PBR atlas material slot."""
    valid_mesh_objs = [o for o in source_objects if o.type == "MESH" and not o.name.endswith(("_COLLIDER", "_ShieldBubble"))]
    if not valid_mesh_objs:
        raise ValueError("No mesh objects available for single-material atlas creation")

    dup_objs = []
    for o in valid_mesh_objs:
        d = o.copy()
        d.data = o.data.copy()
        bpy.context.collection.objects.link(d)
        dup_objs.append(d)

    bpy.ops.object.select_all(action="DESELECT")
    for d in dup_objs:
        d.select_set(True)
    bpy.context.view_layer.objects.active = dup_objs[0]
    bpy.ops.object.join()

    unified_obj = dup_objs[0]
    unified_obj.name = f"{prefix}_UnifiedMesh"
    unified_obj.data.name = f"{prefix}_UnifiedMesh_Data"

    # A joined atlas mesh inherits the active batch object's nonzero origin.
    # Bake its world transform into the vertices before FBX export so the
    # single-mesh export matches the source batches in Unity world space.
    unified_obj.data.transform(unified_obj.matrix_world.copy())
    unified_obj.data.update()
    unified_obj.matrix_world = Matrix.Identity(4)

    atlas_mat = bpy.data.materials.new(f"{prefix}_Material_Atlas")
    atlas_mat.use_nodes = True
    nodes = atlas_mat.node_tree.nodes
    bsdf = nodes.get("Principled BSDF")

    if "albedo" in texture_maps and texture_maps["albedo"].exists():
        tex_img = bpy.data.images.load(str(texture_maps["albedo"]))
        node_albedo = nodes.new("ShaderNodeTexImage")
        node_albedo.image = tex_img
        atlas_mat.node_tree.links.new(node_albedo.outputs["Color"], bsdf.inputs["Base Color"])

    if "emission" in texture_maps and texture_maps["emission"].exists():
        tex_emit = bpy.data.images.load(str(texture_maps["emission"]))
        node_emit = nodes.new("ShaderNodeTexImage")
        node_emit.image = tex_emit
        atlas_mat.node_tree.links.new(node_emit.outputs["Color"], bsdf.inputs["Emission Color"])
        if "Emission Strength" in bsdf.inputs:
            bsdf.inputs["Emission Strength"].default_value = 2.0

    if "normal" in texture_maps and texture_maps["normal"].exists():
        tex_norm = bpy.data.images.load(str(texture_maps["normal"]))
        tex_norm.colorspace_settings.name = "Non-Color"
        node_norm = nodes.new("ShaderNodeTexImage")
        node_norm.image = tex_norm
        node_bump = nodes.new("ShaderNodeNormalMap")
        atlas_mat.node_tree.links.new(node_norm.outputs["Color"], node_bump.inputs["Color"])
        atlas_mat.node_tree.links.new(node_bump.outputs["Normal"], bsdf.inputs["Normal"])

    if "metallic_gloss" in texture_maps and texture_maps["metallic_gloss"].exists():
        tex_mg = bpy.data.images.load(str(texture_maps["metallic_gloss"]))
        tex_mg.colorspace_settings.name = "Non-Color"
        node_mg = nodes.new("ShaderNodeTexImage")
        node_mg.image = tex_mg
        # Red channel provides Metallic
        node_sep = nodes.new("ShaderNodeSeparateColor")
        atlas_mat.node_tree.links.new(node_mg.outputs["Color"], node_sep.inputs["Color"])
        atlas_mat.node_tree.links.new(node_sep.outputs["Red"], bsdf.inputs["Metallic"])
        # Alpha channel provides Smoothness -> Invert to Roughness for Blender's BSDF
        node_inv = nodes.new("ShaderNodeInvert")
        atlas_mat.node_tree.links.new(node_mg.outputs["Alpha"], node_inv.inputs["Color"])
        atlas_mat.node_tree.links.new(node_inv.outputs["Color"], bsdf.inputs["Roughness"])

    unified_obj.data.materials.clear()
    unified_obj.data.materials.append(atlas_mat)
    for poly in unified_obj.data.polygons:
        poly.material_index = 0

    # The atlas is a static single-mesh export. A joined batch can inherit an
    # Armature modifier from its active source mesh; evaluating it would deform
    # the consolidated geometry even though this export contains no armature.
    for modifier in list(unified_obj.modifiers):
        if modifier.type == "ARMATURE":
            unified_obj.modifiers.remove(modifier)

    output_fbx_path.parent.mkdir(parents=True, exist_ok=True)
    export_unity_fbx(output_fbx_path, [unified_obj], include_empties=False)
    return unified_obj


def make_damaged_state(
    source_objects: Sequence[bpy.types.Object],
    prefix: str,
    output_fbx_path: Path,
    output_blend_path: Path | None = None,
    smooth_by_angle: bool = True,
    smooth_angle_deg: float = 35.0,
) -> list[bpy.types.Object]:
    """Generates an active combat-damaged variant (<Name>_Damaged.fbx) with distorted armor plates and scorch marks."""
    mesh_objs = [o for o in source_objects if o.type == "MESH" and not o.name.endswith(("_COLLIDER", "_ShieldBubble"))]
    if not mesh_objs:
        return []

    # Materials for damaged state
    mat_scorched = make_material(
        f"{prefix}_Combat_Scorched",
        (0.14, 0.12, 0.11),
        metallic=0.45,
        roughness=0.82,
        emission_color=(0.95, 0.28, 0.04),
        emission_strength=0.6,
    )
    mat_damaged_metal = make_material(
        f"{prefix}_Damaged_Metal",
        (0.20, 0.22, 0.24),
        metallic=0.65,
        roughness=0.55,
    )

    damaged_objects = []
    turret_skewed = False

    for obj in mesh_objs:
        d_obj = obj.copy()
        d_obj.data = obj.data.copy()
        bpy.context.collection.objects.link(d_obj)
        damaged_objects.append(d_obj)

        name_lower = d_obj.name.lower()

        # 1. Jammed/skewed turret traverse ring (4-6 degrees tilt)
        if ("turret" in name_lower or "cabin" in name_lower) and not turret_skewed:
            d_obj.rotation_euler.x += math.radians(3.5)
            d_obj.rotation_euler.z += math.radians(6.0)
            d_obj.location.x += 0.04
            turret_skewed = True

        # 2. Distort/dent armor skirts and mantlet
        if any(k in name_lower for k in ("skirt", "fender", "glacis", "armor", "mantlet")):
            for v in d_obj.data.vertices:
                if (v.index % 3) == 0:
                    v.co.x += (0.02 if (v.index % 2 == 0) else -0.02)
                    v.co.z += -0.025
            d_obj.data.update()
            if len(d_obj.data.materials) > 0 and (hash(d_obj.name) % 2 == 0):
                d_obj.data.materials[0] = mat_scorched

        # 3. Displaced / skewed road wheel or tread section
        elif any(k in name_lower for k in ("wheel", "tread", "sprocket", "idler")):
            if hash(d_obj.name) % 3 == 0:
                d_obj.rotation_euler.y += math.radians(7.5)
                d_obj.location.z -= 0.02
                if len(d_obj.data.materials) > 0:
                    d_obj.data.materials[0] = mat_damaged_metal

    # Batch and export
    batched = batch_and_join_by_material(
        damaged_objects,
        prefix=f"{prefix}_Damaged",
        smooth_by_angle=smooth_by_angle,
        smooth_angle_deg=smooth_angle_deg,
    )

    output_fbx_path.parent.mkdir(parents=True, exist_ok=True)
    export_unity_fbx(output_fbx_path, batched, include_empties=False)

    if output_blend_path is not None:
        output_blend_path.parent.mkdir(parents=True, exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=str(output_blend_path))

    return batched


def render_tactical_icons(
    model_objects: Sequence[bpy.types.Object],
    output_dir: Path,
    unit_name: str,
    resolutions: Sequence[int] = (64, 128),
    elevation_angle_deg: float = 45.0,
) -> dict[str, Path]:
    """Renders dual orthographic icons (Top-Down zenith and Isometric 45°) with transparent alpha background and calibrated 3-point RTS lighting."""
    output_dir.mkdir(parents=True, exist_ok=True)
    mesh_objects = [o for o in model_objects if o.type == "MESH" and not o.name.endswith(("_COLLIDER", "_ShieldBubble"))]
    if not mesh_objects:
        return {}

    scene = bpy.context.scene
    saved_res_x = scene.render.resolution_x
    saved_res_y = scene.render.resolution_y
    saved_film = scene.render.film_transparent
    saved_format = scene.render.image_settings.file_format
    saved_color = scene.render.image_settings.color_mode

    # Configure transparent alpha render
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"

    # Compute bounding center and extents
    corners = [obj.matrix_world @ Vector(c) for obj in mesh_objects for c in obj.bound_box]
    minimum = Vector(tuple(min(p[axis] for p in corners) for axis in range(3)))
    maximum = Vector(tuple(max(p[axis] for p in corners) for axis in range(3)))
    center = (minimum + maximum) * 0.5
    size = maximum - minimum
    max_dim = max(size.x, size.y, size.z)

    # Temporary camera for icon rendering
    cam_data = bpy.data.cameras.new("IconCameraData")
    cam_data.type = "ORTHO"
    cam_obj = bpy.data.objects.new("IconCamera", cam_data)
    bpy.context.collection.objects.link(cam_obj)
    prev_cam = scene.camera
    scene.camera = cam_obj

    # Calibrated 3-point RTS studio lighting for crisp icon rendering
    lights_created = []
    try:
        # 1. Key Light: Sun from top-left/front (warm sunlight for chamfer specular glints)
        key_data = bpy.data.lights.new("IconKeyLightData", "SUN")
        key_data.energy = 4.2
        key_data.color = (1.0, 0.98, 0.95)
        key_obj = bpy.data.objects.new("IconKeyLight", key_data)
        key_obj.location = center + Vector((-max_dim * 2, -max_dim * 2, max_dim * 3))
        key_obj.rotation_euler = (center - key_obj.location).normalized().to_track_quat("-Z", "Y").to_euler()
        bpy.context.collection.objects.link(key_obj)
        lights_created.append(key_obj)

        # 2. Fill Light: Sun from right/rear (soft cool sky ambient for tread/underside fill)
        fill_data = bpy.data.lights.new("IconFillLightData", "SUN")
        fill_data.energy = 1.8
        fill_data.color = (0.85, 0.92, 1.0)
        fill_obj = bpy.data.objects.new("IconFillLight", fill_data)
        fill_obj.location = center + Vector((max_dim * 2, max_dim * 2, max_dim * 2))
        fill_obj.rotation_euler = (center - fill_obj.location).normalized().to_track_quat("-Z", "Y").to_euler()
        bpy.context.collection.objects.link(fill_obj)
        lights_created.append(fill_obj)

        # 3. Rim Light: Sun from behind/above (sharp silhouette outline)
        rim_data = bpy.data.lights.new("IconRimLightData", "SUN")
        rim_data.energy = 2.4
        rim_data.color = (1.0, 1.0, 1.0)
        rim_obj = bpy.data.objects.new("IconRimLight", rim_data)
        rim_obj.location = center + Vector((0, max_dim * 3, max_dim * 2))
        rim_obj.rotation_euler = (center - rim_obj.location).normalized().to_track_quat("-Z", "Y").to_euler()
        bpy.context.collection.objects.link(rim_obj)
        lights_created.append(rim_obj)
    except Exception as e:
        print(f"Warning: Icon lighting setup: {e}")

    generated_icons = {}

    try:
        # 1. Top-Down (Zenith 90°) View for Minimap & Radar
        cam_obj.location = Vector((center.x, center.y, center.z + 15.0))
        cam_obj.rotation_euler = Euler((0.0, 0.0, 0.0), "XYZ")
        cam_data.ortho_scale = max(size.x, size.y) * 1.15

        for res in resolutions:
            scene.render.resolution_x = res
            scene.render.resolution_y = res
            icon_path = output_dir / f"{unit_name}_Icon_TopDown_{res}.png"
            scene.render.filepath = str(icon_path)
            bpy.ops.render.render(write_still=True)
            generated_icons[f"topdown_{res}"] = icon_path

        # 2. Tactical Isometric (Mathematically exact RTS elevation angle) View for Cards & Selection HUD
        d_horizontal = max_dim * 2.2
        x_off = d_horizontal * math.cos(math.radians(45.0))
        y_off = -d_horizontal * math.sin(math.radians(45.0))
        z_off = d_horizontal * math.tan(math.radians(elevation_angle_deg))
        cam_offset = Vector((x_off, y_off, z_off))
        cam_obj.location = center + cam_offset
        direction = (center - cam_obj.location).normalized()
        cam_obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        cam_data.ortho_scale = max_dim * 1.45

        for res in resolutions:
            scene.render.resolution_x = res
            scene.render.resolution_y = res
            icon_path = output_dir / f"{unit_name}_Icon_Isometric_{res}.png"
            scene.render.filepath = str(icon_path)
            bpy.ops.render.render(write_still=True)
            generated_icons[f"isometric_{res}"] = icon_path

    finally:
        scene.render.resolution_x = saved_res_x
        scene.render.resolution_y = saved_res_y
        scene.render.film_transparent = saved_film
        scene.render.image_settings.file_format = saved_format
        scene.render.image_settings.color_mode = saved_color

        if prev_cam:
            scene.camera = prev_cam
        bpy.data.objects.remove(cam_obj, do_unlink=True)
        bpy.data.cameras.remove(cam_data, do_unlink=True)

        for l_obj in lights_created:
            try:
                l_data = l_obj.data
                bpy.data.objects.remove(l_obj, do_unlink=True)
                bpy.data.lights.remove(l_data, do_unlink=True)
            except Exception:
                pass

    return generated_icons


def hex_to_rgb(hex_code: str) -> Tuple[float, float, float]:
    """Converts a hex string ('#RRGGBB' or 'RRGGBB') to a float RGB tuple (0.0 - 1.0)."""
    h = hex_code.strip().lstrip("#")
    if len(h) == 6:
        r = int(h[0:2], 16) / 255.0
        g = int(h[2:4], 16) / 255.0
        b = int(h[4:6], 16) / 255.0
        return (r, g, b)
    elif len(h) == 3:
        r = int(h[0] * 2, 16) / 255.0
        g = int(h[1] * 2, 16) / 255.0
        b = int(h[2] * 2, 16) / 255.0
        return (r, g, b)
    return (0.5, 0.5, 0.5)


def apply_procedural_camouflage(
    material: bpy.types.Material,
    base_color: Tuple[float, float, float],
    secondary_color: Tuple[float, float, float],
    accent_color: Tuple[float, float, float] | None = None,
    camo_type: str = "splinter",
    scale: float = 1.0,
    contrast: float = 1.0,
) -> None:
    """Attaches a procedural mathematical camouflage generator node tree to the material's Base Color."""
    if not math.isfinite(scale) or scale < 0.1 or scale > 10.0:
        raise ValueError("Camouflage scale must be finite and between 0.1 and 10.0.")
    if not math.isfinite(contrast) or contrast < 0.0 or contrast > 4.0:
        raise ValueError("Camouflage contrast must be finite and between 0.0 and 4.0.")

    if not isinstance(camo_type, str):
        raise ValueError("Camouflage type must be a supported string style.")
    c = camo_type.strip().lower()
    supported_camo_types = {
        "arctic", "desert", "digital", "jungle", "splinter",
        "tigerstripe", "urban", "woodland",
    }
    if c not in supported_camo_types:
        supported = ", ".join(sorted(supported_camo_types))
        raise ValueError(f"Unsupported camouflage type '{camo_type}'. Choose one of: {supported}.")

    if not material or not material.use_nodes:
        return
    tree = material.node_tree
    bsdf = tree.nodes.get("Principled BSDF")
    if not bsdf or "Base Color" not in bsdf.inputs:
        return

    # Object coordinates avoid UV seams; alignment across separate meshes depends on compatible origins.
    tex_coord = tree.nodes.new("ShaderNodeTexCoord")
    midpoint = tuple((base_color[i] + secondary_color[i]) * 0.5 for i in range(3))

    def contrast_adjusted(color: Tuple[float, float, float]) -> Tuple[float, float, float]:
        return tuple(
            max(0.0, min(1.0, midpoint[i] + (color[i] - midpoint[i]) * contrast))
            for i in range(3)
        )

    base_color = contrast_adjusted(base_color)
    secondary_color = contrast_adjusted(secondary_color)
    if accent_color is not None:
        accent_color = contrast_adjusted(accent_color)

    if c == "splinter":
        # Sharp angular Voronoi shards
        vor = tree.nodes.new("ShaderNodeTexVoronoi")
        vor.feature = "F1"
        vor.distance = "MANHATTAN"
        vor.inputs["Scale"].default_value = 3.6 * max(0.1, scale)
        ramp = tree.nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.interpolation = "CONSTANT"
        ramp.color_ramp.elements[0].position = 0.0
        ramp.color_ramp.elements[0].color = (*base_color, 1.0)
        e1 = ramp.color_ramp.elements.new(0.38)
        e1.color = (*secondary_color, 1.0)
        e2 = ramp.color_ramp.elements.new(0.72)
        tertiary = accent_color or (base_color[0] * 0.45, base_color[1] * 0.45, base_color[2] * 0.45)
        e2.color = (*tertiary, 1.0)
        tree.links.new(tex_coord.outputs["Object"], vor.inputs["Vector"])
        tree.links.new(vor.outputs["Distance"], ramp.inputs["Fac"])
        tree.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])

    elif c == "digital":
        # Stepped orthogonal cellular grid (pixelated/voxel camo)
        vor = tree.nodes.new("ShaderNodeTexVoronoi")
        vor.distance = "CHEBYCHEV"
        vor.inputs["Scale"].default_value = 5.8 * max(0.1, scale)
        vor.inputs["Randomness"].default_value = 0.0
        ramp = tree.nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.interpolation = "CONSTANT"
        ramp.color_ramp.elements[0].position = 0.0
        ramp.color_ramp.elements[0].color = (*base_color, 1.0)
        e1 = ramp.color_ramp.elements.new(0.46)
        e1.color = (*secondary_color, 1.0)
        tree.links.new(tex_coord.outputs["Object"], vor.inputs["Vector"])
        tree.links.new(vor.outputs["Distance"], ramp.inputs["Fac"])
        tree.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])

    elif c == "tigerstripe":
        # Directional wavy brushstroke bands
        wave = tree.nodes.new("ShaderNodeTexWave")
        wave.wave_type = "BANDS"
        wave.bands_direction = "X"
        wave.inputs["Scale"].default_value = 2.8 * max(0.1, scale)
        wave.inputs["Distortion"].default_value = 7.5
        wave.inputs["Detail"].default_value = 2.0
        ramp = tree.nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.interpolation = "LINEAR"
        ramp.color_ramp.elements[0].position = 0.35
        ramp.color_ramp.elements[0].color = (*base_color, 1.0)
        ramp.color_ramp.elements[1].position = 0.65
        ramp.color_ramp.elements[1].color = (*secondary_color, 1.0)
        tree.links.new(tex_coord.outputs["Object"], wave.inputs["Vector"])
        tree.links.new(wave.outputs["Color"], ramp.inputs["Fac"])
        tree.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])

    else:
        # Desert, urban, arctic, woodland, jungle: multi-frequency fractal noise blotches
        noise = tree.nodes.new("ShaderNodeTexNoise")
        noise.inputs["Scale"].default_value = 2.4 * max(0.1, scale)
        noise.inputs["Detail"].default_value = 3.0
        noise.inputs["Roughness"].default_value = 0.52
        ramp = tree.nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.interpolation = "LINEAR"
        ramp.color_ramp.elements[0].position = 0.38
        ramp.color_ramp.elements[0].color = (*base_color, 1.0)
        ramp.color_ramp.elements[1].position = 0.62
        ramp.color_ramp.elements[1].color = (*secondary_color, 1.0)
        tree.links.new(tex_coord.outputs["Object"], noise.inputs["Vector"])
        tree.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
        tree.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])


def apply_procedural_normal_relief(
    material: bpy.types.Material,
    scale: float = 1.0,
    strength: float = 0.75,
) -> None:
    """Adds restrained, fine-grained surface relief to the material's Normal socket."""
    if not material or not material.use_nodes:
        return
    tree = material.node_tree
    bsdf = tree.nodes.get("Principled BSDF")
    if not bsdf or "Normal" not in bsdf.inputs:
        return

    tex_coord = tree.nodes.new("ShaderNodeTexCoord")
    tex_coord.location = (-700, -420)
    noise = tree.nodes.new("ShaderNodeTexNoise")
    noise.location = (-480, -420)
    noise.inputs["Scale"].default_value = 20.0 * max(0.1, scale)
    noise.inputs["Detail"].default_value = 2.0
    noise.inputs["Roughness"].default_value = 0.62

    bump = tree.nodes.new("ShaderNodeBump")
    bump.location = (-220, -360)
    bump.inputs["Strength"].default_value = strength
    bump.inputs["Distance"].default_value = 0.010

    tree.links.new(tex_coord.outputs["Object"], noise.inputs["Vector"])
    tree.links.new(noise.outputs["Fac"], bump.inputs["Height"])
    tree.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])

    # Add low-contrast paint and roughness variation so large armor plates do
    # not read as uniformly flat color in previews or baked PBR atlases.
    finish_noise = tree.nodes.new("ShaderNodeTexNoise")
    finish_noise.location = (-480, -80)
    finish_noise.inputs["Scale"].default_value = 4.2 * max(0.1, scale)
    finish_noise.inputs["Detail"].default_value = 2.0
    finish_noise.inputs["Roughness"].default_value = 0.58
    tree.links.new(tex_coord.outputs["Object"], finish_noise.inputs["Vector"])

    color_scale = tree.nodes.new("ShaderNodeMapRange")
    color_scale.location = (-220, -80)
    color_scale.clamp = True
    color_scale.inputs["From Min"].default_value = 0.0
    color_scale.inputs["From Max"].default_value = 1.0
    color_scale.inputs["To Min"].default_value = 0.94
    color_scale.inputs["To Max"].default_value = 1.06
    tree.links.new(finish_noise.outputs["Fac"], color_scale.inputs["Value"])

    base_color_input = bsdf.inputs["Base Color"]
    base_color_link = next((link for link in tree.links if link.to_socket == base_color_input), None)
    color_mix = tree.nodes.new("ShaderNodeMixRGB")
    color_mix.blend_type = "MULTIPLY"
    color_mix.location = (20, -120)
    color_mix.inputs["Fac"].default_value = 1.0
    if base_color_link:
        tree.links.new(base_color_link.from_socket, color_mix.inputs["Color1"])
        tree.links.remove(base_color_link)
    else:
        color_mix.inputs["Color1"].default_value = base_color_input.default_value
    tree.links.new(color_scale.outputs["Result"], color_mix.inputs["Color2"])
    tree.links.new(color_mix.outputs["Color"], base_color_input)

    roughness_input = bsdf.inputs["Roughness"]
    if not roughness_input.is_linked:
        base_roughness = float(roughness_input.default_value)
        roughness_scale = tree.nodes.new("ShaderNodeMapRange")
        roughness_scale.location = (-220, 180)
        roughness_scale.clamp = True
        roughness_scale.inputs["From Min"].default_value = 0.0
        roughness_scale.inputs["From Max"].default_value = 1.0
        roughness_scale.inputs["To Min"].default_value = max(0.04, base_roughness - 0.08)
        roughness_scale.inputs["To Max"].default_value = min(0.98, base_roughness + 0.08)
        tree.links.new(finish_noise.outputs["Fac"], roughness_scale.inputs["Value"])
        tree.links.new(roughness_scale.outputs["Result"], roughness_input)


class FactionPalette:
    """Pre-calibrated 5-tone PBR material set for a Rogue Command faction."""

    def __init__(
        self,
        name: str,
        primary_color: Tuple[float, float, float],
        light_color: Tuple[float, float, float],
        gunmetal_color: Tuple[float, float, float],
        tread_color: Tuple[float, float, float],
        warning_color: Tuple[float, float, float],
        emissive_color: Tuple[float, float, float],
        emissive_strength: float = 2.0,
        camo: str = "none",
        camo_scale: float = 1.0,
        camo_contrast: float = 1.0,
        enable_normal_relief: bool = True,
        normal_intensity: float = 1.0,
    ):
        self.camo = camo
        self.camo_scale = camo_scale
        self.camo_contrast = camo_contrast
        self.enable_normal_relief = enable_normal_relief
        self.normal_intensity = normal_intensity

        self.primary_armor = make_material(f"{name}_PrimaryArmor", primary_color, metallic=0.42, roughness=0.58)
        self.light_armor = make_material(f"{name}_LightArmor", light_color, metallic=0.34, roughness=0.60)
        self.gunmetal = make_material(f"{name}_Gunmetal", gunmetal_color, metallic=0.78, roughness=0.30)
        self.tread = make_material(f"{name}_Tread", tread_color, metallic=0.18, roughness=0.74)
        self.wheel_rim = make_material(f"{name}_WheelRim", light_color, metallic=0.62, roughness=0.40)
        self.wheel_hub = make_material(f"{name}_WheelHub", primary_color, metallic=0.55, roughness=0.50)
        self.warning = make_material(f"{name}_Warning", warning_color, metallic=0.18, roughness=0.36)
        self.emissive = make_material(
            f"{name}_Emissive", emissive_color, metallic=0.15, roughness=0.22,
            emission_color=emissive_color, emission_strength=emissive_strength
        )

        c = camo.lower()
        if c != "none":
            apply_procedural_camouflage(
                self.primary_armor,
                base_color=primary_color,
                secondary_color=light_color,
                accent_color=warning_color,
                camo_type=c,
                scale=camo_scale,
                contrast=camo_contrast,
            )
            apply_procedural_camouflage(
                self.light_armor,
                base_color=light_color,
                secondary_color=primary_color,
                accent_color=warning_color,
                camo_type=c,
                # Keep the light-armor variation inside the helper's public range
                # when the caller selects the maximum supported camo scale.
                scale=min(10.0, camo_scale * 1.25),
                contrast=camo_contrast,
            )

        if enable_normal_relief and normal_intensity > 0.0:
            apply_procedural_normal_relief(self.primary_armor, scale=1.0, strength=0.20 * normal_intensity)
            apply_procedural_normal_relief(self.light_armor, scale=1.2, strength=0.16 * normal_intensity)
            apply_procedural_normal_relief(self.gunmetal, scale=1.5, strength=0.12 * normal_intensity)


CAMO_COLOR_PALETTES: dict[str, tuple[Tuple[float, float, float], Tuple[float, float, float], Tuple[float, float, float], Tuple[float, float, float], Tuple[float, float, float], Tuple[float, float, float], float]] = {
    "desert": (
        (0.42, 0.32, 0.18), (0.55, 0.44, 0.26), (0.07, 0.08, 0.09),
        (0.04, 0.045, 0.05), (0.92, 0.52, 0.10), (0.98, 0.42, 0.08), 2.0,
    ),
    "urban": (
        (0.22, 0.24, 0.26), (0.36, 0.39, 0.42), (0.05, 0.06, 0.07),
        (0.03, 0.035, 0.04), (0.85, 0.85, 0.20), (0.0, 0.85, 1.0), 2.2,
    ),
    "arctic": (
        (0.76, 0.82, 0.88), (0.52, 0.58, 0.66), (0.08, 0.10, 0.14),
        (0.05, 0.06, 0.08), (0.92, 0.15, 0.18), (0.15, 0.85, 1.0), 2.2,
    ),
    "woodland": (
        (0.18, 0.25, 0.14), (0.28, 0.36, 0.22), (0.06, 0.07, 0.08),
        (0.04, 0.045, 0.05), (0.90, 0.65, 0.12), (0.20, 0.95, 0.45), 2.0,
    ),
    "jungle": (
        (0.18, 0.25, 0.14), (0.28, 0.36, 0.22), (0.06, 0.07, 0.08),
        (0.04, 0.045, 0.05), (0.90, 0.65, 0.12), (0.20, 0.95, 0.45), 2.0,
    ),
    "digital": (
        (0.22, 0.26, 0.28), (0.48, 0.45, 0.38), (0.07, 0.08, 0.09),
        (0.04, 0.045, 0.05), (0.88, 0.72, 0.15), (0.12, 0.95, 0.85), 2.2,
    ),
    "splinter": (
        (0.14, 0.16, 0.18), (0.68, 0.72, 0.74), (0.05, 0.06, 0.07),
        (0.03, 0.035, 0.04), (0.95, 0.22, 0.15), (0.98, 0.45, 0.10), 2.3,
    ),
    "tigerstripe": (
        (0.16, 0.24, 0.14), (0.34, 0.26, 0.18), (0.06, 0.07, 0.08),
        (0.04, 0.045, 0.05), (0.85, 0.60, 0.12), (0.10, 0.88, 0.42), 2.0,
    ),
}

FACTION_COLOR_PALETTES: dict[str, tuple[Tuple[float, float, float], Tuple[float, float, float], Tuple[float, float, float], Tuple[float, float, float], Tuple[float, float, float], Tuple[float, float, float], float]] = {
    "siege": (
        (0.26, 0.14, 0.075), (0.43, 0.30, 0.16), (0.065, 0.072, 0.078),
        (0.035, 0.040, 0.043), (0.92, 0.48, 0.08), (0.98, 0.35, 0.05), 2.2,
    ),
    "syndicate": (
        (0.055, 0.11, 0.20), (0.28, 0.34, 0.42), (0.035, 0.042, 0.055),
        (0.025, 0.031, 0.038), (0.77, 0.045, 0.31), (0.0, 0.74, 1.0), 2.4,
    ),
    "arctic": (
        (0.72, 0.78, 0.85), (0.45, 0.52, 0.62), (0.08, 0.10, 0.14),
        (0.05, 0.06, 0.08), (0.90, 0.12, 0.15), (0.15, 0.85, 1.0), 2.0,
    ),
    "vanguard": (
        (0.12, 0.19, 0.24), (0.22, 0.32, 0.38), (0.07, 0.10, 0.12),
        (0.045, 0.055, 0.06), (0.95, 0.32, 0.05), (0.04, 0.72, 0.88), 1.9,
    ),
}


def get_faction_palette(
    faction: str = "vanguard",
    prefix: str = "Unit",
    camo: str = "none",
    primary_override: Tuple[float, float, float] | None = None,
    light_override: Tuple[float, float, float] | None = None,
    emissive_override: Tuple[float, float, float] | None = None,
    camo_scale: float = 1.0,
    camo_contrast: float = 1.0,
    enable_normal_relief: bool = True,
    normal_intensity: float = 1.0,
) -> FactionPalette:
    """Returns a pre-calibrated 5-tone PBR material set with optional custom color overrides."""
    c = camo.lower()
    if c in CAMO_COLOR_PALETTES:
        p_col, l_col, g_col, t_col, w_col, e_col, e_str = CAMO_COLOR_PALETTES[c]
    else:
        f = faction.lower()
        p_col, l_col, g_col, t_col, w_col, e_col, e_str = FACTION_COLOR_PALETTES.get(
            f, FACTION_COLOR_PALETTES["vanguard"]
        )

    if primary_override is not None:
        p_col = primary_override
    if light_override is not None:
        l_col = light_override
    if emissive_override is not None:
        e_col = emissive_override

    return FactionPalette(
        prefix, p_col, l_col, g_col, t_col, w_col, e_col, e_str,
        camo=c, camo_scale=camo_scale, camo_contrast=camo_contrast,
        enable_normal_relief=enable_normal_relief, normal_intensity=normal_intensity,
    )


class RogueVehicleBuilder:
    """Fluent procedural builder for Rogue Command vehicles and units.

    Guarantees:
    - 5-tone faction PBR material standard
    - Scale-aware edge treatment on primary hull, turret, and cabin forms
    - Exact ground contact at Z >= 0.0m
    - Strict coaxial weapon alignment
    - Groups exportable meshes by material while preserving requested pivots; output group counts depend on the inputs
    - Automated quality diagnostics and multi-perspective studio previews
    """

    def __init__(
        self,
        name: str,
        faction: str = "vanguard",
        ground_clearance: float = 0.06,
        camo: str = "none",
        primary_override: Tuple[float, float, float] | None = None,
        light_override: Tuple[float, float, float] | None = None,
        emissive_override: Tuple[float, float, float] | None = None,
        camo_scale: float = 1.0,
        camo_contrast: float = 1.0,
        enable_normal_relief: bool = True,
        normal_intensity: float = 1.0,
    ):
        self.name = name
        self.faction = faction
        self.camo = camo
        self.primary_override = primary_override
        self.light_override = light_override
        self.emissive_override = emissive_override
        self.camo_scale = camo_scale
        self.camo_contrast = camo_contrast
        self.enable_normal_relief = enable_normal_relief
        self.normal_intensity = normal_intensity
        self.palette = get_faction_palette(
            faction,
            name,
            camo=camo,
            primary_override=primary_override,
            light_override=light_override,
            emissive_override=emissive_override,
            camo_scale=camo_scale,
            camo_contrast=camo_contrast,
            enable_normal_relief=enable_normal_relief,
            normal_intensity=normal_intensity,
        )
        self.ground_clearance = ground_clearance
        self.objects: list[bpy.types.Object] = []
        self.preserved_pivots: list[str] = []
        self.hull_deck_z: float = 0.88
        self.turret_center: Vector = Vector((0.0, 0.05, 1.15))
        self.hull_length: float = 3.0
        self.hull_width: float = 2.4
        self.gun_pivot: Vector | None = None
        self.gun_dir: Vector | None = None
        self.secondary_gun_pivot: Vector | None = None
        self.secondary_gun_dir: Vector | None = None
        self.primary_firepoint_locs: list[Vector] = []
        self.turret_pitch_loc: Vector | None = None
        self.recoil_loc: Vector | None = None
        self.recoil_dir: Vector | None = None
        self.sensor_loc: Vector | None = None
        self.exhaust_l: Vector | None = None
        self.exhaust_r: Vector | None = None
        self.damage_smoke_loc: Vector | None = None
        self.damage_fire_loc: Vector | None = None
        self.antenna_loc: Vector | None = None
        self.thruster_vector_l: Vector | None = None
        self.thruster_vector_r: Vector | None = None
        self.center_of_mass_loc: Vector | None = None
        self.locator_sockets: list[bpy.types.Object] = []
        self.collider_objects: list[bpy.types.Object] = []
        self.armature_object: bpy.types.Object | None = None
        self.action_clips: dict[str, bpy.types.Action] = {}
        self.vfx_objects: list[bpy.types.Object] = []
        self.shield_bubble_object: bpy.types.Object | None = None
        self.is_hover: bool = False
        self.archetype: str = "tank"
        self.wear_level: str = "field_tested"
        self.attachment_objects: dict[str, list[bpy.types.Object]] = {}
        self.is_walker: bool = False
        self.leg_type: str | None = None
        self.hip_height: float = 1.45
        self.foot_loc_l: Vector | None = None
        self.foot_loc_r: Vector | None = None
        self.hip_loc_l: Vector | None = None
        self.hip_loc_r: Vector | None = None
        self.knee_loc_l: Vector | None = None
        self.knee_loc_r: Vector | None = None
        self.quad_feet: dict[str, Vector] = {}
        self.quad_hips: dict[str, Vector] = {}
        self.quad_knees: dict[str, Vector] = {}
        self.engine_audio_loc: Vector | None = None
        self.weapon_audio_loc: Vector | None = None
        self.with_ik: bool = False
        self.track_sag: float = 0.0
        self.armor_plating: bool = False
        self.suspension_arms: bool = False
        self.mantlet_boot: bool = False
        self.spaced_skirts: bool = False
        self.wreckage_severity: str = "catastrophic"
        self._last_mantlet_boot_pivots: list[Vector] = []
        self._last_mantlet_boot_direction: Vector | None = None
        self._last_mantlet_boots_added: bool = False

    def set_archetype(self, archetype: str) -> "RogueVehicleBuilder":
        self.archetype = archetype
        return self

    def apply_uniform_scale(self, scale: float) -> "RogueVehicleBuilder":
        """Uniformly scale authored geometry and positional metadata around the ground origin."""
        if not math.isfinite(scale) or scale <= 0.0:
            raise ValueError("scale must be finite and greater than zero")
        if abs(scale - 1.0) <= 1e-8:
            return self

        for obj in self.objects:
            if obj.type == "MESH":
                for vertex in obj.data.vertices:
                    vertex.co *= scale
                obj.data.update()
            else:
                obj.scale *= scale
            obj.location *= scale

        for field_name in (
            "turret_center", "gun_pivot", "secondary_gun_pivot", "turret_pitch_loc",
            "recoil_loc", "sensor_loc", "exhaust_l", "exhaust_r", "damage_smoke_loc",
            "damage_fire_loc", "antenna_loc", "thruster_vector_l", "thruster_vector_r",
            "center_of_mass_loc", "foot_loc_l", "foot_loc_r", "hip_loc_l", "hip_loc_r",
            "knee_loc_l", "knee_loc_r", "engine_audio_loc", "weapon_audio_loc",
        ):
            value = getattr(self, field_name, None)
            if value is not None:
                setattr(self, field_name, Vector(value) * scale)

        self.primary_firepoint_locs = [Vector(value) * scale for value in self.primary_firepoint_locs]
        for field_name in ("quad_feet", "quad_hips", "quad_knees"):
            value_map = getattr(self, field_name, None)
            if value_map:
                setattr(self, field_name, {key: Vector(value) * scale for key, value in value_map.items()})

        for field_name in ("hull_deck_z", "hull_length", "hull_width", "hip_height", "ground_clearance"):
            setattr(self, field_name, getattr(self, field_name) * scale)

        if self._last_mantlet_boot_pivots:
            self._last_mantlet_boot_pivots = [point * scale for point in self._last_mantlet_boot_pivots]
        return self

    def set_wear_level(self, wear_level: str) -> "RogueVehicleBuilder":
        self.wear_level = wear_level
        return self

    def set_wreckage_severity(self, severity: str = "catastrophic") -> "RogueVehicleBuilder":
        self.wreckage_severity = severity
        return self

    def add_suspension_arms(self) -> "RogueVehicleBuilder":
        self.suspension_arms = True
        return self

    def add_spaced_armor_side_skirts(self) -> "RogueVehicleBuilder":
        self.spaced_skirts = True
        return self

    def add_mantlet_dust_boot(
        self,
        collar_radius: float = 0.18,
        taper_radius: float = 0.12,
        length: float = 0.22,
        stages: int = 3,
    ) -> "RogueVehicleBuilder":
        self.mantlet_boot = True
        if (
            self._last_mantlet_boots_added
            or not self._last_mantlet_boot_pivots
            or self._last_mantlet_boot_direction is None
        ):
            return self

        for index, pivot in enumerate(self._last_mantlet_boot_pivots, start=1):
            self.objects.extend(make_mantlet_dust_boot(
                f"{self.name}DustBoot_{index}",
                pivot=pivot - self._last_mantlet_boot_direction * 0.04,
                direction=self._last_mantlet_boot_direction,
                collar_radius=collar_radius,
                taper_radius=taper_radius,
                length=length,
                stages=stages,
                material=self.palette.tread,
                accent_mat=self.palette.gunmetal,
            ))
        self._last_mantlet_boots_added = True
        return self

    def set_camo(
        self,
        camo: str,
        scale: float = 1.0,
        contrast: float = 1.0,
    ) -> "RogueVehicleBuilder":
        self.camo = camo
        self.camo_scale = scale
        self.camo_contrast = contrast
        self.palette = get_faction_palette(
            self.faction,
            self.name,
            camo=camo,
            primary_override=self.primary_override,
            light_override=self.light_override,
            emissive_override=self.emissive_override,
            camo_scale=scale,
            camo_contrast=contrast,
            enable_normal_relief=self.enable_normal_relief,
            normal_intensity=self.normal_intensity,
        )
        return self

    def set_camo_params(self, scale: float = 1.0, contrast: float = 1.0) -> "RogueVehicleBuilder":
        return self.set_camo(self.camo, scale=scale, contrast=contrast)

    def set_normal_params(
        self,
        enabled: bool = True,
        intensity: float = 1.0,
    ) -> "RogueVehicleBuilder":
        self.enable_normal_relief = enabled
        self.normal_intensity = intensity
        self.palette = get_faction_palette(
            self.faction,
            self.name,
            camo=self.camo,
            primary_override=self.primary_override,
            light_override=self.light_override,
            emissive_override=self.emissive_override,
            camo_scale=self.camo_scale,
            camo_contrast=self.camo_contrast,
            enable_normal_relief=enabled,
            normal_intensity=intensity,
        )
        return self

    def add_colliders(
        self,
        include_turret: bool = True,
        model_objects: Sequence[bpy.types.Object] | None = None,
    ) -> "RogueVehicleBuilder":
        self.collider_objects = generate_collision_hulls(
            model_objects if model_objects is not None else self.objects,
            prefix=self.name,
            include_turret_collider=include_turret,
        )
        return self

    def add_muzzle_flash(self, size: float = 0.45) -> "RogueVehicleBuilder":
        """Adds procedural star-spike muzzle flash geometry to weapon muzzle."""
        if self.gun_pivot:
            flash_objs = make_muzzle_flash_mesh(
                f"{self.name}VFX",
                location=self.gun_pivot,
                direction=self.gun_dir if self.gun_dir else (0.0, 1.0, 0.0),
                size=size,
                flash_mat=self.palette.warning,
                core_mat=self.palette.emissive,
            )
            self.vfx_objects.extend(flash_objs)
            self.objects.extend(flash_objs)
        return self

    def add_thruster_plumes(self, length: float = 0.65, radius: float = 0.18) -> "RogueVehicleBuilder":
        """Adds conical glowing jet exhaust plumes to vehicle engine exhausts."""
        exhausts = [e for e in (self.exhaust_l, self.exhaust_r) if e is not None]
        for idx, ex_loc in enumerate(exhausts):
            plumes = make_thruster_plume_mesh(
                f"{self.name}VFX_Exhaust_{idx+1}",
                location=ex_loc,
                direction=(0.0, -1.0, 0.0),
                length=length,
                radius=radius,
                plume_mat=self.palette.warning,
                core_mat=self.palette.emissive,
            )
            self.vfx_objects.extend(plumes)
            self.objects.extend(plumes)
        return self

    def add_shield_bubble(self, radius: float = 2.4, scale_z: float = 0.78) -> "RogueVehicleBuilder":
        """Adds an aerodynamic geodesic shield bubble envelope around the vehicle."""
        bubble = make_shield_bubble_mesh(
            f"{self.name}VFX",
            center=(0.0, 0.0, self.hull_deck_z * 0.9),
            radius=radius,
            scale_z=scale_z,
            shield_mat=self.palette.emissive,
        )
        self.shield_bubble_object = bubble
        self.objects.append(bubble)
        return self

    def add_armature_rig(
        self,
        with_animations: bool = True,
        with_ik: bool = False,
        target_objects: Sequence[bpy.types.Object] | None = None,
    ) -> "RogueVehicleBuilder":
        """Creates a kinematic vehicle armature and binds mesh components to rigid bones."""
        objs = target_objects if target_objects is not None else self.objects
        effective_ik = with_ik or getattr(self, "with_ik", False)
        self.armature_object = generate_procedural_armature(
            objs,
            prefix=self.name,
            turret_center=self.turret_center,
            gun_pivot=self.gun_pivot,
            hull_deck_z=self.hull_deck_z,
            leg_type=self.leg_type,
            hip_height=getattr(self, "hip_height", 1.45),
            hip_loc_l=getattr(self, "hip_loc_l", None),
            hip_loc_r=getattr(self, "hip_loc_r", None),
            knee_loc_l=getattr(self, "knee_loc_l", None),
            knee_loc_r=getattr(self, "knee_loc_r", None),
            foot_loc_l=getattr(self, "foot_loc_l", None),
            foot_loc_r=getattr(self, "foot_loc_r", None),
            quad_hips=getattr(self, "quad_hips", None),
            quad_knees=getattr(self, "quad_knees", None),
            quad_feet=getattr(self, "quad_feet", None),
            with_ik=effective_ik,
        )
        if with_animations:
            self.action_clips = generate_procedural_action_clips(
                self.armature_object,
                prefix=self.name,
                leg_type=self.leg_type,
                locomotion_only=getattr(self, "locomotion_only", False),
            )
        return self

    def add_era_armor(
        self,
        rows: int = 2,
        cols: int = 3,
        location: Tuple[float, float, float] | Vector | None = None,
    ) -> "RogueVehicleBuilder":
        """Adds explosive reactive armor (ERA) tile cassettes to front glacis / turret."""
        loc = location if location is not None else Vector((0.0, self.hull_length * 0.38, self.hull_deck_z * 0.95))
        tiles = make_era_armor_tiles(
            f"{self.name}Attachment_ERA",
            base_location=loc,
            rows=rows,
            cols=cols,
            tile_mat=self.palette.light_armor,
            bolt_mat=self.palette.gunmetal,
        )
        self.attachment_objects.setdefault("era", []).extend(tiles)
        self.objects.extend(tiles)
        return self

    def add_aps_system(self) -> "RogueVehicleBuilder":
        """Adds Active Protection System (APS / Trophy) radar sensor pods and countermeasure tubes."""
        mount_z = self.hull_deck_z + 0.35
        for side in (-1.0, 1.0):
            mount_loc = Vector((side * (self.hull_width * 0.42), self.hull_length * 0.15, mount_z))
            aps_objs = make_aps_hardkill_system(
                f"{self.name}Attachment_APS",
                mount_location=mount_loc,
                side=side,
                pod_mat=self.palette.primary_armor,
                radar_face_mat=self.palette.light_armor,
                barrel_mat=self.palette.gunmetal,
            )
            self.attachment_objects.setdefault("aps", []).extend(aps_objs)
            self.objects.extend(aps_objs)
        return self

    def add_sponson_pods(self, weapon_type: str = "autocannon") -> "RogueVehicleBuilder":
        """Adds armored weapon sponsons to vehicle flanks."""
        mount_z = self.hull_deck_z * 0.55
        for side in (-1.0, 1.0):
            loc = Vector((side * (self.hull_width * 0.52), 0.05, mount_z))
            sponson_objs = make_sponson_pod(
                f"{self.name}Attachment_Sponson",
                location=loc,
                side=side,
                weapon_type=weapon_type,
                armor_mat=self.palette.primary_armor,
                gunmetal_mat=self.palette.gunmetal,
            )
            self.attachment_objects.setdefault("sponson", []).extend(sponson_objs)
            self.objects.extend(sponson_objs)
        return self

    def add_fuel_drums(self) -> "RogueVehicleBuilder":
        """Adds twin external auxiliary fuel/battery drums to rear hull deck."""
        rear_loc = Vector((0.0, -self.hull_length * 0.52, self.hull_deck_z * 0.65))
        drums = make_aux_fuel_drums(
            f"{self.name}Attachment_FuelDrums",
            rear_location=rear_loc,
            rear_width=self.hull_width * 0.85,
            drum_mat=self.palette.primary_armor,
            mount_mat=self.palette.gunmetal,
            strap_mat=self.palette.warning,
        )
        self.attachment_objects.setdefault("fuel_drums", []).extend(drums)
        self.objects.extend(drums)
        return self

    def add_attachments(self, attachment_types: Sequence[str]) -> "RogueVehicleBuilder":
        """Applies a sequence of named attachments ('era', 'aps', 'sponson', 'fuel_drums')."""
        for att in attachment_types:
            a = att.strip().lower()
            if a == "era":
                self.add_era_armor()
            elif a == "aps":
                self.add_aps_system()
            elif a in ("sponson", "sponsons"):
                self.add_sponson_pods()
            elif a in ("fuel_drums", "fuel", "drums"):
                self.add_fuel_drums()
        return self

    def add_sloped_hull(
        self,
        length: float = 3.0,
        width: float = 2.4,
        height: float = 0.88,
        bevel: float = 0.045,
    ) -> "RogueVehicleBuilder":
        self.hull_length = length
        self.hull_width = width
        self.hull_deck_z = height
        half_l = length * 0.5
        half_w = width * 0.5
        self.objects.append(make_sloped_glacis_hull(
            f"{self.name}Armor_Hull",
            length_front=half_l * 1.02, length_rear=half_l * 0.98,
            width_front=half_w * 1.15, width_rear=half_w * 1.22,
            height_lower=self.ground_clearance + 0.18, height_upper=height,
            material=self.palette.primary_armor, bevel=bevel
        ))
        self.objects.append(_make_tapered_shell(
            f"{self.name}Armor_UpperDeck",
            (width * 0.72, length * 0.78, 0.20),
            (0.0, -0.05, height + 0.10),
            self.palette.light_armor,
            top_scale=(0.96, 0.97),
            bevel=0.045,
            bevel_segments=4,
            smooth=True,
        ))
        # Transitional Turret Ring Coaming Collar
        coaming_radius = half_w * 0.65
        self.objects.append(make_cylinder(
            f"{self.name}Armor_TurretCoaming",
            radius=coaming_radius,
            depth=0.06,
            location=(0.0, 0.08, height + 0.20 + 0.03),
            material=self.palette.gunmetal,
            vertices=16,
            bevel=0.008,
        ))
        # Raised Forward Glacis Driver Cupola & Vision Slit
        driver_pos = (0.0, length * 0.28, height + 0.14)
        self.objects.append(make_box(
            f"{self.name}Armor_DriverHood",
            (0.36, 0.32, 0.14),
            driver_pos,
            self.palette.light_armor,
            bevel=0.015,
            rotation=(math.radians(-14), 0.0, 0.0),
        ))
        self.objects.append(make_box(
            f"{self.name}Armor_DriverPeriscope",
            (0.24, 0.05, 0.04),
            Vector(driver_pos) + Vector((0.0, 0.12, 0.04)),
            self.palette.emissive,
            bevel=0.004,
            rotation=(math.radians(-14), 0.0, 0.0),
        ))
        return self

    def add_continuous_tracks(
        self,
        length: float = 2.85,
        height: float = 0.62,
        belt_width: float = 0.34,
        roadwheels: int = 5,
        with_skirts: bool = True,
        track_sag: float = 0.0,
        with_suspension_arms: bool = True,
        with_spaced_skirts: bool = False,
    ) -> "RogueVehicleBuilder":
        half_w = self.hull_width * 0.5
        track_x = half_w * 0.90
        eff_sag = track_sag if track_sag > 0.0 else getattr(self, "track_sag", 0.0)
        eff_susp = with_suspension_arms or getattr(self, "suspension_arms", False)
        eff_spaced = with_spaced_skirts or getattr(self, "spaced_skirts", False)
        for side in (-1, 1):
            bridge_w = track_x - (self.hull_width * 0.32)
            if bridge_w > 0.04:
                self.objects.append(make_box(
                    f"{self.name}Armor_SponsonBridge_{'L' if side < 0 else 'R'}",
                    (bridge_w, length * 0.76, 0.06),
                    (side * (track_x * 0.68), 0.0, self.ground_clearance + height * 0.72),
                    self.palette.primary_armor,
                    bevel=0.012,
                ))
            tracks = make_continuous_track_assembly(
                f"{self.name}Treads",
                x_pos=side * track_x,
                length=length, height=height, belt_width=belt_width,
                roadwheel_count=roadwheels,
                tread_mat=self.palette.tread,
                rim_mat=self.palette.wheel_rim,
                hub_mat=self.palette.wheel_hub,
                axle_mat=self.palette.gunmetal,
                fender_mat=self.palette.light_armor,
                skirt_mat=self.palette.primary_armor if with_skirts else None,
                accent_mat=self.palette.warning,
                ground_z=self.ground_clearance,
                track_sag=eff_sag,
                with_suspension_arms=eff_susp,
                with_spaced_skirts=eff_spaced,
            )
            self.objects.extend(tracks)
        return self

    def add_all_terrain_wheels(
        self,
        wheelbase_y: float = 1.8,
        track_width_x: float = 1.6,
        wheel_radius: float = 0.38,
        wheel_width: float = 0.28,
        middle_axle: bool = False,
        lug_count: int = 10,
        bolt_count: int = 6,
        with_fenders: bool = False,
    ) -> "RogueVehicleBuilder":
        effective_z = self.ground_clearance + wheel_radius + 0.035
        axles = [(-wheelbase_y * 0.5, "Rear"), (wheelbase_y * 0.5, "Front")]
        if middle_axle:
            axles.insert(1, (-wheelbase_y * 0.06, "Mid"))
        wheel_locations = []
        for side in (-1, 1):
            x = side * (track_width_x * 0.5)
            for y_pos, label in axles:
                loc = (x, y_pos, effective_z)
                wheel_locations.append(loc)
                w = make_all_terrain_wheel_assembly(
                    f"{self.name}Wheel_{label}_{'L' if side < 0 else 'R'}",
                    location=loc,
                    radius=wheel_radius, width=wheel_width,
                    tire_mat=self.palette.tread, rim_mat=self.palette.wheel_rim,
                    hub_mat=self.palette.gunmetal, accent_mat=self.palette.warning,
                    lug_count=lug_count, bolt_count=bolt_count,
                )
                self.objects.extend(w)
        # Transverse drive axle beam housing connecting wheels across chassis underside
        if track_width_x > 0.15:
            for y_pos, label in axles:
                self.objects.append(make_rod(
                    f"{self.name}Chassis_AxleBeam_{label}",
                    (-track_width_x * 0.42, y_pos, effective_z),
                    (track_width_x * 0.42, y_pos, effective_z),
                    0.058, self.palette.gunmetal, vertices=8
                ))
                self.objects.append(make_cylinder(
                    f"{self.name}Chassis_Differential_{label}",
                    0.13, 0.20, (0.0, y_pos, effective_z),
                    self.palette.gunmetal, rotation=(0.0, math.radians(90.0), 0.0), vertices=10, bevel=0.012
                ))
                for side in (-1, 1):
                    stag = "L" if side < 0 else "R"
                    self.objects.append(make_rod(
                        f"{self.name}Chassis_ShockStrut_{label}_{stag}",
                        (side * (track_width_x * 0.40), y_pos, effective_z),
                        (side * (track_width_x * 0.34), y_pos, self.hull_deck_z),
                        0.032, self.palette.primary_armor, vertices=6
                    ))
        if with_fenders and track_width_x > 0.05:
            self.add_wheel_fenders(wheel_locations, radius=wheel_radius, width=wheel_width)
        return self

    def add_bipedal_legs(
        self,
        hip_height: float = 1.45,
        hip_spacing_x: float = 0.84,
        total_height: float = 1.60,
    ) -> "RogueVehicleBuilder":
        self.is_walker = True
        self.leg_type = "biped"
        self.hip_height = hip_height
        self.hip_loc_l = Vector((-hip_spacing_x * 0.5, -0.05, hip_height))
        self.hip_loc_r = Vector((hip_spacing_x * 0.5, -0.05, hip_height))
        self.knee_loc_l = Vector((-hip_spacing_x * 0.5 - 0.12, -0.05 - hip_height * 0.14, hip_height * 0.58))
        self.knee_loc_r = Vector((hip_spacing_x * 0.5 + 0.12, -0.05 - hip_height * 0.14, hip_height * 0.58))
        self.foot_loc_l = Vector((-hip_spacing_x * 0.5 - 0.08, -0.05 + hip_height * 0.10 + 0.12, 0.0))
        self.foot_loc_r = Vector((hip_spacing_x * 0.5 + 0.08, -0.05 + hip_height * 0.10 + 0.12, 0.0))
        for side in (-1, 1):
            leg = make_bipedal_digitigrade_leg(
                f"{self.name}Leg",
                hip_location=(side * (hip_spacing_x * 0.5), -0.05, hip_height),
                side=side,
                total_height=total_height,
                armor_mat=self.palette.primary_armor,
                joint_mat=self.palette.gunmetal,
                piston_mat=self.palette.light_armor,
                accent_mat=self.palette.warning
            )
            self.objects.extend(leg)
        return self

    def add_turret(
        self,
        size: Tuple[float, float, float] = (1.4, 1.6, 0.45),
        center_y: float = 0.08,
    ) -> "RogueVehicleBuilder":
        self.turret_center = Vector((0.0, center_y, self.hull_deck_z + 0.24 + size[2] * 0.5))
        turret = make_faceted_turret_housing(
            f"{self.name}Turret",
            location=self.turret_center,
            size=size,
            armor_mat=self.palette.primary_armor,
            roof_mat=self.palette.light_armor,
        )
        self.objects.extend(turret)
        self.preserved_pivots.append(f"{self.name}Turret_BaseShell")
        return self

    def add_cannon(
        self,
        caliber: float = 0.095,
        length: float = 1.65,
        elevation_deg: float = 12.0,
        with_dust_boot: bool = True,
    ) -> "RogueVehicleBuilder":
        gun_pivot = self.turret_center + Vector((0.0, 0.45, 0.02))
        elev_rad = math.radians(elevation_deg)
        gun_dir = Vector((0.0, math.cos(elev_rad), math.sin(elev_rad))).normalized()
        self.gun_pivot = gun_pivot + gun_dir * (length + 0.32)
        self.gun_dir = gun_dir
        eff_boot = with_dust_boot or getattr(self, "mantlet_boot", False)
        self._last_mantlet_boot_pivots = [gun_pivot]
        self._last_mantlet_boot_direction = gun_dir
        self._last_mantlet_boots_added = eff_boot
        cannon = make_coaxial_cannon_assembly(
            f"{self.name}Gun",
            gun_pivot=gun_pivot,
            direction=gun_dir,
            caliber_radius=caliber,
            barrel_length=length,
            armor_mat=self.palette.light_armor,
            barrel_mat=self.palette.gunmetal,
            accent_mat=self.palette.warning,
            has_thermal_sleeve=True,
            has_bore_evacuator=True,
            has_muzzle_brake=True,
            has_dust_boot=eff_boot,
            dust_boot_mat=self.palette.tread,
        )
        self.objects.extend(cannon)
        self.preserved_pivots.append(f"{self.name}Gun_MainBarrel")
        return self

    def add_rotary_gatling(
        self,
        side: int = 1,
        pivot_offset: Tuple[float, float, float] = (0.75, 0.45, 1.35),
    ) -> "RogueVehicleBuilder":
        self.gun_pivot = Vector((side * pivot_offset[0], pivot_offset[1] + 1.10, pivot_offset[2]))
        self.gun_dir = Vector((0.0, 1.0, 0.0))
        self._last_mantlet_boot_pivots = []
        self._last_mantlet_boot_direction = None
        self._last_mantlet_boots_added = False
        # Anti-floating pintle mounting bracket anchoring weapon pivot
        self.objects.append(make_box(
            f"{self.name}Gatling_PintleSaddle_{'L' if side < 0 else 'R'}",
            (0.18, 0.26, 0.16),
            Vector((side * pivot_offset[0], pivot_offset[1] - 0.06, pivot_offset[2] - 0.12)),
            self.palette.primary_armor,
            bevel=0.012,
        ))
        self.objects.append(make_cylinder(
            f"{self.name}Gatling_PintlePin_{'L' if side < 0 else 'R'}",
            0.055, 0.22,
            Vector((side * pivot_offset[0], pivot_offset[1] - 0.06, pivot_offset[2] - 0.12)),
            self.palette.gunmetal,
            rotation=(0.0, math.radians(90.0), 0.0),
            vertices=8,
            bevel=0.006,
        ))
        gatling = make_rotary_gatling_cannon(
            f"{self.name}Gatling_{'L' if side < 0 else 'R'}",
            pivot=(side * pivot_offset[0], pivot_offset[1], pivot_offset[2]),
            direction=(0.0, 1.0, 0.0),
            barrel_count=6, barrel_length=1.10, barrel_radius=0.03, cluster_radius=0.09,
            gunmetal_mat=self.palette.gunmetal, housing_mat=self.palette.light_armor, accent_mat=self.palette.warning
        )
        self.objects.extend(gatling)
        return self

    def add_missile_pod(
        self,
        rows: int = 2,
        cols: int = 3,
        elevation_deg: float = 14.0,
    ) -> "RogueVehicleBuilder":
        pod_loc = self.turret_center + Vector((0.0, 0.10, 0.35))
        elev_rad = math.radians(elevation_deg)
        self.gun_pivot = pod_loc + Vector((0.0, 0.45, 0.15))
        self.gun_dir = Vector((0.0, math.cos(elev_rad), math.sin(elev_rad))).normalized()
        self._last_mantlet_boot_pivots = []
        self._last_mantlet_boot_direction = None
        self._last_mantlet_boots_added = False
        # Anti-floating trunnion cradle anchoring pod to roof
        cradle_h = 0.18
        self.objects.append(make_box(
            f"{self.name}MissilePod_TrunnionCradle",
            (0.52, 0.62, cradle_h),
            pod_loc - Vector((0.0, 0.0, 0.18)),
            self.palette.primary_armor,
            bevel=0.015,
        ))
        self.objects.append(make_cylinder(
            f"{self.name}MissilePod_TrunnionAxle",
            0.085, 0.68,
            pod_loc - Vector((0.0, 0.0, 0.08)),
            self.palette.gunmetal,
            rotation=(0.0, math.radians(90.0), 0.0),
            vertices=10,
            bevel=0.008,
        ))
        pod = make_missile_launcher_pod(
            f"{self.name}MissilePod",
            location=pod_loc,
            size=(0.68, 0.90, 0.45),
            rows=rows, cols=cols, elevation_deg=elevation_deg,
            armor_mat=self.palette.primary_armor, cavity_mat=self.palette.gunmetal,
            warhead_mat=self.palette.warning, accent_mat=self.palette.warning
        )
        self.objects.extend(pod)
        return self

    def add_arc_emitter(
        self,
        core_radius: float = 0.20,
        prongs: int = 3,
    ) -> "RogueVehicleBuilder":
        emitter_loc = self.turret_center + Vector((0.0, 0.35, 0.10))
        self.gun_pivot = emitter_loc + Vector((0.0, 0.70, 0.05))
        self.gun_dir = Vector((0.0, 1.0, 0.08)).normalized()
        self._last_mantlet_boot_pivots = []
        self._last_mantlet_boot_direction = None
        self._last_mantlet_boots_added = False
        # Anti-floating mounting collar seating arc emitter into face
        self.objects.append(make_cylinder(
            f"{self.name}Arc_PedestalCollar",
            core_radius * 1.55, 0.12,
            emitter_loc - Vector((0.0, 0.05, 0.04)),
            self.palette.primary_armor,
            rotation=(math.radians(90.0), 0.0, 0.0),
            vertices=12,
            bevel=0.012,
        ))
        emitter = make_arc_energy_emitter(
            f"{self.name}ArcEmitter",
            base_location=emitter_loc,
            forward_dir=(0.0, 1.0, 0.08),
            core_radius=core_radius, core_length=0.45,
            prong_count=prongs, prong_length=0.50,
            housing_mat=self.palette.primary_armor,
            gunmetal_mat=self.palette.gunmetal,
            arc_mat=self.palette.emissive
        )
        self.objects.extend(emitter)
        return self

    def add_sensor_visor(self) -> "RogueVehicleBuilder":
        visor_loc = self.turret_center + Vector((0.0, 0.35, 0.28))
        self.sensor_loc = visor_loc + Vector((0.0, 0.14, 0.0))
        visor = make_recessed_sensor_visor(
            f"{self.name}Sensor_Visor",
            location=visor_loc,
            size=(0.48, 0.14, 0.16),
            armor_mat=self.palette.light_armor,
            lens_mat=self.palette.emissive,
            cavity_mat=self.palette.gunmetal,
        )
        self.objects.extend(visor)
        return self

    def add_engine_deck(
        self,
        slat_count: int = 4,
        with_exhausts: bool = True,
        size: Tuple[float, float, float] | None = None,
    ) -> "RogueVehicleBuilder":
        deck_y = -self.hull_length * 0.30
        deck_z = self.hull_deck_z + 0.10
        if size is None:
            size = (self.hull_width * 0.34, self.hull_length * 0.22, 0.07)

        # Recessed dark radiator intake cavity under slats
        self.objects.append(make_box(
            f"{self.name}Engine_RadiatorCavity",
            (size[0] * 0.90, size[1] * 0.88, size[2] * 0.45),
            (0.0, deck_y, deck_z - size[2] * 0.15),
            self.palette.gunmetal,
            bevel=0.008,
        ))
        grille = make_engine_deck_grille(
            f"{self.name}Armor_EngineDeck",
            location=(0.0, deck_y, deck_z),
            size=size,
            slat_count=slat_count,
            slat_mat=self.palette.gunmetal,
            frame_mat=self.palette.primary_armor,
        )
        self.objects.extend(grille)

        if with_exhausts:
            exhaust_y = deck_y - size[1] * 0.5 - 0.12
            exhaust_spacing_x = self.hull_width * 0.22
            self.exhaust_l = Vector((-exhaust_spacing_x, exhaust_y, deck_z + 0.20))
            self.exhaust_r = Vector((exhaust_spacing_x, exhaust_y, deck_z + 0.20))
            for side in (-1, 1):
                side_tag = "L" if side < 0 else "R"
                ex_loc = (side * exhaust_spacing_x, exhaust_y, deck_z + 0.10)
                self.objects.append(make_cylinder(
                    f"{self.name}Armor_Exhaust_{side_tag}",
                    radius=0.065, depth=0.34,
                    location=ex_loc,
                    material=self.palette.gunmetal,
                    rotation=(math.radians(-18), 0.0, 0.0),
                    vertices=10, bevel=0.008
                ))
                self.objects.append(make_cylinder(
                    f"{self.name}Armor_ExhaustShield_{side_tag}",
                    radius=0.082, depth=0.18,
                    location=(side * exhaust_spacing_x, exhaust_y + 0.02, deck_z + 0.04),
                    material=self.palette.light_armor,
                    rotation=(math.radians(-18), 0.0, 0.0),
                    vertices=10, bevel=0.008
                ))
        return self

    def add_spring_antenna(
        self,
        location: Tuple[float, float, float] | None = None,
        length: float = 0.65,
        lean_deg: float = 6.0,
    ) -> "RogueVehicleBuilder":
        if location is None:
            if hasattr(self, "turret_center") and self.turret_center is not None:
                base_pos = self.turret_center + Vector((0.38, -0.42, 0.22))
            else:
                base_pos = Vector((self.hull_width * 0.28, -self.hull_length * 0.38, self.hull_deck_z + 0.12))
        else:
            base_pos = Vector(location)

        self.antenna_loc = base_pos
        # Anti-floating deck mounting collar
        self.objects.append(make_cylinder(
            f"{self.name}Sensor_AntennaDeckCollar",
            0.062, 0.06, base_pos - Vector((0.0, 0.0, 0.03)),
            self.palette.primary_armor, vertices=10, bevel=0.008
        ))
        self.objects.append(make_cylinder(
            f"{self.name}Sensor_AntennaBase",
            0.038, 0.08, base_pos,
            self.palette.gunmetal, vertices=8, bevel=0.008
        ))
        lean_rad = math.radians(lean_deg)
        mast_end = base_pos + Vector((0.0, -math.sin(lean_rad) * length, math.cos(lean_rad) * length))
        self.objects.append(make_rod(
            f"{self.name}Sensor_AntennaMast",
            base_pos + Vector((0.0, 0.0, 0.04)), mast_end,
            0.012, self.palette.gunmetal, vertices=6
        ))
        self.objects.append(make_cylinder(
            f"{self.name}Sensor_AntennaTip",
            0.018, 0.04, mast_end,
            self.palette.warning, vertices=8
        ))
        self.preserved_pivots.append("Sensor_Antenna")
        return self

    def add_gimbal_sensor(
        self,
        location: Tuple[float, float, float] | None = None,
        radius: float = 0.16,
    ) -> "RogueVehicleBuilder":
        if location is None:
            if hasattr(self, "turret_center") and self.turret_center is not None:
                loc = self.turret_center + Vector((-0.38, 0.24, 0.24))
            else:
                loc = Vector((-self.hull_width * 0.22, self.hull_length * 0.22, self.hull_deck_z + 0.18))
        else:
            loc = Vector(location)
        sensor = make_gimbal_sensor_turret(
            f"{self.name}GimbalSensor",
            location=loc,
            radius=radius,
            armor_mat=self.palette.light_armor,
            lens_mat=self.palette.emissive,
            gunmetal_mat=self.palette.gunmetal
        )
        self.objects.extend(sensor)
        return self

    def add_armor_applique(
        self,
        start: Tuple[float, float, float],
        end: Tuple[float, float, float],
        count: int = 4,
        size: Tuple[float, float, float] = (0.18, 0.22, 0.05),
        rotation: Tuple[float, float, float] = (0.0, 0.0, 0.0),
    ) -> "RogueVehicleBuilder":
        tiles = make_armor_applique_array(
            f"{self.name}Armor_ERA",
            start=start, end=end, count=count, size=size,
            armor_mat=self.palette.primary_armor,
            standoff_mat=self.palette.gunmetal,
            rotation=rotation
        )
        self.objects.extend(tiles)
        return self

    def add_armor_plating(
        self,
        thickness: float = 0.035,
        inset_depth: float = 0.045,
    ) -> "RogueVehicleBuilder":
        """Applies procedural BMesh armor plating with recessed gutters to hull and turret armor meshes."""
        self.armor_plating = True
        target_armor_objs = [
            obj for obj in self.objects
            if obj.type == "MESH" and any(k in obj.name for k in ("Hull", "Turret", "Torso", "Armor", "Chassis", "Glacis"))
            and not obj.name.endswith(("_COLLIDER", "_ShieldBubble", "_TrackPod", "_Damper", "_Piston"))
        ]
        for obj in target_armor_objs:
            apply_procedural_armor_plating(obj, thickness=thickness, inset_depth=inset_depth)
        return self

    def add_dozer_blade(
        self,
        width: float = 2.4,
        height: float = 0.42,
    ) -> "RogueVehicleBuilder":
        front_y = self.hull_length * 0.54
        blade_z = self.ground_clearance + height * 0.5
        blade = make_dozer_blade(
            f"{self.name}Dozer",
            (0.0, front_y, blade_z),
            width=width, height=height,
            armor_mat=self.palette.primary_armor,
            ram_mat=self.palette.gunmetal
        )
        self.objects.extend(blade)
        return self

    def add_hover_skimmer(
        self,
        float_height: float = 0.52,
    ) -> "RogueVehicleBuilder":
        self.is_hover = True
        skimmer = make_hover_skimmer_assembly(
            f"{self.name}Hover",
            hull_length=self.hull_length,
            hull_width=self.hull_width,
            float_height=float_height,
            skirt_mat=self.palette.primary_armor,
            thruster_mat=self.palette.gunmetal,
            core_mat=self.palette.light_armor,
            glow_mat=self.palette.emissive
        )
        self.objects.extend(skimmer)
        return self

    def add_quad_crawler_legs(
        self,
        knee_height: float = 0.95,
        reach_x: float = 1.05,
        reach_y: float = 0.85,
    ) -> "RogueVehicleBuilder":
        self.is_walker = True
        self.leg_type = "quad"
        self.quad_feet = {}
        self.quad_hips = {}
        self.quad_knees = {}
        half_w = self.hull_width * 0.46
        half_l = self.hull_length * 0.40
        for sx in (-1, 1):
            for sy in (-1, 1):
                tag_mesh = f"{'L' if sx < 0 else 'R'}_{'F' if sy > 0 else 'R'}"
                tag_canonical = f"{'F' if sy > 0 else 'R'}{'L' if sx < 0 else 'R'}"
                hip = Vector((sx * half_w, sy * half_l, self.ground_clearance + 0.65))
                foot_target = Vector((hip.x + sx * reach_x, hip.y + sy * reach_y, 0.0))
                knee_pos = Vector((hip.x + sx * (reach_x * 0.65), hip.y + sy * (reach_y * 0.60), knee_height))
                self.quad_hips[tag_canonical] = hip
                self.quad_knees[tag_canonical] = knee_pos
                self.quad_feet[tag_canonical] = foot_target
                leg = make_quad_crawler_leg(
                    f"{self.name}CrawlerLeg_{tag_mesh}",
                    hip_location=hip,
                    side_x=sx, side_y=sy,
                    reach_x=reach_x, reach_y=reach_y,
                    knee_height=knee_height,
                    armor_mat=self.palette.primary_armor,
                    joint_mat=self.palette.gunmetal,
                    damper_mat=self.palette.light_armor,
                    accent_mat=self.palette.warning
                )
                self.objects.extend(leg)
        return self

    def add_smoke_launchers(
        self,
        tube_count: int = 3,
    ) -> "RogueVehicleBuilder":
        for side in (-1, 1):
            side_tag = "L" if side < 0 else "R"
            if hasattr(self, "turret_center") and self.turret_center is not None:
                loc = self.turret_center + Vector((side * 0.72, 0.28, 0.15))
                dir_v = Vector((side * 0.70, 0.70, 0.45)).normalized()
            else:
                loc = Vector((side * (self.hull_width * 0.48), self.hull_length * 0.25, self.hull_deck_z + 0.08))
                dir_v = Vector((side * 0.70, 0.70, 0.40)).normalized()
            # Anti-floating structural mounting bracket connecting rack to turret cheek
            self.objects.append(make_box(
                f"{self.name}Smoke_MountBracket_{side_tag}",
                (0.16, 0.18, 0.08),
                loc - Vector((side * 0.06, 0.04, 0.04)),
                self.palette.primary_armor,
                bevel=0.008,
                rotation=(0.0, side * math.radians(20.0), 0.0),
            ))
            rack = make_smoke_launcher_rack(
                f"{self.name}Smoke_{side_tag}",
                location=loc,
                tube_count=tube_count,
                direction=dir_v,
                armor_mat=self.palette.primary_armor,
                tube_mat=self.palette.gunmetal,
                cap_mat=self.palette.warning
            )
            self.objects.extend(rack)
        return self

    def add_radar_dish(
        self,
        is_rotodome: bool = False,
        location: Tuple[float, float, float] | None = None,
    ) -> "RogueVehicleBuilder":
        if location is None:
            if hasattr(self, "turret_center") and self.turret_center is not None:
                loc = self.turret_center + Vector((0.0, -0.35, 0.32))
            else:
                loc = Vector((0.0, -self.hull_length * 0.25, self.hull_deck_z + 0.14))
        else:
            loc = Vector(location)
        # Anti-floating structural deck pylon anchoring turntable to roof
        pylon_h = 0.16
        self.objects.append(make_cylinder(
            f"{self.name}Radar_DeckPylon",
            0.12, pylon_h, loc - Vector((0.0, 0.0, pylon_h * 0.5)),
            self.palette.primary_armor, vertices=10, bevel=0.015
        ))
        radar = make_radar_dish_or_rotodome(
            f"{self.name}Radar",
            location=loc,
            dish_radius=0.44,
            is_rotodome=is_rotodome,
            armor_mat=self.palette.primary_armor,
            antenna_mat=self.palette.gunmetal,
            accent_mat=self.palette.warning
        )
        self.objects.extend(radar)
        return self

    def add_side_sponsons(
        self,
        size: Tuple[float, float, float] = (0.24, 0.82, 0.38),
    ) -> "RogueVehicleBuilder":
        for side in (-1, 1):
            side_tag = "L" if side < 0 else "R"
            loc = (side * (self.hull_width * 0.54), -self.hull_length * 0.05, self.ground_clearance + size[2] * 0.8)
            # Anti-floating cantilever support arms bridging sponson to hull
            for y_rel, y_tag in [(-size[1] * 0.32, "Rear"), (size[1] * 0.32, "Front")]:
                self.objects.append(make_box(
                    f"{self.name}Sponson_BridgeArm_{side_tag}_{y_tag}",
                    (self.hull_width * 0.14, 0.10, size[2] * 0.52),
                    (side * (self.hull_width * 0.49), loc[1] + y_rel, loc[2]),
                    self.palette.primary_armor,
                    bevel=0.008,
                ))
            sponson = make_side_sponson_assembly(
                f"{self.name}Sponson_{side_tag}",
                location=loc,
                side=side,
                size=size,
                armor_mat=self.palette.primary_armor,
                strap_mat=self.palette.gunmetal,
                accent_mat=self.palette.warning
            )
            self.objects.extend(sponson)
        return self

    def add_tiltrotors(
        self,
        location_offset: Tuple[float, float, float] = (1.35, 0.15, 0.72),
        radius: float = 0.44,
        tilt_deg: float = 14.0,
    ) -> "RogueVehicleBuilder":
        self.is_hover = True
        for side in (-1, 1):
            side_tag = "L" if side < 0 else "R"
            pod = make_ducted_tiltrotor_assembly(
                f"{self.name}TiltRotor_{side_tag}",
                location=(side * location_offset[0], location_offset[1], location_offset[2]),
                radius=radius,
                depth=0.26,
                tilt_deg=tilt_deg,
                duct_mat=self.palette.primary_armor,
                rotor_mat=self.palette.gunmetal,
                hub_mat=self.palette.light_armor,
                pylon_mat=self.palette.gunmetal,
                glow_mat=self.palette.emissive,
                blade_count=3,
                pylon_inboard_x=-side * 0.32,
            )
            self.objects.extend(pod)
        return self

    def add_rollcage(
        self,
        size: Tuple[float, float, float] = (1.15, 1.45, 0.78),
        location: Tuple[float, float, float] | None = None,
        bar_radius: float = 0.034,
    ) -> "RogueVehicleBuilder":
        if location is None:
            loc = (0.0, -self.hull_length * 0.05, self.hull_deck_z + size[2] * 0.5 + 0.02)
        else:
            loc = location
        cage = make_tubular_rollcage_assembly(
            f"{self.name}RollCage",
            size=size,
            location=loc,
            bar_radius=bar_radius,
            tube_mat=self.palette.gunmetal,
            roof_mat=self.palette.primary_armor,
            visor_mat=self.palette.light_armor,
        )
        self.objects.extend(cage)
        return self

    def add_howitzer(
        self,
        caliber: float = 0.135,
        length: float = 1.15,
        elevation_deg: float = 26.0,
    ) -> "RogueVehicleBuilder":
        gun_pivot = self.turret_center + Vector((0.0, 0.40, 0.02))
        elev_rad = math.radians(elevation_deg)
        gun_dir = Vector((0.0, math.cos(elev_rad), math.sin(elev_rad))).normalized()
        self.gun_pivot = gun_pivot + gun_dir * (length + 0.28)
        self.gun_dir = gun_dir
        self._last_mantlet_boot_pivots = [gun_pivot]
        self._last_mantlet_boot_direction = gun_dir
        eff_boot = self.mantlet_boot
        self._last_mantlet_boots_added = eff_boot
        howitzer = make_heavy_howitzer_assembly(
            f"{self.name}Howitzer",
            gun_pivot=gun_pivot,
            direction=gun_dir,
            caliber_radius=caliber,
            barrel_length=length,
            armor_mat=self.palette.light_armor,
            barrel_mat=self.palette.gunmetal,
            accent_mat=self.palette.warning,
        )
        self.objects.extend(howitzer)
        if eff_boot:
            self.objects.extend(make_mantlet_dust_boot(
                f"{self.name}Howitzer_DustBoot",
                pivot=gun_pivot - gun_dir * 0.04,
                direction=gun_dir,
                collar_radius=caliber * 1.8,
                taper_radius=caliber * 1.25,
                length=0.22,
                stages=3,
                material=self.palette.tread,
                accent_mat=self.palette.warning,
            ))
        self.preserved_pivots.append(f"{self.name}Howitzer_BreechCollar")
        return self

    def add_dual_cannon(
        self,
        caliber: float = 0.082,
        length: float = 1.65,
        spacing: float = 0.38,
        elevation_deg: float = 11.0,
    ) -> "RogueVehicleBuilder":
        gun_pivot = self.turret_center + Vector((0.0, 0.42, 0.02))
        elev_rad = math.radians(elevation_deg)
        gun_dir = Vector((0.0, math.cos(elev_rad), math.sin(elev_rad))).normalized()
        self.gun_pivot = gun_pivot + gun_dir * (length + 0.38)
        self.gun_dir = gun_dir
        u_vec, _, _ = get_orthonormal_basis(gun_dir)
        self.primary_firepoint_locs = [
            gun_pivot + u_vec * (side * spacing * 0.5) + gun_dir * (length + 0.38 + caliber * 1.2)
            for side in (-1, 1)
        ]
        self._last_mantlet_boot_pivots = [
            gun_pivot + u_vec * (side * spacing * 0.5)
            for side in (-1, 1)
        ]
        self._last_mantlet_boot_direction = gun_dir
        eff_boot = self.mantlet_boot
        self._last_mantlet_boots_added = eff_boot
        cannons = make_dual_cannon_assembly(
            f"{self.name}DualGun",
            gun_pivot=gun_pivot,
            direction=gun_dir,
            caliber_radius=caliber,
            barrel_length=length,
            barrel_spacing=spacing,
            armor_mat=self.palette.light_armor,
            barrel_mat=self.palette.gunmetal,
            accent_mat=self.palette.warning,
            has_dust_boot=eff_boot,
            dust_boot_mat=self.palette.tread,
        )
        self.objects.extend(cannons)
        self.preserved_pivots.append(f"{self.name}DualGun_MantletBlock")
        return self

    def add_beam_laser(
        self,
        barrel_length: float = 2.10,
        caliber: float = 0.08,
        elevation_deg: float = 6.0,
    ) -> "RogueVehicleBuilder":
        """Adds a heavy collimated beam laser assembly with magnetic accelerator rings and optical lens."""
        gun_pivot = self.turret_center + Vector((0.0, 0.45, 0.04))
        elev_rad = math.radians(elevation_deg)
        gun_dir = Vector((0.0, math.cos(elev_rad), math.sin(elev_rad))).normalized()
        self.gun_pivot = gun_pivot + gun_dir * (barrel_length + 0.24)
        self.gun_dir = gun_dir
        self._last_mantlet_boot_pivots = []
        self._last_mantlet_boot_direction = None
        self._last_mantlet_boots_added = False
        laser = make_collimated_beam_laser_assembly(
            f"{self.name}Laser",
            gun_pivot=gun_pivot,
            direction=gun_dir,
            barrel_length=barrel_length,
            caliber_radius=caliber,
            housing_mat=self.palette.primary_armor,
            gunmetal_mat=self.palette.gunmetal,
            lens_mat=self.palette.emissive,
            accent_mat=self.palette.warning,
        )
        self.objects.extend(laser)
        self.preserved_pivots.append(f"{self.name}Laser_LaserGenerator")
        return self

    def add_vls_missile_array(
        self,
        rows: int = 2,
        cols: int = 4,
        size: Tuple[float, float, float] = (0.95, 1.35, 0.38),
    ) -> "RogueVehicleBuilder":
        """Adds an armored Vertical Launch System (VLS) missile cell matrix."""
        silo_loc = self.turret_center + Vector((0.0, -0.05, 0.12))
        self.gun_pivot = silo_loc + Vector((0.0, 0.25, size[2] * 0.5 + 0.15))
        self.gun_dir = Vector((0.0, 0.35, 0.94)).normalized()
        self._last_mantlet_boot_pivots = []
        self._last_mantlet_boot_direction = None
        self._last_mantlet_boots_added = False
        vls = make_vls_vertical_missile_array(
            f"{self.name}VLS",
            location=silo_loc,
            size=size,
            rows=rows,
            cols=cols,
            armor_mat=self.palette.primary_armor,
            cavity_mat=self.palette.gunmetal,
            hatch_mat=self.palette.light_armor,
            missile_mat=self.palette.warning,
            accent_mat=self.palette.warning,
        )
        self.objects.extend(vls)
        self.preserved_pivots.append(f"{self.name}VLS_SiloDeck")
        return self

    def add_quad_track_pods(
        self,
        pod_length: float = 1.55,
        pod_height: float = 0.56,
        track_spacing_x: float = 2.4,
        pod_spacing_y: float = 1.9,
        track_sag: float = 0.0,
    ) -> "RogueVehicleBuilder":
        eff_sag = track_sag if track_sag > 0.0 else getattr(self, "track_sag", 0.0)
        for sx in (-1, 1):
            for sy in (-1, 1):
                tag = f"{'L' if sx < 0 else 'R'}_{'F' if sy > 0 else 'R'}"
                pod_loc = (sx * (track_spacing_x * 0.5), sy * (pod_spacing_y * 0.5), 0.0)
                pod = make_quad_track_pod_assembly(
                    f"{self.name}TrackPod_{tag}",
                    location=pod_loc,
                    length=pod_length,
                    height=pod_height,
                    belt_width=0.34,
                    roadwheel_count=3,
                    tread_mat=self.palette.tread,
                    rim_mat=self.palette.wheel_rim,
                    hub_mat=self.palette.wheel_hub,
                    axle_mat=self.palette.gunmetal,
                    fender_mat=self.palette.primary_armor,
                    accent_mat=self.palette.warning,
                    ground_z=self.ground_clearance,
                    inboard_side=-sx,
                    track_sag=eff_sag,
                )
                self.objects.extend(pod)
        return self

    def add_troop_compartment(
        self,
        size: Tuple[float, float, float] = (1.45, 1.65, 0.76),
        location: Tuple[float, float, float] | None = None,
    ) -> "RogueVehicleBuilder":
        if location is None:
            loc = (0.0, -self.hull_length * 0.18, self.hull_deck_z + size[2] * 0.45)
        else:
            loc = location
        cabin = make_troop_compartment(
            f"{self.name}TroopBay",
            location=loc,
            size=size,
            armor_mat=self.palette.primary_armor,
            door_mat=self.palette.light_armor,
            detail_mat=self.palette.gunmetal,
        )
        self.objects.extend(cabin)
        return self

    def add_railgun(
        self,
        rail_length: float = 2.40,
        rail_height: float = 0.28,
        rail_width: float = 0.40,
        bore_gap: float = 0.09,
        elevation_deg: float = 8.0,
    ) -> "RogueVehicleBuilder":
        gun_pivot = self.turret_center + Vector((0.0, 0.44, 0.02))
        elev_rad = math.radians(elevation_deg)
        gun_dir = Vector((0.0, math.cos(elev_rad), math.sin(elev_rad))).normalized()
        self.gun_pivot = gun_pivot + gun_dir * (rail_length + 0.46)
        self.gun_dir = gun_dir
        self._last_mantlet_boot_pivots = [gun_pivot]
        self._last_mantlet_boot_direction = gun_dir
        eff_boot = self.mantlet_boot
        self._last_mantlet_boots_added = eff_boot
        gun = make_railgun_assembly(
            f"{self.name}Railgun",
            gun_pivot=gun_pivot,
            direction=gun_dir,
            rail_length=rail_length,
            rail_height=rail_height,
            rail_width=rail_width,
            bore_gap=bore_gap,
            armor_mat=self.palette.light_armor,
            rail_mat=self.palette.gunmetal,
            glow_mat=self.palette.emissive,
            capacitor_mat=self.palette.primary_armor,
            accent_mat=self.palette.warning,
        )
        self.objects.extend(gun)
        if eff_boot:
            self.objects.extend(make_mantlet_dust_boot(
                f"{self.name}Railgun_DustBoot",
                pivot=gun_pivot - gun_dir * 0.04,
                direction=gun_dir,
                collar_radius=rail_width * 0.52,
                taper_radius=rail_width * 0.40,
                length=0.22,
                stages=3,
                material=self.palette.tread,
                accent_mat=self.palette.warning,
            ))
        self.preserved_pivots.append(f"{self.name}Railgun_BreechHousing")
        return self

    def add_quad_flak(
        self,
        barrel_length: float = 1.65,
        caliber_radius: float = 0.038,
        spacing_x: float = 0.24,
        spacing_z: float = 0.20,
        elevation_deg: float = 34.0,
    ) -> "RogueVehicleBuilder":
        gun_pivot = self.turret_center + Vector((0.0, 0.38, 0.04))
        elev_rad = math.radians(elevation_deg)
        gun_dir = Vector((0.0, math.cos(elev_rad), math.sin(elev_rad))).normalized()
        self.gun_pivot = gun_pivot + gun_dir * (barrel_length + 0.44)
        self.gun_dir = gun_dir
        u_vec, v_vec, _ = get_orthonormal_basis(gun_dir)
        offsets = [
            (-1, 1, "TopL"), (1, 1, "TopR"),
            (-1, -1, "BotL"), (1, -1, "BotR"),
        ]
        self._last_mantlet_boot_pivots = [
            gun_pivot + u_vec * (sx * spacing_x * 0.5) + v_vec * (sz * spacing_z * 0.5)
            for sx, sz, _ in offsets
        ]
        self._last_mantlet_boot_direction = gun_dir
        eff_boot = self.mantlet_boot
        self._last_mantlet_boots_added = eff_boot
        flak = make_quad_flak_autocannon_assembly(
            f"{self.name}QuadFlak",
            gun_pivot=gun_pivot,
            direction=gun_dir,
            barrel_length=barrel_length,
            caliber_radius=caliber_radius,
            spacing_x=spacing_x,
            spacing_z=spacing_z,
            cradle_mat=self.palette.light_armor,
            barrel_mat=self.palette.gunmetal,
            mag_mat=self.palette.primary_armor,
            accent_mat=self.palette.warning,
            has_dust_boot=eff_boot,
            dust_boot_mat=self.palette.tread,
        )
        self.objects.extend(flak)
        self.preserved_pivots.append(f"{self.name}QuadFlak_ElevationCradle")
        return self

    def add_plasma_mortar(
        self,
        caliber: float = 0.175,
        length: float = 0.95,
        elevation_deg: float = 42.0,
    ) -> "RogueVehicleBuilder":
        gun_pivot = self.turret_center + Vector((0.0, 0.40, 0.02))
        elev_rad = math.radians(elevation_deg)
        gun_dir = Vector((0.0, math.cos(elev_rad), math.sin(elev_rad))).normalized()
        self.gun_pivot = gun_pivot + gun_dir * (length + 0.24)
        self.gun_dir = gun_dir
        self._last_mantlet_boot_pivots = [gun_pivot]
        self._last_mantlet_boot_direction = gun_dir
        eff_boot = self.mantlet_boot
        self._last_mantlet_boots_added = eff_boot
        mortar = make_plasma_mortar_assembly(
            f"{self.name}PlasmaMortar",
            gun_pivot=gun_pivot,
            direction=gun_dir,
            caliber_radius=caliber,
            barrel_length=length,
            ring_count=3,
            armor_mat=self.palette.light_armor,
            barrel_mat=self.palette.gunmetal,
            glow_mat=self.palette.emissive,
            accent_mat=self.palette.warning,
        )
        self.objects.extend(mortar)
        if eff_boot:
            self.objects.extend(make_mantlet_dust_boot(
                f"{self.name}PlasmaMortar_DustBoot",
                pivot=gun_pivot - gun_dir * 0.04,
                direction=gun_dir,
                collar_radius=caliber * 1.8,
                taper_radius=caliber * 1.25,
                length=0.22,
                stages=3,
                material=self.palette.tread,
                accent_mat=self.palette.warning,
            ))
        self.preserved_pivots.append(f"{self.name}PlasmaMortar_MortarTrunnion")
        return self

    def add_flamethrower(
        self,
        caliber: float = 0.088,
        length: float = 1.45,
        elevation_deg: float = 6.0,
    ) -> "RogueVehicleBuilder":
        gun_pivot = self.turret_center + Vector((0.0, 0.42, 0.02))
        elev_rad = math.radians(elevation_deg)
        gun_dir = Vector((0.0, math.cos(elev_rad), math.sin(elev_rad))).normalized()
        self.gun_pivot = gun_pivot + gun_dir * (length + 0.28)
        self.gun_dir = gun_dir
        self._last_mantlet_boot_pivots = [gun_pivot]
        self._last_mantlet_boot_direction = gun_dir
        eff_boot = self.mantlet_boot
        self._last_mantlet_boots_added = eff_boot
        flamer = make_flamethrower_assembly(
            f"{self.name}Flamer",
            gun_pivot=gun_pivot,
            direction=gun_dir,
            barrel_length=length,
            caliber_radius=caliber,
            armor_mat=self.palette.light_armor,
            barrel_mat=self.palette.gunmetal,
            tank_mat=self.palette.warning,
            glow_mat=self.palette.emissive,
            accent_mat=self.palette.warning,
        )
        self.objects.extend(flamer)
        if eff_boot:
            self.objects.extend(make_mantlet_dust_boot(
                f"{self.name}Flamer_DustBoot",
                pivot=gun_pivot - gun_dir * 0.04,
                direction=gun_dir,
                collar_radius=caliber * 1.85,
                taper_radius=caliber * 1.30,
                length=0.22,
                stages=3,
                material=self.palette.tread,
                accent_mat=self.palette.warning,
            ))
        self.preserved_pivots.append(f"{self.name}Flamer_FlamerManifold")
        return self

    def add_emp_emitter(
        self,
        core_radius: float = 0.14,
        length: float = 1.55,
        elevation_deg: float = 5.0,
    ) -> "RogueVehicleBuilder":
        gun_pivot = self.turret_center + Vector((0.0, 0.40, 0.04))
        elev_rad = math.radians(elevation_deg)
        gun_dir = Vector((0.0, math.cos(elev_rad), math.sin(elev_rad))).normalized()
        self.gun_pivot = gun_pivot + gun_dir * (length + 0.28)
        self.gun_dir = gun_dir
        self._last_mantlet_boot_pivots = []
        self._last_mantlet_boot_direction = None
        self._last_mantlet_boots_added = False
        emp = make_emp_emitter_assembly(
            f"{self.name}EMPEmitter",
            gun_pivot=gun_pivot,
            direction=gun_dir,
            emitter_length=length,
            core_radius=core_radius,
            armor_mat=self.palette.primary_armor,
            metal_mat=self.palette.gunmetal,
            glow_mat=self.palette.emissive,
            accent_mat=self.palette.warning,
        )
        self.objects.extend(emp)
        self.preserved_pivots.append(f"{self.name}EMPEmitter_EMPResonator")
        return self

    def add_flight_deck(
        self,
        size: Tuple[float, float, float] = (2.10, 3.20, 0.22),
        location: Tuple[float, float, float] | None = None,
    ) -> "RogueVehicleBuilder":
        if location is None:
            loc = Vector((0.0, 0.0, self.hull_deck_z + size[2] * 0.5 + 0.02))
        else:
            loc = Vector(location)
        deck = make_flight_deck_assembly(
            f"{self.name}FlightDeck",
            size=size,
            location=loc,
            deck_mat=self.palette.primary_armor,
            guideline_mat=self.palette.light_armor,
            glow_mat=self.palette.emissive,
            rail_mat=self.palette.gunmetal,
        )
        self.objects.extend(deck)
        return self

    def add_outriggers(
        self,
        reach_x: float = 0.65,
        count: int = 4,
    ) -> "RogueVehicleBuilder":
        outriggers = make_outrigger_stabilizer_legs(
            f"{self.name}Outrigger",
            hull_length=self.hull_length,
            hull_width=self.hull_width,
            ground_z=self.ground_clearance,
            reach_x=reach_x,
            count=count,
            armor_mat=self.palette.primary_armor,
            piston_mat=self.palette.gunmetal,
            pad_mat=self.palette.light_armor,
        )
        self.objects.extend(outriggers)
        return self

    def add_excavator_drum(
        self,
        width: float = 2.4,
        radius: float = 0.34,
        location: Tuple[float, float, float] | None = None,
    ) -> "RogueVehicleBuilder":
        if location is None:
            front_y = self.hull_length * 0.52 + radius
            loc = Vector((0.0, front_y, self.ground_clearance + radius + 0.02))
        else:
            loc = Vector(location)
        drum = make_heavy_excavator_drum(
            f"{self.name}Excavator",
            location=loc,
            width=width,
            radius=radius,
            drum_mat=self.palette.gunmetal,
            tooth_mat=self.palette.warning,
            arm_mat=self.palette.primary_armor,
        )
        self.objects.extend(drum)
        return self

    def add_whip_antenna(
        self,
        location: Tuple[float, float, float] | None = None,
        length: float = 0.85,
        lean_deg: float = 8.0,
    ) -> "RogueVehicleBuilder":
        """Adds a flexible whip antenna with spring coils and updates antenna_loc."""
        if location is None:
            if hasattr(self, "turret_center") and self.turret_center is not None:
                base_pos = self.turret_center + Vector((0.36, -0.42, 0.22))
            else:
                base_pos = Vector((self.hull_width * 0.28, -self.hull_length * 0.38, self.hull_deck_z + 0.12))
        else:
            base_pos = Vector(location)

        self.antenna_loc = base_pos
        self.objects.extend(make_whip_antenna(
            f"{self.name}Sensor_WhipAntenna",
            base_pos,
            length=length,
            lean_deg=lean_deg,
            base_mat=self.palette.gunmetal,
            mast_mat=self.palette.gunmetal,
            tip_mat=self.palette.warning,
        ))
        self.preserved_pivots.append("Sensor_WhipAntenna")
        return self

    def add_crew_cupola(
        self,
        location: Tuple[float, float, float] | None = None,
        radius: float = 0.30,
        height: float = 0.18,
    ) -> "RogueVehicleBuilder":
        """Adds an armored commander's vision cupola with periscopes and split hatch."""
        if location is None:
            if hasattr(self, "turret_center") and self.turret_center is not None:
                cupola_pos = self.turret_center + Vector((-0.28, -0.22, 0.24))
            else:
                cupola_pos = Vector((0.0, 0.0, self.hull_deck_z + 0.10))
        else:
            cupola_pos = Vector(location)

        self.objects.extend(make_crew_cupola_hatch(
            f"{self.name}Turret_Cupola",
            cupola_pos,
            radius=radius,
            height=height,
            cupola_mat=self.palette.primary_armor,
            lens_mat=self.palette.emissive,
            metal_mat=self.palette.gunmetal,
        ))
        return self

    def add_rivets_and_welds(
        self,
        start_point: Tuple[float, float, float] | None = None,
        end_point: Tuple[float, float, float] | None = None,
        count: int = 6,
        rivet_radius: float = 0.016,
    ) -> "RogueVehicleBuilder":
        """Adds a row of perimeter rivets and beveled weld seams along armor plate edges."""
        if start_point is None or end_point is None:
            hy = (self.hull_length * 0.45) if hasattr(self, "hull_length") else 1.2
            hx = (self.hull_width * 0.42) if hasattr(self, "hull_width") else 0.95
            hz = (self.hull_deck_z + 0.02) if hasattr(self, "hull_deck_z") else 0.88
            start_point = (-hx, hy, hz)
            end_point = (hx, hy, hz)
        self.objects.extend(make_rivets_and_weld_seams(
            f"{self.name}Armor_Greeble",
            start_point,
            end_point,
            count=count,
            rivet_radius=rivet_radius,
            metal_mat=self.palette.gunmetal,
        ))
        return self

    def add_track_shoes(
        self,
        center: Tuple[float, float, float] | None = None,
        length: float | None = None,
        width: float = 0.36,
        shoe_count: int = 8,
    ) -> "RogueVehicleBuilder":
        """Adds discrete geometric track shoe links along the ground contact run."""
        t_len = length if length is not None else ((self.hull_length * 0.90) if hasattr(self, "hull_length") else 2.8)
        if center is None:
            hw = (self.hull_width * 0.48) if hasattr(self, "hull_width") else 1.15
            for side in (-1, 1):
                side_center = (side * hw, 0.0, self.ground_clearance)
                stag = "L" if side < 0 else "R"
                self.objects.extend(make_articulated_track_shoes(
                    f"{self.name}Mobility_TrackShoe_{stag}",
                    side_center,
                    length=t_len,
                    width=width,
                    ground_z=self.ground_clearance,
                    shoe_count=shoe_count,
                    shoe_mat=self.palette.tread,
                    pin_mat=self.palette.gunmetal,
                ))
            return self
        else:
            self.objects.extend(make_articulated_track_shoes(
                f"{self.name}Mobility_TrackShoe",
                center,
                length=t_len,
                width=width,
                ground_z=self.ground_clearance,
                shoe_count=shoe_count,
                shoe_mat=self.palette.tread,
                pin_mat=self.palette.gunmetal,
            ))
            return self

    def add_hydraulic_hoses(
        self,
        radius: float = 0.024,
        sag: float = 0.14,
        corrugated: bool = True,
    ) -> "RogueVehicleBuilder":
        """Adds bilateral catenary hydraulic hoses and braided power conduits between chassis/pelvis and turret/limbs."""
        hw = (self.hull_width * 0.42) if hasattr(self, "hull_width") else 0.90
        hl = (self.hull_length * 0.34) if hasattr(self, "hull_length") else 1.15
        hz = self.hull_deck_z if hasattr(self, "hull_deck_z") else 0.85

        if self.archetype in ("walker", "quad_walker", "reverse_walker", "heavy_crane_mech", "plasma_mortar_strider"):
            # Connect pelvis underside to upper thigh hydraulic manifolds on left & right
            pelvis_z = hz - 0.10
            thigh_z = max(0.45, pelvis_z - 0.38)
            for side, stag in ((-1, "L"), (1, "R")):
                p0 = (side * (hw * 0.55), -0.12, pelvis_z)
                p1 = (side * (hw * 0.92), 0.10, thigh_z)
                self.objects.extend(make_catenary_cable_assembly(
                    f"{self.name}Leg_{stag}_HydraulicHose",
                    start_point=p0,
                    end_point=p1,
                    radius=radius,
                    sag=sag,
                    segments=10,
                    corrugated=corrugated,
                    hose_mat=self.palette.tread,
                    coupler_mat=self.palette.gunmetal,
                    accent_mat=self.palette.warning,
                ))
        else:
            # Connect rear engine power pack to mid-hull/turret ring manifold
            tc_z = self.turret_center.z if (hasattr(self, "turret_center") and self.turret_center is not None) else (hz + 0.16)
            tc_y = self.turret_center.y if (hasattr(self, "turret_center") and self.turret_center is not None) else 0.0
            for side, stag in ((-1, "L"), (1, "R")):
                p0 = (side * (hw * 0.72), -hl * 0.85, hz + 0.06)
                p1 = (side * (hw * 0.56), tc_y - 0.18, tc_z - 0.04)
                self.objects.extend(make_catenary_cable_assembly(
                    f"{self.name}Hull_{stag}_PowerHose",
                    start_point=p0,
                    end_point=p1,
                    radius=radius,
                    sag=sag * 0.85,
                    segments=10,
                    corrugated=corrugated,
                    hose_mat=self.palette.tread,
                    coupler_mat=self.palette.gunmetal,
                    accent_mat=self.palette.warning,
                ))
        return self

    def add_hull_greebles(
        self,
        include_lugs: bool = True,
        include_vents: bool = True,
    ) -> "RogueVehicleBuilder":
        """Adds BMesh-inset maintenance hatches, slotted micro-louver vents, and corner forged lifting lugs."""
        hw = self.hull_width if hasattr(self, "hull_width") else 2.2
        hl = self.hull_length if hasattr(self, "hull_length") else 3.4
        hz = self.hull_deck_z if hasattr(self, "hull_deck_z") else 0.88
        center_y = -hl * 0.12 if (hasattr(self, "turret_center") and self.turret_center is not None) else 0.0
        self.objects.extend(make_bmesh_hull_greebles(
            f"{self.name}Hull_Greeble",
            deck_center=(0.0, center_y, 0.0),
            hull_width=hw,
            hull_length=hl,
            deck_z=hz,
            armor_mat=self.palette.light_armor,
            metal_mat=self.palette.gunmetal,
            accent_mat=self.palette.warning,
            include_lugs=include_lugs,
            include_vents=include_vents,
        ))
        return self

    def add_tactical_markings(
        self,
        stripe_count: int = 2,
        include_zenith_id: bool = True,
    ) -> "RogueVehicleBuilder":
        """Projects floating tactical chevron decals and zenith identification plates onto armor via BVHTree."""
        self.objects.extend(make_tactical_decal_plates(
            f"{self.name}Decal",
            target_objects=list(self.objects),
            decal_mat=self.palette.warning,
            secondary_mat=self.palette.light_armor,
            stripe_count=stripe_count,
            include_zenith_id=include_zenith_id,
        ))
        return self

    def add_hydraulic_pistons(
        self,
        outer_radius: float = 0.044,
        inner_radius: float = 0.026,
        stroke_ratio: float = 0.54,
    ) -> "RogueVehicleBuilder":
        """Adds dual-stage telescopic hydraulic actuator rams across walker knees/ankles, gun elevation cradles, or outriggers."""
        hw = (self.hull_width * 0.42) if hasattr(self, "hull_width") else 0.95
        hz = self.hull_deck_z if hasattr(self, "hull_deck_z") else 0.88
        tc = self.turret_center if (hasattr(self, "turret_center") and self.turret_center is not None) else Vector((0.0, 0.0, hz + 0.25))

        if self.is_walker and self.leg_type == "quad" and self.quad_hips and self.quad_knees:
            for tag, hip_v in self.quad_hips.items():
                knee_v = self.quad_knees.get(tag, hip_v + Vector((0.35, 0.25, -0.25)))
                p0 = hip_v + Vector((0.0, 0.0, -0.14))
                p1 = hip_v.lerp(knee_v, 0.72) + Vector((0.0, 0.0, -0.05))
                self.objects.extend(make_telescopic_piston_assembly(
                    f"{self.name}Piston_Quad_{tag}",
                    start_point=p0,
                    end_point=p1,
                    outer_radius=outer_radius,
                    inner_radius=inner_radius,
                    stroke_ratio=stroke_ratio,
                    housing_mat=self.palette.gunmetal,
                    rod_mat=self.palette.wheel_rim,
                    seal_mat=self.palette.tread,
                    accent_mat=self.palette.warning,
                ))
        elif self.is_walker and self.hip_loc_l is not None and self.knee_loc_l is not None:
            for stag, hip_v, knee_v in (
                ("L", self.hip_loc_l, self.knee_loc_l),
                ("R", self.hip_loc_r or self.hip_loc_l, self.knee_loc_r or self.knee_loc_l),
            ):
                p0 = hip_v + Vector((0.0, -0.16, -0.12))
                p1 = knee_v + Vector((0.0, -0.10, 0.08))
                self.objects.extend(make_telescopic_piston_assembly(
                    f"{self.name}Piston_Leg_{stag}",
                    start_point=p0,
                    end_point=p1,
                    outer_radius=outer_radius * 1.1,
                    inner_radius=inner_radius * 1.1,
                    stroke_ratio=stroke_ratio,
                    housing_mat=self.palette.gunmetal,
                    rod_mat=self.palette.wheel_rim,
                    seal_mat=self.palette.tread,
                    accent_mat=self.palette.warning,
                ))
        else:
            # Bilateral gun elevation / recoil actuator rams flanking turret breech / cradle
            for side, stag in ((-1, "L"), (1, "R")):
                p0 = tc + Vector((side * min(0.58, hw * 0.56), -0.12, -0.10))
                p1 = tc + Vector((side * min(0.46, hw * 0.44), 0.52, 0.14))
                self.objects.extend(make_telescopic_piston_assembly(
                    f"{self.name}Piston_Cradle_{stag}",
                    start_point=p0,
                    end_point=p1,
                    outer_radius=outer_radius,
                    inner_radius=inner_radius,
                    stroke_ratio=stroke_ratio,
                    housing_mat=self.palette.gunmetal,
                    rod_mat=self.palette.wheel_rim,
                    seal_mat=self.palette.tread,
                    accent_mat=self.palette.warning,
                ))
        return self

    def add_ammo_belt_chutes(
        self,
        chute_width: float = 0.14,
        chute_thickness: float = 0.048,
        sag: float = 0.11,
    ) -> "RogueVehicleBuilder":
        """Adds articulated metallic ammunition feed chutes from ammo hoppers into weapon receivers."""
        hw = (self.hull_width * 0.42) if hasattr(self, "hull_width") else 0.90
        hz = self.hull_deck_z if hasattr(self, "hull_deck_z") else 0.88
        tc = self.turret_center if (hasattr(self, "turret_center") and self.turret_center is not None) else Vector((0.0, 0.0, hz + 0.25))

        for side, stag in ((-1, "L"), (1, "R")):
            p0 = tc + Vector((side * min(0.68, hw * 0.65), -0.36, -0.06))
            p1 = tc + Vector((side * min(0.42, hw * 0.40), 0.28, 0.08))
            self.objects.extend(make_ammo_belt_chute_assembly(
                f"{self.name}AmmoChute_{stag}",
                start_point=p0,
                end_point=p1,
                chute_width=chute_width,
                chute_thickness=chute_thickness,
                sag=sag,
                lateral_bow=0.07,
                link_count=9,
                hopper_mat=self.palette.primary_armor,
                chute_mat=self.palette.gunmetal,
                brass_mat=self.palette.warning,
            ))
        return self

    def add_aps_hardkill_suite(
        self,
        include_lwr: bool = True,
    ) -> "RogueVehicleBuilder":
        """Adds turret-mounted Active Protection System (APS) hard-kill launchers, 4-quadrant AESA radars, and LWR optical prism."""
        hz = self.hull_deck_z if hasattr(self, "hull_deck_z") else 0.88
        tc = self.turret_center if (hasattr(self, "turret_center") and self.turret_center is not None) else Vector((0.0, 0.0, hz + 0.25))
        tw = min(1.55, (self.hull_width * 0.62) if hasattr(self, "hull_width") else 1.45)
        tl = min(1.75, (self.hull_length * 0.52) if hasattr(self, "hull_length") else 1.65)
        self.objects.extend(make_aps_hardkill_suite(
            f"{self.name}APS",
            turret_center=tc,
            turret_width=tw,
            turret_length=tl,
            turret_height=0.45,
            armor_mat=self.palette.light_armor,
            metal_mat=self.palette.gunmetal,
            radar_mat=self.palette.warning,
            emissive_mat=self.palette.emissive,
            include_lwr=include_lwr,
        ))
        return self

    def add_tactical_stowage(self) -> "RogueVehicleBuilder":
        """Adds external turret bustle cargo rack, rolled camo tarp, Jerry cans, and glacis spare track-link brackets."""
        hw = self.hull_width if hasattr(self, "hull_width") else 2.4
        hl = self.hull_length if hasattr(self, "hull_length") else 3.0
        hz = self.hull_deck_z if hasattr(self, "hull_deck_z") else 0.88
        tc = self.turret_center if (hasattr(self, "turret_center") and self.turret_center is not None) else Vector((0.0, -0.15, hz + 0.22))
        self.objects.extend(make_tactical_stowage_kit(
            f"{self.name}Stowage",
            hull_length=hl,
            hull_width=hw,
            deck_z=hz,
            turret_center=tc,
            turret_length=min(1.65, hl * 0.52),
            turret_width=min(1.45, hw * 0.60),
            armor_mat=self.palette.primary_armor,
            metal_mat=self.palette.gunmetal,
            tarp_mat=self.palette.light_armor,
            tread_mat=self.palette.tread,
            accent_mat=self.palette.warning,
        ))
        return self

    def add_slat_armor(self, slat_count: int = 5) -> "RogueVehicleBuilder":
        """Adds anti-HEAT standoff slat/cage bar armor grids around rear hull flanks and turret bustle."""
        hw = self.hull_width if hasattr(self, "hull_width") else 2.4
        hl = self.hull_length if hasattr(self, "hull_length") else 3.0
        hz = self.hull_deck_z if hasattr(self, "hull_deck_z") else 0.88
        tc = self.turret_center if (hasattr(self, "turret_center") and self.turret_center is not None) else None
        self.objects.extend(make_slat_cage_armor_assembly(
            f"{self.name}SlatArmor",
            hull_length=hl,
            hull_width=hw,
            deck_z=hz,
            turret_center=tc,
            turret_length=min(1.65, hl * 0.52),
            turret_width=min(1.45, hw * 0.60),
            slat_count=slat_count,
            metal_mat=self.palette.gunmetal,
            armor_mat=self.palette.primary_armor,
            accent_mat=self.palette.warning,
        ))
        return self

    def add_era_bricks(self, cols: int = 5, rows: int = 2) -> "RogueVehicleBuilder":
        """Adds staggered Explosive Reactive Armor (ERA) wedge bricks across glacis and turret cheeks."""
        hw = self.hull_width if hasattr(self, "hull_width") else 2.4
        hl = self.hull_length if hasattr(self, "hull_length") else 3.0
        hz = self.hull_deck_z if hasattr(self, "hull_deck_z") else 0.88
        tc = self.turret_center if (hasattr(self, "turret_center") and self.turret_center is not None) else None
        self.objects.extend(make_era_reactive_bricks_assembly(
            f"{self.name}ERABricks",
            hull_length=hl,
            hull_width=hw,
            deck_z=hz,
            turret_center=tc,
            turret_length=min(1.65, hl * 0.52),
            turret_width=min(1.45, hw * 0.60),
            cols=cols,
            rows=rows,
            brick_mat=self.palette.light_armor,
            bolt_mat=self.palette.gunmetal,
            accent_mat=self.palette.warning,
        ))
        return self

    def add_camo_netting(self) -> "RogueVehicleBuilder":
        """Adds procedural 3D noise-displaced draped camouflage netting canopy and barrel ghillie wrap."""
        hw = self.hull_width if hasattr(self, "hull_width") else 2.4
        hl = self.hull_length if hasattr(self, "hull_length") else 3.0
        hz = self.hull_deck_z if hasattr(self, "hull_deck_z") else 0.88
        tc = self.turret_center if (hasattr(self, "turret_center") and self.turret_center is not None) else None
        gp = self.gun_pivot if hasattr(self, "gun_pivot") else None
        gd = self.gun_dir if hasattr(self, "gun_dir") else None
        self.objects.extend(make_camo_netting_assembly(
            f"{self.name}CamoNet",
            hull_length=hl,
            hull_width=hw,
            deck_z=hz,
            turret_center=tc,
            turret_length=min(1.65, hl * 0.52),
            turret_width=min(1.45, hw * 0.60),
            gun_pivot=gp,
            gun_dir=gd,
            net_mat=self.palette.light_armor,
            strap_mat=self.palette.tread,
            pole_mat=self.palette.gunmetal,
        ))
        return self

    def add_thermal_radiators(self, fin_count: int = 6) -> "RogueVehicleBuilder":
        """Adds armored heat-exchanger radiator banks with glowing emissive plasma/coolant fins."""
        hw = self.hull_width if hasattr(self, "hull_width") else 2.4
        hl = self.hull_length if hasattr(self, "hull_length") else 3.0
        hz = self.hull_deck_z if hasattr(self, "hull_deck_z") else 0.88
        self.objects.extend(make_thermal_radiator_assembly(
            f"{self.name}Radiator",
            hull_length=hl,
            hull_width=hw,
            deck_z=hz,
            fin_count=fin_count,
            housing_mat=self.palette.primary_armor,
            fin_mat=self.palette.gunmetal,
            glow_mat=self.palette.emissive,
            pipe_mat=self.palette.wheel_rim,
        ))
        return self

    def add_rws_mount(
        self,
        scale: float = 1.0,
        yaw_deg: float = 0.0,
        pitch_deg: float = 0.0,
        location: Tuple[float, float, float] | Vector | None = None,
    ) -> "RogueVehicleBuilder":
        """Adds a roof-mounted independent Remote Weapon Station (RWS/CROWS) with stabilized EO/IR optical sensor ball, traversing weapon yoke, armored ammo box, and rapid-fire autocannon/HMG."""
        if location is not None:
            rws_loc = Vector(location)
        elif hasattr(self, "turret_center") and self.turret_center:
            tc = Vector(self.turret_center)
            th = self.turret_height if hasattr(self, "turret_height") else 0.45
            tw = self.turret_width if hasattr(self, "turret_width") else 1.40
            tl = self.turret_length if hasattr(self, "turret_length") else 1.60
            rws_loc = tc + Vector((tw * 0.28, -tl * 0.18, th * 0.52))
        else:
            hl = self.hull_length if hasattr(self, "hull_length") else 3.0
            hw = self.hull_width if hasattr(self, "hull_width") else 2.4
            hz = self.hull_deck_z if hasattr(self, "hull_deck_z") else 0.88
            rws_loc = Vector((hw * 0.25, hl * 0.12, hz))

        self.objects.extend(make_remote_weapon_station_assembly(
            f"{self.name}RWS",
            location=rws_loc,
            scale=scale,
            yaw_deg=yaw_deg,
            pitch_deg=pitch_deg,
            housing_mat=self.palette.light_armor,
            gun_mat=self.palette.gunmetal,
            optic_mat=self.palette.emissive,
            ammo_mat=self.palette.primary_armor,
        ))
        return self

    def add_drone_dock(
        self,
        scale: float = 1.0,
        location: Tuple[float, float, float] | Vector | None = None,
    ) -> "RogueVehicleBuilder":
        """Adds a recessed roof/deck landing cradle with magnetic charging guide rails, alignment runway glow LEDs, and a docked parasitic reconnaissance/escort drone."""
        if location is not None:
            dock_loc = Vector(location)
        else:
            hl = self.hull_length if hasattr(self, "hull_length") else 3.0
            hz = self.hull_deck_z if hasattr(self, "hull_deck_z") else 0.88
            dock_loc = Vector((0.0, -hl * 0.24, hz))

        self.objects.extend(make_drone_docking_bay_assembly(
            f"{self.name}DroneBay",
            location=dock_loc,
            scale=scale,
            bay_mat=self.palette.primary_armor,
            frame_mat=self.palette.gunmetal,
            drone_mat=self.palette.light_armor,
            glow_mat=self.palette.emissive,
        ))
        return self

    def add_recoil_spade(
        self,
        width: float | None = None,
        depth: float = 0.85,
        deployed: bool = True,
        location: Tuple[float, float, float] | Vector | None = None,
    ) -> "RogueVehicleBuilder":
        """Adds rear-mounted heavy hydraulic recoil spades / earth anchors that bite into the terrain plane (Z ≈ 0.02) to absorb massive kinetic artillery recoil."""
        hl = self.hull_length if hasattr(self, "hull_length") else 3.0
        hw = self.hull_width if hasattr(self, "hull_width") else 2.4
        hz = self.hull_deck_z if hasattr(self, "hull_deck_z") else 0.88
        spade_w = width if width is not None else hw * 0.82
        spade_loc = Vector(location) if location is not None else Vector((0.0, -hl * 0.52, hz * 0.45))

        self.objects.extend(make_recoil_earth_spade_assembly(
            f"{self.name}Spade",
            location=spade_loc,
            width=spade_w,
            depth=depth,
            deployed=deployed,
            blade_mat=self.palette.primary_armor,
            strut_mat=self.palette.gunmetal,
            ram_mat=self.palette.primary_armor,
            rod_mat=self.palette.wheel_rim,
        ))
        return self

    def add_barrel_shroud(
        self,
        shroud_length: float = 1.4,
        shroud_radius: float = 0.16,
    ) -> "RogueVehicleBuilder":
        """Adds a ventilated thermal barrel jacket with consolidated BMesh cooling perforation slats, reinforced clamping collars, and an aggressive multi-baffle muzzle brake."""
        gp = self.gun_pivot if hasattr(self, "gun_pivot") and self.gun_pivot else Vector((0.0, 1.2, 0.9))
        gd = self.gun_dir if hasattr(self, "gun_dir") and self.gun_dir else Vector((0.0, 1.0, 0.0))
        b_len = self.barrel_length if hasattr(self, "barrel_length") else 2.4
        b_rad = self.barrel_radius if hasattr(self, "barrel_radius") else 0.10

        self.objects.extend(make_perforated_barrel_shroud_assembly(
            f"{self.name}BarrelShroud",
            gun_pivot=gp,
            gun_dir=gd,
            barrel_length=b_len,
            barrel_radius=b_rad,
            shroud_length=shroud_length,
            shroud_radius=shroud_radius,
            shroud_mat=self.palette.gunmetal,
            collar_mat=self.palette.light_armor,
            brake_mat=self.palette.gunmetal,
        ))
        return self

    def add_nanite_projector(
        self,
        head_radius: float = 0.16,
        length: float = 1.2,
        elevation_deg: float = 6.0,
    ) -> "RogueVehicleBuilder":
        """Adds an articulated medical/repair nanite projector assembly with pressurized storage canisters, collimating lens ring, and multi-aperture dispersal nozzle."""
        gun_pivot = self.turret_center + Vector((0.0, 0.45, 0.04))
        elev_rad = math.radians(elevation_deg)
        gun_dir = Vector((0.0, math.cos(elev_rad), math.sin(elev_rad))).normalized()
        self.gun_pivot = gun_pivot + gun_dir * (length + 0.18)
        self.gun_dir = gun_dir
        self._last_mantlet_boot_pivots = []
        self._last_mantlet_boot_direction = None
        self._last_mantlet_boots_added = False
        projector = make_nanite_projector_assembly(
            f"{self.name}Nanite",
            gun_pivot=gun_pivot,
            direction=gun_dir,
            head_radius=head_radius,
            length=length,
            cradle_mat=self.palette.primary_armor,
            nozzle_mat=self.palette.gunmetal,
            glow_mat=self.palette.emissive,
            canister_mat=self.palette.light_armor,
            conduit_mat=self.palette.tread,
        )
        self.objects.extend(projector)
        self.preserved_pivots.append(f"{self.name}Nanite_Nanite_Cradle")
        return self

    def add_gravity_cannon(
        self,
        barrel_length: float = 2.2,
        bore_radius: float = 0.14,
        ring_count: int = 4,
        elevation_deg: float = 6.0,
    ) -> "RogueVehicleBuilder":
        """Adds a super-heavy gravitational singularity cannon / kinetic mass driver with magnetic accelerator ring stages, glowing grav-core chamber, and longitudinal stabilization rails."""
        gun_pivot = self.turret_center + Vector((0.0, 0.45, 0.04))
        elev_rad = math.radians(elevation_deg)
        gun_dir = Vector((0.0, math.cos(elev_rad), math.sin(elev_rad))).normalized()
        self.gun_pivot = gun_pivot + gun_dir * (barrel_length + 0.22)
        self.gun_dir = gun_dir
        self._last_mantlet_boot_pivots = []
        self._last_mantlet_boot_direction = None
        self._last_mantlet_boots_added = False
        cannon = make_gravity_cannon_assembly(
            f"{self.name}GravCannon",
            gun_pivot=gun_pivot,
            direction=gun_dir,
            barrel_length=barrel_length,
            bore_radius=bore_radius,
            ring_count=ring_count,
            mount_mat=self.palette.primary_armor,
            barrel_mat=self.palette.gunmetal,
            core_mat=self.palette.emissive,
            accent_mat=self.palette.warning,
        )
        self.objects.extend(cannon)
        self.preserved_pivots.append(f"{self.name}GravCannon_Gravity_BreechMount")
        return self

    def add_nanite_dispenser(
        self,
        scale: float = 1.0,
        spacing_x: float | None = None,
        location: Tuple[float, float, float] | Vector | None = None,
    ) -> "RogueVehicleBuilder":
        """Adds bilateral armored field nanite repair dispenser pods with glowing inspection sight glasses, pressure manifolds, and atomizing spray heads."""
        hw = self.hull_width if hasattr(self, "hull_width") else 2.4
        hz = self.hull_deck_z if hasattr(self, "hull_deck_z") else 0.88
        hl = self.hull_length if hasattr(self, "hull_length") else 3.0
        sp_x = spacing_x if spacing_x is not None else hw * 0.95
        disp_loc = Vector(location) if location is not None else Vector((0.0, -hl * 0.15, hz * 0.65))

        self.objects.extend(make_nanite_repair_dispenser_assembly(
            f"{self.name}NaniteDisp",
            location=disp_loc,
            scale=scale,
            spacing_x=sp_x,
            canister_mat=self.palette.primary_armor,
            frame_mat=self.palette.gunmetal,
            glow_mat=self.palette.emissive,
            hose_mat=self.palette.tread,
        ))
        return self

    def add_grav_dampener(
        self,
        radius: float = 0.42,
        height: float = 0.28,
        location: Tuple[float, float, float] | Vector | None = None,
    ) -> "RogueVehicleBuilder":
        """Adds a chassis/dorsal gyroscopic gravitational anchor and inertial dampener hub with counter-rotating magnetic rings, glowing singularity sphere, and structural tie-down braces."""
        if location is not None:
            hub_loc = Vector(location)
        else:
            hl = self.hull_length if hasattr(self, "hull_length") else 3.0
            hz = self.hull_deck_z if hasattr(self, "hull_deck_z") else 0.88
            hub_loc = Vector((0.0, -hl * 0.28, hz))

        self.objects.extend(make_grav_dampener_hub_assembly(
            f"{self.name}GravHub",
            location=hub_loc,
            radius=radius,
            height=height,
            housing_mat=self.palette.primary_armor,
            rotor_mat=self.palette.gunmetal,
            core_mat=self.palette.emissive,
            brace_mat=self.palette.light_armor,
        ))
        return self

    def add_comms_mast(
        self,
        height: float = 1.6,
        location: Tuple[float, float, float] | Vector | None = None,
    ) -> "RogueVehicleBuilder":
        """Adds a heavy-duty telescopic communications & EW jamming mast with planar blade antenna fins, cross-dipole jamming horns, satcom dome, and glowing apex beacon LEDs."""
        if location is not None:
            mast_loc = Vector(location)
        else:
            hw = self.hull_width if hasattr(self, "hull_width") else 2.4
            hl = self.hull_length if hasattr(self, "hull_length") else 3.0
            hz = self.hull_deck_z if hasattr(self, "hull_deck_z") else 0.88
            mast_loc = Vector((-hw * 0.25, -hl * 0.28, hz))

        self.objects.extend(make_comms_ew_mast_assembly(
            f"{self.name}CommsMast",
            location=mast_loc,
            height=height,
            base_mat=self.palette.primary_armor,
            mast_mat=self.palette.gunmetal,
            radome_mat=self.palette.light_armor,
            glow_mat=self.palette.emissive,
        ))
        return self

    def add_ram_prow(
        self,
        width: float | None = None,
        height: float = 0.55,
        forward_reach: float = 0.45,
        location: Tuple[float, float, float] | Vector | None = None,
    ) -> "RogueVehicleBuilder":
        """Adds a heavy angled V-shaped kinetic battering ram prow / cowcatcher bumper with structural reinforcing gussets, towing shackles, and diagonal hazard chevron plates."""
        hw = self.hull_width if hasattr(self, "hull_width") else 2.4
        hl = self.hull_length if hasattr(self, "hull_length") else 3.0
        hz = self.ground_clearance if hasattr(self, "ground_clearance") else 0.08
        prow_w = width if width is not None else hw * 1.05
        prow_loc = Vector(location) if location is not None else Vector((0.0, hl * 0.52, hz + height * 0.45))

        self.objects.extend(make_reinforced_ram_prow_assembly(
            f"{self.name}RamProw",
            location=prow_loc,
            width=prow_w,
            height=height,
            forward_reach=forward_reach,
            prow_mat=self.palette.primary_armor,
            bracket_mat=self.palette.gunmetal,
            accent_mat=self.palette.warning,
        ))
        return self

    def add_cast_pelvis(
        self,
        location: Tuple[float, float, float] | Vector = (0.0, 0.0, 1.40),
        width: float = 0.95,
        depth: float = 0.68,
        height: float = 0.45,
    ) -> "RogueVehicleBuilder":
        """Adds an articulated heavy cast pelvis gimbal assembly with bilateral hip clevis actuator knuckles and lower groin ballistic deflector plate."""
        self.objects.extend(make_cast_pelvis_assembly(
            f"{self.name}Armor_Pelvis",
            location=location,
            width=width,
            depth=depth,
            height=height,
            core_mat=self.palette.gunmetal,
            armor_mat=self.palette.primary_armor,
            accent_mat=self.palette.warning,
        ))
        return self

    def add_mech_torso(
        self,
        location: Tuple[float, float, float] | Vector = (0.0, 0.0, 1.95),
        width: float = 1.15,
        length: float = 0.85,
        height: float = 0.82,
    ) -> "RogueVehicleBuilder":
        """Adds a faceted monocoque mech torso with sloped chest glacis, armored visor cowl, shoulder pauldron cuffs, and dorsal reactor bay."""
        loc_v = Vector(location)
        # Heavy yaw bearing race collar physically bridging pelvis to torso
        self.objects.append(make_cylinder(
            f"{self.name}Armor_TorsoYawRace",
            width * 0.38, 0.12,
            loc_v - Vector((0.0, 0.0, height * 0.48)),
            self.palette.gunmetal,
            vertices=14, bevel=0.015
        ))
        self.objects.extend(make_articulated_mech_torso(
            f"{self.name}Armor_Torso",
            location=location,
            width=width,
            length=length,
            height=height,
            primary_mat=self.palette.primary_armor,
            secondary_mat=self.palette.light_armor,
            gunmetal_mat=self.palette.gunmetal,
            emissive_mat=self.palette.emissive,
        ))
        return self

    def add_wheel_fenders(
        self,
        locations: Sequence[Tuple[float, float, float] | Vector],
        radius: float = 0.40,
        width: float = 0.28,
    ) -> "RogueVehicleBuilder":
        """Adds contoured hard-surface wheel arch fenders over all specified wheel positions."""
        for idx, wloc in enumerate(locations, start=1):
            w_vec = Vector(wloc)
            side = 1 if w_vec.x >= 0 else -1
            stag = "R" if side > 0 else "L"
            self.objects.extend(make_sculpted_wheel_fender_assembly(
                f"{self.name}Fender_{idx}_{stag}",
                location=w_vec,
                radius=radius,
                width=width,
                side=side,
                fender_mat=self.palette.primary_armor,
                skirt_mat=self.palette.light_armor,
            ))
        return self

    def add_weld_seams(
        self,
        seams: Sequence[Tuple[Tuple[float, float, float] | Vector, Tuple[float, float, float] | Vector]],
        radius: float = 0.016,
        material: bpy.types.Material | None = None,
        prefix: str = "WeldSeam",
    ) -> "RogueVehicleBuilder":
        """Adds authentic scalloped weld seams along specified plate seam segment tuples."""
        mat = material or self.palette.gunmetal
        for idx, (p1, p2) in enumerate(seams, start=1):
            weld_obj = make_weld_seam_bead(
                f"{self.name}_{prefix}_{idx:02d}",
                p1, p2,
                radius=radius,
                material=mat,
            )
            if weld_obj:
                self.objects.append(weld_obj)
        return self

    def add_deck_edge_weld_seams(self, radius: float = 0.014) -> "RogueVehicleBuilder":
        """Adds scalloped weld beads along both upper deck-to-side-plate edges of a sloped hull."""
        hw = self.hull_width * 0.458
        hl = self.hull_length
        dz = self.hull_deck_z - 0.02
        seams = [
            ((side * hw, -hl * 0.40, dz), (side * hw, hl * 0.12, dz))
            for side in (-1, 1)
        ]
        return self.add_weld_seams(seams, radius=radius, prefix="DeckEdgeWeld")

    def add_glacis_weld_seams(self, radius: float = 0.016) -> "RogueVehicleBuilder":
        """Adds scalloped weld seams along both glacis-to-side-plate junctions, scaled to the current hull."""
        hw = self.hull_width * 0.458
        hl = self.hull_length
        dz = self.hull_deck_z
        seams = []
        for side in (-1, 1):
            seams.append((
                (side * hw, hl * 0.15, dz - 0.23 * dz / 0.88),
                (side * hw * 0.86, hl * 0.48, dz - 0.53 * dz / 0.88),
            ))
        return self.add_weld_seams(seams, radius=radius, prefix="GlacisWeld")

    def add_splash_rib(
        self,
        location: Tuple[float, float, float] | Vector | None = None,
        width: float | None = None,
    ) -> "RogueVehicleBuilder":
        if location is None:
            gy = self.hull_length * 0.28
            gz = self.hull_deck_z + 0.02
            location = (0.0, gy, gz)
        eff_w = width if width is not None else (self.hull_width * 0.55)
        objs = make_glacis_splash_rib(
            f"{self.name}Armor",
            location=location,
            width=eff_w,
            material=self.palette.primary_armor,
        )
        self.objects.extend(objs)
        return self

    def add_bustle_basket(
        self,
        location: Tuple[float, float, float] | Vector | None = None,
        size: Tuple[float, float, float] | None = None,
    ) -> "RogueVehicleBuilder":
        if location is None:
            if hasattr(self, "turret_center") and self.turret_center is not None:
                location = self.turret_center + Vector((0.0, -self.hull_length * 0.22, 0.04))
            else:
                location = Vector((0.0, -self.hull_length * 0.44, self.hull_deck_z + 0.18))
        eff_size = size if size is not None else (self.hull_width * 0.44, 0.38, 0.22)
        objs = make_bustle_stowage_basket(
            f"{self.name}Turret_Bustle",
            location=location,
            size=eff_size,
            frame_mat=self.palette.gunmetal,
            mesh_mat=self.palette.light_armor,
        )
        self.objects.extend(objs)
        return self

    def add_commander_sight(
        self,
        location: Tuple[float, float, float] | Vector | None = None,
    ) -> "RogueVehicleBuilder":
        if location is None:
            if hasattr(self, "turret_center") and self.turret_center is not None:
                location = self.turret_center + Vector((0.32, 0.15, 0.28))
            else:
                location = Vector((self.hull_width * 0.24, self.hull_length * 0.18, self.hull_deck_z + 0.15))
        objs = make_panoramic_commander_sight(
            f"{self.name}Sensor_CITV",
            location=location,
            armor_mat=self.palette.light_armor,
            lens_mat=self.palette.emissive,
            metal_mat=self.palette.gunmetal,
        )
        self.objects.extend(objs)
        return self

    def add_recovery_winch(
        self,
        location: Tuple[float, float, float] | Vector | None = None,
    ) -> "RogueVehicleBuilder":
        if location is None:
            front_y = self.hull_length * 0.50
            front_z = self.ground_clearance + 0.28
            location = (0.0, front_y, front_z)
        objs = make_recovery_winch_assembly(
            f"{self.name}Winch",
            location=location,
            housing_mat=self.palette.primary_armor,
            drum_mat=self.palette.gunmetal,
            roller_mat=self.palette.gunmetal,
        )
        self.objects.extend(objs)
        return self

    def add_exhaust_deflector(
        self,
        location: Tuple[float, float, float] | Vector | None = None,
        size: Tuple[float, float, float] = (0.42, 0.32, 0.14),
    ) -> "RogueVehicleBuilder":
        if location is None:
            location = (0.0, -self.hull_length * 0.38, self.hull_deck_z + size[2] * 0.5)
        objs = make_exhaust_deflector_cowl(
            f"{self.name}ExhaustCowl",
            location=location,
            size=size,
            frame_mat=self.palette.gunmetal,
            slat_mat=self.palette.primary_armor,
        )
        self.objects.extend(objs)
        return self

    def add_interlocking_gussets(
        self,
        locations: Sequence[Tuple[float, float, float] | Vector] | None = None,
        size: Tuple[float, float, float] = (0.05, 0.22, 0.22),
        rotation: Tuple[float, float, float] = (0.0, 0.0, 0.0),
    ) -> "RogueVehicleBuilder":
        if locations is None:
            deck_y = -self.hull_length * 0.22
            deck_z = self.hull_deck_z
            hw = self.hull_width * 0.38
            locations = [(-hw, deck_y, deck_z), (hw, deck_y, deck_z)]
        objs = make_interlocking_armor_gusset(
            f"{self.name}Armor",
            locations=locations,
            size=size,
            material=self.palette.primary_armor,
            rotation=rotation,
        )
        self.objects.extend(objs)
        return self

    def add_towing_shackles(
        self,
        front_spacing_x: float | None = None,
        rear_spacing_x: float | None = None,
    ) -> "RogueVehicleBuilder":
        sx_f = front_spacing_x if front_spacing_x is not None else (self.hull_width * 0.38)
        sx_r = rear_spacing_x if rear_spacing_x is not None else (self.hull_width * 0.38)
        front_y = self.hull_length * 0.48
        rear_y = -self.hull_length * 0.48
        shackle_z = self.ground_clearance + 0.24
        front_locs = [(-sx_f, front_y, shackle_z), (sx_f, front_y, shackle_z)]
        rear_locs = [(-sx_r, rear_y, shackle_z), (sx_r, rear_y, shackle_z)]
        f_objs = make_towing_shackle_assembly(
            f"{self.name}Armor_FrontShackle",
            front_locs,
            material=self.palette.gunmetal,
            pin_material=self.palette.warning,
            direction=(0.0, 1.0, 0.0),
        )
        r_objs = make_towing_shackle_assembly(
            f"{self.name}Armor_RearShackle",
            rear_locs,
            material=self.palette.gunmetal,
            pin_material=self.palette.warning,
            direction=(0.0, -1.0, 0.0),
        )
        self.objects.extend(f_objs)
        self.objects.extend(r_objs)
        return self

    def add_lifting_lugs(
        self,
        corners: Sequence[Tuple[float, float, float] | Vector] | None = None,
    ) -> "RogueVehicleBuilder":
        if corners is None:
            hw = self.hull_width * 0.36
            hl = self.hull_length * 0.32
            hz = self.hull_deck_z + 0.12
            corners = [
                (-hw, hl, hz), (hw, hl, hz),
                (-hw, -hl, hz), (hw, -hl, hz),
            ]
        objs = make_crane_lifting_lugs(
            f"{self.name}Armor",
            corner_locations=corners,
            material=self.palette.gunmetal,
        )
        self.objects.extend(objs)
        return self

    def add_aerodynamic_fuselage(
        self,
        length: float = 2.6,
        width: float = 1.35,
        height: float = 0.58,
        location: Tuple[float, float, float] | Vector = (0.0, 0.10, 0.72),
        with_wing_fairings: bool = True,
        wing_span_x: float = 1.5,
    ) -> "RogueVehicleBuilder":
        objs = make_aerodynamic_fuselage_assembly(
            self.name,
            length=length,
            width=width,
            height=height,
            location=location,
            primary_mat=self.palette.primary_armor,
            canopy_mat=self.palette.light_armor,
            intake_mat=self.palette.gunmetal,
            exhaust_mat=self.palette.warning,
            with_wing_fairings=with_wing_fairings,
            wing_span_x=wing_span_x,
        )
        self.objects.extend(objs)
        return self

    def add_transitional_coaming(
        self,
        prefix: str,
        location: Tuple[float, float, float] | Vector,
        size: Tuple[float, float],
        height: float = 0.045,
    ) -> "RogueVehicleBuilder":
        """Adds a beveled perimeter mounting coaming / weld collar around cabin or equipment bays to bridge 90° intersections."""
        self.objects.append(make_transitional_coaming_collar(
            prefix,
            location=location,
            size=size,
            height=height,
            collar_mat=self.palette.gunmetal,
        ))
        return self


    def add_turret_ring_guard(
        self,
        radius: float | None = None,
        height: float = 0.14,
        thickness: float = 0.075,
    ) -> "RogueVehicleBuilder":
        """Adds a raised circular armored deflector ring guard around the turret base on the hull deck to eliminate shot-traps and physical gaps."""
        if self.turret_center is None:
            return self
        t_radius = radius if radius is not None else 0.65
        ring_loc = Vector((self.turret_center.x, self.turret_center.y, self.hull_deck_z))
        guard_objs = make_turret_ring_guard(
            f"{self.name}TurretRingGuard",
            location=ring_loc,
            radius=t_radius,
            height=height,
            thickness=thickness,
            armor_mat=self.palette.primary_armor,
            gunmetal_mat=self.palette.gunmetal,
        )
        self.objects.extend(guard_objs)
        return self

    def add_driver_periscope_hood(
        self,
        location: Tuple[float, float, float] | Vector | None = None,
        size: Tuple[float, float, float] = (0.24, 0.28, 0.18),
        brow_angle_deg: float = 18.0,
    ) -> "RogueVehicleBuilder":
        """Adds a cast armored driver's vision periscope hood with recessed optical prism to the forward glacis or cabin deck."""
        if location is not None:
            loc_v = Vector(location)
        else:
            loc_v = Vector((0.0, self.hull_length * 0.32, self.hull_deck_z))
        hood_objs = make_driver_periscope_hood(
            f"{self.name}DriverPeriscope",
            location=loc_v,
            armor_mat=self.palette.primary_armor,
            optic_mat=self.palette.emissive,
            size=size,
            brow_angle_deg=brow_angle_deg,
        )
        self.objects.extend(hood_objs)
        return self

    def add_exhaust_heat_shield(
        self,
        location: Tuple[float, float, float] | Vector | None = None,
        pipe_radius: float = 0.065,
        length: float = 0.55,
        direction: Tuple[float, float, float] | Vector = (0.0, 0.0, 1.0),
    ) -> "RogueVehicleBuilder":
        """Adds a perforated exhaust heat shield sleeve with clamp tension bands surrounding an exhaust duct."""
        loc_v = Vector(location) if location is not None else (self.exhaust_l or Vector((0.0, -self.hull_length * 0.45, self.hull_deck_z + 0.25)))
        shield_objs = make_perforated_exhaust_heat_shield(
            f"{self.name}ExhaustHeatShield",
            location=loc_v,
            pipe_radius=pipe_radius,
            length=length,
            direction=direction,
            armor_mat=self.palette.gunmetal,
            gunmetal_mat=self.palette.primary_armor,
        )
        self.objects.extend(shield_objs)
        return self

    def add_weapon_trunnions(
        self,
        spacing_x: float | None = None,
        bracket_width: float = 0.12,
        bracket_height: float = 0.22,
        bracket_depth: float = 0.28,
    ) -> "RogueVehicleBuilder":
        """Adds cast elevation trunnion bearing cheek brackets flanking the weapon mantlet cradle."""
        pivot = self.turret_pitch_loc or self.gun_pivot or (self.turret_center + Vector((0.0, 0.45, 0.10)) if self.turret_center else None)
        if pivot is None:
            return self
        spacing = spacing_x if spacing_x is not None else 0.52
        trunnion_objs = make_weapon_trunnion_bracket_assembly(
            f"{self.name}WeaponTrunnions",
            pivot_center=pivot,
            spacing_x=spacing,
            bracket_width=bracket_width,
            bracket_height=bracket_height,
            bracket_depth=bracket_depth,
            armor_mat=self.palette.primary_armor,
            gunmetal_mat=self.palette.gunmetal,
        )
        self.objects.extend(trunnion_objs)
        return self

    def add_final_drive_housings(
        self,
        radius: float = 0.20,
        depth: float = 0.12,
        is_rear: bool = True,
    ) -> "RogueVehicleBuilder":
        """Adds heavy cast final-drive transmission gearbox housings connecting lower hull to drive sprockets."""
        hy = (-self.hull_length * 0.38) if is_rear else (self.hull_length * 0.38)
        hz = self.ground_clearance + radius * 1.15
        hub_center = Vector((0.0, hy, hz))
        drive_objs = make_final_drive_housing_assembly(
            f"{self.name}FinalDrive",
            hub_center=hub_center,
            track_width=self.hull_width,
            housing_radius=radius,
            housing_depth=depth,
            is_rear=is_rear,
            armor_mat=self.palette.primary_armor,
            gunmetal_mat=self.palette.gunmetal,
        )
        self.objects.extend(drive_objs)
        return self

    def add_access_hatches(
        self,
        location: Tuple[float, float, float] | Vector | None = None,
        size: Tuple[float, float] = (0.42, 0.48),
        rotation: Tuple[float, float, float] = (0.0, 0.0, 0.0),
    ) -> "RogueVehicleBuilder":
        """Adds a raised armored crew access or maintenance inspection hatch with hinge knuckle and dogging latch."""
        if location is not None:
            loc_v = Vector(location)
        else:
            loc_v = Vector((self.hull_width * 0.22, -self.hull_length * 0.25, self.hull_deck_z))
        hatch_objs = make_armored_access_hatch_assembly(
            f"{self.name}AccessHatch",
            location=loc_v,
            size=size,
            rotation=rotation,
            armor_mat=self.palette.primary_armor,
            gunmetal_mat=self.palette.gunmetal,
        )
        self.objects.extend(hatch_objs)
        return self

    def add_track_tensioners(
        self,
        radius: float = 0.16,
        depth: float = 0.10,
    ) -> "RogueVehicleBuilder":
        """Adds hydraulic track tensioners, swing yokes and mud scrapers for front idler wheels."""
        fy = self.hull_length * 0.38
        fz = self.ground_clearance + radius * 1.15
        idler_center = Vector((0.0, fy, fz))
        tensioner_objs = make_track_tensioner_idler_yoke_assembly(
            f"{self.name}TrackTensioner",
            idler_center=idler_center,
            track_width=self.hull_width,
            arm_radius=radius,
            arm_length=depth * 2.4,
            cylinder_radius=radius * 0.36,
            cylinder_length=depth * 2.8,
            armor_mat=self.palette.primary_armor,
            gunmetal_mat=self.palette.gunmetal,
        )
        self.objects.extend(tensioner_objs)
        return self

    def add_coaxial_weapon_ports(
        self,
        offset_x: float = 0.32,
        offset_z: float = 0.06,
        caliber: float = 0.032,
        barrel_length: float = 0.35,
    ) -> "RogueVehicleBuilder":
        """Adds an armored secondary coaxial weapon port beside the main gun mantlet."""
        pivot = self.turret_pitch_loc or (self.turret_center + Vector((0.0, 0.45, 0.10)) if self.turret_center else None)
        if pivot is None:
            pivot = Vector((0.0, self.hull_length * 0.25, self.hull_deck_z + 0.15))
        port_loc = pivot + Vector((offset_x, 0.05, offset_z))
        port_objs = make_coaxial_gun_port_assembly(
            f"{self.name}CoaxPort",
            location=port_loc,
            caliber=caliber,
            barrel_length=barrel_length,
            armor_mat=self.palette.primary_armor,
            gunmetal_mat=self.palette.gunmetal,
        )
        self.objects.extend(port_objs)
        return self

    def add_pioneer_tool_racks(
        self,
        location: Tuple[float, float, float] | Vector | None = None,
        length: float = 0.95,
        width: float = 0.20,
        rotation: Tuple[float, float, float] = (0.0, 0.0, 0.0),
    ) -> "RogueVehicleBuilder":
        """Adds an exterior pioneer tool rack with spade and track crowbar along hull side or sponson."""
        if location is not None:
            loc_v = Vector(location)
        else:
            loc_v = Vector((-self.hull_width * 0.48, 0.0, self.hull_deck_z * 0.85))
        rack_objs = make_pioneer_tool_rack_assembly(
            f"{self.name}PioneerRack",
            location=loc_v,
            length=length,
            width=width,
            rotation=rotation,
            armor_mat=self.palette.primary_armor,
            gunmetal_mat=self.palette.gunmetal,
        )
        self.objects.extend(rack_objs)
        return self

    def add_unity_locators(self) -> "RogueVehicleBuilder":
        self.locator_sockets = create_unity_locator_sockets(
            name_prefix=self.name,
            muzzle_loc=self.gun_pivot,
            muzzle_dir=self.gun_dir,
            muzzle_secondary_loc=self.secondary_gun_pivot,
            muzzle_secondary_dir=self.secondary_gun_dir,
            turret_loc=self.turret_center,
            turret_pitch_loc=self.turret_pitch_loc,
            recoil_loc=self.recoil_loc,
            recoil_dir=self.recoil_dir,
            sensor_loc=self.sensor_loc,
            exhaust_left=self.exhaust_l,
            exhaust_right=self.exhaust_r,
            damage_smoke_loc=self.damage_smoke_loc,
            damage_fire_loc=self.damage_fire_loc,
            center_of_mass_loc=self.center_of_mass_loc,
            antenna_loc=self.antenna_loc,
            thruster_vector_left=self.thruster_vector_l,
            thruster_vector_right=self.thruster_vector_r,
            foot_contact_left=self.foot_loc_l,
            foot_contact_right=self.foot_loc_r,
            quad_foot_contacts=self.quad_feet if self.quad_feet else None,
            engine_audio_loc=self.engine_audio_loc,
            weapon_audio_loc=self.weapon_audio_loc,
            nested_firepoints=True,
            antenna_parts=self.objects,
            primary_firepoint_locs=self.primary_firepoint_locs,
        )
        return self

    def build(self) -> list[bpy.types.Object]:
        """Returns the raw list of editable kitbashed objects."""
        return self.objects


    def add_process_piping_manifold(
        self,
        start_loc: Tuple[float, float, float] | Vector,
        length: float = 3.5,
        pipe_radius: float = 0.08,
        direction: Tuple[float, float, float] | Vector = (0.0, 1.0, 0.0),
    ) -> "RogueStructureBuilder":
        """Adds an ASME B31.3 flanged process piping manifold to structure wall."""
        self.objects.extend(make_process_piping_manifold(
            f"{self.name}Struct_Piping",
            start_loc=start_loc,
            length=length,
            pipe_radius=pipe_radius,
            pipe_mat=self.palette.gunmetal,
            flange_mat=self.palette.wheel_rim,
            valve_mat=self.palette.primary_armor,
            accent_mat=self.palette.warning,
            direction=direction,
        ))
        return self

    def add_pressure_vessel(
        self,
        location: Tuple[float, float, float] | Vector,
        radius: float = 0.70,
        height: float = 2.4,
    ) -> "RogueStructureBuilder":
        """Adds a chemical pressure vessel with ASME 2:1 ellipsoidal dished head."""
        self.objects.extend(make_dished_head_pressure_vessel(
            f"{self.name}Struct_Vessel",
            location=location,
            radius=radius,
            height=height,
            vessel_mat=self.palette.primary_armor,
            metal_mat=self.palette.gunmetal,
            accent_mat=self.palette.warning,
        ))
        return self

    def add_gantry_crane_trolley(
        self,
        location: Tuple[float, float, float] | Vector,
        span: float = 7.0,
    ) -> "RogueStructureBuilder":
        """Adds overhead gantry crane runway trolley with hoist drum and triangular gussets."""
        self.objects.extend(make_gantry_crane_trolley_assembly(
            f"{self.name}Struct_Gantry",
            location=location,
            span=span,
            beam_mat=self.palette.gunmetal,
            hoist_mat=self.palette.wheel_rim,
            accent_mat=self.palette.warning,
        ))
        return self

    def add_caged_ladder(
        self,
        base_location: Tuple[float, float, float] | Vector,
        height: float = 3.8,
    ) -> "RogueStructureBuilder":
        """Adds an OSHA-compliant industrial caged safety ladder to structure wall."""
        self.objects.extend(make_caged_safety_ladder(
            f"{self.name}Struct_Ladder",
            base_location=base_location,
            height=height,
            ladder_mat=self.palette.gunmetal,
            cage_mat=self.palette.warning,
        ))
        return self

    def build_and_export(
        self,
        fbx_path: Path,
        blend_path: Path,
        preview_path: Path | None = None,
        card_path: Path | None = None,
        render_multiview: bool = False,
        ortho_scale: float = 5.8,
        smooth_by_angle: bool = True,
        smooth_angle_deg: float = 38.0,
        weighted_normals: bool = True,
        bake_vertex_dirt: bool = True,
        include_locators: bool = True,
        generate_lods: bool = False,
        lod_ratios: Sequence[Tuple[str, float]] = (("LOD0", 1.0), ("LOD1", 0.50), ("LOD2", 0.22)),
        generate_colliders: bool = False,
        wear_level: str | None = None,
        soot_intensity: float = 1.0,
        gameplay_meta_path: Path | None = None,
        wreckage_fbx_path: Path | None = None,
        damaged_fbx_path: Path | None = None,
        visual_root_path: str = "Vehicle",
        profile_budget: bool = True,
        target_budget: int = DEFAULT_FACE_BUDGET,
        include_vfx_flash: bool = False,
        include_vfx_thruster: bool = False,
        include_shield_bubble: bool = False,
        include_rig: bool = False,
        include_ik: bool = False,
        include_animations: bool = False,
        bake_atlas: bool = False,
        atlas_resolution: int = 1024,
        single_material_atlas: bool = False,
        export_detached_attachments: bool = False,
        render_icons: bool = False,
        icon_resolutions: Sequence[int] = (64, 128),
        icon_elevation: float = 45.0,
        wreckage_severity: str | None = None,
        structural_wear_mask: bool = False,
        unity_batch_names: bool = False,
    ) -> Dict[str, Any]:
        """Runs diagnostics, smart UV unwrapping, preview rendering, batching, and FBX export."""
        blend_path.parent.mkdir(parents=True, exist_ok=True)
        fbx_path.parent.mkdir(parents=True, exist_ok=True)

        # 0. Attach optional procedural VFX meshes
        if include_vfx_flash:
            self.add_muzzle_flash()
        if include_vfx_thruster:
            self.add_thruster_plumes()
        if include_shield_bubble:
            self.add_shield_bubble()

        # 1. Quality diagnostics on raw source objects
        # Mesh vertex edits performed by model presets can leave Blender's cached
        # object bounds stale until the dependency graph evaluates.
        bpy.context.view_layer.update()
        diagnostics = validate_model_mesh_quality(self.objects, allow_hover=self.is_hover)

        # 2. Smart UV Projection for lightmapping
        generate_lightmap_uvs(self.objects)

        # 3. Studio preview rendering
        if preview_path is not None:
            if render_multiview:
                setup_multi_view_studio(
                    self.objects, preview_path.parent, self.name,
                    ortho_scale=ortho_scale, render_all=True
                )
            else:
                setup_preview_studio(self.objects, preview_path, ortho_scale=ortho_scale)
                bpy.ops.render.render(write_still=True)

        # 4. Tactical 128x128 card sprite
        if card_path is not None:
            render_tactical_card_sprite(
                self.objects, card_path, resolution=(128, 128), ortho_scale=ortho_scale * 0.75
            )

        # 4b. Render 2D tactical icons (Top-Down zenith and Isometric 45°)
        if render_icons:
            icon_dir = fbx_path.parent / "Textures" / "Icons"
            icons = render_tactical_icons(self.objects, icon_dir, self.name, resolutions=icon_resolutions, elevation_angle_deg=icon_elevation)
            diagnostics["tactical_icons"] = {k: str(v) for k, v in icons.items()}

        # 5. Create canonical sockets before saving so the editable source keeps
        # the same locator names and antenna parenting as the exported FBX.
        if include_locators and not self.locator_sockets:
            self.add_unity_locators()

        # 6. Procedural multi-channel vertex shading & cavity AO
        effective_wear = wear_level or self.wear_level
        if effective_wear and effective_wear != "none":
            exhausts = [e for e in (self.exhaust_l, self.exhaust_r) if e is not None]
            bake_advanced_vertex_shading(
                self.objects,
                wear_level=effective_wear,
                soot_intensity=soot_intensity,
                exhaust_locs=exhausts if exhausts else None,
                muzzle_loc=self.gun_pivot,
                structural_wear_mask=structural_wear_mask,
            )
        elif bake_vertex_dirt:
            bake_vertex_ambient_dirt(self.objects)

        # Save the editable, unbatched source with baked vertex shading attributes
        bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))

        # Export detached pieces after vertex shading, but before the base-model atlas is baked.
        # Their source materials and UVs are independent of the base atlas.
        detached_attachment_object_ptrs: set[int] = set()
        if export_detached_attachments and self.attachment_objects:
            detached_paths = {}
            for att_type, att_objs in self.attachment_objects.items():
                exportable_objects = [obj for obj in att_objs if obj is not None and obj.type == "MESH"]
                if not exportable_objects:
                    continue
                att_fbx = fbx_path.parent / f"{self.name}_Attachment_{att_type}.fbx"
                export_unity_fbx(att_fbx, exportable_objects, include_empties=False)
                detached_attachment_object_ptrs.update(obj.as_pointer() for obj in exportable_objects)
                detached_paths[att_type] = str(att_fbx)
            diagnostics["detached_attachments"] = detached_paths

        # 7. Subsystem Budget & Profiling on raw source objects
        if profile_budget:
            profile_report = profile_model_budget_and_drawcalls(
                self.objects,
                prefix=self.name,
                target_budget=target_budget,
            )
            diagnostics["profile_report"] = profile_report

        # Detached attachment meshes must not enlarge the base hull collider.
        model_export_source_objects = self.objects
        if detached_attachment_object_ptrs:
            model_export_source_objects = [
                obj for obj in self.objects if obj.as_pointer() not in detached_attachment_object_ptrs
            ]
            diagnostics["detached_attachment_meshes_excluded_from_base"] = len(detached_attachment_object_ptrs)

        # 8. Physics Collision Hulls (generated on raw unjoined base objects)
        if generate_colliders:
            if detached_attachment_object_ptrs:
                # Colliders may have been requested before export. Replace those tracked hulls
                # so detached pieces cannot leave stale bounds on the base vehicle.
                for collider in self.collider_objects:
                    if collider is not None and collider.name in bpy.data.objects:
                        bpy.data.objects.remove(collider, do_unlink=True)
                self.collider_objects = []
                self.add_colliders(include_turret=True, model_objects=model_export_source_objects)
            elif not self.collider_objects:
                self.add_colliders(include_turret=True)
            diagnostics["colliders"] = [c.name for c in self.collider_objects]

        # 8b. Kinematic Armature Rigging & Rigid Vertex Weights (assigned before material batching)
        if include_rig:
            self.add_armature_rig(with_animations=include_animations, with_ik=include_ik, target_objects=model_export_source_objects)
            # Keep the editable authoring source in the same rigged state as the FBX.
            # This save happens before material batching, so each mechanical plate
            # remains a separate rigidly weighted source mesh.
            bpy.context.view_layer.update()
            bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))

        # 9. Material batching for FBX with smart angle-weighted smoothing & face-area weighted normals
        batched_objects = batch_and_join_by_material(
            model_export_source_objects,
            prefix=self.name,
            preserve_names=self.preserved_pivots,
            smooth_by_angle=smooth_by_angle,
            smooth_angle_deg=smooth_angle_deg,
            weighted_normals=weighted_normals,
            unity_batch_names=unity_batch_names,
        )
        diagnostics["weighted_normals"] = weighted_normals

        # 10. Texture Atlas Baking & Single-Material Export
        effective_bake_atlas = bake_atlas or (self.camo and self.camo != "none")
        if effective_bake_atlas:
            tex_dir = fbx_path.parent / "Textures"
            tex_maps = bake_pbr_texture_atlas(
                batched_objects, self.name, tex_dir,
                resolution=atlas_resolution, palette=self.palette
            )
            diagnostics["texture_maps"] = {k: str(v) for k, v in tex_maps.items()}
            if single_material_atlas:
                atlas_fbx_path = fbx_path.parent / f"{fbx_path.stem}_Atlas.fbx"
                create_single_material_atlas_model(batched_objects, self.name, tex_maps, atlas_fbx_path)
                diagnostics["atlas_fbx_path"] = str(atlas_fbx_path)

        # 11. Multi-Level LOD Chain generation
        lod_dict: Dict[str, list[bpy.types.Object]] | None = None
        if generate_lods:
            lod_dict = generate_lod_chain(
                batched_objects,
                prefix=self.name,
                lod_levels=lod_ratios,
                smooth_by_angle=smooth_by_angle,
                smooth_angle_deg=smooth_angle_deg,
                weighted_normals=weighted_normals,
            )
            diagnostics["lod_levels"] = {k: len(v) for k, v in lod_dict.items()}

        # 12. Create Unity locator sockets if requested
        export_objects = list(batched_objects)
        if include_locators:
            export_objects.extend(self.locator_sockets)

        if generate_colliders and self.collider_objects:
            export_objects.extend(self.collider_objects)

        # 13. Finalize Armature Attachment on Batched Meshes
        if include_rig and self.armature_object:
            for b_obj in batched_objects:
                if b_obj.type == "MESH" and not any(m.type == "ARMATURE" for m in b_obj.modifiers):
                    arm_mod = b_obj.modifiers.new(name="Armature", type="ARMATURE")
                    arm_mod.object = self.armature_object
            export_objects.append(self.armature_object)
            diagnostics["has_armature"] = True
            diagnostics["action_clips"] = list(self.action_clips.keys())

        # 14. Export Primary FBX for Unity
        export_unity_fbx(
            fbx_path, export_objects,
            include_empties=include_locators,
            include_armature=include_rig,
            bake_anim=include_animations,
        )

        # 13. Export Dedicated LOD FBX if requested
        if generate_lods and lod_dict:
            lod_fbx_path = fbx_path.parent / f"{fbx_path.stem}_LODs.fbx"
            all_lod_objs = []
            for lod_objs in lod_dict.values():
                all_lod_objs.extend(lod_objs)
            if include_locators:
                all_lod_objs.extend(self.locator_sockets)
            export_unity_fbx(lod_fbx_path, all_lod_objs, include_empties=include_locators)
            diagnostics["lod_fbx_path"] = str(lod_fbx_path)

        # 14. Export Companion Gameplay Metadata JSON
        if gameplay_meta_path is not None:
            meta = export_gameplay_metadata(
                output_path=gameplay_meta_path,
                unit_name=self.name,
                archetype=self.archetype,
                bounds=diagnostics.get("bounds", {}),
                sockets=self.locator_sockets,
                turret_center=self.turret_center,
                gun_pivot=self.gun_pivot,
                visual_root_path=visual_root_path,
            )
            diagnostics["gameplay_metadata"] = meta

        # 15. Export Wreckage Variant if requested
        if wreckage_fbx_path is not None:
            bpy.ops.wm.open_mainfile(filepath=str(blend_path))
            loaded_mesh_objects = [o for o in bpy.context.scene.objects if o.type == "MESH"]
            wreckage_blend = fbx_path.parent.parent.parent / "content" / "source-art" / f"{self.name.lower()}-wreckage.blend"
            eff_severity = wreckage_severity or getattr(self, "wreckage_severity", "catastrophic")
            make_wreckage_variant(
                source_objects=loaded_mesh_objects,
                prefix=self.name,
                output_fbx_path=wreckage_fbx_path,
                output_blend_path=wreckage_blend,
                archetype=self.archetype,
                severity=eff_severity,
                smooth_by_angle=smooth_by_angle,
                smooth_angle_deg=smooth_angle_deg,
            )
            diagnostics["wreckage_fbx_path"] = str(wreckage_fbx_path)

        # 16. Export Damaged Variant if requested
        if damaged_fbx_path is not None:
            bpy.ops.wm.open_mainfile(filepath=str(blend_path))
            loaded_mesh_objects = [o for o in bpy.context.scene.objects if o.type == "MESH"]
            damaged_blend = fbx_path.parent.parent.parent / "content" / "source-art" / f"{self.name.lower()}-damaged.blend"
            make_damaged_state(
                source_objects=loaded_mesh_objects,
                prefix=self.name,
                output_fbx_path=damaged_fbx_path,
                output_blend_path=damaged_blend,
                smooth_by_angle=smooth_by_angle,
                smooth_angle_deg=smooth_angle_deg,
            )
            diagnostics["damaged_fbx_path"] = str(damaged_fbx_path)

        diagnostics["exported_batched_objects"] = len(batched_objects)
        diagnostics["locator_sockets"] = len(self.locator_sockets) if include_locators else 0
        diagnostics["blend_path"] = str(blend_path)
        diagnostics["fbx_path"] = str(fbx_path)
        print(f"[{self.name}] Build and export complete!")
        print(f"  Source parts: {diagnostics['object_count']} -> Batched Unity parts: {len(batched_objects)}")
        print(f"  Polygons: {diagnostics['total_polygons']}, Ground valid: {diagnostics['ground_contact_valid']}")
        return diagnostics


class RogueStructureBuilder:
    """Fluent procedural builder for Rogue Command static defense buildings, factories, and structures.

    Guarantees:
    - Anchored rigidly at ground contact Z = 0.0m
    - 5-tone faction PBR material standard
    - 1-segment chamfers on hard architectural concrete and armor edges
    - Automated collision hulls (_COLLIDER_Base, _COLLIDER_Roof)
    - Production and defensive sockets (Socket_DeployRamp, Socket_RoofMount, Socket_Exhaust, etc.)
    - Multi-level LOD generation
    - Sidecar .gameplay.json export with visualRootPath = 'Building'
    """

    def __init__(
        self,
        name: str,
        building_type: str = "factory",
        faction: str = "vanguard",
        camo: str = "none",
        primary_override: Tuple[float, float, float] | None = None,
        light_override: Tuple[float, float, float] | None = None,
        emissive_override: Tuple[float, float, float] | None = None,
        camo_scale: float = 1.0,
        camo_contrast: float = 1.0,
        enable_normal_relief: bool = True,
        normal_intensity: float = 1.0,
    ):
        self.name = name
        self.building_type = building_type.lower()
        self.palette = get_faction_palette(
            faction,
            name,
            camo=camo,
            primary_override=primary_override,
            light_override=light_override,
            emissive_override=emissive_override,
            camo_scale=camo_scale,
            camo_contrast=camo_contrast,
            enable_normal_relief=enable_normal_relief,
            normal_intensity=normal_intensity,
        )
        self.objects: list[bpy.types.Object] = []
        self.sockets: dict[str, Vector] = {}
        self.collider_objects: list[bpy.types.Object] = []
        self.locator_sockets: list[bpy.types.Object] = []

    def build_factory(
        self,
        width: float = 6.0,
        length: float = 8.0,
        height: float = 4.5,
    ) -> "RogueStructureBuilder":
        objs, socks = make_factory_assembly(
            self.name,
            width=width,
            length=length,
            height=height,
            foundation_mat=self.palette.gunmetal,
            wall_mat=self.palette.primary_armor,
            roof_mat=self.palette.light_armor,
            door_mat=self.palette.gunmetal,
            hazard_mat=self.palette.warning,
            emissive_mat=self.palette.emissive,
            metal_mat=self.palette.gunmetal,
        )
        self.objects.extend(objs)
        self.sockets.update(socks)
        return self

    def build_defense_turret(
        self,
        radius: float = 3.2,
        height: float = 2.2,
        weapon_type: str = "cannon",
    ) -> "RogueStructureBuilder":
        objs, socks, _ = make_turret_bunker_assembly(
            self.name,
            radius=radius,
            height=height,
            weapon_type=weapon_type,
            foundation_mat=self.palette.gunmetal,
            bunker_mat=self.palette.primary_armor,
            metal_mat=self.palette.gunmetal,
            accent_mat=self.palette.warning,
            emissive_mat=self.palette.emissive,
        )
        self.objects.extend(objs)
        self.sockets.update(socks)
        return self

    def build_radar_station(
        self,
        base_radius: float = 2.8,
        height: float = 3.5,
    ) -> "RogueStructureBuilder":
        objs, socks = make_radar_station_assembly(
            self.name,
            base_radius=base_radius,
            height=height,
            foundation_mat=self.palette.gunmetal,
            bunker_mat=self.palette.primary_armor,
            metal_mat=self.palette.gunmetal,
            dish_mat=self.palette.light_armor,
            emissive_mat=self.palette.emissive,
        )
        self.objects.extend(objs)
        self.sockets.update(socks)
        return self

    def build_energy_silo(
        self,
        radius: float = 2.5,
        height: float = 5.0,
    ) -> "RogueStructureBuilder":
        objs, socks = make_energy_silo_assembly(
            self.name,
            radius=radius,
            height=height,
            foundation_mat=self.palette.gunmetal,
            silo_mat=self.palette.primary_armor,
            metal_mat=self.palette.gunmetal,
            containment_mat=self.palette.light_armor,
            emissive_mat=self.palette.emissive,
        )
        self.objects.extend(objs)
        self.sockets.update(socks)
        return self

    def build_orbital_silo(
        self,
        radius: float = 3.6,
        height: float = 4.5,
    ) -> "RogueStructureBuilder":
        objs, socks = make_orbital_silo_assembly(
            self.name,
            radius=radius,
            height=height,
            foundation_mat=self.palette.gunmetal,
            bunker_mat=self.palette.primary_armor,
            metal_mat=self.palette.gunmetal,
            door_mat=self.palette.light_armor,
            accent_mat=self.palette.warning,
            emissive_mat=self.palette.emissive,
        )
        self.objects.extend(objs)
        self.sockets.update(socks)
        return self

    def build_shield_dome(
        self,
        radius: float = 3.2,
        height: float = 3.8,
    ) -> "RogueStructureBuilder":
        objs, socks = make_shield_dome_assembly(
            self.name,
            radius=radius,
            height=height,
            foundation_mat=self.palette.gunmetal,
            bunker_mat=self.palette.primary_armor,
            metal_mat=self.palette.gunmetal,
            coil_mat=self.palette.light_armor,
            accent_mat=self.palette.warning,
            emissive_mat=self.palette.emissive,
        )
        self.objects.extend(objs)
        self.sockets.update(socks)
        return self

    def build_emp_obelisk(
        self,
        radius: float = 2.8,
        height: float = 5.2,
    ) -> "RogueStructureBuilder":
        objs, socks = make_emp_obelisk_assembly(
            self.name,
            radius=radius,
            height=height,
            foundation_mat=self.palette.gunmetal,
            bunker_mat=self.palette.primary_armor,
            metal_mat=self.palette.gunmetal,
            coil_mat=self.palette.light_armor,
            accent_mat=self.palette.warning,
            emissive_mat=self.palette.emissive,
        )
        self.objects.extend(objs)
        self.sockets.update(socks)
        return self

    def build_bunker_pillbox(
        self,
        radius: float = 3.0,
        height: float = 2.0,
    ) -> "RogueStructureBuilder":
        objs, socks = make_bunker_pillbox_assembly(
            self.name,
            radius=radius,
            height=height,
            foundation_mat=self.palette.gunmetal,
            bunker_mat=self.palette.primary_armor,
            metal_mat=self.palette.gunmetal,
            roof_mat=self.palette.light_armor,
            accent_mat=self.palette.warning,
            emissive_mat=self.palette.emissive,
        )
        self.objects.extend(objs)
        self.sockets.update(socks)
        return self

    def build_anti_air_battery(
        self,
        radius: float = 3.2,
        height: float = 3.6,
    ) -> "RogueStructureBuilder":
        objs, socks = make_anti_air_battery_assembly(
            self.name,
            radius=radius,
            height=height,
            foundation_mat=self.palette.gunmetal,
            bunker_mat=self.palette.primary_armor,
            metal_mat=self.palette.gunmetal,
            turret_mat=self.palette.light_armor,
            accent_mat=self.palette.warning,
            emissive_mat=self.palette.emissive,
        )
        self.objects.extend(objs)
        self.sockets.update(socks)
        return self

    def build_comms_relay_tower(
        self,
        radius: float = 2.8,
        height: float = 5.6,
    ) -> "RogueStructureBuilder":
        objs, socks = make_comms_relay_tower_assembly(
            self.name,
            radius=radius,
            height=height,
            foundation_mat=self.palette.gunmetal,
            bunker_mat=self.palette.primary_armor,
            metal_mat=self.palette.gunmetal,
            dish_mat=self.palette.light_armor,
            accent_mat=self.palette.warning,
            emissive_mat=self.palette.emissive,
        )
        self.objects.extend(objs)
        self.sockets.update(socks)
        return self

    def add_hydraulic_hoses(
        self,
        radius: float = 0.032,
        sag: float = 0.22,
    ) -> "RogueStructureBuilder":
        """Adds bilateral external high-voltage power conduits to the structure base."""
        for side, stag in ((-1, "L"), (1, "R")):
            p0 = (side * 2.1, -1.6, 0.35)
            p1 = (side * 1.35, -0.5, 1.65)
            self.objects.extend(make_catenary_cable_assembly(
                f"{self.name}Struct_{stag}_PowerConduit",
                start_point=p0,
                end_point=p1,
                radius=radius,
                sag=sag,
                segments=10,
                corrugated=True,
                hose_mat=self.palette.tread,
                coupler_mat=self.palette.gunmetal,
                accent_mat=self.palette.warning,
            ))
        return self

    def add_hull_greebles(self) -> "RogueStructureBuilder":
        """Adds BMesh-inset maintenance hatches and louver vents to structure roof/deck."""
        roof_pos = self.sockets.get("Socket_RoofMount", Vector((0.0, 0.0, 2.2)))
        self.objects.extend(make_bmesh_hull_greebles(
            f"{self.name}Struct_Greeble",
            deck_center=(0.0, 0.0, 0.0),
            hull_width=3.6,
            hull_length=4.0,
            deck_z=max(0.45, roof_pos.z * 0.45),
            armor_mat=self.palette.light_armor,
            metal_mat=self.palette.gunmetal,
            accent_mat=self.palette.warning,
            include_lugs=True,
            include_vents=True,
        ))
        return self

    def add_tactical_markings(self) -> "RogueStructureBuilder":
        """Projects BVHTree tactical chevron decals onto structure walls."""
        self.objects.extend(make_tactical_decal_plates(
            f"{self.name}Struct_Decal",
            target_objects=list(self.objects),
            decal_mat=self.palette.warning,
            secondary_mat=self.palette.light_armor,
            stripe_count=2,
            include_zenith_id=True,
        ))
        return self

    def add_hydraulic_pistons(self) -> "RogueStructureBuilder":
        """Adds heavy telescopic hydraulic stabilizing struts to structure perimeter."""
        for side, stag in ((-1, "L"), (1, "R")):
            p0 = (side * 2.2, 1.4, 0.28)
            p1 = (side * 1.45, 0.9, 1.55)
            self.objects.extend(make_telescopic_piston_assembly(
                f"{self.name}Struct_{stag}_Piston",
                start_point=p0,
                end_point=p1,
                outer_radius=0.062,
                inner_radius=0.038,
                stroke_ratio=0.55,
                housing_mat=self.palette.gunmetal,
                rod_mat=self.palette.wheel_rim,
                seal_mat=self.palette.tread,
                accent_mat=self.palette.warning,
            ))
        return self

    def add_ammo_belt_chutes(self) -> "RogueStructureBuilder":
        """Adds ammunition feed chutes to defense turret or structure roof mounts."""
        roof_pos = self.sockets.get("Socket_TurretPivot", self.sockets.get("Socket_RoofMount", Vector((0.0, 0.0, 2.2))))
        for side, stag in ((-1, "L"), (1, "R")):
            p0 = roof_pos + Vector((side * 0.95, -0.45, -0.15))
            p1 = roof_pos + Vector((side * 0.55, 0.25, 0.22))
            self.objects.extend(make_ammo_belt_chute_assembly(
                f"{self.name}Struct_{stag}_AmmoChute",
                start_point=p0,
                end_point=p1,
                chute_width=0.16,
                chute_thickness=0.052,
                sag=0.12,
                hopper_mat=self.palette.primary_armor,
                chute_mat=self.palette.gunmetal,
                brass_mat=self.palette.warning,
            ))
        return self

    def add_aps_hardkill_suite(self) -> "RogueStructureBuilder":
        """Adds Active Protection System (APS) hard-kill pods and AESA radar panels to structure roof."""
        roof_pos = self.sockets.get("Socket_TurretPivot", self.sockets.get("Socket_RoofMount", Vector((0.0, 0.0, 2.2))))
        self.objects.extend(make_aps_hardkill_suite(
            f"{self.name}Struct_APS",
            turret_center=roof_pos,
            turret_width=2.1,
            turret_length=2.1,
            turret_height=0.5,
            armor_mat=self.palette.light_armor,
            metal_mat=self.palette.gunmetal,
            radar_mat=self.palette.warning,
            emissive_mat=self.palette.emissive,
            include_lwr=True,
        ))
        return self

    def add_tactical_stowage(self) -> "RogueStructureBuilder":
        """Adds external field stowage rack, tarp bundle, and Jerry cans to structure perimeter."""
        self.objects.extend(make_tactical_stowage_kit(
            f"{self.name}Struct_Stowage",
            hull_length=4.2,
            hull_width=3.6,
            deck_z=1.1,
            armor_mat=self.palette.primary_armor,
            metal_mat=self.palette.gunmetal,
            tarp_mat=self.palette.light_armor,
            tread_mat=self.palette.tread,
            accent_mat=self.palette.warning,
        ))
        return self

    def add_slat_armor(self, slat_count: int = 5) -> "RogueStructureBuilder":
        """Adds perimeter anti-HEAT standoff slat/cage bar armor grids to the structure base."""
        self.objects.extend(make_slat_cage_armor_assembly(
            f"{self.name}Struct_SlatArmor",
            hull_length=4.2,
            hull_width=3.8,
            deck_z=1.15,
            slat_count=slat_count,
            metal_mat=self.palette.gunmetal,
            armor_mat=self.palette.primary_armor,
            accent_mat=self.palette.warning,
        ))
        return self

    def add_era_bricks(self, cols: int = 5, rows: int = 2) -> "RogueStructureBuilder":
        """Adds staggered Explosive Reactive Armor (ERA) wedge bricks to the structure casemate walls."""
        roof_pos = self.sockets.get("Socket_TurretPivot", self.sockets.get("Socket_RoofMount", Vector((0.0, 0.0, 2.0))))
        self.objects.extend(make_era_reactive_bricks_assembly(
            f"{self.name}Struct_ERABricks",
            hull_length=4.2,
            hull_width=3.8,
            deck_z=1.25,
            turret_center=roof_pos,
            turret_length=2.2,
            turret_width=2.2,
            cols=cols,
            rows=rows,
            brick_mat=self.palette.light_armor,
            bolt_mat=self.palette.gunmetal,
            accent_mat=self.palette.warning,
        ))
        return self

    def add_camo_netting(self) -> "RogueStructureBuilder":
        """Adds procedural 3D noise-displaced draped camouflage netting canopy over the structure roof."""
        roof_pos = self.sockets.get("Socket_TurretPivot", self.sockets.get("Socket_RoofMount", Vector((0.0, 0.0, 2.1))))
        self.objects.extend(make_camo_netting_assembly(
            f"{self.name}Struct_CamoNet",
            hull_length=4.4,
            hull_width=4.0,
            deck_z=1.5,
            turret_center=roof_pos,
            turret_length=2.4,
            turret_width=2.4,
            net_mat=self.palette.light_armor,
            strap_mat=self.palette.tread,
            pole_mat=self.palette.gunmetal,
        ))
        return self

    def add_thermal_radiators(self, fin_count: int = 6) -> "RogueStructureBuilder":
        """Adds armored heat-exchanger radiator banks with glowing emissive plasma/coolant fins to the structure."""
        self.objects.extend(make_thermal_radiator_assembly(
            f"{self.name}Struct_Radiator",
            hull_length=4.2,
            hull_width=3.8,
            deck_z=1.05,
            fin_count=fin_count,
            housing_mat=self.palette.primary_armor,
            fin_mat=self.palette.gunmetal,
            glow_mat=self.palette.emissive,
            pipe_mat=self.palette.wheel_rim,
        ))
        return self

    def add_rws_mount(
        self,
        scale: float = 1.0,
        yaw_deg: float = 0.0,
        pitch_deg: float = 0.0,
        location: Tuple[float, float, float] | Vector | None = None,
    ) -> "RogueStructureBuilder":
        """Adds a roof/perimeter mounted independent Remote Weapon Station (RWS/CROWS) with stabilized EO/IR optical sensor ball and rapid-fire autocannon."""
        rws_loc = Vector(location) if location is not None else Vector((1.2, 0.8, 2.1))
        self.objects.extend(make_remote_weapon_station_assembly(
            f"{self.name}Struct_RWS",
            location=rws_loc,
            scale=scale,
            yaw_deg=yaw_deg,
            pitch_deg=pitch_deg,
            housing_mat=self.palette.light_armor,
            gun_mat=self.palette.gunmetal,
            optic_mat=self.palette.emissive,
            ammo_mat=self.palette.primary_armor,
        ))
        return self

    def add_drone_dock(
        self,
        scale: float = 1.0,
        location: Tuple[float, float, float] | Vector | None = None,
    ) -> "RogueStructureBuilder":
        """Adds a roof landing cradle with magnetic charging guide rails, alignment runway glow LEDs, and a docked parasitic reconnaissance drone."""
        dock_loc = Vector(location) if location is not None else Vector((0.0, -1.1, 2.1))
        self.objects.extend(make_drone_docking_bay_assembly(
            f"{self.name}Struct_DroneBay",
            location=dock_loc,
            scale=scale,
            bay_mat=self.palette.primary_armor,
            frame_mat=self.palette.gunmetal,
            drone_mat=self.palette.light_armor,
            glow_mat=self.palette.emissive,
        ))
        return self

    def add_recoil_spade(
        self,
        width: float = 2.4,
        depth: float = 0.95,
        deployed: bool = True,
        location: Tuple[float, float, float] | Vector | None = None,
    ) -> "RogueStructureBuilder":
        """Adds perimeter heavy hydraulic recoil earth spades that bite into the terrain plane (Z ≈ 0.02) to absorb firing recoil."""
        spade_loc = Vector(location) if location is not None else Vector((0.0, -2.1, 0.45))
        self.objects.extend(make_recoil_earth_spade_assembly(
            f"{self.name}Struct_Spade",
            location=spade_loc,
            width=width,
            depth=depth,
            deployed=deployed,
            blade_mat=self.palette.primary_armor,
            strut_mat=self.palette.gunmetal,
            ram_mat=self.palette.primary_armor,
            rod_mat=self.palette.wheel_rim,
        ))
        return self

    def add_barrel_shroud(
        self,
        shroud_length: float = 1.6,
        shroud_radius: float = 0.18,
    ) -> "RogueStructureBuilder":
        """Adds a ventilated thermal barrel jacket with consolidated BMesh cooling perforation slats, reinforced clamping collars, and multi-baffle muzzle brake."""
        gp = self.sockets.get("Socket_MuzzlePrimary", Vector((0.0, 1.8, 1.8)))
        gd = Vector((0.0, 1.0, 0.0))
        self.objects.extend(make_perforated_barrel_shroud_assembly(
            f"{self.name}Struct_BarrelShroud",
            gun_pivot=gp,
            gun_dir=gd,
            barrel_length=2.8,
            barrel_radius=0.12,
            shroud_length=shroud_length,
            shroud_radius=shroud_radius,
            shroud_mat=self.palette.gunmetal,
            collar_mat=self.palette.light_armor,
            brake_mat=self.palette.gunmetal,
        ))
        return self

    def add_nanite_dispenser(
        self,
        scale: float = 1.0,
        spacing_x: float = 3.2,
        location: Tuple[float, float, float] | Vector | None = None,
    ) -> "RogueStructureBuilder":
        """Adds bilateral armored field nanite repair dispenser pods with glowing inspection sight glasses and spray heads to the structure."""
        disp_loc = Vector(location) if location is not None else Vector((0.0, 0.0, 1.2))
        self.objects.extend(make_nanite_repair_dispenser_assembly(
            f"{self.name}Struct_NaniteDisp",
            location=disp_loc,
            scale=scale,
            spacing_x=spacing_x,
            canister_mat=self.palette.primary_armor,
            frame_mat=self.palette.gunmetal,
            glow_mat=self.palette.emissive,
            hose_mat=self.palette.tread,
        ))
        return self

    def add_grav_dampener(
        self,
        radius: float = 0.55,
        height: float = 0.35,
        location: Tuple[float, float, float] | Vector | None = None,
    ) -> "RogueStructureBuilder":
        """Adds a structure-mounted gyroscopic gravitational anchor and inertial dampener hub with glowing singularity sphere."""
        hub_loc = Vector(location) if location is not None else Vector((0.0, 0.0, 2.2))
        self.objects.extend(make_grav_dampener_hub_assembly(
            f"{self.name}Struct_GravHub",
            location=hub_loc,
            radius=radius,
            height=height,
            housing_mat=self.palette.primary_armor,
            rotor_mat=self.palette.gunmetal,
            core_mat=self.palette.emissive,
            brace_mat=self.palette.light_armor,
        ))
        return self

    def add_comms_mast(
        self,
        height: float = 2.2,
        location: Tuple[float, float, float] | Vector | None = None,
    ) -> "RogueStructureBuilder":
        """Adds a heavy-duty telescopic communications & EW jamming mast with planar blade antenna fins and satcom dome to the structure."""
        mast_loc = Vector(location) if location is not None else Vector((1.2, -1.2, 2.2))
        self.objects.extend(make_comms_ew_mast_assembly(
            f"{self.name}Struct_CommsMast",
            location=mast_loc,
            height=height,
            base_mat=self.palette.primary_armor,
            mast_mat=self.palette.gunmetal,
            radome_mat=self.palette.light_armor,
            glow_mat=self.palette.emissive,
        ))
        return self

    def add_ram_prow(
        self,
        width: float = 3.2,
        height: float = 0.65,
        forward_reach: float = 0.55,
        location: Tuple[float, float, float] | Vector | None = None,
    ) -> "RogueStructureBuilder":
        """Adds reinforced perimeter kinetic ram barrier cowcatcher prow with towing shackles and hazard chevrons."""
        prow_loc = Vector(location) if location is not None else Vector((0.0, 2.4, 0.35))
        self.objects.extend(make_reinforced_ram_prow_assembly(
            f"{self.name}Struct_RamProw",
            location=prow_loc,
            width=width,
            height=height,
            forward_reach=forward_reach,
            prow_mat=self.palette.primary_armor,
            bracket_mat=self.palette.gunmetal,
            accent_mat=self.palette.warning,
        ))
        return self

    def add_weld_seams(
        self,
        seams: Sequence[Tuple[Tuple[float, float, float] | Vector, Tuple[float, float, float] | Vector]],
        radius: float = 0.018,
        material: bpy.types.Material | None = None,
        prefix: str = "WeldSeam",
    ) -> "RogueStructureBuilder":
        """Adds authentic scalloped weld seams along structure foundation or armor junctions."""
        mat = material or self.palette.gunmetal
        for idx, (p1, p2) in enumerate(seams, start=1):
            weld_obj = make_weld_seam_bead(
                f"{self.name}_{prefix}_{idx:02d}",
                p1, p2,
                radius=radius,
                material=mat,
            )
            if weld_obj:
                self.objects.append(weld_obj)
        return self

    def add_colliders(self) -> "RogueStructureBuilder":
        self.collider_objects = generate_collision_hulls(
            self.objects, prefix=self.name, include_turret_collider=True
        )
        return self

    def add_access_hatches(
        self,
        location: Tuple[float, float, float] | Vector | None = None,
        size: Tuple[float, float] = (0.48, 0.54),
        rotation: Tuple[float, float, float] = (0.0, 0.0, 0.0),
    ) -> "RogueStructureBuilder":
        """Adds a raised armored maintenance or personnel access hatch to the structure roof or deck."""
        roof_pos = self.sockets.get("Socket_RoofMount", Vector((0.0, 0.0, 2.2)))
        loc_v = Vector(location) if location is not None else Vector((1.2, -0.8, roof_pos.z * 0.48))
        hatch_objs = make_armored_access_hatch_assembly(
            f"{self.name}Struct_AccessHatch",
            location=loc_v,
            size=size,
            rotation=rotation,
            armor_mat=self.palette.light_armor,
            gunmetal_mat=self.palette.gunmetal,
        )
        self.objects.extend(hatch_objs)
        return self

    def add_pioneer_tool_racks(
        self,
        location: Tuple[float, float, float] | Vector | None = None,
        length: float = 1.10,
        width: float = 0.24,
        rotation: Tuple[float, float, float] = (0.0, 0.0, 0.0),
    ) -> "RogueStructureBuilder":
        """Adds an exterior pioneer damage-control tool rack to the facility wall or deck."""
        loc_v = Vector(location) if location is not None else Vector((-1.45, 0.20, 0.45))
        rack_objs = make_pioneer_tool_rack_assembly(
            f"{self.name}Struct_PioneerRack",
            location=loc_v,
            length=length,
            width=width,
            rotation=rotation,
            armor_mat=self.palette.light_armor,
            gunmetal_mat=self.palette.gunmetal,
        )
        self.objects.extend(rack_objs)
        return self

    def add_unity_locators(self) -> "RogueStructureBuilder":
        self.locator_sockets = []
        if "Socket_DamageSmoke" not in self.sockets:
            self.sockets["Socket_DamageSmoke"] = Vector((0.0, 0.0, 2.2))
        if "Socket_DamageFire" not in self.sockets:
            self.sockets["Socket_DamageFire"] = Vector((0.3, -0.3, 2.5))
        s_muzzles = [pos for name, pos in self.sockets.items() if "Muzzle" in name]
        if s_muzzles and "Socket_WeaponAudio" not in self.sockets:
            self.sockets["Socket_WeaponAudio"] = s_muzzles[0]
        if "Socket_BuildingAudio" not in self.sockets:
            self.sockets["Socket_BuildingAudio"] = Vector((0.0, 0.0, 1.0))

        for s_name, s_pos in self.sockets.items():
            fwd = (0.0, 1.0, 0.0) if ("Deploy" in s_name or "Muzzle" in s_name) else (0.0, 0.0, 1.0)
            emp = create_unity_empty_locator(s_name, s_pos, forward=fwd)
            self.locator_sockets.append(emp)
            if "Muzzle" in s_name:
                fp = create_unity_empty_locator("FirePoint_0", s_pos, forward=fwd, radius=0.08)
                fp.parent = emp
                self.locator_sockets.append(fp)
        return self


    def add_process_piping_manifold(
        self,
        start_loc: Tuple[float, float, float] | Vector,
        length: float = 3.5,
        pipe_radius: float = 0.08,
        direction: Tuple[float, float, float] | Vector = (0.0, 1.0, 0.0),
    ) -> "RogueStructureBuilder":
        """Adds an ASME B31.3 flanged process piping manifold to structure wall."""
        self.objects.extend(make_process_piping_manifold(
            f"{self.name}Struct_Piping",
            start_loc=start_loc,
            length=length,
            pipe_radius=pipe_radius,
            pipe_mat=self.palette.gunmetal,
            flange_mat=self.palette.wheel_rim,
            valve_mat=self.palette.primary_armor,
            accent_mat=self.palette.warning,
            direction=direction,
        ))
        return self

    def add_pressure_vessel(
        self,
        location: Tuple[float, float, float] | Vector,
        radius: float = 0.70,
        height: float = 2.4,
    ) -> "RogueStructureBuilder":
        """Adds a chemical pressure vessel with ASME 2:1 ellipsoidal dished head."""
        self.objects.extend(make_dished_head_pressure_vessel(
            f"{self.name}Struct_Vessel",
            location=location,
            radius=radius,
            height=height,
            vessel_mat=self.palette.primary_armor,
            metal_mat=self.palette.gunmetal,
            accent_mat=self.palette.warning,
        ))
        return self

    def add_gantry_crane_trolley(
        self,
        location: Tuple[float, float, float] | Vector,
        span: float = 7.0,
    ) -> "RogueStructureBuilder":
        """Adds overhead gantry crane runway trolley with hoist drum and triangular gussets."""
        self.objects.extend(make_gantry_crane_trolley_assembly(
            f"{self.name}Struct_Gantry",
            location=location,
            span=span,
            beam_mat=self.palette.gunmetal,
            hoist_mat=self.palette.wheel_rim,
            accent_mat=self.palette.warning,
        ))
        return self

    def add_caged_ladder(
        self,
        base_location: Tuple[float, float, float] | Vector,
        height: float = 3.8,
    ) -> "RogueStructureBuilder":
        """Adds an OSHA-compliant industrial caged safety ladder to structure wall."""
        self.objects.extend(make_caged_safety_ladder(
            f"{self.name}Struct_Ladder",
            base_location=base_location,
            height=height,
            ladder_mat=self.palette.gunmetal,
            cage_mat=self.palette.warning,
        ))
        return self

    def build_and_export(
        self,
        fbx_path: Path,
        blend_path: Path,
        preview_path: Path | None = None,
        generate_lods: bool = True,
        lod_ratios: Sequence[Tuple[str, float]] = (("LOD0", 1.0), ("LOD1", 0.50), ("LOD2", 0.22)),
        generate_colliders: bool = True,
        gameplay_meta_path: Path | None = None,
        smooth_by_angle: bool = True,
        smooth_angle_deg: float = 38.0,
        weighted_normals: bool = True,
        render_icons: bool = False,
        icon_resolutions: Sequence[int] = (64, 128),
        icon_elevation: float = 45.0,
        bake_atlas: bool = False,
        atlas_resolution: int = 1024,
        single_material_atlas: bool = False,
        wear_level: str = "field_tested",
        soot_intensity: float = 1.0,
        bake_vertex_shading: bool = True,
        structural_wear_mask: bool = False,
    ) -> dict[str, Any]:
        blend_path.parent.mkdir(parents=True, exist_ok=True)
        fbx_path.parent.mkdir(parents=True, exist_ok=True)

        if generate_colliders and not self.collider_objects:
            self.add_colliders()

        if not self.locator_sockets:
            self.add_unity_locators()

        diagnostics = validate_model_mesh_quality(
            self.objects,
            allow_hover=False,
        )

        # Procedural multi-channel vertex shading & cavity AO for structures
        if bake_vertex_shading and wear_level and wear_level != "none":
            s_muzzles = [pos for name, pos in self.sockets.items() if "Muzzle" in name]
            s_exhausts = [pos for name, pos in self.sockets.items() if any(k in name for k in ("Exhaust", "Chimney", "Smoke", "Vent"))]
            muz_pos = s_muzzles[0] if s_muzzles else None
            bake_advanced_vertex_shading(
                self.objects,
                wear_level=wear_level,
                soot_intensity=soot_intensity,
                exhaust_locs=s_exhausts if s_exhausts else None,
                muzzle_loc=muz_pos,
                structural_wear_mask=structural_wear_mask,
            )

        bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))

        if preview_path is not None:
            setup_preview_studio(self.objects, preview_path, ortho_scale=12.0)
            bpy.ops.render.render(write_still=True)
            diagnostics["preview_image"] = str(preview_path)

        if render_icons:
            icon_dir = fbx_path.parent / "Textures" / "Icons"
            icons = render_tactical_icons(self.objects, icon_dir, self.name, resolutions=icon_resolutions, elevation_angle_deg=icon_elevation)
            diagnostics["tactical_icons"] = {k: str(v) for k, v in icons.items()}

        batched_objects = batch_and_join_by_material(
            self.objects,
            prefix=self.name,
            preserve_names=[],
            smooth_by_angle=smooth_by_angle,
            smooth_angle_deg=smooth_angle_deg,
            weighted_normals=weighted_normals,
        )
        diagnostics["weighted_normals"] = weighted_normals

        if bake_atlas or single_material_atlas or (self.palette.camo and self.palette.camo != "none"):
            texture_dir = fbx_path.parent / "Textures"
            texture_maps = bake_pbr_texture_atlas(
                batched_objects,
                self.name,
                texture_dir,
                resolution=atlas_resolution,
                palette=self.palette,
            )
            diagnostics["texture_maps"] = {key: str(path) for key, path in texture_maps.items()}
            if single_material_atlas:
                atlas_fbx_path = fbx_path.parent / f"{fbx_path.stem}_Atlas.fbx"
                create_single_material_atlas_model(batched_objects, self.name, texture_maps, atlas_fbx_path)
                diagnostics["atlas_fbx_path"] = str(atlas_fbx_path)

        export_objects = list(batched_objects)
        export_objects.extend(self.locator_sockets)
        if generate_colliders and self.collider_objects:
            export_objects.extend(self.collider_objects)

        export_unity_fbx(fbx_path, export_objects, include_empties=True)

        lod_dict: Dict[str, list[bpy.types.Object]] | None = None
        if generate_lods:
            lod_dict = generate_lod_chain(
                batched_objects,
                prefix=self.name,
                lod_levels=lod_ratios,
                smooth_by_angle=smooth_by_angle,
                smooth_angle_deg=smooth_angle_deg,
                weighted_normals=weighted_normals,
            )
            diagnostics["lod_levels"] = {k: len(v) for k, v in lod_dict.items()}
            lod_fbx_path = fbx_path.parent / f"{fbx_path.stem}_LODs.fbx"
            all_lod_objs = []
            for lod_objs in lod_dict.values():
                all_lod_objs.extend(lod_objs)
            all_lod_objs.extend(self.locator_sockets)
            export_unity_fbx(lod_fbx_path, all_lod_objs, include_empties=True)
            diagnostics["lod_fbx_path"] = str(lod_fbx_path)

        if gameplay_meta_path is not None:
            meta = export_gameplay_metadata(
                output_path=gameplay_meta_path,
                unit_name=self.name,
                archetype=f"structure_{self.building_type}",
                bounds=diagnostics.get("bounds", {}),
                sockets=self.locator_sockets,
                visual_root_path="Building",
            )
            diagnostics["gameplay_metadata"] = meta

        diagnostics["exported_batched_objects"] = len(batched_objects)
        diagnostics["locator_sockets"] = len(self.locator_sockets)
        diagnostics["blend_path"] = str(blend_path)
        diagnostics["fbx_path"] = str(fbx_path)
        print(f"[{self.name}] Structure build and export complete!")
        print(f"  Type: {self.building_type}, Batched parts: {len(batched_objects)}")
        print(f"  Polygons: {diagnostics['total_polygons']}, Ground valid: {diagnostics['ground_contact_valid']}")
        return diagnostics
