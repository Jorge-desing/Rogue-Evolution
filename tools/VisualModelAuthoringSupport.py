"""Small deterministic Blender helpers for task-specific visual refinements.

The repository generator remains the source of each preset blockout and export
pipeline.  Build scripts patch the vehicle builder immediately before its normal
validation, UV, vertex-wear, atlas, and FBX stages.
"""

from __future__ import annotations

import math
import sys
from pathlib import Path
from typing import Any, Callable, Mapping, Sequence

import bpy
import bmesh
from mathutils import Vector


REPO_ROOT = Path(__file__).resolve().parents[1]
GENERATOR_PATH = REPO_ROOT / "tools" / "generate_unit_model.py"


def run_preset_build(generator_args: Sequence[str], refine: Callable) -> None:
    """Run the canonical generator while refining the preset before export."""
    sys.path.insert(0, str(REPO_ROOT / "tools"))
    import generate_unit_model as generator

    builder_type = generator.rmk.RogueVehicleBuilder
    original = builder_type.build_and_export

    def build_and_export_with_refinement(builder, *args, **kwargs):
        refine(builder)
        return original(builder, *args, **kwargs)

    builder_type.build_and_export = build_and_export_with_refinement
    sys.argv = [str(GENERATOR_PATH), "--", *generator_args]
    try:
        generator.main()
    finally:
        builder_type.build_and_export = original


def remove_objects(builder, predicate: Callable[[str], bool]) -> list[str]:
    removed = []
    for obj in list(builder.objects):
        if obj is None or obj.name not in bpy.data.objects or not predicate(obj.name):
            continue
        removed.append(obj.name)
        builder.objects.remove(obj)
        bpy.data.objects.remove(obj, do_unlink=True)
    return removed


def reduce_subpixel_details_to_budget(
    builder,
    max_dimension_m: float = 0.60,
) -> dict[str, Any]:
    """Report geometry without applying a calibration-derived limit."""
    import RogueModelKit as rmk

    return rmk.enforce_face_budget_before_export(builder.objects, builder.name)


def simplify_low_value_bevel_details(
    builder,
    max_dimension_m: float = 0.60,
    angle_degrees: float = 28.0,
    max_surface_deviation_m: float = 0.02,
    protected_name_tokens: Sequence[str] = (
        "Weapon", "Cannon", "Gun", "Gatling", "Howitzer", "Barrel", "Missile",
        "Launcher", "Drill", "Auger", "Nanite", "Sensor", "Optic", "Lens",
        "Socket", "FirePoint", "Comms", "Mast", "Radar", "Antenna", "Shield",
        "Hull", "Chassis", "Turret", "Mantlet", "Trunnion", "Crane", "Boom",
        "Wheel", "Track", "Tread", "Leg", "Foot", "Actuator", "Treads_Belt",
        "Turret_BaseShell",
    ),
) -> dict[str, Any]:
    """Collapse redundant bevel facets on small fittings, leaving primary forms intact.

    The generator bakes bevels into source meshes. Dissolving only low-angle edges
    on small details removes redundant bevel rings while retaining a crisp chamfer.
    Each change is rejected if bidirectional sampled surface displacement exceeds
    the meter tolerance. Primary armor, articulated rigs, colliders, authored
    surface data, and named role-defining hardware are excluded so the pass cannot
    erase vertex wear, UV boundaries, or the unit silhouette.
    """
    import RogueModelKit as rmk

    changed_meshes = 0
    faces_removed = 0
    geometry_rejections = 0
    max_accepted_deviation_m = 0.0
    max_rejected_deviation_m = 0.0
    if max_surface_deviation_m < 0.0:
        raise ValueError("max_surface_deviation_m cannot be negative")
    limit = math.radians(angle_degrees)
    protected_tokens = tuple(token.casefold() for token in protected_name_tokens)

    for obj in tuple(builder.objects):
        if obj is None or obj.type != "MESH" or "_COLLIDER_" in obj.name:
            continue
        if any(token in obj.name.casefold() for token in protected_tokens):
            continue
        if max(float(axis) for axis in obj.dimensions) > max_dimension_m:
            continue
        if len(obj.data.polygons) < 20:
            continue
        if len(obj.data.color_attributes) > 0 or len(obj.vertex_groups) > 0:
            continue
        if any(modifier.type == "ARMATURE" for modifier in obj.modifiers):
            continue
        if getattr(obj.data, "has_custom_normals", False):
            continue
        if any(edge.use_seam or edge.use_edge_sharp for edge in obj.data.edges):
            continue
        if len({poly.material_index for poly in obj.data.polygons}) > 1:
            continue

        bm = bmesh.new()
        bm.from_mesh(obj.data)
        candidate_bm = bm.copy()
        old_count = len(candidate_bm.faces)
        bmesh.ops.dissolve_limit(
            candidate_bm,
            angle_limit=limit,
            use_dissolve_boundaries=False,
            verts=list(candidate_bm.verts),
            edges=list(candidate_bm.edges),
            delimit={"MATERIAL", "UV", "SHARP", "SEAM"},
        )
        new_count = len(candidate_bm.faces)
        if new_count < old_count:
            world_scale = max(float(value) for value in obj.matrix_world.to_scale())
            deviation_m = rmk.symmetric_bmesh_surface_deviation(
                bm, candidate_bm, world_scale=world_scale,
            )
            accumulated_deviation_m = rmk.get_accumulated_surface_deviation_m(obj)
            proposed_deviation_m = accumulated_deviation_m + deviation_m
            if proposed_deviation_m <= max_surface_deviation_m:
                bmesh.ops.recalc_face_normals(
                    candidate_bm, faces=list(candidate_bm.faces),
                )
                candidate_bm.to_mesh(obj.data)
                obj.data.update()
                accumulated_deviation_m = rmk.record_surface_deviation_m(obj, deviation_m)
                changed_meshes += 1
                faces_removed += old_count - new_count
                max_accepted_deviation_m = max(
                    max_accepted_deviation_m, accumulated_deviation_m,
                )
            else:
                geometry_rejections += 1
                max_rejected_deviation_m = max(
                    max_rejected_deviation_m, proposed_deviation_m,
                )
        candidate_bm.free()
        bm.free()

    summary = {
        "changed_meshes": changed_meshes,
        "faces_removed": faces_removed,
        "geometry_rejections": geometry_rejections,
        "max_surface_deviation_m": round(max_accepted_deviation_m, 6),
        "max_rejected_deviation_m": round(max_rejected_deviation_m, 6),
    }
    print(
        f"[visual-detail-budget] simplified {changed_meshes} small meshes; "
        f"removed {faces_removed} redundant bevel faces; "
        f"rejected {geometry_rejections} changes above "
        f"{max_surface_deviation_m:.3f} m "
        f"(max rejected {max_rejected_deviation_m:.3f} m)"
    )
    return summary


def set_material_color(material, color: Sequence[float], metallic=None, roughness=None) -> None:
    if material is None:
        return
    rgba = (float(color[0]), float(color[1]), float(color[2]), 1.0)
    material.diffuse_color = rgba
    if not material.use_nodes:
        material.use_nodes = True
    shader = next((n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if shader is None:
        return
    base = shader.inputs.get("Base Color")
    if base is not None:
        base.default_value = rgba
    if metallic is not None and shader.inputs.get("Metallic") is not None:
        shader.inputs["Metallic"].default_value = float(metallic)
    if roughness is not None and shader.inputs.get("Roughness") is not None:
        shader.inputs["Roughness"].default_value = float(roughness)


def hex_color(value: str) -> tuple[float, float, float]:
    value = value.strip().lstrip("#")
    if len(value) != 6:
        raise ValueError(f"Expected an RGB hex color, got {value!r}")
    return tuple(int(value[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def _make_mesh_object(name: str, vertices: Sequence[Sequence[float]],
                      faces: Sequence[Sequence[int]], material,
                      collection=None, smooth=False):
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    if material is not None:
        mesh.materials.append(material)
    for poly in mesh.polygons:
        poly.use_smooth = smooth
    obj = bpy.data.objects.new(name, mesh)
    (collection or bpy.context.collection).objects.link(obj)
    return obj


def add_loft_shell(builder, name: str, stations: Sequence[tuple[float, float, float, float]],
                    material, center_x: float = 0.0, exponent: float = 0.66,
                    smooth: bool = True, segments: int = 16, smooth_caps: bool = True,
                    smooth_side_ends: bool = False):
    """Build a continuous rounded, tapered armor shell from horizontal sections.

    Stations are (z, half-width, front-y, rear-y), with the vehicle nose at +Y.
    The superellipse footprint gives broad armor faces with softened corners.
    """
    if len(stations) < 2:
        raise ValueError(f"{name} needs at least two shell stations")
    segments = max(8, int(segments))
    vertices = []
    for z, half_width, front_y, rear_y in stations:
        if half_width <= 0 or front_y <= rear_y:
            raise ValueError(f"Invalid shell station for {name}: {(z, half_width, front_y, rear_y)}")
        center_y = (front_y + rear_y) * 0.5
        half_length = (front_y - rear_y) * 0.5
        for i in range(segments):
            angle = 2.0 * math.pi * i / segments
            c, s = math.cos(angle), math.sin(angle)
            x = math.copysign(abs(c) ** exponent, c) * half_width
            y = math.copysign(abs(s) ** exponent, s) * half_length
            vertices.append((center_x + x, center_y + y, z))
    faces = [tuple(reversed(range(segments))), tuple((len(stations) - 1) * segments + i
                                                     for i in range(segments))]
    for ring in range(len(stations) - 1):
        a = ring * segments
        b = (ring + 1) * segments
        for i in range(segments):
            n = (i + 1) % segments
            faces.append((a + i, a + n, b + n, b + i))
    obj = _make_mesh_object(name, vertices, faces, material, smooth=smooth)
    if smooth:
        if not smooth_caps:
            for polygon in obj.data.polygons[:2]:
                polygon.use_smooth = False
        if smooth_side_ends:
            for index, polygon in enumerate(obj.data.polygons[2:]):
                angle = 2.0 * math.pi * ((index % segments) + 0.5) / segments
                polygon.use_smooth = abs(math.cos(angle)) < 0.7071
    builder.objects.append(obj)
    return obj


def add_box(builder, name: str, center: Sequence[float], size: Sequence[float], material,
            rotation_z: float = 0.0):
    cx, cy, cz = map(float, center)
    sx, sy, sz = (float(v) * 0.5 for v in size)
    ca, sa = math.cos(rotation_z), math.sin(rotation_z)
    vertices = []
    for x, y, z in ((-sx, -sy, -sz), (sx, -sy, -sz), (sx, sy, -sz), (-sx, sy, -sz),
                    (-sx, -sy, sz), (sx, -sy, sz), (sx, sy, sz), (-sx, sy, sz)):
        vertices.append((cx + x * ca - y * sa, cy + x * sa + y * ca, cz + z))
    faces = [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4),
             (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    obj = _make_mesh_object(name, vertices, faces, material)
    builder.objects.append(obj)
    return obj


def add_superellipse_side_panel(
    builder,
    name: str,
    stations: Sequence[Sequence[float]],
    side: int,
    y_start: float,
    y_end: float,
    z_bottom: float,
    z_top: float,
    material,
    *,
    exponent: float = 0.66,
    thickness: float = 0.035,
    embed: float = 0.012,
    surface_offset: float = 0.0,
    y_segments: int = 8,
    z_segments: int = 6,
    host_floor_z: float | None = None,
    floor_width_ratio: float = 0.96,
):
    """Build a low-relief side plate that follows a Y-lofted superellipse hull.

    Stations use ``(y, center_x, center_z, half_width, half_height)``. The
    plate's back surface embeds into the sampled host by ``embed`` and its
    front surface follows it at ``surface_offset + thickness - embed``.
    """
    if side not in (-1, 1):
        raise ValueError("side must be -1 or 1")
    if (exponent <= 0.0 or thickness <= 0.0 or embed < 0.0 or embed > thickness
            or y_start >= y_end or z_bottom >= z_top):
        raise ValueError(f"Invalid side-panel dimensions for {name}")
    if y_segments < 1 or z_segments < 1 or len(stations) < 2:
        raise ValueError(f"Invalid side-panel sampling for {name}")

    normalized = [tuple(map(float, station)) for station in stations]
    if any(len(station) != 5 for station in normalized):
        raise ValueError("each Y-loft station must contain five values")
    if any(b[0] <= a[0] for a, b in zip(normalized, normalized[1:])):
        raise ValueError("Y-loft station positions must increase monotonically")
    if y_start < normalized[0][0] or y_end > normalized[-1][0]:
        raise ValueError(f"Side panel {name} extends past the Y-loft station range")

    def section_at_y(y: float) -> tuple[float, float, float, float]:
        for lower, upper in zip(normalized, normalized[1:]):
            if lower[0] <= y <= upper[0]:
                t = (y - lower[0]) / (upper[0] - lower[0])
                return tuple(
                    a + (b - a) * t for a, b in zip(lower[1:], upper[1:])
                )
        return tuple(normalized[-1][1:])

    y_values = sorted({
        *(y_start + (y_end - y_start) * index / y_segments for index in range(y_segments + 1)),
        *(station[0] for station in normalized if y_start < station[0] < y_end),
    })
    z_values = [
        z_bottom + (z_top - z_bottom) * index / z_segments
        for index in range(z_segments + 1)
    ]

    back_vertices: list[tuple[float, float, float]] = []
    front_vertices: list[tuple[float, float, float]] = []
    for y in y_values:
        center_x, center_z, half_width, half_height = section_at_y(y)
        if half_width <= 0.0 or half_height <= 0.0:
            raise ValueError(f"Y-loft section for {name} has nonpositive radii")
        for z in z_values:
            vertical = (z - center_z) / half_height
            if abs(vertical) > 1.0 + 1e-6:
                raise ValueError(f"Side panel {name} leaves the host profile at y={y:.4f}, z={z:.4f}")
            host_x = sample_superellipse_loft_side_x(
                normalized,
                y,
                z,
                side,
                exponent=exponent,
                segments=16,
                flat_floor_z=host_floor_z,
                floor_width_ratio=floor_width_ratio,
            )
            back_x = host_x + side * (surface_offset - embed)
            front_x = host_x + side * (surface_offset + thickness - embed)
            back_vertices.append((back_x, y, z))
            front_vertices.append((front_x, y, z))

    point_count = len(back_vertices)
    z_count = len(z_values)
    faces: list[tuple[int, ...]] = []
    for y_index in range(len(y_values) - 1):
        for z_index in range(z_count - 1):
            lower = y_index * z_count + z_index
            upper = (y_index + 1) * z_count + z_index
            faces.append((lower, upper, upper + 1, lower + 1))
            faces.append((point_count + lower, point_count + lower + 1,
                          point_count + upper + 1, point_count + upper))
    for y_index in range(len(y_values) - 1):
        lower = y_index * z_count
        upper = (y_index + 1) * z_count
        faces.append((lower, point_count + lower, point_count + upper, upper))
        lower = y_index * z_count + z_count - 1
        upper = (y_index + 1) * z_count + z_count - 1
        faces.append((lower, upper, point_count + upper, point_count + lower))
    for z_index in range(z_count - 1):
        lower = z_index
        upper = z_index + 1
        faces.append((lower, upper, point_count + upper, point_count + lower))
        lower = (len(y_values) - 1) * z_count + z_index
        upper = lower + 1
        faces.append((lower, point_count + lower, point_count + upper, upper))

    obj = _make_mesh_object(
        name,
        [*back_vertices, *front_vertices],
        faces,
        material,
        smooth=False,
    )
    builder.objects.append(obj)
    return obj


def make_flat_bottom_superellipse_loft_shell(
    name: str,
    stations: Sequence[Sequence[float]],
    flat_floor_z: float | Sequence[float],
    materials: Sequence[Any],
    *,
    band_start_y: float,
    exponent: float = 0.66,
    segments: int = 16,
    floor_width_ratio: float = 0.94,
):
    """Build a faceted Y-loft with a supported, chamfered flat underside.

    The upper half follows the same superellipse profile as
    ``make_lofted_superellipse_shell``. Its lower half resolves into two
    chamfered shoulders and a three-point flat floor, which can seat against a
    mounting coaming without rounding through or below the support surface.
    Faces before ``band_start_y`` use material 0; faces at or beyond it use
    material 1 when both are supplied.
    """
    normalized = [tuple(map(float, station)) for station in stations]
    if len(normalized) < 2 or any(len(station) != 5 for station in normalized):
        raise ValueError("a flat-bottom loft needs at least two five-value stations")
    if any(b[0] <= a[0] for a, b in zip(normalized, normalized[1:])):
        raise ValueError("Y-loft station positions must increase monotonically")
    if segments < 8 or segments % 4 or exponent <= 0.0:
        raise ValueError("superellipse sampling requires exponent > 0 and segments divisible by four")
    if not 0.0 < floor_width_ratio < 1.0:
        raise ValueError("floor_width_ratio must be in the range (0, 1)")
    if isinstance(flat_floor_z, (int, float)):
        floor_levels = [float(flat_floor_z)] * len(normalized)
    else:
        floor_levels = [float(value) for value in flat_floor_z]
    if len(floor_levels) != len(normalized):
        raise ValueError("flat_floor_z must contain one level per loft station")
    if any(station[2] <= floor or station[3] <= 0.0 or station[4] <= 0.0
           for station, floor in zip(normalized, floor_levels)):
        raise ValueError("every loft section must rise above the flat floor and have positive radii")

    half_segments = segments // 2
    ring_size = half_segments + 4  # upper arc, left shoulder, split floor, right shoulder
    vertices: list[tuple[float, float, float]] = []
    for (y, center_x, center_z, half_width, half_height), floor_z in zip(normalized, floor_levels):
        for index in range(half_segments + 1):
            angle = math.pi * index / half_segments
            cosine = math.cos(angle)
            sine = math.sin(angle)
            unit_x = math.copysign(abs(cosine) ** exponent, cosine)
            unit_z = math.copysign(abs(sine) ** exponent, sine)
            vertices.append((center_x + unit_x * half_width, y, center_z + unit_z * half_height))
        floor_half_width = floor_width_ratio * half_width
        vertices.extend((
            (center_x - floor_half_width, y, floor_z),
            (center_x, y, floor_z),
            (center_x + floor_half_width, y, floor_z),
        ))

    face_specs: list[tuple[tuple[int, ...], bool]] = []
    for ring_index in (0, len(normalized) - 1):
        center_index = len(vertices)
        y, center_x, center_z, _half_width, _half_height = normalized[ring_index]
        floor_z = floor_levels[ring_index]
        vertices.append((center_x, y, (center_z + floor_z) * 0.5))
        ring_start = ring_index * ring_size
        for index in range(ring_size):
            following = (index + 1) % ring_size
            face_specs.append(((center_index, ring_start + index, ring_start + following), False))

    for ring_index in range(len(normalized) - 1):
        lower = ring_index * ring_size
        upper = (ring_index + 1) * ring_size
        for index in range(ring_size):
            following = (index + 1) % ring_size
            face_specs.append(((lower + index, lower + following,
                                upper + following, upper + index), index < half_segments))

    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(vertices, [], [face for face, _smooth in face_specs])
    mesh.update()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    for face in bm.faces:
        if len(materials) > 1:
            face.material_index = int(face.calc_center_median().y >= band_start_y)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    for material in materials:
        if material is not None:
            mesh.materials.append(material)
    for polygon in mesh.polygons:
        polygon.use_smooth = (
            polygon.index < len(face_specs)
            and face_specs[polygon.index][1]
            and abs(polygon.normal.y) < 0.5
        )
    obj.select_set(False)
    return obj


def _superellipse_section_at_y(stations: Sequence[Sequence[float]], y: float):
    normalized = [tuple(map(float, station)) for station in stations]
    for lower, upper in zip(normalized, normalized[1:]):
        if lower[0] <= y <= upper[0]:
            t = (y - lower[0]) / (upper[0] - lower[0])
            return tuple(a + (b - a) * t for a, b in zip(lower[1:], upper[1:]))
    if y == normalized[-1][0]:
        return tuple(normalized[-1][1:])
    raise ValueError(f"Y={y:.4f} is outside the superellipse loft station range")


def sample_superellipse_loft_side_x(
    stations: Sequence[Sequence[float]],
    y: float,
    z: float,
    side: int,
    *,
    exponent: float = 0.66,
    segments: int = 16,
    flat_floor_z: float | None = None,
    floor_width_ratio: float = 0.96,
) -> float:
    """Sample a faceted side wall, optionally including a chamfered flat floor."""
    if side not in (-1, 1):
        raise ValueError("side must be -1 or 1")
    if exponent <= 0.0 or segments < 8 or segments % 4:
        raise ValueError("superellipse sampling requires exponent > 0 and segments divisible by four")
    if not 0.0 < floor_width_ratio <= 1.0:
        raise ValueError("floor_width_ratio must be in the range (0, 1]")
    center_x, center_z, half_width, half_height = _superellipse_section_at_y(stations, y)
    z_delta = float(z) - center_z
    if flat_floor_z is not None and z_delta < 0.0:
        if float(z) < flat_floor_z - 1e-6 or center_z <= flat_floor_z:
            raise ValueError(f"Z={z:.4f} is outside the flattened loft section at Y={y:.4f}")
        t = (float(z) - flat_floor_z) / (center_z - flat_floor_z)
        unit_x = floor_width_ratio + (1.0 - floor_width_ratio) * t
        return center_x + side * unit_x * half_width
    z_ratio = z_delta / half_height if flat_floor_z is not None else abs(z_delta / half_height)
    if z_ratio > 1.0 + 1e-6:
        raise ValueError(f"Z={z:.4f} is outside the loft section at Y={y:.4f}")
    z_ratio = min(1.0, z_ratio)
    quadrant_segments = segments // 4
    profile = []
    for index in range(quadrant_segments + 1):
        angle = (math.pi * 0.5) * index / quadrant_segments
        profile.append((math.cos(angle) ** exponent, math.sin(angle) ** exponent))
    for (x0, z0), (x1, z1) in zip(profile, profile[1:]):
        if z0 - 1e-9 <= z_ratio <= z1 + 1e-9:
            t = 0.0 if abs(z1 - z0) < 1e-12 else (z_ratio - z0) / (z1 - z0)
            unit_x = x0 + (x1 - x0) * t
            return center_x + side * unit_x * half_width
    return center_x + side * half_width


def sample_superellipse_loft_top_z(
    stations: Sequence[Sequence[float]],
    x: float,
    y: float,
    *,
    exponent: float = 0.66,
    segments: int = 16,
) -> float:
    """Sample the faceted top surface used by ``make_lofted_superellipse_shell``."""
    if exponent <= 0.0 or segments < 8 or segments % 4:
        raise ValueError("superellipse sampling requires exponent > 0 and segments divisible by four")
    center_x, center_z, half_width, half_height = _superellipse_section_at_y(stations, y)
    x_ratio = abs((float(x) - center_x) / half_width)
    if x_ratio > 1.0 + 1e-6:
        raise ValueError(f"X={x:.4f} is outside the loft section at Y={y:.4f}")
    x_ratio = min(1.0, x_ratio)
    quadrant_segments = segments // 4
    profile = []
    for index in range(quadrant_segments + 1):
        angle = (math.pi * 0.5) * index / quadrant_segments
        unit_x = math.cos(angle) ** exponent
        unit_z = math.sin(angle) ** exponent
        profile.append((unit_x, unit_z))
    for (x0, z0), (x1, z1) in zip(profile, profile[1:]):
        if x0 + 1e-9 >= x_ratio >= x1 - 1e-9:
            t = 0.0 if abs(x1 - x0) < 1e-12 else (x_ratio - x0) / (x1 - x0)
            unit_z = z0 + (z1 - z0) * t
            return center_z + unit_z * half_height
    return center_z + half_height


def add_superellipse_roof_leveler(
    builder,
    name: str,
    stations: Sequence[Sequence[float]],
    bounds: Sequence[float],
    top_z: float,
    material,
    *,
    exponent: float = 0.66,
    embed: float = 0.002,
    segments: int = 16,
    x_segments: int = 8,
    y_segments: int = 8,
):
    """Create a closed, gridded shoe that seats a flat mount on a lofted roof.

    ``bounds`` is ``(x_min, x_max, y_min, y_max)`` in the same local space as
    the shell stations. The underside follows the shell's faceted cross-section
    and sits ``embed`` inside it; the top remains level at ``top_z``.
    """
    if len(bounds) != 4:
        raise ValueError("roof leveler bounds must contain x_min, x_max, y_min, y_max")
    x_min, x_max, y_min, y_max = map(float, bounds)
    if x_min >= x_max or y_min >= y_max or embed <= 0.0:
        raise ValueError(f"Invalid roof leveler dimensions for {name}")
    if x_segments < 1 or y_segments < 1:
        raise ValueError("roof leveler sampling must use at least one segment per axis")
    if y_min < float(stations[0][0]) or y_max > float(stations[-1][0]):
        raise ValueError(f"Roof leveler {name} extends past the Y-loft station range")

    x_values = [x_min + (x_max - x_min) * i / x_segments for i in range(x_segments + 1)]
    y_values = sorted({
        *(y_min + (y_max - y_min) * i / y_segments for i in range(y_segments + 1)),
        *(float(station[0]) for station in stations if y_min < float(station[0]) < y_max),
    })
    bottom_vertices = []
    top_vertices = []
    for y in y_values:
        for x in x_values:
            roof_z = sample_superellipse_loft_top_z(
                stations, x, y, exponent=exponent, segments=segments,
            )
            bottom_vertices.append((x, y, roof_z - embed))
            top_vertices.append((x, y, float(top_z)))
    if any(float(top_z) <= point[2] for point in bottom_vertices):
        raise ValueError(f"Roof leveler {name} does not rise above its fitted roof")

    row_width = len(x_values)
    row_count = len(y_values)
    point_count = row_width * row_count
    vertices = [*bottom_vertices, *top_vertices]
    faces = []
    for y_index in range(row_count - 1):
        for x_index in range(row_width - 1):
            lower = y_index * row_width + x_index
            upper = lower + row_width
            faces.append((point_count + lower, point_count + lower + 1,
                          point_count + upper + 1, point_count + upper))
            faces.append((lower, upper, upper + 1, lower + 1))
    for y_index in range(row_count - 1):
        lower = y_index * row_width
        upper = lower + row_width
        faces.append((lower, point_count + lower, point_count + upper, upper))
        lower = y_index * row_width + row_width - 1
        upper = lower + row_width
        faces.append((lower, upper, point_count + upper, point_count + lower))
    for x_index in range(row_width - 1):
        lower = x_index
        upper = x_index + 1
        faces.append((lower, upper, point_count + upper, point_count + lower))
        lower = (row_count - 1) * row_width + x_index
        upper = lower + 1
        faces.append((lower, point_count + lower, point_count + upper, upper))

    obj = _make_mesh_object(name, vertices, faces, material, smooth=False)
    builder.objects.append(obj)
    return obj


def add_uv_ellipsoid(builder, name: str, center: Sequence[float], radii: Sequence[float], material,
                     segments: int = 16, rings: int = 8):
    cx, cy, cz = map(float, center)
    rx, ry, rz = map(float, radii)
    vertices = []
    for ring in range(rings + 1):
        theta = math.pi * ring / rings
        z = math.cos(theta) * rz
        radial = math.sin(theta)
        for seg in range(segments):
            phi = 2.0 * math.pi * seg / segments
            vertices.append((cx + math.cos(phi) * radial * rx,
                             cy + math.sin(phi) * radial * ry,
                             cz + z))
    faces = []
    for ring in range(rings):
        for seg in range(segments):
            a = ring * segments + seg
            b = ring * segments + (seg + 1) % segments
            c = (ring + 1) * segments + (seg + 1) % segments
            d = (ring + 1) * segments + seg
            faces.append((a, b, c, d))
    obj = _make_mesh_object(name, vertices, faces, material, smooth=True)
    builder.objects.append(obj)
    return obj


def add_y_axis_tube(builder, name: str, center: Sequence[float], radius: float, length: float,
                    material, segments: int = 12):
    cx, cy, cz = map(float, center)
    vertices = []
    for y in (cy - length * 0.5, cy + length * 0.5):
        for i in range(segments):
            angle = 2.0 * math.pi * i / segments
            vertices.append((cx + math.cos(angle) * radius, y, cz + math.sin(angle) * radius))
    faces = [tuple(reversed(range(segments))), tuple(segments + i for i in range(segments))]
    for i in range(segments):
        n = (i + 1) % segments
        faces.append((i, n, segments + n, segments + i))
    obj = _make_mesh_object(name, vertices, faces, material, smooth=True)
    builder.objects.append(obj)
    return obj


def add_z_axis_post(builder, name: str, center: Sequence[float], radius: float,
                    height: float, material, sides: int = 10):
    cx, cy, cz = map(float, center)
    vertices = []
    for z in (cz - height * 0.5, cz + height * 0.5):
        for i in range(sides):
            angle = 2.0 * math.pi * i / sides
            vertices.append((cx + math.cos(angle) * radius,
                             cy + math.sin(angle) * radius, z))
    faces = [tuple(reversed(range(sides))), tuple(sides + i for i in range(sides))]
    for i in range(sides):
        n = (i + 1) % sides
        faces.append((i, n, sides + n, sides + i))
    obj = _make_mesh_object(name, vertices, faces, material, smooth=True)
    builder.objects.append(obj)
    return obj


def add_link(builder, name: str, start: Sequence[float], end: Sequence[float],
             width: float, thickness: float, material):
    a, b = Vector(start), Vector(end)
    axis = (b - a).normalized()
    side = Vector((0.0, 0.0, 1.0)).cross(axis)
    if side.length < 1e-5:
        side = Vector((1.0, 0.0, 0.0)).cross(axis)
    side.normalize()
    normal = axis.cross(side).normalized()
    profile = ((0.0, 0.72), (0.12, 1.0), (0.88, 1.0), (1.0, 0.72))
    segments = 8
    vertices = []
    for t, scale in profile:
        center = a.lerp(b, t)
        for i in range(segments):
            ang = 2.0 * math.pi * i / segments
            x = math.copysign(abs(math.cos(ang)) ** 0.72, math.cos(ang)) * width * scale
            y = math.copysign(abs(math.sin(ang)) ** 0.72, math.sin(ang)) * thickness * scale
            vertices.append(tuple(center + side * x + normal * y))
    faces = [tuple(reversed(range(segments))),
             tuple((len(profile) - 1) * segments + i for i in range(segments))]
    for ring in range(len(profile) - 1):
        a0, b0 = ring * segments, (ring + 1) * segments
        for i in range(segments):
            n = (i + 1) % segments
            faces.append((a0 + i, a0 + n, b0 + n, b0 + i))
    obj = _make_mesh_object(name, vertices, faces, material, smooth=True)
    builder.objects.append(obj)
    return obj


def replace_link_mesh(obj, start: Sequence[float], end: Sequence[float], width: float,
                      thickness: float, material, ring_segments: int = 8,
                      exponent: float = 0.72, smooth: bool = True):
    a, b = Vector(start), Vector(end)
    axis = (b - a).normalized()
    side = Vector((0.0, 0.0, 1.0)).cross(axis)
    if side.length < 1e-5:
        side = Vector((1.0, 0.0, 0.0)).cross(axis)
    side.normalize()
    normal = axis.cross(side).normalized()
    profile = ((0.0, 0.66), (0.12, 1.0), (0.78, 0.90), (1.0, 0.62))
    ring_segments = max(8, int(ring_segments))
    vertices = []
    for t, scale in profile:
        center = a.lerp(b, t)
        for i in range(ring_segments):
            ang = 2.0 * math.pi * i / ring_segments
            x = math.copysign(abs(math.cos(ang)) ** exponent, math.cos(ang)) * width * scale
            y = math.copysign(abs(math.sin(ang)) ** exponent, math.sin(ang)) * thickness * scale
            p = center + side * x + normal * y
            vertices.append(tuple(p))
    faces = [tuple(reversed(range(ring_segments))),
             tuple((len(profile) - 1) * ring_segments + i for i in range(ring_segments))]
    for ring in range(len(profile) - 1):
        a0, b0 = ring * ring_segments, (ring + 1) * ring_segments
        for i in range(ring_segments):
            n = (i + 1) % ring_segments
            faces.append((a0 + i, a0 + n, b0 + n, b0 + i))
    mesh = bpy.data.meshes.new(obj.name + "_RefinedMesh")
    mesh.from_pydata(vertices, [], faces)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    mesh.materials.append(material)
    old = obj.data
    obj.data = mesh
    obj.location = (0.0, 0.0, 0.0)
    obj.rotation_euler = (0.0, 0.0, 0.0)
    obj.scale = (1.0, 1.0, 1.0)
    if old.users == 0:
        bpy.data.meshes.remove(old)
    for poly in mesh.polygons:
        poly.use_smooth = smooth


def simplify_all_terrain_wheels(builder) -> int:
    """Keep rugged wheel silhouettes while removing tread lugs and preserving hub bolts."""
    tires = [o for o in builder.objects if o.type == "MESH" and o.name.endswith("_TireCarcass")]
    removed = remove_objects(builder, lambda n: "_TreadLug_" in n)
    for tire in tires:
        c = tire.matrix_world.translation.copy()
        vertices = []
        faces = []
        radius = min(tire.dimensions.y, tire.dimensions.z) * 0.5 - 0.025
        half_x = tire.dimensions.x * 0.48
        phase = 0.11 if "MidFront" in tire.name else 0.0
        for k in range(8):
            ang = 2.0 * math.pi * k / 8.0 + phase
            radial = Vector((0.0, math.sin(ang), math.cos(ang)))
            tangent = Vector((0.0, math.cos(ang), -math.sin(ang)))
            # The original tire carcass sits slightly above the ground plane; let
            # the tread blocks make first contact without lowering the wheel pivot.
            center = c + radial * (radius + 0.035)
            start = len(vertices)
            for xsign, tsign, rsign in ((-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1),
                                        (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)):
                p = center + Vector((xsign * half_x, 0.0, 0.0)) + tangent * (tsign * 0.068) + radial * (rsign * 0.04)
                vertices.append(tuple(p))
            faces.extend([(start + 3, start + 2, start + 1, start),
                          (start + 4, start + 5, start + 6, start + 7),
                          (start, start + 1, start + 5, start + 4),
                          (start + 1, start + 2, start + 6, start + 5),
                          (start + 2, start + 3, start + 7, start + 6),
                          (start + 3, start, start + 4, start + 7)])
        tread = _make_mesh_object(tire.name.replace("_TireCarcass", "_TreadBlocks"),
                                  vertices, faces, builder.palette.tread)
        builder.objects.append(tread)
    return len(removed)


def validate_root_motion(armature: Any, walk_action: Any, unit_label: str = "Unit") -> None:
    """Validate zero-translation invariant on Bone_Root during Action_Walk."""
    root_path = 'pose.bones["Bone_Root"].location'
    root_curves = [curve for curve in walk_action.fcurves if curve.data_path == root_path]
    if len(root_curves) != 3:
        raise ValueError(f"{unit_label} Action_Walk must key all three Bone_Root location axes")
    for curve in root_curves:
        if any(abs(float(point.co.y)) > 1e-6 for point in curve.keyframe_points):
            raise ValueError(f"{unit_label} Action_Walk violates the zero-root-translation invariant")
    if getattr(armature, "animation_data", None) is None:
        raise ValueError(f"{unit_label} armature has no animation data")


def validate_rigid_weights(meshes: Sequence[Any], armature: Any) -> None:
    """Validate that mesh vertices are rigidly bound (weight = 1.0) to exactly one bone."""
    bone_names = {bone.name for bone in armature.data.bones}
    for mesh in meshes:
        if getattr(mesh, "type", None) != "MESH" or mesh.name.endswith(("_COLLIDER", "_ShieldBubble")):
            continue
        groups = {group.index: group.name for group in mesh.vertex_groups}
        for vertex in mesh.data.vertices:
            influences = [
                (groups[assignment.group], assignment.weight)
                for assignment in vertex.groups
                if assignment.group in groups and assignment.weight > 1e-6
            ]
            if len(influences) != 1 or abs(influences[0][1] - 1.0) > 1e-5:
                raise ValueError(f"{mesh.name} has non-rigid vertex weights at vertex {vertex.index}: {influences}")
            if influences[0][0] not in bone_names:
                raise ValueError(f"{mesh.name} references missing bone {influences[0][0]}")


def create_canonical_sockets(socket_defs: Mapping[str, Sequence[float]]) -> None:
    """Create canonical socket empties and FirePoint_0 child if they do not exist."""
    for name, loc in socket_defs.items():
        if name not in bpy.data.objects:
            empty = bpy.data.objects.new(name, None)
            empty.empty_display_size = 0.1
            bpy.context.collection.objects.link(empty)
            empty.location = tuple(loc)
    if "FirePoint_0" not in bpy.data.objects and "Socket_MuzzlePrimary" in bpy.data.objects:
        fp = bpy.data.objects.new("FirePoint_0", None)
        fp.empty_display_size = 0.05
        bpy.context.collection.objects.link(fp)
        fp.parent = bpy.data.objects["Socket_MuzzlePrimary"]
        fp.location = (0.0, 0.0, 0.0)
