"""AssetBundle reference collection for product and factory assets."""

from __future__ import annotations

import argparse
import importlib.util
import io
import json
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from typing import Any
from unittest.mock import patch


SCRIPT = Path(__file__).resolve().parents[1] / "tools" / "validate_content_bundle_references.py"
spec = importlib.util.spec_from_file_location("validate_content_bundle_references", SCRIPT)
if spec is None or spec.loader is None:
    raise RuntimeError(f"Could not load {SCRIPT}")
validator = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = validator
spec.loader.exec_module(validator)


class ContentBundleReferenceTests(unittest.TestCase):
    def test_collects_factory_card_and_visual_using_factory_bundle_id(self) -> None:
        pack: dict[str, Any] = {
            "units": [
                {
                    "id": "sample:unit",
                    "assetBundleId": "sample:product-assets",
                    "cardImageAssetName": "assets/product.png",
                    "visualAssetName": "assets/product.prefab",
                    "productionBuilding": {
                        "id": "sample:factory",
                        "assetBundleId": "sample:factory-assets",
                        "cardImageAssetName": "assets/factory-card.png",
                        "visualAssetName": "assets/factory-visual.prefab",
                    },
                }
            ]
        }

        references = validator.collect_references(pack)

        self.assertIn(
            (
                "productionBuilding:sample:factory.cardImageAssetName",
                "sample:factory-assets",
                "assets/factory-card.png",
                "Sprite card image",
            ),
            references,
        )
        self.assertIn(
            (
                "productionBuilding:sample:factory.visualAssetName",
                "sample:factory-assets",
                "assets/factory-visual.prefab",
                "GameObject prefab",
            ),
            references,
        )

    def test_product_references_remain_bound_to_product_bundle(self) -> None:
        pack: dict[str, Any] = {
            "units": [
                {
                    "id": "sample:unit",
                    "assetBundleId": "sample:product-assets",
                    "cardImageAssetName": "assets/product.png",
                    "productionBuilding": {
                        "id": "sample:factory",
                        "assetBundleId": "sample:factory-assets",
                        "cardImageAssetName": "assets/factory-card.png",
                    },
                }
            ]
        }

        references = validator.collect_references(pack)

        self.assertIn(
            (
                "unit:sample:unit.cardImageAssetName",
                "sample:product-assets",
                "assets/product.png",
                "Sprite card image",
            ),
            references,
        )

    def test_factory_without_asset_references_produces_no_references(self) -> None:
        pack: dict[str, Any] = {
            "units": [
                {
                    "id": "sample:unit",
                    "productionBuilding": {"id": "sample:factory"},
                }
            ]
        }

        self.assertEqual(validator.collect_references(pack), [])

    def test_factory_references_without_bundle_id_report_undeclared_bundle(self) -> None:
        pack: dict[str, Any] = {
            "packId": "sample:pack",
            "assetBundles": [],
            "units": [
                {
                    "id": "sample:unit",
                    "productionBuilding": {
                        "id": "sample:factory",
                        "cardImageAssetName": "assets/factory-card.png",
                    },
                }
            ],
        }

        with tempfile.TemporaryDirectory() as temporary_directory:
            pack_root = Path(temporary_directory)
            manifest = pack_root / "content.json"
            manifest.write_text(json.dumps(pack), encoding="utf-8")
            output = io.StringIO()

            with patch.object(
                validator,
                "parse_args",
                return_value=argparse.Namespace(pack=pack_root),
            ):
                with redirect_stdout(output):
                    exit_code = validator.main()

        self.assertEqual(exit_code, 1)
        expected_diagnostic = (
            "ERROR productionBuilding:sample:factory.cardImageAssetName: "
            "AssetBundle ID '' is not declared"
        )
        self.assertIn(expected_diagnostic, output.getvalue())
        self.assertIn("Checked 1 AssetBundle reference(s); 1 error(s).", output.getvalue())

    def test_factory_asset_references_without_bundle_id_use_empty_bundle(self) -> None:
        pack: dict[str, Any] = {
            "units": [
                {
                    "id": "sample:unit",
                    "productionBuilding": {
                        "id": "sample:factory",
                        "cardImageAssetName": "assets/factory-card.png",
                        "visualAssetName": "assets/factory-visual.prefab",
                    },
                }
            ]
        }

        references = validator.collect_references(pack)

        self.assertEqual(
            references,
            [
                (
                    "productionBuilding:sample:factory.cardImageAssetName",
                    "",
                    "assets/factory-card.png",
                    "Sprite card image",
                ),
                (
                    "productionBuilding:sample:factory.visualAssetName",
                    "",
                    "assets/factory-visual.prefab",
                    "GameObject prefab",
                ),
            ],
        )



    def test_factory_and_product_assets_resolve_from_their_own_declared_bundles(self) -> None:
        pack: dict[str, Any] = {
            "units": [
                {
                    "id": "sample:unit",
                    "assetBundleId": "sample:product-assets",
                    "cardImageAssetName": "assets/product.png",
                    "visualAssetName": "assets/product.prefab",
                    "productionBuilding": {
                        "id": "sample:factory",
                        "assetBundleId": "sample:factory-assets",
                        "cardImageAssetName": "assets/factory-card.png",
                        "visualAssetName": "assets/factory-visual.prefab",
                    },
                }
            ]
        }
        bundle_entries = {
            "sample:product-assets": [
                ("assets/product.png", "Sprite"),
                ("assets/product.prefab", "GameObject"),
            ],
            "sample:factory-assets": [
                ("assets/factory-card.png", "Sprite"),
                ("assets/factory-visual.prefab", "GameObject"),
            ],
        }

        diagnostics, failures = validator.validate_references(
            validator.collect_references(pack), bundle_entries
        )

        self.assertEqual(failures, 0)
        self.assertEqual(
            diagnostics,
            [
                "OK unit:sample:unit.cardImageAssetName: assets/product.png (Sprite)",
                "OK unit:sample:unit.visualAssetName: assets/product.prefab (GameObject)",
                "OK productionBuilding:sample:factory.cardImageAssetName: assets/factory-card.png (Sprite)",
                "OK productionBuilding:sample:factory.visualAssetName: assets/factory-visual.prefab (GameObject)",
            ],
        )

    def test_factory_reference_rejects_wrong_serialized_type(self) -> None:
        pack: dict[str, Any] = {
            "units": [
                {
                    "id": "sample:unit",
                    "productionBuilding": {
                        "id": "sample:factory",
                        "assetBundleId": "sample:factory-assets",
                        "cardImageAssetName": "assets/factory-card.png",
                    },
                }
            ]
        }
        bundle_entries = {
            "sample:factory-assets": [("assets/factory-card.png", "Texture2D")]
        }

        diagnostics, failures = validator.validate_references(
            validator.collect_references(pack), bundle_entries
        )

        self.assertEqual(failures, 1)
        self.assertEqual(
            diagnostics,
            [
                "ERROR productionBuilding:sample:factory.cardImageAssetName: "
                "assets/factory-card.png is Texture2D, expected Sprite card image (Sprite)"
            ],
        )

    def test_factory_reference_rejects_ambiguous_serialized_matches(self) -> None:
        pack: dict[str, Any] = {
            "units": [
                {
                    "id": "sample:unit",
                    "productionBuilding": {
                        "id": "sample:factory",
                        "assetBundleId": "sample:factory-assets",
                        "cardImageAssetName": "factory-card.png",
                    },
                }
            ]
        }
        bundle_entries = {
            "sample:factory-assets": [
                ("assets/factory-card.png", "Sprite"),
                ("other/factory-card.png", "Sprite"),
            ]
        }

        diagnostics, failures = validator.validate_references(
            validator.collect_references(pack), bundle_entries
        )

        self.assertEqual(failures, 1)
        self.assertEqual(
            diagnostics,
            [
                "ERROR productionBuilding:sample:factory.cardImageAssetName: "
                "'factory-card.png' is ambiguous: assets/factory-card.png (Sprite), "
                "other/factory-card.png (Sprite)"
            ],
        )

if __name__ == "__main__":
    unittest.main()
