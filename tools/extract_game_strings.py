#!/usr/bin/env python3
"""Extrae strings legibles de un archivo binario del juego (DLL, bundle).

Uso:
    python tools/extract_game_strings.py <file> [--min-length 4] [--output strings.txt]
"""
import argparse
import os
import re
import sys
from pathlib import Path


def extract_strings(path: str, min_length: int = 4) -> list[str]:
    """Extrae strings ASCII y Unicode de un archivo binario."""
    data = Path(path).read_bytes()
    ascii_pattern = re.compile(rb"[\x20-\x7e]{" + str(min_length).encode() + rb",}")
    ascii_strings = [s.decode("ascii", errors="replace") for s in ascii_pattern.findall(data)]

    unicode_pattern = re.compile(rb"(?:[\x20-\x7e]\x00){" + str(min_length).encode() + rb",}")
    unicode_raw = unicode_pattern.findall(data)
    unicode_strings = []
    for raw in unicode_raw:
        try:
            unicode_strings.append(raw.decode("utf-16-le", errors="replace"))
        except Exception:
            pass

    seen = set()
    result = []
    for s in ascii_strings + unicode_strings:
        if s not in seen:
            seen.add(s)
            result.append(s)
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description="Extrae strings de un archivo binario")
    parser.add_argument("file", help="Archivo binario")
    parser.add_argument("--min-length", type=int, default=4, help="Longitud mínima de string (default: 4)")
    parser.add_argument("--output", "-o", help="Archivo de salida (default: stdout)")
    args = parser.parse_args()

    if not os.path.isfile(args.file):
        print(f"ERROR: archivo no encontrado: {args.file}", file=sys.stderr)
        return 1

    strings = extract_strings(args.file, args.min_length)
    output = "\n".join(strings)
    if args.output:
        Path(args.output).write_text(output, encoding="utf-8")
        print(f"Extraídos {len(strings)} strings -> {args.output}")
    else:
        print(output)
    return 0


if __name__ == "__main__":
    sys.exit(main())
