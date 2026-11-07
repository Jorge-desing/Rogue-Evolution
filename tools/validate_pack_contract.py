#!/usr/bin/env python3
"""Valida el contrato cruzado de un content pack de Rogue Command.

La función pura :func:`validate_contract` comprueba invariantes que las
herramientas existentes no cubren en conjunto: formato de ``packId``,
paridad efectiva unidad/fábrica (``rarity`` y ``neededExperienceLevel``),
roles de producto frente a edificios, parejas visuales y de bundle,
rango de mapa de etapas, ofertas de fábrica en etapas, formato de ruta de
bundles, IDs duplicados globales y perfiles AI nativos sin aislamiento
de alcance. No muta el pack ni los registros nativos.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key

_PACK_ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_-]*:[A-Za-z0-9][A-Za-z0-9_-]*$")
_BUNDLE_PATH = re.compile(r"^bundles/[a-z0-9][a-z0-9._-]*\.bundle$")
_DEVICE_NAMES = {"con", "nul", "prn", "aux"} | {
    f"com{i}" for i in range(1, 10)
} | {f"lpt{i}" for i in range(1, 10)}


def _effective_value(entity: dict, name: str) -> str | None:
    """Valor efectivo de un campo (estructurado > ``balanceOverrides``)."""
    value = entity.get(name)
    if value is not None and value != "":
        return str(value)
    for override in _as_list(entity.get("balanceOverrides")):
        if isinstance(override, dict) and override.get("name") == name:
            raw = override.get("value")
            return None if raw is None else str(raw)
    return None


def _effective_roles(entity: dict) -> list[str]:
    roles = entity.get("roles")
    if isinstance(roles, list):
        return [str(r) for r in roles if isinstance(r, str)]
    raw = _effective_value(entity, "roles")
    if raw:
        return [part.strip() for part in raw.split(",") if part.strip()]
    return []


def _check_parity(findings: list[str], unit: dict) -> None:
    unit_id = _entity_key(unit) or "?"
    factory = unit.get("productionBuilding")
    if not isinstance(factory, dict):
        return
    for field in ("rarity", "neededExperienceLevel"):
        unit_value = _effective_value(unit, field)
        factory_value = _effective_value(factory, field)
        if unit_value is None and factory_value is None:
            continue
        if unit_value != factory_value:
            findings.append(
                f"unit '{unit_id}': {field} parity unit={unit_value!r} "
                f"factory={factory_value!r}"
            )


def _check_visual_and_bundle(findings: list[str], label: str, entity: dict) -> None:
    has_card = bool(entity.get("cardImageAssetName"))
    has_visual = bool(entity.get("visualAssetName"))
    has_root = bool(entity.get("visualRootPath"))
    if (has_card or has_visual or has_root) and not entity.get("assetBundleId"):
        findings.append(f"{label}: assetBundleId ausente con assets visuales")
    if has_visual != has_root:
        findings.append(f"{label}: visualAssetName/visualRootPath deben ir juntos")


def validate_contract(pack: dict) -> list[str]:
    """Devuelve diagnósticos de contrato para ``pack`` (vacío si es válido)."""
    pack = _as_dict(pack)
    findings: list[str] = []

    pack_id = pack.get("packId")
    if not isinstance(pack_id, str) or not _PACK_ID.match(pack_id):
        findings.append(
            f"packId {pack_id!r} no tiene forma 'namespace:local-id'"
        )
    schema_version = pack.get("schemaVersion")
    if schema_version is not None and schema_version != 1:
        findings.append(f"schemaVersion {schema_version!r} no soportado (se espera 1)")

    seen: dict[str, str] = {}

    def _register(entity_id: str | None, kind: str) -> None:
        if not entity_id:
            return
        if entity_id in seen:
            findings.append(
                f"id duplicado '{entity_id}': {kind} y {seen[entity_id]}"
            )
        else:
            seen[entity_id] = kind

    factory_ids: set[str] = set()
    for bundle in _as_list(pack.get("assetBundles")):
        if isinstance(bundle, dict):
            _register(_entity_key(bundle), "bundle")
    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        unit_id = _entity_key(unit)
        _register(unit_id, "unit")
        factory = unit.get("productionBuilding")
        if isinstance(factory, dict):
            factory_id = _entity_key(factory)
            _register(factory_id, "factory")
            if factory_id:
                factory_ids.add(factory_id)
    for section, kind in (
        ("stages", "stage"),
        ("aiProfiles", "aiProfile"),
        ("relics", "relic"),
        ("buildings", "building"),
        ("upgrades", "upgrade"),
    ):
        for entity in _as_list(pack.get(section)):
            if isinstance(entity, dict):
                _register(_entity_key(entity), kind)

    for unit in _as_list(pack.get("units")):
        if not isinstance(unit, dict):
            continue
        unit_id = _entity_key(unit) or "?"
        label = f"unit '{unit_id}'"
        _check_parity(findings, unit)
        _check_visual_and_bundle(findings, label, unit)
        for role in _effective_roles(unit):
            if role in ("Factory", "All"):
                findings.append(f"{label}: rol de producto prohibido '{role}'")
        factory = unit.get("productionBuilding")
        if isinstance(factory, dict):
            factory_label = f"factory '{_entity_key(factory)}'"
            _check_visual_and_bundle(findings, factory_label, factory)
            roles = _effective_roles(factory)
            if roles:
                if "Building" not in roles:
                    findings.append(f"{factory_label}: falta rol 'Building'")
                if "Factory" not in roles:
                    findings.append(f"{factory_label}: falta rol 'Factory'")

    for stage in _as_list(pack.get("stages")):
        if not isinstance(stage, dict):
            continue
        stage_id = _entity_key(stage) or "?"
        stage_label = f"stage '{stage_id}'"
        map_min = stage.get("mapSizeMin")
        map_max = stage.get("mapSizeMax")
        for value, name in ((map_min, "mapSizeMin"), (map_max, "mapSizeMax")):
            if isinstance(value, (int, float)) and not isinstance(value, bool):
                if not (1 <= value <= 128):
                    findings.append(f"{stage_label}: {name}={value} fuera de 1..128")
        if (
            isinstance(map_min, (int, float))
            and isinstance(map_max, (int, float))
            and not isinstance(map_min, bool)
            and not isinstance(map_max, bool)
            and map_min > map_max
        ):
            findings.append(f"{stage_label}: mapSizeMin={map_min} > mapSizeMax={map_max}")
        for offered in _as_list(stage.get("unitIds")):
            if offered in factory_ids:
                findings.append(
                    f"{stage_label}: unitIds ofrece la fábrica '{offered}'"
                )

    for bundle in _as_list(pack.get("assetBundles")):
        if not isinstance(bundle, dict):
            continue
        path = bundle.get("path")
        if (
            not isinstance(path, str)
            or not _BUNDLE_PATH.match(path)
            or ".." in path
            or "\\" in path
        ):
            findings.append(f"bundle path inválido {path!r}")
        elif path[len("bundles/"):-len(".bundle")] in _DEVICE_NAMES:
            findings.append(f"bundle path con nombre de dispositivo {path!r}")

    for profile in _as_list(pack.get("aiProfiles")):
        if not isinstance(profile, dict):
            continue
        profile_id = _entity_key(profile) or "?"
        if profile.get("nativeTemplateId") and profile.get("scopeToReferencedStages") is not True:
            findings.append(
                f"aiProfile '{profile_id}': plantilla nativa sin "
                "scopeToReferencedStages=true"
            )

    return findings


def _resolve_pack(path: Path) -> Path:
    return path / "content.json" if path.is_dir() else path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Valida el contrato cruzado de un content pack"
    )
    parser.add_argument("target", nargs="?", default=None,
                          help="Directorio de pack o content.json")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    parser.add_argument("--all", action="store_true", help="Audita todo content/packs")
    args = parser.parse_args(argv)
    targets: list[Path] = []
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
        findings = validate_contract(pack)
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
