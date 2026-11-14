"""Tests de la huella digital del pack."""

from __future__ import annotations

import unittest

from tests.python.helpers import load_tool_module

FINGERPRINT = load_tool_module("fingerprint_content_pack")
SCAFFOLD = load_tool_module("scaffold_pack")


class FingerprintTests(unittest.TestCase):
    def test_fingerprint_stable_and_hex(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        first = FINGERPRINT.fingerprint_pack(pack)
        second = FINGERPRINT.fingerprint_pack(SCAFFOLD.generate_scaffold("ns:pack", "alpha"))
        self.assertEqual(first, second)
        self.assertEqual(64, len(first))
        self.assertTrue(all(c in "0123456789abcdef" for c in first))

    def test_fingerprint_changes_with_content(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        other = SCAFFOLD.generate_scaffold("ns:pack", "beta")
        self.assertNotEqual(
            FINGERPRINT.fingerprint_pack(pack),
            FINGERPRINT.fingerprint_pack(other),
        )

    def test_verify(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        fp = FINGERPRINT.fingerprint_pack(pack)
        self.assertTrue(FINGERPRINT.verify_fingerprint(pack, fp))
        self.assertFalse(FINGERPRINT.verify_fingerprint(pack, "0" * 64))
        self.assertFalse(FINGERPRINT.verify_fingerprint(pack, None))


if __name__ == "__main__":
    unittest.main()
