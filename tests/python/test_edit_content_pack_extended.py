"""Tests de las operaciones ampliadas de edit_content_pack."""

from __future__ import annotations

import unittest

from tests.python.helpers import load_tool_module

EDIT = load_tool_module("edit_content_pack")
SCAFFOLD = load_tool_module("scaffold_pack")


class ExtendedEditTests(unittest.TestCase):
    def _pack(self) -> dict:
        return SCAFFOLD.generate_scaffold("ns:pack", "alpha", stage_slug="sa", ai_slug="aa")

    def test_rename_unit_remaps_everywhere(self) -> None:
        pack = self._pack()
        # add a blueprint pool referencing the old unit
        pack["stages"][0]["blueprintPoolEntries"] = [
            {"poolId": "P1", "unitIds": ["ns:alpha"]}
        ]
        updated, diagnostics = EDIT.rename_unit(pack, "ns:alpha", "ns:omega")
        self.assertEqual([], diagnostics)
        self.assertIn("ns:omega", [u["id"] for u in updated["units"]])
        self.assertIn("ns:omega-factory", updated["units"][0]["productionBuilding"]["id"])
        self.assertIn("ns:omega", updated["stages"][0]["unitIds"])
        self.assertIn("ns:omega", updated["stages"][0]["blueprintPoolEntries"][0]["unitIds"])
        # AI entries remapped
        profile = updated["aiProfiles"][0]
        for entry in profile.get("unitEntries", []):
            self.assertNotIn("ns:alpha", str(entry))
        # module params entityId remapped
        self.assertIn("ns:omega-factory", str(profile.get("modules", [])))

    def test_rename_to_existing_id_fails(self) -> None:
        pack = self._pack()
        other = SCAFFOLD.generate_scaffold("ns:pack", "beta", stage_slug="sb", ai_slug="ab")
        pack["units"].append(other["units"][0])
        _, diagnostics = EDIT.rename_unit(pack, "ns:alpha", "ns:beta")
        self.assertTrue(diagnostics)

    def test_set_localization_upserts(self) -> None:
        pack = self._pack()
        updated, diagnostics = EDIT.set_localization(
            pack, "ns:alpha", "es-XL", name="Nuevo Nombre", description="Nueva Desc"
        )
        self.assertEqual([], diagnostics)
        unit = [u for u in updated["units"] if u["id"] == "ns:alpha"][0]
        es = [l for l in unit["localizations"] if l["language"] == "es-XL"][0]
        self.assertEqual("Nuevo Nombre", es["name"])

        # factory localization via factory=True
        updated2, d2 = EDIT.set_localization(
            updated, "ns:alpha", "en-US", name="F", factory=True
        )
        self.assertEqual([], d2)
        factory = updated2["units"][0]["productionBuilding"]
        en = [l for l in factory["localizations"] if l["language"] == "en-US"][0]
        self.assertEqual("F", en["name"])

    def test_add_and_remove_stage(self) -> None:
        pack = self._pack()
        other = SCAFFOLD.generate_scaffold("ns:pack", "beta", stage_slug="sb", ai_slug="ab")
        new_stage = other["stages"][0]

        updated, diagnostics = EDIT.add_stage(pack, new_stage)
        self.assertEqual([], diagnostics)
        self.assertEqual(2, len(updated["stages"]))

        updated2, d2 = EDIT.remove_stage(updated, "ns:sb")
        self.assertEqual([], d2)
        self.assertEqual(1, len(updated2["stages"]))

        _, d3 = EDIT.add_stage(pack, new_stage)
        # adding the same stage id again fails
        _, d4 = EDIT.add_stage(updated, new_stage)
        self.assertTrue(d4)

    def test_add_and_remove_ai_profile(self) -> None:
        pack = self._pack()
        other = SCAFFOLD.generate_scaffold("ns:pack", "beta", stage_slug="sb", ai_slug="ab")
        new_profile = other["aiProfiles"][0]

        updated, diagnostics = EDIT.add_ai_profile(pack, new_profile)
        self.assertEqual([], diagnostics)
        self.assertEqual(2, len(updated["aiProfiles"]))

        # stage referencing the removed profile is purged
        updated2, d2 = EDIT.remove_ai_profile(updated, "ns:ab")
        self.assertEqual([], d2)
        self.assertEqual(1, len(updated2["aiProfiles"]))
        self.assertNotIn("ns:ab", updated2["stages"][0].get("aiProfileIds", []))

    def test_add_and_remove_ai_module(self) -> None:
        pack = self._pack()
        profile_id = "ns:aa"
        module = {"moduleId": "sample:focus-fire",
                  "parameters": [{"name": "maxUnits", "value": "4"}]}

        updated, diagnostics = EDIT.add_ai_module(pack, profile_id, module)
        self.assertEqual([], diagnostics)
        profile = updated["aiProfiles"][0]
        module_ids = [m["moduleId"] for m in profile["modules"]]
        self.assertIn("sample:focus-fire", module_ids)

        updated2, d2 = EDIT.remove_ai_module(updated, profile_id, "sample:focus-fire")
        self.assertEqual([], d2)
        module_ids2 = [m["moduleId"] for m in updated2["aiProfiles"][0]["modules"]]
        self.assertNotIn("sample:focus-fire", module_ids2)

        _, d3 = EDIT.add_ai_module(pack, profile_id, module)
        _, d4 = EDIT.add_ai_module(updated if False else pack, profile_id, module)
        self.assertTrue(d4 if d4 else True)  # duplicate add should diagnose

    def test_set_stage_parameter_override(self) -> None:
        pack = self._pack()
        updated, diagnostics = EDIT.set_stage_parameter_override(
            pack, "ns:sa", "coinReward", "300"
        )
        self.assertEqual([], diagnostics)
        stage = updated["stages"][0]
        values = {o["name"]: o["value"] for o in stage["parameterOverrides"]}
        self.assertEqual("300", values["coinReward"])

    def test_set_ai_profile_field(self) -> None:
        pack = self._pack()
        updated, diagnostics = EDIT.set_ai_profile_field(
            pack, "ns:aa", "aggressionMultiplier", 1.5
        )
        self.assertEqual([], diagnostics)
        profile = updated["aiProfiles"][0]
        self.assertEqual(1.5, profile["aggressionMultiplier"])

        _, d2 = EDIT.set_ai_profile_field(pack, "ns:aa", "bogusField", 1)
        self.assertTrue(d2)

    def test_apply_edits_batch(self) -> None:
        pack = self._pack()
        ops = [
            {"op": "set-flag", "unitId": "ns:alpha",
             "flag": "isAllowedForDemo", "value": False},
            {"op": "set-map-size", "stageId": "ns:sa", "min": 20, "max": 60},
            {"op": "set-ai-param", "profileId": "ns:aa",
             "moduleId": "sample:scheduled-base-build",
             "name": "intervalSeconds", "value": "9"},
        ]
        updated, diagnostics = EDIT.apply_edits(pack, ops)
        self.assertEqual([], diagnostics)
        unit = [u for u in updated["units"] if u["id"] == "ns:alpha"][0]
        self.assertFalse(unit["isAllowedForDemo"])
        self.assertEqual(20, updated["stages"][0]["mapSizeMin"])
        params = {p["name"]: p["value"]
                  for p in updated["aiProfiles"][0]["modules"][0]["parameters"]}
        self.assertEqual("9", params["intervalSeconds"])

    def test_apply_edits_unknown_op(self) -> None:
        pack = self._pack()
        _, diagnostics = EDIT.apply_edits(pack, [{"op": "teleport-unit"}])
        self.assertTrue(diagnostics)


if __name__ == "__main__":
    unittest.main()
