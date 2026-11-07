#!/usr/bin/env python3
"""Valida IDs de plantillas nativas contra IDs conocidos del juego.

Los IDs nativos provienen de la inspección estática del juego
(``resources.assets`` / catálogo de assets) y de los packs de muestra.
La función pura :func:`validate_native_templates` devuelve diagnósticos
para plantillas de unidad, fábrica, etapa, paisaje y AI que no aparecen
en las listas conocidas; el CLI imprime los hallazgos o JSON.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key

KNOWN_UNIT_TEMPLATES = {
    "PCXBaseTank", "ArtilleryTank", "GatlingWalker", "BombBike",
    "LightningTank",
}

KNOWN_FACTORY_TEMPLATES = {
    "PCXBaseTankFactory", "ArtilleryTankFactory", "GatlingWalkerFactory",
    "BombBikeFactory", "LightningTankFactory",
}

KNOWN_STAGE_TEMPLATES = {
    "StageParameters_Pools_Level1SimpleRoleChoiceWithPriceLimit",
    "StageParameters_Pools_Level3",
    "StageParameters_Pools_Level1",
    "StageParameters_Pools_Level2",
}

KNOWN_LANDSCAPE_TEMPLATES = {"27 Circles"}

KNOWN_AI_TEMPLATES = {
    "_01_ Quick Hunter Enemy Var",
    "_01_Enemy - Melee Poker PCXPoker Variant",
    "_01_ Jeeps",
    "_01_CF Hordes Enemy - constant weak attacks, some big waves, learn crowd control and AoE Variant",
}


def validate_native_templates(
    pack: dict,
    known_unit_templates: set[str] | None = None,
    known_factory_templates: set[str] | None = None,
    known_stage_templates: set[str] | None = None,
    known_landscape_templates: set[str] | None = None,
    known_ai_templates: set[str] | None = None,
) -> list[str]:
    """Audita plantillas nativas de ``pack`` y devuelve diagnósticos."""
    pack = _as_dict(pack)
    findings: list[str] = []
    pack_id = pack.get("packId") if isinstance(pack.get("packId"), str) else "<unknown>"
    prefix = f"[{pack_id}]"

    units_known = KNOWN_UNIT_TEMPLATES | (known_unit_templates or set())
    factories_known = KNOWN_FACTORY_TEMPLATES | (known_factory_templates or set())
    stages_known = KNOWN_STAGE_TEMPLATES | (known_stage_templates or set())
    landscapes_known = KNOWN_LANDSCAPE_TEMPLATES | (known_landscape_templates or set())
    ai_known = KNOWN_AI_TEMPLATES | (known_ai_templates or set())

    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        uid = _entity_key(unit) or "<unknown>"
        template = unit.get("nativeTemplateId")
        if isinstance(template, str) and template.strip():
            if template not in units_known:
                findings.append(
                    f"{prefix} unit '{uid}': plantilla de unidad "
                    f"desconocida '{template}'"
                )
        factory = unit.get("productionBuilding")
        if isinstance(factory, dict):
            fid = _entity_key(factory) or "<unknown>"
            ftemplate = factory.get("nativeTemplateId")
            if isinstance(ftemplate, str) and ftemplate.strip():
                if ftemplate not in factories_known:
                    findings.append(
                        f"{prefix} factory '{fid}': plantilla de fábrica "
                        f"desconocida '{ftemplate}'"
                    )

    for stage in _as_list(pack.get("stages")):
        if not isinstance(stage, dict):
            continue
        sid = _entity_key(stage) or "<unknown>"
        template = stage.get("nativeTemplateId")
        if isinstance(template, str) and template.strip():
            if template not in stages_known:
                findings.append(
                    f"{prefix} stage '{sid}': plantilla de etapa "
                    f"desconocida '{template}'"
                )
        landscape = stage.get("landscape")
        if isinstance(landscape, dict):
            ltemplate = landscape.get("nativeTemplateId")
            if isinstance(ltemplate, str) and ltemplate.strip():
                if ltemplate not in landscapes_known:
                    findings.append(
                        f"{prefix} stage '{sid}': plantilla de paisaje "
                        f"desconocida '{ltemplate}'"
                    )

    for profile in _as_list(pack.get("aiProfiles")):
        if not isinstance(profile, dict):
            continue
        pid = _entity_key(profile) or "<unknown>"
        template = profile.get("nativeTemplateId")
        if isinstance(template, str) and template.strip():
            if template not in ai_known:
                findings.append(
                    f"{prefix} aiProfile '{pid}': plantilla AI "
                    f"desconocida '{template}'"
                )

    return findings


def _resolve_pack(path: Path) -> Path:
    if path.is_dir():
        return path / "content.json"
    return path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Valida plantillas nativas contra IDs conocidos"
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
    issues = validate_native_templates(pack)
    if args.json:
        print(json.dumps({"ok": not issues, "issues": issues},
                         ensure_ascii=False, indent=2))
    else:
        if issues:
            for issue in issues:
                print(issue)
        else:
            print("OK: plantillas nativas conocidas")
    return 0 if not issues else 1


if __name__ == "__main__":
    sys.exit(main())
