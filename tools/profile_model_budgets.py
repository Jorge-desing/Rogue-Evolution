"""Compile an authoring profile into calibrated scene and per-asset LOD budgets."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys

from tools.model_budget_policy import (
    ModelBudgetError,
    calibrate_profile,
    load_budget_profile,
)


def _read_samples(path: Path) -> list[dict[str, object]]:
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ModelBudgetError(f"cannot read calibration sample file: {path}") from exc
    if isinstance(value, dict):
        value = value.get("samples")
    if not isinstance(value, list):
        raise ModelBudgetError("calibration sample file must contain an array or a {samples: [...]} object")
    return value


def compile_profile(profile_path: Path, samples_path: Path | None = None) -> dict[str, object]:
    profile = load_budget_profile(profile_path)
    if samples_path is not None:
        calibration = profile.get("calibration")
        if not isinstance(calibration, dict):
            raise ModelBudgetError("profile calibration metadata must be an object before samples can be added")
        calibration["samples"] = _read_samples(samples_path)
    compiled = calibrate_profile(profile)
    return compiled


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--profile", type=Path, default=Path("content/source-art/model-budget-profile.json"))
    parser.add_argument("--samples", type=Path, help="Measured runtime samples JSON: [{triangles,p95FrameTimeMs}, ...]")
    parser.add_argument("--output", type=Path, required=True, help="Compiled profile JSON output path")
    args = parser.parse_args(argv)
    try:
        compiled = compile_profile(args.profile, args.samples)
    except ModelBudgetError as exc:
        print(f"model-budget profile error: {exc}", file=sys.stderr)
        return 2
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(compiled, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    status = str(compiled.get("calibrationStatus", "uncalibrated"))
    if status != "calibrated":
        print(f"calibrationStatus={status}; reason={compiled.get('calibrationFailureReason', 'not measured')}")
        return 2
    report = compiled["budgetReport"]
    print(json.dumps({
        "calibrationStatus": status,
        "targetFps": compiled["targetFps"],
        "targetFrameTimeMs": report["targetFrameTimeMs"],
        "sceneCount": len(report["scenes"]),
        "sceneBudgetPassed": all(scene["sceneBudgetPassed"] for scene in report["scenes"]),
        "output": str(args.output),
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
