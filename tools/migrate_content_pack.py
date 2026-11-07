#!/usr/bin/env python3
"""Migra/normaliza un content pack legacy al formato actual del cargador.

La función pura :func:`migrate_pack` devuelve ``(migrated, changes)`` donde
``changes`` lista las normalizaciones aplicadas: schemaVersion ausente,
alias ``unitId``/``buildingId`` a ``id``, y localizaciones como objeto a lista.
El CLI escribe el pack migrado.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from tools.merge_content_packs import _as_list


def _normalize_entity_id(entity: dict, changes: list[str], label: str) -> None:
    if _has_id(entity):
        return
    for alias in ("unitId", "buildingId"):
        value = entity.get(alias)
        if isinstance(value, str) and value.strip():
            entity["id"] = value
            changes.append(f"{label}: alias '{alias}' -> 'id' ({value})")
            return


def _has_id(entity: dict) -> bool:
    value = entity.get("id")
    return isinstance(value, str) and bool(value.strip())


def _normalize_localizations(entity: dict, changes: list[str], label: str) -> None:
    locs = entity.get("localizations")
    if isinstance(locs, dict):
        # Legacy objeto { idioma: {name, description} } -> lista
        normalized = []
        for lang, data in locs.items():
            if isinstance(data, dict):
                entry = {"language": lang}
                if data.get("name") is not None:
                    entry["name"] = data["name"]
                if data.get("description") is not None:
                    entry["description"] = data["description"]
                normalized.append(entry)
            else:
                normalized.append({"language": lang, "name": str(data)})
        entity["localizations"] = normalized
        changes.append(f"{label}: localizations objeto -> lista")


def migrate_pack(pack: dict) -> tuple[dict, list[str]]:
    """Normaliza un manifiesto legacy. Devuelve ``(migrated, changes)``."""
    migrated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    changes: list[str] = []

    if not isinstance(migrated, dict):
        return {}, ["manifiesto raíz no es un objeto"]

    # schemaVersion
    if "schemaVersion" not in migrated:
        migrated["schemaVersion"] = 1
        changes.append("schemaVersion ausente -> 1")

    for unit in _as_list(migrated.get("units")):
        if not isinstance(unit, dict):
            continue
        uid = unit.get("id") or unit.get("unitId") or "<unknown>"
        _normalize_entity_id(unit, changes, f"unit '{uid}'")
        _normalize_localizations(unit, changes, f"unit '{uid}'")
        factory = unit.get("productionBuilding")
        if isinstance(factory, dict):
            _normalize_entity_id(factory, changes, f"factory de '{uid}'")
            _normalize_localizations(factory, changes, f"factory de '{uid}'")

    for stage in _as_list(migrated.get("stages")):
        if isinstance(stage, dict):
            _normalize_entity_id(stage, changes, "stage")

    for profile in _as_list(migrated.get("aiProfiles")):
        if isinstance(profile, dict):
            _normalize_entity_id(profile, changes, "aiProfile")

    return migrated, changes


def write_migrated(migrated: dict, out_dir: str | Path) -> Path:
    out_path = Path(out_dir)
    out_path.mkdir(parents=True, exist_ok=True)
    target = out_path / "content.json"
    target.write_text(
        json.dumps(migrated, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    return target


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Migra/normaliza un content pack legacy"
    )
    parser.add_argument("--pack", required=True, help="Directorio o content.json")
    parser.add_argument("--out-dir", required=True, help="Directorio destino")
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

    migrated, changes = migrate_pack(pack)
    if not migrated:
        return 1
    written = write_migrated(migrated, args.out_dir)
    print(f"Pack migrado escrito en {written}")
    if changes:
        print("Normalizaciones:")
        for c in changes:
            print(f"  - {c}")
    else:
        print("Sin cambios necesarios.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
