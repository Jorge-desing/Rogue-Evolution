#!/usr/bin/env python3
"""Audita referencias cruzadas de un content pack de Rogue Command.

La función pura :func:`audit_pack` detecta referencias colgantes (unidades,
perfiles AI y fábricas referenciadas pero no definidas), duplicidad de
fábricas, mapeo 1:1 roto y localizaciones sin ``en-US``. El CLI audita un
pack o todo ``content/packs``.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key


def _collect_defined_ids(pack: dict) -> tuple[set[str], set[str], set[str]]:
    unit_ids: set[str] = set()
    factory_ids: set[str] = set()
    profile_ids: set[str] = set()

    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        uid = _entity_key(unit)
        if uid:
            unit_ids.add(uid)
        factory = unit.get("productionBuilding")
        if isinstance(factory, dict):
            fid = _entity_key(factory)
            if fid:
                factory_ids.add(fid)

    for profile in _as_list(pack.get("aiProfiles")):
        if not isinstance(profile, dict):
            continue
        pid = _entity_key(profile)
        if pid:
            profile_ids.add(pid)

    return unit_ids, factory_ids, profile_ids


def _has_english_localization(entity: Any) -> tuple[bool, bool]:
    """Devuelve (has_en_name, has_en_description)."""
    if not isinstance(entity, dict):
        return False, False
    has_name = False
    has_desc = False
    for loc in _as_list(entity.get("localizations")):
        if not isinstance(loc, dict):
            continue
        if loc.get("language") != "en-US":
            continue
        if isinstance(loc.get("name"), str) and loc["name"].strip():
            has_name = True
        if isinstance(loc.get("description"), str) and loc["description"].strip():
            has_desc = True
    return has_name, has_desc


def audit_pack(pack: dict, known_unit_ids: set[str] | None = None,
               known_factory_ids: set[str] | None = None,
               known_profile_ids: set[str] | None = None) -> list[str]:
    """Audita un manifiesto y devuelve errores.

    ``known_*`` amplía el conjunto de IDs válidos (p. ej. para auditoría
    global multi-pack). Por defecto se usan los IDs de este pack.
    """
    pack = _as_dict(pack)
    findings: list[str] = []

    own_units, own_factories, own_profiles = _collect_defined_ids(pack)
    units = own_units | (known_unit_ids or set())
    factories = own_factories | (known_factory_ids or set())
    profiles = own_profiles | (known_profile_ids or set())

    pack_id = pack.get("packId") if isinstance(pack.get("packId"), str) else "<unknown>"
    prefix = f"[{pack_id}]"

    # --- Mapeo 1:1 fábrica <-> unidad -------------------------------------
    factory_owner: dict[str, str | None] = {}
    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        uid = _entity_key(unit) or "<unknown>"
        factory = unit.get("productionBuilding")
        if not isinstance(factory, dict):
            findings.append(f"{prefix} unit '{uid}': falta productionBuilding")
            continue
        fid = _entity_key(factory)
        if not fid:
            findings.append(f"{prefix} unit '{uid}': productionBuilding.id vacío")
            continue
        if fid in factory_owner:
            findings.append(
                f"{prefix} factory '{fid}': compartida por "
                f"'{factory_owner[fid]}' y '{uid}' (se exige 1:1)"
            )
        else:
            factory_owner[fid] = uid
        if fid in own_units:
            findings.append(
                f"{prefix} factory '{fid}': colisiona con un unit id del pack"
            )

    # --- Referencias en etapas -------------------------------------------
    for stage in _as_list(pack.get("stages")):
        if not isinstance(stage, dict):
            continue
        sid = _entity_key(stage) or "<unknown>"

        for uid in _as_list(stage.get("unitIds")):
            if isinstance(uid, str) and uid not in units:
                findings.append(
                    f"{prefix} stage '{sid}': unitIds referencia unidad "
                    f"no definida '{uid}'"
                )

        pool = stage.get("blueprintPoolEntries")
        if isinstance(pool, list):
            for entry in pool:
                if not isinstance(entry, dict):
                    continue
                pool_id = entry.get("poolId") if isinstance(entry.get("poolId"), str) else "<unknown>"
                for uid in _as_list(entry.get("unitIds")):
                    if isinstance(uid, str) and uid not in units:
                        findings.append(
                            f"{prefix} stage '{sid}' pool '{pool_id}': "
                            f"unitIds referencia unidad no definida '{uid}'"
                        )

        for pid in _as_list(stage.get("aiProfileIds")):
            if isinstance(pid, str) and pid not in profiles:
                findings.append(
                    f"{prefix} stage '{sid}': aiProfileIds referencia perfil "
                    f"no definido '{pid}'"
                )

    # --- Referencias en perfiles AI --------------------------------------
    for profile in _as_list(pack.get("aiProfiles")):
        if not isinstance(profile, dict):
            continue
        pid = _entity_key(profile) or "<unknown>"
        for section in ("unitEntries", "productionBuildingEntries"):
            for entry in _as_list(profile.get(section)):
                if not isinstance(entry, dict):
                    continue
                for uid in _as_list(entry.get("unitIds")):
                    if isinstance(uid, str) and uid not in units:
                        findings.append(
                            f"{prefix} ai '{pid}' {section}: unitId "
                            f"no definido '{uid}'"
                        )

    # --- Localización en-US ----------------------------------------------
    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        uid = _entity_key(unit) or "<unknown>"
        has_name, has_desc = _has_english_localization(unit)
        if not has_name:
            findings.append(f"{prefix} unit '{uid}': falta localización en-US name")
        if not has_desc:
            findings.append(f"{prefix} unit '{uid}': falta localización en-US description")
        factory = unit.get("productionBuilding")
        if isinstance(factory, dict):
            fid = _entity_key(factory) or "<unknown>"
            fname, fdesc = _has_english_localization(factory)
            if not fname:
                findings.append(f"{prefix} factory '{fid}': falta localización en-US name")
            if not fdesc:
                findings.append(f"{prefix} factory '{fid}': falta localización en-US description")

    # --- nativeTemplateId vacío (solo si tampoco usa asset bundle) -----
    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        uid = _entity_key(unit) or "<unknown>"
        template = unit.get("nativeTemplateId")
        bundle = unit.get("assetBundleId")
        template_empty = not isinstance(template, str) or not template.strip()
        bundle_empty = not isinstance(bundle, str) or not bundle.strip()
        if template_empty and bundle_empty:
            findings.append(
                f"{prefix} unit '{uid}': nativeTemplateId y assetBundleId vacíos"
            )

    return findings


def audit_all_packs(packs_root: Path) -> dict:
    """Audita todos los packs bajo ``packs_root`` con IDs globales."""
    pack_paths = sorted(Path(packs_root).glob("*/content.json"))
    loaded: dict[Path, dict] = {}
    load_errors: dict[Path, str] = {}

    for path in pack_paths:
        try:
            loaded[path] = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
            load_errors[path] = str(exc)

    # Global known IDs
    known_units: set[str] = set()
    known_factories: set[str] = set()
    known_profiles: set[str] = set()
    for pack in loaded.values():
        u, f, p = _collect_defined_ids(pack)
        known_units |= u
        known_factories |= f
        known_profiles |= p

    reports: list[dict] = []
    total_issues = 0
    for path in pack_paths:
        if path in load_errors:
            issue = f"FAIL {path.parent.name}: unable to read content.json: {load_errors[path]}"
            reports.append({
                "packId": path.parent.name,
                "packFolder": path.parent.name,
                "issues": [issue],
            })
            total_issues += 1
            continue
        pack = loaded[path]
        issues = audit_pack(
            pack,
            known_unit_ids=known_units,
            known_factory_ids=known_factories,
            known_profile_ids=known_profiles,
        )
        reports.append({
            "packId": pack.get("packId", path.parent.name),
            "packFolder": path.parent.name,
            "issues": issues,
        })
        total_issues += len(issues)

    return {
        "summary": {
            "checkedManifests": len(pack_paths),
            "issueCount": total_issues,
            "auditValid": total_issues == 0,
        },
        "limitations": [
            "Esta auditoría es estructural/offline; no prueba carga en runtime.",
            "Las referencias se resuelven contra IDs definidos en este pack o "
            "contra el conjunto global de content/packs.",
        ],
        "packs": reports,
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Audita referencias cruzadas de content packs"
    )
    parser.add_argument("--pack", help="Directorio o content.json a auditar")
    parser.add_argument("--all", action="store_true", help="Audita content/packs")
    parser.add_argument("--packs-root", default="content/packs", help="Raíz de packs (para --all)")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    args = parser.parse_args(argv)

    if args.all:
        result = audit_all_packs(Path(args.packs_root))
        if args.json:
            print(json.dumps(result, indent=2, ensure_ascii=False))
            return 0 if result["summary"]["auditValid"] else 1
        for report in result["packs"]:
            for issue in report["issues"]:
                print(issue)
        if result["summary"]["auditValid"]:
            print(f"PASS checked {result['summary']['checkedManifests']} manifests")
            return 0
        print(f"FAIL {result['summary']['issueCount']} issue(s) across "
              f"{result['summary']['checkedManifests']} manifests")
        return 1

    if not args.pack:
        parser.error("--pack o --all es obligatorio")

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

    issues = audit_pack(pack)
    if args.json:
        print(json.dumps({"issues": issues, "issueCount": len(issues)},
                         indent=2, ensure_ascii=False))
        return 0 if not issues else 1
    if issues:
        for issue in issues:
            print(issue)
        print(f"FAIL {len(issues)} issue(s)")
        return 1
    print("PASS audit sin issue")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
