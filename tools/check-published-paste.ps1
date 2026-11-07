<#
.SYNOPSIS
The paste moment on the published copy, run by check-published.ps1 inside its session (it uses that script's
StashWindows and CopyReadBack): a target window of the check's own, another PowerShell showing an edit box with a caret
and logging every key it receives; from the tray, a posted click and a posted Enter that paste into it; the real
Ctrl+Alt+Space showing the flyout beside its caret within a second; the footer naming it; a real Enter hiding the
flyout and sending it one Ctrl+V; two real letters typed into the search box and Enter pasting their first match; the target closed between summon and Enter, which copies and raises "Copied <middle
dot> press Ctrl+V"; the shortcut hiding the flyout; and, with another program holding Ctrl+Alt+Space when ImageStash starts, a
press that shows nothing while the tray still does. Exits with the number of checks that failed.

With -Store, the running copy's stash is the check's own and the phases that press Enter run; without it the stash is
the user's, and those phases are skipped, since each Enter records a use in it.

Moves the mouse pointer and puts it back, clicks its own window's title bar near the primary display's top-left
corner, sends real keystrokes (the shortcut, two letters, Enter, Esc), replaces the clipboard, restarts the copy once, and leaves
the restarted copy running for check-published.ps1 to stop.
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [Parameter(Mandatory)] [int] $Running,
    [string] $Store = '',
    [Parameter(Mandatory)] [string] $Work
)
$ErrorActionPreference = 'Stop'
$failed = 0
$dot = [char]0xB7
$copiedTitle = "Copied $dot press Ctrl+V"

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// The desk as the check drives and reads it, in physical pixels (per-monitor aware on the calling thread). The INPUT
// layouts are x64's: the union at offset 8, 40 bytes in all.
public static class PasteDesk
{
    private delegate bool EnumProc(IntPtr window, IntPtr data);

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct KeyInput { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public ushort Key; [FieldOffset(10)] public ushort Scan; [FieldOffset(12)] public uint Flags; }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct MouseInput { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public int X; [FieldOffset(12)] public int Y; [FieldOffset(20)] public uint Flags; }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo { public int Size; public uint Flags; public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret; public Rect CaretRect; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetWindowLongW(IntPtr window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, KeyInput[] inputs, int size);
    [DllImport("user32.dll", EntryPoint = "SendInput")] private static extern uint SendMouse(uint count, MouseInput[] inputs, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKeyW(uint code, uint type);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)] private static extern IntPtr SendText(IntPtr window, uint message, IntPtr capacity, StringBuilder text, uint flags, uint milliseconds, out IntPtr result);

    private static void Physical() { SetThreadDpiAwarenessContext(new IntPtr(-4)); }

    public static IntPtr Foreground() { return GetForegroundWindow(); }

    public static int ProcessOf(IntPtr window) { uint process; GetWindowThreadProcessId(window, out process); return (int)process; }

    public static bool Exists(IntPtr window) { return IsWindow(window); }

    /// <summary>The visible window of <paramref name="processId"/> titled <paramref name="title"/>, or none.</summary>
    public static IntPtr Titled(int processId, string title)
    {
        var found = IntPtr.Zero;
        EnumWindows(delegate (IntPtr window, IntPtr data)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            var text = new StringBuilder(256);
            GetWindowTextW(window, text, text.Capacity);
            if (owner == processId && IsWindowVisible(window) && text.ToString() == title) { found = window; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>The visible window of <paramref name="processId"/> without a title bar that is not <paramref name="notTitled"/>: the flyout; or none.</summary>
    public static IntPtr Flyout(int processId, string notTitled)
    {
        var found = IntPtr.Zero;
        EnumWindows(delegate (IntPtr window, IntPtr data)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            var text = new StringBuilder(256);
            GetWindowTextW(window, text, text.Capacity);
            // GWL_STYLE without WS_CAPTION.
            if (owner == processId && IsWindowVisible(window) && (GetWindowLongW(window, -16) & 0x00C00000) != 0x00C00000 && text.ToString() != notTitled) { found = window; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Left, top, right, bottom.</summary>
    public static int[] WindowRect(IntPtr window) { Physical(); Rect r; GetWindowRect(window, out r); return new[] { r.Left, r.Top, r.Right, r.Bottom }; }

    /// <summary>The working area of the display <paramref name="window"/> is on: left, top, right, bottom.</summary>
    public static int[] WorkArea(IntPtr window)
    {
        Physical();
        var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
        GetMonitorInfoW(MonitorFromWindow(window, 2), ref info);
        return new[] { info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom };
    }

    public static double Scaling(IntPtr window) { return GetDpiForWindow(window) / 96.0; }

    /// <summary>The Windows caret of <paramref name="window"/>'s thread on the screen: left, top, width, height; null for none.</summary>
    public static int[] Caret(IntPtr window)
    {
        Physical();
        uint process;
        var info = new GuiThreadInfo { Size = Marshal.SizeOf(typeof(GuiThreadInfo)) };
        if (!GetGUIThreadInfo(GetWindowThreadProcessId(window, out process), ref info) || info.Caret == IntPtr.Zero) { return null; }
        var corner = new Point { X = info.CaretRect.Left, Y = info.CaretRect.Top };
        ClientToScreen(info.Caret, ref corner);
        return new[] { corner.X, corner.Y, info.CaretRect.Right - info.CaretRect.Left, info.CaretRect.Bottom - info.CaretRect.Top };
    }

    /// <summary>Each key down, then each up in reverse order, in one SendInput: the way a person presses a combination.</summary>
    public static bool Press(params ushort[] keys)
    {
        var inputs = new List<KeyInput>();
        foreach (var key in keys) { inputs.Add(new KeyInput { Type = 1, Key = key, Scan = (ushort)MapVirtualKeyW(key, 0) }); }
        for (var at = keys.Length - 1; at >= 0; at--) { inputs.Add(new KeyInput { Type = 1, Key = keys[at], Scan = (ushort)MapVirtualKeyW(keys[at], 0), Flags = 2 }); }
        return SendInput((uint)inputs.Count, inputs.ToArray(), 40) == inputs.Count;
    }

    public static void Click(int x, int y)
    {
        Physical();
        int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
        var ax = (int)Math.Round((x - vx) * 65535.0 / (vw - 1));
        var ay = (int)Math.Round((y - vy) * 65535.0 / (vh - 1));
        // Absolute over the virtual desk: move, left down, left up.
        var click = new[]
        {
            new MouseInput { Type = 0, X = ax, Y = ay, Flags = 0xC001 },
            new MouseInput { Type = 0, X = ax, Y = ay, Flags = 0xC002 },
            new MouseInput { Type = 0, X = ax, Y = ay, Flags = 0xC004 },
        };
        SendMouse((uint)click.Length, click, 40);
    }

    public static int[] Pointer() { Physical(); Point p; GetCursorPos(out p); return new[] { p.X, p.Y }; }

    public static void MovePointer(int x, int y) { Physical(); SetCursorPos(x, y); }

    public static void Close(IntPtr window) { PostMessageW(window, 0x0010, IntPtr.Zero, IntPtr.Zero); }

    /// <summary>A left click posted to <paramref name="window"/> itself, whatever lies over it on the screen.</summary>
    public static void PostClick(IntPtr window) { PostMessageW(window, 0x0201, new IntPtr(1), IntPtr.Zero); PostMessageW(window, 0x0202, IntPtr.Zero, IntPtr.Zero); }

    /// <summary>The OK command posted to a common dialog: as its default button pressed.</summary>
    public static void PostOk(IntPtr dialog) { PostMessageW(dialog, 0x0111, new IntPtr(1), IntPtr.Zero); }

    /// <summary>The text of a control of another process, which only a message reads; empty while it does not answer.</summary>
    public static string ControlText(IntPtr control)
    {
        var text = new StringBuilder(512);
        IntPtr result;
        return SendText(control, 0x000D, new IntPtr(text.Capacity), text, 0x0002, 1000, out result) == IntPtr.Zero ? "" : text.ToString();
    }
}
'@

# Another program's window to paste into: a PowerShell with an edit box that has the focus, logging each key message,
# character and paste its window and edit box receive to a file, which the check reads while it is written.
$targetScript = @'
param([string] $Log, [int] $X, [int] $Y)
Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class CheckPasteTarget {
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
    [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr LoadCursorW(IntPtr instance, IntPtr id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string name);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr window, SubclassProc procedure, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr w, IntPtr l);
    static readonly WndProc mainProcedure = TopWindow;
    static readonly SubclassProc editProcedure = Edit;
    static IntPtr edit;
    static StreamWriter log;
    public static void Run(string path, int x, int y) {
        log = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        WindowClass c = new WindowClass();
        c.Size = (uint)Marshal.SizeOf(typeof(WindowClass));
        c.Procedure = mainProcedure;
        c.Instance = GetModuleHandleW(null);
        c.Cursor = LoadCursorW(IntPtr.Zero, (IntPtr)32512);
        c.Background = (IntPtr)6;
        c.Name = "ImageStashPublishedCheckTarget";
        RegisterClassExW(ref c);
        IntPtr main = CreateWindowExW(0, c.Name, "ImageStash check target", 0x10CF0000, x, y, 400, 160, IntPtr.Zero, IntPtr.Zero, c.Instance, IntPtr.Zero);
        edit = CreateWindowExW(0, "EDIT", "", 0x50810004, 10, 10, 360, 90, main, IntPtr.Zero, c.Instance, IntPtr.Zero);
        SetWindowSubclass(edit, editProcedure, UIntPtr.Zero, UIntPtr.Zero);
        SetFocus(edit);
        Say("ready " + main.ToInt64());
        Message m;
        while (GetMessageW(out m, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref m); DispatchMessageW(ref m); }
    }
    static IntPtr TopWindow(IntPtr window, uint message, IntPtr w, IntPtr l) {
        Log("window", message, w);
        if (message == 7 && edit != IntPtr.Zero) { SetFocus(edit); }
        if (message == 2) { PostQuitMessage(0); return IntPtr.Zero; }
        return DefWindowProcW(window, message, w, l);
    }
    static IntPtr Edit(IntPtr window, uint message, IntPtr w, IntPtr l, UIntPtr id, UIntPtr data) {
        Log("edit", message, w);
        return DefSubclassProc(window, message, w, l);
    }
    static void Log(string where, uint message, IntPtr w) {
        string name = message == 0x100 ? "key WM_KEYDOWN" : message == 0x101 ? "key WM_KEYUP" : message == 0x104 ? "key WM_SYSKEYDOWN" : message == 0x105 ? "key WM_SYSKEYUP" : message == 0x102 ? "other WM_CHAR" : message == 0x106 ? "other WM_SYSCHAR" : message == 0x302 ? "other WM_PASTE" : null;
        if (name != null) { Say(name + ":0x" + w.ToInt64().ToString("X") + " " + where); }
    }
    static void Say(string line) { lock (typeof(CheckPasteTarget)) { log.WriteLine(line); } }
}
"@
[CheckPasteTarget]::Run($Log, $X, $Y)
'@

# Another program holding Ctrl+Alt+Space (MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_SPACE), logging each press it gets.
$holderScript = @'
param([string] $Log)
Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class CheckShortcutHolder {
    [StructLayout(LayoutKind.Sequential)]
    struct Message { public IntPtr Window; public uint Id; public IntPtr W, L; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern int GetMessageW(out Message m, IntPtr window, uint a, uint b);
    public static void Run(string path) {
        var log = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        log.WriteLine(RegisterHotKey(IntPtr.Zero, 1, 0x4003, 0x20) ? "registered" : "refused " + Marshal.GetLastWin32Error());
        Message m;
        while (GetMessageW(out m, IntPtr.Zero, 0, 0) > 0) { if (m.Id == 0x0312) { log.WriteLine("hotkey"); } }
    }
}
"@
[CheckShortcutHolder]::Run($Log)
'@

function Check([bool] $held, [string] $what) {
    if ($held) { Write-Host "ok   $what" } else { Write-Host "FAIL $what"; $script:failed++ }
}

function WaitFor([scriptblock] $condition, [int] $milliseconds) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.ElapsedMilliseconds -lt $milliseconds) {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 5
    }
    return [bool](& $condition)
}

function Lines([string] $path) {
    if (-not (Test-Path $path)) { return @() }
    $stream = [IO.FileStream]::new($path, 'Open', 'Read', 'ReadWrite')
    try { $reader = [IO.StreamReader]::new($stream); @($reader.ReadToEnd() -split "`r?`n" | Where-Object { $_ -ne '' }) }
    finally { $stream.Dispose() }
}

function StartPowerShell([string] $script, [string[]] $arguments) {
    $command = '& { ' + $script + ' } ' + (($arguments | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }) -join ' ')
    # No console and no start-up SW_HIDE, so the window it shows is shown.
    $start = [Diagnostics.ProcessStartInfo]::new('powershell.exe')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.Arguments = '-NoProfile -NonInteractive -EncodedCommand ' + [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    [Diagnostics.Process]::Start($start)
}

function StartTarget([string] $log) {
    $area = [PasteDesk]::WorkArea([PasteDesk]::Foreground())
    $process = StartPowerShell $targetScript @($log, [string]($area[0] + 100), [string]($area[1] + 40))
    $null = WaitFor { (Lines $log | Where-Object { $_ -like 'ready *' }).Count -gt 0 } 60000
    $ready = @(Lines $log | Where-Object { $_ -like 'ready *' })
    if ($ready.Count -eq 0) { throw 'the target window did not start' }
    @{ Process = $process; Window = [IntPtr][int64]$ready[0].Substring(6); Log = $log }
}

function ClickToFront($target) {
    $bounds = [PasteDesk]::WindowRect($target.Window)
    [PasteDesk]::Click([int](($bounds[0] + $bounds[2]) / 2), $bounds[1] + 12)
    WaitFor { [PasteDesk]::Foreground() -eq $target.Window } 5000
}

# The key messages, and the characters and pastes, the target received after line <after> of its log.
function KeysAfter($target, [int] $after) { @(Lines $target.Log | Select-Object -Skip $after | Where-Object { $_ -like 'key *' } | ForEach-Object { ($_ -split ' ')[1] }) }
function OthersAfter($target, [int] $after) { @(Lines $target.Log | Select-Object -Skip $after | Where-Object { $_ -like 'other *' } | ForEach-Object { ($_ -split ' ')[1] }) }

# What one Ctrl+V and nothing else makes an edit box receive: the four key messages, the character Ctrl+V translates
# to, and the paste the edit box makes of it.
$oneCtrlV = 'WM_KEYDOWN:0x11,WM_KEYDOWN:0x56,WM_KEYUP:0x56,WM_KEYUP:0x11'
$ctrlVMakes = 'WM_CHAR:0x16,WM_PASTE:0x0'

# Enter on the flyout: a real press when the shortcut gave it the foreground with its search box focused, as G1
# requires; else a check fails and Enter is posted to it, so the phases after it still run.
function PressEnterOnTheFlyout {
    $inFront = [PasteDesk]::Foreground()
    $focused = [Windows.Automation.AutomationElement]::FocusedElement
    $active = ([PasteDesk]::ProcessOf($inFront) -eq $Running) -and ($focused.Current.ProcessId -eq $Running) -and
        ($focused.Current.ControlType -eq [Windows.Automation.ControlType]::Edit)
    Check $active "the flyout is in front after the shortcut with its search box focused (G1, G7): in front $inFront, focused a $($focused.Current.ControlType.ProgrammaticName) of process $($focused.Current.ProcessId)"
    if ($active) { $null = [PasteDesk]::Press([uint16[]]@(0x0D)) } else { $null = [StashWindows]::PostEnter($Running) }
    $inFront
}

# The flyout beside the caret (the shortcut summon's placement): left edge 28 units left of the caret's, bottom 8 units
# above its top, or top 8 units below its bottom when the working area above has no room; when neither has, 8 units
# right of the caret, else left of it, centred on its line; then moved onto the area.
function BesideCaret([int[]] $caret, [int[]] $flyout, [int[]] $area, [double] $scaling) {
    $width = $flyout[2] - $flyout[0]; $height = $flyout[3] - $flyout[1]
    $gap = [math]::Round(8 * $scaling)
    $x = $caret[0] - [math]::Round(28 * $scaling)
    $above = $caret[1] - $gap - $height
    $below = $caret[1] + $caret[3] + $gap
    $y = if ($above -ge $area[1]) { $above } else { $below }
    if (($above -lt $area[1]) -and ($below + $height -gt $area[3])) {
        $side = $caret[1] + [math]::Floor($caret[3] / 2) - [math]::Floor($height / 2)
        if ($caret[0] + $caret[2] + $gap + $width -le $area[2]) { $x = $caret[0] + $caret[2] + $gap; $y = $side }
        elseif ($caret[0] - $gap - $width -ge $area[0]) { $x = $caret[0] - $gap - $width; $y = $side }
    }
    $x = [math]::Min([math]::Max($x, $area[0]), $area[2] - $width)
    $y = [math]::Min([math]::Max($y, $area[1]), $area[3] - $height)
    @($x, $y)
}

function Footer([IntPtr] $flyout) {
    $root = [Windows.Automation.AutomationElement]::FromHandle($flyout)
    $named = $root.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) |
        ForEach-Object { $_.Current.Name } | Where-Object { $_ -like "Enter $dot *" -or $_ -like "Ctrl+C $dot *" }
    @($named) -join ' | '
}

function Shows([IntPtr] $flyout, [string] $words) {
    $root = [Windows.Automation.AutomationElement]::FromHandle($flyout)
    $named = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $words)
    $null -ne $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $named)
}

$pointerBefore = [PasteDesk]::Pointer()
$holder = $null
$target = $null
$usesQuery = 'SELECT COUNT(*) FROM uses'
$usedQuery = 'SELECT i.finished_file FROM uses u JOIN items i ON i.id = u.item_id ORDER BY u.used_at DESC, u.rowid DESC LIMIT 1'
$hotKey = [uint16[]]@(0x11, 0x12, 0x20)
try {
    $target = StartTarget (Join-Path $Work 'target-1.log')
    Check (ClickToFront $target) "the check's target window, another PowerShell with an edit box, came to the front by a click on its title bar"

    if ($Store) {
        # G8 from the tray, made certain: a posted click moves no foreground, so the window that had focus just before is
        # the check's target, and the posted Enter is a paste into it. Enter waits for the listing, so it is posted again
        # only after a few seconds without a use.
        $stash = Split-Path $Store -Parent
        $took = [StashWindows]::ClickTrayIcon($Running, 5000)
        Check (($took -ge 0) -and ($took -le 1000)) "a left click on the tray icon showed the window within one second over 5,000 pictures (G1, E4): $took ms, '$([StashWindows]::Showing($Running))'"
        $mark = (Lines $target.Log).Count
        $copied = $false
        for ($attempt = 1; ($attempt -le 3) -and -not $copied; $attempt++) {
            $null = [StashWindows]::PostEnter($Running)
            $copied = WaitFor { [int][CopyReadBack]::Query($Store, $usesQuery) -gt 0 } 3000
        }
        $null = WaitFor { (KeysAfter $target $mark).Count -ge 4 } 2000
        Start-Sleep -Milliseconds 500
        $uses = [int][CopyReadBack]::Query($Store, $usesQuery)
        $newest = Join-Path $stash ([CopyReadBack]::Query($Store, 'SELECT finished_file FROM items ORDER BY added_at DESC, id DESC LIMIT 1'))
        $clipboard = [CopyReadBack]::Clipboard()
        $keys = KeysAfter $target $mark
        $others = OthersAfter $target $mark
        Check ($uses -eq 1) "a posted Enter from the tray, with the check's window in front before the click, recorded exactly one use over 5,000 pictures (G10): $uses after $($attempt - 1) post(s)"
        Check ($clipboard -eq "files=$newest png=True dibv5=True effect=1") "the clipboard holds that stash's first item as its file, with ""PNG"" and CF_DIBV5 for the still and Preferred DropEffect Copy (G8, G29): $clipboard"
        Check ((($keys -join ',') -eq $oneCtrlV) -and (($others -join ',') -eq $ctrlVMakes)) "that Enter pasted into the window that had focus before the tray's summon, one Ctrl+V and nothing else (G8, N3): $($keys -join ','); $($others -join ',')"
        Check (WaitFor { [StashWindows]::Showing($Running) -eq '' } 2000) "the flyout hid after that paste (G2): '$([StashWindows]::Showing($Running))'"
    }
    else {
        Write-Host "skipped: the stash is the user's, so the tray's posted Enter, which records a use, does not run"
    }

    # G1, F5: the real shortcut over the target, the pointer far from its caret, shows the flyout beside the caret.
    Check (ClickToFront $target) 'the target came to the front again'
    $caret = [PasteDesk]::Caret($target.Window)
    Check ($null -ne $caret) "the target's edit box shows a caret: $($caret -join ',')"
    $area = [PasteDesk]::WorkArea($target.Window)
    [PasteDesk]::MovePointer($area[2] - 40, $area[3] - 40)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    Check ([PasteDesk]::Press($hotKey)) 'Ctrl+Alt+Space pressed with SendInput'
    $shown = WaitFor { [PasteDesk]::Flyout($Running, $copiedTitle) -ne [IntPtr]::Zero } 3000
    $summoned = $clock.ElapsedMilliseconds
    $flyout = [PasteDesk]::Flyout($Running, $copiedTitle)
    Check ($shown -and $summoned -le 1000) "the open shortcut showed the flyout within one second of the press$(if ($Store) { ' over 5,000 pictures' }) (G1, E4): $summoned ms"
    if ($shown -and $null -ne $caret) {
        $rect = [PasteDesk]::WindowRect($flyout)
        $expected = BesideCaret $caret $rect $area ([PasteDesk]::Scaling($flyout))
        $inside = ($rect[0] -ge $area[0]) -and ($rect[1] -ge $area[1]) -and ($rect[2] -le $area[2]) -and ($rect[3] -le $area[3])
        Check (([math]::Abs($rect[0] - $expected[0]) -le 2) -and ([math]::Abs($rect[1] - $expected[1]) -le 2) -and $inside) "the flyout opened beside the caret, not at the pointer, wholly inside the working area (G1): flyout ($($rect -join ',')) for the caret ($($caret -join ',')), expected its corner at ($($expected -join ',')), area ($($area -join ','))"
        $footer = Footer $flyout
        if (-not $Store -and $footer -eq '' -and (Shows $flyout 'Your stash is empty')) {
            # The paste hints show only while an item is selected, and an empty stash has none to select (G1, G2).
            Write-Host "skipped: the stash is the user's and empty, so no item is selected and the footer shows no paste hints"
        }
        else {
            Check ($footer -eq "Enter $dot Paste into Windows PowerShell | Ctrl+C $dot Copy") "the footer names the app it came from (G8, In use): '$footer'"
        }
    }

    if ($Store) {
        # G8, G10, N3: a real Enter hides the flyout, brings the target forward and sends it one Ctrl+V; what it hands
        # over is Recent's first item, the one the tray's paste used (G4).
        $usesBefore = [int][CopyReadBack]::Query($Store, $usesQuery)
        $used = Join-Path $stash ([CopyReadBack]::Query($Store, $usedQuery))
        Start-Sleep -Milliseconds 300
        $mark = (Lines $target.Log).Count
        $null = PressEnterOnTheFlyout
        $hid = WaitFor { [PasteDesk]::Flyout($Running, $copiedTitle) -eq [IntPtr]::Zero } 2000
        $null = WaitFor { (KeysAfter $target $mark).Count -ge 4 } 2000
        Start-Sleep -Milliseconds 300
        $keys = KeysAfter $target $mark
        $others = OthersAfter $target $mark
        $uses = [int][CopyReadBack]::Query($Store, $usesQuery)
        $usedNow = Join-Path $stash ([CopyReadBack]::Query($Store, $usedQuery))
        $clipboard = [CopyReadBack]::Clipboard()
        Check $hid 'Enter hid the flyout (G2: after Enter''s paste)'
        Check ([PasteDesk]::Foreground() -eq $target.Window) 'the target is in front again'
        Check ((($keys -join ',') -eq $oneCtrlV) -and (($others -join ',') -eq $ctrlVMakes)) "the target received one Ctrl down, V down, V up, Ctrl up after the shortcut's own keys, the paste they make, and nothing else (G8, N3, R6): $($keys -join ','); $($others -join ',')"
        Check (($uses -eq $usesBefore + 1) -and ($usedNow -eq $used)) "that paste recorded one use, of the item Recent showed first (G10, G4): $usesBefore then $uses, $usedNow"
        Check ($clipboard -eq "files=$used png=True dibv5=True effect=1") "the clipboard holds the item Recent showed first, the one used before, as its file with ""PNG"" and CF_DIBV5 (G8, G4): $clipboard"

        # In use, G5, G7: two letters typed into the search box, then Enter, hand over the first match, newest-added
        # first, rather than Recent's first item: "sh" matches only the "shrug" pictures, of which "shrug 4998" is newest.
        Check (ClickToFront $target) 'the target came to the front for two typed letters'
        $null = [PasteDesk]::Press($hotKey)
        Check (WaitFor { [PasteDesk]::Flyout($Running, $copiedTitle) -ne [IntPtr]::Zero } 3000) 'the shortcut showed the flyout over the target again'
        $matched = Join-Path $stash 'shrug 4998.png'
        $usesBefore = [int][CopyReadBack]::Query($Store, $usesQuery)
        Start-Sleep -Milliseconds 300
        $mark = (Lines $target.Log).Count
        $null = [PasteDesk]::Press([uint16[]]@(0x53))
        $null = [PasteDesk]::Press([uint16[]]@(0x48))
        Start-Sleep -Milliseconds 300
        $null = PressEnterOnTheFlyout
        $hid = WaitFor { [PasteDesk]::Flyout($Running, $copiedTitle) -eq [IntPtr]::Zero } 2000
        $null = WaitFor { (KeysAfter $target $mark).Count -ge 4 } 2000
        Start-Sleep -Milliseconds 300
        $keys = KeysAfter $target $mark
        $others = OthersAfter $target $mark
        $uses = [int][CopyReadBack]::Query($Store, $usesQuery)
        $usedNow = Join-Path $stash ([CopyReadBack]::Query($Store, $usedQuery))
        $clipboard = [CopyReadBack]::Clipboard()
        Check ($hid -and (($keys -join ',') -eq $oneCtrlV) -and (($others -join ',') -eq $ctrlVMakes)) "two letters typed into the search box, then Enter: the flyout hid and the target received one Ctrl+V, the paste it makes, and nothing else, the letters included (In use, G7, G8, N3): $($keys -join ','); $($others -join ',')"
        Check (($uses -eq $usesBefore + 1) -and ($usedNow -eq $matched) -and ($clipboard -eq "files=$matched png=True dibv5=True effect=1")) "that paste handed over the first match of ""sh"", newest-added first, and recorded its use (G5, G8, G10): $usesBefore then $uses, $usedNow, $clipboard"
        $used = $matched

        # G8, G2, G16, E6: the target closed between summon and Enter: the copy, the flyout hidden as a paste hides it, and the notification, which takes no focus.
        Check (ClickToFront $target) 'the target came to the front for the fallback'
        $null = [PasteDesk]::Press($hotKey)
        Check (WaitFor { [PasteDesk]::Flyout($Running, $copiedTitle) -ne [IntPtr]::Zero } 3000) 'the shortcut showed the flyout over the target'
        [PasteDesk]::Close($target.Window)
        Check (WaitFor { -not [PasteDesk]::Exists($target.Window) } 5000) 'the target closed while the flyout showed'
        $usesBefore = [int][CopyReadBack]::Query($Store, $usesQuery)
        $null = PressEnterOnTheFlyout
        $clock.Restart()
        $noticed = WaitFor { [PasteDesk]::Titled($Running, $copiedTitle) -ne [IntPtr]::Zero } 2000
        $noticedAfter = $clock.ElapsedMilliseconds
        $notification = [PasteDesk]::Titled($Running, $copiedTitle)
        $stayed = $true
        while ($clock.ElapsedMilliseconds -lt $noticedAfter + 600) {
            if ([PasteDesk]::Foreground() -eq $notification) { $stayed = $false }
            Start-Sleep -Milliseconds 2
        }

        $uses = [int][CopyReadBack]::Query($Store, $usesQuery)
        $clipboard = [CopyReadBack]::Clipboard()
        Check $noticed "the notification ""$copiedTitle"" showed within two seconds of Enter (G8, G16): $noticedAfter ms"
        Check ([PasteDesk]::Flyout($Running, $copiedTitle) -eq [IntPtr]::Zero) 'the flyout hid as for a paste, so what was behind it is in front for its Ctrl+V (G2, G8)'
        Check $stayed "the notification took no focus, sampled for 600 ms (G16)"
        Check (($uses -eq $usesBefore + 1) -and ($clipboard -eq "files=$used png=True dibv5=True effect=1")) "the clipboard was written and the copy recorded one use (G8, G10): $usesBefore then $uses, $clipboard"

        # F5, G2: with the flyout showing, the shortcut hides it.
        $null = [PasteDesk]::Press($hotKey)
        Check (WaitFor { [PasteDesk]::Flyout($Running, $copiedTitle) -ne [IntPtr]::Zero } 3000) 'the shortcut showed the flyout again'
        $null = [PasteDesk]::Press($hotKey)
        Check (WaitFor { [PasteDesk]::Flyout($Running, $copiedTitle) -eq [IntPtr]::Zero } 2000) 'with the flyout showing, the shortcut hid it (F5, G2)'
    }
    else {
        $null = [PasteDesk]::Press($hotKey)
        Check (WaitFor { [PasteDesk]::Flyout($Running, $copiedTitle) -eq [IntPtr]::Zero } 2000) 'with the flyout showing, the shortcut hid it (F5, G2)'
        Write-Host "skipped: the stash is the user's, so Enter's paste and the closed target's copy, which record uses, do not run"
    }

    # G33, E3: another program holding Ctrl+Alt+Space when ImageStash starts: the press is the other program's, and
    # the tray still reaches the window.
    Stop-Process -Id $Running -Force
    (Get-Process -Id $Running -ErrorAction SilentlyContinue) | ForEach-Object { $_.WaitForExit() }
    $holderLog = Join-Path $Work 'holder.log'
    $holder = StartPowerShell $holderScript @($holderLog)
    $null = WaitFor { (Lines $holderLog).Count -gt 0 } 60000
    Check ((Lines $holderLog | Select-Object -First 1) -eq 'registered') "another program registered Ctrl+Alt+Space first: $(Lines $holderLog | Select-Object -First 1)"
    $restarted = Start-Process -FilePath $Exe -PassThru
    Start-Sleep -Seconds 3
    $showing = [StashWindows]::Showing($restarted.Id)
    Check (-not $restarted.HasExited) "ImageStash started after it and keeps running, showing '$showing'"
    $pressesBefore = @(Lines $holderLog | Where-Object { $_ -eq 'hotkey' }).Count
    $null = [PasteDesk]::Press($hotKey)
    $theirs = WaitFor { @(Lines $holderLog | Where-Object { $_ -eq 'hotkey' }).Count -gt $pressesBefore } 1500
    Start-Sleep -Milliseconds 500
    Check ($theirs -and ([StashWindows]::Showing($restarted.Id) -eq $showing)) "the press went to the program holding the shortcut and ImageStash showed nothing more (G33, E3): '$([StashWindows]::Showing($restarted.Id))'"
    $took = [StashWindows]::ClickTrayIcon($restarted.Id, 5000)
    Check ($took -ge 0) "the tray icon still shows the window (G33: the tray menu and the window still reach every action): $took ms"
}
finally {
    [PasteDesk]::MovePointer($pointerBefore[0], $pointerBefore[1])
    if ($holder -and -not $holder.HasExited) { $holder.Kill(); $null = $holder.WaitForExit(5000) }
    if ($target -and -not $target.Process.HasExited) { $target.Process.Kill(); $null = $target.Process.WaitForExit(5000) }
}
exit $failed
