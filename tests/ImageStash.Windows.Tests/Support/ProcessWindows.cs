using System.Runtime.InteropServices;

namespace ImageStash.Windows.Tests.Support;

/// <summary>The top-level windows of the checks' own process, as Windows lists them: F4's count of windows.</summary>
public static class ProcessWindows
{
    /// <summary>How many visible top-level windows this process has, which an overlay inside the stash window must not raise.</summary>
    public static int Visible() => VisibleHandles().Count;

    /// <summary>The visible top-level windows of this process.</summary>
    public static List<nint> VisibleHandles()
    {
        var handles = new List<nint>();
        var self = (uint)Environment.ProcessId;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == self && IsWindowVisible(window))
            {
                handles.Add(window);
            }

            return true;
        }, 0);
        return handles;
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);
}
