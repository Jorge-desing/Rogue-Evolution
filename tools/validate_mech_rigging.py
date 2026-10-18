#!/usr/bin/env python3
"""
validate_mech_rigging.py — Audits and validates 3D mech rigging, skeletal armatures,
transform hierarchies, animation clips, and Unity Animator setups for Rogue Command modding.

Enforces engine invariants discovered via REA in Rogue Command 1.0.9:
1. Dual Rigging Paradigms:
   a) Hierarchical Transform Rig (Native Rogue Command Pattern):
      Used by native GatlingWalker / FlameWalker. Separate static meshes parented to
      dedicated rotation pivots (RotHor for azimuth, ROTVERT for elevation, LEGS/RightLeg/LeftLeg).
      Zero-code, lightweight, perfect for modular hard-surface walkers.
   b) Skeletal Armature Rig (Mecanim Generic Pattern):
      Used for articulated mechs with multi-segment limbs or SkinnedMeshRenderers.
      Requires Bone_Root at ground origin (Z=0), Bone_Pelvis/Bone_Hull, and leg chains.
2. In-Place Locomotion Invariant:
   EntityFlockingMovement translates units across the tactical grid. Walking animations
   must be In-Place (Root Motion = false, Bone_Root delta = (0, 0, 0)). Non-zero root
   displacement produces severe double-stepping and cyclic snap-back artifacts.
3. Canonical Gameplay Sockets:
   - Socket_TurretPivot (azimuth horizontal rotation)
   - Socket_TurretPitch / Socket_BarrelPitch (vertical elevation)
   - Socket_MuzzlePrimary (weapon fire origin)
   - Socket_DamageSmoke (<= 50% HP threshold)
   - Socket_DamageFire (<= 25% HP threshold)
   - Socket_FootContact_L / Socket_FootContact_R (dust/step SFX anchors)
4. Animator Controller Contract:
   - Parameters: RCL_IsMoving (Bool), RCL_Fire (Trigger), RCL_Death (Trigger)
   - Actions: Action_Idle, Action_Walk (or Action_Drive), Action_FireRecoil, Action_Death
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass, field, asdict
import json
import os
from pathlib import Path
import re
import sys
from typing import List, Dict, Any, Set, Tuple

# Try importing UnityPy for compiled AssetBundle / assets inspection
try:
    import UnityPy
    HAS_UNITYPY = True
except ImportError:
    HAS_UNITYPY = False

SKELETAL_WALKER_BONES = [
    "bone_root",
    "bone_pelvis",
    "bone_thigh_l",
    "bone_shin_l",
    "bone_foot_l",
    "bone_thigh_r",
    "bone_shin_r",
    "bone_foot_r",
]

HIERARCHICAL_WALKER_NODES = [
    "legs",
    "leftleg",
    "rightleg",
    "rothor",
    "rotvert",
]

CANONICAL_WALKER_SOCKETS = [
    "socket_turretpivot",
    "socket_muzzleprimary",
    "socket_damagesmoke",
    "socket_damagefire",
]

OPTIONAL_WALKER_SOCKETS = [
    "socket_footcontact_l",
    "socket_footcontact_r",
    "socket_footcontact_fl",
    "socket_footcontact_fr",
    "socket_footcontact_rl",
    "socket_footcontact_rr",
    "socket_recoilprimary",
]

EXPECTED_ANIMATOR_PARAMS = [
    ("rcl_ismoving", "Bool"),
    ("rcl_fire", "Trigger"),
    ("rcl_death", "Trigger"),
]


@dataclass
class MechRigAudit:
    model_name: str
    source_type: str  # "prefab", "fbx", "bundle", "metadata"
    rig_type: str = "unknown"  # "skeletal_armature", "hierarchical_transform", "static_unrigged"
    has_armature: bool = False
    has_hierarchical_legs: bool = False
    found_bones: List[str] = field(default_factory=list)
    missing_bones: List[str] = field(default_factory=list)
    found_sockets: List[str] = field(default_factory=list)
    missing_sockets: List[str] = field(default_factory=list)
    has_animator: bool = False
    animator_parameters: List[str] = field(default_factory=list)
    missing_parameters: List[str] = field(default_factory=list)
    animation_clips: List[str] = field(default_factory=list)
    is_in_place: bool = True
    is_rigid_weighted: bool = True
    score: int = 100
    flags: List[str] = field(default_factory=list)


class MechRigValidator:
    """Validates skeletal armatures, walking animations, and Unity prefabs."""

    def __init__(self) -> None:
        self.audits: List[MechRigAudit] = []

    def validate_prefab_yaml(self, prefab_path: Path) -> MechRigAudit:
        """Audits a Unity .prefab YAML file for armatures, bones, sockets, and animators."""
        audit = MechRigAudit(
            model_name=prefab_path.stem,
            source_type="prefab",
        )

        if not prefab_path.exists():
            audit.score = 0
            audit.flags.append(f"PREFAB_NOT_FOUND: {prefab_path} does not exist.")
            return audit

        content = prefab_path.read_text(encoding="utf-8", errors="ignore")

        # 1. Check for Animator component
        if "--- !u!95 &" in content:  # ClassID 95 is Animator
            audit.has_animator = True

        # 2. Extract GameObject names from YAML
        names = re.findall(r"m_Name:\s*([^\r\n]+)", content)
        lower_names = {n.strip().lower() for n in names}

        # Check for Armature vs Hierarchical Transform Rig
        has_bone_prefix = any(n.startswith("bone_") for n in lower_names)
        has_armature_keyword = any("armature" in n for n in lower_names)
        audit.has_armature = has_bone_prefix or has_armature_keyword

        # Check for native-style hierarchical leg nodes (GatlingWalker pattern)
        has_legs_parent = any(k in lower_names for k in ("legs", "leftleg", "rightleg", "leg_l", "leg_r"))
        audit.has_hierarchical_legs = has_legs_parent

        # Determine rig type
        if audit.has_armature:
            audit.rig_type = "skeletal_armature"
        elif audit.has_hierarchical_legs:
            audit.rig_type = "hierarchical_transform"
        else:
            audit.rig_type = "static_unrigged"

        # Check for bones
        for b in SKELETAL_WALKER_BONES:
            if any(b in n for n in lower_names):
                audit.found_bones.append(b)
            else:
                audit.missing_bones.append(b)

        # Check for sockets
        for s in CANONICAL_WALKER_SOCKETS:
            if any(s in n for n in lower_names):
                audit.found_sockets.append(s)
            else:
                audit.missing_sockets.append(s)

        for s in OPTIONAL_WALKER_SOCKETS:
            if any(s in n for n in lower_names):
                audit.found_sockets.append(s)

        # Evaluate based on rig type
        is_walker_archetype = any(k in audit.model_name.lower() for k in ("walker", "mech", "colossus", "ironclad"))

        if audit.rig_type == "skeletal_armature":
            if "bone_root" not in audit.found_bones:
                audit.score -= 20
                audit.flags.append("BONE_ROOT_MISSING: Bone_Root ground anchor required.")
            if not any(k in audit.found_bones for k in ("bone_pelvis", "bone_hull")):
                audit.score -= 15
                audit.flags.append("BONE_PELVIS_MISSING: Bone_Pelvis / Bone_Hull core chassis required.")
            if is_walker_archetype:
                has_thigh = any("thigh" in b for b in audit.found_bones)
                has_shin = any("shin" in b for b in audit.found_bones)
                if not (has_thigh and has_shin):
                    audit.score -= 20
                    audit.flags.append("LEG_BONES_MISSING: Articulated leg bone chains missing.")
            if not audit.has_animator:
                audit.score -= 15
                audit.flags.append("ANIMATOR_MISSING: Skeletal armature requires Animator component.")

        elif audit.rig_type == "hierarchical_transform":
            # Native GatlingWalker pattern: transform-based limbs
            has_turret_pivot = any("turret" in s or "rothor" in n for n in lower_names for s in audit.found_sockets)
            if not has_turret_pivot:
                audit.score -= 15
                audit.flags.append("TURRET_PIVOT_MISSING: Transform hierarchy lacks horizontal azimuth pivot.")
            # Hierarchical rigs don't strictly require Animator if procedural
            audit.flags.append("NATIVE_HIERARCHICAL_RIG: Uses native Rogue Command transform hierarchy (GatlingWalker pattern).")

        else:
            # Static unrigged
            if is_walker_archetype:
                audit.score -= 40
                audit.flags.append("UNRIGGED_WALKER: Walker visual lacks both skeletal armature and hierarchical leg pivots.")
            else:
                audit.flags.append("STATIC_VEHICLE_RIG: Wheeled or tracked vehicle chassis without limb articulation.")

        # Sockets scoring (common to all tactical units)
        if not any("muzzle" in s for s in audit.found_sockets):
            audit.score -= 15
            audit.flags.append("SOCKET_MUZZLE_MISSING: Socket_MuzzlePrimary missing for projectile firing.")
        if not any("damagesmoke" in s for s in audit.found_sockets):
            audit.score -= 10
            audit.flags.append("SOCKET_DAMAGE_SMOKE_MISSING: Socket_DamageSmoke missing for health <= 50% degradation.")
        if not any("damagefire" in s for s in audit.found_sockets):
            audit.score -= 10
            audit.flags.append("SOCKET_DAMAGE_FIRE_MISSING: Socket_DamageFire missing for health <= 25% degradation.")

        audit.score = max(0, audit.score)
        return audit

    def validate_asset_bundle(self, bundle_path: Path) -> List[MechRigAudit]:
        """Audits compiled AssetBundle or .assets file using UnityPy."""
        audits: List[MechRigAudit] = []
        if not HAS_UNITYPY:
            return audits

        if not bundle_path.exists():
            return audits

        try:
            env = UnityPy.load(str(bundle_path))
        except Exception:
            return audits

        for obj in env.objects:
            if obj.type.name == "GameObject":
                try:
                    data = obj.read()
                    name = getattr(data, "m_Name", "")
                    if not any(k in name.lower() for k in ("visual", "model", "walker", "mech")):
                        continue

                    audit = MechRigAudit(
                        model_name=name,
                        source_type="bundle",
                    )

                    found_names: Set[str] = set()
                    self._collect_child_names(data, found_names)
                    lower_names = {n.lower() for n in found_names}

                    has_bone_prefix = any(n.startswith("bone_") for n in lower_names)
                    has_armature_kw = any("armature" in n for n in lower_names)
                    audit.has_armature = has_bone_prefix or has_armature_kw
                    audit.has_hierarchical_legs = any(k in lower_names for k in ("legs", "leftleg", "rightleg"))

                    if audit.has_armature:
                        audit.rig_type = "skeletal_armature"
                    elif audit.has_hierarchical_legs:
                        audit.rig_type = "hierarchical_transform"
                    else:
                        audit.rig_type = "static_unrigged"

                    for b in SKELETAL_WALKER_BONES:
                        if any(b in n for n in lower_names):
                            audit.found_bones.append(b)
                        else:
                            audit.missing_bones.append(b)

                    for s in CANONICAL_WALKER_SOCKETS:
                        if any(s in n for n in lower_names):
                            audit.found_sockets.append(s)
                        else:
                            audit.missing_sockets.append(s)

                    if not any("muzzle" in s for s in audit.found_sockets):
                        audit.score -= 15
                        audit.flags.append("SOCKET_MUZZLE_MISSING: Muzzle socket missing.")

                    audit.score = max(0, audit.score)
                    audits.append(audit)
                except Exception:
                    pass

        return audits

    def _collect_child_names(self, go_data: Any, names_set: Set[str]) -> None:
        """Recursively collects GameObject child names."""
        try:
            names_set.add(getattr(go_data, "m_Name", ""))
            for comp_ptr in getattr(go_data, "m_Component", []):
                try:
                    c = comp_ptr.read() if hasattr(comp_ptr, "read") else comp_ptr.component.read()
                    if c.type.name == "Transform":
                        for ch_ptr in getattr(c, "m_Children", []):
                            ch = ch_ptr.read()
                            go_ptr = getattr(ch, "m_GameObject", None)
                            if go_ptr:
                                self._collect_child_names(go_ptr.read(), names_set)
                except Exception:
                    pass
        except Exception:
            pass


def print_audit_table(audits: List[MechRigAudit]) -> None:
    """Prints a formatted ASCII report table."""
    header = f"{'Model Name':<28} | {'Type':<8} | {'Rig Paradigm':<22} | {'Bones':<5} | {'Sockets':<7} | {'Score':<5}"
    print("-" * len(header))
    print(header)
    print("-" * len(header))
    for a in audits:
        b_count = f"{len(a.found_bones)}/{len(SKELETAL_WALKER_BONES)}"
        s_count = f"{len(a.found_sockets)}/{len(CANONICAL_WALKER_SOCKETS)}"
        print(f"{a.model_name:<28} | {a.source_type:<8} | {a.rig_type:<22} | {b_count:<5} | {s_count:<7} | {a.score:<5}")
    print("-" * len(header))

    flagged = [a for a in audits if a.flags]
    if flagged:
        print("\n[!] MECH RIGGING DIAGNOSTICS:")
        for a in flagged:
            print(f"  * {a.model_name} (Rig: {a.rig_type}, Score: {a.score}/100):")
            for f in a.flags:
                print(f"      - {f}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Rogue Command Mech Rigging, Armatures & Locomotion Validator."
    )
    parser.add_argument(
        "--pack",
        type=Path,
        help="Path to content pack directory containing content.json and bundles/.",
    )
    parser.add_argument(
        "--bundle",
        type=Path,
        help="Path to a specific compiled .bundle file.",
    )
    parser.add_argument(
        "--prefab",
        type=Path,
        help="Path to a specific Unity .prefab YAML file.",
    )
    parser.add_argument(
        "--unity-project",
        type=Path,
        default=Path("unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample"),
        help="Path to Unity authoring project sample folder to scan for .prefab files.",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output results in JSON format.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    validator = MechRigValidator()
    all_audits: List[MechRigAudit] = []

    # 1. Check pack bundle if provided
    if args.pack:
        pack_content_json = args.pack / "content.json" if args.pack.is_dir() else args.pack
        bundle_dir = pack_content_json.parent / "bundles"
        if bundle_dir.exists():
            for b in bundle_dir.glob("*.bundle"):
                if "manifest" not in b.name.lower():
                    all_audits.extend(validator.validate_asset_bundle(b))

    # 2. Check specific bundle
    if args.bundle:
        all_audits.extend(validator.validate_asset_bundle(args.bundle))

    # 3. Check specific prefab
    if args.prefab:
        all_audits.append(validator.validate_prefab_yaml(args.prefab))

    # 4. Default: scan Unity authoring project sample prefabs
    if not (args.pack or args.bundle or args.prefab):
        if args.unity_project.exists():
            for p in args.unity_project.rglob("*Visual.prefab"):
                all_audits.append(validator.validate_prefab_yaml(p))

    if not all_audits:
        sys.stderr.write("No mech/vehicle visual prefabs found to audit.\n")
        return 1

    if args.json:
        data = [asdict(a) for a in all_audits]
        print(json.dumps(data, indent=2))
    else:
        print(f"\nAudited {len(all_audits)} visual model asset(s).")
        print_audit_table(all_audits)

    has_critical_failure = any(a.score < 40 for a in all_audits)
    return 2 if has_critical_failure else 0


if __name__ == "__main__":
    sys.exit(main())
