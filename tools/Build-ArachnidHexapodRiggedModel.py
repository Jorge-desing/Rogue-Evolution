"""Add animations to the existing Arachnid Hexapod armature and export rigged FBX."""
from __future__ import annotations

import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))

import bpy
import RogueModelKit as rmk
from mathutils import Vector

UNIT_NAME = "ArachnidHexapod"
SOURCE_BLEND = REPO_ROOT / "content" / "source-art" / "arachnid-hexapod.blend"
RIGGED_BLEND = REPO_ROOT / "content" / "source-art" / "arachnidhexapod-rigged.blend"
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
            raise ValueError("Arachnid Hexapod Action_Walk violates the zero-root-translation invariant")
    if armature.animation_data is None:
        raise ValueError("Arachnid Hexapod armature has no animation data")


def build_arachnid_hexapod_rigged_model() -> None:
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
        raise ValueError("Arachnid Hexapod source contains no mesh parts")

    armature = bpy.data.objects.get("ArachnidHexapod_Armature")
    if armature is None or armature.type != "ARMATURE":
        raise ValueError("Arachnid Hexapod source has no armature")

    # Bind every mesh rigidly to its own leg bone before batching/export.
    rmk.assign_rigid_mesh_weights(meshes, UNIT_NAME, armature, leg_type="quad")

    # Generate animation clips for the existing armature
    actions = rmk.generate_procedural_action_clips(armature, prefix=UNIT_NAME, leg_type="quad")
    _validate_root_motion(armature, actions["Walk"])

    # Normalize the authored middle-leg (ML/MR) bone orientation so the shins
    # bend downward like the side legs instead of folding into the hull.
    rmk.normalize_arachnid_middle_leg_orientation(armature)
    bpy.context.view_layer.update()

    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=str(RIGGED_BLEND))

    # Export with armature and animations
    batched_meshes = rmk.batch_and_join_by_material(
        meshes,
        prefix=UNIT_NAME,
        smooth_by_angle=True,
        weighted_normals=True,
        unity_batch_names=True,
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
    print("Arachnid Hexapod armature animated and exported.")


if __name__ == "__main__":
    build_arachnid_hexapod_rigged_model()
