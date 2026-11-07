#!/usr/bin/env python3
"""Analiza el alcance de pools de planos por etapa.

Refleja ``BlueprintPool``/``BlueprintPoolList`` y la clonación con
scoping por etapa del loader: cada ``blueprintPoolEntries`` nombra un
pool nativo y su allowlist es la unión de sus productos, mapeados a sus
fábricas. La función pura :func:`analyze_stage_pools` devuelve un
informe con cobertura y problemas; el CLI imprime el informe o JSON.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key


def _unit_to_factory(pack: dict) -> dict[str, str]:
    mapping: dict[str, str] = {}
    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        uid = _entity_key(unit)
        factory = unit.get("productionBuilding")
        fid = _entity_key(factory) if isinstance(factory, dict) else None
        if uid and fid:
            mapping[uid] = fid
    return mapping


def analyze_stage_pools(pack: dict,
                        known_unit_ids: set[str] | None = None) -> dict[str, Any]:
    """Devuelve el informe de alcance de pools para un pack."""
    pack = _as_dict(pack)
    pack_id = pack.get("packId") if isinstance(pack.get("packId"), str) else None

    own_units = {
        uid for unit in _as_list(pack.get("units"))
        if isinstance(unit, dict) and (uid := _entity_key(unit))
    }
    units = own_units | (known_unit_ids or set())
    factory_of = _unit_to_factory(pack)

    stages_report: list[dict] = []
    for stage in _as_list(pack.get("stages")):
        if not isinstance(stage, dict):
            continue
        sid = _entity_key(stage) or "<unknown>"
        issues: list[str] = []

        declared = [u for u in _as_list(stage.get("unitIds")) if isinstance(u, str)]
        pool_entries = []
        covered: set[str] = set()
        for entry in _as_list(stage.get("blueprintPoolEntries")):
            if not isinstance(entry, dict):
                issues.append(f"etapa '{sid}': blueprintPoolEntry no es objeto")
                continue
            pool_id = entry.get("poolId")
            if not isinstance(pool_id, str) or not pool_id.strip():
                issues.append(f"etapa '{sid}': blueprintPoolEntry sin poolId")
                pool_id = "<sin-pool>"
            entry_units = sorted(
                u for u in _as_list(entry.get("unitIds")) if isinstance(u, str)
            )
            for uid in entry_units:
                if uid not in units:
                    issues.append(
                        f"etapa '{sid}': pool '{pool_id}' referencia unidad "
                        f"no declarada '{uid}'"
                    )
                else:
                    covered.add(uid)
            factories = sorted(factory_of[u] for u in entry_units if u in factory_of)
            pool_entries.append({
                "poolId": pool_id,
                "unitIds": entry_units,
                "factoryIds": factories,
            })

        # Unidades declaradas sin cobertura en ningún pool explícito
        # dependen del fallback genérico (solo factories de la etapa activa).
        uncovered = sorted(u for u in declared if u not in covered)
        # Solo reportar huérfanas que sí están declaradas en el pack
        # para no duplicar el hallazgo de referencias colgantes.
        uncovered_known = [u for u in uncovered if u in units]

        stages_report.append({
            "stageId": sid,
            "declaredUnitIds": sorted(declared),
            "pools": pool_entries,
            "coveredUnitIds": sorted(covered),
            "uncoveredUnitIds": uncovered_known,
            "issues": issues,
        })

    return {
        "packId": pack_id,
        "stageCount": len(stages_report),
        "stages": stages_report,
    }


def _resolve_pack(path: Path) -> Path:
    if path.is_dir():
        return path / "content.json"
    return path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Analiza pools de planos por etapa"
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

    # IDs globales aproximados para referencias cruzadas.
    known = {
        uid for unit in _as_list(pack.get("units"))
        if isinstance(unit, dict) and (uid := _entity_key(unit))
    }
    report = analyze_stage_pools(pack, known_unit_ids=known)

    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2))
    else:
        print(f"pack={report['packId']} etapas={report['stageCount']}")
        for stage in report["stages"]:
            print(f"  {stage['stageId']}: "
                  f"pools={len(stage['pools'])} "
                  f"cubiertas={len(stage['coveredUnitIds'])} "
                  f"huérfanas={stage['uncoveredUnitIds']}")
            for issue in stage["issues"]:
                print(f"    ! {issue}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
