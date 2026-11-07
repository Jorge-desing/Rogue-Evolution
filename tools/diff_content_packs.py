#!/usr/bin/env python3
"""Compara dos content packs de Rogue Command y reporta diferencias.

La función pura :func:`diff_packs` devuelve un dict con unidades, etapas y
perfiles AI añadidos, eliminados y modificados, junto con cambios en
metadatos. El CLI imprime un resumen legible.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key


def _index(entities: list) -> dict[str, Any]:
    result = {}
    for entity in entities:
        key = _entity_key(entity)
        if key is not None:
            result[key] = entity
    return result


def _diff_entities(left: list, right: list) -> dict:
    left_index = _index(left)
    right_index = _index(right)
    added = sorted(set(right_index) - set(left_index))
    removed = sorted(set(left_index) - set(right_index))
    changed = sorted(
        key for key in (set(left_index) & set(right_index))
        if left_index[key] != right_index[key]
    )
    return {
        "added": added,
        "removed": removed,
        "changed": changed,
        "addedCount": len(added),
        "removedCount": len(removed),
        "changedCount": len(changed),
    }


def diff_packs(left: dict, right: dict) -> dict:
    left = _as_dict(left)
    right = _as_dict(right)

    units_diff = _diff_entities(
        _as_list(left.get("units")), _as_list(right.get("units"))
    )
    stages_diff = _diff_entities(
        _as_list(left.get("stages")), _as_list(right.get("stages"))
    )
    ai_diff = _diff_entities(
        _as_list(left.get("aiProfiles")), _as_list(right.get("aiProfiles"))
    )

    metadata_changes = {}
    for key in ("packId", "schemaVersion", "displayName", "version"):
        if left.get(key) != right.get(key):
            metadata_changes[key] = {"left": left.get(key), "right": right.get(key)}

    return {
        "metadataChanges": metadata_changes,
        "metadataChangeCount": len(metadata_changes),
        "units": units_diff,
        "stages": stages_diff,
        "aiProfiles": ai_diff,
        "totalChangeCount": (
            units_diff["addedCount"] + units_diff["removedCount"]
            + units_diff["changedCount"]
            + stages_diff["addedCount"] + stages_diff["removedCount"]
            + stages_diff["changedCount"]
            + ai_diff["addedCount"] + ai_diff["removedCount"]
            + ai_diff["changedCount"]
            + len(metadata_changes)
        ),
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Compara dos content packs de Rogue Command"
    )
    parser.add_argument("--left", required=True, help="Directorio o content.json izquierdo")
    parser.add_argument("--right", required=True, help="Directorio o content.json derecho")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    args = parser.parse_args(argv)

    def _resolve(path_str: str) -> Path:
        p = Path(path_str)
        if p.is_dir():
            p = p / "content.json"
        return p

    left_path = _resolve(args.left)
    right_path = _resolve(args.right)
    if not left_path.is_file() or not right_path.is_file():
        print("Archivo no encontrado", file=sys.stderr)
        return 2

    try:
        left = json.loads(left_path.read_text(encoding="utf-8"))
        right = json.loads(right_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        print(f"JSON inválido: {exc}", file=sys.stderr)
        return 2

    result = diff_packs(left, right)

    if args.json:
        print(json.dumps(result, indent=2, ensure_ascii=False))
        return 0

    print(f"packId: {left.get('packId')} -> {right.get('packId')}")
    print(f"Total cambios: {result['totalChangeCount']}")
    for section in ("units", "stages", "aiProfiles"):
        diff = result[section]
        print(
            f"{section}: +{diff['addedCount']} -{diff['removedCount']} "
            f"~{diff['changedCount']}"
        )
        if diff["added"]:
            print(f"  added: {', '.join(diff['added'])}")
        if diff["removed"]:
            print(f"  removed: {', '.join(diff['removed'])}")
        if diff["changed"]:
            print(f"  changed: {', '.join(diff['changed'])}")
    if result["metadataChanges"]:
        print("metadata:")
        for key, change in result["metadataChanges"].items():
            print(f"  {key}: {change['left']} -> {change['right']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
