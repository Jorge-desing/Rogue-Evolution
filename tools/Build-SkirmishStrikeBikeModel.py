"""Build the original low-poly Skirmish Strike Bike visual source and Unity FBX."""

from pathlib import Path
import math
import sys

import bpy
from mathutils import Vector


REPO_ROOT = Path(__file__).resolve().parents[1]
BLEND_PATH = REPO_ROOT / "content" / "source-art" / "skirmish-strike-bike.blend"
FBX_PATH = (REPO_ROOT / "unity" / "RogueCommandAssetAuthoring" / "Assets" /
            "RogueCommandExpansionSample" / "Models" / "SkirmishStrikeBike.fbx")
PREVIEW_PATH = REPO_ROOT / "artifacts" / "previews" / "skirmish-strike-bike.png"
sys.path.insert(0, str(Path(__file__).resolve().parent))
import RogueModelKit as modelkit
from ActiveUnitVisualEnhancements import apply_wave2_details, apply_wave34_details
from CanonicalUnitHardpoints import create_canonical_hardpoint_sockets


def fit_length_to_native_footprint(model_objects, max_length=2.2):
    diagnostics = modelkit.validate_model_mesh_quality(model_objects)
    current_length = diagnostics["bounds"]["length_y"]
    if current_length <= max_length:
        return

    factor = max_length / current_length
    for obj in model_objects:
        if obj.type != "MESH":
            continue
        local_to_world = obj.matrix_world.copy()
        world_to_local = local_to_world.inverted()
        for vertex in obj.data.vertices:
            world_position = local_to_world @ vertex.co
            world_position.y *= factor
            vertex.co = world_to_local @ world_position
        obj.data.update()


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def make_material(name, color, metallic=0.0, roughness=0.55, emission_color=None, emission_strength=1.0):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True
    shader = material.node_tree.nodes.get("Principled BSDF")
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
    return modelkit.make_lofted_superellipse_shell(
        "StrikeBikeArmor_FrontFairing",
        [
            (0.18, 0.0, 0.78, 0.25, 0.22),
            (0.38, 0.0, 0.84, 0.34, 0.30),
            (0.64, 0.0, 0.90, 0.28, 0.30),
            (0.80, 0.0, 0.74, 0.20, 0.14),
            (0.90, 0.0, 0.62, 0.10, 0.07),
        ],
        material,
        axis="Y",
        segments=20,
        exponent=0.76,
        smooth=True,
    )


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
    warning = make_material("Strike Bike Warning Orange", (0.96, 0.27, 0.045), 0.16, 0.36,
                            emission_color=(0.96, 0.27, 0.045), emission_strength=1.5)
    sensor = make_material("Strike Bike Arc Cyan", (0.035, 0.68, 0.92), 0.22, 0.20,
                           emission_color=(0.035, 0.68, 0.92), emission_strength=1.8)

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
            model_objects.append(make_torus(
                "StrikeBikeWheel_RimTrim_{0}_{1}".format(axle, side_name),
                (side * 0.105, position[1], position[2]), 0.285, 0.022,
                wheel, (0.0, math.radians(90), 0.0)))

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
            modelkit.make_lofted_superellipse_shell(
                "StrikeBikeArmor_SidePod_{0}".format(side),
                [(-0.54, side * 0.46, 0.68, 0.06, 0.10),
                 (-0.42, side * 0.48, 0.69, 0.09, 0.15),
                 (0.20, side * 0.48, 0.69, 0.09, 0.15),
                 (0.30, side * 0.46, 0.68, 0.06, 0.10)],
                armor, axis="Y", segments=16, exponent=0.76, smooth=True),
            make_box("StrikeBikeAccent_SideStripe_{0}".format(side),
                     (0.04, 0.53, 0.055), (side * 0.578, -0.07, 0.76), warning, 0.012),
        ]

    model_objects += [
        modelkit.make_lofted_superellipse_shell(
            "StrikeBikeArmor_LowerFrame",
            [(-0.70, 0.0, 0.60, 0.20, 0.09), (-0.54, 0.0, 0.63, 0.26, 0.13),
             (0.45, 0.0, 0.63, 0.25, 0.13), (0.64, 0.0, 0.59, 0.16, 0.08)],
            gunmetal, axis="Y", segments=20, exponent=0.76, smooth=True),
        modelkit.make_lofted_superellipse_shell(
            "StrikeBikeArmor_BatteryPod",
            [(-0.49, 0.0, 0.89, 0.17, 0.14), (-0.36, 0.0, 0.92, 0.25, 0.20),
             (0.10, 0.0, 0.93, 0.27, 0.21), (0.26, 0.0, 0.89, 0.21, 0.15)],
            armor_light, axis="Y", segments=20, exponent=0.74, smooth=True),
        make_box("StrikeBikeAccent_BatteryRail", (0.76, 0.16, 0.08), (0.0, -0.14, 1.22), warning, 0.025),
        make_front_fairing(armor),
        modelkit.make_lofted_superellipse_shell(
            "StrikeBikeArmor_RearChargeCage",
            [(-1.03, 0.0, 1.00, 0.18, 0.16), (-0.92, 0.0, 1.01, 0.27, 0.22),
             (-0.57, 0.0, 1.01, 0.27, 0.22), (-0.46, 0.0, 0.99, 0.19, 0.17)],
            armor, axis="Y", segments=20, exponent=0.70, smooth=True),
        make_cylinder("StrikeBikePayload_Core", 0.25, 0.64, (0.0, -0.74, 1.07),
                      warning, (math.radians(90), 0.0, 0.0), 10),
        make_box("StrikeBikePayload_ChargeBand", (0.90, 0.10, 0.10),
                 (0.0, -0.74, 1.12), sensor, 0.02),
        make_box("StrikeBikeHandlebar", (0.72, 0.13, 0.075), (0.0, 0.48, 1.23), gunmetal, 0.025),
        make_torus("StrikeBikeArc_InductionRing", (0.0, -0.12, 1.28), 0.26, 0.045, sensor),
        make_box("StrikeBikeArmor_FootPlate_L", (0.27, 0.25, 0.08), (-0.46, -0.04, 0.43), armor_light, 0.025),
        make_box("StrikeBikeArmor_FootPlate_R", (0.27, 0.25, 0.08), (0.46, -0.04, 0.43), armor_light, 0.025),
        make_box("StrikeBikeAccent_FrontMarker_L", (0.07, 0.08, 0.045), (-0.27, 0.82, 0.72), warning, 0.015),
        make_box("StrikeBikeAccent_FrontMarker_R", (0.07, 0.08, 0.045), (0.27, 0.82, 0.72), warning, 0.015),
    ]

    # Four insulated standoffs carry the induction ring from the battery deck
    # instead of leaving the emissive assembly visually unsupported.
    for index, (offset_x, offset_y) in enumerate(
        ((0.18, 0.0), (0.0, -0.18), (-0.18, 0.0), (0.0, 0.18)),
        start=1,
    ):
        model_objects.append(make_cylinder(
            "StrikeBikeArc_InductionSupport_{0}".format(index),
            0.028,
            0.13,
            (offset_x, -0.12 + offset_y, 1.19),
            armor_light,
            vertices=8,
        ))

    # A narrow saddle and visible suspension struts clarify the rider-facing
    # layout while keeping the model inside the BombBike collider width.
    model_objects.append(make_box(
        "StrikeBikeArmor_Saddle", (0.34, 0.31, 0.085),
        (0.0, -0.48, 1.275), gunmetal, 0.035,
        (math.radians(-4), 0.0, 0.0)))
    for side in (-1, 1):
        side_name = "L" if side < 0 else "R"
        model_objects.append(modelkit.make_rod(
            "StrikeBikeFrame_RearShock_{0}".format(side_name),
            (side * 0.12, -0.72, 0.46), (side * 0.20, -0.39, 0.91),
            0.027, wheel, 8))

    # The headlamp lens sits in a dark, armored recess at the front of the fairing.
    model_objects.extend(modelkit.make_recessed_sensor_visor(
        "StrikeBikeSensor_Headlamp", (0.0, 0.90, 0.91), (0.28, 0.12, 0.15),
        armor, sensor, gunmetal))

    # Keep the existing bomb-bike cage and controller silhouette. The twin
    # external charge canisters make its hit-and-run role readable in profile.
    for side in (-1, 1):
        side_name = "L" if side < 0 else "R"
        model_objects.append(make_cylinder(
            "StrikeBikePayload_ExternalCharge_{0}".format(side_name),
            0.12, 0.60, (side * 0.43, -0.75, 1.08), warning,
            (math.radians(90), 0.0, 0.0), 10))
        for end_name, y in (("Front", -0.43), ("Rear", -1.07)):
            model_objects.append(make_cylinder(
                "StrikeBikePayload_ChargeCap_{0}_{1}".format(side_name, end_name),
                0.095, 0.055, (side * 0.43, y, 1.08), gunmetal,
                (math.radians(90), 0.0, 0.0), 10))
        model_objects.append(make_torus(
            "StrikeBikePayload_ChargeBand_{0}".format(side_name),
            (side * 0.43, -0.75, 1.08), 0.12, 0.018,
            sensor, (math.radians(90), 0.0, 0.0)))
        model_objects.append(make_rod(
            "StrikeBikePayload_RackRail_{0}".format(side_name),
            (side * 0.54, -1.02, 1.15), (side * 0.54, -0.48, 1.15),
            0.028, wheel, 8))
        model_objects.append(make_box(
            "StrikeBikeAccent_ChargeWarning_{0}".format(side_name),
            (0.03, 0.25, 0.055), (side * 0.57, -0.75, 1.08), sensor, 0.012))

    # Reinforced fork clamps and the short crash bar sharpen the fast strike
    # bike's front silhouette while staying within the native wheel footprint.
    for side in (-1, 1):
        side_name = "L" if side < 0 else "R"
        model_objects.append(make_box(
            "StrikeBikeFork_Clamp_{0}".format(side_name),
            (0.17, 0.12, 0.12), (side * 0.25, 0.57, 0.89), warning, 0.025,
            (math.radians(-12), 0.0, 0.0)))
    model_objects.append(make_box(
        "StrikeBikeArmor_FrontCrashBar", (0.70, 0.085, 0.095),
        (0.0, 0.98, 0.76), gunmetal, 0.025))

    model_objects.extend(apply_wave2_details(
        "SkirmishStrikeBike",
        objects=model_objects,
        object_prefix="StrikeBike",
        materials={"armor": armor, "gunmetal": gunmetal, "warning": warning, "tread": rubber},
    ))

    model_objects.extend(apply_wave34_details(
        "SkirmishStrikeBike",
        objects=model_objects,
        object_prefix="StrikeBike",
        materials={"armor": armor, "gunmetal": gunmetal, "warning": warning, "tread": rubber},
    ))

    fit_length_to_native_footprint(model_objects)
    modelkit.enforce_face_budget_before_export(model_objects, "SkirmishStrikeBike")
    diagnostics = modelkit.validate_model_mesh_quality(model_objects)
    if not diagnostics["ground_contact_valid"] or diagnostics["missing_materials"]:
        raise ValueError("Strike bike model failed ground-contact or material validation")
    bounds = diagnostics["bounds"]
    if bounds["width_x"] > 1.2 or bounds["height_z"] > 1.4 or bounds["length_y"] > 2.2:
        raise ValueError("Strike bike model exceeds the BombBike native footprint")

    add_preview_stage(model_objects)
    modelkit.bake_advanced_vertex_shading(
        model_objects,
        wear_level="field_tested",
        structural_wear_mask=True,
    )
    hardpoint_sockets = create_canonical_hardpoint_sockets("SkirmishStrikeBike")
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))
    bpy.ops.render.render(write_still=True)

    batched_objects = modelkit.merge_meshes_by_material(model_objects, "SkirmishStrikeBike", {
        armor: "armor", armor_light: "lightArmor", rubber: "rubber", wheel: "wheel",
        gunmetal: "gunmetal", warning: "warning", sensor: "arc"
    })
    modelkit.export_unity_fbx(FBX_PATH, batched_objects + hardpoint_sockets, include_empties=True, generate_profiled_lods=True, asset_id="SkirmishStrikeBike")
    print("Created source model: {0}".format(BLEND_PATH))
    print("Created Unity model: {0}".format(FBX_PATH))
    print("Created preview: {0}".format(PREVIEW_PATH))
    print("Editable source parts: {0}".format(len(model_objects)))
    print("Exported material batches: {0}".format(len(batched_objects)))


if __name__ == "__main__":
    main()
