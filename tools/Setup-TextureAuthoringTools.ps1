[CmdletBinding()]
param(
    [switch]$MaterialMaker,
    [string]$ArmorPaintArchivePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $MaterialMaker -and -not $ArmorPaintArchivePath) {
    Write-Host 'Choose an optional local texture tool:'
    Write-Host '  .\tools\Setup-TextureAuthoringTools.ps1 -MaterialMaker'
    Write-Host '  .\tools\Setup-TextureAuthoringTools.ps1 -ArmorPaintArchivePath "<licensed ArmorPaint zip>"'
    exit 0
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $repositoryRoot '.tools'
$downloadsRoot = Join-Path $toolsRoot 'downloads'
New-Item -ItemType Directory -Path $downloadsRoot -Force | Out-Null

function Get-VerifiedDownload {
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

    Write-Host "Verified SHA-256 $actualSha256"
    return $archivePath
}

function Expand-SafeArchive {
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
            if ([System.IO.Path]::IsPathRooted($relativePath) -or $relativePath -match '(^|\\)\.\.(\\|$)') {
                throw "Unsafe archive entry path: $($entry.FullName)"
            }

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
        throw "The archive did not contain required files: $($missingFiles -join ', ')"
    }

    Write-Host "Staged at $DestinationPath"
}

if ($MaterialMaker) {
    $archive = Get-VerifiedDownload `
        -Uri 'https://github.com/RodZill4/material-maker/releases/download/1.7/material_maker_1_7_windows.zip' `
        -FileName 'material_maker_1_7_windows.zip' `
        -ExpectedSha256 'DEB4416BC939861D48097A866A8B2BF0363C29FF64874F2E04478658FF900808'

    $destination = Join-Path $toolsRoot 'MaterialMaker-1.7'
    Expand-SafeArchive -ArchivePath $archive -DestinationPath $destination -RequiredRelativePaths @(
        'material_maker_1_7_windows\material_maker.exe'
    )
}

if ($ArmorPaintArchivePath) {
    $armorPaintArchive = (Resolve-Path -LiteralPath $ArmorPaintArchivePath).Path
    if ([System.IO.Path]::GetExtension($armorPaintArchive) -ne '.zip') {
        throw 'ArmorPaintArchivePath must point to the official Windows ZIP archive.'
    }

    $archiveHash = (Get-FileHash -LiteralPath $armorPaintArchive -Algorithm SHA256).Hash
    Write-Host "Staging the supplied ArmorPaint archive with SHA-256 $archiveHash"
    $destination = Join-Path $toolsRoot 'ArmorPaint'
    Expand-SafeArchive -ArchivePath $armorPaintArchive -DestinationPath $destination -RequiredRelativePaths @('ArmorPaint.exe')
}

Write-Host 'Texture tools are staged under .tools/. The script does not modify the Unity project, game installation, or launch either application.'
