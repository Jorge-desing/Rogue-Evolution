"""Tests de las consultas de etapas y perfiles AI."""

from __future__ import annotations

import json
import unittest

from tests.python.helpers import SAMPLE_NATIVE_PATH, load_tool_module

QUERY = load_tool_module("query_content_packs")
SCAFFOLD = load_tool_module("scaffold_pack")


class QueryStagesAndProfilesTests(unittest.TestCase):
    def test_query_stages(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        rows = QUERY.query_stages(pack)
        self.assertEqual(1, len(rows))
        self.assertEqual(pack["stages"][0]["id"], rows[0]["stageId"])
        self.assertEqual(1, rows[0]["unitCount"])
        self.assertEqual(1, rows[0]["profileCount"])

    def test_query_stages_filter_by_unit(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        self.assertEqual(1, len(QUERY.query_stages(pack, unit_id="ns:alpha")))
        self.assertEqual([], QUERY.query_stages(pack, unit_id="ns:unknown"))

    def test_query_ai_profiles(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        rows = QUERY.query_ai_profiles(pack)
        self.assertEqual(1, len(rows))
        self.assertEqual(["sample:scheduled-base-build"], rows[0]["moduleIds"])
        self.assertEqual(1, rows[0]["unitEntryCount"])

    def test_empty_pack(self) -> None:
        self.assertEqual([], QUERY.query_stages({}))
        self.assertEqual([], QUERY.query_ai_profiles({}))

    def test_real_pack(self) -> None:
        pack = json.loads(SAMPLE_NATIVE_PATH.read_text(encoding="utf-8"))
        self.assertEqual(3, len(QUERY.query_stages(pack)))
        self.assertEqual(3, len(QUERY.query_ai_profiles(pack)))


if __name__ == "__main__":
    unittest.main()
