#!/usr/bin/env python3
"""Decode selected Unity MonoBehaviour objects without modifying game files."""

from __future__ import annotations

import argparse
import json
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Decode selected serialized objects using the game's local managed assemblies."
    )
    parser.add_argument("--game-root", required=True, type=Path, help="Root directory of the installed game.")
    parser.add_argument(
        "--asset-file",
        required=True,
        help="Path relative to the game's *_Data directory, such as resources.assets.",
    )
    parser.add_argument("--path-id", action="append", required=True, type=int, help="Serialized object path ID; repeat as needed.")
    parser.add_argument("--output", required=True, type=Path, help="JSON output path outside the game install.")
    return parser.parse_args()


def collect_references(value: Any, objects: dict[int, Any], path: str = "") -> list[dict[str, Any]]:
    references: list[dict[str, Any]] = []
    if isinstance(value, dict):
        if "m_PathID" in value and "m_FileID" in value:
            try:
                path_id = int(value["m_PathID"])
                file_id = int(value["m_FileID"])
            except (TypeError, ValueError):
                return references
            if path_id == 0:
                return references
            target = objects.get(path_id) if file_id == 0 else None
            reference: dict[str, Any] = {
                "propertyPath": path or "$",
                "fileId": file_id,
                "pathId": path_id,
                "resolved": target is not None,
            }
            if target is not None:
                target_type = getattr(target, "type", None)
                reference["class"] = str(getattr(target_type, "name", target_type or "Unknown"))
                try:
                    reference["name"] = target.peek_name()
                except Exception as exc:
                    reference["nameError"] = f"{type(exc).__name__}: {exc}"[:500]
            references.append(reference)
            return references
        for key, item in value.items():
            child_path = f"{path}.{key}" if path else str(key)
            references.extend(collect_references(item, objects, child_path))
    elif isinstance(value, list):
        for index, item in enumerate(value):
            references.extend(collect_references(item, objects, f"{path}[{index}]"))
    return references


def main() -> int:
    args = parse_args()
    game_root = args.game_root.resolve()
    output_path = args.output.resolve()
    data_dirs = sorted(path for path in game_root.iterdir() if path.is_dir() and path.name.endswith("_Data")) if game_root.is_dir() else []
    if not data_dirs:
        print(f"No se encontró una carpeta *_Data dentro de: {game_root}", file=sys.stderr)
        return 2

    asset_path = (data_dirs[0] / args.asset_file).resolve()
    if data_dirs[0].resolve() not in asset_path.parents or not asset_path.is_file():
        print(f"El archivo de assets no existe dentro de la carpeta *_Data: {asset_path}", file=sys.stderr)
        return 2
    if output_path == game_root or game_root in output_path.parents:
        print("La salida debe estar fuera de la instalación del juego.", file=sys.stderr)
        return 2

    try:
        env = UnityPy.load(str(asset_path))
        objects = {int(obj.path_id): obj for obj in env.objects}
        unity_version = next(
            (
                str(version)
                for obj in objects.values()
                if (version := getattr(getattr(obj, "assets_file", None), "unity_version", None))
            ),
            None,
        )
        if not unity_version:
            print("No se pudo leer la versión de Unity del archivo de assets.", file=sys.stderr)
            return 2

        generator = TypeTreeGenerator(unity_version)
        generator.load_local_game(str(game_root))
        env.typetree_generator = generator
    except Exception as exc:
        print(f"No se pudo preparar el lector de assets: {type(exc).__name__}: {exc}", file=sys.stderr)
        return 2

    records: list[dict[str, Any]] = []
    failures = 0
    for path_id in dict.fromkeys(args.path_id):
        obj = objects.get(path_id)
        if obj is None:
            records.append({"pathId": path_id, "status": "not_found"})
            failures += 1
            continue

        type_info = getattr(obj, "type", None)
        record: dict[str, Any] = {
            "pathId": path_id,
            "class": str(getattr(type_info, "name", type_info or "Unknown")),
            "status": "ok",
        }
        try:
            record["name"] = obj.peek_name()
        except Exception as exc:
            record["nameError"] = f"{type(exc).__name__}: {exc}"[:500]
        try:
            data = obj.parse_as_dict()
            record["data"] = data
            record["references"] = collect_references(data, objects)
        except Exception as exc:
            record["status"] = "parse_error"
            record["error"] = f"{type(exc).__name__}: {exc}"[:1000]
            failures += 1
        records.append(record)

    report = {
        "schemaVersion": 1,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "readOnly": True,
        "unityPyVersion": UnityPy.__version__,
        "unityVersion": unity_version,
        "assetFile": asset_path.relative_to(data_dirs[0]).as_posix(),
        "objects": records,
        "failures": failures,
    }
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(
        json.dumps(report, ensure_ascii=False, indent=2, default=str) + "\n",
        encoding="utf-8",
    )
    print(f"Objetos solicitados: {len(records)}; errores o IDs ausentes: {failures}")
    print(f"Informe: {output_path}")
    return 0 if failures == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
