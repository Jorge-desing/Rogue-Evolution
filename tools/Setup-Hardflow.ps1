[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $repositoryRoot '.tools'
$downloadsRoot = Join-Path $toolsRoot 'downloads'
$profileRoot = Join-Path $toolsRoot 'Hardflow-1.23.0'
$configRoot = Join-Path $profileRoot 'config'
$extensionsRoot = Join-Path $profileRoot 'extensions'
$extensionPath = Join-Path (Join-Path $extensionsRoot 'user_default') 'hardflow'
$installManifestPath = Join-Path $profileRoot 'install-manifest.json'
$archiveFileName = 'hardflow-1.23.0.zip'
$archiveUri = 'https://github.com/ugulay/hardflow/releases/download/v1.23.0/hardflow-1.23.0.zip'
$expectedSha256 = '5F4B2FE1EDDFC3B4D0397D8052A8FBDFe8BF8F4292C0B8C6FF79BEE00349B947'
$expectedBlenderVersion = '4.5.14'
$blenderExecutablePath = Join-Path $toolsRoot 'Blender-4.5.14\blender-4.5.14-windows-x64\blender.exe'

if (-not (Test-Path -LiteralPath $blenderExecutablePath -PathType Leaf)) {
    throw "Pinned Blender $expectedBlenderVersion is missing at $blenderExecutablePath. Run tools/Setup-ContentAuthoringTools.ps1 -Blender first."
}

$versionOutput = @(& $blenderExecutablePath --version 2>&1)
if ($LASTEXITCODE -ne 0 -or $versionOutput.Count -eq 0 -or $versionOutput[0].ToString() -notmatch "^Blender $([regex]::Escape($expectedBlenderVersion))(\s|$)") {
    $observedVersion = if ($versionOutput.Count -gt 0) { $versionOutput[0].ToString() } else { 'no version output' }
    throw "Expected Blender $expectedBlenderVersion at the project-pinned path, got '$observedVersion'."
}

New-Item -ItemType Directory -Path $downloadsRoot -Force | Out-Null
. (Join-Path $PSScriptRoot 'Setup-ArchiveSupport.ps1')
$archivePath = Get-VerifiedDownload `
    -Uri $archiveUri `
    -FileName $archiveFileName `
    -ExpectedSha256 $expectedSha256 `
    -TargetDownloadsRoot $downloadsRoot

New-Item -ItemType Directory -Path $configRoot, $extensionsRoot -Force | Out-Null

if (Test-Path -LiteralPath $installManifestPath -PathType Leaf) {
    try {
        $installState = Get-Content -LiteralPath $installManifestPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "The existing Hardflow profile marker is unreadable at $installManifestPath. It was left unchanged."
    }

    if ($installState.version -ne '1.23.0' -or
        -not [string]::Equals([string]$installState.archiveSha256, $expectedSha256, [StringComparison]::OrdinalIgnoreCase) -or
        $installState.blenderVersion -ne $expectedBlenderVersion -or
        $installState.repositoryId -ne 'user_default') {
        throw "The existing Hardflow profile marker does not match the pinned install. The profile was left unchanged."
    }
}
elseif (Test-Path -LiteralPath $extensionPath) {
    throw "An unmanaged Hardflow directory already exists at $extensionPath. It was left unchanged; inspect it before running setup again."
}

$previousUserConfig = [Environment]::GetEnvironmentVariable('BLENDER_USER_CONFIG', 'Process')
$previousUserExtensions = [Environment]::GetEnvironmentVariable('BLENDER_USER_EXTENSIONS', 'Process')
try {
    [Environment]::SetEnvironmentVariable('BLENDER_USER_CONFIG', $configRoot, 'Process')
    [Environment]::SetEnvironmentVariable('BLENDER_USER_EXTENSIONS', $extensionsRoot, 'Process')

    $installArguments = @('--command', 'extension', 'install-file', '--repo', 'user_default', '--enable', $archivePath)
    & $blenderExecutablePath @installArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Blender failed to install Hardflow v1.23.0 (exit code $LASTEXITCODE). The project-local profile was retained for inspection."
    }

    $packageManifestPath = Join-Path $extensionPath 'blender_manifest.toml'
    if (-not (Test-Path -LiteralPath $packageManifestPath -PathType Leaf)) {
        throw "Blender returned success, but the Hardflow manifest was not found at $packageManifestPath. The profile was retained for inspection."
    }

    $packageManifest = Get-Content -LiteralPath $packageManifestPath -Raw
    if ($packageManifest -notmatch '(?m)^id\s*=\s*"hardflow"\s*$' -or
        $packageManifest -notmatch '(?m)^version\s*=\s*"1\.23\.0"\s*$') {
        throw "The installed package manifest does not identify Hardflow v1.23.0. The profile was retained for inspection."
    }

    $stateObject = [ordered]@{
        addon = 'Hardflow'
        version = '1.23.0'
        archiveUri = $archiveUri
        archiveSha256 = $expectedSha256
        blenderVersion = $expectedBlenderVersion
        repositoryId = 'user_default'
        extensionPath = '.tools/Hardflow-1.23.0/extensions/user_default/hardflow'
    }
    $stateJson = $stateObject | ConvertTo-Json -Depth 4
    $temporaryManifestPath = "$installManifestPath.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        Set-Content -LiteralPath $temporaryManifestPath -Value $stateJson -Encoding utf8
        Move-Item -LiteralPath $temporaryManifestPath -Destination $installManifestPath -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryManifestPath) {
            Remove-Item -LiteralPath $temporaryManifestPath -Force
        }
    }

    $verifyExpression = 'import addon_utils, importlib; module = "bl_ext.user_default.hardflow"; loaded, enabled = addon_utils.check(module); print(f"[HARDFLOW] module={module} loaded={loaded} enabled={enabled}"); assert enabled, "Hardflow is not enabled in the project-local Blender profile"; addon_utils.enable(module, default_set=False); importlib.import_module(module)'
    $verifyArguments = @('--background', '--python-exit-code', '3', '--python-expr', $verifyExpression)
    & $blenderExecutablePath @verifyArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Hardflow was installed but failed its Blender $expectedBlenderVersion activation check (exit code $LASTEXITCODE). The project-local profile was retained for inspection."
    }
}
finally {
    [Environment]::SetEnvironmentVariable('BLENDER_USER_CONFIG', $previousUserConfig, 'Process')
    [Environment]::SetEnvironmentVariable('BLENDER_USER_EXTENSIONS', $previousUserExtensions, 'Process')
}

Write-Host "Hardflow v1.23.0 is installed and enabled in the project-local Blender profile."
Write-Host "Launch it with: .\tools\Start-BlenderWithHardflow.ps1"
