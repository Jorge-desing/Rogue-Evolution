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

. (Join-Path $PSScriptRoot 'Setup-ArchiveSupport.ps1')

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