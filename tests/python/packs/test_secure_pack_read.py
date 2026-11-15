import os
from pathlib import Path
import tempfile
import unittest

from tools.secure_pack_read import open_pack_file


class SecurePackReadTests(unittest.TestCase):
    def test_reads_regular_file_and_rejects_traversal(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "bundles").mkdir()
            (root / "bundles/a.bundle").write_bytes(b"bundle")
            with open_pack_file(root, "bundles/a.bundle") as stream:
                self.assertEqual(b"bundle", stream.read())
                if os.name == "nt":
                    with self.assertRaises(OSError):
                        (root / "bundles").rename(root / "moved")
            with self.assertRaises(ValueError):
                with open_pack_file(root, "../outside"):
                    pass

    def test_rejects_symlink_if_platform_permits_creation(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "actual").mkdir()
            (root / "actual/a.bundle").write_bytes(b"bundle")
            try:
                (root / "link").symlink_to(root / "actual", target_is_directory=True)
            except OSError as exc:
                self.skipTest(f"symlink creation unavailable: {exc}")
            with self.assertRaises((OSError, ValueError)):
                with open_pack_file(root, "link/a.bundle"):
                    pass

    def test_exclusive_creation_does_not_replace_existing_file(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "bundles").mkdir()
            with open_pack_file(root, "bundles/a.bundle", create=True) as stream:
                stream.write(b"original")
                if os.name == "nt":
                    with self.assertRaises(OSError):
                        (root / "bundles").rename(root / "moved")
            with self.assertRaises(OSError):
                with open_pack_file(root, "bundles/a.bundle", create=True):
                    pass
            self.assertEqual(b"original", (root / "bundles/a.bundle").read_bytes())


if __name__ == "__main__":
    unittest.main()
