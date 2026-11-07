#!/usr/bin/env python3
"""Valida balanceOverrides y customBalance contra los valores nativos.

Fundamentado en REA (Assembly-CSharp.dll, SHA-256 verificado):
el enum ``ChangeableValue`` (0x02000475) define 26 claves por unidad;
``ManaRechargePerSecond`` es global y no per-unit. Los valores int están
acotados por el ensanchado a Single (max 16.777.216) y los float por
Single.MaxValue. La función pura :func:`validate_balance` devuelve errores;
el CLI imprime errores o JSON.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key

# 26 valores ChangeableValue per-unit (REA: 0x02000475, tokens 0x04001d10..0x04001d29).
# ManaRechargePerSecond es global (GameBalancingStore), no per-unit.
PER_UNIT_CHANGEABLE_VALUES = {
    "Cost", "ProductionDuration", "MaxCapacity", "MaxHealth",
    "MaxShield", "MaxMana", "MaxRank", "MoveSpeed", "SightRadius",
    "Damage1", "Damage2", "WeaponRange", "Attack1Cooldown",
    "Attack2Cooldown", "EffectRadius1", "EffectRadius2", "SkillRange",
    "HealAmount1", "HealAmount2", "GainCreditsAmount", "SkillManaCost",
    "Duration1", "SpawnTtl", "MaxArmor", "ArmorProtection",
}

GLOBAL_ONLY_VALUES = {"ManaRechargePerSecond"}

# balanceOverrides crudos aceptados (nombres de fila #US + ParamMap del loader).
RAW_OVERRIDE_NAMES = {
    "cost", "productionDuration", "maxCapacity", "maxHealth",
    "maxShield", "maxMana", "maxRank", "moveSpeed", "sightRadius",
    "damage1", "damage2", "weaponRange", "attackCooldown",
    "attack2Cooldown", "effectRadius1", "effectRadius2", "skillRange",
    "healAmount1", "healAmount2", "gainCreditsAmount", "skillManaCost",
    "duration1", "duration2", "duration",
    "maxArmor", "armorProtection",
    "combatValue", "gridSize", "coinsAmount", "firePointCount",
    "cardModelScalingFactor",
    "neededExperienceLevel", "rarity", "roles", "offeredSystemTags",
    "attackType", "skillType", "factoryForEntityId", "icon",
    "prefabLocation", "imageLocation",
    "cardImageAssetName", "cardModelAsset", "cardModelScalingFactor",
}

# Validaciones numéricas: int-backed capadas por Single, float por Single.MaxValue.
_INT_CAP = 16_777_216
_FLOAT_CAP = 3.4028235e38
_INT_BACKED = {
    "Cost", "MaxCapacity", "MaxHealth", "MaxShield", "MaxMana", "MaxRank",
    "MaxArmor", "SkillManaCost", "duration2", "productionDuration",
    "maxCapacity", "maxHealth", "maxShield", "maxMana", "maxRank",
    "cost", "maxArmor", "neededExperienceLevel", "duration",
    "duration1", "duration2", "gridSize", "firePointCount",
}

RARITY_VALUES = {"Common", "Rare", "UltraRare", "rarityCommon", "rarityRare", "rarityUltraRare"}

# Overrides con valor de cadena: nunca se validan como numéricos.
_STRING_VALUED = {
    "rarity", "roles", "offeredSystemTags", "attackType", "skillType",
    "factoryForEntityId", "icon", "prefabLocation", "imageLocation",
}


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


def _check_numeric(name: str, value: Any, context: str, findings: list[str]) -> None:
    num = _parse_number(value)
    if num is None:
        findings.append(f"{context}: '{name}' = {value!r} no numérico")
        return
    if not math.isfinite(num):
        findings.append(f"{context}: '{name}' no finito ({value!r})")
        return
    if name in _INT_BACKED and num > _INT_CAP:
        findings.append(
            f"{context}: '{name}' int excede el máximo 16.777.216 (Single widening)"
        )
    if num > _FLOAT_CAP:
        findings.append(f"{context}: '{name}' excede Single.MaxValue")


def _validate_custom(custom: dict, context: str, findings: list[str]) -> None:
    for key, value in custom.items():
        if key in GLOBAL_ONLY_VALUES:
            findings.append(
                f"{context}: '{key}' es global (GameBalancingStore), no per-unit"
            )
            continue
        if key not in PER_UNIT_CHANGEABLE_VALUES:
            findings.append(
                f"{context}: '{key}' no es un ChangeableValue por unidad (26)"
            )
            continue
        _check_numeric(key, value, context, findings)


def _validate_raw(overrides: list, context: str, findings: list[str]) -> None:
    for override in overrides:
        if not isinstance(override, dict):
            findings.append(f"{context}: balanceOverride no es un objeto")
            continue
        name = override.get("name")
        value = override.get("value")
        if not isinstance(name, str) or not name.strip():
            findings.append(f"{context}: balanceOverride sin name")
            continue
        if name == "rarity":
            if not isinstance(value, str) or value not in RARITY_VALUES:
                findings.append(
                    f"{context}: rarity '{value}' fuera de {sorted(RARITY_VALUES)}"
                )
            continue
        if name not in RAW_OVERRIDE_NAMES:
            findings.append(
                f"{context}: '{name}' no es un balanceOverride conocido (loader)"
            )
            continue
        if name in _STRING_VALUED:
            continue
        _check_numeric(name, value, context, findings)


def validate_balance(pack: dict) -> list[str]:
    """Valida balanceOverrides/customBalance de unidades y fábricas."""
    pack = _as_dict(pack)
    findings: list[str] = []
    pack_id = pack.get("packId") if isinstance(pack.get("packId"), str) else "<unknown>"
    prefix = f"[{pack_id}]"

    def _validate_entity(entity: dict, kind: str) -> None:
        eid = _entity_key(entity) or "<unknown>"
        ctx = f"{prefix} {kind} '{eid}'"
        custom = entity.get("customBalance")
        if isinstance(custom, dict):
            _validate_custom(custom, ctx, findings)
        _validate_raw(_as_list(entity.get("balanceOverrides")), ctx, findings)

    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        _validate_entity(unit, "unit")
        factory = unit.get("productionBuilding")
        if isinstance(factory, dict):
            _validate_entity(factory, "factory")

    return findings


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Valida balanceOverrides/customBalance contra valores nativos"
    )
    parser.add_argument("--pack", required=True, help="Directorio o content.json")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    args = parser.parse_args(argv)

    pack_path = Path(args.pack)
    if pack_path.is_dir():
        pack_path = pack_path / "content.json"
    if not pack_path.is_file():
        print(f"Archivo no encontrado: {pack_path}", file=sys.stderr)
        return 2

    try:
        pack = json.loads(pack_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        print(f"JSON inválido: {exc}", file=sys.stderr)
        return 2

    findings = validate_balance(pack)
    if args.json:
        print(json.dumps({"issues": findings, "issueCount": len(findings)},
                         indent=2, ensure_ascii=False))
        return 0 if not findings else 1
    if findings:
        for item in findings:
            print(item)
        print(f"FAIL {len(findings)} issue(s)")
        return 1
    print("PASS balance sin issue")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
