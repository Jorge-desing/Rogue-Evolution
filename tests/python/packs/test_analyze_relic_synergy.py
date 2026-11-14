"""Tests del analizador de sinergia de reliquias."""
from __future__ import annotations
import unittest
from tests.python.helpers import load_tool_module

SYNERGY = load_tool_module("analyze_relic_synergy")
SCAFFOLD = load_tool_module("scaffold_pack")

def _valid_pack() -> dict:
    return SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")


class AnalyzeRelicSynergyTests(unittest.TestCase):
    def test_no_relics_ok(self) -> None:
        pack = _valid_pack()
        report = SYNERGY.analyze_relic_synergy(pack)
        self.assertEqual(0, report["relicCount"])
        self.assertEqual([], report["unmatchedRelics"])
        self.assertEqual([], report["issues"])

    def test_relic_with_no_matching_unit(self) -> None:
        pack = _valid_pack()
        pack["relics"] = [{
            "id": "ns:relic-a", "assetBundleId": "ns:assets",
            "imageAssetName": "img.png", "rarity": "Common", "tech": "None",
            "offeredSystemTags": "Zombo", "neededSystemTags": "Stealth",
            "neededExperienceLevel": 0, "isAllowedForDemo": True,
            "isForSpecialists": False,
            "localizations": [{"language": "en-US", "name": "R", "description": "d"}],
            "cardChanges": [{"valueToChange": "MaxHealth", "operation": "Add",
                             "value": 5, "side": "Irrelevant"}],
        }]
        report = SYNERGY.analyze_relic_synergy(pack)
        self.assertEqual(1, report["relicCount"])
        self.assertIn("ns:relic-a", report["unmatchedRelics"])

    def test_relic_matching_unit_system_tag(self) -> None:
        pack = _valid_pack()
        unit = pack["units"][0]
        unit["systemTags"] = ["Vehicle", "Turbo"]
        pack["relics"] = [{
            "id": "ns:relic-b", "assetBundleId": "ns:assets",
            "imageAssetName": "img.png", "rarity": "Common", "tech": "None",
            "offeredSystemTags": "Vehicle", "neededSystemTags": "Vehicle",
            "neededExperienceLevel": 0, "isAllowedForDemo": True,
            "isForSpecialists": False,
            "localizations": [{"language": "en-US", "name": "R", "description": "d"}],
            "cardChanges": [{"valueToChange": "MaxHealth", "operation": "Add",
                             "value": 5, "side": "Irrelevant"}],
        }]
        report = SYNERGY.analyze_relic_synergy(pack)
        self.assertEqual([], report["unmatchedRelics"])
        self.assertEqual(1, report["matchedRelicCount"])

    def test_unknown_changeable_value_flagged(self) -> None:
        pack = _valid_pack()
        pack["relics"] = [{
            "id": "ns:relic-c", "assetBundleId": "ns:assets",
            "imageAssetName": "img.png", "rarity": "Common", "tech": "None",
            "offeredSystemTags": "None", "neededSystemTags": "None",
            "neededExperienceLevel": 0, "isAllowedForDemo": True,
            "isForSpecialists": False,
            "localizations": [{"language": "en-US", "name": "R", "description": "d"}],
            "cardChanges": [{"valueToChange": "BogusValue", "operation": "Add",
                             "value": 5, "side": "Irrelevant"}],
        }]
        report = SYNERGY.analyze_relic_synergy(pack)
        self.assertTrue(any("BogusValue" in i for i in report["issues"]))

    def test_empty_pack(self) -> None:
        report = SYNERGY.analyze_relic_synergy({})
        self.assertEqual(0, report["relicCount"])
        self.assertEqual(0, report["unitCount"])


if __name__ == "__main__":
    unittest.main()
