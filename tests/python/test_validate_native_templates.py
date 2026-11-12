"""Tests de validación de plantillas nativas."""
from __future__ import annotations
import unittest
from tests.python.helpers import load_tool_module

VALIDATE = load_tool_module("validate_native_templates")
SCAFFOLD = load_tool_module("scaffold_pack")

def _valid_pack() -> dict:
    return SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")


class ValidateNativeTemplatesTests(unittest.TestCase):
    def test_valid_pack_no_issues(self) -> None:
        pack = _valid_pack()
        issues = VALIDATE.validate_native_templates(pack)
        # scaffold uses PCXBaseTank / PCXBaseTankFactory etc. which are known
        self.assertEqual([], issues)

    def test_unknown_unit_template_flagged(self) -> None:
        pack = _valid_pack()
        pack["units"][0]["nativeTemplateId"] = "NoSuchTemplate123"
        issues = VALIDATE.validate_native_templates(pack)
        self.assertTrue(any("NoSuchTemplate123" in i for i in issues))

    def test_unknown_factory_template_flagged(self) -> None:
        pack = _valid_pack()
        factory = pack["units"][0]["productionBuilding"]
        factory["nativeTemplateId"] = "NoSuchFactory999"
        issues = VALIDATE.validate_native_templates(pack)
        self.assertTrue(any("NoSuchFactory999" in i for i in issues))

    def test_unknown_stage_template_flagged(self) -> None:
        pack = _valid_pack()
        pack["stages"][0]["nativeTemplateId"] = "StageParameters_NoSuch"
        issues = VALIDATE.validate_native_templates(pack)
        self.assertTrue(any("StageParameters_NoSuch" in i for i in issues))

    def test_unknown_landscape_template_flagged(self) -> None:
        pack = _valid_pack()
        pack["stages"][0]["landscape"] = {"nativeTemplateId": "NoSuchWorld"}
        issues = VALIDATE.validate_native_templates(pack)
        self.assertTrue(any("NoSuchWorld" in i for i in issues))

    def test_custom_known_templates_extend_allowlist(self) -> None:
        pack = _valid_pack()
        pack["units"][0]["nativeTemplateId"] = "CustomBaseTank"
        issues = VALIDATE.validate_native_templates(pack, known_unit_templates={"CustomBaseTank"})
        self.assertFalse(any("CustomBaseTank" in i for i in issues))

    def test_empty_pack_passes(self) -> None:
        self.assertEqual([], VALIDATE.validate_native_templates({}))


if __name__ == "__main__":
    unittest.main()
