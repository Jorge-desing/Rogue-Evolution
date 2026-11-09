$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "unity/RogueCommandAssetAuthoring"
$unityExe = Join-Path $env:ProgramFiles "Unity/Hub/Editor/2021.3.45f2/Editor/Unity.exe"
$logDirectory = Join-Path $repoRoot "artifacts/canonical-two-level"
$logPath = Join-Path $logDirectory "unity-builder.log"
$lockPath = Join-Path $projectPath "Temp/UnityLockfile"

if (-not (Test-Path -LiteralPath $unityExe -PathType Leaf)) {
    throw "Pinned Unity 2021.3.45f2 editor was not found: $unityExe"
}
if (-not (Test-Path -LiteralPath $projectPath -PathType Container)) {
    throw "RogueCommandAssetAuthoring project is missing: $projectPath"
}
if (Test-Path -LiteralPath $lockPath) {
    throw "The authoring project has a Unity lock file. Close its current editor before the batch build: $lockPath"
}

$projectVersion = Get-Content -LiteralPath (Join-Path $projectPath "ProjectSettings/ProjectVersion.txt") -Raw
if ($projectVersion -notmatch "(?m)^m_EditorVersion: 2021\.3\.45f2\s*$") {
    throw "Authoring project is not pinned to Unity 2021.3.45f2."
}

New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$unityArguments = @(
    "-batchmode",
    "-quit",
    "-projectPath",
    $projectPath,
    "-executeMethod",
    "RogueCommandExpansion.Authoring.CanonicalVisualModuleBuilder.BuildCanonicalModules",
    "-logFile",
    $logPath
)
$argumentText = ($unityArguments | ForEach-Object { '"' + ([string]$_).Replace('"', '\"') + '"' }) -join ' '
$editor = Start-Process -FilePath $unityExe -ArgumentList $argumentText -WindowStyle Hidden -PassThru
if ($null -eq $editor) {
    throw "Could not start the pinned Unity editor: $unityExe"
}
while (-not $editor.WaitForExit(15000)) {
    $editor.Refresh()
    if (Test-Path -LiteralPath $logPath) {
        $lastLogLine = Get-Content -LiteralPath $logPath -Tail 1
        if (-not [String]::IsNullOrWhiteSpace($lastLogLine)) {
            Write-Host $lastLogLine
        }
    }
}
$editor.Refresh()
$unityExitCode = $editor.ExitCode
if ($unityExitCode -ne 0) {
    throw "Unity canonical visual module build failed with exit code $unityExitCode. Log: $logPath"
}
if (Test-Path -LiteralPath $lockPath) {
    throw "Unity returned but left a project lock file; inspect $lockPath before reopening the authoring project."
}

$log = Get-Content -LiteralPath $logPath -Raw
if ($log -notmatch "Built 35 visual-only modules, 28 shared PBR sets, labeled .* referenced pack assets, and built both content packs\.") {
    throw "Unity exited without the canonical module build completion record. Log: $logPath"
}
Write-Host "Unity 2021.3.45f2 built 35 canonical visual modules and both content packs. Log: $logPath"
