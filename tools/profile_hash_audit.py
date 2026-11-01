#!/usr/bin/env python3
"""Audita un perfil de jugador contra un checkpoint manifest.

Uso:
    python tools/profile_hash_audit.py <profile_dir> <manifest.json>
"""
import argparse
import hashlib
import json
import os
import sys
from pathlib import Path


def compute_file_hash(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(8192), b""):
            h.update(chunk)
    return h.hexdigest()


def audit_profile(profile_dir: str, manifest_path: str) -> dict:
    """Audita un perfil contra un manifest. Retorna dict con resultados."""
    profile_path = Path(profile_dir)
    manifest = json.loads(Path(manifest_path).read_text(encoding="utf-8"))

    expected = {}
    for entry in manifest.get("profileFiles", []):
        rel = entry["path"].replace("\\", "/")
        expected[rel] = {"size": entry["size"], "sha256": entry["sha256"]}

    actual = {}
    for p in profile_path.rglob("*"):
        if p.is_file():
            rel = p.relative_to(profile_path).as_posix()
            actual[rel] = {
                "size": p.stat().st_size,
                "sha256": compute_file_hash(p),
            }

    added = sorted(set(actual) - set(expected))
    missing = sorted(set(expected) - set(actual))
    changed = sorted(k for k in (set(actual) & set(expected)) if actual[k] != expected[k])

    return {
        "profile_dir": str(profile_path.resolve()),
        "manifest": str(Path(manifest_path).resolve()),
        "file_count_expected": len(expected),
        "file_count_actual": len(actual),
        "exact_match": actual == expected,
        "added": added,
        "missing": missing,
        "changed": changed,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Audita un perfil contra un checkpoint manifest")
    parser.add_argument("profile_dir", help="Directorio del perfil")
    parser.add_argument("manifest", help="Archivo manifest JSON")
    args = parser.parse_args()

    if not Path(args.profile_dir).is_dir():
        print(f"ERROR: directorio no encontrado: {args.profile_dir}", file=sys.stderr)
        return 1
    if not Path(args.manifest).is_file():
        print(f"ERROR: manifest no encontrado: {args.manifest}", file=sys.stderr)
        return 1

    result = audit_profile(args.profile_dir, args.manifest)
    print(json.dumps(result, indent=2, ensure_ascii=False))

    if result["exact_match"]:
        return 0
    else:
        return 1


if __name__ == "__main__":
    sys.exit(main())
