<#
.SYNOPSIS
The captures on the published copy, run by check-published.ps1 inside its session after check-published-screenshot.ps1
(it uses check-published-paste.ps1's PasteDesk, check-published-paste-in.ps1's PasteInClipboard,
check-published-screenshot.ps1's ScreenshotDesk and stash-windows.ps1's StashWindows and check-published.ps1's CopyReadBack): Record a clip
from the tray menu, listed third, from the flyout's + menu, listed after Take a screenshot, and from the real
Ctrl+Shift+PrtScn, each showing the overlay in record mode, closed again, so it adds nothing. Then, over the check's own
stash: a picture on the clipboard as a browser's Copy image places it and the real Ctrl+Alt+V add one "Screenshot
YYYY-MM-DD HH.MM.SS" PNG of its size, announced; the real Ctrl+PrtScn and a real drag add one PNG of the dragged size,
on the clipboard as a file, "PNG" and CF_DIBV5, announced; the real Ctrl+Shift+PrtScn and a real drag select a region
waiting under the frame and the bar with Record, .work\ empty and the tray menu reading Start recording. After a wait,
the tray's Start recording starts it, the bar offers Stop instead of Record and the real Ctrl+Alt+Space shows the stash
window; the tray's Stop recording ends it with one "Recording YYYY-MM-DD HH.MM.SS" GIF named at the start act and
lasting from that act to its end, with two or more images about 7 cs apart, on the clipboard as a file only, announced
with its length, and nothing of the frame or the bar left. The tray's Record a clip and a real drag select another
region; after a wait the real shortcut starts it and the shortcut again stops it, named and timed from that start act.
A third region, cancelled through the bar's Cancel recording while still waiting, adds nothing (G15, G34, F5, F12, F4,
G3, E3). Throughout, no capture records a use; at the end the stash root holds one file per item and the store, .work\
and originals\ hold nothing, and the copy shows no window but the stash window once its notifications have gone. Exits
with the number of checks that failed.

With -Store, the stash is the check's own and every capture runs; without it the stash is the user's, and the captures
that add items to it are skipped.

An overlay shown without real input is in front of nothing: Windows does not bring a window to the front for a choice
made through UI Automation. The cancelled ones are closed with WM_CLOSE, which cancels as Esc does; the recorded one
takes a real drag, which reaches the topmost window under the pointer in front or not.

Sends real keystrokes (Ctrl+Alt+V, Ctrl+PrtScn, Ctrl+Shift+PrtScn, Ctrl+Alt+Space, Esc), moves the pointer and drags across the primary
display's middle and puts the pointer back, posts clicks to the tray icon, replaces the clipboard, records a few seconds
of the screen, and leaves the copy running for check-published.ps1 to stop.
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [string] $Store = ''
)
$ErrorActionPreference = 'Stop'
$failed = 0
$ellipsis = [char]0x2026
$recordTitle = 'ImageStash record a clip'
$screenshotTitle = 'ImageStash screenshot'
$frameTitle = 'ImageStash recording'
$barTitle = 'ImageStash recording bar'
$stillOnly = 'A pasted picture is always a still; to add an animated one, drag its file in.'

Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// A real drag in physical pixels (per-monitor aware on the calling thread), and the copy's visible windows by title.
// The INPUT layout is x64's: the union at offset 8, 40 bytes in all.
public static class CaptureDesk
{
    private delegate bool EnumProc(IntPtr window, IntPtr data);

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct MouseInput { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public int X; [FieldOffset(12)] public int Y; [FieldOffset(20)] public uint Flags; }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }

    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, MouseInput[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr window, StringBuilder text, int length);

    // Absolute over the virtual desk, with the given buttons: 0x0002 left down, 0x0004 left up.
    private static void Send(int x, int y, uint buttons)
    {
        SetThreadDpiAwarenessContext(new IntPtr(-4));
        int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
        var input = new MouseInput { Type = 0, X = (int)Math.Round((x - vx) * 65535.0 / (vw - 1)), Y = (int)Math.Round((y - vy) * 65535.0 / (vh - 1)), Flags = 0xC001 | buttons };
        SendInput(1, new[] { input }, 40);
    }

    /// <summary>
    /// Press at the first corner, move in eight steps, release at the second, as a person drags: the rectangle it
    /// selects, both corners in, where Windows placed the pointer (left, top, width, height).
    /// </summary>
    public static int[] Drag(int fromX, int fromY, int toX, int toY)
    {
        Send(fromX, fromY, 0);
        Thread.Sleep(30);
        Send(fromX, fromY, 0x0002);
        Point pressed;
        GetCursorPos(out pressed);
        for (var step = 1; step <= 8; step++)
        {
            Thread.Sleep(15);
            Send(fromX + (toX - fromX) * step / 8, fromY + (toY - fromY) * step / 8, 0);
        }
        Thread.Sleep(30);
        Point at;
        GetCursorPos(out at);
        Send(toX, toY, 0x0004);
        return new[] { Math.Min(pressed.X, at.X), Math.Min(pressed.Y, at.Y), Math.Abs(at.X - pressed.X) + 1, Math.Abs(at.Y - pressed.Y) + 1 };
    }

    /// <summary>The titles of the process's visible windows, minimised ones left out.</summary>
    public static List<string> Shown(int processId)
    {
        var found = new List<string>();
        EnumWindows(delegate (IntPtr window, IntPtr data)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            var text = new StringBuilder(512);
            GetWindowTextW(window, text, text.Capacity);
            if (owner == processId && IsWindowVisible(window) && !IsIconic(window)) { found.Add(text.ToString()); }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static string TitleOf(IntPtr window)
    {
        var text = new StringBuilder(512);
        GetWindowTextW(window, text, text.Capacity);
        return text.ToString();
    }

    /// <summary>The process's visible window whose title starts with <paramref name="start"/>, the newest notice's words for a notification; or none.</summary>
    public static IntPtr TitledFrom(int processId, string start)
    {
        var found = IntPtr.Zero;
        EnumWindows(delegate (IntPtr window, IntPtr data)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            var text = new StringBuilder(512);
            GetWindowTextW(window, text, text.Capacity);
            if (owner == processId && IsWindowVisible(window) && text.ToString().StartsWith(start, StringComparison.Ordinal)) { found = window; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@

function Check([bool] $held, [string] $what) {
    if ($held) { Write-Host "ok   $what" } else { Write-Host "FAIL $what"; $script:failed++ }
}

function WaitFor([scriptblock] $condition, [int] $milliseconds) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.ElapsedMilliseconds -lt $milliseconds) {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 20
    }
    return [bool](& $condition)
}

# The copy's elements that UI Automation finds under <root> with <name> and <type>.
function Elements($root, [string] $name, $type) {
    $condition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $running),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, $type))
    @($root.FindAll([Windows.Automation.TreeScope]::Descendants, $condition) | Where-Object { (-not $name) -or ($_.Current.Name -eq $name) })
}

function Choose($element) { $element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }

function Overlays([string] $title) { [ScreenshotDesk]::Titled($running, $title) }

function CloseOverlays { foreach ($title in $recordTitle, $screenshotTitle) { foreach ($window in Overlays $title) { [PasteDesk]::Close($window) } } }

function Count([string] $sql) { [int][CopyReadBack]::Query($Store, $sql) }

function Newest([string] $column) { [CopyReadBack]::Query($Store, "SELECT $column FROM items ORDER BY added_at DESC, id DESC LIMIT 1") }

# The auto names of every second the capture spans, and one either side, in local time (F12).
function NamedBetween([string] $kind, [datetime] $from, [datetime] $to) {
    for ($at = $from.AddSeconds(-1); $at -le $to.AddSeconds(1); $at = $at.AddSeconds(1)) { "$kind " + $at.ToString('yyyy-MM-dd HH.mm.ss') }
}

# The file of an item named <name>: the name as its stem, with " 2", " 3" ... while another item's file has it, as two
# captures in one second share their name (P1, F12).
function FileOf([string] $name, [string] $extension, [string] $file) {
    ($file -eq "$name.$extension") -or ($file -match ('^' + [regex]::Escape($name) + ' \d+\.' + $extension + '$'))
}

# The tray menu, opened with a posted right-click: its entries' words, joined by ' | ', and the one named <name> chosen.
function ChooseInTrayMenu([string] $name) {
    Check ([StashWindows]::RightClickTrayIcon($running)) 'a right-click posted to the tray icon'
    $null = WaitFor { (Elements $desktop 'Quit ImageStash' $menuItem).Count -gt 0 } 3000
    $entries = (Elements $desktop '' $menuItem | ForEach-Object { $_.Current.Name }) -join ' | '
    $entry = @(Elements $desktop $name $menuItem)
    if ($entry.Count -eq 1) { Choose $entry[0] }
    else { Check $false "the tray menu holds one $name to choose: $($entry.Count) in '$entries'" }
    $entries
}

# The recording's frame and bar, both visible, or neither (F4).
function RecordingShown { ([ScreenshotDesk]::Titled($running, $frameTitle).Count -eq 1) -and ([ScreenshotDesk]::Titled($running, $barTitle).Count -eq 1) }

# Read the bar through UI Automation, just as Cancel recording is found below (G15).
function BarButtons([bool] $waiting) {
    $bars = @([ScreenshotDesk]::Titled($running, $barTitle))
    if ($bars.Count -ne 1) { return $false }
    $bar = [Windows.Automation.AutomationElement]::FromHandle($bars[0])
    $record = @(Elements $bar 'Record' ([Windows.Automation.ControlType]::Button)).Count
    $stop = @(Elements $bar 'Stop' ([Windows.Automation.ControlType]::Button)).Count
    $cancel = @(Elements $bar 'Cancel recording' ([Windows.Automation.ControlType]::Button)).Count
    ($cancel -eq 1) -and (($waiting -and ($record -eq 1) -and ($stop -eq 0)) -or
        ((-not $waiting) -and ($record -eq 0) -and ($stop -eq 1)))
}

# Release selects only: keep the wait long enough to expose a name or duration taken at release (G15, F12).
function CheckWaiting([string] $which) {
    $shown = WaitFor { ((Overlays $recordTitle).Count -eq 0) -and (RecordingShown) -and (BarButtons $true) } 3000
    Check $shown "$which release selected a waiting region: frame and bar show with Record and Cancel, no Stop (G15, F4)"
    Check (WorkEmpty) "$which .work\ is empty while the region waits (G15, E3)"
    Start-Sleep -Seconds 2
    Check ((RecordingShown) -and (BarButtons $true) -and (WorkEmpty)) "$which after two seconds it still waits with Record and .work\ empty (G15, E3)"
}

function NothingOfTheRecordingLeft { ([ScreenshotDesk]::Titled($running, $frameTitle).Count -eq 0) -and ([ScreenshotDesk]::Titled($running, $barTitle).Count -eq 0) }

function WorkEmpty { @(Get-ChildItem -Path (Join-Path $stash '.work') -Force -ErrorAction SilentlyContinue).Count -eq 0 }

# The GIF's images and their delays in centiseconds, as System.Drawing reads the file (PropertyTagFrameDelay, 0x5100).
function GifDelays([string] $path) {
    $image = [Drawing.Image]::FromStream([IO.MemoryStream]::new([IO.File]::ReadAllBytes($path)))
    try {
        $frames = $image.GetFrameCount([Drawing.Imaging.FrameDimension]::Time)
        $value = $image.GetPropertyItem(0x5100).Value
        @(for ($at = 0; $at -lt $frames; $at++) { [BitConverter]::ToInt32($value, $at * 4) })
    }
    finally { $image.Dispose() }
}

# One recording's clip, the newest item: a GIF named at its start, the act between <from> and <started> that started
# it, with two or more images about 7 cs apart, lasting about the <sampledMs> from that act to its end (F3, F12, G15). A
# sample the PC could not take in time is skipped and the frame before it lasts the longer (G15, T3), so the pace is
# read from the middle delay.
function CheckClip([string] $how, [datetime] $from, [datetime] $started, [long] $sampledMs) {
    $name = Newest 'name'
    $file = Newest 'finished_file'
    $format = Newest 'format'
    # The release came two seconds or more before <from> (CheckWaiting), so the second either side admitted here never
    # holds a name taken at the release.
    Check (((NamedBetween 'Recording' $from $started) -contains $name) -and (FileOf $name 'gif' $file) -and ($format -eq 'gif')) "$how is a GIF named at the Record act in local time, its file named for it (F3, F12): '$name', '$file', $format, start act $($from.ToString('HH:mm:ss.fff')) to $($started.ToString('HH:mm:ss.fff'))"
    $path = Join-Path $stash $file
    $delays = if (Test-Path $path) { @(GifDelays $path) } else { @() }
    $paced = @($delays | Select-Object -SkipLast 1 | Sort-Object)
    $middle = if ($paced.Count -gt 0) { $paced[[math]::Floor($paced.Count / 2)] } else { 0 }
    Check (($delays.Count -ge 2) -and ($middle -ge 6) -and ($middle -le 8) -and (@($paced | Where-Object { $_ -gt 50 }).Count -eq 0)) "its file holds two or more images about 7 cs apart, the last lasting until the end (F3, G15): $($delays.Count) images, delays $($delays -join ' ')"
    $lasts = [long](Newest 'duration_ms')
    $summed = 10 * ($delays | Measure-Object -Sum).Sum
    Check (($lasts -eq $summed) -and ([math]::Abs($lasts - $sampledMs) -le 1000)) "it lasts the recorded time: $lasts ms, its delays summed $summed ms, against $sampledMs ms from the Record act to the end, excluding the wait (G15)"
    @{ Name = $name; Path = $path }
}

$copies = @(Get-Process -Name ImageStash.Windows -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe })
if ($copies.Count -ne 1) {
    Write-Host "FAIL the capture phase found $($copies.Count) running copies of $Exe, not one"
    exit 1
}
$running = $copies[0].Id
$displays = [ScreenshotDesk]::Displays()
$desktop = [Windows.Automation.AutomationElement]::RootElement
$menuItem = [Windows.Automation.ControlType]::MenuItem
$items = if ($Store) { Count 'SELECT COUNT(*) FROM items' } else { 0 }
$pointer = [PasteDesk]::Pointer()
try {
    # G34, G15: the tray's Record a clip, third, shows the overlay in record mode; closed, it adds nothing.
    $entries = ChooseInTrayMenu 'Record a clip'
    Check ($entries -eq 'Open ImageStash | Take a screenshot | Record a clip | Stash clipboard image | Pause shortcuts | Settings | Quit ImageStash') "the tray menu lists Record a clip third, in G34's order (G34): $entries"
    Check (WaitFor { (Overlays $recordTitle).Count -eq $displays } 3000) "the tray's Record a clip showed the overlay in record mode over all $displays display(s) (G34, G15)"
    CloseOverlays
    Check (WaitFor { ((Overlays $recordTitle).Count -eq 0) -and (NothingOfTheRecordingLeft) } 2000) 'closed, the overlay cancelled, and no overlay, frame or bar is left (F4)'
    if ($Store) { Check ((Count 'SELECT COUNT(*) FROM items') -eq $items) "that cancel added nothing to the stash of $items (E3)" }

    # G2, G3, G15: the flyout's + menu lists Record a clip after Take a screenshot; it hides the flyout like any start.
    # Waited for by its title: a notification still up counts as a showing window to ClickTrayIcon.
    $null = [StashWindows]::ClickTrayIcon($running, 5000)
    $null = WaitFor { @([ScreenshotDesk]::Titled($running, 'ImageStash')).Count -eq 1 } 3000
    $flyouts = @([ScreenshotDesk]::Titled($running, 'ImageStash'))
    Check ($flyouts.Count -eq 1) "a posted click on the tray icon showed the stash window: $($flyouts.Count) shown"
    if ($flyouts.Count -eq 1) {
        $flyout = $flyouts[0]
        $add = @(Elements ([Windows.Automation.AutomationElement]::FromHandle($flyout)) 'Add to your stash' ([Windows.Automation.ControlType]::Button))
        if ($add.Count -eq 1) {
            Choose $add[0]
            $null = WaitFor { (Elements $desktop 'Record a clip' $menuItem).Count -gt 0 } 3000
            $entries = (Elements $desktop '' $menuItem | ForEach-Object { $_.Current.Name }) -join ' | '
            Check ($entries -eq "Import files$ellipsis | Paste image | Take a screenshot | Record a clip") "the + menu lists Record a clip after Take a screenshot (G2): $entries"
            $entry = @(Elements $desktop 'Record a clip' $menuItem)
            if ($entry.Count -eq 1) {
                Choose $entry[0]
                $shown = WaitFor { (Overlays $recordTitle).Count -eq $displays } 3000
                Check ($shown -and ([ScreenshotDesk]::Titled($running, 'ImageStash').Count -eq 0)) "the + menu's Record a clip showed the overlay in record mode with the flyout hidden (G3, G15)"
                CloseOverlays
                Check (WaitFor { ((Overlays $recordTitle).Count -eq 0) -and ([ScreenshotDesk]::Titled($running, 'ImageStash') -contains $flyout) } 2000) 'once the overlay closed, the flyout shows again (G3)'
            }
            else {
                Check $false "the + menu holds one Record a clip to choose: $($entry.Count)"
            }
        }
        else {
            Check $false 'the window holds its + button, Add to your stash'
        }
    }

    # F5, G15: the real Ctrl+Shift+PrtScn, registered at the copy's start, reaches the act; a real Esc cancels it.
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x10, 0x2C))
    $shown = WaitFor { ((Overlays $recordTitle).Count -eq $displays) -and ((Overlays $recordTitle) -contains [PasteDesk]::Foreground()) } 3000
    Check $shown "the real Ctrl+Shift+PrtScn showed the overlay in record mode over all $displays display(s), in front (F5, G15)"
    # A person's first key comes a moment after the overlay, not the instant it is in front.
    Start-Sleep -Milliseconds 300
    $null = [PasteDesk]::Press([uint16[]]@(0x1B))
    Check (WaitFor { ((Overlays $recordTitle).Count -eq 0) -and (NothingOfTheRecordingLeft) } 2000) 'a real Esc closed it, and no overlay, frame or bar is left (G15, F4)'
    if ($Store) { Check ((Count 'SELECT COUNT(*) FROM items') -eq $items) "that cancel added nothing to the stash of $items (E3)" }

    if (-not $Store) {
        Write-Host "skipped: the stash is the user's, so the captures that add items to it do not run"
        exit $failed
    }

    $stash = Split-Path $Store -Parent
    $uses = Count 'SELECT COUNT(*) FROM uses'
    # The region dragged: the primary display's middle, clear of the tray's notifications at its bottom right.
    $fromX, $fromY, $toX, $toY = 300, 200, 559, 379

    # G13, G16, F12, F3, N9: Stash clipboard image from its real shortcut, a browser's picture on the clipboard.
    $width = 8
    $height = 5
    $pixels = New-Object byte[] ($width * $height * 4)
    for ($at = 0; $at -lt $width * $height; $at++) {
        $pixels[$at * 4] = 30 * ($at % $width)
        $pixels[$at * 4 + 1] = 140
        $pixels[$at * 4 + 2] = 50 * [math]::Floor($at / $width)
        $pixels[$at * 4 + 3] = 255
    }
    $bitmap = New-Object Drawing.Bitmap $width, $height, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($at = 0; $at -lt $width * $height; $at++) {
        $bitmap.SetPixel($at % $width, [math]::Floor($at / $width), [Drawing.Color]::FromArgb(255, $pixels[$at * 4 + 2], $pixels[$at * 4 + 1], $pixels[$at * 4]))
    }
    $stream = New-Object IO.MemoryStream
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    $png = $stream.ToArray()
    Check ([PasteInClipboard]::Put($png, [PasteInClipboard]::DibV5($width, $height, $pixels))) 'a picture on the clipboard as a browser''s Copy image places it: "PNG", then CF_DIBV5, no file list'
    $before = Get-Date
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x12, 0x56))
    $added = WaitFor { (Count 'SELECT COUNT(*) FROM items') -eq $items + 1 } 10000
    $after = Get-Date
    Check $added "the real Ctrl+Alt+V added one item to the stash of $items (F5, G13)"
    $name = Newest 'name'
    $file = Newest 'finished_file'
    Check (((NamedBetween 'Screenshot' $before $after) -contains $name) -and (FileOf $name 'png' $file) -and ((Newest 'format') -eq 'png') -and ((Newest 'width') -eq "$width") -and ((Newest 'height') -eq "$height")) "it is a $width by $height PNG named like a screenshot at the press's local time (F12, F3, G13): '$name', '$file'"
    $saved = "Saved $([char]0x201C)$name$([char]0x201D) to your stash."
    $announced = WaitFor { [CaptureDesk]::TitledFrom($running, $saved) -ne [IntPtr]::Zero } 3000
    $took = $clock.ElapsedMilliseconds
    $notification = [CaptureDesk]::TitledFrom($running, $saved)
    $text = if ($announced) { [ScreenshotDesk]::Titled($running, "$saved $stillOnly").Count } else { 0 }
    Check ($announced -and ($took -le 1000) -and ($text -eq 1)) "a notification above the tray says it is in the stash and that an animated picture is added by dragging its file in, within a second (G13, G16): $took ms"
    Check ((Count 'SELECT COUNT(*) FROM uses') -eq $uses) 'Stash clipboard image recorded no use (G10)'
    $items++

    # G14, G16, G8, G10, F12: the screenshot from its real shortcut and a real drag, copied as Also copy has it.
    $before = Get-Date
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x2C))
    $shown = WaitFor { ((Overlays $screenshotTitle).Count -eq $displays) -and ((Overlays $screenshotTitle) -contains [PasteDesk]::Foreground()) } 3000
    Check $shown "the real Ctrl+PrtScn showed the screenshot's overlay over all $displays display(s), in front (F5, G14)"
    # A person's first input comes a moment after the overlay, not the instant it is in front.
    Start-Sleep -Milliseconds 300
    $region = [CaptureDesk]::Drag($fromX, $fromY, $toX, $toY)
    $added = WaitFor { (Count 'SELECT COUNT(*) FROM items') -eq $items + 1 } 10000
    $after = Get-Date
    Check ($added -and ((Overlays $screenshotTitle).Count -eq 0)) "the drag's release added one item and closed the overlay (G14, F4)"
    $name = Newest 'name'
    $file = Newest 'finished_file'
    $path = Join-Path $stash $file
    Check (((NamedBetween 'Screenshot' $before $after) -contains $name) -and (FileOf $name 'png' $file) -and ((Newest 'format') -eq 'png') -and ((Newest 'width') -eq "$($region[2])") -and ((Newest 'height') -eq "$($region[3])")) "it is a PNG of the dragged $($region[2]) by $($region[3]) pixels with the auto name (G14, F12, F3): '$name', '$file', $(Newest 'width') by $(Newest 'height')"
    Check (WaitFor { [CopyReadBack]::Clipboard() -eq "files=$path png=True dibv5=True effect=1" } 3000) "the clipboard holds it as a file, ""PNG"" and CF_DIBV5 (G14, G8): $([CopyReadBack]::Clipboard())"
    $saved = "Saved $([char]0x201C)$name$([char]0x201D) to your stash. It is on the clipboard too."
    Check (WaitFor { [ScreenshotDesk]::Titled($running, $saved).Count -eq 1 } 3000) "a notification says it is in the stash and on the clipboard (G16): '$saved'"
    Check ((Count 'SELECT COUNT(*) FROM uses') -eq $uses) 'the screenshot and its copy recorded no use (G10)'
    $items++

    # G15, G34, F5: select by shortcut, start from the tray, end by the tray's Stop recording.
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x10, 0x2C))
    $shown = WaitFor { ((Overlays $recordTitle).Count -eq $displays) -and ((Overlays $recordTitle) -contains [PasteDesk]::Foreground()) } 3000
    Check $shown "the real Ctrl+Shift+PrtScn showed the overlay in record mode over all $displays display(s), in front (F5, G15)"
    Start-Sleep -Milliseconds 300
    $null = [CaptureDesk]::Drag($fromX, $fromY, $toX, $toY)
    CheckWaiting 'the first'
    $before = Get-Date
    $sampled = [Diagnostics.Stopwatch]::StartNew()
    $entries = ChooseInTrayMenu 'Start recording'
    $started = Get-Date
    Check ($entries -eq 'Open ImageStash | Take a screenshot | Start recording | Stash clipboard image | Pause shortcuts | Settings | Quit ImageStash') "while it waits, the tray menu's third entry reads Start recording (G15, G34): $entries"
    Check (WaitFor { (RecordingShown) -and (BarButtons $false) } 3000) "the tray's Start recording started it: the bar holds Stop and Cancel, no Record (G15, G34)"
    # G3: while it records, the real Ctrl+Alt+Space summons the stash window as usual.
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x12, 0x20))
    Check (WaitFor { @([ScreenshotDesk]::Titled($running, 'ImageStash')).Count -eq 1 } 3000) 'while it records, the real Ctrl+Alt+Space shows the stash window (G3)'
    Start-Sleep -Seconds 2
    $entries = ChooseInTrayMenu 'Stop recording'
    $sampled.Stop()
    Check ($entries -eq 'Open ImageStash | Take a screenshot | Stop recording | Stash clipboard image | Pause shortcuts | Settings | Quit ImageStash') "while it records, the tray menu's third entry reads Stop recording (G15, G34): $entries"
    $added = WaitFor { ((Count 'SELECT COUNT(*) FROM items') -eq $items + 1) -and (NothingOfTheRecordingLeft) } 15000
    Check $added "the tray's Stop recording ended it: one item added, the frame and the bar gone (G15, G34, F4): $(([CaptureDesk]::Shown($running)) -join ' | ')"
    $clip = CheckClip 'the clip' $before $started $sampled.ElapsedMilliseconds
    Check (WaitFor { [CopyReadBack]::Clipboard() -eq "files=$($clip.Path) png=False dibv5=False effect=1" } 3000) "the clipboard holds the clip as a file only (G15, G8): $([CopyReadBack]::Clipboard())"
    $saved = "Saved $([char]0x201C)$($clip.Name)$([char]0x201D) to your stash. It is on the clipboard too. It is "
    $announced = WaitFor { [CaptureDesk]::TitledFrom($running, $saved) -ne [IntPtr]::Zero } 3000
    $words = if ($announced) { [CaptureDesk]::TitleOf([CaptureDesk]::TitledFrom($running, $saved)) } else { '' }
    $length = ([long](Newest 'duration_ms') / 1000.0).ToString('0.0', [Globalization.CultureInfo]::InvariantCulture) + ' s'
    Check ($words -eq "$saved$length long.") "a notification says it is in the stash and on the clipboard, with its length (G15, G16): '$words'"
    if ($announced) {
        # The window stacks every card still up, each its picture's name, then its Edit or Trim button, then Undo.
        $found = @([Windows.Automation.AutomationElement]::FromHandle([CaptureDesk]::TitledFrom($running, $saved)).FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition))
        $offers = @()
        $undos = 0
        $owner = $null
        foreach ($element in $found) {
            $word = $element.Current.Name
            $pressed = $element.Current.ControlType -eq [Windows.Automation.ControlType]::Button
            if ($word -match '^(Screenshot|Recording) \d{4}-\d\d-\d\d \d\d\.\d\d\.\d\d') { $owner = $word }
            elseif ($pressed -and ($word -in 'Edit', 'Trim')) { $offers += "$owner=$word"; $owner = $null }
            elseif ($pressed -and ($word -eq 'Undo')) { $undos++ }
        }
        $mismatched = @($offers | Where-Object { -not (($_ -like 'Screenshot *=Edit') -or ($_ -like 'Recording *=Trim')) })
        $settings = @($found | Where-Object { $_.Current.Name -eq 'Settings' }).Count
        Check (($offers -contains "$($clip.Name)=Trim") -and ($mismatched.Count -eq 0) -and ($offers.Count -eq $undos) -and ($settings -eq 0)) "each card offers Edit for its still or Trim for its clip, beside Undo, and none offers Settings, which is not built (G16): $($offers -join ' | '); $undos Undo"
    }
    Check (WorkEmpty) "$stash\.work\ holds nothing of the finished clip (State, R2)"
    $items++

    # G15, G34, F5: select from the tray, then the real shortcut starts and stops the second clip.
    $entries = ChooseInTrayMenu 'Record a clip'
    Check ($entries -eq 'Open ImageStash | Take a screenshot | Record a clip | Stash clipboard image | Pause shortcuts | Settings | Quit ImageStash') "once it ended, the tray menu reads Record a clip again (G15, G34): $entries"
    Check (WaitFor { (Overlays $recordTitle).Count -eq $displays } 3000) "the tray's Record a clip showed the overlay in record mode (G34, G15)"
    Start-Sleep -Milliseconds 300
    $null = [CaptureDesk]::Drag($fromX, $fromY, $toX, $toY)
    CheckWaiting 'the second'
    $before = Get-Date
    $sampled = [Diagnostics.Stopwatch]::StartNew()
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x10, 0x2C))
    $started = Get-Date
    Check (WaitFor { (RecordingShown) -and (BarButtons $false) } 3000) 'the real Ctrl+Shift+PrtScn started the waiting region: Stop and Cancel, no Record (F5, G15)'
    Start-Sleep -Seconds 2
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x10, 0x2C))
    $sampled.Stop()
    $added = WaitFor { ((Count 'SELECT COUNT(*) FROM items') -eq $items + 1) -and (NothingOfTheRecordingLeft) } 15000
    Check $added "the real Ctrl+Shift+PrtScn again stopped it: one item added, the frame and the bar gone (F5, G15, F4)"
    $null = CheckClip 'that clip' $before $started $sampled.ElapsedMilliseconds
    $items++

    # G15, E3: a third region, cancelled through the bar while still waiting, adds nothing.
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x10, 0x2C))
    $shown = WaitFor { ((Overlays $recordTitle).Count -eq $displays) -and ((Overlays $recordTitle) -contains [PasteDesk]::Foreground()) } 3000
    Start-Sleep -Milliseconds 300
    $null = [CaptureDesk]::Drag($fromX, $fromY, $toX, $toY)
    Check $shown 'the real shortcut showed the third selection overlay (F5, G15)'
    CheckWaiting 'the third'
    if (RecordingShown) {
        $bar = [ScreenshotDesk]::Titled($running, $barTitle)[0]
        $cancel = @(Elements ([Windows.Automation.AutomationElement]::FromHandle($bar)) 'Cancel recording' ([Windows.Automation.ControlType]::Button))
        Check ($cancel.Count -eq 1) 'the bar holds its Cancel recording'
        if ($cancel.Count -eq 1) { Choose $cancel[0] }
        Check (WaitFor { NothingOfTheRecordingLeft } 5000) 'Cancel recording while waiting closed the frame and the bar (G15, F4)'
        Start-Sleep -Milliseconds 500
        Check (((Count 'SELECT COUNT(*) FROM items') -eq $items) -and (WorkEmpty)) "Cancel while waiting added nothing to the stash of $items and no clip left in .work\ (G15, E3)"
    }

    # G28, G29, R2, F4: the stash root holds one file per item and the store, and nothing of a finished change waits.
    $listed = Count 'SELECT COUNT(*) FROM items WHERE deleted = 0'
    $inRoot = @(Get-ChildItem -Path $stash -File -Force | Where-Object { $_.Name -ne 'stash.db' }).Count
    Check (($inRoot -eq $listed) -and (Test-Path $Store)) "the stash root holds the store and one file per item: $inRoot files for $listed items (F6, R2)"
    Check ((WorkEmpty) -and (@(Get-ChildItem -Path (Join-Path $stash 'originals') -Force -ErrorAction SilentlyContinue).Count -eq 0)) "$stash\.work\ and originals\ hold nothing: no change waits and nothing was edited (State, G29)"
    Check ((Count 'SELECT COUNT(*) FROM uses') -eq $uses) 'no capture recorded a use (G10)'

    # F4: once the notifications have gone, no window of the copy shows but the stash window.
    [PasteDesk]::MovePointer($fromX, $fromY)
    $alone = WaitFor { @([CaptureDesk]::Shown($running) | Where-Object { $_ -ne 'ImageStash' }).Count -eq 0 } 20000
    Check $alone "once the notifications have gone, the copy shows no window but the stash window (F4): $(([CaptureDesk]::Shown($running)) -join ' | ')"
}
catch {
    Write-Host "FAIL the capture phase stopped: $($_.Exception.Message)"
    $failed++
}
finally {
    CloseOverlays
    [PasteDesk]::MovePointer($pointer[0], $pointer[1])
}
exit $failed
