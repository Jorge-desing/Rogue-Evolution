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

$sourceBlendPath = Join-Path $repositoryRoot 'content/source-art/bulwarksiegetank.blend'
if (-not (Test-Path -LiteralPath $sourceBlendPath -PathType Leaf)) {
    throw "Bulwark source blend is missing: $sourceBlendPath"
}
$backupStamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$backupRoot = Join-Path $repositoryRoot "artifacts/bulwark-source-backups/pilot-$backupStamp"
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
$backupPath = Join-Path $backupRoot 'bulwarksiegetank.blend'
$sourceHash = (Get-FileHash -LiteralPath $sourceBlendPath -Algorithm SHA256).Hash.ToLowerInvariant()
Copy-Item -LiteralPath $sourceBlendPath -Destination $backupPath
$backupHash = (Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($sourceHash -ne $backupHash) {
    throw 'The verified Bulwark pilot backup does not match the current source blend.'
}
$backupManifestPath = Join-Path $backupRoot 'manifest.json'
$repositoryRootFull = [System.IO.Path]::GetFullPath($repositoryRoot).TrimEnd('\', '/')
$backupPathFull = [System.IO.Path]::GetFullPath($backupPath)
if (-not $backupPathFull.StartsWith($repositoryRootFull + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'The verified Bulwark pilot backup must remain under the repository artifacts directory.'
}
$backupRelativePath = $backupPathFull.Substring($repositoryRootFull.Length + 1)
$backupManifest = [ordered]@{
    createdUtc = [DateTime]::UtcNow.ToString('o')
    source = $sourceBlendPath
    sourceSha256 = $sourceHash
    backup = $backupRelativePath
    backupSha256 = $backupHash
    purpose = 'Exact source snapshot for a read-only Hardflow pilot.'
}
$manifestJson = $backupManifest | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText(
    $backupManifestPath,
    $manifestJson + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

$previousUserConfig = [Environment]::GetEnvironmentVariable('BLENDER_USER_CONFIG', 'Process')
$previousUserExtensions = [Environment]::GetEnvironmentVariable('BLENDER_USER_EXTENSIONS', 'Process')
$previousBackupManifest = [Environment]::GetEnvironmentVariable('RCL_BULWARK_BACKUP_MANIFEST', 'Process')
try {
    [Environment]::SetEnvironmentVariable('BLENDER_USER_CONFIG', $configRoot, 'Process')
    [Environment]::SetEnvironmentVariable('BLENDER_USER_EXTENSIONS', $extensionsRoot, 'Process')
    [Environment]::SetEnvironmentVariable('RCL_BULWARK_BACKUP_MANIFEST', $backupManifestPath, 'Process')
    & $blenderExecutable --background --python-exit-code 1 --python $pilotScript
    $blenderExitCode = $LASTEXITCODE
}
finally {
    [Environment]::SetEnvironmentVariable('BLENDER_USER_CONFIG', $previousUserConfig, 'Process')
    [Environment]::SetEnvironmentVariable('BLENDER_USER_EXTENSIONS', $previousUserExtensions, 'Process')
    [Environment]::SetEnvironmentVariable('RCL_BULWARK_BACKUP_MANIFEST', $previousBackupManifest, 'Process')
}

if ($blenderExitCode -ne 0) {
    throw "Bulwark Hardflow pilot failed with exit code $blenderExitCode. See its latest artifacts/bulwark-hardflow-contact-alignment-pilot-* report."
}
