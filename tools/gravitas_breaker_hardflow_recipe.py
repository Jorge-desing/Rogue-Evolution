"""Reproducible Gravitas Breaker service-panel, vent, and fastener recipe.

The standalone Hardflow pilot and the normal headless builder share the same
coordinates and authored inserts. Only the boolean/bevel callbacks differ:
Hardflow is used for the review candidate, while the production builder uses
Blender's built-in modifiers so Hardflow is never a build dependency.
"""

from __future__ import annotations

import math
from typing import Any, Callable, Iterable

import bpy
import bmesh
from mathutils import Vector


REPO_ROOT = __import__("pathlib").Path(__file__).resolve().parents[1]
import sys

sys.path.insert(0, str(REPO_ROOT / "tools"))
import RogueModelKit as rmk


BooleanApply = Callable[[bpy.types.Object, bpy.types.Object], str | None]
BevelApply = Callable[[bpy.types.Object], None]


def _find_mesh(objects: Iterable[bpy.types.Object], name: str) -> bpy.types.Object:
    exact = next((obj for obj in objects if obj and obj.name == name and obj.type == "MESH"), None)
    if exact is None:
        raise ValueError(f"Gravitas Hardflow recipe requires mesh {name!r}")
    return exact


def _cube(name: str, center, size, material=None, rotation=(0.0, 0.0, 0.0)) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=center)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    obj.rotation_euler = rotation
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if material is not None:
        obj.data.materials.append(material)
    return obj


def apply_standard_boolean(target: bpy.types.Object, cutter: bpy.types.Object) -> str:
    """Apply a normal Blender Boolean modifier with exact solver and clean normals."""
    modifier = target.modifiers.new("RCL_GravitasServiceBoolean", "BOOLEAN")
    modifier.operation = "DIFFERENCE"
    modifier.solver = "EXACT"
    modifier.object = cutter
    if len(target.modifiers) > 1:
        target.modifiers.move(len(target.modifiers) - 1, 0)
    target.modifiers.active = modifier
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
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-4)
    bmesh.ops.dissolve_limit(
        bm, angle_limit=math.radians(5.0), verts=list(bm.verts), edges=list(bm.edges)
    )
    loose = [vert for vert in bm.verts if not vert.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(target.data)
    bm.free()
    target.data.update()
    return "EXACT"


def apply_standard_bevel(target: bpy.types.Object) -> None:
    """Reproduce Hardflow's two-tier sharpen with built-in Blender modifiers."""
    bevel_specs = (
        ("RCL_GravitasCutEdgeBevel", 0.004, 1, math.radians(42.0)),
        ("RCL_GravitasMicroBevel", 0.0015, 1, math.radians(45.0)),
    )
    for name, width, segments, angle in bevel_specs:
        modifier = target.modifiers.new(name, "BEVEL")
        modifier.width = width
        modifier.segments = segments
        modifier.limit_method = "ANGLE"
        modifier.angle_limit = angle
        modifier.harden_normals = True
        modifier.use_clamp_overlap = True
        if len(target.modifiers) > 1:
            target.modifiers.move(len(target.modifiers) - 1, 0)
        target.modifiers.active = modifier
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

    normal = target.modifiers.new("RCL_GravitasWeightedNormal", "WEIGHTED_NORMAL")
    normal.keep_sharp = True
    normal.weight = 50
    if len(target.modifiers) > 1:
        target.modifiers.move(len(target.modifiers) - 1, 0)
    target.modifiers.active = normal
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


def _cut(
    target: bpy.types.Object,
    name: str,
    center,
    size,
    apply_boolean: BooleanApply,
    rotation=(0.0, 0.0, 0.0),
) -> str:
    cutter = _cube(name, center, size, rotation=rotation)
    try:
        solver = apply_boolean(target, cutter)
        if not solver:
            raise RuntimeError(f"Boolean failed for {name} on {target.name}")
        return str(solver)
    finally:
        if cutter.name in bpy.data.objects:
            bpy.data.objects.remove(cutter, do_unlink=True)


def _add_box(objects, name, center, size, material, bevel=0.002, rotation=(0.0, 0.0, 0.0)):
    obj = rmk.make_box(
        name,
        size,
        center,
        material,
        bevel=bevel,
        rotation=rotation,
        bevel_segments=1,
        smooth=False,
    )
    objects.append(obj)
    return obj


def _add_bolt(objects, name, center, material, axis="X", radius=0.014):
    rotation = (0.0, math.pi / 2.0, 0.0) if axis == "X" else (0.0, 0.0, 0.0)
    obj = rmk.make_cylinder(
        name,
        radius=radius,
        depth=0.012,
        location=center,
        material=material,
        rotation=rotation,
        vertices=8,
        bevel=0.001,
        bevel_segments=1,
    )
    objects.append(obj)
    return obj


def _add_axis_cylinder(
    objects: list[bpy.types.Object],
    name: str,
    center,
    radius: float,
    depth: float,
    material,
    axis: str = "X",
    vertices: int = 24,
    bevel: float = 0.0015,
):
    rotation = (0.0, math.pi / 2.0, 0.0) if axis == "X" else (0.0, 0.0, 0.0)
    obj = rmk.make_cylinder(
        name,
        radius=radius,
        depth=depth,
        location=center,
        material=material,
        rotation=rotation,
        vertices=vertices,
        bevel=bevel,
        bevel_segments=1,
    )
    objects.append(obj)
    return obj


def _cylinder_cutter(name: str, center, radius: float, depth: float) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=32,
        radius=radius,
        depth=depth,
        location=center,
        rotation=(0.0, math.pi / 2.0, 0.0),
    )
    cutter = bpy.context.object
    cutter.name = name
    return cutter


def _direction_rotation(direction):
    return Vector(direction).normalized().to_track_quat("Z", "Y").to_euler()


def _direction_cylinder_cutter(name: str, center, radius: float, depth: float, direction):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=32,
        radius=radius,
        depth=depth,
        location=center,
        rotation=_direction_rotation(direction),
    )
    cutter = bpy.context.object
    cutter.name = name
    return cutter


def _first_visible_mesh_hit(
    objects: Iterable[bpy.types.Object],
    origin: Vector,
    direction: Vector,
    max_distance: float,
) -> tuple[bpy.types.Object | None, Vector | None]:
    """Return the first render-mesh hit, excluding export-only collision hulls."""
    axis = direction.normalized()
    best: tuple[float, bpy.types.Object, Vector] | None = None
    for candidate in objects:
        if (candidate.type != "MESH" or candidate.hide_render or
                "_COLLIDER_" in candidate.name):
            continue
        inverse = candidate.matrix_world.inverted_safe()
        local_origin = inverse @ origin
        local_direction = (inverse.to_3x3() @ axis).normalized()
        hit, local_point, _normal, _face_index = candidate.ray_cast(
            local_origin,
            local_direction,
            distance=max_distance * 100.0,
        )
        if not hit:
            continue
        world_point = candidate.matrix_world @ local_point
        distance = (world_point - origin).dot(axis)
        if -1e-6 <= distance <= max_distance + 1e-6 and (best is None or distance < best[0]):
            best = (distance, candidate, world_point)
    return (best[1], best[2]) if best else (None, None)


def _add_direction_torus(objects, name, center, major_radius, minor_radius, material, direction):
    bpy.ops.mesh.primitive_torus_add(
        major_segments=24,
        minor_segments=8,
        major_radius=major_radius,
        minor_radius=minor_radius,
        location=center,
        rotation=_direction_rotation(direction),
    )
    obj = bpy.context.object
    obj.name = name
    if material is not None:
        obj.data.materials.append(material)
    objects.append(obj)
    return obj


def _add_flexible_service_line(objects, name, points, material, radius=0.014):
    curve = bpy.data.curves.new(name + "_Curve", "CURVE")
    curve.dimensions = "3D"
    curve.resolution_u = 12
    curve.bevel_depth = radius
    curve.bevel_resolution = 2
    curve.resolution_u = 12
    spline = curve.splines.new("BEZIER")
    spline.bezier_points.add(len(points) - 1)
    for bezier_point, point in zip(spline.bezier_points, points):
        bezier_point.co = point
        bezier_point.handle_left_type = "AUTO"
        bezier_point.handle_right_type = "AUTO"
    if material is not None:
        curve.materials.append(material)
    obj = bpy.data.objects.new(name, curve)
    bpy.context.collection.objects.link(obj)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.convert(target="MESH")
    mesh_obj = bpy.context.object
    mesh_obj.name = name
    objects.append(mesh_obj)
    return mesh_obj


def apply_gravitas_breaker_details(
    objects: list[bpy.types.Object],
    armor_material,
    mechanical_material,
    apply_boolean: BooleanApply = apply_standard_boolean,
    apply_bevel: BevelApply = apply_standard_bevel,
) -> dict[str, Any]:
    """Apply the authored Hardflow pilot geometry to hull, turret, and cannon."""
    core = _find_mesh(objects, "GravitasBreakerArmor_RearPowerCore")
    turret = _find_mesh(objects, "GravitasBreakerTurret_BaseShell")
    cannon = _find_mesh(objects, "GravitasBreakerGravCannon_Gravity_BoreTube")
    cuts: list[dict[str, str]] = []
    added: list[str] = []

    # Recess a serviceable heat-exchanger access panel into both power-core sides.
    for side, tag in ((-1.0, "L"), (1.0, "R")):
        cuts.append({
            "target": core.name,
            "name": f"GravitasBreakerHF_Cutter_HullService_{tag}",
            "solver": _cut(
                core,
                f"GravitasBreakerHF_Cutter_HullService_{tag}",
                (side * 0.700, -1.405, 1.292),
                (0.100, 0.360, 0.220),
                apply_boolean,
            ),
        })
        _add_box(
            objects,
            f"GravitasBreakerHF_HullServiceInsert_{tag}",
            (side * 0.674, -1.405, 1.292),
            (0.016, 0.266, 0.132),
            mechanical_material,
            bevel=0.003,
        )
        for index, z in enumerate((1.254, 1.292, 1.330), 1):
            _add_box(
                objects,
                f"GravitasBreakerHF_HullVentLouver_{tag}_{index}",
                (side * 0.686, -1.405, z),
                (0.014, 0.208, 0.010),
                armor_material,
                bevel=0.002,
            )
            added.append(f"GravitasBreakerHF_HullVentLouver_{tag}_{index}")
        for index, (dy, dz) in enumerate(((-0.153, -0.088), (0.153, -0.088), (-0.153, 0.088), (0.153, 0.088)), 1):
            name = f"GravitasBreakerHF_HullServiceFastener_{tag}_{index}"
            _add_bolt(objects, name, (side * 0.721, -1.405 + dy, 1.292 + dz), mechanical_material)
            added.append(name)
        added.append(f"GravitasBreakerHF_HullServiceInsert_{tag}")

    # Add two compact access panels to the turret bearing shell, clear of the
    # cannon trunnions and keeping the tank's original siege-turret silhouette.
    for side, tag in ((-1.0, "L"), (1.0, "R")):
        cuts.append({
            "target": turret.name,
            "name": f"GravitasBreakerHF_Cutter_TurretService_{tag}",
            "solver": _cut(
                turret,
                f"GravitasBreakerHF_Cutter_TurretService_{tag}",
                (side * 0.780, -0.315, 1.365),
                (0.080, 0.235, 0.150),
                apply_boolean,
            ),
        })
        _add_box(
            objects,
            f"GravitasBreakerHF_TurretServiceInsert_{tag}",
            (side * 0.754, -0.315, 1.365),
            (0.016, 0.157, 0.086),
            mechanical_material,
            bevel=0.003,
        )
        for index, (dy, dz) in enumerate(((-0.094, -0.054), (0.094, -0.054), (-0.094, 0.054), (0.094, 0.054)), 1):
            name = f"GravitasBreakerHF_TurretServiceFastener_{tag}_{index}"
            _add_bolt(objects, name, (side * 0.807, -0.315 + dy, 1.365 + dz), mechanical_material, radius=0.010)
            added.append(name)
        added.append(f"GravitasBreakerHF_TurretServiceInsert_{tag}")

    # Cut three short heat-relief slots in each side of the cannon's straight
    # bore sleeve between accelerator collars; keep them away from muzzle and
    # projectile socket geometry.
    for side, tag in ((-1.0, "L"), (1.0, "R")):
        for index, y in enumerate((2.604, 2.645, 2.686), 1):
            slot = f"GravitasBreakerHF_Cutter_CannonVent_{tag}_{index}"
            cuts.append({
                "target": cannon.name,
                "name": slot,
                "solver": _cut(
                    cannon,
                    slot,
                    (side * 0.136, y, 1.579),
                    (0.042, 0.023, 0.040),
                    apply_boolean,
                ),
            })
            name = f"GravitasBreakerHF_CannonVentLouver_{tag}_{index}"
            _add_box(
                objects,
                name,
                (side * 0.119, y, 1.579),
                (0.010, 0.014, 0.024),
                mechanical_material,
                bevel=0.0015,
            )
            added.append(name)

    for target in (core, turret, cannon):
        apply_bevel(target)

    return {
        "recipeId": "gravitas-breaker-hardflow-pilot-v1",
        "targetMeshes": [core.name, turret.name, cannon.name],
        "booleanCuts": cuts,
        "addedDetailObjects": added,
        "addedDetailObjectCount": len(added),
        "booleanImplementation": getattr(apply_boolean, "__name__", type(apply_boolean).__name__),
        "bevelImplementation": getattr(apply_bevel, "__name__", type(apply_bevel).__name__),
    }


def apply_gravitas_breech_feed_details(
    objects: list[bpy.types.Object],
    mechanical_material,
    warning_material=None,
    apply_boolean: BooleanApply = apply_standard_boolean,
    apply_bevel: BevelApply = apply_standard_bevel,
) -> dict[str, Any]:
    """Cut paired recessed feed seats with nested service connectors.

    The recipe marker makes a completed call a no-op on subsequent calls. A
    saved, partially applied state fails closed so the Boolean cuts cannot be
    silently repeated on already modified geometry.
    """
    target = _find_mesh(objects, "GravitasBreakerGravCannon_Gravity_BreechMount")
    marker = "RCL_GravitasBreechFeedSeats_v2"
    state = target.get(marker)
    detail_names: list[str] = []
    for side, tag in ((-1.0, "L"), (1.0, "R")):
        detail_names.extend((
            f"GravitasBreakerHF_BreechFeedSeat_{tag}",
            f"GravitasBreakerHF_BreechFeedFlange_{tag}",
            f"GravitasBreakerHF_BreechFeedHub_{tag}",
            f"GravitasBreakerHF_BreechFeedPin_{tag}",
            *(f"GravitasBreakerHF_BreechFeedFastener_{tag}_{index}" for index in range(1, 5)),
        ))
    if state == "complete":
        live_names = {obj.name for obj in objects}
        missing = [name for name in detail_names
                   if bpy.data.objects.get(name) is None or name not in live_names]
        if missing:
            raise ValueError(
                f"{target.name} is marked complete but its breech-feed details are missing from the scene/object list: "
                + ", ".join(missing)
            )
        return {
            "recipeId": "gravitas-breaker-breech-feed-seats-v2",
            "targetMesh": target.name,
            "applied": False,
            "idempotentNoOp": True,
            "booleanCuts": [],
            "addedDetailObjects": [],
            "booleanImplementation": getattr(apply_boolean, "__name__", type(apply_boolean).__name__),
            "bevelImplementation": getattr(apply_bevel, "__name__", type(apply_bevel).__name__),
        }
    if state is not None:
        raise RuntimeError(
            f"{target.name} has incomplete {marker} state {state!r}; restore the authored scene before retrying"
        )
    conflicts = [name for name in detail_names if bpy.data.objects.get(name) is not None]
    if conflicts:
        raise ValueError(
            "Reserved Gravitas breech-feed detail names already exist without a completed recipe marker: "
            + ", ".join(conflicts)
        )

    for obj in bpy.context.scene.objects:
        if obj.select_get():
            obj.select_set(False)
    target[marker] = "in-progress"
    cuts: list[dict[str, Any]] = []
    added: list[str] = []
    material_for_connectors = warning_material or mechanical_material
    panel_y = 0.704
    panel_z = 1.645
    try:
        # The seats sit high on the breech's rear quarter, above the trunnion
        # flanges and outside the barrel's 0.22 m recoil corridor.
        for side, tag in ((-1.0, "L"), (1.0, "R")):
            cutter_name = f"GravitasBreakerHF_Cutter_BreechFeedSeat_{tag}"
            _cylinder_cutter(
                cutter_name,
                (side * 0.335, panel_y, panel_z),
                radius=0.055,
                depth=0.100,
            )
            cutter = bpy.data.objects[cutter_name]
            try:
                solver = apply_boolean(target, cutter)
                if not solver:
                    raise RuntimeError(f"Boolean failed for {cutter_name} on {target.name}")
            finally:
                if cutter.name in bpy.data.objects:
                    bpy.data.objects.remove(cutter, do_unlink=True)
            cuts.append({
                "target": target.name,
                "name": cutter_name,
                "solver": str(solver),
                "center": [side * 0.335, panel_y, panel_z],
                "radius": 0.055,
                "depth": 0.100,
            })

            seat_name = f"GravitasBreakerHF_BreechFeedSeat_{tag}"
            seat = _add_axis_cylinder(
                objects,
                seat_name,
                (side * 0.294, panel_y, panel_z),
                radius=0.051,
                depth=0.014,
                material=mechanical_material,
                vertices=32,
                bevel=0.0015,
            )
            added.append(seat.name)

            flange_name = f"GravitasBreakerHF_BreechFeedFlange_{tag}"
            flange = _add_axis_cylinder(
                objects,
                flange_name,
                (side * 0.307, panel_y, panel_z),
                radius=0.037,
                depth=0.020,
                material=mechanical_material,
                vertices=16,
                bevel=0.002,
            )
            added.append(flange.name)

            hub_name = f"GravitasBreakerHF_BreechFeedHub_{tag}"
            hub = _add_axis_cylinder(
                objects,
                hub_name,
                (side * 0.315, panel_y, panel_z),
                radius=0.026,
                depth=0.014,
                material=mechanical_material,
                vertices=16,
                bevel=0.0015,
            )
            added.append(hub.name)

            pin_name = f"GravitasBreakerHF_BreechFeedPin_{tag}"
            pin = _add_axis_cylinder(
                objects,
                pin_name,
                (side * 0.322, panel_y, panel_z),
                radius=0.011,
                depth=0.014,
                material=material_for_connectors,
                vertices=12,
                bevel=0.001,
            )
            added.append(pin.name)

            # Four small recessed flange locks remain within the counterbore.
            for index, angle in enumerate((45.0, 135.0, 225.0, 315.0), 1):
                radians = math.radians(angle)
                name = f"GravitasBreakerHF_BreechFeedFastener_{tag}_{index}"
                bolt = _add_axis_cylinder(
                    objects,
                    name,
                    (
                        side * 0.306,
                        panel_y + math.cos(radians) * 0.046,
                        panel_z + math.sin(radians) * 0.046,
                    ),
                    radius=0.0055,
                    depth=0.010,
                    material=mechanical_material,
                    vertices=8,
                    bevel=0.0008,
                )
                added.append(bolt.name)

        apply_bevel(target)
        target[marker] = "complete"
    except Exception:
        target[marker] = "in-progress"
        raise

    return {
        "recipeId": "gravitas-breaker-breech-feed-seats-v2",
        "targetMesh": target.name,
        "applied": True,
        "idempotentNoOp": False,
        "booleanCuts": cuts,
        "addedDetailObjects": added,
        "addedDetailObjectCount": len(added),
        "booleanImplementation": getattr(apply_boolean, "__name__", type(apply_boolean).__name__),
        "bevelImplementation": getattr(apply_bevel, "__name__", type(apply_bevel).__name__),
    }


def apply_gravitas_trunnion_retainer_details(
    objects: list[bpy.types.Object],
    mechanical_material,
    apply_boolean: BooleanApply = apply_standard_boolean,
    apply_bevel: BevelApply = apply_standard_bevel,
) -> dict[str, Any]:
    """Seat three flush lock bolts in each existing trunnion retention cap.

    The cap already exists around the pin, so this pass only adds small blind
    counterbores and inset fasteners. It checks the exposed cap face at every
    position and refuses to drill if another part or a bevel hides that seat.
    """
    caps = {
        tag: _find_mesh(
            objects,
            f"GravitasBreakerArmor_TrunnionBracket_RetentionCap_{tag}",
        )
        for tag in ("L", "R")
    }
    marker = "RCL_GravitasTrunnionRetainerSeats_v2"
    states = [cap.get(marker) for cap in caps.values()]
    bolt_names = [
        f"GravitasBreakerHF_TrunnionRetainerBolt_{tag}_{index}"
        for tag in ("L", "R")
        for index in range(1, 4)
    ]
    if all(state == "complete" for state in states):
        live_names = {obj.name for obj in objects}
        missing = [name for name in bolt_names
                   if bpy.data.objects.get(name) is None or name not in live_names]
        if missing:
            raise ValueError(
                "Trunnion retainer recipe is marked complete but its bolts are missing "
                "from the scene/object list: " + ", ".join(missing)
            )
        return {
            "recipeId": "gravitas-breaker-trunnion-retainer-seats-v2",
            "targetMeshes": [cap.name for cap in caps.values()],
            "applied": False,
            "idempotentNoOp": True,
            "booleanCuts": [],
            "addedDetailObjects": [],
        }
    if any(state is not None for state in states):
        raise RuntimeError(
            "Trunnion retainer recipe state is partial; restore the authored scene before retrying"
        )

    conflicts = [name for name in bolt_names if bpy.data.objects.get(name) is not None]
    if conflicts:
        raise ValueError(
            "Reserved Gravitas trunnion retainer detail names already exist without a completed recipe marker: "
            + ", ".join(conflicts)
        )

    for cap in caps.values():
        cap[marker] = "in-progress"
    cuts: list[dict[str, Any]] = []
    added: list[str] = []
    try:
        for tag, side in (("L", -1.0), ("R", 1.0)):
            cap = caps[tag]
            inverse = cap.matrix_world.inverted()
            # The cap's lower half is screened by the cheek skirt; put all
            # three recessed locks on the visible upper arc instead.
            for index, angle in enumerate((45.0, 90.0, 135.0), 1):
                radians = math.radians(angle)
                radial_distance = 0.045
                y = 1.020 + math.cos(radians) * radial_distance
                z = 1.520 + math.sin(radians) * radial_distance
                world_ray_origin = Vector((side * 1.0, y, z))
                world_ray_direction = Vector((-side, 0.0, 0.0))
                visible_object, visible_point = _first_visible_mesh_hit(
                    objects,
                    world_ray_origin,
                    world_ray_direction,
                    max_distance=2.0,
                )
                if visible_object != cap:
                    blocker = visible_object.name if visible_object is not None else "no mesh surface"
                    raise ValueError(
                        f"Trunnion retainer seat {tag}_{index} is occluded by {blocker}; "
                        f"the cap face must be the first scene hit"
                    )
                ray_origin = inverse @ Vector((side * 1.0, y, z))
                ray_direction = (
                    inverse.to_3x3() @ Vector((-side, 0.0, 0.0))
                ).normalized()
                hit, local_point, local_normal, _face_index = cap.ray_cast(
                    ray_origin,
                    ray_direction,
                    distance=2.0,
                )
                if not hit:
                    raise ValueError(
                        f"Trunnion retainer seat {tag}_{index} does not hit {cap.name}"
                    )
                world_point = cap.matrix_world @ local_point
                if (visible_point - world_point).length > 0.002:
                    raise ValueError(
                        f"Scene visibility hit for trunnion seat {tag}_{index} does not match {cap.name}"
                    )
                world_normal = (
                    cap.matrix_world.to_3x3().inverted().transposed() @ local_normal
                ).normalized()
                if abs(world_point.x - side * 0.3888) > 0.002 or world_normal.x * side < 0.98:
                    raise ValueError(
                        f"Trunnion retainer seat {tag}_{index} is not on the exposed outer cap face"
                    )

                cutter_name = f"GravitasBreakerHF_Cutter_TrunnionRetainer_{tag}_{index}"
                _cylinder_cutter(
                    cutter_name,
                    (world_point.x, y, z),
                    radius=0.0055,
                    depth=0.014,
                )
                cutter = bpy.data.objects[cutter_name]
                try:
                    solver = apply_boolean(cap, cutter)
                    if not solver:
                        raise RuntimeError(
                            f"Boolean failed for {cutter_name} on {cap.name}"
                        )
                finally:
                    if cutter.name in bpy.data.objects:
                        bpy.data.objects.remove(cutter, do_unlink=True)
                cuts.append({
                    "target": cap.name,
                    "name": cutter_name,
                    "solver": str(solver),
                    "center": [world_point.x, y, z],
                    "radius": 0.0055,
                    "depth": 0.014,
                })

                name = f"GravitasBreakerHF_TrunnionRetainerBolt_{tag}_{index}"
                bolt = _add_axis_cylinder(
                    objects,
                    name,
                    (side * 0.3853, y, z),
                    radius=0.0036,
                    depth=0.006,
                    material=mechanical_material,
                    vertices=8,
                    bevel=0.0003,
                )
                added.append(bolt.name)
            apply_bevel(cap)
            cap[marker] = "complete"
    except Exception:
        for cap in caps.values():
            cap[marker] = "in-progress"
        raise

    return {
        "recipeId": "gravitas-breaker-trunnion-retainer-seats-v2",
        "targetMeshes": [cap.name for cap in caps.values()],
        "applied": True,
        "idempotentNoOp": False,
        "booleanCuts": cuts,
        "addedDetailObjects": added,
        "addedDetailObjectCount": len(added),
        "booleanImplementation": getattr(apply_boolean, "__name__", type(apply_boolean).__name__),
        "bevelImplementation": getattr(apply_bevel, "__name__", type(apply_bevel).__name__),
        "existingAccessHatch": "retained; plate, hinge bar, and dog latch already exist",
    }
