#!/usr/bin/env python3
"""Audit Material Maker outputs against the Rogue Command visual contracts.

This is an authoring-side report for URP, disposable 3D-paint, and procedural
Material Maker workflows. It never imports a mesh into Unity and never changes
production FBX/Blend files. The output is JSON-friendly so an agent can consume
metrics without a screenshot. Pass `--template <path.ptex>` when the graph lives
outside the output directory and `--workspace <paint-dir>` for a 3D-paint manifest.
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Any

from PIL import Image, ImageStat

REQUIRED_MAPS = ("Albedo", "MetallicGloss", "Normal", "Emission")
SAMPLE_SIZE = 64


def _sample(image: Image.Image) -> Image.Image:
    return image.resize((min(SAMPLE_SIZE, image.width), min(SAMPLE_SIZE, image.height)), Image.Resampling.BILINEAR)


def _pixels(image: Image.Image) -> list[tuple[int, ...]]:
    sampled = _sample(image)
    getter = getattr(sampled, "get_flattened_data", None)
    raw: Any = getter() if callable(getter) else sampled.getdata()
    return [tuple(pixel) for pixel in raw]


def _channel_stats(image: Image.Image) -> dict[str, list[float]]:
    sampled = _sample(image)
    stats = ImageStat.Stat(sampled)
    return {
        "mean": [round(value, 3) for value in stats.mean],
        "stddev": [round(value, 3) for value in stats.stddev],
        "min": [value for value, _ in stats.extrema],
        "max": [value for _, value in stats.extrema],
    }


def _normal_metrics(image: Image.Image) -> dict[str, float]:
    pixels = _pixels(image.convert("RGB"))
    length_error = 0.0
    z_non_positive = 0
    for red, green, blue in pixels:
        x = red / 127.5 - 1.0
        y = green / 127.5 - 1.0
        z = blue / 127.5 - 1.0
        length_error += abs(math.sqrt(x * x + y * y + z * z) - 1.0)
        z_non_positive += z <= 0.0
    count = max(1, len(pixels))
    return {
        "normalLengthMeanError": round(length_error / count, 6),
        "normalZNonPositiveFraction": round(z_non_positive / count, 6),
    }


def _metallic_gloss_metrics(image: Image.Image) -> dict[str, float | int]:
    pixels = _pixels(image.convert("RGBA"))
    max_delta = 0
    alpha_nonzero = 0
    for red, green, blue, alpha in pixels:
        max_delta = max(max_delta, abs(red - green), abs(red - blue), abs(green - blue))
        alpha_nonzero += alpha > 0
    count = max(1, len(pixels))
    return {
        "rgbMaxDelta": max_delta,
        "smoothnessAlphaNonzeroFraction": round(alpha_nonzero / count, 6),
    }


def _read_json_if_present(path: Path) -> dict[str, Any] | None:
    if not path.is_file():
        return None
    value = json.loads(path.read_text(encoding="utf-8"))
    return value if isinstance(value, dict) else None


def _find_ptex(directory: Path, prefix: str, template_path: Path | None = None) -> Path | None:
    if template_path is not None and template_path.is_file():
        return template_path
    preferred = directory / f"{prefix}.ptex"
    if preferred.is_file():
        return preferred
    candidates = sorted(directory.glob("*.ptex"))
    return candidates[0] if candidates else None


def build_quality_report(
    directory: Path,
    prefix: str,
    template_path: Path | None = None,
    workspace_path: Path | None = None,
) -> dict[str, Any]:
    directory = Path(directory)
    paths = {suffix: directory / f"{prefix}_{suffix}.png" for suffix in REQUIRED_MAPS}
    missing = [str(path) for path in paths.values() if not path.is_file()]
    if missing:
        raise FileNotFoundError("Missing required Material Maker maps: " + ", ".join(missing))

    maps: dict[str, dict[str, Any]] = {}
    dimensions: set[tuple[int, int]] = set()
    for suffix, path in paths.items():
        with Image.open(path) as image:
            dimensions.add(image.size)
            entry: dict[str, Any] = {
                "path": str(path),
                "size": [image.width, image.height],
                "mode": image.mode,
                "channels": len(image.getbands()),
                "stats": _channel_stats(image.convert("RGBA" if suffix == "MetallicGloss" else "RGB")),
            }
            if suffix == "MetallicGloss":
                entry.update(_metallic_gloss_metrics(image))
            elif suffix == "Normal":
                entry.update(_normal_metrics(image))
            elif suffix == "Emission":
                pixels = _pixels(image.convert("RGB"))
                nonzero = sum(1 for pixel in pixels if max(pixel) > 0)
                entry["nonzeroFraction"] = round(nonzero / max(1, len(pixels)), 6)
            maps[suffix] = entry

    ptex_path = _find_ptex(directory, prefix, template_path)
    ptex = _read_json_if_present(ptex_path) if ptex_path else None
    nodes = ptex.get("nodes", []) if ptex else []
    connections = ptex.get("connections", []) if ptex else []
    node_names = {str(node.get("name")) for node in nodes if isinstance(node, dict)}
    bimodal_connected = any(
        isinstance(connection, dict)
        and connection.get("from") == "colorize_metallic"
        and connection.get("to") == "Material"
        and connection.get("to_port") == 1
        for connection in connections
    )
    emission_connected = any(
        isinstance(connection, dict)
        and connection.get("from") == "colorize_emission"
        and connection.get("to") == "Material"
        and connection.get("to_port") == 3
        for connection in connections
    )
    edge_emission_connected = any(
        isinstance(connection, dict)
        and connection.get("from") == "edge_detect_emission"
        and connection.get("to") == "colorize_emission"
        and connection.get("to_port") == 0
        for connection in connections
    )
    material_node = next(
        (node for node in nodes if isinstance(node, dict) and node.get("name") == "Material"),
        {},
    )
    material_parameters = material_node.get("parameters", {}) if isinstance(material_node, dict) else {}

    workspace_directory = Path(workspace_path) if workspace_path is not None else directory
    workspace = _read_json_if_present(workspace_directory / "paint-workspace.json") or {}
    three_d = {
        "manifestPresent": bool(workspace),
        "workflow": workspace.get("workflow", ""),
        "requiresUv": bool(workspace.get("requiresUv", False)),
        "singleSubmeshForPaint": bool(workspace.get("singleSubmeshForPaint", False)),
        "productionMeshUntouched": bool(workspace.get("productionMeshUntouched", False)),
        "paintProxyMesh": workspace.get("paintProxyMesh"),
    }
    procedural = {
        "ptexPresent": ptex is not None,
        "nodeCount": len(nodes) if isinstance(nodes, list) else 0,
        "connectionCount": len(connections) if isinstance(connections, list) else 0,
        "hasBimodalMetallicNode": "colorize_metallic" in node_names,
        "bimodalMetallicConnected": bimodal_connected,
        "hasEmissionNode": "colorize_emission" in node_names,
        "emissionConnected": emission_connected,
        "hasEdgeEmissionNode": "edge_detect_emission" in node_names,
        "edgeEmissionConnected": edge_emission_connected,
        "emissionEnergy": material_parameters.get("emission_energy", 0),
        "normalNodePresent": "normal_map_2" in node_names,
    }

    failures: list[str] = []
    if len(dimensions) != 1:
        failures.append("map-dimensions-differ")
    if maps["MetallicGloss"].get("rgbMaxDelta", 1) != 0:
        failures.append("metallic-gloss-rgb-not-identical")
    if maps["MetallicGloss"].get("smoothnessAlphaNonzeroFraction", 0) <= 0:
        failures.append("metallic-gloss-alpha-empty")
    if maps["Normal"].get("normalLengthMeanError", 1) > 0.12:
        failures.append("normal-vectors-not-renormalized")
    if maps["Normal"].get("normalZNonPositiveFraction", 1) > 0.02:
        failures.append("normal-z-has-invalid-negative-area")
    if ptex is not None and not bimodal_connected:
        failures.append("procedural-metallic-graph-not-connected")
    if ptex is not None and not emission_connected:
        failures.append("procedural-emission-graph-not-connected")
    if ptex is not None and not edge_emission_connected:
        failures.append("procedural-edge-emission-graph-not-connected")
    if ptex is not None and float(material_parameters.get("emission_energy", 0)) <= 0.0:
        failures.append("procedural-emission-energy-not-positive")
    if workspace and not three_d["singleSubmeshForPaint"]:
        failures.append("3d-paint-proxy-not-single-submesh")
    if workspace and not three_d["productionMeshUntouched"]:
        failures.append("3d-paint-production-mesh-not-preserved")

    workflow_manifest = _read_json_if_present(directory / "material-maker-workflow.json") or {}
    return {
        "kind": "materialMakerQualityReport",
        "prefix": prefix,
        "workflow": workflow_manifest.get("workflow", workspace.get("workflow", "unknown")),
        "maps": maps,
        "dimensions": [list(value) for value in sorted(dimensions)],
        "procedural": procedural,
        "threeDPaint": three_d,
        "qualityGate": {
            "passed": not failures,
            "failureCount": len(failures),
            "failures": failures,
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, required=True)
    parser.add_argument("--prefix", required=True)
    parser.add_argument("--template", type=Path, default=None)
    parser.add_argument("--workspace", type=Path, default=None)
    parser.add_argument("--output", type=Path, default=None)
    args = parser.parse_args()
    report = build_quality_report(
        args.directory,
        args.prefix,
        template_path=args.template,
        workspace_path=args.workspace,
    )
    output = args.output or args.directory / f"{args.prefix}_quality-report.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0 if report["qualityGate"]["passed"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
