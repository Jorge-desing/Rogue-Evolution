param(
    [ValidateSet(9, 10, 11, 12)]
    [int]$SizePower = 11,
    [ValidateRange(0, 2147483647)]
    [int]$DilatePx = 0,
    [string]$DetailNormalPath = "",
    [ValidateScript({ $_ -ge 0.0 -and $_ -le 1.0 -and -not [double]::IsNaN($_) -and -not [double]::IsInfinity($_) })]
    [double]$DetailNormalStrength = 0.35,
    [ValidateRange(1, 8)]
    [int]$MaxParallelExports = 4,
    [ValidateRange(1, 8)]
    [int]$PackWorkers = 4,
    [switch]$OptimizePng,
    [switch]$SkipPromote
)

$ErrorActionPreference = "Stop"
$batchStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

function Stop-MaterialMakerProcess {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $Process.Refresh()
    if (-not $Process.HasExited) {
        try {
            $Process.Kill()
        } catch {
            $Process.Refresh()
            if (-not $Process.HasExited) {
                throw "Could not stop Material Maker process for ${Context}: $($_.Exception.Message)"
            }
        }
    }
    if (-not $Process.WaitForExit(10000)) {
        throw "Material Maker process for $Context did not exit within 10 seconds after termination was requested. PID=$($Process.Id)"
    }
    $Process.Refresh()
    if (-not $Process.HasExited) {
        throw "Material Maker process for $Context is still running after WaitForExit. PID=$($Process.Id)"
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repoRoot "content/source-art/canonical-visual-modules.json"
$batchScript = Join-Path $repoRoot "tools/batch_material_maker_textures.py"
$configPath = Join-Path $repoRoot "tools/material-maker/rogue-command-material-maker.json"
$waitScript = Join-Path $repoRoot "tools/MaterialMakerWaitDevice.gd"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$presets = @($manifest.modules | ForEach-Object { [string]$_.preset } | Sort-Object -Unique)
if ($presets.Count -ne 28) {
    throw "Expected 28 unique canonical module texture presets; found $($presets.Count)."
}

$venvPython = Join-Path $repoRoot ".venv/Scripts/python.exe"
$pythonExe = if (Test-Path -LiteralPath $venvPython -PathType Leaf) { $venvPython } else { "python" }

$artifactRoot = $env:RCL_CANONICAL_VISUAL_ARTIFACT_ROOT
if ([string]::IsNullOrWhiteSpace($artifactRoot)) {
    $artifactRoot = Join-Path $repoRoot "artifacts/canonical-two-level"
}
$artifactRoot = [System.IO.Path]::GetFullPath($artifactRoot)
$artifactPrefix = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts")).TrimEnd('\') + '\'
if (-not $artifactRoot.StartsWith($artifactPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Canonical texture bake output must remain under repository artifacts: $artifactRoot"
}
$resolvedDetailNormal = ""
if (-not [string]::IsNullOrWhiteSpace($DetailNormalPath)) {
    $resolvedDetailNormal = if ([System.IO.Path]::IsPathRooted($DetailNormalPath)) { $DetailNormalPath } else { Join-Path $repoRoot $DetailNormalPath }
    if (-not (Test-Path -LiteralPath $resolvedDetailNormal -PathType Leaf)) {
        throw "Detail normal map does not exist: $resolvedDetailNormal"
    }
}

$unityTextureDir = Join-Path $repoRoot "unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/VisualModules/Textures"
New-Item -ItemType Directory -Force -Path $unityTextureDir | Out-Null

$runningMaterialMaker = Get-Process -Name "material_maker" -ErrorAction SilentlyContinue
if ($runningMaterialMaker) {
    throw "Material Maker is already running. Close its existing session before this batch, because its export target is temporarily configured by the authoring runner."
}

$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$mmDir = Join-Path $repoRoot ".tools/MaterialMaker-1.7/material_maker_1_7_windows"
$officialMmExe = Join-Path $repoRoot $config.tool.installRelativePath
$patchedRuntimeDir = Join-Path $repoRoot ".tools/MaterialMaker-1.7-patched/runtime"
$patchedMmPack = Join-Path $patchedRuntimeDir "material_maker.pck"
$mmExe = Join-Path $patchedRuntimeDir "Godot_v4.7-stable_win64_console.exe"
$patchedGodotGuiExe = Join-Path $patchedRuntimeDir "Godot_v4.7-stable_win64.exe"
foreach ($required in @($configPath, $batchScript, $mmDir, $officialMmExe, $mmExe, $patchedGodotGuiExe, $patchedMmPack, $waitScript)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required Material Maker batch input is missing: $required"
    }
}

$batchEntries = New-Object System.Collections.Generic.List[object]
$templateRoot = Join-Path $repoRoot "artifacts/mm-templates"
foreach ($preset in $presets) {
    $unitName = "RCLModule_$preset"
    $templateFile = if ($preset -eq "olive_armor") { "tactical-military-template.ptex" } else { "tactical-$preset-template.ptex" }
    $templatePath = Join-Path $templateRoot $templateFile
    $outputDir = Join-Path $artifactRoot "texture-work/$preset"
    $rawDir = Join-Path $outputDir "raw"
    $batchEntries.Add([ordered]@{
        preset = $preset
        unitName = $unitName
        template = $templatePath
        outputDir = $outputDir
        rawDir = $rawDir
        templatePrefix = [System.IO.Path]::GetFileNameWithoutExtension($templatePath)
    })
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$batchManifestPath = Join-Path $env:TEMP ("rcl-mm-batch-" + [guid]::NewGuid().ToString("N") + ".json")
$runtimeDir = Split-Path -Parent $mmExe
$overrideCfg = Join-Path $runtimeDir "override.cfg"
$hadOverride = Test-Path -LiteralPath $overrideCfg
$backupOverride = Join-Path $env:TEMP ("rcl-material-maker-override-" + [guid]::NewGuid().ToString("N") + ".cfg")

$userExportDir = Join-Path $env:APPDATA "material_maker_2/export_targets"
New-Item -ItemType Directory -Force -Path $userExportDir | Out-Null
$profileName = [string]$config.exportProfile.name
$exportMaterialName = [string]$config.exportProfile.material
$exportTargetName = $profileName.ToLowerInvariant()
foreach ($invalidFileNameCharacter in @(" ", "/", ".")) {
    $exportMaterialName = $exportMaterialName.Replace($invalidFileNameCharacter, "_")
    $exportTargetName = $exportTargetName.Replace($invalidFileNameCharacter, "_")
}
$exportTargetFile = Join-Path $userExportDir ("${exportMaterialName}_export_${exportTargetName}.mme")
$hadExportTarget = Test-Path -LiteralPath $exportTargetFile
$backupExportTarget = Join-Path $env:TEMP ("rcl-material-maker-target-" + [guid]::NewGuid().ToString("N") + ".mme")

try {
    [System.IO.File]::WriteAllText($batchManifestPath, ($batchEntries | ConvertTo-Json -Depth 6), $utf8NoBom)

    Write-Host "[1/4] Preparing $($batchEntries.Count) Material Maker 1.7 templates (sizePower=$SizePower)..."
    & $pythonExe $batchScript prepare --config $configPath --batch-manifest $batchManifestPath --artifact-root $artifactRoot --template-root $templateRoot --size $SizePower --force
    if ($LASTEXITCODE -ne 0) {
        throw "Batch Material Maker template preparation failed with exit code $LASTEXITCODE."
    }

    if ($hadOverride) {
        Copy-Item -LiteralPath $overrideCfg -Destination $backupOverride -Force
    }
    $waitScriptAbsolute = (Resolve-Path -LiteralPath $waitScript).Path.Replace('\', '/')
    $quote = [char]34
    $overrideText = "[autoload]`r`n`r`nmm_wait_device=$quote*$waitScriptAbsolute$quote`r`n"
    [System.IO.File]::WriteAllText($overrideCfg, $overrideText, $utf8NoBom)

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
    [System.IO.File]::WriteAllText($exportTargetFile, ($exportTarget | ConvertTo-Json -Depth 8), $utf8NoBom)

    Write-Host "[2/4] Exporting $($batchEntries.Count) presets via Material Maker 1.7 CLI (maxParallel=$MaxParallelExports)..."
    $exportStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $queue = New-Object System.Collections.Generic.Queue[object]
    foreach ($entry in $batchEntries) {
        $queue.Enqueue($entry)
    }
    $active = New-Object System.Collections.Generic.List[object]
    $failedEntries = New-Object System.Collections.Generic.List[object]
    $completedExports = 0
    $exportFailure = $null

    Push-Location $mmDir
    try {
        while ($queue.Count -gt 0 -or $active.Count -gt 0) {
            while ($queue.Count -gt 0 -and $active.Count -lt $MaxParallelExports) {
                $entry = $queue.Dequeue()
                $mmLog = Join-Path $entry.outputDir "material-maker.log"
                $mmErrorLog = Join-Path $entry.outputDir "material-maker-error.log"
                $mmArgs = "--main-pack `"$patchedMmPack`" --export-material --target $profileName -o `"$($entry.rawDir)`" `"$($entry.template)`""
                $proc = Start-Process -FilePath $mmExe -ArgumentList $mmArgs -WorkingDirectory $runtimeDir -WindowStyle Hidden -RedirectStandardOutput $mmLog -RedirectStandardError $mmErrorLog -PassThru
                $null = $proc.Handle
                $active.Add([PSCustomObject]@{
                    Entry = $entry
                    Process = $proc
                    StartedAt = [DateTime]::UtcNow
                })
                if ($queue.Count -gt 0 -and $active.Count -lt $MaxParallelExports) {
                    Start-Sleep -Milliseconds 150
                }
            }

            Start-Sleep -Milliseconds 100
            for ($i = $active.Count - 1; $i -ge 0; $i--) {
                $item = $active[$i]
                $proc = $item.Process
                if ($proc.HasExited) {
                    $exitCode = $proc.ExitCode
                    $proc.Dispose()
                    $active.RemoveAt($i)
                    $completedExports++
                    if ($null -ne $exitCode -and $exitCode -ne 0) {
                        Write-Warning "Parallel Material Maker export failed for '$($item.Entry.preset)' (exit=$exitCode); queuing sequential retry."
                        $failedEntries.Add($item.Entry)
                    } else {
                        Write-Host ("  Exported [{0}/{1}] {2}" -f $completedExports, $batchEntries.Count, $item.Entry.preset)
                    }
                } elseif (([DateTime]::UtcNow - $item.StartedAt).TotalSeconds -gt 240) {
                    Stop-MaterialMakerProcess -Process $proc -Context "parallel attempt for '$($item.Entry.preset)'"
                    $proc.Dispose()
                    $active.RemoveAt($i)
                    Write-Warning "Material Maker export timed out for '$($item.Entry.preset)'; queuing sequential retry."
                    $failedEntries.Add($item.Entry)
                }
            }
        }

        foreach ($retryEntry in $failedEntries) {
            if (Test-Path -LiteralPath $retryEntry.rawDir) {
                Remove-Item -LiteralPath $retryEntry.rawDir -Recurse -Force
            }
            New-Item -ItemType Directory -Force -Path $retryEntry.rawDir | Out-Null
            $mmLog = Join-Path $retryEntry.outputDir "material-maker-retry.log"
            $mmErrorLog = Join-Path $retryEntry.outputDir "material-maker-retry-error.log"
            $mmArgs = "--main-pack `"$patchedMmPack`" --export-material --target $profileName -o `"$($retryEntry.rawDir)`" `"$($retryEntry.template)`""
            $retryProc = Start-Process -FilePath $mmExe -ArgumentList $mmArgs -WorkingDirectory $runtimeDir -WindowStyle Hidden -RedirectStandardOutput $mmLog -RedirectStandardError $mmErrorLog -PassThru
            try {
                if (-not $retryProc.WaitForExit(240000)) {
                    Stop-MaterialMakerProcess -Process $retryProc -Context "sequential retry for '$($retryEntry.preset)'"
                    throw "Material Maker export timed out for preset '$($retryEntry.preset)' during its bounded retry."
                }
                $retryProc.Refresh()
                if ($null -ne $retryProc.ExitCode -and $retryProc.ExitCode -ne 0) {
                    throw "Material Maker export failed for preset '$($retryEntry.preset)' after retry with exit code $($retryProc.ExitCode)."
                }
            } finally {
                if (-not $retryProc.HasExited) {
                    Stop-MaterialMakerProcess -Process $retryProc -Context "final cleanup of '$($retryEntry.preset)' retry"
                }
                $retryProc.Dispose()
            }
        }
    } catch {
        $exportFailure = $_
        throw
    } finally {
        $cleanupFailures = New-Object System.Collections.Generic.List[string]
        foreach ($item in $active) {
            try {
                if (-not $item.Process.HasExited) {
                    Stop-MaterialMakerProcess -Process $item.Process -Context "cleanup of '$($item.Entry.preset)'"
                }
            } catch {
                $cleanupFailures.Add("$($item.Entry.preset): $($_.Exception.Message)")
            } finally {
                $item.Process.Dispose()
            }
        }
        Pop-Location
        if ($cleanupFailures.Count -gt 0) {
            $cleanupMessage = "Failed to confirm cleanup of Material Maker processes: $($cleanupFailures -join '; ')"
            if ($null -ne $exportFailure) {
                $cleanupMessage += " Primary export error: $($exportFailure.Exception.ToString())"
            }
            throw $cleanupMessage
        }
    }
    Write-Host ("  Material Maker GPU exports completed in {0:F2}s." -f $exportStopwatch.Elapsed.TotalSeconds)

    Write-Host "[3/4] Packing PBR channels + RNM detail + auditing quality gates ($PackWorkers workers)..."
    $packAuditArgs = @(
        $batchScript,
        "pack-audit",
        "--config", $configPath,
        "--batch-manifest", $batchManifestPath,
        "--artifact-root", $artifactRoot,
        "--template-root", $templateRoot,
        "--workflow", "procedural",
        "--size", "$SizePower",
        "--dilate-px", "$DilatePx",
        "--detail-strength", "$DetailNormalStrength",
        "--workers", "$PackWorkers",
        "--strict"
    )
    if (-not [string]::IsNullOrWhiteSpace($resolvedDetailNormal)) {
        $packAuditArgs += @("--detail-normal", $resolvedDetailNormal)
    }
    if ($OptimizePng.IsPresent) {
        $packAuditArgs += "--optimize-png"
    }
    & $pythonExe @packAuditArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Batch Material Maker pack/audit failed with exit code $LASTEXITCODE."
    }

    Write-Host "[4/4] Promoting validated PBR maps..."
    $promotedCopied = 0
    $promotedKept = 0
    if (-not $SkipPromote) {
        foreach ($entry in $batchEntries) {
            $resolvedOutput = $entry.outputDir
            $templatePrefix = $entry.templatePrefix
            $unitName = $entry.unitName
            foreach ($suffix in @("Albedo", "MetallicGloss", "Normal", "Emission")) {
                $source = Join-Path $resolvedOutput "${templatePrefix}_$suffix.png"
                if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
                    throw "Material Maker output is missing: $source"
                }
                $target = Join-Path $unityTextureDir "${unitName}_$suffix.png"
                $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
                if (Test-Path -LiteralPath $target -PathType Leaf) {
                    $targetHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
                    if (-not [String]::Equals($sourceHash, $targetHash, [StringComparison]::OrdinalIgnoreCase)) {
                        Copy-Item -LiteralPath $source -Destination $target -Force
                        $promotedCopied++
                    } else {
                        $promotedKept++
                    }
                } else {
                    Copy-Item -LiteralPath $source -Destination $target -Force
                    $promotedCopied++
                }
            }
        }
        $promoted = @(Get-ChildItem -LiteralPath $unityTextureDir -File -Filter "RCLModule_*.png")
        if ($promoted.Count -ne 112) {
            throw "Expected 112 promoted PBR maps (28 presets x 4), found $($promoted.Count)."
        }
        Write-Host ("Promoted 28 validated four-map PBR sets to {0} (updated={1}, unchanged={2}) in {3:F2}s total." -f $unityTextureDir, $promotedCopied, $promotedKept, $batchStopwatch.Elapsed.TotalSeconds)
    } else {
        Write-Host ("Completed all 28 bakes without promoting maps to Unity in {0:F2}s total." -f $batchStopwatch.Elapsed.TotalSeconds)
    }
} finally {
    if ($hadOverride) {
        Move-Item -LiteralPath $backupOverride -Destination $overrideCfg -Force
    } elseif (Test-Path -LiteralPath $overrideCfg) {
        Remove-Item -LiteralPath $overrideCfg -Force
    }
    if (Test-Path -LiteralPath $backupOverride) {
        Remove-Item -LiteralPath $backupOverride -Force
    }
    if ($hadExportTarget) {
        Move-Item -LiteralPath $backupExportTarget -Destination $exportTargetFile -Force
    } elseif (Test-Path -LiteralPath $exportTargetFile) {
        Remove-Item -LiteralPath $exportTargetFile -Force
    }
    if (Test-Path -LiteralPath $backupExportTarget) {
        Remove-Item -LiteralPath $backupExportTarget -Force
    }
    if (Test-Path -LiteralPath $batchManifestPath) {
        Remove-Item -LiteralPath $batchManifestPath -Force
    }
}
