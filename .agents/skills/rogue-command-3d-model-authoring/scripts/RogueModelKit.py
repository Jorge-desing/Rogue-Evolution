"""RogueModelKit: Reusable procedural 3D modeling toolkit for Rogue Command vehicles.

Provides parametric geometric primitives, chamfered hard-surface builders,
continuous track loops, stepped wheels, recessed optics, advanced rotary weapons,
missile pods, energy emitters, bipedal legs, draw-call batching, multi-perspective
studio rendering, and automated quality diagnostics designed for Blender 4.5.14 LTS
and Unity 2021.3.45f2 FBX export.
"""

from __future__ import annotations
import json
import math
from pathlib import Path
from typing import Any, Dict, Sequence, Tuple

import bpy
from mathutils import Vector, Euler, Quaternion


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
) -> bpy.types.Object:
    """Assigns material, enforces flat or smooth shading, and applies 1-segment bevel."""
    if material is not None:
        obj.data.materials.append(material)

    for polygon in obj.data.polygons:
        polygon.use_smooth = smooth

    if bevel > 0.0:
        modifier = obj.modifiers.new("machined chamfers", "BEVEL")
        modifier.width = bevel
        modifier.segments = 1
        modifier.limit_method = "ANGLE"
        modifier.angle_limit = math.radians(35)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        for polygon in obj.data.polygons:
            polygon.use_smooth = smooth

    obj.select_set(False)
    return obj


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
) -> bpy.types.Object:
    """Creates a box with baked transforms and 1-segment chamfer."""
    if rotation is None:
        rotation = (0.0, 0.0, 0.0)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    obj.rotation_euler = rotation
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return finish_mesh(obj, material, bevel)


def make_cylinder(
    name: str,
    radius: float,
    depth: float,
    location: Tuple[float, float, float] | Vector,
    material: bpy.types.Material | None,
    rotation: Tuple[float, float, float] = (0.0, 0.0, 0.0),
    vertices: int = 12,
    bevel: float | None = None,
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
    return finish_mesh(obj, material, bevel)


def make_rod(
    name: str,
    start: Sequence[float] | Vector,
    end: Sequence[float] | Vector,
    radius: float,
    material: bpy.types.Material | None,
    vertices: int = 8,
    bevel: float | None = None,
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
    return finish_mesh(obj, material, bevel)


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
    return finish_mesh(obj, material, 0.0)


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
) -> bpy.types.Object:
    """Creates an armored tank chassis with sloped frontal glacis and tapered sides."""
    hw_f = width_front * 0.5
    hw_r = width_rear * 0.5
    vertices = [
        # Lower chassis (ground clearance)
        (-hw_r * 0.88, -length_rear, height_lower),
        ( hw_r * 0.88, -length_rear, height_lower),
        ( hw_f * 0.85,  length_front * 0.65, height_lower),
        (-hw_f * 0.85,  length_front * 0.65, height_lower),
        # Upper deck
        (-hw_r, -length_rear * 0.95, height_upper),
        ( hw_r, -length_rear * 0.95, height_upper),
        ( hw_f,  length_front, height_upper),
        (-hw_f,  length_front, height_upper),
    ]
    faces = [
        (3, 2, 1, 0),  # Bottom
        (4, 5, 6, 7),  # Upper Deck
        (0, 4, 7, 3),  # Port / Left side
        (1, 2, 6, 5),  # Starboard / Right side
        (0, 1, 5, 4),  # Rear plate
        (3, 7, 6, 2),  # Sloped frontal glacis plate
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
    return finish_mesh(obj, material, bevel)


def make_faceted_turret_housing(
    prefix: str,
    location: Tuple[float, float, float] | Vector,
    size: Tuple[float, float, float] = (1.4, 1.6, 0.45),
    armor_mat: bpy.types.Material | None = None,
    roof_mat: bpy.types.Material | None = None,
    bevel: float = 0.035,
) -> list[bpy.types.Object]:
    """Generates an angular, multi-faceted armored turret shell with sloped cheeks and rear bustle."""
    objects = []
    loc_v = Vector(location)
    hw = size[0] * 0.5
    hl = size[1] * 0.5
    hh = size[2] * 0.5

    # Octagonal/faceted main turret body
    objects.append(make_cylinder(
        f"{prefix}_BaseShell", hw * 0.92, size[2],
        loc_v, armor_mat, vertices=8, bevel=bevel
    ))
    # Forward sloped gun mantlet cradle
    objects.append(make_box(
        f"{prefix}_MantletHousing",
        (size[0] * 0.65, size[1] * 0.35, size[2] * 0.88),
        loc_v + Vector((0.0, hl * 0.65, 0.0)),
        roof_mat if roof_mat else armor_mat, bevel=bevel * 0.8,
        rotation=(math.radians(-8), 0.0, 0.0)
    ))
    # Rear bustle counterweight / ammo compartment
    objects.append(make_box(
        f"{prefix}_RearBustle",
        (size[0] * 0.78, size[1] * 0.42, size[2] * 0.76),
        loc_v + Vector((0.0, -hl * 0.72, 0.0)),
        armor_mat, bevel=bevel
    ))
    return objects


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
) -> bpy.types.Object:
    """Creates a continuous articulated track belt with sloped front climb and rounded return idlers."""
    hw = width * 0.5
    half_l = length * 0.5
    approach_y = half_l * 0.68
    front_apex_y = half_l * 0.98
    front_apex_z = ground_z + (height - ground_z) * 0.45

    rear_apex_y = -half_l * 0.98
    rear_apex_z = ground_z + (height - ground_z) * 0.45
    rear_approach_y = -half_l * 0.72

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
) -> list[bpy.types.Object]:
    """Generates a complete parametric continuous track assembly with wheels, cleats, and fenders."""
    objects = []
    side_tag = "L" if x_pos < 0 else "R"
    sign = 1.0 if x_pos >= 0 else -1.0

    # 1. Articulated continuous track belt
    belt = make_continuous_track_belt(
        f"{prefix}_Belt_{side_tag}",
        x_pos=x_pos, length=length, height=height, width=belt_width,
        tread_mat=tread_mat, ground_z=ground_z, y_pos=center_y
    )
    objects.append(belt)

    # 2. Stepped road wheels array along ground run
    half_l = length * 0.5
    usable_l = half_l * 0.72
    step_y = (usable_l * 2.0) / (roadwheel_count - 1) if roadwheel_count > 1 else 0.0
    wheel_radius = (height - ground_z) * 0.34
    wheel_center_z = ground_z + wheel_radius + 0.02

    for idx in range(roadwheel_count):
        y = center_y - usable_l + idx * step_y
        wheel_loc = (x_pos + sign * 0.015, y, wheel_center_z)
        wheels = make_stepped_roadwheel(
            f"{prefix}_RoadWheel_{side_tag}_{idx+1}",
            location=wheel_loc, radius=wheel_radius, width=belt_width * 0.38,
            rim_mat=rim_mat, hub_mat=hub_mat, axle_mat=axle_mat, vertices=12
        )
        objects.extend(wheels)

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
    for c_idx in range(cleat_count):
        cy = cleat_start_y + c_idx * cleat_step
        objects.append(make_box(
            f"{prefix}_Cleat_{side_tag}_{c_idx+1}",
            (belt_width * 0.88, cleat_step * 0.55, 0.045),
            (x_pos, cy, height + 0.02), tread_mat, bevel=0.008
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

    if skirt_mat is not None:
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

    # 5. Hexagonal lug bolts circle (6 bolts on hub face)
    bolt_radius = radius * 0.22
    for b in range(6):
        b_angle = b * (math.pi / 3.0)
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

    # 1. Hip rotary actuator / spherical joint
    objects.append(make_cylinder(
        f"{prefix}_HipJoint_{side_name}", 0.18, 0.18,
        hip_v, joint_mat, rotation=(0.0, math.radians(90), 0.0), vertices=10, bevel=0.015
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
    """Generates an electromagnetic arc weapon with induction coils and convergent prongs."""
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

    # 2. Glowing plasma core cylinder
    core_loc = base_v + dir_vec * (core_length * 0.45)
    objects.append(make_cylinder(
        f"{prefix}_PlasmaCore",
        core_radius * 0.68, core_length,
        core_loc, arc_mat, rotation=rot_euler, vertices=12, bevel=0.01
    ))

    # 3. Toroidal induction accelerator coils (2 spaced rings)
    for idx, t in enumerate((0.25, 0.65), start=1):
        ring_loc = base_v + dir_vec * (core_length * t)
        objects.append(make_torus(
            f"{prefix}_InductionCoil_{idx}",
            ring_loc,
            major_radius=core_radius * 1.05,
            minor_radius=core_radius * 0.18,
            material=arc_mat, rotation=rot_euler,
            major_segments=12, minor_segments=6
        ))
        objects.append(make_cylinder(
            f"{prefix}_InsulatorRing_{idx}",
            core_radius * 1.18, 0.04,
            ring_loc, gunmetal_mat, rotation=rot_euler, vertices=10, bevel=0.008
        ))

    # 4. Forward convergent arc discharge prongs
    focus_point = base_v + dir_vec * (core_length + prong_length * 1.3)
    start_plane = base_v + dir_vec * (core_length * 0.85)

    for i in range(prong_count):
        theta = 2.0 * math.pi * i / prong_count
        prong_radial = (u_vec * math.cos(theta) + v_vec * math.sin(theta)) * (core_radius * 1.15)
        p_base = start_plane + prong_radial
        p_tip = p_base + dir_vec * prong_length + (focus_point - (p_base + dir_vec * prong_length)) * 0.35

        objects.append(make_rod(
            f"{prefix}_Prong_{i+1}", p_base, p_tip,
            core_radius * 0.16, gunmetal_mat, vertices=8, bevel=0.008
        ))
        objects.append(make_cylinder(
            f"{prefix}_ProngTip_{i+1}",
            core_radius * 0.18, 0.06, p_tip,
            arc_mat, rotation=rot_euler, vertices=8, bevel=0.005
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
) -> list[bpy.types.Object]:
    """Generates a parametric, multi-stage tank gun with coaxial alignment."""
    objects = []
    pivot_v = Vector(gun_pivot)
    gun_dir = Vector(direction).normalized()
    gun_rot = gun_dir.to_track_quat("Z", "Y").to_euler()

    # 1. Heavy breech collar
    breech_loc = pivot_v + gun_dir * 0.22
    objects.append(make_cylinder(
        f"{prefix}_BreechCollar", caliber_radius * 1.85, 0.42,
        breech_loc, barrel_mat, rotation=gun_rot, vertices=10, bevel=0.02
    ))

    # 2. Thermal sleeve / heat shroud
    if has_thermal_sleeve:
        sleeve_loc = pivot_v + gun_dir * 0.65
        objects.append(make_cylinder(
            f"{prefix}_ThermalSleeve", caliber_radius * 1.38, 0.52,
            sleeve_loc, armor_mat, rotation=gun_rot, vertices=8, bevel=0.02
        ))

    # 3. Bore evacuator / fume extractor cylinder
    if has_bore_evacuator:
        evac_loc = pivot_v + gun_dir * (barrel_length * 0.62)
        objects.append(make_cylinder(
            f"{prefix}_BoreEvacuator", caliber_radius * 1.55, 0.34,
            evac_loc, armor_mat, rotation=gun_rot, vertices=10, bevel=0.015
        ))
        if accent_mat is not None:
            objects.append(make_cylinder(
                f"{prefix}_EvacuatorStripe", caliber_radius * 1.58, 0.06,
                evac_loc, accent_mat, rotation=gun_rot, vertices=10
            ))

    # 4. Main rifled barrel
    barrel_center = pivot_v + gun_dir * (barrel_length * 0.5 + 0.3)
    objects.append(make_cylinder(
        f"{prefix}_MainBarrel", caliber_radius, barrel_length,
        barrel_center, barrel_mat, rotation=gun_rot, vertices=12, bevel=0.01
    ))

    # 5. Muzzle brake & bore opening
    if has_muzzle_brake:
        brake_loc = pivot_v + gun_dir * (barrel_length + 0.22)
        objects.append(make_box(
            f"{prefix}_MuzzleBrake",
            (caliber_radius * 2.5, caliber_radius * 3.2, caliber_radius * 2.3),
            brake_loc, armor_mat, bevel=0.018, rotation=gun_rot
        ))
        bore_loc = pivot_v + gun_dir * (barrel_length + 0.36)
        objects.append(make_cylinder(
            f"{prefix}_MuzzleBore", caliber_radius * 0.68, 0.10,
            bore_loc, barrel_mat, rotation=gun_rot, vertices=10
        ))

    # 6. Twin hydraulic recoil recuperators
    if has_recoil_cylinders:
        recoil_offset_x = caliber_radius * 2.3
        u_vec = gun_dir.cross(Vector((0.0, 0.0, 1.0))).normalized()
        for side in (-1, 1):
            side_tag = "L" if side < 0 else "R"
            recoil_loc = pivot_v + gun_dir * 0.28 + u_vec * (side * recoil_offset_x)
            objects.append(make_cylinder(
                f"{prefix}_RecoilCylinder_{side_tag}",
                caliber_radius * 0.52, 0.50, recoil_loc, barrel_mat,
                rotation=gun_rot, vertices=8, bevel=0.008
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
    objects.append(make_rod(
        f"{prefix}_TibiaStrut", knee_v, foot_target + Vector((0.0, 0.0, 0.10)),
        0.075, joint_mat, vertices=8
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
    antenna_parts: Sequence[bpy.types.Object] | None = None,
) -> list[bpy.types.Object]:
    """Generates standard Unity empty sockets matching Rogue Command vehicle & weapon mechanics:

    1. Socket_MuzzlePrimary: Main weapon firing point and projectile exit vector.
    2. Socket_MuzzleSecondary: Secondary weapon (coax/pintle/missile) firing point.
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
    """
    empties = []
    def _add_empty(socket_name: str, loc: Vector, rot: Tuple[float, float, float] = (0.0, 0.0, 0.0)):
        bpy.ops.object.empty_add(type="PLAIN_AXES", radius=0.15, location=loc)
        e = bpy.context.object
        e.name = socket_name
        e.rotation_euler = rot
        empties.append(e)

    # 1. Primary Muzzle
    if muzzle_loc is not None:
        m_loc = Vector(muzzle_loc)
        m_dir = Vector(muzzle_dir if muzzle_dir is not None else (0.0, 1.0, 0.0)).normalized()
        rot = m_dir.to_track_quat("Z", "Y").to_euler()
        _add_empty("Socket_MuzzlePrimary", m_loc, rot)

    # 2. Secondary Muzzle
    if muzzle_secondary_loc is not None:
        m2_loc = Vector(muzzle_secondary_loc)
        m2_dir = Vector(muzzle_secondary_dir if muzzle_secondary_dir is not None else (0.0, 1.0, 0.0)).normalized()
        rot2 = m2_dir.to_track_quat("Z", "Y").to_euler()
        _add_empty("Socket_MuzzleSecondary", m2_loc, rot2)

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

    return empties


# ---------------------------------------------------------------------------
# 7. Optimization, Batching & Mesh Quality
# ---------------------------------------------------------------------------

def batch_and_join_by_material(
    objects: Sequence[bpy.types.Object],
    prefix: str = "Unit",
    preserve_names: Sequence[str] | None = None,
    smooth_by_angle: bool = True,
    smooth_angle_deg: float = 38.0,
) -> list[bpy.types.Object]:
    """Combines modular kitbashed objects by material into unified meshes.
    
    Reduces the number of Blender mesh objects/material groups exported. Final
    Unity draw-call counts depend on the selected prefab source, passes, and settings.
    Objects whose names match or contain strings in `preserve_names` remain separate.
    When `smooth_by_angle` is True, applies Blender 4.5 angle-weighted auto-smoothing,
    giving perfectly rounded cylinders/wheels/barrels while preserving crisp chamfers.
    """
    material_groups: Dict[bpy.types.Material | None, list[bpy.types.Object]] = {}
    preserved_objects: list[bpy.types.Object] = []
    preserve_patterns = list(preserve_names) if preserve_names else []

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
            joined_obj.name = f"{prefix}_{mat_name}"

            # Clean up mesh normals
            bpy.ops.object.mode_set(mode="EDIT")
            bpy.ops.mesh.select_all(action="SELECT")
            bpy.ops.mesh.normals_make_consistent(inside=False)
            bpy.ops.object.mode_set(mode="OBJECT")
            joined_objects.append(joined_obj)

        if smooth_by_angle and hasattr(bpy.ops.object, "shade_smooth_by_angle"):
            bpy.ops.object.select_all(action="DESELECT")
            joined_obj.select_set(True)
            bpy.context.view_layer.objects.active = joined_obj
            bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth_angle_deg))
        else:
            for poly in joined_obj.data.polygons:
                poly.use_smooth = False

    # Also smooth preserved objects if requested
    if smooth_by_angle and hasattr(bpy.ops.object, "shade_smooth_by_angle"):
        for p_obj in preserved_objects:
            if p_obj.type == "MESH":
                bpy.ops.object.select_all(action="DESELECT")
                p_obj.select_set(True)
                bpy.context.view_layer.objects.active = p_obj
                bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth_angle_deg))

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
    team_mask_materials: Sequence[bpy.types.Material] | None = None,
    exhaust_locs: Sequence[Vector | Tuple[float, float, float]] | None = None,
    muzzle_loc: Vector | Tuple[float, float, float] | None = None,
) -> None:
    """Calculates multi-channel procedural vertex shading:

    Channel R: Ambient Occlusion / Cavity Dirt (contact shadows in wheel wells, louvers, undersides).
    Channel G: Curvature / Edge Wear (bright glints on sharp chamfers simulating chipped paint).
    Channel B: Thermal Soot / Weathering (darkening gradient around exhausts and muzzles).
    Channel A: Team Color Mask (1.0 for tintable primary armor, 0.0 for fixed gunmetal/rubber).
    """
    wear_mult = 1.4 if wear_level == "battle_hardened" else (0.8 if wear_level == "field_tested" else 0.1)

    team_mat_names = set()
    if team_mask_materials:
        team_mat_names = {m.name for m in team_mask_materials if m}

    ex_v = [Vector(e) for e in (exhaust_locs or [])]
    muz_v = Vector(muzzle_loc) if muzzle_loc is not None else None

    for obj in objects:
        if obj.type != "MESH" or not obj.data.polygons:
            continue

        mesh = obj.data
        color_attr = mesh.color_attributes.get(attribute_name)
        if color_attr is None:
            color_attr = mesh.color_attributes.new(name=attribute_name, type="BYTE_COLOR", domain="CORNER")
        mesh.color_attributes.active_color = color_attr

        # Determine team color mask from material
        mat = obj.data.materials[0] if obj.data.materials else None
        if team_mat_names and mat:
            is_team_surface = mat.name in team_mat_names
        else:
            is_team_surface = mat and any(a in mat.name for a in ("PrimaryArmor", "LightArmor"))
        team_alpha = 1.0 if is_team_surface else 0.0

        world_mat = obj.matrix_world
        for poly in mesh.polygons:
            # R: Cavity / AO
            norm_w = (world_mat.to_3x3() @ poly.normal).normalized()
            is_belly = norm_w.z < -0.1
            cavity_val = 0.35 if is_belly else (0.88 if norm_w.z > 0.4 else 0.65)

            # G: Edge Wear (steep angled bevels catch more wear)
            bevel_factor = 0.55 if (0.15 < abs(norm_w.z) < 0.85) else 0.15
            edge_val = min(1.0, bevel_factor * wear_mult)

            # B: Thermal Soot
            soot_val = 1.0
            if ex_v or muz_v:
                poly_center = world_mat @ poly.center
                min_dist = float("inf")
                for e_pos in ex_v:
                    min_dist = min(min_dist, (poly_center - e_pos).length)
                if muz_v:
                    min_dist = min(min_dist, (poly_center - muz_v).length)
                if min_dist < 0.65:
                    soot_val = max(0.25, min_dist / 0.65)

            for loop_idx in poly.loop_indices:
                color_attr.data[loop_idx].color = (cavity_val, edge_val, soot_val, team_alpha)


def generate_lod_chain(
    batched_objects: Sequence[bpy.types.Object],
    prefix: str = "Unit",
    lod_levels: Sequence[Tuple[str, float]] = (("LOD0", 1.0), ("LOD1", 0.50), ("LOD2", 0.22)),
    smooth_by_angle: bool = True,
    smooth_angle_deg: float = 38.0,
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
    target_budget: int = 4000,
    lod_dict: Dict[str, list[bpy.types.Object]] | None = None,
) -> Dict[str, Any]:
    """Provides granular polygon and draw-call diagnostics categorized by mechanical subsystem."""
    subsystems = {
        "Chassis & Hull": 0,
        "Turret & Cabin": 0,
        "Mobility & Wheels": 0,
        "Weapons": 0,
        "Optics & Greebles": 0,
    }
    total_verts = 0
    total_tris = 0
    materials_used = set()

    for obj in objects:
        if obj.type != "MESH":
            continue
        tris = len(obj.data.polygons)
        verts = len(obj.data.vertices)
        total_tris += tris
        total_verts += verts
        for m in obj.data.materials:
            if m:
                materials_used.add(m.name)

        name = obj.name
        if prefix and name.startswith(prefix):
            name = name[len(prefix):]

        if any(k in name for k in ("Track", "Wheel", "Tire", "Leg", "Skimmer", "Thruster", "TiltRotor", "Outrigger")):
            subsystems["Mobility & Wheels"] += tris
        elif any(k in name for k in ("Cannon", "Barrel", "Howitzer", "Gatling", "Missile", "Gun", "Emitter", "Muzzle", "Railgun", "Flak", "Plasma", "Mortar")):
            subsystems["Weapons"] += tris
        elif any(k in name for k in ("Turret", "Troop", "RollCage", "Cab", "FlightDeck", "Bridge")):
            subsystems["Turret & Cabin"] += tris
        elif any(k in name for k in ("Sensor", "Visor", "Antenna", "Radar", "Smoke", "Sponson", "Applique", "Grille", "Bolt", "Excavator", "Drum")):
            subsystems["Optics & Greebles"] += tris
        else:
            subsystems["Chassis & Hull"] += tris

    pct_of_budget = round((total_tris / max(1, target_budget)) * 100, 1)

    lod_summary = {}
    if lod_dict:
        for lod_name, lod_objs in lod_dict.items():
            l_tris = sum(len(o.data.polygons) for o in lod_objs if o.type == "MESH")
            lod_summary[lod_name] = l_tris

    report = {
        "prefix": prefix,
        "total_triangles": total_tris,
        "total_vertices": total_verts,
        "materials_count": len(materials_used),
        "predicted_draw_calls": len(materials_used),
        "target_budget": target_budget,
        "budget_percentage": pct_of_budget,
        "subsystems": subsystems,
        "lod_levels": lod_summary,
    }

    print(f"\n================ [ {prefix} Subsystem Budget & Profiling ] ================")
    print(f"Total Triangles: {total_tris} | Vertices: {total_verts} | Materials: {len(materials_used)}")
    print(f"Budget Health: {total_tris}/{target_budget} tris ({pct_of_budget}%)")
    print("Subsystem Breakdown:")
    for sub, count in subsystems.items():
        sub_pct = round((count / max(1, total_tris)) * 100, 1)
        print(f"  - {sub:20s}: {count:5d} tris ({sub_pct:4.1f}%)")
    if lod_summary:
        print("LOD Chain Hierarchy:")
        for lod_name, l_tris in lod_summary.items():
            print(f"  - {lod_name}: {l_tris} tris")
    print("========================================================================\n")
    return report


def merge_meshes_by_material(
    objects: Sequence[bpy.types.Object],
    prefix: str,
    material_map: Dict[bpy.types.Material, str] | None = None,
) -> list[bpy.types.Object]:
    """Merges meshes sharing the same PBR material and names them <prefix>_Batch_<materialKey>."""
    material_groups: Dict[bpy.types.Material | None, list[bpy.types.Object]] = {}

    for obj in objects:
        if obj.type != "MESH":
            continue
        mat = obj.data.materials[0] if obj.data.materials else None
        material_groups.setdefault(mat, []).append(obj)

    merged_objects: list[bpy.types.Object] = []

    for mat, group in material_groups.items():
        if not group:
            continue
        key = material_map.get(mat, mat.name.replace(" ", "_") if mat else "None") if material_map else (mat.name.replace(" ", "_") if mat else "None")
        batch_name = f"{prefix}_Batch_{key}"

        if len(group) == 1:
            group[0].name = batch_name
            merged_objects.append(group[0])
            continue

        bpy.ops.object.select_all(action="DESELECT")
        for obj in group:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = group[0]

        bpy.ops.object.join()
        joined_obj = bpy.context.view_layer.objects.active
        joined_obj.name = batch_name

        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.normals_make_consistent(inside=False)
        bpy.ops.object.mode_set(mode="OBJECT")

        for poly in joined_obj.data.polygons:
            poly.use_smooth = False

        merged_objects.append(joined_obj)

    return merged_objects



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


def setup_preview_studio(
    model_objects: Sequence[bpy.types.Object],
    preview_path: Path,
    ortho_scale: float = 5.8,
) -> None:
    """Configures 3-point studio lighting, floor shadow plane, and AgX isometric RTS camera."""
    preview_path.parent.mkdir(parents=True, exist_ok=True)

    # Floor shadow plane
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0.0, 0.0, -0.015))
    floor = bpy.context.object
    floor.name = "PreviewFloor"
    floor_mat = make_material("PreviewFloorMat", (0.035, 0.052, 0.065), 0.0, 0.92)
    finish_mesh(floor, floor_mat)

    scene = bpy.context.scene
    scene.render.resolution_x = 900
    scene.render.resolution_y = 760
    scene.render.resolution_percentage = 100

    # Camera: Standard RTS orthographic view
    bpy.ops.object.camera_add(location=(4.8, 5.8, 3.6))
    camera = bpy.context.object
    camera.name = "PreviewCamera"
    target = Vector((0.0, 0.0, 0.72))
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = ortho_scale
    frame_camera_to_model(camera, model_objects, target)
    bpy.context.scene.camera = camera

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
    scene.render.filepath = str(preview_path)
    scene.world.color = (0.035, 0.045, 0.065)


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
    scene = bpy.context.scene
    scene.render.resolution_x = 900
    scene.render.resolution_y = 760
    scene.render.resolution_percentage = 100

    # 1. Floor shadow plane
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0.0, 0.0, -0.015))
    floor = bpy.context.object
    floor.name = "PreviewFloor"
    floor_mat = make_material("PreviewFloorMat", (0.035, 0.052, 0.065), 0.0, 0.92)
    finish_mesh(floor, floor_mat)

    # 2. 3-Point Area Lights
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

    # 3. View Cameras
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

    if "ISOMETRIC" in cameras:
        scene.camera = cameras["ISOMETRIC"]
    elif cameras:
        scene.camera = next(iter(cameras.values()))

    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 900
    scene.render.resolution_y = 760
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "AgX"
    scene.world.color = (0.035, 0.045, 0.065)

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
    material_keys: dict[bpy.types.Material, str],
) -> list[bpy.types.Object]:
    """Create export-only mesh batches per material while preserving the editable source objects."""
    batches: dict[str, tuple[bpy.types.Material, list[bpy.types.Object]]] = {}
    for source in model_objects:
        if source.type != "MESH":
            continue
        if len(source.data.materials) != 1 or source.data.materials[0] is None:
            raise ValueError(f"{source.name} must have exactly one material before FBX batching")

        material = source.data.materials[0]
        material_key = material_keys.get(material)
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
        mesh_smooth_type="OFF",
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
        "sockets": socket_dict,
    }

    output_path.write_text(json.dumps(meta, indent=2), encoding="utf-8")
    return meta


def make_wreckage_variant(
    source_objects: Sequence[bpy.types.Object],
    prefix: str,
    output_fbx_path: Path,
    output_blend_path: Path | None = None,
    turret_keywords: Sequence[str] = ("Turret", "Cannon", "Barrel", "Howitzer", "Gatling", "Missile", "Gun", "Visor"),
    smooth_by_angle: bool = True,
    smooth_angle_deg: float = 38.0,
) -> list[bpy.types.Object]:
    """Generates a battle-destroyed / burned wreckage model variant for RTS destruction feedback."""
    charred_armor = make_material(f"{prefix}_Wreckage_CharredArmor", (0.08, 0.08, 0.09), metallic=0.05, roughness=0.88)
    burned_metal = make_material(f"{prefix}_Wreckage_BurnedMetal", (0.16, 0.12, 0.10), metallic=0.35, roughness=0.78)

    wreck_objs = []
    turret_objs = []

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
            wreck_objs.append(dup)

    for t_obj in turret_objs:
        t_obj.rotation_euler = Vector((math.radians(16.0), math.radians(-14.0), math.radians(22.0)))
        t_obj.location += Vector((0.18, -0.25, 0.08))
        wreck_objs.append(t_obj)

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
    """Generates a heavy siege mortar / howitzer with reinforced breech, recoil cylinders, and sector elevation plates."""
    objects = []
    p_vec = Vector(gun_pivot)
    d_vec = Vector(direction).normalized()
    u_vec, v_vec, _ = get_orthonormal_basis(d_vec)
    rot_euler = d_vec.to_track_quat("Z", "Y").to_euler()

    # 1. Heavy cylindrical breech housing collar
    breech_loc = p_vec + d_vec * 0.14
    objects.append(make_cylinder(
        f"{prefix}_BreechCollar",
        radius=caliber_radius * 2.35, depth=0.42,
        location=breech_loc, material=armor_mat,
        rotation=rot_euler, vertices=12, bevel=0.035
    ))

    # 2. Main heavy-caliber barrel tube
    barrel_center = p_vec + d_vec * (barrel_length * 0.5 + 0.22)
    objects.append(make_cylinder(
        f"{prefix}_BarrelTube",
        radius=caliber_radius * 1.55, depth=barrel_length,
        location=barrel_center, material=barrel_mat,
        rotation=rot_euler, vertices=14, bevel=0.02
    ))

    # 3. Massive muzzle blast ring / collar
    muzzle_loc = p_vec + d_vec * (barrel_length + 0.22)
    objects.append(make_cylinder(
        f"{prefix}_MuzzleCollar",
        radius=caliber_radius * 1.95, depth=0.18,
        location=muzzle_loc, material=armor_mat,
        rotation=rot_euler, vertices=14, bevel=0.025
    ))
    if accent_mat is not None:
        objects.append(make_cylinder(
            f"{prefix}_MuzzleWarningBand",
            radius=caliber_radius * 1.98, depth=0.045,
            location=muzzle_loc - d_vec * 0.05, material=accent_mat,
            rotation=rot_euler, vertices=14, bevel=0.008
        ))

    # 4. Massive hollow bore opening (dark cavity)
    objects.append(make_cylinder(
        f"{prefix}_BoreCavity",
        radius=caliber_radius * 1.05, depth=0.12,
        location=muzzle_loc + d_vec * 0.05, material=barrel_mat,
        rotation=rot_euler, vertices=12, bevel=0.005
    ))

    # 5. Dual hydraulic recoil recuperator cylinders flanking the breech
    recoil_spacing = caliber_radius * 2.6
    for side in (-1, 1):
        cyl_center = p_vec + u_vec * (side * recoil_spacing) + d_vec * 0.28
        objects.append(make_cylinder(
            f"{prefix}_RecoilCylinder_{'L' if side < 0 else 'R'}",
            radius=caliber_radius * 0.55, depth=0.55,
            location=cyl_center, material=barrel_mat,
            rotation=rot_euler, vertices=10, bevel=0.015
        ))
        objects.append(make_cylinder(
            f"{prefix}_RecoilPiston_{'L' if side < 0 else 'R'}",
            radius=caliber_radius * 0.35, depth=0.35,
            location=cyl_center + d_vec * 0.25, material=armor_mat,
            rotation=rot_euler, vertices=8, bevel=0.01
        ))

    # 6. Sector elevation quadrant plates
    for side in (-1, 1):
        quad_loc = p_vec + u_vec * (side * (recoil_spacing + 0.12)) - v_vec * 0.12
        objects.append(make_box(
            f"{prefix}_ElevationQuadrant_{'L' if side < 0 else 'R'}",
            (0.04, 0.38, 0.24), quad_loc, barrel_mat, bevel=0.01,
            rotation=rot_euler
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
) -> list[bpy.types.Object]:
    """Generates a twin-cannon mantlet assembly with dual synchronized barrels, thermal sleeves, and muzzle brakes."""
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
        p_vec + d_vec * 0.15, armor_mat, bevel=0.035,
        rotation=rot_euler
    ))

    # 2. Dual gun barrels
    for side in (-1, 1):
        side_tag = "L" if side < 0 else "R"
        barrel_pivot = p_vec + u_vec * (side * barrel_spacing * 0.5)
        # Breech collar
        objects.append(make_cylinder(
            f"{prefix}_Breech_{side_tag}",
            radius=caliber_radius * 1.85, depth=0.38,
            location=barrel_pivot + d_vec * 0.32, material=armor_mat,
            rotation=rot_euler, vertices=12, bevel=0.02
        ))
        # Thermal sleeve
        objects.append(make_cylinder(
            f"{prefix}_ThermalSleeve_{side_tag}",
            radius=caliber_radius * 1.45, depth=barrel_length * 0.42,
            location=barrel_pivot + d_vec * (barrel_length * 0.28 + 0.38), material=armor_mat,
            rotation=rot_euler, vertices=12, bevel=0.015
        ))
        # Bore evacuator
        evac_loc = barrel_pivot + d_vec * (barrel_length * 0.58 + 0.38)
        objects.append(make_cylinder(
            f"{prefix}_BoreEvacuator_{side_tag}",
            radius=caliber_radius * 1.62, depth=0.22,
            location=evac_loc, material=armor_mat,
            rotation=rot_euler, vertices=12, bevel=0.015
        ))
        if accent_mat is not None:
            objects.append(make_cylinder(
                f"{prefix}_EvacuatorStripe_{side_tag}",
                radius=caliber_radius * 1.65, depth=0.045,
                location=evac_loc, material=accent_mat,
                rotation=rot_euler, vertices=12, bevel=0.006
            ))
        # Main barrel tube
        objects.append(make_cylinder(
            f"{prefix}_BarrelTube_{side_tag}",
            radius=caliber_radius, depth=barrel_length,
            location=barrel_pivot + d_vec * (barrel_length * 0.5 + 0.38), material=barrel_mat,
            rotation=rot_euler, vertices=12, bevel=0.01
        ))
        # Muzzle brake
        muzzle_loc = barrel_pivot + d_vec * (barrel_length + 0.38)
        objects.append(make_box(
            f"{prefix}_MuzzleBrake_{side_tag}",
            (caliber_radius * 2.8, caliber_radius * 2.8, caliber_radius * 2.2),
            muzzle_loc, barrel_mat, bevel=0.015,
            rotation=rot_euler
        ))
        objects.append(make_cylinder(
            f"{prefix}_MuzzleBore_{side_tag}",
            radius=caliber_radius * 0.85, depth=0.08,
            location=muzzle_loc + d_vec * (caliber_radius * 1.2), material=barrel_mat,
            rotation=rot_euler, vertices=10, bevel=0.005
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
) -> list[bpy.types.Object]:
    """Generates an independent continuous track pod for 4-track super-heavy crawlers."""
    objects = []
    loc_v = Vector(location)

    # 1. Continuous track belt
    belt = make_continuous_track_belt(
        f"{prefix}_Belt",
        x_pos=loc_v.x, length=length, height=height, width=belt_width,
        tread_mat=tread_mat, ground_z=ground_z, y_pos=loc_v.y
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

    # 1. Main cabin box with sloped chamfers
    objects.append(make_box(
        f"{prefix}_CabinShell",
        size, loc_v, armor_mat, bevel=0.04
    ))

    # 2. Roof observation cupola / hatch
    cupola_z = loc_v.z + sz * 0.5 + 0.05
    objects.append(make_cylinder(
        f"{prefix}_RoofCupola",
        radius=sx * 0.22, depth=0.10,
        location=(loc_v.x, loc_v.y + sy * 0.15, cupola_z),
        material=detail_mat, vertices=12, bevel=0.015
    ))
    objects.append(make_cylinder(
        f"{prefix}_HatchCover",
        radius=sx * 0.18, depth=0.035,
        location=(loc_v.x, loc_v.y + sy * 0.15, cupola_z + 0.06),
        material=door_mat, vertices=10, bevel=0.008
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
            door_mat, bevel=0.012
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
    """Generates a heavy plasma siege mortar with magnetic accelerator rings and reinforced hydraulic trunnions."""
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

    # 2. Main short mortar barrel
    barrel_loc = p_vec + d_vec * (barrel_length * 0.5 + 0.22)
    objects.append(make_cylinder(
        f"{prefix}_MortarBarrel",
        radius=caliber_radius * 1.35, depth=barrel_length,
        location=barrel_loc, material=barrel_mat,
        rotation=rot_euler, vertices=14, bevel=0.02
    ))
    # Bore hole
    bore_loc = p_vec + d_vec * (barrel_length + 0.24)
    objects.append(make_cylinder(
        f"{prefix}_MortarBoreHole",
        radius=caliber_radius, depth=0.12,
        location=bore_loc, material=barrel_mat,
        rotation=rot_euler, vertices=14
    ))

    # 3. Magnetic accelerator toroid rings & glowing coils
    step = (barrel_length * 0.65) / max(1, ring_count)
    for i in range(ring_count):
        r_dist = 0.35 + i * step
        r_loc = p_vec + d_vec * r_dist
        # Heavy outer ring
        objects.append(make_torus(
            f"{prefix}_MagRing_{i+1}",
            major_radius=caliber_radius * 1.55,
            minor_radius=0.045,
            location=r_loc, material=armor_mat,
            rotation=rot_euler, major_segments=14, minor_segments=8
        ))
        # Glowing inner coil slit
        if glow_mat is not None:
            objects.append(make_cylinder(
                f"{prefix}_PlasmaCoil_{i+1}",
                radius=caliber_radius * 1.42, depth=0.04,
                location=r_loc, material=glow_mat,
                rotation=rot_euler, vertices=12
            ))

    # 4. Flare muzzle bell
    muzzle_loc = p_vec + d_vec * (barrel_length + 0.18)
    objects.append(make_cylinder(
        f"{prefix}_MuzzleBell",
        radius=caliber_radius * 1.72, depth=0.16,
        location=muzzle_loc, material=armor_mat,
        rotation=rot_euler, vertices=14, bevel=0.02
    ))
    if accent_mat is not None:
        objects.append(make_cylinder(
            f"{prefix}_MuzzleAccent",
            radius=caliber_radius * 1.75, depth=0.04,
            location=muzzle_loc - d_vec * 0.04, material=accent_mat,
            rotation=rot_euler, vertices=14
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



# ---------------------------------------------------------------------------
# Procedural Armature Rigging & Animation Actions
# ---------------------------------------------------------------------------

def generate_procedural_armature(
    objects: Sequence[bpy.types.Object],
    prefix: str,
    turret_center: Vector | Tuple[float, float, float] | None = None,
    gun_pivot: Vector | Tuple[float, float, float] | None = None,
    hull_deck_z: float = 0.88,
) -> bpy.types.Object:
    """Creates a kinematic vehicle armature and binds mesh components to rigid bones."""
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

    # 2. Hull bone (main vehicle body & suspension)
    b_hull = eb.new("Bone_Hull")
    b_hull.head = (0.0, 0.0, 0.22)
    b_hull.tail = (0.0, 0.0, max(0.5, hull_deck_z * 0.85))
    b_hull.parent = b_root

    # 3. Turret Yaw bone (horizontal azimuth traverse)
    b_turret = eb.new("Bone_TurretYaw")
    b_turret.head = (tc.x, tc.y, tc.z)
    b_turret.tail = (tc.x, tc.y, tc.z + 0.32)
    b_turret.parent = b_hull

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
    for obj in objects:
        if obj.type != "MESH" or obj.name.endswith(("_COLLIDER", "_ShieldBubble")):
            continue

        clean_name = obj.name
        if clean_name.startswith(prefix):
            clean_name = clean_name[len(prefix):]

        if any(k in clean_name for k in ("Barrel", "Muzzle", "Bore", "Recoil")):
            target_bone = "Bone_BarrelRecoil"
        elif any(k in clean_name for k in ("Mantlet", "Cradle", "Howitzer", "Rail", "Flak", "Mortar", "Gatling", "Pod", "Emitter")):
            target_bone = "Bone_TurretPitch"
        elif any(k in clean_name for k in ("Turret", "Bustle", "Visor", "Radar", "Rotodome")):
            target_bone = "Bone_TurretYaw"
        else:
            target_bone = "Bone_Hull"

        vg = obj.vertex_groups.get(target_bone) or obj.vertex_groups.new(name=target_bone)
        all_v_indices = list(range(len(obj.data.vertices)))
        if all_v_indices:
            vg.add(all_v_indices, 1.0, "REPLACE")

        arm_mod = None
        for m in obj.modifiers:
            if m.type == "ARMATURE":
                arm_mod = m
                break
        if not arm_mod:
            arm_mod = obj.modifiers.new(name="Armature", type="ARMATURE")
            arm_mod.object = armature_obj

    return armature_obj


def generate_procedural_action_clips(
    armature_obj: bpy.types.Object,
    prefix: str,
) -> dict[str, bpy.types.Action]:
    """Generates baked NLA animation clips (Idle, Drive, FireRecoil, Death) on the vehicle armature."""
    if not armature_obj.animation_data:
        armature_obj.animation_data_create()

    actions = {}

    def set_bone_key(bone_name, data_path, frame, value):
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

    # 1. Action_Idle (Engine vibration: 30 frames)
    act_idle = bpy.data.actions.new(name=f"{prefix}_Action_Idle")
    armature_obj.animation_data.action = act_idle
    for f in (1, 30):
        set_bone_key("Bone_Hull", "location", f, (0.0, 0.0, 0.0))
        set_bone_key("Bone_Hull", "rotation_euler", f, (0.0, 0.0, 0.0))
    set_bone_key("Bone_Hull", "location", 15, (0.0, 0.0, -0.006))
    set_bone_key("Bone_Hull", "rotation_euler", 15, (math.radians(0.35), 0.0, 0.0))
    act_idle.use_fake_user = True
    actions["Idle"] = act_idle

    # 2. Action_Drive (Terrain traversal / chassis suspension rocking: 40 frames)
    act_drive = bpy.data.actions.new(name=f"{prefix}_Action_Drive")
    armature_obj.animation_data.action = act_drive
    for f in (1, 40):
        set_bone_key("Bone_Hull", "location", f, (0.0, 0.0, 0.0))
        set_bone_key("Bone_Hull", "rotation_euler", f, (0.0, 0.0, 0.0))
    set_bone_key("Bone_Hull", "location", 10, (0.0, 0.0, 0.012))
    set_bone_key("Bone_Hull", "rotation_euler", 10, (math.radians(-0.8), math.radians(0.6), 0.0))
    set_bone_key("Bone_Hull", "location", 20, (0.0, 0.0, -0.010))
    set_bone_key("Bone_Hull", "rotation_euler", 20, (math.radians(0.6), math.radians(-0.5), 0.0))
    set_bone_key("Bone_Hull", "location", 30, (0.0, 0.0, 0.008))
    set_bone_key("Bone_Hull", "rotation_euler", 30, (math.radians(-0.5), math.radians(0.4), 0.0))
    act_drive.use_fake_user = True
    actions["Drive"] = act_drive

    # 3. Action_FireRecoil (Sharp ballistic backward impulse & recovery: 20 frames)
    act_fire = bpy.data.actions.new(name=f"{prefix}_Action_FireRecoil")
    armature_obj.animation_data.action = act_fire
    for f in (1, 20):
        set_bone_key("Bone_BarrelRecoil", "location", f, (0.0, 0.0, 0.0))
        set_bone_key("Bone_Hull", "rotation_euler", f, (0.0, 0.0, 0.0))
    set_bone_key("Bone_BarrelRecoil", "location", 2, (0.0, -0.22, 0.0))
    set_bone_key("Bone_Hull", "rotation_euler", 2, (math.radians(-1.2), 0.0, 0.0))
    set_bone_key("Bone_BarrelRecoil", "location", 6, (0.0, -0.06, 0.0))
    set_bone_key("Bone_Hull", "rotation_euler", 6, (math.radians(0.4), 0.0, 0.0))
    set_bone_key("Bone_BarrelRecoil", "location", 14, (0.0, 0.0, 0.0))
    set_bone_key("Bone_Hull", "rotation_euler", 14, (0.0, 0.0, 0.0))
    act_fire.use_fake_user = True
    actions["FireRecoil"] = act_fire

    # 4. Action_Death (Chassis collapse & barrel droop: 30 frames)
    act_death = bpy.data.actions.new(name=f"{prefix}_Action_Death")
    armature_obj.animation_data.action = act_death
    set_bone_key("Bone_Hull", "location", 1, (0.0, 0.0, 0.0))
    set_bone_key("Bone_Hull", "rotation_euler", 1, (0.0, 0.0, 0.0))
    set_bone_key("Bone_TurretPitch", "rotation_euler", 1, (0.0, 0.0, 0.0))
    for f in (25, 30):
        set_bone_key("Bone_Hull", "location", f, (0.0, 0.0, -0.12))
        set_bone_key("Bone_Hull", "rotation_euler", f, (math.radians(4.5), math.radians(-6.5), math.radians(2.0)))
        set_bone_key("Bone_TurretPitch", "rotation_euler", f, (math.radians(-22.0), 0.0, 0.0))
    act_death.use_fake_user = True
    actions["Death"] = act_death

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
    island_margin: float = 0.02,
) -> None:
    """Generates a unified, non-overlapping UV layout across all mesh objects into 'UVMap_Atlas'."""
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

    img_albedo = bpy.data.images.new(f"{prefix}_Albedo", resolution, resolution, alpha=True)
    img_emission = bpy.data.images.new(f"{prefix}_Emission", resolution, resolution, alpha=False)
    img_normal = bpy.data.images.new(f"{prefix}_Normal", resolution, resolution, alpha=False)
    img_metallic = bpy.data.images.new(f"{prefix}_MetallicGloss", resolution, resolution, alpha=True)

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

    # 1. Bake Diffuse Color
    set_bake_target(img_albedo)
    try:
        bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"})
    except Exception as e:
        print(f"Warning: Diffuse bake: {e}")
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
    img_roughness = bpy.data.images.new(f"{prefix}_RoughnessTemp", resolution, resolution, alpha=False)
    set_bake_target(img_roughness)
    try:
        bpy.ops.object.bake(type="ROUGHNESS")
    except Exception as e:
        print(f"Warning: Roughness bake: {e}")

    # 5. Bake Metallic (Grayscale via temporary Emission shader reroute)
    img_metallic_temp = bpy.data.images.new(f"{prefix}_MetallicTemp", resolution, resolution, alpha=False)
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

    # 6. Pack into Unity Standard Shader MetallicGloss Map:
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
        smoothness_vals = np.clip(1.0 - roughness_vals, 0.0, 1.0)

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
            s = max(0.0, min(1.0, 1.0 - r_pix[i]))
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

    if not material or not material.use_nodes:
        return
    tree = material.node_tree
    bsdf = tree.nodes.get("Principled BSDF")
    if not bsdf or "Base Color" not in bsdf.inputs:
        return

    # Use Object coordinates for seamless 3D spatial coherence across adjacent parts
    tex_coord = tree.nodes.new("ShaderNodeTexCoord")
    c = camo_type.lower()
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
    """Attaches a procedural panel seam groove and relief bump network to the material's Normal socket."""
    if not material or not material.use_nodes:
        return
    tree = material.node_tree
    bsdf = tree.nodes.get("Principled BSDF")
    if not bsdf or "Normal" not in bsdf.inputs:
        return

    tex_coord = tree.nodes.new("ShaderNodeTexCoord")
    vor = tree.nodes.new("ShaderNodeTexVoronoi")
    vor.feature = "DISTANCE_TO_EDGE"
    vor.inputs["Scale"].default_value = 3.8 * max(0.1, scale)

    bump = tree.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = strength
    bump.inputs["Distance"].default_value = 0.04

    tree.links.new(tex_coord.outputs["Object"], vor.inputs["Vector"])
    tree.links.new(vor.outputs["Distance"], bump.inputs["Height"])
    tree.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])


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
            apply_procedural_normal_relief(self.primary_armor, scale=1.0, strength=0.75 * normal_intensity)
            apply_procedural_normal_relief(self.light_armor, scale=1.2, strength=0.65 * normal_intensity)
            apply_procedural_normal_relief(self.gunmetal, scale=1.5, strength=0.50 * normal_intensity)


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
    if c == "desert":
        p_col = (0.42, 0.32, 0.18)
        l_col = (0.55, 0.44, 0.26)
        g_col = (0.07, 0.08, 0.09)
        t_col = (0.04, 0.045, 0.05)
        w_col = (0.92, 0.52, 0.10)
        e_col = (0.98, 0.42, 0.08)
        e_str = 2.0
    elif c == "urban":
        p_col = (0.22, 0.24, 0.26)
        l_col = (0.36, 0.39, 0.42)
        g_col = (0.05, 0.06, 0.07)
        t_col = (0.03, 0.035, 0.04)
        w_col = (0.85, 0.85, 0.20)
        e_col = (0.0, 0.85, 1.0)
        e_str = 2.2
    elif c == "arctic":
        p_col = (0.76, 0.82, 0.88)
        l_col = (0.52, 0.58, 0.66)
        g_col = (0.08, 0.10, 0.14)
        t_col = (0.05, 0.06, 0.08)
        w_col = (0.92, 0.15, 0.18)
        e_col = (0.15, 0.85, 1.0)
        e_str = 2.2
    elif c in ("woodland", "jungle"):
        p_col = (0.18, 0.25, 0.14)
        l_col = (0.28, 0.36, 0.22)
        g_col = (0.06, 0.07, 0.08)
        t_col = (0.04, 0.045, 0.05)
        w_col = (0.90, 0.65, 0.12)
        e_col = (0.20, 0.95, 0.45)
        e_str = 2.0
    elif c == "digital":
        p_col = (0.22, 0.26, 0.28)
        l_col = (0.48, 0.45, 0.38)
        g_col = (0.07, 0.08, 0.09)
        t_col = (0.04, 0.045, 0.05)
        w_col = (0.88, 0.72, 0.15)
        e_col = (0.12, 0.95, 0.85)
        e_str = 2.2
    elif c == "splinter":
        p_col = (0.14, 0.16, 0.18)
        l_col = (0.68, 0.72, 0.74)
        g_col = (0.05, 0.06, 0.07)
        t_col = (0.03, 0.035, 0.04)
        w_col = (0.95, 0.22, 0.15)
        e_col = (0.98, 0.45, 0.10)
        e_str = 2.3
    elif c == "tigerstripe":
        p_col = (0.16, 0.24, 0.14)
        l_col = (0.34, 0.26, 0.18)
        g_col = (0.06, 0.07, 0.08)
        t_col = (0.04, 0.045, 0.05)
        w_col = (0.85, 0.60, 0.12)
        e_col = (0.10, 0.88, 0.42)
        e_str = 2.0
    else:
        f = faction.lower()
        if f == "siege":
            p_col = (0.26, 0.14, 0.075)
            l_col = (0.43, 0.30, 0.16)
            g_col = (0.065, 0.072, 0.078)
            t_col = (0.035, 0.040, 0.043)
            w_col = (0.92, 0.48, 0.08)
            e_col = (0.98, 0.35, 0.05)
            e_str = 2.2
        elif f == "syndicate":
            p_col = (0.055, 0.11, 0.20)
            l_col = (0.28, 0.34, 0.42)
            g_col = (0.035, 0.042, 0.055)
            t_col = (0.025, 0.031, 0.038)
            w_col = (0.77, 0.045, 0.31)
            e_col = (0.0, 0.74, 1.0)
            e_str = 2.4
        elif f == "arctic":
            p_col = (0.72, 0.78, 0.85)
            l_col = (0.45, 0.52, 0.62)
            g_col = (0.08, 0.10, 0.14)
            t_col = (0.05, 0.06, 0.08)
            w_col = (0.90, 0.12, 0.15)
            e_col = (0.15, 0.85, 1.0)
            e_str = 2.0
        else:
            p_col = (0.12, 0.19, 0.24)
            l_col = (0.22, 0.32, 0.38)
            g_col = (0.07, 0.10, 0.12)
            t_col = (0.045, 0.055, 0.06)
            w_col = (0.95, 0.32, 0.05)
            e_col = (0.04, 0.72, 0.88)
            e_str = 1.9

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
    - 1-segment chamfers on all hard edges for RTS specular highlights
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

    def set_archetype(self, archetype: str) -> "RogueVehicleBuilder":
        self.archetype = archetype
        return self

    def set_wear_level(self, wear_level: str) -> "RogueVehicleBuilder":
        self.wear_level = wear_level
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
        target_objects: Sequence[bpy.types.Object] | None = None,
    ) -> "RogueVehicleBuilder":
        """Creates a kinematic vehicle armature and binds mesh components to rigid bones."""
        objs = target_objects if target_objects is not None else self.objects
        self.armature_object = generate_procedural_armature(
            objs,
            prefix=self.name,
            turret_center=self.turret_center,
            gun_pivot=self.gun_pivot,
            hull_deck_z=self.hull_deck_z,
        )
        if with_animations:
            self.action_clips = generate_procedural_action_clips(self.armature_object, prefix=self.name)
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
        self.objects.append(make_box(
            f"{self.name}Armor_UpperDeck",
            (width * 0.65, length * 0.72, 0.24),
            (0.0, -0.05, height + 0.10),
            self.palette.light_armor, bevel=0.035
        ))
        return self

    def add_continuous_tracks(
        self,
        length: float = 2.85,
        height: float = 0.62,
        belt_width: float = 0.34,
        roadwheels: int = 5,
        with_skirts: bool = True,
    ) -> "RogueVehicleBuilder":
        half_w = self.hull_width * 0.5
        track_x = half_w * 0.90
        for side in (-1, 1):
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
            )
            self.objects.extend(tracks)
        return self

    def add_all_terrain_wheels(
        self,
        wheelbase_y: float = 1.8,
        track_width_x: float = 1.6,
        wheel_radius: float = 0.38,
        wheel_width: float = 0.28,
    ) -> "RogueVehicleBuilder":
        effective_z = self.ground_clearance + wheel_radius + 0.035
        for side in (-1, 1):
            x = side * (track_width_x * 0.5)
            for y_pos, label in [(-wheelbase_y * 0.5, "Rear"), (wheelbase_y * 0.5, "Front")]:
                w = make_all_terrain_wheel_assembly(
                    f"{self.name}Wheel_{label}_{'L' if side < 0 else 'R'}",
                    location=(x, y_pos, effective_z),
                    radius=wheel_radius, width=wheel_width,
                    tire_mat=self.palette.tread, rim_mat=self.palette.wheel_rim,
                    hub_mat=self.palette.gunmetal, accent_mat=self.palette.warning
                )
                self.objects.extend(w)
        return self

    def add_bipedal_legs(
        self,
        hip_height: float = 1.45,
        hip_spacing_x: float = 0.84,
        total_height: float = 1.60,
    ) -> "RogueVehicleBuilder":
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
    ) -> "RogueVehicleBuilder":
        gun_pivot = self.turret_center + Vector((0.0, 0.45, 0.02))
        elev_rad = math.radians(elevation_deg)
        gun_dir = Vector((0.0, math.cos(elev_rad), math.sin(elev_rad))).normalized()
        self.gun_pivot = gun_pivot + gun_dir * (length + 0.32)
        self.gun_dir = gun_dir
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
        half_w = self.hull_width * 0.46
        half_l = self.hull_length * 0.40
        for sx in (-1, 1):
            for sy in (-1, 1):
                tag = f"{'L' if sx < 0 else 'R'}_{'F' if sy > 0 else 'R'}"
                hip = Vector((sx * half_w, sy * half_l, self.ground_clearance + 0.65))
                leg = make_quad_crawler_leg(
                    f"{self.name}CrawlerLeg_{tag}",
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
        )
        self.objects.extend(cannons)
        self.preserved_pivots.append(f"{self.name}DualGun_MantletBlock")
        return self

    def add_quad_track_pods(
        self,
        pod_length: float = 1.55,
        pod_height: float = 0.56,
        track_spacing_x: float = 2.4,
        pod_spacing_y: float = 1.9,
    ) -> "RogueVehicleBuilder":
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
        self.preserved_pivots.append(f"{self.name}PlasmaMortar_MortarTrunnion")
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
            antenna_parts=self.objects,
        )
        return self

    def build(self) -> list[bpy.types.Object]:
        """Returns the raw list of editable kitbashed objects."""
        return self.objects

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
        bake_vertex_dirt: bool = True,
        include_locators: bool = True,
        generate_lods: bool = False,
        lod_ratios: Sequence[Tuple[str, float]] = (("LOD0", 1.0), ("LOD1", 0.50), ("LOD2", 0.22)),
        generate_colliders: bool = False,
        wear_level: str | None = None,
        gameplay_meta_path: Path | None = None,
        wreckage_fbx_path: Path | None = None,
        damaged_fbx_path: Path | None = None,
        visual_root_path: str = "Vehicle",
        profile_budget: bool = True,
        target_budget: int = 4000,
        include_vfx_flash: bool = False,
        include_vfx_thruster: bool = False,
        include_shield_bubble: bool = False,
        include_rig: bool = False,
        include_animations: bool = False,
        bake_atlas: bool = False,
        atlas_resolution: int = 1024,
        single_material_atlas: bool = False,
        export_detached_attachments: bool = False,
        render_icons: bool = False,
        icon_resolutions: Sequence[int] = (64, 128),
        icon_elevation: float = 45.0,
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

        # Save the editable, unbatched source before vertex shading and export transforms.
        bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))

        # 6. Procedural multi-channel vertex shading & cavity AO
        effective_wear = wear_level or self.wear_level
        if effective_wear:
            exhausts = [e for e in (self.exhaust_l, self.exhaust_r) if e is not None]
            bake_advanced_vertex_shading(
                self.objects,
                wear_level=effective_wear,
                exhaust_locs=exhausts if exhausts else None,
                muzzle_loc=self.gun_pivot,
            )
        elif bake_vertex_dirt:
            bake_vertex_ambient_dirt(self.objects)

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

        # 9. Material batching for FBX with smart angle-weighted smoothing
        batched_objects = batch_and_join_by_material(
            model_export_source_objects,
            prefix=self.name,
            preserve_names=self.preserved_pivots,
            smooth_by_angle=smooth_by_angle,
            smooth_angle_deg=smooth_angle_deg,
        )

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
            )
            diagnostics["lod_levels"] = {k: len(v) for k, v in lod_dict.items()}

        # 12. Create Unity locator sockets if requested
        export_objects = list(batched_objects)
        if include_locators:
            export_objects.extend(self.locator_sockets)

        if generate_colliders and self.collider_objects:
            export_objects.extend(self.collider_objects)

        # 13. Kinematic Armature Rigging & Action Clips
        if include_rig:
            self.add_armature_rig(with_animations=include_animations, target_objects=batched_objects)
            if self.armature_object:
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
            make_wreckage_variant(
                source_objects=loaded_mesh_objects,
                prefix=self.name,
                output_fbx_path=wreckage_fbx_path,
                output_blend_path=wreckage_blend,
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

    def add_colliders(self) -> "RogueStructureBuilder":
        self.collider_objects = generate_collision_hulls(
            self.objects, prefix=self.name, include_turret_collider=True
        )
        return self

    def add_unity_locators(self) -> "RogueStructureBuilder":
        self.locator_sockets = []
        for s_name, s_pos in self.sockets.items():
            fwd = (0.0, 1.0, 0.0) if ("Deploy" in s_name or "Muzzle" in s_name) else (0.0, 0.0, 1.0)
            emp = create_unity_empty_locator(s_name, s_pos, forward=fwd)
            self.locator_sockets.append(emp)
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
        render_icons: bool = False,
        icon_resolutions: Sequence[int] = (64, 128),
        icon_elevation: float = 45.0,
        bake_atlas: bool = False,
        atlas_resolution: int = 1024,
        single_material_atlas: bool = False,
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
        )

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


