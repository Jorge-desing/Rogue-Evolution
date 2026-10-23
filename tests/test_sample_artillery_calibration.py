"""Balance regression checks for the sample-native artillery unit."""

from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path
from typing import Any


def _load_simulator() -> Any:
    script_path = Path(__file__).resolve().parents[1] / "tools" / "simulate_combat_balance.py"
    spec = importlib.util.spec_from_file_location("simulate_combat_balance", script_path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not load simulator from {script_path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


SIMULATOR = _load_simulator()
ROOT = Path(__file__).resolve().parents[1]
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
