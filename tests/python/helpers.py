"""Shared test helpers and dynamic module loaders for Rogue Command Python tests."""

from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[2]
TOOLS_DIR = REPO_ROOT / "tools"
CONTENT_DIR = REPO_ROOT / "content"
SCHEMA_PATH = CONTENT_DIR / "schema" / "content.schema.json"
SAMPLE_EXPANSION_PATH = CONTENT_DIR / "packs" / "sample-expansion" / "content.json"
SAMPLE_NATIVE_PATH = CONTENT_DIR / "packs" / "sample-native-expansion" / "content.json"
TACTICAL_OPERATIONS_PATH = CONTENT_DIR / "packs" / "tactical-operations" / "content.json"


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
    """Read and parse a UTF-8 JSON file."""
    return json.loads(path.read_text(encoding="utf-8"))
