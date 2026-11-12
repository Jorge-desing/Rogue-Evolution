"""Tests del inspector de content packs."""

from __future__ import annotations

import unittest

from tests.python.helpers import load_tool_module

INSPECT = load_tool_module("inspect_content_pack")
SCAFFOLD = load_tool_module("scaffold_pack")


class InspectContentPackTests(unittest.TestCase):
    def test_inspects_counts_and_factory_map(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")
        report = INSPECT.inspect_pack(pack)

        self.assertEqual("ns:pack", report["packId"])
        self.assertEqual(1, report["counts"]["units"])
        self.assertEqual(1, report["counts"]["factories"])
        self.assertEqual([{"unitId": "ns:alpha", "factoryId": "ns:alpha-factory"}],
                         report["unitFactories"])
        self.assertEqual(["ns:sa"], [s["stageId"] for s in report["stages"]])
        self.assertEqual(["ns:aa"], [p["profileId"] for p in report["aiProfiles"]])
        self.assertIn("sample:scheduled-base-build", report["aiProfiles"][0]["modules"])

    def test_inspection_lists_pools(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["stages"][0]["blueprintPoolEntries"] = [
            {"poolId": "P1", "unitIds": ["ns:alpha"]}
        ]
        report = INSPECT.inspect_pack(pack)
        pools = report["stages"][0]["pools"]
        self.assertEqual(1, len(pools))
        self.assertEqual("P1", pools[0]["poolId"])

    def test_inspection_tracks_localization_coverage(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        report = INSPECT.inspect_pack(pack)
        kinds = {(r["kind"], r["id"]) for r in report["localizations"]}
        self.assertIn(("unit", "ns:alpha"), kinds)
        self.assertIn(("factory", "ns:alpha-factory"), kinds)
        self.assertTrue(all(r["hasEnglish"] for r in report["localizations"]))


if __name__ == "__main__":
    unittest.main()
