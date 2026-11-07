#!/usr/bin/env python3
"""Añade o elimina una unidad (y su fábrica) de un content pack.

Funciones puras :func:`add_unit` y :func:`remove_unit` que mantienen
consistentes etapas, blueprint pools y perfiles AI. El CLI aplica la operación
y escribe el pack.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key


def _unit_index(pack: dict) -> dict[str, dict]:
    index = {}
    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        uid = _entity_key(unit)
        if uid:
            index[uid] = unit
    return index


def _factory_index(pack: dict) -> dict[str, dict]:
    index = {}
    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        factory = unit.get("productionBuilding")
        if isinstance(factory, dict):
            fid = _entity_key(factory)
            if fid:
                index[fid] = factory
    return index


def add_unit(
    pack: dict, unit: dict, stage_ids: list[str] | None = None
) -> tuple[dict, list[str]]:
    """Añade ``unit`` al pack. Devuelve ``(pack, diagnostics)``.

    Si ``stage_ids`` se indica, la unidad se registra en esas etapas
    (``unitIds``). Los diagnósticos informan de IDs duplicados o etapas
    desconocidas.
    """
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    diagnostics: list[str] = []

    if not isinstance(unit, dict):
        return updated, ["unit debe ser un objeto"]

    unit_id = _entity_key(unit)
    if not unit_id:
        return updated, ["unit.id vacío o ausente"]

    if unit_id in _unit_index(updated):
        return updated, [f"unit '{unit_id}' ya existe en el pack"]

    factory = unit.get("productionBuilding")
    if not isinstance(factory, dict) or not _entity_key(factory):
        diagnostics.append(
            f"unit '{unit_id}': productionBuilding ausente o id vacío (se exige 1:1)"
        )
    else:
        factory_id = _entity_key(factory)
        if factory_id in _factory_index(updated):
            return updated, [
                f"factory '{factory_id}' ya está en uso (se exige 1:1)"
            ]
        if factory_id in _unit_index(updated):
            return updated, [
                f"factory '{factory_id}' colisiona con un unit id existente"
            ]

    updated.setdefault("units", [])
    updated["units"].append(unit)

    if stage_ids:
        stages = {
            _entity_key(s): s
            for s in _as_list(updated.get("stages"))
            if isinstance(s, dict) and _entity_key(s)
        }
        for sid in stage_ids:
            if sid not in stages:
                diagnostics.append(f"etapa desconocida '{sid}' (se omite)")
                continue
            stage_units = stages[sid].setdefault("unitIds", [])
            if unit_id not in stage_units:
                stage_units.append(unit_id)

    return updated, diagnostics


def remove_unit(pack: dict, unit_id: str) -> tuple[dict, list[str]]:
    """Elimina una unidad y sus referencias. Devuelve ``(pack, diagnostics)``."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    diagnostics: list[str] = []

    if unit_id not in _unit_index(updated):
        return updated, [f"unit '{unit_id}' no existe en el pack"]

    factory = _unit_index(updated)[unit_id].get("productionBuilding")
    factory_id = _entity_key(factory) if isinstance(factory, dict) else None

    updated["units"] = [
        u for u in _as_list(updated.get("units"))
        if _entity_key(u) != unit_id
    ]

    removed_ids = {unit_id} | ({factory_id} if factory_id else set())

    def _purge_id_list(value):
        if isinstance(value, list):
            return [v for v in value if not (isinstance(v, str) and v in removed_ids)]
        return value

    # Stages: purge unitIds and blueprint pool unitIds
    for stage in _as_list(updated.get("stages")):
        if not isinstance(stage, dict):
            continue
        stage["unitIds"] = _purge_id_list(stage.get("unitIds"))
        for pool in _as_list(stage.get("blueprintPoolEntries")):
            if isinstance(pool, dict):
                pool["unitIds"] = _purge_id_list(pool.get("unitIds"))

    # AI profiles: drop entries whose unitId (scalar or list) is removed
    for profile in _as_list(updated.get("aiProfiles")):
        if not isinstance(profile, dict):
            continue
        for section in ("unitEntries", "productionBuildingEntries"):
            entries = profile.get(section)
            if not isinstance(entries, list):
                continue
            kept = []
            for entry in entries:
                if not isinstance(entry, dict):
                    kept.append(entry)
                    continue
                scalar = entry.get("unitId")
                scalar_removed = isinstance(scalar, str) and scalar in removed_ids
                list_ids = entry.get("unitIds")
                list_removed = isinstance(list_ids, list) and any(
                    isinstance(v, str) and v in removed_ids for v in list_ids
                )
                if scalar_removed or list_removed:
                    continue
                kept.append(entry)
            profile[section] = kept

    diagnostics.append(f"unit '{unit_id}' eliminada con sus referencias")
    return updated, diagnostics


def write_pack(pack: dict, out_dir: str | Path) -> Path:
    out_path = Path(out_dir)
    out_path.mkdir(parents=True, exist_ok=True)
    target = out_path / "content.json"
    target.write_text(
        json.dumps(pack, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    return target


def _load_pack(path_str: str) -> tuple[dict | None, str | None]:
    p = Path(path_str)
    if p.is_dir():
        p = p / "content.json"
    if not p.is_file():
        return None, f"Archivo no encontrado: {p}"
    try:
        return json.loads(p.read_text(encoding="utf-8")), None
    except json.JSONDecodeError as exc:
        return None, f"JSON inválido: {exc}"


def main(argv: list[str] | None = None) -> int:
    import argparse

    parser = argparse.ArgumentParser(
        description="Añade o elimina una unidad de un content pack"
    )
    parser.add_argument("--pack", required=True, help="Directorio o content.json")
    parser.add_argument("--operation", choices=["add", "remove"], required=True)
    parser.add_argument("--unit-id", help="ID de la unidad (para remove)")
    parser.add_argument("--unit-json", help="Ruta a un unit.json (para add)")
    parser.add_argument("--stage-ids", nargs="*", default=None,
                        help="Etapas donde registrar la unidad (add)")
    parser.add_argument("--out-dir", required=True, help="Directorio destino")
    args = parser.parse_args(argv)

    pack, error = _load_pack(args.pack)
    if error:
        print(error, file=sys.stderr)
        return 2

    if args.operation == "add":
        if not args.unit_json:
            print("--unit-json es obligatorio para add", file=sys.stderr)
            return 2
        unit_path = Path(args.unit_json)
        if not unit_path.is_file():
            print(f"Archivo no encontrado: {unit_path}", file=sys.stderr)
            return 2
        try:
            unit = json.loads(unit_path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as exc:
            print(f"JSON inválido: {exc}", file=sys.stderr)
            return 2
        updated, diagnostics = add_unit(pack, unit, stage_ids=args.stage_ids)
        if any(d.endswith("ya existe en el pack") or "ya está en uso" in d for d in diagnostics):
            for d in diagnostics:
                print(f"  ! {d}", file=sys.stderr)
            return 1
    else:
        if not args.unit_id:
            print("--unit-id es obligatorio para remove", file=sys.stderr)
            return 2
        updated, diagnostics = remove_unit(pack, args.unit_id)
        if not updated.get("units", []) and any("no existe" in d for d in diagnostics):
            for d in diagnostics:
                print(f"  ! {d}", file=sys.stderr)
            return 1

    for d in diagnostics:
        print(f"  ! {d}")
    written = write_pack(updated, args.out_dir)
    print(f"Pack escrito en {written}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
