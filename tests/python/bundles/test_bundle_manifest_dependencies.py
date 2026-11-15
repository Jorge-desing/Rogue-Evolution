from types import SimpleNamespace
import unittest
from unittest.mock import patch
import tempfile
from pathlib import Path

from tools.bundle_manifest_dependencies import merge_bundle_manifests, resolve_bundle_dependencies
from tools.pack_bundle_transfer import copy_bundle_transfer, plan_bundle_transfer


class BundleManifestDependencyTests(unittest.TestCase):
    def test_merge_real_manifests_preserves_names_and_rejects_duplicates(self):
        root = Path(__file__).resolve().parents[3] / "content/packs"
        first = root / "sample-native-expansion/bundles/assetbundle-manifest.bundle"
        second = root / "tactical-operations/bundles/assetbundle-manifest.bundle"
        with tempfile.TemporaryDirectory() as directory:
            merged = Path(directory) / "merged.bundle"
            merged.write_bytes(merge_bundle_manifests([first, second]))
            self.assertEqual({"sample-native-expansion.bundle", "tactical-operations.bundle"},
                             set(resolve_bundle_dependencies(merged, ["sample-native-expansion.bundle", "tactical-operations.bundle"], require_exact=True)))
            with self.assertRaises(ValueError):
                merge_bundle_manifests([first, first])

    def test_merge_preserves_dependency_edges_after_index_remapping(self):
        import copy
        import UnityPy
        root = Path(__file__).resolve().parents[3] / "content/packs"
        first = root / "sample-native-expansion/bundles/assetbundle-manifest.bundle"
        second = root / "tactical-operations/bundles/assetbundle-manifest.bundle"
        env = UnityPy.load(first.read_bytes())
        obj = next(obj for obj in env.objects if obj.type.name == "AssetBundleManifest")
        tree = obj.read_typetree()
        original_index = tree["AssetBundleNames"][0][0]
        shared_index = 55
        shared_info = copy.deepcopy(tree["AssetBundleInfos"][0][1])
        tree["AssetBundleNames"].append((shared_index, "shared.bundle"))
        tree["AssetBundleInfos"].append((shared_index, shared_info))
        tree["AssetBundleInfos"][0][1]["AssetBundleDependencies"] = [shared_index]
        obj.save_typetree(tree)
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "source.bundle"
            merged = Path(directory) / "merged.bundle"
            source.write_bytes(env.file.save())
            merged.write_bytes(merge_bundle_manifests([source, second]))
            self.assertEqual(["shared.bundle", "sample-native-expansion.bundle"],
                             resolve_bundle_dependencies(merged, ["sample-native-expansion.bundle"]))

    def test_transitive_order_and_missing_dependency(self):
        tree = {"AssetBundleNames": [(0, "unit.bundle"), (1, "shared.bundle"), (2, "shader.bundle")],
                "AssetBundleInfos": [(0, {"AssetBundleDependencies": [1]}),
                                     (1, {"AssetBundleDependencies": [2]}),
                                     (2, {"AssetBundleDependencies": []})]}
        obj = SimpleNamespace(type=SimpleNamespace(name="AssetBundleManifest"), read_typetree=lambda: tree)
        with patch("pathlib.Path.read_bytes", return_value=b"fixture"), patch("UnityPy.load", return_value=SimpleNamespace(objects=[obj])):
            self.assertEqual(["shader.bundle", "shared.bundle", "unit.bundle"],
                             resolve_bundle_dependencies("fixture", ["unit.bundle"]))
            with self.assertRaises(ValueError):
                resolve_bundle_dependencies("fixture", ["unknown.bundle"])
            with self.assertRaises(ValueError):
                resolve_bundle_dependencies("fixture", ["unit.bundle"], require_exact=True)
            tree["AssetBundleInfos"][2][1]["AssetBundleDependencies"] = [0]
            with self.assertRaises(ValueError):
                resolve_bundle_dependencies("fixture", ["unit.bundle"])

    def test_transfer_plan_checks_entire_manifest_and_destination_hash(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "source"
            target = Path(directory) / "target"
            (source / "bundles").mkdir(parents=True)
            (target / "bundles").mkdir(parents=True)
            (source / "bundles/a.bundle").write_bytes(b"bundle")
            (source / "bundles/manifest.bundle").write_bytes(b"manifest")
            pack = {"assetBundleManifestPath": "bundles/manifest.bundle",
                    "assetBundles": [{"id": "ns:a", "path": "bundles/a.bundle"}]}
            with patch("tools.pack_bundle_transfer.resolve_bundle_dependencies") as resolve:
                plan = plan_bundle_transfer(source, target, pack)
                resolve.assert_called_once_with(b"manifest", ["a.bundle"], require_exact=True)
                self.assertEqual(2, len(plan))
                self.assertTrue(all(not row["alreadyPresent"] for row in plan))
                copy_bundle_transfer(source, target, pack)
                self.assertEqual(b"bundle", (target / "bundles/a.bundle").read_bytes())
                self.assertTrue(all(row["alreadyPresent"] for row in plan_bundle_transfer(source, target, pack)))
                (target / "bundles/a.bundle").write_bytes(b"different")
                with self.assertRaises(ValueError):
                    plan_bundle_transfer(source, target, pack)
                pack["assetBundles"][0]["path"] = "../outside.bundle"
                with self.assertRaises(ValueError):
                    plan_bundle_transfer(source, target, pack)

    def test_failed_copy_reports_and_preserves_incomplete_file(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source, target = root / "source", root / "target"
            (source / "bundles").mkdir(parents=True)
            target.mkdir()
            (source / "bundles/a.bundle").write_bytes(b"bundle")
            (source / "bundles/manifest.bundle").write_bytes(b"manifest")
            pack = {"assetBundleManifestPath": "bundles/manifest.bundle",
                    "assetBundles": [{"id": "ns:a", "path": "bundles/a.bundle"}]}
            with patch("tools.pack_bundle_transfer.resolve_bundle_dependencies"), patch("tools.pack_bundle_transfer.os.fsync", side_effect=OSError("simulated")):
                with self.assertRaisesRegex(RuntimeError, "Reversión incompleta"):
                    copy_bundle_transfer(source, target, pack)
            self.assertEqual(b"bundle", (target / "bundles/a.bundle").read_bytes())


if __name__ == "__main__":
    unittest.main()
