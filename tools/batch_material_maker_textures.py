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
import os
from pathlib import Path
import re
import shutil
import sys
import time
import traceback
from typing import Any

REPO_ROOT = Path(__file__).resolve().parent.parent
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from PIL import Image

from tools.audit_material_maker_output import build_quality_report
from tools.pack_material_maker_pbr import pack_maps
from tools.pbr_texture_algorithms import validate_detail_strength
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
PRESET_NAME = re.compile(r"^[a-z0-9][a-z0-9_-]*$")


def _absolute_path(value: object, *, base: Path, label: str) -> Path:
    if not isinstance(value, (str, Path)) or not str(value).strip():
        raise ValueError(f"Batch manifest entry has an empty {label} path.")
    path = Path(value).expanduser()
    if not path.is_absolute():
        path = base / path
    return Path(os.path.abspath(path))


def _is_child_path(path: Path, root: Path) -> bool:
    try:
        relative = path.relative_to(root)
    except ValueError:
        return False
    return bool(relative.parts) and relative != Path(".")


def _assert_contained(path: Path, root: Path, *, label: str) -> None:
    if not _is_child_path(path, root):
        raise ValueError(f"Batch {label} must be below its authorized root '{root}': {path}")
    # Lexical containment alone is not enough when an existing junction/symlink
    # redirects output or cleanup to another directory.
    resolved_root = root.resolve(strict=False)
    resolved_path = path.resolve(strict=False)
    try:
        resolved_relative = resolved_path.relative_to(resolved_root)
    except ValueError as exc:
        raise ValueError(
            f"Batch {label} resolves outside its authorized root '{root}': {path} -> {resolved_path}"
        ) from exc
    if not resolved_relative.parts:
        raise ValueError(f"Batch {label} cannot be the authorized root itself: {path}")


def _has_reparse_point(path: Path) -> bool:
    if path.is_symlink():
        return True
    is_junction = getattr(path, "is_junction", None)
    if callable(is_junction) and is_junction():
        return True
    try:
        attributes = path.lstat().st_file_attributes
    except (AttributeError, FileNotFoundError):
        return False
    return bool(attributes & 0x400)  # FILE_ATTRIBUTE_REPARSE_POINT


def _assert_root_has_no_reparse_points(root: Path, *, label: str) -> None:
    current = Path(root.anchor)
    for part in root.parts[1:]:
        current = current / part
        if _has_reparse_point(current):
            raise ValueError(f"Refusing batch {label} through a symlink or junction: {current}")


def _assert_no_reparse_points_below(path: Path, root: Path, *, label: str) -> None:
    relative = path.relative_to(root)
    current = root
    for part in relative.parts:
        current = current / part
        if _has_reparse_point(current):
            raise ValueError(f"Refusing batch {label} through a symlink or junction: {current}")


def _load_batch_entries(
    manifest_path: Path,
    *,
    artifact_root: Path | None,
    template_root: Path | None,
) -> tuple[list[dict[str, Any]], Path, Path]:
    manifest_path = manifest_path.resolve(strict=True)
    base = manifest_path.parent
    output_root = Path(os.path.abspath(artifact_root or base))
    source_root = Path(os.path.abspath(template_root or base))
    _assert_root_has_no_reparse_points(output_root, label="artifact root")
    _assert_root_has_no_reparse_points(source_root, label="template root")
    output_root = output_root.resolve(strict=False)
    source_root = source_root.resolve(strict=False)

    raw_entries = json.loads(manifest_path.read_text(encoding="utf-8"))
    if not isinstance(raw_entries, list) or not raw_entries:
        raise ValueError("Batch manifest must be a non-empty JSON list.")

    entries: list[dict[str, Any]] = []
    presets: set[str] = set()
    templates: set[str] = set()
    output_paths: list[Path] = []
    for index, raw_entry in enumerate(raw_entries):
        if not isinstance(raw_entry, dict):
            raise ValueError(f"Batch manifest entry {index} must be a JSON object.")
        missing = [key for key in ("preset", "template", "outputDir", "rawDir") if key not in raw_entry]
        if missing:
            raise ValueError(f"Batch manifest entry {index} is missing: {', '.join(missing)}")

        preset_value = raw_entry["preset"]
        if not isinstance(preset_value, str):
            raise ValueError(f"Batch manifest entry {index} preset must be a string; got {type(preset_value).__name__}.")
        preset = preset_value
        if not PRESET_NAME.fullmatch(preset):
            raise ValueError(f"Batch manifest entry {index} has an invalid preset name: {preset!r}")
        if preset in presets:
            raise ValueError(f"Batch manifest contains duplicate preset: {preset}")
        presets.add(preset)

        template = _absolute_path(raw_entry["template"], base=base, label="template")
        output_dir = _absolute_path(raw_entry["outputDir"], base=base, label="outputDir")
        raw_dir = _absolute_path(raw_entry["rawDir"], base=base, label="rawDir")
        if template.suffix.lower() != ".ptex":
            raise ValueError(f"Batch template must be a .ptex file: {template}")
        _assert_contained(template, source_root, label="template")
        _assert_contained(output_dir, output_root, label="outputDir")
        _assert_contained(raw_dir, output_root, label="rawDir")
        if os.path.normcase(str(raw_dir)) != os.path.normcase(str(output_dir / "raw")):
            raise ValueError(f"Batch rawDir must be exactly '<outputDir>/raw': {raw_dir}")
        _assert_no_reparse_points_below(template, source_root, label="template")
        _assert_no_reparse_points_below(output_dir, output_root, label="output")
        _assert_no_reparse_points_below(raw_dir, output_root, label="raw output")

        template_key = os.path.normcase(str(template))
        output_key = os.path.normcase(str(output_dir))
        if template_key in templates:
            raise ValueError(f"Batch manifest reuses a template path: {template}")
        if any(output_key == os.path.normcase(str(existing)) for existing in output_paths):
            raise ValueError(f"Batch manifest reuses an output directory: {output_dir}")
        if any(
            _is_child_path(output_dir, existing) or _is_child_path(existing, output_dir)
            for existing in output_paths
        ):
            raise ValueError(f"Batch output directories cannot overlap or nest: {output_dir}")
        templates.add(template_key)
        output_paths.append(output_dir)

        entry = dict(raw_entry)
        entry.update(
            {
                "preset": preset,
                "template": template,
                "outputDir": output_dir,
                "rawDir": raw_dir,
            }
        )
        entries.append(entry)

    for entry in entries:
        template = Path(str(entry["template"]))
        for output_dir in output_paths:
            if template == output_dir or _is_child_path(template, output_dir):
                raise ValueError(
                    f"Batch template cannot be inside an output directory that may be cleaned: {template}"
                )
            if manifest_path == output_dir or _is_child_path(manifest_path, output_dir):
                raise ValueError(
                    f"Batch manifest cannot be inside a preset output directory that is cleaned: {manifest_path}"
                )

    # Do not create roots (or clean any preset output) until every manifest row
    # has passed path, collision, and reparse-point validation.
    output_root.mkdir(parents=True, exist_ok=True)
    source_root.mkdir(parents=True, exist_ok=True)
    _assert_root_has_no_reparse_points(output_root, label="artifact root")
    _assert_root_has_no_reparse_points(source_root, label="template root")
    if output_root.resolve(strict=True) != output_root or source_root.resolve(strict=True) != source_root:
        raise ValueError("Batch authorization roots changed while they were being validated.")
    return entries, output_root, source_root


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


def _validate_batch_options(
    *,
    size_power: int,
    dilate_px: int | None = None,
    detail_strength: object | None = None,
    workers: int | None = None,
) -> None:
    if isinstance(size_power, bool) or not isinstance(size_power, int) or not 5 <= size_power <= 12:
        raise ValueError(f"Material Maker size power must be an integer from 5 through 12; got {size_power!r}.")
    if dilate_px is not None and (
        isinstance(dilate_px, bool) or not isinstance(dilate_px, int) or dilate_px < 0
    ):
        raise ValueError(f"UV dilation radius must be an integer >= 0; got {dilate_px!r}.")
    if detail_strength is not None:
        validate_detail_strength(detail_strength)
    if workers is not None and (
        isinstance(workers, bool) or not isinstance(workers, int) or not 1 <= workers <= 8
    ):
        raise ValueError(f"Batch worker count must be an integer from 1 through 8; got {workers!r}.")


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
    _validate_batch_options(size_power=args.size)
    _load_validated_config(args.config)
    manifest, _, _ = _load_batch_entries(
        args.batch_manifest,
        artifact_root=args.artifact_root,
        template_root=args.template_root,
    )

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

    failures: list[str] = []
    quality_gate = quality_report.get("qualityGate", {})
    if not quality_gate.get("passed", False):
        gate_failures = [str(failure) for failure in quality_gate.get("failures", [])]
        failures.extend(f"quality gate: {failure}" for failure in gate_failures)
        if not gate_failures:
            failures.append("quality gate: marked failed without failure details")
    pack_warnings = [str(warning) for warning in pack_report.get("warnings", [])]
    if strict and pack_warnings:
        failures.extend(f"PBR pack warning: {warning}" for warning in pack_warnings)
    if strict and actual_resolution != requested_resolution:
        failures.append(
            f"resolution: renderer produced {actual_resolution}x{actual_resolution}; "
            f"requested {requested_resolution}x{requested_resolution}"
        )

    for map_entry in config["textureContract"]["requiredMaps"]:
        suffix = str(map_entry["suffix"])
        expected_file = out_dir / f"{template_prefix}_{suffix}.png"
        if not expected_file.is_file():
            failures.append(f"missing required map: {expected_file}")
    if failures:
        raise RuntimeError(f"Material Maker audit failed for '{preset}': " + "; ".join(failures))

    return {
        "preset": preset,
        "outputDirectory": str(out_dir),
        "resolution": actual_resolution,
        "elapsedSeconds": round(time.perf_counter() - t0, 3),
        "warnings": list(pack_report.get("warnings") or []),
    }


def cmd_pack_audit(args: argparse.Namespace) -> int:
    t0 = time.perf_counter()
    _validate_batch_options(
        size_power=args.size,
        dilate_px=args.dilate_px,
        detail_strength=args.detail_strength,
        workers=args.workers,
    )
    config = _load_validated_config(args.config)
    manifest, _, _ = _load_batch_entries(
        args.batch_manifest,
        artifact_root=args.artifact_root,
        template_root=args.template_root,
    )
    if args.workflow not in config.get("workflows", {}):
        raise ValueError(f"Material Maker workflow is missing from config: {args.workflow}")

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
    failures: list[str] = []
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
            preset = future_map[future]
            try:
                results.append(future.result())
            except Exception as exc:
                rendered_traceback = "".join(traceback.format_exception(exc)).rstrip()
                failures.append(f"--- preset '{preset}' failed ---\n{rendered_traceback}")
    if failures:
        raise RuntimeError(
            f"Batch PBR audit failed for {len(failures)} of {len(manifest)} preset(s):\n"
            + "\n".join(failures)
        )

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
    prep.add_argument("--artifact-root", type=Path, default=None)
    prep.add_argument("--template-root", type=Path, default=None)
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
    pack.add_argument("--artifact-root", type=Path, default=None)
    pack.add_argument("--template-root", type=Path, default=None)
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
