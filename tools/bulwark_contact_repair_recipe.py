"""Repair unsupported visible Bulwark Siege Tank details on an isolated copy.

The shared recipe uses Blender and RogueModelKit geometry only. Its optional
``hardflow_bevel`` callback lets the isolated review pilot author selected
mount edges with Hardflow; the standard builder remains independent of the
extension and replays the same object placements with built-in bevels.
"""

from __future__ import annotations

import math
from typing import Callable, Iterable

import bpy
import bmesh
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

import RogueModelKit as rmk
from bulwark_radiator_vent_recipe import seat_bulwark_radiator_assembly
from hard_surface_geometry_presets import (
    ERA_BRICK_PRESET_ID,
    SERVICE_HATCH_PRESET_ID,
    EraBrickConfig,
    ServiceHatchConfig,
    apply_service_hatch_to_existing_parts,
    create_era_brick_preset,
)


RECIPE_ID = "bulwark-visible-supports-v13-side-mounts"
SCENE_MARKER = "rcl_bulwark_visible_supports_recipe"
DETAIL_MARKER = "rcl_bulwark_visible_supports_detail"
UNIT_PREFIX = "BulwarkSiegeTank"

HardflowBevel = Callable[[bpy.types.Object], None]
HardflowBoolean = Callable[[bpy.types.Object, bpy.types.Object], str]


def _object(objects: Iterable[bpy.types.Object], name: str) -> bpy.types.Object:
    result = next((obj for obj in objects if obj and obj.name == name and obj.type == "MESH"), None)
    if result is None:
        raise ValueError(f"Bulwark contact recipe requires mesh {name!r}")
    return result


def _material(objects: Iterable[bpy.types.Object], key: str) -> bpy.types.Material | None:
    obj = next((candidate for candidate in objects if key in candidate.name), None)
    return obj.data.materials[0] if obj and obj.data.materials else None


def _bounds(obj: bpy.types.Object) -> tuple[Vector, Vector]:
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return (Vector(min(v[i] for v in corners) for i in range(3)),
            Vector(max(v[i] for v in corners) for i in range(3)))


def _mesh_geometry(obj: bpy.types.Object) -> tuple[list[Vector], list[tuple[int, ...]], list[tuple[int, int]]]:
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh(preserve_all_data_layers=False, depsgraph=depsgraph)
    matrix = evaluated.matrix_world.copy()
    try:
        vertices = [matrix @ vertex.co for vertex in mesh.vertices]
        # BVHTree.FromPolygons(all_triangles=True) must receive actual triangles.
        # Passing source ngons with all_triangles=False produced unreliable
        # nearest points/ray fits on the Bulwark hull and gave false mount gaps.
        mesh.calc_loop_triangles()
        faces = [tuple(triangle.vertices) for triangle in mesh.loop_triangles]
        edges = [tuple(edge.vertices) for edge in mesh.edges]
    finally:
        evaluated.to_mesh_clear()
    return vertices, faces, edges


def _samples(geometry: tuple[list[Vector], list[tuple[int, ...]], list[tuple[int, int]]]) -> list[Vector]:
    vertices, faces, edges = geometry
    return (vertices
            + [sum((vertices[i] for i in face), Vector()) / len(face) for face in faces]
            + [(vertices[a] + vertices[b]) * 0.5 for a, b in edges])


def _tree(geometry: tuple[list[Vector], list[tuple[int, ...]], list[tuple[int, int]]]) -> BVHTree:
    return BVHTree.FromPolygons(geometry[0], geometry[1], all_triangles=True, epsilon=0.0)


def _nearest_object_point(obj: bpy.types.Object, point: Vector) -> tuple[Vector, Vector, int, float]:
    hit = _tree(_mesh_geometry(obj)).find_nearest(point)
    if not hit or hit[3] is None:
        raise ValueError(f"Cannot find nearest surface point on {obj.name}")
    return hit[0].copy(), hit[1].copy(), int(hit[2]), float(hit[3])


def _closest_surface_points(a: bpy.types.Object, b: bpy.types.Object) -> tuple[Vector, Vector, float]:
    ga, gb = _mesh_geometry(a), _mesh_geometry(b)
    ta, tb = _tree(ga), _tree(gb)
    best = (float("inf"), Vector(), Vector())
    for source, target, reverse in ((_samples(ga), tb, False), (_samples(gb), ta, True)):
        for point in source:
            hit = target.find_nearest(point)
            if hit and hit[3] is not None and float(hit[3]) < best[0]:
                if reverse:
                    best = (float(hit[3]), hit[0].copy(), point.copy())
                else:
                    best = (float(hit[3]), point.copy(), hit[0].copy())
                if best[0] <= 1e-7:
                    return best[1], best[2], 0.0
    return best[1], best[2], best[0]


def _surface_gap(a: bpy.types.Object, b: bpy.types.Object) -> float:
    return _closest_surface_points(a, b)[2]


def _contact_face_clearance(source: bpy.types.Object, target: bpy.types.Object,
                            face_normal: Vector) -> dict[str, float | int]:
    """Sample a mounting face against its support along the face normal.

    Signed positive values mean the support surface is behind the sampled
    mount face; negative values mean the face is buried into the support. The
    reported absolute p95/max are the local fit error across the mounting face.
    """
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = source.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh(preserve_all_data_layers=False, depsgraph=depsgraph)
    matrix = evaluated.matrix_world.copy()
    normal_matrix = matrix.to_3x3()
    axis = face_normal.normalized()
    target_tree = _tree(_mesh_geometry(target))
    points: list[Vector] = []
    try:
        world_vertices = [matrix @ vertex.co for vertex in mesh.vertices]
        for poly in mesh.polygons:
            normal = (normal_matrix @ poly.normal).normalized()
            if normal.dot(axis) < 0.95:
                continue
            indices = list(poly.vertices)
            points.append(sum((world_vertices[i] for i in indices), Vector()) / len(indices))
            points.extend(world_vertices[i] for i in indices)
            for index, start in enumerate(indices):
                end = indices[(index + 1) % len(indices)]
                points.append((world_vertices[start] + world_vertices[end]) * 0.5)
        signed_gaps = []
        misses = 0
        ray_offset = 0.050
        for point in points:
            hit = target_tree.ray_cast(point + axis * ray_offset, -axis)
            if hit and hit[3] is not None:
                signed_gaps.append((float(hit[3]) - ray_offset) * 1000.0)
            else:
                misses += 1
    finally:
        evaluated.to_mesh_clear()
    if not signed_gaps:
        return {"sampleCount": 0, "missCount": misses, "p95AbsFitErrorMm": float("inf"),
                "maxAbsFitErrorMm": float("inf"), "minSignedMm": float("nan"), "maxSignedMm": float("nan")}
    absolute = sorted(abs(value) for value in signed_gaps)
    index95 = min(len(absolute) - 1, int(math.ceil(len(absolute) * 0.95)) - 1)
    return {"sampleCount": len(signed_gaps), "missCount": misses,
            "p95AbsFitErrorMm": absolute[index95], "maxAbsFitErrorMm": absolute[-1],
            "minSignedMm": min(signed_gaps), "maxSignedMm": max(signed_gaps)}


def _cut_hatch_rebate(
    upper: bpy.types.Object,
    coaming: bpy.types.Object,
    hardflow_boolean_apply: HardflowBoolean | None = None,
) -> dict[str, object]:
    """Cut a shallow deck pocket for the lowered, hull-mounted hatch coaming."""
    before = _bounds(upper)
    coaming_min, coaming_max = _bounds(coaming)
    center = (coaming_min + coaming_max) * 0.5
    deck_top = before[1].z
    pocket_depth = 0.025
    margin = 0.012
    size = (coaming_max.x - coaming_min.x + margin * 2.0,
            coaming_max.y - coaming_min.y + margin * 2.0,
            pocket_depth)

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(center.x, center.y, deck_top - pocket_depth * 0.5))
    cutter = bpy.context.object
    cutter.name = "BulwarkHF_Cutter_AccessHatchRebate"
    cutter.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bevel = cutter.modifiers.new("BulwarkHatchRebate_BuiltInBevel", "BEVEL")
    bevel.width = 0.006
    bevel.segments = 2
    bevel.limit_method = "ANGLE"
    bevel.angle_limit = math.radians(20.0)
    bpy.context.view_layer.objects.active = cutter
    with bpy.context.temp_override(
        object=cutter,
        active_object=cutter,
        selected_objects=[cutter],
        selected_editable_objects=[cutter],
    ):
        bpy.ops.object.modifier_apply(modifier=bevel.name)

    if hardflow_boolean_apply is not None:
        boolean_solver = hardflow_boolean_apply(upper, cutter)
        boolean_backend = "Hardflow 1.23.0"
    else:
        boolean = upper.modifiers.new("RCL_Bulwark_AccessHatchRebate_BOOLEAN", "BOOLEAN")
        boolean.operation = "DIFFERENCE"
        boolean.solver = "EXACT"
        boolean.object = cutter
        bpy.context.view_layer.update()
        bpy.ops.object.select_all(action="DESELECT")
        upper.select_set(True)
        bpy.context.view_layer.objects.active = upper
        with bpy.context.temp_override(
            object=upper,
            active_object=upper,
            selected_objects=[upper],
            selected_editable_objects=[upper],
        ):
            bpy.ops.object.modifier_apply(modifier=boolean.name)
        boolean_solver = "EXACT"
        boolean_backend = "Blender built-in"

    bm = bmesh.new()
    bm.from_mesh(upper.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-4)
    bmesh.ops.dissolve_degenerate(bm, dist=1e-4, edges=list(bm.edges))
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(upper.data)
    bm.free()
    upper.data.update()
    bpy.data.objects.remove(cutter, do_unlink=True)

    after = _bounds(upper)
    if any(abs(after[i][axis] - before[i][axis]) > 1e-5 for i in range(2) for axis in range(3)):
        raise RuntimeError("Hatch rebate unexpectedly changed the UpperDeck bounds")
    if upper.data.uv_layers.get("UVMap") is None:
        raise RuntimeError("Hatch rebate removed UpperDeck UVMap")
    color = upper.data.color_attributes.get("Color")
    if color is None or color.domain != "CORNER" or color.data_type != "BYTE_COLOR":
        raise RuntimeError("Hatch rebate removed the UpperDeck Color CORNER BYTE_COLOR contract")
    return {
        "center": [float(value) for value in center],
        "size": [float(value) for value in size],
        "depthMm": pocket_depth * 1000.0,
        "booleanBackend": boolean_backend,
        "booleanSolver": boolean_solver,
        "floorZ": deck_top - pocket_depth,
        "uvMapPreserved": True,
        "colorAttributePreserved": True,
    }


def _smart_uv(obj: bpy.types.Object) -> None:
    if obj.data.uv_layers.get("UVMap") is not None:
        return
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60.0), island_margin=0.02,
                            area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.select_set(False)


def _box(name: str, size: tuple[float, float, float], center: Vector | tuple[float, float, float],
         material: bpy.types.Material | None, bevel: float = 0.004,
         rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
         hardflow_bevel: HardflowBevel | None = None) -> bpy.types.Object:
    obj = rmk.make_box(name, size, center, material,
                       bevel=0.0 if hardflow_bevel else bevel,
                       rotation=rotation, bevel_segments=1, smooth=False)
    if hardflow_bevel is not None:
        hardflow_bevel(obj)
    obj[DETAIL_MARKER] = RECIPE_ID
    return obj


def _cylinder(name: str, radius: float, depth: float, center: Vector | tuple[float, float, float],
              material: bpy.types.Material | None,
              rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
              vertices: int = 12, bevel: float = 0.001) -> bpy.types.Object:
    obj = rmk.make_cylinder(name, radius, depth, center, material,
                            rotation=rotation, vertices=vertices, bevel=bevel, bevel_segments=1)
    obj[DETAIL_MARKER] = RECIPE_ID
    return obj


def _triangular_contact_prism(name: str, hull_triangle: list[Vector], normal: Vector,
                              depth: float, material: bpy.types.Material | None) -> bpy.types.Object:
    """Extrude the exact triangulated Hull face outward as a short weld seat."""
    if len(hull_triangle) != 3:
        raise ValueError(f"{name} needs one Hull triangle, got {len(hull_triangle)} vertices")
    points = [point.copy() for point in hull_triangle]
    normal = normal.normalized()
    face_normal = (points[1] - points[0]).cross(points[2] - points[0]).normalized()
    if face_normal.dot(normal) < 0.0:
        points[1], points[2] = points[2], points[1]
    outer = [point + normal * depth for point in points]
    vertices = [tuple(point) for point in (*points, *outer)]
    faces = [
        (2, 1, 0), (3, 4, 5),
        (0, 1, 4, 3), (1, 2, 5, 4), (2, 0, 3, 5),
    ]
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    if material is not None:
        mesh.materials.append(material)
    obj[DETAIL_MARKER] = RECIPE_ID
    return obj


def _beam(name: str, start: Vector, end: Vector, width: float, height: float,
          material: bpy.types.Material | None, bevel: float = 0.003,
          hardflow_bevel: HardflowBevel | None = None) -> bpy.types.Object:
    delta = end - start
    length = delta.length
    if length < 0.005:
        raise ValueError(f"Support beam {name} is too short ({length:.6f} m)")
    rotation = delta.normalized().to_track_quat("Z", "Y").to_euler()
    return _box(name, (width, height, length), (start + end) * 0.5,
                material, bevel=bevel, rotation=rotation, hardflow_bevel=hardflow_bevel)


def _contact_beam(name: str, support: bpy.types.Object, target: bpy.types.Object,
                  material: bpy.types.Material | None, section: tuple[float, float],
                  objects_out: list[bpy.types.Object], hardflow_bevel: HardflowBevel | None = None,
                  pad_size: tuple[float, float, float] = (0.12, 0.10, 0.022),
                  round_pad_radius: float = 0.0) -> dict[str, object]:
    """Build a short bolted support between two existing world-space surfaces."""
    support_point, target_point, original_gap = _closest_surface_points(support, target)
    axis = (target_point - support_point).normalized()
    if axis.length < 1e-6:
        raise ValueError(f"Cannot create support {name} for coincident meshes {support.name}/{target.name}")
    support_geometry = _mesh_geometry(support)
    support_tree = _tree(support_geometry)
    support_hit = support_tree.find_nearest(support_point)
    if not support_hit or support_hit[1] is None:
        raise ValueError(f"Cannot find a support normal for {name} on {support.name}")
    support_normal = support_hit[1].normalized()
    if support_normal.dot(axis) < 0.0:
        support_normal.negate()
    support_face_index = int(support_hit[2])
    support_triangle = [support_geometry[0][vertex]
                        for vertex in support_geometry[1][support_face_index]]
    # Fit the seat to the actual triangulated support face. Broad pads crossed
    # curved surfaces and produced false fit errors up to 34.5 mm. The short
    # arm starts on the seat's outer face and overlaps the supported shell by
    # 5 mm at its far end.
    start = support_point + support_normal * pad_size[2]
    end = target_point + axis * 0.005
    patch = _triangular_contact_prism(f"{name}_HullSeat", support_triangle,
                                      support_normal, pad_size[2], material)
    beam = _beam(f"{name}_LoadArm", start, end, section[0], section[1], material,
                 bevel=0.003,
                 hardflow_bevel=hardflow_bevel if hardflow_bevel and "Torsion" in name else None)
    objects_out.extend((patch, beam))
    return {
        "support": support.name,
        "target": target.name,
        "preRepairGapMm": original_gap * 1000.0,
        "supportSeat": patch.name,
        "loadArm": beam.name,
        "supportPoint": list(support_point),
        "supportNormal": list(support_normal),
        "supportSeatContactNormal": list(-support_normal),
        "supportTriangleIndex": support_face_index,
        "supportTriangleAreaMm2": abs((support_triangle[1] - support_triangle[0]).cross(support_triangle[2] - support_triangle[0]).length) * 500000.0,
        "targetPoint": list(target_point),
        "padSizeMm": [pad_size[0] * 1000.0, pad_size[1] * 1000.0, pad_size[2] * 1000.0],
        "supportSeatType": "exact_hull_triangle_prism",
        "supportAxis": list(axis),
        "sectionMm": [section[0] * 1000.0, section[1] * 1000.0],
    }


def _replay_validate(objects: list[bpy.types.Object]) -> dict[str, object]:
    scene = bpy.context.scene
    if scene.get(SCENE_MARKER) == RECIPE_ID:
        expected = scene.get("rcl_bulwark_visible_supports_expected_details", [])
        missing = [name for name in expected if bpy.data.objects.get(name) is None]
        if missing:
            raise ValueError("Bulwark support marker exists but some recipe objects are missing: " + ", ".join(missing[:12]))
        return {
            "recipeId": RECIPE_ID,
            "applied": False,
            "idempotentReplay": True,
            "addedDetailObjects": list(expected),
        }
    partial = [obj.name for obj in objects if obj.get(DETAIL_MARKER) == RECIPE_ID]
    if partial:
        raise ValueError("Partial Bulwark contact repair objects exist without the scene marker: " + ", ".join(partial[:12]))
    return {"recipeId": RECIPE_ID, "applied": None, "idempotentReplay": False}


def apply_bulwark_contact_repairs(
    objects: Iterable[bpy.types.Object] | None = None,
    *,
    hardflow_bevel: HardflowBevel | None = None,
    hardflow_boolean_apply: HardflowBoolean | None = None,
) -> dict[str, object]:
    """Seat the radiator/hatch assembly and repair only visibly unsupported accessories.

    The recipe leaves the coaxial muzzle, bore tube, hidden engine slats,
    exhaust hardware, hatches, turret, tracks, and source silhouette meshes in
    place. New hardpoint details are created with UVMap and RGBA Color data.
    """
    objects = list(objects) if objects is not None else list(bpy.context.scene.objects)
    prior = _replay_validate(objects)
    if prior["applied"] is False:
        return prior

    scene = bpy.context.scene
    upper = _object(objects, "BulwarkSiegeTankArmor_UpperDeck")
    cavity = _object(objects, "BulwarkSiegeTankEngine_RadiatorCavity")
    hatch_coaming = _object(objects, "BulwarkSiegeTankArmor_AccessHatch_CoamingRim")
    # The source blend may already carry the old vent marker while the
    # originally authored high placement remains unchanged. Run the shared
    # bounds-driven seat migration before the contact assertions below.
    radiator_seat = seat_bulwark_radiator_assembly(objects)
    armor = _material(objects, "Armor_UpperDeck") or _material(objects, "Armor_Hull")
    metal = _material(objects, "Chassis_FinalDrive_Housing") or _material(objects, "Suspension_L_TorsionBoss") or armor
    deck_top = _bounds(upper)[1].z
    deck_min, deck_max = _bounds(upper)
    generated: list[bpy.types.Object] = []
    moved: list[dict[str, object]] = []

    # The side ERA cassettes live in the Bulwark Level 2 ERA module. First
    # repair its target skirt supports here so that the canonical module's
    # backing plates have a stable face and sufficient moving-track clearance.
    skirt_repairs = []
    for side, sign in (("L", -1.0), ("R", 1.0)):
        skirt = _object(objects, f"BulwarkSiegeTankTreads_ArmorSkirt_{side}")
        stripe = _object(objects, f"BulwarkSiegeTankTreads_SkirtStripe_{side}")
        fender = _object(objects, f"BulwarkSiegeTankTreads_FenderTop_{side}")
        belt = _object(objects, f"BulwarkSiegeTankTreads_Belt_{side}")
        skirt_before = _bounds(skirt)
        belt_bounds = _bounds(belt)
        current_gap = (_bounds(skirt)[0].x - belt_bounds[1].x if sign > 0.0
                       else belt_bounds[0].x - _bounds(skirt)[1].x)
        shift = max(0.0, 0.022 - current_gap) * sign
        for part in (skirt, stripe):
            matrix = part.matrix_world.copy()
            matrix.translation.x += shift
            part.matrix_world = matrix
        bpy.context.view_layer.update()

        skirt_min, skirt_max = _bounds(skirt)
        fender_min, fender_max = _bounds(fender)
        hanger_x = (skirt_min.x + skirt_max.x) * 0.5
        hanger_width = skirt_max.x - skirt_min.x
        skirt_top = skirt_max.z
        hanger_bottom = skirt_top - 0.001
        hanger_top = fender_max.z
        if hanger_top - hanger_bottom < 0.02:
            raise RuntimeError(f"{side} ArmorSkirt has no measurable support gap to its FenderTop")
        fender_outer_x = fender_max.x if sign > 0.0 else fender_min.x
        start_x = fender_outer_x - sign * 0.030
        start = Vector((start_x, 0.0, hanger_top - 0.001))
        end = Vector((hanger_x, 0.0, hanger_bottom))
        hangers = []
        for index, y in enumerate((-0.85, 0.0, 0.85), 1):
            hanger = _beam(
                f"{UNIT_PREFIX}Armor_SkirtHanger_{side}_{index}",
                Vector((start.x, y, start.z)), Vector((end.x, y, end.z)),
                width=0.040, height=0.080, material=metal, bevel=0.003,
                hardflow_bevel=hardflow_bevel,
            )
            generated.append(hanger)
            hanger_to_skirt = _surface_gap(hanger, skirt)
            hanger_to_fender = _surface_gap(hanger, fender)
            if max(hanger_to_skirt, hanger_to_fender) > 0.002:
                raise RuntimeError(
                    f"{hanger.name} misses its side supports: skirt={hanger_to_skirt * 1000.0:.3f} mm, "
                    f"fender={hanger_to_fender * 1000.0:.3f} mm"
                )
            hangers.append({
                "name": hanger.name,
                "y": y,
                "upperSeat": list((start.x, y, start.z)),
                "lowerSeat": list((end.x, y, end.z)),
                "skirtGapMm": hanger_to_skirt * 1000.0,
                "fenderGapMm": hanger_to_fender * 1000.0,
                "trackGapMm": _surface_gap(hanger, belt) * 1000.0,
            })
            skirt_bolt = _cylinder(
                f"{UNIT_PREFIX}Armor_SkirtHangerBolt_{side}_{index}_Skirt",
                0.009, 0.012,
                (sign * (abs(skirt_max.x if sign > 0.0 else skirt_min.x) + 0.006),
                 y, skirt_top - 0.020),
                metal,
                rotation=(0.0, sign * math.pi * 0.5, 0.0),
                vertices=10,
                bevel=0.001,
            )
            fender_bolt = _cylinder(
                f"{UNIT_PREFIX}Armor_SkirtHangerBolt_{side}_{index}_Fender",
                0.009, 0.012,
                (start.x, y, hanger_top + 0.005),
                metal,
                vertices=10,
                bevel=0.001,
            )
            generated.extend((skirt_bolt, fender_bolt))

        skirt_to_belt = _surface_gap(skirt, belt)
        if skirt_to_belt < 0.0219:
            raise RuntimeError(
                f"{side} ArmorSkirt has only {skirt_to_belt * 1000.0:.3f} mm moving-belt clearance"
            )
        skirt_repairs.append({
            "side": side,
            "translationXmm": shift * 1000.0,
            "preRepairBounds": [list(skirt_before[0]), list(skirt_before[1])],
            "hangers": hangers,
            "hangerCount": len(hangers),
            "supportBoltCount": len(hangers) * 2,
            "skirtToMovingBeltGapMm": skirt_to_belt * 1000.0,
            "eraMountSupportPlaneX": skirt_max.x if sign > 0.0 else skirt_min.x,
            "eraCassetteCentersYZ": [[-0.92, 0.40], [0.92, 0.40]],
        })
    moved.append({
        "assembly": "TrackSideArmorSkirts",
        "geometryPresetTarget": ERA_BRICK_PRESET_ID,
        "sides": skirt_repairs,
        "totalHangerCount": sum(row["hangerCount"] for row in skirt_repairs),
        "eraCassetteCountInLevel2Module": 4,
    })

    # Keep the six front-glacis cassettes as an authored Level 1 armor detail.
    # Each backing plane is oriented to the actual upper or lower Hull facet;
    # the Level 2 module adds a separate bilateral side-skirt array.
    front_hull = _object(objects, "BulwarkSiegeTankArmor_Hull")
    front_hull_tree = _tree(_mesh_geometry(front_hull))
    obstacle_trees = [
        (neighbor, _tree(_mesh_geometry(neighbor)))
        for neighbor in objects
        if neighbor is not front_hull and neighbor.type == "MESH" and not neighbor.name.startswith("Preview")
    ]
    front_era = []
    for row_name, z in (("Lower", 0.52), ("Upper", 0.70)):
        for column, x in enumerate((-0.24, 0.0, 0.24), 1):
            hit = front_hull_tree.ray_cast(Vector((x, 2.0, z)), Vector((0.0, -1.0, 0.0)), 1.0)
            if not hit or hit[0] is None or hit[1] is None:
                raise RuntimeError(f"No front Hull support face for ERA {row_name} row column {column}")
            support_point = hit[0].copy()
            support_normal = hit[1].normalized()
            if support_normal.dot(Vector((0.0, 1.0, 0.0))) < 0.0:
                support_normal.negate()
            prefix = f"{UNIT_PREFIX}Armor_GlacisERA_{row_name}_{column}"
            brick_objects = create_era_brick_preset(
                prefix=prefix,
                config=EraBrickConfig(
                    center=tuple(support_point),
                    normal=tuple(support_normal),
                    size=(0.205, 0.118, 0.050),
                    backing_overhang=0.010,
                    backing_thickness=0.009,
                    seam_width=0.0035,
                    seam_depth=0.0025,
                    edge_margin=0.010,
                    corner_bevel=0.005,
                    fastener_radius=0.0055,
                ),
                armor_material=armor,
                hardware_material=metal,
                hardflow_boolean_apply=hardflow_boolean_apply,
                hardflow_bevel=hardflow_bevel,
            )
            generated.extend(brick_objects)
            backing = next(obj for obj in brick_objects if "_BackingPlate" in obj.name)
            support_fit = _contact_face_clearance(backing, front_hull, -support_normal)
            if (support_fit["sampleCount"] == 0 or support_fit["missCount"] or
                    support_fit["maxAbsFitErrorMm"] > 1.0):
                raise RuntimeError(
                    f"{prefix} backing plate does not conform to the front Hull: "
                    f"max {support_fit['maxAbsFitErrorMm']:.3f} mm, misses "
                    f"{support_fit['missCount']}/{support_fit['sampleCount']}"
                )
            for obj in brick_objects:
                part_tree = _tree(_mesh_geometry(obj))
                for neighbor, neighbor_tree in obstacle_trees:
                    if part_tree.overlap(neighbor_tree):
                        raise RuntimeError(f"{obj.name} intersects neighboring Bulwark part {neighbor.name}")
            front_era.append({
                "prefix": prefix,
                "row": row_name,
                "column": column,
                "supportPoint": list(support_point),
                "supportNormal": list(support_normal),
                "supportTriangle": int(hit[2]),
                "backingSeatFit": support_fit,
                "meshObjectCount": len(brick_objects),
                "preset": ERA_BRICK_PRESET_ID,
            })
    moved.append({
        "assembly": "GlacisERACassetteArray",
        "geometryPreset": ERA_BRICK_PRESET_ID,
        "cassetteCount": len(front_era),
        "brickLayout": "two rows of three, each backing plane conformed to its measured Hull facet",
        "bricks": front_era,
    })

    # Keep the hull-bound hatch within a shallow deck pocket. The panel is
    # fitted to the inside of its existing coaming and the X hinge axis sits
    # on the upper rear edge, so it opens from a seated position without a lift.
    hatch_plate = _object(objects, "BulwarkSiegeTankArmor_AccessHatch_HatchPlate")
    hatch_hinge = _object(objects, "BulwarkSiegeTankArmor_AccessHatch_HingeBar")
    hatch_latch = _object(objects, "BulwarkSiegeTankArmor_AccessHatch_DogLatch")
    rebate_floor_z = deck_top - 0.025
    coaming_min, coaming_max = _bounds(hatch_coaming)
    hatch_delta_z = rebate_floor_z - coaming_min.z
    for part in (hatch_plate, hatch_coaming, hatch_hinge, hatch_latch):
        matrix = part.matrix_world.copy()
        matrix.translation.z += hatch_delta_z
        part.matrix_world = matrix
    bpy.context.view_layer.update()
    coaming_min, coaming_max = _bounds(hatch_coaming)
    hatch_plate.dimensions = Vector((coaming_max.x - coaming_min.x - 0.016,
                                     coaming_max.y - coaming_min.y - 0.016, 0.020))
    hatch_plate.location.z = deck_top - 0.010
    bpy.context.view_layer.update()
    plate_min, plate_max = _bounds(hatch_plate)
    hatch_hinge.location.x = (plate_min.x + plate_max.x) * 0.5
    hatch_hinge.location.y = plate_min.y + 0.0192
    hatch_hinge.location.z = deck_top
    latch_corners = [hatch_latch.matrix_world @ Vector(corner) for corner in hatch_latch.bound_box]
    latch_center = sum(latch_corners, Vector()) / len(latch_corners)
    hatch_latch.scale.x *= 0.66
    hatch_latch.location.x = latch_center.x + 0.170
    hatch_latch.location.y = latch_center.y
    hatch_latch.location.z = deck_top + hatch_latch.dimensions.z * 0.5
    bpy.context.view_layer.update()
    coaming_min, coaming_max = _bounds(hatch_coaming)
    plate_min, plate_max = _bounds(hatch_plate)
    if abs(coaming_min.z - rebate_floor_z) > 0.001 or abs(coaming_max.z - deck_top) > 0.001:
        raise RuntimeError("AccessHatch coaming is not seated between rebate floor and deck top")
    if abs(plate_max.z - deck_top) > 0.001 or abs((plate_max.z - plate_min.z) - 0.020) > 1e-5:
        raise RuntimeError("AccessHatch panel must be 20 mm thick and flush with UpperDeck")
    if _surface_gap(hatch_latch, hatch_plate) > 0.001:
        raise RuntimeError("AccessHatch DogLatch does not contact its panel")
    hatch_mount = {
        "assembly": "AccessHatch",
        "rebateFloorZ": rebate_floor_z,
        "coamingBounds": [list(coaming_min), list(coaming_max)],
        "panelBounds": [list(plate_min), list(plate_max)],
        "panelThicknessMm": (plate_max.z - plate_min.z) * 1000.0,
        "coamingAdjustmentMm": hatch_delta_z * 1000.0,
        "hingeAxis": "world X",
        "hingeCenter": list(hatch_hinge.location),
        "dogLatchBounds": [list(_bounds(hatch_latch)[0]), list(_bounds(hatch_latch)[1])],
        "coamingBottomFloorGapMm": (coaming_min.z - rebate_floor_z) * 1000.0,
        "coamingTopDeckGapMm": (deck_top - coaming_max.z) * 1000.0,
        "panelTopDeckGapMm": (deck_top - plate_max.z) * 1000.0,
        "latchPanelGapMm": _surface_gap(hatch_latch, hatch_plate) * 1000.0,
    }
    moved.append(hatch_mount)

    # Seat both rear gussets on full-length backing plates. Their mesh origins
    # are at the rear foot and their local geometry extends +Y by 220 mm.
    for index, sign in ((1, -1.0), (2, 1.0)):
        gusset = _object(objects, f"BulwarkSiegeTankArmor_Gusset_{index}")
        anchor_y = -1.095
        plate_y = -1.010
        gusset.location = (sign * 0.640, anchor_y, 1.096)
        plate_name = f"BulwarkSiegeTankArmor_GussetAnchorPlate_{'L' if sign < 0 else 'R'}"
        plate = _box(plate_name, (0.08, 0.27, 0.016), (sign * 0.640, plate_y, deck_top + 0.008), armor,
                     bevel=0.004, hardflow_bevel=hardflow_bevel)
        generated.append(plate)
        for bolt_index, y in enumerate((-1.110, -0.910), 1):
            bolt = _cylinder(
                f"BulwarkSiegeTankArmor_GussetAnchorBolt_{'L' if sign < 0 else 'R'}_{bolt_index:02d}",
                0.005, 0.008, (sign * 0.673, y, deck_top + 0.012), metal, vertices=10, bevel=0.0008)
            generated.append(bolt)
        moved.append({"object": gusset.name, "newWorldLocation": list(gusset.location), "supportPlate": plate.name})

    # Relocate the complete Pioneer rack by its aggregate mesh bounds to the
    # starboard fender top, below the deck and outside the turret sweep.
    rack = [obj for obj in objects if obj.type == "MESH" and obj.name.startswith("BulwarkSiegeTankArmor_PioneerRack_")]
    if len(rack) != 8:
        raise ValueError(f"Expected 8 source PioneerRack meshes, found {len(rack)}")
    rack_bounds = [_bounds(obj) for obj in rack]
    rack_min = Vector(min(bounds[0][axis] for bounds in rack_bounds) for axis in range(3))
    rack_max = Vector(max(bounds[1][axis] for bounds in rack_bounds) for axis in range(3))
    rack_center = (rack_min + rack_max) * 0.5
    fender = _object(objects, "BulwarkSiegeTankTreads_FenderTop_R")
    fender_min, fender_max = _bounds(fender)
    rack_target = Vector((
        (fender_min.x + fender_max.x) * 0.5,
        -0.26,
        fender_max.z + (rack_max.z - rack_min.z) * 0.5,
    ))
    rack_delta = rack_target - rack_center
    for obj in rack:
        matrix = obj.matrix_world.copy()
        matrix.translation += rack_delta
        obj.matrix_world = matrix
    bpy.context.view_layer.update()
    rack_bounds_after = [_bounds(obj) for obj in rack]
    rack_min_after = Vector(min(bounds[0][axis] for bounds in rack_bounds_after) for axis in range(3))
    rack_max_after = Vector(max(bounds[1][axis] for bounds in rack_bounds_after) for axis in range(3))
    if rack_min_after.x < fender_min.x + 0.005 or rack_max_after.x > fender_max.x - 0.005:
        raise ValueError("Relocated PioneerRack falls outside the supporting starboard fender width")
    if rack_min_after.y < fender_min.y or rack_max_after.y > fender_max.y:
        raise ValueError("Relocated PioneerRack falls outside the supporting starboard fender length")
    if abs(rack_min_after.z - fender_max.z) > 1e-5:
        raise ValueError("Relocated PioneerRack base does not meet the starboard fender top")
    rack_support_contacts = []
    rack_fastener_contacts = []
    for cleat_index in range(2):
        cleat = _object(objects, f"BulwarkSiegeTankArmor_PioneerRack_MountCleat_{cleat_index}")
        cleat_min, cleat_max = _bounds(cleat)
        cleat_center = (cleat_min + cleat_max) * 0.5
        seat_size = (cleat_max.x - cleat_min.x + 0.020,
                     cleat_max.y - cleat_min.y + 0.020,
                     0.016)
        seat = _box(
            f"BulwarkSiegeTankArmor_PioneerRack_FenderSeat_{cleat_index}",
            seat_size, (cleat_center.x, cleat_center.y, fender_max.z - seat_size[2] * 0.5), armor,
            bevel=0.002, hardflow_bevel=hardflow_bevel if cleat_index == 0 else None)
        generated.append(seat)
        cleat_seat_gap = _surface_gap(cleat, seat)
        seat_fender_gap = _surface_gap(seat, fender)
        if cleat_seat_gap > 0.001 or seat_fender_gap > 0.001:
            raise ValueError(
                f"PioneerRack support {cleat_index} is not seated: "
                f"cleat-seat={cleat_seat_gap * 1000.0:.3f} mm, "
                f"seat-fender={seat_fender_gap * 1000.0:.3f} mm"
            )
        rack_support_contacts.append({
            "mountCleat": cleat.name,
            "seat": seat.name,
            "supportSurface": fender.name,
            "cleatToSeatGapMm": cleat_seat_gap * 1000.0,
            "seatToFenderGapMm": seat_fender_gap * 1000.0,
            "fenderTopZ": fender_max.z,
            "seatTopZ": _bounds(seat)[1].z,
            "cleatBottomZ": cleat_min.z,
        })
        for bolt_index, xoff in enumerate((-0.075, 0.075), 1):
            bolt = _cylinder(
                f"BulwarkSiegeTankArmor_PioneerRack_FenderSeatBolt_{cleat_index}_{bolt_index}",
                0.006, 0.012, (cleat_center.x + xoff, cleat_center.y, fender_max.z + 0.006), metal,
                vertices=10, bevel=0.0009)
            generated.append(bolt)
            bolt_gap = _surface_gap(bolt, seat)
            if bolt_gap > 0.001:
                raise ValueError(f"PioneerRack bolt {bolt.name} floats {bolt_gap * 1000.0:.3f} mm above its seat")
            rack_fastener_contacts.append({"bolt": bolt.name, "seat": seat.name,
                                           "seatGapMm": bolt_gap * 1000.0})
    moved.append({"assembly": "PioneerRack", "sourceMeshCount": len(rack),
                  "rotationDegreesZ": 0.0, "supportSurface": fender.name,
                  "sourceClusterBounds": [list(rack_min), list(rack_max)],
                  "targetClusterBounds": [list(rack_min_after), list(rack_max_after)],
                  "boundsTranslation": list(rack_delta), "fenderTopZ": fender_max.z,
                  "supportContacts": rack_support_contacts,
                  "fastenerContacts": rack_fastener_contacts})

    # Move each lug/pad inward from the sloped deck edge, enlarge the weld pad
    # across the flat deck region, and seat both as one assembly.
    lug_support_contacts = []
    for index, (sign_x, sign_y) in enumerate(((-1.0, 1.0), (1.0, 1.0), (-1.0, -1.0), (1.0, -1.0)), 1):
        lug = _object(objects, f"BulwarkSiegeTankArmor_LiftingLug_{index}")
        pad = _object(objects, f"BulwarkSiegeTankArmor_LugWeldPad_{index}")
        target = Vector((sign_x * 0.72, sign_y * 0.95, 1.142))
        delta = target - lug.matrix_world.translation
        lug.location += delta
        pad.location += delta
        pad.dimensions.x = 0.080
        bpy.context.view_layer.update()
        pad_point, deck_point, pad_gap = _closest_surface_points(pad, upper)
        if pad_gap > 0.0005:
            seat_delta = deck_point - pad_point
            for part in (lug, pad):
                matrix = part.matrix_world.copy()
                matrix.translation += seat_delta
                part.matrix_world = matrix
        bpy.context.view_layer.update()
        pad_point, deck_point, pad_gap = _closest_surface_points(pad, upper)
        lug_pad_gap = _surface_gap(lug, pad)
        if pad_gap > 0.001 or lug_pad_gap > 0.002:
            raise ValueError(f"Lifting lug {index} seat incomplete: pad-deck={pad_gap * 1000.0:.3f} mm, lug-pad={lug_pad_gap * 1000.0:.3f} mm")
        lug_support_contacts.append({"lug": lug.name, "weldPad": pad.name,
                                     "deckSurface": upper.name,
                                     "padToDeckGapMm": pad_gap * 1000.0,
                                     "lugToPadGapMm": lug_pad_gap * 1000.0,
                                     "padFootprintMm": [80.0, (_bounds(pad)[1].y - _bounds(pad)[0].y) * 1000.0],
                                     "sampledContactPointPad": list(pad_point),
                                     "sampledContactPointDeck": list(deck_point)})
        moved.append({"object": lug.name, "weldPad": pad.name, "newLugLocation": list(lug.location),
                      "newPadLocation": list(pad.location), "padDeckGapMm": pad_gap * 1000.0})

    # Lower each existing tread cleat to the belt face while keeping the 20 mm
    # inner clearances on road wheels 2-4 intact.
    lowered = []
    for side in ("L", "R"):
        for index in range(1, 9):
            cleat = _object(objects, f"BulwarkSiegeTankTreads_Cleat_{side}_{index}")
            cleat.location.z -= 0.0015
            lowered.append(cleat.name)
    moved.append({"assembly": "Treads_Cleats", "objectNames": lowered, "loweredMm": 1.5})

    # The vent recipe lowers the radiator and hatch as one cluster. Seat the
    # hatch coaming in a shallow deck rebate; the cavity base contacts the deck
    # directly, so no tall cage or yaw-volume rails are needed.
    cavity_min, cavity_max = _bounds(cavity)
    upper_bounds = _bounds(upper)
    if abs(cavity_min.z - deck_top) > 1e-5:
        raise ValueError(f"Radiator cavity base misses UpperDeck by {(cavity_min.z - deck_top) * 1000.0:.3f} mm")
    if cavity_min.y < deck_min.y - 1e-5 or cavity_max.y > deck_max.y + 1e-5:
        raise ValueError("Lowered radiator footprint falls outside UpperDeck")
    hatch_rebate = _cut_hatch_rebate(upper, hatch_coaming, hardflow_boolean_apply)
    hatch_parts = {
        "panel": hatch_plate,
        "coaming": hatch_coaming,
        "hinge": hatch_hinge,
        "latch": hatch_latch,
    }
    hatch_config = ServiceHatchConfig.from_existing_parts(
        hatch_parts,
        support_top_z=deck_top,
        recess_depth=0.021,
        panel_clearance=0.003,
        panel_thickness=0.020,
        frame_thickness=0.025,
        hinge_axis="X",
        hinge_side="NEGATIVE_Y",
    )
    hatch_preset = apply_service_hatch_to_existing_parts(hatch_parts, hatch_config, support=upper)
    if hatch_preset["verdict"] != "PASS":
        raise RuntimeError("Bulwark AccessHatch does not satisfy the shared service hatch geometry preset: "
                           + str(hatch_preset["checks"]))
    hatch_mount["geometryPreset"] = SERVICE_HATCH_PRESET_ID
    hatch_mount["presetValidation"] = hatch_preset
    moved.append({"assembly": "RadiatorAndAccessHatch", "baseContactZ": cavity_min.z,
                  "upperDeckTopZ": deck_top, "verticalCradleRemoved": True,
                  "seatAdjustment": radiator_seat, "hatchRebate": hatch_rebate})

    # The three louvers gain short axial pivot pins that span from each louver
    # end into the original slot's uncut lip. A retainer may sit outboard of a
    # louver only when the pin provides the measured continuous load path.
    louver_mount_contacts = []
    for index in range(1, 4):
        louver = _object(objects, f"BulwarkSiegeTankEngine_RadiatorVentLouver_{index:02d}")
        lo, hi = _bounds(louver)
        center_y = (lo.y + hi.y) * 0.5
        center_z = (lo.z + hi.z) * 0.5
        half_x = (hi.x - lo.x) * 0.5
        for sign in (-1.0, 1.0):
            center_x = sign * (half_x + 0.013)
            pivot = _cylinder(
                f"BulwarkSiegeTankEngine_RadiatorLouverPivot_{index:02d}_{'R' if sign > 0 else 'L'}",
                0.0035, 0.031, (center_x, center_y, center_z), metal,
                rotation=(0.0, math.radians(90.0), 0.0), vertices=10, bevel=0.0005)
            generated.append(pivot)
            retainer = _cylinder(
                f"BulwarkSiegeTankEngine_RadiatorLouverRetainer_{index:02d}_{'R' if sign > 0 else 'L'}",
                0.007, 0.004, (sign * (half_x + 0.024), center_y, center_z), metal,
                rotation=(0.0, math.radians(90.0), 0.0), vertices=10, bevel=0.0005)
            generated.append(retainer)
            pivot_louver_gap = _surface_gap(pivot, louver)
            retainer_pivot_gap = _surface_gap(retainer, pivot)
            direct_louver_retainer_gap = _surface_gap(retainer, louver)
            if pivot_louver_gap > 0.002 or retainer_pivot_gap > 0.002:
                raise ValueError(f"Louver {index} retainer path is open: pivot-louver={pivot_louver_gap * 1000.0:.3f} mm, retainer-pivot={retainer_pivot_gap * 1000.0:.3f} mm")
            louver_mount_contacts.append({
                "louver": louver.name, "pivot": pivot.name, "retainer": retainer.name,
                "pivotToLouverGapMm": pivot_louver_gap * 1000.0,
                "retainerToPivotGapMm": retainer_pivot_gap * 1000.0,
                "directLouverToRetainerGapMm": direct_louver_retainer_gap * 1000.0,
                "continuousPinLoadPath": True,
            })

    # Relocate each complete towing clevis to a measured, mirrored Hull face.
    # The face coordinates come from a source-mesh inspection and are resolved
    # through the triangulated Hull BVH so the recipe never bridges the old
    # 129-317 mm gaps with a decorative arm.
    shackle_relocations = []
    shackle_mounts = []
    hull = _object(objects, "BulwarkSiegeTankArmor_Hull")
    hull_tree = _tree(_mesh_geometry(hull))
    shackle_face_hints = {
        "Front": Vector((0.49733746, 1.40511119, 0.51701200)),
        "Rear": Vector((0.60928923, -1.45504940, 0.34992528)),
    }
    hull_geometry = _mesh_geometry(hull)
    for end in ("Front", "Rear"):
        for index in (1, 2):
            bracket = _object(objects, f"BulwarkSiegeTankArmor_{end}Shackle_ShackleBracket_{index}")
            pin = _object(objects, f"BulwarkSiegeTankArmor_{end}Shackle_ShacklePin_{index}")
            loop = _object(objects, f"BulwarkSiegeTankArmor_{end}Shackle_ShackleLoop_{index}")
            side_sign = -1.0 if index == 1 else 1.0
            face_hint = shackle_face_hints[end].copy()
            face_hint.x *= side_sign
            hull_hit = hull_tree.find_nearest(face_hint)
            if not hull_hit or hull_hit[1] is None or hull_hit[3] is None:
                raise ValueError(f"No triangulated Hull face for {end} shackle {index}")
            hull_point = hull_hit[0].copy()
            hull_normal = hull_hit[1].normalized()
            face_index = int(hull_hit[2])
            hull_triangle = [hull_geometry[0][vertex] for vertex in hull_geometry[1][face_index]]
            hint_fit_mm = float(hull_hit[3]) * 1000.0
            if hint_fit_mm > 3.0:
                raise ValueError(f"{end} shackle {index} Hull face hint misses by {hint_fit_mm:.3f} mm")
            # Query from outside the selected face to find the outward side of
            # the bracket, then place that point on the outer face of a short
            # 8 mm weld boss. The assembly's internal pin/loop relationship is
            # preserved except for a small closure correction on the rear pair.
            bracket_point, _, _, _ = _nearest_object_point(bracket, hull_point + hull_normal * 0.5)
            _, _, pre_gap = _closest_surface_points(bracket, hull)
            boss_depth = 0.008
            target_bracket_point = hull_point + hull_normal * boss_depth
            delta = target_bracket_point - bracket_point
            for obj in (bracket, pin, loop):
                matrix = obj.matrix_world.copy()
                matrix.translation += delta
                obj.matrix_world = matrix
            bpy.context.view_layer.update()
            boss = _triangular_contact_prism(
                f"BulwarkSiegeTankArmor_{end}ShackleHullBoss_{index}",
                hull_triangle, hull_normal, boss_depth, metal)
            generated.append(boss)
            pin_to_loop_before = _surface_gap(pin, loop)
            if pin_to_loop_before > 0.001:
                loop_point, pin_point, pin_to_loop_before = _closest_surface_points(loop, pin)
                closure = (pin_point - loop_point).normalized() * max(0.0, pin_to_loop_before - 0.001)
                matrix = loop.matrix_world.copy()
                matrix.translation += closure
                loop.matrix_world = matrix
            bpy.context.view_layer.update()
            pad_hull_gap = _surface_gap(boss, hull)
            bracket_boss_gap = _surface_gap(bracket, boss)
            pin_loop_gap = _surface_gap(pin, loop)
            bracket_pin_gap = _surface_gap(bracket, pin)
            bracket_hull_gap = _surface_gap(bracket, hull)
            if max(pad_hull_gap, bracket_boss_gap, pin_loop_gap, bracket_pin_gap) > 0.002:
                raise ValueError(f"{end} shackle {index} seat incomplete: boss-hull={pad_hull_gap * 1000.0:.3f} mm, bracket-boss={bracket_boss_gap * 1000.0:.3f} mm, pin-loop={pin_loop_gap * 1000.0:.3f} mm, bracket-pin={bracket_pin_gap * 1000.0:.3f} mm")
            boss_fit = _contact_face_clearance(boss, hull, -hull_normal)
            if boss_fit["sampleCount"] == 0 or boss_fit["missCount"] or boss_fit["maxAbsFitErrorMm"] > 2.0:
                raise ValueError(f"{end} shackle {index} tangent boss does not fit its local Hull face: p95 {boss_fit['p95AbsFitErrorMm']:.3f} mm, max {boss_fit['maxAbsFitErrorMm']:.3f} mm, misses {boss_fit['missCount']}/{boss_fit['sampleCount'] + boss_fit['missCount']}")
            shackle_mounts.append({
                                   "assembly": f"{end}Shackle_{index}",
                                   "bracket": bracket.name, "pin": pin.name, "loop": loop.name,
                                   "support": hull.name, "supportSeat": boss.name,
                                   "mountType": "short_tangent_weld_boss",
                                   "preRepairGapMm": pre_gap * 1000.0,
                                   "faceHintFitMm": hint_fit_mm,
                                   "hullTriangleIndex": face_index,
                                   "hullTriangleAreaMm2": abs((hull_triangle[1] - hull_triangle[0]).cross(hull_triangle[2] - hull_triangle[0]).length) * 500000.0,
                                   "translation": list(delta),
                                   "supportNormal": list(hull_normal),
                                   "selectedHullPoint": list(hull_point),
                                   "bracketToHullGapAfterMm": bracket_hull_gap * 1000.0,
                                   "bracketToHullGapMm": bracket_hull_gap * 1000.0,
                                   "bossToHullGapMm": pad_hull_gap * 1000.0,
                                   "bossToBracketGapMm": bracket_boss_gap * 1000.0,
                                   "bossFaceFit": boss_fit,
                                   "bracketToPinGapMm": bracket_pin_gap * 1000.0,
                                   "pinToLoopGapBeforeMm": pin_to_loop_before * 1000.0,
                                   "pinToLoopGapAfterMm": pin_loop_gap * 1000.0})
            shackle_relocations.append({"assembly": f"{end}Shackle_{index}",
                                        "objects": [bracket.name, pin.name, loop.name],
                                        "translation": [float(value) for value in delta]})
    moved.extend(shackle_relocations)

    # The SponsonBridge is separated from Hull by a small but visible seam; add
    # a short tie and mount the final-drive housings to it. Leave suspension
    # stations 2-4 open: triangulated candidate arms cross the belt volume, and
    # no concealed axle route has passed a visual occlusion check yet.
    sponson_hull_ties = []
    suspension_mounts = []
    final_drive_mounts = []
    unresolved_suspension_interfaces = []
    for side in ("L", "R"):
        sponson = _object(objects, f"BulwarkSiegeTankArmor_SponsonBridge_{side}")
        hull_tie = _contact_beam(
            f"BulwarkSiegeTankArmor_SponsonHullTie_{side}",
            _object(objects, "BulwarkSiegeTankArmor_Hull"), sponson, metal,
            (0.050, 0.050), generated, hardflow_bevel,
            pad_size=(0.12, 0.10, 0.018),
        )
        sponson_hull_ties.append(hull_tie)
        for station in (2, 3, 4):
            torsion = _object(objects, f"BulwarkSiegeTankSuspension_{side}_TorsionBoss_{station}")
            damper = _object(objects, f"BulwarkSiegeTankSuspension_{side}_ShockDamper_{station}")
            unresolved_suspension_interfaces.extend((
                {"side": side, "station": station, "component": torsion.name,
                 "candidateSupport": sponson.name,
                 "sponsonGapMm": _surface_gap(sponson, torsion) * 1000.0,
                 "hullGapMm": _surface_gap(_object(objects, "BulwarkSiegeTankArmor_Hull"), torsion) * 1000.0,
                 "mountAdded": False,
                 "reason": "Triangulated candidate load arms from the Hull/Sponson pass through the corresponding belt; route left open."},
                {"side": side, "station": station, "component": damper.name,
                 "candidateSupport": sponson.name,
                 "sponsonGapMm": _surface_gap(sponson, damper) * 1000.0,
                 "hullGapMm": _surface_gap(_object(objects, "BulwarkSiegeTankArmor_Hull"), damper) * 1000.0,
                 "mountAdded": False,
                 "reason": "Triangulated candidate load arms from the Hull/Sponson pass through the corresponding belt; route left open."},
            ))
        housing = _object(objects, f"BulwarkSiegeTankChassis_FinalDrive_Housing_{side}")
        final_drive_mounts.append(_contact_beam(
            f"BulwarkSiegeTankArmor_FinalDriveMount_{side}",
            sponson, housing, metal, (0.045, 0.045), generated, hardflow_bevel,
            pad_size=(0.12, 0.10, 0.020),
        ))
    moved.extend(sponson_hull_ties)
    moved.extend(suspension_mounts)
    moved.extend(final_drive_mounts)

    # Verify that mounting faces land within 2 mm and record p95/max fit error.
    support_route_contacts = []
    for mount in [*sponson_hull_ties, *suspension_mounts, *final_drive_mounts]:
        seat = _object(generated, mount["supportSeat"])
        arm = _object(generated, mount["loadArm"])
        support = _object(objects, mount["support"])
        target = _object(objects, mount["target"])
        axis = Vector(mount.get("supportSeatContactNormal", mount["supportNormal"]))
        seat_gap = _surface_gap(seat, support)
        arm_seat_gap = _surface_gap(arm, seat)
        arm_target_gap = _surface_gap(arm, target)
        seat_fit = _contact_face_clearance(seat, support, axis)
        if max(seat_gap, arm_seat_gap, arm_target_gap) > 0.002:
            raise ValueError(f"Support route {mount['loadArm']} leaves a gap over 2 mm")
        if seat_fit["sampleCount"] == 0 or seat_fit["missCount"]:
            raise ValueError(f"Support plate {seat.name} misses its intended support over part of its contact face")
        if seat_fit["maxAbsFitErrorMm"] > 2.0:
            raise ValueError(f"Support plate {seat.name} contact face fit exceeds 2 mm: {seat_fit['maxAbsFitErrorMm']:.3f} mm")
        support_route_contacts.append({
            **mount,
            "supportSeatMinGapMm": seat_gap * 1000.0,
            "loadArmSeatMinGapMm": arm_seat_gap * 1000.0,
            "loadArmTargetMinGapMm": arm_target_gap * 1000.0,
            "supportSeatFaceFit": seat_fit,
        })

    forbidden_track_objects = [
        obj for obj in objects if obj.type == "MESH" and obj.name.startswith((
            "BulwarkSiegeTankTreads_Belt_", "BulwarkSiegeTankTreads_RoadWheel_",
            "BulwarkSiegeTankTreads_DriveSprocket_", "BulwarkSiegeTankTreads_FrontIdler_",
        ))
    ]
    forbidden_trees = {obj.name: _tree(_mesh_geometry(obj)) for obj in forbidden_track_objects}
    route_track_intersections = []
    route_parts = [bpy.data.objects[mount[key]] for mount in [*sponson_hull_ties,
                    *suspension_mounts, *final_drive_mounts] for key in ("supportSeat", "loadArm")]
    route_parts.extend(bpy.data.objects[mount["supportSeat"]] for mount in shackle_mounts)
    route_parts.extend(
        _object(objects, f"BulwarkSiegeTankArmor_{end}Shackle_{part}_{index}")
        for end in ("Front", "Rear") for index in (1, 2)
        for part in ("ShackleBracket", "ShacklePin", "ShackleLoop")
    )
    for part in route_parts:
        part_tree = _tree(_mesh_geometry(part))
        for forbidden in forbidden_track_objects:
            if part_tree.overlap(forbidden_trees[forbidden.name]):
                route_track_intersections.append({"supportPart": part.name, "trackPart": forbidden.name})
    if route_track_intersections:
        raise ValueError("A new Bulwark load support crosses a belt, road wheel, idler, or drive sprocket: "
                         + ", ".join(f"{row['supportPart']}->{row['trackPart']}" for row in route_track_intersections[:8]))

    # The existing weld pads stay paired with the relocated lifting lugs.
    # Keep each lug set outside the measured turret and hatch motion footprint.

    # UV and RGBA vertex-wear contracts on all newly-authored supports.
    for obj in generated:
        _smart_uv(obj)
    missing_color = [obj for obj in generated if obj.data.color_attributes.get("Color") is None]
    if missing_color:
        rmk.bake_advanced_vertex_shading(
            missing_color,
            wear_level="field_tested",
            soot_intensity=0.0,
            exhaust_locs=[],
            muzzle_loc=Vector((0.0, 0.0, 0.0)),
            structural_wear_mask=True,
        )

    # Verify the top-level bounds and mesh contracts before saving the marker.
    upper_bounds_after = _bounds(upper)
    cavity_bounds_after = _bounds(cavity)
    if any(abs(upper_bounds_after[i][axis] - upper_bounds[i][axis]) > 1e-5
           for i in range(2) for axis in range(3)):
        raise RuntimeError("Bulwark support recipe unexpectedly moved or reshaped UpperDeck")
    if any(abs(cavity_bounds_after[i][axis] - cavity_min[axis] if i == 0 else cavity_bounds_after[i][axis] - cavity_max[axis]) > 1e-5
           for i in range(2) for axis in range(3)):
        raise RuntimeError("Bulwark support recipe unexpectedly moved or reshaped the radiator cavity")
    for obj in generated:
        if obj.data.uv_layers.get("UVMap") is None:
            raise RuntimeError(f"New Bulwark mesh {obj.name} lost UVMap")
        color = obj.data.color_attributes.get("Color")
        if color is None or color.domain != "CORNER" or color.data_type != "BYTE_COLOR":
            raise RuntimeError(f"New Bulwark mesh {obj.name} is missing Color CORNER BYTE_COLOR")

    bpy.context.view_layer.update()
    expected_names = [obj.name for obj in generated]
    scene[SCENE_MARKER] = RECIPE_ID
    scene["rcl_bulwark_visible_supports_expected_details"] = expected_names
    report = {
        "recipeId": RECIPE_ID,
        "applied": True,
        "idempotentReplay": False,
        "hardflowBevelProvided": hardflow_bevel is not None,
        "hardflowBooleanProvided": hardflow_boolean_apply is not None,
        "targetBounds": {
            "UpperDeckBefore": [list(upper_bounds[0]), list(upper_bounds[1])],
            "UpperDeckAfter": [list(upper_bounds_after[0]), list(upper_bounds_after[1])],
            "RadiatorCavityBefore": [list(cavity_min), list(cavity_max)],
            "RadiatorCavityAfter": [list(cavity_bounds_after[0]), list(cavity_bounds_after[1])],
        },
        "movedAssemblies": moved,
        "newDetailObjects": expected_names,
        "newDetailObjectCount": len(generated),
        "radiatorUpperDeckSeat": {"baseZ": cavity_min.z, "deckTopZ": deck_top,
                                   "baseGapMm": (cavity_min.z - deck_top) * 1000.0,
                                   "hatchRebate": hatch_rebate},
        "hatchMount": hatch_mount,
        "lugSupportContacts": lug_support_contacts,
        "shackleRelocations": shackle_relocations,
        "shackleHullMounts": shackle_mounts,
        "sponsonHullTies": sponson_hull_ties,
        "suspensionMounts": suspension_mounts,
        "suspensionMountsAdded": len(suspension_mounts),
        "unresolvedSuspensionInterfaces": unresolved_suspension_interfaces,
        "finalDriveMounts": final_drive_mounts,
        "finalDriveMountsAdded": len(final_drive_mounts),
        "supportRouteContacts": support_route_contacts,
        "trackClearanceIntersections": route_track_intersections,
        "louverMountContacts": louver_mount_contacts,
        "booleanImplementation": "EXACT UpperDeck hatch rebate; no high radiator cage or yaw-volume rail",
        "bevelImplementation": "Hardflow callback for selected mounting plates when supplied; otherwise Blender built-in baked BEVEL operators",
        "preservedFunctionalClearances": [
            "coax muzzle within coax sleeve",
            "howitzer barrel inside bore cavity",
            "exhaust to heat-shield thermal gaps",
            "antenna tip to mast joint",
            "front idler splash-fender rotation gap",
            "road-wheel 2-4 to belt 20 mm inner clearance",
            "articulated shackle loop and pin",
            "turret trunnions, recoil cylinders, and hatch hardware",
        ],
        "limitations": [
            "The recipe changes an isolated Blender candidate only; no source blend, FBX, prefab, or bundle is promoted here.",
            "The radiator/hatch pieces are hull-bound and lowered together; the hatch now sits laterally outboard on a deck rebate, and opening sweep must be checked before promotion.",
            "Suspension and final-drive load paths are reported separately; this minimal visual pass does not add unverified belt-crossing arms.",
            "World-space surface gaps use sampled BVH points and can miss edge-edge minima; any intended support overlap is concealed at the contact seat.",
        ],
    }
    return report
