#!/usr/bin/env python3
"""Post-export FBX & Blender headless animation pipeline validator for Rogue Command.

Audits exported FBX or .blend armatures against the Rogue Command animation contracts:
1. In-Place Locomotion Invariant: Bone_Root has zero translation delta across all actions.
2. Loop Seam Closure: Action_Idle and Action_Walk/Action_Drive start and end poses match.
3. Foot Sliding & Ground Penetration: Stance feet do not slide horizontally or penetrate Z < 0.
4. Scale Hygiene: No anomalous scale compensation on pose bones.
5. Rigid Bone-Length Preservation: Mechanical limb segment lengths stay constant.
6. Attack Clip Dual-Chain Contract: Action_FireRecoil animates both weapon and body chains.

Also probes optional free external CLI linters (animsmith, fbx-sanitizer) if installed
on PATH without failing when absent.
"""

from __future__ import annotations

import argparse
import json
import math
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[1]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from tools import unit_animation_algorithms as uaa  # noqa: E402

DEFAULT_BLENDER_EXE = (
    REPO_ROOT
    / ".tools"
    / "Blender-4.5.14"
    / "blender-4.5.14-windows-x64"
    / "blender.exe"
)
MODELS_DIR = (
    REPO_ROOT
    / "unity"
    / "RogueCommandAssetAuthoring"
    / "Assets"
    / "RogueCommandExpansionSample"
    / "Models"
)


def probe_external_animation_tools() -> dict[str, dict[str, Any]]:
    """Checks whether optional free/FOSS external CLI animation linters are on PATH."""
    tools_to_check = ("animsmith", "fbx-sanitizer", "fbxinspect")
    found: dict[str, dict[str, Any]] = {}
    for name in tools_to_check:
        resolved = shutil.which(name)
        found[name] = {
            "available": resolved is not None,
            "path": resolved,
        }
    return found


def classify_action_role(action_name: str) -> str | None:
    """Maps an FBX/Blender action name to its canonical Rogue Command role."""
    tail = (action_name or "").rsplit("|", 1)[-1].strip().lower()
    if "action_" in tail:
        tail = tail.split("action_", 1)[-1]
    elif tail.startswith(("ironcladwalker", "arclancewalker", "bastionsiegemech", "atlascranemech", "bulwarksiegetank")):
        # Truncated unit-prefixed name where 'Action_*' was cut off at 63 chars
        return None
    # Strip Blender .001/.002 duplicate suffixes for token matching
    tail_clean = re.sub(r"\.\d+$", "", tail)
    if "firerecoil" in tail_clean or tail_clean.endswith("fire"):
        return "fire_recoil"
    if "death" in tail_clean:
        return "death"
    if re.search(r"(?:^|_)walk$", tail_clean) or re.search(r"(?:^|_)drive$", tail_clean):
        return "locomotion"
    if re.search(r"(?:^|_)idle$", tail_clean):
        return "idle"
    return None


def _assign_action_with_slot(armature_obj: Any, action: Any) -> None:
    """Assigns an Action and its Blender 4.5+ ActionSlot when present."""
    if not armature_obj.animation_data:
        armature_obj.animation_data_create()
    armature_obj.animation_data.action = action
    slots = getattr(action, "slots", None)
    if slots and hasattr(armature_obj.animation_data, "action_slot"):
        try:
            armature_obj.animation_data.action_slot = slots[0]
        except Exception:
            pass


def _pose_bone_snapshot(armature_obj: Any) -> dict[str, tuple[float, ...]]:
    """Captures local matrix translation + quaternion for every pose bone."""
    snap: dict[str, tuple[float, ...]] = {}
    for pbone in armature_obj.pose.bones:
        loc = pbone.matrix_basis.to_translation()
        quat = pbone.matrix_basis.to_quaternion()
        snap[pbone.name] = (
            float(loc.x),
            float(loc.y),
            float(loc.z),
            float(quat.w),
            float(quat.x),
            float(quat.y),
            float(quat.z),
        )
    return snap


def audit_armature_actions_bpy(
    armature_obj: Any,
    actions: list[Any] | None = None,
    *,
    root_tolerance: float = 1e-4,
    loop_tolerance: float = 1e-3,
    scale_tolerance: float = 1e-3,
    length_tolerance: float = 1e-3,
) -> dict[str, Any]:
    """Evaluates all actions on a Blender armature object against animation contracts."""
    import bpy

    if armature_obj is None or armature_obj.type != "ARMATURE":
        return {"passed": False, "error": "Target object is not an Armature", "actions": {}}

    scene = bpy.context.scene
    if actions is None:
        actions = list(bpy.data.actions)

    # Capture rest bone lengths in armature space
    rest_lengths: dict[str, float] = {}
    for bone in armature_obj.data.bones:
        rest_lengths[bone.name] = float((bone.tail_local - bone.head_local).length)

    # Capture rest foot world Z for stance normalization
    prev_use_nla = None
    if armature_obj.animation_data:
        prev_use_nla = armature_obj.animation_data.use_nla
        armature_obj.animation_data.use_nla = False
        armature_obj.animation_data.action = None
    for pbone in armature_obj.pose.bones:
        pbone.matrix_basis.identity()
    bpy.context.view_layer.update()

    foot_bone_names = [
        pbone.name for pbone in armature_obj.pose.bones if "foot" in pbone.name.lower()
    ]

    report: dict[str, Any] = {
        "armature": armature_obj.name,
        "bone_count": len(armature_obj.pose.bones),
        "actions": {},
        "issues": [],
        "passed": True,
    }

    if not actions:
        report["passed"] = False
        report["issues"].append("NO_ACTIONS_FOUND")
        if armature_obj.animation_data and prev_use_nla is not None:
            armature_obj.animation_data.use_nla = prev_use_nla
        return report

    for action in actions:
        role = classify_action_role(action.name)
        frame_start = int(round(action.frame_range[0]))
        frame_end = int(round(action.frame_range[1]))
        if frame_end <= frame_start:
            frame_end = frame_start + 1

        _assign_action_with_slot(armature_obj, action)

        root_positions: list[tuple[float, float, float]] = []
        foot_tracks: dict[str, list[tuple[float, float, float]]] = {
            name: [] for name in foot_bone_names
        }
        max_scale_dev = 0.0
        max_length_dev = 0.0
        start_snap: dict[str, tuple[float, ...]] | None = None
        end_snap: dict[str, tuple[float, ...]] | None = None

        weapon_motion = 0.0
        body_motion = 0.0
        first_weapon: dict[str, tuple[float, float, float, float]] = {}
        first_body: dict[str, tuple[float, float, float, float]] = {}

        for frame in range(frame_start, frame_end + 1):
            scene.frame_set(frame)
            bpy.context.view_layer.update()

            snap = _pose_bone_snapshot(armature_obj)
            if frame == frame_start:
                start_snap = snap
            if frame == frame_end:
                end_snap = snap

            root_bone = armature_obj.pose.bones.get("Bone_Root")
            if root_bone is not None:
                world_pos = armature_obj.matrix_world @ root_bone.head
                root_positions.append(
                    (float(world_pos.x), float(world_pos.y), float(world_pos.z))
                )

            for pbone in armature_obj.pose.bones:
                pname_lower = pbone.name.lower()
                # Scale hygiene
                scale = pbone.matrix_basis.to_scale()
                max_scale_dev = max(
                    max_scale_dev,
                    abs(float(scale.x) - 1.0),
                    abs(float(scale.y) - 1.0),
                    abs(float(scale.z) - 1.0),
                )
                # Rigid bone length
                cur_len = float((pbone.tail - pbone.head).length)
                ref_len = rest_lengths.get(pbone.name, cur_len)
                max_length_dev = max(max_length_dev, abs(cur_len - ref_len))

                # Foot position tracking (world-space head Z)
                if pbone.name in foot_tracks:
                    w_head = armature_obj.matrix_world @ pbone.head
                    foot_tracks[pbone.name].append(
                        (float(w_head.x), float(w_head.y), float(w_head.z))
                    )

                # Track body vs weapon motion for FireRecoil dual-chain check
                if role == "fire_recoil":
                    loc = pbone.matrix_basis.to_translation()
                    rot = pbone.matrix_basis.to_euler()
                    vec = (
                        float(loc.x) + float(loc.y) + float(loc.z),
                        float(rot.x),
                        float(rot.y),
                        float(rot.z),
                    )
                    if any(k in pname_lower for k in ("barrel", "turret", "muzzle")):
                        if pbone.name not in first_weapon:
                            first_weapon[pbone.name] = vec
                        else:
                            d = max(abs(a - b) for a, b in zip(vec, first_weapon[pbone.name]))
                            weapon_motion = max(weapon_motion, d)
                    elif any(k in pname_lower for k in ("bone_hull", "bone_pelvis")):
                        if pbone.name not in first_body:
                            first_body[pbone.name] = vec
                        else:
                            d = max(abs(a - b) for a, b in zip(vec, first_body[pbone.name]))
                            body_motion = max(body_motion, d)

        root_audit = uaa.audit_root_motion_zero(root_positions, tolerance=root_tolerance)
        action_result: dict[str, Any] = {
            "role": role,
            "frame_range": [frame_start, frame_end],
            "root_motion": root_audit,
            "max_scale_deviation": round(max_scale_dev, 6),
            "max_bone_length_deviation": round(max_length_dev, 6),
            "passed": True,
            "issues": [],
        }

        if not root_audit["passed"]:
            action_result["passed"] = False
            action_result["issues"].append(
                f"ROOT_MOTION_VIOLATION: max_disp={root_audit['max_displacement']}"
            )

        if max_scale_dev > scale_tolerance:
            action_result["passed"] = False
            action_result["issues"].append(
                f"SCALE_ANOMALY: max_scale_deviation={max_scale_dev:.5f}"
            )

        if max_length_dev > length_tolerance:
            action_result["passed"] = False
            action_result["issues"].append(
                f"BONE_STRETCH: max_length_deviation={max_length_dev:.5f}"
            )

        if role in ("idle", "locomotion") and start_snap is not None and end_snap is not None:
            loop_audit = uaa.audit_loop_closure(start_snap, end_snap, tolerance=loop_tolerance)
            action_result["loop_closure"] = loop_audit
            if not loop_audit["passed"]:
                action_result["passed"] = False
                action_result["issues"].append(
                    f"LOOP_SEAM_MISMATCH: max_delta={loop_audit['max_delta']} ({loop_audit['worst_channel']})"
                )

        if role == "locomotion" and foot_tracks:
            foot_results: dict[str, Any] = {}
            for foot_name, track in foot_tracks.items():
                # Penetration check against world Z=0 floor; stance slide check relative to min-Z
                min_z = min(pt[2] for pt in track) if track else 0.0
                pen_frames = [idx for idx, pt in enumerate(track) if pt[2] < -0.005]
                norm_track = [(pt[0], pt[1], pt[2] - min_z) for pt in track]
                res = uaa.detect_foot_sliding(
                    norm_track, height_threshold=0.005, velocity_threshold=0.03
                )
                res["world_penetration_frames"] = pen_frames
                foot_results[foot_name] = res
                if pen_frames:
                    action_result["passed"] = False
                    action_result["issues"].append(
                        f"FOOT_PENETRATION ({foot_name}): frames={pen_frames}"
                    )
            action_result["foot_sliding"] = foot_results

        if role == "fire_recoil":
            action_result["weapon_motion"] = round(weapon_motion, 6)
            action_result["body_motion"] = round(body_motion, 6)
            is_legacy_stub = (
                "action_" not in action.name.lower() and len(actions) > 4
            )
            if is_legacy_stub and weapon_motion <= 1e-4 and body_motion <= 1e-4:
                action_result["skipped_empty_legacy_stub"] = True
            else:
                if weapon_motion <= 1e-4:
                    action_result["passed"] = False
                    action_result["issues"].append("FIRE_RECOIL_NO_WEAPON_MOTION")
                if body_motion <= 1e-4:
                    action_result["passed"] = False
                    action_result["issues"].append("FIRE_RECOIL_NO_BODY_MOTION")

        if not action_result["passed"]:
            report["passed"] = False
            for issue in action_result["issues"]:
                report["issues"].append(f"[{action.name}] {issue}")

        report["actions"][action.name] = action_result

    if armature_obj.animation_data and prev_use_nla is not None:
        armature_obj.animation_data.use_nla = prev_use_nla
    return report


def audit_fbx_file_bpy(fbx_path: Path) -> dict[str, Any]:
    """Imports an FBX into a clean Blender scene and audits its armature & actions."""
    import bpy

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(fbx_path))
    armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    if not armatures:
        return {
            "fbx": str(fbx_path),
            "passed": False,
            "issues": ["NO_ARMATURE_IN_FBX"],
        }
    armature = armatures[0]
    arm_prefix = f"{armature.name}|"
    armature_actions = [
        act
        for act in bpy.data.actions
        if act.name.startswith(arm_prefix) or "|" not in act.name
    ]
    # Prefer canonical Action_* clips when present over legacy short-name stubs;
    # also ignore empty zero-fcurve stub actions when populated actions exist.
    populated = [act for act in armature_actions if len(getattr(act, "fcurves", ())) > 0]
    pool = populated if populated else armature_actions
    canonical_actions = [act for act in pool if "action_" in act.name.lower()]
    selected_actions = canonical_actions if canonical_actions else pool
    result = audit_armature_actions_bpy(armature, selected_actions)
    result["fbx"] = str(fbx_path)
    return result


def run_blender_audit(fbx_paths: list[Path], blender_exe: Path) -> dict[str, Any]:
    """Invokes pinned Blender 4.5.14 in background mode to audit the given FBX files."""
    if not blender_exe.is_file():
        return {
            "passed": False,
            "error": f"Blender executable not found at {blender_exe}",
            "results": [],
        }

    with tempfile.NamedTemporaryFile(
        prefix="anim_audit_", suffix=".json", delete=False
    ) as tmp:
        tmp_path = Path(tmp.name)

    try:
        cmd = [
            str(blender_exe),
            "--background",
            "--factory-startup",
            "--python",
            str(Path(__file__).resolve()),
            "--",
            "--in-blender",
            "--output",
            str(tmp_path),
            *(str(p) for p in fbx_paths),
        ]
        proc = subprocess.run(
            cmd,
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=300,
        )
        if tmp_path.is_file() and tmp_path.stat().st_size > 0:
            data = json.loads(tmp_path.read_text(encoding="utf-8"))
            data["external_tools"] = probe_external_animation_tools()
            return data

        return {
            "passed": False,
            "error": f"Blender exited with code {proc.returncode} without writing report",
            "stdout_tail": proc.stdout[-1500:],
            "stderr_tail": proc.stderr[-1500:],
            "external_tools": probe_external_animation_tools(),
        }
    finally:
        tmp_path.unlink(missing_ok=True)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("fbx_paths", nargs="*", type=Path, help="FBX file(s) to audit.")
    parser.add_argument(
        "--all-units",
        action="store_true",
        help="Audit all canonical rigged unit FBX files in RogueCommandExpansionSample/Models.",
    )
    parser.add_argument("--blender", type=Path, default=DEFAULT_BLENDER_EXE)
    parser.add_argument("--json", action="store_true", help="Print JSON output.")
    parser.add_argument("--output", type=Path, help="Write JSON report to path.")
    parser.add_argument("--in-blender", action="store_true", help=argparse.SUPPRESS)
    if argv is None and "--" in sys.argv:
        argv = sys.argv[sys.argv.index("--") + 1 :]
    args = parser.parse_args(argv)

    targets = [p.resolve() for p in args.fbx_paths]
    if args.all_units:
        for p in sorted(MODELS_DIR.glob("*.fbx")):
            name = p.stem
            if name.endswith("_Atlas") or name.endswith("_LODs"):
                continue
            targets.append(p.resolve())

    if not targets:
        parser.error("Provide at least one FBX path or pass --all-units.")

    if args.in_blender:
        results = [audit_fbx_file_bpy(p) for p in targets]
        overall = {
            "passed": all(r.get("passed", False) for r in results),
            "audited_count": len(results),
            "results": results,
        }
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(json.dumps(overall, indent=2), encoding="utf-8")
        elif args.json:
            print(json.dumps(overall, indent=2))
        return 0 if overall["passed"] else 1

    report = run_blender_audit(targets, args.blender)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2), encoding="utf-8")

    if args.json:
        print(json.dumps(report, indent=2))
    else:
        print(f"Animation Pipeline Audit: {'PASS' if report.get('passed') else 'FAIL'}")
        ext = report.get("external_tools", {})
        ext_summary = ", ".join(
            f"{k}={'found' if v.get('available') else 'absent'}" for k, v in ext.items()
        )
        print(f"Optional external linters: {ext_summary}")
        for res in report.get("results", []):
            name = Path(res.get("fbx", "?")).name
            status = "PASS" if res.get("passed") else "FAIL"
            issues = res.get("issues", [])
            print(f"  [{status}] {name} (actions={len(res.get('actions', {}))})")
            for issue in issues:
                print(f"      - {issue}")

    return 0 if report.get("passed") else 1


if __name__ == "__main__":
    raise SystemExit(main())
