"""Tests del exportador de registro global."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from tests.python.helpers import load_tool_module

EXPORT = load_tool_module("export_registry")
SCAFFOLD = load_tool_module("scaffold_pack")


class ExportRegistryTests(unittest.TestCase):
    def _write_pack(self, root: Path, folder: str, pack: dict) -> None:
        target = root / folder
        target.mkdir(parents=True, exist_ok=True)
        (target / "content.json").write_text(
            json.dumps(pack, indent=2), encoding="utf-8"
        )

    def test_registry_collects_units_factories_stages_profiles(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self._write_pack(root, "pack", pack)
            result = EXPORT.export_registry(root)

            self.assertEqual(1, result["summary"]["checkedManifests"])
            kinds = result["summary"]["kinds"]
            self.assertEqual(1, kinds["unit"])
            self.assertEqual(1, kinds["factory"])
            self.assertEqual(1, kinds["stage"])
            self.assertEqual(1, kinds["aiProfile"])
            self.assertTrue(result["summary"]["registryValid"])

            ids = [e["id"] for e in result["entries"]]
            self.assertIn("ns:alpha", ids)
            self.assertIn("ns:alpha-factory", ids)

            factory_entry = [e for e in result["entries"] if e["id"] == "ns:alpha-factory"][0]
            self.assertEqual("ns:alpha", factory_entry["produces"])
            self.assertEqual(["ns:alpha"], result["incoming"]["ns:alpha-factory"])

    def test_registry_tracks_load_errors(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            bad = root / "bad"
            bad.mkdir()
            (bad / "content.json").write_text("{ not json", encoding="utf-8")
            result = EXPORT.export_registry(root)
            self.assertIn("bad", result["summary"]["loadErrors"])
            self.assertFalse(result["summary"]["registryValid"])


if __name__ == "__main__":
    unittest.main()
