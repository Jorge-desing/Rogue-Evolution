"""Tests del validador de cambios de carta."""
from __future__ import annotations
import unittest
from tests.python.helpers import load_tool_module

VC = load_tool_module("validate_card_changes")
SCAFFOLD = load_tool_module("scaffold_pack")


def _valid_pack():
    return SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")


class ValidateCardChangesTests(unittest.TestCase):
    def test_empty_pack_ok(self):
        report = VC.validate_card_changes({})
        self.assertEqual(0, report["totalChanges"])
        self.assertEqual([], report["issues"])

    def test_valid_relic_change_ok(self):
        pack = _valid_pack()
        pack["relics"] = [{
            "id": "ns:relic-a", "assetBundleId": "ns:assets",
            "imageAssetName": "img.png", "rarity": "Common", "tech": "None",
            "offeredSystemTags": "None", "neededSystemTags": "None",
            "neededExperienceLevel": 0, "isAllowedForDemo": True,
            "isForSpecialists": False,
            "localizations": [{"language": "en-US", "name": "R", "description": "d"}],
            "cardChanges": [{"valueToChange": "MaxHealth", "operation": "Add",
                             "value": 5, "side": "Irrelevant"}],
        }]
        report = VC.validate_card_changes(pack)
        self.assertEqual(1, report["totalChanges"])
        self.assertEqual(1, report["validChanges"])
        self.assertEqual([], report["issues"])

    def test_unknown_changeable_value_flagged(self):
        pack = _valid_pack()
        pack["relics"] = [{
            "id": "ns:relic-b", "assetBundleId": "ns:assets",
            "imageAssetName": "img.png", "rarity": "Common", "tech": "None",
            "offeredSystemTags": "None", "neededSystemTags": "None",
            "neededExperienceLevel": 0, "isAllowedForDemo": True,
            "isForSpecialists": False,
            "localizations": [{"language": "en-US", "name": "R", "description": "d"}],
            "cardChanges": [{"valueToChange": "BogusValue", "operation": "Add",
                             "value": 5, "side": "Irrelevant"}],
        }]
        report = VC.validate_card_changes(pack)
        self.assertTrue(any("BogusValue" in i for i in report["issues"]))

    def test_invalid_operation_flagged(self):
        pack = _valid_pack()
        pack["relics"] = [{
            "id": "ns:relic-c", "assetBundleId": "ns:assets",
            "imageAssetName": "img.png", "rarity": "Common", "tech": "None",
            "offeredSystemTags": "None", "neededSystemTags": "None",
            "neededExperienceLevel": 0, "isAllowedForDemo": True,
            "isForSpecialists": False,
            "localizations": [{"language": "en-US", "name": "R", "description": "d"}],
            "cardChanges": [{"valueToChange": "MaxHealth", "operation": "Frob",
                             "value": 5, "side": "Irrelevant"}],
        }]
        report = VC.validate_card_changes(pack)
        self.assertTrue(any("operation inválida" in i for i in report["issues"]))

    def test_non_finite_value_flagged(self):
        pack = _valid_pack()
        pack["upgrades"] = [{
            "id": "ns:up-a", "targetUnitId": pack["units"][0]["id"],
            "requiredExperienceLevel": 1, "cost": 10,
            "localizations": [{"language": "en-US", "name": "U", "description": "d"}],
            "cardChanges": [{"valueToChange": "MoveSpeed", "operation": "Add",
                             "value": float("inf"), "side": "Irrelevant"}],
        }]
        report = VC.validate_card_changes(pack)
        self.assertTrue(any("no finito" in i for i in report["issues"]))

    def test_invalid_side_flagged(self):
        pack = _valid_pack()
        pack["units"] = [pack["units"][0]]
        pack["units"][0]["cardChanges"] = [{"valueToChange": "MaxHealth",
                                            "operation": "Add", "value": 1,
                                            "side": "Sideways"}]
        report = VC.validate_card_changes(pack)
        self.assertTrue(any("side inválido" in i for i in report["issues"]))


if __name__ == "__main__":
    unittest.main()
