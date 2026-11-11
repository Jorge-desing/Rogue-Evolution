[CmdletBinding()]
param(
    [string]$BlendFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $repositoryRoot '.tools'
$profileRoot = Join-Path $toolsRoot 'Hardflow-1.23.0'
$configRoot = Join-Path $profileRoot 'config'
$extensionsRoot = Join-Path $profileRoot 'extensions'
$extensionPath = Join-Path (Join-Path $extensionsRoot 'user_default') 'hardflow'
$installManifestPath = Join-Path $profileRoot 'install-manifest.json'
$expectedSha256 = '5F4B2FE1EDDFC3B4D0397D8052A8FBDFe8BF8F4292C0B8C6FF79BEE00349B947'
$expectedBlenderVersion = '4.5.14'
$blenderExecutablePath = Join-Path $toolsRoot 'Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe'

if (-not (Test-Path -LiteralPath $blenderExecutablePath -PathType Leaf)) {
    throw "Pinned Blender $expectedBlenderVersion is missing at $blenderExecutablePath. Run tools/Setup-ContentAuthoringTools.ps1 -Blender first."
}
if (-not (Test-Path -LiteralPath $installManifestPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $extensionPath 'blender_manifest.toml') -PathType Leaf)) {
    throw 'Hardflow is not set up for this checkout. Run tools/Setup-Hardflow.ps1 first.'
}

try {
    $installState = Get-Content -LiteralPath $installManifestPath -Raw | ConvertFrom-Json
}
catch {
    throw "The project-local Hardflow install marker is unreadable at $installManifestPath. Run setup after inspecting the profile."
}

if ($installState.version -ne '1.23.0' -or
    -not [string]::Equals([string]$installState.archiveSha256, $expectedSha256, [StringComparison]::OrdinalIgnoreCase) -or
    $installState.blenderVersion -ne $expectedBlenderVersion -or
    $installState.repositoryId -ne 'user_default') {
    throw 'The project-local Hardflow profile does not match the pinned v1.23.0 setup. Run tools/Setup-Hardflow.ps1 and inspect any reported mismatch.'
}

$packageManifest = Get-Content -LiteralPath (Join-Path $extensionPath 'blender_manifest.toml') -Raw
if ($packageManifest -notmatch '(?m)^id\s*=\s*"hardflow"\s*$' -or
    $packageManifest -notmatch '(?m)^version\s*=\s*"1\.23\.0"\s*$') {
    throw 'The installed extension manifest does not match Hardflow v1.23.0. Run setup after inspecting the profile.'
}

if ($BlendFile) {
    $resolvedBlendFile = (Resolve-Path -LiteralPath $BlendFile -ErrorAction Stop).Path
    $blenderArguments = @($resolvedBlendFile)
}
else {
    $blenderArguments = @()
}

$previousUserConfig = [Environment]::GetEnvironmentVariable('BLENDER_USER_CONFIG', 'Process')
$previousUserExtensions = [Environment]::GetEnvironmentVariable('BLENDER_USER_EXTENSIONS', 'Process')
try {
    [Environment]::SetEnvironmentVariable('BLENDER_USER_CONFIG', $configRoot, 'Process')
    [Environment]::SetEnvironmentVariable('BLENDER_USER_EXTENSIONS', $extensionsRoot, 'Process')
    & $blenderExecutablePath @blenderArguments
    $blenderExitCode = $LASTEXITCODE
}
finally {
    [Environment]::SetEnvironmentVariable('BLENDER_USER_CONFIG', $previousUserConfig, 'Process')
    [Environment]::SetEnvironmentVariable('BLENDER_USER_EXTENSIONS', $previousUserExtensions, 'Process')
}

if ($null -ne $blenderExitCode) {
    exit $blenderExitCode
}
