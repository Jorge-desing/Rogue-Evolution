#!/usr/bin/env python3
"""Lint de packs de Rogue Command (avisos de calidad, no errores duros).

Refleja los invariantes del loader: localización bilingüe en-US/es-XL,
mapeo 1:1 fábrica <-> unidad (``unitEntries`` usa el ID del producto,
no de la fábrica) y rangos de mapa coherentes. La función pura
:func:`lint_pack` devuelve avisos; el CLI imprime los avisos o JSON.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from tools.merge_content_packs import _as_dict, _as_list, _entity_key


def _has_language(entity: dict, language: str) -> bool:
    for loc in _as_list(entity.get("localizations")):
        if isinstance(loc, dict) and loc.get("language") == language:
            return True
    return False


def lint_pack(pack: dict) -> list[str]:
    """Devuelve avisos de lint para ``pack``."""
    pack = _as_dict(pack)
    warnings: list[str] = []
    pack_id = pack.get("packId") if isinstance(pack.get("packId"), str) else "<unknown>"
    prefix = f"[{pack_id}]"

    factory_ids: set[str] = set()
    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        uid = _entity_key(unit) or "<unknown>"
        factory = unit.get("productionBuilding")
        if isinstance(factory, dict):
            fid = _entity_key(factory)
            if fid:
                factory_ids.add(fid)
        # Localización española ausente: aviso (el loader solo exige en-US).
        if not _has_language(unit, "es-XL"):
            warnings.append(
                f"{prefix} unit '{uid}': sin localización es-XL (español)"
            )
        if isinstance(factory, dict):
            fid = _entity_key(factory) or "<unknown>"
            if not _has_language(factory, "es-XL"):
                warnings.append(
                    f"{prefix} factory '{fid}': sin localización es-XL (español)"
                )

    for stage in _as_list(pack.get("stages")):
        if not isinstance(stage, dict):
            continue
        sid = _entity_key(stage) or "<unknown>"
        lo, hi = stage.get("mapSizeMin"), stage.get("mapSizeMax")
        if isinstance(lo, int) and isinstance(hi, int) and lo > hi:
            warnings.append(
                f"{prefix} stage '{sid}': mapSizeMin ({lo}) > mapSizeMax ({hi})"
            )

    for profile in _as_list(pack.get("aiProfiles")):
        if not isinstance(profile, dict):
            continue
        pid = _entity_key(profile) or "<unknown>"
        for field in ("unitEntries", "productionBuildingEntries"):
            for entry in _as_list(profile.get(field)):
                if not isinstance(entry, dict):
                    continue
                uid = entry.get("unitId")
                if isinstance(uid, str) and uid in factory_ids:
                    warnings.append(
                        f"{prefix} aiProfile '{pid}': {field} usa el ID de "
                        f"fábrica '{uid}'; debe ser el ID del producto"
                    )

    return warnings


def _resolve_pack(path: Path) -> Path:
    if path.is_dir():
        return path / "content.json"
    return path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Lint de content pack")
    parser.add_argument("target", help="Directorio de pack o content.json")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    parser.add_argument("--all", action="store_true",
                        help="Lint de todo content/packs")
    args = parser.parse_args(argv)

    targets: list[Path] = []
    if args.all:
        root = Path("content/packs")
        if root.is_dir():
            targets = sorted(
                p / "content.json" for p in root.iterdir() if p.is_dir()
            )
    else:
        targets = [_resolve_pack(Path(args.target))]

    all_warnings: list[str] = []
    for target in targets:
        try:
            pack = json.loads(target.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            continue
        all_warnings.extend(lint_pack(pack))

    if args.json:
        print(json.dumps({"warnings": all_warnings},
                         ensure_ascii=False, indent=2))
    else:
        if all_warnings:
            for warning in all_warnings:
                print(warning)
        else:
            print("OK: sin avisos de lint")
    return 0


if __name__ == "__main__":
    sys.exit(main())
