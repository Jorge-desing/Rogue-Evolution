#!/usr/bin/env python3
"""Create and verify an all-profile checkpoint for Steam runtime tests.

The tool is deliberately read-only with respect to the game installation and
profile tree.  It writes only the requested checkpoint/report file, normally
under ``artifacts/runtime-smoke``.

Examples:
    python tools/runtime_profile_checkpoint.py create \
        --profiles-root "C:/Users/Alex/AppData/LocalLow/Feneq/Rogue Command/Profiles" \
        --selector "C:/Users/Alex/AppData/LocalLow/Feneq/Rogue Command/Profiles/currentProfile.txt" \
        --output artifacts/runtime-smoke/session/profile-checkpoint.json

    python tools/runtime_profile_checkpoint.py verify \
        --manifest artifacts/runtime-smoke/session/profile-checkpoint.json \
        --profiles-root "C:/Users/Alex/AppData/LocalLow/Feneq/Rogue Command/Profiles" \
        --selector "C:/Users/Alex/AppData/LocalLow/Feneq/Rogue Command/Profiles/currentProfile.txt" \
        --allow-relative Profile2/metaSavegame.dat

    # After selecting a save-free QA profile through the native profile UI:
    python tools/runtime_profile_checkpoint.py verify \
        --manifest artifacts/runtime-smoke/session/profile-checkpoint.json \
        --profiles-root "C:/Users/Alex/AppData/LocalLow/Feneq/Rogue Command/Profiles" \
        --selector "C:/Users/Alex/AppData/LocalLow/Feneq/Rogue Command/Profiles/currentProfile.txt" \
        --qa-profile Profile3 --qa-active
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import stat
import sys
import tempfile
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath
from typing import Iterable

SCHEMA = "rogue-command/runtime-profile-checkpoint/v1"
_ALLOWED_AUTOSAVE_PATH = "Profile2/metaSavegame.dat"
_HASH_CHUNK_SIZE = 1024 * 1024
_REPARSE_POINT = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)


def compute_file_hash(path: Path) -> str:
    """Return the SHA-256 of *path* without loading the whole file."""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(_HASH_CHUNK_SIZE), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _file_identity(metadata: os.stat_result) -> tuple[int, int, int, int, int]:
    """Return stable metadata useful for detecting a path replacement during a read."""
    return (
        metadata.st_dev,
        metadata.st_ino,
        metadata.st_mode,
        metadata.st_size,
        metadata.st_mtime_ns,
    )


def _hash_open_file(path: Path, expected: os.stat_result) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        opened = os.fstat(stream.fileno())
        if not stat.S_ISREG(opened.st_mode) or _file_identity(opened) != _file_identity(expected):
            raise RuntimeError(f"Profile entry changed before it was opened: {path}")
        for chunk in iter(lambda: stream.read(_HASH_CHUNK_SIZE), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _stable_file_record(path: Path) -> dict[str, int | str]:
    """Hash a file and fail if it changes while it is being read."""
    before = _lstat_no_links(path)
    if not stat.S_ISREG(before.st_mode):
        raise ValueError(f"Refusing to hash a non-regular profile entry: {path}")
    digest = _hash_open_file(path, before)
    after = _lstat_no_links(path)
    if _file_identity(before) != _file_identity(after):
        raise RuntimeError(f"File changed while it was being hashed: {path}")
    return {
        "size": before.st_size,
        "sha256": digest,
    }


def _stable_text_record(path: Path) -> dict[str, int | str]:
    """Read the selector as UTF-8 while checking that it stayed stable."""
    before = _lstat_no_links(path)
    if not stat.S_ISREG(before.st_mode):
        raise ValueError(f"Refusing to read a non-regular selector: {path}")
    with path.open("rb") as stream:
        opened = os.fstat(stream.fileno())
        if not stat.S_ISREG(opened.st_mode) or _file_identity(opened) != _file_identity(before):
            raise RuntimeError(f"Selector changed before it was opened: {path}")
        raw = stream.read()
    after = _lstat_no_links(path)
    if _file_identity(before) != _file_identity(after):
        raise RuntimeError(f"Selector changed while it was being read: {path}")
    try:
        value = raw.decode("utf-8")
    except UnicodeDecodeError as exc:
        raise ValueError(f"Selector is not valid UTF-8: {path}") from exc
    return {
        "value": value,
        "size": len(raw),
        "sha256": hashlib.sha256(raw).hexdigest(),
    }


def _normalise_relative_path(value: str) -> str:
    """Validate and normalize a manifest-relative path."""
    text = value.replace("\\", "/")
    path = PurePosixPath(text)
    if not text or not path.parts:
        raise ValueError(f"Manifest path must be relative: {value!r}")
    if path.is_absolute() or ":" in path.parts[0]:
        raise ValueError(f"Manifest path must be relative: {value!r}")
    if any(part in ("", ".", "..") for part in path.parts):
        raise ValueError(f"Manifest path contains an unsafe segment: {value!r}")
    return path.as_posix()


def _normalise_path(path: Path) -> Path:
    """Return an absolute lexical path without resolving links."""
    path = Path(path).expanduser()
    return Path(os.path.normcase(os.path.abspath(os.fspath(path))))


def _normalise_absolute_path(value: str, field_name: str) -> Path:
    """Normalize a checkpoint path and require its stored form to be absolute."""
    path = Path(value)
    if not path.is_absolute():
        raise ValueError(f"Checkpoint {field_name} must be an absolute path: {value!r}")
    return _normalise_path(path)


def _normalise_existing_path(path: Path) -> Path:
    """Check a supplied path before applying lexical normalization."""
    path = Path(path).expanduser()
    _lstat_no_links(path)
    return _normalise_path(path)


def _lstat_no_links(path: Path) -> os.stat_result:
    """Read metadata without accepting symlink or reparse-point paths."""
    raw_path = Path(path).expanduser()
    absolute_path = raw_path if raw_path.is_absolute() else Path.cwd() / raw_path
    current = Path(absolute_path.anchor)
    metadata: os.stat_result | None = None

    for part in absolute_path.parts:
        if part == absolute_path.anchor or part in ("", "."):
            continue
        if part == "..":
            current = current.parent
            continue
        current = current / part
        metadata = current.lstat()
        if stat.S_ISLNK(metadata.st_mode):
            raise ValueError(f"Refusing to use a profile symlink: {current}")
        if getattr(metadata, "st_file_attributes", 0) & _REPARSE_POINT:
            raise ValueError(f"Refusing to use a profile reparse point: {current}")

    if metadata is None or current != absolute_path:
        metadata = current.lstat()
        if stat.S_ISLNK(metadata.st_mode):
            raise ValueError(f"Refusing to use a profile symlink: {current}")
        if getattr(metadata, "st_file_attributes", 0) & _REPARSE_POINT:
            raise ValueError(f"Refusing to use a profile reparse point: {current}")
    return metadata


def _iter_regular_files(root: Path) -> Iterable[Path]:
    """Yield regular files without following symlinks or reparse links."""
    root = Path(root)
    root_metadata = _lstat_no_links(root)
    if not stat.S_ISDIR(root_metadata.st_mode):
        raise ValueError(f"Profiles root is not a directory: {root}")

    def on_walk_error(error: OSError) -> None:
        raise error

    for current, directory_names, file_names in os.walk(
        root,
        topdown=True,
        onerror=on_walk_error,
        followlinks=False,
    ):
        current_path = Path(current)
        directory_names.sort()
        file_names.sort()
        for name in directory_names:
            candidate = current_path / name
            metadata = _lstat_no_links(candidate)
            if not stat.S_ISDIR(metadata.st_mode):
                raise ValueError(f"Refusing to traverse a non-directory profile entry: {candidate}")
        for name in file_names:
            candidate = current_path / name
            metadata = _lstat_no_links(candidate)
            if not stat.S_ISREG(metadata.st_mode):
                raise ValueError(f"Refusing to hash a non-regular profile entry: {candidate}")
            yield candidate


def _require_layout(profiles_root: Path, selector_path: Path) -> tuple[Path, Path]:
    root_candidate = Path(profiles_root).expanduser()
    selector_candidate = Path(selector_path).expanduser()

    # lstat must happen before any path operation that could follow a link.
    root_metadata = _lstat_no_links(root_candidate)
    selector_metadata = _lstat_no_links(selector_candidate)
    profiles_root = _normalise_path(root_candidate)
    selector_path = _normalise_path(selector_candidate)

    if not stat.S_ISDIR(root_metadata.st_mode):
        raise FileNotFoundError(f"Profiles directory not found: {profiles_root}")
    if not stat.S_ISREG(selector_metadata.st_mode):
        raise FileNotFoundError(f"Profile selector not found: {selector_path}")
    if selector_path.parent != profiles_root:
        raise ValueError(
            "Profile selector must be an immediate child of the profiles root: "
            f"{selector_path} is not inside {profiles_root}"
        )
    return profiles_root, selector_path


def _collect_profile_files(profiles_root: Path) -> list[dict[str, int | str]]:
    records: list[dict[str, int | str]] = []
    for path in _iter_regular_files(profiles_root):
        record = _stable_file_record(path)
        record["path"] = path.relative_to(profiles_root).as_posix()
        records.append(record)
    records.sort(key=lambda item: str(item["path"]))
    return records


def _write_json_atomic(path: Path, value: dict) -> None:
    path = path.expanduser().resolve()
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        raise FileExistsError(f"Refusing to overwrite existing checkpoint: {path}")
    payload = json.dumps(value, indent=2, ensure_ascii=False) + "\n"
    temporary_path: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="w",
            encoding="utf-8",
            dir=path.parent,
            prefix=f".{path.name}.",
            suffix=".tmp",
            delete=False,
        ) as stream:
            temporary_path = Path(stream.name)
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary_path, path)
    finally:
        if temporary_path is not None and temporary_path.exists():
            temporary_path.unlink()


def create_checkpoint(profiles_root: Path, selector_path: Path, output_path: Path) -> dict:
    """Create and write a complete checkpoint, returning its JSON object."""
    profiles_root, selector_path = _require_layout(profiles_root, selector_path)
    output_path = output_path.expanduser().resolve()
    if output_path == profiles_root or profiles_root in output_path.parents:
        raise ValueError("Checkpoint output must not be inside the profile tree")

    selector_record = _stable_text_record(selector_path)
    selector_record["path"] = str(selector_path)
    manifest = {
        "schema": SCHEMA,
        "createdAtUtc": datetime.now(timezone.utc).isoformat(),
        "profilesRoot": str(profiles_root),
        "selector": selector_record,
        "profileFiles": _collect_profile_files(profiles_root),
    }
    _write_json_atomic(output_path, manifest)
    return manifest


def _load_manifest(manifest_path: Path) -> dict:
    manifest_path = manifest_path.expanduser().resolve()
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise ValueError(f"Invalid checkpoint JSON: {manifest_path}") from exc
    if not isinstance(manifest, dict) or manifest.get("schema") != SCHEMA:
        raise ValueError(f"Unsupported checkpoint schema: {manifest_path}")
    entries = manifest.get("profileFiles")
    selector = manifest.get("selector")
    profiles_root = manifest.get("profilesRoot")
    if (
        not isinstance(entries, list)
        or not isinstance(selector, dict)
        or not isinstance(profiles_root, str)
        or not isinstance(selector.get("path"), str)
    ):
        raise ValueError(
            f"Checkpoint is missing profilesRoot, selector.path, or profileFiles: {manifest_path}"
        )
    # These are captured targets, not descriptive metadata.  Keep them
    # absolute and lexical so verification can bind operator arguments to the
    # exact tree and selector without resolving links.
    manifest["profilesRoot"] = str(
        _normalise_absolute_path(profiles_root, "profilesRoot")
    )
    selector["path"] = str(
        _normalise_absolute_path(selector["path"], "selector.path")
    )

    seen: set[str] = set()
    normalized_entries: list[dict[str, int | str]] = []
    for entry in entries:
        if not isinstance(entry, dict):
            raise ValueError(f"Checkpoint contains a non-object profile entry: {manifest_path}")
        relative = _normalise_relative_path(str(entry.get("path", "")))
        if relative in seen:
            raise ValueError(f"Checkpoint contains duplicate profile path: {relative}")
        seen.add(relative)
        if not isinstance(entry.get("size"), int) or not isinstance(entry.get("sha256"), str):
            raise ValueError(f"Checkpoint entry is missing size or sha256: {relative}")
        normalized_entries.append({
            "path": relative,
            "size": entry["size"],
            "sha256": entry["sha256"],
        })
    manifest["profileFiles"] = sorted(normalized_entries, key=lambda item: str(item["path"]))
    return manifest


def verify_checkpoint(
    manifest_path: Path,
    profiles_root: Path,
    selector_path: Path,
    allowed_relative_paths: Iterable[str] = (),
    qa_profile: str | None = None,
    qa_active: bool = False,
) -> dict:
    """Compare the live profile tree with a checkpoint.

    ``allowed_relative_paths`` can be used for an explicitly expected autosave,
    such as ``Profile2/metaSavegame.dat``. ``qa_profile`` allows changes only
    inside a named, pre-existing profile that had no saved run in the baseline.
    With ``qa_active=True``, the selector must point to that QA profile and it
    must still have no ``savegame.dat``; all other profiles must match exactly.
    Without ``qa_active``, the selector must match the baseline for finalization.
    """
    manifest_path = manifest_path.expanduser().resolve()
    manifest = _load_manifest(manifest_path)

    stored_profiles_root = _normalise_absolute_path(
        str(manifest["profilesRoot"]), "profilesRoot"
    )
    stored_selector_path = _normalise_absolute_path(
        str(manifest["selector"]["path"]), "selector.path"
    )
    supplied_profiles_root = _normalise_existing_path(Path(profiles_root))
    supplied_selector_path = _normalise_existing_path(Path(selector_path))
    if supplied_profiles_root != stored_profiles_root:
        raise ValueError(
            "Supplied profiles root does not match the checkpoint profiles root: "
            f"{supplied_profiles_root} != {stored_profiles_root}"
        )
    if supplied_selector_path != stored_selector_path:
        raise ValueError(
            "Supplied selector path does not match the checkpoint selector path: "
            f"{supplied_selector_path} != {stored_selector_path}"
        )

    profiles_root, selector_path = _require_layout(
        supplied_profiles_root, supplied_selector_path
    )
    allowed = {_normalise_relative_path(path) for path in allowed_relative_paths}
    if allowed - {_ALLOWED_AUTOSAVE_PATH}:
        invalid = sorted(allowed - {_ALLOWED_AUTOSAVE_PATH})
        raise ValueError(
            "Only the designated QA autosave may be allow-listed: "
            + ", ".join(invalid)
        )

    if qa_active and qa_profile is None:
        raise ValueError("--qa-active requires an explicit --qa-profile")
    qa_name = None
    qa_number = None
    if qa_profile is not None:
        match = re.fullmatch(r"Profile([1-9][0-9]*)", qa_profile)
        if match is None:
            raise ValueError("QA profile must be an exact name such as Profile3")
        qa_name = qa_profile
        qa_number = match.group(1)

    expected = {str(entry["path"]): entry for entry in manifest["profileFiles"]}
    if qa_name is not None:
        qa_prefix = qa_name + "/"
        baseline_qa_entries = [path for path in expected if path.startswith(qa_prefix)]
        if not baseline_qa_entries:
            raise ValueError("QA profile must already exist in the checkpoint baseline")
        if qa_name + "/savegame.dat" in expected:
            raise ValueError("QA profile has a saved run in the checkpoint baseline")
        if str(manifest["selector"].get("value", "")).strip() == qa_number:
            raise ValueError("QA profile must differ from the checkpoint's active profile")

    actual_entries = _collect_profile_files(profiles_root)
    actual = {str(entry["path"]): entry for entry in actual_entries}
    added = sorted(set(actual) - set(expected))
    missing = sorted(set(expected) - set(actual))
    changed = sorted(
        path for path in (set(expected) & set(actual)) if expected[path] != actual[path]
    )
    qa_prefix = qa_name + "/" if qa_name is not None else None
    qa_added = sorted(path for path in added if qa_prefix is not None and path.startswith(qa_prefix))
    unexpected_added = sorted(path for path in added if path not in qa_added)
    qa_changed = sorted(path for path in changed if qa_prefix is not None and path.startswith(qa_prefix))
    allowed_changed = sorted(path for path in changed if path in allowed and path not in qa_changed)

    expected_selector = manifest["selector"]
    actual_selector = _stable_text_record(selector_path)
    selector_match = (
        expected_selector.get("value") == actual_selector["value"]
        and expected_selector.get("size") == actual_selector["size"]
        and expected_selector.get("sha256") == actual_selector["sha256"]
    )
    qa_selector_match = (
        qa_name is not None
        and str(actual_selector.get("value", "")).strip() == qa_number
    )
    selector_relative_path = Path(str(expected_selector["path"])).name
    selector_relative_key = selector_relative_path.casefold()
    qa_selector_changed = bool(
        qa_active
        and qa_selector_match
        and any(path.casefold() == selector_relative_key for path in changed)
    )
    unexpected_changed = sorted(
        path
        for path in changed
        if path not in allowed_changed
        and path not in qa_changed
        and not (qa_selector_changed and path.casefold() == selector_relative_key)
    )
    qa_has_saved_run = (
        qa_name is not None
        and qa_name + "/savegame.dat" in actual
    )
    safe_to_proceed = bool(
        qa_active
        and qa_name is not None
        and qa_selector_match
        and not qa_has_saved_run
        and not missing
        and not unexpected_added
        and not unexpected_changed
    )

    return {
        "schema": SCHEMA,
        "manifest": str(manifest_path),
        "profilesRoot": str(profiles_root),
        "selectorPath": str(selector_path),
        "profileFilesExactMatch": not added and not missing and not changed,
        "added": added,
        "qaProfileAdded": qa_added,
        "unexpectedAdded": unexpected_added,
        "missing": missing,
        "changed": changed,
        "qaProfileChanged": qa_changed,
        "allowedChanged": allowed_changed,
        "unexpectedChanged": unexpected_changed,
        "selectorMatch": selector_match,
        "qaProfile": qa_name,
        "qaSelectorMatch": qa_selector_match,
        "qaSelectorChanged": qa_selector_changed,
        "qaProfileHasSavedRun": qa_has_saved_run,
        "safe_to_proceed": safe_to_proceed,
        "safe_to_finalize": (
            not unexpected_added
            and not missing
            and not unexpected_changed
            and selector_match
        ),
    }


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Checkpoint and verify all Rogue Command profile files")
    subparsers = parser.add_subparsers(dest="command", required=True)

    create = subparsers.add_parser("create", help="create a checkpoint without modifying the game tree")
    create.add_argument("--profiles-root", required=True, type=Path)
    create.add_argument("--selector", required=True, type=Path)
    create.add_argument("--output", required=True, type=Path)

    verify = subparsers.add_parser("verify", help="verify a checkpoint after a runtime session")
    verify.add_argument("--manifest", required=True, type=Path)
    verify.add_argument("--profiles-root", required=True, type=Path)
    verify.add_argument("--selector", required=True, type=Path)
    verify.add_argument("--allow-relative", action="append", default=[], metavar="PATH")
    verify.add_argument("--qa-profile", metavar="PROFILE")
    verify.add_argument("--qa-active", action="store_true")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = _build_parser().parse_args(argv)
    try:
        if args.command == "create":
            result = create_checkpoint(args.profiles_root, args.selector, args.output)
            print(json.dumps(result, indent=2, ensure_ascii=False))
            return 0
        result = verify_checkpoint(
            args.manifest,
            args.profiles_root,
            args.selector,
            args.allow_relative,
            args.qa_profile,
            args.qa_active,
        )
        print(json.dumps(result, indent=2, ensure_ascii=False))
        return 0 if (result["safe_to_proceed"] if args.qa_active else result["safe_to_finalize"]) else 1
    except (FileNotFoundError, OSError, RuntimeError, ValueError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
