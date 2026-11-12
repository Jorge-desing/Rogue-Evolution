#!/usr/bin/env python3
"""Consulta y filtra contenido de un pack de Rogue Command.

Funciones puras: :func:`query_units` filtra unidades por rol, tag, tech,
rareza, coste o texto libre; :func:`unit_stages` y :func:`unit_ai_profiles`
resuelven dónde se ofrece una unidad y qué perfiles AI la referencian;
:func:`stage_profiles` y :func:`find_units_without_stage` cubren las
relaciones inversas. El CLI imprime filas compactas o JSON.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from tools.export_balance_table import export_balance_table
from tools.merge_content_packs import _as_dict, _as_list, _entity_key


def _parse_number(value: Any) -> float | None:
    if isinstance(value, bool):
        return None
    if isinstance(value, (int, float)):
        return float(value)
    if isinstance(value, str):
        try:
            return float(value)
        except ValueError:
            return None
    return None


def _tokens(entity: dict, field: str) -> list[str]:
    value = entity.get(field)
    if isinstance(value, list):
        return [str(v) for v in value if isinstance(v, str)]
    for override in _as_list(entity.get("balanceOverrides")):
        if isinstance(override, dict) and override.get("name") == field:
            raw = override.get("value")
            if isinstance(raw, str):
                return [p.strip() for p in raw.split(",") if p.strip()]
    return []


def _unit_lookup(pack: dict) -> dict[str, dict]:
    index: dict[str, dict] = {}
    for unit in _as_list(pack.get("units")):
        if isinstance(unit, dict):
            uid = _entity_key(unit)
            if uid:
                index[uid] = unit
    return index


def _display_name(unit: dict) -> str | None:
    for loc in _as_list(unit.get("localizations")):
        if isinstance(loc, dict) and loc.get("language") == "en-US":
            name = loc.get("name")
            return name if isinstance(name, str) else None
    return None


def _effective_str(unit: dict, name: str) -> str | None:
    value = unit.get(name)
    if isinstance(value, str) and value:
        return value
    for override in _as_list(unit.get("balanceOverrides")):
        if isinstance(override, dict) and override.get("name") == name:
            raw = override.get("value")
            return str(raw) if raw is not None else None
    return None


def query_units(
    pack: dict,
    *,
    role: str | None = None,
    tag: str | None = None,
    tech: str | None = None,
    rarity: str | None = None,
    max_cost: float | None = None,
    min_cost: float | None = None,
    text: str | None = None,
) -> list[dict]:
    """Devuelve filas compactas de unidades que cumplen todos los filtros."""
    pack = _as_dict(pack)
    balance = {row["unitId"]: row for row in export_balance_table(pack)}
    rows: list[dict] = []
    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        unit_id = _entity_key(unit)
        if not unit_id:
            continue
        roles = _tokens(unit, "roles")
        tags = _tokens(unit, "offeredSystemTags") or _tokens(unit, "systemTags")
        unit_tech = _effective_str(unit, "tech")
        unit_rarity = _effective_str(unit, "rarity")
        cost = balance.get(unit_id, {}).get("Cost")
        if role is not None and role not in roles:
            continue
        if tag is not None and tag not in tags:
            continue
        if tech is not None and unit_tech != tech:
            continue
        if rarity is not None and unit_rarity != rarity:
            continue
        if max_cost is not None and (cost is None or cost > max_cost):
            continue
        if min_cost is not None and (cost is None or cost < min_cost):
            continue
        name = _display_name(unit)
        if text is not None:
            needle = text.casefold()
            haystacks = [unit_id, name or ""]
            if not any(needle in hay.casefold() for hay in haystacks):
                continue
        rows.append({
            "unitId": unit_id,
            "name": name,
            "roles": roles,
            "systemTags": tags,
            "tech": unit_tech,
            "rarity": unit_rarity,
            "cost": cost,
            "maxHealth": balance.get(unit_id, {}).get("MaxHealth"),
        })
    rows.sort(key=lambda r: r["unitId"])
    return rows


def unit_stages(pack: dict, unit_id: str) -> list[str]:
    """Etapas que ofrecen ``unit_id`` (``unitIds`` o entradas de pool)."""
    pack = _as_dict(pack)
    stages: list[str] = []
    for stage in _as_list(pack.get("stages")):
        if not isinstance(stage, dict):
            continue
        offered = _as_list(stage.get("unitIds"))
        pooled = [
            uid
            for entry in _as_list(stage.get("blueprintPoolEntries"))
            if isinstance(entry, dict)
            for uid in _as_list(entry.get("unitIds"))
        ]
        if unit_id in offered or unit_id in pooled:
            stages.append(_entity_key(stage) or "")
    return sorted(s for s in stages if s)


def unit_ai_profiles(pack: dict, unit_id: str) -> list[dict]:
    """Perfiles AI que referencian ``unit_id`` con sus módulos."""
    pack = _as_dict(pack)
    found: list[dict] = []
    for profile in _as_list(pack.get("aiProfiles")):
        if not isinstance(profile, dict):
            continue
        referenced = False
        for section in ("unitEntries", "productionBuildingEntries"):
            for entry in _as_list(profile.get(section)):
                if isinstance(entry, dict) and entry.get("unitId") == unit_id:
                    referenced = True
        if referenced:
            found.append({
                "profileId": _entity_key(profile),
                "moduleIds": [
                    m.get("moduleId")
                    for m in _as_list(profile.get("modules"))
                    if isinstance(m, dict)
                ],
            })
    found.sort(key=lambda p: str(p["profileId"]))
    return found


def stage_profiles(pack: dict, stage_id: str) -> list[str]:
    """Perfiles AI asignados a la etapa ``stage_id``."""
    pack = _as_dict(pack)
    for stage in _as_list(pack.get("stages")):
        if isinstance(stage, dict) and _entity_key(stage) == stage_id:
            return sorted(
                v for v in _as_list(stage.get("aiProfileIds")) if isinstance(v, str)
            )
    return []


def find_units_without_stage(pack: dict) -> list[str]:
    """Unidades definidas que ninguna etapa ofrece (directo o por pool)."""
    pack = _as_dict(pack)
    offered: set[str] = set()
    for stage in _as_list(pack.get("stages")):
        if not isinstance(stage, dict):
            continue
        offered.update(v for v in _as_list(stage.get("unitIds")) if isinstance(v, str))
        for entry in _as_list(stage.get("blueprintPoolEntries")):
            if isinstance(entry, dict):
                offered.update(
                    v for v in _as_list(entry.get("unitIds")) if isinstance(v, str)
                )
    orphans = [
        uid
        for uid in _unit_lookup(pack)
        if uid not in offered
    ]
    return sorted(orphans)


def query_stages(pack: dict, unit_id: str | None = None) -> list[dict]:
    """Filas compactas de etapas (oferta, tamaño de mapa, perfiles, pools)."""
    pack = _as_dict(pack)
    rows: list[dict] = []
    for stage in _as_list(pack.get("stages")):
        if not isinstance(stage, dict):
            continue
        direct = [v for v in _as_list(stage.get("unitIds")) if isinstance(v, str)]
        pools = [
            [v for v in _as_list(e.get("unitIds")) if isinstance(v, str)]
            for e in _as_list(stage.get("blueprintPoolEntries"))
            if isinstance(e, dict)
        ]
        pooled = [uid for pool in pools for uid in pool]
        profiles = [v for v in _as_list(stage.get("aiProfileIds"))
                    if isinstance(v, str)]
        if unit_id is not None and unit_id not in direct and unit_id not in pooled:
            continue
        rows.append({
            "stageId": _entity_key(stage),
            "nativeTemplateId": stage.get("nativeTemplateId"),
            "mapSizeMin": stage.get("mapSizeMin"),
            "mapSizeMax": stage.get("mapSizeMax"),
            "unitCount": len(direct),
            "poolUnitCount": len(pooled),
            "pools": sorted({str(e.get("poolId", "")) for e in
                             _as_list(stage.get("blueprintPoolEntries"))
                             if isinstance(e, dict)}),
            "profileCount": len(profiles),
            "profiles": sorted(profiles),
        })
    rows.sort(key=lambda r: str(r["stageId"]))
    return rows


def query_ai_profiles(pack: dict, unit_id: str | None = None) -> list[dict]:
    """Filas compactas de perfiles AI (módulos y entradas de roster)."""
    pack = _as_dict(pack)
    rows: list[dict] = []
    for profile in _as_list(pack.get("aiProfiles")):
        if not isinstance(profile, dict):
            continue
        unit_entries = [e for e in _as_list(profile.get("unitEntries"))
                        if isinstance(e, dict)]
        factory_entries = [e for e in _as_list(profile.get("productionBuildingEntries"))
                           if isinstance(e, dict)]
        if unit_id is not None:
            referenced = any(
                e.get("unitId") == unit_id
                for e in unit_entries + factory_entries
            )
            if not referenced:
                continue
        rows.append({
            "profileId": _entity_key(profile),
            "nativeTemplateId": profile.get("nativeTemplateId"),
            "scopeToReferencedStages": profile.get("scopeToReferencedStages", False),
            "unitEntryCount": len(unit_entries),
            "factoryEntryCount": len(factory_entries),
            "moduleIds": sorted([
                str(m.get("moduleId"))
                for m in _as_list(profile.get("modules"))
                if isinstance(m, dict)
            ]),
        })
    rows.sort(key=lambda r: str(r["profileId"]))
    return rows


def _resolve_pack(path: Path) -> Path:
    return path / "content.json" if path.is_dir() else path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Consulta unidades de un content pack"
    )
    parser.add_argument("target", help="Directorio de pack o content.json")
    parser.add_argument("--role", default=None)
    parser.add_argument("--tag", default=None)
    parser.add_argument("--tech", default=None)
    parser.add_argument("--rarity", default=None)
    parser.add_argument("--max-cost", type=float, default=None)
    parser.add_argument("--min-cost", type=float, default=None)
    parser.add_argument("--text", default=None)
    parser.add_argument("--unit", default=None, help="Etapas y perfiles AI de una unidad")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    args = parser.parse_args(argv)
    content_path = _resolve_pack(Path(args.target))
    try:
        pack = json.loads(content_path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        print(f"pack no legible: {exc}")
        return 1
    if args.unit:
        payload = {
            "unitId": args.unit,
            "stages": unit_stages(pack, args.unit),
            "aiProfiles": unit_ai_profiles(pack, args.unit),
        }
    else:
        payload = query_units(
            pack, role=args.role, tag=args.tag, tech=args.tech,
            rarity=args.rarity, max_cost=args.max_cost,
            min_cost=args.min_cost, text=args.text,
        )
    if args.json:
        print(json.dumps(payload, ensure_ascii=False, indent=2))
    elif isinstance(payload, dict):
        print(f"unit={payload['unitId']}")
        print(f"  stages={payload['stages']}")
        for profile in payload["aiProfiles"]:
            print(f"  profile={profile['profileId']} modules={profile['moduleIds']}")
    else:
        print(f"unidades={len(payload)}")
        for row in payload:
            print(f"  {row['unitId']}: {row['name']!r} cost={row['cost']!r}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
