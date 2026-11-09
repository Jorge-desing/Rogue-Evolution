param(
    [ValidateSet(9, 10, 11, 12)]
    [int]$SizePower = 11,
    [switch]$SkipPromote
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repoRoot "content/source-art/canonical-visual-modules.json"
$bakeScript = Join-Path $repoRoot "tools/Run-MaterialMakerBake.ps1"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$presets = @($manifest.modules | ForEach-Object { [string]$_.preset } | Sort-Object -Unique)
if ($presets.Count -ne 28) {
    throw "Expected 28 unique canonical module texture presets; found $($presets.Count)."
}

$unityTextureDir = Join-Path $repoRoot "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/VisualModules/Textures"
New-Item -ItemType Directory -Force -Path $unityTextureDir | Out-Null

$runningMaterialMaker = Get-Process -Name "material_maker" -ErrorAction SilentlyContinue
if ($runningMaterialMaker) {
    throw "Material Maker is already running. Close its existing session before this batch, because its export target is temporarily configured by the authoring runner."
}

for ($index = 0; $index -lt $presets.Count; $index++) {
    $preset = $presets[$index]
    $unitName = "RCLModule_$preset"
    $outputDirectory = "artifacts/canonical-two-level/texture-work/$preset"
    Write-Host "[$($index + 1)/$($presets.Count)] Material Maker 1.7 procedural bake: $preset"

    & $bakeScript `
        -Workflow procedural `
        -Preset $preset `
        -UnitName $unitName `
        -OutputDirectory $outputDirectory `
        -SizePower $SizePower `
        -ForceTemplate `
        -Strict
    if ($LASTEXITCODE -ne 0) {
        throw "Material Maker bake failed for preset '$preset' with exit code $LASTEXITCODE."
    }

    $resolvedOutput = Join-Path $repoRoot $outputDirectory
    $templatePrefix = "tactical-$preset-template"
    foreach ($suffix in @("Albedo", "MetallicGloss", "Normal", "Emission")) {
        $source = Join-Path $resolvedOutput "${templatePrefix}_$suffix.png"
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Material Maker output is missing: $source"
        }
        if (-not $SkipPromote) {
            $target = Join-Path $unityTextureDir "${unitName}_$suffix.png"
            Copy-Item -LiteralPath $source -Destination $target -Force
        }
    }
}

if (-not $SkipPromote) {
    $promoted = @(Get-ChildItem -LiteralPath $unityTextureDir -File -Filter "RCLModule_*.png")
    if ($promoted.Count -ne 112) {
        throw "Expected 112 promoted PBR maps (28 presets x 4), found $($promoted.Count)."
    }
    Write-Host "Promoted 28 validated four-map PBR sets to $unityTextureDir"
} else {
    Write-Host "Completed all 28 bakes without promoting maps to Unity."
}
