"""Tests del validador de contrato cruzado del pack."""

from __future__ import annotations

import json
import unittest

from tests.python.helpers import SAMPLE_NATIVE_PATH, load_tool_module

VALIDATE = load_tool_module("validate_pack_contract")
SCAFFOLD = load_tool_module("scaffold_pack")


class ValidatePackContractTests(unittest.TestCase):
    def test_scaffold_passes(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        self.assertEqual([], VALIDATE.validate_contract(pack))

    def test_real_pack_passes(self) -> None:
        pack = json.loads(SAMPLE_NATIVE_PATH.read_text(encoding="utf-8"))
        self.assertEqual([], VALIDATE.validate_contract(pack))

    def test_pack_id_format(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["packId"] = "sin-namespace"
        findings = VALIDATE.validate_contract(pack)
        self.assertTrue(any("packId" in f for f in findings))

    def test_factory_parity_mismatch(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["units"][0]["productionBuilding"]["balanceOverrides"] = [
            {"name": "rarity", "value": "Rare"},
            {"name": "neededExperienceLevel", "value": "0"},
        ]
        findings = VALIDATE.validate_contract(pack)
        self.assertTrue(any("parity" in f or "rarity" in f for f in findings))

    def test_product_must_not_claim_factory_role(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["units"][0]["balanceOverrides"] = [
            {"name": "roles", "value": "Unit, Vehicle, Factory"},
        ]
        findings = VALIDATE.validate_contract(pack)
        self.assertTrue(any("Factory" in f for f in findings))

    def test_card_requires_bundle(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["units"][0]["cardImageAssetName"] = "assets/card.png"
        findings = VALIDATE.validate_contract(pack)
        self.assertTrue(any("assetBundleId" in f for f in findings))

    def test_visual_pairing(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["units"][0]["visualAssetName"] = "assets/visual.prefab"
        findings = VALIDATE.validate_contract(pack)
        self.assertTrue(any("visual" in f.lower() for f in findings))

    def test_stage_map_range(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["stages"][0]["mapSizeMin"] = 50
        pack["stages"][0]["mapSizeMax"] = 10
        findings = VALIDATE.validate_contract(pack)
        self.assertTrue(any("mapSize" in f for f in findings))

    def test_stage_cannot_offer_factory(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        factory_id = pack["units"][0]["productionBuilding"]["id"]
        pack["stages"][0]["unitIds"] = [factory_id]
        findings = VALIDATE.validate_contract(pack)
        self.assertTrue(any("factory" in f.lower() for f in findings))

    def test_bundle_path_format(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack.setdefault("assetBundles", []).append(
            {"id": "ns:assets", "path": "Bundles/UPPER.bundle"}
        )
        findings = VALIDATE.validate_contract(pack)
        self.assertTrue(any("bundle" in f.lower() for f in findings))

    def test_duplicate_id_across_sections(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["stages"][0]["id"] = pack["units"][0]["id"]
        findings = VALIDATE.validate_contract(pack)
        self.assertTrue(any("duplicado" in f.lower() for f in findings))

    def test_unscoped_native_ai_rejected(self) -> None:
        pack = SCAFFOLD.generate_scaffold("ns:pack", "alpha")
        pack["aiProfiles"][0]["scopeToReferencedStages"] = False
        findings = VALIDATE.validate_contract(pack)
        self.assertTrue(any("scopeToReferencedStages" in f for f in findings))

    def test_empty_pack_reports_missing_pack_id(self) -> None:
        findings = VALIDATE.validate_contract({})
        self.assertTrue(any("packId" in f for f in findings))


if __name__ == "__main__":
    unittest.main()
