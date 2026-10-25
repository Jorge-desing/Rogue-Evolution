[CmdletBinding()]
param(
    [int]$ProcessId = 0,
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($ProcessId -eq 0) {
    $gameProcesses = @(Get-CimInstance -ClassName Win32_Process | Where-Object { $_.Name -eq 'Rogue Command.exe' })
    if ($gameProcesses.Count -ne 1) {
        throw "Expected exactly one Rogue Command.exe process; found $($gameProcesses.Count). Supply -ProcessId when multiple instances exist."
    }
    $ProcessId = [int]$gameProcesses[0].ProcessId
}

$process = Get-Process -Id $ProcessId -ErrorAction Stop
$process.Refresh()
$windowHandle = $process.MainWindowHandle
if ($windowHandle -eq [IntPtr]::Zero) {
    throw "Process $ProcessId has no main window handle."
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutputPath = Join-Path $env:TEMP "rogue-command-$stamp.png"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($OutputPath)
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

if (-not ('RclScreenshot.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace RclScreenshot
{
    public static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
    }
}
'@
}

$rect = New-Object RclScreenshot.Native+Rect
if (-not [RclScreenshot.Native]::GetWindowRect($windowHandle, [ref]$rect)) {
    $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    throw "GetWindowRect failed with Win32 error $errorCode."
}

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) {
    throw "The target window has invalid dimensions: ${width}x${height}."
}

$foregroundBefore = [RclScreenshot.Native]::GetForegroundWindow()
$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = $null
$deviceContext = [IntPtr]::Zero
$printSucceeded = $false
try {
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $deviceContext = $graphics.GetHdc()
        # PW_RENDERFULLCONTENT (2); this captures the target HWND without activating it.
        $printSucceeded = [RclScreenshot.Native]::PrintWindow($windowHandle, $deviceContext, [uint32]2)
    }
    finally {
        if ($deviceContext -ne [IntPtr]::Zero) {
            $graphics.ReleaseHdc($deviceContext)
            $deviceContext = [IntPtr]::Zero
        }
        $graphics.Dispose()
        $graphics = $null
    }

    $foregroundAfter = [RclScreenshot.Native]::GetForegroundWindow()
    if (-not $printSucceeded) {
        throw 'PrintWindow returned false; no screenshot was saved.'
    }

    $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    if ($deviceContext -ne [IntPtr]::Zero -and $null -ne $graphics) {
        $graphics.ReleaseHdc($deviceContext)
    }
    if ($null -ne $graphics) {
        $graphics.Dispose()
    }
    $bitmap.Dispose()
}

[pscustomobject]@{
    ProcessId = $ProcessId
    WindowHandle = ('0x{0:X}' -f $windowHandle.ToInt64())
    Title = $process.MainWindowTitle
    Width = $width
    Height = $height
    PrintWindowSucceeded = $printSucceeded
    ForegroundBefore = ('0x{0:X}' -f $foregroundBefore.ToInt64())
    ForegroundAfter = ('0x{0:X}' -f $foregroundAfter.ToInt64())
    ForegroundUnchanged = ($foregroundBefore -eq $foregroundAfter)
    OutputPath = $OutputPath
}
