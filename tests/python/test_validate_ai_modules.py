"""Tests de la auditoría de módulos AI contra el registro C#."""
from __future__ import annotations
import unittest
from tests.python.helpers import load_tool_module

VALIDATE = load_tool_module("validate_ai_modules")
SCAFFOLD = load_tool_module("scaffold_pack")

def _valid_pack() -> dict:
    return SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")


class ValidateAiModulesTests(unittest.TestCase):
    def test_valid_pack_passes(self) -> None:
        pack = _valid_pack()
        issues = VALIDATE.validate_ai_profiles(pack)
        self.assertEqual([], issues)

    def test_unknown_module_rejected(self) -> None:
        pack = _valid_pack()
        pack["aiProfiles"][0]["modules"].append(
            {"moduleId": "sample:no-such-module", "parameters": []}
        )
        issues = VALIDATE.validate_ai_profiles(pack)
        self.assertTrue(any("no-such-module" in i for i in issues))
        self.assertTrue(any("desconocido" in i for i in issues))

    def test_too_many_modules_rejected(self) -> None:
        pack = _valid_pack()
        base = pack["aiProfiles"][0]["modules"][0]
        for i in range(9):
            pack["aiProfiles"][0]["modules"].append(
                {"moduleId": base["moduleId"], "parameters": []}
            )
        issues = VALIDATE.validate_ai_profiles(pack)
        self.assertTrue(any("módulos" in i.lower() or "modules" in i.lower() for i in issues))

    def test_unknown_parameter_rejected(self) -> None:
        pack = _valid_pack()
        # reactive-base-defense accepts unitId/baseIndex/... but not bogusParam
        for m in pack["aiProfiles"][0]["modules"]:
            if m["moduleId"] == "sample:reactive-base-defense":
                m.setdefault("parameters", []).append({"name": "bogusParam", "value": "1"})
        # scaffold uses scheduled-base-build; add reactive module instead
        if not any("bogusParam" in i for i in VALIDATE.validate_ai_profiles(pack)):
            pack["aiProfiles"][0]["modules"].append(
                {"moduleId": "sample:reactive-base-defense",
                 "parameters": [{"name": "bogusParam", "value": "1"}]}
            )
        issues = VALIDATE.validate_ai_profiles(pack)
        self.assertTrue(any("bogusParam" in i for i in issues))

    def test_out_of_bounds_parameter_rejected(self) -> None:
        pack = _valid_pack()
        pack["aiProfiles"][0]["modules"].append(
            {"moduleId": "sample:reactive-base-defense",
             "parameters": [{"name": "baseIndex", "value": "99"}]}
        )
        issues = VALIDATE.validate_ai_profiles(pack)
        self.assertTrue(any("baseIndex" in i for i in issues))

    def test_native_profile_without_scope_rejected(self) -> None:
        pack = _valid_pack()
        pack["aiProfiles"][0]["scopeToReferencedStages"] = False
        issues = VALIDATE.validate_ai_profiles(pack)
        self.assertTrue(any("scopeToReferencedStages" in i for i in issues))

    def test_dangling_unit_entry_detected_with_known_ids(self) -> None:
        pack = _valid_pack()
        known = {pack["units"][0]["id"]}
        pack["aiProfiles"][0]["unitEntries"].append(
            {"unitId": "ns:ghost", "baseIndex": 0,
             "startingInAscensionLevel": 0, "countAtStart": 1, "maxLiving": 1}
        )
        issues = VALIDATE.validate_ai_profiles(pack, known_unit_ids=known)
        self.assertTrue(any("ghost" in i for i in issues))

    def test_empty_pack_passes(self) -> None:
        self.assertEqual([], VALIDATE.validate_ai_profiles({"aiProfiles": []}))
        self.assertEqual([], VALIDATE.validate_ai_profiles({}))


if __name__ == "__main__":
    unittest.main()
