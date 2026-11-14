"""Tests del análisis de dependencias entre packs."""

from __future__ import annotations

import json
import unittest

from tests.python.helpers import SAMPLE_NATIVE_PATH, load_tool_module

DEPS = load_tool_module("check_pack_dependencies")
SCAFFOLD = load_tool_module("scaffold_pack")


def _packs() -> dict[str, dict]:
    scaffold = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
    return {"scaffold": scaffold}


class CheckPackDependenciesTests(unittest.TestCase):
    def test_self_contained_pack_has_no_external_deps(self) -> None:
        report = DEPS.analyze_dependencies(_packs())
        self.assertEqual(1, report["counts"]["packs"])
        pack_row = report["packs"][0]
        self.assertEqual([], pack_row["externalReferences"])
        self.assertEqual([], pack_row["unresolvedReferences"])

    def test_cross_pack_reference_resolved(self) -> None:
        packs = _packs()
        dependent = SCAFFOLD.generate_scaffold("other:pack", "beta")
        dependent["stages"][0]["unitIds"] = ["ns:alpha"]
        packs["dependent"] = dependent
        report = DEPS.analyze_dependencies(packs)
        row = next(p for p in report["packs"] if p["name"] == "dependent")
        self.assertEqual(["scaffold"], row["resolvedBy"])
        self.assertIn("ns:alpha", row["externalReferences"])
        self.assertEqual([], row["unresolvedReferences"])

    def test_unresolved_reference_reported(self) -> None:
        packs = _packs()
        packs["scaffold"]["stages"][0]["unitIds"] = ["missing:unit"]
        report = DEPS.analyze_dependencies(packs)
        row = report["packs"][0]
        self.assertEqual(["missing:unit"], row["unresolvedReferences"])

    def test_counts_first(self) -> None:
        report = DEPS.analyze_dependencies({})
        self.assertEqual(0, report["counts"]["packs"])
        self.assertEqual([], report["packs"])

    def test_native_ids_not_external(self) -> None:
        packs = _packs()
        packs["scaffold"]["stages"][0]["unitIds"] = ["PCXBaseTank"]
        report = DEPS.analyze_dependencies(packs)
        self.assertEqual([], report["packs"][0]["externalReferences"])

    def test_real_packs_resolve(self) -> None:
        sample_native = json.loads(SAMPLE_NATIVE_PATH.read_text(encoding="utf-8"))
        packs = {"sample-native-expansion": sample_native}
        report = DEPS.analyze_dependencies(packs)
        self.assertEqual(1, report["counts"]["packs"])


if __name__ == "__main__":
    unittest.main()
