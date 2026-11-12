"""Analiza cadenas de mejoras (upgrades) en packs de contenido.

Refleja ``ContentUpgradeDefinition`` y ``CardChangeScriptableObject``:
cada mejora debe apuntar a una unidad existente, con nivel de experiencia
requerido acotado, costo no negativo y cambios de carta válidos. La
función pura :func:`analyze_upgrade_chains` devuelve un informe con
conteos, mejoras huérfanas y problemas.
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key
from tools.validate_card_changes import CHANGEABLE_VALUES, VALID_OPERATIONS, VALID_SIDES

VALID_OPERATIONS_LOCAL = VALID_OPERATIONS
VALID_SIDES_LOCAL = VALID_SIDES


def _is_finite_number(value: Any) -> bool:
    if isinstance(value, bool):
        return False
    if isinstance(value, (int, float)):
        return math.isfinite(value)
    return False


def analyze_upgrade_chains(pack: dict) -> dict[str, Any]:
    """Devuelve el informe de cadenas de mejora del pack."""
    pack = _as_dict(pack)
    pack_id = pack.get("packId") if isinstance(pack.get("packId"), str) else None
    units = [u for u in _as_list(pack.get("units")) if isinstance(u, dict)]
    unit_ids = {_entity_key(u) for u in units if _entity_key(u)}
    upgrades = [u for u in _as_list(pack.get("upgrades")) if isinstance(u, dict)]
    issues: list[str] = []
    orphan_upgrades: list[str] = []
    total_changes = 0
    valid_changes = 0

    seen_ids: set[str] = set()
    for upgrade in upgrades:
        uid = _entity_key(upgrade) or "<unknown>"
        if uid in seen_ids:
            issues.append(f"upgrade duplicado '{uid}'")
        seen_ids.add(uid)

        target = upgrade.get("targetUnitId")
        if not (isinstance(target, str) and target in unit_ids):
            issues.append(f"upgrade '{uid}': targetUnitId no resuelve a una unidad del pack ('{target}')")
            orphan_upgrades.append(uid)

        level = upgrade.get("requiredExperienceLevel")
        if level is not None:
            if not isinstance(level, int) or isinstance(level, bool) or level < 0 or level > 100:
                issues.append(f"upgrade '{uid}': requiredExperienceLevel fuera de rango [0,100]")

        cost = upgrade.get("cost")
        if cost is not None and not (isinstance(cost, (int, float)) and not isinstance(cost, bool) and cost >= 0):
            issues.append(f"upgrade '{uid}': cost inválido (debe ser >= 0)")

        for i, change in enumerate(_as_list(upgrade.get("cardChanges"))):
            total_changes += 1
            if not isinstance(change, dict):
                issues.append(f"upgrade '{uid}': cardChanges[{i}] no es objeto")
                continue
            vtc = change.get("valueToChange")
            if not (isinstance(vtc, str) and vtc in CHANGEABLE_VALUES):
                issues.append(f"upgrade '{uid}': valueToChange desconocido '{vtc}'")
                continue
            op = change.get("operation")
            if op is not None and op not in VALID_OPERATIONS:
                issues.append(f"upgrade '{uid}': operation inválida '{op}'")
                continue
            if "value" in change and not _is_finite_number(change.get("value")):
                issues.append(f"upgrade '{uid}': value no finito")
                continue
            side = change.get("side")
            if side is not None and side not in VALID_SIDES:
                issues.append(f"upgrade '{uid}': side inválido '{side}'")
                continue
            valid_changes += 1

    return {
        "packId": pack_id,
        "upgradeCount": len(upgrades),
        "unitCount": len(units),
        "orphanUpgrades": sorted(orphan_upgrades),
        "totalChanges": total_changes,
        "validChanges": valid_changes,
        "issues": issues,
    }


def _resolve_pack(path: Path) -> Path:
    if path.is_dir():
        return path / "content.json"
    return path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Analiza cadenas de mejora del pack")
    parser.add_argument("target", help="Directorio de pack o content.json")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    args = parser.parse_args(argv)
    path = _resolve_pack(Path(args.target))
    pack = json.loads(path.read_text(encoding="utf-8"))
    report = analyze_upgrade_chains(pack)
    if args.json:
        print(json.dumps(report, indent=2, ensure_ascii=False))
    else:
        print(f"upgrades={report['upgradeCount']} units={report['unitCount']} "
              f"orphans={len(report['orphanUpgrades'])} issues={len(report['issues'])}")
        for issue in report["issues"]:
            print(f"  - {issue}")
    return 1 if report["issues"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
