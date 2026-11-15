#!/usr/bin/env python3
"""Unified 6-Phase Animation Validation Matrix runner for Rogue Command.

Orchestrates the end-to-end offline animation QA flow:
  Phase 1-2: Python pre-export algorithmic metrics & unit tests
             (tools/unit_animation_algorithms.py, tests/python/rigging/test_unit_animation_algorithms.py)
  Phase 3:   Post-export FBX headless Blender 4.5.14 audit + optional external CLI linter probe
             (tools/validate_fbx_animation_pipeline.py)
  Phase 4:   Prefab & AssetBundle structural rigging audit
             (tools/validate_mech_rigging.py)
  Phase 5:   Unity 2021.3.45f2 Editor batchmode clip & controller audit
             (RogueCommandExpansion.Authoring.AnimationQAValidator)

Note: Phase 6 (Steam runtime telemetry via BepInEx MCP) is executed separately
against a live Steam session and is not fabricated by this offline runner.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_BLENDER_EXE = (
    REPO_ROOT
    / ".tools"
    / "Blender-4.5.14"
    / "blender-4.5.14-windows-x64"
    / "blender.exe"
)
DEFAULT_UNITY_EXE = Path(
    os.environ.get(
        "UNITY_EXE",
        r"C:\Program Files\Unity\Hub\Editor\2021.3.45f2\Editor\Unity.exe",
    )
)
UNITY_PROJECT_DIR = REPO_ROOT / "unity" / "RogueCommandAssetAuthoring"


def _run_step(
    name: str,
    cmd: list[str],
    *,
    env: dict[str, str] | None = None,
    timeout: int = 600,
) -> dict[str, Any]:
    started = time.perf_counter()
    proc = subprocess.run(
        cmd,
        cwd=REPO_ROOT,
        env=env,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
    )
    elapsed = round(time.perf_counter() - started, 3)
    return {
        "phase": name,
        "command": cmd,
        "exit_code": proc.returncode,
        "passed": proc.returncode == 0,
        "elapsed_seconds": elapsed,
        "stdout": proc.stdout,
        "stderr": proc.stderr,
    }


def run_matrix(
    *,
    python_exe: Path,
    blender_exe: Path,
    unity_exe: Path,
    output_path: Path,
    fbx_targets: list[Path],
    all_units: bool = True,
    skip_unity: bool = False,
    strict_loops: bool = False,
) -> dict[str, Any]:
    phases: list[dict[str, Any]] = []
    out_dir = output_path.parent
    out_dir.mkdir(parents=True, exist_ok=True)

    py_env = dict(os.environ)
    site_pkgs = str(REPO_ROOT / ".venv" / "Lib" / "site-packages")
    existing_pp = py_env.get("PYTHONPATH", "")
    py_env["PYTHONPATH"] = f"{site_pkgs}{os.pathsep}{existing_pp}" if existing_pp else site_pkgs

    # Phase 1-2: Pre-export algorithmic metrics & unit tests
    phases.append(
        _run_step(
            "Phase 1-2: Pre-Export Algorithmic Metrics",
            [
                str(python_exe),
                "-m",
                "unittest",
                "tests.python.rigging.test_unit_animation_algorithms",
                "-v",
            ],
            env=py_env,
        )
    )

    # Phase 3: Post-export FBX Blender 4.5.14 audit
    fbx_report = out_dir / "fbx-pipeline-report.json"
    fbx_cmd = [
        str(python_exe),
        "-m",
        "tools.validate_fbx_animation_pipeline",
        "--blender",
        str(blender_exe),
        "--output",
        str(fbx_report),
    ]
    if fbx_targets:
        fbx_cmd.extend(str(p) for p in fbx_targets)
    elif all_units:
        fbx_cmd.append("--all-units")
    phases.append(_run_step("Phase 3: Post-Export FBX Audit", fbx_cmd, env=py_env, timeout=900))

    # Phase 4: Prefab & pack rigging structure audit
    for pack_rel in (
        "content/packs/sample-native-expansion",
        "content/packs/tactical-operations",
    ):
        pack_dir = REPO_ROOT / pack_rel
        if pack_dir.is_dir():
            phases.append(
                _run_step(
                    f"Phase 4: Mech Rigging Audit ({pack_dir.name})",
                    [
                        str(python_exe),
                        str(REPO_ROOT / "tools" / "validate_mech_rigging.py"),
                        "--pack",
                        str(pack_dir),
                        "--json",
                    ],
                    env=py_env,
                )
            )

    # Phase 5: Unity Editor batchmode AnimationQAValidator
    if not skip_unity:
        if not unity_exe.is_file():
            phases.append(
                {
                    "phase": "Phase 5: Unity Editor AnimationQA",
                    "passed": False,
                    "exit_code": -1,
                    "error": f"Unity executable not found at {unity_exe}",
                }
            )
        else:
            unity_log = out_dir / "unity-matrix-qa.log"
            method = (
                "RogueCommandExpansion.Authoring.AnimationQAValidator.AuditAllUnitAnimationsStrictBatch"
                if strict_loops
                else "RogueCommandExpansion.Authoring.AnimationQAValidator.AuditAllUnitAnimationsBatch"
            )
            phases.append(
                _run_step(
                    "Phase 5: Unity Editor AnimationQA",
                    [
                        str(unity_exe),
                        "-batchmode",
                        "-quit",
                        "-nographics",
                        "-projectPath",
                        str(UNITY_PROJECT_DIR),
                        "-executeMethod",
                        method,
                        "-logFile",
                        str(unity_log),
                    ],
                    timeout=600,
                )
            )

    overall_passed = all(p.get("passed", False) for p in phases)
    report = {
        "matrix_version": "1.0.0",
        "passed": overall_passed,
        "phases": phases,
        "runtime_phase_note": "Phase 6 (Steam runtime telemetry via BepInEx MCP) must be run separately against a live game session.",
    }
    output_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("fbx_paths", nargs="*", type=Path, help="Optional specific FBX files for Phase 3.")
    parser.add_argument("--python", type=Path, default=Path(sys.executable))
    parser.add_argument("--blender", type=Path, default=DEFAULT_BLENDER_EXE)
    parser.add_argument("--unity", type=Path, default=DEFAULT_UNITY_EXE)
    parser.add_argument(
        "--output",
        type=Path,
        default=REPO_ROOT / "artifacts" / "animation-qa" / "matrix-report.json",
    )
    parser.add_argument("--skip-unity", action="store_true")
    parser.add_argument("--strict-loops", action="store_true")
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)

    report = run_matrix(
        python_exe=args.python,
        blender_exe=args.blender,
        unity_exe=args.unity,
        output_path=args.output.resolve(),
        fbx_targets=[p.resolve() for p in args.fbx_paths],
        all_units=not bool(args.fbx_paths),
        skip_unity=args.skip_unity,
        strict_loops=args.strict_loops,
    )

    if args.json:
        print(json.dumps(report, indent=2))
    else:
        print(f"Animation Validation Matrix: {'PASS' if report['passed'] else 'FAIL'}")
        for phase in report["phases"]:
            status = "PASS" if phase.get("passed") else "FAIL"
            secs = phase.get("elapsed_seconds", 0.0)
            print(f"  [{status}] {phase['phase']} ({secs:.2f}s)")
        print(f"Report written to: {args.output}")

    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
