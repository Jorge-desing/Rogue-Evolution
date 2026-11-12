"""JSON Schema contract for native visual rig retarget mappings."""

import copy
import json
import unittest
from pathlib import Path

from jsonschema import Draft202012Validator, ValidationError


ROOT = Path(__file__).resolve().parents[3]
SCHEMA = ROOT / "content" / "schema" / "content.schema.json"
SAMPLE = ROOT / "content" / "packs" / "sample-native-expansion" / "content.json"
UNIT_ID = "sample-native:assault-walker"


class NativeRigRetargetSchemaTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
        Draft202012Validator.check_schema(schema)
        cls.validator = Draft202012Validator(schema)
        cls.sample = json.loads(SAMPLE.read_text(encoding="utf-8"))
        cls.unit_index = next(
            index for index, unit in enumerate(cls.sample["units"])
            if unit["id"] == UNIT_ID
        )
        cls.unit_path = ["units", cls.unit_index]
        cls.retarget_path = cls.unit_path + ["nativeRigRetarget"]
        cls.validator.validate(cls.sample)

    def manifest_with_mappings(self, mappings):
        manifest = copy.deepcopy(self.sample)
        manifest["units"][self.unit_index]["nativeRigRetarget"] = {"mappings": mappings}
        return manifest

    def assert_mapping_rejected(self, mappings, path, keyword) -> None:
        with self.assertRaises(ValidationError) as caught:
            self.validator.validate(self.manifest_with_mappings(mappings))
        self.assertEqual(list(caught.exception.absolute_path), self.retarget_path + path)
        self.assertEqual(caught.exception.validator, keyword)

    def test_retarget_is_optional(self) -> None:
        manifest = copy.deepcopy(self.sample)
        del manifest["units"][self.unit_index]["nativeRigRetarget"]
        self.validator.validate(manifest)

    def test_native_walker_mappings_are_accepted(self) -> None:
        mappings = [
            {
                "sourcePath": "RoboMarine_Mesh/LEGS/LeftLeg/LLow",
                "targetPath": "IroncladWalkerModel/IroncladWalker_Armature/Bone_Root/Bone_Pelvis/Bone_Thigh_L/Bone_Shin_L",
            },
            {
                "sourcePath": "RoboMarine_Mesh/RotHor",
                "targetPath": "IroncladWalkerModel/IroncladWalker_Armature/Bone_Root/Bone_Pelvis/Bone_TurretYaw",
            },
        ]
        self.validator.validate(self.manifest_with_mappings(mappings))

    def test_one_to_64_mappings_are_accepted(self) -> None:
        for count in (1, 64):
            with self.subTest(count=count):
                mappings = [
                    {"sourcePath": "Native/Rig/Joint", "targetPath": f"Visual/Rig/Joint{index}"}
                    for index in range(count)
                ]
                self.validator.validate(self.manifest_with_mappings(mappings))

    def test_more_than_64_mappings_are_rejected(self) -> None:
        mapping = {"sourcePath": "Native/Rig/Joint", "targetPath": "Visual/Rig/Joint"}
        self.assert_mapping_rejected(
            [copy.deepcopy(mapping) for _ in range(65)], ["mappings"], "maxItems"
        )

    def test_mapping_requires_both_paths_and_rejects_unknown_fields(self) -> None:
        self.assert_mapping_rejected([{"targetPath": "Visual/Rig/Joint"}], ["mappings", 0], "required")
        self.assert_mapping_rejected(
            [{"sourcePath": "Native/Rig/Joint", "targetPath": "Visual/Rig/Joint", "scale": 1}],
            ["mappings", 0], "additionalProperties",
        )

    def test_paths_reject_empty_traversal_and_windows_segments(self) -> None:
        for invalid in ("", "/Native/Rig", "Native//Rig", "Native/../Rig", "Native/./Rig", "Native\\Rig", "Native/\n"):
            with self.subTest(path=invalid):
                self.assert_mapping_rejected(
                    [{"sourcePath": invalid, "targetPath": "Visual/Rig/Joint"}],
                    ["mappings", 0, "sourcePath"], "minLength" if invalid == "" else "pattern",
                )

    def test_retarget_requires_native_template_bundle_and_visual_roots(self) -> None:
        manifest = copy.deepcopy(self.sample)
        unit = manifest["units"][self.unit_index]
        del unit["nativeTemplateId"]
        errors = list(self.validator.iter_errors(manifest))
        self.assertTrue(
            any(
                list(error.absolute_path) == self.unit_path
                and error.validator == "required"
                and "nativeTemplateId" in error.message
                for error in errors
            ),
            "A retarget declaration without a nativeTemplateId must fail the unit requirements.",
        )


if __name__ == "__main__":
    unittest.main()
