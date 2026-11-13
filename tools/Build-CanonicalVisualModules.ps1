$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "unity/RogueCommandAssetAuthoring"
$unityExe = Join-Path $env:ProgramFiles "Unity/Hub/Editor/2021.3.45f2/Editor/Unity.exe"
$blenderExe = Join-Path $repoRoot ".tools/Blender-4.5.14/blender-4.5.14-windows-x64/blender.exe"
$geometryScript = Join-Path $repoRoot "tools/Build-CanonicalVisualModules.py"
$textureScript = Join-Path $repoRoot "tools/Build-CanonicalVisualModuleTextures.ps1"
$buildStamp = [DateTime]::UtcNow.ToString("yyyyMMddTHHmmssZ")
$artifactRoot = Join-Path $repoRoot "artifacts/canonical-level2-$buildStamp"
$logDirectory = Join-Path $artifactRoot "logs"
$geometryLogPath = Join-Path $logDirectory "blender-builder.log"
$levelOneLogPath = Join-Path $logDirectory "level-one-unity-builder.log"
$logPath = Join-Path $logDirectory "unity-builder.log"
$lockPath = Join-Path $projectPath "Temp/UnityLockfile"
$provenanceScript = Join-Path $PSScriptRoot "CanonicalVisualBuildProvenance.ps1"
. $provenanceScript

if (Test-Path -LiteralPath $artifactRoot) {
    throw "Canonical Level 2 build output already exists; refusing to overwrite it: $artifactRoot"
}
$env:RCL_CANONICAL_VISUAL_ARTIFACT_ROOT = $artifactRoot

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
$geometryLog = Get-Content -LiteralPath $geometryLogPath -Raw
$geometryCompletion = [System.Text.RegularExpressions.Regex]::Match(
    $geometryLog,
    "Saved (?<count>\d+) modules, source blend, and manifest:"
)
if (-not $geometryCompletion.Success) {
    throw "Blender exited without the canonical module completion record. Log: $geometryLogPath"
}

$moduleManifestPath = Join-Path $repoRoot "content/source-art/canonical-visual-modules.json"
$moduleManifest = Get-Content -LiteralPath $moduleManifestPath -Raw | ConvertFrom-Json
$moduleCount = @($moduleManifest.modules).Count
if ($moduleCount -le 0 -or $moduleCount -ne [int]$geometryCompletion.Groups["count"].Value) {
    throw "Canonical Blender manifest count does not match its completion record: $moduleManifestPath"
}
$expectedModuleCount = 0
foreach ($packName in @("sample-native-expansion", "tactical-operations")) {
    $packDefinition = Get-Content -LiteralPath (Join-Path $repoRoot "content/packs/$packName/content.json") -Raw | ConvertFrom-Json
    foreach ($unit in @($packDefinition.units)) {
        $variants = @($unit.visualVariants)
        for ($variantIndex = 1; $variantIndex -lt $variants.Count; $variantIndex++) {
            if ($null -ne $variants[$variantIndex].modules) {
                $expectedModuleCount += @($variants[$variantIndex].modules).Count
            }
        }
    }
}
if ($moduleCount -ne $expectedModuleCount) {
    throw "Canonical Blender manifest has $moduleCount modules, but active Level 2 declarations contain $expectedModuleCount."
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
if ($presets.Count -le 0) {
    throw "Canonical module manifest has no shared PBR presets."
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

Write-Host "Refreshing the Level 1 prefabs and their canonical hardpoint hierarchy."
$levelOneUnityArguments = @(
    "-batchmode",
    "-quit",
    "-projectPath",
    $projectPath,
    "-executeMethod",
    "RogueCommandExpansion.Authoring.RogueCommandVisualSampleScaffolder.RefreshCanonicalLevelOneVisuals",
    "-logFile",
    $levelOneLogPath
)
$levelOneArgumentText = ($levelOneUnityArguments | ForEach-Object { '"' + ([string]$_).Replace('"', '\"') + '"' }) -join ' '
$levelOneEditor = Start-Process -FilePath $unityExe -ArgumentList $levelOneArgumentText -WindowStyle Hidden -PassThru
if ($null -eq $levelOneEditor) {
    throw "Could not start Unity to refresh the Level 1 visuals: $unityExe"
}
while (-not $levelOneEditor.WaitForExit(15000)) {
    $levelOneEditor.Refresh()
    if (Test-Path -LiteralPath $levelOneLogPath) {
        $lastLevelOneLogLine = Get-Content -LiteralPath $levelOneLogPath -Tail 1
        if (-not [String]::IsNullOrWhiteSpace($lastLevelOneLogLine)) {
            Write-Host $lastLevelOneLogLine
        }
    }
}
$levelOneEditor.Refresh()
if ($levelOneEditor.ExitCode -ne 0) {
    throw "Unity Level 1 visual refresh failed with exit code $($levelOneEditor.ExitCode). Log: $levelOneLogPath"
}
if (Test-Path -LiteralPath $lockPath) {
    throw "Unity returned but left a project lock file after refreshing Level 1 visuals; inspect $lockPath."
}
$levelOneLog = Get-Content -LiteralPath $levelOneLogPath -Raw
if ($levelOneLog -notmatch "Refreshed 4 Level 1 prefab hierarchies and validated four unique canonical hardpoints on each; existing animator and visual hierarchies were preserved\.") {
    throw "Unity exited without validating all four Level 1 prefabs and their hardpoints. Log: $levelOneLogPath"
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
$unityCompletion = [System.Text.RegularExpressions.Regex]::Match(
    $log,
    "Built (?<modules>\d+) visual-only modules, (?<presets>\d+) shared PBR sets, validated (?<hardpoints>\d+) Level 1 hardpoint bindings, labeled .* referenced pack assets, and built both content packs\."
)
if (-not $unityCompletion.Success -or
    [int]$unityCompletion.Groups["modules"].Value -ne $moduleCount -or
    [int]$unityCompletion.Groups["presets"].Value -ne $presets.Count -or
    [int]$unityCompletion.Groups["hardpoints"].Value -ne $moduleCount) {
    throw "Unity exited without the expected Level 1/Level 2 canonical module build record. Log: $logPath"
}

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

Write-Host "Blender 4.5.14, Material Maker 1.7, and Unity 2021.3.45f2 rebuilt, staged, and validated $moduleCount canonical visual modules against their Level 1 hardpoints in both content packs. Logs: $geometryLogPath ; $logPath"
