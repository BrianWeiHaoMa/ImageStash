<#
.SYNOPSIS
The editor on the published copy, run by check-published.ps1 inside its session after check-published-capture.ps1 (it
uses check-published-paste.ps1's PasteDesk, check-published-screenshot.ps1's ScreenshotDesk,
check-published-capture.ps1's CaptureDesk and stash-windows.ps1's StashWindows and check-published.ps1's CopyReadBack), over the check's
own stash: a screenshot from the real Ctrl+PrtScn and a real drag; a real click on its card's Edit shows the stash
window in front, in the editor on that item with Text current, the card still above it with its Undo, until its close
button dismisses it; a real click on the Text row's Caption bar and one on the picture, words typed and Ctrl+S save it. The
saved PNG, decoded by System.Drawing, has the screenshot's width, is taller by a white bar with dark text, and holds
the screenshot's pixels beneath it; the original in originals\ keeps the screenshot's bytes; the clipboard, which held
the screenshot's copy, holds the saved file with a "PNG" of its height and pixels; the store keeps the caption bar; the
window stays in front with the item selected, which Space's preview names, and Esc closes the preview. Then a
recording from the real Ctrl+Shift+PrtScn and a real drag, five seconds long, the flyout still up; a real click on its
card's Trim shows the editor on it with Trim current; a screenshot and a recording started then, each cancelled with
Esc, hide the window and bring the edit back as left; the filmstrip's left handle dragged to its middle with real
input and Ctrl+S save a GIF of about the second half of the images, at the recording's own delays, the store keeping
the trim, the original in originals\ keeping every image, and the window stays in front
(G16, G20, G21, G22, G26, G3, F3, F6, G29). No edit records a use (G10). Exits with the number of checks that failed.

With -Store, the stash is the check's own and every step runs; without it the stash is the user's, and the phase is
skipped, since it adds two items there and edits them.

Sends real keystrokes (Ctrl+PrtScn, Ctrl+Shift+PrtScn, letters, Ctrl+S, Space, Esc), clicks the capture cards and the
stash window, drags across the primary display's middle and along the editor's filmstrip and puts the pointer back,
replaces the clipboard with a screenshot's copy and its refresh after Save, records about five seconds of the screen,
and leaves the copy running for check-published.ps1 to stop.
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [string] $Store = ''
)
$ErrorActionPreference = 'Stop'
$failed = 0
$screenshotTitle = 'ImageStash screenshot'
$recordTitle = 'ImageStash record a clip'
$frameTitle = 'ImageStash recording'
$barTitle = 'ImageStash recording bar'

Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

// The saved pictures read back by System.Drawing, the clipboard's "PNG" read the same way, a window's title bar, and the
// window under a point.
public static class EditorDesk
{
    [StructLayout(LayoutKind.Sequential)] private struct ScreenPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(ScreenPoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string name);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr memory);
    [DllImport("user32.dll")] private static extern int GetWindowLongW(IntPtr window, int index);

    /// <summary>Whether <paramref name="window"/> has a title bar: GWL_STYLE has WS_CAPTION, as the pinned window does.</summary>
    public static bool Captioned(IntPtr window) { return (GetWindowLongW(window, -16) & 0x00C00000) == 0x00C00000; }

    /// <summary>The top-level window a click at (<paramref name="x"/>, <paramref name="y"/>), in physical pixels, would reach.</summary>
    public static IntPtr Under(int x, int y) { return GetAncestor(WindowFromPoint(new ScreenPoint { X = x, Y = y }), 2); }

    // Top-down rows of 32-bit ARGB, read from the bytes so the file is not held open.
    private static int[] Pixels(byte[] file, out int width, out int height)
    {
        using (var bitmap = new Bitmap(new MemoryStream(file)))
        {
            width = bitmap.Width;
            height = bitmap.Height;
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var pixels = new int[width * height];
                for (var y = 0; y < height; y++) { Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * width, width); }
                return pixels;
            }
            finally { bitmap.UnlockBits(data); }
        }
    }

    /// <summary>
    /// The band <paramref name="saved"/> adds above <paramref name="original"/>: its height, 1 when its four corners are
    /// white, how many of its pixels are dark, and 1 when the rows beneath it are the original's, pixel for pixel; null
    /// when the widths differ or the saved one is not taller.
    /// </summary>
    public static int[] Band(string saved, string original)
    {
        int width, height, originalWidth, originalHeight;
        var after = Pixels(File.ReadAllBytes(saved), out width, out height);
        var before = Pixels(File.ReadAllBytes(original), out originalWidth, out originalHeight);
        if (width != originalWidth || height <= originalHeight) { return null; }
        var band = height - originalHeight;
        var white = unchecked((int)0xFFFFFFFF);
        var corners = after[0] == white && after[width - 1] == white && after[(band - 1) * width] == white && after[band * width - 1] == white;
        var dark = 0;
        for (var at = 0; at < band * width; at++)
        {
            var pixel = after[at];
            if (((pixel >> 16) & 0xFF) < 100 && ((pixel >> 8) & 0xFF) < 100 && (pixel & 0xFF) < 100) { dark++; }
        }
        var same = true;
        for (var at = 0; same && at < before.Length; at++) { same = after[band * width + at] == before[at]; }
        return new[] { band, corners ? 1 : 0, dark, same ? 1 : 0 };
    }

    /// <summary>
    /// The clipboard's "PNG" as System.Drawing decodes it: its width, its height, and 1 when its pixels are
    /// <paramref name="file"/>'s; null when there is none or the clipboard stayed held.
    /// </summary>
    public static int[] PngOnClipboard(string file)
    {
        for (var attempt = 0; !OpenClipboard(IntPtr.Zero); attempt++)
        {
            if (attempt == 40) { return null; }
            Thread.Sleep(50);
        }
        byte[] png;
        try
        {
            var memory = GetClipboardData(RegisterClipboardFormatW("PNG"));
            if (memory == IntPtr.Zero) { return null; }
            png = new byte[(int)GlobalSize(memory).ToUInt64()];
            Marshal.Copy(GlobalLock(memory), png, 0, png.Length);
            GlobalUnlock(memory);
        }
        finally { CloseClipboard(); }
        int width, height, fileWidth, fileHeight;
        var copied = Pixels(png, out width, out height);
        var saved = Pixels(File.ReadAllBytes(file), out fileWidth, out fileHeight);
        var same = width == fileWidth && height == fileHeight;
        for (var at = 0; same && at < copied.Length; at++) { same = copied[at] == saved[at]; }
        return new[] { width, height, same ? 1 : 0 };
    }
}
'@

function Check([bool] $held, [string] $what) {
    if ($held) { Write-Host "ok   $what" } else { Write-Host "FAIL $what"; $script:failed++ }
}

# A window's elements can go while UI Automation reads them, which throws: the condition does not hold yet.
function Holds([scriptblock] $condition) { try { [bool](& $condition) } catch { $false } }

function WaitFor([scriptblock] $condition, [int] $milliseconds) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.ElapsedMilliseconds -lt $milliseconds) {
        if (Holds $condition) { return $true }
        Start-Sleep -Milliseconds 20
    }
    return (Holds $condition)
}

# The copy's elements that UI Automation finds under <root> named <name>, of <type> when one is given.
function Elements($root, [string] $name, $type) {
    $conditions = @(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $running),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $name))
    if ($type) { $conditions += [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, $type) }
    @($root.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.AndCondition]::new([Windows.Automation.Condition[]]$conditions)))
}

# A check the steps after it rest on: once it fails, the phase stops there.
function Need([bool] $held, [string] $what) {
    Check $held $what
    if (-not $held) { throw "the steps after '$what' need it" }
}

# The one element of <found>, needed for <what>. A search that found nothing arrives here as null, not as an empty list.
function One($found, [string] $what) {
    $all = @($found | Where-Object { $null -ne $_ })
    Need ($all.Count -eq 1) "$what is there once: $($all.Count) found"
    $all[0]
}

# A real click at the middle of <element>: UI Automation's rectangles are in physical pixels ("Understanding Screen
# Scaling Issues", Microsoft Learn, 2025-07-14), as PasteDesk's input is.
function ClickOn($element) {
    $box = $element.Current.BoundingRectangle
    [PasteDesk]::Click([int]($box.X + $box.Width / 2), [int]($box.Y + $box.Height / 2))
}

function Toggled($element) {
    $element.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [Windows.Automation.ToggleState]::On
}

# Each letter of <word> as its own real key press, lower case.
function TypeWord([string] $word) { foreach ($letter in $word.ToUpperInvariant().ToCharArray()) { $null = [PasteDesk]::Press([uint16][char]$letter) } }

function Count([string] $sql) { [int][CopyReadBack]::Query($Store, $sql) }

function Newest([string] $column) { [CopyReadBack]::Query($Store, "SELECT $column FROM items ORDER BY added_at DESC, id DESC LIMIT 1") }

function Of([long] $id, [string] $column) { [CopyReadBack]::Query($Store, "SELECT $column FROM items WHERE id = $id") }

# The stash window, titled for the product; or none.
function StashWindow { @([ScreenshotDesk]::Titled($running, 'ImageStash')) | Select-Object -First 1 }

function InFront { $window = StashWindow; ($null -ne $window) -and ([PasteDesk]::Foreground() -eq $window) }

function Front { "'$([CaptureDesk]::TitleOf([PasteDesk]::Foreground()))'" }

# Saved: the layers committed, the picture rendered for them and placed, nothing pending (State: Save).
function Saved([long] $id) { (Count "SELECT COUNT(*) FROM items WHERE id = $id AND layer_revision > 0 AND rendered_revision = layer_revision AND pending_steps IS NULL AND original_in_originals = 1") -eq 1 }

function RecordingShown { ([ScreenshotDesk]::Titled($running, $frameTitle).Count -eq 1) -and ([ScreenshotDesk]::Titled($running, $barTitle).Count -eq 1) }

function NothingOfTheRecordingLeft { ([ScreenshotDesk]::Titled($running, $frameTitle).Count -eq 0) -and ([ScreenshotDesk]::Titled($running, $barTitle).Count -eq 0) }

# The GIF's delays in centiseconds, one per image, as System.Drawing reads the file (PropertyTagFrameDelay, 0x5100).
function GifDelays([string] $path) {
    $image = [Drawing.Image]::FromStream([IO.MemoryStream]::new([IO.File]::ReadAllBytes($path)))
    try {
        $frames = $image.GetFrameCount([Drawing.Imaging.FrameDimension]::Time)
        $value = $image.GetPropertyItem(0x5100).Value
        @(for ($at = 0; $at -lt $frames; $at++) { [BitConverter]::ToInt32($value, $at * 4) })
    }
    finally { $image.Dispose() }
}

function Sha([string] $path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash }

# The capture card whose words begin with <saved>, its <action> clicked with real input (G16).
function ClickCard([string] $saved, [string] $action) {
    Need (WaitFor { [CaptureDesk]::TitledFrom($running, $saved) -ne [IntPtr]::Zero } 5000) "a card says '$saved' (G16)"
    # The window stacks every card still up, and one going moves the others: click once this card is the only one.
    $alone = WaitFor { (Elements ([Windows.Automation.AutomationElement]::FromHandle([CaptureDesk]::TitledFrom($running, $saved))) 'Undo' $button).Count -eq 1 } 10000
    Need $alone "its card is the only one up, so it holds still under the click"
    $card = [Windows.Automation.AutomationElement]::FromHandle([CaptureDesk]::TitledFrom($running, $saved))
    ClickOn (One (Elements $card $action $button) "its $action")
}

# The stash window in front in the editor, its tool row holding <tool>, within five seconds of <act>; its root element.
function Editor([string] $tool, [string] $act) {
    $open = WaitFor { (InFront) -and ((Elements ([Windows.Automation.AutomationElement]::FromHandle((StashWindow))) $tool $button).Count -eq 1) } 5000
    $found = try { (Elements ([Windows.Automation.AutomationElement]::FromHandle((StashWindow))) $tool $button).Count } catch { 'no window, or no' }
    Need $open "$act showed the stash window in front, in the editor (G16, G20): in front $(Front), $found $tool found"
    [Windows.Automation.AutomationElement]::FromHandle((StashWindow))
}

# After a Save the window a card opened stays in front, back on the grid, its footer reading Copy (G26, G8).
function StaysOpen($root, [string] $after) {
    $back = WaitFor { (InFront) -and ((Elements $root 'Crop' $button).Count -eq 0) } 5000
    Check $back "after $after the window stays in front with the editor closed (G26): in front $(Front)"
    Check (WaitFor { (Elements $root "Enter $([char]0xB7) Copy" $null).Count -ge 1 } 3000) "its footer reads Enter $([char]0xB7) Copy, no paste target (G8)"
}

if (-not $Store) {
    Write-Host "skipped: the stash is the user's, under $(Join-Path $env:LOCALAPPDATA 'ImageStash'), so the editor phase, which adds a screenshot and a recording to it and edits them, does not run"
    exit 0
}
$copies = @(Get-Process -Name ImageStash.Windows -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe })
if ($copies.Count -ne 1) {
    Write-Host "FAIL the editor phase found $($copies.Count) running copies of $Exe, not one"
    exit 1
}
$running = $copies[0].Id
$stash = Split-Path $Store -Parent
$originals = Join-Path $stash 'originals'
$displays = [ScreenshotDesk]::Displays()
$button = [Windows.Automation.ControlType]::Button
$items = Count 'SELECT COUNT(*) FROM items'
$uses = Count 'SELECT COUNT(*) FROM uses'
$pointer = [PasteDesk]::Pointer()
# The region dragged: the primary display's middle, clear of the tray's notifications at its bottom right.
$fromX, $fromY, $toX, $toY = 300, 200, 559, 379
try {
    # G14, G16, G20: a screenshot from the real shortcut, then its card's Edit clicked as a person clicks it.
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x2C))
    Need (WaitFor { ([ScreenshotDesk]::Titled($running, $screenshotTitle).Count -eq $displays) -and ([ScreenshotDesk]::Titled($running, $screenshotTitle) -contains [PasteDesk]::Foreground()) } 3000) "the real Ctrl+PrtScn showed the screenshot's overlay, in front (F5, G14)"
    # A person's first input comes a moment after the overlay, not the instant it is in front.
    Start-Sleep -Milliseconds 300
    $region = [CaptureDesk]::Drag($fromX, $fromY, $toX, $toY)
    Need (WaitFor { (Count 'SELECT COUNT(*) FROM items') -eq $items + 1 } 10000) "the drag's release added one item to the stash of $items (G14)"
    $items++
    $shot = [long](Newest 'id')
    $name = Of $shot 'name'
    $screenshot = Join-Path $stash (Of $shot 'finished_file')
    $screenshotSha = Sha $screenshot
    Check (WaitFor { [CopyReadBack]::Clipboard() -eq "files=$screenshot png=True dibv5=True effect=1" } 3000) "the clipboard holds the screenshot, as Also copy has it (G14): $([CopyReadBack]::Clipboard())"
    ClickCard "Saved $([char]0x201C)$name$([char]0x201D) to your stash. It is on the clipboard too." 'Edit'
    $root = Editor 'Crop' "the card's Edit"
    Check (-not [EditorDesk]::Captioned((StashWindow))) 'the window is the flyout, without a title bar, as it was not pinned (G16, G1)'
    # G16: the flyout and the card both stand above the tray; the card stays above the flyout its Edit opened, so its Undo
    # still takes a click while that edit is open.
    $cardWindow = [CaptureDesk]::TitledFrom($running, "Saved $([char]0x201C)$name$([char]0x201D) to your stash.")
    $undo = (One (Elements ([Windows.Automation.AutomationElement]::FromHandle($cardWindow)) 'Undo' $button) "the card's Undo").Current.BoundingRectangle
    $undoX = [int]($undo.X + $undo.Width / 2); $undoY = [int]($undo.Y + $undo.Height / 2)
    Check (WaitFor { [EditorDesk]::Under($undoX, $undoY) -eq $cardWindow } 2000) "the card stays above the flyout its Edit opened, its Undo the window under a click at ($undoX, $undoY) (G16): $([EditorDesk]::Under($undoX, $undoY)) there, the card's window $cardWindow, the flyout $(StashWindow)"
    # Over the flyout's lower rows the card is in the way, as for a person, who dismisses it with its close button.
    ClickOn (One (Elements ([Windows.Automation.AutomationElement]::FromHandle($cardWindow)) 'Close' $button) "the card's close button")
    Need (WaitFor { [CaptureDesk]::TitledFrom($running, "Saved $([char]0x201C)$name$([char]0x201D) to your stash.") -eq [IntPtr]::Zero } 3000) "the card's close button dismissed it (G16)"
    $text = One (Elements $root 'Text' $button) "the tool row's Text"
    Check ((Toggled $text) -and ((Elements $root 'Trim' $button).Count -eq 0) -and ((Elements $root $name ([Windows.Automation.ControlType]::Text)).Count -ge 1)) "the editor is on '$name' with Text current and no Trim, a still's (G16, G20)"

    # G21: Caption bar on the Text row, then a click on the picture adds a bar with its field open for the words typed.
    ClickOn (One (Elements $root 'Caption bar' $button) "the Text row's Caption bar")
    $head = (One (Elements $root 'Back' $button) "the editor's Back").Current.BoundingRectangle
    $tools = $text.Current.BoundingRectangle
    $whole = $root.Current.BoundingRectangle
    # The canvas is no element of its own; the picture is centred on it, between the head and the tool row.
    [PasteDesk]::Click([int]($whole.X + $whole.Width / 2), [int](($head.Bottom + $tools.Top) / 2))
    Need (WaitFor { (Elements $root 'Text' ([Windows.Automation.ControlType]::Edit)).Count -eq 1 } 3000) "a click on the picture under Caption bar opened a caption bar's text field (G21)"
    TypeWord 'hello'
    Start-Sleep -Milliseconds 200
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x53))
    Need (WaitFor { Saved $shot } 10000) "Ctrl+S saved it: the layers committed, rendered and placed, the original moved into originals\ (G25, G26, F6)"

    # F3, G21, G29: the saved file and the original read back by another decoder.
    $finished = Join-Path $stash (Of $shot 'finished_file')
    $original = Join-Path $originals (Of $shot 'original_file')
    Check ((Test-Path $original) -and ((Sha $original) -eq $screenshotSha)) "the original in originals\ is the screenshot's bytes unchanged (G29, F6): $original"
    $band = if ((Test-Path $finished) -and (Test-Path $original)) { [EditorDesk]::Band($finished, $original) } else { $null }
    Check (($null -ne $band) -and ((Of $shot 'finished_format') -eq 'png') -and ((Of $shot 'finished_width') -eq "$($region[2])") -and ((Of $shot 'finished_height') -eq "$($region[3] + $band[0])")) "the saved $finished is a PNG of the screenshot's width, $($region[2]), taller than its $($region[3]) by the bar, as System.Drawing reads it (F3, G21): band $(if ($band) { $band[0] })"
    Check (($null -ne $band) -and ($band[1] -eq 1) -and ($band[2] -gt 0) -and ($band[3] -eq 1)) "the bar above is white with dark words, and beneath it the screenshot's pixels are unchanged (G21): corners white $(if ($band) { $band[1] }), $(if ($band) { $band[2] }) dark pixels, the picture beneath the same $(if ($band) { $band[3] })"
    $refreshed = WaitFor { ([CopyReadBack]::Clipboard() -eq "files=$finished png=True dibv5=True effect=1") -and (([EditorDesk]::PngOnClipboard($finished) -join ' ') -eq "$($region[2]) $($region[3] + $band[0]) 1") } 3000
    Check $refreshed "the clipboard, which held ImageStash's copy of the screenshot, holds the saved picture: its file, and a ""PNG"" of its size and pixels (G26): $([CopyReadBack]::Clipboard()), PNG width, height, same pixels: $(([EditorDesk]::PngOnClipboard($finished)) -join ' ')"
    $layers = @((Of $shot 'layers') | ConvertFrom-Json | ForEach-Object { $_ })
    Check (($layers.Count -eq 1) -and ($layers[0].kind -eq 'captionBar') -and ($layers[0].text -eq 'hello')) "the store keeps the layer for the next Edit: one caption bar holding the words typed (G26, F7): $(Of $shot 'layers')"
    StaysOpen $root 'Save'
    # Space on the grid previews the selected item, which names it (G7, G19).
    $null = [PasteDesk]::Press([uint16[]]@(0x20))
    Check (WaitFor { ((Elements $root 'Edit' $button).Count -eq 1) -and ((Elements $root $name ([Windows.Automation.ControlType]::Text)).Count -ge 1) } 3000) "the item saved is the one selected: Space previews '$name' (G26, G19)"
    $null = [PasteDesk]::Press([uint16[]]@(0x1B))
    Check (WaitFor { (Elements $root 'Edit' $button).Count -eq 0 } 3000) 'Esc closed the preview (G2)'

    # G15, G16, G22: a recording from the real shortcut, then its card's Trim.
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x10, 0x2C))
    Need (WaitFor { ([ScreenshotDesk]::Titled($running, $recordTitle).Count -eq $displays) -and ([ScreenshotDesk]::Titled($running, $recordTitle) -contains [PasteDesk]::Foreground()) } 3000) "the real Ctrl+Shift+PrtScn showed the overlay in record mode, in front (F5, G15)"
    Start-Sleep -Milliseconds 300
    $null = [CaptureDesk]::Drag($fromX, $fromY, $toX, $toY)
    Need (WaitFor { RecordingShown } 3000) 'the release left the region waiting under the frame and the bar (G15)'
    Start-Sleep -Milliseconds 300
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x10, 0x2C))
    $bar = [Windows.Automation.AutomationElement]::FromHandle([ScreenshotDesk]::Titled($running, $barTitle)[0])
    Need (WaitFor { (Elements $bar 'Stop' $button).Count -eq 1 } 3000) 'the real Ctrl+Shift+PrtScn again started it: the bar offers Stop (G15)'
    Start-Sleep -Seconds 5
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x10, 0x2C))
    Need (WaitFor { ((Count 'SELECT COUNT(*) FROM items') -eq $items + 1) -and (NothingOfTheRecordingLeft) } 15000) "the shortcut once more stopped it after five seconds: one item added to the stash of $items (G15)"
    $items++
    $clip = [long](Newest 'id')
    $clipName = Of $clip 'name'
    $recording = Join-Path $stash (Of $clip 'finished_file')
    $recordingSha = Sha $recording
    $recorded = @(GifDelays $recording)
    Need ($recorded.Count -ge 4) "the recording holds images enough to trim: $($recorded.Count), delays $($recorded -join ' ')"
    ClickCard "Saved $([char]0x201C)$clipName$([char]0x201D) to your stash. It is on the clipboard too. It is " 'Trim'
    $root = Editor 'Trim' "the card's Trim"
    $trim = One (Elements $root 'Trim' $button) "the tool row's Trim"
    Check ((Toggled $trim) -and ((Elements $root $clipName ([Windows.Automation.ControlType]::Text)).Count -ge 1)) "the editor is on '$clipName' with the Trim tool selected (G16, G22)"

    # G3: a screenshot and a recording started during the edit hide the flyout until they end; each cancelled with Esc
    # at its overlay, the window shows again with the edit as it was left.
    foreach ($start in @{ Keys = [uint16[]]@(0x11, 0x2C); Title = $screenshotTitle; Act = 'a screenshot' }, @{ Keys = [uint16[]]@(0x11, 0x10, 0x2C); Title = $recordTitle; Act = 'a recording' }) {
        $null = [PasteDesk]::Press($start.Keys)
        Check (WaitFor { ([ScreenshotDesk]::Titled($running, $start.Title) -contains [PasteDesk]::Foreground()) -and ($null -eq (StashWindow)) } 3000) "$($start.Act) started during the edit hid the flyout, its overlay in front (G3)"
        # A person's first key comes a moment after the overlay, not the instant it is in front.
        Start-Sleep -Milliseconds 300
        $null = [PasteDesk]::Press([uint16[]]@(0x1B))
        $back = WaitFor { ([ScreenshotDesk]::Titled($running, $start.Title).Count -eq 0) -and ($null -ne (StashWindow)) -and (@(Elements $root 'Trim' $button | Where-Object { Toggled $_ }).Count -eq 1) } 3000
        Check ($back -and ((Count 'SELECT COUNT(*) FROM items') -eq $items)) "cancelled with Esc, it added nothing and the window shows again with the edit on Trim as left (G3)"
    }

    # G22: the filmstrip's left handle, at the kept frames' first edge, dragged to the strip's middle.
    $strip = (One (Elements $root 'Filmstrip' $null) 'the filmstrip').Current.BoundingRectangle
    Start-Sleep -Seconds 1
    # The strip is a lane 16 tall over cells 44 tall; the press lands in the cells' middle.
    $cells = [int]($strip.Y + $strip.Height * 38 / 60)
    $null = [CaptureDesk]::Drag([int]($strip.X + 2), $cells, [int]($strip.X + $strip.Width / 2), $cells)
    Start-Sleep -Milliseconds 200
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x53))
    Need (WaitFor { Saved $clip } 15000) "Ctrl+S saved the trim: committed, rendered and placed, the recording moved into originals\ (G25, G26, F6)"

    # F3, G22, G29: the kept frames at the recording's own timing, the trimmed ones still in the original.
    $finished = Join-Path $stash (Of $clip 'finished_file')
    $original = Join-Path $originals (Of $clip 'original_file')
    $kept = if (Test-Path $finished) { @(GifDelays $finished) } else { @() }
    $tail = @($recorded | Select-Object -Last $kept.Count)
    # The handle went to the cell edge nearest the strip's middle, so the second half is kept, give or take a cell.
    $half = $recorded.Count - [math]::Round($recorded.Count / 2)
    Check (((Of $clip 'finished_format') -eq 'gif') -and ([math]::Abs($kept.Count - $half) -le 1) -and (($kept -join ' ') -eq ($tail -join ' ')) -and ((Of $clip 'finished_frame_count') -eq "$($kept.Count)")) "the saved $finished is a GIF of $($kept.Count) of the recording's $($recorded.Count) images, about the second half, its delays the recording's last ones, as System.Drawing reads them (G22, F3): $($kept -join ' ') against $($tail -join ' ')"
    $trimmed = @((Of $clip 'layers') | ConvertFrom-Json | ForEach-Object { $_ })
    Check (($trimmed.Count -eq 1) -and ($trimmed[0].kind -eq 'trim') -and ($trimmed[0].range[0] -eq $recorded.Count - $kept.Count) -and ($trimmed[0].range[1] -eq $recorded.Count - 1)) "the store keeps the trim for the next Edit: from the first image kept to the last (G26, G22): $(Of $clip 'layers')"
    $whole = if (Test-Path $original) { @(GifDelays $original) } else { @() }
    Check ((Test-Path $original) -and ((Sha $original) -eq $recordingSha) -and ($whole.Count -eq $recorded.Count)) "the original in originals\ is the recording unchanged, every one of its $($recorded.Count) images kept (G22, G29): $($whole.Count)"
    StaysOpen $root 'the trim''s Save'
    Check ((Count 'SELECT COUNT(*) FROM uses') -eq $uses) 'no capture, edit or clipboard refresh recorded a use (G10)'
}
catch {
    Write-Host "FAIL the editor phase stopped: $($_.Exception.Message)"
    $failed++
}
finally {
    [PasteDesk]::MovePointer($pointer[0], $pointer[1])
}
exit $failed
