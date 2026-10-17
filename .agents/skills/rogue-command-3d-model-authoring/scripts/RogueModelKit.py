"""RogueModelKit: Reusable procedural 3D modeling toolkit for Rogue Command vehicles.

Provides parametric geometric primitives, chamfered hard-surface builders,
continuous track loops, stepped wheels, recessed optics, advanced rotary weapons,
missile pods, energy emitters, bipedal legs, draw-call batching, multi-perspective
studio rendering, and automated quality diagnostics designed for Blender 4.5.14 LTS
and Unity 2021.3.45f2 FBX export.
"""

from __future__ import annotations
import math
from pathlib import Path
from typing import Any, Dict, Sequence, Tuple

import bpy
from mathutils import Vector


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


def create_unity_locator_sockets(
    name_prefix: str,
    muzzle_loc: Tuple[float, float, float] | Vector | None = None,
    muzzle_dir: Tuple[float, float, float] | Vector | None = None,
    turret_loc: Tuple[float, float, float] | Vector | None = None,
    sensor_loc: Tuple[float, float, float] | Vector | None = None,
    exhaust_left: Tuple[float, float, float] | Vector | None = None,
    exhaust_right: Tuple[float, float, float] | Vector | None = None,
) -> list[bpy.types.Object]:
    """Generates standard Unity empty sockets (MuzzlePoint, TurretPivot, Exhausts) for engine binding."""
    empties = []
    def _add_empty(socket_name: str, loc: Vector, rot: Tuple[float, float, float] = (0.0, 0.0, 0.0)):
        bpy.ops.object.empty_add(type="PLAIN_AXES", radius=0.15, location=loc)
        e = bpy.context.object
        e.name = f"{name_prefix}_{socket_name}"
        e.rotation_euler = rot
        empties.append(e)

    if muzzle_loc is not None:
        m_loc = Vector(muzzle_loc)
        m_dir = Vector(muzzle_dir if muzzle_dir is not None else (0.0, 1.0, 0.0)).normalized()
        rot = m_dir.to_track_quat("Z", "Y").to_euler()
        _add_empty("Socket_MuzzlePrimary", m_loc, rot)

    if turret_loc is not None:
        _add_empty("Socket_TurretPivot", Vector(turret_loc))

    if sensor_loc is not None:
        _add_empty("Socket_SensorEye", Vector(sensor_loc))

    if exhaust_left is not None:
        _add_empty("Socket_Exhaust_L", Vector(exhaust_left))

    if exhaust_right is not None:
        _add_empty("Socket_Exhaust_R", Vector(exhaust_right))

    _add_empty("Socket_GroundCenter", Vector((0.0, 0.0, 0.0)))
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
    
    Dramatically reduces draw calls in Unity (e.g. from 80+ GameObjects to 4-6).
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
        if obj.type != "MESH":
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
) -> None:
    """Exports model objects to FBX with optimal scale and axis mappings for Unity."""
    fbx_path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in model_objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = model_objects[0]

    obj_types = {"MESH", "EMPTY"} if include_empties else {"MESH"}
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
    )


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
    ):
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


def get_faction_palette(faction: str = "vanguard", prefix: str = "Unit", camo: str = "none") -> FactionPalette:
    """Returns a pre-calibrated 5-tone PBR material set.
    Factions:
      - 'vanguard': Slate teal, seafoam, charcoal gunmetal, amber accent, arc cyan emission
      - 'siege': Rust ochre, desert sand, cast iron, hazard yellow, molten orange emission
      - 'syndicate': Deep cobalt, titanium grey, anodized black, electric magenta, plasma cyan emission
      - 'arctic': Frost white, slate blue, dark tungsten, crimson warning, bright blue emission
    Camo styles:
      - 'desert': Ochre sand / khaki two-tone with matte finish
      - 'urban': Neutral slate / asphalt ash grey with cool cyan accents
      - 'arctic': Glacial white / blue-grey with high-visibility signal markings
      - 'woodland': Olive drab / forest green tactical split
    """
    c = camo.lower()
    if c == "desert":
        return FactionPalette(prefix, (0.42, 0.32, 0.18), (0.55, 0.44, 0.26), (0.07, 0.08, 0.09),
                              (0.04, 0.045, 0.05), (0.92, 0.52, 0.10), (0.98, 0.42, 0.08), 2.0)
    elif c == "urban":
        return FactionPalette(prefix, (0.22, 0.24, 0.26), (0.36, 0.39, 0.42), (0.05, 0.06, 0.07),
                              (0.03, 0.035, 0.04), (0.85, 0.85, 0.20), (0.0, 0.85, 1.0), 2.2)
    elif c == "arctic":
        return FactionPalette(prefix, (0.76, 0.82, 0.88), (0.52, 0.58, 0.66), (0.08, 0.10, 0.14),
                              (0.05, 0.06, 0.08), (0.92, 0.15, 0.18), (0.15, 0.85, 1.0), 2.2)
    elif c in ("woodland", "jungle"):
        return FactionPalette(prefix, (0.18, 0.25, 0.14), (0.28, 0.36, 0.22), (0.06, 0.07, 0.08),
                              (0.04, 0.045, 0.05), (0.90, 0.65, 0.12), (0.20, 0.95, 0.45), 2.0)

    f = faction.lower()
    if f == "siege":
        return FactionPalette(prefix, (0.26, 0.14, 0.075), (0.43, 0.30, 0.16), (0.065, 0.072, 0.078),
                              (0.035, 0.040, 0.043), (0.92, 0.48, 0.08), (0.98, 0.35, 0.05), 2.2)
    elif f == "syndicate":
        return FactionPalette(prefix, (0.055, 0.11, 0.20), (0.28, 0.34, 0.42), (0.035, 0.042, 0.055),
                              (0.025, 0.031, 0.038), (0.77, 0.045, 0.31), (0.0, 0.74, 1.0), 2.4)
    elif f == "arctic":
        return FactionPalette(prefix, (0.72, 0.78, 0.85), (0.45, 0.52, 0.62), (0.08, 0.10, 0.14),
                              (0.05, 0.06, 0.08), (0.90, 0.12, 0.15), (0.15, 0.85, 1.0), 2.0)
    else:
        return FactionPalette(prefix, (0.12, 0.19, 0.24), (0.22, 0.32, 0.38), (0.07, 0.10, 0.12),
                              (0.045, 0.055, 0.06), (0.95, 0.32, 0.05), (0.04, 0.72, 0.88), 1.9)


class RogueVehicleBuilder:
    """Fluent procedural builder for Rogue Command vehicles and units.
    
    Guarantees:
    - 5-tone faction PBR material standard
    - 1-segment chamfers on all hard edges for RTS specular highlights
    - Exact ground contact at Z >= 0.0m
    - Strict coaxial weapon alignment
    - Automated draw-call batching to 4-6 submeshes
    - Automated quality diagnostics and multi-perspective studio previews
    """

    def __init__(self, name: str, faction: str = "vanguard", ground_clearance: float = 0.06, camo: str = "none"):
        self.name = name
        self.palette = get_faction_palette(faction, name, camo=camo)
        self.ground_clearance = ground_clearance
        self.objects: list[bpy.types.Object] = []
        self.preserved_pivots: list[str] = []
        self.hull_deck_z: float = 0.88
        self.turret_center: Vector = Vector((0.0, 0.05, 1.15))
        self.hull_length: float = 3.0
        self.hull_width: float = 2.4
        self.gun_pivot: Vector | None = None
        self.gun_dir: Vector | None = None
        self.sensor_loc: Vector | None = None
        self.exhaust_l: Vector | None = None
        self.exhaust_r: Vector | None = None
        self.locator_sockets: list[bpy.types.Object] = []
        self.is_hover: bool = False

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
    ) -> "RogueVehicleBuilder":
        for side in (-1, 1):
            leg = make_bipedal_digitigrade_leg(
                f"{self.name}Leg",
                hip_location=(side * (hip_spacing_x * 0.5), -0.05, hip_height),
                side=side,
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

    def add_unity_locators(self) -> "RogueVehicleBuilder":
        self.locator_sockets = create_unity_locator_sockets(
            name_prefix=self.name,
            muzzle_loc=self.gun_pivot,
            muzzle_dir=self.gun_dir,
            turret_loc=self.turret_center,
            sensor_loc=self.sensor_loc,
            exhaust_left=self.exhaust_l,
            exhaust_right=self.exhaust_r,
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
    ) -> Dict[str, Any]:
        """Runs diagnostics, smart UV unwrapping, preview rendering, batching, and FBX export."""
        blend_path.parent.mkdir(parents=True, exist_ok=True)
        fbx_path.parent.mkdir(parents=True, exist_ok=True)

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

        # 5. Save editable source .blend
        bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))

        # 6. Procedural vertex ambient dirt baking (cavity shading)
        if bake_vertex_dirt:
            bake_vertex_ambient_dirt(self.objects)

        # 7. Material batching for FBX with smart angle-weighted smoothing
        batched_objects = batch_and_join_by_material(
            self.objects,
            prefix=self.name,
            preserve_names=self.preserved_pivots,
            smooth_by_angle=smooth_by_angle,
            smooth_angle_deg=smooth_angle_deg,
        )

        # 8. Create Unity locator sockets if requested
        export_objects = list(batched_objects)
        if include_locators:
            if not self.locator_sockets:
                self.add_unity_locators()
            export_objects.extend(self.locator_sockets)

        # 9. Export FBX for Unity
        export_unity_fbx(fbx_path, export_objects, include_empties=include_locators)

        diagnostics["exported_batched_objects"] = len(batched_objects)
        diagnostics["locator_sockets"] = len(self.locator_sockets) if include_locators else 0
        diagnostics["blend_path"] = str(blend_path)
        diagnostics["fbx_path"] = str(fbx_path)
        print(f"[{self.name}] Build and export complete!")
        print(f"  Source parts: {diagnostics['object_count']} -> Batched Unity parts: {len(batched_objects)}")
        print(f"  Polygons: {diagnostics['total_polygons']}, Ground valid: {diagnostics['ground_contact_valid']}")
        return diagnostics

