#!/usr/bin/env python3
"""Check content-pack asset references against Unity AssetBundle entries."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path, PurePosixPath
from typing import Any

import UnityPy


EXPECTED_TYPES = {
    "Sprite card image": {"Sprite"},
    "GameObject prefab": {"GameObject"},
    "ScriptableObject asset": {"MonoBehaviour", "ScriptableObject"},
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Validate content.json AssetBundle references without launching Rogue Command."
    )
    parser.add_argument("pack", type=Path, help="Directory containing the pack's content.json.")
    return parser.parse_args()


def safe_bundle_path(pack_root: Path, relative_path: str) -> Path:
    if not relative_path or "\\" in relative_path or ":" in relative_path or relative_path.startswith("/"):
        raise ValueError(f"invalid bundle path: {relative_path!r}")
    parts = relative_path.split("/")
    if any(part in ("", ".", "..") for part in parts):
        raise ValueError(f"bundle path must be relative and cannot traverse directories: {relative_path!r}")

    candidate = pack_root.joinpath(*parts)
    resolved = candidate.resolve(strict=True)
    if pack_root not in resolved.parents or not resolved.is_file():
        raise ValueError(f"bundle path escapes the pack or is not a file: {relative_path!r}")
    if any(part.is_symlink() for part in (candidate, *candidate.parents) if part != pack_root.parent):
        raise ValueError(f"bundle path cannot contain symlinks: {relative_path!r}")
    return resolved


def load_asset_entries(bundle_path: Path) -> list[tuple[str, str]]:
    environment = UnityPy.load(str(bundle_path))
    entries: list[tuple[str, str]] = []
    for serialized_file in environment.files.values():
        for name, object_reader in getattr(serialized_file, "container", {}).items():
            object_type = getattr(getattr(object_reader, "type", None), "name", "Unknown")
            entries.append((str(name).replace("\\", "/"), str(object_type)))
    return entries


def resolve_asset(entries: list[tuple[str, str]], requested_name: str) -> list[tuple[str, str]]:
    if not isinstance(requested_name, str):
        return []
    if "\\" in requested_name:
        return []
    exact = [entry for entry in entries if entry[0] == requested_name]
    if exact:
        return exact
    if "/" in requested_name:
        return []

    requested = requested_name.casefold()
    requested_path = PurePosixPath(requested)
    if requested_path.suffix:
        return [entry for entry in entries if PurePosixPath(entry[0]).name.casefold() == requested]

    return [
        entry
        for entry in entries
        if PurePosixPath(entry[0]).stem.casefold() == requested
    ]


def collect_references(pack: dict[str, Any]) -> list[tuple[str, str, str, str]]:
    references: list[tuple[str, str, str, str]] = []

    def add(owner: str, field: str, bundle_id: str | None, asset_name: str | None, expected: str) -> None:
        if asset_name:
            references.append((f"{owner}.{field}", bundle_id or "", asset_name, expected))

    for unit in pack.get("units") or []:
        owner = f"unit:{unit.get('id', '<missing-id>')}"
        add(owner, "balanceAssetName", unit.get("assetBundleId"), unit.get("balanceAssetName"), "ScriptableObject asset")
        add(owner, "prefabAssetName", unit.get("assetBundleId"), unit.get("prefabAssetName"), "GameObject prefab")
        add(owner, "cardImageAssetName", unit.get("assetBundleId"), unit.get("cardImageAssetName"), "Sprite card image")
        add(owner, "visualAssetName", unit.get("assetBundleId"), unit.get("visualAssetName"), "GameObject prefab")

        factory = unit.get("productionBuilding")
        if isinstance(factory, dict):
            factory_owner = f"productionBuilding:{factory.get('id', '<missing-id>')}"
            factory_bundle_id = factory.get("assetBundleId") or ""
            add(factory_owner, "cardImageAssetName", factory_bundle_id,
                factory.get("cardImageAssetName"), "Sprite card image")
            add(factory_owner, "visualAssetName", factory_bundle_id,
                factory.get("visualAssetName"), "GameObject prefab")

    for stage in pack.get("stages") or []:
        if not stage.get("nativeTemplateId"):
            add(
                f"stage:{stage.get('id', '<missing-id>')}",
                "assetName",
                stage.get("assetBundleId"),
                stage.get("assetName"),
                "ScriptableObject asset",
            )

    for profile in pack.get("aiProfiles") or []:
        if not profile.get("nativeTemplateId"):
            add(
                f"aiProfile:{profile.get('id', '<missing-id>')}",
                "prefabAssetName",
                profile.get("assetBundleId"),
                profile.get("prefabAssetName"),
                "GameObject prefab",
            )
    return references


def validate_references(
    references: list[tuple[str, str, str, str]],
    bundle_entries: dict[str, list[tuple[str, str]]],
) -> tuple[list[str], int]:
    diagnostics: list[str] = []
    failures = 0
    for location, bundle_id, asset_name, expected_label in references:
        if bundle_id not in bundle_entries:
            diagnostics.append(f"ERROR {location}: AssetBundle ID {bundle_id!r} is not declared")
            failures += 1
            continue

        matches = resolve_asset(bundle_entries[bundle_id], asset_name)
        if not matches:
            diagnostics.append(
                f"ERROR {location}: {asset_name!r} does not match a serialized AssetBundle entry"
            )
            failures += 1
            continue
        if len(matches) != 1:
            candidates = ", ".join(f"{path} ({kind})" for path, kind in matches)
            diagnostics.append(f"ERROR {location}: {asset_name!r} is ambiguous: {candidates}")
            failures += 1
            continue

        path, object_type = matches[0]
        expected_types = EXPECTED_TYPES[expected_label]
        if object_type not in expected_types:
            diagnostics.append(
                f"ERROR {location}: {path} is {object_type}, expected {expected_label} "
                f"({', '.join(sorted(expected_types))})"
            )
            failures += 1
            continue
        diagnostics.append(f"OK {location}: {path} ({object_type})")

    return diagnostics, failures


def main() -> int:
    args = parse_args()
    pack_root = args.pack.resolve()
    manifest_path = pack_root / "content.json"
    if not pack_root.is_dir() or not manifest_path.is_file():
        print(f"Pack directory must contain content.json: {pack_root}", file=sys.stderr)
        return 2

    try:
        pack = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
        if not isinstance(pack, dict):
            raise ValueError("content.json root must be an object")
        bundle_entries: dict[str, list[tuple[str, str]]] = {}
        for bundle in pack.get("assetBundles") or []:
            bundle_id = str(bundle.get("id", ""))
            if not bundle_id or bundle_id in bundle_entries:
                raise ValueError(f"missing or duplicate AssetBundle ID: {bundle_id!r}")
            path = safe_bundle_path(pack_root, str(bundle.get("path", "")))
            bundle_entries[bundle_id] = load_asset_entries(path)
        references = collect_references(pack)
    except Exception as exc:
        print(f"Could not inspect pack bundles: {type(exc).__name__}: {exc}", file=sys.stderr)
        return 2

    diagnostics, failures = validate_references(references, bundle_entries)
    for diagnostic in diagnostics:
        print(diagnostic)

    print(f"Checked {len(references)} AssetBundle reference(s); {failures} error(s).")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
