import unittest
from unittest import mock

from tests.python.helpers import load_tool_module

audit_unity_bundle = load_tool_module("audit_unity_bundle")


class AuditUnityBundleTests(unittest.TestCase):
    def test_audit_bundle_returns_dict_with_required_keys(self):
        with mock.patch("UnityPy.load") as mock_load:
            mock_env = mock.MagicMock()
            mock_env.objects = []
            mock_env.file.dependencies = []
            mock_load.return_value = mock_env
            result = audit_unity_bundle.audit_bundle("fake_bundle.assets")
            self.assertIsInstance(result, dict)
            self.assertIn("bundle_path", result)
            self.assertIn("object_count", result)
            self.assertIn("type_counts", result)
            self.assertIn("texture_count", result)
            self.assertIn("shader_count", result)

    def test_audit_bundle_handles_missing_file_gracefully(self):
        with mock.patch("UnityPy.load", side_effect=FileNotFoundError("not found")):
            with self.assertRaises(FileNotFoundError):
                audit_unity_bundle.audit_bundle("nonexistent.assets")

    def test_audit_bundle_counts_object_types(self):
        with mock.patch("UnityPy.load") as mock_load:
            obj1 = mock.MagicMock()
            obj1.type.name = "Texture2D"
            obj1.byte_size = 1024
            obj2 = mock.MagicMock()
            obj2.type.name = "Texture2D"
            obj2.byte_size = 2048
            obj3 = mock.MagicMock()
            obj3.type.name = "Mesh"
            obj3.byte_size = 512
            mock_env = mock.MagicMock()
            mock_env.objects = [obj1, obj2, obj3]
            mock_env.file.dependencies = []
            mock_load.return_value = mock_env
            result = audit_unity_bundle.audit_bundle("test.assets")
            self.assertEqual(result["object_count"], 3)
            self.assertEqual(result["type_counts"]["Texture2D"], 2)
            self.assertEqual(result["type_counts"]["Mesh"], 1)


if __name__ == "__main__":
    unittest.main()
