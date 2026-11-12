<#
.SYNOPSIS
    Installs and configures recommended open-source Blender authoring addons for Rogue Command.

.DESCRIPTION
    Installs EdgeFlow, DreamUV, and Ucupaint into Blender 4.5's user scripts/addons directory
    and registers them for hard-surface edge flow, trim-sheet UV mapping, and layered PBR painting.
#>

[CmdletBinding()]
param(
    [string]$BlenderExecutablePath = "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$appdata = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::ApplicationData)
$addonsDir = Join-Path $appdata 'Blender Foundation\Blender\4.5\scripts\addons'

if (-not (Test-Path -LiteralPath $addonsDir -PathType Container)) {
    New-Item -ItemType Directory -Path $addonsDir -Force | Out-Null
    Write-Host "Created Blender 4.5 scripts/addons directory: $addonsDir"
}

$addons = @(
    @{ Name = "EdgeFlow"; Uri = "https://github.com/BenjaminSauder/EdgeFlow.git" },
    @{ Name = "dream_uv"; Uri = "https://github.com/leukbaars/DreamUV.git" },
    @{ Name = "ucupaint"; Uri = "https://github.com/ucupumar/ucupaint.git" }
)

foreach ($addon in $addons) {
    $addonPath = Join-Path $addonsDir $addon.Name
    if (-not (Test-Path -LiteralPath $addonPath -PathType Container)) {
        Write-Host "Cloning $($addon.Name)..."
        git -c http.sslBackend=schannel clone --depth 1 $addon.Uri $addonPath
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to clone $($addon.Name)"
        }
        Write-Host "Cloned $($addon.Name) successfully."
    } else {
        Write-Host "$($addon.Name) is already installed at $addonPath"
    }
}

# Verify and enable in Blender
if (Test-Path -LiteralPath $BlenderExecutablePath -PathType Leaf) {
    Write-Host "Verifying addons with Blender at $BlenderExecutablePath..."
    $verifyPy = @"
import addon_utils
for a in ['EdgeFlow', 'dream_uv', 'ucupaint']:
    try:
        addon_utils.enable(a, default_set=True)
        print(f'[VERIFIED] Enabled {a}')
    except Exception as ex:
        print(f'[ERROR] Could not enable {a}: {ex}')
"@
    $tempScript = [System.IO.Path]::GetTempFileName() + ".py"
    try {
        Set-Content -Path $tempScript -Value $verifyPy -Encoding UTF8
        & $BlenderExecutablePath --background --python $tempScript
    }
    finally {
        if (Test-Path -LiteralPath $tempScript) {
            Remove-Item -Path $tempScript -Force
        }
    }
} else {
    Write-Warning "Blender executable not found at $BlenderExecutablePath. Addons were installed to $addonsDir but not verified in Blender."
}

Write-Host "Blender authoring addons setup complete!"
