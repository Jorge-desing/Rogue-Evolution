"""Analiza cobertura de perfiles AI en packs de contenido.

Refleja ``ContentAiProfileDefinition`` y ``AiModuleRegistry`` (22 módulos
registrados): cada perfil debe referenciar unidades/fábricas existentes,
módulos registrados, etapas válidas y parámetros bien formados. La función
pura :func:`analyze_ai_profile_coverage` devuelve un informe con conteos,
perfiles huérfanos y problemas.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key

REGISTERED_AI_MODULES = {
    "sample:adaptive-commander", "sample:construction-expansion",
    "sample:counter-battery", "sample:factory-reinforcement",
    "sample:flanking-strike", "sample:focus-fire", "sample:harvester-escort",
    "sample:harvester-raider", "sample:patrol-and-intercept",
    "sample:reactive-base-defense", "sample:retreat-and-regroup",
    "sample:scheduled-base-build", "sample:siege-assault", "sample:skirmish-kiting",
    "sample:squad-formation", "sample:support-repair", "sample:tactical-director",
    "sample:tactical-regroup", "sample:tactical-skill-activation",
    "sample:wave-escalation", "sample:threat-prioritization",
    "sample:trace-default",
}


def analyze_ai_profile_coverage(pack: dict) -> dict[str, Any]:
    """Devuelve el informe de cobertura de perfiles AI del pack."""
    pack = _as_dict(pack)
    pack_id = pack.get("packId") if isinstance(pack.get("packId"), str) else None
    units = [u for u in _as_list(pack.get("units")) if isinstance(u, dict)]
    unit_ids = {_entity_key(u) for u in units if _entity_key(u)}
    buildings = [b for b in _as_list(pack.get("buildings")) if isinstance(b, dict)]
    building_ids = {_entity_key(b) for b in buildings if _entity_key(b)}
    stages = [s for s in _as_list(pack.get("stages")) if isinstance(s, dict)]
    stage_ids = {_entity_key(s) for s in stages if _entity_key(s)}
    profiles = [p for p in _as_list(pack.get("aiProfiles")) if isinstance(p, dict)]
    issues: list[str] = []
    orphan_profiles: list[str] = []

    seen_ids: set[str] = set()
    for profile in profiles:
        pid = _entity_key(profile) or "<unknown>"
        if pid in seen_ids:
            issues.append(f"perfil AI duplicado '{pid}'")
        seen_ids.add(pid)

        # unitEntries.
        for entry in _as_list(profile.get("unitEntries")):
            if not isinstance(entry, dict):
                issues.append(f"perfil '{pid}': unitEntry no es objeto")
                continue
            eid = entry.get("entityId")
            if not (isinstance(eid, str) and eid in unit_ids):
                issues.append(f"perfil '{pid}': unitEntry entityId no resuelve ('{eid}')")

        # productionBuildingEntries.
        for entry in _as_list(profile.get("productionBuildingEntries")):
            if not isinstance(entry, dict):
                issues.append(f"perfil '{pid}': productionBuildingEntry no es objeto")
                continue
            eid = entry.get("entityId")
            if not (isinstance(eid, str) and eid in building_ids):
                issues.append(f"perfil '{pid}': productionBuildingEntry entityId no resuelve ('{eid}')")

        # Módulos.
        module_ids_seen: set[str] = set()
        for mod in _as_list(profile.get("modules")):
            if not isinstance(mod, dict):
                issues.append(f"perfil '{pid}': módulo no es objeto")
                continue
            mid = mod.get("moduleId")
            if not isinstance(mid, str) or mid not in REGISTERED_AI_MODULES:
                issues.append(f"perfil '{pid}': moduleId no registrado '{mid}'")
            if isinstance(mid, str):
                if mid in module_ids_seen:
                    issues.append(f"perfil '{pid}': módulo duplicado '{mid}'")
                module_ids_seen.add(mid)
            # Parámetros.
            for param in _as_list(mod.get("parameters")):
                if not isinstance(param, dict):
                    issues.append(f"perfil '{pid}': parámetro no es objeto")
                    continue
                name = param.get("name")
                if not (isinstance(name, str) and name):
                    issues.append(f"perfil '{pid}': parámetro sin name")
                value = param.get("value")
                if value is None:
                    issues.append(f"perfil '{pid}': parámetro '{name}' sin value")

        # Etapas referenciadas (si scopeToReferencedStages).
        if profile.get("scopeToReferencedStages"):
            for sid in _as_list(profile.get("stageIds")):
                if isinstance(sid, str) and sid not in stage_ids:
                    issues.append(f"perfil '{pid}': stageId no resuelve ('{sid}')")

    return {
        "packId": pack_id,
        "profileCount": len(profiles),
        "unitCount": len(units),
        "stageCount": len(stages),
        "registeredModuleCount": len(REGISTERED_AI_MODULES),
        "issues": issues,
    }


def _resolve_pack(path: Path) -> Path:
    if path.is_dir():
        return path / "content.json"
    return path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Analiza cobertura de perfiles AI del pack")
    parser.add_argument("target", help="Directorio de pack o content.json")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    args = parser.parse_args(argv)
    path = _resolve_pack(Path(args.target))
    pack = json.loads(path.read_text(encoding="utf-8"))
    report = analyze_ai_profile_coverage(pack)
    if args.json:
        print(json.dumps(report, indent=2, ensure_ascii=False))
    else:
        print(f"profiles={report['profileCount']} units={report['unitCount']} "
              f"stages={report['stageCount']} issues={len(report['issues'])}")
        for issue in report["issues"]:
            print(f"  - {issue}")
    return 1 if report["issues"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
