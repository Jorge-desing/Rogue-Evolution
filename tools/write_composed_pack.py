"""Stage a composed content pack and its bundle files before publication."""
from __future__ import annotations

import json
import os
import ctypes
import errno
import hashlib
from pathlib import Path, PureWindowsPath
import shutil
import secrets
import warnings
from contextlib import contextmanager

from tools.bundle_manifest_dependencies import merge_bundle_manifests
from tools.pack_bundle_transfer import copy_bundle_transfer, plan_bundle_transfer
from tools.secure_pack_read import open_pack_file
from tools.validate_content_pack import validate_pack


def _publish_windows_verified(source: Path, destination: Path, identity: tuple[int, int] | None) -> None:
    """Rename the exclusively guarded stage handle after checking its original identity."""
    from ctypes import wintypes
    class FileInfo(ctypes.Structure):
        _fields_ = [("attributes", wintypes.DWORD), ("creation", wintypes.FILETIME),
                    ("access", wintypes.FILETIME), ("write", wintypes.FILETIME),
                    ("volume", wintypes.DWORD), ("size_high", wintypes.DWORD),
                    ("size_low", wintypes.DWORD), ("links", wintypes.DWORD),
                    ("index_high", wintypes.DWORD), ("index_low", wintypes.DWORD)]
    class RenameInfo(ctypes.Structure):
        _fields_ = [("replace", wintypes.BOOL), ("root", wintypes.HANDLE),
                    ("length", wintypes.DWORD), ("name", wintypes.WCHAR * 1)]
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p,
                                   wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
    kernel.CreateFileW.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    handle = kernel.CreateFileW(str(source), 0x80010000, 3, None, 3, 0x02200000, None)
    if handle == ctypes.c_void_p(-1).value:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        kernel.GetFileInformationByHandle.argtypes = [wintypes.HANDLE, ctypes.POINTER(FileInfo)]
        info = FileInfo()
        if not kernel.GetFileInformationByHandle(handle, ctypes.byref(info)):
            raise ctypes.WinError(ctypes.get_last_error())
        current = (info.volume, (info.index_high << 32) | info.index_low)
        if not info.attributes & 0x10 or info.attributes & 0x400:
            raise ValueError("La etapa no es un directorio regular")
        if identity is not None and current != (identity[0] & 0xffffffff, identity[1]):
            raise ValueError("La etapa fue reemplazada durante la composición")
        encoded = str(destination).encode("utf-16-le")
        # Reserve a UTF-16 terminator: on Windows a non-terminated buffer can
        # produce a destination name with stray trailing characters.
        buffer = ctypes.create_string_buffer(RenameInfo.name.offset + len(encoded) + 2)
        rename = RenameInfo.from_buffer(buffer)
        rename.replace = False
        rename.root = None
        rename.length = len(encoded)
        ctypes.memmove(ctypes.addressof(buffer) + RenameInfo.name.offset, encoded, len(encoded))
        kernel.SetFileInformationByHandle.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
        kernel.SetFileInformationByHandle.restype = wintypes.BOOL
        if not kernel.SetFileInformationByHandle(handle, 3, buffer, len(buffer)):
            raise ctypes.WinError(ctypes.get_last_error())
    finally:
        kernel.CloseHandle(handle)


def _publish_no_replace(source: Path, destination: Path, identity: tuple[int, int] | None = None) -> None:
    """Atomically publish without replacing a destination created concurrently."""
    if os.name == "nt":
        _publish_windows_verified(source, destination, identity)
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


def _remove_empty_staging_parent(path: Path, identity: tuple[int, int]) -> bool:
    """Remove only the original empty Windows directory, through its open handle."""
    if os.name != "nt":
        return False
    from ctypes import wintypes
    class FileInfo(ctypes.Structure):
        _fields_ = [("attributes", wintypes.DWORD), ("creation", wintypes.FILETIME),
                    ("access", wintypes.FILETIME), ("write", wintypes.FILETIME),
                    ("volume", wintypes.DWORD), ("size_high", wintypes.DWORD),
                    ("size_low", wintypes.DWORD), ("links", wintypes.DWORD),
                    ("index_high", wintypes.DWORD), ("index_low", wintypes.DWORD)]
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p,
                                  wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
    kernel.CreateFileW.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    handle = kernel.CreateFileW(str(path), 0x80010000, 3, None, 3, 0x02200000, None)
    if handle == ctypes.c_void_p(-1).value:
        return False
    try:
        kernel.GetFileInformationByHandle.argtypes = [wintypes.HANDLE, ctypes.POINTER(FileInfo)]
        info = FileInfo()
        if not kernel.GetFileInformationByHandle(handle, ctypes.byref(info)):
            return False
        current = (info.volume, (info.index_high << 32) | info.index_low)
        if not info.attributes & 0x10 or info.attributes & 0x400 or current != (identity[0] & 0xffffffff, identity[1]):
            return False
        disposition = wintypes.BOOL(True)
        kernel.SetFileInformationByHandle.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
        kernel.SetFileInformationByHandle.restype = wintypes.BOOL
        return bool(kernel.SetFileInformationByHandle(handle, 4, ctypes.byref(disposition), ctypes.sizeof(disposition)))
    finally:
        kernel.CloseHandle(handle)


@contextmanager
def _created_stage(parent: Path, name: str = "pack"):
    """Create the Windows stage and acquire its non-delete-sharing handle atomically."""
    if not name or name in (".", "..") or PureWindowsPath(name).name != name:
        raise ValueError("Nombre de directorio temporal inválido")
    stage = parent / name
    if os.name != "nt":
        stage.mkdir(mode=0o700)
        with open_pack_file(parent, name, directory=True):
            yield stage
        return
    from ctypes import wintypes
    class UnicodeString(ctypes.Structure):
        _fields_ = [("length", wintypes.USHORT), ("maximum", wintypes.USHORT), ("buffer", wintypes.LPWSTR)]
    class ObjectAttributes(ctypes.Structure):
        _fields_ = [("length", wintypes.ULONG), ("root", wintypes.HANDLE),
                    ("name", ctypes.POINTER(UnicodeString)), ("attributes", wintypes.ULONG),
                    ("security", ctypes.c_void_p), ("quality", ctypes.c_void_p)]
    class IoStatus(ctypes.Structure):
        _fields_ = [("status", ctypes.c_void_p), ("information", ctypes.c_size_t)]
    ntdll = ctypes.WinDLL("ntdll")
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    ntdll.NtCreateFile.argtypes = [ctypes.POINTER(wintypes.HANDLE), wintypes.ULONG,
                                  ctypes.POINTER(ObjectAttributes), ctypes.POINTER(IoStatus),
                                  ctypes.c_void_p, wintypes.ULONG, wintypes.ULONG, wintypes.ULONG,
                                  wintypes.ULONG, ctypes.c_void_p, wintypes.ULONG]
    ntdll.NtCreateFile.restype = ctypes.c_long
    ntdll.RtlNtStatusToDosError.argtypes = [ctypes.c_long]
    ntdll.RtlNtStatusToDosError.restype = wintypes.ULONG
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    absolute = str(stage.absolute())
    if absolute.startswith("\\\\"):
        raise ValueError("La creación protegida de etapa no admite rutas UNC")
    name = "\\??\\" + absolute
    raw = ctypes.create_unicode_buffer(name)
    length = len(name.encode("utf-16-le"))
    if length > 65532:
        raise ValueError("Ruta de etapa demasiado larga")
    text = UnicodeString(length, length + 2, ctypes.cast(raw, wintypes.LPWSTR))
    attrs = ObjectAttributes(ctypes.sizeof(ObjectAttributes), None, ctypes.pointer(text), 0x40, None, None)
    status = IoStatus()
    handle = wintypes.HANDLE()
    # FILE_CREATE, FILE_DIRECTORY_FILE | FILE_SYNCHRONOUS_IO_NONALERT |
    # FILE_OPEN_REPARSE_POINT. The parent and ancestors are already guarded.
    result = ntdll.NtCreateFile(ctypes.byref(handle), 0x80100000, ctypes.byref(attrs),
                                ctypes.byref(status), None, 0, 3, 2, 0x200021, None, 0)
    if result < 0:
        raise ctypes.WinError(ntdll.RtlNtStatusToDosError(result))
    try:
        yield stage
    finally:
        kernel.CloseHandle(handle)


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
    temporary = output_path.parent / (".rogue-compose-" + secrets.token_hex(16))
    try:
        # Hold the staging parent across all writes and publication. On Windows
        # this prevents its rename or replacement while its path is in use.
        with _created_stage(output_path.parent, temporary.name):
            temporary_info = Path(temporary).stat()
            temporary_identity = (temporary_info.st_dev, temporary_info.st_ino)
            # Creation and the first stage guard are one operation on Windows.
            # Publication compares identity after releasing that guard.
            with _created_stage(Path(temporary)) as stage:
                stage_info = stage.stat()
                stage_identity = (stage_info.st_dev, stage_info.st_ino)
                manifest_sources: dict[str, bytes] = {}
                expected_files: dict[str, str] = {}
                for index, (root, source_pack) in enumerate(bundle_sources):
                    isolated = _make_stage_dir(Path(temporary), f"source-{index}", exist_ok=False)
                    copy_bundle_transfer(root, isolated, source_pack)
                    with open_pack_file(isolated, source_pack["assetBundleManifestPath"]) as stream:
                        manifest_bytes = stream.read()
                    manifest_sources.setdefault(hashlib.sha256(manifest_bytes).hexdigest(), manifest_bytes)
                    for bundle in source_pack["assetBundles"]:
                        relative = PureWindowsPath(bundle["path"])
                        target = stage.joinpath(*relative.parts)
                        expected_files[bundle["path"]] = _guarded_hash(isolated, bundle["path"])
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
                    expected_files[pack["assetBundleManifestPath"]] = hashlib.sha256(manifest_bytes).hexdigest()
                content_bytes = (json.dumps(pack, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
                with open_pack_file(stage, "content.json", create=True) as output_stream:
                    output_stream.write(content_bytes)
                expected_files["content.json"] = hashlib.sha256(content_bytes).hexdigest()
                if dependency_report is not None:
                    dependency_report = dict(dependency_report, contentSha256=hashlib.sha256(content_bytes).hexdigest())
                    report_bytes = (json.dumps(dependency_report, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
                    with open_pack_file(stage, "composition-dependencies.json", create=True) as output_stream:
                        output_stream.write(report_bytes)
                    expected_files["composition-dependencies.json"] = hashlib.sha256(report_bytes).hexdigest()
                errors = validate_pack(str(stage))
                if errors:
                    raise ValueError("Pack compuesto inválido: " + "; ".join(errors))
                plan_bundle_transfer(stage, stage, pack)
                for relative, expected in expected_files.items():
                    if _guarded_hash(stage, relative) != expected:
                        raise ValueError(f"El archivo de la etapa cambió durante la composición: {relative}")
                if output_path.exists() or output_path.is_symlink():
                    raise FileExistsError(f"El destino apareció durante la composición: {output_path}")
            _publish_no_replace(stage, output_path, stage_identity)
    except Exception as exc:
        raise RuntimeError(f"Composición fallida; etapa conservada en {temporary}: {exc}") from exc
    # Delete only the original empty Windows directory by verified handle.
    # On unsupported systems or any mismatch, preserve it for inspection.
    if not _remove_empty_staging_parent(Path(temporary), temporary_identity):
        warnings.warn(f"Copias temporales conservadas para inspección: {temporary}", RuntimeWarning)
    return output_path
