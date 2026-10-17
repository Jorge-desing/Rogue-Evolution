"""Build the original low-poly Skirmish Strike Bike visual source and Unity FBX."""

from pathlib import Path
import math

import bpy
from mathutils import Vector


REPO_ROOT = Path(__file__).resolve().parents[1]
BLEND_PATH = REPO_ROOT / "content" / "source-art" / "skirmish-strike-bike.blend"
FBX_PATH = (REPO_ROOT / "unity" / "RogueCommandAssetAuthoring" / "Assets" /
            "RogueCommandExpansionSample" / "Models" / "SkirmishStrikeBike.fbx")
PREVIEW_PATH = REPO_ROOT / "artifacts" / "previews" / "skirmish-strike-bike.png"


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def make_material(name, color, metallic=0.0, roughness=0.55):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True
    shader = material.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1.0)
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness
    return material


def finish_mesh(obj, material, bevel=0.0):
    obj.data.materials.append(material)
    for polygon in obj.data.polygons:
        polygon.use_smooth = False
    if bevel > 0:
        modifier = obj.modifiers.new("machined chamfers", "BEVEL")
        modifier.width = bevel
        modifier.segments = 1
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        for polygon in obj.data.polygons:
            polygon.use_smooth = False
    obj.select_set(False)
    return obj


def make_box(name, size, location, material, bevel=0.025, rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    obj.rotation_euler = rotation
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish_mesh(obj, material, bevel)


def make_cylinder(name, radius, depth, location, material, rotation=(0.0, 0.0, 0.0), vertices=12):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=depth, location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish_mesh(obj, material, min(radius * 0.16, depth * 0.14))


def make_rod(name, start, end, radius, material, vertices=8):
    start = Vector(start)
    end = Vector(end)
    direction = end - start
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=direction.length, location=(start + end) * 0.5)
    obj = bpy.context.object
    obj.name = name
    obj.rotation_euler = direction.to_track_quat("Z", "Y").to_euler()
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish_mesh(obj, material, min(radius * 0.2, direction.length * 0.03))


def make_torus(name, location, major_radius, minor_radius, material, rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_torus_add(
        major_segments=12, minor_segments=6, major_radius=major_radius,
        minor_radius=minor_radius, location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    return finish_mesh(obj, material)


def make_front_fairing(material):
    vertices = [
        (-0.39, 0.20, 0.55), (0.39, 0.20, 0.55),
        (-0.29, 0.92, 0.58), (0.29, 0.92, 0.58),
        (-0.23, 0.69, 1.22), (0.23, 0.69, 1.22),
        (-0.32, 0.24, 1.04), (0.32, 0.24, 1.04),
    ]
    faces = [
        (3, 2, 1, 0), (4, 5, 3, 2), (0, 1, 7, 6),
        (1, 3, 5, 7), (0, 6, 4, 2), (6, 7, 5, 4),
    ]
    mesh = bpy.data.meshes.new("Strike Bike sloped front fairing")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new("StrikeBikeArmor_FrontFairing", mesh)
    bpy.context.collection.objects.link(obj)
    return finish_mesh(obj, material, 0.035)


def add_preview_stage(model_objects):
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0.0, 0.0, -0.025))
    floor = bpy.context.object
    floor.name = "Preview floor"
    floor.data.materials.append(make_material("Preview concrete", (0.035, 0.052, 0.065), 0.0, 0.92))

    bpy.ops.object.camera_add(location=(4.2, 5.2, 3.2))
    camera = bpy.context.object
    camera.name = "Preview camera"
    target = Vector((0.0, 0.0, 0.72))
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 3.9
    bpy.context.scene.camera = camera

    for name, location, energy, size in (
        ("Key", (3.5, 4.5, 6.0), 760, 4.0),
        ("Fill", (-4.0, 1.0, 3.5), 460, 3.5),
        ("Rim", (-1.5, -4.0, 4.5), 900, 3.0),
    ):
        bpy.ops.object.light_add(type="AREA", location=location)
        light = bpy.context.object
        light.name = "Preview " + name
        light.data.energy = energy
        light.data.shape = "DISK"
        light.data.size = size
        direction = Vector((0.0, 0.0, 0.6)) - light.location
        light.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 900
    scene.render.resolution_y = 760
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "AgX"
    scene.render.filepath = str(PREVIEW_PATH)
    scene.world.color = (0.035, 0.045, 0.065)

    bpy.ops.object.select_all(action="DESELECT")
    for obj in model_objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = model_objects[0]


def main():
    clear_scene()
    BLEND_PATH.parent.mkdir(parents=True, exist_ok=True)
    FBX_PATH.parent.mkdir(parents=True, exist_ok=True)
    PREVIEW_PATH.parent.mkdir(parents=True, exist_ok=True)

    armor = make_material("Strike Bike Armor", (0.055, 0.13, 0.19), 0.46, 0.34)
    armor_light = make_material("Strike Bike Light Armor", (0.20, 0.34, 0.40), 0.34, 0.39)
    rubber = make_material("Strike Bike Tire", (0.025, 0.033, 0.04), 0.08, 0.78)
    wheel = make_material("Strike Bike Wheel Metal", (0.19, 0.28, 0.33), 0.62, 0.38)
    gunmetal = make_material("Strike Bike Gunmetal", (0.07, 0.10, 0.13), 0.76, 0.30)
    warning = make_material("Strike Bike Warning Orange", (0.96, 0.27, 0.045), 0.16, 0.36)
    sensor = make_material("Strike Bike Arc Cyan", (0.035, 0.68, 0.92), 0.22, 0.20)

    model_objects = []
    wheel_rotation = (0.0, math.radians(90), 0.0)
    wheel_positions = [("Rear", (0.0, -0.78, 0.39)), ("Front", (0.0, 0.75, 0.39))]
    for axle, position in wheel_positions:
        model_objects.append(make_cylinder(
            "StrikeBikeTire_{0}".format(axle),
            0.38, 0.20, position, rubber, wheel_rotation, 12))
        for side in (-1, 1):
            side_name = "L" if side < 0 else "R"
            model_objects.append(make_cylinder(
                "StrikeBikeWheel_Hub_{0}_{1}".format(axle, side_name),
                0.22, 0.035, (side * 0.108, position[1], position[2]),
                wheel, wheel_rotation, 10))
            model_objects.append(make_cylinder(
                "StrikeBikeWheel_AxleCap_{0}_{1}".format(axle, side_name),
                0.10, 0.045, (side * 0.13, position[1], position[2]),
                gunmetal, wheel_rotation, 8))

    # The narrow two-wheel chassis keeps the shape aligned with the native bike controller.
    for side in (-1, 1):
        x = side * 0.47
        model_objects += [
            make_rod("StrikeBikeFrame_Main_{0}".format(side), (x, -0.79, 0.40),
                     (x, 0.76, 0.50), 0.075, gunmetal),
            make_rod("StrikeBikeFrame_Support_{0}".format(side), (x, -0.66, 0.38),
                     (side * 0.23, -0.06, 0.79), 0.055, wheel),
            make_rod("StrikeBikeFork_{0}".format(side), (x, 0.72, 0.42),
                     (side * 0.19, 0.50, 1.16), 0.055, wheel),
            make_box("StrikeBikeArmor_SidePod_{0}".format(side),
                     (0.18, 0.78, 0.30), (side * 0.48, -0.10, 0.69), armor, 0.045),
            make_box("StrikeBikeAccent_SideStripe_{0}".format(side),
                     (0.04, 0.53, 0.055), (side * 0.581, -0.07, 0.76), warning, 0.012),
        ]

    model_objects += [
        make_box("StrikeBikeArmor_LowerFrame", (0.60, 1.28, 0.29), (0.0, -0.03, 0.62), gunmetal, 0.08),
        make_box("StrikeBikeArmor_BatteryPod", (0.69, 0.65, 0.51), (0.0, -0.13, 0.94), armor_light, 0.095),
        make_box("StrikeBikeAccent_BatteryRail", (0.76, 0.16, 0.08), (0.0, -0.14, 1.22), warning, 0.025),
        make_front_fairing(armor),
        make_box("StrikeBikeArmor_RearChargeCage", (0.88, 0.43, 0.68), (0.0, -0.74, 1.05), armor, 0.095),
        make_cylinder("StrikeBikePayload_Core", 0.25, 0.64, (0.0, -0.74, 1.07),
                      warning, (math.radians(90), 0.0, 0.0), 10),
        make_box("StrikeBikePayload_ChargeBand", (0.90, 0.10, 0.10),
                 (0.0, -0.74, 1.12), sensor, 0.02),
        make_box("StrikeBikeHandlebar", (0.72, 0.13, 0.075), (0.0, 0.48, 1.23), gunmetal, 0.025),
        make_cylinder("StrikeBikeSensor_Headlamp", 0.105, 0.055, (0.0, 0.94, 0.91),
                      sensor, (math.radians(90), 0.0, 0.0), 10),
        make_torus("StrikeBikeArc_InductionRing", (0.0, -0.12, 1.28), 0.26, 0.045, sensor),
        make_box("StrikeBikeArmor_FootPlate_L", (0.34, 0.25, 0.08), (-0.55, -0.04, 0.43), armor_light, 0.025),
        make_box("StrikeBikeArmor_FootPlate_R", (0.34, 0.25, 0.08), (0.55, -0.04, 0.43), armor_light, 0.025),
        make_box("StrikeBikeAccent_FrontMarker_L", (0.07, 0.08, 0.045), (-0.27, 0.82, 0.72), warning, 0.015),
        make_box("StrikeBikeAccent_FrontMarker_R", (0.07, 0.08, 0.045), (0.27, 0.82, 0.72), warning, 0.015),
    ]

    add_preview_stage(model_objects)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))
    bpy.ops.render.render(write_still=True)

    bpy.ops.object.select_all(action="DESELECT")
    for obj in model_objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = model_objects[0]
    bpy.ops.export_scene.fbx(
        filepath=str(FBX_PATH), use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_NONE",
        use_mesh_modifiers=True, mesh_smooth_type="OFF", path_mode="AUTO",
        axis_forward="-Z", axis_up="Y", bake_space_transform=False,
        add_leaf_bones=False, use_custom_props=False)
    print("Created source model: {0}".format(BLEND_PATH))
    print("Created Unity model: {0}".format(FBX_PATH))
    print("Created preview: {0}".format(PREVIEW_PATH))
    print("Exported mesh objects: {0}".format(len(model_objects)))


if __name__ == "__main__":
    main()
