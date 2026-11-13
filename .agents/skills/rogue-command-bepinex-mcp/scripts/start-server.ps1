$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
$pythonPath = Join-Path $repositoryRoot '.tools\BepInExMCP\.venv\Scripts\python.exe'
$serverPath = Join-Path $repositoryRoot 'tools\bepinex_mcp_shared.py'

if (-not (Test-Path -LiteralPath $pythonPath -PathType Leaf)) {
    [Console]::Error.WriteLine("BepInEx MCP Python environment is missing: $pythonPath")
    exit 2
}

if (-not (Test-Path -LiteralPath $serverPath -PathType Leaf)) {
    [Console]::Error.WriteLine("Project BepInEx MCP server is missing: $serverPath")
    exit 2
}

Push-Location $repositoryRoot
$serverExitCode = 1
try {
    & $pythonPath $serverPath
    $serverExitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}
exit $serverExitCode
