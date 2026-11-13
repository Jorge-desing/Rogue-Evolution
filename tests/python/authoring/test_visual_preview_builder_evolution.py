"""Evolution contract for RogueCommandVisualPreviewBuilder built-bundle logging."""
from pathlib import Path
import unittest

REPO_ROOT = Path(__file__).resolve().parents[3]
BUILDER = REPO_ROOT / "unity" / "RogueCommandAssetAuthoring" / "Assets" / "Editor" / "RogueCommandVisualPreviewBuilder.cs"


class TestVisualPreviewBuilderEvolution(unittest.TestCase):
    def test_built_bundle_summary_log(self):
        src = BUILDER.read_text(encoding="utf-8")
        self.assertIn("ForceRebuildAssetBundle", src, "missing force rebuild")


if __name__ == "__main__":
    unittest.main()
