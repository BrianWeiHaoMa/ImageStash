using System.Runtime.InteropServices;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// The clipboard owned by a window of the check's own on a thread of its own, holding some formats now and others
/// rendered only when a reader asks for them (delayed rendering), keeping which were asked for: so a check sees what a
/// read takes and what it leaves untouched. Disposing it empties the clipboard while it still owns it.
/// </summary>
public sealed class ClipboardOnRequest : IDisposable
{
    private const uint RenderFormat = 0x0305;
    private const uint Close = 0x0400 + 1;
    private readonly Thread thread;
    private readonly WindowProcedure procedure;
    private readonly string className = "ImageStashCheckClipboard" + Guid.NewGuid().ToString("N");
    private readonly Dictionary<uint, byte[]> onRequest;
    private readonly List<uint> requested = [];
    private nint window;

    private ClipboardOnRequest((uint Format, byte[] Bytes)[] now, (uint Format, byte[] Bytes)[] later)
    {
        onRequest = later.ToDictionary(format => format.Format, format => format.Bytes);
        procedure = Procedure;
        using var placed = new ManualResetEventSlim();
        Exception? failed = null;
        thread = new Thread(() =>
        {
            try
            {
                Place(now, later);
            }
            catch (Exception exception)
            {
                failed = exception;
                placed.Set();
                return;
            }

            placed.Set();
            while (GetMessageW(out var message, 0, 0, 0) > 0)
            {
                DispatchMessageW(message);
            }
        }) { IsBackground = true };
        thread.Start();
        Assert.IsTrue(placed.Wait(TimeSpan.FromSeconds(10)), "the check's clipboard owner did not start");
        if (failed is not null)
        {
            throw failed;
        }
    }

    /// <summary>The formats asked for since the clipboard was filled, in order.</summary>
    public IReadOnlyList<uint> Requested
    {
        get
        {
            lock (requested)
            {
                return [.. requested];
            }
        }
    }

    /// <summary>Replaces the clipboard's contents with <paramref name="now"/>, then <paramref name="later"/> as formats rendered when asked for.</summary>
    public static ClipboardOnRequest Put((uint Format, byte[] Bytes)[] now, (uint Format, byte[] Bytes)[] later) => new(now, later);

    public void Dispose()
    {
        PostMessageW(window, Close, 0, 0);
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "the check's clipboard owner did not end");
        UnregisterClassW(className, GetModuleHandleW(null));
    }

    private void Place((uint Format, byte[] Bytes)[] now, (uint Format, byte[] Bytes)[] later)
    {
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Procedure = Marshal.GetFunctionPointerForDelegate(procedure),
            Instance = GetModuleHandleW(null),
            ClassName = className,
        };
        Assert.AreNotEqual((ushort)0, RegisterClassExW(windowClass), $"the check could not register its window class: error {Marshal.GetLastPInvokeError()}");
        window = CreateWindowExW(0, className, "ImageStash check: clipboard owner", 0, 0, 0, 0, 0, -3, 0, windowClass.Instance, 0);
        Assert.AreNotEqual(nint.Zero, window, $"the check could not make its window: error {Marshal.GetLastPInvokeError()}");
        TestClipboard.Open(window);
        try
        {
            Assert.IsTrue(EmptyClipboard(), $"the check could not empty the clipboard: error {Marshal.GetLastPInvokeError()}");
            foreach (var (format, bytes) in now.Append(TestClipboard.NotForHistory()))
            {
                Assert.AreNotEqual(nint.Zero, SetClipboardData(format, TestClipboard.Global(bytes)));
            }

            foreach (var (format, _) in later)
            {
                // No memory: Windows asks this window for the format when a reader wants it.
                SetClipboardData(format, 0);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private nint Procedure(nint handle, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case RenderFormat:
                var format = (uint)wParam;
                lock (requested)
                {
                    requested.Add(format);
                }

                if (onRequest.TryGetValue(format, out var bytes))
                {
                    SetClipboardData(format, TestClipboard.Global(bytes));
                }

                return 0;
            case Close:
                if (GetClipboardOwner() == handle && OpenClipboard(handle))
                {
                    EmptyClipboard();
                    CloseClipboard();
                }

                DestroyWindow(handle);
                PostQuitMessage(0);
                return 0;
            default:
                return DefWindowProcW(handle, message, wParam, lParam);
        }
    }

    private delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern ushort RegisterClassExW(in WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetMessageW(out Message message, nint window, uint first, uint last);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint DispatchMessageW(in Message message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint GetModuleHandleW(string? name);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("user32.dll")]
    private static extern nint GetClipboardOwner();
}
