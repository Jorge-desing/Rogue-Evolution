#!/usr/bin/env python3
"""Evalúa presupuestos de contenido: tamaño, IDs, dependencias y cobertura.

La función pura :func:`pack_stats` devuelve conteos por plantilla nativa,
relics con sus card changes, bundles, localizaciones bilingües y
relaciones de dependencia (etapas->perfiles->módulos).
"""
from __future__ import annotations

import argparse
import json
import sys
from collections import Counter
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key


def _count_by_template(entities: list, field: str) -> dict:
    counts: Counter = Counter()
    for entity in entities:
        if isinstance(entity, dict) and isinstance(entity.get(field), str):
            counts[entity[field]] += 1
    return dict(sorted(counts.items()))


def _localization_stats(entities: list) -> dict:
    total = 0
    bilingual = 0
    english = 0
    for entity in entities:
        if not isinstance(entity, dict):
            continue
        total += 1
        langs = {
            loc["language"]
            for loc in _as_list(entity.get("localizations"))
            if isinstance(loc, dict) and isinstance(loc.get("language"), str)
        }
        if "en-US" in langs:
            english += 1
        if "en-US" in langs and "es-XL" in langs:
            bilingual += 1
    return {
        "entities": total,
        "english": english,
        "bilingual": bilingual,
        "englishPercent": (100.0 * english / total) if total else 0.0,
        "bilingualPercent": (100.0 * bilingual / total) if total else 0.0,
    }


def pack_stats(pack: dict) -> dict:
    pack = _as_dict(pack)
    units = [u for u in _as_list(pack.get("units")) if isinstance(u, dict)]
    factories = [
        u.get("productionBuilding")
        for u in units if isinstance(u.get("productionBuilding"), dict)
    ]
    stages = [s for s in _as_list(pack.get("stages")) if isinstance(s, dict)]
    profiles = [p for p in _as_list(pack.get("aiProfiles")) if isinstance(p, dict)]
    relics = [r for r in _as_list(pack.get("relics")) if isinstance(r, dict)]

    card_ops: Counter = Counter()
    for relic in relics:
        for change in _as_list(relic.get("cardChanges")):
            if isinstance(change, dict) and isinstance(change.get("operation"), str):
                card_ops[change["operation"]] += 1

    modules: Counter = Counter()
    for profile in profiles:
        for module in _as_list(profile.get("modules")):
            if isinstance(module, dict) and isinstance(module.get("moduleId"), str):
                modules[module["moduleId"]] += 1

    bundles = _as_list(pack.get("assetBundles"))

    return {
        "packId": pack.get("packId"),
        "counts": {
            "units": len(units),
            "factories": len(factories),
            "stages": len(stages),
            "aiProfiles": len(profiles),
            "relics": len(relics),
            "bundles": len(bundles),
        },
        "unitTemplates": _count_by_template(units, "nativeTemplateId"),
        "stageTemplates": _count_by_template(stages, "nativeTemplateId"),
        "relicCardOperations": dict(sorted(card_ops.items())),
        "aiModules": dict(sorted(modules.items())),
        "localization": {
            "units": _localization_stats(units),
            "factories": _localization_stats(factories),
        },
        "bundlePaths": [
            b.get("path") or b.get("bundlePath") or b.get("id")
            for b in bundles if isinstance(b, dict)
        ],
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Estadísticas y presupuestos de un content pack"
    )
    parser.add_argument("--pack", required=True, help="Directorio o content.json")
    parser.add_argument("--json", action="store_true")
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

    stats = pack_stats(pack)
    if args.json:
        print(json.dumps(stats, indent=2, ensure_ascii=False))
        return 0

    print(f"pack={stats['packId']} counts={stats['counts']}")
    print(f"unitTemplates={stats['unitTemplates']}")
    print(f"stageTemplates={stats['stageTemplates']}")
    print(f"relicCardOperations={stats['relicCardOperations']}")
    print(f"aiModules={stats['aiModules']}")
    loc = stats["localization"]["units"]
    print(f"units localization: en={loc['english']}/{loc['entities']} "
          f"bilingüe={loc['bilingual']}/{loc['entities']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
