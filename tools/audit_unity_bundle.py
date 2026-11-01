#!/usr/bin/env python3
"""Audita un bundle de Unity usando UnityPy.

Uso:
    python tools/audit_unity_bundle.py <path_to_bundle> [--output report.json]
"""
import argparse
import json
import os
import sys
from collections import Counter
from pathlib import Path

import UnityPy


def audit_bundle(path: str) -> dict:
    """Audita un bundle de Unity y retorna un diccionario con el reporte."""
    env = UnityPy.load(path)
    type_counts = Counter()
    shaders = []
    textures = []
    dependencies = []

    for obj in env.objects:
        type_counts[obj.type.name] += 1

        if obj.type.name == "Shader":
            try:
                data = obj.read()
                shaders.append({
                    "name": getattr(data, "m_Name", "unknown"),
                    "path": getattr(data, "m_PathName", ""),
                })
            except Exception as e:
                shaders.append({"name": "ERROR", "error": str(e)})

        if obj.type.name == "Texture2D":
            try:
                data = obj.read()
                textures.append({
                    "name": getattr(data, "m_Name", "unknown"),
                    "width": getattr(data, "m_Width", 0),
                    "height": getattr(data, "m_Height", 0),
                    "format": str(getattr(data, "m_TextureFormat", "unknown")),
                    "mipmap_count": getattr(data, "m_MipCount", 0),
                })
            except Exception as e:
                textures.append({"name": "ERROR", "error": str(e)})

    # Dependencias externas (del objeto AssetBundle)
    try:
        for obj in env.objects:
            if obj.type.name == "AssetBundle":
                data = obj.read()
                deps = getattr(data, "m_Dependencies", [])
                for dep in deps:
                    dependencies.append(str(dep))
                break
    except Exception:
        pass

    try:
        bundle_size = os.path.getsize(path)
    except OSError:
        bundle_size = 0

    return {
        "bundle_path": str(Path(path).resolve()),
        "bundle_size_bytes": bundle_size,
        "object_count": sum(type_counts.values()),
        "type_counts": dict(type_counts.most_common()),
        "shader_count": len(shaders),
        "shaders": shaders,
        "texture_count": len(textures),
        "textures": textures,
        "dependency_count": len(dependencies),
        "dependencies": dependencies,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Audita un bundle de Unity usando UnityPy")
    parser.add_argument("bundle", help="Ruta al bundle de Unity")
    parser.add_argument("--output", "-o", help="Archivo de salida JSON (default: stdout)")
    args = parser.parse_args()

    if not os.path.isfile(args.bundle):
        print(f"ERROR: archivo no encontrado: {args.bundle}", file=sys.stderr)
        return 1

    try:
        report = audit_bundle(args.bundle)
    except Exception as e:
        print(f"ERROR al auditar bundle: {e}", file=sys.stderr)
        return 2

    output = json.dumps(report, indent=2, ensure_ascii=False)
    if args.output:
        Path(args.output).write_text(output, encoding="utf-8")
        print(f"Reporte guardado en: {args.output}")
    else:
        print(output)
    return 0


if __name__ == "__main__":
    sys.exit(main())
