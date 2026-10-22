#!/usr/bin/env python3
"""Offline validation for Rogue Command expansion loader pack manifests.

Ports the StageBlueprintFactoryIds / StageBlueprintPoolFactoryIds and AI stage-range
contiguity checks so authors can catch registry issues before building AssetBundles.

Run from the repository root:
    python tools/validate_loader_registry.py
"""
import json
import sys
from pathlib import Path

PACKS_ROOT = Path("content/packs")


def load_pack(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def check_unit_factory_uniqueness(pack):
    diagnostics = []
    units = pack.get("units", []) or []
    seen = {}
    for unit in units:
        factory_id = unit.get("productionBuilding", {}).get("id")
        if not factory_id:
            continue
        if factory_id in seen:
            diagnostics.append(
                f"FAIL {unit.get('id', '<unknown>')}: duplicate productionBuilding.id={factory_id}"
            )
        else:
            seen[factory_id] = unit.get("id")
    return diagnostics


def check_stage_pool_uniqueness(pack):
    diagnostics = []
    stages = pack.get("stages", []) or []
    for stage in stages:
        for entry in stage.get("blueprintPoolEntries", []) or []:
            seen = set()
            for unit_id in entry.get("unitIds", []) or []:
                if unit_id in seen:
                    diagnostics.append(
                        f"FAIL {stage.get('id', '<unknown>')} pool {entry.get('poolId', '<unknown>')}: "
                        f"duplicate unitId={unit_id}"
                    )
                seen.add(unit_id)
    return diagnostics


def check_ai_stage_range_contiguity(pack):
    diagnostics = []
    stages = pack.get("stages", []) or []
    stage_indices = {stage["id"]: i for i, stage in enumerate(stages) if "id" in stage}

    for profile in pack.get("aiProfiles", []) or []:
        if not profile.get("scopeToReferencedStages"):
            continue

        referenced_ids = [
            sid
            for sid in (
                profile.get("referencedStageIds")
                or [
                    stage["id"]
                    for stage in stages
                    if profile["id"] in (stage.get("aiProfileIds") or [])
                ]
            )
            if sid in stage_indices
        ]

        if not referenced_ids:
            continue

        indices = sorted(stage_indices[sid] for sid in referenced_ids)
        for i in range(1, len(indices)):
            if indices[i] != indices[i - 1] + 1:
                diagnostics.append(
                    f"FAIL {profile.get('id', '<unknown>')}: non-contiguous stage range {indices}"
                )
                break
    return diagnostics


def main():
    pack_paths = sorted(PACKS_ROOT.glob("*/content.json"))
    diagnostics = []
    for pack_path in pack_paths:
        pack = load_pack(pack_path)
        diagnostics.extend(check_unit_factory_uniqueness(pack))
        diagnostics.extend(check_stage_pool_uniqueness(pack))
        diagnostics.extend(check_ai_stage_range_contiguity(pack))

    if diagnostics:
        for diagnostic in diagnostics:
            print(diagnostic)
        print(f"FAIL found {len(diagnostics)} registry issue(s) across {len(pack_paths)} manifests")
        return 1

    print(f"PASS checked {len(pack_paths)} manifests")
    return 0


if __name__ == "__main__":
    sys.exit(main())
