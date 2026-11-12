"""Tests del análisis de mecánicas de unidad."""

from __future__ import annotations

import json
import unittest

from tests.python.helpers import SAMPLE_NATIVE_PATH, load_tool_module

MECHANICS = load_tool_module("analyze_unit_mechanics")
SCAFFOLD = load_tool_module("scaffold_pack")


class AnalyzeUnitMechanicsTests(unittest.TestCase):
    def test_counts_before_details(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        report = MECHANICS.analyze_mechanics(pack)
        self.assertEqual(1, report["counts"]["units"])
        unit = report["units"][0]
        self.assertEqual("ns:alpha", unit["unitId"])
        self.assertEqual([], unit["mechanics"])
        pack["units"][0]["variants"] = [{"id": "ap"}]
        report = MECHANICS.analyze_mechanics(pack)
        self.assertIn("variants", report["units"][0]["mechanics"])

    def test_mechanics_usage_and_coverage(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["units"][0]["aura"] = {"auraRadius": 5}
        report = MECHANICS.analyze_mechanics(pack)
        unit = report["units"][0]
        self.assertIn("aura", unit["mechanics"])
        self.assertNotIn("aura", report["unusedMechanics"])
        self.assertIn("shieldRegeneration", report["unusedMechanics"])

    def test_empty_subobject_not_counted(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["units"][0]["aura"] = {}
        report = MECHANICS.analyze_mechanics(pack)
        self.assertNotIn("aura", report["units"][0]["mechanics"])

    def test_usage_histogram(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["units"][0]["aura"] = {"auraRadius": 5}
        report = MECHANICS.analyze_mechanics(pack)
        self.assertEqual(1, report["usage"]["aura"])

    def test_empty_pack(self) -> None:
        report = MECHANICS.analyze_mechanics({})
        self.assertEqual(0, report["counts"]["units"])
        self.assertEqual([], report["units"])

    def test_real_pack(self) -> None:
        pack = json.loads(SAMPLE_NATIVE_PATH.read_text(encoding="utf-8"))
        report = MECHANICS.analyze_mechanics(pack)
        self.assertEqual(9, report["counts"]["units"])
        variants = report["usage"].get("variants", 0)
        self.assertGreater(variants, 0)


if __name__ == "__main__":
    unittest.main()
