#!/usr/bin/env python3
"""Analiza el uso de mecánicas por unidad en un content pack.

La función pura :func:`analyze_mechanics` reporta, con conteos antes que el
detalle, qué subdefiniciones de unidad (cadenas, cargas, auras, sigilo,
economía, efectos, etc.) declara cada unidad, el histograma de uso y las
mecánicas que el esquema soporta pero ningún pack usa (mapa de
oportunidades del cargador). Son estimaciones locales de planificación, no
evidencia de balance en partida.
"""
from __future__ import annotations

import argparse
import json
import sys
from collections import Counter
from pathlib import Path

from tools.merge_content_packs import _as_dict, _as_list, _entity_key

# Subdefiniciones de unidad con comportamiento/meánica (content.schema.json).
MECHANIC_FIELDS = (
    "abilities", "aiOverrides", "activeSkills", "combatBoosts", "upgrades",
    "experience", "synergies", "visualVariants", "variants", "spawn",
    "statusEffects", "productionQueue", "resourceCosts", "movement",
    "detection", "weapons", "armor", "healing", "stealth", "command",
    "experienceReward", "aura", "resourceGathering", "aiBehavior",
    "shieldRegeneration", "vampiricLeech", "tauntPulse", "shieldOverloadNova",
    "damageReflect", "deathNova",
    "baselineStatusEffects", "onHitStatusEffects",
)


def _is_declared(value) -> bool:
    return (isinstance(value, dict) and bool(value)) or (
        isinstance(value, list) and bool(value))


def analyze_mechanics(pack: dict) -> dict:
    """Informe de mecánicas por unidad (conteos primero)."""
    pack = _as_dict(pack)
    units: list[dict] = []
    usage: Counter = Counter()
    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        mechanics = sorted(
            field for field in MECHANIC_FIELDS
            if _is_declared(unit.get(field))
        )
        for field in mechanics:
            usage[field] += 1
        units.append({
            "unitId": _entity_key(unit),
            "mechanics": mechanics,
            "mechanicCount": len(mechanics),
            "variantCount": len([v for v in _as_list(unit.get("variants"))
                                 if isinstance(v, dict)]),
        })
    units.sort(key=lambda u: str(u["unitId"]))
    unused = sorted(set(MECHANIC_FIELDS) - set(usage))
    return {
        "packId": pack.get("packId"),
        "counts": {"units": len(units)},
        "usage": dict(sorted(usage.items())),
        "unusedMechanics": unused,
        "units": units,
    }


def _resolve_pack(path: Path) -> Path:
    return path / "content.json" if path.is_dir() else path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Analiza el uso de mecánicas de unidad")
    parser.add_argument("target", nargs="?", default=None,
                        help="Directorio de pack o content.json")
    parser.add_argument("--all", action="store_true",
                        help="Analiza todo content/packs")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    args = parser.parse_args(argv)
    if args.all:
        packs_dir = Path(args.target) if args.target else Path("content/packs")
        targets = sorted(packs_dir.glob("*/content.json"))
    elif args.target:
        targets = [_resolve_pack(Path(args.target))]
    else:
        parser.error("se requiere target o --all")
    exit_code = 0
    for target in targets:
        try:
            pack = json.loads(target.read_text(encoding="utf-8"))
        except (OSError, ValueError) as exc:
            print(f"{target}: pack no legible: {exc}")
            exit_code = 1
            continue
        report = analyze_mechanics(pack)
        if args.json:
            print(json.dumps(report, ensure_ascii=False, indent=2))
        else:
            print(f"{target}: unidades={report['counts']['units']} "
                  f"sinUso={len(report['unusedMechanics'])}")
            for field, count in report["usage"].items():
                print(f"  {field}: {count}")
            if report["unusedMechanics"]:
                print(f"  sin uso: {', '.join(report['unusedMechanics'])}")
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
