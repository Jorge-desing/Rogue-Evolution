#!/usr/bin/env bash
# Run the project's Python validation suite without Hermes-injected PYTHONPATH
# shadowing the .venv wheels (cp312 vs cp314 ABI mismatch).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# Hermes injects PYTHONPATH pointing at cp314 site-packages, which shadows
# the project's cp312 wheels (Pillow _imaging, rpds, etc.). Clear it.
export PYTHONPATH=""

PY="$ROOT/.venv/Scripts/python.exe"
if [[ ! -x "$PY" ]]; then
  PY="$ROOT/.venv/bin/python"
fi
if [[ ! -x "$PY" ]]; then
  echo "ERROR: no Python interpreter found in .venv" >&2
  exit 2
fi

echo "=== Python test suite (unit + Blender) ==="
"$PY" -B -m tools.run_python_tests --output artifacts/python-tests/report.json

echo "=== Content bundle references ==="
"$PY" -B -m tools.validate_content_bundle_references content/packs/sample-native-expansion

echo "=== Loader registry ==="
"$PY" -B -m tools.validate_loader_registry

echo "=== Whitespace check ==="
git diff --check -- ':!unity/'
git diff --cached --check -- ':!unity/'

echo "=== ALL PYTHON CHECKS PASSED ==="
