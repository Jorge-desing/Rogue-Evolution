#!/usr/bin/env python3
"""Informe de economía de unidades y etapas para packs de Rogue Command.

La función pura :func:`economy_report` deriva coste, EHP (salud + escudo),
DPS primario/secundario, DPS total y eficiencias por unidad desde la tabla
de balance efectiva del loader (``customBalance`` > ``balanceOverrides`` >
campos estructurados). :func:`stage_economy` resume por etapa recompensas,
tamaño de mapa y conteos ofrecidos. Los conteos van antes que el detalle
para que el truncamiento nunca oculte el total. Son estimaciones locales de
planificación, no evidencia de balance en partida.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from tools.export_balance_table import export_balance_table
from tools.merge_content_packs import _as_dict, _as_list, _entity_key


def _number(row: dict, name: str) -> float:
    value = row.get(name)
    return float(value) if isinstance(value, (int, float)) and not isinstance(value, bool) else 0.0


def _dps(damage: float, cooldown: float) -> float:
    return damage / cooldown if cooldown > 0 else 0.0


def _override_number(stage: dict, name: str) -> float | None:
    for override in _as_list(stage.get("parameterOverrides")):
        if isinstance(override, dict) and override.get("name") == name:
            try:
                return float(override.get("value"))
            except (TypeError, ValueError):
                return None
    return None


def _int_or_none(value: Any) -> int | None:
    if isinstance(value, bool):
        return None
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def economy_report(pack: dict) -> dict:
    """Informe de coste/eficiencia por unidad con conteos primero."""
    pack = _as_dict(pack)
    units: list[dict] = []
    for row in export_balance_table(pack):
        cost = _number(row, "Cost")
        health = _number(row, "MaxHealth")
        shield = _number(row, "MaxShield")
        ehp = health + shield
        dps1 = _dps(_number(row, "Damage1"), _number(row, "Attack1Cooldown"))
        dps2 = _dps(_number(row, "Damage2"), _number(row, "Attack2Cooldown"))
        dps_total = dps1 + dps2
        units.append({
            "unitId": row.get("unitId"),
            "cost": cost if cost else None,
            "productionDuration": _number(row, "ProductionDuration") or None,
            "maxHealth": health or None,
            "ehp": ehp,
            "damage1": _number(row, "Damage1") or None,
            "damage2": _number(row, "Damage2") or None,
            "dps1": dps1,
            "dps2": dps2,
            "dpsTotal": dps_total,
            "costPerHp": (cost / ehp) if ehp > 0 else None,
            "dpsPerCost": (dps_total / cost) if cost > 0 else None,
        })
    units.sort(key=lambda u: str(u["unitId"]))
    costs = [u["cost"] for u in units if u["cost"] is not None]
    totals = {
        "avgCost": (sum(costs) / len(costs)) if costs else None,
        "avgEhp": (sum(u["ehp"] for u in units) / len(units)) if units else None,
    }
    return {
        "packId": pack.get("packId"),
        "counts": {"units": len(units)},
        "totals": totals,
        "units": units,
    }


def stage_economy(pack: dict) -> dict:
    """Resumen económico y de tamaño por etapa (conteos primero)."""
    pack = _as_dict(pack)
    stages: list[dict] = []
    for stage in _as_list(pack.get("stages")):
        if not isinstance(stage, dict):
            continue
        pools = [e for e in _as_list(stage.get("blueprintPoolEntries")) if isinstance(e, dict)]
        stages.append({
            "stageId": _entity_key(stage),
            "levelCount": _override_number(stage, "levelCount"),
            "maxWidth": _override_number(stage, "maxWidth"),
            "coinReward": _override_number(stage, "coinReward"),
            "startCrystalReward": _override_number(stage, "startCrystalReward"),
            "mapSizeMin": _int_or_none(stage.get("mapSizeMin")),
            "mapSizeMax": _int_or_none(stage.get("mapSizeMax")),
            "unitCount": len([u for u in _as_list(stage.get("unitIds")) if isinstance(u, str)]),
            "profileCount": len([p for p in _as_list(stage.get("aiProfileIds")) if isinstance(p, str)]),
            "poolCount": len(pools),
            "poolUnitCount": sum(
                len([u for u in _as_list(e.get("unitIds")) if isinstance(u, str)])
                for e in pools
            ),
        })
    stages.sort(key=lambda s: str(s["stageId"]))
    return {
        "packId": pack.get("packId"),
        "counts": {"stages": len(stages)},
        "stages": stages,
    }


def _resolve_pack(path: Path) -> Path:
    return path / "content.json" if path.is_dir() else path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Informe de economía de un content pack"
    )
    parser.add_argument("target", help="Directorio de pack o content.json")
    parser.add_argument("--stages", action="store_true", help="Informe de etapas")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    args = parser.parse_args(argv)
    content_path = _resolve_pack(Path(args.target))
    try:
        pack = json.loads(content_path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        print(f"pack no legible: {exc}")
        return 1
    report = stage_economy(pack) if args.stages else economy_report(pack)
    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2))
    elif args.stages:
        print(f"etapas={report['counts']['stages']}")
        for stage in report["stages"]:
            print(f"  {stage['stageId']}: reward={stage['coinReward']!r} "
                  f"units={stage['unitCount']} pools={stage['poolCount']}")
    else:
        print(f"unidades={report['counts']['units']}")
        for unit in report["units"]:
            print(f"  {unit['unitId']}: cost={unit['cost']!r} "
                  f"ehp={unit['ehp']:.1f} dps={unit['dpsTotal']:.2f}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
