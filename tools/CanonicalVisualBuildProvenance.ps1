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
        'tools/Build-CanonicalVisualModules.ps1',
        'tools/Build-CanonicalVisualModules.py',
        'tools/Build-CanonicalVisualModuleTextures.ps1',
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

    $files = @(
        foreach ($fullPath in $inputPaths) {
            if (-not $fullPath.StartsWith($root + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Canonical visual build input escaped the repository: $fullPath"
            }
            $item = Get-Item -LiteralPath $fullPath -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Canonical visual build input cannot be a reparse point: $fullPath"
            }
            $relativePath = $fullPath.Substring($root.Length + 1).Replace('\', '/')
            [pscustomobject]@{
                path = $relativePath
                sha256 = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
    ) | Sort-Object -Property path

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
