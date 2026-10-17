[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$mcpRoot = Join-Path $repositoryRoot '.tools\BepInExMCP'
$pythonPath = Join-Path $mcpRoot '.venv\Scripts\python.exe'
$entryPointPath = Join-Path $mcpRoot 'mcp\ModdersHelperApp.py'
$constraintsPath = Join-Path $PSScriptRoot 'BepInExMCP.constraints.txt'

foreach ($requiredPath in @($pythonPath, $entryPointPath, $constraintsPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "BepInEx MCP repair prerequisite is missing: $requiredPath. Run the repository's BepInEx MCP setup first."
    }
}

$mcpRootFullPath = [System.IO.Path]::GetFullPath($mcpRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
$mcpRootBoundary = $mcpRootFullPath + [System.IO.Path]::DirectorySeparatorChar
foreach ($path in @($pythonPath, $entryPointPath)) {
    $resolved = [System.IO.Path]::GetFullPath($path)
    if (-not $resolved.StartsWith($mcpRootBoundary, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Resolved repair path escaped the local BepInEx MCP checkout: $resolved"
    }

    $currentPath = $resolved
    while ($true) {
        $item = Get-Item -LiteralPath $currentPath -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to repair BepInEx MCP through a reparse-point path: $currentPath"
        }

        if ([string]::Equals($currentPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar),
                $mcpRootFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            break
        }

        $currentPath = Split-Path -Parent $currentPath
        if ([string]::IsNullOrWhiteSpace($currentPath)) {
            throw "Could not resolve the BepInEx MCP checkout root for $resolved"
        }
    }
}

& $pythonPath -m pip install --disable-pip-version-check --constraint $constraintsPath 'pydantic>=2.12,<2.14'
if ($LASTEXITCODE -ne 0) {
    throw "Installing the compatible Pydantic version failed with exit code $LASTEXITCODE."
}

& $pythonPath -m pip check
if ($LASTEXITCODE -ne 0) {
    throw "BepInEx MCP Python dependency check failed with exit code $LASTEXITCODE."
}

$pydanticMetadata = @(& $pythonPath -m pip show pydantic)
$mcpMetadata = @(& $pythonPath -m pip show mcp)
$pydanticVersionLine = $pydanticMetadata | Where-Object { $_ -match '^Version:\s*(\S+)$' } | Select-Object -First 1
$mcpVersionLine = $mcpMetadata | Where-Object { $_ -match '^Version:\s*(\S+)$' } | Select-Object -First 1
if ($pydanticVersionLine -notmatch '^Version:\s*(2\.(12|13)\.\S+)$') {
    throw "Incompatible or missing Pydantic version: $pydanticVersionLine"
}
if ($mcpVersionLine -notmatch '^Version:\s*1\.20\.0$') {
    throw "Unexpected or missing MCP SDK version: $mcpVersionLine"
}

Write-Host $pydanticVersionLine
Write-Host $mcpVersionLine

# Pydantic Settings 2.15+ warns when the MCP SDK's generic Settings.lifespan
# forward reference remains unresolved. The pinned SDK defines Settings before
# FastMCP, so rebuild it after the module import has completed and before the
# local server constructs FastMCP. Keep this guarded and idempotent.
$startupSource = [System.IO.File]::ReadAllText($entryPointPath)
$rebuildMarker = 'FastMCPSettings.model_rebuild()'
if (-not $startupSource.Contains($rebuildMarker)) {
    if ($startupSource.Contains('from mcp.server.fastmcp.server import Settings as FastMCPSettings')) {
        throw 'BepInEx MCP startup patch is incomplete; inspect the local entrypoint before repairing it.'
    }

    $fastMcpImport = 'from mcp.server.fastmcp import FastMCP'
    if ([System.Text.RegularExpressions.Regex]::Matches(
            $startupSource, [System.Text.RegularExpressions.Regex]::Escape($fastMcpImport)).Count -ne 1) {
        throw 'Could not find exactly one expected FastMCP import in the pinned BepInEx MCP entrypoint.'
    }

    $startupPatch = $fastMcpImport + "`r`nfrom mcp.server.fastmcp.server import Settings as FastMCPSettings`r`nFastMCPSettings.model_rebuild()"
    $startupSource = $startupSource.Replace($fastMcpImport, $startupPatch)
    [System.IO.File]::WriteAllText($entryPointPath, $startupSource, [System.Text.UTF8Encoding]::new($false))
}

& $pythonPath $entryPointPath --help | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "BepInEx MCP server import/entrypoint verification failed with exit code $LASTEXITCODE."
}

@'
from mcp.server.fastmcp import FastMCP
from mcp.server.fastmcp.server import Settings as FastMCPSettings
FastMCPSettings.model_rebuild()
assert FastMCPSettings.__pydantic_complete__, "FastMCP Settings forward references remain unresolved"
FastMCP("pydantic-settings-repair-check")
'@ | & $pythonPath -
if ($LASTEXITCODE -ne 0) {
    throw "FastMCP settings forward-reference repair verification failed with exit code $LASTEXITCODE."
}

Write-Host 'BepInEx MCP Python dependencies are repaired, the upstream FastMCP settings reference is rebuilt, and the server imports successfully.'
Write-Host 'This script does not edit Codex configuration, install the Unity bridge, launch the game, or call game tools.'
