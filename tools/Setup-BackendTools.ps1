[CmdletBinding()]
param(
    [switch]$RuntimeUnityEditor
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RuntimeUnityEditor) {
    Write-Host 'Select an optional backend diagnostic tool:'
    Write-Host '  .\tools\Setup-BackendTools.ps1 -RuntimeUnityEditor'
    exit 0
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $repositoryRoot '.tools'
$toolsRootFullPath = [System.IO.Path]::GetFullPath($toolsRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$downloadsRoot = Join-Path $toolsRoot 'downloads'
$archiveName = 'RuntimeUnityEditor.Bepin5_v6.3.2.1.zip'
$archivePath = Join-Path $downloadsRoot $archiveName
$destination = Join-Path $toolsRoot 'RuntimeUnityEditor-6.3.2.1'
$staging = Join-Path $toolsRoot '.RuntimeUnityEditor-6.3.2.1-staging'
$expectedSha256 = 'B0F0F0AC89E50D31E542FBCDD611A482CF8FEF7DC5ABDD12501611AEFA77E348'
$requiredFiles = @(
    'RuntimeUnityEditor.Bepin5.dll',
    'RuntimeUnityEditor.Core.dll',
    'README.md',
    'LICENSE',
    'mcs.dll'
)

New-Item -ItemType Directory -Path $downloadsRoot -Force | Out-Null
foreach ($directoryPath in @($toolsRoot, $downloadsRoot)) {
    $directoryItem = Get-Item -LiteralPath $directoryPath -Force
    if (($directoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to use reparse-point directory $directoryPath."
    }
}

if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
    Invoke-WebRequest `
        -Uri 'https://github.com/ManlyMarco/RuntimeUnityEditor/releases/download/v6.3.2.1/RuntimeUnityEditor.Bepin5_v6.3.2.1.zip' `
        -OutFile $archivePath `
        -UseBasicParsing
}

$archiveItem = Get-Item -LiteralPath $archivePath -Force
if (($archiveItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Refusing to hash or extract reparse-point archive $archivePath."
}

$actualSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
if (-not [string]::Equals($actualSha256, $expectedSha256, [StringComparison]::OrdinalIgnoreCase)) {
    throw "SHA-256 mismatch for $archivePath. Expected $expectedSha256, got $actualSha256. The archive was left in place for inspection."
}

if (Test-Path -LiteralPath $destination -PathType Container) {
    $missingFiles = @($requiredFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $destination $_) -PathType Leaf) })
    if ($missingFiles.Count -eq 0) {
        Write-Host "Already staged at $destination"
        exit 0
    }

    throw "The existing directory $destination is incomplete. It was left unchanged."
}

if (Test-Path -LiteralPath $staging) {
    throw "The staging path $staging already exists. It was left unchanged for inspection."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
$stagingFullPath = [System.IO.Path]::GetFullPath($staging).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$packagePrefix = 'BepInEx/plugins/RuntimeUnityEditor/'
try {
    foreach ($entry in $archive.Entries) {
        $relativePath = $entry.FullName.Replace('/', '\')
        $resolvedPath = [System.IO.Path]::GetFullPath((Join-Path $staging $relativePath))
        if (-not $resolvedPath.StartsWith($stagingFullPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe archive entry path: $($entry.FullName)"
        }
    }

    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    foreach ($entry in $archive.Entries) {
        if (-not $entry.FullName.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $relativePath = $entry.FullName.Substring($packagePrefix.Length).Replace('/', '\')
        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            continue
        }

        $outputPath = [System.IO.Path]::GetFullPath((Join-Path $staging $relativePath))
        if (-not $outputPath.StartsWith($stagingFullPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe package entry path: $($entry.FullName)"
        }

        if ($entry.FullName.EndsWith('/')) {
            New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
            continue
        }

        $parentDirectory = Split-Path -Parent $outputPath
        New-Item -ItemType Directory -Path $parentDirectory -Force | Out-Null
        $sourceStream = $entry.Open()
        $targetStream = [System.IO.File]::Create($outputPath)
        try {
            $sourceStream.CopyTo($targetStream)
        }
        finally {
            $targetStream.Dispose()
            $sourceStream.Dispose()
        }
    }

    $missingFiles = @($requiredFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $staging $_) -PathType Leaf) })
    if ($missingFiles.Count -gt 0) {
        throw "The verified archive is missing required files: $($missingFiles -join ', ')"
    }
}
finally {
    $archive.Dispose()
}

if (Test-Path -LiteralPath $destination) {
    throw "The destination $destination appeared during extraction. Files remain at $staging."
}

$stagingFullPath = [System.IO.Path]::GetFullPath($staging)
$destinationFullPath = [System.IO.Path]::GetFullPath($destination)
if (-not $stagingFullPath.StartsWith($toolsRootFullPath, [StringComparison]::OrdinalIgnoreCase) -or
    -not $destinationFullPath.StartsWith($toolsRootFullPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The resolved staging and destination paths must both stay under the repository .tools directory.'
}

Move-Item -LiteralPath $stagingFullPath -Destination $destinationFullPath
Write-Host "Staged Runtime Unity Editor 6.3.2.1 at $destination"
Write-Host 'This script does not copy files to the game, change game configuration, or launch Rogue Command.'
