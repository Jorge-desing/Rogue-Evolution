[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $repositoryRoot '.tools'
$downloadsRoot = Join-Path $toolsRoot 'downloads'
$patchRoot = Join-Path $toolsRoot 'MaterialMaker-1.7-patched'
$runtimeRoot = Join-Path $patchRoot 'runtime'
$materialMakerInstall = Join-Path $toolsRoot 'MaterialMaker-1.7\material_maker_1_7_windows'
$materialMakerExecutable = Join-Path $materialMakerInstall 'material_maker.exe'
$logStamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$buildLogRoot = Join-Path $repositoryRoot "artifacts/mm-warning-fix/build-$logStamp"
$stageRoot = Join-Path $toolsRoot ('.stage-material-maker-cli-' + [guid]::NewGuid().ToString('N'))
$locationPushed = $false

New-Item -ItemType Directory -Path $downloadsRoot, $buildLogRoot, $stageRoot -Force | Out-Null
. (Join-Path $PSScriptRoot 'Setup-ArchiveSupport.ps1')

function Invoke-MaterialMakerBuildProcess {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$LogName
    )

    $stdoutPath = Join-Path $buildLogRoot "$LogName.stdout.log"
    $stderrPath = Join-Path $buildLogRoot "$LogName.stderr.log"
    $process = Start-Process `
        -FilePath $Executable `
        -ArgumentList $Arguments `
        -WorkingDirectory $WorkingDirectory `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -Wait `
        -PassThru
    if ($process.ExitCode -ne 0) {
        throw "$LogName exited with code $($process.ExitCode). Review $stdoutPath and $stderrPath."
    }

    $combined = @()
    if (Test-Path -LiteralPath $stdoutPath) { $combined += Get-Content -LiteralPath $stdoutPath }
    if (Test-Path -LiteralPath $stderrPath) { $combined += Get-Content -LiteralPath $stderrPath }
    $fatalDiagnostics = $combined | Select-String -Pattern '^(SCRIPT ERROR:|ERROR: Error loading GDExtension|ERROR: GDExtension dynamic library not found|ERROR: Failed to load script|ERROR: Failed to load resource|.*Parse Error:|.*Compile Error:)'
    if ($fatalDiagnostics) {
        throw "$LogName reported source or extension errors. Review $stdoutPath and $stderrPath."
    }
}

try {
    if (-not (Test-Path -LiteralPath $materialMakerExecutable -PathType Leaf)) {
        throw 'Stage Material Maker 1.7 first with tools/Setup-TextureAuthoringTools.ps1 -MaterialMaker.'
    }

    $godotArchive = Get-VerifiedDownload `
        -Uri 'https://github.com/godotengine/godot/releases/download/4.7-stable/Godot_v4.7-stable_win64.exe.zip' `
        -FileName 'Godot_v4.7-stable_win64.exe.zip' `
        -ExpectedSha256 '02A5312236F4E0209C78BCB2F52135B1963E6B8888C873C9CEE81459E60BCD71'
    $materialMakerSourceArchive = Get-VerifiedDownload `
        -Uri 'https://github.com/RodZill4/material-maker/archive/refs/tags/1.7.tar.gz' `
        -FileName 'material-maker-1.7-source.tar.gz' `
        -ExpectedSha256 '6C662AE5DEDAEE7EB33736D6D8B094F9FB65297D57FEF4F5991FBF9504C0A3CA'

    $godotStage = Join-Path $stageRoot 'godot'
    Expand-SafeArchive -ArchivePath $godotArchive -DestinationPath $godotStage -RequiredRelativePaths @(
        'Godot_v4.7-stable_win64_console.exe',
        'Godot_v4.7-stable_win64.exe'
    )
    $godotConsole = Join-Path $godotStage 'Godot_v4.7-stable_win64_console.exe'
    $godotGui = Join-Path $godotStage 'Godot_v4.7-stable_win64.exe'
    foreach ($godotExecutable in @($godotConsole, $godotGui)) {
        $version = (Get-Item -LiteralPath $godotExecutable).VersionInfo.ProductVersion
        if ($version -notmatch '^4\.7\.stable\.official') {
            throw "Unexpected Godot version at ${godotExecutable}: $version"
        }
    }

    $sourceRoot = Join-Path $stageRoot 'source'
    New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null
    & tar.exe -xzf $materialMakerSourceArchive -C $sourceRoot
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not extract the verified Material Maker 1.7 source archive.'
    }
    $sourceProject = Join-Path $sourceRoot 'material-maker-1.7'
    if (-not (Test-Path -LiteralPath (Join-Path $sourceProject 'project.godot') -PathType Leaf)) {
        throw 'The verified Material Maker source archive has an unexpected layout.'
    }
    Push-Location $repositoryRoot
    $locationPushed = $true
    try {
        & python -m tools.patch_material_maker_cli_shutdown --source $sourceProject
        if ($LASTEXITCODE -ne 0) {
            throw 'The pinned Material Maker CLI shutdown patch did not apply.'
        }
    } finally {
        Pop-Location
        $locationPushed = $false
    }

    $godotConsoleVersion = (Get-Item -LiteralPath $godotConsole).VersionInfo.ProductVersion
    $godotGuiVersion = (Get-Item -LiteralPath $godotGui).VersionInfo.ProductVersion
    if ($godotConsoleVersion -notmatch '^4\.7\.stable\.official' -or $godotGuiVersion -notmatch '^4\.7\.stable\.official') {
        throw 'Godot runtime executables must both be the pinned 4.7 stable build.'
    }

    # Keep the source import isolated from an interactive desktop editor session.
    # The actual Material Maker CLI export is separately exercised with its Vulkan renderer.
    $importArguments = "--headless --editor --path `"$sourceProject`" --quit"
    Invoke-MaterialMakerBuildProcess -Executable $godotConsole -Arguments $importArguments -WorkingDirectory $sourceProject -LogName 'godot-import'

    $stagedPack = Join-Path $stageRoot 'material_maker.pck'
    $packArguments = "--headless --path `"$sourceProject`" --export-pack `"Windows Steam`" `"$stagedPack`""
    Invoke-MaterialMakerBuildProcess -Executable $godotConsole -Arguments $packArguments -WorkingDirectory $sourceProject -LogName 'godot-pack'
    if (-not (Test-Path -LiteralPath $stagedPack -PathType Leaf) -or (Get-Item -LiteralPath $stagedPack).Length -lt 1MB) {
        throw 'Godot did not produce a complete Material Maker PCK.'
    }

    $runningRuntime = Get-CimInstance Win32_Process | Where-Object {
        $_.ExecutablePath -in @((Join-Path $runtimeRoot 'Godot_v4.7-stable_win64_console.exe'), (Join-Path $runtimeRoot 'Godot_v4.7-stable_win64.exe'))
    }
    if ($runningRuntime) {
        $processIds = ($runningRuntime | ForEach-Object { $_.ProcessId }) -join ', '
        throw "Close the local patched Material Maker process before replacing its runtime (PID $processIds)."
    }

    New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
    Copy-Item -LiteralPath $godotConsole -Destination (Join-Path $runtimeRoot 'Godot_v4.7-stable_win64_console.exe') -Force
    Copy-Item -LiteralPath $godotGui -Destination (Join-Path $runtimeRoot 'Godot_v4.7-stable_win64.exe') -Force

    foreach ($runtimeFolder in @('doc', 'environments', 'examples', 'export', 'library', 'meshes', 'nodes')) {
        $sourceFolder = Join-Path $materialMakerInstall $runtimeFolder
        if (-not (Test-Path -LiteralPath $sourceFolder -PathType Container)) {
            throw "The official Material Maker install is missing required runtime data: $sourceFolder"
        }
        $runtimeFolderPath = Join-Path $runtimeRoot $runtimeFolder
        New-Item -ItemType Directory -Path $runtimeFolderPath -Force | Out-Null
        Get-ChildItem -LiteralPath $sourceFolder -Force | Copy-Item -Destination $runtimeFolderPath -Recurse -Force
    }

    $runtimePack = Join-Path $runtimeRoot 'material_maker.pck'
    $candidatePack = Join-Path $runtimeRoot ('material_maker.' + [guid]::NewGuid().ToString('N') + '.pck')
    Copy-Item -LiteralPath $stagedPack -Destination $candidatePack
    $candidateHash = (Get-FileHash -LiteralPath $candidatePack -Algorithm SHA256).Hash
    $stagedHash = (Get-FileHash -LiteralPath $stagedPack -Algorithm SHA256).Hash
    if ($candidateHash -ne $stagedHash) {
        throw 'The candidate Material Maker PCK differs from the verified build output.'
    }
    $previousPackBackup = ''
    if (Test-Path -LiteralPath $runtimePack -PathType Leaf) {
        $previousPackBackup = Join-Path $buildLogRoot 'material_maker.before.pck'
        [System.IO.File]::Replace($candidatePack, $runtimePack, $previousPackBackup)
    } else {
        Move-Item -LiteralPath $candidatePack -Destination $runtimePack
    }

    $manifest = [ordered]@{
        kind = 'materialMakerCliBuild'
        materialMakerVersion = '1.7'
        godotVersion = $godotConsoleVersion
        sourceArchiveSha256 = (Get-FileHash -LiteralPath $materialMakerSourceArchive -Algorithm SHA256).Hash
        godotArchiveSha256 = (Get-FileHash -LiteralPath $godotArchive -Algorithm SHA256).Hash
        sourcePatcherSha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'patch_material_maker_cli_shutdown.py') -Algorithm SHA256).Hash
        pckSha256 = (Get-FileHash -LiteralPath $runtimePack -Algorithm SHA256).Hash
        runtimeDirectory = $runtimeRoot
        pck = $runtimePack
        previousPckBackup = $previousPackBackup
        importStdout = Join-Path $buildLogRoot 'godot-import.stdout.log'
        importStderr = Join-Path $buildLogRoot 'godot-import.stderr.log'
        packStdout = Join-Path $buildLogRoot 'godot-pack.stdout.log'
        packStderr = Join-Path $buildLogRoot 'godot-pack.stderr.log'
    }
    $manifestPath = Join-Path $buildLogRoot 'build-manifest.json'
    [System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Built local Material Maker 1.7 CLI runtime at $runtimeRoot"
    Write-Host "Build manifest: $manifestPath"
    Get-FileHash -LiteralPath $runtimePack -Algorithm SHA256
} finally {
    if ($locationPushed) {
        Pop-Location
    }
    $stageFullPath = [System.IO.Path]::GetFullPath($stageRoot)
    $toolsFullPath = [System.IO.Path]::GetFullPath($toolsRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if ($stageFullPath.StartsWith($toolsFullPath, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $stageFullPath -PathType Container)) {
        Remove-Item -LiteralPath $stageFullPath -Recurse -Force
    }
}
