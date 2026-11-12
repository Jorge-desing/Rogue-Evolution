<#
.SYNOPSIS
Shared download, SHA-256 validation, and path-traversal-safe extraction for setup scripts.
#>

function Get-VerifiedDownload {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $true)][string]$ExpectedSha256,
        [string]$TargetDownloadsRoot = ''
    )

    if ([string]::IsNullOrWhiteSpace($TargetDownloadsRoot)) {
        $TargetDownloadsRoot = if ($downloadsRoot) { $downloadsRoot } else { Join-Path (Split-Path -Parent $PSScriptRoot) '.tools\downloads' }
    }

    $archivePath = Join-Path $TargetDownloadsRoot $FileName
    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
        Write-Host "Downloading $FileName"
        Invoke-WebRequest -Uri $Uri -OutFile $archivePath -UseBasicParsing
    }

    $actualSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if (-not [string]::Equals($actualSha256, $ExpectedSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "SHA-256 mismatch for $archivePath. Expected $ExpectedSha256, got $actualSha256. The archive was left in place for inspection."
    }

    Write-Host "Verified SHA-256 $actualSha256"
    return $archivePath
}

function Expand-SafeArchive {
    param(
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][string[]]$RequiredRelativePaths,
        [string]$TargetToolsRoot = ''
    )

    if ([string]::IsNullOrWhiteSpace($TargetToolsRoot)) {
        $TargetToolsRoot = if ($toolsRoot) { $toolsRoot } else { Join-Path (Split-Path -Parent $PSScriptRoot) '.tools' }
    }

    $destinationFullPath = [System.IO.Path]::GetFullPath($DestinationPath).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (Test-Path -LiteralPath $DestinationPath -PathType Container) {
        $missingFiles = @($RequiredRelativePaths | Where-Object { -not (Test-Path -LiteralPath (Join-Path $DestinationPath $_) -PathType Leaf) })
        if ($missingFiles.Count -eq 0) {
            Write-Host "Already staged at $DestinationPath"
            return
        }

        $existingItems = @(Get-ChildItem -LiteralPath $DestinationPath -Force)
        if ($existingItems.Count -gt 0) {
            throw "The existing directory $DestinationPath is incomplete. It was left unchanged."
        }
    }

    $tempExtractRoot = Join-Path $TargetToolsRoot (".stage-" + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempExtractRoot -Force | Out-Null
    try {
        Expand-Archive -LiteralPath $ArchivePath -DestinationPath $tempExtractRoot -Force

        $extractedFiles = Get-ChildItem -LiteralPath $tempExtractRoot -Recurse -Force
        foreach ($file in $extractedFiles) {
            $targetPath = [System.IO.Path]::GetFullPath((Join-Path $DestinationPath (Resolve-Path -LiteralPath $file.FullName -RelativeBasePath $tempExtractRoot)))
            if (-not $targetPath.StartsWith($destinationFullPath, [StringComparison]::OrdinalIgnoreCase)) {
                throw "The archive contains an entry that escapes the destination path: $($file.FullName)"
            }
        }

        if (Test-Path -LiteralPath $DestinationPath -PathType Container) {
            Remove-Item -LiteralPath $DestinationPath -Recurse -Force
        }
        Move-Item -LiteralPath $tempExtractRoot -Destination $DestinationPath
        Write-Host "Extracted to $DestinationPath"
    }
    finally {
        if (Test-Path -LiteralPath $tempExtractRoot) {
            Remove-Item -LiteralPath $tempExtractRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-VerifiedArchive {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $true)][string]$ExpectedSha256
    )
    return Get-VerifiedDownload -Uri $Uri -FileName $FileName -ExpectedSha256 $ExpectedSha256
}

function Expand-VerifiedArchive {
    param(
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][string[]]$RequiredRelativePaths
    )
    Expand-SafeArchive -ArchivePath $ArchivePath -DestinationPath $DestinationPath -RequiredRelativePaths $RequiredRelativePaths
}
