"""Tests de la normalización canónica del pack."""

from __future__ import annotations

import json
import unittest

from tests.python.helpers import SAMPLE_NATIVE_PATH, load_tool_module

NORMALIZE = load_tool_module("normalize_content_pack")
SCAFFOLD = load_tool_module("scaffold_pack")


class NormalizeContentPackTests(unittest.TestCase):
    def test_scaffold_normalizes_to_sorted_arrays(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        out = NORMALIZE.normalize_pack(pack)
        for key in ("units", "stages", "aiProfiles", "relics", "buildings", "assetBundles"):
            self.assertIn(key, out)
        self.assertEqual([], out["relics"])
        self.assertEqual([], out["buildings"])

    def test_normalize_is_pure(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        snapshot = json.loads(json.dumps(pack))
        NORMALIZE.normalize_pack(pack)
        self.assertEqual(snapshot, pack)

    def test_normalize_sorts_and_stringifies(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        unit = pack["units"][0]
        unit["balanceOverrides"] = [
            {"name": "neededExperienceLevel", "value": 0},
            {"name": "rarity", "value": "Common"},
        ]
        out = NORMALIZE.normalize_pack(pack)
        names = [o["name"] for o in out["units"][0]["balanceOverrides"]]
        self.assertEqual(["neededExperienceLevel", "rarity"], names)
        self.assertEqual("0", out["units"][0]["balanceOverrides"][0]["value"])

    def test_normalize_dedupes_and_sorts_stage_units(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        uid = pack["units"][0]["id"]
        pack["stages"][0]["unitIds"] = [uid, uid]
        out = NORMALIZE.normalize_pack(pack)
        self.assertEqual([uid], out["stages"][0]["unitIds"])

    def test_canonical_roundtrip_is_stable(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        once = NORMALIZE.canonical_json(pack)
        twice = NORMALIZE.canonical_json(NORMALIZE.normalize_pack(pack))
        self.assertEqual(once, twice)

    def test_is_canonical_flags_scaffold(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        normalized = NORMALIZE.normalize_pack(pack)
        self.assertTrue(NORMALIZE.is_canonical(normalized))

    def test_empty_pack_gets_skeleton(self) -> None:
        out = NORMALIZE.normalize_pack({})
        self.assertEqual([], out["units"])
        self.assertEqual([], out["stages"])

    def test_real_pack_normalizes_without_error(self) -> None:
        pack = json.loads(SAMPLE_NATIVE_PATH.read_text(encoding="utf-8"))
        out = NORMALIZE.normalize_pack(pack)
        self.assertEqual(9, len(out["units"]))
        self.assertEqual(3, len(out["stages"]))


if __name__ == "__main__":
    unittest.main()
