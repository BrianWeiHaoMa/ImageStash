using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using ImageStash.Seams;

namespace ImageStash.Windows.Seams;

/// <summary>
/// ImageStash's own small windows shown without activation, Avalonia's own way (S10), and activated by nothing but a
/// click into a field taking the activation, which the window then holds until it is given back, so any other click on
/// one moves no activation and no focus, whatever window is in front (G16); a window shown only in parts through its
/// window region, which Windows neither draws nor hit-tests outside (G15); its corners rounded as Windows 11 rounds its
/// own windows; the tray icon's rectangle, which the shell
/// reports for the icon's identity (S16), read afresh at every call; and the pointer's position, in physical pixels
/// because Avalonia makes the process per-monitor DPI aware (S10).
/// </summary>
public sealed class WindowsQuietWindows(WindowsTray tray) : IQuietWindows
{
    /// <summary>Why a window could not take the activation, as the hold's failure names it (G35).</summary>
    internal const string NotBroughtToFront = "it could not be brought to the front";

    private const string TakeAction = "Activate the notification";
    private const string ShowOnlyAction = "Show the window in parts";
    private const string NoShape = "Windows would not give it that shape";
    private const int RgnOr = 2;
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000;
    private const uint WmDestroy = 0x0002;
    private const uint WmMouseActivate = 0x0021;
    private const nint MaNoActivate = 3;
    private const int WhCbt = 5;
    private const int HcbtActivate = 5;
    private const int HcbtSetFocus = 9;

    private readonly ConditionalWeakTable<Avalonia.Controls.Window, object> keptInactive = [];

    // By window handle, so a hold is the window's whichever instance of the seam showed it or took it. UI thread only.
    /// <summary>By quiet window, while a hold is on: the window in front before its first take, and how many holds remain.</summary>
    private static readonly Dictionary<nint, Holding> holdings = [];

    /// <summary>The windows shown through this seam, by handle while they exist: each refused focus and activation without a hold.</summary>
    private static readonly HashSet<nint> quiet = [];

    // Held in a static, so the collector never frees the callback Windows calls while the hook is installed.
    private static readonly CbtProcedure refusal = RefuseWithoutHold;

    /// <summary>The UI thread's CBT hook, installed at the first showing and kept for the process; 0 until Windows installs it.</summary>
    private static nint cbtHook;

    public void ShowWithoutActivating(Avalonia.Controls.Window window)
    {
        // WS_EX_NOACTIVATE: a click on the window leaves the app in front with the keystrokes (G16). Avalonia rewrites
        // the extended style whenever a property of the window changes, so its styles callback adds it each time, and it
        // is added now for the showing that follows.
        if (keptInactive.TryAdd(window, window))
        {
            Win32Properties.AddWindowStylesCallback(window, (style, extended) => (style, extended | (uint)WsExNoActivate));
            // Avalonia joins a window's hooks into one delegate and returns the last one's result: a hook added after this
            // one replaces its answer to the click, which the refusal below then discards.
            Win32Properties.AddWndProcHookCallback(window, AnswerClickInactive);
        }

        if (window.TryGetPlatformHandle() is { } handle)
        {
            quiet.Add(handle.Handle);
            SetWindowLongPtrW(handle.Handle, GwlExStyle, GetWindowLongPtrW(handle.Handle, GwlExStyle) | (nint)WsExNoActivate);
        }

        // Avalonia sets the focus on a window at every button-down, which activates it while ImageStash is in front; the
        // hook refuses that and every other way a quiet window without a hold would be activated. Windows refusing the
        // hook leaves the window shown as before, its words and Undo still there (G35), and the next showing tries again.
        if (cbtHook == 0)
        {
            cbtHook = SetWindowsHookExW(WhCbt, refusal, 0, GetCurrentThreadId());
        }

        window.ShowActivated = false;
        window.Show();
    }

    public Outcome ShowOnly(Avalonia.Controls.Window window, IReadOnlyList<PixelRect> parts)
    {
        if (window.TryGetPlatformHandle() is not { Handle: var handle } || handle == 0)
        {
            return new Failure(ShowOnlyAction, window.Title ?? "", "it is not on the screen");
        }

        // The union of the parts becomes the window's region: outside it Windows neither draws the window nor hit-tests
        // it, so a click there reaches the window beneath (probed).
        var region = CreateRectRgn(0, 0, 0, 0);
        var made = region != 0;
        foreach (var part in parts)
        {
            var piece = made ? CreateRectRgn(part.X, part.Y, part.Right, part.Bottom) : 0;
            made = piece != 0 && CombineRgn(region, region, piece, RgnOr) != 0;
            if (piece != 0)
            {
                DeleteObject(piece);
            }
        }

        // Windows owns a region it took; one it refused, or one never set, is still this call's to let go.
        if (!made || SetWindowRgn(handle, region, redraw: true) == 0)
        {
            if (region != 0)
            {
                DeleteObject(region);
            }

            return new Failure(ShowOnlyAction, window.Title ?? "", NoShape);
        }

        return Outcome.Done;
    }

    public Outcome<IDisposable> TakeActivation(Avalonia.Controls.Window window)
    {
        if (window.TryGetPlatformHandle() is not { Handle: var handle } || handle == 0)
        {
            return new Failure(TakeAction, window.Title ?? "", NotBroughtToFront);
        }

        if (holdings.TryGetValue(handle, out var holding))
        {
            // A second field's click keeps the window the first one took it from, never this window itself.
            holding.Count++;
            return Outcome<IDisposable>.Of(new Hold(handle));
        }

        var front = GetForegroundWindow();
        // Recorded before the window is brought to the front, so the refusal of a quiet window's activation lets this one through.
        holdings[handle] = new Holding(front);
        // The user's click is the last input event, which lets this process set the foreground (SetForegroundWindow).
        if (front != handle && !SetForegroundWindow(handle))
        {
            holdings.Remove(handle);
            return new Failure(TakeAction, window.Title ?? "", NotBroughtToFront);
        }

        return Outcome<IDisposable>.Of(new Hold(handle));
    }

    /// <summary>
    /// The window's last hold ended: the activation goes back to the window in front before the first take, while the
    /// quiet window still has it; a window the user put in front meanwhile keeps it, so the chat keeps the keystrokes (G16).
    /// </summary>
    private static void Release(nint window)
    {
        if (!holdings.TryGetValue(window, out var holding) || --holding.Count > 0)
        {
            return;
        }

        holdings.Remove(window);
        if (GetForegroundWindow() == window && holding.Previous != 0 && IsWindow(holding.Previous))
        {
            SetForegroundWindow(holding.Previous);
        }
    }

    /// <summary>
    /// Windows' own activation by a click is answered "no activation" while the window holds none, so the click still
    /// reaches it: refused at the hook instead, the click would be discarded and Undo and ? would not work. A destroyed
    /// window's records leave with it, since Windows reuses handles.
    /// </summary>
    private static nint AnswerClickInactive(nint window, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmMouseActivate && !holdings.ContainsKey(window))
        {
            handled = true;
            return MaNoActivate;
        }

        if (message == WmDestroy)
        {
            quiet.Remove(window);
            holdings.Remove(window);
        }

        return 0;
    }

    /// <summary>
    /// The UI thread's CBT hook: a quiet window without a hold is refused the keyboard focus and the activation, whatever
    /// asks for them (Avalonia's focus on a click among them), so the window in front keeps the activation, its focus,
    /// its caret and the keystrokes, and the stash window sees no focus lost or gained (G2, G8, G16); a field's take,
    /// holding the window, lets both through. Both are refused, since an activation by another path while the focus is
    /// refused would leave the window active without the keystrokes.
    /// </summary>
    private static nint RefuseWithoutHold(int code, nint wParam, nint lParam) =>
        code is HcbtActivate or HcbtSetFocus && quiet.Contains(wParam) && !holdings.ContainsKey(wParam)
            ? 1
            : CallNextHookEx(cbtHook, code, wParam, lParam);

    /// <summary>
    /// Windows 11's own rounding, asked for through Avalonia's corner preference, which it applies as the window's
    /// DWMWA_WINDOW_CORNER_PREFERENCE; a build of Windows that does not round leaves the window square.
    /// </summary>
    public void RoundCorners(Avalonia.Controls.Window window) => Win32Properties.SetWindowCornerPreference(window, Win32Properties.WindowCornerPreference.Round);

    public PixelRect? TrayIconRectangle()
    {
        try
        {
            if (tray.Icon is not { IsVisible: true } icon)
            {
                return null;
            }

            // This reflection is pinned to Avalonia 12.1.4's private fields; an unavailable identity means no placement hint.
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
            var impl = typeof(TrayIcon).GetField("_impl", instance)?.GetValue(icon);
            if (impl is null)
            {
                return null;
            }

            var implType = impl.GetType();
            if (implType.GetField("_uniqueId", instance)?.GetValue(impl) is not int uniqueId)
            {
                return null;
            }

            var platformType = implType.Assembly.GetType("Avalonia.Win32.Win32Platform");
            var platform = platformType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
            if (platform is null || platformType?.GetProperty("Handle", instance)?.GetValue(platform) is not nint handle || handle == 0)
            {
                return null;
            }

            var identity = new NotifyIconIdentifier
            {
                Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
                Window = handle,
                Id = unchecked((uint)uniqueId),
                Item = Guid.Empty,
            };
            if (Shell_NotifyIconGetRect(ref identity, out var rectangle) != 0
                || rectangle.Right <= rectangle.Left || rectangle.Bottom <= rectangle.Top)
            {
                return null;
            }

            return new PixelRect(rectangle.Left, rectangle.Top,
                checked(rectangle.Right - rectangle.Left), checked(rectangle.Bottom - rectangle.Top));
        }
        catch (Exception)
        {
            // Shell and private implementation failures leave placement to the caller's fallback.
            return null;
        }
    }

    public PixelPoint? PointerPosition() => GetCursorPos(out var point) ? new PixelPoint(point.X, point.Y) : null;

    public PixelRect? FrontWindowRectangle() =>
        GetForegroundWindow() is var window && window != 0 && GetWindowRect(window, out var bounds) && bounds.Right > bounds.Left && bounds.Bottom > bounds.Top
            ? new PixelRect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top)
            : null;

    private sealed class Holding(nint previous)
    {
        public nint Previous { get; } = previous;

        public int Count { get; set; } = 1;
    }

    /// <summary>One take's hold; disposed more than once, it is released once.</summary>
    private sealed class Hold(nint window) : IDisposable
    {
        private bool released;

        public void Dispose()
        {
            if (!released)
            {
                released = true;
                Release(window);
            }
        }
    }

    private delegate nint CbtProcedure(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public Guid Item;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X, Y;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rectangle rectangle);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out Rectangle iconLocation);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SetWindowsHookExW(int hook, CbtProcedure procedure, nint module, uint thread);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SetWindowLongPtrW(nint window, int index, nint value);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int CombineRgn(nint destination, nint first, nint second, int mode);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint item);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
}
