import unittest

from tests.python.helpers import load_tool_module


audit_unity_build = load_tool_module("audit_unity_build")


class AuditUnityBuildTests(unittest.TestCase):
    def test_parse_successful_assetbundle_build(self):
        log = """
        Built from '2021.3/respin/2021.3.45f2-0da89fac8e' branch; Version is '2021.3.45f2 (88f88f591b2e)'
        Bundle Name: tactical-operations.bundle
        TARGETED_TACTICAL_BUNDLE_OK: C:/bundles/tactical-operations.bundle bytes=123456
        [Performance] BuildAssetBundles  Total. 16.59 s
        Batchmode quit successfully invoked - shutting down!
        """
        report = audit_unity_build.parse_unity_build_log(
            log,
            bundle_metrics=[
                {
                    "bundle_path": "C:/bundles/tactical-operations.bundle",
                    "bundle_size_bytes": 123456,
                    "quality_gate": {"passed": True, "issues": []},
                }
            ],
        )

        self.assertEqual(report["unity_version"], "2021.3.45f2")
        self.assertTrue(report["build_succeeded"])
        self.assertEqual(report["fatal_error_count"], 0)
        self.assertEqual(report["bundle_count"], 1)
        self.assertEqual(report["build_duration_seconds"], 16.59)
        self.assertTrue(report["quality_gate"]["passed"])

    def test_licensing_error_is_diagnostic_but_not_build_failure(self):
        log = """
        [Licensing::Client] Error: Code 10 while verifying Licensing Client signature
        [Licensing::Module] Error: Access token is unavailable; failed to update
        Built from '2021.3/respin/2021.3.45f2-0da89fac8e' branch; Version is '2021.3.45f2 (88f88f591b2e)'
        TARGETED_TACTICAL_BUNDLE_OK: C:/bundles/tactical-operations.bundle bytes=100
        Batchmode quit successfully invoked - shutting down!
        """
        report = audit_unity_build.parse_unity_build_log(log)

        self.assertTrue(report["build_succeeded"])
        self.assertEqual(report["fatal_error_count"], 0)
        self.assertGreaterEqual(report["licensing_diagnostic_count"], 2)
        self.assertTrue(report["quality_gate"]["passed"])

    def test_summarize_bundle_report_exposes_urp_and_3d_metrics(self):
        summary = audit_unity_build.summarize_bundle_report(
            {
                "bundle_path": "bundle",
                "bundle_size_bytes": 10,
                "object_count": 42,
                "shader_count": 2,
                "shaders": [{"name": ""}, {"name": "Standard"}],
                "texture_count": 4,
                "textures": [{"name": "Albedo", "width": 2048, "height": 2048, "color_space": 1}],
                "material_count": 3,
                "type_counts": {
                    "GameObject": 5,
                    "Transform": 5,
                    "Mesh": 2,
                    "SkinnedMeshRenderer": 1,
                    "Animator": 1,
                    "LODGroup": 1,
                },
            }
        )

        self.assertEqual(summary["urp"]["shader_names_unresolved_count"], 1)
        self.assertEqual(summary["urp"]["texture_dimensions"][0]["width"], 2048)
        self.assertEqual(summary["three_d"]["mesh_count"], 2)
        self.assertEqual(summary["three_d"]["skinned_mesh_renderer_count"], 1)
        self.assertEqual(summary["three_d"]["lod_group_count"], 1)

        report = audit_unity_build.parse_unity_build_log(
            "Version is '2021.3.45f2'\nException: BuildPipeline failed\n"
        )

        self.assertFalse(report["build_succeeded"])
        self.assertEqual(report["fatal_error_count"], 1)
        self.assertFalse(report["quality_gate"]["passed"])
        self.assertIn("fatal-build-error", report["quality_gate"]["issues"])


if __name__ == "__main__":
    unittest.main()
