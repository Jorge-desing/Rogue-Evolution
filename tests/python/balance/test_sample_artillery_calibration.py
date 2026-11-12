"""Balance regression checks for the sample-native artillery unit."""

from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path
from typing import Any


from tests.python.helpers import REPO_ROOT, SAMPLE_NATIVE_PATH, load_tool_module

SIMULATOR = load_tool_module("simulate_combat_balance")
ROOT = Path(__file__).resolve().parents[3]
SAMPLE_PACK = ROOT / "content" / "packs" / "sample-native-expansion" / "content.json"


class SampleArtilleryCalibrationTests(unittest.TestCase):
    def test_heavy_artillery_meets_siege_dps_band(self) -> None:
        repository = SIMULATOR.BalanceRepository()
        repository.load_content_pack(SAMPLE_PACK)
        artillery = repository.profiles["sample-native:heavy-artillery"]

        self.assertGreaterEqual(artillery.dps, 14.0)
        self.assertLessEqual(artillery.dps, 19.0)


if __name__ == "__main__":
    unittest.main()
