#!/usr/bin/env python3
"""Batch-prepare, pack, and audit Material Maker texture presets in parallel.

Eliminates repeated Python startup, NumPy/Pillow/SciPy imports, detail-normal
disk reloads, and sequential post-processing across multi-preset batches (such as
the 28 canonical visual module presets) while preserving exact pixel output and
the Material Maker 1.7 + VehiclePBR_VertexWear quality gates.
"""

from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
import importlib.util
import json
from pathlib import Path
import shutil
import sys
import time
from typing import Any

REPO_ROOT = Path(__file__).resolve().parent.parent
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from PIL import Image

from tools.audit_material_maker_output import build_quality_report
from tools.pack_material_maker_pbr import pack_maps
from tools.validate_material_maker_config import validate_config

REQUIRED_TEMPLATE_TOKENS = (
    "colorize_metallic",
    "edge_detect_emission",
    "colorize_emission",
    "micro_perlin",
    "micro_detail_blend",
    "micro_roughness_blend",
    "tactical-hd-v2",
)


def _load_prepare_template_module() -> Any:
    script_path = REPO_ROOT / "tools" / "Prepare-MaterialMakerTacticalTemplate.py"
    spec = importlib.util.spec_from_file_location("prepare_mm_tactical_template", script_path)
    if spec is None or spec.loader is None:
        raise ImportError(f"Unable to load template generator: {script_path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _load_validated_config(config_path: Path) -> dict[str, Any]:
    errors = validate_config(config_path)
    if errors:
        raise ValueError(
            f"Material Maker workflow configuration '{config_path}' is invalid: "
            + "; ".join(errors)
        )
    return json.loads(config_path.read_text(encoding="utf-8"))


def _template_needs_prepare(template_path: Path, force: bool) -> bool:
    if force or not template_path.is_file():
        return True
    content = template_path.read_text(encoding="utf-8")
    return any(token not in content for token in REQUIRED_TEMPLATE_TOKENS)


def _clean_output_dir(out_dir: Path, raw_dir: Path, template_prefix: str) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)
    if raw_dir.exists():
        shutil.rmtree(raw_dir)
    raw_dir.mkdir(parents=True, exist_ok=True)

    stale_names = (
        f"{template_prefix}_Albedo.png",
        f"{template_prefix}_MetallicGloss.png",
        f"{template_prefix}_Normal.png",
        f"{template_prefix}_Emission.png",
        f"{template_prefix}_pack-report.json",
        f"{template_prefix}_quality-report.json",
        f"{template_prefix}_blender-preview.blend",
        f"{template_prefix}_blender-preview.json",
        "material-maker-workflow.json",
    )
    for name in stale_names:
        candidate = out_dir / name
        if candidate.is_file():
            candidate.unlink()


def cmd_prepare(args: argparse.Namespace) -> int:
    t0 = time.perf_counter()
    _load_validated_config(args.config)
    manifest = json.loads(args.batch_manifest.read_text(encoding="utf-8"))
    if not isinstance(manifest, list) or not manifest:
        raise ValueError("Batch manifest must be a non-empty JSON list.")

    template_mod = _load_prepare_template_module()
    prepared_count = 0
    for entry in manifest:
        preset = str(entry["preset"])
        template_path = Path(str(entry["template"]))
        out_dir = Path(str(entry["outputDir"]))
        raw_dir = Path(str(entry["rawDir"]))
        template_prefix = template_path.stem

        _clean_output_dir(out_dir, raw_dir, template_prefix)
        if _template_needs_prepare(template_path, args.force):
            project = template_mod.build_template_project(
                preset_name=preset,
                size_power=args.size,
            )
            template_path.parent.mkdir(parents=True, exist_ok=True)
            template_path.write_text(json.dumps(project, indent="\t") + "\n", encoding="utf-8")
            prepared_count += 1

    elapsed = time.perf_counter() - t0
    print(
        f"Prepared {prepared_count}/{len(manifest)} Material Maker templates and clean output "
        f"directories in {elapsed:.2f}s."
    )
    return 0


def _process_single_pack_audit(
    entry: dict[str, Any],
    *,
    config: dict[str, Any],
    workflow_name: str,
    requested_resolution: int,
    dilate_px: int,
    detail_rgb: object | None,
    detail_strength: float,
    strict: bool,
    optimize_png: bool,
) -> dict[str, Any]:
    t0 = time.perf_counter()
    preset = str(entry["preset"])
    template_path = Path(str(entry["template"]))
    out_dir = Path(str(entry["outputDir"]))
    raw_dir = Path(str(entry["rawDir"]))
    template_prefix = template_path.stem

    pack_report = pack_maps(
        raw_dir,
        out_dir,
        template_prefix,
        dilate_px=dilate_px,
        detail_strength=detail_strength,
        optimize_png=optimize_png,
        detail_rgb=detail_rgb,
    )
    pack_report_path = out_dir / f"{template_prefix}_pack-report.json"
    pack_report_path.write_text(json.dumps(pack_report, indent=2), encoding="utf-8")

    workflow_cfg = config.get("workflows", {}).get(workflow_name)
    if not isinstance(workflow_cfg, dict):
        raise ValueError(f"Material Maker workflow is missing from config: {workflow_name}")

    workflow_manifest = {
        "workflow": workflow_name,
        "preset": preset,
        "resolutionRequested": requested_resolution,
        "resolutionActual": 0,
        "profile": str(config["exportProfile"]["name"]),
        "pipeline": str(config["textureContract"]["pipeline"]),
        "shader": str(config["textureContract"]["shader"]),
        "template": str(template_path),
        "outputDirectory": str(out_dir),
        "sourceAuthority": str(workflow_cfg["sourceAuthority"]),
        "postProcess": str(workflow_cfg["postProcess"]),
        "blenderPreviewSource": "",
        "blenderPreviewBlend": "",
        "blenderPreviewReport": "",
    }
    workflow_manifest_path = out_dir / "material-maker-workflow.json"
    workflow_manifest_path.write_text(json.dumps(workflow_manifest, indent=2), encoding="utf-8")

    quality_report = build_quality_report(
        out_dir,
        template_prefix,
        template_path=template_path,
        workspace_path=out_dir.parent if workflow_name == "three_d" else None,
        workflow_name=workflow_name,
    )
    quality_report_path = out_dir / f"{template_prefix}_quality-report.json"
    quality_report_path.write_text(json.dumps(quality_report, indent=2), encoding="utf-8")

    dims = quality_report.get("dimensions") or []
    actual_resolution = int(dims[0][0]) if dims else 0
    workflow_manifest["resolutionActual"] = actual_resolution
    workflow_manifest_path.write_text(json.dumps(workflow_manifest, indent=2), encoding="utf-8")

    if not quality_report.get("qualityGate", {}).get("passed", False):
        failures = quality_report.get("qualityGate", {}).get("failures", [])
        raise RuntimeError(
            f"Material Maker quality gate failed for '{preset}': {', '.join(map(str, failures))}"
        )
    if strict and actual_resolution != requested_resolution:
        raise RuntimeError(
            f"Material Maker renderer produced {actual_resolution}x{actual_resolution} for '{preset}'; "
            f"requested {requested_resolution}x{requested_resolution}"
        )

    for map_entry in config["textureContract"]["requiredMaps"]:
        suffix = str(map_entry["suffix"])
        expected_file = out_dir / f"{template_prefix}_{suffix}.png"
        if not expected_file.is_file():
            raise FileNotFoundError(
                f"Material Maker bake completed without required map: {expected_file}"
            )

    return {
        "preset": preset,
        "outputDirectory": str(out_dir),
        "resolution": actual_resolution,
        "elapsedSeconds": round(time.perf_counter() - t0, 3),
        "warnings": list(pack_report.get("warnings") or []),
    }


def cmd_pack_audit(args: argparse.Namespace) -> int:
    t0 = time.perf_counter()
    config = _load_validated_config(args.config)
    manifest = json.loads(args.batch_manifest.read_text(encoding="utf-8"))
    if not isinstance(manifest, list) or not manifest:
        raise ValueError("Batch manifest must be a non-empty JSON list.")

    detail_rgb = None
    if args.detail_normal is not None:
        if not args.detail_normal.is_file():
            raise FileNotFoundError(f"Detail normal map is missing: {args.detail_normal}")
        import numpy as np

        with Image.open(args.detail_normal) as det_im:
            detail_rgb = np.asarray(det_im.convert("RGB"), dtype=np.uint8)

    requested_resolution = 1 << args.size
    worker_count = max(1, min(args.workers, len(manifest)))
    results: list[dict[str, Any]] = []
    with ThreadPoolExecutor(max_workers=worker_count) as pool:
        future_map = {
            pool.submit(
                _process_single_pack_audit,
                entry,
                config=config,
                workflow_name=args.workflow,
                requested_resolution=requested_resolution,
                dilate_px=args.dilate_px,
                detail_rgb=detail_rgb,
                detail_strength=args.detail_strength,
                strict=args.strict,
                optimize_png=args.optimize_png,
            ): str(entry["preset"])
            for entry in manifest
        }
        for future in as_completed(future_map):
            results.append(future.result())

    results.sort(key=lambda item: item["preset"])
    total_elapsed = time.perf_counter() - t0
    summary = {
        "presetCount": len(results),
        "workers": worker_count,
        "resolution": requested_resolution,
        "optimizePng": bool(args.optimize_png),
        "elapsedSeconds": round(total_elapsed, 3),
        "results": results,
    }
    if args.summary_output is not None:
        args.summary_output.parent.mkdir(parents=True, exist_ok=True)
        args.summary_output.write_text(json.dumps(summary, indent=2), encoding="utf-8")

    print(
        f"Packed and audited {len(results)} Material Maker PBR sets ({requested_resolution}x{requested_resolution}) "
        f"using {worker_count} workers in {total_elapsed:.2f}s."
    )
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    prep = subparsers.add_parser("prepare", help="Validate config and prepare .ptex templates + output dirs.")
    prep.add_argument(
        "--config",
        type=Path,
        default=REPO_ROOT / "tools/material-maker/rogue-command-material-maker.json",
    )
    prep.add_argument("--batch-manifest", type=Path, required=True)
    prep.add_argument("--size", type=int, default=11)
    prep.add_argument("--force", action="store_true")
    prep.set_defaults(func=cmd_prepare)

    pack = subparsers.add_parser("pack-audit", help="Pack raw Material Maker outputs and run quality audits.")
    pack.add_argument(
        "--config",
        type=Path,
        default=REPO_ROOT / "tools/material-maker/rogue-command-material-maker.json",
    )
    pack.add_argument("--batch-manifest", type=Path, required=True)
    pack.add_argument("--workflow", default="procedural")
    pack.add_argument("--size", type=int, default=11)
    pack.add_argument("--dilate-px", type=int, default=0)
    pack.add_argument("--detail-normal", type=Path, default=None)
    pack.add_argument("--detail-strength", type=float, default=0.35)
    pack.add_argument("--workers", type=int, default=4)
    pack.add_argument("--strict", action="store_true")
    pack.add_argument("--optimize-png", action="store_true")
    pack.add_argument("--summary-output", type=Path, default=None)
    pack.set_defaults(func=cmd_pack_audit)

    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    return int(args.func(args))


if __name__ == "__main__":
    raise SystemExit(main())
