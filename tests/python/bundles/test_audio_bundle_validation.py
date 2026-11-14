"""Serialized AudioSource auditing must not substitute healthy defaults."""
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace as NS
from unittest.mock import patch
from tests.python.helpers import REPO_ROOT, load_tool_module

AUDIO = load_tool_module("validate_audio_assets")

class AudioBundleValidationTests(unittest.TestCase):
    def audit(self, data=None, error=None):
        obj = NS(type=NS(name="AudioSource"), path_id=17)
        def read():
            if error:
                raise error
            return data
        obj.read = read
        with tempfile.TemporaryDirectory() as directory:
            bundle = Path(directory) / "test.bundle"
            bundle.touch()
            with patch.object(AUDIO.UnityPy, "load", return_value=NS(objects=[obj])):
                return AUDIO.AudioAssetValidator().validate_bundle_audio(bundle)[1][0]

    def source(self, blend=1.0, minimum=3.0, maximum=60.0, doppler=0.0):
        return NS(panLevelCustomCurve=NS(m_Curve=[NS(value=blend)]),
                  MinDistance=minimum, MaxDistance=maximum, DopplerLevel=doppler)

    def test_serialized_fields_are_read(self):
        result = self.audit(self.source(minimum=5, maximum=75, doppler=0.1))
        self.assertEqual((1, 5, 75, 0.1), (result.spatial_blend, result.min_distance,
                                          result.max_distance, result.doppler_level))
        self.assertEqual(100, result.score)

    def test_2d_blend_and_doppler_are_detected(self):
        result = self.audit(self.source(blend=0, doppler=1))
        self.assertTrue(any("2D_SPATIAL" in f for f in result.flags))
        self.assertTrue(any("EXCESSIVE_DOPPLER" in f for f in result.flags))

    def test_missing_invalid_or_unreadable_data_remains_in_report(self):
        for data in (NS(), self.source(minimum=70), self.source(doppler=float("nan"))):
            with self.subTest(data=data):
                result = self.audit(data)
                self.assertEqual(0, result.score)
                self.assertIn("AUDIO_READ_ERROR", result.flags[0])
        self.assertEqual(0, self.audit(error=ValueError("corrupt")).score)

    def test_missing_bundle_is_an_error(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(FileNotFoundError):
                AUDIO.AudioAssetValidator().validate_bundle_audio(Path(directory) / "absent.bundle")

    def test_skill_copy_matches(self):
        root = REPO_ROOT
        self.assertEqual((root / "tools/validate_audio_assets.py").read_bytes(),
                         (root / ".agents/skills/rogue-command-audio-authoring/scripts/validate_audio_assets.py").read_bytes())
