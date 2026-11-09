<#
.SYNOPSIS
Lists every display's scale and sets one, for running the desktop checks at another scale. Without -Set it prints one
line per active display: its index, its name, its bounds in physical pixels, whether it is the primary, and its
recommended, current and offered scales, then exits 0, changing nothing.

-Set <percent> sets the scale of the display -Source names (its index in the listing), the primary display when
-Source is left out, through DisplayConfigSetDeviceInfo, as Settings > Display > Scale does, unelevated and at once.
It then reads the scale back through GetDpiForMonitor, for up to three seconds, prints the scale it replaced and the
scale it read back, and exits 0 only when the read-back is the scale asked for. A percent the display is not offered,
an index that names no display, a refused set or a read-back that differs exits 1 naming it; nothing else is changed,
and nothing is changed at all when the percent is not offered.

.EXAMPLE
powershell.exe -NoProfile -File tools/display-scale.ps1
.EXAMPLE
powershell.exe -NoProfile -File tools/display-scale.ps1 -Set 150
.EXAMPLE
powershell.exe -NoProfile -File tools/display-scale.ps1 -Set 100 -Source 0
#>
param([int] $Set, [int] $Source)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// The active displays with their scales, read and set through the display configuration API, and each display's
// effective DPI and bounds read on a per-monitor-aware thread, so the bounds are physical pixels.
public static class DisplayScale
{
    // The scales Windows steps through, in percent; a display's scales are indices into this table relative to its
    // recommended one.
    public static readonly int[] Table = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

    [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] struct PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
    [StructLayout(LayoutKind.Sequential)] struct PATH_TARGET_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation; public uint scaling; public uint rrNum; public uint rrDen; public uint scanLineOrdering; public int targetAvailable; public uint statusFlags; }
    [StructLayout(LayoutKind.Sequential)] struct PATH_INFO { public PATH_SOURCE_INFO sourceInfo; public PATH_TARGET_INFO targetInfo; public uint flags; }
    [StructLayout(LayoutKind.Sequential, Size = 64)] struct MODE_INFO { public uint infoType; public uint id; public LUID adapterId; }
    [StructLayout(LayoutKind.Sequential)] struct GET_DPI { public int type; public uint size; public LUID adapterId; public uint id; public int minScaleRel; public int curScaleRel; public int maxScaleRel; }
    [StructLayout(LayoutKind.Sequential)] struct SET_DPI { public int type; public uint size; public LUID adapterId; public uint id; public int scaleRel; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct SOURCE_NAME { public int type; public uint size; public LUID adapterId; public uint id; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct MONITORINFOEX { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }
    delegate bool MonitorEnum(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PATH_INFO[] paths, ref uint numModes, [Out] MODE_INFO[] modes, IntPtr currentTopology);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref GET_DPI info);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref SOURCE_NAME info);
    [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(ref SET_DPI info);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnum callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFOEX info);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    public sealed class Display
    {
        public int Index; public string Name; public LUID Adapter; public uint Id;
        public int Recommended, Current, Offered;
        public int X, Y, Width, Height; public bool Primary; public int EffectivePercent;
    }

    public static List<Display> Displays()
    {
        const uint activeOnly = 2;
        uint pathCount, modeCount;
        Check(GetDisplayConfigBufferSizes(activeOnly, out pathCount, out modeCount), "GetDisplayConfigBufferSizes");
        var paths = new PATH_INFO[pathCount];
        var modes = new MODE_INFO[modeCount];
        Check(QueryDisplayConfig(activeOnly, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero), "QueryDisplayConfig");
        var monitors = Monitors();
        var displays = new List<Display>();
        for (var i = 0; i < pathCount; i++)
        {
            var source = paths[i].sourceInfo;
            var name = new SOURCE_NAME { type = 1, adapterId = source.adapterId, id = source.id };
            name.size = (uint)Marshal.SizeOf(name);
            Check(DisplayConfigGetDeviceInfo(ref name), "reading a display's name");
            var dpi = new GET_DPI { type = -3, adapterId = source.adapterId, id = source.id };
            dpi.size = (uint)Marshal.SizeOf(dpi);
            Check(DisplayConfigGetDeviceInfo(ref dpi), "reading " + name.viewGdiDeviceName + "'s scale");
            var recommended = -dpi.minScaleRel;
            var display = new Display
            {
                Index = i, Name = name.viewGdiDeviceName, Adapter = source.adapterId, Id = source.id,
                Recommended = Table[recommended],
                Current = Table[recommended + dpi.curScaleRel],
                Offered = Table[recommended + dpi.maxScaleRel],
            };
            MONITORINFOEX info;
            IntPtr monitor;
            if (monitors.TryGetValue(display.Name, out monitor) && Info(monitor, out info))
            {
                display.X = info.rcMonitor.Left; display.Y = info.rcMonitor.Top;
                display.Width = info.rcMonitor.Right - info.rcMonitor.Left; display.Height = info.rcMonitor.Bottom - info.rcMonitor.Top;
                display.Primary = (info.dwFlags & 1) != 0;
                display.EffectivePercent = EffectivePercent(monitor);
            }

            displays.Add(display);
        }

        return displays;
    }

    // Sets the display's scale to percent; 0 when Windows took it, else its error code.
    public static int SetScale(Display display, int percent)
    {
        var set = new SET_DPI { type = -4, adapterId = display.Adapter, id = display.Id, scaleRel = Array.IndexOf(Table, percent) - Array.IndexOf(Table, display.Recommended) };
        set.size = (uint)Marshal.SizeOf(set);
        return DisplayConfigSetDeviceInfo(ref set);
    }

    // The display's effective DPI as a percent of 96, read now, as a per-monitor-aware window on it sees it.
    public static int EffectivePercentOf(string name)
    {
        IntPtr monitor;
        return Monitors().TryGetValue(name, out monitor) ? EffectivePercent(monitor) : -1;
    }

    static Dictionary<string, IntPtr> Monitors()
    {
        var found = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, dc, rect, data) =>
            {
                MONITORINFOEX info;
                if (Info(monitor, out info)) found[info.szDevice] = monitor;
                return true;
            }, IntPtr.Zero);
        }
        finally
        {
            SetThreadDpiAwarenessContext(previous);
        }

        return found;
    }

    static bool Info(IntPtr monitor, out MONITORINFOEX info)
    {
        info = new MONITORINFOEX();
        info.cbSize = Marshal.SizeOf(info);
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try { return GetMonitorInfoW(monitor, ref info); }
        finally { SetThreadDpiAwarenessContext(previous); }
    }

    static int EffectivePercent(IntPtr monitor)
    {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            uint x, y;
            return GetDpiForMonitor(monitor, 0, out x, out y) == 0 ? (int)Math.Round(x * 100 / 96.0) : -1;
        }
        finally
        {
            SetThreadDpiAwarenessContext(previous);
        }
    }

    static void Check(int code, string what)
    {
        if (code != 0) throw new InvalidOperationException(what + " failed with error " + code);
    }
}
'@

function Write-Displays($displays) {
    foreach ($display in $displays) {
        $primary = if ($display.Primary) { 'primary' } else { 'secondary' }
        "[$($display.Index)] $($display.Name) $($display.Width) x $($display.Height) at ($($display.X), $($display.Y)), $primary, " +
            "recommended $($display.Recommended) %, current $($display.Current) %, offered 100 % to $($display.Offered) %"
    }
}

$displays = [DisplayScale]::Displays()
if (-not $PSBoundParameters.ContainsKey('Set')) {
    Write-Displays $displays
    exit 0
}

$display = if ($PSBoundParameters.ContainsKey('Source')) { $displays | Where-Object { $_.Index -eq $Source } } else { $displays | Where-Object { $_.Primary } }
if (-not $display) {
    $named = if ($PSBoundParameters.ContainsKey('Source')) { "index $Source" } else { 'the primary display' }
    Write-Output "display-scale: no active display at $named"
    Write-Displays $displays
    exit 1
}

if ([array]::IndexOf([DisplayScale]::Table, $Set) -lt 0 -or $Set -gt $display.Offered) {
    Write-Output "display-scale: $($display.Name) is offered 100 % to $($display.Offered) % in Windows' steps, not $Set %; nothing changed"
    exit 1
}

$replaced = $display.Current
$code = [DisplayScale]::SetScale($display, $Set)
if ($code -ne 0) {
    Write-Output "display-scale: Windows refused $Set % on $($display.Name) with error $code; it stays at $replaced %"
    exit 1
}

# Windows applies the scale asynchronously: read it back as a per-monitor-aware window sees it.
$readBack = -1
$deadline = [DateTime]::UtcNow.AddSeconds(3)
do {
    $readBack = [DisplayScale]::EffectivePercentOf($display.Name)
    if ($readBack -eq $Set) { break }
    Start-Sleep -Milliseconds 100
} while ([DateTime]::UtcNow -lt $deadline)

if ($readBack -ne $Set) {
    Write-Output "display-scale: asked $($display.Name) for $Set % (it was at $replaced %), but GetDpiForMonitor reads $readBack % after three seconds"
    exit 1
}

Write-Output "display-scale: $($display.Name) set to $Set %, read back $readBack % ($([int]($readBack * 96 / 100)) DPI); it was at $replaced %"
exit 0
