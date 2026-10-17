"""RogueModelKit: Reusable procedural 3D modeling toolkit for Rogue Command vehicles.

Provides parametric geometric primitives, chamfered hard-surface builders,
continuous track loops, stepped wheels, recessed optics, and preview rendering
designed for Blender 4.5.14 LTS and Unity 2021.3.45f2 FBX export.
"""

from __future__ import annotations
import math
from pathlib import Path
from typing import Sequence, Tuple

import bpy
from mathutils import Vector


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


def make_box(
    name: str,
    size: Tuple[float, float, float],
    location: Tuple[float, float, float],
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
    location: Tuple[float, float, float],
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
    start: Sequence[float],
    end: Sequence[float],
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
    location: Tuple[float, float, float],
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
        # Lower chassis
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


def make_continuous_track_belt(
    name: str,
    x_pos: float,
    length: float,
    height: float,
    width: float,
    tread_mat: bpy.types.Material | None,
    ground_z: float = 0.08,
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
        (-hw, rear_approach_y, ground_z),   # 0
        ( hw, rear_approach_y, ground_z),   # 1
        ( hw, approach_y, ground_z),        # 2
        (-hw, approach_y, ground_z),        # 3
        # Front climb
        ( hw, front_apex_y, front_apex_z),  # 4
        (-hw, front_apex_y, front_apex_z),  # 5
        # Front top curve
        ( hw, half_l * 0.82, height),       # 6
        (-hw, half_l * 0.82, height),       # 7
        # Top return run
        ( hw, -half_l * 0.80, height),      # 8
        (-hw, -half_l * 0.80, height),      # 9
        # Rear over curve
        ( hw, rear_apex_y, rear_apex_z),    # 10
        (-hw, rear_apex_y, rear_apex_z),    # 11
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
    location: Tuple[float, float, float],
    radius: float,
    width: float,
    rim_mat: bpy.types.Material | None,
    hub_mat: bpy.types.Material | None,
    axle_mat: bpy.types.Material | None,
    vertices: int = 12,
) -> list[bpy.types.Object]:
    """Creates a 3-tier road wheel: outer tread rim, recessed hub dish, and center axle cap."""
    objects = []
    wheel_rot = (0.0, math.radians(90), 0.0)
    # 1. Main outer rim
    objects.append(make_cylinder(
        f"{prefix}_Rim", radius, width, location, rim_mat,
        rotation=wheel_rot, vertices=vertices, bevel=radius * 0.14
    ))
    # 2. Recessed inner dish on outer face
    sign = 1.0 if location[0] > 0 else -1.0
    dish_offset = sign * (width * 0.5 + 0.004)
    objects.append(make_cylinder(
        f"{prefix}_Dish", radius * 0.74, 0.012,
        (location[0] + dish_offset, location[1], location[2]), hub_mat,
        rotation=wheel_rot, vertices=vertices - 2, bevel=0.008
    ))
    # 3. Center hex axle cap
    cap_offset = sign * (width * 0.5 + 0.018)
    objects.append(make_cylinder(
        f"{prefix}_AxleCap", radius * 0.32, 0.025,
        (location[0] + cap_offset, location[1], location[2]), axle_mat,
        rotation=wheel_rot, vertices=8, bevel=0.006
    ))
    return objects


def make_engine_deck_grille(
    prefix: str,
    location: Tuple[float, float, float],
    size: Tuple[float, float, float],
    slat_count: int,
    slat_mat: bpy.types.Material | None,
    frame_mat: bpy.types.Material | None,
) -> list[bpy.types.Object]:
    """Creates an armored engine intake deck with angled ventilation louvers."""
    objects = []
    # Base recessed well
    objects.append(make_box(f"{prefix}_Frame", size, location, frame_mat, bevel=0.018))

    # Angled louvers
    slat_spacing = (size[1] * 0.82) / slat_count
    slat_width = size[0] * 0.84
    slat_thickness = size[2] * 0.55
    start_y = location[1] - (size[1] * 0.41) + (slat_spacing * 0.5)

    for i in range(slat_count):
        y = start_y + i * slat_spacing
        objects.append(make_box(
            f"{prefix}_Slat_{i+1}",
            (slat_width, slat_spacing * 0.65, slat_thickness),
            (location[0], y, location[2] + size[2] * 0.35),
            slat_mat, bevel=0.006,
            rotation=(math.radians(-24), 0.0, 0.0),
        ))
    return objects


def make_recessed_sensor_visor(
    prefix: str,
    location: Tuple[float, float, float],
    size: Tuple[float, float, float],
    armor_mat: bpy.types.Material | None,
    lens_mat: bpy.types.Material | None,
    cavity_mat: bpy.types.Material | None,
) -> list[bpy.types.Object]:
    """Creates a protective armored visor housing with recessed glowing optical slits."""
    objects = []
    # 1. Armored protective housing with forward eyebrow
    objects.append(make_box(
        f"{prefix}_Housing", size, location, armor_mat, bevel=0.025
    ))
    # 2. Recessed dark optic well
    well_size = (size[0] * 0.80, size[1] * 0.35, size[2] * 0.58)
    well_loc = (location[0], location[1] + size[1] * 0.35, location[2])
    objects.append(make_box(
        f"{prefix}_Well", well_size, well_loc, cavity_mat, bevel=0.008
    ))
    # 3. Twin glowing sensor lenses
    lens_w = well_size[0] * 0.36
    for idx, side in enumerate((-1, 1), start=1):
        lens_x = location[0] + side * (lens_w * 0.68)
        objects.append(make_box(
            f"{prefix}_Lens_{idx}",
            (lens_w, 0.025, well_size[2] * 0.72),
            (lens_x, location[1] + size[1] * 0.48, location[2]),
            lens_mat, bevel=0.004
        ))
    return objects


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

    # Camera: Standard RTS orthographic view
    bpy.ops.object.camera_add(location=(4.8, 5.8, 3.6))
    camera = bpy.context.object
    camera.name = "PreviewCamera"
    target = Vector((0.0, 0.0, 0.72))
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = ortho_scale
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


def export_unity_fbx(fbx_path: Path, model_objects: Sequence[bpy.types.Object]) -> None:
    """Exports model objects to FBX with optimal scale and axis mappings for Unity."""
    fbx_path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in model_objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = model_objects[0]

    bpy.ops.export_scene.fbx(
        filepath=str(fbx_path),
        use_selection=True,
        object_types={"MESH"},
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
