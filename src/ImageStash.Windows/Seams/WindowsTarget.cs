using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using ImageStash.Seams;

namespace ImageStash.Windows.Seams;

/// <summary>
/// The paste target on Windows (G8, S6, S15). A foreground event hook, out of context, on a thread of the seam's own
/// with its own message loop, keeps the latest windows reported in front, each read under G8's rule as it came, so the
/// window that had focus just before ImageStash's is known whichever thread asks and however busy the UI thread is;
/// the window in front now is read directly. ImageStash's own windows are known by their process, the desktop, the
/// taskbar and the shell's task switcher, which G8 passes over, by the shell's window classes. The app's name is
/// its executable's file description, else its file name; for the application frame host, the hosted app's. Input
/// goes only through SendInput: the target is brought to the front and sent Ctrl down, V down, V up, Ctrl up, and
/// nothing else (N3, R6). The caret of the target's focused control is found by <see cref="WindowsCaret"/>.
/// </summary>
public sealed class WindowsTarget : ITarget, IDisposable
{
    // The shell's own windows that are the desktop or the taskbar, which G8 makes no target: the desktop's Progman and
    // WorkerW, the taskbars, and Windows 11's overflow of the notification area, observed on build 26300 as the window
    // in front, after Shell_TrayWnd, once Show hidden icons is clicked.
    private static readonly HashSet<string> DesktopAndTaskbar = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "TopLevelWindowForOverflowXamlIsland"];

    // The shell's task switcher, observed on build 26300 in front between the app and the window Alt+Tab switches to:
    // the switcher itself while Alt is held (Task View's window has its class too), else its staging window, all hidden
    // once the switch is done. G8 passes over it: a paste into it would land in a window nobody sees, and the app the
    // user came from is the window in front before it.
    private static readonly HashSet<string> TaskSwitcher = ["XamlExplorerHostIslandWindow", "ForegroundStaging"];

    // A hosted app's window is a child of the frame host's window of this class, in the app's own process (observed on
    // build 26300 with Calculator: CalculatorApp.exe, "Calculator", under ApplicationFrameHost.exe's frame).
    private const string FrameHost = "ApplicationFrameHost.exe";
    private const string HostedWindow = "Windows.UI.Core.CoreWindow";

    // The name shown when no executable can be read for the window, so the footer still says what Enter does.
    private const string UnnamedApp = "the app you came from";

    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WmQuit = 0x0012;
    // WM_APP + 1: posted to the hook's thread, which answers it once it has taken every report queued before it.
    private const uint WmCaughtUp = 0x8001;
    private const uint PmNoRemove = 0;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const int SwRestore = 9;
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;
    private const short KeyDown = unchecked((short)0x8000);

    // Shift, Alt and the two Win keys: held, each makes another chord of Ctrl+V; Ctrl held keeps it a paste.
    private static readonly int[] ChordChangers = [0x10, 0x12, 0x5B, 0x5C];
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(10);

    // How long a read of the window before ImageStash's waits for the hook's thread to take the reports queued for it:
    // the bound G1's caret lookup has, so a busy hook delays Enter by at most that.
    private static readonly TimeSpan CatchUpBound = TimeSpan.FromMilliseconds(250);

    // More reports than one activation, the shell's task switcher it came through (three reports seen for one Alt+Tab)
    // and the windows of ImageStash's own around it make.
    private const int Remembered = 16;

    private readonly uint ownProcess = (uint)Environment.ProcessId;
    private readonly Func<uint, uint?> integrityOf;
    private readonly uint? ownIntegrity;
    private readonly WinEventProc onForeground;
    private readonly Thread thread;
    // The latest reports, oldest first, each as read when it came, null for the shell's task switcher; touched under
    // its own lock.
    private readonly List<(nint Window, ForegroundReading? Reading)> reported = [];
    // When LastBeforeMine last answered, on the clock the hook's event times are on (GetTickCount's), and whether it has.
    private int lastAnswered;
    private volatile bool answered;
    // The latest catch-up asked for and the latest the hook's thread has answered, touched under catchUpGate.
    private ulong catchUpAsked;
    private ulong caughtUp;
    private readonly object catchUpGate = new();
    private uint threadId;
    private bool disposed;

    public event Action? ReportedLate;

    public WindowsTarget()
        : this(IntegrityOf)
    {
    }

    /// <param name="integrityOf">The integrity read; checks give one of their own to stand for a process of higher or unreadable rights.</param>
    internal WindowsTarget(Func<uint, uint?> integrityOf)
    {
        this.integrityOf = integrityOf;
        ownIntegrity = integrityOf(ownProcess);
        onForeground = (_, _, window, _, _, _, time) => Report(window, unchecked((int)time));
        using var ready = new ManualResetEventSlim();
        thread = new Thread(() => Loop(ready)) { IsBackground = true, Name = "Paste target" };
        thread.Start();
        ready.Wait();
    }

    /// <summary>The thread the foreground hook reports on; checks hold it to make its reports come late.</summary>
    internal uint HookThread => threadId;

    public ForegroundReading Foreground()
    {
        if (Read(GetForegroundWindow()) is { } reading)
        {
            return reading;
        }

        lock (reported)
        {
            var at = PassingOver(reported.Count - 1);
            return at < 0 ? new ForegroundReading(false, null) : reported[at].Reading!.Value;
        }
    }

    /// <summary>
    /// While another window is in front, that window under G8's rule; while one of ImageStash's is, the window reported
    /// in front just before it, passing over the shell's task switcher, and none when that one too was ImageStash's own,
    /// a notification or a dialog included (G8: "none when that window was ImageStash itself, one of its
    /// notifications"), so a switch cancelled back to ImageStash's window leaves none. While one of ImageStash's is in
    /// front, the history is read once the hook's thread has taken the reports queued for it, waiting at most
    /// <see cref="CatchUpBound"/> (<see cref="CatchUp"/>); a history still behind then answers none, so Enter copies
    /// rather than pasting into an app the user had left, and the late report then comes by <see cref="ReportedLate"/>.
    /// </summary>
    public PasteTarget? LastBeforeMine()
    {
        Volatile.Write(ref lastAnswered, Environment.TickCount);
        answered = true;
        var now = GetForegroundWindow();
        if (Read(now) is { IsMine: false } other)
        {
            return other.Target;
        }

        if (!CaughtUp())
        {
            return null;
        }

        lock (reported)
        {
            // Past the switcher's reports the shell makes again once a switch is over, then the reports of the window in
            // front, when they have come, then the switcher it came through.
            var at = PassingOver(reported.Count - 1);
            while (at >= 0 && reported[at].Window == now)
            {
                at--;
            }

            at = PassingOver(at);
            return at < 0 ? null : reported[at].Reading!.Value.Target;
        }
    }

    public Outcome Confirm(PasteTarget target)
    {
        if (!IsWindow(target.Handle))
        {
            return Refused(target, PasteTargetReasons.Gone);
        }

        GetWindowThreadProcessId(target.Handle, out var process);
        return Refusal(integrityOf(process), ownIntegrity) is { } reason ? Refused(target, reason) : Outcome.Done;
    }

    public Task<Outcome> ActivateAndPaste(PasteTarget target, TimeSpan bound) => Task.Run(() =>
    {
        var clock = Stopwatch.StartNew();
        var window = target.Handle;
        if (IsIconic(window))
        {
            // Asked without waiting on the target's thread, which may be busy for longer than the bound (ShowWindowAsync).
            ShowWindowAsync(window, SwRestore);
        }

        // Refused, Windows leaves the foreground where it is (S6): the wait then runs out and nothing is sent. A window
        // still minimised has not answered its restore, and a keystroke sent to it would wait in its queue past the bound.
        SetForegroundWindow(window);
        while (GetForegroundWindow() != window || IsIconic(window))
        {
            if (clock.Elapsed >= bound)
            {
                return Refused(target, PasteTargetReasons.NotBroughtToFront);
            }

            Thread.Sleep(Poll);
        }

        // SendInput leaves the keyboard's state as it is, so a key the user still holds (Alt through a double-click,
        // Shift or Win through the menu's click) would join the Ctrl+V and make another chord of it, and nothing else may
        // be sent to undo it (R6): the paste waits for it to be let go, within the bound. Read with the target in front,
        // whose rights the confirmation found no higher than ImageStash's, since the read answers "up" for any key while
        // a window of higher rights is in front (GetAsyncKeyState: zero when UIPI prevents access to the foreground thread).
        while (ChordChangers.Any(key => (GetAsyncKeyState(key) & KeyDown) != 0))
        {
            if (clock.Elapsed >= bound)
            {
                return Refused(target, PasteTargetReasons.KeyHeld);
            }

            Thread.Sleep(Poll);
        }

        // Checked here too, on the thread that sends, so a step that took long never lets the keystroke out past the bound (R6).
        if (clock.Elapsed >= bound)
        {
            return Refused(target, PasteTargetReasons.NotBroughtToFront);
        }

        Input[] paste = [Key(VkControl, up: false), Key(VkV, up: false), Key(VkV, up: true), Key(VkControl, up: true)];
        return SendInput((uint)paste.Length, paste, Marshal.SizeOf<Input>()) == paste.Length
            ? Outcome.Done
            : Refused(target, PasteTargetReasons.KeystrokeRefused);
    });

    /// <summary>The caret of the focused control in <paramref name="target"/>'s window by G1's chain, which <see cref="WindowsCaret"/> holds.</summary>
    public async Task<PixelRect?> CaretOf(PasteTarget target, TimeSpan bound) => (await WindowsCaret.Find(target.Handle, bound))?.Rectangle;

    /// <summary>
    /// Why ImageStash may not send input to a process at <paramref name="target"/>'s integrity level, or null when it
    /// may: a level above ImageStash's own, and one that cannot be read, count as above (S6).
    /// </summary>
    internal static string? Refusal(uint? target, uint? own) => (target, own) switch
    {
        ({ } level, { } mine) when level <= mine => null,
        ({ }, { }) => PasteTargetReasons.HigherRights,
        _ => PasteTargetReasons.RightsUnreadable,
    };

    /// <summary>
    /// What <paramref name="window"/> is under G8's rule: ImageStash's own, the desktop or the taskbar (none), or a target
    /// with its app's name; null for the shell's task switcher, which G8 passes over.
    /// </summary>
    internal ForegroundReading? Read(nint window)
    {
        if (window == 0 || GetWindowThreadProcessId(window, out var process) == 0)
        {
            return new ForegroundReading(false, null);
        }

        if (process == ownProcess)
        {
            return new ForegroundReading(true, null);
        }

        GetWindowThreadProcessId(GetShellWindow(), out var shell);
        if (process == shell && ClassName(window) is var name)
        {
            if (DesktopAndTaskbar.Contains(name))
            {
                return new ForegroundReading(false, null);
            }

            if (TaskSwitcher.Contains(name))
            {
                return null;
            }
        }

        return new ForegroundReading(false, new PasteTarget(window, AppNameOf(window)));
    }

    // The index of the latest report up to at that is not of the shell's task switcher, or -1; called under the lock.
    private int PassingOver(int at)
    {
        while (at >= 0 && reported[at].Reading is null)
        {
            at--;
        }

        return at;
    }

    /// <summary>The app's name for <paramref name="window"/> (What crosses every boundary).</summary>
    internal static string AppNameOf(nint window)
    {
        GetWindowThreadProcessId(window, out var process);
        var path = ImagePath(process);
        if (path is not null && Path.GetFileName(path).Equals(FrameHost, StringComparison.OrdinalIgnoreCase))
        {
            path = HostedApp(window, process) is { } hosted ? ImagePath(hosted) : null;
        }

        return path is null ? UnnamedApp : NameOf(path);
    }

    /// <summary>The integrity level's relative identifier of <paramref name="process"/>'s token, or null when it cannot be read (S6).</summary>
    internal static uint? IntegrityOf(uint process)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, process);
        if (handle == 0)
        {
            return null;
        }

        try
        {
            if (!OpenProcessToken(handle, TokenQuery, out var token))
            {
                return null;
            }

            try
            {
                GetTokenInformation(token, TokenIntegrityLevel, 0, 0, out var size);
                if (size <= 0)
                {
                    return null;
                }

                var label = Marshal.AllocHGlobal(size);
                try
                {
                    if (!GetTokenInformation(token, TokenIntegrityLevel, label, size, out _))
                    {
                        return null;
                    }

                    // TOKEN_MANDATORY_LABEL starts with its SID's pointer; the level is that SID's last sub-authority.
                    var sid = Marshal.ReadIntPtr(label);
                    var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                    return count == 0 ? null : unchecked((uint)Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1))));
                }
                finally
                {
                    Marshal.FreeHGlobal(label);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static Outcome Refused(PasteTarget target, string reason) => new Failure(PasteTargetReasons.Action, target.AppName, reason);

    private void Report(nint window, int time)
    {
        // Every report of the switcher is kept, however late, since the hook can report late (the staging window seen 31
        // to 78 ms late, or after the switch is over while the hook's thread is busy); left out, a switch cancelled back to
        // ImageStash's own window would leave the app before it as the target (G8). The one the shell makes again after
        // the window switched to (observed on build 26300 a millisecond after a held Alt+Tab ended) is passed over by
        // LastBeforeMine.
        var reading = Read(window);
        lock (reported)
        {
            reported.Add((window, reading));
            if (reported.Count > Remembered)
            {
                reported.RemoveAt(0);
            }
        }

        // A change that came before LastBeforeMine's latest answer, heard only now: that answer may have missed it.
        if (answered && unchecked(time - Volatile.Read(ref lastAnswered)) <= 0)
        {
            ReportedLate?.Invoke();
        }
    }

    /// <summary>
    /// Waits, for at most <see cref="CatchUpBound"/>, until the hook's thread has taken every report the system queued
    /// for it before this call (G8): a click from another app into ImageStash's window is then in the history read
    /// next, even while the hook's thread is busy reading an app's name. Whether it has: false once the bound ran out or
    /// the request could not be posted. Never waits on the hook's own thread, which has taken every report it is in the
    /// middle of, nor once the seam is disposed, when no report comes any more.
    /// </summary>
    private bool CaughtUp()
    {
        if (disposed || GetCurrentThreadId() == threadId)
        {
            return true;
        }

        ulong ticket;
        lock (catchUpGate)
        {
            ticket = ++catchUpAsked;
        }

        if (!PostThreadMessageW(threadId, WmCaughtUp, (nuint)ticket, 0))
        {
            return false;
        }

        var clock = Stopwatch.StartNew();
        lock (catchUpGate)
        {
            while (caughtUp < ticket)
            {
                var left = CatchUpBound - clock.Elapsed;
                if (left <= TimeSpan.Zero)
                {
                    return false;
                }

                Monitor.Wait(catchUpGate, left);
            }

            return true;
        }
    }

    private void Loop(ManualResetEventSlim ready)
    {
        threadId = GetCurrentThreadId();
        // The queue exists once the thread has called a message function, before the hook or Dispose can post to it.
        PeekMessageW(out _, 0, 0, 0, PmNoRemove);
        var hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, 0, onForeground, 0, 0, WinEventOutOfContext);
        // What was in front before the first report: the window ImageStash was started from.
        Report(GetForegroundWindow(), Environment.TickCount);
        ready.Set();
        while (GetMessageW(out var message, 0, 0, 0) > 0)
        {
            if (message.Window == 0 && message.Id == WmCaughtUp)
            {
                // The foreground events queued meanwhile are handed to the hook inside a call that looks at the queue, which
                // a posted message does not wait for: one look first, so their reports are in before the answer.
                PeekMessageW(out _, 0, 0, 0, PmNoRemove);
                lock (catchUpGate)
                {
                    caughtUp = Math.Max(caughtUp, (ulong)message.WParam);
                    Monitor.PulseAll(catchUpGate);
                }

                continue;
            }

            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }

        if (hook != 0)
        {
            UnhookWinEvent(hook);
        }
    }

    private static string NameOf(string path)
    {
        try
        {
            if (FileVersionInfo.GetVersionInfo(path).FileDescription is { } description && !string.IsNullOrWhiteSpace(description))
            {
                return description.Trim();
            }
        }
        catch (Exception error) when (error is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            // An executable whose version resource cannot be read is named by its file.
        }

        return Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>The process of the hosted app's window inside a frame host's <paramref name="frame"/>, or null when it has none.</summary>
    private static uint? HostedApp(nint frame, uint host)
    {
        for (var child = FindWindowExW(frame, 0, HostedWindow, null); child != 0; child = FindWindowExW(frame, child, HostedWindow, null))
        {
            GetWindowThreadProcessId(child, out var process);
            if (process != host && process != 0)
            {
                return process;
            }
        }

        return null;
    }

    private static string? ImagePath(uint process)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, process);
        if (handle == 0)
        {
            return null;
        }

        try
        {
            var path = new StringBuilder(32768);
            var size = (uint)path.Capacity;
            return QueryFullProcessImageNameW(handle, 0, path, ref size) ? path.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static string ClassName(nint window)
    {
        var name = new StringBuilder(256);
        return GetClassNameW(window, name, name.Capacity) > 0 ? name.ToString() : "";
    }

    private static Input Key(ushort key, bool up) => new()
    {
        Type = InputKeyboard,
        Keyboard = new KeyboardInput { VirtualKey = key, ScanCode = (ushort)MapVirtualKeyW(key, 0), Flags = up ? KeyUp : 0 },
    };

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        PostThreadMessageW(threadId, WmQuit, 0, 0);
        thread.Join();
    }

    private delegate void WinEventProc(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    private struct ThreadMessage
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    // INPUT's union is as large as its MOUSEINPUT member, eight bytes more than KEYBDINPUT on x64.
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public KeyboardInput Keyboard;
        public ulong Rest;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetMessageW(out ThreadMessage message, nint window, uint first, uint last);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(out ThreadMessage message, nint window, uint first, uint last, uint remove);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref ThreadMessage message);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint DispatchMessageW(ref ThreadMessage message);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(uint thread, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint window, int command);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, StringBuilder name, int capacity);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern nint FindWindowExW(nint parent, nint after, string? className, string? title);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(nint process, uint flags, StringBuilder path, ref uint size);

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);

    [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(nint token, int informationClass, nint information, int length, out int returnLength);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern nint GetSidSubAuthorityCount(nint sid);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern nint GetSidSubAuthority(nint sid, uint index);
}
