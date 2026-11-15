function Get-CanonicalVisualBuildInputState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $root = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\')
    $unityRoot = Join-Path $root 'unity/RogueCommandAssetAuthoring'
    $fixedInputs = @(
        'content/packs/sample-native-expansion/content.json',
        'content/packs/tactical-operations/content.json',
        'content/source-art/canonical-visual-modules.blend',
        'content/source-art/canonical-visual-modules.json',
        'content/source-art/canonical-visual-module-roles.json',
        'content/source-art/canonical-visual-module-build-policy.json',
        'tools/Build-CanonicalVisualModules.ps1',
        'tools/Build-CanonicalVisualModules.py',
        'tools/Build-CanonicalVisualModuleTextures.ps1',
        'tools/Build-SampleScoutModel.py',
        'tools/Build-ColossusSiegeModel.py',
        'tools/Build-SkirmishStrikeBikeModel.py',
        'tools/Build-SurgeArcTankModel.py',
        'tools/Build-SandlanceScoutRiggedModel.py',
        'tools/Build-ColossusSiegeRiggedModel.py',
        'tools/Build-SkirmishStrikeBikeRiggedModel.py',
        'tools/Build-SurgeArcTankRiggedModel.py',
        'tools/CanonicalUnitHardpoints.py',
        'tools/VisualModelAuthoringSupport.py',
        'tools/hard_surface_geometry_presets.py',
        'tools/hard_surface_geometry_presets_v2.py',
        'tools/hard_surface_geometry_presets_functional.py',
        'tools/hard_surface_geometry_presets_detailing.py',
        'tools/CanonicalVisualBuildProvenance.ps1',
        'tools/Run-MaterialMakerBake.ps1',
        'tools/Prepare-MaterialMakerTacticalTemplate.py',
        'tools/pack_material_maker_pbr.py',
        'tools/audit_material_maker_output.py',
        'tools/MaterialMakerWaitDevice.gd',
        'tools/validate_material_maker_config.py',
        'tools/material-maker/rogue-command-material-maker.json',
        'unity/RogueCommandAssetAuthoring/Packages/manifest.json'
    )
    $inputPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($relativePath in $fixedInputs) {
        $fullPath = [System.IO.Path]::GetFullPath((Join-Path $root $relativePath))
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "Canonical visual build input is missing: $relativePath"
        }
        [void]$inputPaths.Add($fullPath)
    }

    foreach ($directoryName in @('Assets', 'ProjectSettings', 'Packages')) {
        $directoryPath = Join-Path $unityRoot $directoryName
        if (-not (Test-Path -LiteralPath $directoryPath -PathType Container)) {
            throw "Unity authoring input directory is missing: $directoryPath"
        }
        $directoryItem = Get-Item -LiteralPath $directoryPath -Force
        if (($directoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Canonical visual build input directories cannot be reparse points: $directoryPath"
        }
        foreach ($file in Get-ChildItem -LiteralPath $directoryPath -File -Recurse -Force) {
            if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Canonical visual build inputs cannot contain reparse-point files: $($file.FullName)"
            }
            [void]$inputPaths.Add([System.IO.Path]::GetFullPath($file.FullName))
        }
    }

    # Use an ordinally sorted map instead of culture-sensitive Sort-Object.
    # Unity asset paths can compare equal under culture rules while differing
    # ordinally, making a valid manifest fingerprint depend on process/order.
    $filesByPath = [System.Collections.Generic.SortedDictionary[string, string]]::new(
        [System.StringComparer]::Ordinal
    )
    foreach ($fullPath in $inputPaths) {
        if (-not $fullPath.StartsWith($root + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Canonical visual build input escaped the repository: $fullPath"
        }
        $item = Get-Item -LiteralPath $fullPath -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Canonical visual build input cannot be a reparse point: $fullPath"
        }
        $relativePath = $fullPath.Substring($root.Length + 1).Replace('\', '/')
        $filesByPath.Add(
            $relativePath,
            (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
        )
    }

    $files = @(
        foreach ($relativePath in $filesByPath.Keys) {
            [pscustomobject]@{
                path = $relativePath
                sha256 = $filesByPath[$relativePath]
            }
        }
    )

    $fingerprintText = [string]::Join("`n", @($files | ForEach-Object { "$($_.path)`t$($_.sha256)" }))
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        $fingerprint = [System.BitConverter]::ToString(
            $hasher.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($fingerprintText))
        ).Replace('-', '').ToLowerInvariant()
    } finally {
        $hasher.Dispose()
    }

    return [pscustomobject]@{
        sourceFingerprint = $fingerprint
        sourceFiles = @($files)
    }
}

function New-CanonicalVisualBuildManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [string]$ArtifactRoot,
        [Parameter(Mandatory = $true)]
        [string]$ValidatedPackRoot,
        [Parameter(Mandatory = $true)]
        [string[]]$PackNames
    )

    $sourceState = Get-CanonicalVisualBuildInputState -RepositoryRoot $RepositoryRoot
    $packRecords = @(
        foreach ($packName in $PackNames) {
            if ($packName -notmatch '^[a-z0-9][a-z0-9-]*$') {
                throw "Invalid canonical visual pack name: $packName"
            }
            $packRoot = Join-Path $ValidatedPackRoot $packName
            $contentPath = Join-Path $packRoot 'content.json'
            if (-not (Test-Path -LiteralPath $contentPath -PathType Leaf)) {
                throw "Validated canonical pack is missing content.json: $packName"
            }
            $definition = Get-Content -LiteralPath $contentPath -Raw | ConvertFrom-Json
            $relativeBundlePaths = @([string]$definition.assetBundleManifestPath)
            $relativeBundlePaths += @($definition.assetBundles | ForEach-Object { [string]$_.path })
            $seenPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            $bundleRecords = @(
                foreach ($relativePath in $relativeBundlePaths) {
                    if ([string]::IsNullOrWhiteSpace($relativePath) -or
                        $relativePath -notmatch '^bundles/[a-z0-9][a-z0-9._-]*\.bundle$' -or
                        -not $seenPaths.Add($relativePath)) {
                        throw "Invalid or duplicate declared bundle path in '$packName': $relativePath"
                    }
                    $bundlePath = [System.IO.Path]::GetFullPath(
                        (Join-Path $packRoot ($relativePath.Replace('/', '\')))
                    )
                    $packPrefix = [System.IO.Path]::GetFullPath($packRoot).TrimEnd('\') + '\'
                    if (-not $bundlePath.StartsWith($packPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
                        -not (Test-Path -LiteralPath $bundlePath -PathType Leaf)) {
                        throw "Validated canonical pack is missing its declared bundle: $packName/$relativePath"
                    }
                    [pscustomobject]@{
                        path = $relativePath
                        sha256 = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
                    }
                }
            )
            [pscustomobject]@{
                packName = $packName
                contentJsonSha256 = (Get-FileHash -LiteralPath $contentPath -Algorithm SHA256).Hash.ToLowerInvariant()
                bundles = @($bundleRecords)
            }
        }
    )

    $manifest = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        sourceFingerprint = $sourceState.sourceFingerprint
        sourceFiles = $sourceState.sourceFiles
        packs = $packRecords
    }
    $manifestPath = Join-Path $ArtifactRoot 'build-manifest.json'
    $manifestDirectory = Split-Path -Parent $manifestPath
    New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    return $manifestPath
}

function Assert-CanonicalVisualBuildFreshness {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [string]$ArtifactRoot,
        [Parameter(Mandatory = $true)]
        [string]$ValidatedPackRoot,
        [Parameter(Mandatory = $true)]
        [string]$PackName
    )

    $manifestPath = Join-Path $ArtifactRoot 'build-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Canonical pack provenance is missing. Run tools/Build-CanonicalVisualModules.ps1 first: $manifestPath"
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1) {
        throw "Unsupported canonical visual build manifest schema: $($manifest.schemaVersion)"
    }
    $currentState = Get-CanonicalVisualBuildInputState -RepositoryRoot $RepositoryRoot
    if ($manifest.sourceFingerprint -ne $currentState.sourceFingerprint) {
        throw 'Canonical visual build inputs changed after the last successful build. Run tools/Build-CanonicalVisualModules.ps1 again.'
    }

    $packRecords = @($manifest.packs | Where-Object { $_.packName -ceq $PackName })
    if ($packRecords.Count -ne 1) {
        throw "Canonical visual build manifest does not contain exactly one '$PackName' pack."
    }
    $packRecord = $packRecords[0]
    $packRoot = Join-Path $ValidatedPackRoot $PackName
    $contentPath = Join-Path $packRoot 'content.json'
    $sourceContentPath = Join-Path $RepositoryRoot "content/packs/$PackName/content.json"
    if (-not (Test-Path -LiteralPath $contentPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $sourceContentPath -PathType Leaf)) {
        throw "Canonical staged or source pack content is missing: $PackName"
    }
    $contentHash = (Get-FileHash -LiteralPath $contentPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $sourceContentHash = (Get-FileHash -LiteralPath $sourceContentPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($contentHash -ne $sourceContentHash -or $contentHash -ne $packRecord.contentJsonSha256) {
        throw "Canonical staged content or provenance is stale for '$PackName'. Run tools/Build-CanonicalVisualModules.ps1 again."
    }

    $definition = Get-Content -LiteralPath $contentPath -Raw | ConvertFrom-Json
    $declaredPaths = @([string]$definition.assetBundleManifestPath)
    $declaredPaths += @($definition.assetBundles | ForEach-Object { [string]$_.path })
    if ($declaredPaths.Count -ne @($packRecord.bundles).Count) {
        throw "Canonical bundle inventory changed after the last build for '$PackName'."
    }
    foreach ($relativePath in $declaredPaths) {
        if ($relativePath -notmatch '^bundles/[a-z0-9][a-z0-9._-]*\.bundle$') {
            throw "Invalid canonical bundle path in '$PackName': $relativePath"
        }
        $bundleRecord = @($packRecord.bundles | Where-Object { $_.path -ceq $relativePath })
        if ($bundleRecord.Count -ne 1) {
            throw "Canonical bundle provenance is missing or duplicated: $PackName/$relativePath"
        }
        $bundlePath = [System.IO.Path]::GetFullPath(
            (Join-Path $packRoot ($relativePath.Replace('/', '\')))
        )
        $packPrefix = [System.IO.Path]::GetFullPath($packRoot).TrimEnd('\') + '\'
        if (-not $bundlePath.StartsWith($packPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $bundlePath -PathType Leaf)) {
            throw "Canonical staged bundle is missing or outside its pack: $PackName/$relativePath"
        }
        $bundleHash = (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($bundleHash -ne $bundleRecord[0].sha256) {
            throw "Canonical staged bundle changed after the last successful build: $PackName/$relativePath"
        }
    }
}

function Get-CanonicalPhaseInputHash {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [ValidateSet('blender', 'textures', 'level1-textures', 'level1-models', 'unity')]
        [string]$Phase
    )

    $root = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\')
    $unityRoot = Join-Path $root 'unity/RogueCommandAssetAuthoring'
    $requiredInputs = @()
    $optionalInputs = @()
    $includeUnityDirectories = $false
    $includeLevelOneSourceBlends = $false

    switch ($Phase) {
        'blender' {
            $requiredInputs = @(
                'content/packs/sample-native-expansion/content.json',
                'content/packs/tactical-operations/content.json',
                'content/source-art/canonical-visual-module-roles.json',
                'content/source-art/canonical-visual-module-build-policy.json',
                'tools/Build-CanonicalVisualModules.py',
                'tools/CanonicalUnitHardpoints.py',
                'tools/VisualModelAuthoringSupport.py',
                'tools/hard_surface_geometry_presets.py',
                'tools/hard_surface_geometry_presets_v2.py',
                'tools/hard_surface_geometry_presets_functional.py',
                'tools/hard_surface_geometry_presets_detailing.py'
            )
        }
        'textures' {
            $requiredInputs = @(
                'content/source-art/canonical-visual-modules.json',
                'tools/Build-CanonicalVisualModuleTextures.ps1',
                'tools/batch_material_maker_textures.py',
                'tools/Run-MaterialMakerBake.ps1',
                'tools/Prepare-MaterialMakerTacticalTemplate.py',
                'tools/pack_material_maker_pbr.py',
                'tools/audit_material_maker_output.py',
                'tools/MaterialMakerWaitDevice.gd',
                'tools/validate_material_maker_config.py',
                'tools/material-maker/rogue-command-material-maker.json'
            )
            $optionalInputs = @(
                'unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Textures/Detail/RCL_SteelMicroDetail_Normal.png'
            )
        }
        'level1-textures' {
            $requiredInputs = @(
                'tools/build_canonical_level1_textures.py',
                'tools/audit_material_maker_output.py',
                'tools/pack_material_maker_pbr.py',
                'tools/material-maker/rogue-command-material-maker.json'
            )
            $optionalInputs = @(
                'tools/extract_level1_uv_regions.py',
                'tools/Run-MaterialMakerBake.ps1',
                'tools/Prepare-MaterialMakerTacticalTemplate.py',
                'tools/MaterialMakerWaitDevice.gd',
                'unity/RogueCommandAssetAuthoring/Assets/RogueCommandExpansionSample/Textures/Detail/RCL_SteelMicroDetail_Normal.png'
            )
        }
        'level1-models' {
            $requiredInputs = @(
                'tools/RogueModelKit.py',
                'tools/CanonicalUnitHardpoints.py',
                'tools/VisualModelAuthoringSupport.py'
            )
            $optionalInputs = @(
                'tools/sample_native_vehicles_v2_recipes.py',
                'tools/sample_native_walkers_v2_recipes.py',
                'tools/tactical_operations_v2_recipes.py',
                'tools/Build-SampleNativeVehiclesV2SourceBlends.py',
                'tools/Build-SampleNativeWalkersV2SourceBlends.py',
                'tools/Build-TacticalOperationsV2SourceBlends.py'
            )
            $includeLevelOneSourceBlends = $true
        }
        'unity' {
            $requiredInputs = @(
                'content/packs/sample-native-expansion/content.json',
                'content/packs/tactical-operations/content.json',
                'content/source-art/canonical-visual-modules.json',
                'unity/RogueCommandAssetAuthoring/Packages/manifest.json'
            )
            $includeUnityDirectories = $true
        }
    }

    $inputPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($relativePath in $requiredInputs) {
        $fullPath = [System.IO.Path]::GetFullPath((Join-Path $root $relativePath))
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "Required input for phase '$Phase' is missing: $relativePath"
        }
        [void]$inputPaths.Add($fullPath)
    }

    foreach ($relativePath in $optionalInputs) {
        $fullPath = [System.IO.Path]::GetFullPath((Join-Path $root $relativePath))
        if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            [void]$inputPaths.Add($fullPath)
        }
    }

    if ($includeLevelOneSourceBlends) {
        $sourceArtDir = Join-Path $root 'content/source-art'
        if (Test-Path -LiteralPath $sourceArtDir -PathType Container) {
            $sourceArtItem = Get-Item -LiteralPath $sourceArtDir -Force
            if (($sourceArtItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Phase '$Phase' input directories cannot be reparse points: $sourceArtDir"
            }
            foreach ($file in Get-ChildItem -LiteralPath $sourceArtDir -Filter '*.blend' -File -Force) {
                $name = $file.Name
                if (-not $name.EndsWith('.blend', [System.StringComparison]::OrdinalIgnoreCase)) {
                    continue
                }
                if ($name.EndsWith('-v2.blend', [System.StringComparison]::OrdinalIgnoreCase) -or
                    $name.EndsWith('-rigged.blend', [System.StringComparison]::OrdinalIgnoreCase) -or
                    [string]::Equals($name, 'canonical-visual-modules.blend', [System.StringComparison]::OrdinalIgnoreCase)) {
                    continue
                }
                if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                    continue
                }
                $fullPath = [System.IO.Path]::GetFullPath($file.FullName)
                if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
                    [void]$inputPaths.Add($fullPath)
                }
            }
        }
    }

    if ($includeUnityDirectories) {
        foreach ($directoryName in @('Assets', 'ProjectSettings', 'Packages')) {
            $directoryPath = Join-Path $unityRoot $directoryName
            if (-not (Test-Path -LiteralPath $directoryPath -PathType Container)) {
                throw "Unity authoring input directory is missing for phase '$Phase': $directoryPath"
            }
            $directoryItem = Get-Item -LiteralPath $directoryPath -Force
            if (($directoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Phase '$Phase' input directories cannot be reparse points: $directoryPath"
            }
            foreach ($file in Get-ChildItem -LiteralPath $directoryPath -File -Recurse -Force) {
                if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Phase '$Phase' inputs cannot contain reparse-point files: $($file.FullName)"
                }
                [void]$inputPaths.Add([System.IO.Path]::GetFullPath($file.FullName))
            }
        }
    }

    $filesByPath = [System.Collections.Generic.SortedDictionary[string, string]]::new(
        [System.StringComparer]::Ordinal
    )
    foreach ($fullPath in $inputPaths) {
        if (-not $fullPath.StartsWith($root + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Phase '$Phase' input escaped the repository: $fullPath"
        }
        $item = Get-Item -LiteralPath $fullPath -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Phase '$Phase' input cannot be a reparse point: $fullPath"
        }
        $relativePath = $fullPath.Substring($root.Length + 1).Replace('\', '/')
        $filesByPath.Add(
            $relativePath,
            (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
        )
    }

    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add("phase`t$Phase")
    foreach ($relativePath in $filesByPath.Keys) {
        [void]$lines.Add("$relativePath`t$($filesByPath[$relativePath])")
    }

    $fingerprintText = [string]::Join("`n", $lines.ToArray())
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [System.BitConverter]::ToString(
            $hasher.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($fingerprintText))
        ).Replace('-', '').ToLowerInvariant()
    } finally {
        $hasher.Dispose()
    }
}

function Test-CanonicalPhaseCacheHit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$CachePath,
        [Parameter(Mandatory = $true)]
        [string]$Phase,
        [Parameter(Mandatory = $true)]
        [string]$InputHash,
        [Parameter(Mandatory = $false)]
        [string[]]$ExpectedOutputs = @()
    )

    if (-not (Test-Path -LiteralPath $CachePath -PathType Leaf)) {
        return $false
    }

    $cache = $null
    try {
        $raw = Get-Content -LiteralPath $CachePath -Raw
        if ([string]::IsNullOrWhiteSpace($raw)) {
            return $false
        }
        $cache = $raw | ConvertFrom-Json
    } catch {
        return $false
    }

    if ($null -eq $cache -or $null -eq $cache.phases) {
        return $false
    }

    $phaseProperty = $cache.phases.PSObject.Properties[$Phase]
    if ($null -eq $phaseProperty -or $null -eq $phaseProperty.Value) {
        return $false
    }

    $cachedHash = [string]$phaseProperty.Value.inputHash
    if ($cachedHash -cne $InputHash) {
        return $false
    }

    foreach ($outputPath in @($ExpectedOutputs)) {
        if ([string]::IsNullOrWhiteSpace($outputPath)) {
            return $false
        }
        if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
            return $false
        }
        $item = Get-Item -LiteralPath $outputPath -Force
        if ($item.Length -le 0) {
            return $false
        }
    }

    return $true
}

function Save-CanonicalPhaseCache {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$CachePath,
        [Parameter(Mandatory = $true)]
        [string]$Phase,
        [Parameter(Mandatory = $true)]
        [string]$InputHash,
        [Parameter(Mandatory = $false)]
        [hashtable]$Metadata = @{}
    )

    $resolvedCachePath = [System.IO.Path]::GetFullPath($CachePath)
    $cacheDirectory = Split-Path -Parent $resolvedCachePath
    if (-not [string]::IsNullOrWhiteSpace($cacheDirectory)) {
        New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null
    }

    $phases = [ordered]@{}
    if (Test-Path -LiteralPath $resolvedCachePath -PathType Leaf) {
        try {
            $raw = Get-Content -LiteralPath $resolvedCachePath -Raw
            if (-not [string]::IsNullOrWhiteSpace($raw)) {
                $existing = $raw | ConvertFrom-Json
                if ($null -ne $existing -and $null -ne $existing.phases) {
                    foreach ($prop in $existing.phases.PSObject.Properties) {
                        $phases[$prop.Name] = $prop.Value
                    }
                }
            }
        } catch {
            $phases = [ordered]@{}
        }
    }

    $phases[$Phase] = [ordered]@{
        inputHash = $InputHash
        updatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        metadata = if ($null -ne $Metadata) { $Metadata } else { @{} }
    }

    $cacheObject = [ordered]@{
        schemaVersion = 1
        updatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        phases = $phases
    }

    $tempPath = "$resolvedCachePath.tmp.$PID.$([Guid]::NewGuid().ToString('N'))"
    try {
        $cacheObject | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $tempPath -Encoding UTF8
        Move-Item -LiteralPath $tempPath -Destination $resolvedCachePath -Force
    } finally {
        if (Test-Path -LiteralPath $tempPath -PathType Leaf) {
            Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-CanonicalBundlePromotion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [string]$ValidatedPackRoot,
        [Parameter(Mandatory = $true)]
        [string]$BackupRoot,
        [Parameter(Mandatory = $true)]
        [string[]]$PackNames
    )

    $root = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\')
    $validatedRootFull = [System.IO.Path]::GetFullPath($ValidatedPackRoot).TrimEnd('\')
    $backupRootFull = [System.IO.Path]::GetFullPath($BackupRoot).TrimEnd('\')

    $summaries = @(
        foreach ($packName in $PackNames) {
            if ($packName -notmatch '^[a-z0-9][a-z0-9-]*$') {
                throw "Invalid canonical visual pack name for promotion: $packName"
            }

            $sourcePackDir = Join-Path $root "content\packs\$packName"
            $sourceContentPath = Join-Path $sourcePackDir 'content.json'
            if (-not (Test-Path -LiteralPath $sourceContentPath -PathType Leaf)) {
                throw "Source pack content.json is missing for promotion: $sourceContentPath"
            }

            $validatedPackDir = Join-Path $validatedRootFull $packName
            if (-not (Test-Path -LiteralPath $validatedPackDir -PathType Container)) {
                throw "Validated pack directory is missing for promotion: $validatedPackDir"
            }

            $definition = Get-Content -LiteralPath $sourceContentPath -Raw | ConvertFrom-Json
            $declaredPaths = @([string]$definition.assetBundleManifestPath)
            $declaredPaths += @($definition.assetBundles | ForEach-Object { [string]$_.path })

            $seenPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            $normalizedPaths = @(
                foreach ($relativePath in $declaredPaths) {
                    if ([string]::IsNullOrWhiteSpace($relativePath) -or
                        $relativePath -notmatch '^bundles/[a-z0-9][a-z0-9._-]*\.bundle$') {
                        throw "Invalid declared bundle path in '$packName': $relativePath"
                    }
                    if ($seenPaths.Add($relativePath)) {
                        $relativePath
                    }
                }
            )

            $packBackupDir = Join-Path $backupRootFull $packName
            New-Item -ItemType Directory -Path $packBackupDir -Force | Out-Null

            $sourcePackPrefix = [System.IO.Path]::GetFullPath($sourcePackDir).TrimEnd('\') + '\'
            $validatedPackPrefix = [System.IO.Path]::GetFullPath($validatedPackDir).TrimEnd('\') + '\'
            $packBackupPrefix = [System.IO.Path]::GetFullPath($packBackupDir).TrimEnd('\') + '\'

            $backupRecords = New-Object System.Collections.Generic.List[object]
            $promotedRecords = New-Object System.Collections.Generic.List[object]
            $backedUpCount = 0

            foreach ($relativePath in $normalizedPaths) {
                $relativeWin = $relativePath.Replace('/', '\')
                $validatedBundlePath = [System.IO.Path]::GetFullPath((Join-Path $validatedPackDir $relativeWin))
                $liveBundlePath = [System.IO.Path]::GetFullPath((Join-Path $sourcePackDir $relativeWin))
                $backupBundlePath = [System.IO.Path]::GetFullPath((Join-Path $packBackupDir $relativeWin))

                if (-not $validatedBundlePath.StartsWith($validatedPackPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
                    -not $liveBundlePath.StartsWith($sourcePackPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
                    -not $backupBundlePath.StartsWith($packBackupPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                    throw "Bundle path escaped pack boundary during promotion for '$packName': $relativePath"
                }

                if (-not (Test-Path -LiteralPath $validatedBundlePath -PathType Leaf)) {
                    throw "Validated bundle is missing for promotion: $packName/$relativePath"
                }
                $validatedItem = Get-Item -LiteralPath $validatedBundlePath -Force
                if (($validatedItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
                    $validatedItem.Length -le 0) {
                    throw "Validated bundle is unsafe or empty: $packName/$relativePath"
                }

                if (Test-Path -LiteralPath $liveBundlePath -PathType Leaf) {
                    $priorHash = (Get-FileHash -LiteralPath $liveBundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
                    $backupParent = Split-Path -Parent $backupBundlePath
                    New-Item -ItemType Directory -Path $backupParent -Force | Out-Null
                    Copy-Item -LiteralPath $liveBundlePath -Destination $backupBundlePath -Force
                    $backupHash = (Get-FileHash -LiteralPath $backupBundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
                    if ($backupHash -ne $priorHash) {
                        throw "Backup SHA-256 mismatch for '$packName/$relativePath' (expected $priorHash, got $backupHash)"
                    }
                    $backedUpCount++
                    [void]$backupRecords.Add([ordered]@{
                        path = $relativePath
                        existed = $true
                        sha256 = $priorHash
                        backupPath = $backupBundlePath
                    })
                } else {
                    [void]$backupRecords.Add([ordered]@{
                        path = $relativePath
                        existed = $false
                        sha256 = $null
                        backupPath = $null
                    })
                }
            }

            $backupManifestPath = Join-Path $packBackupDir 'backup-manifest.json'
            $backupManifest = [ordered]@{
                schemaVersion = 1
                packName = $packName
                createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
                bundles = @($backupRecords.ToArray())
            }
            $backupManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $backupManifestPath -Encoding UTF8

            foreach ($relativePath in $normalizedPaths) {
                $relativeWin = $relativePath.Replace('/', '\')
                $validatedBundlePath = [System.IO.Path]::GetFullPath((Join-Path $validatedPackDir $relativeWin))
                $liveBundlePath = [System.IO.Path]::GetFullPath((Join-Path $sourcePackDir $relativeWin))

                $sourceHash = (Get-FileHash -LiteralPath $validatedBundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
                $liveParent = Split-Path -Parent $liveBundlePath
                New-Item -ItemType Directory -Path $liveParent -Force | Out-Null
                Copy-Item -LiteralPath $validatedBundlePath -Destination $liveBundlePath -Force
                $postCopyHash = (Get-FileHash -LiteralPath $liveBundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
                if ($postCopyHash -ne $sourceHash) {
                    throw "Post-copy SHA-256 verification failed for '$packName/$relativePath' (expected $sourceHash, got $postCopyHash)"
                }

                [void]$promotedRecords.Add([pscustomobject]@{
                    path = $relativePath
                    sha256 = $postCopyHash
                    destinationPath = $liveBundlePath
                })
            }

            [pscustomobject]@{
                packName = $packName
                backupManifestPath = $backupManifestPath
                backedUpCount = $backedUpCount
                promotedCount = $promotedRecords.Count
                bundles = @($promotedRecords.ToArray())
            }
        }
    )

    return $summaries
}

function Restore-CanonicalBundlePromotion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [string]$BackupRoot,
        [Parameter(Mandatory = $false)]
        [string[]]$PackNames = @()
    )

    $root = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\')
    $backupRootFull = [System.IO.Path]::GetFullPath($BackupRoot).TrimEnd('\')
    if (-not (Test-Path -LiteralPath $backupRootFull -PathType Container)) {
        throw "Backup root directory does not exist for restore: $backupRootFull"
    }
    $backupRootItem = Get-Item -LiteralPath $backupRootFull -Force
    if (($backupRootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Backup root directory cannot be a reparse point: $backupRootFull"
    }
    $backupRootPrefix = if ($backupRootFull.EndsWith('\')) { $backupRootFull } else { $backupRootFull + '\' }

    $targetPacks = [System.Collections.Generic.List[string]]::new()
    if ($null -eq $PackNames -or $PackNames.Count -eq 0) {
        $discovered = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($dir in Get-ChildItem -LiteralPath $backupRootFull -Directory -Force) {
            $candidateManifest = Join-Path $dir.FullName 'backup-manifest.json'
            if (Test-Path -LiteralPath $candidateManifest -PathType Leaf) {
                if (($dir.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Backup pack directory cannot be a reparse point: $($dir.FullName)"
                }
                [void]$discovered.Add($dir.Name)
            }
        }
        foreach ($name in $discovered) {
            [void]$targetPacks.Add($name)
        }
        if ($targetPacks.Count -eq 0) {
            throw "No pack backup manifests (backup-manifest.json) found under backup root: $backupRootFull"
        }
    } else {
        $seenInputPacks = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($rawPack in $PackNames) {
            $candidateName = if ($null -eq $rawPack) { '' } else { $rawPack.Trim() }
            if ($seenInputPacks.Add($candidateName)) {
                [void]$targetPacks.Add($candidateName)
            }
        }
    }

    $summaries = @(
        foreach ($packName in $targetPacks) {
            if ([string]::IsNullOrWhiteSpace($packName) -or $packName -notmatch '^[a-z0-9][a-z0-9-]*$') {
                throw "Invalid canonical visual pack name for restore: $packName"
            }

            $packBackupDir = [System.IO.Path]::GetFullPath((Join-Path $backupRootFull $packName))
            if (-not $packBackupDir.StartsWith($backupRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Backup pack directory escaped backup root for '$packName': $packBackupDir"
            }
            if (-not (Test-Path -LiteralPath $packBackupDir -PathType Container)) {
                throw "Backup pack directory is missing for restore: $packBackupDir"
            }
            $packBackupItem = Get-Item -LiteralPath $packBackupDir -Force
            if (($packBackupItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Backup pack directory cannot be a reparse point: $packBackupDir"
            }
            $packBackupPrefix = $packBackupDir.TrimEnd('\') + '\'

            $backupManifestPath = [System.IO.Path]::GetFullPath((Join-Path $packBackupDir 'backup-manifest.json'))
            if (-not (Test-Path -LiteralPath $backupManifestPath -PathType Leaf)) {
                throw "Backup manifest is missing for pack '$packName': $backupManifestPath"
            }
            $manifestItem = Get-Item -LiteralPath $backupManifestPath -Force
            if (($manifestItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Backup manifest cannot be a reparse point: $backupManifestPath"
            }

            $manifest = Get-Content -LiteralPath $backupManifestPath -Raw | ConvertFrom-Json
            if ($null -eq $manifest -or $manifest.schemaVersion -is [bool] -or $manifest.schemaVersion -ne 1) {
                throw "Unsupported backup manifest schemaVersion in '$backupManifestPath': $($manifest.schemaVersion)"
            }
            if ([string]$manifest.packName -cne $packName) {
                throw "Backup manifest packName mismatch in '$backupManifestPath': expected '$packName', got '$($manifest.packName)'"
            }
            if ($null -eq $manifest.bundles) {
                throw "Backup manifest is missing bundles array in '$backupManifestPath'"
            }

            $sourcePackDir = [System.IO.Path]::GetFullPath((Join-Path $root "content\packs\$packName"))
            $sourcePackPrefix = $sourcePackDir.TrimEnd('\') + '\'
            foreach ($dirToCheck in @((Join-Path $root 'content'), (Join-Path $root 'content\packs'), $sourcePackDir)) {
                if (Test-Path -LiteralPath $dirToCheck) {
                    $dirItem = Get-Item -LiteralPath $dirToCheck -Force
                    if (($dirItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                        throw "Destination pack directory cannot be a reparse point: $dirToCheck"
                    }
                }
            }

            $seenPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            $bundleResults = New-Object System.Collections.Generic.List[object]
            $restoredCount = 0
            $removedCount = 0

            foreach ($entry in @($manifest.bundles)) {
                if ($null -eq $entry) {
                    throw "Backup manifest contains null bundle entry in '$backupManifestPath'"
                }
                $relativePath = [string]$entry.path
                if ([string]::IsNullOrWhiteSpace($relativePath) -or
                    $relativePath -notmatch '^bundles/[a-z0-9][a-z0-9._-]*\.bundle$' -or
                    -not $seenPaths.Add($relativePath)) {
                    throw "Invalid or duplicate bundle path in backup manifest '$packName': $relativePath"
                }
                if ($null -eq $entry.existed -or ($entry.existed -isnot [bool])) {
                    throw "Backup manifest entry for '$packName/$relativePath' has invalid 'existed' flag."
                }

                $relativeWin = $relativePath.Replace('/', '\')
                $liveBundlePath = [System.IO.Path]::GetFullPath((Join-Path $sourcePackDir $relativeWin))
                if (-not $liveBundlePath.StartsWith($sourcePackPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                    throw "Destination bundle path escaped pack boundary during restore for '$packName': $relativePath"
                }

                $liveParent = Split-Path -Parent $liveBundlePath
                if (Test-Path -LiteralPath $liveParent) {
                    $liveParentItem = Get-Item -LiteralPath $liveParent -Force
                    if (($liveParentItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                        throw "Destination bundle directory cannot be a reparse point: $liveParent"
                    }
                }
                $existingLiveItem = Get-Item -LiteralPath $liveBundlePath -Force -ErrorAction SilentlyContinue
                if ($null -ne $existingLiveItem) {
                    if (($existingLiveItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                        throw "Destination bundle path cannot be a reparse point: $liveBundlePath"
                    }
                    if ($existingLiveItem.PSIsContainer) {
                        throw "Destination bundle path cannot be a directory: $liveBundlePath"
                    }
                }

                if ($entry.existed -eq $true) {
                    $expectedSha = [string]$entry.sha256
                    if ([string]::IsNullOrWhiteSpace($expectedSha) -or $expectedSha -notmatch '^[0-9a-fA-F]{64}$') {
                        throw "Backup manifest entry for '$packName/$relativePath' is missing a valid SHA-256."
                    }

                    $resolvedBackupPath = $null
                    $primaryBackupPath = [System.IO.Path]::GetFullPath((Join-Path $packBackupDir $relativeWin))
                    if (-not $primaryBackupPath.StartsWith($packBackupPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                        throw "Backup bundle path escaped pack backup boundary for '$packName': $relativePath"
                    }
                    $primaryExisting = Get-Item -LiteralPath $primaryBackupPath -Force -ErrorAction SilentlyContinue
                    if ($null -ne $primaryExisting) {
                        if (($primaryExisting.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                            throw "Backup bundle cannot be a reparse point: $primaryBackupPath"
                        }
                        if (-not $primaryExisting.PSIsContainer) {
                            $resolvedBackupPath = $primaryBackupPath
                        }
                    } elseif (-not [string]::IsNullOrWhiteSpace([string]$entry.backupPath)) {
                        $rawFallback = [string]$entry.backupPath
                        $fallbackCandidate = if ([System.IO.Path]::IsPathRooted($rawFallback)) {
                            [System.IO.Path]::GetFullPath($rawFallback)
                        } else {
                            [System.IO.Path]::GetFullPath((Join-Path $packBackupDir $rawFallback))
                        }
                        if (-not $fallbackCandidate.StartsWith($packBackupPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                            throw "Fallback backupPath escaped pack backup boundary for '$packName/$relativePath': $rawFallback"
                        }
                        $fallbackExisting = Get-Item -LiteralPath $fallbackCandidate -Force -ErrorAction SilentlyContinue
                        if ($null -ne $fallbackExisting) {
                            if (($fallbackExisting.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                                throw "Backup bundle cannot be a reparse point: $fallbackCandidate"
                            }
                            if (-not $fallbackExisting.PSIsContainer) {
                                $resolvedBackupPath = $fallbackCandidate
                            }
                        }
                    }

                    if ($null -eq $resolvedBackupPath) {
                        throw "Backup bundle file is missing for '$packName/$relativePath' under '$packBackupDir'."
                    }

                    $backupHash = (Get-FileHash -LiteralPath $resolvedBackupPath -Algorithm SHA256).Hash.ToLowerInvariant()
                    if (-not [string]::Equals($backupHash, $expectedSha, [System.StringComparison]::OrdinalIgnoreCase)) {
                        throw "Backup SHA-256 mismatch for '$packName/$relativePath' (expected $expectedSha, got $backupHash)"
                    }

                    New-Item -ItemType Directory -Path $liveParent -Force | Out-Null
                    Copy-Item -LiteralPath $resolvedBackupPath -Destination $liveBundlePath -Force
                    $postCopyHash = (Get-FileHash -LiteralPath $liveBundlePath -Algorithm SHA256).Hash.ToLowerInvariant()
                    if (-not [string]::Equals($postCopyHash, $expectedSha, [System.StringComparison]::OrdinalIgnoreCase)) {
                        throw "Post-copy SHA-256 verification failed during restore for '$packName/$relativePath' (expected $expectedSha, got $postCopyHash)"
                    }

                    $restoredCount++
                    [void]$bundleResults.Add([pscustomobject]@{
                        path = $relativePath
                        action = 'restored'
                        existed = $true
                        sha256 = $postCopyHash
                        backupPath = $resolvedBackupPath
                        destinationPath = $liveBundlePath
                    })
                } else {
                    if ($null -ne $existingLiveItem) {
                        Remove-Item -LiteralPath $liveBundlePath -Force
                        $removedCount++
                        [void]$bundleResults.Add([pscustomobject]@{
                            path = $relativePath
                            action = 'removed'
                            existed = $false
                            sha256 = $null
                            backupPath = $null
                            destinationPath = $liveBundlePath
                        })
                    } else {
                        [void]$bundleResults.Add([pscustomobject]@{
                            path = $relativePath
                            action = 'absent'
                            existed = $false
                            sha256 = $null
                            backupPath = $null
                            destinationPath = $liveBundlePath
                        })
                    }
                }
            }

            [pscustomobject]@{
                packName = $packName
                backupManifestPath = $backupManifestPath
                restoredCount = $restoredCount
                removedCount = $removedCount
                bundles = @($bundleResults.ToArray())
            }
        }
    )

    return $summaries
}
