"""Extract UV triangle regions and material color metadata from Level 1 FBXs."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import sys

import bpy
import numpy as np

REGION_GUTTER = 0
REGION_PRIMARY_ARMOR = 1
REGION_LIGHT_ARMOR = 2
REGION_GUNMETAL = 3
REGION_TREAD = 4
REGION_WHEEL_METAL = 5
REGION_WARNING = 6
REGION_EMISSIVE = 7


def _linear_to_srgb_u8(val: float) -> int:
    c = max(0.0, min(1.0, float(val)))
    if c <= 0.0031308:
        srgb = 12.92 * c
    else:
        srgb = 1.055 * (c ** (1.0 / 2.4)) - 0.055
    return int(round(max(0.0, min(1.0, srgb)) * 255.0))


def classify_material_region(material_name: str) -> int:
    spaced = re.sub(r"([a-z0-9])([A-Z])", r"\1 \2", material_name or "")
    key = spaced.strip().lower().replace("_", " ").replace("-", " ")
    tokens = set(re.findall(r"[a-z0-9]+", key))
    if tokens & {"emissive", "sensor", "optic", "visor", "beacon"} or ("arc" in tokens and "cyan" in tokens):
        return REGION_EMISSIVE
    if tokens & {"warning", "signal", "amber", "hazard"}:
        return REGION_WARNING
    if tokens & {"tread", "tire", "track", "rubber"} or ("workshop" in tokens and "floor" in tokens):
        return REGION_TREAD
    if tokens & {"wheel", "wheelrim", "wheelhub", "rim", "hub"}:
        return REGION_WHEEL_METAL
    if "light" in tokens and "armor" in tokens:
        return REGION_LIGHT_ARMOR
    if tokens & {"gunmetal", "machined", "hardware", "barrel", "steel", "chassis"}:
        return REGION_GUNMETAL
    return REGION_PRIMARY_ARMOR


def _extract_material_colors(mat: bpy.types.Material | None) -> tuple[list[int], list[int]]:
    base_rgb = [140, 148, 156]
    emit_rgb = [0, 0, 0]
    if mat is None:
        return base_rgb, emit_rgb
    if mat.use_nodes and mat.node_tree is not None:
        bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf is not None:
            if "Base Color" in bsdf.inputs:
                col = bsdf.inputs["Base Color"].default_value
                base_rgb = [_linear_to_srgb_u8(col[0]), _linear_to_srgb_u8(col[1]), _linear_to_srgb_u8(col[2])]
            emit_input = bsdf.inputs.get("Emission Color") or bsdf.inputs.get("Emission")
            strength_input = bsdf.inputs.get("Emission Strength")
            if emit_input is not None:
                ecol = emit_input.default_value
                strength = float(strength_input.default_value) if strength_input is not None else 1.0
                if strength > 0.0 and (ecol[0] > 0.005 or ecol[1] > 0.005 or ecol[2] > 0.005):
                    # Preserve vibrant hue for emission texture in [0..255]
                    emit_rgb = [
                        _linear_to_srgb_u8(min(1.0, ecol[0] * min(2.0, max(1.0, strength * 0.5)))),
                        _linear_to_srgb_u8(min(1.0, ecol[1] * min(2.0, max(1.0, strength * 0.5)))),
                        _linear_to_srgb_u8(min(1.0, ecol[2] * min(2.0, max(1.0, strength * 0.5)))),
                    ]
    else:
        col = mat.diffuse_color
        base_rgb = [_linear_to_srgb_u8(col[0]), _linear_to_srgb_u8(col[1]), _linear_to_srgb_u8(col[2])]
    return base_rgb, emit_rgb


def extract_unit_uv_regions(unit_name: str, models_dir: Path, output_dir: Path) -> None:
    fbx_path = models_dir / f"{unit_name}.fbx"
    if not fbx_path.is_file():
        fbx_path = models_dir / f"{unit_name}_LODs.fbx"
    if not fbx_path.is_file():
        raise FileNotFoundError(f"Missing FBX for UV extraction: {models_dir / unit_name}.fbx")

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(fbx_path))

    meshes = [
        obj for obj in bpy.context.scene.objects
        if obj.type == "MESH"
        and "_COLLIDER" not in obj.name.upper()
        and "_SHIELDBUBBLE" not in obj.name.upper()
        and "LOD1" not in obj.name.upper()
        and "LOD2" not in obj.name.upper()
    ]
    if not meshes:
        raise ValueError(f"No LOD0 mesh objects found in {fbx_path}")

    triangles: list[list[float]] = []
    regions: list[int] = []
    region_colors: dict[int, list[int]] = {}
    emissive_colors: list[list[int]] = []
    material_names: dict[str, int] = {}

    for obj in meshes:
        mesh = obj.data
        uv_layer = mesh.uv_layers.active
        if uv_layer is None:
            continue
        mats = list(mesh.materials)
        slot_regions: dict[int, int] = {}
        for idx, mat in enumerate(mats):
            mat_name = mat.name if mat else obj.name
            reg = classify_material_region(mat_name)
            slot_regions[idx] = reg
            material_names[mat_name] = reg
            base_rgb, emit_rgb = _extract_material_colors(mat)
            region_colors.setdefault(reg, base_rgb)
            if reg == REGION_EMISSIVE and any(v > 10 for v in emit_rgb):
                emissive_colors.append(emit_rgb)
        if not slot_regions:
            reg = classify_material_region(obj.name)
            slot_regions[0] = reg
            material_names[obj.name] = reg

        mesh.calc_loop_triangles()
        uvs = uv_layer.data
        for tri in mesh.loop_triangles:
            reg = slot_regions.get(tri.material_index, REGION_PRIMARY_ARMOR)
            li0, li1, li2 = tri.loops
            uv0, uv1, uv2 = uvs[li0].uv, uvs[li1].uv, uvs[li2].uv
            triangles.append([
                float(uv0.x), float(uv0.y),
                float(uv1.x), float(uv1.y),
                float(uv2.x), float(uv2.y),
            ])
            regions.append(reg)

    if not triangles:
        raise ValueError(f"No UV triangles extracted from {fbx_path}")

    output_dir.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(
        output_dir / f"{unit_name}_uv_triangles.npz",
        triangles=np.asarray(triangles, dtype=np.float32),
        regions=np.asarray(regions, dtype=np.uint8),
    )

    if REGION_WARNING not in region_colors:
        region_colors[REGION_WARNING] = [226, 148, 28]
    if not emissive_colors:
        emissive_colors.append(region_colors.get(REGION_EMISSIVE, [64, 210, 245]))
    # Ensure emissive RGB is vivid if BSDF Emission Color wasn't set bright in imported FBX
    primary_emit = emissive_colors[0]
    if max(primary_emit) < 80:
        cand = region_colors.get(REGION_EMISSIVE, [64, 210, 245])
        primary_emit = cand if max(cand) >= 80 else [64, 210, 245]

    meta = {
        "unitName": unit_name,
        "sourceFbx": str(fbx_path.name),
        "triangleCount": len(triangles),
        "materials": material_names,
        "regionColors": {str(k): v for k, v in sorted(region_colors.items())},
        "emissiveRgb": primary_emit,
    }
    (output_dir / f"{unit_name}_uv_meta.json").write_text(
        json.dumps(meta, indent=2), encoding="utf-8"
    )
    print(f"[uv-extract] {unit_name}: {len(triangles)} UV tris | regions={sorted(set(regions))}")


def main() -> None:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--models-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("units", nargs="+")
    args = parser.parse_args(argv)
    for unit_name in args.units:
        extract_unit_uv_regions(unit_name, args.models_dir, args.output_dir)


if __name__ == "__main__":
    main()
