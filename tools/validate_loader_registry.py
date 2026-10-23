#!/usr/bin/env python3
"""Offline structural/partial validation for loader registry invariants.

This checks selected manifest shapes and registry relationships only; it is not
JSON Schema or full C# package validation.

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


def _validate_string_array(value, path, diagnostics, required=False):
    if value is None and not required:
        return
    if not isinstance(value, list):
        diagnostics.append(f"FAIL {path} must be an array")
        return
    for index, item in enumerate(value):
        if not _nonempty_string(item):
            diagnostics.append(f"FAIL {path}[{index}] must be a non-empty string")


def _validate_ai_entries(profile, profile_path, field, fields, diagnostics):
    if field not in profile:
        return
    entries = profile[field]
    path = f"{profile_path}.{field}"
    if not isinstance(entries, list):
        diagnostics.append(f"FAIL {path} must be an array")
        return
    if len(entries) > 32:
        diagnostics.append(f"FAIL {path} must contain at most 32 entries")
    for index, entry in enumerate(entries):
        entry_path = f"{path}[{index}]"
        if not isinstance(entry, dict):
            diagnostics.append(f"FAIL {entry_path} must be an object")
            continue
        if not _nonempty_string(entry.get("unitId")):
            diagnostics.append(f"FAIL {entry_path}.unitId must be a non-empty string")
        for name, minimum, maximum in fields:
            value = entry.get(name)
            if isinstance(value, bool) or not isinstance(value, int):
                diagnostics.append(f"FAIL {entry_path}.{name} must be an integer")
            elif value < minimum or value > maximum:
                diagnostics.append(
                    f"FAIL {entry_path}.{name} must be between {minimum} and {maximum}"
                )
        if field == "unitEntries":
            max_living = entry.get("maxLiving")
            count_at_start = entry.get("countAtStart")
            if (not isinstance(max_living, bool) and isinstance(max_living, int) and
                    not isinstance(count_at_start, bool) and isinstance(count_at_start, int) and
                    max_living < count_at_start):
                diagnostics.append(
                    f"FAIL {entry_path}.maxLiving must be at least countAtStart and at most 32"
                )


def _validate_registry_structure(pack):
    if not isinstance(pack, dict):
        return ["FAIL manifest root must be a JSON object"]

    diagnostics = []
    for section in ("units", "stages", "aiProfiles"):
        if section not in pack:
            continue
        entries = pack[section]
        if not isinstance(entries, list):
            diagnostics.append(f"FAIL {section} must be an array")
            continue

        for index, entry in enumerate(entries):
            entry_path = f"{section}[{index}]"
            if not isinstance(entry, dict):
                diagnostics.append(f"FAIL {entry_path} must be an object")
                continue
            if not _nonempty_string(entry.get("id")):
                diagnostics.append(f"FAIL {entry_path}.id must be a non-empty string")

            if section == "units":
                factory = entry.get("productionBuilding")
                if not isinstance(factory, dict):
                    diagnostics.append(f"FAIL {entry_path}.productionBuilding must be an object")
                elif not _nonempty_string(factory.get("id")):
                    diagnostics.append(
                        f"FAIL {entry_path}.productionBuilding.id must be a non-empty string"
                    )

            elif section == "stages":
                for field in ("unitIds", "aiProfileIds"):
                    if field in entry:
                        _validate_string_array(entry[field], f"{entry_path}.{field}", diagnostics)

                if "blueprintPoolEntries" in entry:
                    pool_entries = entry["blueprintPoolEntries"]
                    if not isinstance(pool_entries, list):
                        diagnostics.append(
                            f"FAIL {entry_path}.blueprintPoolEntries must be an array"
                        )
                    else:
                        for pool_index, pool_entry in enumerate(pool_entries):
                            pool_path = f"{entry_path}.blueprintPoolEntries[{pool_index}]"
                            if not isinstance(pool_entry, dict):
                                diagnostics.append(f"FAIL {pool_path} must be an object")
                                continue
                            if not _nonempty_string(pool_entry.get("poolId")):
                                diagnostics.append(f"FAIL {pool_path}.poolId must be a non-empty string")
                            _validate_string_array(
                                pool_entry.get("unitIds"), f"{pool_path}.unitIds", diagnostics, required=True
                            )

            elif section == "aiProfiles":
                if "scopeToReferencedStages" in entry and not isinstance(
                    entry["scopeToReferencedStages"], bool
                ):
                    diagnostics.append(
                        f"FAIL {entry_path}.scopeToReferencedStages must be a boolean"
                    )
                _validate_ai_entries(
                    entry, entry_path, "unitEntries",
                    (("baseIndex", 0, 31), ("startingInAscensionLevel", 0, 19),
                     ("countAtStart", 1, 8), ("maxLiving", 1, 32)), diagnostics,
                )
                _validate_ai_entries(
                    entry, entry_path, "productionBuildingEntries",
                    (("baseIndex", 0, 31), ("startingInAscensionLevel", 0, 19),
                     ("maxLiving", 1, 32)), diagnostics,
                )

    return diagnostics


def _unit_factory_map(pack):
    mapping = {}
    seen_unit_ids = set()
    for unit in _iter_dicts(_as_dict(pack).get("units")):
        unit_id = unit.get("id")
        if not _nonempty_string(unit_id):
            continue
        if unit_id in seen_unit_ids:
            mapping.pop(unit_id, None)
            continue
        seen_unit_ids.add(unit_id)
        factory_id = _as_dict(unit.get("productionBuilding")).get("id")
        if _nonempty_string(factory_id):
            mapping[unit_id] = factory_id
    return mapping


def _unit_ids(pack):
    unit_ids = set()
    for unit in _iter_dicts(_as_dict(pack).get("units")):
        unit_id = unit.get("id")
        if _nonempty_string(unit_id):
            unit_ids.add(unit_id)
    return unit_ids


def _global_unit_factory_map(packs):
    pack_list = [pack for pack in packs if not _validate_registry_structure(pack)]
    pack_counts = {}
    factory_counts = {}
    for pack in pack_list:
        factory_map = _unit_factory_map(pack)
        for unit_id in _unit_ids(pack):
            pack_counts[unit_id] = pack_counts.get(unit_id, 0) + 1
        for factory_id in factory_map.values():
            factory_counts[factory_id] = factory_counts.get(factory_id, 0) + 1

    mapping = {}
    for pack in pack_list:
        for product_id, factory_id in _unit_factory_map(pack).items():
            if pack_counts.get(product_id) == 1 and factory_counts.get(factory_id) == 1:
                mapping[product_id] = factory_id
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
            scope["unitIds"].add(product_id)
            factory_id = factory_by_product.get(product_id)
            if _nonempty_string(factory_id):
                scope["factoryIds"].add(factory_id)

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
    for localization in _iter_dicts(entity.get("localizations")):
        language = localization.get("language")
        if not _nonempty_string(language):
            continue
        if _nonempty_string(localization.get("name")):
            names.add(language)
        if _nonempty_string(localization.get("description")):
            descriptions.add(language)
        if _nonempty_string(localization.get("skillDescription")):
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


def build_pack_report(pack, pack_path: Path, available_factory_by_product=None):
    structure_diagnostics = _validate_registry_structure(pack)
    pack = _as_dict(pack)
    local_factory_by_product = _unit_factory_map(pack)
    stage_factory_by_product = (
        available_factory_by_product
        if available_factory_by_product is not None
        else local_factory_by_product
    )
    unit_factories = [
        {"unitId": unit_id, "factoryId": factory_id}
        for unit_id, factory_id in sorted(local_factory_by_product.items())
    ]
    stages = []
    for stage in _iter_dicts(pack.get("stages")):
        stage_factory_ids = _collect_loader_factory_ids(stage, stage_factory_by_product)
        unlisted_pool_factory_ids = _factory_ids_for_products(
            _as_list(stage.get("unitIds")), stage_factory_by_product
        )
        stages.append({
            "stageId": stage.get("id", ""),
            "stageFactoryIds": stage_factory_ids,
            "unlistedPoolFactoryIds": unlisted_pool_factory_ids,
            "explicitPoolScopes": _collect_explicit_pool_scopes(stage, stage_factory_by_product),
        })

    diagnostics = list(structure_diagnostics)
    diagnostics.extend(check_unit_factory_uniqueness(pack))
    diagnostics.extend(check_duplicate_unit_ids_within_manifest(pack))
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


def check_duplicate_unit_ids_within_manifest(pack):
    counts = {}
    for unit in _iter_dicts(_as_dict(pack).get("units")):
        unit_id = unit.get("id")
        if _nonempty_string(unit_id):
            counts[unit_id] = counts.get(unit_id, 0) + 1

    return [
        f"FAIL duplicate unit ID within manifest {unit_id}"
        for unit_id, count in sorted(counts.items())
        if count > 1
    ]


def check_stage_pool_uniqueness(pack):
    diagnostics = []
    for stage in _iter_dicts(_as_dict(pack).get("stages")):
        seen_pool_ids = set()
        for entry in _iter_dicts(stage.get("blueprintPoolEntries")):
            pool_id = entry.get("poolId")
            if _nonempty_string(pool_id):
                if pool_id in seen_pool_ids:
                    diagnostics.append(
                        f"FAIL {stage.get('id', '<unknown>')}: duplicate blueprint pool ID={pool_id}"
                    )
                seen_pool_ids.add(pool_id)

            seen = set()
            for unit_id in _as_list(entry.get("unitIds")):
                if not _nonempty_string(unit_id):
                    continue
                if unit_id in seen:
                    diagnostics.append(
                        f"FAIL {stage.get('id', '<unknown>')} pool {pool_id or '<unknown>'}: "
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


def check_cross_pack_duplicate_unit_ids(pack_items):
    pack_folders_by_unit = {}
    for pack_path, raw_pack in pack_items:
        pack = _as_dict(raw_pack)
        pack_folder = pack_path.parent.name
        unit_ids = {
            unit.get("id")
            for unit in _iter_dicts(pack.get("units"))
            if _nonempty_string(unit.get("id"))
        }
        for unit_id in unit_ids:
            pack_folders_by_unit.setdefault(unit_id, set()).add(pack_folder)

    return [
        "FAIL duplicate unit ID across manifests " + unit_id + ": " + ", ".join(sorted(pack_folders))
        for unit_id, pack_folders in sorted(pack_folders_by_unit.items())
        if len(pack_folders) > 1
    ]


def check_cross_pack_duplicate_factory_ids(pack_items):
    pack_folders_by_factory = {}
    for pack_path, raw_pack in pack_items:
        if _validate_registry_structure(raw_pack):
            continue
        pack_folder = pack_path.parent.name
        factory_ids = set(_unit_factory_map(raw_pack).values())
        for factory_id in factory_ids:
            pack_folders_by_factory.setdefault(factory_id, set()).add(pack_folder)

    return [
        "FAIL duplicate productionBuilding.id across manifests " + factory_id + ": " +
        ", ".join(sorted(pack_folders))
        for factory_id, pack_folders in sorted(pack_folders_by_factory.items())
        if len(pack_folders) > 1
    ]


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Validate selected registry invariants in content pack manifests."
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="write machine-readable mappings and validation results as JSON",
    )
    args = parser.parse_args(argv)

    pack_paths = sorted(PACKS_ROOT.glob("*/content.json"))
    loaded_packs = {}
    load_errors = {}
    for pack_path in pack_paths:
        try:
            loaded_packs[pack_path] = load_pack(pack_path)
        except (OSError, UnicodeDecodeError, json.JSONDecodeError) as error:
            load_errors[pack_path] = error

    available_factory_by_product = _global_unit_factory_map(loaded_packs.values())
    pack_reports = []
    for pack_path in pack_paths:
        if pack_path in load_errors:
            error = load_errors[pack_path]
            report = {
                "packId": pack_path.parent.name,
                "packFolder": pack_path.parent.name,
                "unitFactories": [],
                "stages": [],
                "aiProfileStageRanges": [],
                "diagnostics": [
                    f"FAIL {pack_path.parent.name}: unable to read content.json: {error}"
                ],
            }
        else:
            report = build_pack_report(
                loaded_packs[pack_path], pack_path, available_factory_by_product
            )
        pack_reports.append(report)

    issues = [
        {"packId": report["packId"], "message": message}
        for report in pack_reports
        for message in report["diagnostics"]
    ]
    issues.extend(
        {"packId": "<registry>", "message": message}
        for message in check_cross_pack_duplicate_unit_ids(loaded_packs.items())
    )
    issues.extend(
        {"packId": "<registry>", "message": message}
        for message in check_cross_pack_duplicate_factory_ids(loaded_packs.items())
    )
    result = {
        "summary": {
            "checkedManifests": len(pack_paths),
            "issueCount": len(issues),
            "registryValid": not issues,
        },
        "limitations": [
            "This structural/partial check does not replace JSON Schema or full C# package validation.",
            "AI stageMin/stageMax are pack-local array indices, not runtime progression indices.",
            "Unlisted pools derive eligibility only from stage.unitIds; blueprintPoolEntries do not broaden them.",
            "Cross-pack stage factory IDs use unique mappings from readable manifests; this static report does not prove runtime loading, map assignment, or offers.",
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
