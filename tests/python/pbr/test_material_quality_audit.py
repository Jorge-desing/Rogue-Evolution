"""Tests for Material Maker URP, 3D-paint, and procedural quality metrics."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from PIL import Image

from tests.python.helpers import load_tool_module

AUDIT = load_tool_module("audit_material_maker_output")


class MaterialQualityAuditTests(unittest.TestCase):
    def test_audit_reports_map_contract_and_procedural_graph_metrics(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            prefix = "QualityUnit"
            size = (8, 8)
            Image.new("RGB", size, (96, 112, 128)).save(root / f"{prefix}_Albedo.png")
            Image.new("RGBA", size, (128, 128, 128, 220)).save(root / f"{prefix}_MetallicGloss.png")
            Image.new("RGB", size, (128, 128, 255)).save(root / f"{prefix}_Normal.png")
            Image.new("RGB", size, (0, 0, 0)).save(root / f"{prefix}_Emission.png")
            (root / "QualityUnit.ptex").write_text(
                json.dumps(
                    {
                        "rogueCommandQuality": {"profile": "tactical-hd-v2"},
                        "nodes": [
                            {"name": "Material", "parameters": {"emission_energy": 1.5}},
                            {"name": "colorize_metallic"},
                            {"name": "colorize_emission"},
                            {"name": "edge_detect_emission"},
                            {"name": "normal_map_2"},
                            {"name": "micro_perlin"},
                            {"name": "micro_detail_blend"},
                            {"name": "micro_roughness_blend"},
                        ],
                        "connections": [
                            {"from": "colorize_metallic", "to": "Material", "to_port": 1},
                            {"from": "edge_detect_emission", "to": "colorize_emission", "to_port": 0},
                            {"from": "colorize_emission", "to": "Material", "to_port": 3},
                            {"from": "micro_detail_blend", "to": "normal_map_2", "to_port": 0},
                            {"from": "micro_roughness_blend", "to": "Material", "to_port": 2},
                        ],
                    }
                ),
                encoding="utf-8",
            )
            (root / "paint-workspace.json").write_text(
                json.dumps(
                    {
                        "workflow": "three_d",
                        "requiresUv": True,
                        "uvFrozen": True,
                        "paintProxyRequired": False,
                        "singleSubmeshForPaint": True,
                        "productionMeshUntouched": True,
                    }
                ),
                encoding="utf-8",
            )

            report = AUDIT.build_quality_report(root, prefix, workflow_name="three_d")

            self.assertTrue(report["qualityGate"]["passed"])
            self.assertEqual("three_d", report["workflow"])
            self.assertEqual([8, 8], report["maps"]["Albedo"]["size"])
            self.assertEqual(0, report["maps"]["MetallicGloss"]["rgbMaxDelta"])
            self.assertLess(report["maps"]["Normal"]["normalLengthMeanError"], 0.01)
            self.assertTrue(report["procedural"]["bimodalMetallicConnected"])
            self.assertTrue(report["procedural"]["emissionConnected"])
            self.assertTrue(report["procedural"]["edgeEmissionConnected"])
            self.assertTrue(report["procedural"]["microDetailConnected"])
            self.assertTrue(report["procedural"]["microRoughnessConnected"])
            self.assertEqual("tactical-hd-v2", report["procedural"]["qualityProfile"])
            self.assertGreater(report["procedural"]["emissionEnergy"], 0.0)
            self.assertTrue(report["threeDPaint"]["singleSubmeshForPaint"])
            self.assertTrue(report["threeDPaint"]["uvFrozen"])
            self.assertTrue(report["threeDPaint"]["productionMeshUntouched"])

    def test_audit_fails_when_a_required_map_is_missing(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            Image.new("RGB", (4, 4), (80, 80, 80)).save(root / "Missing_Albedo.png")
            with self.assertRaises(FileNotFoundError):
                AUDIT.build_quality_report(root, "Missing")

    def test_audit_rejects_dark_albedo_and_unconnected_v3_plate_variation(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            prefix = "Crushed"
            Image.new("RGB", (16, 16), (40, 42, 46)).save(root / f"{prefix}_Albedo.png")
            Image.new("RGBA", (16, 16), (255, 255, 255, 190)).save(root / f"{prefix}_MetallicGloss.png")
            Image.new("RGB", (16, 16), (128, 128, 255)).save(root / f"{prefix}_Normal.png")
            Image.new("RGB", (16, 16), (0, 0, 0)).save(root / f"{prefix}_Emission.png")
            (root / f"{prefix}.ptex").write_text(
                json.dumps(
                    {
                        "rogueCommandQuality": {"profile": "tactical-hd-v2", "schemaVersion": 3},
                        "nodes": [
                            {"name": "Material", "parameters": {"emission_energy": 2.0}},
                            {"name": "colorize_metallic"},
                            {"name": "colorize_emission"},
                            {"name": "edge_detect_emission"},
                            {"name": "micro_perlin"},
                            {"name": "normal_map_2"},
                        ],
                        "connections": [
                            {"from": "colorize_metallic", "to": "Material", "to_port": 1},
                            {"from": "colorize_emission", "to": "Material", "to_port": 5},
                            {"from": "edge_detect_emission", "to": "colorize_emission", "to_port": 0},
                            {"from": "micro_perlin", "to": "micro_detail_blend", "to_port": 0},
                            {"from": "micro_perlin", "to": "micro_roughness_blend", "to_port": 0},
                        ],
                    }
                ),
                encoding="utf-8",
            )

            report = AUDIT.build_quality_report(root, prefix)
            self.assertFalse(report["qualityGate"]["passed"])
            self.assertLess(report["maps"]["Albedo"]["luminanceMean"], 72.0)
            self.assertIn("lowFreqBlotchStd", report["maps"]["Albedo"])
            self.assertIn("albedo-too-dark-for-urp", report["qualityGate"]["failures"])
            self.assertIn(
                "procedural-plate-variation-graph-not-connected",
                report["qualityGate"]["failures"],
            )


if __name__ == "__main__":
    unittest.main()
