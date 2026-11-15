param(
    [string[]]$Packs = @("sample-native-expansion", "tactical-operations"),
    [switch]$Incremental,
    [switch]$SkipBlender,
    [switch]$SkipTextures,
    [switch]$SkipLevelOneRefresh,
    [switch]$SkipUnityBuild,
    [switch]$SkipAnimationMatrix,
    [switch]$NoPromoteBundles,
    [switch]$RebuildLevelOneTextures,
    [switch]$DryRun,
    [string]$ArtifactRoot = "",
    [ValidateRange(7, 13)][int]$TextureSizePower = 11
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
. $provenanceScript

# Validate $Packs before anything else (including -DryRun)
if ($null -eq $Packs -or $Packs.Count -eq 0) {
    throw "At least one pack name must be provided in -Packs."
}
$validatedPacks = [System.Collections.Generic.List[string]]::new()
$seenPacks = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($rawPack in $Packs) {
    $packName = if ($null -eq $rawPack) { "" } else { $rawPack.Trim() }
    if ([string]::IsNullOrWhiteSpace($packName) -or $packName -notmatch '^[a-z0-9][a-z0-9-]*$') {
        throw "Invalid canonical pack name: '$rawPack'. Expected lowercase alphanumeric with hyphens (^[a-z0-9][a-z0-9-]*$)."
    }
    $packContentPath = Join-Path $repoRoot "content/packs/$packName/content.json"
    if (-not (Test-Path -LiteralPath $packContentPath -PathType Leaf)) {
        throw "Canonical pack content.json was not found for '$packName': $packContentPath"
    }
    if ($seenPacks.Add($packName)) {
        [void]$validatedPacks.Add($packName)
    }
}
$Packs = @($validatedPacks)

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

function Resolve-LevelOneTextureAssets {
    param(
        [string[]]$TargetPacks
    )

    $packsJson = $TargetPacks | ConvertTo-Json -Compress
    if ($TargetPacks.Count -eq 1) {
        $packsJson = "[$packsJson]"
    }
    $pyCode = @"
import json, sys
from tools.UnitPbrPresetRegistry import UNIT_PBR_REGISTRY
from tools.build_canonical_level1_textures import FACTORY_PRESET_MAP
packs = set(json.loads(sys.argv[1]))
assets = []
for profile in UNIT_PBR_REGISTRY.values():
    if profile.pack_id in packs and profile.model_name not in assets:
        assets.append(profile.model_name)
for factory_name in FACTORY_PRESET_MAP.keys():
    if factory_name not in assets:
        assets.append(factory_name)
print(json.dumps(assets))
"@
    $raw = & $pythonExe -c $pyCode $packsJson
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to resolve Level 1 texture asset list from UnitPbrPresetRegistry."
    }
    return @($raw | ConvertFrom-Json)
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

# Plan phases
$plannedPhases = [System.Collections.Generic.List[object]]::new()

# 1. level1-textures
if ($RebuildLevelOneTextures) {
    [void]$plannedPhases.Add([ordered]@{
        name = "level1-textures"
        action = "executed"
        reason = "-RebuildLevelOneTextures specified"
    })
} else {
    [void]$plannedPhases.Add([ordered]@{
        name = "level1-textures"
        action = "skipped"
        reason = "-RebuildLevelOneTextures not specified"
    })
}

# 2. blender
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

# 3. textures
$texturesInputHash = $null
if ($SkipTextures) {
    [void]$plannedPhases.Add([ordered]@{
        name = "textures"
        action = "skipped"
        reason = "-SkipTextures specified"
    })
} elseif ($Incremental) {
    $texturesInputHash = Get-CanonicalPhaseInputHash -RepositoryRoot $repoRoot -Phase "textures"
    $expectedTextureOutputs = Get-ExpectedTextureOutputs
    if ($expectedTextureOutputs.Count -gt 0 -and (Test-CanonicalPhaseCacheHit -CachePath $cachePath -Phase "textures" -InputHash $texturesInputHash -ExpectedOutputs $expectedTextureOutputs)) {
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
} else {
    [void]$plannedPhases.Add([ordered]@{
        name = "textures"
        action = "executed"
        reason = "Full build"
    })
}

# 4. unity
$unityMethod = ""
if ((-not $SkipLevelOneRefresh) -and (-not $SkipUnityBuild)) {
    $unityMethod = "RogueCommandExpansion.Authoring.CanonicalVisualModuleBuilder.RefreshAndBuildCanonicalModules"
    [void]$plannedPhases.Add([ordered]@{
        name = "unity"
        action = "executed"
        reason = "Single batchmode RefreshAndBuildCanonicalModules"
    })
} elseif ($SkipLevelOneRefresh -and (-not $SkipUnityBuild)) {
    $unityMethod = "RogueCommandExpansion.Authoring.CanonicalVisualModuleBuilder.BuildCanonicalModules"
    [void]$plannedPhases.Add([ordered]@{
        name = "unity"
        action = "executed"
        reason = "BuildCanonicalModules (-SkipLevelOneRefresh)"
    })
} elseif ((-not $SkipLevelOneRefresh) -and $SkipUnityBuild) {
    $unityMethod = "RogueCommandExpansion.Authoring.RogueCommandVisualSampleScaffolder.RefreshCanonicalLevelOneVisuals"
    [void]$plannedPhases.Add([ordered]@{
        name = "unity"
        action = "executed"
        reason = "RefreshCanonicalLevelOneVisuals (-SkipUnityBuild)"
    })
} else {
    [void]$plannedPhases.Add([ordered]@{
        name = "unity"
        action = "skipped"
        reason = "Both -SkipLevelOneRefresh and -SkipUnityBuild specified"
    })
}

# 5. unity-audit
if ($unityMethod -ne "") {
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

# 6. staging-and-pack-validation
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

# 7. bundle-promotion
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

# 8. animation-matrix
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

if ($DryRun) {
    Write-Host "=== Canonical Visual Modules Build (DryRun) ==="
    Write-Host "artifactRoot: $resolvedArtifactRoot"
    Write-Host "packs: $($Packs -join ', ')"
    Write-Host "cachePath: $cachePath"
    Write-Host "textureSizePower: $TextureSizePower"
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

if ($planByName["blender"].action -eq "executed") {
    if (-not (Test-Path -LiteralPath $blenderExe -PathType Leaf)) {
        throw "Pinned Blender 4.5.14 executable was not found: $blenderExe"
    }
    if (Get-Process -Name blender -ErrorAction SilentlyContinue) {
        throw "Close Blender before regenerating the canonical module source files."
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

New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

$phaseResults = [System.Collections.Generic.List[object]]::new()
$audits = [ordered]@{
    level1Textures = @()
    textures = @()
    unity = $null
    animationMatrix = $null
}
$promotionResult = $null
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
        [scriptblock]$ActionBlock
    )

    if ($PlannedAction -ne "executed") {
        [void]$phaseResults.Add([ordered]@{
            name = $Name
            status = $PlannedAction
            reason = $PlannedReason
            elapsedSeconds = 0.0
        })
        return
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $details = & $ActionBlock
        $sw.Stop()
        [void]$phaseResults.Add([ordered]@{
            name = $Name
            status = "executed"
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
    # Phase: level1-textures
    Invoke-TrackedPhase -Name "level1-textures" -PlannedAction $planByName["level1-textures"].action -PlannedReason $planByName["level1-textures"].reason -ActionBlock {
        $level1Assets = Resolve-LevelOneTextureAssets -TargetPacks $Packs
        if ($level1Assets.Count -eq 0) {
            throw "No Level 1 texture assets resolved for packs: $($Packs -join ', ')"
        }
        $resolution = [int][Math]::Pow(2, $TextureSizePower)
        Write-Host "Rebuilding Level 1 canonical textures ($($level1Assets.Count) assets, resolution ${resolution}x${resolution})."
        & $pythonExe -m tools.build_canonical_level1_textures @level1Assets --resolution $resolution
        if ($LASTEXITCODE -ne 0) {
            throw "Level 1 texture generation failed with exit code $LASTEXITCODE."
        }
        $assetReports = [System.Collections.Generic.List[object]]::new()
        foreach ($asset in $level1Assets) {
            $auditOut = Join-Path $logDirectory "mm-audit-level1-${asset}.json"
            & $pythonExe -m tools.audit_material_maker_output --directory $levelOneTextureDirectory --prefix $asset --workflow urp --output $auditOut
            if ($LASTEXITCODE -ne 0) {
                throw "Material Maker URP audit failed for Level 1 asset '$asset' (exit code $LASTEXITCODE)."
            }
            $report = Get-Content -LiteralPath $auditOut -Raw | ConvertFrom-Json
            if (-not $report.qualityGate -or $report.qualityGate.passed -ne $true) {
                throw "Material Maker URP quality gate failed for Level 1 asset '$asset'."
            }
            [void]$assetReports.Add([ordered]@{
                asset = $asset
                passed = $true
                reportPath = $auditOut
            })
        }
        $audits.level1Textures = @($assetReports)
        return [ordered]@{ assetCount = $level1Assets.Count; resolution = $resolution }
    }

    # Phase: blender
    Invoke-TrackedPhase -Name "blender" -PlannedAction $planByName["blender"].action -PlannedReason $planByName["blender"].reason -ActionBlock {
        $blenderVersion = @(& $blenderExe --version)
        if ($LASTEXITCODE -ne 0 -or $blenderVersion.Count -eq 0 -or $blenderVersion[0].Trim() -notmatch '^Blender 4\.5\.14(?:\s|$)') {
            throw "Canonical module geometry requires Blender 4.5.14; reported version: $($blenderVersion | Select-Object -First 1)"
        }
        Write-Host "Generating canonical Level 2 geometry with Blender 4.5.14."
        & $blenderExe --background --python-exit-code 1 --python $geometryScript -- --force 2>&1 |
            Tee-Object -FilePath $geometryLogPath
        $blenderExitCode = $LASTEXITCODE
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

        $hashToSave = if ($null -ne $blenderInputHash) { $blenderInputHash } else { Get-CanonicalPhaseInputHash -RepositoryRoot $repoRoot -Phase "blender" }
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

    # Phase: textures
    Invoke-TrackedPhase -Name "textures" -PlannedAction $planByName["textures"].action -PlannedReason $planByName["textures"].reason -ActionBlock {
        if ($presets.Count -le 0) {
            throw "Canonical module manifest has no shared PBR presets."
        }
        $detailNormalPath = Join-Path $projectPath "Assets/RogueCommandExpansionSample/Textures/Detail/RCL_SteelMicroDetail_Normal.png"
        Write-Host "Generating $($presets.Count) canonical four-map PBR sets with Material Maker 1.7 (SizePower=$TextureSizePower)."
        if (Test-Path -LiteralPath $detailNormalPath -PathType Leaf) {
            & $textureScript -SizePower $TextureSizePower -DetailNormalPath $detailNormalPath -DetailNormalStrength 0.25
        } else {
            & $textureScript -SizePower $TextureSizePower
        }
        if ($LASTEXITCODE -ne 0) {
            throw "Material Maker texture generation failed with exit code $LASTEXITCODE."
        }

        $textureAudits = [System.Collections.Generic.List[object]]::new()
        foreach ($preset in $presets) {
            $texturePrefix = "RCLModule_$preset"
            foreach ($suffix in @("Albedo", "MetallicGloss", "Normal", "Emission")) {
                $texturePath = Join-Path $textureDirectory "${texturePrefix}_${suffix}.png"
                if (-not (Test-Path -LiteralPath $texturePath -PathType Leaf) -or
                    (Get-Item -LiteralPath $texturePath).Length -le 0) {
                    throw "Material Maker output is missing or empty: $texturePath"
                }
            }
            $auditOut = Join-Path $logDirectory "mm-audit-${preset}.json"
            & $pythonExe -m tools.audit_material_maker_output --directory $textureDirectory --prefix $texturePrefix --workflow urp --output $auditOut
            if ($LASTEXITCODE -ne 0) {
                throw "Material Maker URP audit failed for preset '$preset' (exit code $LASTEXITCODE)."
            }
            $report = Get-Content -LiteralPath $auditOut -Raw | ConvertFrom-Json
            if (-not $report.qualityGate -or $report.qualityGate.passed -ne $true) {
                throw "Material Maker URP quality gate failed for preset '$preset'."
            }
            [void]$textureAudits.Add([ordered]@{
                preset = $preset
                passed = $true
                reportPath = $auditOut
            })
        }
        $audits.textures = @($textureAudits)

        $hashToSave = if ($null -ne $texturesInputHash) { $texturesInputHash } else { Get-CanonicalPhaseInputHash -RepositoryRoot $repoRoot -Phase "textures" }
        Save-CanonicalPhaseCache -CachePath $cachePath -Phase "textures" -InputHash $hashToSave -Metadata @{
            sizePower = $TextureSizePower
            presetCount = $presets.Count
        } | Out-Null

        return [ordered]@{ presetCount = $presets.Count; sizePower = $TextureSizePower }
    }

    # Phase: unity
    Invoke-TrackedPhase -Name "unity" -PlannedAction $planByName["unity"].action -PlannedReason $planByName["unity"].reason -ActionBlock {
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
                $sourceContentPath = Join-Path $repoRoot "content/packs/$packName/content.json"
                $definition = Get-Content -LiteralPath $sourceContentPath -Raw | ConvertFrom-Json
                $builtRoot = Join-Path $builtPackRoot $packName
                $relPaths = @($definition.assetBundleManifestPath)
                $relPaths += @($definition.assetBundles | ForEach-Object { $_.path })
                foreach ($relPath in $relPaths) {
                    if (-not [string]::IsNullOrWhiteSpace($relPath)) {
                        $candidate = Join-Path $builtRoot ($relPath.Replace('/', '\'))
                        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                            [void]$builtBundlePaths.Add($candidate)
                        }
                    }
                }
            }
        }

        return [ordered]@{ executeMethod = $unityMethod; logPath = $script:unityExecutedLogPath }
    }

    # Phase: unity-audit
    Invoke-TrackedPhase -Name "unity-audit" -PlannedAction $planByName["unity-audit"].action -PlannedReason $planByName["unity-audit"].reason -ActionBlock {
        $auditArgs = @("-m", "tools.audit_unity_build", "--strict", "--log", $unityExecutedLogPath, "--output", $unityAuditOutputPath)
        foreach ($bundlePath in $builtBundlePaths) {
            $auditArgs += @("--bundle", $bundlePath)
        }
        & $pythonExe @auditArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Unity build audit failed (exit code $LASTEXITCODE). Report: $unityAuditOutputPath"
        }
        $unityReport = Get-Content -LiteralPath $unityAuditOutputPath -Raw | ConvertFrom-Json
        $audits.unity = [ordered]@{
            passed = [bool]$unityReport.build.quality_gate.passed
            reportPath = $unityAuditOutputPath
            bundleCount = [int]$unityReport.metrics.assetbundle_count
        }
        return $audits.unity
    }

    # Phase: staging-and-pack-validation
    Invoke-TrackedPhase -Name "staging-and-pack-validation" -PlannedAction $planByName["staging-and-pack-validation"].action -PlannedReason $planByName["staging-and-pack-validation"].reason -ActionBlock {
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

            $relativeBundlePaths = @($definition.assetBundleManifestPath)
            $relativeBundlePaths += @($definition.assetBundles | ForEach-Object { $_.path })
            foreach ($relativePath in $relativeBundlePaths) {
                if ([string]::IsNullOrWhiteSpace($relativePath) -or
                    $relativePath -notmatch '^bundles/[a-z0-9][a-z0-9._-]*\.bundle$') {
                    throw "Pack '$packName' declares an invalid bundle path: $relativePath"
                }
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
        return [ordered]@{ buildManifestPath = $manifestOut; packsValidated = $Packs }
    }

    # Phase: bundle-promotion
    Invoke-TrackedPhase -Name "bundle-promotion" -PlannedAction $planByName["bundle-promotion"].action -PlannedReason $planByName["bundle-promotion"].reason -ActionBlock {
        $script:promotionResult = Invoke-CanonicalBundlePromotion `
            -RepositoryRoot $repoRoot `
            -ValidatedPackRoot $validatedPackRoot `
            -BackupRoot $backupRoot `
            -PackNames $Packs
        return $script:promotionResult
    }

    # Phase: animation-matrix
    Invoke-TrackedPhase -Name "animation-matrix" -PlannedAction $planByName["animation-matrix"].action -PlannedReason $planByName["animation-matrix"].reason -ActionBlock {
        & $pythonExe -m tools.run_animation_validation_matrix --strict-loops --output $animationMatrixOutputPath
        if ($LASTEXITCODE -ne 0) {
            throw "Animation validation matrix failed (exit code $LASTEXITCODE). Report: $animationMatrixOutputPath"
        }
        $matrixReport = Get-Content -LiteralPath $animationMatrixOutputPath -Raw | ConvertFrom-Json
        $audits.animationMatrix = [ordered]@{
            passed = [bool]$matrixReport.overall_passed
            reportPath = $animationMatrixOutputPath
        }
        return $audits.animationMatrix
    }

    $overallSuccess = $true
    Write-Host "Canonical visual build pipeline completed ($($Packs -join ', ')). Summary: $(Join-Path $resolvedArtifactRoot 'build-summary.json')"
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
            rebuildLevelOneTextures = [bool]$RebuildLevelOneTextures
            dryRun = [bool]$DryRun
            artifactRoot = $resolvedArtifactRoot
            textureSizePower = $TextureSizePower
        }
        phases = @($phaseResults)
        packs = @($Packs)
        audits = $audits
        promotion = $promotionResult
        overallSuccess = $overallSuccess
    }
    $summaryPath = Join-Path $resolvedArtifactRoot "build-summary.json"
    New-Item -ItemType Directory -Force -Path $resolvedArtifactRoot | Out-Null
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 10), $utf8NoBom)
}
