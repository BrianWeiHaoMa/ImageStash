using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// The checks' own reading of the signed-in user's real clipboard, which only a check may do (N9, R5): what a paste
/// would find there, format by format; and text, files, a PNG, bitmaps and a GDI bitmap put there as another program
/// would copy them, kept out of the system's clipboard history but for the text PowerShell's Set-Clipboard places.
/// </summary>
public static class TestClipboard
{
    public const uint CfBitmap = 2;
    public const uint CfDib = 8;
    public const uint CfUnicodeText = 13;
    public const uint CfHdrop = 15;
    public const uint CfDibV5 = 17;

    /// <summary>The registered format "PNG", as a browser puts a copied image's PNG bytes there.</summary>
    public static uint Png => RegisterClipboardFormatW("PNG");

    /// <summary>
    /// The clipboard's files, its "PNG" bytes, its CF_DIBV5 bytes and its "Preferred DropEffect", each null when it is
    /// not there.
    /// </summary>
    public static ClipboardRead Read()
    {
        Open();
        try
        {
            var effect = Global(RegisterClipboardFormatW("Preferred DropEffect"));
            return new ClipboardRead(Files(), Global(RegisterClipboardFormatW("PNG")), Global(CfDibV5), effect is { Length: >= 4 } ? BitConverter.ToUInt32(effect) : null);
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>The clipboard's text up to its first NUL, or null when it holds none.</summary>
    public static string? Text()
    {
        Open();
        try
        {
            return Global(CfUnicodeText) is { } bytes ? Encoding.Unicode.GetString(bytes).Split('\0')[0] : null;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Replaces the clipboard's contents with <paramref name="text"/> alone.</summary>
    public static void PutText(string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text + "\0");
        Open();
        try
        {
            Assert.IsTrue(EmptyClipboard(), $"the check could not empty the clipboard: error {Marshal.GetLastPInvokeError()}");
            var memory = GlobalAlloc(0x0002, (nuint)bytes.Length);
            Marshal.Copy(bytes, 0, GlobalLock(memory), bytes.Length);
            GlobalUnlock(memory);
            Assert.AreNotEqual(nint.Zero, SetClipboardData(CfUnicodeText, memory), $"the check could not put text there: error {Marshal.GetLastPInvokeError()}");
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Whether the clipboard offers <paramref name="format"/>, asked without opening it.</summary>
    public static bool Holds(uint format) => IsClipboardFormatAvailable(format);

    /// <summary>Replaces the clipboard's contents with <paramref name="text"/> through PowerShell's Set-Clipboard, another program's copy of text.</summary>
    public static void SetClipboard(string text)
    {
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", $"Set-Clipboard -Value '{text.Replace("'", "''")}'" })
        {
            start.ArgumentList.Add(argument);
        }

        using var setter = Process.Start(start)!;
        if (!setter.WaitForExit(60_000))
        {
            setter.Kill(entireProcessTree: true);
            Assert.Fail("Set-Clipboard did not finish");
        }

        Assert.AreEqual(0, setter.ExitCode, "Set-Clipboard failed");
    }

    /// <summary>Empties the clipboard, leaving no format on it at all.</summary>
    public static void Empty()
    {
        Open();
        try
        {
            Assert.IsTrue(EmptyClipboard(), $"the check could not empty the clipboard: error {Marshal.GetLastPInvokeError()}");
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Replaces the clipboard's contents with <paramref name="formats"/>, placed in their order in one open.</summary>
    public static void Put(params (uint Format, byte[] Bytes)[] formats)
    {
        Open();
        try
        {
            Assert.IsTrue(EmptyClipboard(), $"the check could not empty the clipboard: error {Marshal.GetLastPInvokeError()}");
            foreach (var (format, bytes) in formats.Append(NotForHistory()))
            {
                Assert.AreNotEqual(nint.Zero, SetClipboardData(format, Global(bytes)), $"the check could not put format {format} there: error {Marshal.GetLastPInvokeError()}");
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Replaces the clipboard's contents with a GDI bitmap alone, as WPF's and WinForms' SetImage place one, from
    /// <paramref name="pixels"/>, BGRA top-down, all four bytes kept; Windows makes CF_DIB and CF_DIBV5 from it.
    /// </summary>
    public static void PutBitmap(int width, int height, byte[] pixels)
    {
        var bitmap = CreateBitmap(width, height, 1, 32, pixels);
        Assert.AreNotEqual(nint.Zero, bitmap, $"the check could not make a bitmap: error {Marshal.GetLastPInvokeError()}");
        Open();
        try
        {
            Assert.IsTrue(EmptyClipboard(), $"the check could not empty the clipboard: error {Marshal.GetLastPInvokeError()}");
            Assert.AreNotEqual(nint.Zero, SetClipboardData(CfBitmap, bitmap), $"the check could not put the bitmap there: error {Marshal.GetLastPInvokeError()}");
            var (format, bytes) = NotForHistory();
            Assert.AreNotEqual(nint.Zero, SetClipboardData(format, Global(bytes)));
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>CF_HDROP as File Explorer's Copy places it: a DROPFILES header and every path, wide, each ending in a NUL.</summary>
    public static (uint Format, byte[] Bytes) FileList(params string[] paths)
    {
        var list = Encoding.Unicode.GetBytes(string.Concat(paths.Select(path => path + '\0')) + '\0');
        var bytes = new byte[20 + list.Length];
        BitConverter.GetBytes(20).CopyTo(bytes, 0);
        BitConverter.GetBytes(1).CopyTo(bytes, 16);
        list.CopyTo(bytes, 20);
        return (CfHdrop, bytes);
    }

    /// <summary>CF_UNICODETEXT of <paramref name="text"/>, ending in a NUL.</summary>
    public static (uint Format, byte[] Bytes) Text(string text) => (CfUnicodeText, Encoding.Unicode.GetBytes(text + '\0'));

    /// <summary>
    /// The format that keeps a copy out of the system's clipboard history and its sync, which would otherwise read
    /// every check's formats, those rendered on request included.
    /// </summary>
    internal static (uint Format, byte[] Bytes) NotForHistory() => (RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing"), [0, 0, 0, 0]);

    /// <summary>Movable memory holding <paramref name="bytes"/>, for the clipboard to own once it takes it.</summary>
    internal static nint Global(byte[] bytes)
    {
        var memory = GlobalAlloc(0x0002, (nuint)Math.Max(bytes.Length, 1));
        Marshal.Copy(bytes, 0, GlobalLock(memory), bytes.Length);
        GlobalUnlock(memory);
        return memory;
    }

    /// <summary>Opens the clipboard, retried for a few seconds, since the system's clipboard history may be reading it.</summary>
    internal static void Open(nint owner = 0)
    {
        var clock = Stopwatch.StartNew();
        while (!OpenClipboard(owner))
        {
            Assert.IsLessThan(5000, clock.ElapsedMilliseconds, $"the check could not open the clipboard: error {Marshal.GetLastPInvokeError()}");
            Thread.Sleep(20);
        }
    }

    private static IReadOnlyList<string> Files()
    {
        var drop = GetClipboardData(CfHdrop);
        if (drop == 0)
        {
            return [];
        }

        var count = DragQueryFileW(drop, uint.MaxValue, null, 0);
        var files = new List<string>();
        for (var i = 0u; i < count; i++)
        {
            var name = new StringBuilder((int)DragQueryFileW(drop, i, null, 0) + 1);
            DragQueryFileW(drop, i, name, (uint)name.Capacity);
            files.Add(name.ToString());
        }

        return files;
    }

    private static byte[]? Global(uint format)
    {
        var memory = GetClipboardData(format);
        if (memory == 0)
        {
            return null;
        }

        var bytes = new byte[checked((int)GlobalSize(memory))];
        var pointer = GlobalLock(memory);
        try
        {
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
        }
        finally
        {
            GlobalUnlock(memory);
        }

        return bytes;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint flags, nuint size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint RegisterClipboardFormatW(string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint GlobalSize(nint memory);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint DragQueryFileW(nint drop, uint index, StringBuilder? path, uint capacity);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateBitmap(int width, int height, uint planes, uint bitCount, byte[] bits);
}

/// <summary>What the clipboard held when the check read it.</summary>
public sealed record ClipboardRead(IReadOnlyList<string> Files, byte[]? Png, byte[]? DibV5, uint? DropEffect);

/// <summary>
/// Another program holding the clipboard open, as a program does while it copies: a PowerShell that opens it with a
/// window of its own and keeps it open until <see cref="Release"/>. A hold with no window, or from this process,
/// keeps nobody out, so it is a separate process with a window handle (probed on Windows 11).
/// </summary>
public sealed class OtherProgramHoldingTheClipboard : IDisposable
{
    private const string Script = """
        Add-Type -TypeDefinition @'
        using System;
        using System.Runtime.InteropServices;
        public static class ClipboardHolder {
            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
            [DllImport("user32.dll", SetLastError = true)] public static extern bool OpenClipboard(IntPtr owner);
            [DllImport("user32.dll", SetLastError = true)] public static extern bool CloseClipboard();
            [DllImport("user32.dll", SetLastError = true)] public static extern bool DestroyWindow(IntPtr window);
        }
        '@
        $window = [ClipboardHolder]::CreateWindowExW(0, 'STATIC', 'ImageStash check: clipboard holder', 0, 0, 0, 0, 0, [IntPtr]::new(-3), [IntPtr]::Zero, [IntPtr]::Zero, [IntPtr]::Zero)
        if ($window -eq [IntPtr]::Zero) { 'no window'; exit 1 }
        $opened = $false
        for ($try = 0; $try -lt 100 -and -not $opened; $try++) {
            $opened = [ClipboardHolder]::OpenClipboard($window)
            if (-not $opened) { Start-Sleep -Milliseconds 50 }
        }
        if (-not $opened) { 'not opened'; [void][ClipboardHolder]::DestroyWindow($window); exit 1 }
        [Console]::Out.WriteLine('held'); [Console]::Out.Flush()
        [void][Console]::In.ReadLine()
        [void][ClipboardHolder]::CloseClipboard()
        [void][ClipboardHolder]::DestroyWindow($window)
        [Console]::Out.WriteLine('released'); [Console]::Out.Flush()
        """;

    private readonly Process holder;

    private OtherProgramHoldingTheClipboard(Process holder) => this.holder = holder;

    /// <summary>Starts the holder and returns once it holds the clipboard open.</summary>
    public static OtherProgramHoldingTheClipboard Start()
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)) })
        {
            start.ArgumentList.Add(argument);
        }

        var holder = new OtherProgramHoldingTheClipboard(Process.Start(start)!);
        var said = holder.holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60)).GetAwaiter().GetResult();
        if (said != "held")
        {
            holder.Dispose();
            Assert.Fail($"the holder did not hold the clipboard: {said}");
        }

        return holder;
    }

    /// <summary>Lets go of the clipboard and waits until the holder has closed it.</summary>
    public void Release()
    {
        if (holder.HasExited)
        {
            return;
        }

        holder.StandardInput.WriteLine();
        holder.StandardInput.Flush();
        Assert.AreEqual("released", holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());
        Assert.IsTrue(holder.WaitForExit(10_000), "the holder did not exit");
    }

    public void Dispose()
    {
        if (!holder.HasExited)
        {
            // Its exit closes the clipboard it held.
            holder.Kill(entireProcessTree: true);
            holder.WaitForExit();
        }

        holder.Dispose();
    }
}
