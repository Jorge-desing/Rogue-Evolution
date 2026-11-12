#!/usr/bin/env python3
"""Clona un content pack con un nuevo namespace y packId.

La función pura :func:`clone_pack` reescribe cada ID namespaced
``old_ns:*`` a ``new_ns:*`` (unidades, fábricas, etapas, perfiles AI y todas
sus referencias) y fija el nuevo ``packId``. El CLI escribe el clon.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from tools.scaffold_pack import _validate_pack_id


def _namespace_of(pack_id: str) -> str:
    if not isinstance(pack_id, str) or ":" not in pack_id:
        return ""
    return pack_id.split(":", 1)[0]


def _remap_value(value: Any, old_ns: str, new_ns: str) -> Any:
    if isinstance(value, str) and value.startswith(old_ns + ":"):
        return new_ns + value[len(old_ns):]
    if isinstance(value, dict):
        return {k: _remap_value(v, old_ns, new_ns) for k, v in value.items()}
    if isinstance(value, list):
        return [_remap_value(v, old_ns, new_ns) for v in value]
    return value


def clone_pack(pack: dict, new_pack_id: str) -> tuple[dict, list[str]]:
    """Clona ``pack`` con ``new_pack_id``, remapeando IDs namespaced.

    Devuelve ``(cloned, diagnostics)``. Los diagnósticos informan cuando el
    pack no tiene un namespace extraíble o el nuevo packId es inválido.
    """
    diagnostics: list[str] = []
    if not isinstance(pack, dict):
        return {}, ["manifiesto raíz no es un objeto"]

    try:
        _validate_pack_id(new_pack_id)
    except ValueError as exc:
        return {}, [f"packId inválido: {exc}"]

    old_pack_id = pack.get("packId")
    old_ns = _namespace_of(old_pack_id if isinstance(old_pack_id, str) else "")
    new_ns = _namespace_of(new_pack_id)

    if not old_ns:
        diagnostics.append(
            "packId sin namespace extraíble; se fija new packId sin remapear IDs"
        )
        cloned = json.loads(json.dumps(pack))
        cloned["packId"] = new_pack_id
        return cloned, diagnostics

    cloned = _remap_value(pack, old_ns, new_ns)
    cloned["packId"] = new_pack_id
    return cloned, diagnostics


def write_cloned(cloned: dict, out_dir: str | Path) -> Path:
    out_path = Path(out_dir)
    out_path.mkdir(parents=True, exist_ok=True)
    target = out_path / "content.json"
    target.write_text(
        json.dumps(cloned, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    return target


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Clona un content pack con nuevo namespace/packId"
    )
    parser.add_argument("--pack", required=True, help="Directorio o content.json a clonar")
    parser.add_argument("--new-pack-id", required=True, help="Nuevo packId namespaced")
    parser.add_argument("--out-dir", required=True, help="Directorio destino")
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

    cloned, diagnostics = clone_pack(pack, args.new_pack_id)
    for d in diagnostics:
        print(f"  ! {d}", file=sys.stderr)
    if not cloned:
        return 1

    written = write_cloned(cloned, args.out_dir)
    print(f"Clon escrito en {written}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
