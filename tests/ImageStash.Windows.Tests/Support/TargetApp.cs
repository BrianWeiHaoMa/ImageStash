using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// Another program's window to paste into: a PowerShell, so a process other than the check's with the file description
/// "Windows PowerShell", showing a window with an edit box that has focus, or with none when asked, so it shows no
/// caret. It reports every key message, character,
/// paste and left-button press and release its window and edit box receive, and on request the edit box's text. It is placed on the primary display,
/// where a click reaches it, until disposed.
/// </summary>
public sealed class TargetApp : IDisposable
{
    private const string Script = """
        Add-Type -TypeDefinition @'
        using System;
        using System.Runtime.InteropServices;
        using System.Text;
        using System.Threading;
        public static class CheckTarget {
            delegate IntPtr WndProc(IntPtr window, uint message, IntPtr w, IntPtr l);
            delegate IntPtr SubclassProc(IntPtr window, uint message, IntPtr w, IntPtr l, UIntPtr id, UIntPtr data);
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            struct WindowClass { public uint Size, Style; public WndProc Procedure; public int ClassExtra, WindowExtra; public IntPtr Instance, Icon, Cursor, Background; public string Menu, Name; public IntPtr SmallIcon; }
            [StructLayout(LayoutKind.Sequential)]
            struct Message { public IntPtr Window; public uint Id; public IntPtr W, L; public uint Time; public int X, Y; public uint Private; }
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassExW(ref WindowClass c);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
            [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr w, IntPtr l);
            [DllImport("user32.dll")] static extern int GetMessageW(out Message m, IntPtr window, uint a, uint b);
            [DllImport("user32.dll")] static extern bool TranslateMessage(ref Message m);
            [DllImport("user32.dll")] static extern IntPtr DispatchMessageW(ref Message m);
            [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr window, uint message, IntPtr w, IntPtr l);
            [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
            [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr window);
            [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr window);
            [DllImport("user32.dll")] static extern IntPtr LoadCursorW(IntPtr instance, IntPtr id);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr window, StringBuilder text, int capacity);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string name);
            [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr window, SubclassProc procedure, UIntPtr id, UIntPtr data);
            [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr w, IntPtr l);
            const uint App = 0x8000;
            static readonly WndProc mainProcedure = TopWindow;
            static readonly SubclassProc editProcedure = Edit;
            static IntPtr main, edit;
            public static void Run(string title, int x, int y, bool withEdit) {
                WindowClass c = new WindowClass();
                c.Size = (uint)Marshal.SizeOf(typeof(WindowClass));
                c.Procedure = mainProcedure;
                c.Instance = GetModuleHandleW(null);
                c.Cursor = LoadCursorW(IntPtr.Zero, (IntPtr)32512);
                c.Background = (IntPtr)6;
                c.Name = "ImageStashCheckTarget";
                RegisterClassExW(ref c);
                main = CreateWindowExW(0, c.Name, title, 0x10CF0000, x, y, 400, 260, IntPtr.Zero, IntPtr.Zero, c.Instance, IntPtr.Zero);
                if (withEdit) {
                    edit = CreateWindowExW(0, "EDIT", "", 0x50810004, 10, 10, 360, 190, main, IntPtr.Zero, c.Instance, IntPtr.Zero);
                    SetWindowSubclass(edit, editProcedure, UIntPtr.Zero, UIntPtr.Zero);
                    SetFocus(edit);
                }
                Say("ready " + main.ToInt64());
                Thread reader = new Thread(Read);
                reader.IsBackground = true;
                reader.Start();
                Message m;
                while (GetMessageW(out m, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref m); DispatchMessageW(ref m); }
            }
            static void Read() {
                string line;
                while ((line = Console.In.ReadLine()) != null) {
                    uint code = line == "text" ? 1u : line == "close" ? 2u : 0u;
                    PostMessageW(main, App + code, IntPtr.Zero, IntPtr.Zero);
                }
                PostMessageW(main, App + 2, IntPtr.Zero, IntPtr.Zero);
            }
            static IntPtr TopWindow(IntPtr window, uint message, IntPtr w, IntPtr l) {
                Log("window", message, w);
                if (message == 7 && edit != IntPtr.Zero) { SetFocus(edit); }
                if (message == App + 1) { StringBuilder text = new StringBuilder(4096); GetWindowTextW(edit, text, text.Capacity); Say("text=" + text); return IntPtr.Zero; }
                if (message == App + 2) { DestroyWindow(window); return IntPtr.Zero; }
                if (message == 2) { PostQuitMessage(0); return IntPtr.Zero; }
                return DefWindowProcW(window, message, w, l);
            }
            static IntPtr Edit(IntPtr window, uint message, IntPtr w, IntPtr l, UIntPtr id, UIntPtr data) {
                Log("edit", message, w);
                return DefSubclassProc(window, message, w, l);
            }
            static void Log(string where, uint message, IntPtr w) {
                string name = message == 0x100 ? "WM_KEYDOWN" : message == 0x101 ? "WM_KEYUP" : message == 0x104 ? "WM_SYSKEYDOWN" : message == 0x105 ? "WM_SYSKEYUP" : message == 0x102 ? "WM_CHAR" : message == 0x302 ? "WM_PASTE" : message == 0x201 ? "WM_LBUTTONDOWN" : message == 0x202 ? "WM_LBUTTONUP" : null;
                if (name != null) { Say("message " + name + ":0x" + w.ToInt64().ToString("X") + " " + where); }
            }
            static void Say(string line) { lock (typeof(CheckTarget)) { Console.Out.WriteLine(line); Console.Out.Flush(); } }
        }
        '@
        [CheckTarget]::Run('{{title}}', {{x}}, {{y}}, ${{edit}})
        """;

    private readonly Process process;
    private readonly ConcurrentQueue<string> said;

    private TargetApp(Process process, ConcurrentQueue<string> said, nint window)
    {
        this.process = process;
        this.said = said;
        Window = window;
    }

    /// <summary>Its window's handle.</summary>
    public nint Window { get; }

    /// <summary>Its window's whole rectangle on the desktop, in physical pixels.</summary>
    public PixelRect Bounds => GetWindowRect(Window, out var rectangle) ? new PixelRect(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top) : default;

    public int ProcessId => process.Id;

    /// <summary>Every key message, character, paste and left-button press and release it has received so far, in order, as "WM_KEYDOWN:0x11" and the like.</summary>
    public List<string> Messages() => said.Where(line => line.StartsWith("message ")).Select(line => line.Split(' ')[1]).ToList();

    /// <summary>
    /// Starts it with its window's top-left corner at (<paramref name="x"/>, <paramref name="y"/>), with its edit box
    /// unless <paramref name="editBox"/> is false, and waits until the window exists.
    /// </summary>
    public static TargetApp Start(string title, int x, int y, bool editBox = true)
    {
        var script = Script.Replace("{{title}}", title).Replace("{{x}}", x.ToString()).Replace("{{y}}", y.ToString()).Replace("{{edit}}", editBox ? "true" : "false");
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
        {
            start.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = start };
        var said = new ConcurrentQueue<string>();
        process.OutputDataReceived += (_, line) =>
        {
            if (line.Data is { } data)
            {
                said.Enqueue(data);
            }
        };
        process.Start();
        process.BeginOutputReadLine();
        var clock = Stopwatch.StartNew();
        string? ready;
        while ((ready = said.FirstOrDefault(line => line.StartsWith("ready "))) is null && !process.HasExited && clock.Elapsed < TimeSpan.FromSeconds(60))
        {
            Thread.Sleep(20);
        }

        Assert.IsNotNull(ready, "the target window did not start");
        return new TargetApp(process, said, nint.Parse(ready["ready ".Length..]));
    }

    /// <summary>The edit box's text.</summary>
    public string Text() => Ask("text", "text=")["text=".Length..];

    /// <summary>A real click on its title bar, as a person brings it to the front, then until it is the window in front.</summary>
    public void ClickTitleBar() => ClickTitleBar(Window);

    /// <summary>A real click on the title bar of <paramref name="window"/>, then until it is the window in front.</summary>
    public static void ClickTitleBar(nint window)
    {
        Assert.IsTrue(GetWindowRect(window, out var rectangle));
        TestMouse.Click(new PixelPoint((rectangle.Left + rectangle.Right) / 2, rectangle.Top + 12));
        FrontWindow.WaitUntilInFront(window);
    }

    /// <summary>It is the window in front, and a letter typed now reaches it: <paramref name="when"/> says at what moment.</summary>
    public void KeepsTheKeystrokes(string when)
    {
        Thread.Sleep(300);
        Assert.AreEqual(Window, FrontWindow.GetForegroundWindow(), $"the chat in front {when}");
        var before = Messages().Count;
        TestHotKeys.Tap('A');
        var clock = Stopwatch.StartNew();
        while (!Messages().Skip(before).Contains("WM_KEYDOWN:0x41") && clock.ElapsedMilliseconds < 3000)
        {
            Thread.Sleep(20);
        }

        CollectionAssert.Contains(Messages().Skip(before).ToList(), "WM_KEYDOWN:0x41", $"a letter typed {when} reaches the chat");
    }

    /// <summary>Closes its window, which ends it.</summary>
    public void Close()
    {
        Tell("close");
        Assert.IsTrue(process.WaitForExit(10_000), "the target did not end");
    }

    private string Ask(string request, string answer)
    {
        var before = said.Count;
        Tell(request);
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (said.Skip(before).FirstOrDefault(line => line.StartsWith(answer)) is { } line)
            {
                return line;
            }

            Thread.Sleep(10);
        }

        Assert.Fail($"the target did not answer {request}");
        return "";
    }

    private void Tell(string line)
    {
        process.StandardInput.WriteLine(line);
        process.StandardInput.Flush();
    }

    public void Dispose()
    {
        if (!process.HasExited)
        {
            process.StandardInput.Close();
            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }

        process.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rectangle rectangle);
}
