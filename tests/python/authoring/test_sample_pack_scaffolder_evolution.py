"""Evolution contract for RogueCommandSamplePackScaffolder bundle build."""
from pathlib import Path
import unittest

REPO_ROOT = Path(__file__).resolve().parents[3]
SCAFFOLDER = REPO_ROOT / "unity" / "RogueCommandAssetAuthoring" / "Assets" / "Editor" / "RogueCommandSamplePackScaffolder.cs"


class TestSamplePackScaffolderEvolution(unittest.TestCase):
    def test_force_rebuild_and_bundle_validation(self):
        src = SCAFFOLDER.read_text(encoding="utf-8")
        self.assertIn("ForceRebuildAssetBundle", src, "missing ForceRebuildAssetBundle")
        self.assertIn("ValidateBuiltBundles", src, "missing post-build bundle validation")
        self.assertIn("Built bundle", src, "missing per-bundle build summary log")


if __name__ == "__main__":
    unittest.main()
