[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$blenderPath = Join-Path $repoRoot '.tools\Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe'
$profileRoot = Join-Path $repoRoot '.tools\Hardflow-1.23.0'
$configPath = Join-Path $profileRoot 'config'
$extensionsPath = Join-Path $profileRoot 'extensions'
$pilotScript = Join-Path $PSScriptRoot 'Run-GravitasBreakerHardflowPilot.py'

foreach ($requiredPath in @($blenderPath, $configPath, $extensionsPath, $pilotScript)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required Hardflow pilot path is missing: $requiredPath"
    }
}

$env:BLENDER_USER_CONFIG = (Resolve-Path -LiteralPath $configPath).Path
$env:BLENDER_USER_EXTENSIONS = (Resolve-Path -LiteralPath $extensionsPath).Path
& $blenderPath --background --python $pilotScript
if ($LASTEXITCODE -ne 0) {
    throw "Blender Hardflow pilot exited with code $LASTEXITCODE"
}
