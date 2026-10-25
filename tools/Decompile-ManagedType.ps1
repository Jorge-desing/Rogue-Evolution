[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$AssemblyPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$TypeName,

    [string]$ReferenceDirectory,

    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $defaultManagedPath = 'C:\Program Files (x86)\Steam\steamapps\common\Rogue Command\Rogue Command_Data\Managed\Assembly-CSharp.dll'
    if (Test-Path -LiteralPath $defaultManagedPath -PathType Leaf) {
        $AssemblyPath = $defaultManagedPath
    }
    else {
        throw "AssemblyPath was not specified and default Rogue Command Assembly-CSharp.dll was not found at $defaultManagedPath"
    }
}

$resolvedAssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$assemblyDirectory = Split-Path -Parent $resolvedAssemblyPath

if ([string]::IsNullOrWhiteSpace($ReferenceDirectory)) {
    $resolvedReferenceDirectory = $assemblyDirectory
}
else {
    $resolvedReferenceDirectory = (Resolve-Path -LiteralPath $ReferenceDirectory).Path
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $resolvedOutputDirectory = Join-Path $repoRoot 'artifacts\rea-tools\decompiled'
}
elseif ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $resolvedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
}
else {
    $resolvedOutputDirectory = [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $OutputDirectory))
}

$toolVersion = '9.1.0.7988'
$toolDirectory = Join-Path $repoRoot 'artifacts\rea-tools\ilspycmd'
$toolPath = Join-Path $toolDirectory 'ilspycmd.exe'
$null = New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force

if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) {
    $null = New-Item -ItemType Directory -Path $toolDirectory -Force
    $installOutput = & dotnet tool install ilspycmd --tool-path $toolDirectory --version $toolVersion --add-source 'https://api.nuget.org/v3/index.json' 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Could not install ILSpyCmd $toolVersion. dotnet output: $($installOutput -join [Environment]::NewLine)"
    }
}

$reportedVersion = (& $toolPath --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "ILSpyCmd did not start successfully: $reportedVersion"
}
if ($reportedVersion -notmatch [regex]::Escape($toolVersion)) {
    $updateOutput = & dotnet tool update ilspycmd --tool-path $toolDirectory --version $toolVersion --add-source 'https://api.nuget.org/v3/index.json' 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Could not pin ILSpyCmd to $toolVersion. dotnet output: $($updateOutput -join [Environment]::NewLine)"
    }

    $reportedVersion = (& $toolPath --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $reportedVersion -notmatch [regex]::Escape($toolVersion)) {
        throw "Expected ILSpyCmd $toolVersion after update; found '$reportedVersion'."
    }
}

$safeTypeName = $TypeName -replace '[^A-Za-z0-9_.-]', '_'
$outputPath = Join-Path $resolvedOutputDirectory ($safeTypeName + '.cs')
$stderrPath = [System.IO.Path]::GetTempFileName()
$toolArguments = @(
    '--disable-updatecheck',
    '--referencepath', $resolvedReferenceDirectory,
    '--type', $TypeName,
    $resolvedAssemblyPath
)

try {
    $decompiledLines = & $toolPath @toolArguments 2> $stderrPath
    if ($LASTEXITCODE -ne 0) {
        $toolError = Get-Content -LiteralPath $stderrPath -Raw -ErrorAction SilentlyContinue
        throw "ILSpyCmd failed to decompile '$TypeName' from '$resolvedAssemblyPath'. $toolError"
    }

    [System.IO.File]::WriteAllLines($outputPath, $decompiledLines, [System.Text.UTF8Encoding]::new($false))

    if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf) -or (Get-Item -LiteralPath $outputPath).Length -eq 0) {
        throw "ILSpyCmd returned success but produced no source for '$TypeName'."
    }
}
finally {
    Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue
}

Write-Output "ILSpyCmd $toolVersion decompiled '$TypeName' to '$outputPath'."
