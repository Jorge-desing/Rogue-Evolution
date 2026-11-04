#!/usr/bin/env python3
"""Prepare a disposable single-submesh UV-frozen 3D paint workspace for Material Maker 1.7.

Material Maker's 3D paint panel requires a mesh with UVs and a single submesh.
Production Rogue Command FBX models can have multiple submeshes, hierarchy sockets,
LODs, and RGBA vertex wear (`Color`). This tool creates a disposable paint proxy
under `artifacts/texture-work/<unit>/MaterialMaker/` (via Blender 4.5 headless when
a source `.blend`/`.fbx` is provided) alongside a reproducible `.ptex` template and
a paint handoff manifest, leaving production meshes untouched.
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import shutil
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
PREPARE_TEMPLATE_PATH = REPO_ROOT / "tools" / "Prepare-MaterialMakerTacticalTemplate.py"

BLENDER_CANDIDATES = [
    shutil.which("blender"),
    r"C:\Program Files\Blender Foundation\Blender 4.5\blender.exe",
]


def _load_template_builder():
    spec = importlib.util.spec_from_file_location(
        "prepare_mm_template", PREPARE_TEMPLATE_PATH
    )
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load {PREPARE_TEMPLATE_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def find_blender() -> str | None:
    for candidate in BLENDER_CANDIDATES:
        if candidate and Path(candidate).is_file():
            return str(candidate)
    return None


BLENDER_PROXY_SNIPPET = """
import sys
from pathlib import Path
import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
src = Path(argv[0])
dst_obj = Path(argv[1])
REPO = Path(argv[2])
sys.path.insert(0, str(REPO / "tools"))
import RogueModelKit as rmk

bpy.ops.wm.read_factory_settings(use_empty=True)
if src.suffix.lower() == ".blend":
    bpy.ops.wm.open_mainfile(filepath=str(src))
elif src.suffix.lower() == ".fbx":
    bpy.ops.import_scene.fbx(filepath=str(src))
else:
    raise RuntimeError(f"Unsupported source model extension: {src.suffix}")

meshes = [
    o for o in bpy.context.scene.objects
    if o.type == "MESH" and not o.name.endswith(("_COLLIDER", "_ShieldBubble"))
]
if not meshes:
    raise RuntimeError(f"No paintable mesh objects found in {src}")

rmk.unwrap_atlas_uvs(meshes, island_margin=0.01)
bpy.ops.object.select_all(action="DESELECT")
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()

merged = bpy.context.view_layer.objects.active
merged.name = dst_obj.stem
merged.data.materials.clear()
mat = bpy.data.materials.new(name=f"{dst_obj.stem}_PaintMat")
merged.data.materials.append(mat)

dst_obj.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action="DESELECT")
merged.select_set(True)
bpy.ops.wm.obj_export(
    filepath=str(dst_obj),
    export_selected_objects=True,
    export_uv=True,
    export_normals=True,
    export_materials=False,
)
print("EXPORTED_PAINT_PROXY", dst_obj)
"""


def prepare_3d_paint_workspace(
    unit_name: str,
    workspace_root: Path | None = None,
    source_model: Path | None = None,
    preset: str = "olive_armor",
    size_power: int = 11,
) -> dict[str, object]:
    workspace = (
        workspace_root
        if workspace_root is not None
        else REPO_ROOT / "artifacts" / "texture-work" / unit_name / "MaterialMaker"
    )
    workspace.mkdir(parents=True, exist_ok=True)

    template_mod = _load_template_builder()
    ptex_path = workspace / f"{unit_name}.ptex"
    project = template_mod.build_template_project(
        preset_name=preset, size_power=size_power
    )
    project["label"] = f"{unit_name} 3D Paint & Procedural Atlas"
    project["name"] = f"{unit_name.lower()}_paint"
    ptex_path.write_text(json.dumps(project, indent="\t"), encoding="utf-8")

    proxy_obj_path = workspace / f"{unit_name}_PaintProxy.obj"
    proxy_exported = False
    if source_model is not None:
        if not source_model.is_file():
            raise FileNotFoundError(f"Source model for 3D paint is missing: {source_model}")
        blender = find_blender()
        if blender is not None:
            runner = workspace / "_export_paint_proxy.py"
            runner.write_text(BLENDER_PROXY_SNIPPET, encoding="utf-8")
            try:
                proc = subprocess.run(
                    [
                        blender,
                        "-b",
                        "--python",
                        str(runner),
                        "--",
                        str(source_model.resolve()),
                        str(proxy_obj_path.resolve()),
                        str(REPO_ROOT.resolve()),
                    ],
                    capture_output=True,
                    text=True,
                    timeout=300,
                )
                if proc.returncode != 0 or not proxy_obj_path.is_file():
                    raise RuntimeError(
                        f"Blender paint proxy export failed:\n{proc.stdout[-1500:]}\n{proc.stderr[-1500:]}"
                    )
                proxy_exported = True
            finally:
                runner.unlink(missing_ok=True)

    manifest = {
        "unitName": unit_name,
        "workflow": "three_d",
        "preset": preset,
        "resolution": 1 << size_power,
        "ptexPath": str(ptex_path),
        "paintProxyMesh": str(proxy_obj_path) if proxy_exported else None,
        "singleSubmeshForPaint": True,
        "requiresUv": True,
        "productionMeshUntouched": True,
    }
    manifest_path = workspace / "paint-workspace.json"
    manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unit-name", required=True)
    parser.add_argument("--workspace", type=Path, default=None)
    parser.add_argument("--source-model", type=Path, default=None)
    parser.add_argument("--preset", default="olive_armor")
    parser.add_argument("--size", type=int, default=11, choices=[9, 10, 11, 12])
    args = parser.parse_args()

    manifest = prepare_3d_paint_workspace(
        unit_name=args.unit_name,
        workspace_root=args.workspace,
        source_model=args.source_model,
        preset=args.preset,
        size_power=args.size,
    )
    print(f"Prepared 3D paint workspace: {manifest['ptexPath']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
