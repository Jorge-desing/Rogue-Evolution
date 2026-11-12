"""Analiza cobertura de perfiles AI en packs de contenido.

Refleja ``ContentAiProfileDefinition`` y los IDs de
``AiModuleRegistry.CreateDefault()``: cada perfil debe referenciar productos
de unidad existentes y módulos registrados. Las referencias de pack único se
pueden resolver contra el catálogo completo de unidades del CLI. La función
pura :func:`analyze_ai_profile_coverage` devuelve conteos e incidencias.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

from tools.ai_module_contracts import registered_ai_module_ids
from tools.merge_content_packs import _as_dict, _as_list, _entity_key

REGISTERED_AI_MODULES = registered_ai_module_ids()


def analyze_ai_profile_coverage(
    pack: dict,
    known_unit_ids: set[str] | None = None,
) -> dict[str, Any]:
    """Devuelve el informe de cobertura de perfiles AI del pack."""
    pack = _as_dict(pack)
    pack_id = pack.get("packId") if isinstance(pack.get("packId"), str) else None
    units = [u for u in _as_list(pack.get("units")) if isinstance(u, dict)]
    unit_ids = {_entity_key(u) for u in units if _entity_key(u)}
    if known_unit_ids is None:
        known_unit_ids = unit_ids
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
            unit_id = entry.get("unitId")
            if not (isinstance(unit_id, str) and unit_id in known_unit_ids):
                issues.append(f"perfil '{pid}': unitEntry unitId no resuelve ('{unit_id}')")

        # productionBuildingEntries.
        for entry in _as_list(profile.get("productionBuildingEntries")):
            if not isinstance(entry, dict):
                issues.append(f"perfil '{pid}': productionBuildingEntry no es objeto")
                continue
            unit_id = entry.get("unitId")
            if not (isinstance(unit_id, str) and unit_id in known_unit_ids):
                issues.append(
                    f"perfil '{pid}': productionBuildingEntry unitId no resuelve ('{unit_id}')"
                )

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
    known_unit_ids: set[str] = set()
    packs_root = Path(__file__).resolve().parents[1] / "content" / "packs"
    if packs_root.is_dir():
        for pack_file in packs_root.glob("*/content.json"):
            try:
                registered_pack = json.loads(pack_file.read_text(encoding="utf-8"))
            except (OSError, ValueError):
                continue
            for unit in _as_list(registered_pack.get("units")):
                unit_id = _entity_key(unit)
                if unit_id:
                    known_unit_ids.add(unit_id)

    report = analyze_ai_profile_coverage(pack, known_unit_ids=known_unit_ids)
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
