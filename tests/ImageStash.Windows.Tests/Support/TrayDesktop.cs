using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using ImageStash.Windows.Seams;
using ImageStash.Look;

namespace ImageStash.Windows.Tests.Support;

/// <summary>One real Windows desktop dispatcher for the process's tray checks.</summary>
[TestClass]
public sealed class TrayDesktop
{
    private static readonly CancellationTokenSource stopping = new();
    private static Thread? uiThread;

    [AssemblyInitialize]
    public static void Start(TestContext context)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        uiThread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
                ready.SetResult();
                Dispatcher.UIThread.MainLoop(stopping.Token);
            }
            catch (Exception error)
            {
                ready.TrySetException(error);
            }
        }) { Name = "Tray desktop", IsBackground = true };
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        ready.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Every check starts in the look App gives, the default take among it, whatever ran before it in the process: a check
    /// run alone then shows its windows as it does in the full run, and a take an earlier check applied does not carry over.
    /// </summary>
    [GlobalTestInitialize]
    public static void StartInTheProductLook(TestContext context) => UseProductTheme();

    [AssemblyCleanup]
    public static void Stop()
    {
        stopping.Cancel();
        Assert.IsTrue(uiThread?.Join(TimeSpan.FromSeconds(10)) ?? true, "The tray dispatcher did not stop.");
        stopping.Dispose();
    }

    // The checks wait on this thread's work, bounded, so a blocked UI thread fails the check that met it, naming the work
    // asked and the work running, instead of hanging the whole run with no check named; the checks after it fail at once.
    private static readonly TimeSpan RoundTrip = TimeSpan.FromMinutes(2);
    private static volatile string? running;
    private static volatile string? blocked;

    public static void Run(Action work) => OnTheUiThread(() =>
    {
        work();
        return true;
    }, work.Method);

    /// <summary>
    /// The look App gives every window, the Fluent theme, its tooltips and the default take of the look, so a window lays
    /// out and paints here as it does in the product; a take a check applied of its own is replaced by the default.
    /// </summary>
    public static void UseProductTheme() => Run(() =>
    {
        ImageStash.Window.ToolTips.PlaceBeneathTheirControls();
        if (!Application.Current!.Styles.OfType<FluentTheme>().Any())
        {
            Application.Current.Styles.Add(new FluentTheme());
        }

        Looks.Apply(Application.Current, Looks.All[0], Looks.Accent(Application.Current));
    });

    public static T Get<T>(Func<T> work) => OnTheUiThread(work, work.Method);

    private static T OnTheUiThread<T>(Func<T> work, MethodInfo asked)
    {
        // Asked from work already running here (a helper that reads a control, called inside a check's own Get): queued
        // and waited on, it would wait on this very thread; it runs in place, as the dispatcher would run it.
        if (Dispatcher.UIThread.CheckAccess())
        {
            return work();
        }

        if (blocked is { } since)
        {
            Assert.Fail($"the tray desktop's UI thread has been blocked since an earlier check: {since}");
        }

        var name = $"{asked.DeclaringType?.FullName}.{asked.Name}";
        var task = Dispatcher.UIThread.InvokeAsync(() =>
        {
            var outer = running;
            running = name;
            try
            {
                return work();
            }
            finally
            {
                running = outer;
            }
        }).GetTask();
        if (!((IAsyncResult)task).AsyncWaitHandle.WaitOne(RoundTrip))
        {
            blocked = $"it did not run {name} within {RoundTrip.TotalMinutes} minutes, {running ?? "no work of the checks'"} running then";
            Assert.Fail($"the tray desktop's UI thread is blocked: {blocked}");
        }

        return task.GetAwaiter().GetResult();
    }

    public static async Task WithShownTray(Action<WindowsTray> check)
    {
        var tray = new WindowsTray();
        try
        {
            Run(() => tray.Show([], "ImageStash Windows check", () => { }));
            // Give Explorer time to register the real icon while the UI thread continues pumping native messages.
            await Task.Delay(1000);
            Run(() => check(tray));
        }
        finally
        {
            Run(tray.Dispose);
        }
    }

    public static PixelRect VirtualDesktop => new(GetSystemMetrics(76), GetSystemMetrics(77),
        GetSystemMetrics(78), GetSystemMetrics(79));

    public static void SendTrayMouse(WindowsTray tray, int mouseMessage)
    {
        // Match Avalonia 12.1.4 TrayIconImpl: WM_USER + 1024, wParam = icon ID, lParam = button message.
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var impl = typeof(TrayIcon).GetField("_impl", instance)!.GetValue(tray.Icon)!;
        var implType = impl.GetType();
        var uniqueId = (int)implType.GetField("_uniqueId", instance)!.GetValue(impl)!;
        var platformType = implType.Assembly.GetType("Avalonia.Win32.Win32Platform", true)!;
        var platform = platformType.GetProperty("Instance", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var handle = (nint)platformType.GetProperty("Handle", instance)!.GetValue(platform)!;
        Assert.AreNotEqual(nint.Zero, handle);
        SendMessage(handle, 0x0400 + 1024, uniqueId, mouseMessage);
    }

    /// <summary>
    /// The icon's real menu opened as a right-click opens it, at the pointer away from the checks' windows, and a real
    /// click on the entry at <paramref name="index"/> of its <paramref name="count"/>, the menu's rows being of one height.
    /// </summary>
    public static void ChooseFromRealMenu(WindowsTray tray, int index, int count)
    {
        TestMouse.MoveTo(TestMouse.Away);
        var before = ProcessWindows.VisibleHandles();
        Run(() => SendTrayMouse(tray, 0x0205)); // WM_RBUTTONUP
        var clock = System.Diagnostics.Stopwatch.StartNew();
        nint seen;
        while ((seen = ProcessWindows.VisibleHandles().Except(before).FirstOrDefault()) == 0)
        {
            Assert.IsLessThan(5000, clock.ElapsedMilliseconds, $"the tray's menu did not open; {InFront()}");
            Thread.Sleep(20);
        }

        // Sized to its content once shown; read then, since a window the opening made for a moment may have gone by now.
        Thread.Sleep(300);
        Assert.IsTrue(GetWindowRect(ProcessWindows.VisibleHandles().Except(before).FirstOrDefault(), out var bounds), $"the tray's menu, seen open as {seen:X}, was gone 300 ms later; {InFront()}");
        TestMouse.Click(new PixelPoint((bounds.Left + bounds.Right) / 2, bounds.Top + (bounds.Bottom - bounds.Top) * (2 * index + 1) / (2 * count)));
    }

    private static string InFront()
    {
        var front = FrontWindow.GetForegroundWindow();
        return $"{front:X} \"{FrontWindow.NativeText(front)}\" is in front";
    }

    private const int TaskbarHeight = 48;

    /// <summary>
    /// The icon's real menu as a person reaches it: when the icon is among the hidden icons, a real click on Show hidden
    /// icons, where Windows then places it; a real right-click on the icon; a real click on the entry at
    /// <paramref name="index"/> of the <paramref name="count"/> rows, where the menu opened at the pointer.
    /// </summary>
    public static void ChooseFromRealMenuThroughTheIcon(WindowsTray tray, int index, int count)
    {
        var quiet = new WindowsQuietWindows(tray);
        var icon = Get(quiet.TrayIconRectangle);
        Assert.IsNotNull(icon, "the check's tray icon has a place");
        // Above the taskbar, the hidden icons are open already and the place is the icon's; on it, the place may be Show
        // hidden icons, and the icon's own shows once the shell has slid them in.
        var inOverflow = icon;
        if (icon.Value.Bottom > GetSystemMetrics(1) - TaskbarHeight)
        {
            TestMouse.Click(icon.Value.Center);
            Thread.Sleep(900);
            inOverflow = Get(quiet.TrayIconRectangle);
        }

        Assert.IsNotNull(inOverflow);
        var before = ProcessWindows.VisibleHandles();
        TestMouse.RightClick(inOverflow.Value.Center);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (ProcessWindows.VisibleHandles().Except(before).FirstOrDefault() == 0)
        {
            Assert.IsLessThan(5000, clock.ElapsedMilliseconds, $"the tray's menu did not open at {inOverflow}, the icon having answered {icon}");
            Thread.Sleep(20);
        }

        Thread.Sleep(300);
        Assert.IsTrue(GetWindowRect(ProcessWindows.VisibleHandles().Except(before).FirstOrDefault(), out var bounds), "the menu stayed open");
        TestMouse.Click(new PixelPoint((bounds.Left + bounds.Right) / 2, bounds.Top + (bounds.Bottom - bounds.Top) * (2 * index + 1) / (2 * count)));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rectangle rectangle);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetSystemMetrics(int index);
}
