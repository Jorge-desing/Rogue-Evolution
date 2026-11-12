"""Valida filtros de pools de blueprints por etapa.

Refleja ``BlueprintPoolList`` / ``BlueprintPool`` y el filtro nativo
``blueprintFilter.IsBlueprintValid``: cada ``blueprintPoolEntries`` debe
tener ``poolId`` no vacío, ``unitIds`` no vacíos y sin duplicados, y las
unidades deben existir. La función pura
:func:`check_stage_blueprint_filters` devuelve un informe con conteos y
problemas.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key


def check_stage_blueprint_filters(pack: dict) -> dict[str, Any]:
    """Devuelve el informe de filtros de pools de blueprints del pack."""
    pack = _as_dict(pack)
    pack_id = pack.get("packId") if isinstance(pack.get("packId"), str) else None
    units = [u for u in _as_list(pack.get("units")) if isinstance(u, dict)]
    unit_ids = {_entity_key(u) for u in units if _entity_key(u)}
    stages = [s for s in _as_list(pack.get("stages")) if isinstance(s, dict)]
    issues: list[str] = []
    pool_count = 0
    entry_count = 0

    seen_pools: set[tuple[str, str]] = set()
    for stage in stages:
        sid = _entity_key(stage) or "<unknown>"
        for pool in _as_list(stage.get("blueprintPoolEntries")):
            if not isinstance(pool, dict):
                issues.append(f"stage '{sid}': pool entry no es objeto")
                continue
            pool_count += 1
            pool_id = pool.get("poolId")
            if not (isinstance(pool_id, str) and pool_id.strip()):
                issues.append(f"stage '{sid}': poolId vacío o inválido")
            unit_ids_in_pool = _as_list(pool.get("unitIds"))
            if not unit_ids_in_pool:
                issues.append(f"stage '{sid}' pool '{pool_id}': unitIds vacío")
            seen: set[str] = set()
            for uid in unit_ids_in_pool:
                entry_count += 1
                if not isinstance(uid, str) or not uid.strip():
                    issues.append(f"stage '{sid}' pool '{pool_id}': unitId inválido")
                    continue
                if uid in seen:
                    issues.append(f"stage '{sid}' pool '{pool_id}': unitId duplicado '{uid}'")
                seen.add(uid)
                if uid not in unit_ids:
                    issues.append(f"stage '{sid}' pool '{pool_id}': unitId no resuelve '{uid}'")
            key = (sid, str(pool_id))
            if key in seen_pools:
                issues.append(f"stage '{sid}': poolId duplicado '{pool_id}'")
            seen_pools.add(key)

    return {
        "packId": pack_id,
        "stageCount": len(stages),
        "poolCount": pool_count,
        "entryCount": entry_count,
        "issues": issues,
    }


def _resolve_pack(path: Path) -> Path:
    if path.is_dir():
        return path / "content.json"
    return path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Valida filtros de pools de blueprints del pack")
    parser.add_argument("target", help="Directorio de pack o content.json")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    args = parser.parse_args(argv)
    path = _resolve_pack(Path(args.target))
    pack = json.loads(path.read_text(encoding="utf-8"))
    report = check_stage_blueprint_filters(pack)
    if args.json:
        print(json.dumps(report, indent=2, ensure_ascii=False))
    else:
        print(f"stages={report['stageCount']} pools={report['poolCount']} "
              f"entries={report['entryCount']} issues={len(report['issues'])}")
        for issue in report["issues"]:
            print(f"  - {issue}")
    return 1 if report["issues"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
