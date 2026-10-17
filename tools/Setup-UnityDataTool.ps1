[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $repositoryRoot '.tools'
$downloadsRoot = Join-Path $toolsRoot 'downloads'
$archiveName = 'UnityDataTool-windows-x64-v2.4.0.zip'
$archivePath = Join-Path $downloadsRoot $archiveName
$destination = Join-Path $toolsRoot 'UnityDataTool-2.4.0'
$staging = Join-Path $toolsRoot '.UnityDataTool-2.4.0-staging'
$expectedSha256 = '8104B43B38E11EEFD8E0340F689011857FB0886D1F512866E9E4D2F96F6DFC69'
$downloadUri = 'https://github.com/Unity-Technologies/UnityDataTools/releases/download/v2.4.0/UnityDataTool-windows-x64.zip'
$requiredFiles = @(
    'UnityDataTool.exe',
    'UnityFileSystemApi.dll',
    'README.md',
    'LICENSE.md',
    'Documentation/unitydatatool.md'
)

New-Item -ItemType Directory -Path $downloadsRoot -Force | Out-Null
foreach ($directoryPath in @($toolsRoot, $downloadsRoot)) {
    $directoryItem = Get-Item -LiteralPath $directoryPath -Force
    if (($directoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to use reparse-point directory $directoryPath."
    }
}

if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
    Invoke-WebRequest -Uri $downloadUri -OutFile $archivePath -UseBasicParsing
}

$archiveItem = Get-Item -LiteralPath $archivePath -Force
if (($archiveItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Refusing to hash or extract reparse-point archive $archivePath."
}

$actualSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
if (-not [string]::Equals($actualSha256, $expectedSha256, [StringComparison]::OrdinalIgnoreCase)) {
    throw "SHA-256 mismatch for $archivePath. Expected $expectedSha256, got $actualSha256. The archive was left in place for inspection."
}

if (Test-Path -LiteralPath $destination -PathType Container) {
    $destinationItem = Get-Item -LiteralPath $destination -Force
    if (($destinationItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to use reparse-point destination $destination."
    }

    $missingFiles = @($requiredFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $destination $_) -PathType Leaf) })
    if ($missingFiles.Count -gt 0) {
        throw "The existing directory $destination is incomplete. Missing: $($missingFiles -join ', '). It was left unchanged."
    }

    $installedVersion = (& (Join-Path $destination 'UnityDataTool.exe') --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $installedVersion -notmatch '2\.4\.0') {
        throw "The existing UnityDataTool installation did not report version 2.4.0: $installedVersion"
    }

    Write-Host "Already staged at $destination ($installedVersion)"
    exit 0
}

if (Test-Path -LiteralPath $staging) {
    throw "The staging path $staging already exists. It was left unchanged for inspection."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
$stagingFullPath = [System.IO.Path]::GetFullPath($staging).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$seenEntries = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$maximumExpandedBytes = 536870912
try {
    $totalExpandedBytes = [long]0
    foreach ($entry in $archive.Entries) {
        $normalizedEntry = $entry.FullName.Replace('\', '/')
        $segments = $normalizedEntry.TrimEnd('/').Split('/')
        if ([string]::IsNullOrWhiteSpace($normalizedEntry) -or
            $normalizedEntry.StartsWith('/') -or
            [System.IO.Path]::IsPathRooted($normalizedEntry) -or
            @($segments | Where-Object { [string]::IsNullOrWhiteSpace($_) -or $_ -eq '..' -or $_ -eq '.' -or $_.Contains(':') }).Count -gt 0) {
            throw "Unsafe archive entry path: $($entry.FullName)"
        }

        if (-not $seenEntries.Add($normalizedEntry.TrimEnd('/'))) {
            throw "Duplicate archive entry path: $($entry.FullName)"
        }

        $unixFileType = ($entry.ExternalAttributes -shr 16) -band 0xF000
        if ($unixFileType -eq 0xA000) {
            throw "Refusing symbolic-link archive entry: $($entry.FullName)"
        }

        $resolvedPath = [System.IO.Path]::GetFullPath((Join-Path $staging $normalizedEntry.Replace('/', '\')))
        if (-not $resolvedPath.StartsWith($stagingFullPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe archive entry path: $($entry.FullName)"
        }

        $totalExpandedBytes += $entry.Length
        if ($totalExpandedBytes -gt $maximumExpandedBytes) {
            throw "Archive expanded size exceeds the $maximumExpandedBytes-byte safety limit."
        }
    }

    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    foreach ($entry in $archive.Entries) {
        $relativePath = $entry.FullName.Replace('/', '\')
        $outputPath = [System.IO.Path]::GetFullPath((Join-Path $staging $relativePath))
        if (-not $outputPath.StartsWith($stagingFullPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe archive entry path: $($entry.FullName)"
        }

        if ($entry.FullName.EndsWith('/')) {
            New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
            continue
        }

        $parentDirectory = Split-Path -Parent $outputPath
        New-Item -ItemType Directory -Path $parentDirectory -Force | Out-Null
        $sourceStream = $entry.Open()
        $targetStream = [System.IO.File]::Create($outputPath)
        try {
            $sourceStream.CopyTo($targetStream)
        }
        finally {
            $targetStream.Dispose()
            $sourceStream.Dispose()
        }
    }

    $missingFiles = @($requiredFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $staging $_) -PathType Leaf) })
    if ($missingFiles.Count -gt 0) {
        throw "The verified archive is missing required files: $($missingFiles -join ', ')"
    }
}
finally {
    $archive.Dispose()
}

if (Test-Path -LiteralPath $destination) {
    throw "The destination $destination appeared during extraction. Files remain at $staging."
}

$stagingFull = [System.IO.Path]::GetFullPath($staging)
$toolsRootFull = [System.IO.Path]::GetFullPath($toolsRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$destinationFull = [System.IO.Path]::GetFullPath($destination)
if (-not $stagingFull.StartsWith($toolsRootFull, [StringComparison]::OrdinalIgnoreCase) -or
    -not $destinationFull.StartsWith($toolsRootFull, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The resolved staging and destination paths must both stay under the repository .tools directory.'
}

$stagedVersion = (& (Join-Path $staging 'UnityDataTool.exe') --version 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $stagedVersion -notmatch '2\.4\.0') {
    throw "The verified archive did not run as UnityDataTool 2.4.0: $stagedVersion"
}

Move-Item -LiteralPath $stagingFull -Destination $destinationFull
Write-Host "Staged UnityDataTool 2.4.0 at $destinationFull"
Write-Host "Archive SHA-256: $actualSha256"
Write-Host 'This script does not copy files to the game, change game configuration, or launch Rogue Command.'
