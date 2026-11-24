using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Win32.Input;
using ImageStash.Seams;

namespace ImageStash.Windows.Seams;

/// <summary>
/// The global shortcuts as system hot keys (S5), on a thread of the seam's own with its own message loop: a hot key
/// registered with no window is posted to the queue of the thread that registered it, and Avalonia 12.1.4's
/// dispatcher hands no thread message to product code. The same loop samples the keyboard layout of the foreground
/// window's thread once a second (S13) and judges every registered combination under it, and judges a combination
/// once more when its hot key fires, before it acts; one that would then type a character is released, reported, and
/// its press swallowed (How G33 holds). One function judges at registration, at each sample and at each press.
/// </summary>
public sealed class WindowsShortcuts : IShortcuts, IDisposable
{
    private const uint WmQuit = 0x0012;
    private const uint WmTimer = 0x0113;
    private const uint WmHotkey = 0x0312;
    private const uint WmApp = 0x8000;
    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModWin = 0x8;
    private const uint ModNoRepeat = 0x4000;
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private const uint SampleMilliseconds = 1000;
    private const uint PmNoRemove = 0;
    private const uint MapVkToScanCode = 0;
    // ToUnicodeEx's flag bit 2: keyboard state is not changed (Windows 10, version 1607 and newer).
    private const uint KeepKeyboardState = 0x4;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLeftWin = 0x5B;
    private const int VkLeftShift = 0xA0;
    private const int VkLeftControl = 0xA2;
    private const int VkLeftMenu = 0xA4;

    private readonly Func<Combination, nint, ShortcutRefusal?> typing;
    private readonly Thread thread;
    private readonly ConcurrentQueue<Action> work = new();
    // Touched only on the seam's thread.
    private readonly Dictionary<int, Registration> registered = [];
    private int nextId = 1;
    private uint threadId;
    private volatile bool ended;
    private volatile Action<Shortcut, ShortcutRefusal>? released;

    public WindowsShortcuts()
        : this(Typing)
    {
    }

    /// <param name="typing">The layout judgement, with the character typed; checks give one of their own to change what the layout in use types.</param>
    internal WindowsShortcuts(Func<Combination, nint, ShortcutRefusal?> typing)
    {
        this.typing = typing;
        using var ready = new ManualResetEventSlim();
        thread = new Thread(() => Loop(ready)) { IsBackground = true, Name = "Shortcuts" };
        thread.Start();
        ready.Wait();
    }

    public ShortcutRefusal? Register(Shortcut shortcut, Action pressed)
    {
        var combination = CombinationOf(shortcut);
        if (combination.Key == 0)
        {
            return ShortcutRefusal.NotAKey(shortcut.Gesture.Key);
        }

        var registration = new Registration(shortcut, combination, pressed);
        return OnThread(() => RegisterHere(registration), () => ShortcutRefusal.Stated("ImageStash's shortcuts have stopped"));
    }

    public void Release(Shortcut shortcut) => OnThread(() =>
    {
        foreach (var (id, registration) in registered.Where(r => r.Value.Shortcut == shortcut).ToList())
        {
            UnregisterHotKey(0, id);
            registered.Remove(id);
        }

        return true;
    }, () => false);

    public void ListenForReleases(Action<Shortcut, ShortcutRefusal> released) => this.released = released;

    /// <summary>
    /// Whether pressing <paramref name="combination"/> would type a character under <paramref name="layout"/>, judged
    /// from its translation made without touching the keyboard state (S5).
    /// </summary>
    internal static bool TypesACharacter(Combination combination, nint layout) => Typing(combination, layout) is not null;

    /// <summary>
    /// Why <paramref name="combination"/> cannot be a shortcut under <paramref name="layout"/>, when it would type a
    /// character there, with the character its one translation yields (none for a dead key, whose translation is the
    /// accent waiting for the next key); null when it types none. Judged as <see cref="TypesACharacter(uint, int, ReadOnlySpan{char})"/> judges.
    /// </summary>
    internal static ShortcutRefusal? Typing(Combination combination, nint layout)
    {
        var state = new byte[256];
        void Down(params int[] keys)
        {
            foreach (var key in keys)
            {
                state[key] = 0x80;
            }
        }

        if ((combination.Modifiers & ModControl) != 0)
        {
            Down(VkControl, VkLeftControl);
        }

        if ((combination.Modifiers & ModAlt) != 0)
        {
            Down(VkMenu, VkLeftMenu);
        }

        if ((combination.Modifiers & ModShift) != 0)
        {
            Down(VkShift, VkLeftShift);
        }

        if ((combination.Modifiers & ModWin) != 0)
        {
            Down(VkLeftWin);
        }

        var buffer = new char[8];
        var scanCode = MapVirtualKeyExW(combination.Key, MapVkToScanCode, layout);
        var count = ToUnicodeEx(combination.Key, scanCode, state, buffer, buffer.Length, KeepKeyboardState, layout);
        return Typing(combination.Modifiers, count, buffer);
    }

    /// <summary>
    /// Why a combination of <paramref name="modifiers"/> cannot be a shortcut, given its translation, when
    /// <see cref="TypesACharacter(uint, int, ReadOnlySpan{char})"/> judges that it types: with the characters the translation
    /// counts, none for a dead key; null when it types none.
    /// </summary>
    internal static ShortcutRefusal? Typing(uint modifiers, int count, ReadOnlySpan<char> translation) =>
        !TypesACharacter(modifiers, count, translation) ? null
        : ShortcutRefusal.TypesACharacter(count > 0 ? new string(translation[..Math.Min(count, translation.Length)]) : null);

    /// <summary>
    /// Whether a combination of <paramref name="modifiers"/> types a character, given its translation: ToUnicodeEx's
    /// <paramref name="count"/>, negative for a dead key, of the characters in <paramref name="translation"/>. A dead key
    /// or a character types, other than the control code <c>Ctrl</c> makes of a key (<c>Ctrl</c> with a letter yields
    /// one, and no text receives it); without <c>Ctrl</c>, a control character is the key's own, Enter's, Tab's,
    /// Backspace's and Esc's, and is typed. The Windows key with Alt and without <c>Ctrl</c> types none, whatever the
    /// translation (for a letter, the letter alone): Windows delivers that press as a system keystroke, a menu mnemonic
    /// with no typed text (S5).
    /// </summary>
    internal static bool TypesACharacter(uint modifiers, int count, ReadOnlySpan<char> translation)
    {
        var withCtrl = (modifiers & ModControl) != 0;
        if (!withCtrl && (modifiers & (ModWin | ModAlt)) == (ModWin | ModAlt))
        {
            return false;
        }

        if (count < 0)
        {
            return true;
        }

        foreach (var character in translation[..Math.Min(count, translation.Length)])
        {
            if (!char.IsControl(character) || !withCtrl)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The keyboard layout of the foreground window's thread, the layout keystrokes are typed in now (S13).</summary>
    internal static nint ForegroundLayout() => GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), 0));

    /// <summary>The combination Windows registers for <paramref name="shortcut"/>; its key is 0 for a key Windows has no code for.</summary>
    internal static Combination CombinationOf(Shortcut shortcut) =>
        new(ModifiersOf(shortcut.Gesture.KeyModifiers), (uint)KeyInterop.VirtualKeyFromKey(shortcut.Gesture.Key));

    private static uint ModifiersOf(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Control) ? ModControl : 0)
        | (modifiers.HasFlag(KeyModifiers.Alt) ? ModAlt : 0)
        | (modifiers.HasFlag(KeyModifiers.Shift) ? ModShift : 0)
        | (modifiers.HasFlag(KeyModifiers.Meta) ? ModWin : 0);

    private ShortcutRefusal? RegisterHere(Registration registration)
    {
        if (typing(registration.Combination, ForegroundLayout()) is { } types)
        {
            return types;
        }

        var id = nextId++;
        if (!RegisterHotKey(0, id, registration.Combination.Modifiers | ModNoRepeat, registration.Combination.Key))
        {
            var error = Marshal.GetLastPInvokeError();
            return error == ErrorHotkeyAlreadyRegistered ? ShortcutRefusal.HeldByAnotherProgram : ShortcutRefusal.Stated(new Win32Exception(error).Message);
        }

        registered[id] = registration;
        return null;
    }

    private void Loop(ManualResetEventSlim ready)
    {
        threadId = GetCurrentThreadId();
        // The thread's message queue exists once it has called a message function: made before anything is posted to it.
        PeekMessageW(out _, 0, 0, 0, PmNoRemove);
        var timer = SetTimer(0, 0, SampleMilliseconds, 0);
        ready.Set();
        while (GetMessageW(out var message, 0, 0, 0) > 0)
        {
            switch (message.Id)
            {
                case WmApp:
                    while (work.TryDequeue(out var job))
                    {
                        job();
                    }

                    break;
                case WmTimer:
                    JudgeAll();
                    break;
                case WmHotkey:
                    Fire((int)message.WParam);
                    break;
            }
        }

        KillTimer(0, timer);
        foreach (var id in registered.Keys)
        {
            UnregisterHotKey(0, id);
        }

        registered.Clear();
    }

    /// <summary>The once-a-second check (G33): every registered combination under the layout in use now.</summary>
    private void JudgeAll()
    {
        var layout = ForegroundLayout();
        foreach (var (id, registration) in registered.ToList())
        {
            if (typing(registration.Combination, layout) is { } types)
            {
                ReleaseHere(id, registration, types);
            }
        }
    }

    /// <summary>A press, judged before it acts (G33); a combination that would now type a character is released and its press swallowed.</summary>
    private void Fire(int id)
    {
        if (!registered.TryGetValue(id, out var registration))
        {
            return;
        }

        if (typing(registration.Combination, ForegroundLayout()) is { } types)
        {
            ReleaseHere(id, registration, types);
            return;
        }

        registration.Pressed();
    }

    private void ReleaseHere(int id, Registration registration, ShortcutRefusal types)
    {
        UnregisterHotKey(0, id);
        registered.Remove(id);
        released?.Invoke(registration.Shortcut, types);
    }

    /// <summary>Runs <paramref name="job"/> on the seam's thread and waits for it; <paramref name="whenEnded"/> once that thread has stopped.</summary>
    private T OnThread<T>(Func<T> job, Func<T> whenEnded)
    {
        if (Environment.CurrentManagedThreadId == thread.ManagedThreadId)
        {
            return job();
        }

        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Enqueue(() =>
        {
            try
            {
                done.SetResult(job());
            }
            catch (Exception error)
            {
                done.SetException(error);
            }
        });
        if (ended || !PostThreadMessageW(threadId, WmApp, 0, 0))
        {
            return whenEnded();
        }

        return done.Task.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (ended)
        {
            return;
        }

        ended = true;
        PostThreadMessageW(threadId, WmQuit, 0, 0);
        thread.Join();
    }

    /// <summary>A combination as Windows registers it: MOD_ flags and a virtual-key code.</summary>
    internal readonly record struct Combination(uint Modifiers, uint Key);

    private sealed record Registration(Shortcut Shortcut, Combination Combination, Action Pressed);

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

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetMessageW(out ThreadMessage message, nint window, uint first, uint last);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(out ThreadMessage message, nint window, uint first, uint last, uint remove);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(uint thread, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint callback);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool KillTimer(nint window, nuint id);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint window, nint processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetKeyboardLayout(uint thread);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint MapVirtualKeyExW(uint code, uint mapType, nint layout);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyState, [Out, MarshalAs(UnmanagedType.LPArray)] char[] buffer, int bufferSize, uint flags, nint layout);
}
