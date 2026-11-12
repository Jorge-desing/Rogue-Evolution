#!/usr/bin/env python3
"""Bind generated Level 2 visual modules to declared non-primary variants.

The canonical manifest is generated from active pack declarations by
Build-CanonicalVisualModules.py. This script only adds the visual `modules`
field to those exact variants; it leaves primary Level 1 variants unchanged.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "content" / "source-art" / "canonical-visual-modules.json"
PACK_ROOT = ROOT / "content" / "packs"
EXPECTED_PACKS = ("sample-native-expansion", "tactical-operations")
SLOT_ORDER = {"ERA": 0, "APS": 1, "Sponsons": 2, "FuelDrums": 3}
MAX_MODULES_PER_VARIANT = 4


def load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8-sig") as stream:
        return json.load(stream)


def write_json(path: Path, value: Any) -> None:
    with path.open("w", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)
        stream.write("\n")


def bind(check_only: bool) -> tuple[int, int, int, int]:
    manifest = load_json(MANIFEST)
    modules = manifest.get("modules")
    if not isinstance(modules, list):
        raise ValueError(f"Manifest has no modules array: {MANIFEST}")

    modules_by_pack: dict[str, list[dict[str, Any]]] = {pack: [] for pack in EXPECTED_PACKS}
    for module in modules:
        pack = module.get("packName")
        if pack not in modules_by_pack or module.get("level") != 2:
            raise ValueError(f"Unexpected module declaration: {module}")
        if not str(module.get("assetBundleId", "")).endswith(":assets"):
            raise ValueError(f"Module bundle is not the owning pack's assets bundle: {module}")
        modules_by_pack[pack].append(module)

    if not modules:
        raise ValueError("Canonical module manifest contains no Level 2 modules")
    seen_bindings: set[tuple[str, str, str]] = set()
    for module in modules:
        slot_id = module.get("slotId")
        key = (module.get("packName"), module.get("variantId"), slot_id)
        if slot_id not in SLOT_ORDER or key in seen_bindings:
            raise ValueError(f"Invalid or duplicate Level 2 variant-slot binding: {key}")
        seen_bindings.add(key)

    changed = 0
    primary_count = 0
    for pack_name in EXPECTED_PACKS:
        pack_path = PACK_ROOT / pack_name / "content.json"
        pack = load_json(pack_path)
        unit_by_id = {unit["id"]: unit for unit in pack.get("units", [])}
        expected_units = {row["unitId"] for row in modules_by_pack[pack_name]}
        if not expected_units.issubset(unit_by_id):
            missing = sorted(expected_units.difference(unit_by_id))
            raise ValueError(f"Manifest units missing from {pack_name}: {missing}")

        indexed_variants: dict[str, tuple[dict[str, Any], dict[str, Any], int]] = {}
        for unit in pack.get("units", []):
            variants = unit.get("visualVariants", [])
            if not variants:
                raise ValueError(f"Unit has no visual variants: {unit.get('id')}")
            primary_count += 1
            if "modules" in variants[0]:
                raise ValueError(f"Primary Level 1 variant must not declare modules: {variants[0].get('id')}")
            for index, variant in enumerate(variants):
                variant_id = variant.get("id")
                if not variant_id or variant_id in indexed_variants:
                    raise ValueError(f"Missing or duplicate variant ID in {pack_name}: {variant_id}")
                indexed_variants[variant_id] = (unit, variant, index)

        rows_by_variant: dict[str, list[dict[str, Any]]] = {}
        for module in modules_by_pack[pack_name]:
            rows_by_variant.setdefault(module["variantId"], []).append(module)
            indexed = indexed_variants.get(module["variantId"])
            if indexed is None:
                raise ValueError(f"Level 2 variant missing from {pack_name}: {module['variantId']}")
            unit, variant, index = indexed
            if index == 0 or unit["id"] != module["unitId"]:
                raise ValueError(f"Level 2/owner mismatch for {module['variantId']}")
            if unit.get("assetBundleId") != module.get("assetBundleId"):
                raise ValueError(f"Module asset bundle differs from unit bundle for {module['variantId']}")
        expected_ids = set(rows_by_variant)
        for variant_id, module_rows in rows_by_variant.items():
            _, variant, _ = indexed_variants[variant_id]
            if len(module_rows) > MAX_MODULES_PER_VARIANT:
                raise ValueError(f"Variant exceeds the {MAX_MODULES_PER_VARIANT}-module limit: {variant_id}")
            declaration = [
                {"slotId": row["slotId"], "assetName": row["assetName"]}
                for row in sorted(module_rows, key=lambda row: SLOT_ORDER[row["slotId"]])
            ]
            existing = variant.get("modules") or []
            existing_slots = [row.get("slotId") for row in existing if isinstance(row, dict)]
            if len(existing_slots) != len(existing) or len(existing_slots) != len(set(existing_slots)):
                raise ValueError(f"Invalid or duplicate module slots on {variant_id}")
            if not set(existing_slots).issubset({row["slotId"] for row in declaration}):
                raise ValueError(f"Refusing to replace an unrelated module slot on {variant_id}")
            if existing == declaration:
                continue
            if check_only:
                raise ValueError(f"Canonical module bindings are stale for {variant_id}")
            variant["modules"] = declaration
            changed += 1

        actual_ids = set()
        for variant_id, (_, variant, _) in indexed_variants.items():
            declarations = variant.get("modules") or []
            if declarations:
                actual_ids.add(variant_id)
            elif "modules" in variant and variant_id not in expected_ids:
                raise ValueError(f"Unexpected empty Level 2 module declaration on {variant_id}")
        if actual_ids != expected_ids:
            extra = sorted(actual_ids - expected_ids)
            missing = sorted(expected_ids - actual_ids)
            raise ValueError(f"Module binding set differs in {pack_name}; extra={extra}, missing={missing}")

        if not check_only and changed:
            write_json(pack_path, pack)

    if primary_count != 18:
        raise ValueError(f"Expected 18 primary variants, found {primary_count}")
    active_variant_count = sum(
        len({row["variantId"] for row in rows}) for rows in modules_by_pack.values()
    )
    return changed, primary_count, len(modules), active_variant_count


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Verify bindings without writing pack manifests")
    args = parser.parse_args()
    changed, primaries, module_count, variant_count = bind(args.check)
    print(
        f"Canonical Level 2 bindings: {module_count} modules across {variant_count} visual variants "
        f"for {primaries} units; changed={changed}."
    )


if __name__ == "__main__":
    main()
