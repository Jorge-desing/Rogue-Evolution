"""Plan safe transfer of the complete bundle set required by a Unity manifest."""
from __future__ import annotations

import hashlib
import os
from pathlib import Path, PureWindowsPath
import stat

from tools.bundle_manifest_dependencies import resolve_bundle_dependencies
from tools.secure_pack_read import open_pack_file
from tools.validate_content_pack import _reference_error


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _pack_sha256(root: Path, relative: str) -> str:
    digest = hashlib.sha256()
    with open_pack_file(root, relative) as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def plan_bundle_transfer(source_root: str | Path, destination_root: str | Path, pack: dict) -> list[dict]:
    """Check complete source manifest and destination conflicts without writing files.

    Each result identifies a pack-relative file, its SHA-256, and whether it is
    already present at the destination. The caller must recheck before copying.
    """
    source, destination = Path(source_root), Path(destination_root)
    if not source.is_dir() or not destination.is_dir():
        raise ValueError("Los directorios de origen y destino deben existir")
    for root in (source, destination):
        info = root.lstat()
        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError(f"La raíz del pack no puede ser un enlace: {root}")
    bundles = pack.get("assetBundles", [])
    manifest = pack.get("assetBundleManifestPath")
    if not bundles and not manifest:
        return []
    if not isinstance(bundles, list) or not bundles or not isinstance(manifest, str) or not manifest:
        raise ValueError("Se requieren manifest y declaraciones de bundles")
    paths = [bundle.get("path") if isinstance(bundle, dict) else None for bundle in bundles]
    if any(not isinstance(path, str) for path in paths) or len(set(paths)) != len(paths):
        raise ValueError("Rutas de bundles inválidas o duplicadas")
    for path in paths + [manifest]:
        error = _reference_error(source, path)
        if error:
            raise ValueError(error)
    names = [PureWindowsPath(path).name for path in paths]
    if len(set(names)) != len(names):
        raise ValueError("Nombres de bundles duplicados")
    with open_pack_file(source, manifest) as stream:
        resolve_bundle_dependencies(stream.read(), names, require_exact=True)
    result = []
    for relative in paths + [manifest]:
        parts = PureWindowsPath(relative).parts
        target = destination
        for part in parts:
            target /= part
            try:
                info = target.lstat()
            except FileNotFoundError:
                continue
            if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT:
                raise ValueError(f"Enlace o punto de reanálisis no permitido: {relative}")
        digest = hashlib.sha256()
        with open_pack_file(source, relative) as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)
        source_hash = digest.hexdigest()
        exists = target.exists()
        if exists and (not target.is_file() or _pack_sha256(destination, relative) != source_hash):
            raise ValueError(f"Archivo de destino en conflicto: {relative}")
        result.append({"path": relative, "sha256": source_hash, "alreadyPresent": exists})
    return result


def copy_bundle_transfer(source_root: str | Path, destination_root: str | Path, pack: dict) -> list[dict]:
    """Copy a checked bundle set without replacing existing files; report retained files on failure."""
    source, destination = Path(source_root), Path(destination_root)
    plan = plan_bundle_transfer(source, destination, pack)
    created_files: list[tuple[Path, tuple[int, int], str | None]] = []
    created_dirs: list[Path] = []
    try:
        for row in plan:
            if row["alreadyPresent"]:
                continue
            # Recheck all paths and hashes immediately before each write.
            current = plan_bundle_transfer(source, destination, pack)
            check = next(item for item in current if item["path"] == row["path"])
            if check["sha256"] != row["sha256"] or check["alreadyPresent"]:
                raise ValueError(f"El archivo cambió durante la transferencia: {row['path']}")
            parts = PureWindowsPath(row["path"]).parts
            target = destination
            for part in parts[:-1]:
                # Lock the existing parent while creating the child. A second
                # guarded open below rejects a child replaced with a link.
                parent = target
                with open_pack_file(parent.parent, parent.name, directory=True):
                    target = parent / part
                    try:
                        target.mkdir()
                        created_dirs.append(target)
                    except FileExistsError:
                        pass
                    with open_pack_file(parent, part, directory=True):
                        pass
            target /= parts[-1]
            with open_pack_file(source, row["path"]) as input_stream, open_pack_file(destination, row["path"], create=True) as output_stream:
                info = os.fstat(output_stream.fileno())
                created_files.append((target, (info.st_dev, info.st_ino), None))
                for block in iter(lambda: input_stream.read(1024 * 1024), b""):
                    output_stream.write(block)
                output_stream.flush()
                os.fsync(output_stream.fileno())
            if _pack_sha256(destination, row["path"]) != row["sha256"]:
                raise ValueError(f"El archivo cambió durante la copia: {row['path']}")
            created_files[-1] = (target, (info.st_dev, info.st_ino), row["sha256"])
        final = plan_bundle_transfer(source, destination, pack)
        if any(not item["alreadyPresent"] or item["sha256"] != original["sha256"]
               for item, original in zip(final, plan)):
            raise ValueError("El conjunto de bundles cambió durante la transferencia")
        return plan
    except Exception as original:
        # Never unlink by pathname after an exception: a different process can
        # replace it between an identity check and deletion. Retain all created
        # paths and report them for inspection.
        retained = [str(path) for path, _, _ in created_files]
        retained.extend(str(path) for path in created_dirs)
        if retained:
            raise RuntimeError(f"Reversión incompleta; archivos conservados: {', '.join(retained)}; error original: {original}") from original
        raise
