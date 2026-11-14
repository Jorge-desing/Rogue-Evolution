"""Shared test helpers and dynamic module loaders for Rogue Command Python tests."""

from __future__ import annotations

import ast
import importlib.util
import json
import sys
from functools import lru_cache
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[2]
TOOLS_DIR = REPO_ROOT / "tools"
CONTENT_DIR = REPO_ROOT / "content"
SCHEMA_PATH = CONTENT_DIR / "schema" / "content.schema.json"
SAMPLE_EXPANSION_PATH = CONTENT_DIR / "packs" / "sample-expansion" / "content.json"
SAMPLE_NATIVE_PATH = CONTENT_DIR / "packs" / "sample-native-expansion" / "content.json"
TACTICAL_OPERATIONS_PATH = CONTENT_DIR / "packs" / "tactical-operations" / "content.json"
BLENDER_EXE = Path(r"C:\Program Files\Blender Foundation\Blender 4.5\blender.exe")
GAME_ROOT = Path(r"C:\Program Files (x86)\Steam\steamapps\common\Rogue Command")


def load_tool_module(module_name: str, script_filename: str | None = None) -> Any:
    """Load a Python script from the repository tools/ directory by filename."""
    filename = script_filename or f"{module_name}.py"
    script_path = TOOLS_DIR / filename
    spec = importlib.util.spec_from_file_location(module_name, script_path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not load tool module {module_name} from {script_path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def read_json(path: Path) -> Any:
    """Read and parse a UTF-8 JSON file (uncached so fixture updates remain visible)."""
    return json.loads(path.read_text(encoding="utf-8"))


@lru_cache(maxsize=None)
def _read_text_resolved(resolved_path: str, encoding: str) -> str:
    return Path(resolved_path).read_text(encoding=encoding)


def read_text_cached(path: Path, encoding: str = "utf-8") -> str:
    """Read a static repository text file once per worker process."""
    return _read_text_resolved(str(path.resolve()), encoding)


@lru_cache(maxsize=None)
def _parse_ast_resolved(resolved_path: str, encoding: str) -> ast.Module:
    source = _read_text_resolved(resolved_path, encoding)
    return ast.parse(source, filename=resolved_path)


def parse_ast_cached(path: Path, encoding: str = "utf-8") -> ast.Module:
    """Parse a static repository Python file into an AST once per worker process."""
    return _parse_ast_resolved(str(path.resolve()), encoding)
