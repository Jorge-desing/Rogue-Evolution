"""Tests del validador de balance."""

from __future__ import annotations

import unittest

from tests.python.helpers import load_tool_module

BALANCE = load_tool_module("validate_balance_overrides")
SCAFFOLD = load_tool_module("scaffold_pack")


class ValidateBalanceTests(unittest.TestCase):
    def _pack(self) -> dict:
        return SCAFFOLD.generate_scaffold("ns:pack", "alpha")

    def test_valid_scaffold_passes(self) -> None:
        issues = BALANCE.validate_balance(self._pack())
        self.assertEqual([], issues)

    def test_rejects_global_only_mana_recharge(self) -> None:
        pack = self._pack()
        pack["units"][0]["customBalance"] = {"ManaRechargePerSecond": 5}
        issues = BALANCE.validate_balance(pack)
        self.assertTrue(any("global" in i for i in issues))

    def test_rejects_unknown_changeable(self) -> None:
        pack = self._pack()
        pack["units"][0]["customBalance"] = {"FlyingSpeed": 10}
        issues = BALANCE.validate_balance(pack)
        self.assertTrue(any("ChangeableValue" in i for i in issues))

    def test_int_over_single_widening_cap(self) -> None:
        pack = self._pack()
        pack["units"][0]["customBalance"] = {"MaxHealth": 20_000_000}
        issues = BALANCE.validate_balance(pack)
        self.assertTrue(any("16.777.216" in i for i in issues))

    def test_rejects_unknown_raw_override(self) -> None:
        pack = self._pack()
        pack["units"][0]["balanceOverrides"] = [{"name": "invisibility", "value": "1"}]
        issues = BALANCE.validate_balance(pack)
        self.assertTrue(any("invisibility" in i for i in issues))

    def test_rarity_enum_validated(self) -> None:
        pack = self._pack()
        pack["units"][0]["balanceOverrides"] = [{"name": "rarity", "value": "Legendary"}]
        issues = BALANCE.validate_balance(pack)
        self.assertTrue(any("rarity" in i for i in issues))

        pack2 = self._pack()
        pack2["units"][0]["balanceOverrides"] = [{"name": "rarity", "value": "Rare"}]
        issues2 = BALANCE.validate_balance(pack2)
        self.assertFalse(any("rarity" in i for i in issues2))


if __name__ == "__main__":
    unittest.main()
