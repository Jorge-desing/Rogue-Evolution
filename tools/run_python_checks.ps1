# Run the project's Python validation suite without Hermes-injected PYTHONPATH
# shadowing the .venv wheels (cp312 vs cp314 ABI mismatch).
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $Root

# Hermes injects PYTHONPATH pointing at cp314 site-packages, which shadows
# the project's cp312 wheels (Pillow _imaging, rpds, etc.). Clear it.
$env:PYTHONPATH = ""

$Py = Join-Path $Root ".venv\Scripts\python.exe"
if (-not (Test-Path $Py)) { $Py = Join-Path $Root ".venv/bin/python" }
if (-not (Test-Path $Py)) { throw "No Python interpreter found in .venv" }

echo "=== Python test suite (unit + Blender) ==="
& $Py -B -m tools.run_python_tests --output artifacts/python-tests/report.json
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

echo "=== Content bundle references ==="
& $Py -B -m tools.validate_content_bundle_references content/packs/sample-native-expansion
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

echo "=== Loader registry ==="
& $Py -B -m tools.validate_loader_registry
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

echo "=== Whitespace check ==="
git diff --check -- ':!unity/'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
git diff --cached --check -- ':!unity/'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

echo "=== ALL PYTHON CHECKS PASSED ==="
