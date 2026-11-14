import hashlib
import json
import os
import tempfile
import unittest
from pathlib import Path

from tests.python.helpers import load_tool_module

profile_hash_audit = load_tool_module("profile_hash_audit")


class ProfileHashAuditTests(unittest.TestCase):
    def _make_profile_and_manifest(self, files: dict[str, str]) -> tuple[str, str]:
        d = tempfile.mkdtemp()
        profile = Path(d) / "profile"
        profile.mkdir()
        for name, content in files.items():
            p = profile / name
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text(content, encoding="utf-8")

        manifest_files = []
        for name in files:
            p = profile / name
            manifest_files.append({
                "path": name,
                "size": p.stat().st_size,
                "sha256": profile_hash_audit.compute_file_hash(p),
            })
        manifest = {"profileFiles": manifest_files}
        manifest_path = Path(d) / "manifest.json"
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
        return str(profile), str(manifest_path)

    def test_exact_match_returns_true(self):
        profile, manifest = self._make_profile_and_manifest({"a.txt": "hello", "b.txt": "world"})
        result = profile_hash_audit.audit_profile(profile, manifest)
        self.assertTrue(result["exact_match"])
        self.assertEqual(result["added"], [])
        self.assertEqual(result["missing"], [])
        self.assertEqual(result["changed"], [])

    def test_detects_added_file(self):
        profile, manifest = self._make_profile_and_manifest({"a.txt": "hello"})
        (Path(profile) / "extra.txt").write_text("extra", encoding="utf-8")
        result = profile_hash_audit.audit_profile(profile, manifest)
        self.assertFalse(result["exact_match"])
        self.assertIn("extra.txt", result["added"])

    def test_detects_changed_file(self):
        profile, manifest = self._make_profile_and_manifest({"a.txt": "hello"})
        (Path(profile) / "a.txt").write_text("changed", encoding="utf-8")
        result = profile_hash_audit.audit_profile(profile, manifest)
        self.assertFalse(result["exact_match"])
        self.assertIn("a.txt", result["changed"])

    def test_detects_missing_file(self):
        profile, manifest = self._make_profile_and_manifest({"a.txt": "hello", "b.txt": "world"})
        (Path(profile) / "b.txt").unlink()
        result = profile_hash_audit.audit_profile(profile, manifest)
        self.assertFalse(result["exact_match"])
        self.assertIn("b.txt", result["missing"])

    def test_compute_file_hash_consistent(self):
        with tempfile.NamedTemporaryFile(delete=False) as f:
            f.write(b"test content")
            path = f.name
        h1 = profile_hash_audit.compute_file_hash(Path(path))
        h2 = profile_hash_audit.compute_file_hash(Path(path))
        self.assertEqual(h1, h2)
        self.assertEqual(len(h1), 64)
        os.unlink(path)


if __name__ == "__main__":
    unittest.main()
