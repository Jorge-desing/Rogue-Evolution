#!/usr/bin/env python3
"""Valida un content pack de Rogue Command.

Uso:
    python tools/validate_content_pack.py <pack_dir> [--schema schema.json]
"""
import argparse
import json
import sys
from pathlib import Path

import jsonschema

SCHEMA_PATH = Path(__file__).resolve().parents[1] / "content/schema/content.schema.json"


def validate_pack(pack_dir: str, schema_path: str | None = None) -> list[str]:
    """Valida un content pack y retorna lista de errores (vacía si OK)."""
    errors = []
    pack_path = Path(pack_dir)

    if not pack_path.is_dir():
        return [f"Directorio no encontrado: {pack_dir}"]

    content_json = pack_path / "content.json"
    if not content_json.is_file():
        return [f"content.json no encontrado en: {pack_dir}"]

    try:
        data = json.loads(content_json.read_text(encoding="utf-8"))
    except (ValueError, OSError) as e:
        return [f"content.json inválido: {e}"]

    schema_file = Path(schema_path) if schema_path else SCHEMA_PATH
    if not schema_file.is_file():
        return [f"Schema no encontrado: {schema_file}"]
    try:
        active_schema = json.loads(schema_file.read_text(encoding="utf-8"))
        jsonschema.validate(instance=data, schema=active_schema)
    except jsonschema.ValidationError as e:
        return [f"Schema: {e.message} (path: {'/'.join(str(p) for p in e.absolute_path)})"]
    except (ValueError, OSError, jsonschema.SchemaError) as e:
        return [f"Schema inválido ({schema_file}): {e}"]

    if not isinstance(data, dict):
        return ["content.json debe ser un objeto"]

    # Verificar archivos referenciados por unidad (iconPath, prefabPath, bundlePath)
    for unit in data.get("units", []):
        for field in ("iconPath", "prefabPath", "bundlePath"):
            if field in unit and unit[field]:
                ref = pack_path / unit[field]
                if not ref.is_file():
                    uid = unit.get("id") or unit.get("unitId") or "?"
                    errors.append(f"Unit '{uid}': {field} no existe: {unit[field]}")

    for building in data.get("buildings", []):
        for field in ("iconPath", "prefabPath", "bundlePath"):
            if field in building and building[field]:
                ref = pack_path / building[field]
                if not ref.is_file():
                    bid = building.get("buildingId") or building.get("id") or "?"
                    errors.append(f"Building '{bid}': {field} no existe: {building[field]}")

    # Verificar archivos referenciados (assetBundleManifestPath y assetBundles)
    manifest_path = data.get("assetBundleManifestPath")
    if manifest_path:
        ref = pack_path / manifest_path
        if not ref.is_file():
            errors.append(f"assetBundleManifestPath no existe: {manifest_path}")

    for bundle in data.get("assetBundles", []):
        bundle_path = bundle.get("path") or bundle.get("bundlePath") or bundle.get("file")
        if bundle_path:
            ref = pack_path / bundle_path
            if not ref.is_file():
                errors.append(f"AssetBundle no existe: {bundle_path}")

    # Verificar duplicados de unit IDs
    unit_ids = [u.get("id") or u.get("unitId") for u in data.get("units", [])]
    if len(unit_ids) != len(set(unit_ids)):
        errors.append("Unit IDs duplicados detectados")

    building_ids = [b.get("buildingId") or b.get("id") for b in data.get("buildings", [])]
    if len(building_ids) != len(set(building_ids)):
        errors.append("Building IDs duplicados detectados")

    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description="Valida un content pack de Rogue Command")
    parser.add_argument("pack_dir", help="Directorio del content pack")
    parser.add_argument("--schema", help="Archivo JSON Schema externo (opcional)")
    args = parser.parse_args()

    errors = validate_pack(args.pack_dir, schema_path=args.schema)
    if errors:
        print(f"ERRORES ({len(errors)}):", file=sys.stderr)
        for e in errors:
            print(f"  - {e}", file=sys.stderr)
        return 1
    else:
        print(f"OK: content pack válido en {args.pack_dir}")
        return 0


if __name__ == "__main__":
    sys.exit(main())
