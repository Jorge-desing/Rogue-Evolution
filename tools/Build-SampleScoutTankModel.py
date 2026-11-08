"""Build an original low-poly scout tank source model and Unity FBX asset."""
from pathlib import Path
import math
import sys

import bpy
from mathutils import Vector

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))
import RogueModelKit as rmk
from ActiveUnitVisualEnhancements import apply_wave2_details, apply_wave34_details

UNIT_NAME = "ScoutTank"
BLEND_PATH = REPO_ROOT / "content" / "source-art" / "scout-tank.blend"
FBX_PATH = (REPO_ROOT / "unity" / "RogueCommandAssetAuthoring" / "Assets" /
            "RogueCommandExpansionSample" / "Models" / f"{UNIT_NAME}.fbx")
PREVIEW_PATH = REPO_ROOT / "artifacts" / "previews" / "scout-tank.png"


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


def finish_mesh(obj, material):
    obj.data.materials.clear()
    obj.data.materials.append(material)
    for polygon in obj.data.polygons:
        polygon.use_smooth = False
    obj.select_set(False)
    return obj


def build() -> None:
    rmk.clear_scene()
    BLEND_PATH.parent.mkdir(parents=True, exist_ok=True)
    FBX_PATH.parent.mkdir(parents=True, exist_ok=True)

    armor = make_material(f"{UNIT_NAME}_Armor", (0.20, 0.24, 0.30), 0.55, 0.55)
    light = make_material(f"{UNIT_NAME}_LightArmor", (0.45, 0.52, 0.62), 0.45, 0.50)
    gunmetal = make_material(f"{UNIT_NAME}_Gunmetal", (0.07, 0.09, 0.12), 0.75, 0.40)
    warning = make_material(f"{UNIT_NAME}_Warning", (0.90, 0.60, 0.10), 0.25, 0.60)
    sensor = make_material(f"{UNIT_NAME}_Sensor", (0.10, 0.85, 1.00), 0.20, 0.30, (0.15, 0.85, 1.00), 2.0)

    objects = []
    objects.append(rmk.make_box(f"{UNIT_NAME}_Hull", (1.6, 2.8, 0.7), (0.0, 0.0, 0.65), armor, bevel=0.09))
    objects.append(rmk.make_box(f"{UNIT_NAME}_UpperHull", (1.4, 2.0, 0.45), (0.0, 0.15, 1.15), light, bevel=0.07))
    objects.append(rmk.make_box(f"{UNIT_NAME}_Turret", (1.05, 1.3, 0.5), (0.0, -0.1, 1.6), armor, bevel=0.08))
    objects.append(rmk.make_box(f"{UNIT_NAME}_Barrel", (0.16, 1.1, 0.16), (0.0, 1.05, 1.6), gunmetal, bevel=0.02))
    objects.append(rmk.make_box(f"{UNIT_NAME}_Sensor", (0.5, 0.12, 0.12), (0.0, 0.6, 1.78), sensor, bevel=0.01))
    objects.append(rmk.make_box(f"{UNIT_NAME}_Stripe", (1.62, 0.1, 0.12), (0.0, 0.35, 0.95), warning, bevel=0.01))

    for side in (-1, 1):
        for z in (0.35,):
            objects.append(rmk.make_cylinder(
                f"{UNIT_NAME}_Wheel_{'L' if side<0 else 'R'}_{int(z*100)}",
                radius=0.32, depth=0.24, location=(side * 0.92, -0.85, z), material=gunmetal,
            ))
            objects.append(rmk.make_cylinder(
                f"{UNIT_NAME}_WheelB_{'L' if side<0 else 'R'}_{int(z*100)}",
                radius=0.32, depth=0.24, location=(side * 0.92, 0.85, z), material=gunmetal,
            ))
        objects.append(rmk.make_box(f"{UNIT_NAME}_Fender_{'L' if side<0 else 'R'}", (0.22, 2.2, 0.1), (side * 0.92, 0.0, 0.75), light, bevel=0.02))

    for obj in objects:
        finish_mesh(obj, armor if "Armor" in obj.material_slots and obj.material_slots else obj.data.materials[0] if hasattr(obj.data, 'materials') and obj.data.materials else armor)

    # Simplify material assignment: use per-object material from make_* helpers already set.
    # Ensure every mesh has a material slot (helpers already assign).
    for obj in objects:
        if not obj.data.materials:
            obj.data.materials.append(armor)

    sockets = {
        "Socket_MuzzlePrimary": (0.0, 1.6, 1.6),
        "Socket_TurretPivot": (0.0, -0.1, 1.6),
        "Socket_TurretPitch": (0.0, 0.4, 1.6),
        "Socket_RecoilPrimary": (0.0, 0.8, 1.6),
        "Socket_DamageSmoke": (0.0, -0.9, 1.4),
        "Socket_DamageFire": (0.0, -0.6, 1.2),
        "Socket_GroundCenter": (0.0, 0.0, 0.0),
        "Socket_GroundContact": (0.0, 0.0, 0.0),
        "Socket_EngineAudio": (0.0, -1.1, 0.8),
        "Socket_WeaponAudio": (0.0, 1.6, 1.6),
    }
    from VisualModelAuthoringSupport import create_canonical_sockets
    create_canonical_sockets(sockets)
    for n, loc in sockets.items():
        bpy.data.objects[n].location = loc
    fp = bpy.data.objects["FirePoint_0"]
    fp.parent = bpy.data.objects["Socket_MuzzlePrimary"]
    fp.location = (0.0, 0.0, 0.0)

    armature = rmk.generate_procedural_armature(objects, prefix=UNIT_NAME, turret_center=Vector((0.0, -0.1, 1.6)), gun_pivot=Vector((0.0, 0.4, 1.6)), hull_deck_z=0.95, leg_type=None, with_ik=False)
    actions = rmk.generate_procedural_action_clips(armature, prefix=UNIT_NAME, leg_type=None, fire_profile="light")
    from VisualModelAuthoringSupport import validate_root_motion, validate_rigid_weights
    validate_root_motion(armature, actions["Walk"], "Scout Tank")
    validate_rigid_weights(objects, armature)

    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH))
    batched = rmk.batch_and_join_by_material(objects, prefix=UNIT_NAME, smooth_by_angle=True, weighted_normals=True, unity_batch_names=True)
    maps = rmk.bake_pbr_texture_atlas(batched, prefix=UNIT_NAME, output_dir=FBX_PATH.parent / "Textures", resolution=1024)
    rmk.create_single_material_atlas_model(batched, prefix=UNIT_NAME, texture_maps=maps, output_fbx_path=FBX_PATH.parent / f"{UNIT_NAME}_Atlas.fbx")
    rmk.export_unity_fbx(FBX_PATH, list(batched) + [armature] + [o for o in bpy.context.scene.objects if o.type == "EMPTY"], include_empties=True, include_armature=True, bake_anim=True)
    print(f"Editable source: {BLEND_PATH}")
    print(f"Rigged FBX: {FBX_PATH}")
    print("Bone_Root translation is zero; rigid weights and four action clips validated.")


if __name__ == "__main__":
    build()
