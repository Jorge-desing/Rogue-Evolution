#!/usr/bin/env python3
"""Analiza sinergia entre reliquias y unidades del pack.

Refleja ``CardChangeScriptableObject`` (26 valores ``ChangeableValue``)
y ``RelicBalancingStore`` (``neededSystemTags`` vs ``offeredSystemTags``):
una reliquia solo se ofrece si alguna unidad aporta el tag necesario.
La función pura :func:`analyze_relic_synergy` devuelve un informe con
conteos, reliquias sin coincidencia y problemas; el CLI lo imprime o JSON.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key

# 26 valores ChangeableValue canónicos (lista del handoff + schema cardChange).
CHANGEABLE_VALUES = {
    "Health", "Cost", "ProductionDuration", "MaxCapacity", "MaxHealth",
    "MaxShield", "MaxMana", "MaxRank", "MoveSpeed", "SightRadius",
    "Damage1", "Damage2", "WeaponRange", "Attack1Cooldown", "Attack2Cooldown",
    "EffectRadius1", "EffectRadius2", "SkillRange", "HealAmount1",
    "HealAmount2", "GainCreditsAmount", "SkillManaCost", "Duration1",
    "SpawnTtl", "ManaRechargePerSecond", "MaxArmor", "ArmorProtection",
}


def _split_tags(value: Any) -> set[str]:
    tags: set[str] = set()
    if isinstance(value, str):
        for part in value.split(","):
            part = part.strip()
            if part:
                tags.add(part)
    elif isinstance(value, list):
        for item in value:
            if isinstance(item, str) and item.strip():
                tags.add(item.strip())
    return tags


def _unit_offered_tags(unit: dict) -> set[str]:
    # systemTags (estructurado) + offeredSystemTags en balanceOverrides crudos.
    tags = _split_tags(unit.get("systemTags"))
    for override in _as_list(unit.get("balanceOverrides")):
        if isinstance(override, dict) and override.get("name") == "offeredSystemTags":
            tags |= _split_tags(override.get("value"))
    return tags


def analyze_relic_synergy(pack: dict) -> dict[str, Any]:
    """Devuelve el informe de sinergia reliquias <-> unidades."""
    pack = _as_dict(pack)
    pack_id = pack.get("packId") if isinstance(pack.get("packId"), str) else None
    issues: list[str] = []

    units = [u for u in _as_list(pack.get("units")) if isinstance(u, dict)]
    unit_tags: dict[str, set[str]] = {}
    for unit in units:
        uid = _entity_key(unit) or "<unknown>"
        unit_tags[uid] = _unit_offered_tags(unit)

    all_tags: set[str] = set()
    for tags in unit_tags.values():
        all_tags |= tags

    relics = [r for r in _as_list(pack.get("relics")) if isinstance(r, dict)]
    unmatched: list[str] = []
    matched = 0
    tag_hits: dict[str, int] = {}
    for relic in relics:
        rid = _entity_key(relic) or "<unknown>"
        # cardChanges deben usar valores ChangeableValue conocidos.
        for change in _as_list(relic.get("cardChanges")):
            if not isinstance(change, dict):
                continue
            val = change.get("valueToChange")
            if isinstance(val, str) and val and val not in CHANGEABLE_VALUES:
                issues.append(
                    f"relic '{rid}': valueToChange desconocido '{val}'"
                )
        needed = _split_tags(relic.get("neededSystemTags"))
        needed.discard("None")
        if not needed:
            matched += 1
            continue
        hits = [uid for uid, tags in unit_tags.items() if tags & needed]
        if hits:
            matched += 1
            for uid in hits:
                for tag in needed & unit_tags[uid]:
                    tag_hits[tag] = tag_hits.get(tag, 0) + 1
        else:
            unmatched.append(rid)

    return {
        "packId": pack_id,
        "relicCount": len(relics),
        "unitCount": len(units),
        "matchedRelicCount": matched,
        "unmatchedRelics": sorted(unmatched),
        "tagHitCounts": dict(sorted(tag_hits.items())),
        "issues": issues,
    }


def _resolve_pack(path: Path) -> Path:
    if path.is_dir():
        return path / "content.json"
    return path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Analiza sinergia reliquia-unidad"
    )
    parser.add_argument("target", help="Directorio de pack o content.json")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    args = parser.parse_args(argv)
    content_path = _resolve_pack(Path(args.target))
    try:
        pack = json.loads(content_path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exc:
        print(f"pack no legible: {exc}")
        return 1
    report = analyze_relic_synergy(pack)
    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2))
    else:
        print(f"pack={report['packId']} reliquias={report['relicCount']} "
              f"unidades={report['unitCount']} "
              f"coincidentes={report['matchedRelicCount']}")
        if report["unmatchedRelics"]:
            print(f"  sin coincidencia: {report['unmatchedRelics']}")
        for issue in report["issues"]:
            print(f"  ! {issue}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
