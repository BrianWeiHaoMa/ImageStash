using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// The shell's Open-with dialog as the checks meet it (S16): a window of the shell's own OpenWith.exe host, of the class
/// "Open With", the window in front while it is up, and absent from the windows EnumWindows lists, so it is looked for as
/// the window in front, then by its class, then among the desktop window's children. It is closed as a cancel, which
/// starts no program: WM_CLOSE posted to it, then Esc, then the system menu's Close. It also closes itself, as a cancel,
/// once another window comes to the front. Made before a check opens the dialog, it fails the check while a dialog is
/// already up, which is not the check's, and remembers the hosts already running; disposed, it closes a dialog still up
/// and ends each host started since that is still running, as one was seen left after its dialog.
/// </summary>
public sealed class OpenWithDialog : IDisposable
{
    private const string ClassName = "Open With";
    private const string HostName = "OpenWith";
    private const uint WmClose = 0x0010;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysCommand = 0x0112;
    private const nint Escape = 0x1B;
    private const nint ScClose = 0xF060;

    private readonly HashSet<int> hostsBefore;

    /// <summary>Made before the check opens the dialog: one already up is not the check's, so the check fails rather than close it.</summary>
    public OpenWithDialog()
    {
        Assert.AreEqual((nint)0, Find(), "an Open-with dialog is already up; these checks close only their own, so close it first");
        hostsBefore = Hosts();
    }

    /// <summary>
    /// Waits up to ten seconds for the dialog to be the window in front, failing the check if it never is (a dialog found
    /// elsewhere is named in the failure); its window.
    /// </summary>
    public static async Task<nint> InFront()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var front = FrontWindow.GetForegroundWindow();
            if (front != 0 && ClassOf(front) == ClassName)
            {
                return front;
            }

            Assert.IsLessThan(deadline, DateTime.UtcNow, $"no window of class \"{ClassName}\" came to the front; {front:X} ({ClassOf(front)}) is in front, the dialog found elsewhere: {Find():X}");
            await Task.Delay(20);
        }
    }

    /// <summary>The dialog's window while one is up, wherever it is found; 0 while none is.</summary>
    public static nint Find()
    {
        var front = FrontWindow.GetForegroundWindow();
        if (front != 0 && ClassOf(front) == ClassName)
        {
            return front;
        }

        var found = FindWindowW(ClassName, null);
        if (found != 0)
        {
            return found;
        }

        nint child = 0;
        EnumChildWindows(GetDesktopWindow(), (window, _) =>
        {
            if (ClassOf(window) == ClassName && IsWindowVisible(window))
            {
                child = window;
                return false;
            }

            return true;
        }, 0);
        return child;
    }

    /// <summary>
    /// Whether <paramref name="window"/> takes input: the window the dialog was asked for does not while it is up, since the
    /// shell makes the dialog modal to it by disabling it, not by owning it (observed: no owner, a topmost tool window
    /// centred on the display).
    /// </summary>
    public static bool TakesInput(nint window) => IsWindowEnabled(window);

    /// <summary>The name of the process whose window <paramref name="window"/> is.</summary>
    public static string ProcessOf(nint window)
    {
        GetWindowThreadProcessId(window, out var id);
        using var process = Process.GetProcessById((int)id);
        return process.ProcessName;
    }

    /// <summary>Closes the dialog as a cancel and waits until its window has gone, failing the check if it stays.</summary>
    public static async Task Close(nint dialog)
    {
        PostMessageW(dialog, WmClose, 0, 0);
        if (await Gone(dialog, TimeSpan.FromSeconds(3)))
        {
            return;
        }

        PostMessageW(dialog, WmKeyDown, Escape, 0);
        PostMessageW(dialog, WmKeyUp, Escape, 0);
        if (await Gone(dialog, TimeSpan.FromSeconds(3)))
        {
            return;
        }

        PostMessageW(dialog, WmSysCommand, ScClose, 0);
        Assert.IsTrue(await Gone(dialog, TimeSpan.FromSeconds(4)), "the Open-with dialog did not close");
    }

    /// <summary>Whether a dialog's window is still up.</summary>
    public static bool Up(nint dialog) => IsWindow(dialog) && IsWindowVisible(dialog);

    public void Dispose()
    {
        try
        {
            // A check that failed with the dialog up closes it, so it covers no later check (F4).
            for (var attempt = 0; attempt < 3 && Find() is var dialog and not 0; attempt++)
            {
                Close(dialog).GetAwaiter().GetResult();
            }
        }
        finally
        {
            // A host started since and still running is the check's: ended, which also takes down a dialog that would not close.
            foreach (var id in Hosts().Except(hostsBefore))
            {
                try
                {
                    using var host = Process.GetProcessById(id);
                    host.Kill();
                    host.WaitForExit(5000);
                }
                catch (ArgumentException)
                {
                    // Ended by itself meanwhile.
                }
                catch (InvalidOperationException)
                {
                    // Ended by itself meanwhile.
                }
            }
        }
    }

    private static async Task<bool> Gone(nint dialog, TimeSpan within)
    {
        var deadline = DateTime.UtcNow + within;
        while (Up(dialog))
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            await Task.Delay(20);
        }

        return true;
    }

    /// <summary>The Open-with hosts running in this Windows session now, by process id.</summary>
    private static HashSet<int> Hosts()
    {
        using var current = Process.GetCurrentProcess();
        var session = current.SessionId;
        var hosts = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName(HostName))
        {
            using (process)
            {
                if (process.SessionId == session)
                {
                    hosts.Add(process.Id);
                }
            }
        }

        return hosts;
    }

    private static string ClassOf(nint window)
    {
        var name = new StringBuilder(256);
        return window != 0 && GetClassNameW(window, name, name.Capacity) > 0 ? name.ToString() : "";
    }

    private delegate bool EnumProc(nint window, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetClassNameW(nint window, StringBuilder name, int length);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint FindWindowW(string? className, string? windowName);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint parent, EnumProc proc, nint parameter);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetDesktopWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
}
