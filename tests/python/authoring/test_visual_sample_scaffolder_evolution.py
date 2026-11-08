"""Evolution contract for RogueCommandVisualSampleScaffolder declarative delegation."""
from pathlib import Path
import unittest

REPO_ROOT = Path(__file__).resolve().parents[3]
SCAFFOLDER = REPO_ROOT / "unity" / "RogueCommandAssetAuthoring" / "Assets" / "Editor" / "RogueCommandVisualSampleScaffolder.cs"


def _read():
    return SCAFFOLDER.read_text(encoding="utf-8")


class TestVisualSampleScaffolderEvolution(unittest.TestCase):
    def test_vehicle_creates_delegate_to_rigged_helper(self):
        src = _read()
        # Each vehicle visual must delegate to the shared helper instead of
        # duplicating the 60-line instantiate/materials/sockets/animator body.
        for name in ["CreateSampleVisual", "CreateSkirmishBikeVisual", "CreateColossusVisual", "CreateSurgeArcTankVisual"]:
            # find method body slice starting at the declaration
            start = src.find(f"private static string {name}()")
            self.assertGreaterEqual(start, 0, f"{name} missing")
            body = src[start:start + 600]
            self.assertIn("CreateRiggedWalkerVisual(", body, f"{name} does not delegate to CreateRiggedWalkerVisual: {body[:200]}")

    def test_lod_priority_and_turret_sockets_present(self):
        src = _read()
        self.assertIn("ResolveEffectiveModelPath", src, "missing LOD-priority resolver")
        self.assertIn("Socket_TurretPivot", src, "missing turret pivot socket guarantee")
        self.assertIn("EnsureTurretAndRadarSockets", src, "missing turret/radar socket helper")

    def test_post_build_validation_logging_present(self):
        src = _read()
        self.assertIn("ValidateSavedPrefab", src, "missing post-build prefab validation")


if __name__ == "__main__":
    unittest.main()
