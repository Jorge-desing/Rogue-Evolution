#!/usr/bin/env python3
"""Apply and integrate the 44 Space-Military PBR presets to Rogue Command units across all packs.

Integrates Material Maker 1.7 template preparation, disposable 3D paint workspace setup,
PBR texture baking, and visual quality auditing for all 19 units across:
- `content/packs/sample-expansion`
- `content/packs/sample-native-expansion`
- `content/packs/tactical-operations`

Usage Examples:
    python tools/Apply-UnitPbrPresets.py --list
    python tools/Apply-UnitPbrPresets.py --audit
    python tools/Apply-UnitPbrPresets.py --unit tactical-operations:gravitas-breaker --prepare-templates
    python tools/Apply-UnitPbrPresets.py --pack tactical-operations --prepare-templates
    python tools/Apply-UnitPbrPresets.py --all --prepare-templates
    python tools/Apply-UnitPbrPresets.py --export-json artifacts/pbr-integration-matrix.json
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional, Sequence

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT / "tools"))

from UnitPbrPresetRegistry import (
    UNIT_PBR_REGISTRY,
    UnitPbrProfile,
    get_pack_units,
    get_unit_profile,
    verify_all_presets_mapped,
)

PREPARE_TEMPLATE_TOOL = REPO_ROOT / "tools" / "Prepare-MaterialMakerTacticalTemplate.py"
PREPARE_PAINT_TOOL = REPO_ROOT / "tools" / "prepare_material_maker_3d_paint.py"
AUDIT_OUTPUT_TOOL = REPO_ROOT / "tools" / "audit_material_maker_output.py"
BAKE_SCRIPT = REPO_ROOT / "tools" / "Run-MaterialMakerBake.ps1"


def print_unit_preset_matrix() -> None:
    """Print the human-readable unit-to-preset mapping matrix."""
    print("=" * 90)
    print(f"{'Unit ID':<44} | {'Primary Preset':<30} | {'Variants'}")
    print("=" * 90)

    packs = sorted({p.pack_id for p in UNIT_PBR_REGISTRY.values()})
    for pack in packs:
        print(f"\n--- Content Pack: {pack} ---")
        for unit in get_pack_units(pack):
            variants_str = ", ".join(
                f"{k}->{v}" for k, v in unit.tactical_variant_presets.items()
            )
            print(f"{unit.unit_id:<44} | {unit.primary_preset:<30} | {variants_str}")
    print("=" * 90)


def audit_presets() -> int:
    """Perform a strict verification that all 44 presets are mapped and reachable."""
    is_valid, unmapped, mapped = verify_all_presets_mapped()
    print("PBR Preset-to-Unit Audit:")
    print(f"  Total mod units: {len(UNIT_PBR_REGISTRY)}")
    print(f"  Total mapped presets: {len(mapped)} / 44")
    if unmapped:
        print(f"  ERROR: Unmapped presets detected: {unmapped}")
        return 1
    print("  PASS: All 44 space-military tactical presets are fully integrated across mod units.")
    return 0


def prepare_unit_templates(
    unit: UnitPbrProfile,
    size_power: int = 11,
    include_variants: bool = True,
    output_base: Optional[Path] = None,
) -> List[Path]:
    """Generate .ptex template projects for a unit's primary and variant presets."""
    import importlib.util

    spec = importlib.util.spec_from_file_location(
        "prepare_mm_template", PREPARE_TEMPLATE_TOOL
    )
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load {PREPARE_TEMPLATE_TOOL}")
    template_mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(template_mod)

    base_dir = output_base or (REPO_ROOT / "artifacts" / "texture-work" / unit.model_name / "MaterialMaker")
    base_dir.mkdir(parents=True, exist_ok=True)

    generated: List[Path] = []

    # 1. Primary preset template
    primary_ptex = base_dir / f"{unit.model_name}_{unit.primary_preset}.ptex"
    primary_proj = template_mod.build_template_project(
        preset_name=unit.primary_preset, size_power=size_power
    )
    primary_proj["label"] = f"{unit.model_name} [{unit.primary_preset}]"
    primary_proj["name"] = f"{unit.model_name.lower()}_{unit.primary_preset}"
    primary_ptex.write_text(json.dumps(primary_proj, indent="\t"), encoding="utf-8")
    generated.append(primary_ptex)

    # 2. Variants
    if include_variants:
        for var_name, var_preset in unit.tactical_variant_presets.items():
            var_ptex = base_dir / f"{unit.model_name}_{var_preset}.ptex"
            var_proj = template_mod.build_template_project(
                preset_name=var_preset, size_power=size_power
            )
            var_proj["label"] = f"{unit.model_name} Variant:{var_name} [{var_preset}]"
            var_proj["name"] = f"{unit.model_name.lower()}_{var_name}_{var_preset}"
            var_ptex.write_text(json.dumps(var_proj, indent="\t"), encoding="utf-8")
            generated.append(var_ptex)

    return generated


def prepare_paint_workspace(
    unit: UnitPbrProfile,
    size_power: int = 11,
) -> Optional[Dict[str, Any]]:
    """Prepare a 3D paint proxy workspace for the unit using its canonical source model."""
    source_model = REPO_ROOT / unit.canonical_source_art
    if not source_model.is_file():
        print(f"  Note: Source art {unit.canonical_source_art} not found for {unit.model_name}; skipping 3D paint proxy.")
        return None

    import importlib.util
    spec = importlib.util.spec_from_file_location("prep_paint", PREPARE_PAINT_TOOL)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load {PREPARE_PAINT_TOOL}")
    paint_mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(paint_mod)

    workspace = REPO_ROOT / "artifacts" / "texture-work" / unit.model_name / "MaterialMaker"
    manifest = paint_mod.prepare_3d_paint_workspace(
        unit_name=unit.model_name,
        workspace_root=workspace,
        source_model=source_model,
        preset=unit.primary_preset,
        size_power=size_power,
    )
    return manifest


def export_integration_matrix_json(output_path: Path) -> None:
    """Export the complete unit-to-preset integration data as structured JSON."""
    data = {
        "version": "1.0.0",
        "totalUnits": len(UNIT_PBR_REGISTRY),
        "totalPresets": 44,
        "units": {},
        "presetsUsage": {},
    }

    for uid, profile in UNIT_PBR_REGISTRY.items():
        data["units"][uid] = {
            "unitId": profile.unit_id,
            "packId": profile.pack_id,
            "modelName": profile.model_name,
            "sourceArt": profile.canonical_source_art,
            "primaryPreset": profile.primary_preset,
            "primaryColorTint": profile.primary_color_tint,
            "roleDoctrine": profile.role_doctrine,
            "tacticalVariants": [
                {
                    "variantKey": k,
                    "preset": v,
                    "colorTint": profile.variant_color_tints.get(k, "#808080"),
                }
                for k, v in profile.tactical_variant_presets.items()
            ],
        }

        # Track usage
        p = profile.primary_preset
        data["presetsUsage"].setdefault(p, []).append({"unitId": uid, "role": "primary"})
        for k, v in profile.tactical_variant_presets.items():
            data["presetsUsage"].setdefault(v, []).append({"unitId": uid, "role": f"variant:{k}"})

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(data, indent=2), encoding="utf-8")
    print(f"Exported PBR integration matrix JSON to: {output_path}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--list", action="store_true", help="List unit preset mappings table")
    parser.add_argument("--audit", action="store_true", help="Audit mapping completeness (all 44 presets)")
    parser.add_argument("--unit", type=str, help="Target a specific unit by namespaced ID")
    parser.add_argument("--pack", type=str, help="Target all units in a specific content pack")
    parser.add_argument("--all", action="store_true", help="Target all units across all packs")
    parser.add_argument("--prepare-templates", action="store_true", help="Generate Material Maker .ptex templates")
    parser.add_argument("--prepare-paint", action="store_true", help="Generate 3D paint proxies and manifests")
    parser.add_argument("--export-json", type=Path, help="Export integration matrix JSON to file")
    parser.add_argument("--size", type=int, default=11, choices=[9, 10, 11, 12], help="Texture size power (default 11 = 2048)")
    args = parser.parse_args()

    if args.list:
        print_unit_preset_matrix()
        return 0

    if args.audit:
        return audit_presets()

    if args.export_json:
        export_integration_matrix_json(args.export_json)
        return 0

    targets: List[UnitPbrProfile] = []
    if args.unit:
        profile = get_unit_profile(args.unit)
        if not profile:
            print(f"Unknown unit ID: {args.unit}")
            return 1
        targets.append(profile)
    elif args.pack:
        targets = get_pack_units(args.pack)
        if not targets:
            print(f"No units found for pack: {args.pack}")
            return 1
    elif args.all:
        targets = list(UNIT_PBR_REGISTRY.values())

    if not targets and not (args.list or args.audit or args.export_json):
        parser.print_help()
        return 0

    if args.prepare_templates:
        total_ptex = 0
        for t in targets:
            generated = prepare_unit_templates(t, size_power=args.size)
            total_ptex += len(generated)
            print(f"Generated {len(generated)} template(s) for {t.unit_id} ({t.model_name})")
        print(f"Total .ptex templates prepared: {total_ptex}")

    if args.prepare_paint:
        total_paint = 0
        for t in targets:
            manifest = prepare_paint_workspace(t, size_power=args.size)
            if manifest:
                total_paint += 1
                print(f"Prepared 3D paint workspace for {t.unit_id} ({t.model_name})")
        print(f"Total 3D paint workspaces prepared: {total_paint}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
