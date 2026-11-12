"""Reproducible recessed radiator vent for the Bulwark Siege Tank.

The normal model builder uses Blender's built-in Exact Boolean and bevel
operations. Hardflow can be used for a manual visual authoring pass, but this
recipe keeps the production build independent of that optional extension.
"""

from __future__ import annotations

import math
from typing import Iterable

import bpy
import bmesh
from mathutils import Vector

import RogueModelKit as rmk


RECIPE_ID = "bulwark-radiator-vent-seat-v1"
TARGET_NAME = "BulwarkSiegeTankEngine_RadiatorCavity"
UPPER_DECK_NAME = "BulwarkSiegeTankArmor_UpperDeck"
MARKER_KEY = "rcl_bulwark_radiator_vent_recipe"
BOUNDS_MARKER_KEY = "rcl_bulwark_radiator_vent_bounds"
LOUVER_NAMES = tuple(
    f"BulwarkSiegeTankEngine_RadiatorVentLouver_{index:02d}"
    for index in range(1, 4)
)
HATCH_OBJECT_NAMES = (
    "BulwarkSiegeTankArmor_AccessHatch_HatchPlate",
    "BulwarkSiegeTankArmor_AccessHatch_CoamingRim",
    "BulwarkSiegeTankArmor_AccessHatch_HingeBar",
    "BulwarkSiegeTankArmor_AccessHatch_DogLatch",
)


def _find_mesh(objects: Iterable[bpy.types.Object], name: str) -> bpy.types.Object:
    target = next(
        (obj for obj in objects if obj and obj.name == name and obj.type == "MESH"),
        None,
    )
    if target is None:
        raise ValueError(f"Bulwark radiator recipe requires mesh {name!r}")
    return target


def _world_bounds(obj: bpy.types.Object) -> tuple[Vector, Vector]:
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return (
        Vector(min(corner[axis] for corner in corners) for axis in range(3)),
        Vector(max(corner[axis] for corner in corners) for axis in range(3)),
    )


def _bounds_equal(a: tuple[Vector, Vector], b: tuple[Vector, Vector], tolerance=1e-5) -> bool:
    return all((a[index] - b[index]).length <= tolerance for index in range(2))


def seat_bulwark_radiator_assembly(
    objects: Iterable[bpy.types.Object] | None = None,
) -> dict[str, object]:
    """Seat the vented cavity and hatch together, including older marked sources.

    This migration is safe to call after the vent recipe: it computes the
    remaining translation from current bounds and updates the vent bounds
    marker, so the regular vent replay continues to validate as a no-op.
    """
    objects = list(objects) if objects is not None else list(bpy.context.scene.objects)
    target = _find_mesh(objects, TARGET_NAME)
    upper_deck = _find_mesh(objects, UPPER_DECK_NAME)
    louvers = []
    for name in LOUVER_NAMES:
        louver = _find_mesh(objects, name)
        louvers.append(louver)
    hatch_objects = [_find_mesh(objects, name) for name in HATCH_OBJECT_NAMES]
    coaming = _find_mesh(objects, HATCH_OBJECT_NAMES[1])

    deck_min, deck_max = _world_bounds(upper_deck)
    cavity_before = _world_bounds(target)
    hatch_before = _world_bounds(coaming)
    delta = Vector((
        0.0,
        deck_min.y - cavity_before[0].y,
        deck_max.z - cavity_before[0].z,
    ))
    desired_hatch_gap = 0.032
    lateral_x = max(0.0, cavity_before[1].x + desired_hatch_gap - hatch_before[0].x)
    hatch_delta = delta + Vector((lateral_x, 0.0, 0.0))

    if delta.length > 1e-7:
        for obj in (target, *louvers):
            matrix = obj.matrix_world.copy()
            matrix.translation += delta
            obj.matrix_world = matrix
    if hatch_delta.length > 1e-7:
        for obj in hatch_objects:
            matrix = obj.matrix_world.copy()
            matrix.translation += hatch_delta
            obj.matrix_world = matrix

    bpy.context.view_layer.update()
    cavity_after = _world_bounds(target)
    hatch_after = _world_bounds(coaming)
    deck_after = _world_bounds(upper_deck)
    if abs(cavity_after[0].z - deck_after[1].z) > 1e-5:
        raise RuntimeError("Bulwark radiator cavity base did not seat on UpperDeck")
    if abs(cavity_after[0].y - deck_after[0].y) > 1e-5:
        raise RuntimeError("Bulwark radiator rear edge did not remain over UpperDeck")
    if cavity_after[0].x < deck_after[0].x - 1e-5 or cavity_after[1].x > deck_after[1].x + 1e-5:
        raise RuntimeError("Bulwark radiator cavity footprint falls outside UpperDeck")
    if hatch_after[0].x < deck_after[0].x - 1e-5 or hatch_after[1].x > deck_after[1].x + 1e-5:
        raise RuntimeError("Bulwark access hatch footprint falls outside UpperDeck")
    if hatch_after[0].y < deck_after[0].y - 1e-5 or hatch_after[1].y > deck_after[1].y + 1e-5:
        raise RuntimeError("Bulwark access hatch footprint falls outside UpperDeck")
    measured_gap = hatch_after[0].x - cavity_after[1].x
    if measured_gap < desired_hatch_gap - 1e-5:
        raise RuntimeError(f"Bulwark hatch/cavity seat gap is only {measured_gap * 1000.0:.3f} mm")

    target[BOUNDS_MARKER_KEY] = [
        *(float(value) for value in cavity_after[0]),
        *(float(value) for value in cavity_after[1]),
    ]
    target.data.update()
    return {
        "cavityBoundsBefore": [[float(value) for value in cavity_before[0]], [float(value) for value in cavity_before[1]]],
        "cavityBoundsAfter": [[float(value) for value in cavity_after[0]], [float(value) for value in cavity_after[1]]],
        "upperDeckBounds": [[float(value) for value in deck_after[0]], [float(value) for value in deck_after[1]]],
        "clusterTranslation": [float(value) for value in delta],
        "hatchLateralOffsetX": float(lateral_x),
        "hatchCoamingBoundsAfter": [[float(value) for value in hatch_after[0]], [float(value) for value in hatch_after[1]]],
        "hatchToCavitySeatGapMm": float(measured_gap * 1000.0),
        "boundsMarkerUpdated": True,
    }


def _mesh_attribute_signature(obj: bpy.types.Object) -> tuple[tuple[str, ...], tuple[tuple[str, str, str], ...]]:
    return (
        tuple(layer.name for layer in obj.data.uv_layers),
        tuple((attr.name, attr.domain, attr.data_type) for attr in obj.data.color_attributes),
    )


def _make_rounded_cutter(
    name: str,
    center: Vector,
    size: tuple[float, float, float],
    corner_bevel: float,
    material: bpy.types.Material | None,
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=center)
    cutter = bpy.context.object
    cutter.name = name
    cutter.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if material is not None:
        cutter.data.materials.append(material)

    if corner_bevel > 0.0:
        bevel = cutter.modifiers.new(f"{name}_BuiltInBevel", "BEVEL")
        bevel.width = corner_bevel
        bevel.segments = 2
        bevel.limit_method = "ANGLE"
        bevel.angle_limit = math.radians(20.0)
        bevel.use_clamp_overlap = True
        bpy.context.view_layer.objects.active = cutter
        with bpy.context.temp_override(
            object=cutter,
            active_object=cutter,
            selected_objects=[cutter],
            selected_editable_objects=[cutter],
        ):
            bpy.ops.object.modifier_apply(modifier=bevel.name)
    return cutter


def _apply_exact_difference(
    target: bpy.types.Object,
    cutter: bpy.types.Object,
    modifier_name: str,
) -> str:
    boolean = target.modifiers.new(modifier_name, "BOOLEAN")
    boolean.operation = "DIFFERENCE"
    boolean.solver = "EXACT"
    boolean.object = cutter
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    with bpy.context.temp_override(
        object=target,
        active_object=target,
        selected_objects=[target],
        selected_editable_objects=[target],
    ):
        bpy.ops.object.modifier_apply(modifier=boolean.name)

    bm = bmesh.new()
    bm.from_mesh(target.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-4)
    bmesh.ops.dissolve_degenerate(bm, dist=1e-4, edges=list(bm.edges))
    loose = [vertex for vertex in bm.verts if not vertex.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(target.data)
    bm.free()
    target.data.update()
    return "EXACT"


def _smart_uv_project(obj: bpy.types.Object) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(
        angle_limit=math.radians(60.0),
        island_margin=0.02,
        area_weight=0.0,
        correct_aspect=True,
        scale_to_bounds=False,
    )
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.select_set(False)


def apply_bulwark_radiator_vent(
    objects: Iterable[bpy.types.Object] | None = None,
) -> dict[str, object]:
    """Cut the radiator vent and seat its attached deck assembly at hull height.

    The opening is derived from the existing cavity's world bounds. Three
    inset louvers stay with the cavity. The cavity, louvers, and four existing
    access-hatch meshes are translated as one hull-mounted cluster so the
    cavity base meets UpperDeck and its full rear edge stays over the deck.
    """
    objects = list(objects) if objects is not None else list(bpy.context.scene.objects)
    target = _find_mesh(objects, TARGET_NAME)
    upper_deck = _find_mesh(objects, UPPER_DECK_NAME)
    target_bounds_before = _world_bounds(target)
    upper_deck_bounds_before = _world_bounds(upper_deck)
    uv_before, color_before = _mesh_attribute_signature(target)
    if "UVMap" not in uv_before:
        raise ValueError(f"{TARGET_NAME} must retain its existing UVMap")
    existing_color = target.data.color_attributes.get("Color")
    if existing_color is not None and (existing_color.domain != "CORNER" or existing_color.data_type != "BYTE_COLOR"):
        raise ValueError(f"{TARGET_NAME} Color must be a CORNER BYTE_COLOR attribute")

    existing_louvers = [bpy.data.objects.get(name) for name in LOUVER_NAMES]
    recipe_marker = target.get(MARKER_KEY)
    if recipe_marker == RECIPE_ID:
        missing = [name for name, obj in zip(LOUVER_NAMES, existing_louvers) if obj is None or obj.type != "MESH"]
        if missing:
            raise ValueError("Bulwark radiator recipe marker exists but louvers are missing: " + ", ".join(missing))
        color = target.data.color_attributes.get("Color")
        if color is None or color.domain != "CORNER" or color.data_type != "BYTE_COLOR":
            raise ValueError(f"{TARGET_NAME} lost its CORNER BYTE_COLOR wear attribute")
        stored_bounds = target.get(BOUNDS_MARKER_KEY)
        current_bounds = [*target_bounds_before[0], *target_bounds_before[1]]
        if stored_bounds is None or any(abs(float(a) - float(b)) > 1e-5 for a, b in zip(stored_bounds, current_bounds)):
            raise ValueError(f"{TARGET_NAME} bounds changed after the radiator vent recipe")
        for louver in existing_louvers:
            if louver.data.uv_layers.get("UVMap") is None:
                raise ValueError(f"{louver.name} lost UVMap")
            louver_color = louver.data.color_attributes.get("Color")
            if louver_color is None or louver_color.domain != "CORNER" or louver_color.data_type != "BYTE_COLOR":
                raise ValueError(f"{louver.name} lost its CORNER BYTE_COLOR wear attribute")
        return {
            "recipeId": RECIPE_ID,
            "applied": False,
            "target": TARGET_NAME,
            "louverObjects": list(LOUVER_NAMES),
            "booleanSolver": "EXACT",
            "bevelImplementation": "Blender built-in BEVEL modifiers on cutters and louver boxes",
            "targetBounds": [[*target_bounds_before[0]], [*target_bounds_before[1]]],
            "upperDeckBounds": [[*upper_deck_bounds_before[0]], [*upper_deck_bounds_before[1]]],
            "uvLayers": list(uv_before),
            "colorAttributes": [list(row) for row in color_before],
        }
    if any(obj is not None for obj in existing_louvers):
        raise ValueError("Partial Bulwark radiator vent objects exist without the recipe marker")

    minimum, maximum = target_bounds_before
    size = maximum - minimum
    center = (maximum + minimum) * 0.5
    seat_width = size.x * 0.70
    seat_length = size.y * 0.82
    seat_depth = min(0.010, size.z * 0.32)
    seat_floor_z = maximum.z - seat_depth
    material = next((slot for slot in target.data.materials if slot is not None), None)

    seat = _make_rounded_cutter(
        "BulwarkHF_Cutter_RadiatorSeat",
        Vector((center.x, center.y, maximum.z - seat_depth * 0.5 + 0.002)),
        (seat_width, seat_length, seat_depth + 0.004),
        min(0.008, seat_width * 0.018),
        material,
    )
    try:
        _apply_exact_difference(target, seat, "RCL_Bulwark_RadiatorSeat_BOOLEAN")
    finally:
        bpy.data.objects.remove(seat, do_unlink=True)

    slot_width_x = seat_width - 0.035
    slot_width_y = 0.035
    slot_offsets = (-seat_length * 0.25, 0.0, seat_length * 0.25)
    slot_centers: list[Vector] = []
    for index, offset in enumerate(slot_offsets, 1):
        slot_center = Vector((center.x, center.y + offset, center.z))
        slot_centers.append(slot_center)
        cutter = _make_rounded_cutter(
            f"BulwarkHF_Cutter_RadiatorSlot_{index:02d}",
            slot_center,
            (slot_width_x, slot_width_y, size.z + 0.008),
            0.002,
            material,
        )
        try:
            _apply_exact_difference(target, cutter, f"RCL_Bulwark_RadiatorSlot_{index:02d}_BOOLEAN")
        finally:
            bpy.data.objects.remove(cutter, do_unlink=True)

    louvers: list[bpy.types.Object] = []
    louver_angle = math.radians(18.0)
    louver_length_x = slot_width_x - 0.045
    louver_width_y = slot_width_y - 0.012
    louver_thickness_z = 0.004
    louver_half_height = 0.5 * (
        louver_width_y * abs(math.sin(louver_angle))
        + louver_thickness_z * abs(math.cos(louver_angle))
    )
    louver_center_z = seat_floor_z - louver_half_height - 0.001
    for index, slot_center in enumerate(slot_centers, 1):
        louver = rmk.make_box(
            LOUVER_NAMES[index - 1],
            (louver_length_x, louver_width_y, louver_thickness_z),
            (center.x, slot_center.y + 0.004, louver_center_z),
            material,
            bevel=0.001,
            rotation=(louver_angle, 0.0, 0.0),
            bevel_segments=1,
            smooth=False,
        )
        louver[MARKER_KEY] = RECIPE_ID
        louver["rcl_bulwark_vent_slot_index"] = index
        _smart_uv_project(louver)
        louvers.append(louver)

    shading_targets = [target, *louvers]
    missing_color = [
        obj for obj in shading_targets
        if obj.data.color_attributes.get("Color") is None
    ]
    if missing_color:
        rmk.bake_advanced_vertex_shading(
            missing_color,
            wear_level="field_tested",
            soot_intensity=1.0,
            exhaust_locs=[Vector((0.0, -1.35, 1.18))],
            muzzle_loc=Vector((0.0, 1.765, 1.992)),
            structural_wear_mask=True,
        )

    bpy.context.view_layer.update()
    target_bounds_after_cut = _world_bounds(target)
    if not _bounds_equal(target_bounds_before, target_bounds_after_cut):
        raise RuntimeError(f"Bulwark radiator vent changed target bounds: {target_bounds_before} -> {target_bounds_after_cut}")

    hatch_names = HATCH_OBJECT_NAMES
    seat_result = seat_bulwark_radiator_assembly()

    bpy.context.view_layer.update()
    target_bounds_after = _world_bounds(target)
    upper_deck_bounds_after = _world_bounds(upper_deck)
    if not _bounds_equal(upper_deck_bounds_before, upper_deck_bounds_after):
        raise RuntimeError("Bulwark radiator vent unexpectedly changed UpperDeck bounds")
    uv_after, color_after = _mesh_attribute_signature(target)
    expected_colors = color_before
    if not any(name == "Color" for name, _domain, _data_type in expected_colors):
        expected_colors = (*expected_colors, ("Color", "CORNER", "BYTE_COLOR"))
    if uv_after != uv_before or color_after != expected_colors:
        raise RuntimeError("Bulwark radiator Boolean changed the radiator UV or color-attribute contract")
    color = target.data.color_attributes.get("Color")
    if color is None or color.domain != "CORNER" or color.data_type != "BYTE_COLOR":
        raise RuntimeError(f"{TARGET_NAME} did not retain a CORNER BYTE_COLOR wear attribute")
    for louver in louvers:
        if "UVMap" not in {layer.name for layer in louver.data.uv_layers}:
            raise RuntimeError(f"Bulwark louver {louver.name} did not receive UVMap")
        louver_min, louver_max = _world_bounds(louver)
        if any(louver_min[axis] < target_bounds_after[0][axis] - 1e-5 or louver_max[axis] > target_bounds_after[1][axis] + 1e-5 for axis in range(3)):
            raise RuntimeError(f"Bulwark louver {louver.name} extends outside the radiator cavity bounds")

    target[MARKER_KEY] = RECIPE_ID
    target[BOUNDS_MARKER_KEY] = [*target_bounds_after[0], *target_bounds_after[1]]
    target.data.update()
    return {
        "recipeId": RECIPE_ID,
        "applied": True,
        "target": TARGET_NAME,
        "upperDeckUntouchedByVentCut": True,
        "attachedHullCluster": [TARGET_NAME, *LOUVER_NAMES, *hatch_names],
        "seating": seat_result,
        "louverObjects": list(LOUVER_NAMES),
        "booleanSolver": "EXACT",
        "bevelImplementation": "Blender built-in BEVEL modifiers on cutters and louver boxes",
        "seatWidth": seat_width,
        "seatLength": seat_length,
        "seatDepth": seat_depth,
        "slotCenters": [tuple(float(value) for value in point) for point in slot_centers],
        "targetBoundsBefore": [[float(value) for value in target_bounds_before[0]], [float(value) for value in target_bounds_before[1]]],
        "targetBounds": [[float(value) for value in target_bounds_after[0]], [float(value) for value in target_bounds_after[1]]],
        "upperDeckBounds": [[float(value) for value in upper_deck_bounds_after[0]], [float(value) for value in upper_deck_bounds_after[1]]],
        "uvLayers": list(uv_after),
        "colorAttributes": [list(row) for row in color_after],
    }
