#!/usr/bin/env python3
"""Huella digital determinista de un content pack (sha256 de JSON canónico).

La función pura :func:`fingerprint_pack` calcula el hash SHA-256 del JSON
canónico del manifiesto; :func:`verify_fingerprint` lo compara con un
valor esperado. El CLI genera la huella o verifica contra un valor.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path
from typing import Any


def canonical_json(pack: dict) -> str:
    """JSON canónico: claves ordenadas, indentación fija, UTF-8."""
    return json.dumps(pack, sort_keys=True, indent=2, ensure_ascii=False) + "\n"


def fingerprint_pack(pack: dict) -> str:
    """SHA-256 (hex) del JSON canónico del manifiesto."""
    return hashlib.sha256(canonical_json(pack).encode("utf-8")).hexdigest()


def verify_fingerprint(pack: dict, expected: str) -> bool:
    """True si la huella del pack coincide con ``expected``."""
    if not isinstance(expected, str):
        return False
    return fingerprint_pack(pack) == expected.strip().lower()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Huella digital SHA-256 de un content pack"
    )
    parser.add_argument("--pack", required=True, help="Directorio o content.json")
    parser.add_argument("--expected", help="Huella esperada (verifica)")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    args = parser.parse_args(argv)

    pack_path = Path(args.pack)
    if pack_path.is_dir():
        pack_path = pack_path / "content.json"
    if not pack_path.is_file():
        print(f"Archivo no encontrado: {pack_path}", file=sys.stderr)
        return 2

    try:
        pack = json.loads(pack_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        print(f"JSON inválido: {exc}", file=sys.stderr)
        return 2

    fingerprint = fingerprint_pack(pack)
    if args.expected:
        ok = verify_fingerprint(pack, args.expected)
        if args.json:
            print(json.dumps({"fingerprint": fingerprint, "match": ok},
                             indent=2, ensure_ascii=False))
        else:
            print(f"fingerprint={fingerprint}")
            print(f"match={ok}")
            if not ok:
                print(f"expected={args.expected}", file=sys.stderr)
        return 0 if ok else 1

    if args.json:
        print(json.dumps({"fingerprint": fingerprint, "packId": pack.get("packId")},
                         indent=2, ensure_ascii=False))
    else:
        print(fingerprint)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
