#!/usr/bin/env python3
"""Analiza qué unidades son alcanzables desde cada etapa, y cuáles son
contenido muerto (definidas pero nunca ofrecidas).

La función pura :func:`analyze_reachability` devuelve por etapa las
unidades ofrecidas (unitIds + pools) y la lista global de unidades sin
ninguna etapa que las ofrezca. El CLI imprime el informe o JSON.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key


def _stage_offered_units(stage: dict) -> set[str]:
    offered: set[str] = set()
    for uid in _as_list(stage.get("unitIds")):
        if isinstance(uid, str):
            offered.add(uid)
    for pool in _as_list(stage.get("blueprintPoolEntries")):
        if isinstance(pool, dict):
            for uid in _as_list(pool.get("unitIds")):
                if isinstance(uid, str):
                    offered.add(uid)
    return offered


def analyze_reachability(pack: dict) -> dict:
    """Para cada etapa, qué unidades ofrece; y unidades nunca ofrecidas."""
    pack = _as_dict(pack)
    pack_id = pack.get("packId") if isinstance(pack.get("packId"), str) else "<unknown>"

    defined_units = {
        _entity_key(u) for u in _as_list(pack.get("units"))
        if isinstance(u, dict) and _entity_key(u)
    }

    stages: list[dict] = []
    offered_anywhere: set[str] = set()
    unit_entries: dict[str, list[str]] = {}

    for stage in _as_list(pack.get("stages")):
        if not isinstance(stage, dict):
            continue
        sid = _entity_key(stage) or "<unknown>"
        offered = _stage_offered_units(stage)
        offered_anywhere |= offered
        for uid in offered:
            unit_entries.setdefault(uid, []).append(sid)
        stages.append({
            "stageId": sid,
            "offeredUnitIds": sorted(offered),
            "offeredCount": len(offered),
            "orphanOfferedIds": sorted(offered - defined_units),
        })

    unreachable = sorted(defined_units - offered_anywhere)

    return {
        "packId": pack_id,
        "definedUnitCount": len(defined_units),
        "stageCount": len(stages),
        "stages": stages,
        "unreachableUnitIds": unreachable,
        "unreachableCount": len(unreachable),
        "unitsOfferedPerStage": {
            uid: sorted(stage_ids) for uid, stage_ids in sorted(unit_entries.items())
        },
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Analiza alcance de unidades por etapa"
    )
    parser.add_argument("--pack", required=True, help="Directorio o content.json")
    parser.add_argument("--json", action="store_true")
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

    report = analyze_reachability(pack)
    if args.json:
        print(json.dumps(report, indent=2, ensure_ascii=False))
        return 0

    print(f"pack={report['packId']} units={report['definedUnitCount']} stages={report['stageCount']}")
    for stage in report["stages"]:
        print(f"  stage {stage['stageId']}: {stage['offeredCount']} unidades")
    print(f"unreachable={report['unreachableCount']}: {report['unreachableUnitIds']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
