#!/usr/bin/env python3
"""Normaliza canónicamente un content pack de Rogue Command.

La función pura :func:`normalize_pack` devuelve copia determinista: asegura
las secciones vacías, ordena las entidades por ID, ordena y deduplica las
listas de referencias (``unitIds``, entradas de pool, ``parameterOverrides``)
y serializa los valores de ``balanceOverrides`` como cadenas. Nunca muta el
paquete de entrada. :func:`canonical_json` produce la forma serializable
estable para diffs y revisiones.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key

_SECTIONS = ("units", "stages", "aiProfiles", "relics", "buildings",
             "assetBundles", "upgrades")


def _sort_by_id(items: list[dict]) -> list[dict]:
    return sorted(items, key=lambda e: _entity_key(e) or "")


def _normalize_overrides(overrides: Any) -> list[dict]:
    cleaned = []
    for override in _as_list(overrides):
        if not isinstance(override, dict) or not override.get("name"):
            continue
        value = override.get("value")
        cleaned.append({"name": str(override["name"]),
                        "value": "" if value is None else str(value)})
    cleaned.sort(key=lambda o: o["name"])
    return cleaned


def _normalize_unit(unit: dict) -> dict:
    if isinstance(unit.get("balanceOverrides"), list):
        unit["balanceOverrides"] = _normalize_overrides(unit["balanceOverrides"])
    factory = unit.get("productionBuilding")
    if isinstance(factory, dict) and isinstance(factory.get("balanceOverrides"), list):
        factory["balanceOverrides"] = _normalize_overrides(factory["balanceOverrides"])
    return unit


def _normalize_stage(stage: dict) -> dict:
    if isinstance(stage.get("parameterOverrides"), list):
        stage["parameterOverrides"] = _normalize_overrides(stage["parameterOverrides"])
    if isinstance(stage.get("unitIds"), list):
        unique: list[str] = []
        for value in stage["unitIds"]:
            if isinstance(value, str) and value not in unique:
                unique.append(value)
        stage["unitIds"] = sorted(unique)
    if isinstance(stage.get("blueprintPoolEntries"), list):
        entries = []
        for entry in stage["blueprintPoolEntries"]:
            if not isinstance(entry, dict):
                continue
            ids = entry.get("unitIds")
            if isinstance(ids, list):
                entry = dict(entry)
                entry["unitIds"] = sorted({v for v in ids if isinstance(v, str)})
            entries.append(entry)
        entries.sort(key=lambda e: str(e.get("poolId", "")))
        stage["blueprintPoolEntries"] = entries
    if isinstance(stage.get("aiProfileIds"), list):
        stage["aiProfileIds"] = sorted(
            {v for v in stage["aiProfileIds"] if isinstance(v, str)}
        )
    return stage


def normalize_pack(pack: dict) -> dict:
    """Devuelve una copia canónica de ``pack`` (entrada intacta)."""
    source = _as_dict(pack)
    out: dict[str, Any] = json.loads(json.dumps(source))
    for section in _SECTIONS:
        out[section] = [e for e in _as_list(out.get(section)) if isinstance(e, dict)]
        out[section] = _sort_by_id(out[section])
    out["units"] = [_normalize_unit(u) for u in out["units"]]
    out["stages"] = [_normalize_stage(s) for s in out["stages"]]
    return out


def canonical_json(pack: dict) -> str:
    """Serialización determinista de la forma normalizada."""
    return json.dumps(normalize_pack(pack), ensure_ascii=False,
                      indent=2, sort_keys=True) + "\n"


def is_canonical(pack: dict) -> bool:
    """``True`` cuando el pack ya está en forma canónica."""
    return canonical_json(pack) == canonical_json(normalize_pack(pack))


def _resolve_pack(path: Path) -> Path:
    return path / "content.json" if path.is_dir() else path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Normaliza canónicamente un content pack"
    )
    parser.add_argument("target", help="Directorio de pack o content.json")
    parser.add_argument("--check", action="store_true",
                        help="Sólo comprueba (sale 1 si no es canónico)")
    parser.add_argument("--write", action="store_true",
                        help="Reescribe el archivo en forma canónica")
    args = parser.parse_args(argv)
    content_path = _resolve_pack(Path(args.target))
    try:
        pack = json.loads(content_path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        print(f"pack no legible: {exc}")
        return 1
    canonical = canonical_json(pack)
    if args.check:
        current = content_path.read_text(encoding="utf-8")
        if current == canonical:
            print(f"{content_path}: canónico")
            return 0
        print(f"{content_path}: no canónico")
        return 1
    if args.write:
        content_path.write_text(canonical, encoding="utf-8")
        print(f"{content_path}: normalizado")
        return 0
    print(canonical, end="")
    return 0


if __name__ == "__main__":
    sys.exit(main())
