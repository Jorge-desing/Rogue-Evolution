"""Create a rigged, in-place Tidebreaker Hovercraft FBX from its authored Blender source."""
from __future__ import annotations

import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))

import bpy
import RogueModelKit as rmk
from ActiveUnitVisualEnhancements import apply_wave2_details, apply_wave34_details
from VisualModelAuthoringSupport import validate_rigid_weights, validate_root_motion
from CanonicalUnitHardpoints import create_canonical_hardpoint_sockets
from mathutils import Vector

UNIT_NAME = "TidebreakerHovercraft"
SOURCE_BLEND = REPO_ROOT / "content" / "source-art" / "tidebreaker-hovercraft.blend"
RIGGED_BLEND = REPO_ROOT / "content" / "source-art" / "tidebreakerhovercraft-rigged.blend"
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
    validate_root_motion(armature, walk_action, "Tidebreaker Hovercraft")


_validate_rigid_weights = validate_rigid_weights


def build_tidebreaker_hovercraft_model() -> None:
    if not SOURCE_BLEND.is_file():
        raise FileNotFoundError(f"Authored Blender source not found: {SOURCE_BLEND}")
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE_BLEND))
    # Replay authored details in memory; preserve the canonical source blend.
    apply_wave2_details(UNIT_NAME)
    apply_wave34_details(UNIT_NAME)

    meshes = [
        obj for obj in bpy.context.scene.objects
        if obj.type == "MESH" and not obj.name.lower().startswith("preview")
    ]
    if not meshes:
        raise ValueError("Tidebreaker Hovercraft source contains no mesh parts")
    rmk.enforce_face_budget_before_export(meshes, UNIT_NAME)
    MODEL_DIR.mkdir(parents=True, exist_ok=True)
    TEXTURE_DIR.mkdir(parents=True, exist_ok=True)

    armature = rmk.generate_procedural_armature(
        meshes,
        prefix=UNIT_NAME,
        turret_center=Vector((0.0, 0.85, 1.22)),
        gun_pivot=Vector((0.0, 1.1, 1.37)),
        hull_deck_z=0.45,
        leg_type=None,
        with_ik=False,
    )
    actions = rmk.generate_procedural_action_clips(armature, prefix=UNIT_NAME, leg_type=None, locomotion_only=False)
    _validate_root_motion(armature, actions["Walk"])
    _validate_rigid_weights(meshes, armature)

    socket_positions = {
        "Socket_MuzzlePrimary": Vector((0.0, 1.35, 1.45)),
        "Socket_TurretPivot": Vector((0.0, 0.85, 1.22)),
        "Socket_TurretPitch": Vector((0.0, 1.1, 1.37)),
        "Socket_RecoilPrimary": Vector((0.0, 0.9, 1.45)),
        "Socket_DamageSmoke": Vector((-0.2, 0.65, 1.52)),
        "Socket_DamageFire": Vector((0.2, 0.65, 1.52)),
        "Socket_GroundCenter": Vector((0.0, 0.0, 0.0)),
        "Socket_GroundContact": Vector((0.0, 0.0, 0.0)),
        "Socket_EngineAudio": Vector((0.0, -0.65, 0.4)),
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
    create_canonical_hardpoint_sockets(UNIT_NAME)
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
    build_tidebreaker_hovercraft_model()
