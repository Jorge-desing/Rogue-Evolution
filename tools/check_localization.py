#!/usr/bin/env python3
"""Valida la localización de un content pack de Rogue Command.

La función pura :func:`validate_localization` comprueba el contrato de
``content/schema/README.md``: idiomas soportados por el juego, ausencia de
idiomas duplicados, nombre ``en-US`` no vacío cuando hay localizaciones,
``en-US`` obligatorio en unidades sin plantilla nativa, en paisajes de
etapa y en reliquias/mejoras/edificios. Las unidades con plantilla nativa
pueden omitir localizaciones y heredar el texto nativo. No muta el pack.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key

# Idiomas con resolución en el cargador (content/schema/README.md).
SUPPORTED_LANGUAGES = frozenset({
    "en-US", "de-DE", "ja-JP", "ko-KR", "zh-CN", "zh-TW",
    "fr-FR", "es-XL", "pt-BR", "ru-RU",
})


def _check_entity(findings: list[str], label: str, entity: dict,
                  require_english: bool) -> None:
    localizations = entity.get("localizations")
    if not isinstance(localizations, list) or not localizations:
        if require_english:
            findings.append(f"{label}: falta localización 'en-US'")
        return
    seen: set[str] = set()
    english_name: str | None = None
    for loc in localizations:
        if not isinstance(loc, dict):
            continue
        language = loc.get("language")
        if language in seen:
            findings.append(f"{label}: idioma duplicado '{language}'")
        seen.add(language)
        if language not in SUPPORTED_LANGUAGES:
            findings.append(f"{label}: idioma no soportado '{language}'")
        if language == "en-US":
            name = loc.get("name")
            english_name = name if isinstance(name, str) else None
    if english_name is None or not english_name.strip():
        findings.append(f"{label}: falta nombre 'en-US' no vacío")


def validate_localization(pack: dict) -> list[str]:
    """Devuelve diagnósticos de localización (vacío si el pack es válido)."""
    pack = _as_dict(pack)
    findings: list[str] = []

    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        unit_id = _entity_key(unit) or "?"
        custom = not unit.get("nativeTemplateId")
        _check_entity(findings, f"unit '{unit_id}'", unit, require_english=custom)
        factory = unit.get("productionBuilding")
        if isinstance(factory, dict):
            _check_entity(findings, f"factory '{_entity_key(factory)}'",
                          factory, require_english=True)

    for stage in _as_list(pack.get("stages")):
        if not isinstance(stage, dict):
            continue
        landscape = stage.get("landscape")
        if isinstance(landscape, dict):
            _check_entity(findings, f"landscape de '{_entity_key(stage)}'",
                          landscape, require_english=True)

    for section, kind in (("relics", "relic"), ("upgrades", "upgrade"),
                          ("buildings", "building")):
        for entity in _as_list(pack.get(section)):
            if not isinstance(entity, dict):
                continue
            _check_entity(findings, f"{kind} '{_entity_key(entity)}'",
                          entity, require_english=True)
    return findings


def _resolve_pack(path: Path) -> Path:
    return path / "content.json" if path.is_dir() else path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Valida la localización de un content pack")
    parser.add_argument("target", nargs="?", default=None,
                        help="Directorio de pack o content.json")
    parser.add_argument("--all", action="store_true",
                        help="Valida todo content/packs")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    args = parser.parse_args(argv)
    if args.all:
        packs_dir = Path(args.target) if args.target else Path("content/packs")
        targets = sorted(packs_dir.glob("*/content.json"))
    elif args.target:
        targets = [_resolve_pack(Path(args.target))]
    else:
        parser.error("se requiere target o --all")
    failures = 0
    for target in targets:
        try:
            pack = json.loads(target.read_text(encoding="utf-8"))
        except (OSError, ValueError) as exc:
            print(f"{target}: pack no legible: {exc}")
            failures += 1
            continue
        findings = validate_localization(pack)
        if args.json:
            print(json.dumps({"pack": str(target), "findings": findings},
                             ensure_ascii=False, indent=2))
        elif findings:
            print(f"{target}: {len(findings)} hallazgo(s)")
            for finding in findings:
                print(f"  {finding}")
        else:
            print(f"{target}: OK")
        failures += len(findings)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
