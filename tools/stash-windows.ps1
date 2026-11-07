<#
.SYNOPSIS
The StashWindows type the published checks and the installer check share, dot-sourced by each: a running copy's
windows as Windows sees them, and clicks posted to its tray icon.
#>
# The process's top-level windows as Windows sees them: its visible ones, and Avalonia's hidden platform window, which
# receives the tray icon's messages (Avalonia 12.1.4 TrayIconImpl: WM_USER + 1024, wParam the icon's ID, 1 for the
# process's first and only icon, lParam the mouse message).
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class StashWindows
{
    private delegate bool EnumProc(IntPtr window, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr window, StringBuilder name, int length);
    [DllImport("user32.dll")] private static extern int GetWindowLongW(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static List<IntPtr> Of(int processId)
    {
        var found = new List<IntPtr>();
        EnumWindows(delegate (IntPtr window, IntPtr data)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == processId) { found.Add(window); }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Each visible window: "pinned" for one with a title bar, "flyout" for one without, joined by commas; "" for none.</summary>
    public static string Showing(int processId)
    {
        var shown = new List<string>();
        foreach (var window in Of(processId))
        {
            if (IsWindowVisible(window))
            {
                // GWL_STYLE has WS_CAPTION.
                shown.Add((GetWindowLongW(window, -16) & 0x00C00000) == 0x00C00000 ? "pinned" : "flyout");
            }
        }
        return string.Join(",", shown);
    }

    private static IntPtr PlatformWindow(int processId)
    {
        var platform = IntPtr.Zero;
        foreach (var window in Of(processId))
        {
            var name = new StringBuilder(256);
            GetClassNameW(window, name, name.Capacity);
            if (name.ToString().StartsWith("AvaloniaMessageWindow ")) { platform = window; }
        }
        return platform;
    }

    /// <summary>Posts a left click on the tray icon and returns the milliseconds until a window shows, or -1.</summary>
    public static long ClickTrayIcon(int processId, int timeoutMs)
    {
        var platform = PlatformWindow(processId);
        if (platform == IntPtr.Zero) { return -1; }
        var clock = Stopwatch.StartNew();
        PostMessageW(platform, 0x0400 + 1024, new IntPtr(1), new IntPtr(0x0202));
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            if (Showing(processId) != "") { return clock.ElapsedMilliseconds; }
            Thread.Sleep(5);
        }
        return -1;
    }

    /// <summary>Posts a right click on the tray icon, which opens its menu; false when the process has no platform window.</summary>
    public static bool RightClickTrayIcon(int processId)
    {
        var platform = PlatformWindow(processId);
        return platform != IntPtr.Zero && PostMessageW(platform, 0x0400 + 1024, new IntPtr(1), new IntPtr(0x0205));
    }

    /// <summary>Posts Enter, down and up, to the process's visible window; false when none shows.</summary>
    public static bool PostEnter(int processId)
    {
        foreach (var window in Of(processId))
        {
            if (!IsWindowVisible(window)) { continue; }
            // WM_KEYDOWN and WM_KEYUP for VK_RETURN, its scan code 0x1C in the key data.
            PostMessageW(window, 0x0100, new IntPtr(0x0D), new IntPtr(0x001C0001));
            PostMessageW(window, 0x0101, new IntPtr(0x0D), new IntPtr(unchecked((int)0xC01C0001)));
            return true;
        }
        return false;
    }
}
'@
