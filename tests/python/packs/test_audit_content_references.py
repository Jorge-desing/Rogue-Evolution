"""Tests de la auditoría de referencias cruzadas de content packs."""

from __future__ import annotations

import json
import unittest

from tests.python.helpers import load_tool_module

AUDIT = load_tool_module("audit_content_references")
SCAFFOLD = load_tool_module("scaffold_pack")


class AuditContentReferencesTests(unittest.TestCase):
    def _valid_pack(self) -> dict:
        return SCAFFOLD.generate_scaffold(
            "ns:pack", "alpha", stage_slug="sa", ai_slug="aa"
        )

    def test_valid_pack_has_no_issues(self) -> None:
        pack = self._valid_pack()
        issues = AUDIT.audit_pack(pack)
        self.assertEqual([], issues)

    def test_detects_undefined_unit_reference_in_stage(self) -> None:
        pack = self._valid_pack()
        pack["stages"][0]["unitIds"].append("ns:ghost-unit")
        issues = AUDIT.audit_pack(pack)
        self.assertTrue(any("ghost-unit" in i for i in issues))
        self.assertTrue(any("no definida" in i for i in issues))

    def test_detects_undefined_ai_profile_reference(self) -> None:
        pack = self._valid_pack()
        pack["stages"][0]["aiProfileIds"].append("ns:ghost-profile")
        issues = AUDIT.audit_pack(pack)
        self.assertTrue(any("ghost-profile" in i for i in issues))

    def test_detects_shared_factory(self) -> None:
        pack = self._valid_pack()
        # Add a second unit reusing the same factory id
        other = SCAFFOLD.generate_scaffold(
            "ns:pack", "beta", stage_slug="sb", ai_slug="ab"
        )
        other_unit = other["units"][0]
        other_unit["productionBuilding"]["id"] = pack["units"][0]["productionBuilding"]["id"]
        pack["units"].append(other_unit)

        issues = AUDIT.audit_pack(pack)
        self.assertTrue(any("compartida" in i for i in issues))

    def test_detects_missing_production_building(self) -> None:
        pack = self._valid_pack()
        del pack["units"][0]["productionBuilding"]
        issues = AUDIT.audit_pack(pack)
        self.assertTrue(any("falta productionBuilding" in i for i in issues))

    def test_detects_missing_english_localization(self) -> None:
        pack = self._valid_pack()
        # Remove the en-US localization (keep es-XL)
        pack["units"][0]["localizations"] = [
            loc for loc in pack["units"][0]["localizations"] if loc["language"] != "en-US"
        ]
        issues = AUDIT.audit_pack(pack)
        self.assertTrue(any("en-US name" in i for i in issues))

    def test_known_ids_allow_cross_pack_reference(self) -> None:
        pack = self._valid_pack()
        pack["stages"][0]["unitIds"].append("other:ghost")
        issues = AUDIT.audit_pack(pack)
        self.assertTrue(any("other:ghost" in i for i in issues))
        # With global known IDs, it resolves
        issues2 = AUDIT.audit_pack(pack, known_unit_ids={"other:ghost"})
        self.assertFalse(any("other:ghost" in i for i in issues2))


if __name__ == "__main__":
    unittest.main()
