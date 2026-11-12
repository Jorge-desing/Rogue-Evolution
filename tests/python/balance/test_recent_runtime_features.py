"""Contracts for adopting the latest tactical AI features in mod rosters."""

from __future__ import annotations

import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
PACK_PATH = ROOT / "content" / "packs" / "sample-native-expansion" / "content.json"


class RecentRuntimeFeatureManifestTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        manifest = json.loads(PACK_PATH.read_text(encoding="utf-8"))
        cls.profiles = {profile["id"]: profile for profile in manifest["aiProfiles"]}

    @staticmethod
    def _module(profile: dict, module_id: str) -> dict:
        for module in profile.get("modules", []):
            if module.get("moduleId") == module_id:
                return module
        raise AssertionError(f"{profile['id']} is missing {module_id}")

    @staticmethod
    def _parameters(module: dict) -> dict[str, str]:
        return {item["name"]: item["value"] for item in module.get("parameters", [])}

    def test_horde_profile_uses_formation_and_wave_escalation(self) -> None:
        profile = self.profiles["sample-native:horde-swarm-assault"]
        formation = self._module(profile, "sample:squad-formation")
        wave = self._module(profile, "sample:wave-escalation")

        self.assertEqual(
            self._parameters(formation),
            {
                "baseIndex": "0",
                "anchorRole": "Tank",
                "maxFollowersPerCaptain": "3",
                "formationRadiusCells": "18",
                "cooldownSeconds": "6",
            },
        )
        self.assertEqual(
            self._parameters(wave),
            {"baseIndex": "0", "waveIntervalSeconds": "75", "maxWaves": "6"},
        )
        self.assertLessEqual(len(profile["modules"]), 8)

    def test_raider_profile_uses_mech_formation_for_its_roster(self) -> None:
        profile = self.profiles["sample-native:raider-patrol-strike"]
        formation = self._module(profile, "sample:squad-formation")

        self.assertEqual(
            self._parameters(formation),
            {
                "baseIndex": "0",
                "anchorRole": "Mech",
                "maxFollowersPerCaptain": "2",
                "formationRadiusCells": "14",
                "cooldownSeconds": "8",
            },
        )
        self.assertLessEqual(len(profile["modules"]), 8)


if __name__ == "__main__":
    unittest.main()
