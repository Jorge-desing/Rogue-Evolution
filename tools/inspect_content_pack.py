#!/usr/bin/env python3
"""Inspecciona un content pack y resume su estructura para el cargador.

La función pura :func:`inspect_pack` devuelve un dict con identidad,
conteos, mapeo fábrica->unidad, etapas con sus pools y rangos AI, y un
resumen de localizaciones. El CLI imprime el informe o JSON.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from tools.merge_content_packs import _as_dict, _as_list, _entity_key


def inspect_pack(pack: dict) -> dict:
    pack = _as_dict(pack)
    pack_id = pack.get("packId") if isinstance(pack.get("packId"), str) else None

    unit_factories: list[dict] = []
    unit_ids: list[str] = []
    factory_ids: list[str] = []
    localization_rows: list[dict] = []

    def _loc_summary(entity: dict, kind: str) -> dict:
        eid = _entity_key(entity) or "<unknown>"
        langs = set()
        for loc in _as_list(entity.get("localizations")):
            if isinstance(loc, dict) and isinstance(loc.get("language"), str):
                langs.add(loc["language"])
        return {
            "kind": kind,
            "id": eid,
            "languages": sorted(langs),
            "hasEnglish": "en-US" in langs,
        }

    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        uid = _entity_key(unit)
        if uid:
            unit_ids.append(uid)
        factory = unit.get("productionBuilding")
        fid = _entity_key(factory) if isinstance(factory, dict) else None
        if uid and fid:
            unit_factories.append({"unitId": uid, "factoryId": fid})
        if fid:
            factory_ids.append(fid)
        if uid:
            localization_rows.append(_loc_summary(unit, "unit"))
        if isinstance(factory, dict) and fid:
            localization_rows.append(_loc_summary(factory, "factory"))

    stages: list[dict] = []
    for stage in _as_list(pack.get("stages")):
        if not isinstance(stage, dict):
            continue
        pools = []
        for entry in _as_list(stage.get("blueprintPoolEntries")):
            if isinstance(entry, dict):
                pools.append({
                    "poolId": entry.get("poolId"),
                    "unitIds": sorted(_as_list(entry.get("unitIds"))),
                })
        stages.append({
            "stageId": _entity_key(stage),
            "unitIds": sorted(_as_list(stage.get("unitIds"))),
            "aiProfileIds": sorted(_as_list(stage.get("aiProfileIds"))),
            "pools": pools,
        })

    ai_profiles: list[dict] = []
    for profile in _as_list(pack.get("aiProfiles")):
        if not isinstance(profile, dict):
            continue
        pid = _entity_key(profile)
        if not pid:
            continue
        modules = []
        for module in _as_list(profile.get("modules")):
            if isinstance(module, dict) and module.get("moduleId"):
                modules.append(module["moduleId"])
        ai_profiles.append({
            "profileId": pid,
            "scopeToReferencedStages": bool(profile.get("scopeToReferencedStages")),
            "unitEntries": len(_as_list(profile.get("unitEntries"))),
            "productionBuildingEntries": len(
                _as_list(profile.get("productionBuildingEntries"))
            ),
            "modules": sorted(modules),
        })

    bundles = []
    for bundle in _as_list(pack.get("assetBundles")):
        if isinstance(bundle, dict):
            bundles.append(
                bundle.get("path") or bundle.get("bundlePath") or bundle.get("file") or "<unknown>"
            )
        else:
            bundles.append(str(bundle))

    return {
        "packId": pack_id,
        "schemaVersion": pack.get("schemaVersion"),
        "counts": {
            "units": len(unit_ids),
            "factories": len(factory_ids),
            "stages": len(stages),
            "aiProfiles": len(ai_profiles),
            "assetBundles": len(bundles),
        },
        "unitIds": sorted(unit_ids),
        "factoryIds": sorted(factory_ids),
        "unitFactories": sorted(unit_factories, key=lambda r: r["unitId"]),
        "assetBundles": sorted(bundles),
        "stages": stages,
        "aiProfiles": ai_profiles,
        "localizations": localization_rows,
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Inspecciona un content pack"
    )
    parser.add_argument("--pack", required=True, help="Directorio o content.json")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    args = parser.parse_args(argv)

    pack_path = Path(args.pack)
    if pack_path.is_dir():
        pack_path = pack_path / "content.json"
    if not pack_path.is_file():
        print(f"Archivo no encontrado: {pack_path}", file=sys.stderr)
        return 2

    try:
        pack = json.loads(pack_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        print(f"JSON inválido: {exc}", file=sys.stderr)
        return 2

    report = inspect_pack(pack)
    if args.json:
        print(json.dumps(report, indent=2, ensure_ascii=False))
        return 0

    print(f"packId: {report['packId']} schema={report['schemaVersion']}")
    c = report["counts"]
    print(
        f"counts: units={c['units']} factories={c['factories']} "
        f"stages={c['stages']} aiProfiles={c['aiProfiles']} bundles={c['assetBundles']}"
    )
    for row in report["unitFactories"]:
        print(f"  {row['unitId']} -> {row['factoryId']}")
    for stage in report["stages"]:
        print(
            f"stage {stage['stageId']}: units={stage['unitIds']} "
            f"ai={stage['aiProfileIds']} pools={[p['poolId'] for p in stage['pools']]}"
        )
    for profile in report["aiProfiles"]:
        print(
            f"ai {profile['profileId']}: modules={profile['modules']} "
            f"unitEntries={profile['unitEntries']}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
