import unittest

from tools.bepinex_cli import _parse_cli_value


class BepInExCliTests(unittest.TestCase):
    def test_parse_cli_value_handles_numbers_bools_json_and_strings(self) -> None:
        self.assertEqual(_parse_cli_value("42"), 42)
        self.assertEqual(_parse_cli_value("-7"), -7)
        self.assertEqual(_parse_cli_value("+15"), 15)
        self.assertAlmostEqual(_parse_cli_value("-3.5"), -3.5)
        self.assertIs(_parse_cli_value("true"), True)
        self.assertIs(_parse_cli_value("FALSE"), False)
        self.assertEqual(_parse_cli_value('{"a": 1}'), {"a": 1})
        self.assertEqual(_parse_cli_value("[1, 2]"), [1, 2])
        self.assertEqual(_parse_cli_value("NaN"), "NaN")
        self.assertEqual(_parse_cli_value("inf"), "inf")
        self.assertEqual(_parse_cli_value("hello"), "hello")


if __name__ == "__main__":
    unittest.main()
