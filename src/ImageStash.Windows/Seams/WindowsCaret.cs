using System.Runtime.InteropServices;
using Avalonia;

namespace ImageStash.Windows.Seams;

/// <summary>Which of G1's caret lookups found the caret (T1).</summary>
public enum CaretStep
{
    /// <summary>The Windows caret of the window's thread.</summary>
    WindowsCaret,

    /// <summary>The accessibility caret object of that thread's focused window.</summary>
    AccessibleCaret,

    /// <summary>Through UI Automation, the bounds of the focused text control's collapsed selection.</summary>
    Selection,

    /// <summary>Through UI Automation, the bounds of that selection expanded to one character.</summary>
    SelectionCharacter,
}

/// <summary>A caret found: its rectangle on the screen in physical pixels, and the lookup that found it.</summary>
public sealed record CaretFound(PixelRect Rectangle, CaretStep Step);

/// <summary>
/// The text caret of the focused control in a window, by G1's chain in its order (How the guarantees hold, G1; S15):
/// the Windows caret of the window's thread; else the accessibility caret object of that thread's focused window; else,
/// through UI Automation, the bounds of the focused text control's collapsed selection, then of that selection expanded
/// to one character; else none. A step answers only for that window: the thread's caret and focus count only inside
/// it, and UI Automation's focused element only while the window is in front and only from its process, so another
/// window, of the same thread or one that took the focus since, lends no caret; and selected text is no caret. Each
/// lookup runs on a thread of its own in the
/// multithreaded apartment, never one that serves a window, so an app that does not answer holds only that thread: the
/// lookup is abandoned at its bound, its late answer dropped, and the next lookup starts on a new thread.
/// </summary>
public static class WindowsCaret
{
    private const uint ObjectIdCaret = 0xFFFFFFF8;
    private const int ChildSelf = 0;
    private const int ProcessIdProperty = 30002;
    private const int TextPattern = 10014;
    private const int CharacterUnit = 0;
    private const int StartEndpoint = 0;
    private const int EndEndpoint = 1;
    private const uint RootAncestor = 2;
    private const nint PerMonitorAwareV2 = -4;
    private const uint MonitorNearest = 2;
    private const int EffectiveDpi = 0;

    /// <summary>The caret of the focused control in <paramref name="window"/> and the step that found it, or null when none is found within <paramref name="bound"/>.</summary>
    public static async Task<CaretFound?> Find(nint window, TimeSpan bound)
    {
        var answer = new TaskCompletionSource<CaretFound?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lookup = new Thread(() =>
        {
            // Every rectangle in physical pixels, whatever awareness the process itself declared (E1).
            SetThreadDpiAwarenessContext(PerMonitorAwareV2);
            answer.SetResult(Lookup(window));
        })
        { IsBackground = true, Name = "Caret lookup" };
        lookup.SetApartmentState(ApartmentState.MTA);
        lookup.Start();
        try
        {
            return await answer.Task.WaitAsync(bound);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    /// <summary>The chain itself, on the calling thread, unbounded.</summary>
    internal static CaretFound? Lookup(nint window)
    {
        var thread = GetWindowThreadProcessId(window, out var process);
        if (thread == 0)
        {
            return null;
        }

        var root = GetAncestor(window, RootAncestor);
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        var known = GetGUIThreadInfo(thread, ref info);
        if (known && Within(info.Caret, root) && WindowsCaretOf(info) is { } windowsCaret)
        {
            return new CaretFound(windowsCaret, CaretStep.WindowsCaret);
        }

        var focused = known && Within(info.Focus, root) ? info.Focus : window;
        GetWindowThreadProcessId(focused, out var focusedProcess);
        return Attempt(() => AccessibleCaretOf(focused)) ?? Attempt(() => SelectionCaretOf(root, process, focusedProcess));
    }

    private static bool Within(nint child, nint root) => child != 0 && GetAncestor(child, RootAncestor) == root;

    /// <summary>Whether the window in front is <paramref name="root"/>, which then holds the keyboard focus.</summary>
    private static bool InFront(nint root) => GetAncestor(GetForegroundWindow(), RootAncestor) == root;

    /// <summary>
    /// The Windows caret of a thread, from its client coordinates to the screen's (GUITHREADINFO). Its rectangle is in
    /// the caret window's own logical coordinates, "not virtualized into the mode of the calling thread"
    /// (GetGUIThreadInfo, DPI Virtualization), so it is scaled from that window's DPI to its display's before it is
    /// placed at the client area's origin, which this per-monitor aware thread reads in physical pixels.
    /// </summary>
    internal static PixelRect? WindowsCaretOf(GuiThreadInfo info)
    {
        var caret = info.CaretRectangle;
        if (info.Caret == 0 || caret.Bottom <= caret.Top)
        {
            return null;
        }

        var origin = new NativePoint();
        return ClientToScreen(info.Caret, ref origin) ? OnScreen(new PixelPoint(origin.X, origin.Y), caret, PhysicalPerLogical(info.Caret)) : null;
    }

    /// <summary>A caret rectangle in its window's logical client coordinates, on the screen in physical pixels.</summary>
    internal static PixelRect OnScreen(PixelPoint clientOrigin, Rectangle caret, double physicalPerLogical)
    {
        int Scaled(int logical) => (int)Math.Round(logical * physicalPerLogical);
        return new PixelRect(clientOrigin.X + Scaled(caret.Left), clientOrigin.Y + Scaled(caret.Top), Math.Max(0, Scaled(caret.Right - caret.Left)), Scaled(caret.Bottom - caret.Top));
    }

    /// <summary>
    /// The window's display DPI over its own: 1 for a per-monitor aware window, whose DPI is its display's; 2 for a
    /// DPI-unaware one, whose DPI is 96, on a 200 % display (GetDpiForWindow; GetDpiForMonitor's effective DPI, the
    /// actual one for a per-monitor aware process). 1 when either cannot be read.
    /// </summary>
    private static double PhysicalPerLogical(nint window)
    {
        var logical = GetDpiForWindow(window);
        return logical > 0 && GetDpiForMonitor(MonitorFromWindow(window, MonitorNearest), EffectiveDpi, out var physical, out _) == 0 && physical > 0
            ? physical / (double)logical
            : 1;
    }

    /// <summary>The accessibility caret object of <paramref name="focused"/>, located on the screen.</summary>
    internal static CaretFound? AccessibleCaretOf(nint focused)
    {
        var iid = typeof(IAccessible).GUID;
        if (AccessibleObjectFromWindow(focused, ObjectIdCaret, ref iid, out var caret) < 0 || caret is null)
        {
            return null;
        }

        try
        {
            return caret.AccLocation(out var left, out var top, out var width, out var height, ChildSelf) >= 0 && height > 0
                ? new CaretFound(new PixelRect(left, top, Math.Max(0, width), height), CaretStep.AccessibleCaret)
                : null;
        }
        finally
        {
            Marshal.FinalReleaseComObject(caret);
        }
    }

    /// <summary>
    /// The collapsed selection of the focused text control in the top-level window <paramref name="root"/> through UI
    /// Automation, else that selection expanded to one character; none unless that window is in front throughout and
    /// the focused element is of <paramref name="process"/> or <paramref name="focusedProcess"/>.
    /// </summary>
    internal static CaretFound? SelectionCaretOf(nint root, uint process, uint focusedProcess)
    {
        var held = new List<object>();
        try
        {
            if (!InFront(root))
            {
                return null;
            }

            var automation = (IUIAutomation)new CUIAutomation();
            held.Add(automation);
            if (automation.GetFocusedElement(out var element) < 0 || element is null)
            {
                return null;
            }

            held.Add(element);
            if (element.GetCurrentPropertyValue(ProcessIdProperty, out var owner) < 0 || owner is not int id || ((uint)id != process && (uint)id != focusedProcess) || !InFront(root))
            {
                return null;
            }

            if (element.GetCurrentPattern(TextPattern, out var found) < 0 || found is null)
            {
                return null;
            }

            held.Add(found);
            if (found is not IUIAutomationTextPattern pattern || pattern.GetSelection(out var ranges) < 0 || ranges is null)
            {
                return null;
            }

            held.Add(ranges);
            if (ranges.GetLength(out var count) < 0 || count == 0 || ranges.GetElement(0, out var selection) < 0 || selection is null)
            {
                return null;
            }

            held.Add(selection);
            if (selection.CompareEndpoints(StartEndpoint, selection, EndEndpoint, out var order) < 0 || order != 0)
            {
                return null;
            }

            if (BoundsOf(selection) is { } collapsed)
            {
                return new CaretFound(collapsed, CaretStep.Selection);
            }

            // A degenerate range answers no rectangle (GetBoundingRectangles); a copy of it expanded to the character at it does.
            if (selection.Clone(out var character) < 0 || character is null)
            {
                return null;
            }

            held.Add(character);
            return character.ExpandToEnclosingUnit(CharacterUnit) >= 0 && BoundsOf(character) is { } bounds ? new CaretFound(bounds, CaretStep.SelectionCharacter) : null;
        }
        finally
        {
            for (var at = held.Count - 1; at >= 0; at--)
            {
                Marshal.FinalReleaseComObject(held[at]);
            }
        }
    }

    /// <summary>A range's first rectangle, packed as left, top, width and height in physical screen coordinates; null for none.</summary>
    private static PixelRect? BoundsOf(IUIAutomationTextRange range)
    {
        if (range.GetBoundingRectangles(out var packed) < 0 || packed is not { Length: >= 4 })
        {
            return null;
        }

        var bounds = new PixelRect((int)Math.Round(packed[0]), (int)Math.Round(packed[1]), Math.Max(0, (int)Math.Round(packed[2])), (int)Math.Round(packed[3]));
        return bounds.Height > 0 ? bounds : null;
    }

    /// <summary>
    /// One step of the chain: another program's accessibility answers fail in whatever exception its HRESULT maps to,
    /// and a step that failed found no caret, so the chain goes on to the next (G1: "when no caret can be found").
    /// </summary>
    private static CaretFound? Attempt(Func<CaretFound?> step)
    {
        try
        {
            return step();
        }
        catch (Exception)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public nint Active;
        public nint Focus;
        public nint Capture;
        public nint MenuOwner;
        public nint MoveSize;
        public nint Caret;
        public Rectangle CaretRectangle;
    }

    // The interfaces below declare every slot before the last one called, in the order of the type libraries Windows
    // carries (oleacc.dll for IAccessible, a dual interface; UIAutomationCore.dll for UI Automation), read on build 26300.

    [ComImport]
    [Guid("618736e0-3c3d-11cf-810c-00aa00389b71")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IAccessible
    {
        [PreserveSig] int GetAccParent([MarshalAs(UnmanagedType.IDispatch)] out object? parent);
        [PreserveSig] int GetAccChildCount(out int count);
        [PreserveSig] int GetAccChild([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.IDispatch)] out object? found);
        [PreserveSig] int GetAccName([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string? name);
        [PreserveSig] int GetAccValue([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string? value);
        [PreserveSig] int GetAccDescription([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string? description);
        [PreserveSig] int GetAccRole([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.Struct)] out object? role);
        [PreserveSig] int GetAccState([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.Struct)] out object? state);
        [PreserveSig] int GetAccHelp([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string? help);
        [PreserveSig] int GetAccHelpTopic([MarshalAs(UnmanagedType.BStr)] out string? helpFile, [MarshalAs(UnmanagedType.Struct)] object child, out int topic);
        [PreserveSig] int GetAccKeyboardShortcut([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string? shortcut);
        [PreserveSig] int GetAccFocus([MarshalAs(UnmanagedType.Struct)] out object? focus);
        [PreserveSig] int GetAccSelection([MarshalAs(UnmanagedType.Struct)] out object? selection);
        [PreserveSig] int GetAccDefaultAction([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string? action);
        [PreserveSig] int AccSelect(int flags, [MarshalAs(UnmanagedType.Struct)] object child);
        [PreserveSig] int AccLocation(out int left, out int top, out int width, out int height, [MarshalAs(UnmanagedType.Struct)] object child);
    }

    [ComImport]
    [Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
    private class CUIAutomation
    {
    }

    [ComImport]
    [Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        [PreserveSig] int CompareElements(IntPtr first, IntPtr second, out int same);
        [PreserveSig] int CompareRuntimeIds(IntPtr first, IntPtr second, out int same);
        [PreserveSig] int GetRootElement(out IntPtr root);
        [PreserveSig] int ElementFromHandle(IntPtr window, out IntPtr element);
        [PreserveSig] int ElementFromPoint(NativePoint point, out IntPtr element);
        [PreserveSig] int GetFocusedElement(out IUIAutomationElement? element);
    }

    [ComImport]
    [Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        [PreserveSig] int SetFocus();
        [PreserveSig] int GetRuntimeId(out IntPtr runtimeId);
        [PreserveSig] int FindFirst(int scope, IntPtr condition, out IntPtr found);
        [PreserveSig] int FindAll(int scope, IntPtr condition, out IntPtr found);
        [PreserveSig] int FindFirstBuildCache(int scope, IntPtr condition, IntPtr cacheRequest, out IntPtr found);
        [PreserveSig] int FindAllBuildCache(int scope, IntPtr condition, IntPtr cacheRequest, out IntPtr found);
        [PreserveSig] int BuildUpdatedCache(IntPtr cacheRequest, out IntPtr updated);
        [PreserveSig] int GetCurrentPropertyValue(int property, [MarshalAs(UnmanagedType.Struct)] out object? value);
        [PreserveSig] int GetCurrentPropertyValueEx(int property, int ignoreDefault, [MarshalAs(UnmanagedType.Struct)] out object? value);
        [PreserveSig] int GetCachedPropertyValue(int property, [MarshalAs(UnmanagedType.Struct)] out object? value);
        [PreserveSig] int GetCachedPropertyValueEx(int property, int ignoreDefault, [MarshalAs(UnmanagedType.Struct)] out object? value);
        [PreserveSig] int GetCurrentPatternAs(int pattern, in Guid iid, out IntPtr found);
        [PreserveSig] int GetCachedPatternAs(int pattern, in Guid iid, out IntPtr found);
        [PreserveSig] int GetCurrentPattern(int pattern, [MarshalAs(UnmanagedType.IUnknown)] out object? found);
    }

    [ComImport]
    [Guid("32eba289-3583-42c9-9c59-3b6d9a1e9b6a")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextPattern
    {
        [PreserveSig] int RangeFromPoint(NativePoint point, out IntPtr range);
        [PreserveSig] int RangeFromChild(IntPtr child, out IntPtr range);
        [PreserveSig] int GetSelection(out IUIAutomationTextRangeArray? ranges);
    }

    [ComImport]
    [Guid("ce4ae76a-e717-4c98-81ea-47371d028eb6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRangeArray
    {
        [PreserveSig] int GetLength(out int length);
        [PreserveSig] int GetElement(int index, out IUIAutomationTextRange? range);
    }

    [ComImport]
    [Guid("a543cc6a-f4ae-494b-8239-c814481187a8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRange
    {
        [PreserveSig] int Clone(out IUIAutomationTextRange? copy);
        [PreserveSig] int Compare(IntPtr range, out int same);
        [PreserveSig] int CompareEndpoints(int endpoint, IUIAutomationTextRange range, int targetEndpoint, out int order);
        [PreserveSig] int ExpandToEnclosingUnit(int unit);
        [PreserveSig] int FindAttribute(int attribute, [MarshalAs(UnmanagedType.Struct)] object value, int backward, out IntPtr found);
        [PreserveSig] int FindText([MarshalAs(UnmanagedType.BStr)] string text, int backward, int ignoreCase, out IntPtr found);
        [PreserveSig] int GetAttributeValue(int attribute, [MarshalAs(UnmanagedType.Struct)] out object? value);
        [PreserveSig] int GetBoundingRectangles([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_R8)] out double[]? rectangles);
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SetThreadDpiAwarenessContext(nint context);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref NativePoint point);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("shcore.dll", ExactSpelling = true)]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("oleacc.dll", ExactSpelling = true)]
    private static extern int AccessibleObjectFromWindow(nint window, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IAccessible? accessible);
}
