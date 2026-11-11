[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $repositoryRoot '.tools'
$profileRoot = Join-Path $toolsRoot 'Hardflow-1.23.0'
$blenderExecutable = Join-Path $toolsRoot 'Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe'
$pilotScript = Join-Path $PSScriptRoot 'Run-BulwarkHardflowPilot.py'
$configRoot = Join-Path $profileRoot 'config'
$extensionsRoot = Join-Path $profileRoot 'extensions'
$manifestPath = Join-Path $profileRoot 'install-manifest.json'

if (-not (Test-Path -LiteralPath $blenderExecutable -PathType Leaf)) {
    throw "Pinned Blender 4.5.14 is missing: $blenderExecutable"
}
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw 'Hardflow is not installed in the project-local profile. Run tools/Setup-Hardflow.ps1 first.'
}
$install = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($install.version -ne '1.23.0' -or $install.blenderVersion -ne '4.5.14' -or
    $install.repositoryId -ne 'user_default' -or
    $install.archiveSha256 -ne '5F4B2FE1EDDFC3B4D0397D8052A8FBDFe8BF8F4292C0B8C6FF79BEE00349B947') {
    throw 'The project-local Hardflow profile does not match the pinned v1.23.0 / Blender 4.5.14 setup.'
}

$previousUserConfig = [Environment]::GetEnvironmentVariable('BLENDER_USER_CONFIG', 'Process')
$previousUserExtensions = [Environment]::GetEnvironmentVariable('BLENDER_USER_EXTENSIONS', 'Process')
try {
    [Environment]::SetEnvironmentVariable('BLENDER_USER_CONFIG', $configRoot, 'Process')
    [Environment]::SetEnvironmentVariable('BLENDER_USER_EXTENSIONS', $extensionsRoot, 'Process')
    & $blenderExecutable --background --python-exit-code 1 --python $pilotScript
    $blenderExitCode = $LASTEXITCODE
}
finally {
    [Environment]::SetEnvironmentVariable('BLENDER_USER_CONFIG', $previousUserConfig, 'Process')
    [Environment]::SetEnvironmentVariable('BLENDER_USER_EXTENSIONS', $previousUserExtensions, 'Process')
}

if ($blenderExitCode -ne 0) {
    throw "Bulwark Hardflow pilot failed with exit code $blenderExitCode. See its latest artifacts/bulwark-hardflow-service-hatch-pilot-* report."
}
