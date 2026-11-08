"""Evolution contract for ValidateIntegration tactical coverage."""
from pathlib import Path
import unittest

REPO_ROOT = Path(__file__).resolve().parents[3]
VALIDATOR = REPO_ROOT / "unity" / "RogueCommandAssetAuthoring" / "Assets" / "Editor" / "ValidateIntegration.cs"

ALL_UNITS = [
    "TidebreakerHovercraft",
    "SalvageHauler",
    "SkyguardSAM",
    "BulwarkSiegeTank",
    "ArcLanceWalker",
    "FieldMedicRover",
    "SentinelRelay",
    "GravitasBreaker",
    "CommandRelay",
]


class TestValidateIntegrationEvolution(unittest.TestCase):
    def test_covers_all_nine_tactical_units(self):
        src = VALIDATOR.read_text(encoding="utf-8")
        for unit in ALL_UNITS:
            self.assertIn(unit, src, f"validator misses {unit}")

    def test_canonical_turret_sockets_checked(self):
        src = VALIDATOR.read_text(encoding="utf-8")
        for socket in ["Socket_TurretPivot", "Socket_TurretPitch", "Socket_RadarMast", "Socket_Antenna"]:
            self.assertIn(socket, src, f"validator misses {socket}")


if __name__ == "__main__":
    unittest.main()
