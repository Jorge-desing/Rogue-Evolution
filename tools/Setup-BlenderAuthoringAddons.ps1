<#
.SYNOPSIS
    Installs and configures recommended open-source Blender authoring addons for Rogue Command.

.DESCRIPTION
    Installs EdgeFlow, DreamUV, and Ucupaint into Blender 4.5's user scripts/addons directory
    and registers them for hard-surface edge flow, trim-sheet UV mapping, and layered PBR painting.
    With -IncludeExtended, also installs TexTools-Blender (UV layout, texel density, baking)
    and Wiggle 2 (secondary bone physics for antennas, cables, and articulated tails).
#>

[CmdletBinding()]
param(
    [string]$BlenderExecutablePath = "C:\Program Files\Blender Foundation\Blender 4.5\blender.exe",
    [switch]$IncludeExtended
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
if ($IncludeExtended) {
    $addons += @(
        @{ Name = "TexTools"; Uri = "https://github.com/franMarz/TexTools-Blender.git" },
        @{ Name = "wiggle_2"; Uri = "https://github.com/shteeve3d/blender-wiggle-2.git"; SingleFile = "wiggle_2.py" }
    )
}

foreach ($addon in $addons) {
    if ($addon.ContainsKey("SingleFile") -and $addon.SingleFile) {
        $targetFile = Join-Path $addonsDir $addon.SingleFile
        if (-not (Test-Path -LiteralPath $targetFile -PathType Leaf)) {
            $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
            $stagingDir = Join-Path $repoRoot ".tools\blender-$($addon.Name)"
            if (-not (Test-Path -LiteralPath $stagingDir -PathType Container)) {
                Write-Host "Cloning $($addon.Name) into staging directory..."
                git -c http.sslBackend=schannel clone --depth 1 $addon.Uri $stagingDir
                if ($LASTEXITCODE -ne 0) {
                    throw "Failed to clone $($addon.Name)"
                }
            }
            $sourceFile = Join-Path $stagingDir $addon.SingleFile
            if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf)) {
                throw "Expected single-file addon '$($addon.SingleFile)' not found in $stagingDir"
            }
            Copy-Item -LiteralPath $sourceFile -Destination $targetFile -Force
            Write-Host "Installed $($addon.Name) single-file addon to $targetFile."
        } else {
            Write-Host "$($addon.Name) is already installed at $targetFile"
        }
        continue
    }

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
    $addonNamesPy = ($addons | ForEach-Object { "'$($_.Name)'" }) -join ", "
    $verifyPy = @"
import addon_utils, sys
failures = []
for a in [$addonNamesPy]:
    try:
        addon_utils.enable(a, default_set=True)
        _, is_loaded = addon_utils.check(a)
        if not is_loaded:
            failures.append(a)
            print(f'[ERROR] Addon {a} did not load')
        else:
            print(f'[VERIFIED] Enabled {a}')
    except Exception as ex:
        failures.append(a)
        print(f'[ERROR] Could not enable {a}: {ex}')
if failures:
    sys.exit(1)
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
