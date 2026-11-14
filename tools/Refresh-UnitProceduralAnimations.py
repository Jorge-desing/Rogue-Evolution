"""Refresh Layer-1 procedural animations across all 18 rigged unit .blend and .fbx assets.

Preserves existing unit meshes, UVs, VertexWear RGBA attributes, sockets, and rigid bone
weights while regenerating Action_Idle, Action_Walk/Action_Drive, Action_FireRecoil, and
Action_Death with quadratic Bezier foot trajectories and root-safe F-Curve micro-vibration.
"""

from __future__ import annotations

import argparse
import gc
import pathlib
import shutil
import sys
import time

import bpy

REPO_ROOT = pathlib.Path(__file__).resolve().parents[1]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from tools import RogueModelKit as rmk


MODELS_DIR = (
    REPO_ROOT
    / "unity"
    / "RogueCommandAssetAuthoring"
    / "Assets"
    / "RogueCommandExpansionSample"
    / "Models"
)
SOURCE_ART_DIR = REPO_ROOT / "content" / "source-art"

# (unit_name, source_blend_filename, leg_type, fire_profile, extra_kwargs)
UNIT_SPECS: list[tuple[str, str, str | None, str, dict]] = [
    ("ArachnidHexapod", "arachnidhexapod-rigged.blend", "quad", "medium", {}),
    ("ArcLanceWalker", "arclancewalker-rigged.blend", "biped", "medium", {"locomotion_only": False}),
    ("AtlasCraneMech", "atlas-crane-mech.blend", "biped", "walker", {}),
    (
        "BastionSiegeMech",
        "bastion-siege-mech.blend",
        "quad",
        "heavy",
        {"fire_end_frame": 15, "death_end_frame": 45},
    ),
    ("BulwarkSiegeTank", "bulwarksiegetank-rigged.blend", None, "heavy", {"locomotion_only": False}),
    ("ColossusSiegePlatform", "colossussiegeplatform-rigged.blend", None, "heavy", {}),
    ("CommandRelay", "command-relay.blend", None, "medium", {}),
    ("FieldMedicRover", "fieldmedicrover-rigged.blend", None, "light", {"locomotion_only": False}),
    ("GravitasBreaker", "gravitas-breaker.blend", None, "heavy", {}),
    ("IroncladWalker", "ironclad-walker.blend", "biped", "walker", {}),
    ("OverlordCommandHQ", "overlordcommandhq-rigged.blend", None, "heavy", {}),
    ("SalvageHauler", "salvagehauler-rigged.blend", None, "heavy", {"locomotion_only": False}),
    ("SandlanceScout", "sandlancescout-rigged.blend", None, "light", {}),
    ("SentinelRelay", "sentinel-relay-v2.blend", None, "medium", {}),
    ("SkirmishStrikeBike", "skirmishstrikebike-rigged.blend", None, "light", {}),
    ("SkyguardSAM", "skyguardsam-rigged.blend", None, "sam", {"locomotion_only": False}),
    ("SurgeArcTank", "surgearctank-rigged.blend", None, "medium", {}),
    ("TidebreakerHovercraft", "tidebreakerhovercraft-rigged.blend", None, "light", {"locomotion_only": False, "hover": True}),
]


def _collect_export_objects(armature: bpy.types.Object) -> tuple[list[bpy.types.Object], list[bpy.types.Object]]:
    """Collect base render/collider meshes (excluding _LOD1/_LOD2 clones) and sockets."""
    meshes: list[bpy.types.Object] = []
    sockets: list[bpy.types.Object] = []
    for obj in bpy.data.objects:
        if obj == armature:
            continue
        if obj.type == "MESH":
            upper = obj.name.upper()
            if upper.endswith("_LOD1") or upper.endswith("_LOD2") or "_LOD1_" in upper or "_LOD2_" in upper:
                continue
            meshes.append(obj)
        elif obj.type == "EMPTY" and (
            obj.name.startswith("Socket_") or obj.name.startswith("FirePoint_")
        ):
            sockets.append(obj)
    return meshes, sockets


def _verify_bone_root_stationary(actions: dict[str, bpy.types.Action]) -> None:
    for action_name, action in actions.items():
        for fc in action.fcurves:
            if fc.data_path == 'pose.bones["Bone_Root"].location':
                for kp in fc.keyframe_points:
                    if abs(kp.co[1]) > 1e-5:
                        raise ValueError(
                            f"{action_name} translated Bone_Root ({kp.co[1]}), violating in-place invariant."
                        )


def refresh_unit(unit_name: str, blend_name: str, leg_type: str | None, fire_profile: str, extra_kwargs: dict) -> None:
    blend_path = SOURCE_ART_DIR / blend_name
    if not blend_path.is_file():
        raise FileNotFoundError(f"Missing source .blend for {unit_name}: {blend_path}")

    bpy.ops.wm.open_mainfile(filepath=str(blend_path))

    armature = next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)
    if armature is None:
        raise RuntimeError(f"No Armature object found in {blend_path}")

    # Clear existing actions and NLA tracks cleanly before regenerating Layer-1 procedural clips
    if armature.animation_data is not None:
        armature.animation_data.action = None
        for track in list(armature.animation_data.nla_tracks):
            armature.animation_data.nla_tracks.remove(track)
    for act in list(bpy.data.actions):
        bpy.data.actions.remove(act, do_unlink=True)

    actions = rmk.generate_procedural_action_clips(
        armature,
        prefix=unit_name,
        leg_type=leg_type,
        fire_profile=fire_profile,
        **extra_kwargs,
    )
    if unit_name == "ArachnidHexapod":
        idle_act = actions["Idle"]
        armature.animation_data.action = idle_act
        for tag in ("FL", "FR", "ML", "MR", "RL", "RR"):
            for part in ("Thigh", "Shin", "Foot"):
                bone_name = f"Bone_{part}_{tag}"
                pbone = armature.pose.bones.get(bone_name)
                if pbone is None:
                    continue
                pbone.rotation_mode = "XYZ"
                for frame in (1, 15, 30):
                    pbone.location = (0.0, 0.0, 0.0)
                    pbone.rotation_euler = (0.0, 0.0, 0.0)
                    pbone.keyframe_insert(data_path="location", frame=frame)
                    pbone.keyframe_insert(data_path="rotation_euler", frame=frame)
        walk_act = actions["Walk"]
        for fc in list(walk_act.fcurves):
            if "Bone_Hull" in fc.data_path:
                walk_act.fcurves.remove(fc)
        armature.animation_data.action = None
    _verify_bone_root_stationary(actions)

    meshes, sockets = _collect_export_objects(armature)
    if not meshes:
        raise RuntimeError(f"No exportable meshes found in {blend_path}")

    bpy.ops.wm.save_mainfile(filepath=str(blend_path))

    fbx_path = MODELS_DIR / f"{unit_name}.fbx"
    tmp_dir = REPO_ROOT / "artifacts" / "tmp-fbx-refresh" / f"{unit_name}_{int(time.time())}"
    tmp_dir.mkdir(parents=True, exist_ok=True)
    tmp_fbx = tmp_dir / f"{unit_name}.fbx"
    try:
        rmk.export_unity_fbx(
            tmp_fbx,
            meshes + [armature] + sockets,
            include_empties=True,
            include_armature=True,
            bake_anim=True,
            generate_profiled_lods=True,
            asset_id=unit_name,
            allow_uncalibrated_export=True,
            optimize_vertex_cache=True,
        )
        for generated in tmp_dir.glob(f"{unit_name}*.fbx"):
            dest = MODELS_DIR / generated.name
            shutil.move(str(generated), str(dest))
    finally:
        shutil.rmtree(tmp_dir, ignore_errors=True)
        gc.collect()
    print(f"[REFRESHED] {unit_name}: actions={len(actions)}, meshes={len(meshes)}, sockets={len(sockets)} -> {fbx_path.name}")


def main() -> None:
    parser = argparse.ArgumentParser(description="Refresh Layer-1 procedural animations across units.")
    parser.add_argument("--start-index", type=int, default=0, help="Start index in UNIT_SPECS.")
    parser.add_argument("--units", nargs="*", help="Optional explicit unit names to refresh.")
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    args = parser.parse_args(argv)

    specs = UNIT_SPECS[args.start_index :]
    if args.units:
        wanted = {u.strip() for u in args.units}
        specs = [s for s in specs if s[0] in wanted]

    for unit_name, blend_name, leg_type, fire_profile, extra_kwargs in specs:
        refresh_unit(unit_name, blend_name, leg_type, fire_profile, extra_kwargs)
    print(f"Successfully refreshed Layer-1 procedural animations for {len(specs)} units.")


if __name__ == "__main__":
    main()
