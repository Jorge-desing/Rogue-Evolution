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


# ---------------------------------------------------------------------------
# Operaciones ampliadas: rename, localización, etapas, perfiles AI y batch
# ---------------------------------------------------------------------------

def rename_unit(
    pack: dict, old_id: str, new_id: str
) -> tuple[dict, list[str]]:
    """Renombra una unidad (y su fábrica) en todo el pack.

    Remapea ``old_id`` y su factory id en unidades, etapas, pools, entradas AI
    y parámetros de módulos. Falla si ``old_id`` no existe o ``new_id`` colisiona.
    """
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    if old_id not in _unit_index(updated):
        return updated, [f"unit '{old_id}' no existe en el pack"]
    if new_id in _unit_index(updated) or new_id in _factory_index(updated):
        return updated, [
            f"new id '{new_id}' ya existe en el pack (unidad o fábrica)"
        ]
    if _entity_key(updated and None) and new_id == old_id:
        return updated, [f"new id '{new_id}' es igual al actual"]

    old_unit = _unit_index(updated)[old_id]
    factory = old_unit.get("productionBuilding") if isinstance(old_unit, dict) else None
    old_factory = _entity_key(factory) if isinstance(factory, dict) else None

    def _remap(value):
        if isinstance(value, str) and value == old_id:
            return new_id
        if isinstance(value, str) and old_factory and value == old_factory:
            # factory new id: replace the unit slug prefix keeping suffix
            suffix = old_factory[len(old_id):] if old_factory.startswith(old_id) else "-factory"
            return new_id + suffix
        if isinstance(value, dict):
            return {k: _remap(v) for k, v in value.items()}
        if isinstance(value, list):
            return [_remap(v) for v in value]
        return value

    new_pack = _remap(updated)
    # Fix the unit's own factory id to new_id + suffix
    remapped_unit = _unit_index(new_pack).get(new_id)
    if remapped_unit is not None and isinstance(
        remapped_unit.get("productionBuilding"), dict
    ):
        build = remapped_unit["productionBuilding"]
        if isinstance(build.get("id"), str):
            # if the generic remap didn't produce the expected factory id
            expected = new_id + (old_factory[len(old_id):] if old_factory and old_factory.startswith(old_id) else "-factory")
            if build["id"] != expected:
                build["id"] = expected
    return new_pack, []


def set_localization(
    pack: dict, entity_id: str, language: str,
    name: str | None = None, description: str | None = None,
    factory: bool = False,
) -> tuple[dict, list[str]]:
    """Inserta o actualiza una localización (unidad o fábrica)."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    if not isinstance(language, str) or not language.strip():
        return updated, ["language vacío"]
    entity = _resolve_entity(updated, entity_id, factory)
    if entity is None:
        return updated, [f"entity '{entity_id}' no existe (o sin factory) en el pack"]

    localizations = entity.setdefault("localizations", [])
    entry = None
    for loc in localizations:
        if isinstance(loc, dict) and loc.get("language") == language:
            entry = loc
            break
    if entry is None:
        entry = {"language": language}
        localizations.append(entry)
    if name is not None:
        entry["name"] = name
    if description is not None:
        entry["description"] = description
    if "name" not in entry and "description" not in entry:
        return updated, ["se requiere name o description"]
    return updated, []


def add_stage(pack: dict, stage: dict) -> tuple[dict, list[str]]:
    """Añade una etapa. Falla si el id ya existe."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    if not isinstance(stage, dict) or not _entity_key(stage):
        return updated, ["stage.id vacío o ausente"]
    stage_id = _entity_key(stage)
    existing = {
        _entity_key(s) for s in _as_list(updated.get("stages")) if isinstance(s, dict)
    }
    if stage_id in existing:
        return updated, [f"stage '{stage_id}' ya existe en el pack"]
    updated.setdefault("stages", []).append(stage)
    return updated, []


def remove_stage(pack: dict, stage_id: str) -> tuple[dict, list[str]]:
    """Elimina una etapa. Falla si no existe."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    stages = _as_list(updated.get("stages"))
    kept = [s for s in stages if not (isinstance(s, dict) and _entity_key(s) == stage_id)]
    if len(kept) == len(stages):
        return updated, [f"stage '{stage_id}' no existe en el pack"]
    updated["stages"] = kept
    return updated, []


def add_ai_profile(pack: dict, profile: dict) -> tuple[dict, list[str]]:
    """Añade un perfil AI. Falla si el id ya existe."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    if not isinstance(profile, dict) or not _entity_key(profile):
        return updated, ["aiProfile.id vacío o ausente"]
    profile_id = _entity_key(profile)
    existing = {
        _entity_key(p) for p in _as_list(updated.get("aiProfiles")) if isinstance(p, dict)
    }
    if profile_id in existing:
        return updated, [f"ai profile '{profile_id}' ya existe en el pack"]
    updated.setdefault("aiProfiles", []).append(profile)
    return updated, []


def remove_ai_profile(pack: dict, profile_id: str) -> tuple[dict, list[str]]:
    """Elimina un perfil AI y purga su referencia en etapas."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    profiles = _as_list(updated.get("aiProfiles"))
    kept = [p for p in profiles if not (isinstance(p, dict) and _entity_key(p) == profile_id)]
    if len(kept) == len(profiles):
        return updated, [f"ai profile '{profile_id}' no existe en el pack"]
    updated["aiProfiles"] = kept
    for stage in _as_list(updated.get("stages")):
        if isinstance(stage, dict):
            ai_ids = stage.get("aiProfileIds", [])
            if isinstance(ai_ids, list) and profile_id in ai_ids:
                stage["aiProfileIds"] = [a for a in ai_ids if a != profile_id]
    return updated, []


def add_ai_module(
    pack: dict, profile_id: str, module: dict
) -> tuple[dict, list[str]]:
    """Añade un módulo a un perfil AI. Falla si ya existe o el perfil no existe."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    if not isinstance(module, dict) or not module.get("moduleId"):
        return updated, ["module.moduleId vacío o ausente"]
    module_id = module["moduleId"]

    profile = None
    for candidate in _as_list(updated.get("aiProfiles")):
        if isinstance(candidate, dict) and _entity_key(candidate) == profile_id:
            profile = candidate
            break
    if profile is None:
        return updated, [f"ai profile '{profile_id}' no existe en el pack"]

    existing = {
        m.get("moduleId") for m in _as_list(profile.get("modules")) if isinstance(m, dict)
    }
    if module_id in existing:
        return updated, [
            f"módulo '{module_id}' ya existe en '{profile_id}'"
        ]
    modules = profile.setdefault("modules", [])
    modules.append(module)
    return updated, []


def remove_ai_module(
    pack: dict, profile_id: str, module_id: str
) -> tuple[dict, list[str]]:
    """Elimina un módulo de un perfil AI."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    profile = None
    for candidate in _as_list(updated.get("aiProfiles")):
        if isinstance(candidate, dict) and _entity_key(candidate) == profile_id:
            profile = candidate
            break
    if profile is None:
        return updated, [f"ai profile '{profile_id}' no existe en el pack"]

    modules = _as_list(profile.get("modules"))
    kept = [m for m in modules if not (isinstance(m, dict) and m.get("moduleId") == module_id)]
    if len(kept) == len(modules):
        return updated, [f"módulo '{module_id}' no encontrado en '{profile_id}'"]
    profile["modules"] = kept
    return updated, []


def set_stage_parameter_override(
    pack: dict, stage_id: str, name: str, value: Any
) -> tuple[dict, list[str]]:
    """Inserta o actualiza un ``parameterOverride`` de etapa."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    if not isinstance(name, str) or not name.strip():
        return updated, ["name de parameterOverride vacío"]

    stage = None
    for candidate in _as_list(updated.get("stages")):
        if isinstance(candidate, dict) and _entity_key(candidate) == stage_id:
            stage = candidate
            break
    if stage is None:
        return updated, [f"stage '{stage_id}' no existe en el pack"]

    overrides = stage.setdefault("parameterOverrides", [])
    string_value = value if isinstance(value, str) else str(value)
    for entry in overrides:
        if isinstance(entry, dict) and entry.get("name") == name:
            entry["value"] = string_value
            return updated, []
    overrides.append({"name": name, "value": string_value})
    return updated, []


AI_PROFILE_FIELDS = (
    "scopeToReferencedStages",
    "doctrine",
    "aggressionMultiplier",
    "defaultUnitStance",
    "defaultUnitLoadout",
    "startingUnitVeterancyRank",
)


def set_ai_profile_field(
    pack: dict, profile_id: str, field: str, value: Any
) -> tuple[dict, list[str]]:
    """Fija un campo escalar de un perfil AI."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    if field not in AI_PROFILE_FIELDS:
        return updated, [
            f"campo '{field}' no permitido (permitidos: {', '.join(AI_PROFILE_FIELDS)})"
        ]
    if field == "scopeToReferencedStages" and not isinstance(value, bool):
        return updated, ["scopeToReferencedStages debe ser booleano"]
    if field == "aggressionMultiplier":
        if not isinstance(value, (int, float)) or isinstance(value, bool):
            return updated, ["aggressionMultiplier debe ser numérico"]

    profile = None
    for candidate in _as_list(updated.get("aiProfiles")):
        if isinstance(candidate, dict) and _entity_key(candidate) == profile_id:
            profile = candidate
            break
    if profile is None:
        return updated, [f"ai profile '{profile_id}' no existe en el pack"]
    profile[field] = value
    return updated, []


_PATTERN_UNITS = ("units",)
_PATTERN_STAGES = ("stages",)
_PATTERN_AI = ("aiProfiles",)


def apply_edits(
    pack: dict, operations: list[dict]
) -> tuple[dict, list[str]]:
    """Aplica una lista de operaciones de edición en secuencia.

    Cada operación es ``{"op": <nombre>, ...args}``. Devuelve
    ``(pack, diagnostics)`` con los diagnósticos de todas las operaciones.
    Las operaciones con error no detienen el batch; se reportan.
    """
    current = pack if isinstance(pack, dict) else {}
    all_diagnostics: list[str] = []

    for index, op in enumerate(operations):
        if not isinstance(op, dict):
            all_diagnostics.append(f"ops[{index}]: debe ser un objeto")
            continue
        name = op.get("op")
        try:
            if name == "set-flag":
                current, d = set_unit_flag(
                    current, op.get("unitId", ""), op.get("flag", ""),
                    op.get("value", False),
                )
            elif name == "remove":
                current, d = remove_unit(current, op.get("unitId", ""))
            elif name == "add":
                current, d = add_unit(
                    current, op.get("unit", {}), op.get("stageIds")
                )
            elif name == "set-balance":
                current, d = set_balance_override(
                    current, op.get("unitId", ""), op.get("name", ""),
                    op.get("value", ""), factory=bool(op.get("factory", False)),
                )
            elif name == "remove-balance":
                current, d = remove_balance_override(
                    current, op.get("unitId", ""), op.get("name", ""),
                    factory=bool(op.get("factory", False)),
                )
            elif name == "set-map-size":
                current, d = set_stage_map_size(
                    current, op.get("stageId", ""), op.get("min", 0), op.get("max", 0)
                )
            elif name == "add-pool":
                current, d = add_pool_entry(
                    current, op.get("stageId", ""), op.get("poolId", ""),
                    op.get("unitIds", []),
                )
            elif name == "remove-pool":
                current, d = remove_pool_entry(
                    current, op.get("stageId", ""), op.get("poolId", "")
                )
            elif name == "set-ai-param":
                current, d = set_ai_module_param(
                    current, op.get("profileId", ""), op.get("moduleId", ""),
                    op.get("name", ""), op.get("value", ""),
                )
            elif name == "rename-unit":
                current, d = rename_unit(
                    current, op.get("unitId", ""), op.get("newId", "")
                )
            elif name == "set-localization":
                current, d = set_localization(
                    current, op.get("unitId", ""), op.get("language", ""),
                    name=op.get("name"), description=op.get("description"),
                    factory=bool(op.get("factory", False)),
                )
            elif name == "add-stage":
                current, d = add_stage(current, op.get("stage", {}))
            elif name == "remove-stage":
                current, d = remove_stage(current, op.get("stageId", ""))
            elif name == "add-ai-profile":
                current, d = add_ai_profile(current, op.get("profile", {}))
            elif name == "remove-ai-profile":
                current, d = remove_ai_profile(current, op.get("profileId", ""))
            elif name == "add-ai-module":
                current, d = add_ai_module(
                    current, op.get("profileId", ""), op.get("module", {})
                )
            elif name == "remove-ai-module":
                current, d = remove_ai_module(
                    current, op.get("profileId", ""), op.get("moduleId", "")
                )
            elif name == "set-stage-param":
                current, d = set_stage_parameter_override(
                    current, op.get("stageId", ""), op.get("name", ""),
                    op.get("value", ""),
                )
            elif name == "set-ai-field":
                current, d = set_ai_profile_field(
                    current, op.get("profileId", ""), op.get("field", ""),
                    op.get("value"),
                )
            else:
                d = [f"op desconocida '{name}'"]
        except Exception as exc:  # keep the batch running per-op
            d = [f"{type(exc).__name__}: {exc}"]
        for item in d:
            all_diagnostics.append(f"ops[{index}] {name}: {item}")

    return current, all_diagnostics


def extract_units_to_pack(
    pack: dict, unit_ids: list[str]
) -> tuple[dict, list[str]]:
    """Extrae un subconjunto de unidades a un pack nuevo (con dependencias).

    Incluye las unidades pedidas (con sus fábricas) y solo las etapas/perfiles
    AI que referencian esas unidades. Falla si alguna unidad no existe.
    """
    source = _as_dict(pack)
    diagnostics: list[str] = []
    index = _unit_index(source)
    for uid in unit_ids:
        if uid not in index:
            diagnostics.append(f"unit '{uid}' no existe en el pack")
    if diagnostics:
        return {}, diagnostics

    extracted: dict[str, Any] = {}
    if isinstance(source.get("packId"), str):
        extracted["packId"] = source["packId"]
    if "schemaVersion" in source:
        extracted["schemaVersion"] = source["schemaVersion"]

    wanted = set(unit_ids)
    extracted["units"] = [
        u for u in _as_list(source.get("units")) if _entity_key(u) in wanted
    ]

    stages = []
    for stage in _as_list(source.get("stages")):
        if not isinstance(stage, dict):
            continue
        stage_units = set(_as_list(stage.get("unitIds")))
        if stage_units & wanted:
            stages.append(stage)
    if stages:
        extracted["stages"] = stages

    wanted_profiles = set()
    for stage in stages:
        wanted_profiles.update(_as_list(stage.get("aiProfileIds")))
    profiles = []
    for profile in _as_list(source.get("aiProfiles")):
        if not isinstance(profile, dict):
            continue
        if _entity_key(profile) in wanted_profiles:
            profiles.append(profile)
    if profiles:
        extracted["aiProfiles"] = profiles

    # bundles: only those whose produced units are extracted
    # (keep assetBundles empty unless units declare assetBundleId)
    has_bundle_units = any(
        u.get("assetBundleId") for u in extracted["units"]
    )
    extracted["assetBundles"] = list(_as_list(source.get("assetBundles"))) if has_bundle_units else []

    return extracted, []


def import_units(
    pack: dict, source: dict, unit_ids: list[str]
) -> tuple[dict, list[str]]:
    """Importa unidades desde ``source`` al pack actual.

    Duplica las unidades pedidas (con fábricas) y las etapas/perfiles mínimos
    que las referencian. Falla si alguna ya existe.
    """
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    src = _as_dict(source)
    diagnostics: list[str] = []

    src_index = _unit_index(src)
    existing = _unit_index(updated)
    existing_factories = _factory_index(updated)

    to_import = []
    for uid in unit_ids:
        if uid not in src_index:
            diagnostics.append(f"unit '{uid}' no existe en el pack origen")
            continue
        if uid in existing:
            diagnostics.append(f"unit '{uid}' ya existe en el pack destino")
            continue
        unit = src_index[uid]
        factory = unit.get("productionBuilding") if isinstance(unit, dict) else None
        fid = _entity_key(factory) if isinstance(factory, dict) else None
        if fid and fid in existing_factories:
            diagnostics.append(f"factory '{fid}' ya existe en el pack destino")
            continue
        to_import.append(unit)

    if diagnostics:
        return updated, diagnostics

    for unit in to_import:
        updated.setdefault("units", []).append(json.loads(json.dumps(unit)))

    wanted = set(unit_ids)
    # import stages that reference the imported units (if not already present)
    existing_stages = {
        _entity_key(s) for s in _as_list(updated.get("stages")) if isinstance(s, dict)
    }
    for stage in _as_list(src.get("stages")):
        if not isinstance(stage, dict):
            continue
        if set(_as_list(stage.get("unitIds"))) & wanted and _entity_key(stage) not in existing_stages:
            updated.setdefault("stages", []).append(json.loads(json.dumps(stage)))
            existing_stages.add(_entity_key(stage))

    existing_profiles = {
        _entity_key(p) for p in _as_list(updated.get("aiProfiles")) if isinstance(p, dict)
    }
    wanted_profiles = set()
    for stage in _as_list(updated.get("stages")):
        if isinstance(stage, dict):
            wanted_profiles.update(_as_list(stage.get("aiProfileIds")))
    for profile in _as_list(src.get("aiProfiles")):
        if not isinstance(profile, dict):
            continue
        if _entity_key(profile) in wanted_profiles and _entity_key(profile) not in existing_profiles:
            updated.setdefault("aiProfiles", []).append(json.loads(json.dumps(profile)))
            existing_profiles.add(_entity_key(profile))

    return updated, []


def purge_dangling_references(pack: dict) -> tuple[dict, list[str]]:
    """Elimina referencias a entidades no definidas (auto-sanación).

    Devuelve ``(pack, cleared)`` donde ``cleared`` lista los IDs eliminados.
    """
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    known_units = set(_unit_index(updated))
    known_factories = set(_factory_index(updated))
    known_profiles = {
        _entity_key(p) for p in _as_list(updated.get("aiProfiles")) if isinstance(p, dict)
    }
    cleared: list[str] = []

    for stage in _as_list(updated.get("stages")):
        if not isinstance(stage, dict):
            continue
        for key in ("unitIds", "aiProfileIds"):
            ids = stage.get(key, [])
            if not isinstance(ids, list):
                continue
            known = known_units if key == "unitIds" else known_profiles
            kept = []
            for value in ids:
                if isinstance(value, str) and value not in known:
                    cleared.append(value)
                else:
                    kept.append(value)
            stage[key] = kept
        for pool in _as_list(stage.get("blueprintPoolEntries")):
            if not isinstance(pool, dict):
                continue
            ids = pool.get("unitIds", [])
            if not isinstance(ids, list):
                continue
            kept = []
            for value in ids:
                if isinstance(value, str) and value not in known_units:
                    cleared.append(value)
                else:
                    kept.append(value)
            pool["unitIds"] = kept

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
                if isinstance(scalar, str) and scalar not in known_units:
                    cleared.append(scalar)
                    continue
                kept.append(entry)
            profile[section] = kept

    return updated, sorted(set(cleared))


def rename_stage(pack: dict, old_id: str, new_id: str) -> tuple[dict, list[str]]:
    """Renombra una etapa. Falla si no existe o el nuevo id colisiona."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    ids = {_entity_key(s) for s in _as_list(updated.get("stages")) if isinstance(s, dict)}
    if old_id not in ids:
        return updated, [f"stage '{old_id}' no existe en el pack"]
    if new_id in ids:
        return updated, [f"stage '{new_id}' ya existe en el pack"]
    for stage in _as_list(updated.get("stages")):
        if isinstance(stage, dict) and _entity_key(stage) == old_id:
            stage["id"] = new_id
            break
    return updated, []


def rename_ai_profile(pack: dict, old_id: str, new_id: str) -> tuple[dict, list[str]]:
    """Renombra un perfil AI (y su referencia en etapas)."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    ids = {_entity_key(p) for p in _as_list(updated.get("aiProfiles")) if isinstance(p, dict)}
    if old_id not in ids:
        return updated, [f"ai profile '{old_id}' no existe en el pack"]
    if new_id in ids:
        return updated, [f"ai profile '{new_id}' ya existe en el pack"]
    for profile in _as_list(updated.get("aiProfiles")):
        if isinstance(profile, dict) and _entity_key(profile) == old_id:
            profile["id"] = new_id
            break
    for stage in _as_list(updated.get("stages")):
        if isinstance(stage, dict) and isinstance(stage.get("aiProfileIds"), list):
            stage["aiProfileIds"] = [
                new_id if v == old_id else v for v in stage["aiProfileIds"]
            ]
    return updated, []


def clone_stage(pack: dict, stage_id: str, new_id: str) -> tuple[dict, list[str]]:
    """Clona una etapa con un nuevo id."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    ids = {_entity_key(s) for s in _as_list(updated.get("stages")) if isinstance(s, dict)}
    if stage_id not in ids:
        return updated, [f"stage '{stage_id}' no existe en el pack"]
    if new_id in ids:
        return updated, [f"stage '{new_id}' ya existe en el pack"]
    for stage in _as_list(updated.get("stages")):
        if isinstance(stage, dict) and _entity_key(stage) == stage_id:
            clone = json.loads(json.dumps(stage))
            clone["id"] = new_id
            updated.setdefault("stages", []).append(clone)
            return updated, []
    return updated, [f"stage '{stage_id}' no encontrado al clonar"]


def clone_ai_profile(pack: dict, profile_id: str, new_id: str) -> tuple[dict, list[str]]:
    """Clona un perfil AI con un nuevo id."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    ids = {_entity_key(p) for p in _as_list(updated.get("aiProfiles")) if isinstance(p, dict)}
    if profile_id not in ids:
        return updated, [f"ai profile '{profile_id}' no existe en el pack"]
    if new_id in ids:
        return updated, [f"ai profile '{new_id}' ya existe en el pack"]
    for profile in _as_list(updated.get("aiProfiles")):
        if isinstance(profile, dict) and _entity_key(profile) == profile_id:
            clone = json.loads(json.dumps(profile))
            clone["id"] = new_id
            updated.setdefault("aiProfiles", []).append(clone)
            return updated, []
    return updated, [f"ai profile '{profile_id}' no encontrado al clonar"]


def bulk_set_balance(
    pack: dict, name: str, value: Any, factory: bool = False
) -> tuple[dict, list[str]]:
    """Aplica un ``balanceOverride`` a TODAS las unidades (o fábricas)."""
    updated = json.loads(json.dumps(pack)) if isinstance(pack, dict) else {}
    if not isinstance(name, str) or not name.strip():
        return updated, ["name de balanceOverride vacío"]
    count = 0
    for unit in _as_list(updated.get("units")):
        if not isinstance(unit, dict):
            continue
        entity = _resolve_entity(updated, _entity_key(unit) or "", factory)
        if entity is None:
            continue
        overrides = entity.setdefault("balanceOverrides", [])
        string_value = value if isinstance(value, str) else str(value)
        replaced = False
        for entry in overrides:
            if isinstance(entry, dict) and entry.get("name") == name:
                entry["value"] = string_value
                replaced = True
                break
        if not replaced:
            overrides.append({"name": name, "value": string_value})
        count += 1
    if count == 0:
        return updated, [f"sin unidades objetivo para '{name}'"]
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


def _read_json_file(path_str: str):
    """Lee un JSON cualquiera. Devuelve el objeto o None (imprime el error)."""
    p = Path(path_str)
    if not p.is_file():
        print(f"Archivo no encontrado: {p}", file=sys.stderr)
        return None
    try:
        return json.loads(p.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        print(f"JSON inválido: {exc}", file=sys.stderr)
        return None


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
            "rename-unit", "set-localization",
            "add-stage", "remove-stage",
            "add-ai-profile", "remove-ai-profile",
            "add-ai-module", "remove-ai-module",
            "set-stage-param", "set-ai-field",
            "batch",
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
    # extended ops
    parser.add_argument("--new-id", help="Nuevo ID (rename-unit)")
    parser.add_argument("--language", help="Idioma (set-localization)")
    parser.add_argument("--loc-name", help="Nombre localizado")
    parser.add_argument("--loc-description", help="Descripción localizada")
    parser.add_argument("--stage-json", help="Ruta a un stage.json (add-stage)")
    parser.add_argument("--profile-json", help="Ruta a un profile.json (add-ai-profile)")
    parser.add_argument("--module-json", help="Ruta a un module.json (add-ai-module)")
    parser.add_argument("--sp-name", help="Nombre del stage parameterOverride")
    parser.add_argument("--sp-value", help="Valor del stage parameterOverride")
    parser.add_argument("--ai-field", help="Campo escalar del perfil AI")
    parser.add_argument("--ai-field-value", help="Valor del campo AI")
    parser.add_argument("--script", help="Ruta a un script JSON de operaciones (batch)")
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
    elif args.operation == "rename-unit":
        if not (args.unit_id and args.new_id):
            print("--unit-id y --new-id son obligatorios", file=sys.stderr)
            return 2
        updated, diagnostics = rename_unit(pack, args.unit_id, args.new_id)
    elif args.operation == "set-localization":
        if not (args.unit_id and args.language):
            print("--unit-id y --language son obligatorios", file=sys.stderr)
            return 2
        updated, diagnostics = set_localization(
            pack, args.unit_id, args.language,
            name=args.loc_name, description=args.loc_description,
            factory=args.factory,
        )
    elif args.operation == "add-stage":
        if not args.stage_json:
            print("--stage-json es obligatorio para add-stage", file=sys.stderr)
            return 2
        stage = _read_json_file(args.stage_json)
        if stage is None:
            return 2
        updated, diagnostics = add_stage(pack, stage)
    elif args.operation == "remove-stage":
        if not args.stage_id:
            print("--stage-id es obligatorio", file=sys.stderr)
            return 2
        updated, diagnostics = remove_stage(pack, args.stage_id)
    elif args.operation == "add-ai-profile":
        if not args.profile_json:
            print("--profile-json es obligatorio para add-ai-profile", file=sys.stderr)
            return 2
        profile = _read_json_file(args.profile_json)
        if profile is None:
            return 2
        updated, diagnostics = add_ai_profile(pack, profile)
    elif args.operation == "remove-ai-profile":
        if not args.profile_id:
            print("--profile-id es obligatorio", file=sys.stderr)
            return 2
        updated, diagnostics = remove_ai_profile(pack, args.profile_id)
    elif args.operation == "add-ai-module":
        if not (args.profile_id and args.module_json):
            print("--profile-id y --module-json son obligatorios", file=sys.stderr)
            return 2
        module = _read_json_file(args.module_json)
        if module is None:
            return 2
        updated, diagnostics = add_ai_module(pack, args.profile_id, module)
    elif args.operation == "remove-ai-module":
        if not (args.profile_id and args.module_id):
            print("--profile-id y --module-id son obligatorios", file=sys.stderr)
            return 2
        updated, diagnostics = remove_ai_module(pack, args.profile_id, args.module_id)
    elif args.operation == "set-stage-param":
        if not (args.stage_id and args.sp_name and args.sp_value):
            print("--stage-id, --sp-name y --sp-value son obligatorios", file=sys.stderr)
            return 2
        updated, diagnostics = set_stage_parameter_override(
            pack, args.stage_id, args.sp_name, args.sp_value
        )
    elif args.operation == "set-ai-field":
        if not (args.profile_id and args.ai_field and args.ai_field_value):
            print("--profile-id, --ai-field y --ai-field-value son obligatorios",
                  file=sys.stderr)
            return 2
        try:
            field_value = json.loads(args.ai_field_value)
        except json.JSONDecodeError:
            field_value = args.ai_field_value
        updated, diagnostics = set_ai_profile_field(
            pack, args.profile_id, args.ai_field, field_value
        )
    elif args.operation == "batch":
        if not args.script:
            print("--script es obligatorio para batch", file=sys.stderr)
            return 2
        script = _read_json_file(args.script)
        if script is None:
            return 2
        if not isinstance(script, list):
            print("El script debe ser una lista de operaciones", file=sys.stderr)
            return 2
        updated, diagnostics = apply_edits(pack, script)

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
