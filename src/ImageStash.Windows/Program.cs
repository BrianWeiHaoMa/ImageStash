using Avalonia;
using Avalonia.Controls;
using ImageStash.Seams;
using ImageStash.Windows.Seams;

namespace ImageStash.Windows;

/// <summary>
/// The entry point: holds the session's instance before anything else, builds the seams, and starts Avalonia,
/// whose <see cref="App"/> runs the rest of the start sequence, handed the arguments that tell the start at sign-in
/// from a launch by hand (G3).
/// </summary>
public static class Program
{
    // Held in a static for the process's lifetime: a local would be collected and release the instance (G3).
    private static WindowsOneInstance? instance;

    [STAThread]
    public static int Main(string[] args)
    {
        instance = HoldInstance(args);
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
                new WindowsQuietWindows(tray), new WindowsClipboardAndDrops(), new WindowsDialogsAndExplorer(), shortcuts, target, new WindowsScreen(), new WindowsFonts(), new WindowsBorderDrag(), new WindowsWindowOrder());
            return StartAvalonia(seams, args);
        }
        finally
        {
            // Released after the seams, even when one of them fails to let go: a launch that finds the instance free then
            // finds the shortcuts free too, rather than taking them as another program's (G3, G33).
            instance.Dispose();
        }
    }

    /// <summary>
    /// The session's instance, or null once another process holds it, which a launch by hand has asked to show its window
    /// and the start at sign-in has not; touches no folder (G3, E2).
    /// </summary>
    private static WindowsOneInstance? HoldInstance(string[] args)
    {
        var one = new WindowsOneInstance(WindowsOneInstance.SessionName);
        if (Launch.HoldInstance(one, args))
        {
            return one;
        }

        one.Dispose();
        return null;
    }

    /// <summary>With no window ImageStash keeps running until Quit (F4).</summary>
    private static int StartAvalonia(PlatformSeams seams, string[] args) =>
        AppBuilder.Configure(() => new App(seams, args))
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
}
