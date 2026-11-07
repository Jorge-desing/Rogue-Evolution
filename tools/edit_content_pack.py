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

        # Module parameters that reference the removed factory/unit (entityId)
        for module in _as_list(profile.get("modules")):
            if not isinstance(module, dict):
                continue
            params = module.get("parameters")
            if not isinstance(params, list):
                continue
            module["parameters"] = [
                p for p in params
                if not (isinstance(p, dict) and p.get("value") in removed_ids)
            ]

    diagnostics.append(f"unit '{unit_id}' eliminada con sus referencias")
    return updated, diagnostics


# ---------------------------------------------------------------------------
# Operaciones avanzadas de edición
# ---------------------------------------------------------------------------

UNIT_BOOL_FLAGS = (
    "isAllowedAsStartingBlueprint",
    "isAllowedForAi",
    "isAllowedForDemo",
    "isForSpecialists",
)


def _find_unit(pack: dict, unit_id: str) -> dict | None:
    return _unit_index(pack).get(unit_id)


def _resolve_entity(pack: dict, unit_id: str, factory: bool) -> dict | None:
    unit = _find_unit(pack, unit_id)
    if unit is None:
        return None
    if not factory:
        return unit
    build = unit.get("productionBuilding")
    return build if isinstance(build, dict) else None


def set_unit_flag(
    pack: dict, unit_id: str, flag: str, value: bool
) -> tuple[dict, list[str]]:
    """Fija un flag booleano de unidad. Devuelve ``(pack, diagnostics)``."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    if flag not in UNIT_BOOL_FLAGS:
        return updated, [
            f"flag desconocido '{flag}' (permitidos: {', '.join(UNIT_BOOL_FLAGS)})"
        ]
    if not isinstance(value, bool):
        return updated, [f"value de '{flag}' debe ser booleano"]
    unit = _find_unit(updated, unit_id)
    if unit is None:
        return updated, [f"unit '{unit_id}' no existe en el pack"]
    unit[flag] = value
    return updated, []


def set_balance_override(
    pack: dict, unit_id: str, name: str, value: Any, factory: bool = False
) -> tuple[dict, list[str]]:
    """Inserta o actualiza un ``balanceOverride`` (unidad o fábrica)."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    if not isinstance(name, str) or not name.strip():
        return updated, ["name de balanceOverride vacío"]
    entity = _resolve_entity(updated, unit_id, factory)
    if entity is None:
        return updated, [f"unit '{unit_id}' no existe (o sin factory) en el pack"]
    overrides = entity.setdefault("balanceOverrides", [])
    string_value = value if isinstance(value, str) else str(value)
    for entry in overrides:
        if isinstance(entry, dict) and entry.get("name") == name:
            entry["value"] = string_value
            return updated, []
    overrides.append({"name": name, "value": string_value})
    return updated, []


def remove_balance_override(
    pack: dict, unit_id: str, name: str, factory: bool = False
) -> tuple[dict, list[str]]:
    """Elimina un ``balanceOverride`` por nombre (unidad o fábrica)."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    entity = _resolve_entity(updated, unit_id, factory)
    if entity is None:
        return updated, [f"unit '{unit_id}' no existe (o sin factory) en el pack"]
    overrides = entity.get("balanceOverrides", [])
    kept = [
        e for e in _as_list(overrides)
        if not (isinstance(e, dict) and e.get("name") == name)
    ]
    if len(kept) == len(_as_list(overrides)):
        return updated, [f"balanceOverride '{name}' no encontrado en '{unit_id}'"]
    entity["balanceOverrides"] = kept
    return updated, []


def set_stage_map_size(
    pack: dict, stage_id: str, min_size: int, max_size: int
) -> tuple[dict, list[str]]:
    """Fija el rango de tamaño del mapa de una etapa (1..64, min<=max)."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    if not isinstance(min_size, int) or not isinstance(max_size, int):
        return updated, ["map sizes deben ser enteros"]
    if min_size < 1 or max_size > 128:
        return updated, ["map size fuera de rango 1..128"]
    if max_size < min_size:
        return updated, ["mapSizeMax debe ser >= mapSizeMin"]

    stage = None
    for candidate in _as_list(updated.get("stages")):
        if isinstance(candidate, dict) and _entity_key(candidate) == stage_id:
            stage = candidate
            break
    if stage is None:
        return updated, [f"stage '{stage_id}' no existe en el pack"]
    stage["mapSizeMin"] = min_size
    stage["mapSizeMax"] = max_size
    return updated, []


def add_pool_entry(
    pack: dict, stage_id: str, pool_id: str, unit_ids: list[str]
) -> tuple[dict, list[str]]:
    """Crea o actualiza una entrada de blueprint pool en una etapa."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    if not isinstance(pool_id, str) or not pool_id.strip():
        return updated, ["poolId vacío"]
    valid_units = _unit_index(updated)
    for uid in unit_ids:
        if uid not in valid_units and uid not in _factory_index(updated):
            return updated, [f"unit '{uid}' no definida en el pack"]

    stage = None
    for candidate in _as_list(updated.get("stages")):
        if isinstance(candidate, dict) and _entity_key(candidate) == stage_id:
            stage = candidate
            break
    if stage is None:
        return updated, [f"stage '{stage_id}' no existe en el pack"]

    pools = stage.setdefault("blueprintPoolEntries", [])
    for entry in pools:
        if isinstance(entry, dict) and entry.get("poolId") == pool_id:
            entry["unitIds"] = list(unit_ids)
            return updated, []
    pools.append({"poolId": pool_id, "unitIds": list(unit_ids)})
    return updated, []


def remove_pool_entry(
    pack: dict, stage_id: str, pool_id: str
) -> tuple[dict, list[str]]:
    """Elimina una entrada de blueprint pool de una etapa."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    stage = None
    for candidate in _as_list(updated.get("stages")):
        if isinstance(candidate, dict) and _entity_key(candidate) == stage_id:
            stage = candidate
            break
    if stage is None:
        return updated, [f"stage '{stage_id}' no existe en el pack"]
    pools = _as_list(stage.get("blueprintPoolEntries"))
    kept = [p for p in pools if not (isinstance(p, dict) and p.get("poolId") == pool_id)]
    if len(kept) == len(pools):
        return updated, [f"pool '{pool_id}' no encontrado en stage '{stage_id}'"]
    stage["blueprintPoolEntries"] = kept
    return updated, []


def set_ai_module_param(
    pack: dict, profile_id: str, module_id: str, param_name: str, value: Any
) -> tuple[dict, list[str]]:
    """Inserta o actualiza un parámetro de un módulo AI de un perfil."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    if not isinstance(param_name, str) or not param_name.strip():
        return updated, ["param_name vacío"]
    profile = None
    for candidate in _as_list(updated.get("aiProfiles")):
        if isinstance(candidate, dict) and _entity_key(candidate) == profile_id:
            profile = candidate
            break
    if profile is None:
        return updated, [f"ai profile '{profile_id}' no existe en el pack"]

    module = None
    for candidate in _as_list(profile.get("modules")):
        if isinstance(candidate, dict) and candidate.get("moduleId") == module_id:
            module = candidate
            break
    if module is None:
        return updated, [f"módulo '{module_id}' no encontrado en '{profile_id}'"]

    params = module.setdefault("parameters", [])
    string_value = value if isinstance(value, str) else str(value)
    for entry in params:
        if isinstance(entry, dict) and entry.get("name") == param_name:
            entry["value"] = string_value
            return updated, []
    params.append({"name": param_name, "value": string_value})
    return updated, []


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
        description="Edita un content pack (unidades, flags, balance, etapas, pools, módulos AI)"
    )
    parser.add_argument("--pack", required=True, help="Directorio o content.json")
    parser.add_argument(
        "--operation",
        choices=[
            "add", "remove", "set-flag", "set-balance", "remove-balance",
            "set-map-size", "add-pool", "remove-pool", "set-ai-param",
        ],
        required=True,
    )
    parser.add_argument("--unit-id", help="ID de la unidad")
    parser.add_argument("--unit-json", help="Ruta a un unit.json (para add)")
    parser.add_argument("--stage-ids", nargs="*", default=None,
                        help="Etapas donde registrar la unidad (add)")
    # set-flag
    parser.add_argument("--flag", help="Flag booleano (set-flag)")
    parser.add_argument("--flag-value", choices=["true", "false"],
                        help="Valor del flag (set-flag)")
    # balance
    parser.add_argument("--balance-name", help="Nombre del balanceOverride")
    parser.add_argument("--balance-value", help="Valor del balanceOverride")
    parser.add_argument("--factory", action="store_true", help="Apunta a la fábrica")
    # map size
    parser.add_argument("--stage-id", help="ID de la etapa")
    parser.add_argument("--map-min", type=int, help="mapSizeMin")
    parser.add_argument("--map-max", type=int, help="mapSizeMax")
    # pools
    parser.add_argument("--pool-id", help="ID del blueprint pool")
    parser.add_argument("--pool-unit-ids", nargs="*", default=None,
                        help="IDs de unidad del pool")
    # ai param
    parser.add_argument("--profile-id", help="ID del perfil AI")
    parser.add_argument("--module-id", help="ID del módulo AI")
    parser.add_argument("--param-name", help="Nombre del parámetro")
    parser.add_argument("--param-value", help="Valor del parámetro")
    parser.add_argument("--out-dir", required=True, help="Directorio destino")
    args = parser.parse_args(argv)

    pack, error = _load_pack(args.pack)
    if error:
        print(error, file=sys.stderr)
        return 2

    updated = None
    diagnostics: list[str] = []

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
    elif args.operation == "remove":
        if not args.unit_id:
            print("--unit-id es obligatorio para remove", file=sys.stderr)
            return 2
        updated, diagnostics = remove_unit(pack, args.unit_id)
        if not updated.get("units", []) and any("no existe" in d for d in diagnostics):
            for d in diagnostics:
                print(f"  ! {d}", file=sys.stderr)
            return 1
    elif args.operation == "set-flag":
        if not (args.unit_id and args.flag and args.flag_value):
            print("--unit-id, --flag y --flag-value son obligatorios", file=sys.stderr)
            return 2
        updated, diagnostics = set_unit_flag(
            pack, args.unit_id, args.flag, args.flag_value == "true"
        )
    elif args.operation == "set-balance":
        if not (args.unit_id and args.balance_name and args.balance_value):
            print("--unit-id, --balance-name y --balance-value son obligatorios", file=sys.stderr)
            return 2
        updated, diagnostics = set_balance_override(
            pack, args.unit_id, args.balance_name, args.balance_value,
            factory=args.factory,
        )
    elif args.operation == "remove-balance":
        if not (args.unit_id and args.balance_name):
            print("--unit-id y --balance-name son obligatorios", file=sys.stderr)
            return 2
        updated, diagnostics = remove_balance_override(
            pack, args.unit_id, args.balance_name, factory=args.factory
        )
    elif args.operation == "set-map-size":
        if not (args.stage_id and args.map_min is not None and args.map_max is not None):
            print("--stage-id, --map-min y --map-max son obligatorios", file=sys.stderr)
            return 2
        updated, diagnostics = set_stage_map_size(
            pack, args.stage_id, args.map_min, args.map_max
        )
    elif args.operation == "add-pool":
        if not (args.stage_id and args.pool_id):
            print("--stage-id y --pool-id son obligatorios", file=sys.stderr)
            return 2
        updated, diagnostics = add_pool_entry(
            pack, args.stage_id, args.pool_id, args.pool_unit_ids or []
        )
    elif args.operation == "remove-pool":
        if not (args.stage_id and args.pool_id):
            print("--stage-id y --pool-id son obligatorios", file=sys.stderr)
            return 2
        updated, diagnostics = remove_pool_entry(pack, args.stage_id, args.pool_id)
    elif args.operation == "set-ai-param":
        if not (args.profile_id and args.module_id and args.param_name and args.param_value):
            print("--profile-id, --module-id, --param-name y --param-value son obligatorios",
                  file=sys.stderr)
            return 2
        updated, diagnostics = set_ai_module_param(
            pack, args.profile_id, args.module_id, args.param_name, args.param_value
        )

    if diagnostics:
        for d in diagnostics:
            print(f"  ! {d}")
        # operaciones que dejan el pack sin cambios válidos no escriben
        if args.operation in ("remove",) and any("no existe" in d for d in diagnostics):
            return 1
    written = write_pack(updated, args.out_dir)
    print(f"Pack escrito en {written}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
