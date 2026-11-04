# Run-MaterialMakerBake.ps1
# Automatic Material Maker 1.7 bake for Rogue Command PBR textures.
# The generated output stays under artifacts/ until PBR validation and Unity import.

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$mmDir = Join-Path $repoRoot ".tools/MaterialMaker-1.7/material_maker_1_7_windows"
$mmExe = Join-Path $mmDir "material_maker.exe"
$template = Join-Path $repoRoot "artifacts/mm-templates/tactical-military-template.ptex"
$outDir = Join-Path $repoRoot "artifacts/mm-bake-output"
$rawDir = Join-Path $outDir "raw"
$waitScript = Join-Path $repoRoot "tools/MaterialMakerWaitDevice.gd"
$packScript = Join-Path $repoRoot "tools/pack_material_maker_pbr.py"
$prepareScript = Join-Path $repoRoot "tools/Prepare-MaterialMakerTacticalTemplate.py"
$overrideCfg = Join-Path $mmDir "override.cfg"
$userExportDir = Join-Path $env:APPDATA "material_maker_2/export_targets"
$exportTargetFile = Join-Path $userExportDir "material_export_roguecommand.mme"
$templatePrefix = [System.IO.Path]::GetFileNameWithoutExtension($template)

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
New-Item -ItemType Directory -Force -Path $rawDir | Out-Null
New-Item -ItemType Directory -Force -Path $userExportDir | Out-Null

foreach ($required in @($mmExe, $waitScript, $packScript, $prepareScript)) {
    if (-not (Test-Path $required)) {
        throw "Required Material Maker bake input is missing: $required"
    }
}
if (-not (Test-Path $template)) {
    & python $prepareScript --output $template
    if ($LASTEXITCODE -ne 0) {
        throw "Material Maker template preparation failed with exit code $LASTEXITCODE"
    }
}

# Material Maker 1.7 issue #1506: the CLI can start exporting before the
# background RenderingDevice exists and crash with 0xC0000005. The autoload
# waits for that device. Keep this workaround beside the ignored local tool
# installation and restore any pre-existing override.cfg after the bake.
$hadOverride = Test-Path $overrideCfg
$backupOverride = Join-Path $env:TEMP ("rcl-material-maker-override-" + [guid]::NewGuid().ToString("N") + ".cfg")
if ($hadOverride) {
    Copy-Item -LiteralPath $overrideCfg -Destination $backupOverride -Force
}
$waitScriptAbsolute = (Resolve-Path -LiteralPath $waitScript).Path.Replace('\', '/')
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$quote = [char]34
$overrideText = "[autoload]`r`n`r`nmm_wait_device=$quote*$waitScriptAbsolute$quote`r`n"
[System.IO.File]::WriteAllText($overrideCfg, $overrideText, $utf8NoBom)

# Material Maker's built-in Blender profile writes metallic/roughness as EXR.
# This external profile forces PNG scalar exports so the packer can create the
# project's required MetallicGloss (RGB metallic, A smoothness) map.
$hadExportTarget = Test-Path $exportTargetFile
$backupExportTarget = Join-Path $env:TEMP ("rcl-material-maker-target-" + [guid]::NewGuid().ToString("N") + ".mme")
if ($hadExportTarget) {
    Copy-Item -LiteralPath $exportTargetFile -Destination $backupExportTarget -Force
}
$exportTarget = [ordered]@{
    name = "RogueCommand"
    material = "material"
    files = @(
        [ordered]@{ file_name = "`$(path_prefix)_Albedo.png"; type = "texture"; output = 0 }
        [ordered]@{ file_name = "`$(path_prefix)_Metallic.png"; type = "texture"; output = 12 }
        [ordered]@{ file_name = "`$(path_prefix)_Roughness.png"; type = "texture"; output = 13 }
        [ordered]@{ file_name = "`$(path_prefix)_Normal.png"; type = "texture"; output = 7 }
        [ordered]@{ file_name = "`$(path_prefix)_Emission.png"; type = "texture"; output = 2 }
    )
}
$exportJson = $exportTarget | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText($exportTargetFile, $exportJson, $utf8NoBom)

try {
    Push-Location $mmDir
    try {
        $mmArguments = "--export-material --target RogueCommand -o `"$rawDir`" `"$template`""
        $mmLog = Join-Path $outDir "material-maker.log"
        $mmErrorLog = Join-Path $outDir "material-maker-error.log"
        $materialMakerProcess = Start-Process -FilePath $mmExe -ArgumentList $mmArguments -WorkingDirectory $mmDir -RedirectStandardOutput $mmLog -RedirectStandardError $mmErrorLog -Wait -PassThru
        $materialMakerExitCode = $materialMakerProcess.ExitCode
        if ($null -ne $materialMakerExitCode -and $materialMakerExitCode -ne 0) {
            throw "Material Maker export failed with exit code $materialMakerExitCode"
        }
    } finally {
        Pop-Location
    }

    & python $packScript --input-dir $rawDir --output-dir $outDir --prefix $templatePrefix
    if ($LASTEXITCODE -ne 0) {
        throw "Material Maker PBR pack failed with exit code $LASTEXITCODE"
    }
} finally {
    if ($hadOverride) {
        Move-Item -LiteralPath $backupOverride -Destination $overrideCfg -Force
    } elseif (Test-Path $overrideCfg) {
        Remove-Item -LiteralPath $overrideCfg -Force
    }
    if (Test-Path $backupOverride) {
        Remove-Item -LiteralPath $backupOverride -Force
    }
    if ($hadExportTarget) {
        Move-Item -LiteralPath $backupExportTarget -Destination $exportTargetFile -Force
    } elseif (Test-Path $exportTargetFile) {
        Remove-Item -LiteralPath $exportTargetFile -Force
    }
    if (Test-Path $backupExportTarget) {
        Remove-Item -LiteralPath $backupExportTarget -Force
    }
}

$expected = @(
    "${templatePrefix}_Albedo.png",
    "${templatePrefix}_MetallicGloss.png",
    "${templatePrefix}_Normal.png",
    "${templatePrefix}_Emission.png"
)
foreach ($name in $expected) {
    $path = Join-Path $outDir $name
    if (-not (Test-Path $path)) {
        throw "Material Maker bake completed without required map: $path"
    }
}

Write-Host "Material Maker bake output: $outDir"
Get-ChildItem -LiteralPath $outDir -File | Select-Object Name, Length, LastWriteTime
