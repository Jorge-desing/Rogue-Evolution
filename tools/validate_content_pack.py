#!/usr/bin/env python3
"""Valida un content pack de Rogue Command.

Uso:
    python tools/validate_content_pack.py <pack_dir> [--schema schema.json]
"""
import argparse
import json
import sys
import stat
from pathlib import Path, PureWindowsPath

import jsonschema

SCHEMA_PATH = Path(__file__).resolve().parents[1] / "content/schema/content.schema.json"


def _reference_error(root: Path, value: object) -> str | None:
    if not isinstance(value, str) or not value:
        return f"ruta inválida: {value!r}"
    windows = PureWindowsPath(value)
    if windows.anchor or ".." in windows.parts or ":" in value or "\x00" in value:
        return f"ruta no relativa al pack: {value}"

    candidate = root
    try:
        for part in windows.parts:
            candidate = candidate / part
            info = candidate.lstat()
            if stat.S_ISLNK(info.st_mode) or (
                getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT
            ):
                return f"enlace o punto de reanálisis no permitido: {value}"
        if windows.parts and stat.S_ISREG(info.st_mode):
            return None
    except FileNotFoundError:
        pass
    except OSError as exc:
        return f"no se pudo inspeccionar {value}: {exc}"
    return f"no existe o no es un archivo: {value}"


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

    def check_reference(label, value):
        if value is not None and value != "":
            error = _reference_error(pack_path, value)
            if error:
                errors.append(f"{label}: {error}")

    for section, label, id_field in (("units", "Unit", "unitId"), ("buildings", "Building", "buildingId")):
        for entity in data.get(section, []):
            for field in ("iconPath", "prefabPath", "bundlePath"):
                check_reference(f"{label} '{entity.get('id') or entity.get(id_field) or '?'}' {field}", entity.get(field))

    check_reference("assetBundleManifestPath", data.get("assetBundleManifestPath"))
    for bundle in data.get("assetBundles", []):
        check_reference("AssetBundle", bundle.get("path") or bundle.get("bundlePath") or bundle.get("file"))

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
