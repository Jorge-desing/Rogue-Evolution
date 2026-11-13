"""Evolution contract for RogueCommandAssetBundleBuilder post-build validation."""
from pathlib import Path
import unittest

REPO_ROOT = Path(__file__).resolve().parents[3]
BUILDER = REPO_ROOT / "unity" / "RogueCommandAssetAuthoring" / "Assets" / "Editor" / "RogueCommandAssetBundleBuilder.cs"


class TestAssetBundleBuilderEvolution(unittest.TestCase):
    def test_force_rebuild_and_post_build_validation(self):
        src = BUILDER.read_text(encoding="utf-8")
        self.assertIn("ForceRebuildAssetBundle", src, "missing ForceRebuildAssetBundle for deterministic bundles")
        self.assertIn("ValidateBuiltBundles", src, "missing post-build bundle validation")



if __name__ == "__main__":
    unittest.main()
