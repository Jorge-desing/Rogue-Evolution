"""Stage a composed content pack and its bundle files before publication."""
from __future__ import annotations

import json
import os
import ctypes
import errno
import hashlib
from pathlib import Path, PureWindowsPath
import shutil
import tempfile
import warnings

from tools.bundle_manifest_dependencies import merge_bundle_manifests
from tools.pack_bundle_transfer import copy_bundle_transfer, plan_bundle_transfer
from tools.secure_pack_read import open_pack_file
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


def _guarded_hash(root: Path, relative: str) -> str:
    digest = hashlib.sha256()
    with open_pack_file(root, relative) as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _make_stage_dir(parent: Path, name: str, *, exist_ok: bool = True) -> Path:
    """Create a single child while holding its parent, then reject reparse points."""
    with open_pack_file(parent.parent, parent.name, directory=True):
        child = parent / name
        child.mkdir(exist_ok=exist_ok)
        with open_pack_file(parent, name, directory=True):
            pass
        return child


def write_composed_pack(pack: dict, bundle_sources: list[tuple[Path, dict]], output: str | Path,
                        dependency_report: dict | None = None) -> Path:
    """Write a new pack directory; existing destinations are never intentionally replaced.

    Each bundle source supplies a complete manifest and declaration set. Sources
    with conflicting file bytes fail before publication. The returned pack is
    checked against the content schema and its referenced files.
    """
    output_path = Path(output).absolute()
    if not output_path.parent.name:
        raise ValueError("El directorio padre no puede ser la raíz del volumen")
    with open_pack_file(output_path.parent.parent, output_path.parent.name, directory=True):
        return _write_guarded(pack, bundle_sources, output_path, dependency_report)


def _write_guarded(pack: dict, bundle_sources: list[tuple[Path, dict]], output_path: Path,
                   dependency_report: dict | None = None) -> Path:
    if output_path.exists() or output_path.is_symlink():
        raise FileExistsError(f"El destino ya existe: {output_path}")
    if not output_path.parent.is_dir():
        raise ValueError("El directorio padre del destino debe existir")
    # Keep failed staging directories for inspection: automatic recursive cleanup
    # could remove files changed by another process after an error.
    temporary = tempfile.mkdtemp(prefix=".rogue-compose-", dir=output_path.parent)
    try:
        # Hold the staging parent across all writes and publication. On Windows
        # this prevents its rename or replacement while its path is in use.
        with open_pack_file(output_path.parent, Path(temporary).name, directory=True):
            stage = _make_stage_dir(Path(temporary), "pack", exist_ok=False)
            manifest_sources: dict[str, bytes] = {}
            for index, (root, source_pack) in enumerate(bundle_sources):
                isolated = _make_stage_dir(Path(temporary), f"source-{index}", exist_ok=False)
                copy_bundle_transfer(root, isolated, source_pack)
                with open_pack_file(isolated, source_pack["assetBundleManifestPath"]) as stream:
                    manifest_bytes = stream.read()
                manifest_sources.setdefault(hashlib.sha256(manifest_bytes).hexdigest(), manifest_bytes)
                for bundle in source_pack["assetBundles"]:
                    relative = PureWindowsPath(bundle["path"])
                    target = stage.joinpath(*relative.parts)
                    _make_stage_dir(stage, relative.parts[0])
                    if target.exists():
                        if _guarded_hash(stage, bundle["path"]) != _guarded_hash(isolated, bundle["path"]):
                            raise ValueError(f"Archivo de bundle en conflicto: {relative}")
                    else:
                        with open_pack_file(isolated, bundle["path"]) as input_stream, open_pack_file(stage, bundle["path"], create=True) as output_stream:
                            shutil.copyfileobj(input_stream, output_stream, 1024 * 1024)
            if manifest_sources:
                target_manifest = stage.joinpath(*PureWindowsPath(pack["assetBundleManifestPath"]).parts)
                _make_stage_dir(stage, PureWindowsPath(pack["assetBundleManifestPath"]).parts[0])
                if target_manifest.exists():
                    raise ValueError("La ruta del manifest coincide con un bundle")
                manifests = list(manifest_sources.values())
                manifest_bytes = manifests[0] if len(manifests) == 1 else merge_bundle_manifests(manifests)
                with open_pack_file(stage, pack["assetBundleManifestPath"], create=True) as output_stream:
                    output_stream.write(manifest_bytes)
            content_bytes = (json.dumps(pack, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
            with open_pack_file(stage, "content.json", create=True) as output_stream:
                output_stream.write(content_bytes)
            if dependency_report is not None:
                dependency_report = dict(dependency_report, contentSha256=hashlib.sha256(content_bytes).hexdigest())
                with open_pack_file(stage, "composition-dependencies.json", create=True) as output_stream:
                    output_stream.write((json.dumps(dependency_report, indent=2, ensure_ascii=False) + "\n").encode("utf-8"))
            errors = validate_pack(str(stage))
            if errors:
                raise ValueError("Pack compuesto inválido: " + "; ".join(errors))
            plan_bundle_transfer(stage, stage, pack)
            if output_path.exists() or output_path.is_symlink():
                raise FileExistsError(f"El destino apareció durante la composición: {output_path}")
            _publish_no_replace(stage, output_path)
    except Exception as exc:
        raise RuntimeError(f"Composición fallida; etapa conservada en {temporary}: {exc}") from exc
    # No source copies exist for a data-only pack, so removing the now-empty
    # staging parent is safe and atomic. Otherwise report the retained copies.
    try:
        Path(temporary).rmdir()
    except OSError:
        warnings.warn(f"Copias temporales conservadas para inspección: {temporary}", RuntimeWarning)
    return output_path
