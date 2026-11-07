"""Unit tests for the Unit-to-PBR Preset Registry and integration tooling."""

from __future__ import annotations

import json
import re
import tempfile
import unittest
from pathlib import Path

from tests.python.helpers import load_tool_module

REGISTRY = load_tool_module("UnitPbrPresetRegistry")
APPLY_TOOL = load_tool_module("Apply-UnitPbrPresets")
TEMPLATE_TOOL = load_tool_module("Prepare-MaterialMakerTacticalTemplate")


class UnitPbrPresetRegistryTests(unittest.TestCase):
    def test_all_44_presets_mapped_with_zero_gaps(self) -> None:
        is_valid, unmapped, mapped = REGISTRY.verify_all_presets_mapped()
        self.assertTrue(is_valid, f"Unmapped presets found: {unmapped}")
        self.assertEqual([], unmapped)
        self.assertEqual(44, len(mapped))

    def test_all_19_units_present_and_configured(self) -> None:
        self.assertEqual(19, len(REGISTRY.UNIT_PBR_REGISTRY))

        hex_pattern = re.compile(r"^#[0-9a-fA-F]{6}$")

        for unit_id, profile in REGISTRY.UNIT_PBR_REGISTRY.items():
            self.assertEqual(unit_id, profile.unit_id)
            self.assertIn(profile.pack_id, {"sample-expansion", "sample-native-expansion", "tactical-operations"})
            self.assertTrue(profile.model_name)
            self.assertIn(profile.primary_preset, TEMPLATE_TOOL.PRESETS)
            self.assertTrue(hex_pattern.match(profile.primary_color_tint), f"Invalid primary tint {profile.primary_color_tint} for {unit_id}")

            for var_key, var_preset in profile.tactical_variant_presets.items():
                self.assertIn(var_preset, TEMPLATE_TOOL.PRESETS, f"Invalid variant preset {var_preset} in {unit_id}")
                if var_key in profile.variant_color_tints:
                    tint = profile.variant_color_tints[var_key]
                    self.assertTrue(hex_pattern.match(tint), f"Invalid tint {tint} for {unit_id} variant {var_key}")

    def test_pack_unit_counts(self) -> None:
        sample_units = REGISTRY.get_pack_units("sample-expansion")
        native_units = REGISTRY.get_pack_units("sample-native-expansion")
        tactical_units = REGISTRY.get_pack_units("tactical-operations")

        self.assertEqual(1, len(sample_units))
        self.assertEqual(9, len(native_units))
        self.assertEqual(9, len(tactical_units))

    def test_query_units_by_preset(self) -> None:
        matches = REGISTRY.get_units_for_preset("gravitas_singularity_chassis")
        self.assertTrue(any(u == "tactical-operations:gravitas-breaker" and r == "primary" for u, r in matches))

        matches_cermet = REGISTRY.get_units_for_preset("hypervelocity_railgun_cermet")
        self.assertTrue(any(u == "sample-native:heavy-artillery" and r == "primary" for u, r in matches_cermet))

    def test_prepare_unit_templates_generates_valid_ptex(self) -> None:
        profile = REGISTRY.get_unit_profile("tactical-operations:gravitas-breaker")
        self.assertIsNotNone(profile)

        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            generated = APPLY_TOOL.prepare_unit_templates(
                profile, size_power=10, include_variants=True, output_base=tmp_path
            )
            # 1 primary + 2 variants = 3 templates
            self.assertEqual(3, len(generated))
            for ptex in generated:
                self.assertTrue(ptex.is_file())
                data = json.loads(ptex.read_text(encoding="utf-8"))
                self.assertIn("nodes", data)
                self.assertIn("connections", data)
                node_names = [n.get("name") for n in data["nodes"]]
                self.assertIn("Material", node_names)
                self.assertIn("colorize_metallic", node_names)

    def test_audit_function_exits_clean(self) -> None:
        exit_code = APPLY_TOOL.audit_presets()
        self.assertEqual(0, exit_code)

    def test_export_integration_matrix_json(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            out_file = Path(tmp) / "matrix.json"
            APPLY_TOOL.export_integration_matrix_json(out_file)
            self.assertTrue(out_file.is_file())
            data = json.loads(out_file.read_text(encoding="utf-8"))
            self.assertEqual(19, data["totalUnits"])
            self.assertEqual(44, data["totalPresets"])
            self.assertIn("sample:scout-tank", data["units"])
            self.assertIn("tactical-operations:gravitas-breaker", data["units"])


if __name__ == "__main__":
    unittest.main()
