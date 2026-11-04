# Run-MaterialMakerBake.ps1
# Bakes Material Maker 1.7 textures for Rogue Command tactical military theme.
# Prereq: open Material Maker GUI once and configure an export target (any path) so
# that user://export_targets is created; otherwise CLI export fails with
# "Cannot open user://export_targets".

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$mmExe = Join-Path $repoRoot ".tools/MaterialMaker-1.7/material_maker_1_7_windows/material_maker.exe"
$template = Join-Path $repoRoot "artifacts/mm-templates/tactical-military-template.ptex"
$outDir = Join-Path $repoRoot "artifacts/mm-bake-output"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

if (-not (Test-Path $mmExe)) { throw "Material Maker not staged: $mmExe" }
if (-not (Test-Path $template)) { throw "Template missing: $template" }

Push-Location (Split-Path $mmExe)
try {
    & $mmExe --export-material --target Blender -o $outDir $template --quit
} finally {
    Pop-Location
}

Write-Host "Bake output:" $outDir
Get-ChildItem $outDir