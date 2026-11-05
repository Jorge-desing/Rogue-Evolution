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


def _ensure_middle_leg_bones(armature: bpy.types.Object) -> None:
    """Create ML/MR chains when the source carries their meshes but not bones."""
    if armature is None or armature.type != "ARMATURE":
        raise ValueError("Arachnid Hexapod source has no valid armature")

    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    try:
        edit_bones = armature.data.edit_bones
        hull = edit_bones.get("Bone_Hull")
        if hull is None:
            raise ValueError("Arachnid Hexapod armature is missing Bone_Hull")

        for tag, sign in (("ML", -1.0), ("MR", 1.0)):
            hip = Vector((sign * 1.152, 0.0, 0.95))
            knee = Vector((sign * 1.913, 0.0, 0.6463))
            foot = Vector((sign * 2.452, 0.0, 0.04))
            thigh = edit_bones.get(f"Bone_Thigh_{tag}") or edit_bones.new(f"Bone_Thigh_{tag}")
            thigh.head, thigh.tail, thigh.parent, thigh.use_deform = hip, knee, hull, True
            shin = edit_bones.get(f"Bone_Shin_{tag}") or edit_bones.new(f"Bone_Shin_{tag}")
            shin.head, shin.tail, shin.parent, shin.use_deform = (
                knee, Vector((foot.x, foot.y, foot.z + 0.12)), thigh, True
            )
            foot_bone = edit_bones.get(f"Bone_Foot_{tag}") or edit_bones.new(f"Bone_Foot_{tag}")
            foot_bone.head, foot_bone.tail, foot_bone.parent, foot_bone.use_deform = (
                Vector((foot.x, foot.y, foot.z + 0.12)), foot, shin, True
            )
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")


def _decouple_leg_chains_from_idle_hull(armature: bpy.types.Object) -> None:
    """Keep the six leg chains outside the hull's idle-breathing hierarchy."""
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    try:
        edit_bones = armature.data.edit_bones
        root = edit_bones.get("Bone_Root")
        if root is None:
            raise ValueError("Arachnid Hexapod armature is missing Bone_Root")
        for tag in ("FL", "FR", "ML", "MR", "RL", "RR"):
            thigh = edit_bones.get(f"Bone_Thigh_{tag}")
            if thigh is None:
                raise ValueError(f"Arachnid Hexapod armature is missing Bone_Thigh_{tag}")
            authored_matrix = thigh.matrix.copy()
            thigh.parent = root
            thigh.use_connect = False
            thigh.matrix = authored_matrix
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")


def _remove_leg_ik_constraints(armature: bpy.types.Object) -> None:
    """Leave authored six-leg gait clips in control while the unit is idle."""
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="POSE")
    try:
        for tag in ("FL", "FR", "ML", "MR", "RL", "RR"):
            shin = armature.pose.bones.get(f"Bone_Shin_{tag}")
            if shin is None:
                raise ValueError(f"Arachnid Hexapod armature is missing Bone_Shin_{tag}")
            for constraint in list(shin.constraints):
                if constraint.type == "IK":
                    shin.constraints.remove(constraint)
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")


def _normalise_leg_tag(name: str) -> str | None:
    for raw_tag, normalized in (
        ("_L_F_", "FL"), ("_R_F_", "FR"), ("_L_R_", "RL"), ("_R_R_", "RR"),
        ("_LF_", "FL"), ("_RF_", "FR"), ("_LR_", "RL"), ("_RR_", "RR"),
        ("_ML_", "ML"), ("_MR_", "MR"), ("_FL_", "FL"), ("_FR_", "FR"), ("_RL_", "RL"),
    ):
        if raw_tag in name or name.endswith(raw_tag[:-1]):
            return normalized
    return None


def _retarget_leg_attachments(meshes: list[bpy.types.Object]) -> None:
    """Bind loose leg hardware to its chain instead of animated Bone_Hull."""
    for mesh in meshes:
        if mesh.type != "MESH":
            continue
        name = mesh.name
        tag = _normalise_leg_tag(name)
        target_bone = None
        if name.startswith(f"{UNIT_NAME}CrawlerLeg_") and tag:
            if any(part in name for part in ("Foot", "Talon", "Ankle")):
                target_bone = f"Bone_Foot_{tag}"
            elif any(part in name for part in ("Tibia", "Knee", "Shin")):
                target_bone = f"Bone_Shin_{tag}"
            elif any(part in name for part in ("Femur", "Hip")):
                target_bone = f"Bone_Thigh_{tag}"
        elif name.startswith(f"{UNIT_NAME}Piston_Quad_") and tag:
            target_bone = f"Bone_Thigh_{tag}"
        elif name.startswith(f"{UNIT_NAME}HydraulicRam_") and tag:
            target_bone = f"Bone_Thigh_{tag}"
        elif name.startswith(f"{UNIT_NAME}HydraulicRod_") and tag:
            target_bone = f"Bone_Thigh_{tag}"
        if target_bone is None:
            continue
        for vertex_group in list(mesh.vertex_groups):
            mesh.vertex_groups.remove(vertex_group)
        vertex_group = mesh.vertex_groups.new(name=target_bone)
        vertex_indices = list(range(len(mesh.data.vertices)))
        if vertex_indices:
            vertex_group.add(vertex_indices, 1.0, "REPLACE")


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


def _clear_nla_tracks(armature: bpy.types.Object) -> None:
    """Prevent overlapping NLA strips from leaking Walk poses into exported Idle."""
    if armature.animation_data is None:
        return
    for track in list(armature.animation_data.nla_tracks):
        armature.animation_data.nla_tracks.remove(track)


def _author_idle_leg_rest_pose(armature: bpy.types.Object, idle_action: bpy.types.Action) -> None:
    """Key every leg chain at rest so export cannot retain the prior Walk/Death pose."""
    if armature.animation_data is None:
        raise ValueError("Arachnid Hexapod armature has no animation data")
    armature.animation_data.action = idle_action
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="POSE")
    try:
        for tag in ("FL", "FR", "ML", "MR", "RL", "RR"):
            for segment in ("Thigh", "Shin", "Foot"):
                bone_name = f"Bone_{segment}_{tag}"
                pose_bone = armature.pose.bones.get(bone_name)
                if pose_bone is None:
                    raise ValueError(f"Arachnid Hexapod armature is missing {bone_name}")
                pose_bone.location = (0.0, 0.0, 0.0)
                pose_bone.rotation_mode = "XYZ"
                pose_bone.rotation_euler = (0.0, 0.0, 0.0)
                for frame in (1, 30):
                    pose_bone.keyframe_insert(data_path="location", frame=frame, group=bone_name)
                    pose_bone.keyframe_insert(data_path="rotation_euler", frame=frame, group=bone_name)
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")
    armature.animation_data.action = idle_action
    bpy.context.view_layer.update()


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

    _ensure_middle_leg_bones(armature)
    _decouple_leg_chains_from_idle_hull(armature)
    _remove_leg_ik_constraints(armature)
    rmk.normalize_arachnid_middle_leg_orientation(armature)
    rmk.assign_rigid_mesh_weights(meshes, prefix=UNIT_NAME, armature_obj=armature, leg_type="quad")
    _retarget_leg_attachments(meshes)
    _validate_rigid_weights(meshes, armature)

    # Generate animation clips for the complete six-leg armature.
    actions = rmk.generate_procedural_action_clips(armature, prefix=UNIT_NAME, leg_type="quad")
    _clear_nla_tracks(armature)
    _author_idle_leg_rest_pose(armature, actions["Idle"])
    _validate_root_motion(armature, actions["Walk"])

    # Strip hull movement from Walk so ONLY legs animate (no body bob/pitch/roll).
    walk_act = actions["Walk"]
    for fc in list(walk_act.fcurves):
        if 'Bone_Hull' in fc.data_path:
            walk_act.fcurves.remove(fc)

    # Backfill the RGBA vertex-wear contract (R cavity AO, G structural edge
    # wear, B thermal soot, A team mask) on meshes that lack it before
    # batching/export.
    muzzle_socket = bpy.data.objects.get("Socket_MuzzlePrimary")
    engine_socket = bpy.data.objects.get("Socket_EngineAudio")
    missing_vertex_color = [
        obj for obj in meshes
        if obj.type == "MESH" and obj.data.color_attributes.get("Color") is None
    ]
    if missing_vertex_color:
        rmk.bake_advanced_vertex_shading(
            missing_vertex_color,
            wear_level="field_tested",
            soot_intensity=1.0,
            exhaust_locs=[Vector(engine_socket.location)] if engine_socket else None,
            muzzle_loc=Vector(muzzle_socket.location) if muzzle_socket else None,
            structural_wear_mask=True,
        )

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
    # Refresh the PBR atlas (Albedo, MetallicGloss, Normal, Emission) so the
    # Unity VehiclePBR_VertexWear import matches the rigged mesh.
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
    print("Arachnid Hexapod armature animated and exported.")


if __name__ == "__main__":
    build_arachnid_hexapod_rigged_model()
