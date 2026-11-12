"""Tests del generador de esqueletos de content pack."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from tests.python.helpers import load_tool_module
from tools.validate_content_pack import validate_pack

SCAFFOLD = load_tool_module("scaffold_pack")
REPO_ROOT = Path(__file__).resolve().parent.parent.parent
SCHEMA_PATH = REPO_ROOT / "content" / "schema" / "content.schema.json"


class ScaffoldPackTests(unittest.TestCase):
    def test_generate_scaffold_namespaces_every_id(self) -> None:
        scaffold = SCAFFOLD.generate_scaffold("mypack:expansion", "hover-scout")

        self.assertEqual("mypack:expansion", scaffold["packId"])
        self.assertEqual(1, scaffold["schemaVersion"])
        self.assertEqual([], scaffold["assetBundles"])

        unit = scaffold["units"][0]
        self.assertEqual("mypack:hover-scout", unit["id"])
        self.assertEqual("mypack:hover-scout-factory", unit["productionBuilding"]["id"])

        stage = scaffold["stages"][0]
        self.assertEqual("mypack:training-stage", stage["id"])
        self.assertEqual(["mypack:hover-scout"], stage["unitIds"])
        self.assertEqual(["mypack:base-defense"], stage["aiProfileIds"])

        ai = scaffold["aiProfiles"][0]
        self.assertEqual("mypack:base-defense", ai["id"])
        self.assertTrue(ai["scopeToReferencedStages"])
        self.assertEqual(unit["productionBuilding"]["id"], ai["modules"][0]["parameters"][0]["value"])

    def test_generate_scaffold_is_deterministic(self) -> None:
        first = SCAFFOLD.generate_scaffold("mypack:expansion", "hover-scout")
        second = SCAFFOLD.generate_scaffold("mypack:expansion", "hover-scout")
        self.assertEqual(first, second)

    def test_generate_scaffold_rejects_invalid_ids(self) -> None:
        with self.assertRaises(ValueError):
            SCAFFOLD.generate_scaffold("pack sin namespace", "hover-scout")

        with self.assertRaises(ValueError):
            SCAFFOLD.generate_scaffold("mypack:expansion", "slug inválido")

    def test_generated_scaffold_passes_schema_and_pack_validation(self) -> None:
        scaffold = SCAFFOLD.generate_scaffold("mypack:expansion", "hover-scout")
        with tempfile.TemporaryDirectory() as tmp:
            SCAFFOLD.write_scaffold(scaffold, tmp)
            content = Path(tmp) / "content.json"
            self.assertEqual(scaffold, json.loads(content.read_text(encoding="utf-8")))

            errors = validate_pack(tmp, schema_path=str(SCHEMA_PATH))
            self.assertEqual([], errors)


if __name__ == "__main__":
    unittest.main()
