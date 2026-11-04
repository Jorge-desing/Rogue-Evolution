#!/usr/bin/env python3
"""Produce a machine-readable Unity/URP/3D/AssetBundle build diagnostic.

The tool combines a Unity batch log with offline UnityPy AssetBundle audits.
It distinguishes build evidence from Steam runtime evidence and never launches
Unity or Rogue Command.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any, Iterable


EXPECTED_UNITY_VERSION = "2021.3.45f2"
_VERSION_RE = re.compile(r"Version is ['\"](?P<version>[^'\"]+)")
_BUNDLE_NAME_RE = re.compile(r"^\s*Bundle Name:\s*(?P<name>[^\s]+)", re.MULTILINE)
_DURATION_RE = re.compile(r"BuildAssetBundles.*?Total\.\s*(?P<seconds>[0-9]+(?:\.[0-9]+)?)\s*s", re.IGNORECASE)


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _unique(values: Iterable[str]) -> list[str]:
    return list(dict.fromkeys(value for value in values if value))


def parse_unity_build_log(
    text: str,
    *,
    bundle_metrics: list[dict[str, Any]] | None = None,
    expected_unity_version: str = EXPECTED_UNITY_VERSION,
) -> dict[str, Any]:
    """Parse Unity batch output without treating licensing noise as a build failure."""
    version_match = _VERSION_RE.search(text)
    unity_version = version_match.group("version").split(" ", 1)[0] if version_match else ""
    lines = text.splitlines()
    licensing_diagnostics = [
        line.strip() for line in lines if "Licensing::" in line and "Error:" in line
    ]
    fatal_lines = []
    for line in lines:
        stripped = line.strip()
        if "Licensing::" in stripped:
            continue
        if re.search(r"\b(?:Exception|Build failed|Error building|BUILD_FAILED)\b", stripped, re.IGNORECASE):
            fatal_lines.append(stripped)
    fatal_lines = _unique(fatal_lines)

    success_marker = bool(
        re.search(r"(?:SUCCESS! Built|TARGETED_[A-Z_]+_OK:|Built (?:seven|from|the) )", text)
    )
    batchmode_completed = (
        "Batchmode quit successfully" in text
        or "Exiting batchmode successfully" in text
        or (success_marker and "shutting down" in text)
    )
    build_succeeded = bool(success_marker and not fatal_lines)
    bundle_names = _unique(_BUNDLE_NAME_RE.findall(text))
    duration_match = _DURATION_RE.search(text)
    duration = float(duration_match.group("seconds")) if duration_match else None

    reports = bundle_metrics or []
    bundle_issues: list[str] = []
    for report in reports:
        quality = report.get("quality_gate") or {}
        if quality.get("passed") is False:
            bundle_issues.extend(str(issue) for issue in quality.get("issues") or [])
        if int(report.get("bundle_size_bytes", 0) or 0) <= 0:
            bundle_issues.append("empty-assetbundle")

    quality_issues: list[str] = []
    if not build_succeeded:
        quality_issues.append("build-failed")
    if fatal_lines:
        quality_issues.append("fatal-build-error")
    if expected_unity_version and unity_version != expected_unity_version:
        quality_issues.append("unity-version-mismatch")
    quality_issues.extend(_unique(bundle_issues))

    return {
        "unity_version": unity_version,
        "expected_unity_version": expected_unity_version,
        "build_succeeded": build_succeeded,
        "success_marker_present": success_marker,
        "batchmode_completed": batchmode_completed,
        "fatal_error_count": len(fatal_lines),
        "fatal_errors": fatal_lines,
        "licensing_diagnostic_count": len(licensing_diagnostics),
        "licensing_diagnostics": licensing_diagnostics,
        "bundle_count": len(bundle_names),
        "bundle_names": bundle_names,
        "build_duration_seconds": duration,
        "quality_gate": {
            "passed": not quality_issues,
            "issues": _unique(quality_issues),
        },
    }


def summarize_bundle_report(report: dict[str, Any]) -> dict[str, Any]:
    """Add stable URP and 3D metrics to the legacy UnityPy audit shape."""
    type_counts = report.get("type_counts") or {}
    shaders = report.get("shaders") or []
    raw_shader_names = [str(row.get("name", "")) for row in shaders if isinstance(row, dict)]
    shader_names = _unique(raw_shader_names)
    textures = report.get("textures") or []
    texture_dimensions = [
        {
            "name": str(row.get("name", "")),
            "width": int(row.get("width", 0) or 0),
            "height": int(row.get("height", 0) or 0),
            "color_space": row.get("color_space"),
        }
        for row in textures
        if isinstance(row, dict)
    ]
    urp = {
        "shader_count": int(report.get("shader_count", 0) or 0),
        "shader_names": shader_names,
        "shader_names_unresolved_count": sum(1 for name in raw_shader_names if not name),
        "material_count": int(report.get("material_count", type_counts.get("Material", 0)) or 0),
        "texture_count": int(report.get("texture_count", type_counts.get("Texture2D", 0)) or 0),
        "texture_dimensions": texture_dimensions,
        "self_contained_shader_evidence": bool(shader_names),
    }
    three_d = {
        "game_object_count": int(type_counts.get("GameObject", 0) or 0),
        "transform_count": int(type_counts.get("Transform", 0) or 0),
        "mesh_count": int(type_counts.get("Mesh", 0) or 0),
        "mesh_filter_count": int(type_counts.get("MeshFilter", 0) or 0),
        "mesh_renderer_count": int(type_counts.get("MeshRenderer", 0) or 0),
        "skinned_mesh_renderer_count": int(type_counts.get("SkinnedMeshRenderer", 0) or 0),
        "animator_count": int(type_counts.get("Animator", 0) or 0),
        "animation_clip_count": int(type_counts.get("AnimationClip", 0) or 0),
        "particle_system_count": int(type_counts.get("ParticleSystem", 0) or 0),
        "lod_group_count": int(type_counts.get("LODGroup", 0) or 0),
        "serialized_object_count": int(report.get("object_count", 0) or 0),
    }
    return {
        "bundle_path": report.get("bundle_path", ""),
        "bundle_size_bytes": int(report.get("bundle_size_bytes", 0) or 0),
        "bundle_sha256": report.get("bundle_sha256", ""),
        "urp": urp,
        "three_d": three_d,
        "legacy_quality_gate": report.get("quality_gate", {"passed": True, "issues": []}),
    }


def audit_build(log_path: Path, bundle_paths: list[Path]) -> dict[str, Any]:
    """Audit one Unity log and zero or more bundles."""
    if not log_path.is_file():
        raise FileNotFoundError(log_path)
    from audit_unity_bundle import audit_bundle

    bundle_reports: list[dict[str, Any]] = []
    for bundle_path in bundle_paths:
        report = audit_bundle(str(bundle_path))
        if not report.get("bundle_sha256") and bundle_path.is_file():
            report["bundle_sha256"] = _sha256(bundle_path)
        report["diagnostic_summary"] = summarize_bundle_report(report)
        bundle_reports.append(report)

    build = parse_unity_build_log(
        log_path.read_text(encoding="utf-8", errors="replace"),
        bundle_metrics=bundle_reports,
    )
    artifact_files = []
    for bundle_path in bundle_paths:
        artifact_files.append(
            {
                "path": str(bundle_path.resolve()),
                "exists": bundle_path.is_file(),
                "size_bytes": bundle_path.stat().st_size if bundle_path.is_file() else 0,
                "sha256": _sha256(bundle_path) if bundle_path.is_file() else "",
            }
        )
    return {
        "kind": "rogue-command-unity-visual-build-audit",
        "evidence": "local Unity batch log and offline AssetBundle inspection; not Steam runtime evidence",
        "log_path": str(log_path.resolve()),
        "build": build,
        "assetbundles": bundle_reports,
        "artifacts": artifact_files,
        "metrics": {
            "assetbundle_count": len(bundle_reports),
            "total_bundle_bytes": sum(row["size_bytes"] for row in artifact_files),
            "total_serialized_objects": sum(int(row.get("object_count", 0) or 0) for row in bundle_reports),
            "total_textures": sum(int(row.get("texture_count", 0) or 0) for row in bundle_reports),
            "total_shaders": sum(int(row.get("shader_count", 0) or 0) for row in bundle_reports),
            "total_meshes": sum(int((row.get("diagnostic_summary") or {}).get("three_d", {}).get("mesh_count", 0)) for row in bundle_reports),
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--log", required=True, type=Path, help="Unity batch log")
    parser.add_argument("--bundle", action="append", type=Path, default=[], help="AssetBundle to inspect; repeatable")
    parser.add_argument("--output", "-o", type=Path, help="JSON report destination")
    parser.add_argument("--strict", action="store_true", help="Return non-zero when the quality gate fails")
    args = parser.parse_args()

    try:
        report = audit_build(args.log, args.bundle)
    except Exception as exc:
        print(f"ERROR auditing Unity build: {type(exc).__name__}: {exc}", file=sys.stderr)
        return 2
    rendered = json.dumps(report, indent=2, ensure_ascii=False)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered, encoding="utf-8")
        print(f"Reporte guardado en: {args.output}")
    else:
        print(rendered)
    return 1 if args.strict and not report["build"]["quality_gate"]["passed"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
