"""Regression contracts for preserving authored PBR materials at runtime."""

from __future__ import annotations

import unittest

from tests.python.helpers import REPO_ROOT as ROOT

LOADER_PATH = ROOT / "src" / "RogueCommandExpansionLoader" / "Content" / "ContentPackRuntimeLoader.cs"


class RuntimeMaterialBindingContractTests(unittest.TestCase):
    def test_pbr_fallback_is_considered_before_urp_lit(self) -> None:
        source = LOADER_PATH.read_text(encoding="utf-8")
        method_start = source.index("private static Material EnsureMaterialSupported")
        method_end = source.index("private static void CopyLitProperties", method_start)
        method = source[method_start:method_end]

        pbr_call = method.index('FindNativeShader("RogueCommand/VehiclePBR_VertexWear")')
        urp_call = method.index('FindNativeShader("Universal Render Pipeline/Lit")')
        self.assertLess(
            pbr_call,
            urp_call,
            "A PBR-authored material must try the self-contained VehiclePBR shader before the generic URP/Lit fallback.",
        )


if __name__ == "__main__":
    unittest.main()
