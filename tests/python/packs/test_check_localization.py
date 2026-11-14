"""Tests del validador de localización del pack."""

from __future__ import annotations

import json
import unittest

from tests.python.helpers import SAMPLE_NATIVE_PATH, load_tool_module

LOCA = load_tool_module("check_localization")
SCAFFOLD = load_tool_module("scaffold_pack")


class CheckLocalizationTests(unittest.TestCase):
    def test_scaffold_passes(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        self.assertEqual([], LOCA.validate_localization(pack))

    def test_real_packs_pass(self) -> None:
        pack = json.loads(SAMPLE_NATIVE_PATH.read_text(encoding="utf-8"))
        self.assertEqual([], LOCA.validate_localization(pack))

    def test_unsupported_language_rejected(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["units"][0]["localizations"].append(
            {"language": "xx-YY", "name": "Nada"})
        findings = LOCA.validate_localization(pack)
        self.assertTrue(any("xx-YY" in f for f in findings))

    def test_duplicate_language_rejected(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["units"][0]["localizations"].append(
            {"language": "en-US", "name": "Otro nombre"})
        findings = LOCA.validate_localization(pack)
        self.assertTrue(any("duplicad" in f.lower() for f in findings))

    def test_custom_unit_requires_english_name(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        unit = pack["units"][0]
        unit.pop("nativeTemplateId", None)
        unit["localizations"] = [
            {"language": "es-XL", "name": "Sólo español"}]
        findings = LOCA.validate_localization(pack)
        self.assertTrue(any("en-US" in f for f in findings))

    def test_native_unit_may_omit_localizations(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["units"][0].pop("localizations", None)
        findings = LOCA.validate_localization(pack)
        self.assertFalse(any("en-US" in f for f in findings))

    def test_blank_english_name_rejected(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["units"][0]["localizations"][0]["name"] = "   "
        findings = LOCA.validate_localization(pack)
        self.assertTrue(any("en-US" in f for f in findings))

    def test_landscape_requires_english(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["stages"][0]["landscape"] = {
            "nativeTemplateId": "27 Circles",
            "localizations": [
                {"language": "es-XL", "name": "Sin inglés"}],
        }
        findings = LOCA.validate_localization(pack)
        self.assertTrue(any("landscape" in f.lower() for f in findings))

    def test_relic_requires_english_name(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["relics"] = [{
            "id": "ns:relic",
            "localizations": [{"language": "es-XL", "name": "Reliquia"}],
        }]
        findings = LOCA.validate_localization(pack)
        self.assertTrue(any("relic" in f.lower() for f in findings))

    def test_empty_pack(self) -> None:
        self.assertEqual([], LOCA.validate_localization({}))


if __name__ == "__main__":
    unittest.main()
