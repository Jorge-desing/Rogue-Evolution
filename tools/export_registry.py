#!/usr/bin/env python3
"""Exporta el registro global de contenido (todos los IDs de todos los packs).

La función pura :func:`export_registry` recorre ``content/packs`` y devuelve
un inventario ordenado de unidades, fábricas, etapas, perfiles AI, relics y
bundles, con su pack de origen y las referencias entrantes. El CLI imprime
JSON o un resumen.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key


def _collect(pack: dict, pack_folder: str, registry: dict) -> None:
    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        uid = _entity_key(unit)
        if not uid:
            continue
        factory = unit.get("productionBuilding")
        fid = _entity_key(factory) if isinstance(factory, dict) else None
        entry = {
            "id": uid,
            "kind": "unit",
            "packFolder": pack_folder,
            "packId": pack.get("packId"),
            "factoryId": fid,
            "nativeTemplateId": unit.get("nativeTemplateId"),
        }
        registry.setdefault(uid, entry)
        if fid and fid not in registry:
            registry[fid] = {
                "id": fid,
                "kind": "factory",
                "packFolder": pack_folder,
                "packId": pack.get("packId"),
                "produces": uid,
            }

    for stage in _as_list(pack.get("stages")):
        if isinstance(stage, dict) and _entity_key(stage):
            registry.setdefault(_entity_key(stage), {
                "id": _entity_key(stage), "kind": "stage",
                "packFolder": pack_folder, "packId": pack.get("packId"),
                "nativeTemplateId": stage.get("nativeTemplateId"),
            })

    for profile in _as_list(pack.get("aiProfiles")):
        if isinstance(profile, dict) and _entity_key(profile):
            registry.setdefault(_entity_key(profile), {
                "id": _entity_key(profile), "kind": "aiProfile",
                "packFolder": pack_folder, "packId": pack.get("packId"),
                "nativeTemplateId": profile.get("nativeTemplateId"),
            })

    for relic in _as_list(pack.get("relics")):
        if isinstance(relic, dict) and _entity_key(relic):
            registry.setdefault(_entity_key(relic), {
                "id": _entity_key(relic), "kind": "relic",
                "packFolder": pack_folder, "packId": pack.get("packId"),
            })

    for bundle in _as_list(pack.get("assetBundles")):
        if isinstance(bundle, dict) and _entity_key(bundle):
            registry.setdefault(_entity_key(bundle), {
                "id": _entity_key(bundle), "kind": "bundle",
                "packFolder": pack_folder, "packId": pack.get("packId"),
            })


def _incoming_references(registry: dict) -> dict[str, list[str]]:
    """Para cada ID, lista los IDs que lo referencian (origen -> destino)."""
    incoming: dict[str, list[str]] = {key: [] for key in registry}
    for uid, entry in list(registry.items()):
        if entry.get("kind") != "unit":
            continue
        fid = entry.get("factoryId")
        if fid in incoming:
            incoming[fid].append(uid)
    return incoming


def export_registry(packs_root: Path) -> dict:
    """Exporta el inventario global de ``packs_root``."""
    root = Path(packs_root)
    pack_paths = sorted(root.glob("*/content.json"))
    registry: dict[str, dict] = {}
    load_errors: dict[str, str] = {}

    for path in pack_paths:
        try:
            pack = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
            load_errors[path.parent.name] = str(exc)
            continue
        _collect(pack, path.parent.name, registry)

    entries = [registry[k] for k in sorted(registry)]
    incoming = _incoming_references(registry)

    kind_counts: dict[str, int] = {}
    for entry in entries:
        kind_counts[entry["kind"]] = kind_counts.get(entry["kind"], 0) + 1

    packs = sorted({e.get("packFolder") for e in entries if e.get("packFolder")})

    return {
        "summary": {
            "checkedManifests": len(pack_paths),
            "totalIds": len(entries),
            "kinds": kind_counts,
            "packs": packs,
            "loadErrors": load_errors,
            "registryValid": not load_errors,
        },
        "limitations": [
            "Inventario offline estático; no prueba carga en runtime.",
            "Las referencias entrantes listan solo el mapeo fábrica->unidad.",
        ],
        "entries": entries,
        "incoming": incoming,
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Exporta el registro global de IDs de content packs"
    )
    parser.add_argument("--packs-root", default="content/packs")
    parser.add_argument("--pack", help="Inspecciona solo un pack (dir o content.json)")
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)

    if args.pack:
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
        registry: dict[str, dict] = {}
        _collect(pack, pack_path.parent.name, registry)
        result = {
            "summary": {"totalIds": len(registry)},
            "entries": [registry[k] for k in sorted(registry)],
            "incoming": _incoming_references(registry),
        }
    else:
        result = export_registry(Path(args.packs_root))

    if args.json:
        print(json.dumps(result, indent=2, ensure_ascii=False))
        return 0

    summary = result["summary"]
    print(f"packs={summary['checkedManifests']} ids={summary['totalIds']} "
          f"kinds={summary['kinds']}")
    for entry in result["entries"][:20]:
        print(f"  {entry['kind']}: {entry['id']} ({entry.get('packFolder')})")
    if len(result["entries"]) > 20:
        print(f"  ... +{len(result['entries']) - 20} más")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
