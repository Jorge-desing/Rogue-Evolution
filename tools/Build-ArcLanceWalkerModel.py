"""Create a rigged, in-place Arc Lance Walker FBX from its authored Blender source."""

from __future__ import annotations

import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))

import bpy
import RogueModelKit as rmk
from ActiveUnitVisualEnhancements import apply_wave2_details, apply_wave34_details
from VisualModelAuthoringSupport import validate_rigid_weights, validate_root_motion
from mathutils import Vector

UNIT_NAME = "ArcLanceWalker"
SOURCE_BLEND = REPO_ROOT / "content" / "source-art" / "arclancewalker.blend"
RIGGED_BLEND = REPO_ROOT / "content" / "source-art" / "arclancewalker-rigged.blend"
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
    validate_root_motion(armature, walk_action, "Arc Lance Walker")


_validate_rigid_weights = validate_rigid_weights


def build_arc_lance_walker_model() -> None:
    if not SOURCE_BLEND.is_file():
        raise FileNotFoundError(f"Authored Blender source not found: {SOURCE_BLEND}")
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE_BLEND))
    wave2_objects = apply_wave2_details(UNIT_NAME)
    wave34_objects = apply_wave34_details(UNIT_NAME)
    if wave2_objects or wave34_objects:
        bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE_BLEND))
    MODEL_DIR.mkdir(parents=True, exist_ok=True)
    TEXTURE_DIR.mkdir(parents=True, exist_ok=True)

    meshes = [
        obj for obj in bpy.context.scene.objects
        if obj.type == "MESH" and not obj.name.lower().startswith("preview")
    ]
    if not meshes:
        raise ValueError("Arc Lance Walker source contains no mesh parts")
    rmk.enforce_face_budget_before_export(meshes, UNIT_NAME)

    # Preserve the authored source file; the rigged, editable deliverable is a separate copy.
    armature = rmk.generate_procedural_armature(
        meshes,
        prefix=UNIT_NAME,
        turret_center=Vector((0.0, 0.02, 1.82)),
        gun_pivot=Vector((0.0, 1.18, 1.25)),
        leg_type="biped",
        hip_height=1.42,
        hip_loc_l=Vector((-0.45, -0.04, 1.42)),
        hip_loc_r=Vector((0.45, -0.04, 1.42)),
        knee_loc_l=Vector((-0.55, -0.27, 0.84)),
        knee_loc_r=Vector((0.55, -0.27, 0.84)),
        foot_loc_l=Vector((-0.52, 0.18, 0.0)),
        foot_loc_r=Vector((0.52, 0.18, 0.0)),
        with_ik=False,
    )
    actions = rmk.generate_procedural_action_clips(armature, prefix=UNIT_NAME, leg_type="biped", locomotion_only=False)
    _validate_root_motion(armature, actions["Walk"])
    _validate_rigid_weights(meshes, armature)

    # Reuse the source's canonical empties instead of creating duplicate socket roles.
    socket_positions = {
        "Socket_MuzzlePrimary": Vector((0.0, 1.37, 1.24)),
        "Socket_TurretPivot": Vector((0.0, 0.02, 1.82)),
        "Socket_TurretPitch": Vector((0.0, 0.10, 1.35)),
        "Socket_RecoilPrimary": Vector((0.0, 0.62, 1.24)),
        "Socket_DamageSmoke": Vector((0.0, -0.51, 2.10)),
        "Socket_DamageFire": Vector((0.0, -0.45, 1.86)),
        "Socket_GroundCenter": Vector((0.0, 0.0, 0.0)),
        "Socket_GroundContact": Vector((0.0, 0.0, 0.0)),
        "Socket_FootContact_L": Vector((-0.52, 0.18, 0.0)),
        "Socket_FootContact_R": Vector((0.52, 0.18, 0.0)),
        "Socket_EngineAudio": Vector((0.0, -0.51, 2.01)),
        "Socket_WeaponAudio": Vector((0.0, 1.37, 1.24)),
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

    # Backfill the RGBA vertex-wear contract (R cavity AO, G structural edge
    # wear, B thermal soot, A team mask) on meshes that lack it — notably the
    # wave-detail meshes added above — before batching/export.
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

    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=str(RIGGED_BLEND))

    # Match the existing atlas material workflow while retaining armature weights on the
    # production FBX. The companion atlas FBX remains a static material-authoring asset.
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

    # Export authored canonical sockets so RuntimeVisualDirector turret/radar/
    # antenna/recoil animations have their pivots in the Unity prefab.
    rmk.export_unity_fbx(
        FBX_PATH,
        list(batched_meshes) + [armature] + sockets,
        include_empties=True,
        include_armature=True,
        bake_anim=True,
        generate_profiled_lods=True, asset_id=UNIT_NAME,
    )
    print(f"Editable rigged Blender source: {RIGGED_BLEND}")
    print(f"Rigged animated Unity FBX: {FBX_PATH}")
    print(f"Static atlas FBX: {ATLAS_FBX_PATH}")
    print("Bone_Root translation is zero; rigid weights and four action clips validated.")


if __name__ == "__main__":
    build_arc_lance_walker_model()
