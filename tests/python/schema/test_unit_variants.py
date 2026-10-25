"""JSON Schema contract for declarative unit loadout variants."""

import copy
import json
import unittest
from pathlib import Path

from jsonschema import Draft202012Validator, ValidationError


ROOT = Path(__file__).resolve().parents[3]
SCHEMA = ROOT / "content" / "schema" / "content.schema.json"
SAMPLE = ROOT / "content" / "packs" / "sample-native-expansion" / "content.json"
VARIANT_PATH = ["units", 0, "variants"]


class UnitVariantSchemaTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
        Draft202012Validator.check_schema(schema)
        cls.validator = Draft202012Validator(schema)
        cls.sample = json.loads(SAMPLE.read_text(encoding="utf-8"))
        # Invalid fixtures must fail for the variant, not a pre-existing pack error.
        cls.validator.validate(cls.sample)

    def manifest_with_variants(self, variants):
        manifest = copy.deepcopy(self.sample)
        manifest["units"][0]["variants"] = variants
        return manifest

    def assert_variant_rejected(self, variants, path, keyword) -> None:
        with self.assertRaises(ValidationError) as caught:
            self.validator.validate(self.manifest_with_variants(variants))
        self.assertEqual(list(caught.exception.absolute_path), VARIANT_PATH + path)
        self.assertEqual(caught.exception.validator, keyword)

    def test_variants_are_optional(self) -> None:
        self.validator.validate(copy.deepcopy(self.sample))

    def test_ap_and_he_with_raw_balance_override_are_accepted(self) -> None:
        variants = [
            {"id": "ap", "loadout": "AP", "balanceOverrides": [
                {"name": "damage1", "value": "9"},
            ]},
            {"id": "he", "loadout": "HE", "balanceOverrides": [
                {"name": "effectRadius1", "value": "1.5"},
            ]},
        ]
        self.validator.validate(self.manifest_with_variants(variants))

    def test_only_id_is_required_and_local_ascii_punctuation_is_allowed(self) -> None:
        self.validator.validate(self.manifest_with_variants([
            {"id": "a._-"},
            {"id": "a" + "0" * 63, "balanceOverrides": []},
        ]))

    def test_eight_variants_are_accepted(self) -> None:
        self.validator.validate(self.manifest_with_variants([
            {"id": f"loadout-{index}"} for index in range(8)
        ]))

    def test_more_than_eight_variants_are_rejected(self) -> None:
        self.assert_variant_rejected(
            [{"id": f"loadout-{index}"} for index in range(9)], [], "maxItems"
        )

    def test_more_than_64_variant_balance_overrides_are_rejected(self) -> None:
        overrides = [{"name": "damage1", "value": "9"} for _ in range(65)]
        self.assert_variant_rejected(
            [{"id": "ap", "balanceOverrides": overrides}],
            [0, "balanceOverrides"], "maxItems",
        )

    def test_invalid_or_blank_local_ids_are_rejected(self) -> None:
        for invalid in ("", " ", " AP", "AP", "1ap", "ap:he", "ap/he", "éclair", "a" * 65):
            with self.subTest(id=invalid):
                self.assert_variant_rejected([{"id": invalid}], [0, "id"], "pattern")

    def test_local_ids_reject_trailing_line_terminators(self) -> None:
        for invalid in ("ap" + chr(10), "ap" + chr(13), "ap" + chr(13) + chr(10),
                        "ap" + chr(0x2028), "ap" + chr(0x2029)):
            with self.subTest(id=invalid):
                self.assert_variant_rejected([{"id": invalid}], [0, "id"], "pattern")

    def test_variant_id_is_required_and_must_be_string(self) -> None:
        self.assert_variant_rejected([{"loadout": "AP"}], [0], "required")
        self.assert_variant_rejected([{"id": 3}], [0, "id"], "type")

    def test_invalid_loadout_is_rejected(self) -> None:
        for invalid in ("ap", "HEAT", "", None):
            with self.subTest(loadout=invalid):
                keyword = "type" if invalid is None else "enum"
                self.assert_variant_rejected(
                    [{"id": "ap", "loadout": invalid}], [0, "loadout"], keyword
                )

    def test_invalid_raw_override_names_and_values_are_rejected(self) -> None:
        for override, suffix, keyword in (
            ({"name": "Damage1", "value": "9"}, "name", "enum"),
            ({"name": "damage1", "value": 9}, "value", "type"),
            ({"name": "damage1", "value": "-1"}, "value", "pattern"),
        ):
            with self.subTest(override=override):
                self.assert_variant_rejected(
                    [{"id": "ap", "balanceOverrides": [override]}],
                    [0, "balanceOverrides", 0, suffix], keyword,
                )

    def test_additional_variant_fields_are_rejected(self) -> None:
        for field in ("productionBuilding", "customBalance", "unexpected"):
            with self.subTest(field=field):
                self.assert_variant_rejected(
                    [{"id": "ap", field: {}}], [0], "additionalProperties"
                )

    def test_variant_container_and_entries_must_have_correct_types(self) -> None:
        self.assert_variant_rejected(None, [], "type")
        self.assert_variant_rejected([None], [0], "type")
        self.assert_variant_rejected([{"id": "ap", "balanceOverrides": {}}],
                                     [0, "balanceOverrides"], "type")


if __name__ == "__main__":
    unittest.main()
