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

. (Join-Path $PSScriptRoot 'Setup-ArchiveSupport.ps1')

if ($Blender) {
    $archive = Get-VerifiedArchive `
        -Uri 'https://download.blender.org/release/Blender4.5/blender-4.5.14-windows-x64.zip' `
        -FileName 'blender-4.5.14-windows-x64.zip' `
        -ExpectedSha256 '2840D0429C52E4968C5E2851ACCD0F37956CCDE249EE92BF9392AE2D6C0284FB'

    Expand-VerifiedArchive `
        -ArchivePath $archive `
        -DestinationPath (Join-Path $toolsRoot 'Blender-4.5.14') `
        -RequiredRelativePaths @(
            'blender.exe',
            'blender-launcher.exe'
        )
}

if ($UnityExplorer) {
    $archive = Get-VerifiedArchive `
        -Uri 'https://github.com/sinai-dev/UnityExplorer/releases/download/4.10.4/UnityExplorer.BepInEx5.Mono.zip' `
        -FileName 'UnityExplorer.BepInEx5.Mono.zip' `
        -ExpectedSha256 'CDAC19451433BF7099778D4ED9A296D4BCE0B873B6F07F2FF30DFB52702D8E3E'

    Expand-VerifiedArchive `
        -ArchivePath $archive `
        -DestinationPath (Join-Path $toolsRoot 'UnityExplorer-4.10.4') `
        -RequiredRelativePaths @(
            'sinai-dev-UnityExplorer\UnityExplorer.BE5.Mono.dll',
            'sinai-dev-UnityExplorer\UniverseLib.Mono.dll'
        )
}