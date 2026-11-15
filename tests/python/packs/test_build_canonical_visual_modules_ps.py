"""Tests for Build-CanonicalVisualModules.ps1, CanonicalVisualBuildProvenance.ps1, and batch audit CLI."""

from __future__ import annotations

import hashlib
import json
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image


REPO_ROOT = Path(__file__).resolve().parents[3]
BUILD_SCRIPT = REPO_ROOT / "tools" / "Build-CanonicalVisualModules.ps1"
PROVENANCE_SCRIPT = REPO_ROOT / "tools" / "CanonicalVisualBuildProvenance.ps1"
POWERSHELL_EXE = shutil.which("powershell.exe") or shutil.which("powershell") or "powershell.exe"

EXPECTED_PHASE_ORDER = [
    "level1-models",
    "level1-textures",
    "blender",
    "textures",
    "unity",
    "unity-audit",
    "staging-and-pack-validation",
    "animation-matrix",
    "bundle-promotion",
    "package-loader",
]

BLENDER_PHASE_REQUIRED_INPUTS = (
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
)

TEXTURES_PHASE_REQUIRED_INPUTS = (
    "content/source-art/canonical-visual-modules.json",
    "tools/Build-CanonicalVisualModuleTextures.ps1",
    "tools/batch_material_maker_textures.py",
    "tools/Run-MaterialMakerBake.ps1",
    "tools/Prepare-MaterialMakerTacticalTemplate.py",
    "tools/pack_material_maker_pbr.py",
    "tools/audit_material_maker_output.py",
    "tools/MaterialMakerWaitDevice.gd",
    "tools/validate_material_maker_config.py",
    "tools/material-maker/rogue-command-material-maker.json",
)

_PHASE_LINE_RE = re.compile(r"^\s*-\s+([a-z0-9-]+):\s+(executed|cached|skipped)\s+\((.*)\)\s*$")


def _ps_quote(value: str | Path) -> str:
    """Wrap a string or path in single quotes safe for inline PowerShell scripts."""
    return "'" + str(value).replace("'", "''") + "'"


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


def _parse_dry_run_phases(output: str) -> list[tuple[str, str, str]]:
    """Extract ordered (name, action, reason) tuples from `-DryRun` stdout."""
    lines = output.splitlines()
    try:
        phases_idx = next(i for i, line in enumerate(lines) if line.strip() == "phases:")
    except StopIteration as exc:
        raise AssertionError(f"DryRun output did not contain 'phases:' header:\n{output}") from exc

    parsed: list[tuple[str, str, str]] = []
    for line in lines[phases_idx + 1 :]:
        match = _PHASE_LINE_RE.match(line)
        if match:
            parsed.append((match.group(1), match.group(2), match.group(3)))
    return parsed


def _write_stub_files(root: Path, relative_paths: tuple[str, ...]) -> None:
    """Create deterministic placeholder files for relative paths in an isolated repo."""
    for rel in relative_paths:
        file_path = root / rel
        if file_path.exists():
            continue
        file_path.parent.mkdir(parents=True, exist_ok=True)
        file_path.write_text(f"fixture:{rel}\n", encoding="utf-8")


def _write_png_pbr_set(
    directory: Path,
    prefix: str,
    *,
    metallic_rgba: tuple[int, int, int, int] = (128, 128, 128, 220),
    normal_rgb: tuple[int, int, int] = (128, 128, 255),
    size: tuple[int, int] = (8, 8),
) -> None:
    """Write a four-map Material Maker PBR PNG set for tests."""
    directory.mkdir(parents=True, exist_ok=True)
    Image.new("RGB", size, (96, 112, 128)).save(directory / f"{prefix}_Albedo.png")
    Image.new("RGBA", size, metallic_rgba).save(directory / f"{prefix}_MetallicGloss.png")
    Image.new("RGB", size, normal_rgb).save(directory / f"{prefix}_Normal.png")
    Image.new("RGB", size, (0, 0, 0)).save(directory / f"{prefix}_Emission.png")


def _setup_isolated_pipeline_repo(root: Path) -> tuple[Path, Path]:
    """Create a self-contained temp repo with copied PS1 scripts and valid Unity cache artifacts."""
    repo = root / "repo"
    cached_artifact = root / "cached-artifact"

    tools_dir = repo / "tools"
    tools_dir.mkdir(parents=True, exist_ok=True)
    shutil.copy2(BUILD_SCRIPT, tools_dir / "Build-CanonicalVisualModules.ps1")
    shutil.copy2(PROVENANCE_SCRIPT, tools_dir / "CanonicalVisualBuildProvenance.ps1")

    manifest_rel = "bundles/sample-native-expansion.manifest.bundle"
    bundle_rel = "bundles/sample-native-expansion.bundle"
    for pack_name in ("sample-native-expansion", "tactical-operations"):
        pack_dir = repo / "content" / "packs" / pack_name
        pack_dir.mkdir(parents=True, exist_ok=True)
        (pack_dir / "content.json").write_text(
            json.dumps(
                {
                    "schemaVersion": 1,
                    "packId": f"test:{pack_name}",
                    "assetBundleManifestPath": f"bundles/{pack_name}.manifest.bundle",
                    "assetBundles": [{"path": f"bundles/{pack_name}.bundle"}],
                }
            ),
            encoding="utf-8",
        )

    fbx_rel = "Assets/RogueCommandExpansionSample/VisualModules/Source/TestModule.fbx"
    source_art_dir = repo / "content" / "source-art"
    source_art_dir.mkdir(parents=True, exist_ok=True)
    (source_art_dir / "canonical-visual-modules.blend").write_bytes(b"blend-payload")
    (source_art_dir / "canonical-visual-modules.json").write_text(
        json.dumps({"modules": [{"preset": "steel", "fbxAssetPath": fbx_rel}]}),
        encoding="utf-8",
    )

    _write_stub_files(repo, BLENDER_PHASE_REQUIRED_INPUTS)
    _write_stub_files(repo, TEXTURES_PHASE_REQUIRED_INPUTS)

    unity_root = repo / "unity" / "RogueCommandAssetAuthoring"
    for subdir in ("Assets", "ProjectSettings", "Packages"):
        (unity_root / subdir).mkdir(parents=True, exist_ok=True)
    (unity_root / "Packages" / "manifest.json").write_text("{}", encoding="utf-8")
    (unity_root / "ProjectSettings" / "ProjectVersion.txt").write_text(
        "m_EditorVersion: 2021.3.45f2\n", encoding="utf-8"
    )
    (unity_root / "Assets" / "UnitMarker.txt").write_text("unit-marker-v1\n", encoding="utf-8")

    fbx_file = unity_root / fbx_rel
    fbx_file.parent.mkdir(parents=True, exist_ok=True)
    fbx_file.write_bytes(b"fbx-payload")

    tex_dir = unity_root / "Assets" / "RogueCommandExpansionSample" / "VisualModules" / "Textures"
    _write_png_pbr_set(tex_dir, "RCLModule_steel")

    audit_path = cached_artifact / "logs" / "unity-audit.json"
    audit_path.parent.mkdir(parents=True, exist_ok=True)
    audit_path.write_text(
        json.dumps(
            {
                "build": {"quality_gate": {"passed": True}},
                "metrics": {"assetbundle_count": 2},
            }
        ),
        encoding="utf-8",
    )
    for rel, payload in (
        (manifest_rel, b"cached-manifest-bundle"),
        (bundle_rel, b"cached-main-bundle"),
    ):
        target = cached_artifact / "content-packs" / "sample-native-expansion" / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(payload)

    return repo, cached_artifact


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

        parsed = _parse_dry_run_phases(output)
        phase_names = [name for name, _, _ in parsed]
        actions = {name: action for name, action, _ in parsed}
        self.assertEqual(EXPECTED_PHASE_ORDER, phase_names)
        self.assertLess(
            phase_names.index("animation-matrix"),
            phase_names.index("bundle-promotion"),
            msg="animation-matrix must execute before bundle-promotion",
        )
        self.assertEqual("skipped", actions["level1-models"])
        self.assertEqual("skipped", actions["level1-textures"])
        self.assertEqual("skipped", actions["package-loader"])

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

    def test_dry_run_opt_in_rebuild_and_package_loader_flags(self) -> None:
        result = _run_ps_file(
            BUILD_SCRIPT,
            [
                "-DryRun",
                "-RebuildLevelOneModels",
                "-RebuildLevelOneTextures",
                "-PackageLoader",
            ],
        )
        self.assertEqual(0, result.returncode, msg=result.stdout + "\n" + result.stderr)

        output = result.stdout
        parsed = _parse_dry_run_phases(output)
        self.assertEqual(EXPECTED_PHASE_ORDER, [name for name, _, _ in parsed])
        actions = {name: action for name, action, _ in parsed}
        self.assertEqual("executed", actions["level1-models"])
        self.assertEqual("executed", actions["level1-textures"])
        self.assertEqual("executed", actions["package-loader"])
        for phase in EXPECTED_PHASE_ORDER:
            self.assertEqual("executed", actions[phase], msg=f"Expected {phase} executed in:\n{output}")

        # Verify -SkipUnityBuild gates -PackageLoader in DryRun
        gated = _run_ps_file(BUILD_SCRIPT, ["-DryRun", "-SkipUnityBuild", "-PackageLoader"])
        self.assertEqual(0, gated.returncode, msg=gated.stdout + "\n" + gated.stderr)
        gated_actions = {name: action for name, action, _ in _parse_dry_run_phases(gated.stdout)}
        self.assertEqual("skipped", gated_actions["package-loader"])

    def test_dry_run_restore_bundles_from_prints_restore_plan(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            restore_root = Path(tmp) / "prior-bundles-backup"
            pack_backup = restore_root / "sample-native-expansion"
            pack_backup.mkdir(parents=True, exist_ok=True)
            manifest_path = pack_backup / "backup-manifest.json"
            manifest_path.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "packName": "sample-native-expansion",
                        "bundles": [
                            {
                                "path": "bundles/sample-native-expansion.bundle",
                                "existed": True,
                                "sha256": "0" * 64,
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )

            result = _run_ps_file(
                BUILD_SCRIPT,
                ["-DryRun", "-RestoreBundlesFrom", str(restore_root)],
            )
            self.assertEqual(0, result.returncode, msg=result.stdout + "\n" + result.stderr)

            output = result.stdout
            self.assertIn("=== Canonical Visual Modules Restore (DryRun) ===", output)
            self.assertIn(f"backupRoot: {restore_root.resolve()}", output)
            self.assertIn("packs: sample-native-expansion", output)
            self.assertIn(str(manifest_path.resolve()), output)

    def test_invalid_pack_fails_nonzero(self) -> None:
        result = _run_ps_file(BUILD_SCRIPT, ["-DryRun", "-Packs", "non-existent-pack"])
        self.assertNotEqual(0, result.returncode)

    def test_invalid_texture_size_power_fails_nonzero(self) -> None:
        result = _run_ps_file(BUILD_SCRIPT, ["-DryRun", "-TextureSizePower", "99"])
        self.assertNotEqual(0, result.returncode)

    def test_invalid_cli_parameter_ranges_fail_nonzero(self) -> None:
        invalid_invocations = [
            ["-DryRun", "-TextureSizePower", "8"],
            ["-DryRun", "-TextureSizePower", "13"],
            ["-DryRun", "-MaxParallelExports", "99"],
            ["-DryRun", "-MaxParallelExports", "0"],
            ["-DryRun", "-PackWorkers", "0"],
            ["-DryRun", "-PackWorkers", "9"],
            ["-DryRun", "-RestoreBundlesFrom", str(REPO_ROOT / "non-existent-restore-dir-xyz")],
        ]
        for args in invalid_invocations:
            with self.subTest(args=args):
                result = _run_ps_file(BUILD_SCRIPT, args)
                self.assertNotEqual(0, result.returncode, msg=f"Expected failure for {args}")

    def test_provenance_phase_hash_cache_and_promotion_end_to_end(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            repo = root / "repo"
            validated_root = root / "validated"
            backup_root = root / "backup"
            cache_path = root / "cache" / "canonical-level2-cache.json"

            # Create minimal required inputs for the 'blender' phase in the isolated repo
            _write_stub_files(repo, BLENDER_PHASE_REQUIRED_INPUTS)

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
. {_ps_quote(PROVENANCE_SCRIPT)}

$hash1 = Get-CanonicalPhaseInputHash -RepositoryRoot {_ps_quote(repo)} -Phase 'blender'
$hash2 = Get-CanonicalPhaseInputHash -RepositoryRoot {_ps_quote(repo)} -Phase 'blender'
$missBefore = Test-CanonicalPhaseCacheHit -CachePath {_ps_quote(cache_path)} -Phase 'blender' -InputHash $hash1 -ExpectedOutputs @({_ps_quote(expected_output)})

Save-CanonicalPhaseCache -CachePath {_ps_quote(cache_path)} -Phase 'blender' -InputHash $hash1 -Metadata @{{ packCount = 2 }}
$hitAfter = Test-CanonicalPhaseCacheHit -CachePath {_ps_quote(cache_path)} -Phase 'blender' -InputHash $hash1 -ExpectedOutputs @({_ps_quote(expected_output)})
$missEmpty = Test-CanonicalPhaseCacheHit -CachePath {_ps_quote(cache_path)} -Phase 'blender' -InputHash $hash1 -ExpectedOutputs @({_ps_quote(empty_output)})
$missWrongHash = Test-CanonicalPhaseCacheHit -CachePath {_ps_quote(cache_path)} -Phase 'blender' -InputHash ('0' * 64) -ExpectedOutputs @({_ps_quote(expected_output)})

# Modify an input and verify hash changes
Set-Content -LiteralPath {_ps_quote(repo / "tools/Build-CanonicalVisualModules.py")} -Value 'modified' -Encoding UTF8
$hash3 = Get-CanonicalPhaseInputHash -RepositoryRoot {_ps_quote(repo)} -Phase 'blender'
$missAfterChange = Test-CanonicalPhaseCacheHit -CachePath {_ps_quote(cache_path)} -Phase 'blender' -InputHash $hash3 -ExpectedOutputs @({_ps_quote(expected_output)})

$promotion = @(Invoke-CanonicalBundlePromotion -RepositoryRoot {_ps_quote(repo)} -ValidatedPackRoot {_ps_quote(validated_root)} -BackupRoot {_ps_quote(backup_root)} -PackNames @({_ps_quote(pack_name)}))

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

    def test_restore_canonical_bundle_promotion_end_to_end_and_tamper_rejection(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            repo = root / "repo"
            validated_root = root / "validated"
            backup_root = root / "backup"

            pack_name = "test-pack"
            existing_rel = "bundles/test-pack.manifest.bundle"
            new_only_rel = "bundles/test-pack-extra.bundle"
            pack_dir = repo / "content" / "packs" / pack_name
            pack_dir.mkdir(parents=True, exist_ok=True)
            (pack_dir / "content.json").write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "packId": "test:pack",
                        "assetBundleManifestPath": existing_rel,
                        "assetBundles": [{"path": new_only_rel}],
                    }
                ),
                encoding="utf-8",
            )

            old_existing_bytes = b"original-manifest-bundle-bytes"
            new_existing_bytes = b"promoted-manifest-bundle-bytes"
            new_only_bytes = b"newly-promoted-extra-bundle-bytes"

            live_existing = pack_dir / existing_rel
            live_existing.parent.mkdir(parents=True, exist_ok=True)
            live_existing.write_bytes(old_existing_bytes)
            live_new_only = pack_dir / new_only_rel
            self.assertFalse(live_new_only.exists())

            for rel, payload in ((existing_rel, new_existing_bytes), (new_only_rel, new_only_bytes)):
                target = validated_root / pack_name / rel
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(payload)

            ps_promote_and_restore = f"""
$ErrorActionPreference = 'Stop'
. {_ps_quote(PROVENANCE_SCRIPT)}

$promoted = @(Invoke-CanonicalBundlePromotion -RepositoryRoot {_ps_quote(repo)} -ValidatedPackRoot {_ps_quote(validated_root)} -BackupRoot {_ps_quote(backup_root)} -PackNames @({_ps_quote(pack_name)}))
$liveBeforeRestoreHasNew = Test-Path -LiteralPath {_ps_quote(live_new_only)} -PathType Leaf
$restored = @(Restore-CanonicalBundlePromotion -RepositoryRoot {_ps_quote(repo)} -BackupRoot {_ps_quote(backup_root)})

[ordered]@{{
    promoted = $promoted
    liveBeforeRestoreHasNew = $liveBeforeRestoreHasNew
    restored = $restored
}} | ConvertTo-Json -Depth 10
"""
            result = _run_ps_command(ps_promote_and_restore)
            self.assertEqual(0, result.returncode, msg=result.stdout + "\n" + result.stderr)
            payload = json.loads(result.stdout)

            self.assertTrue(payload["liveBeforeRestoreHasNew"])
            self.assertEqual(1, len(payload["restored"]))
            summary = payload["restored"][0]
            self.assertEqual(pack_name, summary["packName"])
            self.assertEqual(1, summary["restoredCount"])
            self.assertEqual(1, summary["removedCount"])

            by_path = {entry["path"]: entry for entry in summary["bundles"]}
            self.assertEqual("restored", by_path[existing_rel]["action"])
            self.assertTrue(by_path[existing_rel]["existed"])
            self.assertEqual(_sha256_hex(old_existing_bytes), by_path[existing_rel]["sha256"])
            self.assertEqual("removed", by_path[new_only_rel]["action"])
            self.assertFalse(by_path[new_only_rel]["existed"])

            # Verify live files on disk after restore
            self.assertEqual(old_existing_bytes, live_existing.read_bytes())
            self.assertEqual(_sha256_hex(old_existing_bytes), _sha256_hex(live_existing.read_bytes()))
            self.assertFalse(live_new_only.exists())

            # Tamper the backed-up file and verify Restore-CanonicalBundlePromotion fails closed
            live_existing.write_bytes(new_existing_bytes)
            tampered_backup_file = backup_root / pack_name / existing_rel
            tampered_backup_file.write_bytes(b"TAMPERED-BACKUP-PAYLOAD")

            ps_tampered = f"""
$ErrorActionPreference = 'Stop'
. {_ps_quote(PROVENANCE_SCRIPT)}
Restore-CanonicalBundlePromotion -RepositoryRoot {_ps_quote(repo)} -BackupRoot {_ps_quote(backup_root)} -PackNames @({_ps_quote(pack_name)}) | Out-Null
"""
            tampered_result = _run_ps_command(ps_tampered)
            self.assertNotEqual(0, tampered_result.returncode)
            combined_output = tampered_result.stdout + "\n" + tampered_result.stderr
            self.assertIn("SHA-256 mismatch", combined_output)
            # Live bundle must remain untouched when backup SHA-256 verification fails
            self.assertEqual(new_existing_bytes, live_existing.read_bytes())

    def test_level1_models_phase_input_hash_determinism_and_blend_filtering(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp) / "repo"
            _write_stub_files(
                repo,
                (
                    "tools/RogueModelKit.py",
                    "tools/CanonicalUnitHardpoints.py",
                    "tools/VisualModelAuthoringSupport.py",
                    "tools/sample_native_vehicles_v2_recipes.py",
                ),
            )
            source_art = repo / "content" / "source-art"
            source_art.mkdir(parents=True, exist_ok=True)
            v1_blend = source_art / "sandlance-scout.blend"
            v2_blend = source_art / "sandlance-scout-v2.blend"
            rigged_blend = source_art / "sandlance-scout-rigged.blend"
            level2_blend = source_art / "canonical-visual-modules.blend"
            extra_v2_blend = source_art / "atlas-crane-mech-v2.blend"
            extra_v1_blend = source_art / "colossus-siege.blend"

            v1_blend.write_bytes(b"v1-blend-original")
            v2_blend.write_bytes(b"v2-blend-original")
            rigged_blend.write_bytes(b"rigged-blend-original")
            level2_blend.write_bytes(b"level2-blend-original")

            ps_script = f"""
$ErrorActionPreference = 'Stop'
. {_ps_quote(PROVENANCE_SCRIPT)}

$h1 = Get-CanonicalPhaseInputHash -RepositoryRoot {_ps_quote(repo)} -Phase 'level1-models'
$h2 = Get-CanonicalPhaseInputHash -RepositoryRoot {_ps_quote(repo)} -Phase 'level1-models'

# Mutate excluded blends and add a new *-v2.blend; hash must stay identical
Set-Content -LiteralPath {_ps_quote(v2_blend)} -Value 'v2-blend-modified' -Encoding UTF8
Set-Content -LiteralPath {_ps_quote(rigged_blend)} -Value 'rigged-blend-modified' -Encoding UTF8
Set-Content -LiteralPath {_ps_quote(level2_blend)} -Value 'level2-blend-modified' -Encoding UTF8
Set-Content -LiteralPath {_ps_quote(extra_v2_blend)} -Value 'new-v2-blend' -Encoding UTF8
$hAfterExcluded = Get-CanonicalPhaseInputHash -RepositoryRoot {_ps_quote(repo)} -Phase 'level1-models'

# Mutate v1 .blend; hash must change
Set-Content -LiteralPath {_ps_quote(v1_blend)} -Value 'v1-blend-modified' -Encoding UTF8
$hAfterV1Edit = Get-CanonicalPhaseInputHash -RepositoryRoot {_ps_quote(repo)} -Phase 'level1-models'

# Add another v1 .blend; hash must change again
Set-Content -LiteralPath {_ps_quote(extra_v1_blend)} -Value 'another-v1-blend' -Encoding UTF8
$hAfterV1Add = Get-CanonicalPhaseInputHash -RepositoryRoot {_ps_quote(repo)} -Phase 'level1-models'

# Mutate optional recipe script; hash must change again
Set-Content -LiteralPath {_ps_quote(repo / "tools/sample_native_vehicles_v2_recipes.py")} -Value 'recipe-v2' -Encoding UTF8
$hAfterRecipeEdit = Get-CanonicalPhaseInputHash -RepositoryRoot {_ps_quote(repo)} -Phase 'level1-models'

[ordered]@{{
    h1 = $h1
    h2 = $h2
    hAfterExcluded = $hAfterExcluded
    hAfterV1Edit = $hAfterV1Edit
    hAfterV1Add = $hAfterV1Add
    hAfterRecipeEdit = $hAfterRecipeEdit
}} | ConvertTo-Json -Depth 5
"""
            result = _run_ps_command(ps_script)
            self.assertEqual(0, result.returncode, msg=result.stdout + "\n" + result.stderr)

            hashes = json.loads(result.stdout)
            self.assertRegex(hashes["h1"], r"^[0-9a-f]{64}$")
            self.assertEqual(hashes["h1"], hashes["h2"])
            self.assertEqual(hashes["h1"], hashes["hAfterExcluded"])
            self.assertNotEqual(hashes["h1"], hashes["hAfterV1Edit"])
            self.assertNotEqual(hashes["hAfterV1Edit"], hashes["hAfterV1Add"])
            self.assertNotEqual(hashes["hAfterV1Add"], hashes["hAfterRecipeEdit"])

    def test_audit_material_maker_output_batch_cli_passing_and_failing(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            tex_dir = root / "textures"
            _write_png_pbr_set(tex_dir, "UnitAlpha")
            _write_png_pbr_set(tex_dir, "UnitBeta")

            pass_out_dir = root / "audit-pass"
            pass_batch_file = root / "audit-pass" / "mm-audit-batch.json"
            pass_proc = subprocess.run(
                [
                    sys.executable,
                    "-m",
                    "tools.audit_material_maker_output",
                    "--directory",
                    str(tex_dir),
                    "--prefixes",
                    "UnitAlpha",
                    "UnitBeta",
                    "UnitAlpha",
                    "--workflow",
                    "urp",
                    "--output-dir",
                    str(pass_out_dir),
                    "--output",
                    str(pass_batch_file),
                ],
                cwd=str(REPO_ROOT),
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                check=False,
            )
            self.assertEqual(0, pass_proc.returncode, msg=pass_proc.stdout + "\n" + pass_proc.stderr)

            alpha_report_path = pass_out_dir / "mm-audit-UnitAlpha.json"
            beta_report_path = pass_out_dir / "mm-audit-UnitBeta.json"
            self.assertTrue(alpha_report_path.is_file())
            self.assertTrue(beta_report_path.is_file())
            self.assertTrue(pass_batch_file.is_file())

            batch_report = json.loads(pass_batch_file.read_text(encoding="utf-8"))
            self.assertEqual(batch_report, json.loads(pass_proc.stdout))
            self.assertEqual("materialMakerBatchQualityReport", batch_report["kind"])
            self.assertEqual("urp", batch_report["workflow"])
            self.assertEqual(2, batch_report["totalCount"])
            self.assertEqual(2, batch_report["passedCount"])
            self.assertEqual([], batch_report["failedPrefixes"])
            self.assertEqual({"passed": True, "failureCount": 0}, batch_report["qualityGate"])
            self.assertEqual(["UnitAlpha", "UnitBeta"], [r["prefix"] for r in batch_report["reports"]])
            for report_path in (alpha_report_path, beta_report_path):
                per_prefix = json.loads(report_path.read_text(encoding="utf-8"))
                self.assertEqual("materialMakerQualityReport", per_prefix["kind"])
                self.assertTrue(per_prefix["qualityGate"]["passed"])

            # Failing batch case: add an invalid PBR set and verify exit code 2 + report files
            _write_png_pbr_set(
                tex_dir,
                "UnitBroken",
                metallic_rgba=(255, 0, 0, 0),
                normal_rgb=(128, 128, 0),
            )
            fail_out_dir = root / "audit-fail"
            fail_batch_file = root / "audit-fail" / "mm-audit-batch-fail.json"
            fail_proc = subprocess.run(
                [
                    sys.executable,
                    "-m",
                    "tools.audit_material_maker_output",
                    "--directory",
                    str(tex_dir),
                    "--prefixes",
                    "UnitAlpha",
                    "UnitBroken",
                    "--workflow",
                    "urp",
                    "--output-dir",
                    str(fail_out_dir),
                    "--output",
                    str(fail_batch_file),
                ],
                cwd=str(REPO_ROOT),
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                check=False,
            )
            self.assertEqual(2, fail_proc.returncode, msg=fail_proc.stdout + "\n" + fail_proc.stderr)
            self.assertTrue((fail_out_dir / "mm-audit-UnitAlpha.json").is_file())
            broken_report_path = fail_out_dir / "mm-audit-UnitBroken.json"
            self.assertTrue(broken_report_path.is_file())
            self.assertTrue(fail_batch_file.is_file())

            fail_batch = json.loads(fail_batch_file.read_text(encoding="utf-8"))
            self.assertEqual("materialMakerBatchQualityReport", fail_batch["kind"])
            self.assertEqual(2, fail_batch["totalCount"])
            self.assertEqual(1, fail_batch["passedCount"])
            self.assertEqual(["UnitBroken"], fail_batch["failedPrefixes"])
            self.assertFalse(fail_batch["qualityGate"]["passed"])
            self.assertGreaterEqual(fail_batch["qualityGate"]["failureCount"], 2)

            broken_report = json.loads(broken_report_path.read_text(encoding="utf-8"))
            self.assertFalse(broken_report["qualityGate"]["passed"])
            self.assertIn("metallic-gloss-rgb-not-identical", broken_report["qualityGate"]["failures"])
            self.assertIn("metallic-gloss-alpha-empty", broken_report["qualityGate"]["failures"])

    def test_find_unity_cached_artifact_root_validates_audit_and_bundles(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            repo, cached_artifact = _setup_isolated_pipeline_repo(root)
            cache_path = repo / "artifacts" / "canonical-level2-cache.json"
            bundle_path = (
                cached_artifact
                / "content-packs"
                / "sample-native-expansion"
                / "bundles"
                / "sample-native-expansion.bundle"
            )
            audit_path = cached_artifact / "logs" / "unity-audit.json"

            # Load only the cache helper functions via PowerShell AST so top-level orchestration does not run
            ps_script = f"""
$ErrorActionPreference = 'Stop'
$repoRoot = {_ps_quote(repo)}
. {_ps_quote(repo / "tools" / "CanonicalVisualBuildProvenance.ps1")}

$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile({_ps_quote(repo / "tools" / "Build-CanonicalVisualModules.ps1")}, [ref]$tokens, [ref]$errors)
if ($errors.Count -gt 0) {{
    throw "Parse errors loading Build-CanonicalVisualModules.ps1"
}}
$fnAsts = $ast.FindAll({{
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -in @('Get-PhaseCacheEntry', 'Get-PackDeclaredBundleRelativePaths', 'Find-UnityCachedArtifactRoot')
}}, $true)
foreach ($fn in $fnAsts) {{
    Invoke-Expression $fn.Extent.Text
}}

$unityHash = Get-CanonicalPhaseInputHash -RepositoryRoot $repoRoot -Phase 'unity'
Save-CanonicalPhaseCache -CachePath {_ps_quote(cache_path)} -Phase 'unity' -InputHash $unityHash -Metadata @{{
    artifactRoot = {_ps_quote(cached_artifact)}
    packs = @('sample-native-expansion')
}} | Out-Null

$hitRoot = Find-UnityCachedArtifactRoot -CacheFilePath {_ps_quote(cache_path)} -InputHash $unityHash -TargetPacks @('sample-native-expansion')
$missWrongHash = Find-UnityCachedArtifactRoot -CacheFilePath {_ps_quote(cache_path)} -InputHash ('0' * 64) -TargetPacks @('sample-native-expansion')

# Missing bundle invalidates cache lookup
$savedBundleBytes = [System.IO.File]::ReadAllBytes({_ps_quote(bundle_path)})
Remove-Item -LiteralPath {_ps_quote(bundle_path)} -Force
$missMissingBundle = Find-UnityCachedArtifactRoot -CacheFilePath {_ps_quote(cache_path)} -InputHash $unityHash -TargetPacks @('sample-native-expansion')
[System.IO.File]::WriteAllBytes({_ps_quote(bundle_path)}, $savedBundleBytes)

# Failing unity-audit.json invalidates cache lookup
$savedAuditRaw = Get-Content -LiteralPath {_ps_quote(audit_path)} -Raw
Set-Content -LiteralPath {_ps_quote(audit_path)} -Value '{{"build":{{"quality_gate":{{"passed":false}}}}}}' -Encoding UTF8
$missFailedAudit = Find-UnityCachedArtifactRoot -CacheFilePath {_ps_quote(cache_path)} -InputHash $unityHash -TargetPacks @('sample-native-expansion')
Set-Content -LiteralPath {_ps_quote(audit_path)} -Value $savedAuditRaw -Encoding UTF8

$hitRestored = Find-UnityCachedArtifactRoot -CacheFilePath {_ps_quote(cache_path)} -InputHash $unityHash -TargetPacks @('sample-native-expansion')

[ordered]@{{
    hitRoot = $hitRoot
    missWrongHash = $missWrongHash
    missMissingBundle = $missMissingBundle
    missFailedAudit = $missFailedAudit
    hitRestored = $hitRestored
}} | ConvertTo-Json -Depth 5
"""
            result = _run_ps_command(ps_script)
            self.assertEqual(0, result.returncode, msg=result.stdout + "\n" + result.stderr)

            payload = json.loads(result.stdout)
            expected_root = str(cached_artifact.resolve())
            self.assertEqual(expected_root, payload["hitRoot"])
            self.assertIsNone(payload["missWrongHash"])
            self.assertIsNone(payload["missMissingBundle"])
            self.assertIsNone(payload["missFailedAudit"])
            self.assertEqual(expected_root, payload["hitRestored"])

    def test_unity_cache_hydration_and_upstream_invalidation_in_dry_run(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            repo, cached_artifact = _setup_isolated_pipeline_repo(root)
            cache_path = repo / "artifacts" / "canonical-level2-cache.json"
            isolated_build_script = repo / "tools" / "Build-CanonicalVisualModules.ps1"

            # Populate all three incremental caches (blender, textures, unity) in the isolated repo
            ps_seed = f"""
$ErrorActionPreference = 'Stop'
. {_ps_quote(repo / "tools" / "CanonicalVisualBuildProvenance.ps1")}

$blenderHash = Get-CanonicalPhaseInputHash -RepositoryRoot {_ps_quote(repo)} -Phase 'blender'
Save-CanonicalPhaseCache -CachePath {_ps_quote(cache_path)} -Phase 'blender' -InputHash $blenderHash -Metadata @{{ moduleCount = 1 }} | Out-Null

$texturesHash = Get-CanonicalPhaseInputHash -RepositoryRoot {_ps_quote(repo)} -Phase 'textures'
Save-CanonicalPhaseCache -CachePath {_ps_quote(cache_path)} -Phase 'textures' -InputHash $texturesHash -Metadata @{{ sizePower = 11; presetCount = 1 }} | Out-Null

$unityHash = Get-CanonicalPhaseInputHash -RepositoryRoot {_ps_quote(repo)} -Phase 'unity'
Save-CanonicalPhaseCache -CachePath {_ps_quote(cache_path)} -Phase 'unity' -InputHash $unityHash -Metadata @{{
    artifactRoot = {_ps_quote(cached_artifact)}
    packs = @('sample-native-expansion')
}} | Out-Null
"""
            seed_result = _run_ps_command(ps_seed)
            self.assertEqual(0, seed_result.returncode, msg=seed_result.stdout + "\n" + seed_result.stderr)

            # 1. All upstream + unity inputs cached -> unity and unity-audit are cached with cached artifactRoot
            hit_run = _run_ps_file(
                isolated_build_script,
                ["-DryRun", "-Incremental", "-Packs", "sample-native-expansion"],
            )
            self.assertEqual(0, hit_run.returncode, msg=hit_run.stdout + "\n" + hit_run.stderr)
            hit_phases = {name: (action, reason) for name, action, reason in _parse_dry_run_phases(hit_run.stdout)}
            self.assertEqual("cached", hit_phases["blender"][0])
            self.assertEqual("cached", hit_phases["textures"][0])
            self.assertEqual("cached", hit_phases["unity"][0])
            self.assertEqual("cached", hit_phases["unity-audit"][0])
            self.assertIn(str(cached_artifact.resolve()), hit_phases["unity"][1])
            self.assertIn(str(cached_artifact.resolve()), hit_phases["unity-audit"][1])

            # 2. Mutating a textures input invalidates textures and forces unity/unity-audit to execute
            pbr_tool = repo / "tools" / "pack_material_maker_pbr.py"
            saved_pbr_tool = pbr_tool.read_text(encoding="utf-8")
            pbr_tool.write_text("modified-pbr-tool\n", encoding="utf-8")
            tex_invalidated = _run_ps_file(
                isolated_build_script,
                ["-DryRun", "-Incremental", "-Packs", "sample-native-expansion"],
            )
            self.assertEqual(0, tex_invalidated.returncode, msg=tex_invalidated.stdout + "\n" + tex_invalidated.stderr)
            tex_phases = {
                name: (action, reason) for name, action, reason in _parse_dry_run_phases(tex_invalidated.stdout)
            }
            self.assertEqual("cached", tex_phases["blender"][0])
            self.assertEqual("executed", tex_phases["textures"][0])
            self.assertEqual("executed", tex_phases["unity"][0])
            self.assertIn("Upstream phase scheduled to rebuild Unity inputs", tex_phases["unity"][1])
            self.assertEqual("executed", tex_phases["unity-audit"][0])

            # 3. Restore textures input, mutate Unity Assets input -> blender/textures stay cached, unity misses
            pbr_tool.write_text(saved_pbr_tool, encoding="utf-8")
            (repo / "unity" / "RogueCommandAssetAuthoring" / "Assets" / "UnitMarker.txt").write_text(
                "unit-marker-v2\n", encoding="utf-8"
            )
            unity_invalidated = _run_ps_file(
                isolated_build_script,
                ["-DryRun", "-Incremental", "-Packs", "sample-native-expansion"],
            )
            self.assertEqual(0, unity_invalidated.returncode, msg=unity_invalidated.stdout + "\n" + unity_invalidated.stderr)
            unity_phases = {
                name: (action, reason) for name, action, reason in _parse_dry_run_phases(unity_invalidated.stdout)
            }
            self.assertEqual("cached", unity_phases["blender"][0])
            self.assertEqual("cached", unity_phases["textures"][0])
            self.assertEqual("executed", unity_phases["unity"][0])
            self.assertIn("Incremental cache miss", unity_phases["unity"][1])
            self.assertEqual("executed", unity_phases["unity-audit"][0])


if __name__ == "__main__":
    unittest.main()
