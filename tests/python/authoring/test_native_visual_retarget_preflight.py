"""Serialized bundle-side checks for native visual retarget mappings."""

from __future__ import annotations

import json
import unittest
from pathlib import Path
from typing import Any

from tests.python.helpers import REPO_ROOT, load_tool_module


PREFLIGHT = load_tool_module("validate_native_visual_roots")
PACK_ROOT = REPO_ROOT / "content/packs/sample-native-expansion"
GAME_ROOT = Path(r"C:\Program Files (x86)\Steam\steamapps\common\Rogue Command")


class NativeVisualRetargetPreflightTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        pack = json.loads((PACK_ROOT / "content.json").read_text(encoding="utf-8-sig"))
        cls.unit = next(unit for unit in pack["units"] if unit.get("nativeRigRetarget"))
        bundle_entry = next(
            entry for entry in pack["assetBundles"] if entry["id"] == cls.unit["assetBundleId"]
        )
        bundle_path = PACK_ROOT / bundle_entry["path"]
        if not bundle_path.is_file():
            raise unittest.SkipTest(f"sample-native bundle is missing: {bundle_path}")
        cls.hierarchy = PREFLIGHT.BundleHierarchy(bundle_path)
        cls.prefab_root = cls.hierarchy.resolve_prefab_root(cls.unit["visualAssetName"])
        resources_path = GAME_ROOT / "Rogue Command_Data/resources.assets"
        if not resources_path.is_file():
            raise unittest.SkipTest(f"pinned Rogue Command resources are missing: {resources_path}")
        cls.native_hierarchy = PREFLIGHT.NativeHierarchy(PREFLIGHT.verify_identity(GAME_ROOT))
        cls.native_root = cls.native_hierarchy.resolve_template_root(cls.unit["nativeTemplateId"])
        cls.native_visual_root = cls.native_hierarchy.resolve_path(
            cls.native_root, cls.unit["visualRootPath"], "sample-native visualRootPath"
        )

    def test_every_ironclad_retarget_source_and_target_resolves_in_its_declared_root(self) -> None:
        mappings: list[dict[str, Any]] = self.unit["nativeRigRetarget"]["mappings"]
        self.assertEqual(len(mappings), 8)
        resolved_targets: set[str] = set()
        for index, mapping in enumerate(mappings):
            with self.subTest(mapping=index, source=mapping["sourcePath"], target=mapping["targetPath"]):
                self.native_hierarchy.resolve_path(
                    self.native_root,
                    mapping["sourcePath"],
                    f"sample-native:ironclad retarget[{index}] sourcePath",
                )
                self.hierarchy.resolve_path(
                    self.prefab_root,
                    mapping["targetPath"],
                    f"sample-native:ironclad retarget[{index}] targetPath",
                )
                self.assertFalse(
                    mapping["sourcePath"] == self.unit["visualRootPath"]
                    or self.unit["visualRootPath"].startswith(mapping["sourcePath"] + "/")
                )
                self.assertNotIn(mapping["targetPath"], resolved_targets)
                resolved_targets.add(mapping["targetPath"])

    def test_native_and_bundle_retarget_paths_require_exact_relative_segments(self) -> None:
        for path in ("../Bone", "IroncladWalkerModel//Bone", "IroncladWalkerModel\\Bone"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                self.hierarchy.resolve_path(self.prefab_root, path, "test targetPath")
            with self.subTest(path=path), self.assertRaises(ValueError):
                self.native_hierarchy.resolve_path(self.native_root, path, "test sourcePath")

    def test_bundle_file_resolution_rejects_pack_escape_paths(self) -> None:
        for path in ("../outside.bundle", "bundles\\outside.bundle", "C:/outside.bundle"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                PREFLIGHT.safe_pack_file(PACK_ROOT, path)


if __name__ == "__main__":
    unittest.main()
