<#
.SYNOPSIS
Takes the README's pictures of the current build: publishes it into %TEMP%, builds the demo stash with the focused
check DemoStashTests, runs the published copy over that stash on this desktop and drives four scenes with real input,
each confirmed through UI Automation and taken from the screen once it has stopped changing: the paste moment over a
chat window of the script's own, the flyout beside its caret with the footer naming it (paste.png); the pinned window
over the grid and its tag chips (grid.png); the preview of the edited item (preview.png); and the editor on it with its
layers live and the Text tool's row (editor.png). The pictures are staged under %TEMP% and replace those in
docs\screenshots only once every scene was taken and every one of those files could be opened for writing, so a failed
run leaves the committed pictures as they were. Exits 0 once every picture is written; 1 with a line naming the scene
or the picture that failed; 1 after a "refused:" line, having done nothing.

The capture notification is not among the scenes: the product excludes every notification from screen capture, so a
grab of the screen shows what is beneath it, and PrintWindow pictures it black.

Run from PowerShell (never Git Bash) on the interactive desktop, with ImageStash not running and
%LOCALAPPDATA%\ImageStash absent: the copy runs over that real folder, since nothing redirects it, so the script refuses
while it exists and removes it afterwards. It moves the mouse pointer and puts it back, sends real keystrokes and
clicks, shows a chat window of its own near the primary display's bottom-left corner, lets the copy write the Run value
ImageStash and puts back what was there, and stops every process it started, whether it succeeded or not.

.EXAMPLE
powershell.exe -NoProfile -File tools/make-readme-screenshots.ps1
#>
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$pictures = Join-Path $repository 'docs\screenshots'
$appData = Join-Path $env:LOCALAPPDATA 'ImageStash'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$scenes = @('paste', 'grid', 'preview', 'editor')
$dot = [char]0xB7

$found = @(Get-Process -Name ImageStash.Windows -ErrorAction SilentlyContinue)
if ($found.Count -gt 0) {
    Write-Host "refused: ImageStash is running (process $($found[0].Id), $($found[0].Path)); quit it from its tray menu, then run this again."
    exit 1
}
if (Test-Path -LiteralPath $appData) {
    Write-Host "refused: $appData exists, and the published copy would run over it; move it aside, then run this again."
    exit 1
}

. (Join-Path $PSScriptRoot 'stash-windows.ps1')
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// The desk as the script drives and pictures it, in physical pixels (per-monitor aware on the calling thread). The
// INPUT layouts are x64's: the union at offset 8, 40 bytes in all.
public static class ReadmeDesk
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
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect rect, int size);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, KeyInput[] inputs, int size);
    [DllImport("user32.dll", EntryPoint = "SendInput")] private static extern uint SendMouse(uint count, MouseInput[] inputs, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKeyW(uint code, uint type);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);

    private static void Physical() { SetThreadDpiAwarenessContext(new IntPtr(-4)); }

    /// <summary>Physical pixels to a device-independent unit on the primary display.</summary>
    public static double Scale() { Physical(); return GetDpiForSystem() / 96.0; }

    /// <summary>The primary display's working area: left, top, right, bottom.</summary>
    public static int[] WorkArea()
    {
        Physical();
        var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
        GetMonitorInfoW(MonitorFromPoint(new Point(), 1), ref info);
        return new[] { info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom };
    }

    public static IntPtr Foreground() { return GetForegroundWindow(); }

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

    /// <summary>Left, top, right, bottom, Windows' invisible resize borders included.</summary>
    public static int[] WindowRect(IntPtr window) { Physical(); Rect r; GetWindowRect(window, out r); return new[] { r.Left, r.Top, r.Right, r.Bottom }; }

    /// <summary>Left, top, right, bottom of what the window shows, without Windows' invisible resize borders (DWMWA_EXTENDED_FRAME_BOUNDS).</summary>
    public static int[] FrameRect(IntPtr window)
    {
        Physical();
        Rect r;
        if (DwmGetWindowAttribute(window, 9, out r, Marshal.SizeOf(typeof(Rect))) != 0) { GetWindowRect(window, out r); }
        return new[] { r.Left, r.Top, r.Right, r.Bottom };
    }

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

    /// <summary>Move, left down, left up, absolute over the virtual desk.</summary>
    public static void Click(int x, int y)
    {
        Physical();
        int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
        var ax = (int)Math.Round((x - vx) * 65535.0 / (vw - 1));
        var ay = (int)Math.Round((y - vy) * 65535.0 / (vh - 1));
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

    // A pixel of a picture rather than of the window around it: strongly coloured, or dark.
    private static long Pictured(int[] pixels)
    {
        long count = 0;
        foreach (var pixel in pixels)
        {
            int r = (pixel >> 16) & 0xFF, g = (pixel >> 8) & 0xFF, b = pixel & 0xFF;
            int high = Math.Max(r, Math.Max(g, b)), low = Math.Min(r, Math.Min(g, b));
            if (high - low > 60 || high < 120) { count++; }
        }
        return count;
    }

    private static int[] Pixels(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new int[bitmap.Width * bitmap.Height];
            for (var y = 0; y < bitmap.Height; y++) { Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * bitmap.Width, bitmap.Width); }
            return pixels;
        }
        finally { bitmap.UnlockBits(data); }
    }

    /// <summary>
    /// Grabs the rectangle from the screen every quarter second until two grabs in a row differ in no more than a
    /// blinking caret's pixels and at least <paramref name="pictured"/> percent of the later is picture rather than the
    /// window's white and grey (a pixel strongly coloured, or dark), and writes it as a PNG at <paramref name="path"/>:
    /// a window still drawing, or waiting on a picture it has not decoded yet, is never taken. Null once written;
    /// otherwise why not.
    /// </summary>
    public static string TakeSettled(int left, int top, int right, int bottom, string path, int milliseconds, int pictured)
    {
        Physical();
        int width = right - left, height = bottom - top;
        if (width <= 0 || height <= 0) { return "the rectangle is empty"; }
        var tolerance = Math.Max(200, width * height / 1000);
        var clock = Stopwatch.StartNew();
        int[] previous = null;
        var share = 0;
        while (true)
        {
            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(bitmap)) { graphics.CopyFromScreen(left, top, 0, 0, new Size(width, height)); }
                var pixels = Pixels(bitmap);
                if (previous != null)
                {
                    var differing = 0;
                    for (var at = 0; at < pixels.Length; at++) { if (pixels[at] != previous[at]) { differing++; } }
                    share = (int)(100L * Pictured(pixels) / pixels.Length);
                    if (differing <= tolerance && share >= pictured)
                    {
                        bitmap.Save(path, ImageFormat.Png);
                        return null;
                    }
                }
                previous = pixels;
            }
            if (clock.ElapsedMilliseconds > milliseconds)
            {
                return share < pictured
                    ? "after " + milliseconds + " ms only " + share + " % of it shows pictures, not " + pictured + " %: they never drew"
                    : "it kept changing for " + milliseconds + " ms";
            }
            Thread.Sleep(250);
        }
    }
}
'@

# A chat window of the script's own for the paste picture, built as Chat.exe so the footer names it "Chat": a short
# conversation and a message box holding the caret. Its arguments place it in physical pixels at a scale.
$chatSource = @'
using System;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: AssemblyTitle("Chat")]
[assembly: AssemblyProduct("Chat")]

public static class ChatWindow
{
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

    [STAThread]
    public static void Main(string[] args)
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        int x = int.Parse(args[0]), y = int.Parse(args[1]), width = int.Parse(args[2]), height = int.Parse(args[3]);
        var scale = double.Parse(args[4], CultureInfo.InvariantCulture);
        Func<double, int> units = value => (int)Math.Round(value * scale);
        var form = new Form { Text = "Chat", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(x, y, width, height), BackColor = Color.White, Font = new Font("Segoe UI", 11f) };
        var inner = form.ClientSize;
        var box = new TextBox { Text = "ha, perfect ", BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 12f) };
        box.SetBounds(units(16), inner.Height - units(16) - box.PreferredHeight, inner.Width - units(32), box.PreferredHeight);
        form.Controls.Add(box);
        Say(form, "Sam: did the release go out?", false, units(16), units);
        Say(form, "just now", true, units(70), units);
        Say(form, "how did the demo go?", true, units(124), units);
        Say(form, "Sam: screenshots or it didn't happen", false, units(178), units);
        form.Shown += delegate { box.Focus(); box.SelectionStart = box.Text.Length; box.SelectionLength = 0; };
        Application.Run(form);
    }

    private static void Say(Form form, string words, bool mine, int top, Func<double, int> units)
    {
        var bubble = new Label { Text = words, AutoSize = true, Padding = new Padding(units(10), units(6), units(10), units(6)), BackColor = mine ? Color.FromArgb(0xD6, 0xE8, 0xFF) : Color.FromArgb(0xEE, 0xEE, 0xEE) };
        form.Controls.Add(bubble);
        bubble.Location = new Point(mine ? form.ClientSize.Width - units(16) - bubble.PreferredWidth : units(16), top);
    }
}
'@

function WaitFor([scriptblock] $condition, [int] $milliseconds) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.ElapsedMilliseconds -lt $milliseconds) {
        if (Holds $condition) { return $true }
        Start-Sleep -Milliseconds 50
    }
    return (Holds $condition)
}

# A window's elements can go while UI Automation reads them, which throws: the condition does not hold yet.
function Holds([scriptblock] $condition) { try { [bool](& $condition) } catch { $false } }

# A scene not as it must be stops the run, naming what was missing.
function Need([bool] $held, [string] $what) { if (-not $held) { throw $what } }

$button = [Windows.Automation.ControlType]::Button
$text = [Windows.Automation.ControlType]::Text
$edit = [Windows.Automation.ControlType]::Edit

# The copy's elements under <root> of <type>, named <name> when one is given.
function Elements($root, [string] $name, $type) {
    $condition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $copy.Id),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, $type))
    @($root.FindAll([Windows.Automation.TreeScope]::Descendants, $condition) | Where-Object { (-not $name) -or ($_.Current.Name -eq $name) })
}

function One($found, [string] $what) {
    $all = @($found | Where-Object { $null -ne $_ })
    Need ($all.Count -eq 1) "$what is there once: $($all.Count) found"
    $all[0]
}

# A real click at the middle of <element>: UI Automation's rectangles are in physical pixels.
function ClickOn($element) {
    $box = $element.Current.BoundingRectangle
    [ReadmeDesk]::Click([int]($box.X + $box.Width / 2), [int]($box.Y + $box.Height / 2))
}

function StashWindow { [ReadmeDesk]::Titled($copy.Id, 'ImageStash') }

function Root { [Windows.Automation.AutomationElement]::FromHandle((StashWindow)) }

# The words the window counts its listing with: "1 item", "13 items".
function Counted($root) { @(Elements $root '' $text | ForEach-Object { $_.Current.Name } | Where-Object { $_ -match '^\d+ items?\b' }) -join ' | ' }

function Footer($root) { @(Elements $root '' $text | ForEach-Object { $_.Current.Name } | Where-Object { $_ -like "Enter $dot *" -or $_ -like "Ctrl+C $dot *" }) -join ' | ' }

function SearchText($root) { (One (Elements $root 'Search your stash' $edit) 'the search box').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value }

# Letters typed with their keys, as a person types them in lower case.
function TypeWord([string] $word) { foreach ($letter in $word.ToUpperInvariant().ToCharArray()) { $null = [ReadmeDesk]::Press([uint16][char]$letter) } }

# The pointer moved off every window the scenes show, so no hover shows in a picture.
function PointerAside { [ReadmeDesk]::MovePointer($area[2] - 4, [int](($area[1] + $area[3]) / 2)) }

# <pictured>: the percent of the scene that must be picture once its pictures have drawn, about half what this desktop
# showed (the paste scene's one tile 4.7 %, the others 23 to 25 %), and well above the scene without them.
function Take([string] $scene, [int[]] $rect, [int] $pictured) {
    PointerAside
    $why = [ReadmeDesk]::TakeSettled($rect[0], $rect[1], $rect[2], $rect[3], (Join-Path $staging "$scene.png"), 20000, $pictured)
    Need ($null -eq $why) "its picture could not be taken: $why"
    Write-Host "took the $scene scene ($($rect[2] - $rect[0]) x $($rect[3] - $rect[1]))"
}

$untidy = New-Object System.Collections.Generic.List[string]

function Tidy([string] $what, [scriptblock] $step) {
    try { & $step } catch { $untidy.Add("${what}: $($_.Exception.Message)") }
}

# A folder an ended process may still hold a moment, removed once it lets go; the last attempt's error stands.
function RemoveFolder([string] $path) {
    for ($attempt = 1; Test-Path -LiteralPath $path; $attempt++) {
        try { Remove-Item -LiteralPath $path -Recurse -Force }
        catch { if ($attempt -ge 20) { throw }; Start-Sleep -Milliseconds 500 }
    }
}

# The Run value as stored: its registry type and its data with environment variables left unexpanded.
function StoredRunValue {
    $key = Get-Item -Path $runKey
    if ($key.GetValueNames() -notcontains 'ImageStash') { return $null }
    @{ Kind = $key.GetValueKind('ImageStash'); Data = $key.GetValue('ImageStash', $null, 'DoNotExpandEnvironmentNames') }
}

$work = Join-Path $env:TEMP ('ImageStash-readme-screenshots-' + [guid]::NewGuid().ToString('N'))
$publish = Join-Path $work 'publish'
$exe = Join-Path $publish 'ImageStash.Windows.exe'
$stash = Join-Path $work 'stash'
$staging = Join-Path $work 'staging'
$runBefore = StoredRunValue
$pointerBefore = [ReadmeDesk]::Pointer()
$createdAppData = $false
$copy = $null
$chat = $null
$scene = 'setup'
$failure = $null
try {
    New-Item -ItemType Directory -Path $staging | Out-Null
    & (Join-Path $PSScriptRoot 'publish.ps1') -Output $publish | Out-Host
    $env:IMAGESTASH_README_STASH = $stash
    try { & dotnet test (Join-Path $repository 'tests\ImageStash.Tests') --filter 'FullyQualifiedName~DemoStash' | Out-Host }
    finally { Remove-Item -Path Env:\IMAGESTASH_README_STASH }
    Need (($LASTEXITCODE -eq 0) -and (Test-Path (Join-Path $stash 'stash.db'))) "the demo-stash check did not leave its stash at $stash (exit $LASTEXITCODE)"

    $scale = [ReadmeDesk]::Scale()
    $area = [ReadmeDesk]::WorkArea()
    # Owned, and so removed at the end, only once this run made it: a folder another program made meanwhile stays.
    New-Item -ItemType Directory -Path $appData | Out-Null
    $createdAppData = $true
    # Light, at fixed sizes, over the demo stash; the pinned window near the working area's top-left corner.
    $settings = [ordered]@{
        version = 1; stashFolder = $stash; theme = 'light'; thumbnailSize = 'medium'
        flyoutSize = [ordered]@{ width = 460; height = 520 }; pinnedSize = [ordered]@{ width = 900; height = 600 }
        pinnedPosition = [ordered]@{ x = $area[0] + [int](40 * $scale); y = $area[1] + [int](32 * $scale) }
    }
    [IO.File]::WriteAllText((Join-Path $appData 'settings.json'), ($settings | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))

    $chatExe = Join-Path $work 'chat\Chat.exe'
    New-Item -ItemType Directory -Path (Split-Path $chatExe -Parent) | Out-Null
    Add-Type -OutputAssembly $chatExe -OutputType WindowsApplication -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition $chatSource
    # 820 by 680 units at the working area's bottom-left, so the flyout opens above the message box, over the chat.
    $chatPlace = @([int]($area[0] + 24 * $scale), [int]([math]::Max($area[1], $area[3] - 696 * $scale)), [int](820 * $scale), [int](680 * $scale))
    $chat = Start-Process -FilePath $chatExe -ArgumentList (@($chatPlace) + @($scale.ToString([Globalization.CultureInfo]::InvariantCulture))) -PassThru
    Need (WaitFor { $chat.Refresh(); $chat.MainWindowHandle -ne [IntPtr]::Zero } 30000) 'the chat window did not show'
    $chatWindow = $chat.MainWindowHandle

    $copy = Start-Process -FilePath $exe -PassThru
    Write-Host "started the published copy over the demo stash: $exe"
    # A tray click shows the window once the stash is open; Esc hides it again, the search being empty.
    $shownAfter = -1
    $null = WaitFor { $script:shownAfter = [StashWindows]::ClickTrayIcon($copy.Id, 2000); $script:shownAfter -ge 0 } 60000
    Need ($shownAfter -ge 0) 'the copy never showed its window from the tray'
    Need (WaitFor { (Counted (Root)) -ne '' } 20000) 'the copy never listed the demo stash'
    $null = [ReadmeDesk]::Press([uint16[]]@(0x1B))
    Need (WaitFor { [StashWindows]::Showing($copy.Id) -eq '' } 5000) 'Esc did not hide the window shown from the tray'

    $scene = 'paste'
    $chatFrame = [ReadmeDesk]::FrameRect($chatWindow)
    $summoned = $false
    for ($attempt = 1; ($attempt -le 5) -and -not $summoned; $attempt++) {
        [ReadmeDesk]::Click([int](($chatFrame[0] + $chatFrame[2]) / 2), $chatFrame[1] + [int](12 * $scale))
        Need (WaitFor { [ReadmeDesk]::Foreground() -eq $chatWindow } 5000) 'the chat window did not come to the front for a click on its title bar'
        # Read while the chat window has the focus: its caret goes once the flyout takes it.
        $caret = [ReadmeDesk]::Caret($chatWindow)
        Need ($null -ne $caret) 'the chat window shows no caret'
        $null = [ReadmeDesk]::Press([uint16[]]@(0x11, 0x12, 0x20))
        $summoned = WaitFor { [StashWindows]::Showing($copy.Id) -eq 'flyout' } 3000
    }
    Need $summoned 'the real Ctrl+Alt+Space over the chat window did not show the flyout'
    TypeWord 'sh'
    $root = Root
    Need (WaitFor { ((SearchText $root) -eq 'sh') -and ((Counted $root) -like '1 item*') } 5000) "two letters typed into the search box did not find one item: '$(SearchText $root)', $(Counted $root)"
    Need (WaitFor { (Footer $root) -eq "Enter $dot Paste into Chat | Ctrl+C $dot Copy" } 5000) "the footer does not name the chat window: '$(Footer $root)'"
    $flyout = [ReadmeDesk]::FrameRect((StashWindow))
    $beside = (($flyout[3] -le $caret[1]) -and ($caret[1] - $flyout[3] -le 24 * $scale)) -or (($flyout[1] -ge $caret[1] + $caret[3]) -and ($flyout[1] - $caret[1] - $caret[3] -le 24 * $scale))
    Need $beside "the flyout ($($flyout -join ', ')) is not beside the chat window's caret ($($caret -join ', '))"
    $chatFrame = [ReadmeDesk]::FrameRect($chatWindow)
    Take 'paste' @([math]::Min($chatFrame[0], $flyout[0]), [math]::Min($chatFrame[1], $flyout[1]), [math]::Max($chatFrame[2], $flyout[2]), [math]::Max($chatFrame[3], $flyout[3])) 3

    $scene = 'grid'
    $null = [ReadmeDesk]::Press([uint16[]]@(0x1B))
    Need (WaitFor { (SearchText $root) -eq '' } 3000) 'Esc did not clear the search'
    ClickOn (One (Elements $root 'All' $button) 'the All chip')
    Need (WaitFor { (Counted $root) -like '13 items*' } 5000) "All does not count the demo stash's 13 items: $(Counted $root)"
    ClickOn (One (Elements $root 'Keep open' $button) 'the pin')
    $place = $settings.pinnedPosition
    Need (WaitFor { $rect = [ReadmeDesk]::WindowRect((StashWindow)); ([StashWindows]::Showing($copy.Id) -eq 'pinned') -and ([math]::Abs($rect[0] - $place.x) -le 2) -and ([math]::Abs($rect[1] - $place.y) -le 2) } 5000) "the pin did not make the window pinned at its place: '$([StashWindows]::Showing($copy.Id))', $(([ReadmeDesk]::WindowRect((StashWindow))) -join ', ')"
    $root = Root
    $chips = @(Elements $root '' $button | Where-Object { $_.Current.Name -like '#*' })
    Need ($chips.Count -ge 3) "the chip row shows $($chips.Count) tag chips, not three or more"
    Need ((Counted $root) -like '13 items*') "the pinned window does not list the 13 items: $(Counted $root)"
    Take 'grid' ([ReadmeDesk]::FrameRect((StashWindow))) 12

    $scene = 'preview'
    ClickOn (One (Elements $root 'Search your stash' $edit) 'the search box')
    $null = [ReadmeDesk]::Press([uint16[]]@(0x11, 0x41))
    $null = [ReadmeDesk]::Press([uint16[]]@(0x08))
    TypeWord 'monday'
    Need (WaitFor { ((SearchText $root) -eq 'monday') -and ((Counted $root) -like '1 item*') } 5000) "'monday' typed into the search box did not find one item: $(Counted $root)"
    $null = [ReadmeDesk]::Press([uint16[]]@(0x09))
    $null = [ReadmeDesk]::Press([uint16[]]@(0x20))
    Need (WaitFor { ((Elements $root 'Edit' $button).Count -eq 1) -and ((Elements $root 'monday' $text).Count -ge 1) -and (@(Elements $root '' $text | Where-Object { $_.Current.Name -like 'Edited here*' }).Count -ge 1) } 5000) "Space did not open the preview of the edited item with its Edit button and ""Edited here"""
    Take 'preview' ([ReadmeDesk]::FrameRect((StashWindow))) 12

    $scene = 'editor'
    ClickOn (One (Elements $root 'Edit' $button) "the preview's Edit")
    $tool = $null
    Need (WaitFor { $script:tool = One (Elements $root 'Text' $button) "the tool row's Text"; ($script:tool.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [Windows.Automation.ToggleState]::On) -and ((Elements $root 'Meme' $button).Count -eq 1) -and ((Elements $root 'Click the picture to add text' $text).Count -eq 1) } 5000) "the preview's Edit did not open the editor with the Text tool and its row (Meme, ""Click the picture to add text"")"
    Take 'editor' ([ReadmeDesk]::FrameRect((StashWindow))) 12

    # All or nothing: every committed picture is held for writing and read before any is written, so a picture another
    # program holds stops the run with none changed, and a write that fails puts back the ones already written.
    $scene = 'writing'
    New-Item -ItemType Directory -Force -Path $pictures | Out-Null
    $fresh = [ordered]@{}
    foreach ($name in $scenes) { $fresh[$name] = [IO.File]::ReadAllBytes((Join-Path $staging "$name.png")) }
    $open = [ordered]@{}
    $before = @{}
    $made = @()
    $written = $false
    try {
        foreach ($name in $scenes) {
            $target = Join-Path $pictures "$name.png"
            $existed = Test-Path -LiteralPath $target
            try { $open[$name] = [IO.File]::Open($target, 'OpenOrCreate', 'ReadWrite', 'None') }
            catch { throw "the $name picture could not be written to ${target}: $($_.Exception.GetBaseException().Message)" }
            if (-not $existed) { $made += $target }
            $before[$name] = New-Object byte[] $open[$name].Length
            $null = $open[$name].Read($before[$name], 0, $before[$name].Length)
        }
        $touched = @()
        try {
            foreach ($name in $scenes) {
                $touched += $name
                $open[$name].SetLength(0)
                $open[$name].Write($fresh[$name], 0, $fresh[$name].Length)
                $open[$name].Flush($true)
            }
        }
        catch {
            $cause = $_.Exception.GetBaseException().Message
            foreach ($done in $touched) { $open[$done].SetLength(0); $open[$done].Write($before[$done], 0, $before[$done].Length) }
            throw "the $name picture could not be written, so the pictures written before it were put back: $cause"
        }
        $written = $true
    }
    finally {
        foreach ($stream in $open.Values) { $stream.Dispose() }
        if (-not $written) { foreach ($path in $made) { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue } }
    }
    Write-Host "wrote $(($scenes | ForEach-Object { "docs\screenshots\$_.png" }) -join ', ')"
}
catch {
    $failure = if ($scene -eq 'writing') { $_.Exception.Message } elseif ($scene -eq 'setup') { "setup failed: $($_.Exception.Message)" } else { "the $scene scene failed: $($_.Exception.Message)" }
}
finally {
    # Each step on its own, so one that fails leaves the others done; a failed one is named and fails the run.
    Tidy 'put the mouse pointer back' { [ReadmeDesk]::MovePointer($pointerBefore[0], $pointerBefore[1]) }
    Tidy 'stop the published copy' {
        foreach ($process in @(Get-Process -Name ImageStash.Windows -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            $process.WaitForExit()
        }
    }
    Tidy 'stop the chat window' { if ($chat -and -not $chat.HasExited) { $chat.Kill(); $null = $chat.WaitForExit(5000) } }
    # The copy wrote this value for itself; put back what was there before the run.
    Tidy 'put back the Run value ImageStash' {
        if ($null -ne $runBefore) { New-ItemProperty -Path $runKey -Name ImageStash -PropertyType $runBefore.Kind -Value $runBefore.Data -Force | Out-Null }
        else { Remove-ItemProperty -Path $runKey -Name ImageStash -ErrorAction SilentlyContinue }
    }
    if ($createdAppData) { Tidy "remove $appData" { RemoveFolder $appData } }
    Tidy "remove $work" { RemoveFolder $work }
}
if ($failure) { Write-Host "make-readme-screenshots: $failure" }
foreach ($line in $untidy) { Write-Host "make-readme-screenshots: could not $line" }
if ($failure -or $untidy.Count -gt 0) { exit 1 }
exit 0
