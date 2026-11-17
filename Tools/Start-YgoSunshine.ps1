[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string] $MonitorHardwareId
)

$ErrorActionPreference = 'Stop'
$game = Get-Item -LiteralPath (Join-Path $PSScriptRoot '../Builds/Windows/YGOPortable.exe')

Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class YgoStreamWindow
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplayDevices(string name, uint index, ref DisplayDevice device, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect bounds);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    public static bool Covers(IntPtr window, int x, int y, int width, int height)
    {
        Rect bounds;
        return GetWindowRect(window, out bounds) && bounds.Left == x && bounds.Top == y &&
            bounds.Right - bounds.Left == width && bounds.Bottom - bounds.Top == height;
    }
}
'@

# Use physical pixels for both WinForms display bounds and Win32 window placement.
$previousDpiContext = [YgoStreamWindow]::SetThreadDpiAwarenessContext([IntPtr](-4))
try {
    $target = $null
    foreach ($screen in ([System.Windows.Forms.Screen]::AllScreens | Sort-Object { $_.Bounds.X })) {
        for ($index = 0; ; $index++) {
            $monitor = New-Object YgoStreamWindow+DisplayDevice
            $monitor.Size = [Runtime.InteropServices.Marshal]::SizeOf($monitor)
            if (-not [YgoStreamWindow]::EnumDisplayDevices($screen.DeviceName, $index, [ref]$monitor, 0)) { break }
            if (($monitor.Id -split '\\')[1] -ieq $MonitorHardwareId) {
                $target = $screen
                break
            }
        }
        if ($target) { break }
    }
    if (-not $target) { throw "The requested streaming display ($MonitorHardwareId) is not active." }

    $bounds = $target.Bounds
    if (-not $PSCmdlet.ShouldProcess("$($target.DeviceName) $bounds", "Start $($game.Name) for Sunshine")) { return }

    $player = Start-Process -FilePath $game.FullName -WorkingDirectory $game.DirectoryName -PassThru -ArgumentList @(
        '-screen-fullscreen', '0', '-popupwindow', '-screen-width', $bounds.Width, '-screen-height', $bounds.Height
    )
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $stable = 0
    do {
        Start-Sleep -Milliseconds 400
        $player.Refresh()
        if ($player.HasExited) { exit $player.ExitCode }
        $window = $player.MainWindowHandle
        if ($window -eq [IntPtr]::Zero) { continue }
        if ([YgoStreamWindow]::Covers($window, $bounds.X, $bounds.Y, $bounds.Width, $bounds.Height)) {
            $stable++
        } else {
            $stable = 0
            # Resize and show only the Unity window owned by the process we started.
            [void][YgoStreamWindow]::SetWindowPos($window, [IntPtr]::Zero, $bounds.X, $bounds.Y,
                $bounds.Width, $bounds.Height, 0x54)
        }
    } while ($stable -lt 3 -and [DateTime]::UtcNow -lt $deadline)

    if ($stable -ge 3) { [void][YgoStreamWindow]::SetForegroundWindow($window) }
    else { Write-Warning "Could not confirm the window on $($target.DeviceName)." }
    $player.WaitForExit()
    exit $player.ExitCode
} finally {
    if ($previousDpiContext -ne [IntPtr]::Zero) {
        [void][YgoStreamWindow]::SetThreadDpiAwarenessContext($previousDpiContext)
    }
}
