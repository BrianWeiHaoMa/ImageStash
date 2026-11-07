<#
.SYNOPSIS
Open with... on the published copy, run by check-published.ps1 inside its session after check-published-editor.ps1 (it
uses check-published-paste.ps1's PasteDesk, check-published-screenshot.ps1's ScreenshotDesk,
check-published-capture.ps1's CaptureDesk and check-published.ps1's StashWindows and CopyReadBack), over the check's
own stash, with this script standing for the program a person would choose. An unedited PNG, found by its name typed
into the search box, gets its item menu from the menu key, where Open with... stands between Save a copy... and Reveal
in File Explorer; a real click on it brings the shell's Open-with dialog to the front while the flyout stays, the row
recording a session whose copy, under %LOCALAPPDATA%\ImageStash\open-with\, holds the finished picture's bytes; the
dialog is closed as a cancel and the flyout is in front again. The copy written over in place with a PNG of another
size raises "Replaced <name> with the picture another program saved." with Undo, the row already final as it shows:
the root file under the item's name holds the rewrite, its hash the original's, the previous picture held in .work\,
and the tile shows the new picture; a real click on Undo puts the previous bytes back and sends the rewrite to the
Recycle Bin, and the tile shows the old picture again. Undecodable bytes over that copy are named, the file and
"unreadable", and change nothing. Two more rewrites, the second while the first's notification shows, are adopted
with one Undo standing, the picture the first replaced recycled at once and the one the second replaced once the
notification has gone by itself. Then the editor phase's captioned screenshot, an edited item, from its preview's More
options menu: the copy holds the rendered picture; replaced by a JPEG, the root holds the rewrite under the item's
name with .jpg, the render deleted rather than recycled, the layers dropped, the original still in originals\; Undo
brings back the original and the layers, the finished picture rendered again pixel for pixel, and recycles the JPEG;
another JPEG left to its notification's end sends the previous original to the Recycle Bin (G27, G9, G19, G2, G16,
G29, G35, R9, F3, F4). No step records a use (G10). Exits with the number of checks that failed.

With -Store, the stash is the check's own and every step runs; without it the stash is the user's, and the phase is
skipped, since it replaces two items' pictures there.

Sends real keystrokes (letters typed as characters, Ctrl+A, Tab, the menu key, Space), clicks the stash window, its
menus and a notification, opens the shell's Open-with dialog twice and closes it with WM_CLOSE (then Esc, then the
system menu's Close), ending an OpenWith.exe host it started that is still running, writes over the two Open with...
copies as another program would, reads the screen's pixels over the stash window's grid, removes from the Recycle Bin
the pictures the phase sends there, failed or not, puts the pointer back, and leaves the copy running for
check-published.ps1 to stop.
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [string] $Store = ''
)
$ErrorActionPreference = 'Stop'
$failed = 0

Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// The shell's Open-with dialog, characters typed as a person's keyboard would send them, and the screen's pixels.
public static class OpenWithDesk
{
    private delegate bool EnumProc(IntPtr window, IntPtr data);

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct KeyInput { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public ushort Key; [FieldOffset(10)] public ushort Scan; [FieldOffset(12)] public uint Flags; }

    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, KeyInput[] inputs, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr window, StringBuilder name, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowW(string className, string title);
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr window, uint message, IntPtr w, IntPtr l);

    // The dialog's class on Windows 11: a window of the shell's OpenWith.exe host, which EnumWindows does not list.
    private const string DialogClass = "Open With";

    /// <summary>Each character of <paramref name="text"/> as a key press carrying it (KEYEVENTF_UNICODE), whatever the layout.</summary>
    public static bool Type(string text)
    {
        var inputs = new KeyInput[text.Length * 2];
        for (var at = 0; at < text.Length; at++)
        {
            inputs[at * 2] = new KeyInput { Type = 1, Scan = text[at], Flags = 4 };
            inputs[at * 2 + 1] = new KeyInput { Type = 1, Scan = text[at], Flags = 6 };
        }
        return SendInput((uint)inputs.Length, inputs, 40) == inputs.Length;
    }

    private static string ClassOf(IntPtr window)
    {
        var name = new StringBuilder(256);
        return window != IntPtr.Zero && GetClassNameW(window, name, name.Capacity) > 0 ? name.ToString() : "";
    }

    /// <summary>The dialog's window in front, or none.</summary>
    public static IntPtr InFront()
    {
        var front = GetForegroundWindow();
        return ClassOf(front) == DialogClass ? front : IntPtr.Zero;
    }

    /// <summary>The dialog's window wherever it is: in front, by its class, or among the desktop window's children; or none.</summary>
    public static IntPtr Find()
    {
        var found = InFront();
        if (found == IntPtr.Zero) { found = FindWindowW(DialogClass, null); }
        if (found == IntPtr.Zero)
        {
            EnumChildWindows(GetDesktopWindow(), delegate (IntPtr window, IntPtr data)
            {
                if (ClassOf(window) == DialogClass && IsWindowVisible(window)) { found = window; return false; }
                return true;
            }, IntPtr.Zero);
        }
        return found;
    }

    public static bool Up(IntPtr dialog) { return IsWindow(dialog) && IsWindowVisible(dialog); }

    /// <summary>Asks the dialog to close as a cancel, which starts no program: 0 WM_CLOSE, 1 Esc, 2 the system menu's Close.</summary>
    public static void Close(IntPtr dialog, int way)
    {
        if (way == 0) { PostMessageW(dialog, 0x0010, IntPtr.Zero, IntPtr.Zero); }
        else if (way == 1) { PostMessageW(dialog, 0x0100, new IntPtr(0x1B), IntPtr.Zero); PostMessageW(dialog, 0x0101, new IntPtr(0x1B), IntPtr.Zero); }
        else { PostMessageW(dialog, 0x0112, new IntPtr(0xF060), IntPtr.Zero); }
    }

    /// <summary>How many pixels of the screen's rectangle, in physical pixels, are within <paramref name="tolerance"/> of the colour.</summary>
    public static int Coloured(int left, int top, int width, int height, int red, int green, int blue, int tolerance)
    {
        SetThreadDpiAwarenessContext(new IntPtr(-4));
        using (var shot = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            using (var graphics = Graphics.FromImage(shot)) { graphics.CopyFromScreen(left, top, 0, 0, new Size(width, height)); }
            var data = shot.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[width];
                var count = 0;
                for (var y = 0; y < height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, width);
                    foreach (var pixel in row)
                    {
                        if (Math.Abs(((pixel >> 16) & 0xFF) - red) <= tolerance && Math.Abs(((pixel >> 8) & 0xFF) - green) <= tolerance && Math.Abs((pixel & 0xFF) - blue) <= tolerance) { count++; }
                    }
                }
                return count;
            }
            finally { shot.UnlockBits(data); }
        }
    }

    /// <summary>A picture of one flat colour, encoded as <paramref name="format"/> ("png" or "jpeg").</summary>
    public static byte[] Flat(int width, int height, int red, int green, int blue, string format)
    {
        using (var picture = new Bitmap(width, height, PixelFormat.Format24bppRgb))
        {
            using (var graphics = Graphics.FromImage(picture)) { graphics.Clear(Color.FromArgb(red, green, blue)); }
            var bytes = new MemoryStream();
            picture.Save(bytes, format == "jpeg" ? ImageFormat.Jpeg : ImageFormat.Png);
            return bytes.ToArray();
        }
    }

    /// <summary>Whether two encoded pictures decode to the same size and the same pixels, however each was encoded.</summary>
    public static bool SamePixels(byte[] one, byte[] other)
    {
        using (var first = new Bitmap(new MemoryStream(one)))
        using (var second = new Bitmap(new MemoryStream(other)))
        {
            if (first.Width != second.Width || first.Height != second.Height) { return false; }
            var whole = new Rectangle(0, 0, first.Width, first.Height);
            var a = first.LockBits(whole, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var b = second.LockBits(whole, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var rowA = new int[first.Width];
                var rowB = new int[first.Width];
                for (var y = 0; y < first.Height; y++)
                {
                    Marshal.Copy(a.Scan0 + y * a.Stride, rowA, 0, first.Width);
                    Marshal.Copy(b.Scan0 + y * b.Stride, rowB, 0, first.Width);
                    for (var x = 0; x < first.Width; x++) { if (rowA[x] != rowB[x]) { return false; } }
                }
                return true;
            }
            finally { first.UnlockBits(a); second.UnlockBits(b); }
        }
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

# A check the steps after it rest on: once it fails, the phase stops there.
function Need([bool] $held, [string] $what) {
    Check $held $what
    if (-not $held) { throw "the steps after '$what' need it" }
}

# The copy's elements that UI Automation finds under <root> named <name>, of <type> when one is given.
# UI Automation can fail a read with E_FAIL while the window redraws: a read that failed is not what the window shows,
# so it is read again.
function Elements($root, [string] $name, $type) {
    $conditions = @(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $running),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $name))
    if ($type) { $conditions += [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, $type) }
    foreach ($read in 1..3) {
        try { return @($root.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.AndCondition]::new([Windows.Automation.Condition[]]$conditions))) }
        catch { if ($read -eq 3) { throw }; Start-Sleep -Milliseconds 50 }
    }
}

# The one element of <found>, needed for <what>. A search that found nothing arrives here as null, not as an empty list.
function One($found, [string] $what) {
    $all = @($found | Where-Object { $null -ne $_ })
    Need ($all.Count -eq 1) "$what is there once: $($all.Count) found"
    $all[0]
}

# A real click at the middle of <element>: UI Automation's rectangles are in physical pixels, as PasteDesk's input is.
function ClickOn($element) {
    $box = $element.Current.BoundingRectangle
    [PasteDesk]::Click([int]($box.X + $box.Width / 2), [int]($box.Y + $box.Height / 2))
}

function Count([string] $sql) { [int][CopyReadBack]::Query($Store, $sql) }

function Of([long] $id, [string] $column) { [CopyReadBack]::Query($Store, "SELECT $column FROM items WHERE id = $id") }

# Whether the row of <id> meets <where>, read in SQL, so a NULL is tested as one.
function Holding([long] $id, [string] $where) { (Count "SELECT COUNT(*) FROM items WHERE id = $id AND $where") -eq 1 }

# A file's SHA-256, read again for up to five seconds while another program, a scanner say, holds it (E3); null for none.
function Sha([string] $path) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
        try { return (Get-FileHash -Algorithm SHA256 -LiteralPath $path -ErrorAction Stop).Hash }
        catch { if ($clock.ElapsedMilliseconds -gt 5000) { throw } }
        Start-Sleep -Milliseconds 100
    }
}

function ShaOf([byte[]] $bytes) { -join ([Security.Cryptography.SHA256]::Create().ComputeHash($bytes) | ForEach-Object { $_.ToString('X2') }) }

# The stash window, titled for the product; or none.
function StashWindow { @([ScreenshotDesk]::Titled($running, 'ImageStash')) | Select-Object -First 1 }

function InFront { $window = StashWindow; ($null -ne $window) -and ([PasteDesk]::Foreground() -eq $window) }

function Front { "'$([CaptureDesk]::TitleOf([PasteDesk]::Foreground()))'" }

# The item menus' entries now open, top to bottom, wherever their popups are.
function MenuEntries {
    $menu = [Windows.Automation.AndCondition]::new([Windows.Automation.Condition[]]@(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $running),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::MenuItem)))
    @([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Descendants, $menu) | Sort-Object { $_.Current.BoundingRectangle.Y })
}

# Whether <entries> list Open with... right after Save a copy... and right before Reveal in File Explorer (G19).
function OpenWithBetween($entries) {
    $names = @($entries | ForEach-Object { $_.Current.Name })
    $at = [array]::IndexOf($names, "Open with$ellipsis")
    ($at -gt 0) -and ($names[$at - 1] -eq "Save a copy$ellipsis") -and ($at + 1 -lt $names.Count) -and ($names[$at + 1] -eq 'Reveal in File Explorer')
}

# The stash window's grid, the one scrolled pane with a size, as left, top, width, height.
function Grid {
    $panes = [Windows.Automation.AndCondition]::new([Windows.Automation.Condition[]]@(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $running),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty, 'ScrollViewer')))
    $box = @([Windows.Automation.AutomationElement]::FromHandle((StashWindow)).FindAll([Windows.Automation.TreeScope]::Descendants, $panes) |
        ForEach-Object { $_.Current.BoundingRectangle } | Where-Object { -not $_.IsEmpty })[0]
    @([int]$box.X, [int]$box.Y, [int]$box.Width, [int]$box.Height)
}

# The grid's pixels of the colour every rewrite of the unedited item is painted in.
function Magenta { $box = Grid; [OpenWithDesk]::Coloured($box[0], $box[1], $box[2], $box[3], 255, 0, 255, 24) }

# The item <id> alone in the grid and selected: its name typed into the search box with real input (G5).
function Find([long] $id) {
    $name = Of $id 'name'
    $root = [Windows.Automation.AutomationElement]::FromHandle((StashWindow))
    $box = One (Elements $root 'Search your stash' ([Windows.Automation.ControlType]::Edit)) 'the search box'
    ClickOn $box
    # The search answers after the keys, so the footer's count, which it moves with the selection, is read only once
    # it has left an earlier search's.
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x41))
    $null = [PasteDesk]::Press([uint16[]]@(0x08))
    Need (WaitFor { (Elements $root '1 item' ([Windows.Automation.ControlType]::Text)).Count -eq 0 } 3000) 'the search box emptied with Ctrl+A and Backspace, the scope back in the grid (G5)'
    $null = [OpenWithDesk]::Type($name)
    $typed = WaitFor { ($box.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -eq $name) -and ((Elements $root '1 item' ([Windows.Automation.ControlType]::Text)).Count -eq 1) } 3000
    Need $typed "'$name' typed into the search box leaves one match, which is selected (G5)"
    $root
}

# The dialog in front within ten seconds of <act>, the stash window still showing; closed as a cancel; its copy's path.
function Dialog([long] $id, [string] $act, [string] $finished) {
    Need (WaitFor { [OpenWithDesk]::InFront() -ne [IntPtr]::Zero } 10000) "$act brought the shell's Open-with dialog to the front (G27, S16): in front $(Front)"
    $dialog = [OpenWithDesk]::InFront()
    Check ($null -ne (StashWindow)) "the stash window stays showing while the dialog it opened is up (G2): '$([StashWindows]::Showing($running))'"
    Need (Holding $id 'open_with_session IS NOT NULL') "the row of '$(Of $id 'name')' records an Open with... session"
    $session = (Of $id 'open_with_session') | ConvertFrom-Json
    $copy = Join-Path (Join-Path $openWithRoot $session.folder) $session.file
    Check ((Test-Path -LiteralPath $copy) -and ((Sha $copy) -eq (Sha $finished)) -and ($session.sha256 -eq (Sha $finished))) "the row records an Open with... session whose copy, $copy, outside the stash folder, holds the finished picture's bytes and their hash (G27, G9)"
    for ($way = 0; ($way -lt 3) -and [OpenWithDesk]::Up($dialog); $way++) {
        [OpenWithDesk]::Close($dialog, $way)
        $null = WaitFor { -not [OpenWithDesk]::Up($dialog) } 3000
    }
    Need (-not [OpenWithDesk]::Up($dialog)) 'the dialog closed as a cancel, starting no program'
    Check (WaitFor { InFront } 3000) "the flyout is in front again once the dialog has closed, so it stays (G2): in front $(Front)"
    [PasteDesk]::MovePointer(200, 150)
    $copy
}

# A program saving over <copy>: in place, or a new file renamed over it, tried again while the window's reading holds it.
function Rewrite([string] $copy, [byte[]] $bytes, [switch] $Replace) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        try {
            if ($Replace) {
                [IO.File]::WriteAllBytes("$copy.saving", $bytes)
                # A null backup, not the empty string PowerShell would pass for $null.
                [IO.File]::Replace("$copy.saving", $copy, [NullString]::Value)
            }
            else { [IO.File]::WriteAllBytes($copy, $bytes) }
            return
        }
        catch { $why = $_.Exception.Message }
        Start-Sleep -Milliseconds 50
    } while ($clock.ElapsedMilliseconds -lt 5000)
    Need $false "the copy, $copy, was written over as another program saves it: $why"
}

# The notification saying <words>, within ten seconds, showing them without taking the focus (G16); its window. The
# moment it was first seen is kept for its lifetime.
function Notification([string] $words) {
    Need (WaitFor { [CaptureDesk]::TitledFrom($running, $words) -ne [IntPtr]::Zero } 10000) "a notification titled '$words' is up"
    $script:shownAt = [DateTime]::UtcNow
    $window = [CaptureDesk]::TitledFrom($running, $words)
    Check ((WaitFor { (Elements ([Windows.Automation.AutomationElement]::FromHandle($window)) $words ([Windows.Automation.ControlType]::Text)).Count -ge 1 } 2000) -and ([PasteDesk]::Foreground() -ne $window)) "it shows those words and has not taken the focus (G16): in front $(Front)"
    $window
}

# The notification saying <words> gone by itself within fifteen seconds of its showing, and not before its six (G16).
function GoneByItself([string] $words) {
    Need (WaitFor { [CaptureDesk]::TitledFrom($running, $words) -eq [IntPtr]::Zero } 15000) 'the notification went by itself'
    $lasted = ([DateTime]::UtcNow - $shownAt).TotalSeconds
    Check ($lasted -ge 5.5) ("it lasted {0:N1} seconds from the phase first seeing it, its six seconds and no fewer (G16)" -f $lasted)
}

# The Recycle Bin's entries on the stash's drive that came from inside the stash folder: each $I record names the path
# its $R file came from, after a character count at byte 24 in version 2, the one Windows 10 and 11 write.
function Recycled {
    $bin = Join-Path ([IO.Path]::GetPathRoot($stash)) ('$Recycle.Bin\' + [Security.Principal.WindowsIdentity]::GetCurrent().User.Value)
    $inside = $stash.TrimEnd('\') + '\'
    if (-not (Test-Path -LiteralPath $bin)) { return }
    foreach ($record in @(Get-ChildItem -LiteralPath $bin -Filter '$I*' -Force)) {
        try { $bytes = [IO.File]::ReadAllBytes($record.FullName) } catch { continue }
        if (($bytes.Length -lt 28) -or ([BitConverter]::ToInt64($bytes, 0) -ne 2)) { continue }
        $from = [Text.Encoding]::Unicode.GetString($bytes, 28, ([BitConverter]::ToInt32($bytes, 24) - 1) * 2)
        $file = Join-Path $bin ('$R' + $record.Name.Substring(2))
        if ($from.StartsWith($inside, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $file -PathType Leaf)) {
            [pscustomobject]@{ Record = $record.FullName; File = $file; From = $from; Sha = Sha $file }
        }
    }
}

# The titles of the copy's notifications now up: a notification is titled with its newest notice's words, and the stash
# window with the product's name; a popup or tooltip carries no title.
function Notifications { @([CaptureDesk]::Shown($running) | Where-Object { ($_ -ne '') -and ($_ -ne 'ImageStash') }) }

# The Undo buttons in the notification saying <words>, one for each notice with an Undo it shows; 0 while none says it.
function UndoButtons([string] $words) {
    $window = [CaptureDesk]::TitledFrom($running, $words)
    if ($window -eq [IntPtr]::Zero) { return 0 }
    (Elements ([Windows.Automation.AutomationElement]::FromHandle($window)) 'Undo' $button).Count
}

# A real click on the one Undo of the notification saying <words>.
function ClickUndo([string] $words) {
    ClickOn (One (Elements ([Windows.Automation.AutomationElement]::FromHandle([CaptureDesk]::TitledFrom($running, $words))) 'Undo' $button) "the notification's Undo")
}

# How many pictures of <sha> are in the Recycle Bin from <from>, a path inside the stash folder or the folder itself.
# Two screenshots of one unchanging region have the same bytes, so where a picture came from tells them apart.
function Binned([string] $sha, [string] $from = $stash) {
    @(Recycled | Where-Object { ($_.Sha -eq $sha) -and $_.From.StartsWith($from, [StringComparison]::OrdinalIgnoreCase) }).Count
}

# The facts an adoption, an Undo or a refused rewrite may change, as one line.
function Facts([long] $id) { Of $id "hex(original_sha256) || '|' || original_file || '|' || finished_file || '|' || original_in_originals || '|' || layers || '|' || layer_revision || '|' || rendered_revision || '|' || IFNULL(pending_steps, '') || '|' || IFNULL(adoption_undo, '')" }

if (-not $Store) {
    Write-Host "skipped: the stash is the user's, under $(Join-Path $env:LOCALAPPDATA 'ImageStash'), so the Open with... phase, which replaces two items' pictures there, does not run"
    exit 0
}
$copies = @(Get-Process -Name ImageStash.Windows -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe })
if ($copies.Count -ne 1) {
    Write-Host "FAIL the Open with... phase found $($copies.Count) running copies of $Exe, not one"
    exit 1
}
$running = $copies[0].Id
$ellipsis = [char]0x2026
$stash = Split-Path $Store -Parent
$originals = Join-Path $stash 'originals'
$openWithRoot = Join-Path $env:LOCALAPPDATA 'ImageStash\open-with'
$button = [Windows.Automation.ControlType]::Button
$uses = Count 'SELECT COUNT(*) FROM uses'
$pointer = [PasteDesk]::Pointer()
$windowsSession = [Diagnostics.Process]::GetCurrentProcess().SessionId
$hosts = @(Get-Process -Name OpenWith -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $windowsSession } | ForEach-Object { $_.Id })
# What the Recycle Bin holds from the check's stash before the phase, so the phase removes only what it sent there.
$binBefore = @(Recycled | ForEach-Object { $_.Record })
$dialogBefore = [OpenWithDesk]::Find()
$opened = $false
# An item the search finds alone, and whose rewrite lands under its name: two captures in one second share a name, the
# second's file then carrying " 2" (State: File names).
$alone = "finished_file = name || '.png' AND NOT EXISTS (SELECT 1 FROM items other WHERE other.id <> items.id AND other.name = items.name)"
try {
    Need ($dialogBefore -eq [IntPtr]::Zero) 'no Open-with dialog is up before the phase opens its own'
    Need (WaitFor { InFront } 3000) "the stash window the editor phase left is in front: $(Front)"
    # A card the editor phase raised may still be within its six seconds: the phase reads notifications of its own only.
    [PasteDesk]::MovePointer(200, 150)
    Need (WaitFor { (Notifications).Count -eq 0 } 20000) "no notification of the copy is up before the phase's own: $((Notifications) -join ' | ')"

    # G27, G19, G2: an unedited still, its item menu from the menu key on its tile (G7), Open with... clicked.
    $plain = [long][CopyReadBack]::Query($Store, "SELECT IFNULL(MAX(id), 0) FROM (SELECT id FROM items WHERE deleted = 0 AND layers = '[]' AND original_in_originals = 0 AND format = 'Png' AND $alone ORDER BY added_at DESC, id DESC LIMIT 1)")
    Need ($plain -ne 0) 'the stash holds an unedited PNG whose name no other item has, its file named after it'
    $name = Of $plain 'name'
    $before = Join-Path $stash (Of $plain 'finished_file')
    $beforeSha = Sha $before
    $root = Find $plain
    $null = [PasteDesk]::Press([uint16[]]@(0x09))
    Start-Sleep -Milliseconds 200
    $null = [PasteDesk]::Press([uint16[]]@(0x5D))
    Need (WaitFor { (MenuEntries).Count -gt 0 } 3000) 'Tab, then the menu key, opened the selected tile''s item menu (G7)'
    Check (OpenWithBetween (MenuEntries)) "its item menu lists Open with... between Save a copy... and Reveal in File Explorer: $((MenuEntries | ForEach-Object { $_.Current.Name }) -join ', ')"
    $opened = $true
    ClickOn (One @(MenuEntries | Where-Object { $_.Current.Name -eq "Open with$ellipsis" }) "the item menu's Open with...")
    $copy = Dialog $plain "a real click on the item menu's Open with... on '$name'" $before

    # G27, R9, G29: the copy written over in place with a PNG of another size, in a colour only the rewrite has.
    $magentaBefore = Magenta
    $width, $height = [int](Of $plain 'width'), [int](Of $plain 'height')
    $rewrite = [OpenWithDesk]::Flat($width + 37, $height + 23, 255, 0, 255, 'png')
    $rewriteSha = ShaOf $rewrite
    Rewrite $copy $rewrite
    $replaced = "Replaced $([char]0x201C)$name$([char]0x201D) with the picture another program saved."
    $null = Notification $replaced
    # Read as soon as the notification is seen, which is raised only after the adoption's last transaction and file
    # step (R9): an adoption still unfinished then fails it, though one finished in the instant between passes.
    $adopted = Join-Path $stash "$name.png"
    Check (Holding $plain 'pending_steps IS NULL AND rendered_revision = layer_revision AND adoption_undo IS NOT NULL') "as soon as the notification is seen, the adoption is complete: nothing pending, the picture placed for the row's revision, its Undo recorded (R9, G28)"
    Check ((Of $plain "hex(original_sha256) || '|' || original_file || '|' || finished_file || '|' || layers || '|' || width || 'x' || height") -eq "$rewriteSha|$name.png|$name.png|[]|$($width + 37)x$($height + 23)") "the rewrite is the item's original and finished picture, under the item's name with its own extension, its hash and size the rewrite's (G27, F3): $(Of $plain "original_file || ' ' || width || 'x' || height")"
    Check ((Test-Path -LiteralPath $adopted) -and ((Sha $adopted) -eq $rewriteSha)) "the stash folder's $adopted holds the rewrite's bytes (G27)"
    Check (@(Get-ChildItem -LiteralPath (Join-Path $stash '.work') -File -Force | Where-Object { (Sha $_.FullName) -eq $beforeSha }).Count -eq 1) 'the previous picture is held in .work\ while the notification stands, not yet in the Recycle Bin (G27, G29)'
    Check (WaitFor { (Magenta) -gt $magentaBefore + 2000 } 5000) "the tile shows the new picture: $(Magenta) of the grid's pixels in its colour, $magentaBefore before (G27)"

    # G27, G29: Undo on that notification, clicked as a person clicks it.
    ClickUndo $replaced
    Check (WaitFor { Holding $plain "adoption_undo IS NULL AND pending_steps IS NULL AND hex(original_sha256) = '$beforeSha'" } 10000) "Undo restored the previous original as the row's, nothing pending and no Undo standing (G27): $(Of $plain "hex(original_sha256) || ' ' || original_file")"
    Check ((Test-Path -LiteralPath $before) -and ((Sha $before) -eq $beforeSha)) "the stash folder's $before holds the previous bytes again (G27, G29)"
    Check ((Binned $rewriteSha) -eq 1) "the rewrite went to the Recycle Bin from the stash folder: $((Recycled | Where-Object { $_.Sha -eq $rewriteSha } | ForEach-Object { $_.From }) -join ', ') (G27, G29)"
    Check (WaitFor { [CaptureDesk]::TitledFrom($running, $replaced) -eq [IntPtr]::Zero } 3000) 'the notification went with its Undo'
    Check (WaitFor { (Magenta) -le $magentaBefore + 200 } 5000) "the tile shows the previous picture again: $(Magenta) of the grid's pixels in the rewrite's colour, $magentaBefore before (G27)"

    # G35: bytes that decode as nothing written over the same copy are named, and nothing of the item changes.
    $facts = Facts $plain
    Rewrite $copy ([Text.Encoding]::ASCII.GetBytes(('not a picture ' * 64)))
    $unreadable = "Take back an Open with$ellipsis edit failed: $(Split-Path $copy -Leaf), it is unreadable"
    $refusal = Notification $unreadable
    Check (((Facts $plain) -eq $facts) -and ((Sha $before) -eq $beforeSha)) "the undecodable rewrite is named with the copy's file and changes nothing: the row and $before as they were (G35, G27)"
    ClickOn (One (Elements ([Windows.Automation.AutomationElement]::FromHandle($refusal)) 'Close' $button) "the refusal's close button")
    Need (WaitFor { [CaptureDesk]::TitledFrom($running, $unreadable) -eq [IntPtr]::Zero } 3000) 'its close button dismissed it (G16)'
    [PasteDesk]::MovePointer(200, 150)

    # G27, G29: rewritten again, then again while that adoption's notification shows: adopted twice with one Undo
    # standing, the picture the first replaced recycled at once, and the one the second replaced once its notification
    # has gone by itself.
    $first = [OpenWithDesk]::Flat($width + 11, $height + 7, 0, 255, 255, 'png')
    $firstSha = ShaOf $first
    Rewrite $copy $first
    $null = Notification $replaced
    Need (Holding $plain "hex(original_sha256) = '$firstSha' AND pending_steps IS NULL AND adoption_undo IS NOT NULL") 'the next decodable rewrite after the refused one is adopted (G27, G35)'
    $second = [OpenWithDesk]::Flat($width + 5, $height + 3, 255, 255, 0, 'png')
    $secondSha = ShaOf $second
    Rewrite $copy $second
    Need (WaitFor { Holding $plain "hex(original_sha256) = '$secondSha' AND pending_steps IS NULL AND adoption_undo IS NOT NULL" } 10000) 'a second rewrite while that notification shows is adopted again (G27)'
    # Its notice is raised once that row is final, so its six seconds start no earlier than this.
    $shownAt = [DateTime]::UtcNow
    Check (WaitFor { (UndoButtons $replaced) -eq 1 } 3000) "one notification with one Undo stands for the item, not two: $(UndoButtons $replaced) (G27, G16)"
    Check (WaitFor { (Binned $beforeSha) -eq 1 } 5000) 'the picture the first of them replaced went to the Recycle Bin as the second took its place (G27, G29)'
    GoneByItself $replaced
    Check (WaitFor { (Holding $plain 'adoption_undo IS NULL') -and ((Binned $firstSha) -eq 1) } 10000) 'once it had gone, no Undo stands and the picture the second replaced, held in .work\ until then, is in the Recycle Bin (G27, G29)'
    Check (((Sha $adopted) -eq $secondSha) -and (@(Get-ChildItem -LiteralPath (Join-Path $stash '.work') -File -Force | Where-Object { @($beforeSha, $firstSha) -contains (Sha $_.FullName) }).Count -eq 0)) "the second rewrite stays the item's picture, and .work\ holds nothing of the pictures it replaced (G27, G29)"

    # G27, G19, F3: the editor phase's captioned screenshot, an edited item, from its preview's More options menu.
    $edited = [long][CopyReadBack]::Query($Store, "SELECT IFNULL(MAX(id), 0) FROM (SELECT id FROM items WHERE deleted = 0 AND layers <> '[]' AND original_in_originals = 1 AND finished_format = 'Png' AND $alone ORDER BY added_at DESC, id DESC LIMIT 1)")
    Need ($edited -ne 0) 'the stash holds the editor phase''s edited PNG, its name no other item''s, its file named after it'
    $name = Of $edited 'name'
    $layers = Of $edited 'layers'
    $render = Join-Path $stash (Of $edited 'finished_file')
    $renderSha = Sha $render
    $renderBytes = [IO.File]::ReadAllBytes($render)
    $original = Join-Path $originals (Of $edited 'original_file')
    $originalSha = Sha $original
    $root = Find $edited
    $null = [PasteDesk]::Press([uint16[]]@(0x09))
    Start-Sleep -Milliseconds 200
    $null = [PasteDesk]::Press([uint16[]]@(0x20))
    Need (WaitFor { ((Elements $root 'Edit' $button).Count -eq 1) -and ((Elements $root $name ([Windows.Automation.ControlType]::Text)).Count -ge 1) } 3000) "Tab, then Space, previewed '$name' (G7, G19)"
    # UI Automation reads the preview before it is drawn, and a click in that moment reaches what was drawn before it.
    Start-Sleep -Milliseconds 300
    ClickOn (One (Elements $root 'More options' $button) "the preview's More options")
    Need (WaitFor { (MenuEntries).Count -gt 0 } 3000) "the preview's More options opened its menu (G19)"
    Check ((OpenWithBetween (MenuEntries)) -and (@(MenuEntries | Where-Object { $_.Current.Name -eq 'Revert to original' }).Count -eq 1)) "the preview's menu lists Open with... between Save a copy... and Reveal in File Explorer, and Revert to original for an edited item (G19): $((MenuEntries | ForEach-Object { $_.Current.Name }) -join ', ')"
    ClickOn (One @(MenuEntries | Where-Object { $_.Current.Name -eq "Open with$ellipsis" }) "the preview menu's Open with...")
    $copy = Dialog $edited "a real click on the preview menu's Open with... on '$name'" $render

    # G27, G29, F3: the copy replaced, as a program saving through a temporary file does, by a JPEG under its .png name.
    $rewrite = [OpenWithDesk]::Flat(320, 200, 0, 128, 128, 'jpeg')
    $rewriteSha = ShaOf $rewrite
    Rewrite $copy $rewrite -Replace
    $replaced = "Replaced $([char]0x201C)$name$([char]0x201D) with the picture another program saved."
    $null = Notification $replaced
    $adopted = Join-Path $stash "$name.jpg"
    Check (Holding $edited 'pending_steps IS NULL AND rendered_revision = layer_revision AND adoption_undo IS NOT NULL') 'as soon as the notification is seen, the edited item''s adoption is complete: nothing pending, the picture placed, its Undo recorded (R9, G28)'
    Check ((Of $edited "hex(original_sha256) || '|' || original_file || '|' || finished_file || '|' || original_in_originals || '|' || layers || '|' || format") -eq "$rewriteSha|$name.jpg|$name.jpg|0|[]|Jpeg") "the JPEG is the item's original and finished picture under its name with .jpg, its layers dropped, nothing in originals\ the row's (G27, F3, F7): $(Of $edited "original_file || ' ' || layers")"
    Check ((Test-Path -LiteralPath $adopted) -and ((Sha $adopted) -eq $rewriteSha) -and -not (Test-Path -LiteralPath $render)) "the stash folder's $adopted holds the JPEG and the render $render is gone (G27, G29)"
    Check ((Test-Path -LiteralPath $original) -and ((Sha $original) -eq $originalSha)) "the previous original, $original, stays in originals\ while the notification stands (G27)"

    # G27, G29: Undo puts the previous original and its layers back, the finished picture rendered again from them.
    ClickUndo $replaced
    [PasteDesk]::MovePointer(200, 150)
    Check (WaitFor { Holding $edited "adoption_undo IS NULL AND pending_steps IS NULL AND original_in_originals = 1 AND rendered_revision = layer_revision AND hex(original_sha256) = '$originalSha'" } 15000) "Undo made the previous original the row's again, in originals\, nothing pending (G27): $(Of $edited "original_file || ' ' || original_in_originals")"
    $rendered = Join-Path $stash (Of $edited 'finished_file')
    $rerenderSha = Sha $rendered
    Check (((Of $edited 'layers') -eq $layers) -and ($null -ne $rerenderSha) -and [OpenWithDesk]::SamePixels($renderBytes, [IO.File]::ReadAllBytes($rendered))) "and its layers, the finished picture $rendered rendered again from them, pixel for pixel the render it had (G27): $(Of $edited 'layers')"
    Check ((Test-Path -LiteralPath $original) -and ((Sha $original) -eq $originalSha) -and -not (Test-Path -LiteralPath $adopted) -and ((Binned $rewriteSha) -eq 1)) "the original is in originals\ as it was, and the JPEG left the stash folder for the Recycle Bin (G27, G29)"
    Need (WaitFor { [CaptureDesk]::TitledFrom($running, $replaced) -eq [IntPtr]::Zero } 3000) 'the notification went with its Undo'

    # G27, G29: another JPEG over the copy, its notification left to go by itself; the previous original then recycled.
    $again = [OpenWithDesk]::Flat(240, 160, 128, 0, 128, 'jpeg')
    $againSha = ShaOf $again
    Rewrite $copy $again -Replace
    $null = Notification $replaced
    Check ((Of $edited "hex(original_sha256) || '|' || original_file || '|' || original_in_originals || '|' || layers") -eq "$againSha|$name.jpg|0|[]") "the edited item, Open with...'s copy rewritten again, takes the second JPEG, its layers dropped again (G27)"
    Check ((Test-Path -LiteralPath $original) -and -not (Test-Path -LiteralPath $rendered)) "the previous original stays in originals\ while the notification stands, and the render is gone (G27, G29)"
    [PasteDesk]::MovePointer(200, 150)
    GoneByItself $replaced
    Check (WaitFor { (Holding $edited 'adoption_undo IS NULL') -and -not (Test-Path -LiteralPath $original) } 10000) "once it had gone, no Undo stands and $original has left originals\ (G27)"
    Check (WaitFor { ((Binned $originalSha $original) -eq 1) -and ((Binned $renderSha) -eq 0) -and ((Binned $rerenderSha) -eq 0) } 5000) "the previous original went from originals\ to the Recycle Bin, and neither replaced render did (G27, G29)"
    Check ((Test-Path -LiteralPath $adopted) -and ((Sha $adopted) -eq $againSha)) "the adopted $adopted stays the item's picture (G27)"
    Check ((Count 'SELECT COUNT(*) FROM uses') -eq $uses) 'no Open with..., adoption or Undo recorded a use (G10)'
}
catch {
    Write-Host "FAIL the Open with... phase stopped: $($_.Exception.Message)"
    $failed++
}
finally {
    # Once the phase has asked for a dialog, one still up and a host started since that is still running are its own.
    if ($opened) {
        $left = [OpenWithDesk]::Find()
        if (($left -ne [IntPtr]::Zero) -and ($left -ne $dialogBefore)) { [OpenWithDesk]::Close($left, 0) }
        foreach ($process in @(Get-Process -Name OpenWith -ErrorAction SilentlyContinue | Where-Object { ($_.SessionId -eq $windowsSession) -and ($hosts -notcontains $_.Id) })) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
    # Whatever reached the Recycle Bin from the check's stash during the phase, the phase sent there, failed or not.
    foreach ($entry in @(Recycled | Where-Object { $binBefore -notcontains $_.Record })) { Remove-Item -LiteralPath $entry.File, $entry.Record -Force -ErrorAction SilentlyContinue }
    [PasteDesk]::MovePointer($pointer[0], $pointer[1])
}
exit $failed
