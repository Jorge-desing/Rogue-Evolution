#!/usr/bin/env python3
"""Exporta la tabla de balance efectiva de unidades del pack.

Refleja la precedencia del loader (``UnitBalanceOverrideMapper``):
``customBalance`` > ``balanceOverrides`` crudos > campos estructurados
(``combatValue``, ``gridSize``, ...). La función pura
:func:`export_balance_table` devuelve filas normalizadas; el CLI
imprime CSV o JSON para integrar con el simulador de balance.
"""
from __future__ import annotations

import argparse
import csv
import io
import json
import sys
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key

# Claves customBalance (ChangeableValue) -> columna de salida.
CUSTOM_TO_COLUMN = {
    "Cost": "Cost",
    "ProductionDuration": "ProductionDuration",
    "MaxCapacity": "MaxCapacity",
    "MaxHealth": "MaxHealth",
    "MaxShield": "MaxShield",
    "MaxMana": "MaxMana",
    "MaxRank": "MaxRank",
    "MoveSpeed": "MoveSpeed",
    "SightRadius": "SightRadius",
    "Damage1": "Damage1",
    "Damage2": "Damage2",
    "WeaponRange": "WeaponRange",
    "Attack1Cooldown": "Attack1Cooldown",
    "Attack2Cooldown": "Attack2Cooldown",
    "EffectRadius1": "EffectRadius1",
    "EffectRadius2": "EffectRadius2",
    "SkillRange": "SkillRange",
    "HealAmount1": "HealAmount1",
    "HealAmount2": "HealAmount2",
    "GainCreditsAmount": "GainCreditsAmount",
    "SkillManaCost": "SkillManaCost",
    "Duration1": "Duration1",
    "SpawnTtl": "SpawnTtl",
    "MaxArmor": "MaxArmor",
    "ArmorProtection": "ArmorProtection",
}

# balanceOverrides crudos (nombre de fila) -> columna de salida.
RAW_TO_COLUMN = {
    "cost": "Cost",
    "productionDuration": "ProductionDuration",
    "maxCapacity": "MaxCapacity",
    "maxHealth": "MaxHealth",
    "maxShield": "MaxShield",
    "maxMana": "MaxMana",
    "maxRank": "MaxRank",
    "moveSpeed": "MoveSpeed",
    "sightRadius": "SightRadius",
    "damage1": "Damage1",
    "damage2": "Damage2",
    "weaponRange": "WeaponRange",
    "attackCooldown": "Attack1Cooldown",
    "attack2Cooldown": "Attack2Cooldown",
    "effectRadius1": "EffectRadius1",
    "effectRadius2": "EffectRadius2",
    "skillRange": "SkillRange",
    "healAmount1": "HealAmount1",
    "healAmount2": "HealAmount2",
    "gainCreditsAmount": "GainCreditsAmount",
    "skillManaCost": "SkillManaCost",
    "duration1": "Duration1",
    "duration2": "SpawnTtl",
    "maxArmor": "MaxArmor",
    "armorProtection": "ArmorProtection",
    "combatValue": "combatValue",
    "gridSize": "gridSize",
    "coinsAmount": "coinsAmount",
    "firePointCount": "firePointCount",
}

STRUCTURED_FIELDS = ("combatValue", "gridSize", "maxCapacity",
                     "cardModelScalingFactor", "coinsAmount", "firePointCount")

_COLUMNS = ["unitId", "nativeTemplateId", "Cost", "MaxHealth", "Damage1",
            "Damage2", "WeaponRange", "Attack1Cooldown", "MoveSpeed",
            "SightRadius", "ProductionDuration", "MaxCapacity"]


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


def export_balance_table(pack: dict) -> list[dict[str, Any]]:
    """Devuelve una fila por unidad con su balance efectivo."""
    pack = _as_dict(pack)
    rows: list[dict[str, Any]] = []
    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        row: dict[str, Any] = {
            "unitId": _entity_key(unit),
            "nativeTemplateId": unit.get("nativeTemplateId"),
        }
        # 1. balanceOverrides crudos (menor precedencia).
        for override in _as_list(unit.get("balanceOverrides")):
            if not isinstance(override, dict):
                continue
            column = RAW_TO_COLUMN.get(override.get("name"))
            if column is None:
                continue
            num = _parse_number(override.get("value"))
            if num is not None:
                row[column] = num
        # 2. Campos estructurados.
        for field in STRUCTURED_FIELDS:
            if unit.get(field) is not None:
                row[field] = unit[field]
        # 3. customBalance (mayor precedencia).
        custom = unit.get("customBalance")
        if isinstance(custom, dict):
            for key, value in custom.items():
                column = CUSTOM_TO_COLUMN.get(key)
                if column is None:
                    continue
                num = _parse_number(value)
                if num is not None:
                    row[column] = num
        queue = _as_dict(unit.get("productionQueue"))
        build_time = _parse_number(queue.get("buildTime"))
        if build_time is not None and 0 < build_time < float("inf") and "ProductionDuration" not in row:
            row["ProductionDuration"] = build_time

        weapons = _as_list(unit.get("weapons"))
        if weapons and isinstance(weapons[0], dict):
            primary = weapons[0]
            for source, column in (("damage", "Damage1"), ("range", "WeaponRange"), ("fireRate", "Attack1Cooldown")):
                value = _parse_number(primary.get(source))
                if value is not None and 0 < value < float("inf") and column not in row:
                    row[column] = min(3.4028234663852886e38, 1 / value) if source == "fireRate" else value

        # 4. movement.moveSpeed si es positivo y no está en customBalance.
        movement = unit.get("movement")
        if isinstance(movement, dict):
            move = _parse_number(movement.get("moveSpeed"))
            custom = unit.get("customBalance")
            has_move = isinstance(custom, dict) and "MoveSpeed" in custom
            if move is not None and move > 0 and not has_move and "MoveSpeed" not in row:
                row["MoveSpeed"] = move
        rows.append(row)
    return rows


def rows_to_csv(rows: list[dict[str, Any]]) -> str:
    """Serializa filas a CSV con las columnas principales."""
    if not rows:
        return ""
    # Incluye columnas extra que aparezcan (p. ej. estructuradas).
    extra: list[str] = []
    for row in rows:
        for key in row:
            if key not in _COLUMNS and key not in extra:
                extra.append(key)
    fields = [c for c in _COLUMNS if c in _known_columns(rows)] + extra
    buf = io.StringIO()
    writer = csv.DictWriter(buf, fieldnames=fields)
    writer.writeheader()
    for row in rows:
        writer.writerow({k: row.get(k, "") for k in fields})
    return buf.getvalue()


def _known_columns(rows: list[dict[str, Any]]) -> set[str]:
    cols: set[str] = set()
    for row in rows:
        cols.update(row.keys())
    return cols


def _resolve_pack(path: Path) -> Path:
    if path.is_dir():
        return path / "content.json"
    return path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Exporta la tabla de balance efectiva"
    )
    parser.add_argument("target", help="Directorio de pack o content.json")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    parser.add_argument("--csv", action="store_true", help="Salida CSV")
    args = parser.parse_args(argv)
    content_path = _resolve_pack(Path(args.target))
    try:
        pack = json.loads(content_path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        print(f"pack no legible: {exc}")
        return 1
    rows = export_balance_table(pack)
    if args.json:
        print(json.dumps(rows, ensure_ascii=False, indent=2))
    elif args.csv:
        print(rows_to_csv(rows), end="")
    else:
        print(f"unidades={len(rows)}")
        for row in rows:
            print(f"  {row.get('unitId')}: Cost={row.get('Cost')!r} "
                  f"HP={row.get('MaxHealth')!r} DMG={row.get('Damage1')!r}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
