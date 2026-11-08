<#
.SYNOPSIS
Reports the desktop a CI job runs on and, on a GitHub Actions runner, makes it one the desktop checks can use. It prints
the current Windows session (query session's line for it, its id, whether the process is interactive and can open the
input desktop) and the primary display's size in physical pixels and its scale, before and after.

While GITHUB_ACTIONS is true, a display smaller than 1280 x 800 is raised to 1280 x 800 at 32 bits, the smallest
display the desktop checks run on (they place their windows from the display, so a larger one serves too), by
ChangeDisplaySettingsEx, kept in the registry for the session; a larger display is left as it is. The script then
exits 1, naming what it found, when the session is session 0 or not interactive, when it cannot open the input desktop,
or when the display is still smaller than 1280 x 800, the smallest the desktop checks run on, so a job stops on one
readable line instead of on checks that time out. -Test, on a runner or anywhere else, makes the same decisions but only
asks the adapter whether it would accept each size (CDS_TEST), changing nothing. Off a runner and without -Test it only
reports, changes nothing and exits 0, so a person's display is never changed.

.EXAMPLE
powershell.exe -NoProfile -File tools/runner-desktop.ps1
#>
param([switch] $Test)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

// The primary display's mode and scale, and a change of its mode, read and made in physical pixels.
public static class RunnerDisplay
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODEW
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettingsW(string device, int mode, ref DEVMODEW devMode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsExW(string device, ref DEVMODEW devMode, IntPtr window, uint flags, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);

    private const int EnumCurrentSettings = -1;
    private const int DmBitsPerPel = 0x40000, DmPelsWidth = 0x80000, DmPelsHeight = 0x100000;

    private static DEVMODEW Current()
    {
        // Per-monitor aware (version 2), so the scale read below is the display's own, not the 96 DPI of an unaware thread.
        SetThreadDpiAwarenessContext(new IntPtr(-4));
        var mode = new DEVMODEW();
        mode.dmSize = (short)Marshal.SizeOf(typeof(DEVMODEW));
        if (!EnumDisplaySettingsW(null, EnumCurrentSettings, ref mode)) { throw new InvalidOperationException("EnumDisplaySettings failed"); }
        return mode;
    }

    public static int Width() { return Current().dmPelsWidth; }

    public static int Height() { return Current().dmPelsHeight; }

    public static int Bits() { return Current().dmBitsPerPel; }

    /// <summary>The primary display's scale in percent, from its effective DPI.</summary>
    public static int ScalePercent()
    {
        SetThreadDpiAwarenessContext(new IntPtr(-4));
        // MONITOR_DEFAULTTOPRIMARY; the origin is always on the primary display.
        var monitor = MonitorFromPoint(new POINT(), 1);
        uint dpiX, dpiY;
        if (GetDpiForMonitor(monitor, 0, out dpiX, out dpiY) != 0) { return 0; }
        return (int)Math.Round(dpiX * 100.0 / 96);
    }

    /// <summary>Asks for the primary display at this size and 32 bits: DISP_CHANGE_SUCCESSFUL is 0. CDS_TEST (2) only
    /// asks whether the adapter would accept it; CDS_UPDATEREGISTRY (1) changes it and keeps it.</summary>
    public static int Change(int width, int height, bool test)
    {
        var mode = Current();
        mode.dmPelsWidth = width;
        mode.dmPelsHeight = height;
        mode.dmBitsPerPel = 32;
        mode.dmFields = DmPelsWidth | DmPelsHeight | DmBitsPerPel;
        return ChangeDisplaySettingsExW(null, ref mode, IntPtr.Zero, test ? 2u : 1u, IntPtr.Zero);
    }

    /// <summary>Whether this process can open the desktop that receives input, which real keystrokes and clicks need.</summary>
    public static bool CanOpenInputDesktop()
    {
        // DESKTOP_READOBJECTS.
        var desktop = OpenInputDesktop(0, false, 0x0001);
        if (desktop == IntPtr.Zero) { return false; }
        CloseDesktop(desktop);
        return true;
    }
}
'@

function Write-Display([string] $when) {
    Write-Host ("runner-desktop: display {0}: {1} x {2} at {3} bits, scale {4} %" -f $when, [RunnerDisplay]::Width(), [RunnerDisplay]::Height(), [RunnerDisplay]::Bits(), [RunnerDisplay]::ScalePercent())
}

$sessionId = [Diagnostics.Process]::GetCurrentProcess().SessionId
$sessionLine = 'unavailable (no query.exe)'
if (Get-Command query.exe -ErrorAction SilentlyContinue) {
    $marked = @(query.exe session 2>$null | Where-Object { $_ -like '>*' })
    $sessionLine = if ($marked.Count -gt 0) { $marked[0].Trim() } else { 'not listed' }
}
$interactive = [Environment]::UserInteractive
$inputDesktop = [RunnerDisplay]::CanOpenInputDesktop()
Write-Host "runner-desktop: session $sessionId, query session: $sessionLine"
Write-Host "runner-desktop: UserInteractive $interactive, input desktop opened $inputDesktop"
Write-Display 'before'

$onRunner = $env:GITHUB_ACTIONS -eq 'true'
if (-not $onRunner -and -not $Test) {
    Write-Host 'runner-desktop: not on a GitHub Actions runner, so the display is left as it is'
    exit 0
}

$problems = New-Object System.Collections.Generic.List[string]
if ($sessionId -eq 0) { $problems.Add('the job runs in session 0, the services session, which has no desktop to click or capture') }
if (-not $interactive) { $problems.Add('the process is not interactive') }
if (-not $inputDesktop) { $problems.Add('the input desktop cannot be opened, so real keystrokes and clicks reach nothing') }

$width = [RunnerDisplay]::Width()
$height = [RunnerDisplay]::Height()
$verb = if ($Test) { 'would accept' } else { 'changed to' }
if ($width -lt 1280 -or $height -lt 800) {
    $result = [RunnerDisplay]::Change(1280, 800, [bool] $Test)
    if ($result -eq 0) {
        Write-Host "runner-desktop: the adapter $verb 1280 x 800"
    } else {
        Write-Host "runner-desktop: the adapter refused 1280 x 800 (ChangeDisplaySettingsEx returned $result)"
    }
}
Write-Display 'after'
$width = [RunnerDisplay]::Width()
$height = [RunnerDisplay]::Height()
if ($width -lt 1280 -or $height -lt 800) {
    $problems.Add("the display is $width x $height, smaller than 1280 x 800, the smallest the desktop checks run on")
}
if ($problems.Count -gt 0) {
    foreach ($problem in $problems) { Write-Host "runner-desktop: unusable: $problem" }
    exit 1
}
Write-Host 'runner-desktop: the desktop is usable for the desktop checks'
exit 0
