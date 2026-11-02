import json
import tempfile
import unittest
from pathlib import Path

from tests.python.helpers import load_tool_module

validate_content_pack = load_tool_module("validate_content_pack")


class ValidateContentPackTests(unittest.TestCase):
    def _make_pack(self, content: dict, files: list[str] | None = None) -> str:
        d = tempfile.mkdtemp()
        (Path(d) / "content.json").write_text(json.dumps(content), encoding="utf-8")
        for f in (files or []):
            p = Path(d) / f
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text("dummy", encoding="utf-8")
        return d

    def test_valid_minimal_pack(self):
        content = {
            "packId": "test-pack",
            "displayName": "Test Pack",
            "version": "1.0.0",
            "units": [{"unitId": "u1", "displayName": "Unit 1"}],
            "buildings": [{"buildingId": "b1", "displayName": "Building 1"}],
            "stages": [{"stageId": "s1", "displayName": "Stage 1"}],
        }
        d = self._make_pack(content)
        errors = validate_content_pack.validate_pack(d)
        self.assertEqual(errors, [])

    def test_missing_content_json(self):
        d = tempfile.mkdtemp()
        errors = validate_content_pack.validate_pack(d)
        self.assertTrue(any("content.json" in e for e in errors))

    def test_invalid_schema_missing_required(self):
        content = {}
        d = self._make_pack(content)
        errors = validate_content_pack.validate_pack(d)
        self.assertTrue(len(errors) > 0)

    def test_duplicate_unit_ids(self):
        content = {
            "packId": "test-pack",
            "displayName": "Test",
            "version": "1.0.0",
            "units": [
                {"unitId": "u1", "displayName": "U1"},
                {"unitId": "u1", "displayName": "U2"},
            ],
            "buildings": [],
            "stages": [],
        }
        d = self._make_pack(content)
        errors = validate_content_pack.validate_pack(d)
        self.assertTrue(any("duplicad" in e.lower() or "duplicate" in e.lower() for e in errors))

    def test_referenced_file_missing(self):
        content = {
            "packId": "test-pack",
            "displayName": "Test",
            "version": "1.0.0",
            "units": [{"unitId": "u1", "displayName": "U1", "iconPath": "icons/missing.png"}],
            "buildings": [],
            "stages": [],
        }
        d = self._make_pack(content)
        errors = validate_content_pack.validate_pack(d)
        self.assertTrue(any("no existe" in e or "missing" in e.lower() for e in errors))

    def test_referenced_file_exists(self):
        content = {
            "packId": "test-pack",
            "displayName": "Test",
            "version": "1.0.0",
            "units": [{"unitId": "u1", "displayName": "U1", "iconPath": "icons/ok.png"}],
            "buildings": [],
            "stages": [],
        }
        d = self._make_pack(content, files=["icons/ok.png"])
        errors = validate_content_pack.validate_pack(d)
        self.assertEqual(errors, [])

    def test_custom_schema_validation(self):
        content = {
            "packId": "test-pack",
            "displayName": "Test",
            "version": "1.0.0",
            "customProp": "allowed",
        }
        custom_schema = {
            "type": "object",
            "required": ["packId", "customProp"],
            "properties": {
                "packId": {"type": "string"},
                "customProp": {"type": "string"},
            },
        }
        d = self._make_pack(content)
        schema_path = Path(d) / "custom_schema.json"
        schema_path.write_text(json.dumps(custom_schema), encoding="utf-8")

        # Valid with custom schema
        errors = validate_content_pack.validate_pack(d, schema_path=str(schema_path))
        self.assertEqual(errors, [])

        # Nonexistent schema file
        errors_missing = validate_content_pack.validate_pack(d, schema_path=str(Path(d) / "not_there.json"))
        self.assertTrue(any("Schema no encontrado" in e for e in errors_missing))


if __name__ == "__main__":
    unittest.main()
