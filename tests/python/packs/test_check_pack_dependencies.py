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
        self.assertIn("PCXBaseTank (unit)", report["packs"][0]["unverifiedNativeReferences"])

    def test_unit_upgrade_and_upgrade_exclusion_dependencies_are_reported(self) -> None:
        pack = _packs()["scaffold"]
        pack["units"][0]["upgrades"] = [{"targetUnitId": "external:next"}]
        pack["upgrades"] = [{"id": "ns:upgrade", "mustNotHaveOneOfTheseEntityIds": ["external:blocked"]}]
        refs = DEPS.external_references(pack)
        self.assertIn("external:next", refs)
        self.assertIn("external:blocked", refs)

    def test_native_copy_and_upgrade_exclusions_are_unverified(self) -> None:
        pack = _packs()["scaffold"]
        pack["units"][0]["copyUpgradesToEntityId"] = "NativeTarget"
        pack["upgrades"] = [{"id": "ns:upgrade", "mustNotHaveOneOfTheseEntityIds": ["NativeBlocked"]}]
        row = DEPS.analyze_dependencies({"pack": pack})["packs"][0]
        self.assertIn("NativeTarget (entity)", row["unverifiedNativeReferences"])
        self.assertIn("NativeBlocked (entity)", row["unverifiedNativeReferences"])

    def test_unit_reference_cannot_resolve_to_a_profile_or_bundle(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["stages"][0]["unitIds"] = ["other:thing"]
        provider = SCAFFOLD.generate_scaffold("other:pack", "beta")
        provider["aiProfiles"][0]["id"] = "other:thing"
        report = DEPS.analyze_dependencies({"consumer": pack, "provider": provider})
        row = next(row for row in report["packs"] if row["name"] == "consumer")
        self.assertIn("other:thing (unit)", row["unresolvedReferences"])

    def test_scheduled_factory_and_card_target_require_correct_kinds(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["aiProfiles"][0]["modules"] = [{"moduleId": "sample:scheduled-base-build", "parameters": [
            {"name": "entityId", "value": "ns:alpha"}]}]
        pack["relics"] = [{"id": "ns:r", "cardChanges": [{"onlyForTheseEntityIds": ["ns:alpha-factory"]}]}]
        row = DEPS.analyze_dependencies({"pack": pack})["packs"][0]
        self.assertIn("ns:alpha (factory)", row["unresolvedReferences"])
        self.assertIn("ns:alpha-factory (unit)", row["unresolvedReferences"])

    def test_synergy_profile_dependencies_module_and_ambiguous_providers(self) -> None:
        packs = _packs()
        dependent = SCAFFOLD.generate_scaffold("other:pack", "beta")
        dependent["units"][0]["synergies"] = [{"targetUnitIds": ["ns:alpha"]}]
        dependent["aiProfiles"][0]["unitDependencies"] = ["ns:alpha"]
        dependent["aiProfiles"][0]["modules"] = [{"parameters": [{"name": "entityId", "value": "ns:alpha-factory"}]}]
        packs["dependent"] = dependent
        packs["duplicate"] = SCAFFOLD.generate_scaffold("ns:other", "alpha")
        row = next(item for item in DEPS.analyze_dependencies(packs)["packs"] if item["name"] == "dependent")
        self.assertIn("ns:alpha", row["externalReferences"])
        self.assertIn("ns:alpha-factory", row["externalReferences"])
        self.assertIn("ns:alpha", row["ambiguousReferences"])

    def test_real_packs_resolve(self) -> None:
        sample_native = json.loads(SAMPLE_NATIVE_PATH.read_text(encoding="utf-8"))
        packs = {"sample-native-expansion": sample_native}
        report = DEPS.analyze_dependencies(packs)
        self.assertEqual(1, report["counts"]["packs"])


if __name__ == "__main__":
    unittest.main()
