"""Declared localization inventory in the offline registry report (not runtime fallback)."""

import contextlib
import importlib.util
import io
import json
import sys
import unittest
from pathlib import Path
from unittest import mock

SCRIPT = Path(__file__).resolve().parents[1] / "tools" / "validate_loader_registry.py"
spec = importlib.util.spec_from_file_location("validate_loader_registry_localization", SCRIPT)
if spec is None or spec.loader is None:
    raise RuntimeError(f"Could not load {SCRIPT}")
validator = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = validator
spec.loader.exec_module(validator)


class LoaderRegistryLocalizationTests(unittest.TestCase):
    def test_report_separates_declared_product_and_factory_fields(self):
        pack = {
            "packId": "sample:pack",
            "units": [
                {
                    "id": "sample:unit",
                    "nativeTemplateId": "PCXBaseTank",
                    "localizations": [
                        {"language": "es-XL", "name": "Unidad", "description": "Texto"},
                        {"language": "en-US", "name": "Unit", "skillDescription": "Skill"},
                    ],
                    "productionBuilding": {
                        "id": "sample:factory", "nativeTemplateId": "PCXBaseTankFactory",
                        "localizations": [{"language": "en-US", "name": "Factory"}],
                    },
                },
                {"id": "sample:untranslated", "nativeTemplateId": "PCXBaseTank"},
            ],
        }
        report = validator.build_pack_report(pack, Path("sample/content.json"))
        self.assertEqual(report["unitLocalizations"], [
            {
                "unitId": "sample:factory", "nameLanguages": ["en-US"],
                "descriptionLanguages": [], "skillDescriptionLanguages": [],
                "hasEnglishName": True, "hasNativeTemplate": True,
            },
            {
                "unitId": "sample:unit", "nameLanguages": ["en-US", "es-XL"],
                "descriptionLanguages": ["es-XL"], "skillDescriptionLanguages": ["en-US"],
                "hasEnglishName": True, "hasNativeTemplate": True,
            },
            {
                "unitId": "sample:untranslated", "nameLanguages": [],
                "descriptionLanguages": [], "skillDescriptionLanguages": [],
                "hasEnglishName": False, "hasNativeTemplate": True,
            },
        ])

    def test_text_report_prints_declared_localization_not_fallback_claims(self):
        pack = {
            "packId": "sample:pack",
            "units": [{"id": "sample:unit", "productionBuilding": {"id": "sample:factory"},
                       "localizations": [{"language": "en-US", "name": "Unit"}]}],
        }
        output = io.StringIO()
        root = mock.Mock()
        root.glob.return_value = [Path("sample/content.json")]
        with mock.patch.object(validator, "PACKS_ROOT", root), \
             mock.patch.object(validator, "load_pack", return_value=pack), \
             contextlib.redirect_stdout(output):
            exit_code = validator.main([])
        self.assertEqual(exit_code, 0)
        self.assertIn("INFO sample:pack localization sample:unit declared names [en-US]", output.getvalue())
        self.assertIn("INFO sample:pack localization sample:factory declared names [none]", output.getvalue())
        self.assertNotIn("resolved fallback", output.getvalue())

    def test_json_inventory_does_not_reject_descriptive_relations_or_partial_queue(self):
        pack = {
            "packId": "sample:pack",
            "units": [
                {"id": "sample:a", "productionBuilding": {"id": "sample:a-factory"},
                 "productionQueue": {"buildTime": 10},
                 "synergies": [{"targetUnitIds": ["sample:b"]}]},
                {"id": "sample:b", "productionBuilding": {"id": "sample:b-factory"},
                 "synergies": [{"targetUnitIds": ["sample:a"]}]},
            ],
        }
        output = io.StringIO()
        root = mock.Mock()
        root.glob.return_value = [Path("sample/content.json")]
        with mock.patch.object(validator, "PACKS_ROOT", root), \
             mock.patch.object(validator, "load_pack", return_value=pack), \
             contextlib.redirect_stdout(output):
            exit_code = validator.main(["--json"])
        result = json.loads(output.getvalue())
        self.assertEqual(exit_code, 0)
        self.assertEqual(result["summary"]["issueCount"], 0)
        self.assertTrue(result["summary"]["registryValid"])
        self.assertEqual(result["packs"][0]["unitLocalizations"][0]["unitId"], "sample:a")
        self.assertIn("declared", " ".join(result["limitations"]).lower())


if __name__ == "__main__":
    unittest.main()
