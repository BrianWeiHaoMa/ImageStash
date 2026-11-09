using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// Microsoft Edge, the Chromium the chat apps are built on, in app mode on a page of the check's own: its own browser
/// process with a profile of its own under %TEMP%, at (100, 100) on the primary display, until disposed, when the
/// process tree ends and the profile is removed. The page is a textarea holding a word with the focus, or a picture
/// filling the page, which <see cref="CopyImage"/> copies as a person copies a picture in a browser.
/// </summary>
public sealed class EdgeApp : IDisposable
{
    private const string TextareaTitle = "ImageStash check textarea";
    private const string PictureTitle = "ImageStash check picture";
    private const ushort VkReturn = 0x0D;
    private const ushort VkEscape = 0x1B;
    private const ushort VkDown = 0x28;
    private const ushort VkY = 0x59;
    private const nint HwndTopmost = -1;
    private const nint HwndNoTopmost = -2;
    // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE.
    private const uint KeepPlaceAndSizeWithoutActivating = 0x0001 | 0x0002 | 0x0010;
    private readonly Process process;
    private readonly string profile;

    private EdgeApp(Process process, string profile, nint window)
    {
        this.process = process;
        this.profile = profile;
        Window = window;
    }

    /// <summary>Its app window's handle.</summary>
    public nint Window { get; }

    public int ProcessId => process.Id;

    /// <summary>Starts it on the textarea's page and waits until its window shows; null when Edge is not installed on this PC.</summary>
    public static EdgeApp? Textarea() =>
        Start(TextareaTitle, "data:text/html,%3Ctitle%3E" + Uri.EscapeDataString(TextareaTitle) + "%3C/title%3E%3Ctextarea%20autofocus%20style=%22font-size:20px;width:90%25;height:200px%22%3Ereaction%3C/textarea%3E");

    /// <summary>
    /// Starts it on a page showing <paramref name="png"/> stretched over the whole page, and waits until the picture
    /// has loaded; null when Edge is not installed on this PC.
    /// </summary>
    public static EdgeApp? Picture(string png)
    {
        var page = $"<title>ImageStash check page</title><body style=\"margin:0;background:#808080\"><img src=\"data:image/png;base64,{Convert.ToBase64String(File.ReadAllBytes(png))}\" style=\"display:block;width:100vw;height:100vh;image-rendering:pixelated\" onload=\"document.title='{PictureTitle}'\">";
        return Start(PictureTitle, "data:text/html," + Uri.EscapeDataString(page));
    }

    /// <summary>
    /// Copy image from the picture's own context menu with the real pointer and keys: a click on its title bar brings
    /// Edge forward, a right-click on the picture opens its menu, and Copy image is chosen by its access key, Y in Edge
    /// 155, else by walking down the menu to it; true once the clipboard holds the "PNG" Edge places. Whatever a wrong
    /// entry opens stays inside Edge's own process tree, which <see cref="Dispose"/> ends.
    /// </summary>
    public bool CopyImage()
    {
        Assert.IsTrue(GetWindowRect(Window, out var bounds));
        // Started from a program that is not in front, Edge opens behind whatever the user has there, which would take
        // the click: above every window for the click, then back among the others, in front of them.
        Assert.IsTrue(SetWindowPos(Window, HwndTopmost, 0, 0, 0, 0, KeepPlaceAndSizeWithoutActivating));
        TestMouse.Click(new PixelPoint((bounds.Left + bounds.Right) / 2, bounds.Top + DisplayPlaces.Scaled(DisplayPlaces.Primary(), 12)));
        FrontWindow.WaitUntilInFront(Window);
        Assert.IsTrue(SetWindowPos(Window, HwndNoTopmost, 0, 0, 0, 0, KeepPlaceAndSizeWithoutActivating));
        var picture = new PixelPoint((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2);
        return FromItsMenu(picture, VkY)
            || new[] { 3, 5, 6, 7 }.Any(steps => FromItsMenu(picture, [.. Enumerable.Repeat(VkDown, steps), VkReturn]));
    }

    public void Dispose()
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(10_000);
        }

        process.Dispose();
        // Edge's last child processes can hold the profile for a moment after the browser ends.
        for (var attempt = 0; attempt < 20 && Directory.Exists(profile); attempt++)
        {
            try
            {
                Directory.Delete(profile, recursive: true);
            }
            catch (IOException)
            {
                Thread.Sleep(250);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(250);
            }
        }
    }

    private static EdgeApp? Start(string title, string page)
    {
        var edge = new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles }
            .Select(folder => Path.Combine(Environment.GetFolderPath(folder), @"Microsoft\Edge\Application\msedge.exe"))
            .FirstOrDefault(File.Exists);
        if (edge is null)
        {
            return null;
        }

        var profile = Path.Combine(Path.GetTempPath(), "ImageStash-check-edge-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo(edge) { UseShellExecute = false };
        foreach (var argument in new[] { $"--user-data-dir={profile}", "--no-first-run", "--no-default-browser-check", "--disable-sync", "--disable-background-networking", "--disable-component-update", "--window-position=100,100", "--window-size=700,500", $"--app={page}" })
        {
            start.ArgumentList.Add(argument);
        }

        var process = Process.Start(start)!;
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            process.Refresh();
            if (process.HasExited)
            {
                break;
            }

            if (process.MainWindowTitle == title)
            {
                return new EdgeApp(process, profile, process.MainWindowHandle);
            }

            Thread.Sleep(100);
        }

        new EdgeApp(process, profile, 0).Dispose();
        Assert.Fail("Edge's app window did not show");
        return null;
    }

    private bool FromItsMenu(PixelPoint picture, params ushort[] keys)
    {
        TestClipboard.Empty();
        var shown = ShownWindows();
        TestMouse.RightClick(picture);
        // The menu is a window of Edge's own that opens a moment after the click and takes the keys only once it shows.
        var clock = Stopwatch.StartNew();
        while (ShownWindows() <= shown && clock.ElapsedMilliseconds < 3000)
        {
            Thread.Sleep(20);
        }

        Thread.Sleep(150);
        foreach (var key in keys)
        {
            TestHotKeys.Tap(key);
        }

        for (clock.Restart(); clock.ElapsedMilliseconds < 3000; Thread.Sleep(50))
        {
            if (TestClipboard.Holds(TestClipboard.Png))
            {
                return true;
            }
        }

        TestHotKeys.Tap(VkEscape);
        return false;
    }

    private int ShownWindows()
    {
        var count = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            count += owner == process.Id && IsWindowVisible(window) ? 1 : 0;
            return true;
        }, 0);
        return count;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    private delegate bool EnumProc(nint window, nint data);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumProc proc, nint data);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
