"""Tests de descubrimiento: catálogo de operaciones del editor."""

from __future__ import annotations

import io
import json
import unittest
from contextlib import redirect_stdout

from tests.python.helpers import load_tool_module

EDIT = load_tool_module("edit_content_pack")


class OperationsCatalogTests(unittest.TestCase):
    def test_catalog_covers_cli_choices(self) -> None:
        catalog = EDIT.operations_catalog()
        ops = {entry["op"] for entry in catalog}
        # CLI choices (sin batch/list-operations, que son utilidades)
        for cli_op in (
            "add", "remove", "set-flag", "set-balance", "remove-balance",
            "set-map-size", "add-pool", "remove-pool", "set-ai-param",
            "rename-unit", "set-localization",
            "add-stage", "remove-stage", "add-ai-profile", "remove-ai-profile",
            "add-ai-module", "remove-ai-module", "set-stage-param", "set-ai-field",
            "set-template", "add-relic", "remove-relic", "set-relic-card-change",
            "add-bundle", "remove-bundle", "duplicate-unit",
            "add-upgrade", "remove-upgrade", "set-upgrade-field",
            "set-upgrade-card-change", "set-landscape", "set-unit-assets",
            "set-pack-id", "set-schema-version",
        ):
            self.assertIn(cli_op, ops, f"op '{cli_op}' falta en el catálogo")

    def test_catalog_entries_have_args_and_summary(self) -> None:
        for entry in EDIT.operations_catalog():
            self.assertIn("op", entry)
            self.assertIn("summary", entry)
            self.assertIn("args", entry)
            self.assertIn("op", entry)
            self.assertTrue(entry["summary"].strip())

    def test_operations_json_cli(self) -> None:
        buf = io.StringIO()
        with redirect_stdout(buf):
            code = EDIT.main(["--operations-json"])
        self.assertEqual(0, code)
        data = json.loads(buf.getvalue())
        self.assertGreater(len(data), 30)
        ops = {entry["op"] for entry in data}
        self.assertIn("set-flag", ops)
        self.assertIn("purge-dangling", ops)


if __name__ == "__main__":
    unittest.main()
