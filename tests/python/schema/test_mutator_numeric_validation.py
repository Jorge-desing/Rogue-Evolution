import unittest
from tests.python.helpers import load_tool_module
MOD = load_tool_module("validate_mutators_and_hazards")

class MutatorNumericValidationTests(unittest.TestCase):
    def test_invalid_values_are_critical_and_serializable(self):
        for value in (True, False, None, "bad", float("nan"), float("inf"), -float("inf")):
            with self.subTest(value=value):
                result = MOD.MutatorValidator().validate_mutator_entry({"stat":"MaxHealth", "value":value}, "test")
                self.assertEqual(0, result.score)
                self.assertTrue(any("INVALID_VALUE" in f for f in result.flags))
                self.assertEqual(0, result.value)

    def test_finite_signed_and_zero_additions_remain_valid(self):
        for value in (-5, 0, 3.5, "2.5"):
            with self.subTest(value=value):
                result = MOD.MutatorValidator().validate_mutator_entry({"stat":"MaxHealth", "value":value}, "test")
                self.assertEqual(100, result.score)
                self.assertEqual(float(value), result.value)
