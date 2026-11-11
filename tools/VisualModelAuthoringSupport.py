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
    max_faces: int | None = None,
    max_dimension_m: float = 0.60,
) -> dict[str, Any]:
    """Apply RogueModelKit's shared, protected fitting simplification pass."""
    import RogueModelKit as rmk

    target_faces = max_faces
    if target_faces is None:
        allocation = rmk.resolve_geometry_budget(builder.name)
        target_faces = int(allocation["lods"][0]["targetFacesPerInstance"])
    return rmk.reduce_subpixel_details_to_budget(
        builder.objects,
        max_faces=target_faces,
        max_dimension_m=max_dimension_m,
    )


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
    """Keep eight rugged wheel silhouettes while removing tiny repeated lug detail."""
    tires = [o for o in builder.objects if o.type == "MESH" and o.name.endswith("_TireCarcass")]
    removed = remove_objects(builder, lambda n: "_TreadLug_" in n or "_LugBolt_" in n)
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
