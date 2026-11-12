"""Exporta un catálogo estructurado de unidades de un pack.

Refleja ``ContentUnitDefinition`` y sus sub-definiciones (movement,
weapon, armor, healing, stealth, command, aura, synergy, visual variant,
etc.): produce una lista compacta con rol, rareza, tech, costo, HP,
velocidad, fábrica y etapas. La función pura :func:`export_unit_catalog`
devuelve el catálogo; el CLI lo imprime (texto o JSON).
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key


def _get(unit: dict, *keys: str) -> Any:
    for key in keys:
        if key in unit and unit.get(key) is not None:
            return unit.get(key)
    return None


def export_unit_catalog(pack: dict) -> dict[str, Any]:
    """Devuelve el catálogo de unidades del pack."""
    pack = _as_dict(pack)
    pack_id = pack.get("packId") if isinstance(pack.get("packId"), str) else None
    units = [u for u in _as_list(pack.get("units")) if isinstance(u, dict)]

    # Mapa unidad -> etapas que la ofrecen.
    unit_stages: dict[str, list[str]] = {}
    for stage in _as_list(pack.get("stages")):
        if not isinstance(stage, dict):
            continue
        sid = _entity_key(stage) or "<unknown>"
        for uid in _as_list(stage.get("unitIds")):
            if isinstance(uid, str):
                unit_stages.setdefault(uid, []).append(sid)
        for pool in _as_list(stage.get("blueprintPoolEntries")):
            if not isinstance(pool, dict):
                continue
            for uid in _as_list(pool.get("unitIds")):
                if isinstance(uid, str):
                    unit_stages.setdefault(uid, []).append(sid)

    catalog: list[dict[str, Any]] = []
    for unit in units:
        uid = _entity_key(unit) or "<unknown>"
        movement = unit.get("movement") if isinstance(unit.get("movement"), dict) else {}
        weapon = unit.get("weapon") if isinstance(unit.get("weapon"), dict) else {}
        armor = unit.get("armor") if isinstance(unit.get("armor"), dict) else {}
        stages = sorted(set(unit_stages.get(uid, [])))
        catalog.append({
            "id": uid,
            "role": unit.get("role"),
            "rarity": unit.get("rarity"),
            "tech": unit.get("tech"),
            "cost": unit.get("cost"),
            "maxHealth": _get(armor, "maxHealth") if isinstance(armor, dict) else None,
            "moveSpeed": movement.get("moveSpeed") if isinstance(movement, dict) else None,
            "weaponRange": weapon.get("range") if isinstance(weapon, dict) else None,
            "attackDamage": weapon.get("damage") if isinstance(weapon, dict) else None,
            "productionBuilding": (
                unit.get("productionBuilding", {}).get("id")
                if isinstance(unit.get("productionBuilding"), dict)
                else unit.get("productionBuilding")
            ),
            "neededExperienceLevel": unit.get("neededExperienceLevel"),
            "stages": stages,
        })

    return {
        "packId": pack_id,
        "unitCount": len(catalog),
        "units": catalog,
    }


def _resolve_pack(path: Path) -> Path:
    if path.is_dir():
        return path / "content.json"
    return path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Exporta catálogo de unidades del pack")
    parser.add_argument("target", help="Directorio de pack o content.json")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    args = parser.parse_args(argv)
    path = _resolve_pack(Path(args.target))
    pack = json.loads(path.read_text(encoding="utf-8"))
    report = export_unit_catalog(pack)
    if args.json:
        print(json.dumps(report, indent=2, ensure_ascii=False))
    else:
        print(f"units={report['unitCount']}")
        for u in report["units"]:
            print(f"  {u['id']} role={u['role']} rarity={u['rarity']} tech={u['tech']} "
                  f"cost={u['cost']} hp={u['maxHealth']} speed={u['moveSpeed']} "
                  f"factory={u['productionBuilding']} stages={len(u['stages'])}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
