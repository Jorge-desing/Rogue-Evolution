"""Create a rigged, in-place BastionSiegeMech FBX from its authored Blender source.

Bastion is a quadrupedal siege mech validated in Unity for FireRecoil 15
frames, Death 45 frames, Action_Walk with the Bone_Root in-place invariant and
single-bone 1.0 weights. This builder preserves the authored armature/meshes,
uses the generic generator with explicit frame overrides, then batches/bakes and
exports like the other rigged builders.
"""
from __future__ import annotations

import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))

import bpy
import RogueModelKit as rmk
from VisualModelAuthoringSupport import validate_rigid_weights, validate_root_motion
from CanonicalUnitHardpoints import create_canonical_hardpoint_sockets
from mathutils import Vector
from ActiveUnitVisualEnhancements import apply_wave2_details, apply_wave34_details

UNIT_NAME = "BastionSiegeMech"
SOURCE_BLEND = REPO_ROOT / "content" / "source-art" / "bastion-siege-mech-v2.blend"
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


def _validate_root_motion(armature, walk_action):
    validate_root_motion(armature, walk_action, "Bastion Siege Mech")


_validate_rigid_weights = validate_rigid_weights


def _remove_leg_ik_constraints(armature: bpy.types.Object) -> None:
    """Remove leg IK constraints so FK quad gait and neutral Idle rest poses control the limbs."""
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="POSE")
    try:
        for tag in ("FL", "FR", "RL", "RR"):
            shin = armature.pose.bones.get(f"Bone_Shin_{tag}")
            if shin is None:
                continue
            for constraint in list(shin.constraints):
                if constraint.type == "IK":
                    shin.constraints.remove(constraint)
        for pb in armature.pose.bones:
            pb.location = (0.0, 0.0, 0.0)
            pb.rotation_mode = "XYZ"
            pb.rotation_euler = (0.0, 0.0, 0.0)
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")


def _author_idle_leg_rest_pose(armature: bpy.types.Object, actions: dict[str, bpy.types.Action]) -> None:
    """Key every quadruped leg chain at rest in Idle, FireRecoil, and Death so Walk NLA tracks never bleed in."""
    if armature.animation_data is None:
        raise ValueError("Bastion Siege Mech armature has no animation data")
    targets = (
        ("Idle", (1, 30)),
        ("FireRecoil", (1, 15)),
        ("Death", (1, 45)),
    )
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="POSE")
    try:
        for action_key, frames in targets:
            action = actions.get(action_key)
            if action is None:
                continue
            armature.animation_data.action = action
            for tag in ("FL", "FR", "RL", "RR"):
                for segment in ("Thigh", "Shin", "Foot"):
                    bone_name = f"Bone_{segment}_{tag}"
                    pose_bone = armature.pose.bones.get(bone_name)
                    if pose_bone is None:
                        raise ValueError(f"Bastion Siege Mech armature is missing {bone_name}")
                    pose_bone.location = (0.0, 0.0, 0.0)
                    pose_bone.rotation_mode = "XYZ"
                    pose_bone.rotation_euler = (0.0, 0.0, 0.0)
                    for frame in frames:
                        pose_bone.keyframe_insert(data_path="location", frame=frame, group=bone_name)
                        pose_bone.keyframe_insert(data_path="rotation_euler", frame=frame, group=bone_name)
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")
    armature.animation_data.action = actions["Idle"]
    bpy.context.view_layer.update()


def build_model() -> None:
    if not SOURCE_BLEND.is_file():
        raise FileNotFoundError(f"Authored Blender source not found: {SOURCE_BLEND}")
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE_BLEND))
    armatures = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    if len(armatures) != 1:
        raise ValueError(f"BastionSiegeMech: expected exactly one armature, found {len(armatures)}")
    armature = armatures[0]
    meshes = [
        obj for obj in bpy.context.scene.objects
        if obj.type == "MESH" and not obj.name.lower().startswith("preview")
    ]
    if not meshes:
        raise ValueError("BastionSiegeMech source contains no mesh parts")
    rmk.enforce_face_budget_before_export(meshes, UNIT_NAME)
    MODEL_DIR.mkdir(parents=True, exist_ok=True)
    TEXTURE_DIR.mkdir(parents=True, exist_ok=True)

    if armature.animation_data:
        for track in list(armature.animation_data.nla_tracks):
            armature.animation_data.nla_tracks.remove(track)
        for act in list(bpy.data.actions):
            if "_Action_" in act.name or act.name.startswith(UNIT_NAME):
                try:
                    bpy.data.actions.remove(act)
                except ReferenceError:
                    pass
        if armature.animation_data.action is not None:
            try:
                bpy.data.actions.remove(armature.animation_data.action)
            except (ReferenceError, RuntimeError):
                pass

    _remove_leg_ik_constraints(armature)

    # Quadrupedal siege mech; FireRecoil 15 / Death 45 is the Unity rig contract.
    actions = rmk.generate_procedural_action_clips(
        armature, prefix=UNIT_NAME, leg_type="quad", fire_profile="heavy",
        fire_end_frame=15, death_end_frame=45,
    )
    _author_idle_leg_rest_pose(armature, actions)
    _validate_root_motion(armature, actions["Walk"])

    socket_positions = {
        name: Vector(bpy.data.objects[name].location)
        for name in (
            "Socket_MuzzlePrimary", "Socket_TurretPivot", "Socket_TurretPitch", "Socket_RecoilPrimary",
            "Socket_DamageSmoke", "Socket_DamageFire", "Socket_GroundCenter", "Socket_GroundContact",
            "Socket_FootContact_FL", "Socket_FootContact_FR", "Socket_FootContact_RL", "Socket_FootContact_RR",
            "Socket_EngineAudio", "Socket_WeaponAudio",
        )
    }
    for socket_name in socket_positions:
        if bpy.data.objects.get(socket_name) is None:
            raise ValueError(f"Expected existing canonical socket {socket_name} in authored source")

    from sample_native_walkers_v2_recipes import apply_bastion_siege_mech_v2
    apply_bastion_siege_mech_v2(UNIT_NAME, armature=armature)
    meshes = [
        obj for obj in bpy.context.scene.objects
        if obj.type == "MESH" and not obj.name.lower().startswith("preview")
    ]
    _validate_rigid_weights(meshes, armature)

    rmk.bake_advanced_vertex_shading(
        meshes,
        wear_level="field_tested",
        soot_intensity=1.0,
        exhaust_locs=[socket_positions["Socket_EngineAudio"]],
        muzzle_loc=socket_positions["Socket_MuzzlePrimary"],
        structural_wear_mask=True,
    )

    create_canonical_hardpoint_sockets(UNIT_NAME)
    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE_BLEND))

    batched_meshes = rmk.batch_and_join_by_material(
        meshes, prefix=UNIT_NAME, smooth_by_angle=True, weighted_normals=True, unity_batch_names=True,
    )
    atlas_maps = rmk.bake_pbr_texture_atlas(batched_meshes, prefix=UNIT_NAME, output_dir=TEXTURE_DIR, resolution=2048)
    rmk.create_single_material_atlas_model(
        batched_meshes, prefix=UNIT_NAME, texture_maps=atlas_maps, output_fbx_path=ATLAS_FBX_PATH,
    )
    create_canonical_hardpoint_sockets(UNIT_NAME)
    if armature.animation_data is not None:
        # Clear active action before FBX export so Idle/Death channels do not mask NLA Walk strips.
        armature.animation_data.action = None
    bpy.context.view_layer.update()
    sockets = [obj for obj in bpy.context.scene.objects if obj.type == "EMPTY"]
    rmk.export_unity_fbx(
        FBX_PATH, list(batched_meshes) + [armature] + sockets,
        include_empties=True, include_armature=True, bake_anim=True,
        generate_profiled_lods=True, asset_id=UNIT_NAME,
    )
    print(f"Editable source: {SOURCE_BLEND}")
    print(f"Rigged animated Unity FBX: {FBX_PATH}")
    print("Bone_Root translation is zero; rigid weights and four action clips validated.")


if __name__ == "__main__":
    build_model()
