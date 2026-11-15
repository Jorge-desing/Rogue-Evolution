param(
    [string[]]$Packs = @("sample-native-expansion", "tactical-operations"),
    [switch]$Incremental,
    [switch]$SkipBlender,
    [switch]$SkipTextures,
    [switch]$SkipLevelOneRefresh,
    [switch]$SkipUnityBuild,
    [switch]$SkipAnimationMatrix,
    [switch]$NoPromoteBundles,
    [switch]$RebuildLevelOneModels,
    [switch]$RebuildLevelOneTextures,
    [switch]$SkipNativeVisualValidation,
    [string]$GameRoot = "C:\Program Files (x86)\Steam\steamapps\common\Rogue Command",
    [string]$RestoreBundlesFrom = "",
    [switch]$PackageLoader,
    [switch]$DryRun,
    [string]$ArtifactRoot = "",
    [ValidateSet(9, 10, 11, 12)][int]$TextureSizePower = 11,
    [ValidateRange(1, 8)][int]$MaxParallelExports = 4,
    [ValidateRange(1, 8)][int]$PackWorkers = 4
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "unity/RogueCommandAssetAuthoring"
$unityExe = Join-Path $env:ProgramFiles "Unity/Hub/Editor/2021.3.45f2/Editor/Unity.exe"
$blenderExe = Join-Path $repoRoot ".tools/Blender-4.5.14/blender-4.5.14-windows-x64/blender.exe"
$geometryScript = Join-Path $repoRoot "tools/Build-CanonicalVisualModules.py"
$textureScript = Join-Path $repoRoot "tools/Build-CanonicalVisualModuleTextures.ps1"
$pythonExe = Join-Path $repoRoot ".venv/Scripts/python.exe"
$provenanceScript = Join-Path $PSScriptRoot "CanonicalVisualBuildProvenance.ps1"
$packageLoaderScript = Join-Path $PSScriptRoot "Package-Loader.ps1"
. $provenanceScript

function Resolve-ValidatedPackNames {
    param(
        [string[]]$RawPacks,
        [string]$RepositoryRoot
    )

    if ($null -eq $RawPacks -or $RawPacks.Count -eq 0) {
        throw "At least one pack name must be provided in -Packs."
    }
    $validated = [System.Collections.Generic.List[string]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($rawPack in $RawPacks) {
        $packName = if ($null -eq $rawPack) { "" } else { $rawPack.Trim() }
        if ([string]::IsNullOrWhiteSpace($packName) -or $packName -notmatch '^[a-z0-9][a-z0-9-]*$') {
            throw "Invalid canonical pack name: '$rawPack'. Expected lowercase alphanumeric with hyphens (^[a-z0-9][a-z0-9-]*$)."
        }
        $packContentPath = Join-Path $RepositoryRoot "content/packs/$packName/content.json"
        if (-not (Test-Path -LiteralPath $packContentPath -PathType Leaf)) {
            throw "Canonical pack content.json was not found for '$packName': $packContentPath"
        }
        if ($seen.Add($packName)) {
            [void]$validated.Add($packName)
        }
    }
    return @($validated)
}

# Handle standalone -RestoreBundlesFrom before normal pipeline planning
if (-not [string]::IsNullOrWhiteSpace($RestoreBundlesFrom)) {
    $resolvedRestoreRoot = if ([System.IO.Path]::IsPathRooted($RestoreBundlesFrom)) {
        [System.IO.Path]::GetFullPath($RestoreBundlesFrom)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path $repoRoot $RestoreBundlesFrom))
    }
    if (-not (Test-Path -LiteralPath $resolvedRestoreRoot -PathType Container)) {
        throw "Restore backup directory does not exist: $resolvedRestoreRoot"
    }

    $explicitPacksProvided = $PSBoundParameters.ContainsKey("Packs")
    if ($explicitPacksProvided) {
        $Packs = Resolve-ValidatedPackNames -RawPacks $Packs -RepositoryRoot $repoRoot
        $restorePackNames = @($Packs)
        $discoveredManifests = [System.Collections.Generic.List[string]]::new()
        foreach ($packName in $restorePackNames) {
            $candidateManifest = Join-Path (Join-Path $resolvedRestoreRoot $packName) "backup-manifest.json"
            if (Test-Path -LiteralPath $candidateManifest -PathType Leaf) {
                [void]$discoveredManifests.Add($candidateManifest)
            }
        }
    } else {
        $discoveredPacks = [System.Collections.Generic.List[string]]::new()
        $discoveredManifests = [System.Collections.Generic.List[string]]::new()
        foreach ($dir in @(Get-ChildItem -LiteralPath $resolvedRestoreRoot -Directory -Force | Sort-Object -Property Name)) {
            $candidateManifest = Join-Path $dir.FullName "backup-manifest.json"
            if (Test-Path -LiteralPath $candidateManifest -PathType Leaf) {
                [void]$discoveredPacks.Add($dir.Name)
                [void]$discoveredManifests.Add($candidateManifest)
            }
        }
        $restorePackNames = @($discoveredPacks)
    }

    if ($DryRun) {
        Write-Host "=== Canonical Visual Modules Restore (DryRun) ==="
        Write-Host "backupRoot: $resolvedRestoreRoot"
        Write-Host ("packs: {0}" -f $(if ($restorePackNames.Count -gt 0) { $restorePackNames -join ', ' } else { '(none)' }))
        Write-Host "manifests:"
        if ($discoveredManifests.Count -eq 0) {
            Write-Host "  - (none)"
        } else {
            foreach ($manifestPath in $discoveredManifests) {
                Write-Host "  - $manifestPath"
            }
        }
        exit 0
    }

    $restoreSummary = @(Restore-CanonicalBundlePromotion `
        -RepositoryRoot $repoRoot `
        -BackupRoot $resolvedRestoreRoot `
        -PackNames $restorePackNames)
    Write-Host "=== Canonical Visual Modules Restore Summary ==="
    Write-Host "backupRoot: $resolvedRestoreRoot"
    foreach ($packSummary in $restoreSummary) {
        Write-Host ("  - {0}: restored={1}, removed={2} (manifest: {3})" -f `
            $packSummary.packName, `
            $packSummary.restoredCount, `
            $packSummary.removedCount, `
            $packSummary.backupManifestPath)
    }
    exit 0
}

# Validate $Packs before anything else (including -DryRun)
$Packs = Resolve-ValidatedPackNames -RawPacks $Packs -RepositoryRoot $repoRoot

$buildStamp = [DateTime]::UtcNow.ToString("yyyyMMddTHHmmssZ")
$explicitArtifactRoot = -not [string]::IsNullOrWhiteSpace($ArtifactRoot)
if ($explicitArtifactRoot) {
    $resolvedArtifactRoot = if ([System.IO.Path]::IsPathRooted($ArtifactRoot)) {
        [System.IO.Path]::GetFullPath($ArtifactRoot)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ArtifactRoot))
    }
} else {
    $resolvedArtifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts/canonical-level2-$buildStamp"))
}

$cachePath = Join-Path $repoRoot "artifacts/canonical-level2-cache.json"
$logDirectory = Join-Path $resolvedArtifactRoot "logs"
$geometryLogPath = Join-Path $logDirectory "blender-builder.log"
$levelOneLogPath = Join-Path $logDirectory "level-one-unity-builder.log"
$logPath = Join-Path $logDirectory "unity-builder.log"
$unityAuditOutputPath = Join-Path $logDirectory "unity-audit.json"
$animationMatrixOutputPath = Join-Path $logDirectory "animation-matrix-report.json"
$lockPath = Join-Path $projectPath "Temp/UnityLockfile"
$moduleManifestPath = Join-Path $repoRoot "content/source-art/canonical-visual-modules.json"
$moduleBlendPath = Join-Path $repoRoot "content/source-art/canonical-visual-modules.blend"
$textureDirectory = Join-Path $projectPath "Assets/RogueCommandExpansionSample/VisualModules/Textures"
$levelOneTextureDirectory = Join-Path $projectPath "Assets/RogueCommandExpansionSample/Models/Textures"
$builtPackRoot = Join-Path $resolvedArtifactRoot "content-packs"
$validatedPackRoot = Join-Path $resolvedArtifactRoot "validation-packs"
$backupRoot = Join-Path $resolvedArtifactRoot "prior-bundles-backup"

function Get-ExpectedLevelOneModelOutputs {
    param(
        [string[]]$TargetPacks
    )

    $outputs = [System.Collections.Generic.List[string]]::new()
    if ($TargetPacks -contains "sample-native-expansion") {
        foreach ($name in @(
            "sandlance-scout-v2.blend",
            "colossus-siege-v2.blend",
            "skirmish-strike-bike-v2.blend",
            "surge-arc-tank-v2.blend",
            "overlord-command-hq-v2.blend",
            "ironclad-walker-v2.blend",
            "atlas-crane-mech-v2.blend",
            "bastion-siege-mech-v2.blend",
            "arachnid-hexapod-v2.blend",
            "arachnidhexapodfactory-v2.blend"
        )) {
            [void]$outputs.Add((Join-Path $repoRoot "content/source-art/$name"))
        }
    }
    if ($TargetPacks -contains "tactical-operations") {
        foreach ($name in @(
            "arclancewalker-v2.blend",
            "tidebreaker-hovercraft-v2.blend",
            "fieldmedicrover-v2.blend",
            "skyguard-sam-v2.blend",
            "salvage-hauler-v2.blend",
            "sentinel-relay-v2.blend"
        )) {
            [void]$outputs.Add((Join-Path $repoRoot "content/source-art/$name"))
        }
    }
    return @($outputs)
}

function Get-ExpectedLevelOneTextureOutputs {
    param(
        [string[]]$Assets
    )

    $outputs = [System.Collections.Generic.List[string]]::new()
    foreach ($asset in @($Assets)) {
        if ([string]::IsNullOrWhiteSpace($asset)) {
            continue
        }
        foreach ($suffix in @("Albedo", "MetallicGloss", "Normal", "Emission")) {
            [void]$outputs.Add((Join-Path $levelOneTextureDirectory "${asset}_${suffix}.png"))
        }
    }
    return @($outputs)
}

function Get-ExpectedBlenderOutputs {
    $outputs = [System.Collections.Generic.List[string]]::new()
    [void]$outputs.Add($moduleManifestPath)
    [void]$outputs.Add($moduleBlendPath)
    if (Test-Path -LiteralPath $moduleManifestPath -PathType Leaf) {
        try {
            $manifest = Get-Content -LiteralPath $moduleManifestPath -Raw | ConvertFrom-Json
            foreach ($module in @($manifest.modules)) {
                if (-not [string]::IsNullOrWhiteSpace([string]$module.fbxAssetPath)) {
                    [void]$outputs.Add((Join-Path $projectPath ([string]$module.fbxAssetPath)))
                }
            }
        } catch {
            # Unreadable manifest will fail cache hit if outputs are incomplete
        }
    }
    return @($outputs)
}

function Get-ExpectedTextureOutputs {
    $outputs = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-Path -LiteralPath $moduleManifestPath -PathType Leaf)) {
        return @()
    }
    try {
        $manifest = Get-Content -LiteralPath $moduleManifestPath -Raw | ConvertFrom-Json
        $presets = @($manifest.modules | ForEach-Object { [string]$_.preset } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
        foreach ($preset in $presets) {
            foreach ($suffix in @("Albedo", "MetallicGloss", "Normal", "Emission")) {
                [void]$outputs.Add((Join-Path $textureDirectory "RCLModule_${preset}_${suffix}.png"))
            }
        }
    } catch {
        return @()
    }
    return @($outputs)
}

function Get-PhaseCacheEntry {
    param(
        [string]$CacheFilePath,
        [string]$Phase
    )

    if (-not (Test-Path -LiteralPath $CacheFilePath -PathType Leaf)) {
        return $null
    }
    try {
        $raw = Get-Content -LiteralPath $CacheFilePath -Raw
        if ([string]::IsNullOrWhiteSpace($raw)) {
            return $null
        }
        $cache = $raw | ConvertFrom-Json
        if ($null -eq $cache -or $null -eq $cache.phases) {
            return $null
        }
        $prop = $cache.phases.PSObject.Properties[$Phase]
        if ($null -eq $prop) {
            return $null
        }
        return $prop.Value
    } catch {
        return $null
    }
}

function Test-PhaseCacheOptionalMetadata {
    param(
        [string]$CacheFilePath,
        [string]$Phase,
        [int]$ExpectedSizePower = -1,
        [string[]]$RequiredPacks = @()
    )

    $entry = Get-PhaseCacheEntry -CacheFilePath $CacheFilePath -Phase $Phase
    if ($null -eq $entry -or $null -eq $entry.metadata) {
        return $true
    }
    $meta = $entry.metadata
    if ($ExpectedSizePower -gt 0 -and $null -ne $meta.PSObject.Properties["sizePower"] -and $null -ne $meta.sizePower) {
        if ([int]$meta.sizePower -ne $ExpectedSizePower) {
            return $false
        }
    }
    if ($RequiredPacks.Count -gt 0 -and $null -ne $meta.PSObject.Properties["packs"] -and $null -ne $meta.packs) {
        $cachedPacks = @($meta.packs | ForEach-Object { [string]$_ })
        foreach ($packName in $RequiredPacks) {
            if ($cachedPacks -notcontains $packName) {
                return $false
            }
        }
    }
    return $true
}

function Get-PackDeclaredBundleRelativePaths {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackName
    )

    $sourceContentPath = Join-Path $repoRoot "content/packs/$PackName/content.json"
    $definition = Get-Content -LiteralPath $sourceContentPath -Raw | ConvertFrom-Json
    $rawPaths = @([string]$definition.assetBundleManifestPath)
    $rawPaths += @($definition.assetBundles | ForEach-Object { [string]$_.path })

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $validatedPaths = [System.Collections.Generic.List[string]]::new()
    foreach ($relativePath in $rawPaths) {
        if ([string]::IsNullOrWhiteSpace($relativePath) -or
            $relativePath -notmatch '^bundles/[a-z0-9][a-z0-9._-]*\.bundle$') {
            throw "Pack '$PackName' declares an invalid bundle path: $relativePath"
        }
        if ($seen.Add($relativePath)) {
            [void]$validatedPaths.Add($relativePath)
        }
    }
    return @($validatedPaths)
}

function Find-UnityCachedArtifactRoot {
    param(
        [string]$CacheFilePath,
        [string]$InputHash,
        [string[]]$TargetPacks
    )

    if (-not (Test-CanonicalPhaseCacheHit -CachePath $CacheFilePath -Phase "unity" -InputHash $InputHash)) {
        return $null
    }
    $entry = Get-PhaseCacheEntry -CacheFilePath $CacheFilePath -Phase "unity"
    if ($null -eq $entry -or $null -eq $entry.metadata) {
        return $null
    }
    $rawArtifactRoot = [string]$entry.metadata.artifactRoot
    if ([string]::IsNullOrWhiteSpace($rawArtifactRoot)) {
        return $null
    }
    $cachedRoot = [System.IO.Path]::GetFullPath($rawArtifactRoot)
    if (-not (Test-Path -LiteralPath $cachedRoot -PathType Container)) {
        return $null
    }
    $cachedRootItem = Get-Item -LiteralPath $cachedRoot -Force
    if (($cachedRootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        return $null
    }

    $cachedAuditPath = Join-Path (Join-Path $cachedRoot "logs") "unity-audit.json"
    if (-not (Test-Path -LiteralPath $cachedAuditPath -PathType Leaf)) {
        return $null
    }
    $cachedAuditItem = Get-Item -LiteralPath $cachedAuditPath -Force
    if (($cachedAuditItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or $cachedAuditItem.Length -le 0) {
        return $null
    }
    try {
        $auditJson = Get-Content -LiteralPath $cachedAuditPath -Raw | ConvertFrom-Json
        if ($null -eq $auditJson -or $null -eq $auditJson.build -or $null -eq $auditJson.build.quality_gate -or $auditJson.build.quality_gate.passed -ne $true) {
            return $null
        }
    } catch {
        return $null
    }

    $cachedPackRoot = Join-Path $cachedRoot "content-packs"
    foreach ($packName in $TargetPacks) {
        $packDir = Join-Path $cachedPackRoot $packName
        if (-not (Test-Path -LiteralPath $packDir -PathType Container)) {
            return $null
        }
        $packDirItem = Get-Item -LiteralPath $packDir -Force
        if (($packDirItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            return $null
        }
        $packPrefix = [System.IO.Path]::GetFullPath($packDir).TrimEnd('\') + '\'
        try {
            $relPaths = Get-PackDeclaredBundleRelativePaths -PackName $packName
        } catch {
            return $null
        }
        if ($relPaths.Count -eq 0) {
            return $null
        }
        foreach ($relPath in $relPaths) {
            $bundleFile = [System.IO.Path]::GetFullPath((Join-Path $packDir ($relPath.Replace('/', '\'))))
            if (-not $bundleFile.StartsWith($packPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
                -not (Test-Path -LiteralPath $bundleFile -PathType Leaf)) {
                return $null
            }
            $bundleItem = Get-Item -LiteralPath $bundleFile -Force
            if (($bundleItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or $bundleItem.Length -le 0) {
                return $null
            }
        }
    }

    return $cachedRoot
}

function Resolve-LevelOneTextureAssets {
    param(
        [string[]]$TargetPacks
    )

    $pyCode = @"
import json, sys
from tools.UnitPbrPresetRegistry import UNIT_PBR_REGISTRY
from tools.build_canonical_level1_textures import FACTORY_PRESET_MAP
packs = set(sys.argv[1:])
assets = []
for profile in UNIT_PBR_REGISTRY.values():
    if profile.pack_id in packs and profile.model_name not in assets:
        assets.append(profile.model_name)
for factory_name in FACTORY_PRESET_MAP.keys():
    if factory_name not in assets:
        assets.append(factory_name)
print(json.dumps(assets))
"@
    $raw = & $pythonExe -c $pyCode @TargetPacks
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to resolve Level 1 texture asset list from UnitPbrPresetRegistry."
    }
    return @($raw | ConvertFrom-Json)
}

function Assert-PinnedBlender {
    $blenderVersion = @(& $blenderExe --version)
    if ($LASTEXITCODE -ne 0 -or $blenderVersion.Count -eq 0 -or $blenderVersion[0].Trim() -notmatch '^Blender 4\.5\.14(?:\s|$)') {
        throw "Canonical geometry requires Blender 4.5.14; reported version: $($blenderVersion | Select-Object -First 1)"
    }
}

function Invoke-BlenderScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ScriptPath,
        [Parameter(Mandatory = $true)]
        [string]$LogFilePath,
        [string[]]$ScriptArgs = @(),
        [switch]$FactoryStartup
    )

    $blenderArgs = @("--background")
    if ($FactoryStartup) {
        $blenderArgs += "--factory-startup"
    }
    $blenderArgs += @("--python-exit-code", "1", "--python", $ScriptPath)
    if ($ScriptArgs.Count -gt 0) {
        $blenderArgs += "--"
        $blenderArgs += $ScriptArgs
    }

    $previousEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $blenderExe @blenderArgs 2>&1 |
            ForEach-Object {
                if ($_ -is [System.Management.Automation.ErrorRecord]) {
                    $_.ToString()
                } else {
                    $_
                }
            } |
            Tee-Object -FilePath $LogFilePath
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousEap
    }
    return $exitCode
}

function Invoke-UnityBatchmode {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecuteMethod,
        [Parameter(Mandatory = $true)]
        [string]$UnityLogFile,
        [Parameter(Mandatory = $true)]
        [string]$FailureContext
    )

    $unityArgs = @(
        "-batchmode",
        "-quit",
        "-projectPath",
        $projectPath,
        "-executeMethod",
        $ExecuteMethod,
        "-logFile",
        $UnityLogFile
    )
    $argText = ($unityArgs | ForEach-Object { '"' + ([string]$_).Replace('"', '\"') + '"' }) -join ' '
    $editor = Start-Process -FilePath $unityExe -ArgumentList $argText -WindowStyle Hidden -PassThru
    if ($null -eq $editor) {
        throw "Could not start Unity ($FailureContext): $unityExe"
    }
    while (-not $editor.WaitForExit(15000)) {
        $editor.Refresh()
        if (Test-Path -LiteralPath $UnityLogFile) {
            $lastLine = Get-Content -LiteralPath $UnityLogFile -Tail 1
            if (-not [string]::IsNullOrWhiteSpace($lastLine)) {
                Write-Host $lastLine
            }
        }
    }
    $editor.Refresh()
    if ($editor.ExitCode -ne 0) {
        throw "Unity $FailureContext failed with exit code $($editor.ExitCode). Log: $UnityLogFile"
    }
    if (Test-Path -LiteralPath $lockPath) {
        throw "Unity returned ($FailureContext) but left a project lock file; inspect $lockPath before reopening the authoring project."
    }
}

# Plan the 10 phases in exact execution order
$plannedPhases = [System.Collections.Generic.List[object]]::new()

# 1. level1-models
$level1ModelsInputHash = $null
if (-not $RebuildLevelOneModels) {
    [void]$plannedPhases.Add([ordered]@{
        name = "level1-models"
        action = "skipped"
        reason = "-RebuildLevelOneModels not specified"
    })
} elseif ($Incremental) {
    $level1ModelsInputHash = Get-CanonicalPhaseInputHash -RepositoryRoot $repoRoot -Phase "level1-models"
    $expectedLevel1ModelOutputs = Get-ExpectedLevelOneModelOutputs -TargetPacks $Packs
    $requiredModelPacks = @($Packs | Where-Object { $_ -in @("sample-native-expansion", "tactical-operations") })
    if ($expectedLevel1ModelOutputs.Count -gt 0 -and
        (Test-CanonicalPhaseCacheHit -CachePath $cachePath -Phase "level1-models" -InputHash $level1ModelsInputHash -ExpectedOutputs $expectedLevel1ModelOutputs) -and
        (Test-PhaseCacheOptionalMetadata -CacheFilePath $cachePath -Phase "level1-models" -RequiredPacks $requiredModelPacks)) {
        [void]$plannedPhases.Add([ordered]@{
            name = "level1-models"
            action = "cached"
            reason = "Incremental cache hit ($level1ModelsInputHash)"
        })
    } else {
        [void]$plannedPhases.Add([ordered]@{
            name = "level1-models"
            action = "executed"
            reason = "Incremental cache miss ($level1ModelsInputHash)"
        })
    }
} else {
    [void]$plannedPhases.Add([ordered]@{
        name = "level1-models"
        action = "executed"
        reason = "-RebuildLevelOneModels specified"
    })
}

# 2. level1-textures
$level1TexturesInputHash = $null
$resolvedLevel1Assets = @()
if (-not $RebuildLevelOneTextures) {
    [void]$plannedPhases.Add([ordered]@{
        name = "level1-textures"
        action = "skipped"
        reason = "-RebuildLevelOneTextures not specified"
    })
} elseif ($Incremental) {
    $level1TexturesInputHash = Get-CanonicalPhaseInputHash -RepositoryRoot $repoRoot -Phase "level1-textures"
    if (Test-Path -LiteralPath $pythonExe -PathType Leaf) {
        $resolvedLevel1Assets = @(Resolve-LevelOneTextureAssets -TargetPacks $Packs)
    }
    $expectedLevel1TextureOutputs = Get-ExpectedLevelOneTextureOutputs -Assets $resolvedLevel1Assets
    if ($expectedLevel1TextureOutputs.Count -gt 0 -and
        (Test-CanonicalPhaseCacheHit -CachePath $cachePath -Phase "level1-textures" -InputHash $level1TexturesInputHash -ExpectedOutputs $expectedLevel1TextureOutputs) -and
        (Test-PhaseCacheOptionalMetadata -CacheFilePath $cachePath -Phase "level1-textures" -ExpectedSizePower $TextureSizePower -RequiredPacks $Packs)) {
        [void]$plannedPhases.Add([ordered]@{
            name = "level1-textures"
            action = "cached"
            reason = "Incremental cache hit ($level1TexturesInputHash)"
        })
    } else {
        [void]$plannedPhases.Add([ordered]@{
            name = "level1-textures"
            action = "executed"
            reason = "Incremental cache miss ($level1TexturesInputHash)"
        })
    }
} else {
    [void]$plannedPhases.Add([ordered]@{
        name = "level1-textures"
        action = "executed"
        reason = "-RebuildLevelOneTextures specified"
    })
}

# 3. blender
$blenderInputHash = $null
if ($SkipBlender) {
    [void]$plannedPhases.Add([ordered]@{
        name = "blender"
        action = "skipped"
        reason = "-SkipBlender specified"
    })
} elseif ($Incremental) {
    $blenderInputHash = Get-CanonicalPhaseInputHash -RepositoryRoot $repoRoot -Phase "blender"
    $expectedBlenderOutputs = Get-ExpectedBlenderOutputs
    if ($expectedBlenderOutputs.Count -gt 0 -and (Test-CanonicalPhaseCacheHit -CachePath $cachePath -Phase "blender" -InputHash $blenderInputHash -ExpectedOutputs $expectedBlenderOutputs)) {
        [void]$plannedPhases.Add([ordered]@{
            name = "blender"
            action = "cached"
            reason = "Incremental cache hit ($blenderInputHash)"
        })
    } else {
        [void]$plannedPhases.Add([ordered]@{
            name = "blender"
            action = "executed"
            reason = "Incremental cache miss ($blenderInputHash)"
        })
    }
} else {
    [void]$plannedPhases.Add([ordered]@{
        name = "blender"
        action = "executed"
        reason = "Full build"
    })
}

$level1TexturesPlannedAction = ($plannedPhases | Where-Object { $_.name -eq "level1-textures" }).action
$blenderPlannedAction = ($plannedPhases | Where-Object { $_.name -eq "blender" }).action

# 4. textures
$texturesInputHash = $null
if ($SkipTextures) {
    [void]$plannedPhases.Add([ordered]@{
        name = "textures"
        action = "skipped"
        reason = "-SkipTextures specified"
    })
} elseif ($Incremental) {
    if ($blenderPlannedAction -eq "executed") {
        [void]$plannedPhases.Add([ordered]@{
            name = "textures"
            action = "executed"
            reason = "Upstream blender phase scheduled to rebuild module manifest"
        })
    } else {
        $texturesInputHash = Get-CanonicalPhaseInputHash -RepositoryRoot $repoRoot -Phase "textures"
        $expectedTextureOutputs = Get-ExpectedTextureOutputs
        if ($expectedTextureOutputs.Count -gt 0 -and
            (Test-CanonicalPhaseCacheHit -CachePath $cachePath -Phase "textures" -InputHash $texturesInputHash -ExpectedOutputs $expectedTextureOutputs) -and
            (Test-PhaseCacheOptionalMetadata -CacheFilePath $cachePath -Phase "textures" -ExpectedSizePower $TextureSizePower)) {
            [void]$plannedPhases.Add([ordered]@{
                name = "textures"
                action = "cached"
                reason = "Incremental cache hit ($texturesInputHash)"
            })
        } else {
            [void]$plannedPhases.Add([ordered]@{
                name = "textures"
                action = "executed"
                reason = "Incremental cache miss ($texturesInputHash)"
            })
        }
    }
} else {
    [void]$plannedPhases.Add([ordered]@{
        name = "textures"
        action = "executed"
        reason = "Full build"
    })
}

$texturesPlannedAction = ($plannedPhases | Where-Object { $_.name -eq "textures" }).action

# 5. unity
$unityMethod = ""
$unityInputHash = $null
$cachedUnityArtifactRoot = $null
$upstreamUnityInputsWillRebuild = (
    ($level1TexturesPlannedAction -eq "executed") -or
    ($blenderPlannedAction -eq "executed") -or
    ($texturesPlannedAction -eq "executed")
)

if ((-not $SkipLevelOneRefresh) -and (-not $SkipUnityBuild)) {
    $unityMethod = "RogueCommandExpansion.Authoring.CanonicalVisualModuleBuilder.RefreshAndBuildCanonicalModules"
} elseif ($SkipLevelOneRefresh -and (-not $SkipUnityBuild)) {
    $unityMethod = "RogueCommandExpansion.Authoring.CanonicalVisualModuleBuilder.BuildCanonicalModules"
} elseif ((-not $SkipLevelOneRefresh) -and $SkipUnityBuild) {
    $unityMethod = "RogueCommandExpansion.Authoring.RogueCommandVisualSampleScaffolder.RefreshCanonicalLevelOneVisuals"
}

if ($SkipLevelOneRefresh -and $SkipUnityBuild) {
    [void]$plannedPhases.Add([ordered]@{
        name = "unity"
        action = "skipped"
        reason = "Both -SkipLevelOneRefresh and -SkipUnityBuild specified"
    })
} elseif ($SkipUnityBuild) {
    [void]$plannedPhases.Add([ordered]@{
        name = "unity"
        action = "executed"
        reason = "RefreshCanonicalLevelOneVisuals (-SkipUnityBuild)"
    })
} elseif ($Incremental) {
    if ($upstreamUnityInputsWillRebuild) {
        [void]$plannedPhases.Add([ordered]@{
            name = "unity"
            action = "executed"
            reason = "Upstream phase scheduled to rebuild Unity inputs"
        })
    } else {
        $unityInputHash = Get-CanonicalPhaseInputHash -RepositoryRoot $repoRoot -Phase "unity"
        $cachedUnityArtifactRoot = Find-UnityCachedArtifactRoot -CacheFilePath $cachePath -InputHash $unityInputHash -TargetPacks $Packs
        if ($null -ne $cachedUnityArtifactRoot) {
            [void]$plannedPhases.Add([ordered]@{
                name = "unity"
                action = "cached"
                reason = "Incremental cache hit ($unityInputHash, artifactRoot=$cachedUnityArtifactRoot)"
            })
        } else {
            [void]$plannedPhases.Add([ordered]@{
                name = "unity"
                action = "executed"
                reason = "Incremental cache miss ($unityInputHash)"
            })
        }
    }
} else {
    $unityReason = if ($SkipLevelOneRefresh) {
        "BuildCanonicalModules (-SkipLevelOneRefresh)"
    } else {
        "Single batchmode RefreshAndBuildCanonicalModules"
    }
    [void]$plannedPhases.Add([ordered]@{
        name = "unity"
        action = "executed"
        reason = $unityReason
    })
}

$unityPlannedAction = ($plannedPhases | Where-Object { $_.name -eq "unity" }).action

# 6. unity-audit
if ($unityPlannedAction -eq "cached") {
    [void]$plannedPhases.Add([ordered]@{
        name = "unity-audit"
        action = "cached"
        reason = "Verify and load cached unity-audit.json ($cachedUnityArtifactRoot)"
    })
} elseif ($unityPlannedAction -eq "executed") {
    [void]$plannedPhases.Add([ordered]@{
        name = "unity-audit"
        action = "executed"
        reason = "Unity ran ($unityMethod)"
    })
} else {
    [void]$plannedPhases.Add([ordered]@{
        name = "unity-audit"
        action = "skipped"
        reason = "Unity phase skipped"
    })
}

# 7. staging-and-pack-validation
if ($SkipUnityBuild) {
    [void]$plannedPhases.Add([ordered]@{
        name = "staging-and-pack-validation"
        action = "skipped"
        reason = "-SkipUnityBuild specified"
    })
} else {
    [void]$plannedPhases.Add([ordered]@{
        name = "staging-and-pack-validation"
        action = "executed"
        reason = "Stage and validate packs: $($Packs -join ', ')"
    })
}

# 8. animation-matrix (MUST run before bundle-promotion)
if ($SkipAnimationMatrix) {
    [void]$plannedPhases.Add([ordered]@{
        name = "animation-matrix"
        action = "skipped"
        reason = "-SkipAnimationMatrix specified"
    })
} else {
    [void]$plannedPhases.Add([ordered]@{
        name = "animation-matrix"
        action = "executed"
        reason = "Run strict-loops animation validation matrix"
    })
}

# 9. bundle-promotion
if ($NoPromoteBundles) {
    [void]$plannedPhases.Add([ordered]@{
        name = "bundle-promotion"
        action = "skipped"
        reason = "-NoPromoteBundles specified"
    })
} elseif ($SkipUnityBuild) {
    [void]$plannedPhases.Add([ordered]@{
        name = "bundle-promotion"
        action = "skipped"
        reason = "-SkipUnityBuild specified"
    })
} else {
    [void]$plannedPhases.Add([ordered]@{
        name = "bundle-promotion"
        action = "executed"
        reason = "Promote validated bundles into content/packs"
    })
}

# 10. package-loader
if (-not $PackageLoader) {
    [void]$plannedPhases.Add([ordered]@{
        name = "package-loader"
        action = "skipped"
        reason = "-PackageLoader not specified"
    })
} elseif ($SkipUnityBuild) {
    [void]$plannedPhases.Add([ordered]@{
        name = "package-loader"
        action = "skipped"
        reason = "-SkipUnityBuild specified"
    })
} else {
    [void]$plannedPhases.Add([ordered]@{
        name = "package-loader"
        action = "executed"
        reason = "Package loader archive via Package-Loader.ps1"
    })
}

if ($DryRun) {
    Write-Host "=== Canonical Visual Modules Build (DryRun) ==="
    Write-Host "artifactRoot: $resolvedArtifactRoot"
    Write-Host "packs: $($Packs -join ', ')"
    Write-Host "cachePath: $cachePath"
    Write-Host "gameRoot: $GameRoot"
    Write-Host "textureSizePower: $TextureSizePower"
    Write-Host "maxParallelExports: $MaxParallelExports"
    Write-Host "packWorkers: $PackWorkers"
    Write-Host "phases:"
    foreach ($p in $plannedPhases) {
        Write-Host ("  - {0}: {1} ({2})" -f $p.name, $p.action, $p.reason)
    }
    exit 0
}

# Real execution pre-flight checks
if (-not $explicitArtifactRoot -and (Test-Path -LiteralPath $resolvedArtifactRoot)) {
    throw "Canonical Level 2 build output already exists; refusing to overwrite it: $resolvedArtifactRoot"
}
$env:RCL_CANONICAL_VISUAL_ARTIFACT_ROOT = $resolvedArtifactRoot

$planByName = @{}
foreach ($p in $plannedPhases) {
    $planByName[$p.name] = $p
}

if ($planByName["unity"].action -eq "executed") {
    if (-not (Test-Path -LiteralPath $unityExe -PathType Leaf)) {
        throw "Pinned Unity 2021.3.45f2 editor was not found: $unityExe"
    }
    if (-not (Test-Path -LiteralPath $projectPath -PathType Container)) {
        throw "RogueCommandAssetAuthoring project is missing: $projectPath"
    }
    if (Test-Path -LiteralPath $lockPath) {
        throw "The authoring project has a Unity lock file. Close its current editor before the batch build: $lockPath"
    }
    $projectVersion = Get-Content -LiteralPath (Join-Path $projectPath "ProjectSettings/ProjectVersion.txt") -Raw
    if ($projectVersion -notmatch "(?m)^m_EditorVersion: 2021\.3\.45f2\s*$") {
        throw "Authoring project is not pinned to Unity 2021.3.45f2."
    }
}

if (($planByName["level1-models"].action -eq "executed") -or
    ($planByName["blender"].action -eq "executed")) {
    if (-not (Test-Path -LiteralPath $blenderExe -PathType Leaf)) {
        throw "Pinned Blender 4.5.14 executable was not found: $blenderExe"
    }
    if (Get-Process -Name blender -ErrorAction SilentlyContinue) {
        throw "Close Blender before regenerating canonical source/module geometry."
    }
}

if (($planByName["level1-textures"].action -eq "executed") -or
    ($planByName["textures"].action -eq "executed") -or
    ($planByName["unity-audit"].action -eq "executed") -or
    ($planByName["staging-and-pack-validation"].action -eq "executed") -or
    ($planByName["animation-matrix"].action -eq "executed")) {
    if (-not (Test-Path -LiteralPath $pythonExe -PathType Leaf)) {
        throw "The repository Python environment is required: $pythonExe"
    }
}

if ($planByName["package-loader"].action -eq "executed") {
    if (-not (Test-Path -LiteralPath $packageLoaderScript -PathType Leaf)) {
        throw "Package-Loader.ps1 was not found: $packageLoaderScript"
    }
}

New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

$phaseResults = [System.Collections.Generic.List[object]]::new()
$audits = [ordered]@{
    level1Textures = @()
    textures = @()
    unity = $null
    nativeVisualRoots = @()
    animationMatrix = $null
}
$promotionAttempted = $false
$promotionResult = $null
$packageLoaderResult = $null
$rollbackResult = $null
$currentPhaseName = $null
$overallSuccess = $false
$moduleCount = $null
$presets = @()
$builtBundlePaths = [System.Collections.Generic.List[string]]::new()
$unityExecutedLogPath = $null

function Invoke-TrackedPhase {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$PlannedAction,
        [Parameter(Mandatory = $true)]
        [string]$PlannedReason,
        [Parameter(Mandatory = $true)]
        [scriptblock]$ActionBlock,
        [switch]$RunWhenCached
    )

    $shouldRun = ($PlannedAction -eq "executed") -or ($RunWhenCached -and $PlannedAction -eq "cached")
    if (-not $shouldRun) {
        [void]$phaseResults.Add([ordered]@{
            name = $Name
            status = $PlannedAction
            reason = $PlannedReason
            elapsedSeconds = 0.0
        })
        return
    }

    $script:currentPhaseName = $Name
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $details = & $ActionBlock
        $sw.Stop()
        [void]$phaseResults.Add([ordered]@{
            name = $Name
            status = $PlannedAction
            reason = $PlannedReason
            elapsedSeconds = [Math]::Round($sw.Elapsed.TotalSeconds, 3)
            details = $details
        })
    } catch {
        $sw.Stop()
        [void]$phaseResults.Add([ordered]@{
            name = $Name
            status = "failed"
            reason = $PlannedReason
            elapsedSeconds = [Math]::Round($sw.Elapsed.TotalSeconds, 3)
            error = $_.Exception.Message
        })
        throw
    }
}

try {
    # Phase 1: level1-models
    Invoke-TrackedPhase -Name "level1-models" -PlannedAction $planByName["level1-models"].action -PlannedReason $planByName["level1-models"].reason -ActionBlock {
        Assert-PinnedBlender
        $ranScripts = [System.Collections.Generic.List[string]]::new()
        $logs = [System.Collections.Generic.List[string]]::new()

        if ($Packs -contains "sample-native-expansion") {
            $vehiclesScript = Join-Path $repoRoot "tools/Build-SampleNativeVehiclesV2SourceBlends.py"
            $vehiclesLog = Join-Path $logDirectory "level1-vehicles-v2-blender.log"
            Write-Host "Rebuilding Level 1 v2 vehicle source blends with Blender 4.5.14."
            $rc = Invoke-BlenderScript -ScriptPath $vehiclesScript -LogFilePath $vehiclesLog -FactoryStartup
            if ($rc -ne 0) {
                throw "Level 1 v2 vehicle source blend build failed with exit code $rc. Log: $vehiclesLog"
            }
            [void]$ranScripts.Add("tools/Build-SampleNativeVehiclesV2SourceBlends.py")
            [void]$logs.Add($vehiclesLog)

            $walkersScript = Join-Path $repoRoot "tools/Build-SampleNativeWalkersV2SourceBlends.py"
            $walkersLog = Join-Path $logDirectory "level1-walkers-v2-blender.log"
            Write-Host "Rebuilding Level 1 v2 walker source blends with Blender 4.5.14."
            $rc = Invoke-BlenderScript -ScriptPath $walkersScript -LogFilePath $walkersLog -FactoryStartup
            if ($rc -ne 0) {
                throw "Level 1 v2 walker source blend build failed with exit code $rc. Log: $walkersLog"
            }
            [void]$ranScripts.Add("tools/Build-SampleNativeWalkersV2SourceBlends.py")
            [void]$logs.Add($walkersLog)
        }

        if ($Packs -contains "tactical-operations") {
            $tacOpsScript = Join-Path $repoRoot "tools/Build-TacticalOperationsV2SourceBlends.py"
            $tacOpsLog = Join-Path $logDirectory "level1-tactical-operations-v2-blender.log"
            Write-Host "Rebuilding Level 1 v2 tactical-operations source blends with Blender 4.5.14."
            $rc = Invoke-BlenderScript -ScriptPath $tacOpsScript -LogFilePath $tacOpsLog -FactoryStartup
            if ($rc -ne 0) {
                throw "Level 1 v2 tactical-operations source blend build failed with exit code $rc. Log: $tacOpsLog"
            }
            [void]$ranScripts.Add("tools/Build-TacticalOperationsV2SourceBlends.py")
            [void]$logs.Add($tacOpsLog)
        }

        if ($Packs -contains "sample-native-expansion") {
            $surfaceFitTest = Join-Path $repoRoot "tests/python/authoring/test_sample_native_v2_surface_fit.py"
            $surfaceFitLog = Join-Path $logDirectory "level1-sample-native-v2-surface-fit.log"
            Write-Host "Running sample-native v2 surface-fit verification in Blender 4.5.14."
            $rc = Invoke-BlenderScript -ScriptPath $surfaceFitTest -LogFilePath $surfaceFitLog -FactoryStartup
            if ($rc -ne 0) {
                throw "Level 1 v2 surface-fit verification failed with exit code $rc. Log: $surfaceFitLog"
            }
            [void]$ranScripts.Add("tests/python/authoring/test_sample_native_v2_surface_fit.py")
            [void]$logs.Add($surfaceFitLog)
        }

        $expectedModelOutputs = Get-ExpectedLevelOneModelOutputs -TargetPacks $Packs
        foreach ($blendPath in $expectedModelOutputs) {
            if (-not (Test-Path -LiteralPath $blendPath -PathType Leaf) -or (Get-Item -LiteralPath $blendPath).Length -le 0) {
                throw "Expected Level 1 v2 source blend output is missing or empty: $blendPath"
            }
        }

        $hashToSave = Get-CanonicalPhaseInputHash -RepositoryRoot $repoRoot -Phase "level1-models"
        $requiredModelPacks = @($Packs | Where-Object { $_ -in @("sample-native-expansion", "tactical-operations") })
        Save-CanonicalPhaseCache -CachePath $cachePath -Phase "level1-models" -InputHash $hashToSave -Metadata @{
            packs = @($requiredModelPacks)
            outputCount = $expectedModelOutputs.Count
            scripts = @($ranScripts)
        } | Out-Null

        return [ordered]@{
            packs = @($Packs)
            scripts = @($ranScripts)
            outputCount = $expectedModelOutputs.Count
            logs = @($logs)
        }
    }

    # Phase 2: level1-textures
    Invoke-TrackedPhase -Name "level1-textures" -PlannedAction $planByName["level1-textures"].action -PlannedReason $planByName["level1-textures"].reason -ActionBlock {
        $level1Assets = if ($resolvedLevel1Assets.Count -gt 0) {
            $resolvedLevel1Assets
        } else {
            @(Resolve-LevelOneTextureAssets -TargetPacks $Packs)
        }
        if ($level1Assets.Count -eq 0) {
            throw "No Level 1 texture assets resolved for packs: $($Packs -join ', ')"
        }
        $resolution = [int][Math]::Pow(2, $TextureSizePower)
        Write-Host "Rebuilding Level 1 canonical textures ($($level1Assets.Count) assets, resolution ${resolution}x${resolution})."
        & $pythonExe -m tools.build_canonical_level1_textures @level1Assets --resolution $resolution
        if ($LASTEXITCODE -ne 0) {
            throw "Level 1 texture generation failed with exit code $LASTEXITCODE."
        }

        foreach ($asset in $level1Assets) {
            foreach ($suffix in @("Albedo", "MetallicGloss", "Normal", "Emission")) {
                $texturePath = Join-Path $levelOneTextureDirectory "${asset}_${suffix}.png"
                if (-not (Test-Path -LiteralPath $texturePath -PathType Leaf) -or
                    (Get-Item -LiteralPath $texturePath).Length -le 0) {
                    throw "Level 1 texture output is missing or empty: $texturePath"
                }
            }
        }

        $level1BatchAuditOut = Join-Path $logDirectory "mm-audit-level1-batch.json"
        & $pythonExe -m tools.audit_material_maker_output --directory $levelOneTextureDirectory --prefixes @level1Assets --workflow urp --output-dir $logDirectory --output $level1BatchAuditOut
        if ($LASTEXITCODE -ne 0) {
            throw "Material Maker URP batch audit failed for Level 1 assets (exit code $LASTEXITCODE). Report: $level1BatchAuditOut"
        }
        $batchReport = Get-Content -LiteralPath $level1BatchAuditOut -Raw | ConvertFrom-Json
        if (-not $batchReport.qualityGate -or $batchReport.qualityGate.passed -ne $true -or [int]$batchReport.totalCount -ne $level1Assets.Count) {
            throw "Material Maker URP quality gate failed for Level 1 batch audit: $level1BatchAuditOut"
        }

        $assetReports = [System.Collections.Generic.List[object]]::new()
        foreach ($entry in @($batchReport.reports)) {
            $assetName = [string]$entry.prefix
            [void]$assetReports.Add([ordered]@{
                asset = $assetName
                passed = [bool]$entry.qualityGate.passed
                reportPath = (Join-Path $logDirectory "mm-audit-${assetName}.json")
            })
        }
        $audits.level1Textures = @($assetReports)

        $hashToSave = Get-CanonicalPhaseInputHash -RepositoryRoot $repoRoot -Phase "level1-textures"
        Save-CanonicalPhaseCache -CachePath $cachePath -Phase "level1-textures" -InputHash $hashToSave -Metadata @{
            assetCount = $level1Assets.Count
            resolution = $resolution
            sizePower = $TextureSizePower
            packs = @($Packs)
            batchReportPath = $level1BatchAuditOut
        } | Out-Null

        return [ordered]@{
            assetCount = $level1Assets.Count
            resolution = $resolution
            batchReportPath = $level1BatchAuditOut
        }
    }

    # Phase 3: blender
    Invoke-TrackedPhase -Name "blender" -PlannedAction $planByName["blender"].action -PlannedReason $planByName["blender"].reason -ActionBlock {
        Assert-PinnedBlender
        Write-Host "Generating canonical Level 2 geometry with Blender 4.5.14."
        $blenderExitCode = Invoke-BlenderScript -ScriptPath $geometryScript -LogFilePath $geometryLogPath -ScriptArgs @("--force")
        if ($blenderExitCode -ne 0) {
            throw "Blender canonical module generation failed with exit code $blenderExitCode. Log: $geometryLogPath"
        }
        $geometryLog = Get-Content -LiteralPath $geometryLogPath -Raw
        $geometryCompletion = [System.Text.RegularExpressions.Regex]::Match(
            $geometryLog,
            "Saved (?<count>\d+) modules, source blend, and manifest:"
        )
        if (-not $geometryCompletion.Success) {
            throw "Blender exited without the canonical module completion record. Log: $geometryLogPath"
        }

        $moduleManifest = Get-Content -LiteralPath $moduleManifestPath -Raw | ConvertFrom-Json
        $script:moduleCount = @($moduleManifest.modules).Count
        if ($script:moduleCount -le 0 -or $script:moduleCount -ne [int]$geometryCompletion.Groups["count"].Value) {
            throw "Canonical Blender manifest count does not match its completion record: $moduleManifestPath"
        }
        $expectedModuleCount = 0
        foreach ($activePackName in @("sample-native-expansion", "tactical-operations")) {
            $packDefinition = Get-Content -LiteralPath (Join-Path $repoRoot "content/packs/$activePackName/content.json") -Raw | ConvertFrom-Json
            foreach ($unit in @($packDefinition.units)) {
                $variants = @($unit.visualVariants)
                for ($variantIndex = 1; $variantIndex -lt $variants.Count; $variantIndex++) {
                    if ($null -ne $variants[$variantIndex].modules) {
                        $expectedModuleCount += @($variants[$variantIndex].modules).Count
                    }
                }
            }
        }
        if ($script:moduleCount -ne $expectedModuleCount) {
            throw "Canonical Blender manifest has $($script:moduleCount) modules, but active Level 2 declarations contain $expectedModuleCount."
        }
        $fbxRoot = Join-Path $projectPath "Assets/RogueCommandExpansionSample/VisualModules/Source"
        $fbxRootPrefix = [System.IO.Path]::GetFullPath($fbxRoot).TrimEnd('\') + '\'
        foreach ($module in $moduleManifest.modules) {
            $fbxPath = [System.IO.Path]::GetFullPath((Join-Path $projectPath ([string]$module.fbxAssetPath)))
            if (-not $fbxPath.StartsWith($fbxRootPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
                -not (Test-Path -LiteralPath $fbxPath -PathType Leaf) -or
                (Get-Item -LiteralPath $fbxPath).Length -le 0) {
                throw "Blender output is missing, empty, or outside the canonical module FBX folder: $($module.fbxAssetPath)"
            }
        }

        $hashToSave = Get-CanonicalPhaseInputHash -RepositoryRoot $repoRoot -Phase "blender"
        Save-CanonicalPhaseCache -CachePath $cachePath -Phase "blender" -InputHash $hashToSave -Metadata @{
            moduleCount = $script:moduleCount
            manifestPath = "content/source-art/canonical-visual-modules.json"
        } | Out-Null

        return [ordered]@{ moduleCount = $script:moduleCount; logPath = $geometryLogPath }
    }

    # Ensure $moduleCount and $presets are loaded even if blender/textures were cached or skipped
    if (Test-Path -LiteralPath $moduleManifestPath -PathType Leaf) {
        $loadedManifest = Get-Content -LiteralPath $moduleManifestPath -Raw | ConvertFrom-Json
        if ($null -eq $moduleCount) {
            $moduleCount = @($loadedManifest.modules).Count
        }
        $presets = @($loadedManifest.modules | ForEach-Object { [string]$_.preset } | Sort-Object -Unique)
    }

    # Phase 4: textures
    Invoke-TrackedPhase -Name "textures" -PlannedAction $planByName["textures"].action -PlannedReason $planByName["textures"].reason -ActionBlock {
        if ($presets.Count -le 0) {
            throw "Canonical module manifest has no shared PBR presets."
        }
        $detailNormalPath = Join-Path $projectPath "Assets/RogueCommandExpansionSample/Textures/Detail/RCL_SteelMicroDetail_Normal.png"
        Write-Host "Generating $($presets.Count) canonical four-map PBR sets with Material Maker 1.7 (SizePower=$TextureSizePower, MaxParallelExports=$MaxParallelExports, PackWorkers=$PackWorkers)."
        if (Test-Path -LiteralPath $detailNormalPath -PathType Leaf) {
            & $textureScript -SizePower $TextureSizePower -MaxParallelExports $MaxParallelExports -PackWorkers $PackWorkers -DetailNormalPath $detailNormalPath -DetailNormalStrength 0.25
        } else {
            & $textureScript -SizePower $TextureSizePower -MaxParallelExports $MaxParallelExports -PackWorkers $PackWorkers
        }
        if ($LASTEXITCODE -ne 0) {
            throw "Material Maker texture generation failed with exit code $LASTEXITCODE."
        }

        $modulePrefixes = [System.Collections.Generic.List[string]]::new()
        foreach ($preset in $presets) {
            $texturePrefix = "RCLModule_$preset"
            foreach ($suffix in @("Albedo", "MetallicGloss", "Normal", "Emission")) {
                $texturePath = Join-Path $textureDirectory "${texturePrefix}_${suffix}.png"
                if (-not (Test-Path -LiteralPath $texturePath -PathType Leaf) -or
                    (Get-Item -LiteralPath $texturePath).Length -le 0) {
                    throw "Material Maker output is missing or empty: $texturePath"
                }
            }
            [void]$modulePrefixes.Add($texturePrefix)
        }

        $level2BatchAuditOut = Join-Path $logDirectory "mm-audit-level2-batch.json"
        & $pythonExe -m tools.audit_material_maker_output --directory $textureDirectory --prefixes @modulePrefixes --workflow urp --output-dir $logDirectory --output $level2BatchAuditOut
        if ($LASTEXITCODE -ne 0) {
            throw "Material Maker URP batch audit failed for Level 2 presets (exit code $LASTEXITCODE). Report: $level2BatchAuditOut"
        }
        $batchReport = Get-Content -LiteralPath $level2BatchAuditOut -Raw | ConvertFrom-Json
        if (-not $batchReport.qualityGate -or $batchReport.qualityGate.passed -ne $true -or [int]$batchReport.totalCount -ne $presets.Count) {
            throw "Material Maker URP quality gate failed for Level 2 batch audit: $level2BatchAuditOut"
        }

        $textureAudits = [System.Collections.Generic.List[object]]::new()
        foreach ($preset in $presets) {
            $texturePrefix = "RCLModule_$preset"
            $entry = @($batchReport.reports | Where-Object { [string]$_.prefix -ceq $texturePrefix }) | Select-Object -First 1
            if ($null -eq $entry -or -not $entry.qualityGate -or $entry.qualityGate.passed -ne $true) {
                throw "Material Maker URP quality gate failed for preset '$preset'."
            }
            [void]$textureAudits.Add([ordered]@{
                preset = $preset
                passed = $true
                reportPath = (Join-Path $logDirectory "mm-audit-${texturePrefix}.json")
            })
        }
        $audits.textures = @($textureAudits)

        $hashToSave = Get-CanonicalPhaseInputHash -RepositoryRoot $repoRoot -Phase "textures"
        Save-CanonicalPhaseCache -CachePath $cachePath -Phase "textures" -InputHash $hashToSave -Metadata @{
            sizePower = $TextureSizePower
            presetCount = $presets.Count
            batchReportPath = $level2BatchAuditOut
        } | Out-Null

        return [ordered]@{
            presetCount = $presets.Count
            sizePower = $TextureSizePower
            batchReportPath = $level2BatchAuditOut
        }
    }

    # Phase 5: unity
    Invoke-TrackedPhase -Name "unity" -PlannedAction $planByName["unity"].action -PlannedReason $planByName["unity"].reason -RunWhenCached -ActionBlock {
        if ($planByName["unity"].action -eq "cached") {
            $cachedRootFull = [System.IO.Path]::GetFullPath($cachedUnityArtifactRoot)
            $sameRoot = [string]::Equals($cachedRootFull, $resolvedArtifactRoot, [System.StringComparison]::OrdinalIgnoreCase)
            Write-Host "Restoring cached Unity AssetBundles and logs from $cachedRootFull."

            foreach ($packName in $Packs) {
                $relPaths = Get-PackDeclaredBundleRelativePaths -PackName $packName
                $cachedPackDir = Join-Path (Join-Path $cachedRootFull "content-packs") $packName
                $builtDir = Join-Path $builtPackRoot $packName
                if (-not $sameRoot) {
                    New-Item -ItemType Directory -Path $builtDir -Force | Out-Null
                }
                foreach ($relPath in $relPaths) {
                    $relWin = $relPath.Replace('/', '\')
                    $srcBundle = [System.IO.Path]::GetFullPath((Join-Path $cachedPackDir $relWin))
                    $dstBundle = [System.IO.Path]::GetFullPath((Join-Path $builtDir $relWin))
                    if (-not $sameRoot) {
                        New-Item -ItemType Directory -Path (Split-Path -Parent $dstBundle) -Force | Out-Null
                        Copy-Item -LiteralPath $srcBundle -Destination $dstBundle -Force
                        $srcHash = (Get-FileHash -LiteralPath $srcBundle -Algorithm SHA256).Hash
                        $dstHash = (Get-FileHash -LiteralPath $dstBundle -Algorithm SHA256).Hash
                        if ($srcHash -ne $dstHash) {
                            throw "Cached Unity bundle SHA-256 verification failed for '$packName/$relPath'."
                        }
                    }
                    [void]$builtBundlePaths.Add($dstBundle)
                }
            }

            $cachedAuditFile = Join-Path (Join-Path $cachedRootFull "logs") "unity-audit.json"
            if (-not $sameRoot) {
                Copy-Item -LiteralPath $cachedAuditFile -Destination $unityAuditOutputPath -Force
                $srcAuditHash = (Get-FileHash -LiteralPath $cachedAuditFile -Algorithm SHA256).Hash
                $dstAuditHash = (Get-FileHash -LiteralPath $unityAuditOutputPath -Algorithm SHA256).Hash
                if ($srcAuditHash -ne $dstAuditHash) {
                    throw "Cached unity-audit.json SHA-256 verification failed."
                }
            }

            $cachedLogFile = Join-Path (Join-Path $cachedRootFull "logs") "unity-builder.log"
            if (Test-Path -LiteralPath $cachedLogFile -PathType Leaf) {
                if (-not $sameRoot) {
                    Copy-Item -LiteralPath $cachedLogFile -Destination $logPath -Force
                    $srcLogHash = (Get-FileHash -LiteralPath $cachedLogFile -Algorithm SHA256).Hash
                    $dstLogHash = (Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash
                    if ($srcLogHash -ne $dstLogHash) {
                        throw "Cached unity-builder.log SHA-256 verification failed."
                    }
                }
                $script:unityExecutedLogPath = $logPath
            }

            return [ordered]@{
                cachedArtifactRoot = $cachedRootFull
                bundleCount = $builtBundlePaths.Count
                logPath = $script:unityExecutedLogPath
            }
        }

        if ((-not $SkipLevelOneRefresh) -and (-not $SkipUnityBuild)) {
            Write-Host "Refreshing Level 1 prefabs and building canonical modules in a single Unity batchmode session."
            $script:unityExecutedLogPath = $logPath
            Invoke-UnityBatchmode -ExecuteMethod $unityMethod -UnityLogFile $logPath -FailureContext "RefreshAndBuildCanonicalModules"
            $logText = Get-Content -LiteralPath $logPath -Raw

            $levelOneMatch = [System.Text.RegularExpressions.Regex]::Match(
                $logText,
                "Validated (?<units>\d+) unit prefabs and (?<factories>\d+) factory prefabs with LODGroup, Material Maker VehiclePBR_VertexWear \+ steel micro-detail, and four unique canonical hardpoints per unit\."
            )
            if (-not $levelOneMatch.Success -or
                [int]$levelOneMatch.Groups["units"].Value -lt 18 -or
                [int]$levelOneMatch.Groups["factories"].Value -lt 3) {
                throw "Unity exited without validating all 18 unit prefabs and 3 factory prefabs for Level 1 canonical parity. Log: $logPath"
            }

            $unityCompletion = [System.Text.RegularExpressions.Regex]::Match(
                $logText,
                "Built (?<modules>\d+) visual-only modules, (?<presets>\d+) shared PBR sets, validated (?<hardpoints>\d+) Level 1 hardpoint bindings, labeled .* referenced pack assets, and built both content packs\."
            )
            if (-not $unityCompletion.Success -or
                [int]$unityCompletion.Groups["modules"].Value -ne $moduleCount -or
                [int]$unityCompletion.Groups["presets"].Value -ne $presets.Count -or
                [int]$unityCompletion.Groups["hardpoints"].Value -ne $moduleCount) {
                throw "Unity exited without the expected Level 1/Level 2 canonical module build record. Log: $logPath"
            }
        } elseif ($SkipLevelOneRefresh -and (-not $SkipUnityBuild)) {
            Write-Host "Building canonical modules in Unity batchmode (-SkipLevelOneRefresh)."
            $script:unityExecutedLogPath = $logPath
            Invoke-UnityBatchmode -ExecuteMethod $unityMethod -UnityLogFile $logPath -FailureContext "BuildCanonicalModules"
            $logText = Get-Content -LiteralPath $logPath -Raw
            $unityCompletion = [System.Text.RegularExpressions.Regex]::Match(
                $logText,
                "Built (?<modules>\d+) visual-only modules, (?<presets>\d+) shared PBR sets, validated (?<hardpoints>\d+) Level 1 hardpoint bindings, labeled .* referenced pack assets, and built both content packs\."
            )
            if (-not $unityCompletion.Success -or
                [int]$unityCompletion.Groups["modules"].Value -ne $moduleCount -or
                [int]$unityCompletion.Groups["presets"].Value -ne $presets.Count -or
                [int]$unityCompletion.Groups["hardpoints"].Value -ne $moduleCount) {
                throw "Unity exited without the expected canonical module build record. Log: $logPath"
            }
        } elseif ((-not $SkipLevelOneRefresh) -and $SkipUnityBuild) {
            Write-Host "Refreshing Level 1 prefabs in Unity batchmode (-SkipUnityBuild)."
            $script:unityExecutedLogPath = $levelOneLogPath
            Invoke-UnityBatchmode -ExecuteMethod $unityMethod -UnityLogFile $levelOneLogPath -FailureContext "RefreshCanonicalLevelOneVisuals"
            $levelOneLog = Get-Content -LiteralPath $levelOneLogPath -Raw
            $levelOneMatch = [System.Text.RegularExpressions.Regex]::Match(
                $levelOneLog,
                "Validated (?<units>\d+) unit prefabs and (?<factories>\d+) factory prefabs with LODGroup, Material Maker VehiclePBR_VertexWear \+ steel micro-detail, and four unique canonical hardpoints per unit\."
            )
            if (-not $levelOneMatch.Success -or
                [int]$levelOneMatch.Groups["units"].Value -lt 18 -or
                [int]$levelOneMatch.Groups["factories"].Value -lt 3) {
                throw "Unity exited without validating all 18 unit prefabs and 3 factory prefabs for Level 1 canonical parity. Log: $levelOneLogPath"
            }
        }

        if (-not $SkipUnityBuild) {
            foreach ($packName in $Packs) {
                $builtRoot = Join-Path $builtPackRoot $packName
                $relPaths = Get-PackDeclaredBundleRelativePaths -PackName $packName
                foreach ($relPath in $relPaths) {
                    $candidate = Join-Path $builtRoot ($relPath.Replace('/', '\'))
                    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                        [void]$builtBundlePaths.Add($candidate)
                    }
                }
            }
        }

        return [ordered]@{ executeMethod = $unityMethod; logPath = $script:unityExecutedLogPath }
    }

    # Phase 6: unity-audit
    Invoke-TrackedPhase -Name "unity-audit" -PlannedAction $planByName["unity-audit"].action -PlannedReason $planByName["unity-audit"].reason -RunWhenCached -ActionBlock {
        if ($planByName["unity-audit"].action -eq "cached") {
            if (-not (Test-Path -LiteralPath $unityAuditOutputPath -PathType Leaf)) {
                throw "Copied cached unity-audit.json is missing: $unityAuditOutputPath"
            }
            $unityReport = Get-Content -LiteralPath $unityAuditOutputPath -Raw | ConvertFrom-Json
            if ($null -eq $unityReport -or $null -eq $unityReport.build -or $null -eq $unityReport.build.quality_gate -or $unityReport.build.quality_gate.passed -ne $true) {
                throw "Cached unity-audit.json failed quality gate verification: $unityAuditOutputPath"
            }
            $bundleCount = if ($null -ne $unityReport.metrics -and $null -ne $unityReport.metrics.assetbundle_count) {
                [int]$unityReport.metrics.assetbundle_count
            } else {
                $builtBundlePaths.Count
            }
            $audits.unity = [ordered]@{
                passed = $true
                cached = $true
                reportPath = $unityAuditOutputPath
                bundleCount = $bundleCount
            }
            return $audits.unity
        }

        $auditArgs = @("-m", "tools.audit_unity_build", "--strict", "--log", $unityExecutedLogPath, "--output", $unityAuditOutputPath)
        foreach ($bundlePath in $builtBundlePaths) {
            $auditArgs += @("--bundle", $bundlePath)
        }
        & $pythonExe @auditArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Unity build audit failed (exit code $LASTEXITCODE). Report: $unityAuditOutputPath"
        }
        $unityReport = Get-Content -LiteralPath $unityAuditOutputPath -Raw | ConvertFrom-Json
        if (-not $unityReport.build -or -not $unityReport.build.quality_gate -or $unityReport.build.quality_gate.passed -ne $true) {
            throw "Unity build audit quality gate did not pass: $unityAuditOutputPath"
        }
        $audits.unity = [ordered]@{
            passed = [bool]$unityReport.build.quality_gate.passed
            cached = $false
            reportPath = $unityAuditOutputPath
            bundleCount = [int]$unityReport.metrics.assetbundle_count
        }

        if (-not $SkipUnityBuild) {
            $hashToSave = Get-CanonicalPhaseInputHash -RepositoryRoot $repoRoot -Phase "unity"
            Save-CanonicalPhaseCache -CachePath $cachePath -Phase "unity" -InputHash $hashToSave -Metadata @{
                artifactRoot = $resolvedArtifactRoot
                executeMethod = $unityMethod
                bundleCount = [int]$unityReport.metrics.assetbundle_count
                packs = @($Packs)
            } | Out-Null
        }

        return $audits.unity
    }

    # Phase 7: staging-and-pack-validation
    Invoke-TrackedPhase -Name "staging-and-pack-validation" -PlannedAction $planByName["staging-and-pack-validation"].action -PlannedReason $planByName["staging-and-pack-validation"].reason -ActionBlock {
        $nativeVisualAuditResults = [System.Collections.Generic.List[object]]::new()
        $gameVersionFile = Join-Path $GameRoot "version.txt"
        $hasGameInstall = Test-Path -LiteralPath $gameVersionFile -PathType Leaf

        foreach ($packName in $Packs) {
            $sourcePackRoot = Join-Path $repoRoot "content/packs/$packName"
            $sourceContentPath = Join-Path $sourcePackRoot "content.json"
            $definition = Get-Content -LiteralPath $sourceContentPath -Raw | ConvertFrom-Json
            $builtRoot = Join-Path $builtPackRoot $packName
            $stagedRoot = Join-Path $validatedPackRoot $packName
            $stagedBundles = Join-Path $stagedRoot "bundles"
            if (-not (Test-Path -LiteralPath $builtRoot -PathType Container)) {
                throw "Unity did not create the build output for pack '$packName': $builtRoot"
            }
            foreach ($directory in @($builtRoot, $stagedRoot, $stagedBundles)) {
                if (Test-Path -LiteralPath $directory) {
                    $item = Get-Item -LiteralPath $directory -Force
                    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                        throw "Refusing to stage pack output through a reparse point: $directory"
                    }
                }
            }
            New-Item -ItemType Directory -Path $stagedBundles -Force | Out-Null
            Copy-Item -LiteralPath $sourceContentPath -Destination (Join-Path $stagedRoot "content.json") -Force

            $relativeBundlePaths = Get-PackDeclaredBundleRelativePaths -PackName $packName
            foreach ($relativePath in $relativeBundlePaths) {
                $relativeFile = $relativePath.Replace('/', '\')
                $builtFile = [System.IO.Path]::GetFullPath((Join-Path $builtRoot $relativeFile))
                $builtPrefix = [System.IO.Path]::GetFullPath($builtRoot).TrimEnd('\') + '\'
                if (-not $builtFile.StartsWith($builtPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
                    -not (Test-Path -LiteralPath $builtFile -PathType Leaf)) {
                    throw "Pack '$packName' is missing a declared bundle in the current Unity output: $relativePath"
                }
                $bundleItem = Get-Item -LiteralPath $builtFile -Force
                if (($bundleItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or $bundleItem.Length -le 0) {
                    throw "Pack '$packName' has an unsafe or empty built bundle: $relativePath"
                }
                $stagedFile = Join-Path $stagedRoot $relativeFile
                New-Item -ItemType Directory -Path (Split-Path -Parent $stagedFile) -Force | Out-Null
                Copy-Item -LiteralPath $builtFile -Destination $stagedFile -Force
                if ((Get-FileHash -LiteralPath $builtFile -Algorithm SHA256).Hash -ne
                    (Get-FileHash -LiteralPath $stagedFile -Algorithm SHA256).Hash) {
                    throw "Staged bundle hash mismatch for pack '$packName': $relativePath"
                }
            }

            $stagedContentPath = Join-Path $stagedRoot "content.json"
            if ((Get-FileHash -LiteralPath $sourceContentPath -Algorithm SHA256).Hash -ne
                (Get-FileHash -LiteralPath $stagedContentPath -Algorithm SHA256).Hash) {
                throw "Staged content.json does not match the source pack: $packName"
            }
            & $pythonExe (Join-Path $repoRoot "tools/validate_content_pack.py") $stagedRoot
            if ($LASTEXITCODE -ne 0) {
                throw "Content-pack validation failed for '$packName'."
            }
            & $pythonExe (Join-Path $repoRoot "tools/validate_content_bundle_references.py") $stagedRoot
            if ($LASTEXITCODE -ne 0) {
                throw "AssetBundle reference validation failed for '$packName'."
            }

            $hasNativeVisualUnit = $false
            foreach ($unit in @($definition.units)) {
                if ((-not [string]::IsNullOrWhiteSpace([string]$unit.visualRootPath)) -or ($null -ne $unit.nativeRigRetarget)) {
                    $hasNativeVisualUnit = $true
                    break
                }
            }

            if ($SkipNativeVisualValidation) {
                [void]$nativeVisualAuditResults.Add([ordered]@{
                    pack = $packName
                    status = "skipped"
                    reason = "-SkipNativeVisualValidation specified"
                })
            } elseif (-not $hasGameInstall) {
                [void]$nativeVisualAuditResults.Add([ordered]@{
                    pack = $packName
                    status = "skipped"
                    reason = "GameRoot version.txt not found: $gameVersionFile"
                })
            } elseif (-not $hasNativeVisualUnit) {
                [void]$nativeVisualAuditResults.Add([ordered]@{
                    pack = $packName
                    status = "skipped"
                    reason = "No units declare visualRootPath or nativeRigRetarget"
                })
            } else {
                $nativeVisualLog = Join-Path $logDirectory "native-visual-roots-${packName}.log"
                $previousEap = $ErrorActionPreference
                $ErrorActionPreference = "Continue"
                try {
                    & $pythonExe (Join-Path $repoRoot "tools/validate_native_visual_roots.py") $stagedRoot --game-root $GameRoot 2>&1 |
                        ForEach-Object {
                            if ($_ -is [System.Management.Automation.ErrorRecord]) {
                                $_.ToString()
                            } else {
                                $_
                            }
                        } |
                        Tee-Object -FilePath $nativeVisualLog
                    $nativeVisualExit = $LASTEXITCODE
                } finally {
                    $ErrorActionPreference = $previousEap
                }
                if ($nativeVisualExit -ne 0) {
                    [void]$nativeVisualAuditResults.Add([ordered]@{
                        pack = $packName
                        status = "failed"
                        passed = $false
                        gameRoot = $GameRoot
                        logPath = $nativeVisualLog
                    })
                    $audits.nativeVisualRoots = @($nativeVisualAuditResults)
                    throw "Native visual root validation failed for '$packName' (exit code $nativeVisualExit). Log: $nativeVisualLog"
                }
                [void]$nativeVisualAuditResults.Add([ordered]@{
                    pack = $packName
                    status = "passed"
                    passed = $true
                    gameRoot = $GameRoot
                    logPath = $nativeVisualLog
                })
            }
            $audits.nativeVisualRoots = @($nativeVisualAuditResults)

            Write-Host "Staged and validated fresh Unity output for '$packName': $stagedRoot"
        }

        $manifestOut = New-CanonicalVisualBuildManifest `
            -RepositoryRoot $repoRoot `
            -ArtifactRoot $resolvedArtifactRoot `
            -ValidatedPackRoot $validatedPackRoot `
            -PackNames $Packs
        foreach ($packName in $Packs) {
            Assert-CanonicalVisualBuildFreshness `
                -RepositoryRoot $repoRoot `
                -ArtifactRoot $resolvedArtifactRoot `
                -ValidatedPackRoot $validatedPackRoot `
                -PackName $packName
        }
        return [ordered]@{
            buildManifestPath = $manifestOut
            packsValidated = $Packs
            nativeVisualRoots = @($nativeVisualAuditResults)
        }
    }

    # Phase 8: animation-matrix (MUST run before bundle-promotion)
    Invoke-TrackedPhase -Name "animation-matrix" -PlannedAction $planByName["animation-matrix"].action -PlannedReason $planByName["animation-matrix"].reason -ActionBlock {
        & $pythonExe -m tools.run_animation_validation_matrix --strict-loops --output $animationMatrixOutputPath
        if ($LASTEXITCODE -ne 0) {
            throw "Animation validation matrix failed (exit code $LASTEXITCODE). Report: $animationMatrixOutputPath"
        }
        $matrixReport = Get-Content -LiteralPath $animationMatrixOutputPath -Raw | ConvertFrom-Json
        if (-not $matrixReport.passed) {
            throw "Animation validation matrix reported passed=false: $animationMatrixOutputPath"
        }
        $audits.animationMatrix = [ordered]@{
            passed = [bool]$matrixReport.passed
            reportPath = $animationMatrixOutputPath
        }
        return $audits.animationMatrix
    }

    # Phase 9: bundle-promotion
    Invoke-TrackedPhase -Name "bundle-promotion" -PlannedAction $planByName["bundle-promotion"].action -PlannedReason $planByName["bundle-promotion"].reason -ActionBlock {
        $script:promotionAttempted = $true
        $script:promotionResult = Invoke-CanonicalBundlePromotion `
            -RepositoryRoot $repoRoot `
            -ValidatedPackRoot $validatedPackRoot `
            -BackupRoot $backupRoot `
            -PackNames $Packs
        return $script:promotionResult
    }

    # Phase 10: package-loader
    Invoke-TrackedPhase -Name "package-loader" -PlannedAction $planByName["package-loader"].action -PlannedReason $planByName["package-loader"].reason -ActionBlock {
        $packageInfo = @()
        $packageStdout = @(& $packageLoaderScript -CanonicalVisualArtifactRoot $resolvedArtifactRoot -GameRoot $GameRoot -InformationVariable packageInfo)
        $infoStrings = @($packageInfo | ForEach-Object { $_.ToString() })
        $allOutputLines = @($infoStrings + @($packageStdout | ForEach-Object { [string]$_ }))
        $archivePath = $null
        foreach ($line in $allOutputLines) {
            if ($line -match '^Package created:\s*(.+)$') {
                $archivePath = $Matches[1].Trim()
            }
        }
        $script:packageLoaderResult = [ordered]@{
            executed = $true
            canonicalVisualArtifactRoot = $resolvedArtifactRoot
            gameRoot = $GameRoot
            archivePath = $archivePath
            output = $allOutputLines
        }
        return $script:packageLoaderResult
    }

    $overallSuccess = $true
    Write-Host "Canonical visual build pipeline completed ($($Packs -join ', ')). Summary: $(Join-Path $resolvedArtifactRoot 'build-summary.json')"
} catch {
    $buildError = $_
    if ($promotionAttempted -and ($currentPhaseName -in @("bundle-promotion", "package-loader"))) {
        $packsWithBackups = @(
            foreach ($packName in $Packs) {
                $manifestCandidate = Join-Path (Join-Path $backupRoot $packName) "backup-manifest.json"
                if (Test-Path -LiteralPath $manifestCandidate -PathType Leaf) {
                    $packName
                }
            }
        )
        if ($packsWithBackups.Count -gt 0) {
            Write-Warning "Build failed during '$currentPhaseName' after bundle promotion started; restoring backups from '$backupRoot' for: $($packsWithBackups -join ', ')"
            try {
                $restoredPacks = @(Restore-CanonicalBundlePromotion `
                    -RepositoryRoot $repoRoot `
                    -BackupRoot $backupRoot `
                    -PackNames $packsWithBackups)
                $rollbackResult = [ordered]@{
                    attempted = $true
                    succeeded = $true
                    triggerPhase = $currentPhaseName
                    triggerError = $buildError.Exception.Message
                    backupRoot = $backupRoot
                    packs = $restoredPacks
                }
            } catch {
                $rollbackError = $_
                $rollbackResult = [ordered]@{
                    attempted = $true
                    succeeded = $false
                    triggerPhase = $currentPhaseName
                    triggerError = $buildError.Exception.Message
                    backupRoot = $backupRoot
                    error = $rollbackError.Exception.Message
                }
                Write-Warning "Automatic bundle rollback failed: $($rollbackError.Exception.Message)"
            }
        }
    }
    throw
} finally {
    $summary = [ordered]@{
        buildStamp = $buildStamp
        calibrationStatus = "UNCALIBRATED_USER_AUTHORIZED"
        parameters = [ordered]@{
            packs = @($Packs)
            incremental = [bool]$Incremental
            skipBlender = [bool]$SkipBlender
            skipTextures = [bool]$SkipTextures
            skipLevelOneRefresh = [bool]$SkipLevelOneRefresh
            skipUnityBuild = [bool]$SkipUnityBuild
            skipAnimationMatrix = [bool]$SkipAnimationMatrix
            noPromoteBundles = [bool]$NoPromoteBundles
            rebuildLevelOneModels = [bool]$RebuildLevelOneModels
            rebuildLevelOneTextures = [bool]$RebuildLevelOneTextures
            skipNativeVisualValidation = [bool]$SkipNativeVisualValidation
            packageLoader = [bool]$PackageLoader
            dryRun = [bool]$DryRun
            artifactRoot = $resolvedArtifactRoot
            gameRoot = $GameRoot
            restoreBundlesFrom = $RestoreBundlesFrom
            textureSizePower = $TextureSizePower
            maxParallelExports = $MaxParallelExports
            packWorkers = $PackWorkers
        }
        phases = @($phaseResults)
        packs = @($Packs)
        audits = $audits
        promotion = $promotionResult
        packageLoader = $packageLoaderResult
        rollback = $rollbackResult
        overallSuccess = $overallSuccess
    }
    $summaryPath = Join-Path $resolvedArtifactRoot "build-summary.json"
    New-Item -ItemType Directory -Force -Path $resolvedArtifactRoot | Out-Null
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 10), $utf8NoBom)
}
