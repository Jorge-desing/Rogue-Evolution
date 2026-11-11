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
    if len(target.modifiers) > 1:
        target.modifiers.move(len(target.modifiers) - 1, 0)
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
