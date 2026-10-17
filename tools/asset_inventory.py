#!/usr/bin/env python3
"""Build a read-only index of serialized Unity assets for Rogue Command."""

from __future__ import annotations

import argparse
import csv
import json
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import UnityPy


ASSET_SUFFIXES = {".assets", ".bundle", ".assetbundle", ".unity3d", ".ab"}
CONTENT_TERMS = (
    "unit",
    "robot",
    "enemy",
    "turret",
    "blueprint",
    "level",
    "stage",
    "map",
    "world",
    "landscape",
    "mission",
    "progression",
    "boss",
    "faction",
)
CSV_COLUMNS = ("source_file", "path_id", "class", "class_id", "name")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Index Unity asset object names and types without exporting or modifying game files."
        )
    )
    parser.add_argument(
        "--asset-root",
        required=True,
        type=Path,
        help="Rogue Command_Data directory from the installed game.",
    )
    parser.add_argument(
        "--output-dir",
        required=True,
        type=Path,
        help="Directory outside the game install where the CSV and JSON report are written.",
    )
    return parser.parse_args()


def is_serialized_asset(path: Path, asset_root: Path) -> bool:
    if path.suffix.lower() in ASSET_SUFFIXES:
        return True
    name = path.name.lower()
    if path.parent == asset_root and (name == "globalgamemanagers" or (name.startswith("level") and name[5:].isdigit())):
        return True
    return False


def get_name(obj: Any) -> str:
    peek = getattr(obj, "peek_name", None)
    if not callable(peek):
        return ""
    try:
        value = peek()
    except Exception:
        return ""
    return "" if value is None else str(value)


def value_or_none(value: Any) -> int | str | None:
    if value is None:
        return None
    try:
        return int(value)
    except (TypeError, ValueError):
        return str(value)


def find_assets(asset_root: Path) -> list[Path]:
    return sorted(
        (path for path in asset_root.rglob("*") if path.is_file() and is_serialized_asset(path, asset_root)),
        key=lambda path: path.as_posix().casefold(),
    )


def get_unity_versions(env: Any) -> list[str]:
    versions: set[str] = set()
    for obj in env.objects:
        asset_file = getattr(obj, "assets_file", None)
        version = getattr(asset_file, "unity_version", None)
        if version:
            versions.add(str(version))
    return sorted(versions)


def main() -> int:
    args = parse_args()
    asset_root = args.asset_root.resolve()
    output_dir = args.output_dir.resolve()

    if not asset_root.is_dir():
        print(f"No existe la carpeta de assets: {asset_root}", file=sys.stderr)
        return 2
    if output_dir == asset_root or asset_root in output_dir.parents:
        print("El directorio de salida debe estar fuera de la instalación del juego.", file=sys.stderr)
        return 2

    files = find_assets(asset_root)
    if not files:
        print("No se encontraron archivos serializados compatibles en la carpeta indicada.", file=sys.stderr)
        return 2

    output_dir.mkdir(parents=True, exist_ok=True)
    csv_path = output_dir / "asset-objects.csv"
    summary_path = output_dir / "asset-inventory-summary.json"
    type_counts: Counter[str] = Counter()
    file_records: list[dict[str, Any]] = []
    interesting_objects: list[dict[str, Any]] = []
    total_objects = 0

    with csv_path.open("w", newline="", encoding="utf-8-sig") as output:
        writer = csv.DictWriter(output, fieldnames=CSV_COLUMNS)
        writer.writeheader()

        for index, path in enumerate(files, start=1):
            relative_path = path.relative_to(asset_root).as_posix()
            record: dict[str, Any] = {
                "path": relative_path,
                "bytes": path.stat().st_size,
                "status": "ok",
                "unityVersions": [],
                "objectCount": 0,
                "typeCounts": {},
            }
            per_file_counts: Counter[str] = Counter()

            try:
                env = UnityPy.load(str(path))
                record["unityVersions"] = get_unity_versions(env)
                for obj in env.objects:
                    type_info = getattr(obj, "type", None)
                    type_name = str(getattr(type_info, "name", type_info or "Unknown"))
                    class_id = value_or_none(getattr(type_info, "value", None))
                    path_id = value_or_none(getattr(obj, "path_id", None))
                    name = get_name(obj)
                    row = {
                        "source_file": relative_path,
                        "path_id": "" if path_id is None else path_id,
                        "class": type_name,
                        "class_id": "" if class_id is None else class_id,
                        "name": name,
                    }
                    writer.writerow(row)
                    per_file_counts[type_name] += 1
                    total_objects += 1

                    name_lower = name.casefold()
                    if "scriptableobject" in type_name.casefold() or any(term in name_lower for term in CONTENT_TERMS):
                        interesting_objects.append(row)

                record["objectCount"] = sum(per_file_counts.values())
                record["typeCounts"] = dict(sorted(per_file_counts.items()))
                type_counts.update(per_file_counts)
            except Exception as exc:
                record["status"] = "error"
                record["error"] = f"{type(exc).__name__}: {exc}"[:500]

            file_records.append(record)
            print(f"[{index}/{len(files)}] {relative_path}: {record['status']} ({record['objectCount']} objetos)")

    version_file = asset_root.parent / "version.txt"
    game_version = None
    if version_file.is_file():
        game_version = version_file.read_text(encoding="utf-8", errors="replace").strip()

    report = {
        "schemaVersion": 1,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "readOnly": True,
        "unityPyVersion": UnityPy.__version__,
        "gameVersion": game_version,
        "assetRootName": asset_root.name,
        "filesFound": len(files),
        "filesRead": sum(record["status"] == "ok" for record in file_records),
        "filesWithErrors": sum(record["status"] == "error" for record in file_records),
        "objectsIndexed": total_objects,
        "typeCounts": dict(sorted(type_counts.items())),
        "files": file_records,
        "contentNameMatches": interesting_objects,
        "contentNameSearchTerms": list(CONTENT_TERMS),
    }
    summary_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"\nÍndice: {csv_path}")
    print(f"Resumen: {summary_path}")
    print(f"Objetos indexados: {total_objects}; archivos con errores: {report['filesWithErrors']}")
    return 0 if report["filesWithErrors"] == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
