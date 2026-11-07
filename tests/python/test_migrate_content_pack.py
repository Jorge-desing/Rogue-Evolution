"""Tests del migrador de content packs."""

from __future__ import annotations

import unittest

from tests.python.helpers import load_tool_module

MIGRATE = load_tool_module("migrate_content_pack")
SCAFFOLD = load_tool_module("scaffold_pack")


class MigrateContentPackTests(unittest.TestCase):
    def test_current_pack_needs_no_changes(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        migrated, changes = MIGRATE.migrate_pack(pack)
        self.assertEqual([], changes)
        self.assertEqual(pack, migrated)

    def test_adds_missing_schema_version(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        del pack["schemaVersion"]
        migrated, changes = MIGRATE.migrate_pack(pack)
        self.assertIn(1, [migrated["schemaVersion"]])
        self.assertTrue(any("schemaVersion" in c for c in changes))

    def test_alias_unit_id_to_id(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        del pack["units"][0]["id"]
        pack["units"][0]["unitId"] = "ns:alpha"
        migrated, changes = MIGRATE.migrate_pack(pack)
        self.assertEqual("ns:alpha", migrated["units"][0]["id"])
        self.assertTrue(any("unitId" in c for c in changes))

    def test_object_localizations_to_list(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["units"][0]["localizations"] = {
            "en-US": {"name": "N", "description": "D"},
            "es-XL": {"name": "N2", "description": "D2"},
        }
        migrated, changes = MIGRATE.migrate_pack(pack)
        locs = migrated["units"][0]["localizations"]
        self.assertEqual(2, len(locs))
        self.assertIn("en-US", [l["language"] for l in locs])
        self.assertTrue(any("localizations" in c for c in changes))


if __name__ == "__main__":
    unittest.main()
