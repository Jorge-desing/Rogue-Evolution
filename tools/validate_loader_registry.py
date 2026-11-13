#!/usr/bin/env python3
"""Offline validation for Rogue Command expansion loader pack manifests.

Ports the StageBlueprintFactoryIds / StageBlueprintPoolFactoryIds and AI stage-range
contiguity checks so authors can catch registry issues before building AssetBundles.

Run from the repository root:
    python tools/validate_loader_registry.py
    python tools/validate_loader_registry.py --json
"""
import argparse
import json
import sys
from pathlib import Path

PACKS_ROOT = Path("content/packs")


def load_pack(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def _as_dict(value):
    return value if isinstance(value, dict) else {}


def _as_list(value):
    return value if isinstance(value, list) else []


def _nonempty_string(value):
    return isinstance(value, str) and bool(value.strip())


def _iter_dicts(value):
    return (item for item in _as_list(value) if isinstance(item, dict))


def _unit_factory_map(pack):
    mapping = {}
    for unit in _iter_dicts(_as_dict(pack).get("units")):
        unit_id = unit.get("id")
        factory_id = _as_dict(unit.get("productionBuilding")).get("id")
        if _nonempty_string(unit_id) and _nonempty_string(factory_id):
            mapping.setdefault(unit_id, factory_id)
    return mapping


def _factory_ids_for_products(product_ids, factory_by_product):
    return sorted({
        factory_by_product[product_id]
        for product_id in product_ids
        if _nonempty_string(product_id) and product_id in factory_by_product
    })


def _collect_loader_factory_ids(stage, factory_by_product):
    product_ids = list(_as_list(stage.get("unitIds")))
    for entry in _iter_dicts(stage.get("blueprintPoolEntries")):
        product_ids.extend(_as_list(entry.get("unitIds")))

    return _factory_ids_for_products(product_ids, factory_by_product)


def _collect_explicit_pool_scopes(stage, factory_by_product):
    pools = {}
    for entry in _iter_dicts(stage.get("blueprintPoolEntries")):
        pool_id = entry.get("poolId")
        if not _nonempty_string(pool_id):
            continue

        scope = pools.setdefault(pool_id, {"factoryIds": set(), "unitIds": set()})
        for product_id in _as_list(entry.get("unitIds")):
            if not _nonempty_string(product_id):
                continue
            factory_id = factory_by_product.get(product_id)
            if _nonempty_string(factory_id):
                scope["factoryIds"].add(factory_id)
                scope["unitIds"].add(factory_id)
            else:
                scope["unitIds"].add(product_id)

    return [
        {
            "poolId": pool_id,
            "factoryIds": sorted(scope["factoryIds"]),
            "unitIds": sorted(scope["unitIds"]),
        }
        for pool_id, scope in sorted(pools.items())
    ]


def _collect_ai_profile_stage_ranges(pack):
    pack = _as_dict(pack)
    stages = _as_list(pack.get("stages"))
    stage_indices_by_profile = {}
    stage_ids_by_profile = {}

    for stage_index, raw_stage in enumerate(stages):
        stage = _as_dict(raw_stage)
        stage_id = stage.get("id")
        if not _nonempty_string(stage_id):
            continue
        for profile_id in set(
            profile_id
            for profile_id in _as_list(stage.get("aiProfileIds"))
            if _nonempty_string(profile_id)
        ):
            stage_indices_by_profile.setdefault(profile_id, []).append(stage_index)
            stage_ids_by_profile.setdefault(profile_id, []).append(stage_id)

    ranges = []
    for profile in _iter_dicts(pack.get("aiProfiles")):
        profile_id = profile.get("id")
        if not profile.get("scopeToReferencedStages") or not _nonempty_string(profile_id):
            continue

        indices = stage_indices_by_profile.get(profile_id, [])
        stage_ids = stage_ids_by_profile.get(profile_id, [])
        contiguous = bool(indices) and all(
            indices[i] == indices[i - 1] + 1
            for i in range(1, len(indices))
        )
        ranges.append({
            "profileId": profile_id,
            "indexScope": "pack-local array order",
            "stageIds": stage_ids,
            "stageIndices": indices,
            "stageMin": min(indices) if indices else None,
            "stageMax": max(indices) if indices else None,
            "isContiguous": contiguous,
        })

    return sorted(ranges, key=lambda item: item["profileId"])


def _localization_report(entity):
    names = set()
    descriptions = set()
    skills = set()
    for item in _iter_dicts(entity.get("localizations")):
        language = item.get("language")
        if not _nonempty_string(language):
            continue
        if _nonempty_string(item.get("name")):
            names.add(language)
        if _nonempty_string(item.get("description")):
            descriptions.add(language)
        if _nonempty_string(item.get("skillDescription")):
            skills.add(language)
    return {
        "unitId": entity["id"],
        "nameLanguages": sorted(names),
        "descriptionLanguages": sorted(descriptions),
        "skillDescriptionLanguages": sorted(skills),
        "hasEnglishName": "en-US" in names,
        "hasNativeTemplate": _nonempty_string(entity.get("nativeTemplateId")),
    }


def _collect_unit_localizations(pack):
    reports = []
    for unit in _iter_dicts(pack.get("units")):
        if _nonempty_string(unit.get("id")):
            reports.append(_localization_report(unit))
        factory = _as_dict(unit.get("productionBuilding"))
        if _nonempty_string(factory.get("id")):
            reports.append(_localization_report(factory))
    return sorted(reports, key=lambda report: report["unitId"])


def build_pack_report(pack, pack_path: Path):
    pack = _as_dict(pack)
    factory_by_product = _unit_factory_map(pack)
    unit_factories = [
        {"unitId": unit_id, "factoryId": factory_id}
        for unit_id, factory_id in sorted(factory_by_product.items())
    ]
    stages = []
    for stage in _iter_dicts(pack.get("stages")):
        stage_factory_ids = _collect_loader_factory_ids(stage, factory_by_product)
        unlisted_pool_factory_ids = _factory_ids_for_products(
            _as_list(stage.get("unitIds")), factory_by_product
        )
        stages.append({
            "stageId": stage.get("id", ""),
            "stageFactoryIds": stage_factory_ids,
            "unlistedPoolFactoryIds": unlisted_pool_factory_ids,
            "explicitPoolScopes": _collect_explicit_pool_scopes(stage, factory_by_product),
        })

    diagnostics = []
    diagnostics.extend(check_unit_factory_uniqueness(pack))
    diagnostics.extend(check_stage_pool_uniqueness(pack))
    diagnostics.extend(check_ai_stage_range_contiguity(pack))
    pack_id = pack.get("packId")
    if not _nonempty_string(pack_id):
        pack_id = pack_path.parent.name

    return {
        "packId": pack_id,
        "packFolder": pack_path.parent.name,
        "unitFactories": unit_factories,
        "unitLocalizations": _collect_unit_localizations(pack),
        "stages": stages,
        "aiProfileStageRanges": _collect_ai_profile_stage_ranges(pack),
        "diagnostics": diagnostics,
    }


def check_unit_factory_uniqueness(pack):
    diagnostics = []
    seen = {}
    for unit in _iter_dicts(_as_dict(pack).get("units")):
        factory_id = _as_dict(unit.get("productionBuilding")).get("id")
        if not _nonempty_string(factory_id):
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
    for stage in _iter_dicts(_as_dict(pack).get("stages")):
        for entry in _iter_dicts(stage.get("blueprintPoolEntries")):
            seen = set()
            for unit_id in _as_list(entry.get("unitIds")):
                if not _nonempty_string(unit_id):
                    continue
                if unit_id in seen:
                    diagnostics.append(
                        f"FAIL {stage.get('id', '<unknown>')} pool {entry.get('poolId', '<unknown>')}: "
                        f"duplicate unitId={unit_id}"
                    )
                seen.add(unit_id)
    return diagnostics


def check_ai_stage_range_contiguity(pack):
    diagnostics = []
    for stage_range in _collect_ai_profile_stage_ranges(pack):
        profile_id = stage_range["profileId"]
        if not stage_range["stageIds"]:
            diagnostics.append(f"FAIL {profile_id}: no referenced stages")
        elif not stage_range["isContiguous"]:
            diagnostics.append(
                f"FAIL {profile_id}: non-contiguous stage range {stage_range['stageIndices']}"
            )
    return diagnostics


def main(argv=None):
    parser = argparse.ArgumentParser(description="Validate and inspect loader registries in content packs.")
    parser.add_argument(
        "--json",
        action="store_true",
        help="write machine-readable mappings and validation results as JSON",
    )
    args = parser.parse_args(argv)

    pack_paths = sorted(PACKS_ROOT.glob("*/content.json"))
    pack_reports = []
    for pack_path in pack_paths:
        try:
            pack = load_pack(pack_path)
            report = build_pack_report(pack, pack_path)
        except (OSError, json.JSONDecodeError) as error:
            report = {
                "packId": pack_path.parent.name,
                "packFolder": pack_path.parent.name,
                "unitFactories": [],
                "unitLocalizations": [],
                "stages": [],
                "aiProfileStageRanges": [],
                "diagnostics": [
                    f"FAIL {pack_path.parent.name}: unable to read content.json: {error}"
                ],
            }
        pack_reports.append(report)

    issues = [
        {"packId": report["packId"], "message": message}
        for report in pack_reports
        for message in report["diagnostics"]
    ]
    result = {
        "summary": {
            "checkedManifests": len(pack_paths),
            "issueCount": len(issues),
            "registryValid": not issues,
        },
        "limitations": [
            "AI stageMin/stageMax are pack-local array indices, not runtime progression indices.",
            "Unlisted pools derive eligibility only from stage.unitIds; blueprintPoolEntries do not broaden them.",
            "Unit localization lists show declared text only; native and en-US fallback resolution is not verified by this offline report.",
        ],
        "packs": pack_reports,
        "issues": issues,
    }

    if args.json:
        print(json.dumps(result, indent=2, ensure_ascii=False))
        return 1 if issues else 0

    for report in pack_reports:
        for item in report["unitFactories"]:
            print(f"INFO {report['packId']} unit {item['unitId']} -> {item['factoryId']}")
        for localization in report["unitLocalizations"]:
            names = ", ".join(localization["nameLanguages"]) or "none"
            descriptions = ", ".join(localization["descriptionLanguages"]) or "none"
            skills = ", ".join(localization["skillDescriptionLanguages"]) or "none"
            native = "yes" if localization["hasNativeTemplate"] else "no"
            english = "yes" if localization["hasEnglishName"] else "no"
            print(f"INFO {report['packId']} localization {localization['unitId']} "
                  f"declared names [{names}], descriptions [{descriptions}], "
                  f"skill descriptions [{skills}], en-US name={english}, "
                  f"native template declared={native}")
        for stage in report["stages"]:
            fallback = ", ".join(stage["unlistedPoolFactoryIds"]) or "none"
            print(f"INFO {report['packId']} stage {stage['stageId']} unlisted pools -> [{fallback}]")
            for scope in stage["explicitPoolScopes"]:
                unit_ids = ", ".join(scope["unitIds"]) or "none"
                print(f"INFO {report['packId']} stage {stage['stageId']} pool {scope['poolId']} -> [{unit_ids}]")
        for stage_range in report["aiProfileStageRanges"]:
            status = "contiguous" if stage_range["isContiguous"] else "not contiguous"
            indices = ", ".join(str(index) for index in stage_range["stageIndices"]) or "none"
            print(f"INFO {report['packId']} AI {stage_range['profileId']} pack-local stages [{indices}] ({status})")

    if issues:
        for issue in issues:
            print(issue["message"])
        print(f"FAIL found {len(issues)} registry issue(s) across {len(pack_paths)} manifests")
        return 1

    print(f"PASS checked {len(pack_paths)} manifests")
    return 0


if __name__ == "__main__":
    sys.exit(main())
