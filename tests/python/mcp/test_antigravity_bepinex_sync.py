"""Isolated checks for Antigravity BepInEx synchronization."""
import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from tools import sync_antigravity_bepinex as sync


class AntigravitySyncTests(unittest.TestCase):
    def test_sync_preserves_unmanaged_files_and_backs_up_changes(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            home = root / "antigravity"
            existing = home / "mcp/bepinex_mcp/system_ping.json"
            existing.parent.mkdir(parents=True)
            existing.write_text("old", encoding="utf-8")
            unrelated = existing.parent / "custom.json"
            unrelated.write_text("keep", encoding="utf-8")
            with mock.patch.object(sync, "ROOT", root), mock.patch("sys.argv", ["sync", "--home", str(home)]):
                self.assertEqual(sync.main(), 0)
            self.assertEqual(unrelated.read_text(encoding="utf-8"), "keep")
            backup = next((root / "artifacts/antigravity-backups").iterdir())
            self.assertEqual((backup / "mcp/bepinex_mcp/system_ping.json").read_text(), "old")
            manifest = json.loads((backup / "manifest.json").read_text(encoding="utf-8"))
            self.assertTrue(any(row["path"] == str(existing) and row["beforeSha256"] for row in manifest))
            with mock.patch.object(sync, "ROOT", root), mock.patch("sys.argv", ["sync", "--home", str(home), "--check"]):
                self.assertEqual(sync.main(), 0)

    def test_skill_and_schema_are_current(self):
        with tempfile.TemporaryDirectory() as tmp:
            files = sync.payloads(Path(tmp))
            seq = next(value for path, value in files.items() if path.name == "automation_run_sequence.json")
            schema = json.loads(seq)
            self.assertIn("expect", schema["parameters"]["properties"]["steps"]["items"]["properties"])
            self.assertTrue(any(path.name == "automation_wait_for_state.json" for path in files))
            self.assertEqual(next(value for path, value in files.items() if path.name == "automation.md"),
                             (sync.SOURCE / "references/automation.md").read_bytes())
            self.assertEqual(sum(path.suffix == ".json" for path in files), len(sync.TOOL_SPECS))
            for name in ("rogue-command-steam-runtime-validation", "rogue-command-clean-room-diagnostics", "rogue-command-harmony-policy"):
                self.assertTrue((sync.SOURCE.parent / name / "SKILL.md").is_file())


if __name__ == "__main__":
    unittest.main()
