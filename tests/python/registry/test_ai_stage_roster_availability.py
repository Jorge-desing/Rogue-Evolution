"""Ensure stage-scoped AI rosters are available in every referenced stage."""

from __future__ import annotations

import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
PACKS_ROOT = ROOT / "content" / "packs"


class StageAiRosterAvailabilityTests(unittest.TestCase):
    def test_scoped_ai_rosters_are_listed_in_each_referenced_stage(self) -> None:
        for manifest_path in sorted(PACKS_ROOT.glob("*/content.json")):
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            for profile in manifest.get("aiProfiles", []):
                if not profile.get("scopeToReferencedStages"):
                    continue

                roster_ids = {
                    entry["unitId"]
                    for field in ("unitEntries", "productionBuildingEntries")
                    for entry in profile.get(field, [])
                }
                for stage in manifest.get("stages", []):
                    if profile["id"] not in stage.get("aiProfileIds", []):
                        continue

                    with self.subTest(
                        pack=manifest.get("packId"),
                        profile=profile["id"],
                        stage=stage["id"],
                    ):
                        self.assertLessEqual(
                            roster_ids,
                            set(stage.get("unitIds", [])),
                            "Scoped AI roster products must be available in every stage "
                            "that references the profile",
                        )


if __name__ == "__main__":
    unittest.main()
