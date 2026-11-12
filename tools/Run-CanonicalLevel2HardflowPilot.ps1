[CmdletBinding()]
param(
    [switch]$UseHardflow,
    [switch]$RenderPreviews
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $repositoryRoot '.tools'
$blenderExecutable = Join-Path $toolsRoot 'Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe'
$hardflowRoot = Join-Path $toolsRoot 'Hardflow-1.23.0'
$configRoot = Join-Path $hardflowRoot 'config'
$extensionsRoot = Join-Path $hardflowRoot 'extensions'
$pilotScript = Join-Path $PSScriptRoot 'Run-CanonicalLevel2HardflowPilot.py'

if (-not (Test-Path -LiteralPath $blenderExecutable -PathType Leaf)) {
    throw "Pinned Blender 4.5.14 executable is missing at $blenderExecutable."
}

$previousUserConfig = [Environment]::GetEnvironmentVariable('BLENDER_USER_CONFIG', 'Process')
$previousUserExtensions = [Environment]::GetEnvironmentVariable('BLENDER_USER_EXTENSIONS', 'Process')

try {
    if (Test-Path -LiteralPath $configRoot) {
        [Environment]::SetEnvironmentVariable('BLENDER_USER_CONFIG', $configRoot, 'Process')
        [Environment]::SetEnvironmentVariable('BLENDER_USER_EXTENSIONS', $extensionsRoot, 'Process')
    }

    $arguments = @('--background', '--python', $pilotScript, '--')
    if ($UseHardflow) {
        $arguments += '--use-hardflow'
    }
    if ($RenderPreviews) {
        $arguments += '--render-previews'
    }

    Write-Host "Running Canonical Level 2 Hardflow Pilot in Blender 4.5.14..."
    & $blenderExecutable @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Canonical Level 2 Hardflow Pilot failed with exit code $LASTEXITCODE."
    }
}
finally {
    [Environment]::SetEnvironmentVariable('BLENDER_USER_CONFIG', $previousUserConfig, 'Process')
    [Environment]::SetEnvironmentVariable('BLENDER_USER_EXTENSIONS', $previousUserExtensions, 'Process')
}

Write-Host "Canonical Level 2 Hardflow Pilot completed successfully."
