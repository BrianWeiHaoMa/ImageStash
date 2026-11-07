using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ImageStash.Seams;
using ImageStash.Windows.Seams;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// The checks' own keyboard: a shortcut pressed and released through SendInput, as a person's keys arrive, so a hot
/// key registered for it fires; the window in front receives the modifiers' own key messages, as it does for a person.
/// </summary>
public static class TestHotKeys
{
    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModWin = 0x8;
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkLeftWin = 0x5B;
    private static readonly TimeSpan SettleBeforeSwitch = TimeSpan.FromMilliseconds(500);

    /// <summary>Presses the shortcut's modifiers and key, then releases them in reverse order.</summary>
    public static void Press(Shortcut shortcut)
    {
        var keys = KeysOf(shortcut);
        Send(keys.Select(key => Key(key, up: false)).Concat(Enumerable.Reverse(keys).Select(key => Key(key, up: true))));
    }

    /// <summary>Presses the shortcut's modifiers and key, then releases the modifiers before the key, as a person may.</summary>
    public static void PressReleasingModifiersFirst(Shortcut shortcut)
    {
        var keys = KeysOf(shortcut);
        Send(keys.Select(key => Key(key, up: false)).Concat(keys.Select(key => Key(key, up: true))));
    }

    /// <summary>The shortcut's modifiers' keys, then its key.</summary>
    private static List<ushort> KeysOf(Shortcut shortcut)
    {
        var combination = WindowsShortcuts.CombinationOf(shortcut);
        return new[] { (ModControl, VkControl), (ModAlt, VkMenu), (ModShift, VkShift), (ModWin, VkLeftWin) }
            .Where(modifier => (combination.Modifiers & modifier.Item1) != 0)
            .Select(modifier => modifier.Item2)
            .Append((ushort)combination.Key)
            .ToList();
    }

    private static void Send(IEnumerable<Input> sent)
    {
        var inputs = sent.ToArray();
        Assert.AreEqual((uint)inputs.Length, SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()), $"the keys were not sent: error {Marshal.GetLastPInvokeError()}");
    }

    /// <summary>One key, by its virtual-key code, pressed and released alone.</summary>
    public static void Tap(ushort virtualKey)
    {
        Input[] inputs = [Key(virtualKey, up: false), Key(virtualKey, up: true)];
        Assert.AreEqual((uint)inputs.Length, SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()), $"the key was not sent: error {Marshal.GetLastPInvokeError()}");
    }

    /// <summary>
    /// Alt+Tab as a person switches windows: from a window in front for a moment, Alt held, Tab pressed once,
    /// <paramref name="whileHeld"/> run after <paramref name="held"/>, then Alt released.
    /// </summary>
    public static void SwitchWindows(TimeSpan held, Action whileHeld)
    {
        const ushort vkTab = 0x09;
        // Pressed the instant a click brought a window to the front, Windows' switch can still miss that activation and
        // leave the window in front, or bring a third one forward (5 of 34 quick switches on build 26300, none of 33 after
        // this wait); a person never switches that fast.
        Thread.Sleep(SettleBeforeSwitch);
        Input[] press = [Key(VkMenu, up: false), Key(vkTab, up: false), Key(vkTab, up: true)];
        try
        {
            Assert.AreEqual((uint)press.Length, SendInput((uint)press.Length, press, Marshal.SizeOf<Input>()), $"the keys were not sent: error {Marshal.GetLastPInvokeError()}");
            Thread.Sleep(held);
            whileHeld();
        }
        finally
        {
            Input[] release = [Key(VkMenu, up: true)];
            SendInput(1, release, Marshal.SizeOf<Input>());
        }
    }

    /// <summary>Alt pressed and held, as a person holds it during a double-click, until the returned object is disposed.</summary>
    public static IDisposable HoldAlt() => Hold(VkMenu);

    /// <summary>One key, by its virtual-key code, pressed and held until the returned object is first disposed.</summary>
    public static IDisposable Hold(ushort virtualKey)
    {
        Input[] press = [Key(virtualKey, up: false)];
        Assert.AreEqual(1u, SendInput(1, press, Marshal.SizeOf<Input>()), $"the key was not pressed: error {Marshal.GetLastPInvokeError()}");
        return new HeldKey(virtualKey);
    }

    private sealed class HeldKey(ushort key) : IDisposable
    {
        private bool released;

        public void Dispose()
        {
            if (released)
            {
                return;
            }

            released = true;
            Input[] release = [Key(key, up: true)];
            SendInput(1, release, Marshal.SizeOf<Input>());
        }
    }

    private static Input Key(ushort key, bool up) => new()
    {
        Type = InputKeyboard,
        Keyboard = new KeyboardInput { VirtualKey = key, ScanCode = (ushort)MapVirtualKeyW(key, 0), Flags = up ? KeyUp : 0 },
    };

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

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);
}

/// <summary>
/// Another program holding a combination as its global hot key: a PowerShell that registers it on its own thread and
/// keeps it until <see cref="Dispose"/>; a press of the combination then goes to it.
/// </summary>
public sealed class OtherProgramHoldingAHotKey : IDisposable
{
    private readonly Process holder;

    private OtherProgramHoldingAHotKey(Process holder) => this.holder = holder;

    /// <summary>Starts the holder; null when the combination could not be held because something already holds it.</summary>
    public static OtherProgramHoldingAHotKey? Start(Shortcut shortcut)
    {
        var combination = WindowsShortcuts.CombinationOf(shortcut);
        var script = $$"""
            Add-Type -TypeDefinition @'
            using System;
            using System.Runtime.InteropServices;
            public static class HotKeyHolder {
                [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
                [DllImport("user32.dll", SetLastError = true)] public static extern bool UnregisterHotKey(IntPtr window, int id);
            }
            '@
            if (-not [HotKeyHolder]::RegisterHotKey([IntPtr]::Zero, 1, {{combination.Modifiers | 0x4000}}, {{combination.Key}})) { 'refused ' + [Runtime.InteropServices.Marshal]::GetLastWin32Error(); exit 1 }
            [Console]::Out.WriteLine('held'); [Console]::Out.Flush()
            [void][Console]::In.ReadLine()
            [void][HotKeyHolder]::UnregisterHotKey([IntPtr]::Zero, 1)
            """;
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

        var holder = new OtherProgramHoldingAHotKey(Process.Start(start)!);
        var said = holder.holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60)).GetAwaiter().GetResult();
        if (said == "held")
        {
            return holder;
        }

        holder.Dispose();
        Assert.StartsWith("refused 1409", said ?? "", "the holder failed for another reason than a held combination");
        return null;
    }

    public void Dispose()
    {
        if (!holder.HasExited)
        {
            holder.StandardInput.WriteLine();
            holder.StandardInput.Flush();
            if (!holder.WaitForExit(10_000))
            {
                // Its exit releases the hot key its thread held.
                holder.Kill(entireProcessTree: true);
                holder.WaitForExit();
            }
        }

        holder.Dispose();
    }
}
