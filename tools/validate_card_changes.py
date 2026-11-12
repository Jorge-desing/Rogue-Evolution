"""Valida cambios de carta (cardChanges) en packs de contenido.

Refleja ``CardChangeScriptableObject`` y los 26 ``ChangeableValue`` del
``EntityBalancingStore``: cada ``cardChanges`` de unidades, reliquias y
mejoras debe apuntar a un valor canónico, con operación válida
(Add/Multiply/Set), valor finito y lado válido. La función pura
:func:`validate_card_changes` devuelve un informe con conteos, cambios
inválidos y problemas; el CLI lo imprime o en JSON.
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key

CHANGEABLE_VALUES = {
    "Health", "Cost", "ProductionDuration", "MaxCapacity", "MaxHealth",
    "MaxShield", "MaxMana", "MaxRank", "MoveSpeed", "SightRadius",
    "Damage1", "Damage2", "WeaponRange", "Attack1Cooldown", "Attack2Cooldown",
    "EffectRadius1", "EffectRadius2", "SkillRange", "HealAmount1",
    "HealAmount2", "GainCreditsAmount", "SkillManaCost", "Duration1",
    "SpawnTtl", "ManaRechargePerSecond", "MaxArmor", "ArmorProtection",
}

VALID_OPERATIONS = {"Add", "Multiply", "Set"}
VALID_SIDES = {"False", "True", "Irrelevant"}


def _is_finite_number(value: Any) -> bool:
    if isinstance(value, bool):
        return False
    if isinstance(value, (int, float)):
        return math.isfinite(value)
    return False


def _validate_change(change: Any, owner: str, index: int, issues: list[str]) -> bool:
    if not isinstance(change, dict):
        issues.append(f"{owner}: cardChanges[{index}] no es objeto")
        return False
    ok = True
    val = change.get("valueToChange")
    if not (isinstance(val, str) and val in CHANGEABLE_VALUES):
        issues.append(f"{owner}: valueToChange desconocido '{val}'")
        ok = False
    op = change.get("operation")
    if op is not None and op not in VALID_OPERATIONS:
        issues.append(f"{owner}: operation inválida '{op}'")
        ok = False
    if "value" in change and not _is_finite_number(change.get("value")):
        issues.append(f"{owner}: value no finito en cardChanges[{index}]")
        ok = False
    side = change.get("side")
    if side is not None and side not in VALID_SIDES:
        issues.append(f"{owner}: side inválido '{side}'")
        ok = False
    return ok


def validate_card_changes(pack: dict) -> dict[str, Any]:
    """Devuelve el informe de validación de cambios de carta del pack."""
    pack = _as_dict(pack)
    pack_id = pack.get("packId") if isinstance(pack.get("packId"), str) else None
    issues: list[str] = []
    total_changes = 0
    valid_changes = 0
    owners_checked = 0

    # Unidades.
    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        uid = _entity_key(unit) or "<unknown>"
        owners_checked += 1
        for i, change in enumerate(_as_list(unit.get("cardChanges"))):
            total_changes += 1
            if _validate_change(change, f"unit '{uid}'", i, issues):
                valid_changes += 1

    # Reliquias.
    for relic in _as_list(pack.get("relics")):
        if not isinstance(relic, dict):
            continue
        rid = _entity_key(relic) or "<unknown>"
        owners_checked += 1
        for i, change in enumerate(_as_list(relic.get("cardChanges"))):
            total_changes += 1
            if _validate_change(change, f"relic '{rid}'", i, issues):
                valid_changes += 1

    # Mejoras.
    for upgrade in _as_list(pack.get("upgrades")):
        if not isinstance(upgrade, dict):
            continue
        upid = _entity_key(upgrade) or "<unknown>"
        owners_checked += 1
        for i, change in enumerate(_as_list(upgrade.get("cardChanges"))):
            total_changes += 1
            if _validate_change(change, f"upgrade '{upid}'", i, issues):
                valid_changes += 1

    return {
        "packId": pack_id,
        "ownersChecked": owners_checked,
        "totalChanges": total_changes,
        "validChanges": valid_changes,
        "invalidChanges": total_changes - valid_changes,
        "issues": issues,
    }


def _resolve_pack(path: Path) -> Path:
    if path.is_dir():
        return path / "content.json"
    return path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Valida cambios de carta del pack")
    parser.add_argument("target", help="Directorio de pack o content.json")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    args = parser.parse_args(argv)
    path = _resolve_pack(Path(args.target))
    pack = json.loads(path.read_text(encoding="utf-8"))
    report = validate_card_changes(pack)
    if args.json:
        print(json.dumps(report, indent=2, ensure_ascii=False))
    else:
        print(f"ownersChecked={report['ownersChecked']} totalChanges={report['totalChanges']} "
              f"valid={report['validChanges']} invalid={report['invalidChanges']}")
        for issue in report["issues"]:
            print(f"  - {issue}")
    return 1 if report["issues"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
