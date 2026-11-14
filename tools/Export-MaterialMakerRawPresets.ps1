param(
    [Parameter(Mandatory = $true)]
    [string[]]$Presets,
    [ValidateSet(9, 10, 11, 12)]
    [int]$SizePower = 11,
    [string]$OutputRoot = "",
    [ValidateRange(1, 8)]
    [int]$MaxParallelExports = 4,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

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
        throw "Material Maker process for $Context did not exit within 10s. PID=$($Process.Id)"
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$venvPython = Join-Path $repoRoot ".venv/Scripts/python.exe"
$pythonExe = if (Test-Path -LiteralPath $venvPython -PathType Leaf) { $venvPython } else { "python" }

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts/level1-mm-cache"
} elseif (-not [System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot $OutputRoot
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$batchScript = Join-Path $repoRoot "tools/batch_material_maker_textures.py"
$configPath = Join-Path $repoRoot "tools/material-maker/rogue-command-material-maker.json"
$waitScript = Join-Path $repoRoot "tools/MaterialMakerWaitDevice.gd"
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json

$mmDir = Join-Path $repoRoot ".tools/MaterialMaker-1.7/material_maker_1_7_windows"
$patchedRuntimeDir = Join-Path $repoRoot ".tools/MaterialMaker-1.7-patched/runtime"
$patchedMmPack = Join-Path $patchedRuntimeDir "material_maker.pck"
$mmExe = Join-Path $patchedRuntimeDir "Godot_v4.7-stable_win64_console.exe"
foreach ($required in @($configPath, $batchScript, $mmDir, $mmExe, $patchedMmPack, $waitScript)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required Material Maker input is missing: $required"
    }
}

$runningMaterialMaker = Get-Process -Name "material_maker" -ErrorAction SilentlyContinue
if ($runningMaterialMaker) {
    throw "Close running Material Maker GUI before running batch CLI export."
}

$templateRoot = Join-Path $repoRoot "artifacts/mm-templates"
New-Item -ItemType Directory -Force -Path $templateRoot | Out-Null

$uniquePresets = @($Presets | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Sort-Object -Unique)
$pendingEntries = New-Object System.Collections.Generic.List[object]
$rawSuffixes = @("Albedo", "Metallic", "Roughness", "Normal", "Emission")

foreach ($preset in $uniquePresets) {
    $templateFile = if ($preset -eq "olive_armor") { "tactical-military-template.ptex" } else { "tactical-$preset-template.ptex" }
    $templatePath = Join-Path $templateRoot $templateFile
    $templatePrefix = [System.IO.Path]::GetFileNameWithoutExtension($templatePath)
    $outputDir = Join-Path $OutputRoot "texture-work/$preset"
    $rawDir = Join-Path $outputDir "raw"

    $allPresent = $true
    foreach ($suf in $rawSuffixes) {
        if (-not (Test-Path -LiteralPath (Join-Path $rawDir "${templatePrefix}_$suf.png") -PathType Leaf)) {
            $allPresent = $false
            break
        }
    }
    if ($allPresent -and -not $Force) {
        continue
    }

    $pendingEntries.Add([ordered]@{
        preset = $preset
        unitName = "RCLLevel1Raw_$preset"
        template = $templatePath
        outputDir = $outputDir
        rawDir = $rawDir
        templatePrefix = $templatePrefix
    })
}

if ($pendingEntries.Count -eq 0) {
    Write-Host "[MaterialMakerRaw] All $($uniquePresets.Count) presets already cached in $OutputRoot."
    exit 0
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$batchManifestPath = Join-Path $env:TEMP ("rcl-mm-l1-raw-" + [guid]::NewGuid().ToString("N") + ".json")
$runtimeDir = Split-Path -Parent $mmExe
$overrideCfg = Join-Path $runtimeDir "override.cfg"
$hadOverride = Test-Path -LiteralPath $overrideCfg
$backupOverride = Join-Path $env:TEMP ("rcl-mm-override-" + [guid]::NewGuid().ToString("N") + ".cfg")

$userExportDir = Join-Path $env:APPDATA "material_maker_2/export_targets"
New-Item -ItemType Directory -Force -Path $userExportDir | Out-Null
$profileName = [string]$config.exportProfile.name
$exportMaterialName = ([string]$config.exportProfile.material).Replace(" ", "_").Replace("/", "_").Replace(".", "_")
$exportTargetName = $profileName.ToLowerInvariant().Replace(" ", "_").Replace("/", "_").Replace(".", "_")
$exportTargetFile = Join-Path $userExportDir ("${exportMaterialName}_export_${exportTargetName}.mme")
$hadExportTarget = Test-Path -LiteralPath $exportTargetFile
$backupExportTarget = Join-Path $env:TEMP ("rcl-mm-target-" + [guid]::NewGuid().ToString("N") + ".mme")

try {
    [System.IO.File]::WriteAllText($batchManifestPath, (ConvertTo-Json -InputObject @($pendingEntries.ToArray()) -Depth 6), $utf8NoBom)

    Write-Host "[MaterialMakerRaw] Preparing $($pendingEntries.Count) templates (sizePower=$SizePower)..."
    & $pythonExe $batchScript prepare --config $configPath --batch-manifest $batchManifestPath --artifact-root $OutputRoot --template-root $templateRoot --size $SizePower --force
    if ($LASTEXITCODE -ne 0) {
        throw "Material Maker template preparation failed ($LASTEXITCODE)."
    }

    if ($hadOverride) { Copy-Item -LiteralPath $overrideCfg -Destination $backupOverride -Force }
    $waitScriptAbsolute = (Resolve-Path -LiteralPath $waitScript).Path.Replace('\', '/')
    [System.IO.File]::WriteAllText($overrideCfg, "[autoload]`r`n`r`nmm_wait_device=`"*$waitScriptAbsolute`"`r`n", $utf8NoBom)

    if ($hadExportTarget) { Copy-Item -LiteralPath $exportTargetFile -Destination $backupExportTarget -Force }
    $rawFiles = foreach ($rawFile in $config.exportProfile.rawFiles) {
        [ordered]@{
            file_name = "`$(path_prefix)_$($rawFile.suffix).png"
            type = "texture"
            output = [int]$rawFile.output
        }
    }
    $exportTarget = [ordered]@{
        name = $profileName
        material = [string]$config.exportProfile.material
        files = @($rawFiles)
    }
    [System.IO.File]::WriteAllText($exportTargetFile, ($exportTarget | ConvertTo-Json -Depth 8), $utf8NoBom)

    Write-Host "[MaterialMakerRaw] Exporting $($pendingEntries.Count) Material Maker 1.7 presets..."
    $queue = New-Object System.Collections.Generic.Queue[object]
    foreach ($e in $pendingEntries) { $queue.Enqueue($e) }
    $active = New-Object System.Collections.Generic.List[object]
    $failed = New-Object System.Collections.Generic.List[object]
    $completed = 0

    Push-Location $mmDir
    try {
        while ($queue.Count -gt 0 -or $active.Count -gt 0) {
            while ($queue.Count -gt 0 -and $active.Count -lt $MaxParallelExports) {
                $e = $queue.Dequeue()
                $mmLog = Join-Path $e.outputDir "material-maker.log"
                $mmErr = Join-Path $e.outputDir "material-maker-error.log"
                $mmArgs = "--main-pack `"$patchedMmPack`" --export-material --target $profileName -o `"$($e.rawDir)`" `"$($e.template)`""
                $proc = Start-Process -FilePath $mmExe -ArgumentList $mmArgs -WorkingDirectory $runtimeDir -WindowStyle Hidden -RedirectStandardOutput $mmLog -RedirectStandardError $mmErr -PassThru
                $null = $proc.Handle
                $active.Add([PSCustomObject]@{ Entry = $e; Process = $proc; StartedAt = [DateTime]::UtcNow })
                if ($queue.Count -gt 0 -and $active.Count -lt $MaxParallelExports) { Start-Sleep -Milliseconds 150 }
            }
            Start-Sleep -Milliseconds 100
            for ($i = $active.Count - 1; $i -ge 0; $i--) {
                $item = $active[$i]
                if ($item.Process.HasExited) {
                    $code = $item.Process.ExitCode
                    $item.Process.Dispose()
                    $active.RemoveAt($i)
                    $completed++
                    if ($null -ne $code -and $code -ne 0) {
                        $failed.Add($item.Entry)
                    } else {
                        Write-Host ("  Exported [{0}/{1}] {2}" -f $completed, $pendingEntries.Count, $item.Entry.preset)
                    }
                } elseif (([DateTime]::UtcNow - $item.StartedAt).TotalSeconds -gt 240) {
                    Stop-MaterialMakerProcess -Process $item.Process -Context $item.Entry.preset
                    $item.Process.Dispose()
                    $active.RemoveAt($i)
                    $failed.Add($item.Entry)
                }
            }
        }
        foreach ($retry in $failed) {
            if (Test-Path -LiteralPath $retry.rawDir) { Remove-Item -LiteralPath $retry.rawDir -Recurse -Force }
            New-Item -ItemType Directory -Force -Path $retry.rawDir | Out-Null
            $mmArgs = "--main-pack `"$patchedMmPack`" --export-material --target $profileName -o `"$($retry.rawDir)`" `"$($retry.template)`""
            $p = Start-Process -FilePath $mmExe -ArgumentList $mmArgs -WorkingDirectory $runtimeDir -WindowStyle Hidden -PassThru
            if (-not $p.WaitForExit(240000)) { Stop-MaterialMakerProcess -Process $p -Context $retry.preset; throw "Timeout exporting $($retry.preset)" }
            if ($null -ne $p.ExitCode -and $p.ExitCode -ne 0) { throw "Failed exporting $($retry.preset) (exit=$($p.ExitCode))" }
            $p.Dispose()
            Write-Host "  Retried OK: $($retry.preset)"
        }
    } finally {
        foreach ($item in $active) {
            try { if (-not $item.Process.HasExited) { Stop-MaterialMakerProcess -Process $item.Process -Context $item.Entry.preset } } finally { $item.Process.Dispose() }
        }
        Pop-Location
    }
} finally {
    if ($hadOverride) { Move-Item -LiteralPath $backupOverride -Destination $overrideCfg -Force }
    elseif (Test-Path -LiteralPath $overrideCfg) { Remove-Item -LiteralPath $overrideCfg -Force }
    if (Test-Path -LiteralPath $backupOverride) { Remove-Item -LiteralPath $backupOverride -Force }

    if ($hadExportTarget) { Move-Item -LiteralPath $backupExportTarget -Destination $exportTargetFile -Force }
    elseif (Test-Path -LiteralPath $exportTargetFile) { Remove-Item -LiteralPath $exportTargetFile -Force }
    if (Test-Path -LiteralPath $backupExportTarget) { Remove-Item -LiteralPath $backupExportTarget -Force }

    if (Test-Path -LiteralPath $batchManifestPath) { Remove-Item -LiteralPath $batchManifestPath -Force }
}
