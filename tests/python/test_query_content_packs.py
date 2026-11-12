"""Tests de consulta y filtrado del pack."""

from __future__ import annotations

import json
import unittest

from tests.python.helpers import SAMPLE_NATIVE_PATH, load_tool_module

QUERY = load_tool_module("query_content_packs")
SCAFFOLD = load_tool_module("scaffold_pack")


class QueryContentPacksTests(unittest.TestCase):
    def test_query_all_returns_compact_rows(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        rows = QUERY.query_units(pack)
        self.assertEqual(1, len(rows))
        self.assertEqual("ns:alpha", rows[0]["unitId"])
        self.assertIn("cost", rows[0])

    def test_query_by_text(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha", unit_display_name="Zorblax")
        self.assertEqual(1, len(QUERY.query_units(pack, text="zorblax")))
        self.assertEqual(1, len(QUERY.query_units(pack, text="alpha")))
        self.assertEqual([], QUERY.query_units(pack, text="missing"))

    def test_query_by_rarity_and_cost(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["units"][0]["balanceOverrides"] = [
            {"name": "rarity", "value": "Rare"},
            {"name": "cost", "value": "100"},
        ]
        self.assertEqual(1, len(QUERY.query_units(pack, rarity="Rare")))
        self.assertEqual([], QUERY.query_units(pack, rarity="UltraRare"))
        self.assertEqual(1, len(QUERY.query_units(pack, max_cost=150)))
        self.assertEqual([], QUERY.query_units(pack, max_cost=50))
        self.assertEqual(1, len(QUERY.query_units(pack, min_cost=100)))

    def test_query_by_role_and_tag(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["units"][0]["balanceOverrides"] = [
            {"name": "roles", "value": "Unit, Vehicle"},
            {"name": "offeredSystemTags", "value": "Turbo, Scout"},
        ]
        self.assertEqual(1, len(QUERY.query_units(pack, role="Vehicle")))
        self.assertEqual([], QUERY.query_units(pack, role="Mech"))
        self.assertEqual(1, len(QUERY.query_units(pack, tag="Turbo")))

    def test_unit_stages_includes_pools(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        uid = pack["units"][0]["id"]
        self.assertEqual([pack["stages"][0]["id"]], QUERY.unit_stages(pack, uid))
        self.assertEqual([], QUERY.unit_stages(pack, "ns:unknown"))

    def test_unit_ai_profiles(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        uid = pack["units"][0]["id"]
        profiles = QUERY.unit_ai_profiles(pack, uid)
        self.assertEqual(1, len(profiles))
        self.assertEqual(pack["aiProfiles"][0]["id"], profiles[0]["profileId"])
        self.assertEqual(["sample:scheduled-base-build"], profiles[0]["moduleIds"])

    def test_stage_profiles_and_orphans(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        stage_id = pack["stages"][0]["id"]
        self.assertEqual([pack["aiProfiles"][0]["id"]], QUERY.stage_profiles(pack, stage_id))
        self.assertEqual([], QUERY.find_units_without_stage(pack))
        pack["stages"][0]["unitIds"] = []
        self.assertEqual([pack["units"][0]["id"]], QUERY.find_units_without_stage(pack))

    def test_empty_pack(self) -> None:
        self.assertEqual([], QUERY.query_units({}))
        self.assertEqual([], QUERY.unit_stages({}, "ns:x"))
        self.assertEqual([], QUERY.unit_ai_profiles({}, "ns:x"))

    def test_real_pack_query(self) -> None:
        pack = json.loads(SAMPLE_NATIVE_PATH.read_text(encoding="utf-8"))
        rows = QUERY.query_units(pack)
        self.assertEqual(9, len(rows))
        scout = QUERY.query_units(pack, text="scout")
        self.assertTrue(scout)
        stages = QUERY.unit_stages(pack, "sample-native:scout-tank")
        self.assertTrue(stages)


if __name__ == "__main__":
    unittest.main()
