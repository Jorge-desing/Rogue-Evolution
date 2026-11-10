[CmdletBinding()]
param(
    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Rogue Command',
    [string]$BepInExCorePath = ''
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$canonicalArtifactRoot = Join-Path $repositoryRoot 'artifacts\canonical-two-level'
$canonicalValidatedPackRoot = Join-Path $canonicalArtifactRoot 'validation-packs'
. (Join-Path $PSScriptRoot 'CanonicalVisualBuildProvenance.ps1')
$projectPath = Join-Path $repositoryRoot 'src\RogueCommandExpansionLoader\RogueCommandExpansionLoader.csproj'
$pluginSourcePath = Join-Path $repositoryRoot 'src\RogueCommandExpansionLoader\RogueCommandExpansionPlugin.cs'
$pluginAssemblyPath = Join-Path $repositoryRoot 'artifacts\plugin\RogueCommandExpansionLoader.dll'
$schemaPath = Join-Path $repositoryRoot 'content\schema\content.schema.json'
$schemaReadmePath = Join-Path $repositoryRoot 'content\schema\README.md'
$nativeSamplePath = Join-Path $repositoryRoot 'content\packs\sample-native-expansion\content.json'
$reactiveAiSamplePath = Join-Path $repositoryRoot 'content\packs\sample-reactive-ai\content.json'

if ([string]::IsNullOrWhiteSpace($BepInExCorePath)) {
    $BepInExCorePath = Join-Path $repositoryRoot '.tools\bepinex-dist-5.4.23.5\BepInEx\core'
}

foreach ($pathValue in @($GameRoot, $BepInExCorePath)) {
    if ($pathValue.IndexOf(';') -ge 0 -or $pathValue.IndexOf("`r") -ge 0 -or
        $pathValue.IndexOf("`n") -ge 0) {
        throw 'GameRoot and BepInExCorePath cannot contain semicolons or line breaks when passed to MSBuild.'
    }
}

$pluginSource = Get-Content -LiteralPath $pluginSourcePath -Raw
$versionMatch = [regex]::Match($pluginSource, 'PluginVersion\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+)"')
if (-not $versionMatch.Success) {
    throw "Could not read PluginVersion from $pluginSourcePath."
}
$pluginVersion = $versionMatch.Groups[1].Value

$buildArguments = @(
    'build',
    $projectPath,
    '--configuration', 'Release',
    '--verbosity', 'minimal',
    "-p:GameRoot=$GameRoot",
    "-p:BepInExCorePath=$BepInExCorePath"
)

Write-Host 'Building Rogue Command Expansion Loader...'
& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $pluginAssemblyPath -PathType Leaf)) {
    throw "The plugin assembly was not produced: $pluginAssemblyPath"
}
if (-not (Test-Path -LiteralPath $schemaPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $schemaReadmePath -PathType Leaf)) {
    throw 'The content schema files are missing; refusing to create an incomplete package.'
}
if (-not (Test-Path -LiteralPath $nativeSamplePath -PathType Leaf)) {
    throw 'The native-template sample pack is missing; refusing to create an incomplete package.'
}
if (-not (Test-Path -LiteralPath $reactiveAiSamplePath -PathType Leaf)) {
    throw 'The reactive AI sample pack is missing; refusing to create an incomplete package.'
}

$nativeSampleRoot = Split-Path -Parent $nativeSamplePath
$nativeSampleBuildRoot = Join-Path $canonicalValidatedPackRoot 'sample-native-expansion'
$nativeSampleBuildContentPath = Join-Path $nativeSampleBuildRoot 'content.json'
$nativeSampleBundleDirectory = Join-Path $nativeSampleBuildRoot 'bundles'
$nativeSampleDefinition = Get-Content -LiteralPath $nativeSamplePath -Raw | ConvertFrom-Json
if ($nativeSampleDefinition.packId -ne 'sample-native:expansion' -or
    @($nativeSampleDefinition.assetBundles).Count -lt 1 -or
    [string]::IsNullOrWhiteSpace($nativeSampleDefinition.assetBundleManifestPath)) {
    throw 'The native sample pack must declare its bundle and Unity bundle manifest.'
}
if (-not (Test-Path -LiteralPath $nativeSampleBuildContentPath -PathType Leaf)) {
    throw "Fresh canonical pack output is missing. Run tools/Build-CanonicalVisualModules.ps1 first: $nativeSampleBuildContentPath"
}
Assert-CanonicalVisualBuildFreshness `
    -RepositoryRoot $repositoryRoot `
    -ArtifactRoot $canonicalArtifactRoot `
    -ValidatedPackRoot $canonicalValidatedPackRoot `
    -PackName 'sample-native-expansion'
if ((Get-FileHash -LiteralPath $nativeSamplePath -Algorithm SHA256).Hash -ne
    (Get-FileHash -LiteralPath $nativeSampleBuildContentPath -Algorithm SHA256).Hash) {
    throw 'The staged native sample content.json is stale. Run tools/Build-CanonicalVisualModules.ps1 after content changes.'
}

foreach ($inputDirectory in @($nativeSampleRoot, $nativeSampleBuildRoot, $nativeSampleBundleDirectory)) {
    if (-not (Test-Path -LiteralPath $inputDirectory -PathType Container)) {
        throw "A required native sample content directory is missing: $inputDirectory"
    }
    $inputDirectoryItem = Get-Item -LiteralPath $inputDirectory -Force
    if (($inputDirectoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to package native sample content through a reparse point: $inputDirectory"
    }
}

$reactiveAiSampleRoot = Split-Path -Parent $reactiveAiSamplePath
$reactiveAiSampleDefinition = Get-Content -LiteralPath $reactiveAiSamplePath -Raw | ConvertFrom-Json
if ($reactiveAiSampleDefinition.packId -ne 'sample-reactive-ai:expansion' -or
    @($reactiveAiSampleDefinition.assetBundles).Count -ne 0 -or
    -not [string]::IsNullOrWhiteSpace($reactiveAiSampleDefinition.assetBundleManifestPath)) {
    throw 'The reactive AI sample must be a data-only pack without an AssetBundle manifest.'
}
$reactiveAiSampleRootItem = Get-Item -LiteralPath $reactiveAiSampleRoot -Force
if (-not $reactiveAiSampleRootItem.PSIsContainer -or
    ($reactiveAiSampleRootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Refusing to package the reactive AI sample through a reparse point: $reactiveAiSampleRoot"
}

$nativeSampleBundlePaths = @($nativeSampleDefinition.assetBundleManifestPath)
$nativeSampleBundlePaths += @($nativeSampleDefinition.assetBundles | ForEach-Object { $_.path })
$nativeSampleBundleSources = @()
$nativeSampleBundlePathSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$nativeSampleBuildRootPrefix = [System.IO.Path]::GetFullPath($nativeSampleBuildRoot).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
foreach ($relativeBundlePath in $nativeSampleBundlePaths) {
    if ($relativeBundlePath -notmatch '^bundles/[a-z0-9][a-z0-9._-]*\.bundle$') {
        throw "The native sample contains an invalid bundle path: $relativeBundlePath"
    }
    if (-not $nativeSampleBundlePathSet.Add($relativeBundlePath)) {
        throw "The native sample declares a bundle path more than once: $relativeBundlePath"
    }

    $bundleSourcePath = [System.IO.Path]::GetFullPath((Join-Path $nativeSampleBuildRoot ($relativeBundlePath.Replace('/', '\'))))
    if (-not $bundleSourcePath.StartsWith($nativeSampleBuildRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to package a native sample bundle outside the canonical build output: $bundleSourcePath"
    }
    if (-not (Test-Path -LiteralPath $bundleSourcePath -PathType Leaf)) {
        throw "A bundle declared by the native sample is missing: $relativeBundlePath"
    }

    $bundleSourceItem = Get-Item -LiteralPath $bundleSourcePath -Force
    if (($bundleSourceItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to package a native sample bundle through a reparse point: $relativeBundlePath"
    }

    $maximumBytes = if ($relativeBundlePath -eq $nativeSampleDefinition.assetBundleManifestPath) {
        64MB
    } else {
        512MB
    }
    if ($bundleSourceItem.Length -le 0 -or $bundleSourceItem.Length -gt $maximumBytes) {
        throw "Native sample bundle is empty or exceeds its limit: $relativeBundlePath"
    }

    $nativeSampleBundleSources += [pscustomobject]@{
        RelativePath = $relativeBundlePath
        SourcePath = $bundleSourcePath
        FileName = [System.IO.Path]::GetFileName($bundleSourcePath)
    }
}

$packageRoot = Join-Path $repositoryRoot 'artifacts\packages'
$archivePath = Join-Path $packageRoot "RogueCommandExpansionLoader-$pluginVersion.zip"
$temporaryArchivePath = Join-Path $packageRoot (
    "RogueCommandExpansionLoader-$pluginVersion-" + [guid]::NewGuid().ToString('N') + '.tmp.zip')
$replacementBackupPath = Join-Path $packageRoot (
    "RogueCommandExpansionLoader-$pluginVersion-" + [guid]::NewGuid().ToString('N') + '.bak.zip')
$repositoryPrefix = $repositoryRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
$packageRootFullPath = [System.IO.Path]::GetFullPath($packageRoot)
if (-not $packageRootFullPath.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to write outside the repository: $packageRootFullPath"
}

$currentOutputPath = $repositoryRoot
$relativeOutputPath = $packageRootFullPath.Substring($repositoryPrefix.Length)
foreach ($segment in $relativeOutputPath.Split([System.IO.Path]::DirectorySeparatorChar)) {
    $currentOutputPath = Join-Path $currentOutputPath $segment
    if (Test-Path -LiteralPath $currentOutputPath) {
        $outputItem = Get-Item -LiteralPath $currentOutputPath -Force
        if (($outputItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to write through a junction or symbolic link: $currentOutputPath"
        }
        if (-not $outputItem.PSIsContainer) {
            throw "Expected an output directory but found a file: $currentOutputPath"
        }
    }
}

$temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$stagePath = [System.IO.Path]::GetFullPath((Join-Path $temporaryRoot (
    'rc-expansion-loader-package-' + [guid]::NewGuid().ToString('N'))))
$temporaryPrefix = $temporaryRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar

if (-not $stagePath.StartsWith($temporaryPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not [System.IO.Path]::GetFileName($stagePath).StartsWith('rc-expansion-loader-package-',
        [System.StringComparison]::Ordinal)) {
    throw "Refusing to use an unexpected temporary staging path: $stagePath"
}

$stageCreated = $false
$temporaryArchiveOwned = $false
try {
    if (Test-Path -LiteralPath $stagePath) {
        throw "Temporary staging path already exists; refusing to reuse it: $stagePath"
    }
    New-Item -ItemType Directory -Path $stagePath -ErrorAction Stop | Out-Null
    $stageCreated = $true
    $stageItem = Get-Item -LiteralPath $stagePath -Force
    if (-not $stageItem.PSIsContainer -or
        ($stageItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Temporary staging path is not a normal directory: $stagePath"
    }

    $pluginDirectory = Join-Path $stagePath 'BepInEx\plugins'
    $contentDirectory = Join-Path $pluginDirectory 'RogueCommandExpansionLoader\content'
    $packsDirectory = Join-Path $contentDirectory 'packs'
    $schemaDirectory = Join-Path $contentDirectory 'schema'
    $samplePackDirectory = Join-Path $packsDirectory 'sample-native-expansion'
    $samplePackBundleDirectory = Join-Path $samplePackDirectory 'bundles'
    $reactiveAiSampleDirectory = Join-Path $packsDirectory 'sample-reactive-ai'
    New-Item -ItemType Directory -Path $packsDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $schemaDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $samplePackDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $samplePackBundleDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $reactiveAiSampleDirectory -Force | Out-Null

    Copy-Item -LiteralPath $pluginAssemblyPath -Destination $pluginDirectory
    Copy-Item -LiteralPath $schemaPath -Destination $schemaDirectory
    Copy-Item -LiteralPath $schemaReadmePath -Destination $schemaDirectory
    Copy-Item -LiteralPath $nativeSampleBuildContentPath -Destination $samplePackDirectory
    Copy-Item -LiteralPath $reactiveAiSamplePath -Destination $reactiveAiSampleDirectory
    foreach ($bundleSource in $nativeSampleBundleSources) {
        Copy-Item -LiteralPath $bundleSource.SourcePath -Destination (Join-Path $samplePackBundleDirectory $bundleSource.FileName)
    }

    @'
Rogue Command Expansion Loader content packs

Place each content pack in its own folder under this directory. The included
sample-native-expansion includes a Unity AssetBundle and its manifest. The
sample-reactive-ai pack is data-only. Other packs must include every AssetBundle
declared by content.json and its manifest.
Use the adjacent schema directory when authoring content.json.

The native sample adds nine localized native-template units with script-free
visual overlays: Sandlance Scout, Colossus Siege Platform, Ironclad Walker,
Atlas Crane Mech, Bastion Siege Mech, Arachnid Hexapod, Overlord Command HQ,
Skirmish Strike Bike, and Surge Arc Tank. Its Level 2 variants preserve each
Level 1 chassis and can mount explicit visual-only modules at the ERA, APS,
Sponsons, and FuelDrums hardpoints; preset, tint, and scale alone do not add
geometry. Every unit declares its own new production building; random blueprint
rewards offer that building to produce its unit. Seven units have custom card
Sprites; the remaining unit cards keep their native artwork. All nine units
keep native gameplay controllers, while the pack adds a stage variant, a seeded
native landscape variant, and a scheduled-build AI module.
Each card's persistent profile-level requirement is configured on its unit and
factory rows. Reaching that level makes the card eligible for randomized reward
offers; it does not force the card into a particular offer.
The reactive AI stage references Sandlance and uses a native AI template with a
bounded compiled counterattack module. The landscape reuses native 27 Circles rules
with a fixed seed and is scoped to its stage; it does not add new terrain logic.
The game keeps a global blueprint eligibility flag on each custom factory row.
Stage reward pools are cloned and admit factories for products listed by that stage.
Rewards created without a pool ID use the game's global eligibility fallback.
'@ | Set-Content -LiteralPath (Join-Path $packsDirectory 'README.txt') -Encoding UTF8

    @"
Rogue Command Expansion Loader $pluginVersion

Install:
1. Install BepInEx 5.4.23.5 win-x64 Mono into the Rogue Command game folder.
2. Extract this archive into the Rogue Command game folder, preserving its BepInEx directory.
   3. The included sample-native-expansion pack adds nine localized native-template units
   with bundled model overlays and explicit Level 2 modules for the ERA, APS,
   Sponsons, and FuelDrums hardpoints. The modules preserve the Level 1 chassis and
   add visual equipment only: Sandlance Scout, Colossus Siege Platform, Ironclad
   Walker, Atlas Crane Mech, Bastion Siege Mech, Arachnid Hexapod, Overlord Command
   HQ, Skirmish Strike Bike, and Surge Arc Tank. Each unit has a separately registered
   production building with its own ID, native factory prefab, and localized name.
   Persistent profile-level requirements are configured on each unit and factory
   row; reaching a threshold makes a card eligible for randomized offers but does
   not guarantee a draw.
   Random rewards offer the new building blueprint; that building produces its unit.
   Seven units have custom card Sprites, while the two newest units use native card
   artwork. The pack also adds a native stage variant, a fixed-seed landscape variant,
   and a scheduled-build AI module; every unit keeps its native gameplay controller.
   The sample-reactive-ai pack demonstrates a native stage variant that references
   the Sandlance Scout from sample-native-expansion and an AI module that sends up
   to two nearby units to counterattack an active attacker. Stage reward pools are
   scoped to factories for their declared products; rewards without a pool ID use
   the global eligibility fallback.
4. Add other packs under BepInEx/plugins/RogueCommandExpansionLoader/content/packs/;
   custom models and original terrain-generation assets require valid Unity AssetBundles.

Compatibility is limited to Rogue Command 1.0.9, Unity 2021.3.45f2, and the verified
Assembly-CSharp.dll build. The plugin logs a compatibility diagnostic and skips its
Harmony patches when those checks fail.

This package contains the loader, content schema, one sample pack with a script-free
Unity AssetBundle, and one data-only reactive-AI sample pack.
It does not include the game, BepInEx, Harmony, or game assemblies.
"@ | Set-Content -LiteralPath (Join-Path $stagePath 'INSTALL.txt') -Encoding UTF8

    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    if (Test-Path -LiteralPath $temporaryArchivePath) {
        throw "Temporary archive path already exists; refusing to reuse it: $temporaryArchivePath"
    }
    Add-Type -AssemblyName System.IO.Compression
    $archiveStream = [System.IO.File]::Open(
        $temporaryArchivePath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None)
    $temporaryArchiveOwned = $true
    $zipArchive = $null
    try {
        $zipArchive = [System.IO.Compression.ZipArchive]::new(
            $archiveStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $true)

        $stagePrefix = [System.IO.Path]::GetFullPath($stagePath).TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        $stagedFiles = @(Get-ChildItem -LiteralPath $stagePath -File -Recurse -Force | Sort-Object -Property FullName)
        if ($stagedFiles.Count -eq 0) {
            throw "Refusing to create an empty install archive from $stagePath."
        }

        foreach ($stagedFile in $stagedFiles) {
            if (($stagedFile.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing to package a reparse-point file: $($stagedFile.FullName)"
            }

            $stagedFilePath = [System.IO.Path]::GetFullPath($stagedFile.FullName)
            if (-not $stagedFilePath.StartsWith($stagePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to package a file outside the staging directory: $stagedFilePath"
            }

            $entryName = $stagedFilePath.Substring($stagePrefix.Length).Replace('\', '/')
            $entrySegments = $entryName.Split('/')
            if ([System.IO.Path]::IsPathRooted($entryName) -or $entrySegments -contains '' -or
                $entrySegments -contains '.' -or $entrySegments -contains '..') {
                throw "Refusing an invalid ZIP entry path: $entryName"
            }

            $entry = $zipArchive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
            $inputStream = [System.IO.File]::OpenRead($stagedFilePath)
            $entryStream = $null
            try {
                $entryStream = $entry.Open()
                $inputStream.CopyTo($entryStream)
            }
            finally {
                if ($entryStream) {
                    $entryStream.Dispose()
                }
                $inputStream.Dispose()
            }
        }
    }
    finally {
        if ($zipArchive) {
            $zipArchive.Dispose()
        }
        if ($archiveStream) {
            $archiveStream.Dispose()
        }
    }

    if (Test-Path -LiteralPath $archivePath) {
        $existingArchive = Get-Item -LiteralPath $archivePath -Force
        if ($existingArchive.PSIsContainer -or
            ($existingArchive.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to replace a directory, junction, or symbolic link: $archivePath"
        }
        if (Test-Path -LiteralPath $replacementBackupPath) {
            throw "Replacement backup path already exists; refusing to reuse it: $replacementBackupPath"
        }
        [System.IO.File]::Replace($temporaryArchivePath, $archivePath, $replacementBackupPath)
        $backupArchive = Get-Item -LiteralPath $replacementBackupPath -Force
        if ($backupArchive.PSIsContainer -or
            ($backupArchive.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            Write-Warning "Package replaced, but the previous archive backup needs manual inspection: $replacementBackupPath"
        }
        else {
            try {
                Remove-Item -LiteralPath $replacementBackupPath -Force -ErrorAction Stop
            }
            catch {
                Write-Warning "Package replaced, but the previous archive backup could not be removed: $replacementBackupPath"
            }
        }
    }
    else {
        [System.IO.File]::Move($temporaryArchivePath, $archivePath)
    }
    $temporaryArchiveOwned = $false
    Write-Host "Package created: $archivePath"
}
finally {
    if ($temporaryArchiveOwned) {
        try {
            if (Test-Path -LiteralPath $temporaryArchivePath) {
                $temporaryArchiveItem = Get-Item -LiteralPath $temporaryArchivePath -Force
                if ($temporaryArchiveItem.PSIsContainer -or
                    ($temporaryArchiveItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Refusing to remove a replaced temporary archive directory or link: $temporaryArchivePath"
                }
                Remove-Item -LiteralPath $temporaryArchivePath -Force -ErrorAction Stop
            }
        }
        catch {
            Write-Warning "Could not safely clean the temporary archive '$temporaryArchivePath': $($_.Exception.Message)"
        }
    }

    if ($stageCreated) {
        try {
            if (Test-Path -LiteralPath $stagePath) {
                $stageItem = Get-Item -LiteralPath $stagePath -Force
                if (($stageItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Refusing recursive cleanup of a replaced staging junction or symbolic link: $stagePath"
                }
                Remove-Item -LiteralPath $stagePath -Recurse -Force -ErrorAction Stop
            }
        }
        catch {
            Write-Warning "Could not safely clean the package staging directory '$stagePath': $($_.Exception.Message)"
        }
    }
}
