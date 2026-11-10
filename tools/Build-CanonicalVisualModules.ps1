$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "unity/RogueCommandAssetAuthoring"
$unityExe = Join-Path $env:ProgramFiles "Unity/Hub/Editor/2021.3.45f2/Editor/Unity.exe"
$blenderExe = Join-Path $repoRoot ".tools/Blender-4.5.14/blender-4.5.14-windows-x64/blender.exe"
$geometryScript = Join-Path $repoRoot "tools/Build-CanonicalVisualModules.py"
$textureScript = Join-Path $repoRoot "tools/Build-CanonicalVisualModuleTextures.ps1"
$logDirectory = Join-Path $repoRoot "artifacts/canonical-two-level"
$geometryLogPath = Join-Path $logDirectory "blender-builder.log"
$logPath = Join-Path $logDirectory "unity-builder.log"
$lockPath = Join-Path $projectPath "Temp/UnityLockfile"
$provenanceScript = Join-Path $PSScriptRoot "CanonicalVisualBuildProvenance.ps1"
. $provenanceScript

if (-not (Test-Path -LiteralPath $unityExe -PathType Leaf)) {
    throw "Pinned Unity 2021.3.45f2 editor was not found: $unityExe"
}
if (-not (Test-Path -LiteralPath $blenderExe -PathType Leaf)) {
    throw "Pinned Blender 4.5.14 executable was not found: $blenderExe"
}
if (-not (Test-Path -LiteralPath $projectPath -PathType Container)) {
    throw "RogueCommandAssetAuthoring project is missing: $projectPath"
}
if (Test-Path -LiteralPath $lockPath) {
    throw "The authoring project has a Unity lock file. Close its current editor before the batch build: $lockPath"
}
if (Get-Process -Name blender -ErrorAction SilentlyContinue) {
    throw "Close Blender before regenerating the canonical module source files."
}

$projectVersion = Get-Content -LiteralPath (Join-Path $projectPath "ProjectSettings/ProjectVersion.txt") -Raw
if ($projectVersion -notmatch "(?m)^m_EditorVersion: 2021\.3\.45f2\s*$") {
    throw "Authoring project is not pinned to Unity 2021.3.45f2."
}

New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

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
if ((Get-Content -LiteralPath $geometryLogPath -Raw) -notmatch "Saved 35 modules, source blend, and manifest:") {
    throw "Blender exited without the canonical 35-module completion record. Log: $geometryLogPath"
}

$moduleManifestPath = Join-Path $repoRoot "content/source-art/canonical-visual-modules.json"
$moduleManifest = Get-Content -LiteralPath $moduleManifestPath -Raw | ConvertFrom-Json
if (@($moduleManifest.modules).Count -ne 35) {
    throw "Canonical Blender manifest must contain exactly 35 module entries: $moduleManifestPath"
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

Write-Host "Generating the 28 canonical four-map PBR sets with Material Maker 1.7."
& $textureScript -SizePower 11
$textureDirectory = Join-Path $projectPath "Assets/RogueCommandExpansionSample/VisualModules/Textures"
$presets = @($moduleManifest.modules | ForEach-Object { [string]$_.preset } | Sort-Object -Unique)
if ($presets.Count -ne 28) {
    throw "Canonical module manifest must use 28 unique PBR presets; found $($presets.Count)."
}
foreach ($preset in $presets) {
    $texturePrefix = "RCLModule_$preset"
    foreach ($suffix in @("Albedo", "MetallicGloss", "Normal", "Emission")) {
        $texturePath = Join-Path $textureDirectory "${texturePrefix}_${suffix}.png"
        if (-not (Test-Path -LiteralPath $texturePath -PathType Leaf) -or
            (Get-Item -LiteralPath $texturePath).Length -le 0) {
            throw "Material Maker output is missing or empty: $texturePath"
        }
    }
}

$unityArguments = @(
    "-batchmode",
    "-quit",
    "-projectPath",
    $projectPath,
    "-executeMethod",
    "RogueCommandExpansion.Authoring.CanonicalVisualModuleBuilder.BuildCanonicalModules",
    "-logFile",
    $logPath
)
$argumentText = ($unityArguments | ForEach-Object { '"' + ([string]$_).Replace('"', '\"') + '"' }) -join ' '
$editor = Start-Process -FilePath $unityExe -ArgumentList $argumentText -WindowStyle Hidden -PassThru
if ($null -eq $editor) {
    throw "Could not start the pinned Unity editor: $unityExe"
}
while (-not $editor.WaitForExit(15000)) {
    $editor.Refresh()
    if (Test-Path -LiteralPath $logPath) {
        $lastLogLine = Get-Content -LiteralPath $logPath -Tail 1
        if (-not [String]::IsNullOrWhiteSpace($lastLogLine)) {
            Write-Host $lastLogLine
        }
    }
}
$editor.Refresh()
$unityExitCode = $editor.ExitCode
if ($unityExitCode -ne 0) {
    throw "Unity canonical visual module build failed with exit code $unityExitCode. Log: $logPath"
}
if (Test-Path -LiteralPath $lockPath) {
    throw "Unity returned but left a project lock file; inspect $lockPath before reopening the authoring project."
}

$log = Get-Content -LiteralPath $logPath -Raw
if ($log -notmatch "Built 35 visual-only modules, 28 shared PBR sets, labeled .* referenced pack assets, and built both content packs\.") {
    throw "Unity exited without the canonical module build completion record. Log: $logPath"
}

$artifactRoot = Join-Path $repoRoot "artifacts/canonical-two-level"
$builtPackRoot = Join-Path $artifactRoot "content-packs"
$validatedPackRoot = Join-Path $artifactRoot "validation-packs"
$pythonExe = Join-Path $repoRoot ".venv/Scripts/python.exe"
if (-not (Test-Path -LiteralPath $pythonExe -PathType Leaf)) {
    throw "The repository Python environment is required for pack validation: $pythonExe"
}
foreach ($packName in @("sample-native-expansion", "tactical-operations")) {
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

$packNames = @("sample-native-expansion", "tactical-operations")
$null = New-CanonicalVisualBuildManifest `
    -RepositoryRoot $repoRoot `
    -ArtifactRoot $artifactRoot `
    -ValidatedPackRoot $validatedPackRoot `
    -PackNames $packNames
foreach ($packName in $packNames) {
    Assert-CanonicalVisualBuildFreshness `
        -RepositoryRoot $repoRoot `
        -ArtifactRoot $artifactRoot `
        -ValidatedPackRoot $validatedPackRoot `
        -PackName $packName
}

Write-Host "Blender 4.5.14, Material Maker 1.7, and Unity 2021.3.45f2 rebuilt, staged, and validated 35 canonical visual modules in both content packs. Logs: $geometryLogPath ; $logPath"
