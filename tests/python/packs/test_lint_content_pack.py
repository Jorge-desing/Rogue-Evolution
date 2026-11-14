"""Tests del linter de content packs."""
from __future__ import annotations
import unittest
from tests.python.helpers import load_tool_module

LINT = load_tool_module("lint_content_pack")
SCAFFOLD = load_tool_module("scaffold_pack")

def _valid_pack() -> dict:
    return SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")


class LintContentPackTests(unittest.TestCase):
    def test_valid_pack_no_warnings(self) -> None:
        pack = _valid_pack()
        warnings = LINT.lint_pack(pack)
        # scaffold is bilingual en-US + es-XL; no warnings expected
        self.assertEqual([], warnings)

    def test_missing_spanish_warns(self) -> None:
        pack = _valid_pack()
        unit = pack["units"][0]
        unit["localizations"] = [
            loc for loc in unit["localizations"] if loc["language"] != "es-XL"
        ]
        warnings = LINT.lint_pack(pack)
        self.assertTrue(any("es-XL" in w or "español" in w.lower() for w in warnings))

    def test_factory_id_in_unit_entries_warns(self) -> None:
        pack = _valid_pack()
        factory_id = pack["units"][0]["productionBuilding"]["id"]
        pack["aiProfiles"][0]["unitEntries"].append(
            {"unitId": factory_id, "baseIndex": 0,
             "startingInAscensionLevel": 0, "countAtStart": 1, "maxLiving": 1}
        )
        warnings = LINT.lint_pack(pack)
        self.assertTrue(any(factory_id in w for w in warnings))

    def test_map_size_inverted_warns(self) -> None:
        pack = _valid_pack()
        pack["stages"][0]["mapSizeMin"] = 50
        pack["stages"][0]["mapSizeMax"] = 20
        warnings = LINT.lint_pack(pack)
        self.assertTrue(any("mapSize" in w for w in warnings))

    def test_empty_pack_no_warnings(self) -> None:
        self.assertEqual([], LINT.lint_pack({}))


if __name__ == "__main__":
    unittest.main()
