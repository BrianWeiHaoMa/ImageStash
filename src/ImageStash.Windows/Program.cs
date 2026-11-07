using Avalonia;
using Avalonia.Controls;
using ImageStash.Seams;
using ImageStash.Windows.Seams;

namespace ImageStash.Windows;

/// <summary>
/// The entry point: holds the session's instance before anything else, builds the seams, and starts Avalonia,
/// whose <see cref="App"/> runs the rest of the start sequence.
/// </summary>
public static class Program
{
    // Held in a static for the process's lifetime: a local would be collected and release the instance (G3).
    private static WindowsOneInstance? instance;

    [STAThread]
    public static int Main(string[] args)
    {
        instance = HoldInstance();
        if (instance is null)
        {
            return 0;
        }

        try
        {
            using var tray = new WindowsTray();
            using var shortcuts = new WindowsShortcuts();
            using var target = new WindowsTarget();
            var seams = new PlatformSeams(tray, new WindowsRecycleBin(), new WindowsStartWithWindows(WindowsStartWithWindows.ProductValueName), new WindowsLocations(), instance,
                new WindowsQuietWindows(tray), new WindowsClipboardAndDrops(), new WindowsDialogsAndExplorer(), shortcuts, target, new WindowsScreen(), new WindowsFonts(), new WindowsBorderDrag());
            return StartAvalonia(seams, args);
        }
        finally
        {
            // Released after the seams, even when one of them fails to let go: a launch that finds the instance free then
            // finds the shortcuts free too, rather than taking them as another program's (G3, G33).
            instance.Dispose();
        }
    }

    /// <summary>The session's instance, or null after asking the running one to show its window; touches no folder (G3, E2).</summary>
    private static WindowsOneInstance? HoldInstance()
    {
        var one = new WindowsOneInstance(WindowsOneInstance.SessionName);
        if (one.TryHold())
        {
            return one;
        }

        one.SignalRunning();
        one.Dispose();
        return null;
    }

    /// <summary>With no window ImageStash keeps running until Quit (F4).</summary>
    private static int StartAvalonia(PlatformSeams seams, string[] args) =>
        AppBuilder.Configure(() => new App(seams))
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
}
