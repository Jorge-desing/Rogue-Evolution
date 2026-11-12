"""Evolution contract for VehiclePrefabBuilder post-build validation and turret sockets."""
from pathlib import Path
import unittest

REPO_ROOT = Path(__file__).resolve().parents[3]
BUILDER = REPO_ROOT / "unity" / "RogueCommandAssetAuthoring" / "Assets" / "Editor" / "VehiclePrefabBuilder.cs"


class TestVehiclePrefabBuilderEvolution(unittest.TestCase):
    def test_turret_socket_helper_and_call_site(self):
        src = BUILDER.read_text(encoding="utf-8")
        self.assertIn("EnsureTurretAndRadarSockets", src, "missing turret socket helper")
        self.assertIn("Socket_TurretPivot", src, "missing turret pivot guarantee")
        # called after canonical VFX sockets in gameplay builder
        self.assertIn("EnsureTurretAndRadarSockets(root", src, "helper not called in BuildPrefabFromGameplayJson")

    def test_post_build_validation_logging(self):
        src = BUILDER.read_text(encoding="utf-8")
        self.assertIn("ValidateSavedPrefab", src, "missing post-build prefab validation")


if __name__ == "__main__":
    unittest.main()
