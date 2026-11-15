"""Tests for Build-CanonicalVisualModules.ps1 and CanonicalVisualBuildProvenance.ps1."""

from __future__ import annotations

import hashlib
import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[3]
BUILD_SCRIPT = REPO_ROOT / "tools" / "Build-CanonicalVisualModules.ps1"
PROVENANCE_SCRIPT = REPO_ROOT / "tools" / "CanonicalVisualBuildProvenance.ps1"
POWERSHELL_EXE = shutil.which("powershell.exe") or shutil.which("powershell") or "powershell.exe"


def _run_ps_file(script_path: Path, args: list[str]) -> subprocess.CompletedProcess[str]:
    cmd = [
        POWERSHELL_EXE,
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        str(script_path),
        *args,
    ]
    return subprocess.run(
        cmd,
        cwd=str(REPO_ROOT),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )


def _run_ps_command(command: str) -> subprocess.CompletedProcess[str]:
    cmd = [
        POWERSHELL_EXE,
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-Command",
        command,
    ]
    return subprocess.run(
        cmd,
        cwd=str(REPO_ROOT),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )


def _sha256_hex(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().lower()


class BuildCanonicalVisualModulesPsTests(unittest.TestCase):
    def test_dry_run_prints_all_planned_phases(self) -> None:
        result = _run_ps_file(BUILD_SCRIPT, ["-DryRun"])
        self.assertEqual(0, result.returncode, msg=result.stdout + "\n" + result.stderr)

        output = result.stdout
        for phase in (
            "level1-textures",
            "blender",
            "textures",
            "unity",
            "unity-audit",
            "staging-and-pack-validation",
            "bundle-promotion",
            "animation-matrix",
        ):
            self.assertRegex(output, rf"(?m)^\s*-\s+{phase}:\s+(executed|cached|skipped)\b")

        self.assertIn("blender: executed", output)
        self.assertIn("textures: executed", output)
        self.assertIn("unity: executed", output)
        self.assertIn("unity-audit: executed", output)
        self.assertIn("staging-and-pack-validation: executed", output)
        self.assertIn("bundle-promotion: executed", output)
        self.assertIn("animation-matrix: executed", output)

    def test_dry_run_reflects_skipped_phases(self) -> None:
        result = _run_ps_file(
            BUILD_SCRIPT,
            [
                "-DryRun",
                "-SkipBlender",
                "-SkipTextures",
                "-NoPromoteBundles",
                "-SkipAnimationMatrix",
            ],
        )
        self.assertEqual(0, result.returncode, msg=result.stdout + "\n" + result.stderr)

        output = result.stdout
        self.assertIn("blender: skipped", output)
        self.assertIn("textures: skipped", output)
        self.assertIn("bundle-promotion: skipped", output)
        self.assertIn("animation-matrix: skipped", output)
        self.assertIn("unity: executed", output)
        self.assertIn("unity-audit: executed", output)
        self.assertIn("staging-and-pack-validation: executed", output)

    def test_invalid_pack_fails_nonzero(self) -> None:
        result = _run_ps_file(BUILD_SCRIPT, ["-DryRun", "-Packs", "non-existent-pack"])
        self.assertNotEqual(0, result.returncode)

    def test_invalid_texture_size_power_fails_nonzero(self) -> None:
        result = _run_ps_file(BUILD_SCRIPT, ["-DryRun", "-TextureSizePower", "99"])
        self.assertNotEqual(0, result.returncode)

    def test_provenance_phase_hash_cache_and_promotion_end_to_end(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            repo = root / "repo"
            validated_root = root / "validated"
            backup_root = root / "backup"
            cache_path = root / "cache" / "canonical-level2-cache.json"

            # Create minimal required inputs for the 'blender' phase in the isolated repo
            required_blender_inputs = [
                "content/packs/sample-native-expansion/content.json",
                "content/packs/tactical-operations/content.json",
                "content/source-art/canonical-visual-module-roles.json",
                "content/source-art/canonical-visual-module-build-policy.json",
                "tools/Build-CanonicalVisualModules.py",
                "tools/CanonicalUnitHardpoints.py",
                "tools/VisualModelAuthoringSupport.py",
                "tools/hard_surface_geometry_presets.py",
                "tools/hard_surface_geometry_presets_v2.py",
                "tools/hard_surface_geometry_presets_functional.py",
                "tools/hard_surface_geometry_presets_detailing.py",
            ]
            for rel in required_blender_inputs:
                file_path = repo / rel
                file_path.parent.mkdir(parents=True, exist_ok=True)
                file_path.write_text(f"fixture:{rel}\n", encoding="utf-8")

            # Create a test pack with existing live bundles and fresh validated bundles
            pack_name = "test-pack"
            manifest_rel = "bundles/test-pack.manifest.bundle"
            bundle_rel = "bundles/test-pack.bundle"

            pack_dir = repo / "content" / "packs" / pack_name
            pack_dir.mkdir(parents=True, exist_ok=True)
            (pack_dir / "content.json").write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "packId": "test:pack",
                        "assetBundleManifestPath": manifest_rel,
                        "assetBundles": [{"path": bundle_rel}],
                    }
                ),
                encoding="utf-8",
            )

            old_manifest_bytes = b"old-manifest-bundle-bytes"
            old_bundle_bytes = b"old-main-bundle-bytes"
            new_manifest_bytes = b"new-manifest-bundle-bytes-v2"
            new_bundle_bytes = b"new-main-bundle-bytes-v2"

            for rel, payload in ((manifest_rel, old_manifest_bytes), (bundle_rel, old_bundle_bytes)):
                target = pack_dir / rel
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(payload)

            for rel, payload in ((manifest_rel, new_manifest_bytes), (bundle_rel, new_bundle_bytes)):
                target = validated_root / pack_name / rel
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(payload)

            expected_output = root / "outputs" / "module.fbx"
            expected_output.parent.mkdir(parents=True, exist_ok=True)
            expected_output.write_bytes(b"fbx-payload")

            empty_output = root / "outputs" / "empty.fbx"
            empty_output.write_bytes(b"")

            ps_script = f"""
$ErrorActionPreference = 'Stop'
. '{PROVENANCE_SCRIPT}'

$hash1 = Get-CanonicalPhaseInputHash -RepositoryRoot '{repo}' -Phase 'blender'
$hash2 = Get-CanonicalPhaseInputHash -RepositoryRoot '{repo}' -Phase 'blender'
$missBefore = Test-CanonicalPhaseCacheHit -CachePath '{cache_path}' -Phase 'blender' -InputHash $hash1 -ExpectedOutputs @('{expected_output}')

Save-CanonicalPhaseCache -CachePath '{cache_path}' -Phase 'blender' -InputHash $hash1 -Metadata @{{ packCount = 2 }}
$hitAfter = Test-CanonicalPhaseCacheHit -CachePath '{cache_path}' -Phase 'blender' -InputHash $hash1 -ExpectedOutputs @('{expected_output}')
$missEmpty = Test-CanonicalPhaseCacheHit -CachePath '{cache_path}' -Phase 'blender' -InputHash $hash1 -ExpectedOutputs @('{empty_output}')
$missWrongHash = Test-CanonicalPhaseCacheHit -CachePath '{cache_path}' -Phase 'blender' -InputHash ('0' * 64) -ExpectedOutputs @('{expected_output}')

# Modify an input and verify hash changes
Set-Content -LiteralPath '{repo / "tools/Build-CanonicalVisualModules.py"}' -Value 'modified' -Encoding UTF8
$hash3 = Get-CanonicalPhaseInputHash -RepositoryRoot '{repo}' -Phase 'blender'
$missAfterChange = Test-CanonicalPhaseCacheHit -CachePath '{cache_path}' -Phase 'blender' -InputHash $hash3 -ExpectedOutputs @('{expected_output}')

$promotion = @(Invoke-CanonicalBundlePromotion -RepositoryRoot '{repo}' -ValidatedPackRoot '{validated_root}' -BackupRoot '{backup_root}' -PackNames @('{pack_name}'))

[ordered]@{{
    hash1 = $hash1
    hash2 = $hash2
    hash3 = $hash3
    missBefore = $missBefore
    hitAfter = $hitAfter
    missEmpty = $missEmpty
    missWrongHash = $missWrongHash
    missAfterChange = $missAfterChange
    promotion = $promotion
}} | ConvertTo-Json -Depth 10
"""
            result = _run_ps_command(ps_script)
            self.assertEqual(0, result.returncode, msg=result.stdout + "\n" + result.stderr)

            payload = json.loads(result.stdout)
            self.assertEqual(64, len(payload["hash1"]))
            self.assertEqual(payload["hash1"], payload["hash2"])
            self.assertNotEqual(payload["hash1"], payload["hash3"])

            self.assertFalse(payload["missBefore"])
            self.assertTrue(payload["hitAfter"])
            self.assertFalse(payload["missEmpty"])
            self.assertFalse(payload["missWrongHash"])
            self.assertFalse(payload["missAfterChange"])

            self.assertTrue(cache_path.is_file())
            cache_json = json.loads(cache_path.read_text(encoding="utf-8-sig"))
            self.assertEqual(payload["hash1"], cache_json["phases"]["blender"]["inputHash"])

            promotion = payload["promotion"]
            self.assertEqual(1, len(promotion))
            summary = promotion[0]
            self.assertEqual(pack_name, summary["packName"])
            self.assertEqual(2, summary["backedUpCount"])
            self.assertEqual(2, summary["promotedCount"])

            backup_manifest_path = Path(summary["backupManifestPath"])
            self.assertTrue(backup_manifest_path.is_file())
            backup_manifest = json.loads(backup_manifest_path.read_text(encoding="utf-8-sig"))
            self.assertEqual(1, backup_manifest["schemaVersion"])
            self.assertEqual(pack_name, backup_manifest["packName"])

            expected_old_hashes = {
                manifest_rel: _sha256_hex(old_manifest_bytes),
                bundle_rel: _sha256_hex(old_bundle_bytes),
            }
            for entry in backup_manifest["bundles"]:
                rel = entry["path"]
                self.assertTrue(entry["existed"])
                self.assertEqual(expected_old_hashes[rel], entry["sha256"])
                self.assertEqual(
                    expected_old_hashes[rel],
                    _sha256_hex(Path(entry["backupPath"]).read_bytes()),
                )

            # Verify live bundles now match the fresh validated bundles
            self.assertEqual(new_manifest_bytes, (pack_dir / manifest_rel).read_bytes())
            self.assertEqual(new_bundle_bytes, (pack_dir / bundle_rel).read_bytes())


if __name__ == "__main__":
    unittest.main()
