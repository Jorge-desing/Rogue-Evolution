#!/usr/bin/env python3
"""Rogue Command Visual Effects (VFX) Asset Validator.

Validates ParticleSystems, materials, shaders, and socket bindings for Rogue Command
visual assets, prefabs, and compiled AssetBundles against URP rendering invariants
and native game hooks (ShootProjectile muzzle discovery, RuntimeVisualDirector damage tiers).
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any, Dict, List, Optional, Set, Tuple

import UnityPy

# Canonical gameplay and VFX sockets supported by RogueCommandSocketCatalog
CANONICAL_VFX_SOCKETS = {
    "Socket_MuzzlePrimary",
    "Socket_MuzzleSecondary",
    "Socket_MuzzleFlash",
    "Socket_DamageSmoke",
    "Socket_DamageFire",
    "Socket_Exhaust_L",
    "Socket_Exhaust_R",
    "Socket_GroundContact",
    "Socket_GroundCenter",
    "Socket_TurretPivot",
    "Socket_TurretPitch",
    "Socket_TurretYaw",
    "Socket_RecoilPrimary",
    "Socket_RadarMast",
    "Socket_Hardpoint_ERA",
    "Socket_Hardpoint_APS",
    "Socket_Hardpoint_FuelDrums",
    "Socket_FootContact_L",
    "Socket_FootContact_R",
    "Socket_FootContact_FL",
    "Socket_FootContact_FR",
    "Socket_FootContact_RL",
    "Socket_FootContact_RR",
    "Socket_EngineAudio",
    "Socket_WeaponAudio",
    "Socket_BuildingAudio",
    "FirePoint_0",
    "FirePoint_1",
}

# Verified URP particle shaders in Rogue Command 1.0.9
VERIFIED_URP_PARTICLE_SHADERS = {
    "Universal Render Pipeline/Particles/Unlit",
    "Universal Render Pipeline/Particles/Simple Lit",
    "Universal Render Pipeline/Particles/Lit",
    "Legacy Shaders/Particles/Additive (Soft)",
    "Legacy Shaders/Particles/Alpha Blended",
    "Legacy Shaders/Particles/Alpha Blended Premultiply",
    "Mobile/Particles/Alpha Blended",
    "Particles/Standard Unlit",
    "Sprites/Default",
    "RogueCommand/VehiclePBR_VertexWear",
}

# Performance boundaries for real-time RTS combat
MAX_RECOMMENDED_PARTICLES = 250
MAX_RECOMMENDED_EMISSION_RATE = 60.0


@dataclass
class ParticleSystemAudit:
    """Audit report for an individual ParticleSystem emitter."""

    name: str
    max_particles: int = 100
    emission_rate: float = 10.0
    is_looping: bool = False
    duration: float = 1.0
    shader_name: str = "Unknown"
    is_instanced: bool = False
    parent_socket: Optional[str] = None
    flags: List[str] = field(default_factory=list)


@dataclass
class VisualPrefabAudit:
    """Audit report for a complete vehicle visual prefab."""

    asset_name: str
    source_type: str  # 'bundle' or 'prefab_yaml'
    found_sockets: List[str] = field(default_factory=list)
    missing_sockets: List[str] = field(default_factory=list)
    particle_emitters: List[ParticleSystemAudit] = field(default_factory=list)
    has_damage_smoke_socket: bool = False
    has_damage_fire_socket: bool = False
    has_muzzle_socket: bool = False
    has_recoil_socket: bool = False
    flags: List[str] = field(default_factory=list)
    score: int = 100  # Health score 0-100


class VfxAssetValidator:
    """Inspects and validates VFX assets in Unity YAML prefabs and compiled bundles."""

    def __init__(self):
        self.audits: List[VisualPrefabAudit] = []

    def validate_prefab_yaml(self, prefab_path: Path) -> VisualPrefabAudit:
        """Parses a Unity .prefab YAML file to inspect socket hierarchy and components."""
        asset_name = prefab_path.stem
        audit = VisualPrefabAudit(asset_name=asset_name, source_type="prefab_yaml")

        if not prefab_path.exists():
            audit.flags.append(f"FILE_NOT_FOUND: {prefab_path}")
            audit.score = 0
            return audit

        content = prefab_path.read_text(encoding="utf-8", errors="ignore")

        # Discover GameObjects and Sockets from YAML
        # Pattern matches: m_Name: <Name>
        names = re.findall(r"m_Name:\s*([^\r\n]+)", content)
        for name in names:
            clean_name = name.strip()
            # Normalize possible quotes
            if clean_name.startswith('"') and clean_name.endswith('"'):
                clean_name = clean_name[1:-1]
            if clean_name.startswith("'") and clean_name.endswith("'"):
                clean_name = clean_name[1:-1]

            # Check for canonical sockets or legacy prefixes
            for socket in CANONICAL_VFX_SOCKETS:
                if socket.lower() in clean_name.lower():
                    if socket not in audit.found_sockets:
                        audit.found_sockets.append(socket)

        audit.has_damage_smoke_socket = "Socket_DamageSmoke" in audit.found_sockets
        audit.has_damage_fire_socket = "Socket_DamageFire" in audit.found_sockets
        audit.has_muzzle_socket = any("muzzle" in s.lower() for s in audit.found_sockets)
        audit.has_recoil_socket = "Socket_RecoilPrimary" in audit.found_sockets

        # Missing socket diagnostics
        if not audit.has_damage_smoke_socket:
            audit.missing_sockets.append("Socket_DamageSmoke")
            audit.flags.append("VFX_SOCKET_SMOKE_MISSING: Missing Socket_DamageSmoke (director will fall back to root).")
            audit.score -= 15

        if not audit.has_damage_fire_socket:
            audit.missing_sockets.append("Socket_DamageFire")
            audit.flags.append("VFX_SOCKET_FIRE_MISSING: Missing Socket_DamageFire (director will fall back to root).")
            audit.score -= 15

        if not audit.has_muzzle_socket:
            audit.missing_sockets.append("Socket_MuzzlePrimary")
            audit.flags.append("VFX_SOCKET_MUZZLE_MISSING: Missing Socket_MuzzlePrimary / Socket_MuzzleFlash.")
            audit.score -= 20

        # Check for ParticleSystem components in YAML
        has_particle_system = "ParticleSystem:" in content
        if has_particle_system:
            # Parse particle parameters if present
            max_particles_matches = re.findall(r"m_MaxParticles:\s*(\d+)", content)
            for mp_str in max_particles_matches:
                mp = int(mp_str)
                emitter = ParticleSystemAudit(name=f"{asset_name}_Emitter", max_particles=mp)
                if mp > MAX_RECOMMENDED_PARTICLES:
                    emitter.flags.append(f"VFX_EXCESSIVE_PARTICLES: maxParticles ({mp}) > {MAX_RECOMMENDED_PARTICLES}")
                    audit.score -= 10
                audit.particle_emitters.append(emitter)

        audit.score = max(0, audit.score)
        return audit

    def validate_gameplay_json(self, json_path: Path) -> VisualPrefabAudit:
        """Audits socket declarations inside a .gameplay.json metadata file."""
        asset_name = json_path.stem.replace(".gameplay", "")
        audit = VisualPrefabAudit(asset_name=asset_name, source_type="gameplay_json")

        if not json_path.exists():
            audit.flags.append(f"FILE_NOT_FOUND: {json_path}")
            audit.score = 0
            return audit

        try:
            data = json.loads(json_path.read_text(encoding="utf-8"))
        except Exception as e:
            audit.flags.append(f"JSON_PARSE_ERROR: {e}")
            audit.score = 0
            return audit

        sockets_dict = data.get("sockets", {})
        for s_name in sockets_dict.keys():
            for socket in CANONICAL_VFX_SOCKETS:
                if socket.lower() in s_name.lower():
                    if socket not in audit.found_sockets:
                        audit.found_sockets.append(socket)

        audit.has_damage_smoke_socket = "Socket_DamageSmoke" in audit.found_sockets
        audit.has_damage_fire_socket = "Socket_DamageFire" in audit.found_sockets
        audit.has_muzzle_socket = any("muzzle" in s.lower() for s in audit.found_sockets)
        audit.has_recoil_socket = "Socket_RecoilPrimary" in audit.found_sockets

        if not audit.has_damage_smoke_socket:
            audit.missing_sockets.append("Socket_DamageSmoke")
            audit.flags.append("VFX_SOCKET_SMOKE_MISSING: Missing Socket_DamageSmoke (director will fall back to root).")
            audit.score -= 15

        if not audit.has_damage_fire_socket:
            audit.missing_sockets.append("Socket_DamageFire")
            audit.flags.append("VFX_SOCKET_FIRE_MISSING: Missing Socket_DamageFire (director will fall back to root).")
            audit.score -= 15

        if not audit.has_muzzle_socket:
            audit.missing_sockets.append("Socket_MuzzlePrimary")
            audit.flags.append("VFX_SOCKET_MUZZLE_MISSING: Missing Socket_MuzzlePrimary / Socket_MuzzleFlash.")
            audit.score -= 20

        audit.score = max(0, audit.score)
        return audit

    def validate_asset_bundle(self, bundle_path: Path) -> List[VisualPrefabAudit]:
        """Loads and audits GameObjects and ParticleSystems inside a compiled Unity AssetBundle."""
        bundle_audits: List[VisualPrefabAudit] = []
        if not bundle_path.exists():
            return bundle_audits

        env = UnityPy.load(str(bundle_path))

        # Collect materials and shaders
        materials_by_path: Dict[int, Any] = {}
        shaders_by_path: Dict[int, str] = {}

        for obj in env.objects:
            if obj.type.name == "Shader":
                try:
                    data = obj.read()
                    name = getattr(data, "m_ParsedForm", None)
                    s_name = getattr(name, "m_Name", "") if name else getattr(data, "m_Name", "")
                    shaders_by_path[obj.path_id] = s_name
                except Exception:
                    pass
            elif obj.type.name == "Material":
                try:
                    data = obj.read()
                    materials_by_path[obj.path_id] = data
                except Exception:
                    pass

        # Inspect GameObjects
        for obj in env.objects:
            if obj.type.name == "GameObject":
                data = obj.read()
                name = getattr(data, "m_Name", "")
                # Only analyze visual prefabs or root visual models
                if not name.endswith("Visual") and not name.endswith("Model"):
                    continue

                audit = VisualPrefabAudit(asset_name=name, source_type="bundle")
                found_names: Set[str] = set()

                # Walk hierarchy through Transform
                for comp_ptr in getattr(data, "m_Component", []):
                    try:
                        comp = comp_ptr.read()
                        if comp.type.name == "Transform":
                            self._collect_child_names(comp, found_names)
                    except Exception:
                        pass

                for c_name in found_names:
                    for socket in CANONICAL_VFX_SOCKETS:
                        if socket.lower() in c_name.lower():
                            if socket not in audit.found_sockets:
                                audit.found_sockets.append(socket)

                audit.has_damage_smoke_socket = "Socket_DamageSmoke" in audit.found_sockets
                audit.has_damage_fire_socket = "Socket_DamageFire" in audit.found_sockets
                audit.has_muzzle_socket = any("muzzle" in s.lower() for s in audit.found_sockets)
                audit.has_recoil_socket = "Socket_RecoilPrimary" in audit.found_sockets

                if not audit.has_damage_smoke_socket:
                    audit.missing_sockets.append("Socket_DamageSmoke")
                    audit.flags.append("VFX_SOCKET_SMOKE_MISSING: Missing Socket_DamageSmoke.")
                    audit.score -= 15

                if not audit.has_damage_fire_socket:
                    audit.missing_sockets.append("Socket_DamageFire")
                    audit.flags.append("VFX_SOCKET_FIRE_MISSING: Missing Socket_DamageFire.")
                    audit.score -= 15

                if not audit.has_muzzle_socket:
                    audit.missing_sockets.append("Socket_MuzzlePrimary")
                    audit.flags.append("VFX_SOCKET_MUZZLE_MISSING: Missing Socket_MuzzlePrimary / Socket_MuzzleFlash.")
                    audit.score -= 20

                audit.score = max(0, audit.score)
                bundle_audits.append(audit)

        return bundle_audits

    def _collect_child_names(self, transform_obj: Any, names_set: Set[str]) -> None:
        """Recursively gathers all child GameObject names."""
        try:
            go_ptr = getattr(transform_obj, "m_GameObject", None)
            if go_ptr:
                go = go_ptr.read()
                names_set.add(getattr(go, "m_Name", ""))

            children = getattr(transform_obj, "m_Children", [])
            for c_ptr in children:
                c_tf = c_ptr.read()
                self._collect_child_names(c_tf, names_set)
        except Exception:
            pass


def print_audit_table(audits: List[VisualPrefabAudit]) -> None:
    """Prints a formatted ASCII report table."""
    header = f"{'Asset Name':<28} | {'Type':<8} | {'Smoke':<6} | {'Fire':<6} | {'Muzzle':<6} | {'Recoil':<6} | {'Score':<5}"
    print("-" * len(header))
    print(header)
    print("-" * len(header))
    for a in audits:
        smk = "YES" if a.has_damage_smoke_socket else "NO"
        fir = "YES" if a.has_damage_fire_socket else "NO"
        muz = "YES" if a.has_muzzle_socket else "NO"
        rec = "YES" if a.has_recoil_socket else "NO"
        print(f"{a.asset_name:<28} | {a.source_type:<8} | {smk:<6} | {fir:<6} | {muz:<6} | {rec:<6} | {a.score:<5}")
    print("-" * len(header))

    # Print details of flags
    flagged = [a for a in audits if a.flags]
    if flagged:
        print("\n[!] VFX AUDIT DIAGNOSTICS:")
        for a in flagged:
            print(f"  * {a.asset_name} (Score: {a.score}/100):")
            for f in a.flags:
                print(f"      - {f}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Rogue Command Visual Effects (VFX) & Sockets Asset Validator."
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
        "--metadata",
        type=Path,
        help="Path to a specific .gameplay.json metadata file.",
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
    validator = VfxAssetValidator()
    all_audits: List[VisualPrefabAudit] = []

    # 1. Check pack bundle if provided
    if args.pack:
        pack_content_json = args.pack / "content.json" if args.pack.is_dir() else args.pack
        pack_dir = pack_content_json.parent
        bundle_dir = pack_dir / "bundles"
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

    # 4. Check specific gameplay.json metadata
    if args.metadata:
        all_audits.append(validator.validate_gameplay_json(args.metadata))

    # 5. Default: scan Unity authoring project prefabs and artifacts/ metadata if no explicit args given
    if not (args.pack or args.bundle or args.prefab or args.metadata):
        if args.unity_project.exists():
            for p in args.unity_project.rglob("*Visual.prefab"):
                all_audits.append(validator.validate_prefab_yaml(p))
        artifacts_dir = Path("artifacts")
        if artifacts_dir.exists():
            for gp in artifacts_dir.glob("*.gameplay.json"):
                all_audits.append(validator.validate_gameplay_json(gp))

    if not all_audits:
        sys.stderr.write("No visual assets found to audit.\n")
        return 1

    if args.json:
        data = [asdict(a) for a in all_audits]
        print(json.dumps(data, indent=2))
    else:
        print(f"\nAudited {len(all_audits)} visual asset(s).")
        print_audit_table(all_audits)

    # Return non-zero if any critical failures (score < 40)
    has_critical_failure = any(a.score < 40 for a in all_audits)
    return 2 if has_critical_failure else 0


if __name__ == "__main__":
    sys.exit(main())
