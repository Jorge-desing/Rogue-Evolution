# Run-MaterialMakerBake.ps1
# Automatic Material Maker 1.7 bake for Rogue Command PBR textures.
# Supports workflows: urp, blender, three_d, procedural.

param(
    [ValidateSet("urp", "blender", "three_d", "procedural")]
    [string]$Workflow = "urp",
    [ValidateSet(
        "olive_armor",
        "gunmetal_chassis",
        "desert_camo",
        "hazard_industrial",
        "orbital_titanium",
        "stealth_void_composite",
        "solar_gold_radiator",
        "battleship_tungsten",
        "cerberus_white_ceramic",
        "plasma_cinder_ablative",
        "cyber_nanoweave",
        "anodized_cobalt",
        "quantum_hyperdrive_alloy",
        "beryllium_mirror_hull",
        "obsidian_strike_interceptor",
        "fusion_reactor_core_plating",
        "martian_iron_rust",
        "neosteel_command_citadel",
        "antimatter_containment_lattice",
        "cryo_glacier_camo",
        "hypervelocity_railgun_cermet",
        "stealth_radar_absorbent_ferrite",
        "solar_flare_radiation_shield",
        "ion_thruster_titanium_grate",
        "nanite_repair_chitin",
        "tungsten_heavy_flak_plating",
        "zero_g_docking_alloy",
        "plasma_arc_containment",
        "tachyon_warp_radiator",
        "graphene_ablative_lattice",
        "dreadnought_adamantine_hull",
        "void_stalker_metamaterial",
        "orbital_drop_pod_cermet",
        "pulsar_magnetic_shield",
        "exoskeleton_reactive_scale",
        "cryogenic_fuel_shroud",
        "subterranean_mole_borer",
        "longshot_sniper_camo",
        "minefield_ordnance_carrier",
        "gravitas_singularity_chassis",
        "arc_discharge_conduit",
        "combat_medic_bioceramic",
        "salvage_reclamation_ironclad",
        "orbital_phased_array_comm"
    )]
    [string]$Preset = "olive_armor",
    [ValidateSet(9, 10, 11, 12)]
    [int]$SizePower = 11,
    [string]$TemplatePath = "",
    [string]$OutputDirectory = "",
    [string]$UnitName = "TacticalHull",
    [string]$PaintSourceModel = "",
    [string]$BlenderPreviewModel = "",
    [switch]$ForceTemplate,
    [switch]$Strict,
    [switch]$VerboseMaterialMaker
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$configPath = Join-Path $repoRoot "tools/material-maker/rogue-command-material-maker.json"
$configValidator = Join-Path $repoRoot "tools/validate_material_maker_config.py"
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$workflowConfig = $config.workflows.$Workflow
if ($null -eq $workflowConfig) {
    throw "Material Maker workflow is missing from config: $Workflow"
}

$mmDir = Join-Path $repoRoot ".tools/MaterialMaker-1.7/material_maker_1_7_windows"
$officialMmExe = Join-Path $repoRoot $config.tool.installRelativePath
$patchedRuntimeDir = Join-Path $repoRoot ".tools/MaterialMaker-1.7-patched/runtime"
$patchedMmPack = Join-Path $patchedRuntimeDir "material_maker.pck"
$patchedGodotExe = Join-Path $patchedRuntimeDir "Godot_v4.7-stable_win64_console.exe"
$patchedGodotGuiExe = Join-Path $patchedRuntimeDir "Godot_v4.7-stable_win64.exe"
$usesPatchedMaterialMaker = (Test-Path $patchedMmPack) -and (Test-Path $patchedGodotExe) -and (Test-Path $patchedGodotGuiExe)
if (-not $usesPatchedMaterialMaker) {
    throw "The Material Maker 1.7 shutdown patch is not staged. Run tools/Setup-TextureAuthoringTools.ps1 -MaterialMaker or tools/Build-MaterialMakerCli.ps1."
}
$mmExe = $patchedGodotExe
$runtimeDir = Split-Path -Parent $mmExe
$prepareScript = Join-Path $repoRoot "tools/Prepare-MaterialMakerTacticalTemplate.py"
$prepare3dScript = Join-Path $repoRoot "tools/prepare_material_maker_3d_paint.py"
$applyBlenderScript = Join-Path $repoRoot "tools/apply_material_maker_to_blender.py"
$blenderPreviewSource = ""

$templateWasExplicit = -not [string]::IsNullOrWhiteSpace($TemplatePath)
if ($templateWasExplicit) {
    $template = if ([System.IO.Path]::IsPathRooted($TemplatePath)) { $TemplatePath } else { Join-Path $repoRoot $TemplatePath }
} elseif ($Workflow -eq "three_d") {
    $paintWorkspace = Join-Path $repoRoot "artifacts/texture-work/$UnitName/MaterialMaker"
    $paintArgs = @($prepare3dScript, "--unit-name", $UnitName, "--workspace", $paintWorkspace, "--preset", $Preset, "--size", "$SizePower")
    if (-not [string]::IsNullOrWhiteSpace($PaintSourceModel)) {
        $resolvedSource = if ([System.IO.Path]::IsPathRooted($PaintSourceModel)) { $PaintSourceModel } else { Join-Path $repoRoot $PaintSourceModel }
        $blenderPreviewSource = $resolvedSource
        $paintArgs += @("--source-model", $resolvedSource)
    }
    & python @paintArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Material Maker 3D paint workspace preparation failed with exit code $LASTEXITCODE"
    }
    $template = Join-Path $paintWorkspace "$UnitName.ptex"
} else {
    $templateFile = if ($Preset -eq "olive_armor") { "tactical-military-template.ptex" } else { "tactical-$Preset-template.ptex" }
    $template = Join-Path $repoRoot "artifacts/mm-templates/$templateFile"
}

if (-not [string]::IsNullOrWhiteSpace($BlenderPreviewModel)) {
    $blenderPreviewSource = if ([System.IO.Path]::IsPathRooted($BlenderPreviewModel)) {
        $BlenderPreviewModel
    } else {
        Join-Path $repoRoot $BlenderPreviewModel
    }
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $outDir = Join-Path $repoRoot ([string]$workflowConfig.outputDirectory)
} elseif ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $outDir = $OutputDirectory
} else {
    $outDir = Join-Path $repoRoot $OutputDirectory
}
$rawDir = Join-Path $outDir "raw"
$waitScript = Join-Path $repoRoot "tools/MaterialMakerWaitDevice.gd"
$packScript = Join-Path $repoRoot ([string]$config.exportProfile.packer)
$auditScript = Join-Path $repoRoot "tools/audit_material_maker_output.py"
$overrideCfg = Join-Path $runtimeDir "override.cfg"
$userExportDir = Join-Path $env:APPDATA "material_maker_2/export_targets"
$profileName = [string]$config.exportProfile.name
$exportMaterialName = [string]$config.exportProfile.material
$exportTargetName = $profileName.ToLowerInvariant()
foreach ($invalidFileNameCharacter in @(" ", "/", ".")) {
    $exportMaterialName = $exportMaterialName.Replace($invalidFileNameCharacter, "_")
    $exportTargetName = $exportTargetName.Replace($invalidFileNameCharacter, "_")
}
$exportTargetFile = Join-Path $userExportDir ("${exportMaterialName}_export_${exportTargetName}.mme")
$templatePrefix = [System.IO.Path]::GetFileNameWithoutExtension($template)

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
if (Test-Path $rawDir) {
    Remove-Item -LiteralPath $rawDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $rawDir | Out-Null
$staleOutputs = @(
    "${templatePrefix}_Albedo.png",
    "${templatePrefix}_MetallicGloss.png",
    "${templatePrefix}_Normal.png",
    "${templatePrefix}_Emission.png",
    "${templatePrefix}_pack-report.json",
    "${templatePrefix}_quality-report.json",
    "${templatePrefix}_blender-preview.blend",
    "${templatePrefix}_blender-preview.json",
    "material-maker-workflow.json"
)
foreach ($staleOutput in $staleOutputs) {
    $stalePath = Join-Path $outDir $staleOutput
    if (Test-Path $stalePath) {
        Remove-Item -LiteralPath $stalePath -Force
        Write-Host "Removed stale Material Maker output: $stalePath"
    }
}
New-Item -ItemType Directory -Force -Path $userExportDir | Out-Null

foreach ($required in @($configPath, $configValidator, $mmDir, $officialMmExe, $mmExe, $patchedGodotGuiExe, $patchedMmPack, $waitScript, $packScript, $auditScript, $prepareScript, $applyBlenderScript)) {
    if (-not (Test-Path $required)) {
        throw "Required Material Maker bake input is missing: $required"
    }
}
& python $configValidator --config $configPath
if ($LASTEXITCODE -ne 0) {
    throw "Material Maker workflow configuration is invalid"
}

$needsPrepare = (-not (Test-Path $template)) -or $ForceTemplate.IsPresent
if (-not $needsPrepare -and -not $templateWasExplicit -and (Test-Path $template)) {
    $existingContent = Get-Content -LiteralPath $template -Raw
    if ($existingContent -notmatch "colorize_metallic" -or
        $existingContent -notmatch "edge_detect_emission" -or
        $existingContent -notmatch "colorize_emission" -or
        $existingContent -notmatch "micro_perlin" -or
        $existingContent -notmatch "micro_detail_blend" -or
        $existingContent -notmatch "micro_roughness_blend" -or
        $existingContent -notmatch "tactical-hd-v2") {
        $needsPrepare = $true
    }
}
if ($needsPrepare) {
    if ($templateWasExplicit -and -not (Test-Path $template)) {
        throw "Explicit Material Maker template is missing: $template"
    }
    & python $prepareScript --output $template --preset $Preset --size $SizePower --force
    if ($LASTEXITCODE -ne 0) {
        throw "Material Maker template preparation failed with exit code $LASTEXITCODE"
    }
}

# Material Maker 1.7 issue #1506 workaround
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

$hadExportTarget = Test-Path $exportTargetFile
$backupExportTarget = Join-Path $env:TEMP ("rcl-material-maker-target-" + [guid]::NewGuid().ToString("N") + ".mme")
if ($hadExportTarget) {
    Copy-Item -LiteralPath $exportTargetFile -Destination $backupExportTarget -Force
}
$rawFiles = @()
foreach ($rawFile in $config.exportProfile.rawFiles) {
    $rawFiles += [ordered]@{
        file_name = "`$(path_prefix)_$($rawFile.suffix).png"
        type = "texture"
        output = [int]$rawFile.output
    }
}
$exportTarget = [ordered]@{
    name = $profileName
    material = [string]$config.exportProfile.material
    files = $rawFiles
}
$exportJson = $exportTarget | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText($exportTargetFile, $exportJson, $utf8NoBom)

try {
    Push-Location $mmDir
    try {
        $exportArguments = "--export-material --target $profileName -o `"$rawDir`" `"$template`""
        if ($usesPatchedMaterialMaker) {
            $mmArguments = "--main-pack `"$patchedMmPack`" $exportArguments"
            Write-Host "Using the local Material Maker 1.7 shutdown patch."
        } else {
            $mmArguments = $exportArguments
        }
        $hadDiagnosticEnvironment = Test-Path Env:RCL_MM_DIAG
        $previousDiagnosticEnvironment = $env:RCL_MM_DIAG
        if ($VerboseMaterialMaker.IsPresent) {
            $mmArguments = "--verbose $mmArguments"
            $env:RCL_MM_DIAG = "1"
        }
        $mmLog = Join-Path $outDir "material-maker.log"
        $mmErrorLog = Join-Path $outDir "material-maker-error.log"
        try {
            $materialMakerProcess = Start-Process -FilePath $mmExe -ArgumentList $mmArguments -WorkingDirectory $mmDir -WindowStyle Hidden -RedirectStandardOutput $mmLog -RedirectStandardError $mmErrorLog -Wait -PassThru
        } finally {
            if ($hadDiagnosticEnvironment) {
                $env:RCL_MM_DIAG = $previousDiagnosticEnvironment
            } else {
                Remove-Item Env:RCL_MM_DIAG -ErrorAction SilentlyContinue
            }
        }
        $materialMakerExitCode = $materialMakerProcess.ExitCode
        if ($null -ne $materialMakerExitCode -and $materialMakerExitCode -ne 0) {
            throw "Material Maker export failed with exit code $materialMakerExitCode"
        }
    } finally {
        Pop-Location
    }

    $packArgs = @($packScript, "--input-dir", $rawDir, "--output-dir", $outDir, "--prefix", $templatePrefix)
    if ($Strict.IsPresent) {
        $packArgs += "--strict"
    }
    & python @packArgs
    $packExitCode = $LASTEXITCODE
    if ($packExitCode -ne 0) {
        Write-Warning "Material Maker PBR packing returned $packExitCode; retrying once after the failed process has exited."
        & python @packArgs
        $packExitCode = $LASTEXITCODE
    }
    if ($packExitCode -ne 0) {
        throw "Material Maker PBR pack failed with exit code $packExitCode after one retry."
    }
    $qualityReport = Join-Path $outDir ("${templatePrefix}_quality-report.json")
    $auditArgs = @(
        $auditScript,
        "--directory", $outDir,
        "--prefix", $templatePrefix,
        "--template", $template,
        "--workflow", $Workflow,
        "--output", $qualityReport
    )
    if ($Workflow -eq "three_d") {
        $auditArgs += @("--workspace", (Split-Path -Parent $template))
    }
    & python @auditArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Material Maker quality audit failed with exit code $LASTEXITCODE"
    }
    $qualityData = Get-Content -LiteralPath $qualityReport -Raw | ConvertFrom-Json
    $requestedResolution = 1 -shl $SizePower
    $actualResolution = 0
    if ($null -ne $qualityData.dimensions -and $qualityData.dimensions.Count -gt 0) {
        $actualResolution = [int]$qualityData.dimensions[0][0]
    }
    if ($Strict.IsPresent -and $actualResolution -ne $requestedResolution) {
        throw "Material Maker renderer produced ${actualResolution}x${actualResolution}; requested ${requestedResolution}x${requestedResolution}"
    }
    $blenderPreviewBlend = ""
    $blenderPreviewReport = ""
    if (-not [string]::IsNullOrWhiteSpace($blenderPreviewSource)) {
        if (-not (Test-Path $blenderPreviewSource)) {
            throw "Blender preview source model is missing: $blenderPreviewSource"
        }
        $blenderPreviewBlend = Join-Path $outDir "${templatePrefix}_blender-preview.blend"
        $blenderPreviewReport = Join-Path $outDir "${templatePrefix}_blender-preview.json"
        $previewArgs = @(
            $applyBlenderScript,
            "--model", $blenderPreviewSource,
            "--texture-dir", $outDir,
            "--prefix", $templatePrefix,
            "--output-blend", $blenderPreviewBlend,
            "--report", $blenderPreviewReport
        )
        & python @previewArgs
        if ($LASTEXITCODE -ne 0 -or
            -not (Test-Path $blenderPreviewBlend) -or
            -not (Test-Path $blenderPreviewReport)) {
            throw "Blender PBR preview generation failed with exit code $LASTEXITCODE"
        }
    }
    $workflowManifest = [ordered]@{
        workflow = $Workflow
        preset = $Preset
        resolutionRequested = $requestedResolution
        resolutionActual = $actualResolution
        profile = $profileName
        pipeline = [string]$config.textureContract.pipeline
        shader = [string]$config.textureContract.shader
        template = $template
        outputDirectory = $outDir
        sourceAuthority = [string]$workflowConfig.sourceAuthority
        postProcess = [string]$workflowConfig.postProcess
        blenderPreviewSource = $blenderPreviewSource
        blenderPreviewBlend = $blenderPreviewBlend
        blenderPreviewReport = $blenderPreviewReport
    }
    [System.IO.File]::WriteAllText(
        (Join-Path $outDir "material-maker-workflow.json"),
        ($workflowManifest | ConvertTo-Json -Depth 8),
        $utf8NoBom)
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

$expected = @()
foreach ($map in $config.textureContract.requiredMaps) {
    $expected += "${templatePrefix}_$($map.suffix).png"
}
foreach ($name in $expected) {
    $path = Join-Path $outDir $name
    if (-not (Test-Path $path)) {
        throw "Material Maker bake completed without required map: $path"
    }
}

Write-Host "Material Maker bake output: $outDir"
Get-ChildItem -LiteralPath $outDir -File | Select-Object Name, Length, LastWriteTime
