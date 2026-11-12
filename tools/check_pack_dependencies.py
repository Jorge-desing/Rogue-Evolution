#!/usr/bin/env python3
"""Analiza dependencias entre packs de contenido.

La función pura :func:`analyze_dependencies` recibe ``{nombre: pack}`` y
resuelve, por pack, qué referencias namespaced provienen de otros packs
(``unitIds``, entradas de pool, ``aiProfileIds``, ``unitEntries``,
``productionBuildingEntries``, ``unitDependencies``, cambios de relictia
restringidos y ``copyUpgradesToEntityId``), quién las define
(``resolvedBy``) y cuáles no resuelven en ningún pack. Los IDs nativos sin
``:`` (plantillas del juego) no cuentan como dependencia externa. Conteos
antes que el detalle.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from tools.merge_content_packs import _as_dict, _as_list, _entity_key

_SECTIONS = ("units", "stages", "aiProfiles", "relics", "buildings",
             "assetBundles", "upgrades")


def _is_namespaced(value) -> bool:
    return isinstance(value, str) and ":" in value


def defined_ids(pack: dict) -> set[str]:
    """IDs namespaced definidos por el pack (unidades, fábricas, …)."""
    pack = _as_dict(pack)
    ids: set[str] = set()
    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        uid = _entity_key(unit)
        if uid:
            ids.add(uid)
        factory = unit.get("productionBuilding")
        if isinstance(factory, dict):
            fid = _entity_key(factory)
            if fid:
                ids.add(fid)
    for section in _SECTIONS:
        if section == "units":
            continue
        for entity in _as_list(pack.get(section)):
            if isinstance(entity, dict):
                eid = _entity_key(entity)
                if eid:
                    ids.add(eid)
    return ids


def external_references(pack: dict) -> set[str]:
    """Referencias namespaced que el pack no define."""
    pack = _as_dict(pack)
    defined = defined_ids(pack)
    refs: set[str] = set()

    def _add(value) -> None:
        if _is_namespaced(value):
            refs.add(value)

    for unit in _as_list(pack.get("units")):
        if isinstance(unit, dict):
            _add(unit.get("copyUpgradesToEntityId"))
    for stage in _as_list(pack.get("stages")):
        if not isinstance(stage, dict):
            continue
        for value in _as_list(stage.get("unitIds")):
            _add(value)
        for entry in _as_list(stage.get("blueprintPoolEntries")):
            if isinstance(entry, dict):
                for value in _as_list(entry.get("unitIds")):
                    _add(value)
        for value in _as_list(stage.get("aiProfileIds")):
            _add(value)
    for profile in _as_list(pack.get("aiProfiles")):
        if not isinstance(profile, dict):
            continue
        for section in ("unitEntries", "productionBuildingEntries",
                        "unitDependencies"):
            for entry in _as_list(profile.get(section)):
                if isinstance(entry, dict):
                    _add(entry.get("unitId"))
    for relic in _as_list(pack.get("relics")) + _as_list(pack.get("upgrades")):
        if not isinstance(relic, dict):
            continue
        for change in _as_list(relic.get("cardChanges")):
            if isinstance(change, dict):
                for key in ("onlyForTheseEntityIds", "entityIdsNotAllowed"):
                    for value in _as_list(change.get(key)):
                        _add(value)
    return {ref for ref in refs if ref not in defined}


def analyze_dependencies(packs: dict) -> dict:
    """Resuelve dependencias externas entre varias packs (conteos primero)."""
    providers: dict[str, list[str]] = {}
    for name, pack in packs.items():
        for entity_id in defined_ids(pack):
            providers.setdefault(entity_id, []).append(name)
    rows: list[dict] = []
    total_unresolved = 0
    for name in sorted(packs):
        external = external_references(packs[name])
        resolved_by = sorted({
            provider
            for ref in external
            for provider in providers.get(ref, [])
            if provider != name
        })
        unresolved = sorted(
            ref for ref in external if ref not in providers
        )
        total_unresolved += len(unresolved)
        rows.append({
            "name": name,
            "packId": _as_dict(packs[name]).get("packId"),
            "externalReferences": sorted(external),
            "resolvedBy": resolved_by,
            "unresolvedReferences": unresolved,
        })
    return {
        "counts": {
            "packs": len(rows),
            "packsWithExternalDeps": sum(1 for r in rows if r["externalReferences"]),
            "unresolvedReferences": total_unresolved,
        },
        "packs": rows,
    }


def _load_all(packs_dir: Path) -> dict[str, dict]:
    packs: dict[str, dict] = {}
    for content_path in sorted(packs_dir.glob("*/content.json")):
        packs[content_path.parent.name] = json.loads(
            content_path.read_text(encoding="utf-8"))
    return packs


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Analiza dependencias entre content packs")
    parser.add_argument("target", nargs="?", default=None,
                        help="Directorio de packs o content.json múltiple")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    args = parser.parse_args(argv)
    if args.target and Path(args.target).is_dir() and (Path(args.target) / "content.json").exists():
        packs = {Path(args.target).name: json.loads(
            (Path(args.target) / "content.json").read_text(encoding="utf-8"))}
    elif args.target:
        packs = _load_all(Path(args.target))
    else:
        packs = _load_all(Path("content/packs"))
    try:
        report = analyze_dependencies(packs)
    except (OSError, ValueError) as exc:
        print(f"packs no legibles: {exc}")
        return 1
    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2))
    else:
        print(f"packs={report['counts']['packs']} "
              f"conDependencias={report['counts']['packsWithExternalDeps']} "
              f"sinResolver={report['counts']['unresolvedReferences']}")
        for row in report["packs"]:
            print(f"  {row['name']}: externas={row['externalReferences']} "
                  f"resueltopor={row['resolvedBy']} "
                  f"pendientes={row['unresolvedReferences']}")
    return 1 if report["counts"]["unresolvedReferences"] else 0


if __name__ == "__main__":
    sys.exit(main())
