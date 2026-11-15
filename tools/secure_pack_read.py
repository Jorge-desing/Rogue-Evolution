"""Open pack files through non-reparse directory handles."""
from __future__ import annotations

from contextlib import contextmanager
import os
from pathlib import Path, PureWindowsPath
import stat


@contextmanager
def open_pack_file(root: str | Path, relative: str, *, create: bool = False):
    """Open or exclusively create a pack-relative file without following links."""
    path = PureWindowsPath(relative)
    if not relative or path.anchor or ".." in path.parts or ":" in relative or "\x00" in relative:
        raise ValueError(f"Ruta no relativa al pack: {relative!r}")
    absolute_root = Path(root).absolute()
    if os.name == "nt":
        import ctypes
        from ctypes import wintypes
        import msvcrt

        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        create_file = kernel.CreateFileW
        create_file.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p,
                           wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
        create_file.restype = wintypes.HANDLE
        close = kernel.CloseHandle
        close.argtypes = [wintypes.HANDLE]
        close.restype = wintypes.BOOL
        class Info(ctypes.Structure):
            _fields_ = [("attributes", wintypes.DWORD), ("tag", wintypes.DWORD)]
        query = kernel.GetFileInformationByHandleEx
        query.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
        query.restype = wintypes.BOOL
        handles = []
        stream = None
        try:
            current = Path(absolute_root.anchor)
            directories = list(absolute_root.parts[1:]) + list(path.parts[:-1])
            for part in [None] + directories:
                if part is not None:
                    current /= part
                handle = create_file(str(current), 0, 3, None, 3, 0x02200000, None)
                if handle == ctypes.c_void_p(-1).value:
                    raise ctypes.WinError(ctypes.get_last_error())
                handles.append(handle)
                info = Info()
                if not query(handle, 9, ctypes.byref(info), ctypes.sizeof(info)):
                    raise ctypes.WinError(ctypes.get_last_error())
                if not info.attributes & 0x10 or info.attributes & 0x400:
                    raise ValueError(f"Directorio no regular o reparse: {current}")
            file_path = current / path.parts[-1]
            handle = create_file(str(file_path), 0x40000000 if create else 0x80000000,
                            0 if create else 1, None, 1 if create else 3, 0x00200000, None)
            if handle == ctypes.c_void_p(-1).value:
                raise ctypes.WinError(ctypes.get_last_error())
            handles.append(handle)
            info = Info()
            if not query(handle, 9, ctypes.byref(info), ctypes.sizeof(info)):
                raise ctypes.WinError(ctypes.get_last_error())
            if info.attributes & (0x10 | 0x400):
                raise ValueError(f"Archivo no regular o reparse: {file_path}")
            fd = msvcrt.open_osfhandle(handle, (os.O_WRONLY if create else os.O_RDONLY) | os.O_BINARY)
            handles.pop()
            stream = os.fdopen(fd, "wb" if create else "rb")
            yield stream
        finally:
            if stream is not None:
                stream.close()
            for handle in reversed(handles):
                close(handle)
    elif os.name == "posix" and hasattr(os, "O_NOFOLLOW"):
        descriptors = []
        stream = None
        try:
            current = os.open(absolute_root.anchor, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
            descriptors.append(current)
            for part in list(absolute_root.parts[1:]) + list(path.parts[:-1]):
                current = os.open(part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=current)
                descriptors.append(current)
            flags = (os.O_WRONLY | os.O_CREAT | os.O_EXCL) if create else os.O_RDONLY
            fd = os.open(path.parts[-1], flags | os.O_NOFOLLOW, 0o600, dir_fd=current)
            if not stat.S_ISREG(os.fstat(fd).st_mode):
                os.close(fd)
                raise ValueError("El archivo del pack no es regular")
            stream = os.fdopen(fd, "wb" if create else "rb")
            yield stream
        finally:
            if stream is not None:
                stream.close()
            for fd in reversed(descriptors):
                os.close(fd)
    else:
        raise OSError("Apertura segura de archivos de pack no soportada en este sistema")
