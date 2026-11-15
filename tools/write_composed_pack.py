"""Stage a composed content pack and its bundle files before publication."""
from __future__ import annotations

import json
import os
import ctypes
import errno
from pathlib import Path, PureWindowsPath
import shutil
import tempfile

from tools.bundle_manifest_dependencies import merge_bundle_manifests
from tools.pack_bundle_transfer import _sha256, copy_bundle_transfer, plan_bundle_transfer
from tools.validate_content_pack import validate_pack


def _publish_no_replace(source: Path, destination: Path) -> None:
    """Atomically publish without replacing a destination created concurrently."""
    if os.name == "nt":
        # Windows os.rename does not replace an existing destination.
        os.rename(source, destination)
        return
    if os.name == "posix":
        libc = ctypes.CDLL(None, use_errno=True)
        renameat2 = getattr(libc, "renameat2", None)
        if renameat2 is None:
            raise OSError(errno.ENOTSUP, "renameat2 no disponible para publicación sin reemplazo")
        renameat2.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]
        renameat2.restype = ctypes.c_int
        if renameat2(-100, os.fsencode(source), -100, os.fsencode(destination), 1) != 0:
            error = ctypes.get_errno()
            raise OSError(error, os.strerror(error), str(destination))
        return
    raise OSError(errno.ENOTSUP, "Publicación atómica sin reemplazo no soportada")


def write_composed_pack(pack: dict, bundle_sources: list[tuple[Path, dict]], output: str | Path) -> Path:
    """Write a new pack directory; existing destinations are never intentionally replaced.

    Each bundle source supplies a complete manifest and declaration set. Sources
    with conflicting file bytes fail before publication. The returned pack is
    checked against the content schema and its referenced files.
    """
    output_path = Path(output).absolute()
    if output_path.exists() or output_path.is_symlink():
        raise FileExistsError(f"El destino ya existe: {output_path}")
    if not output_path.parent.is_dir():
        raise ValueError("El directorio padre del destino debe existir")
    # Keep failed staging directories for inspection: automatic recursive cleanup
    # could remove files changed by another process after an error.
    temporary = tempfile.mkdtemp(prefix=".rogue-compose-", dir=output_path.parent)
    try:
        stage = Path(temporary) / "pack"
        stage.mkdir()
        manifest_sources: dict[str, Path] = {}
        for index, (root, source_pack) in enumerate(bundle_sources):
            isolated = Path(temporary) / f"source-{index}"
            isolated.mkdir()
            copy_bundle_transfer(root, isolated, source_pack)
            manifest_path = isolated.joinpath(*PureWindowsPath(source_pack["assetBundleManifestPath"]).parts)
            manifest_sources.setdefault(_sha256(manifest_path), manifest_path)
            for bundle in source_pack["assetBundles"]:
                relative = PureWindowsPath(bundle["path"])
                src = isolated.joinpath(*relative.parts)
                target = stage.joinpath(*relative.parts)
                target.parent.mkdir(parents=True, exist_ok=True)
                if target.exists():
                    if _sha256(target) != _sha256(src):
                        raise ValueError(f"Archivo de bundle en conflicto: {relative}")
                else:
                    shutil.copyfile(src, target)
        if manifest_sources:
            target_manifest = stage.joinpath(*PureWindowsPath(pack["assetBundleManifestPath"]).parts)
            target_manifest.parent.mkdir(parents=True, exist_ok=True)
            if target_manifest.exists():
                raise ValueError("La ruta del manifest coincide con un bundle")
            manifests = list(manifest_sources.values())
            target_manifest.write_bytes(manifests[0].read_bytes() if len(manifests) == 1 else merge_bundle_manifests(manifests))
        (stage / "content.json").write_text(json.dumps(pack, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        errors = validate_pack(str(stage))
        if errors:
            raise ValueError("Pack compuesto inválido: " + "; ".join(errors))
        plan_bundle_transfer(stage, stage, pack)
        if output_path.exists() or output_path.is_symlink():
            raise FileExistsError(f"El destino apareció durante la composición: {output_path}")
        _publish_no_replace(stage, output_path)
    except Exception as exc:
        raise RuntimeError(f"Composición fallida; etapa conservada en {temporary}: {exc}") from exc
    # After successful publication only the isolated copies remain. Keep them
    # as well, rather than recursively deleting content that may have changed.
    return output_path
