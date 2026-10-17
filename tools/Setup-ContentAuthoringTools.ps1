[CmdletBinding()]
param(
    [switch]$Blender,
    [switch]$UnityExplorer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $Blender -and -not $UnityExplorer) {
    Write-Host 'Select one or both optional tools:'
    Write-Host '  .\tools\Setup-ContentAuthoringTools.ps1 -Blender -UnityExplorer'
    exit 0
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $repositoryRoot '.tools'
$downloadsRoot = Join-Path $toolsRoot 'downloads'
New-Item -ItemType Directory -Path $downloadsRoot -Force | Out-Null

function Get-VerifiedArchive {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $true)][string]$ExpectedSha256
    )

    $archivePath = Join-Path $downloadsRoot $FileName
    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
        Write-Host "Downloading $FileName"
        Invoke-WebRequest -Uri $Uri -OutFile $archivePath -UseBasicParsing
    }

    $actualSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if (-not [string]::Equals($actualSha256, $ExpectedSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "SHA-256 mismatch for $archivePath. Expected $ExpectedSha256, got $actualSha256. The archive was left in place for inspection."
    }

    return $archivePath
}

function Expand-VerifiedArchive {
    param(
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][string[]]$RequiredRelativePaths
    )

    $destinationFullPath = [System.IO.Path]::GetFullPath($DestinationPath).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (Test-Path -LiteralPath $DestinationPath -PathType Container) {
        $missingFiles = @($RequiredRelativePaths | Where-Object { -not (Test-Path -LiteralPath (Join-Path $DestinationPath $_) -PathType Leaf) })
        if ($missingFiles.Count -eq 0) {
            Write-Host "Already staged at $DestinationPath"
            return
        }

        $existingItems = @(Get-ChildItem -LiteralPath $DestinationPath -Force)
        if ($existingItems.Count -gt 0) {
            throw "The existing directory $DestinationPath is incomplete. It was left unchanged."
        }
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entry in $archive.Entries) {
            $relativePath = $entry.FullName.Replace('/', '\')
            $resolvedPath = [System.IO.Path]::GetFullPath((Join-Path $DestinationPath $relativePath))
            if (-not $resolvedPath.StartsWith($destinationFullPath, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Unsafe archive entry path: $($entry.FullName)"
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
    [System.IO.Compression.ZipFile]::ExtractToDirectory($ArchivePath, $DestinationPath)

    $missingFiles = @($RequiredRelativePaths | Where-Object { -not (Test-Path -LiteralPath (Join-Path $DestinationPath $_) -PathType Leaf) })
    if ($missingFiles.Count -gt 0) {
        throw "The verified archive did not contain required files: $($missingFiles -join ', ')"
    }

    Write-Host "Staged at $DestinationPath"
}

if ($Blender) {
    $archive = Get-VerifiedArchive `
        -Uri 'https://download.blender.org/release/Blender4.5/blender-4.5.14-windows-x64.zip' `
        -FileName 'blender-4.5.14-windows-x64.zip' `
        -ExpectedSha256 'b9533d2397ac1984db4466fb23a7a4649391cca93f6e84209f9bcc60d071c8b9'

    $destination = Join-Path $toolsRoot 'Blender-4.5.14'
    Expand-VerifiedArchive -ArchivePath $archive -DestinationPath $destination -RequiredRelativePaths @(
        'blender-4.5.14-windows-x64\blender.exe'
    )
}

if ($UnityExplorer) {
    $archive = Get-VerifiedArchive `
        -Uri 'https://thunderstore.io/package/download/sinai-dev/UnityExplorer/4.8.2/' `
        -FileName 'UnityExplorer-4.8.2.zip' `
        -ExpectedSha256 'B3D30229EE785E2C85914BA79C6D4F6D0ECA97D11C8715E55CC71D3085489C24'

    $destination = Join-Path $toolsRoot 'UnityExplorer-4.8.2'
    Expand-VerifiedArchive -ArchivePath $archive -DestinationPath $destination -RequiredRelativePaths @(
        'UnityExplorer.BIE5.Mono.dll',
        'UniverseLib.Mono.dll',
        'mcs.dll',
        'Tomlet.dll'
    )
}

Write-Host 'The tools are staged under .tools/. This script does not modify the game installation or launch Rogue Command.'
