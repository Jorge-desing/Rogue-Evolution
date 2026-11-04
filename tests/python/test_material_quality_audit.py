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
                        "nodes": [{"name": "colorize_metallic"}, {"name": "normal_map_2"}],
                        "connections": [
                            {"from": "colorize_metallic", "to": "Material", "to_port": 1}
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
                        "singleSubmeshForPaint": True,
                        "productionMeshUntouched": True,
                    }
                ),
                encoding="utf-8",
            )

            report = AUDIT.build_quality_report(root, prefix)

            self.assertTrue(report["qualityGate"]["passed"])
            self.assertEqual([8, 8], report["maps"]["Albedo"]["size"])
            self.assertEqual(0, report["maps"]["MetallicGloss"]["rgbMaxDelta"])
            self.assertLess(report["maps"]["Normal"]["normalLengthMeanError"], 0.01)
            self.assertTrue(report["procedural"]["bimodalMetallicConnected"])
            self.assertTrue(report["threeDPaint"]["singleSubmeshForPaint"])
            self.assertTrue(report["threeDPaint"]["productionMeshUntouched"])

    def test_audit_fails_when_a_required_map_is_missing(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            Image.new("RGB", (4, 4), (80, 80, 80)).save(root / "Missing_Albedo.png")
            with self.assertRaises(FileNotFoundError):
                AUDIT.build_quality_report(root, "Missing")


if __name__ == "__main__":
    unittest.main()
