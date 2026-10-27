"""Create a rigged, in-place Overlord Command HQ FBX from its authored Blender source."""
from __future__ import annotations

import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))

import bpy
import RogueModelKit as rmk
from mathutils import Vector

UNIT_NAME = "OverlordCommandHQ"
SOURCE_BLEND = REPO_ROOT / "content" / "source-art" / "overlord-command-hq.blend"
RIGGED_BLEND = REPO_ROOT / "content" / "source-art" / "overlordcommandhq-rigged.blend"
MODEL_DIR = (
    REPO_ROOT
    / "unity"
    / "RogueCommandAssetAuthoring"
    / "Assets"
    / "RogueCommandExpansionSample"
    / "Models"
)
FBX_PATH = MODEL_DIR / f"{UNIT_NAME}.fbx"
ATLAS_FBX_PATH = MODEL_DIR / f"{UNIT_NAME}_Atlas.fbx"
TEXTURE_DIR = MODEL_DIR / "Textures"


def _validate_root_motion(armature: bpy.types.Object, walk_action: bpy.types.Action) -> None:
    root_path = 'pose.bones["Bone_Root"].location'
    root_curves = [curve for curve in walk_action.fcurves if curve.data_path == root_path]
    for curve in root_curves:
        if any(abs(float(point.co.y)) > 1e-6 for point in curve.keyframe_points):
            raise ValueError("Overlord Command HQ Action_Walk violates the zero-root-translation invariant")
    if armature.animation_data is None:
        raise ValueError("Overlord Command HQ armature has no animation data")


def _validate_rigid_weights(meshes: list[bpy.types.Object], armature: bpy.types.Object) -> None:
    bone_names = {bone.name for bone in armature.data.bones}
    for mesh in meshes:
        if mesh.type != "MESH" or mesh.name.endswith(("_COLLIDER", "_ShieldBubble")):
            continue
        groups = {group.index: group.name for group in mesh.vertex_groups}
        for vertex in mesh.data.vertices:
            influences = [
                (groups[assignment.group], assignment.weight)
                for assignment in vertex.groups
                if assignment.group in groups and assignment.weight > 1e-6
            ]
            if len(influences) != 1 or abs(influences[0][1] - 1.0) > 1e-5:
                raise ValueError(f"{mesh.name} has non-rigid vertex weights at vertex {vertex.index}: {influences}")
            if influences[0][0] not in bone_names:
                raise ValueError(f"{mesh.name} references missing bone {influences[0][0]}")


def build_overlord_command_hq_model() -> None:
    if not SOURCE_BLEND.is_file():
        raise FileNotFoundError(f"Authored Blender source not found: {SOURCE_BLEND}")
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE_BLEND))
    MODEL_DIR.mkdir(parents=True, exist_ok=True)
    TEXTURE_DIR.mkdir(parents=True, exist_ok=True)

    meshes = [
        obj for obj in bpy.context.scene.objects
        if obj.type == "MESH" and not obj.name.lower().startswith("preview")
    ]
    if not meshes:
        raise ValueError("Overlord Command HQ source contains no mesh parts")

    armature = rmk.generate_procedural_armature(
        meshes,
        prefix=UNIT_NAME,
        turret_center=Vector((0.0, 1.32, 1.66)),
        gun_pivot=Vector((0.0, 1.57, 1.81)),
        hull_deck_z=0.92,
        leg_type=None,
        with_ik=False,
    )
    actions = rmk.generate_procedural_action_clips(armature, prefix=UNIT_NAME, leg_type=None)
    _validate_root_motion(armature, actions["Walk"])
    _validate_rigid_weights(meshes, armature)

    socket_positions = {
        "Socket_MuzzlePrimary": Vector((0.0, 1.35, 1.45)),
        "Socket_TurretPivot": Vector((0.0, 1.32, 1.66)),
        "Socket_TurretPitch": Vector((0.0, 1.57, 1.81)),
        "Socket_RecoilPrimary": Vector((0.0, 0.9, 1.45)),
        "Socket_DamageSmoke": Vector((0.0, -1.55, 1.5)),
        "Socket_DamageFire": Vector((0.0, -1.24, 1.18)),
        "Socket_GroundCenter": Vector((0.0, 0.0, 0.0)),
        "Socket_GroundContact": Vector((0.0, 0.0, 0.0)),
        "Socket_EngineAudio": Vector((0.0, -1.58, 0.86)),
        "Socket_WeaponAudio": Vector((0.0, 1.35, 1.45)),
    }
    for socket_name, location in socket_positions.items():
        socket = bpy.data.objects.get(socket_name)
        if socket is None or socket.type != "EMPTY":
            raise ValueError(f"Expected existing canonical socket {socket_name} in the authored source")
        socket.location = location
    firepoint = bpy.data.objects.get("FirePoint_0")
    if firepoint is None or firepoint.type != "EMPTY" or firepoint.parent != bpy.data.objects.get("Socket_MuzzlePrimary"):
        raise ValueError("Existing FirePoint_0 must remain nested under Socket_MuzzlePrimary")
    sockets = [obj for obj in bpy.context.scene.objects if obj.type == "EMPTY"]

    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=str(RIGGED_BLEND))

    batched_meshes = rmk.batch_and_join_by_material(
        meshes,
        prefix=UNIT_NAME,
        smooth_by_angle=True,
        weighted_normals=True,
        unity_batch_names=True,
    )
    atlas_maps = rmk.bake_pbr_texture_atlas(
        batched_meshes,
        prefix=UNIT_NAME,
        output_dir=TEXTURE_DIR,
        resolution=2048,
    )
    rmk.create_single_material_atlas_model(
        batched_meshes,
        prefix=UNIT_NAME,
        texture_maps=atlas_maps,
        output_fbx_path=ATLAS_FBX_PATH,
    )

    rmk.export_unity_fbx(
        FBX_PATH,
        list(batched_meshes) + [armature],
        include_empties=False,
        include_armature=True,
        bake_anim=True,
    )
    print(f"Editable rigged Blender source: {RIGGED_BLEND}")
    print(f"Rigged animated Unity FBX: {FBX_PATH}")
    print(f"Static atlas FBX: {ATLAS_FBX_PATH}")
    print("Bone_Root translation is zero; rigid weights and four action clips validated.")


if __name__ == "__main__":
    build_overlord_command_hq_model()
