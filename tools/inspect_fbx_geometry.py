"""Inventory Blender face counts and exported Unity triangle counts from FBX files.

Run with the repository-pinned Blender 4.5.14 executable:
    blender --background --python tools/inspect_fbx_geometry.py -- --root <path> --output <json>
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import sys
from typing import Any

import bpy


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _clear_imported_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)


def inspect_fbx(path: Path) -> dict[str, Any]:
    _clear_imported_scene()
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    meshes = [
        obj for obj in bpy.context.scene.objects
        if obj.type == "MESH" and "_COLLIDER_" not in obj.name
    ]
    faces = 0
    triangles = 0
    vertices = 0
    for obj in meshes:
        obj.data.calc_loop_triangles()
        faces += len(obj.data.polygons)
        triangles += len(obj.data.loop_triangles)
        vertices += len(obj.data.vertices)
    if not meshes or faces <= 0 or triangles <= 0:
        raise ValueError(f"FBX has no renderable mesh geometry: {path}")
    return {
        "assetId": path.stem,
        "sourcePath": str(path),
        "sourceFaces": faces,
        "sourceTriangles": triangles,
        "meshObjects": len(meshes),
        "vertices": vertices,
        "sha256": _sha256(path),
    }


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, action="append", required=True, help="Directory containing FBX assets; repeatable")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--include-atlas", action="store_true", help="Include *_Atlas.fbx assets")
    args = parser.parse_args(argv)
    files: list[Path] = []
    for root in args.root:
        if not root.is_dir():
            parser.error(f"FBX inventory root is not a directory: {root}")
        files.extend(root.rglob("*.fbx"))
    files = sorted({path.resolve() for path in files if args.include_atlas or not path.stem.endswith("_Atlas")})
    if not files:
        parser.error("no FBX files were found")
    args.files = files
    return args


def main(argv: list[str] | None = None) -> int:
    raw_args = argv if argv is not None else sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    args = parse_args(raw_args)
    rows = []
    for index, path in enumerate(args.files, 1):
        try:
            rows.append(inspect_fbx(path))
        except Exception as exc:
            raise RuntimeError(f"[{index}/{len(args.files)}] failed to inspect {path}: {exc}") from exc
        if index % 10 == 0 or index == len(args.files):
            print(f"[fbx-geometry] {index}/{len(args.files)} inspected", flush=True)
    output = {
        "schemaVersion": 1,
        "blenderVersion": bpy.app.version_string,
        "files": rows,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(output, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"wrote {len(rows)} FBX geometry rows: {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
