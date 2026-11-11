"""Build an original low-poly scout tank source model and Unity FBX asset."""

from pathlib import Path
import math
import sys

import bpy
from mathutils import Vector


REPO_ROOT = Path(__file__).resolve().parents[1]
BLEND_PATH = REPO_ROOT / "content" / "source-art" / "sandlance-scout.blend"
FBX_PATH = (REPO_ROOT / "unity" / "RogueCommandAssetAuthoring" / "Assets" /
            "RogueCommandExpansionSample" / "Models" / "SandlanceScout.fbx")
PREVIEW_PATH = REPO_ROOT / "artifacts" / "previews" / "sandlance-scout.png"
sys.path.insert(0, str(Path(__file__).resolve().parent))
import RogueModelKit as modelkit
from ActiveUnitVisualEnhancements import apply_wave2_details, apply_wave34_details
from CanonicalUnitHardpoints import create_canonical_hardpoint_sockets


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
        vertices=vertices, radius=radius, depth=direction.length,
        location=(start + end) * 0.5)
    obj = bpy.context.object
    obj.name = name
    obj.rotation_euler = direction.to_track_quat("Z", "Y").to_euler()
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish_mesh(obj, material, min(radius * 0.18, direction.length * 0.025))


def make_hull(material):
    """Create a continuous tapered hull with a rounded shoulder and sloped bow."""
    return modelkit.make_lofted_superellipse_shell(
        "ScoutArmor_Hull",
        [
            (-1.30, 0.0, 0.45, 0.50, 0.25),
            (-1.18, 0.0, 0.48, 0.62, 0.29),
            (-0.82, 0.0, 0.48, 0.66, 0.30),
            (0.52, 0.0, 0.48, 0.64, 0.29),
            (0.92, 0.0, 0.46, 0.58, 0.27),
            (1.16, 0.0, 0.40, 0.44, 0.19),
            (1.28, 0.0, 0.36, 0.09, 0.07),
        ],
        material,
        axis="Y",
        segments=28,
        exponent=0.78,
    )


def add_preview_stage(model_objects):
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0.0, 0.0, -0.015))
    floor = bpy.context.object
    floor.name = "Preview floor"
    floor.data.materials.append(make_material("Preview concrete", (0.035, 0.052, 0.065), 0.0, 0.92))

    bpy.ops.object.camera_add(location=(4.6, 5.8, 3.5))
    camera = bpy.context.object
    camera.name = "Preview camera"
    target = Vector((0.0, 0.0, 0.72))
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 5.7
    bpy.context.scene.camera = camera

    for name, location, energy, size in (
        ("Key", (3.5, 4.5, 6.0), 820, 4.0),
        ("Fill", (-4.0, 1.0, 3.5), 500, 3.5),
        ("Rim", (-1.5, -4.0, 4.5), 950, 3.0),
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

    armor = make_material("Sandlance Armor", (0.105, 0.185, 0.225), 0.42, 0.37)
    armor_light = make_material("Sandlance Light Armor", (0.24, 0.34, 0.36), 0.32, 0.4)
    tread = make_material("Sandlance Tread", (0.045, 0.055, 0.06), 0.18, 0.74)
    wheel = make_material("Sandlance Wheel Metal", (0.17, 0.23, 0.24), 0.6, 0.42)
    gunmetal = make_material("Sandlance Gunmetal", (0.075, 0.105, 0.12), 0.74, 0.3)
    amber = make_material("Sandlance Signal Amber", (0.95, 0.30, 0.055), 0.18, 0.36,
                          emission_color=(0.95, 0.30, 0.055), emission_strength=1.5)
    sensor = make_material("Sandlance Sensor Glass", (0.035, 0.72, 0.70), 0.2, 0.24,
                           emission_color=(0.035, 0.72, 0.70), emission_strength=1.8)

    model_objects = [make_hull(armor)]
    model_objects += [
        modelkit.make_lofted_superellipse_shell(
            "ScoutArmor_Deck",
            [(-0.78, 0.0, 0.82, 0.32, 0.07), (-0.62, 0.0, 0.83, 0.48, 0.11),
             (0.22, 0.0, 0.83, 0.46, 0.11), (0.43, 0.0, 0.79, 0.24, 0.07)],
            armor_light, axis="Y", segments=24, exponent=0.78),
        *[
            modelkit.make_lofted_superellipse_shell(
                "ScoutArmor_SidePod_{0}".format(side_name),
                [(-0.64, side * 0.71, 0.56, 0.07, 0.11),
                 (-0.50, side * 0.72, 0.56, 0.09, 0.15),
                 (0.48, side * 0.72, 0.56, 0.09, 0.15),
                 (0.62, side * 0.70, 0.54, 0.06, 0.12)],
                armor, axis="Y", segments=16, exponent=0.72)
            for side, side_name in ((-1, "L"), (1, "R"))
        ],
    ]

    for side in (-1, 1):
        model_objects.append(modelkit.make_continuous_track_belt(
            "ScoutTreads_Belt_{0}".format("L" if side < 0 else "R"),
            side * 0.91, 2.45, 0.62, 0.25, tread, ground_z=0.055))
        for index, y in enumerate((-0.89, -0.39, 0.15, 0.68), start=1):
            model_objects.append(make_cylinder(
                "ScoutWheel_Road_{0}_{1}".format("L" if side < 0 else "R", index),
                0.17, 0.095, (side * 1.035, y, 0.35), wheel,
                (0.0, math.radians(90), 0.0), 12))
        for index, y in enumerate((-1.10, 0.94), start=1):
            model_objects.append(make_cylinder(
                "ScoutWheel_Idler_{0}_{1}".format("L" if side < 0 else "R", index),
                0.145, 0.105, (side * 1.035, y, 0.35), gunmetal,
                (0.0, math.radians(90), 0.0), 12))
        model_objects.append(make_box(
            "ScoutAccent_IdentificationStripe_{0}".format("L" if side < 0 else "R"),
            (0.045, 0.88, 0.075), (side * 0.817, -0.02, 0.63), amber, 0.018))

    model_objects += [
        modelkit.make_lofted_superellipse_shell(
            "ScoutArmor_LowTurret",
            [(-0.72, 0.0, 0.94, 0.34, 0.10), (-0.58, 0.0, 0.97, 0.46, 0.15),
             (-0.18, 0.0, 0.97, 0.49, 0.16), (0.20, 0.0, 0.95, 0.41, 0.14),
             (0.34, 0.0, 0.92, 0.24, 0.10)],
            armor_light, axis="Y", segments=24, exponent=0.78),
        modelkit.make_lofted_superellipse_shell(
            "ScoutArmor_TurretNose",
            [(0.12, 0.0, 0.98, 0.27, 0.10), (0.28, 0.0, 0.98, 0.34, 0.12),
             (0.49, 0.0, 0.97, 0.28, 0.10)],
            armor, axis="Y", segments=20, exponent=0.76),
        make_cylinder("ScoutGun_Barrel_L", 0.055, 0.90, (-0.14, 0.78, 1.01), gunmetal,
                      (math.radians(90), 0.0, 0.0), 10),
        make_cylinder("ScoutGun_Barrel_R", 0.055, 0.90, (0.14, 0.78, 1.01), gunmetal,
                      (math.radians(90), 0.0, 0.0), 10),
        make_cylinder("ScoutSensor_Mast", 0.035, 0.43, (0.0, -0.64, 1.25), gunmetal, vertices=8),
        make_box("ScoutAccent_FrontLight_L", (0.12, 0.07, 0.06), (-0.37, 1.08, 0.66), sensor, 0.015),
        make_box("ScoutAccent_FrontLight_R", (0.12, 0.07, 0.06), (0.37, 1.08, 0.66), sensor, 0.015),
        make_box("ScoutAccent_TopPanel", (0.28, 0.34, 0.055), (0.0, -0.75, 0.93), amber, 0.025),
    ]

    # Recess the paired optics inside an armored visor so the sensor array reads
    # cleanly at RTS zoom instead of appearing as two flat lights on a slab.
    model_objects.extend(modelkit.make_recessed_sensor_visor(
        "ScoutSensor_Array", (0.0, -0.64, 1.49), (0.50, 0.12, 0.18),
        armor_light, sensor, gunmetal))

    # Individual shoes follow the upper and ground-contact runs of the closed
    # belt loop; two angled pads mark the sloped front and rear climbs.
    for side in (-1, 1):
        side_name = "L" if side < 0 else "R"
        for index, y in enumerate((-1.02, -0.74, -0.46, -0.18, 0.10, 0.38, 0.66, 0.94), start=1):
            model_objects.append(make_box(
                "ScoutTreads_ShoeTop_{0}_{1}".format(side_name, index),
                (0.22, 0.15, 0.075), (side * 0.91, y, 0.625), tread, 0.018))
            model_objects.append(make_box(
                "ScoutTreads_ShoeBottom_{0}_{1}".format(side_name, index),
                (0.22, 0.15, 0.075), (side * 0.91, y, 0.045), tread, 0.018))
        model_objects.append(make_box(
            "ScoutTreads_ShoeFrontClimb_{0}".format(side_name),
            (0.22, 0.15, 0.065), (side * 0.91, 1.00, 0.34), tread, 0.015,
            (math.radians(35), 0.0, 0.0)))
        model_objects.append(make_box(
            "ScoutTreads_ShoeRearClimb_{0}".format(side_name),
            (0.22, 0.15, 0.065), (side * 0.91, -1.00, 0.34), tread, 0.015,
            (math.radians(-35), 0.0, 0.0)))

    # Armored collars and a braced sensor mast add readable mechanical structure
    # without changing the original hull, turret, weapon or prefab references.
    for side in (-1, 1):
        side_name = "L" if side < 0 else "R"
        model_objects.append(make_cylinder(
            "ScoutGun_MuzzleCollar_{0}".format(side_name), 0.083, 0.13,
            (side * 0.14, 0.54, 1.01), armor_light,
            (math.radians(90), 0.0, 0.0), 10))
        model_objects.append(make_rod(
            "ScoutSensor_MastBrace_{0}".format(side_name),
            (side * 0.16, -0.64, 1.22), (side * 0.18, -0.64, 1.42),
            0.018, gunmetal, 6))
        model_objects.append(make_cylinder(
            "ScoutSensor_DorsalPod_{0}".format(side_name), 0.095, 0.055,
            (side * 0.45, -0.75, 0.93), sensor, vertices=10))
        model_objects.append(make_cylinder(
            "ScoutSensor_DorsalPodMount_{0}".format(side_name), 0.071, 0.045,
            (side * 0.45, -0.75, 0.91), gunmetal, vertices=8))
        model_objects.append(make_box(
            "ScoutAccent_RearMarker_{0}".format(side_name),
            (0.12, 0.16, 0.035), (side * 0.45, -0.56, 0.92), amber, 0.012))

    model_objects.extend(apply_wave2_details(
        "SandlanceScout",
        objects=model_objects,
        object_prefix="Scout",
        materials={"armor": armor, "gunmetal": gunmetal, "warning": amber, "tread": tread},
    ))

    model_objects.extend(apply_wave34_details(
        "SandlanceScout",
        objects=model_objects,
        object_prefix="Scout",
        materials={"armor": armor, "gunmetal": gunmetal, "warning": amber, "tread": tread},
    ))
    modelkit.enforce_face_budget_before_export(model_objects, "SandlanceScout")

    diagnostics = modelkit.validate_model_mesh_quality(model_objects)
    if not diagnostics["ground_contact_valid"] or diagnostics["missing_materials"]:
        raise ValueError("Scout model failed ground-contact or material validation")
    bounds = diagnostics["bounds"]
    if bounds["width_x"] > 2.4 or bounds["height_z"] > 1.6 or bounds["length_y"] > 2.8:
        raise ValueError("Scout model exceeds the PCXBaseTank native footprint")

    add_preview_stage(model_objects)
    modelkit.bake_advanced_vertex_shading(
        model_objects,
        wear_level="field_tested",
        structural_wear_mask=True,
    )
    hardpoint_sockets = create_canonical_hardpoint_sockets("SandlanceScout")
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))
    bpy.ops.render.render(write_still=True)

    batched_objects = modelkit.merge_meshes_by_material(model_objects, "SandlanceScout", {
        armor: "armor", armor_light: "lightArmor", tread: "tread", wheel: "wheel",
        gunmetal: "gunmetal", amber: "amber", sensor: "sensor"
    })
    modelkit.export_unity_fbx(FBX_PATH, batched_objects + hardpoint_sockets, include_empties=True, generate_profiled_lods=True, asset_id="SandlanceScout")
    print("Created source model: {0}".format(BLEND_PATH))
    print("Created Unity model: {0}".format(FBX_PATH))
    print("Created preview: {0}".format(PREVIEW_PATH))
    print("Editable source parts: {0}".format(len(model_objects)))
    print("Exported material batches: {0}".format(len(batched_objects)))


if __name__ == "__main__":
    main()
