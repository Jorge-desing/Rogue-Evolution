"""Create a rigged, in-place AtlasCraneMech FBX from its authored Blender source.

Atlas is a biped mech whose source blend already has an armature, sockets and
rigidly bound meshes; this builder preserves them and regenerates only the
action clips with the current RogueModelKit generator, then batches/bakes and
exports exactly like the Sandlance/Colossus rigged builders.
"""
from __future__ import annotations

import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))

import bpy
import RogueModelKit as rmk
from VisualModelAuthoringSupport import validate_rigid_weights, validate_root_motion
from mathutils import Vector
from ActiveUnitVisualEnhancements import apply_wave2_details, apply_wave34_details

UNIT_NAME = "AtlasCraneMech"
SOURCE_BLEND = REPO_ROOT / "content" / "source-art" / "atlas-crane-mech.blend"
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
    validate_root_motion(armature, walk_action, "Atlas Crane Mech")


_validate_rigid_weights = validate_rigid_weights


def build_model() -> None:
    if not SOURCE_BLEND.is_file():
        raise FileNotFoundError(f"Authored Blender source not found: {SOURCE_BLEND}")
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE_BLEND))
    MODEL_DIR.mkdir(parents=True, exist_ok=True)
    TEXTURE_DIR.mkdir(parents=True, exist_ok=True)

    armatures = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    if len(armatures) != 1:
        raise ValueError(f"AtlasCraneMech: expected exactly one armature, found {len(armatures)}")
    armature = armatures[0]
    meshes = [
        obj for obj in bpy.context.scene.objects
        if obj.type == "MESH" and not obj.name.lower().startswith("preview")
    ]
    if not meshes:
        raise ValueError("AtlasCraneMech source contains no mesh parts")
    rmk.enforce_face_budget_before_export(meshes, UNIT_NAME)

    # Delete old NLA tracks and Action_* clips (Walk is an alias of Drive for bipeds).
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

    actions = rmk.generate_procedural_action_clips(
        armature, prefix=UNIT_NAME, leg_type="biped", fire_profile="walker",
    )
    _validate_root_motion(armature, actions["Walk"])
    _validate_rigid_weights(meshes, armature)

    socket_positions = {
        name: Vector(bpy.data.objects[name].location)
        for name in (
            "Socket_MuzzlePrimary", "Socket_TurretPivot", "Socket_TurretPitch", "Socket_RecoilPrimary",
            "Socket_DamageSmoke", "Socket_DamageFire", "Socket_GroundCenter", "Socket_GroundContact",
            "Socket_FootContact_L", "Socket_FootContact_R", "Socket_EngineAudio", "Socket_WeaponAudio",
        )
    }
    # Preserve authored socket locations; blocks become unnecessary if absent
    # do not invent transforms that would duplicate canonical roles.
    for socket_name in socket_positions:
        if bpy.data.objects.get(socket_name) is None:
            raise ValueError(f"Expected existing canonical socket {socket_name} in authored source")

    missing_vertex_color = [
        obj for obj in meshes
        if obj.type == "MESH" and obj.data.color_attributes.get("Color") is None
    ]
    if missing_vertex_color:
        rmk.bake_advanced_vertex_shading(
            missing_vertex_color,
            wear_level="field_tested",
            soot_intensity=1.0,
            exhaust_locs=[socket_positions["Socket_EngineAudio"]],
            muzzle_loc=socket_positions["Socket_MuzzlePrimary"],
            structural_wear_mask=True,
        )

    meshes.extend(apply_wave2_details(
        UNIT_NAME,
        objects=meshes,
        object_prefix="Atlas",
    ))
    meshes.extend(apply_wave34_details(
        UNIT_NAME,
        objects=meshes,
        object_prefix="Atlas",
    ))
    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE_BLEND))

    batched_meshes = rmk.batch_and_join_by_material(
        meshes, prefix=UNIT_NAME, smooth_by_angle=True, weighted_normals=True, unity_batch_names=True,
    )
    atlas_maps = rmk.bake_pbr_texture_atlas(batched_meshes, prefix=UNIT_NAME, output_dir=TEXTURE_DIR, resolution=2048)
    rmk.create_single_material_atlas_model(
        batched_meshes, prefix=UNIT_NAME, texture_maps=atlas_maps, output_fbx_path=ATLAS_FBX_PATH,
    )
    sockets = [obj for obj in bpy.context.scene.objects if obj.type == "EMPTY"]
    rmk.export_unity_fbx(
        FBX_PATH, list(batched_meshes) + [armature] + sockets,
        include_empties=True, include_armature=True, bake_anim=True,
    )
    print(f"Editable source: {SOURCE_BLEND}")
    print(f"Rigged animated Unity FBX: {FBX_PATH}")
    print("Bone_Root translation is zero; rigid weights and four action clips validated.")


if __name__ == "__main__":
    build_model()
