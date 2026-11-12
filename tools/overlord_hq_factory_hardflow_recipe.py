"""Reproducible Hardflow detail recipe for the Overlord HQ factory.

Hardflow authors the local Boolean/sharpen pass; the normal model builder uses
the same dimensions and geometry with Blender's built-in Exact Boolean and
bevel operators. No Hardflow import is required for headless builds.
"""

from __future__ import annotations

import math
from typing import Any, Callable, Iterable

import bpy
import bmesh
from mathutils import Vector

RECIPE_ID = "overlord-command-hq-factory-hardflow-v1"
MARKER_KEY = "RCL_OverlordHQFactoryHardflowRecipe"
DETAIL_PREFIX = "OverlordCommandHQFactoryHF_"

BooleanApply = Callable[[bpy.types.Object, bpy.types.Object], str | None]
SharpenApply = Callable[[bpy.types.Object], None]


def _find_mesh(objects: Iterable[bpy.types.Object], name: str) -> bpy.types.Object:
    obj = next((row for row in objects if row and row.name == name and row.type == "MESH"), None)
    if obj is None:
        raise ValueError(f"Overlord HQ factory recipe requires mesh {name!r}")
    return obj


def _world_x_bounds(obj: bpy.types.Object) -> tuple[float, float]:
    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    return min(point.x for point in points), max(point.x for point in points)


def _cube(name: str, center, size) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=center)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return obj


def apply_standard_boolean(target: bpy.types.Object, cutter: bpy.types.Object) -> str:
    """Apply an Exact Boolean and clean the resulting surface with Blender tools."""
    modifier = target.modifiers.new("RCL_OverlordFactoryServiceSeat", "BOOLEAN")
    modifier.operation = "DIFFERENCE"
    modifier.solver = "EXACT"
    modifier.object = cutter
    bpy.ops.object.select_all(action="DESELECT")
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    try:
        with bpy.context.temp_override(
            object=target,
            active_object=target,
            selected_objects=[target],
            selected_editable_objects=[target],
        ):
            bpy.ops.object.modifier_apply(modifier=modifier.name)
    except Exception:
        leftover = target.modifiers.get(modifier.name)
        if leftover is not None:
            target.modifiers.remove(leftover)
        raise

    bm = bmesh.new()
    bm.from_mesh(target.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-5)
    bmesh.ops.dissolve_limit(
        bm,
        angle_limit=math.radians(2.0),
        verts=list(bm.verts),
        edges=list(bm.edges),
    )
    loose = [vertex for vertex in bm.verts if not vertex.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(target.data)
    bm.free()
    target.data.update()
    return "EXACT"


def apply_standard_sharpen(target: bpy.types.Object) -> None:
    """Replay Hardflow Smart Sharpen with native Blender bevel/normal modifiers."""
    specs = (
        ("RCL_OverlordFactoryCutEdgeBevel", 0.004, 1, math.radians(42.0)),
        ("RCL_OverlordFactoryMicroBevel", 0.0015, 1, math.radians(45.0)),
    )
    for name, width, segments, angle in specs:
        modifier = target.modifiers.new(name, "BEVEL")
        modifier.width = width
        modifier.segments = segments
        modifier.limit_method = "ANGLE"
        modifier.angle_limit = angle
        modifier.harden_normals = True
        modifier.use_clamp_overlap = True
        bpy.ops.object.select_all(action="DESELECT")
        target.select_set(True)
        bpy.context.view_layer.objects.active = target
        with bpy.context.temp_override(
            object=target,
            active_object=target,
            selected_objects=[target],
            selected_editable_objects=[target],
        ):
            bpy.ops.object.modifier_apply(modifier=modifier.name)

    normal = target.modifiers.new("RCL_OverlordFactoryWeightedNormal", "WEIGHTED_NORMAL")
    normal.keep_sharp = True
    normal.weight = 50
    bpy.ops.object.select_all(action="DESELECT")
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    with bpy.context.temp_override(
        object=target,
        active_object=target,
        selected_objects=[target],
        selected_editable_objects=[target],
    ):
        bpy.ops.object.modifier_apply(modifier=normal.name)


def _add_box(objects, rmk, name, center, size, material, bevel=0.002):
    obj = rmk.make_box(
        name,
        size,
        center,
        material,
        bevel=bevel,
        bevel_segments=1,
        smooth=False,
    )
    objects.append(obj)
    return obj


def _ensure_uv_map(obj: bpy.types.Object) -> None:
    if obj.data.uv_layers.get("UVMap") is not None:
        return
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    result = bpy.ops.uv.smart_project(
        angle_limit=math.radians(66.0),
        island_margin=0.01,
        area_weight=0.0,
        correct_aspect=True,
        scale_to_bounds=False,
    )
    bpy.ops.object.mode_set(mode="OBJECT")
    if "FINISHED" not in result or obj.data.uv_layers.get("UVMap") is None:
        raise RuntimeError(f"Unable to generate a UVMap for modified mesh {obj.name}")


def _add_cylinder(objects, rmk, name, center, radius, depth, material, axis_x=False):
    rotation = (0.0, math.pi / 2.0, 0.0) if axis_x else (0.0, 0.0, 0.0)
    obj = rmk.make_cylinder(
        name,
        radius=radius,
        depth=depth,
        location=center,
        material=material,
        rotation=rotation,
        vertices=12,
        bevel=min(0.003, radius * 0.16, depth * 0.12),
        bevel_segments=1,
    )
    objects.append(obj)
    return obj


def _cut(target, name, center, size, apply_boolean) -> str:
    cutter = _cube(name, center, size)
    try:
        solver = apply_boolean(target, cutter)
        if not solver:
            raise RuntimeError(f"Boolean failed for {name} on {target.name}")
        return str(solver)
    finally:
        if cutter.name in bpy.data.objects:
            bpy.data.objects.remove(cutter, do_unlink=True)


def apply_overlord_hq_factory_details(
    objects: list[bpy.types.Object],
    gunmetal_material: bpy.types.Material,
    apply_boolean: BooleanApply = apply_standard_boolean,
    apply_sharpen: SharpenApply = apply_standard_sharpen,
) -> dict[str, Any]:
    """Seat factory service plates and add working door hinges and a latch.

The six authored access plates are retained and shifted into 75 mm shallow
rebates in their existing sidewalls. Each gets one central quarter-turn
lock. The existing maintenance door receives three aligned barrel hinges,
welded leaves, and a single handle kept clear of the vision panel. The two
landing rails are interrupted at the door opening, with finished end caps and
the center post removed so the service door remains reachable.
    """
    existing_details = [obj for obj in objects if obj.name.startswith(DETAIL_PREFIX)]
    if bpy.context.scene.get(MARKER_KEY) == RECIPE_ID:
        if len(existing_details) != 27:
            raise RuntimeError(
                f"Recipe marker exists but {len(existing_details)} of 27 details are present"
            )
        return {
            "recipeId": RECIPE_ID,
            "status": "alreadyApplied",
            "addedObjects": [obj.name for obj in existing_details],
            "addedObjectCount": len(existing_details),
            "cuts": [],
        }
    if existing_details:
        raise RuntimeError("Partial Overlord factory Hardflow geometry found without its recipe marker")

    sys_path = str(__import__("pathlib").Path(__file__).resolve().parents[1] / "tools")
    import sys
    if sys_path not in sys.path:
        sys.path.insert(0, sys_path)
    import RogueModelKit as rmk

    sidewalls = {
        "L": _find_mesh(objects, "OverlordCommandHQFactoryArmor_Sidewall_L"),
        "R": _find_mesh(objects, "OverlordCommandHQFactoryArmor_Sidewall_R"),
    }
    plates = {
        (side, index): _find_mesh(
            objects, f"OverlordCommandHQFactoryArmor_ServicePlate_{side}_{index}"
        )
        for side in ("L", "R")
        for index in (1, 2, 3)
    }
    _find_mesh(objects, "OverlordCommandHQFactoryMaintenance_Door")
    _find_mesh(objects, "OverlordCommandHQFactoryMaintenance_DoorFrame")
    lower_rail = _find_mesh(objects, "OverlordCommandHQFactoryMaintenance_Handrail_Lower")
    upper_rail = _find_mesh(objects, "OverlordCommandHQFactoryMaintenance_Handrail_Upper")
    center_post = _find_mesh(objects, "OverlordCommandHQFactoryMaintenance_RailPost_2")
    right_post = _find_mesh(objects, "OverlordCommandHQFactoryMaintenance_RailPost_3")
    center_post_name = center_post.name

    cuts: list[dict[str, str]] = []
    added: list[bpy.types.Object] = []

    # Shallow, precise rebates give the existing armor covers a machined seat
    # and continuous reveal without cutting through the thick sidewall.
    for side, sign in (("L", -1.0), ("R", 1.0)):
        wall = sidewalls[side]
        bounds = _world_x_bounds(wall)
        wall_surface_x = bounds[0] if sign < 0.0 else bounds[1]
        cutter_center_x = wall_surface_x - sign * 0.040
        for index in (1, 2, 3):
            plate = plates[(side, index)]
            width_y = float(plate.dimensions.y) + 0.024
            height_z = float(plate.dimensions.z) + 0.024
            name = f"{DETAIL_PREFIX}Cutter_ServiceSeat_{side}_{index}"
            cuts.append({
                "target": wall.name,
                "name": name,
                "solver": _cut(
                    wall,
                    name,
                    (cutter_center_x, float(plate.location.y), float(plate.location.z)),
                    (0.100, width_y, height_z),
                    apply_boolean,
                ),
            })
            # Move the plate 60 mm toward the chassis: its outer surface lands
            # almost flush, with enough depth for the recessed gasket reveal.
            plate.location.x -= sign * 0.060

            outer_face_x = float(plate.location.x) + sign * float(plate.dimensions.x) * 0.5
            lock_center_x = outer_face_x + sign * 0.009
            _add_cylinder(
                added,
                rmk,
                f"{DETAIL_PREFIX}ServiceLock_{side}_{index}",
                (lock_center_x, float(plate.location.y), float(plate.location.z)),
                radius=0.020,
                depth=0.018,
                material=gunmetal_material,
                axis_x=True,
            )
            _add_box(
                added,
                rmk,
                f"{DETAIL_PREFIX}ServiceLockHandle_{side}_{index}",
                (outer_face_x + sign * 0.022, float(plate.location.y), float(plate.location.z)),
                (0.010, 0.074, 0.014),
                gunmetal_material,
                bevel=0.0015,
            )

        apply_sharpen(wall)

    # Open a 920 mm passage through the existing landing rails at the service
    # door. Keep both outboard supports and move the right support to the new
    # end; this removes the original obstruction instead of dressing over it.
    for rail, tag, center_z in (
        (lower_rail, "Lower", 0.920),
        (upper_rail, "Upper", 1.305),
    ):
        name = f"{DETAIL_PREFIX}Cutter_DoorAccess_{tag}Rail"
        cuts.append({
            "target": rail.name,
            "name": name,
            "solver": _cut(
                rail,
                name,
                (-4.670, 0.560, center_z),
                (0.180, 0.920, 0.520),
                apply_boolean,
            ),
        })
        apply_sharpen(rail)

        cap_material = rail.data.materials[0] if rail.data.materials else gunmetal_material
        for side_tag, y in (("L", 0.100), ("R", 1.020)):
            _add_box(
                added,
                rmk,
                f"{DETAIL_PREFIX}DoorAccessRailEndCap_{tag}_{side_tag}",
                (-4.670, y, center_z),
                (0.078, 0.028, 0.082),
                cap_material,
                bevel=0.004,
            )

    bpy.data.objects.remove(center_post, do_unlink=True)
    if center_post in objects:
        objects.remove(center_post)
    right_post.location.y = 1.050
    _ensure_uv_map(right_post)

    # The hinges follow the left edge of the existing service door. Each
    # barrel is tied to both the door leaf and its fixed frame leaf.
    hinge_y = 0.165
    for index, z in enumerate((0.86, 1.35, 1.84), start=1):
        _add_cylinder(
            added,
            rmk,
            f"{DETAIL_PREFIX}MaintenanceHingeBarrel_{index}",
            (-4.165, hinge_y, z),
            radius=0.031,
            depth=0.112,
            material=gunmetal_material,
        )
        _add_box(
            added,
            rmk,
            f"{DETAIL_PREFIX}MaintenanceHingeDoorLeaf_{index}",
            (-4.158, 0.235, z),
            (0.018, 0.142, 0.070),
            gunmetal_material,
            bevel=0.002,
        )
        _add_box(
            added,
            rmk,
            f"{DETAIL_PREFIX}MaintenanceHingeFrameLeaf_{index}",
            (-4.063, 0.095, z),
            (0.018, 0.126, 0.070),
            gunmetal_material,
            bevel=0.002,
        )

    # Put the latch on the clear rear edge of the door, away from its glazing.
    _add_box(
        added,
        rmk,
        f"{DETAIL_PREFIX}MaintenanceLatchPlate",
        (-4.160, 0.842, 1.285),
        (0.018, 0.084, 0.204),
        gunmetal_material,
        bevel=0.002,
    )
    _add_cylinder(
        added,
        rmk,
        f"{DETAIL_PREFIX}MaintenanceLatchGrip",
        (-4.180, 0.842, 1.285),
        radius=0.014,
        depth=0.126,
        material=gunmetal_material,
    )

    # All new hardware is mechanical gunmetal: team-mask alpha remains zero;
    # AO, convex wear, and soot channels are generated by the project baker.
    rmk.bake_advanced_vertex_shading(
        added,
        wear_level="factory_new",
        soot_intensity=0.0,
        structural_wear_mask=True,
    )
    for obj in added:
        obj["rcl_hardflow_recipe"] = RECIPE_ID

    bpy.context.scene[MARKER_KEY] = RECIPE_ID
    return {
        "recipeId": RECIPE_ID,
        "status": "applied",
        "cuts": cuts,
        "sharpenedTargets": [obj.name for obj in sidewalls.values()],
        "recessedServicePlates": [obj.name for obj in plates.values()],
        "addedObjects": [obj.name for obj in added],
        "addedObjectCount": len(added),
        "doorAccessClearance": {
            "removedObstruction": center_post_name,
            "relocatedSupport": right_post.name,
            "openingWidthM": 0.920,
            "cutRails": [lower_rail.name, upper_rail.name],
        },
        "hardflowOptional": True,
        "budgetProfile": "content/source-art/model-budget-profile.json",
    }
