#!/usr/bin/env python3
"""Fusiona dos content packs de Rogue Command de forma aditiva.

La función pura :func:`merge_packs` combina unidades, etapas, perfiles AI y
AssetBundles de dos manifiestos, detectando conflictos de ID o de contenido.
El CLI escribe el manifiesto fusionado y, opcionalmente, lo valida.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from tools.scaffold_pack import _validate_pack_id


def _as_list(value: Any) -> list:
    return value if isinstance(value, list) else []


def _as_dict(value: Any) -> dict:
    return value if isinstance(value, dict) else {}


def _entity_key(entity: Any) -> str | None:
    if not isinstance(entity, dict):
        return None
    for field in ("id", "unitId", "stageId", "profileId", "aiProfileId"):
        value = entity.get(field)
        if isinstance(value, str) and value.strip():
            return value
    return None


def _bundle_key(bundle: Any) -> str:
    if not isinstance(bundle, dict):
        return json.dumps(bundle, sort_keys=True, ensure_ascii=False)
    for field in ("path", "bundlePath", "file", "id", "bundleId"):
        value = bundle.get(field)
        if isinstance(value, str) and value.strip():
            return value
    return json.dumps(bundle, sort_keys=True, ensure_ascii=False)


def _merge_entity_lists(
    left: list, right: list, label: str, conflicts: list[str]
) -> list:
    merged: list = []
    by_key: dict[str, dict] = {}
    for entity in left:
        key = _entity_key(entity)
        if key is None:
            merged.append(entity)
            continue
        by_key[key] = entity
        merged.append(entity)

    for entity in right:
        key = _entity_key(entity)
        if key is None:
            merged.append(entity)
            continue
        if key in by_key:
            if by_key[key] != entity:
                conflicts.append(
                    f"{label}: conflicto de contenido para id '{key}'"
                )
            # keep the left version on conflict
            continue
        by_key[key] = entity
        merged.append(entity)

    return merged


def _merge_bundles(left: list, right: list) -> list:
    merged: list = []
    seen: set[str] = set()
    for bundle in left + right:
        key = _bundle_key(bundle)
        if key in seen:
            continue
        seen.add(key)
        merged.append(bundle)
    return merged


def merge_packs(
    left: dict,
    right: dict,
    pack_id: str | None = None,
) -> tuple[dict, list[str]]:
    """Fusiona dos packs de forma aditiva.

    Devuelve ``(merged, conflicts)``. Si ``conflicts`` no está vacío, el
    resultado no debe escribirse como válido.
    """
    left = _as_dict(left)
    right = _as_dict(right)
    conflicts: list[str] = []

    # packId
    left_id = left.get("packId")
    right_id = right.get("packId")
    if pack_id is not None:
        try:
            _validate_pack_id(pack_id)
        except ValueError as exc:
            conflicts.append(f"packId inválido: {exc}")
            pack_id = None
        resolved_pack_id = pack_id
    else:
        if isinstance(left_id, str) and left_id == right_id:
            resolved_pack_id = left_id
        elif isinstance(left_id, str) and isinstance(right_id, str):
            conflicts.append(
                f"packId distinto entre manifiestos: '{left_id}' vs '{right_id}' "
                "(use --pack-id para forzar uno)"
            )
            resolved_pack_id = left_id
        else:
            resolved_pack_id = left_id if isinstance(left_id, str) else right_id

    # schemaVersion: máximo numérico
    def _schema_version(value):
        if isinstance(value, bool):
            return 0
        if isinstance(value, int):
            return value
        if isinstance(value, str):
            try:
                return int(value)
            except ValueError:
                return 0
        return 0

    merged_schema = max(_schema_version(left.get("schemaVersion")),
                        _schema_version(right.get("schemaVersion")))
    if isinstance(left.get("schemaVersion"), str) or isinstance(
        right.get("schemaVersion"), str
    ):
        # preserve string form if either side used strings
        if isinstance(left.get("schemaVersion"), str) or isinstance(
            right.get("schemaVersion"), str
        ):
            merged_schema = str(merged_schema)

    merged: dict[str, Any] = {}
    merged["schemaVersion"] = merged_schema if merged_schema != 0 else left.get(
        "schemaVersion", right.get("schemaVersion")
    )
    if resolved_pack_id is not None:
        merged["packId"] = resolved_pack_id

    # Preserve scalar metadata from left, then fill gaps from right.
    for key in ("displayName", "version", "assetBundleManifestPath"):
        if key in left:
            merged[key] = left[key]
        elif key in right:
            merged[key] = right[key]

    merged["assetBundles"] = _merge_bundles(
        _as_list(left.get("assetBundles")), _as_list(right.get("assetBundles"))
    )

    merged["units"] = _merge_entity_lists(
        _as_list(left.get("units")), _as_list(right.get("units")), "units", conflicts
    )
    merged["stages"] = _merge_entity_lists(
        _as_list(left.get("stages")), _as_list(right.get("stages")), "stages", conflicts
    )
    merged["aiProfiles"] = _merge_entity_lists(
        _as_list(left.get("aiProfiles")), _as_list(right.get("aiProfiles")),
        "aiProfiles", conflicts,
    )

    # Preserve any extra top-level keys from left then right.
    for source in (left, right):
        for key, value in source.items():
            if key not in merged:
                merged[key] = value

    return merged, conflicts


def write_merged(merged: dict, out_dir: str | Path) -> Path:
    out_path = Path(out_dir)
    out_path.mkdir(parents=True, exist_ok=True)
    target = out_path / "content.json"
    target.write_text(
        json.dumps(merged, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    return target


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Fusiona dos content packs de Rogue Command de forma aditiva"
    )
    parser.add_argument("--left", required=True, help="Directorio o content.json izquierdo")
    parser.add_argument("--right", required=True, help="Directorio o content.json derecho")
    parser.add_argument("--out-dir", required=True, help="Directorio destino")
    parser.add_argument("--pack-id", help="packId del manifiesto fusionado")
    parser.add_argument(
        "--validate",
        action="store_true",
        help="Valida el pack fusionado contra content/schema/content.schema.json",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Escribe aunque haya conflictos (no recomendado)",
    )
    args = parser.parse_args(argv)

    def _resolve(path_str: str) -> Path:
        p = Path(path_str)
        if p.is_dir():
            p = p / "content.json"
        return p

    left_path = _resolve(args.left)
    right_path = _resolve(args.right)
    if not left_path.is_file():
        print(f"Archivo no encontrado: {left_path}", file=sys.stderr)
        return 2
    if not right_path.is_file():
        print(f"Archivo no encontrado: {right_path}", file=sys.stderr)
        return 2

    try:
        left = json.loads(left_path.read_text(encoding="utf-8"))
        right = json.loads(right_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        print(f"JSON inválido: {exc}", file=sys.stderr)
        return 2

    merged, conflicts = merge_packs(left, right, pack_id=args.pack_id)

    if conflicts:
        print(f"Conflictos ({len(conflicts)}):", file=sys.stderr)
        for c in conflicts:
            print(f"  - {c}", file=sys.stderr)
        if not args.force:
            return 1

    written = write_merged(merged, args.out_dir)
    print(f"Pack fusionado escrito en {written}")

    if args.validate:
        repo_root = Path(__file__).resolve().parent.parent
        schema_path = repo_root / "content" / "schema" / "content.schema.json"
        import jsonschema

        schema = json.loads(schema_path.read_text(encoding="utf-8"))
        try:
            jsonschema.validate(instance=merged, schema=schema)
        except jsonschema.ValidationError as exc:
            print(f"Validación JSON Schema falló: {exc.message}", file=sys.stderr)
            return 1
        print("Validación JSON Schema: OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
