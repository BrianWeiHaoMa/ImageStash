<#
.SYNOPSIS
The paste into the stash on the published copy, run by check-published.ps1 inside its session after
check-published-paste.ps1 (it uses that script's PasteDesk and stash-windows.ps1's StashWindows and check-published.ps1's CopyReadBack): a
picture placed on the clipboard as a browser's Copy image places it, "PNG" and then CF_DIBV5, and no file list; the copy
started afresh over the check's own stash, so the real Ctrl+Alt+Space is its own again; the flyout the shortcut
summons, two real letters typed into its search box and the real Ctrl+V: the stash root then holds one new
"Screenshot YYYY-MM-DD HH.MM.SS.png" at the paste's local time, the clipboard's PNG as it was, which the store lists; the
notice says so with the still-only sentence; the search box is empty again; and a real Ctrl+C copies that item, so it
is the one selected. Exits with the number of checks that failed.

With -Store, the stash is the check's own and the phase runs; without it the stash is the user's, and the phase is
skipped, since the paste adds an item to it.

Replaces the clipboard, restarts the copy once, sends real keystrokes (the shortcut, two letters, Ctrl+V, Ctrl+C), and
leaves the restarted copy running for check-published.ps1 to stop.
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [string] $Store = ''
)
$ErrorActionPreference = 'Stop'
$failed = 0
$dot = [char]0xB7
$copiedTitle = "Copied $dot press Ctrl+V"
$stillOnly = 'A pasted picture is always a still; to add an animated one, drag its file in.'

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

# The names UI Automation gives the elements of <window> whose name matches <like>, joined by ' | '.
function Named([IntPtr] $window, [string] $like) {
    $root = [Windows.Automation.AutomationElement]::FromHandle($window)
    $named = $root.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) |
        ForEach-Object { $_.Current.Name } | Where-Object { $_ -like $like }
    @($named) -join ' | '
}

# The text in the flyout's search box, read through UI Automation.
function SearchText([IntPtr] $window) {
    $root = [Windows.Automation.AutomationElement]::FromHandle($window)
    $box = $root.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Search your stash'))
    if ($null -eq $box) { return $null }
    $box.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
}

if (-not $Store) {
    Write-Host "skipped: the stash is the user's, so the paste into it, which adds an item there, does not run"
    exit 0
}

Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;

// The clipboard as a browser's Copy image leaves it: "PNG", then a 32-bit CF_DIBV5 of the same pixels, its alpha declared
// by its mask, straight; kept out of the clipboard history.
public static class PasteInClipboard
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string name);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);

    /// <summary>A BITMAPV5HEADER (124 bytes, 32 bpp, BI_BITFIELDS, masks BGRA, LCS_sRGB) and the rows bottom-up.</summary>
    public static byte[] DibV5(int width, int height, byte[] topDownBgra)
    {
        var dib = new byte[124 + topDownBgra.Length];
        BitConverter.GetBytes(124).CopyTo(dib, 0);
        BitConverter.GetBytes(width).CopyTo(dib, 4);
        BitConverter.GetBytes(height).CopyTo(dib, 8);
        BitConverter.GetBytes((short)1).CopyTo(dib, 12);
        BitConverter.GetBytes((short)32).CopyTo(dib, 14);
        BitConverter.GetBytes(3).CopyTo(dib, 16);
        BitConverter.GetBytes(topDownBgra.Length).CopyTo(dib, 20);
        BitConverter.GetBytes(0x00FF0000).CopyTo(dib, 40);
        BitConverter.GetBytes(0x0000FF00).CopyTo(dib, 44);
        BitConverter.GetBytes(0x000000FF).CopyTo(dib, 48);
        BitConverter.GetBytes(unchecked((int)0xFF000000)).CopyTo(dib, 52);
        BitConverter.GetBytes(0x73524742).CopyTo(dib, 56);
        BitConverter.GetBytes(4).CopyTo(dib, 108);
        for (var row = 0; row < height; row++)
        {
            Array.Copy(topDownBgra, row * width * 4, dib, 124 + (height - 1 - row) * width * 4, width * 4);
        }
        return dib;
    }

    public static bool Put(byte[] png, byte[] dibV5)
    {
        for (var attempt = 0; !OpenClipboard(IntPtr.Zero); attempt++)
        {
            if (attempt == 100) { return false; }
            Thread.Sleep(50);
        }
        try
        {
            return EmptyClipboard()
                && SetClipboardData(RegisterClipboardFormatW("PNG"), Global(png)) != IntPtr.Zero
                && SetClipboardData(17, Global(dibV5)) != IntPtr.Zero
                && SetClipboardData(RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing"), Global(new byte[4])) != IntPtr.Zero;
        }
        finally { CloseClipboard(); }
    }

    private static IntPtr Global(byte[] bytes)
    {
        var memory = GlobalAlloc(0x0002, (UIntPtr)bytes.Length);
        Marshal.Copy(bytes, 0, GlobalLock(memory), bytes.Length);
        GlobalUnlock(memory);
        return memory;
    }
}
'@

# A 6 by 4 picture whose top-left pixel is transparent and every other one opaque, each its own colour.
$width = 6
$height = 4
$pixels = New-Object byte[] ($width * $height * 4)
for ($at = 0; $at -lt $width * $height; $at++) {
    $pixels[$at * 4] = 40 * ($at % $width)
    $pixels[$at * 4 + 1] = 90
    $pixels[$at * 4 + 2] = 60 * [math]::Floor($at / $width)
    $pixels[$at * 4 + 3] = if ($at -eq 0) { 0 } else { 255 }
}
$bitmap = New-Object Drawing.Bitmap $width, $height, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
for ($at = 0; $at -lt $width * $height; $at++) {
    $bitmap.SetPixel($at % $width, [math]::Floor($at / $width), [Drawing.Color]::FromArgb($pixels[$at * 4 + 3], $pixels[$at * 4 + 2], $pixels[$at * 4 + 1], $pixels[$at * 4]))
}
$stream = New-Object IO.MemoryStream
$bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
$png = $stream.ToArray()

$stash = Split-Path $Store -Parent
$hotKey = [uint16[]]@(0x11, 0x12, 0x20)
try {
    # Started while another program held the shortcut, the running copy left it unregistered (G33): start it afresh.
    foreach ($process in @(Get-Process -Name ImageStash.Windows -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe })) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
    $copy = Start-Process -FilePath $Exe -PassThru
    # The stash opens at start, before a summon reads it (State: Every change); 5,000 rows take a moment.
    Start-Sleep -Seconds 3
    $items = [int][CopyReadBack]::Query($Store, 'SELECT COUNT(*) FROM items')
    $root = @(Get-ChildItem -Path $stash -File -Force | ForEach-Object { $_.Name })

    Check ([PasteInClipboard]::Put($png, [PasteInClipboard]::DibV5($width, $height, $pixels))) 'a picture on the clipboard as a browser''s Copy image places it: "PNG", then CF_DIBV5'
    Check ([CopyReadBack]::Clipboard() -eq 'files= png=True dibv5=True effect=-1') "the clipboard holds that picture and no file list (G13): $([CopyReadBack]::Clipboard())"

    $null = [PasteDesk]::Press($hotKey)
    $shown = WaitFor { [PasteDesk]::Flyout($copy.Id, $copiedTitle) -ne [IntPtr]::Zero } 3000
    $flyout = [PasteDesk]::Flyout($copy.Id, $copiedTitle)
    $focused = [Windows.Automation.AutomationElement]::FocusedElement
    $ready = $shown -and ([PasteDesk]::ProcessOf([PasteDesk]::Foreground()) -eq $copy.Id) -and ($focused.Current.ProcessId -eq $copy.Id) -and
        ($focused.Current.ControlType -eq [Windows.Automation.ControlType]::Edit)
    Check $ready "the open shortcut showed the flyout in front with its search box focused (G1, G7): in front $([PasteDesk]::Foreground()), focused a $($focused.Current.ControlType.ProgrammaticName) of process $($focused.Current.ProcessId)"

    if ($ready) {
        # G12, G13, F12, F3, G7: two letters typed, so the paste's import has a search to clear, then Ctrl+V.
        $null = [PasteDesk]::Press([uint16[]]@(0x53))
        $null = [PasteDesk]::Press([uint16[]]@(0x48))
        Check (WaitFor { (SearchText $flyout) -eq 'sh' } 2000) "the two letters are in the search box: '$(SearchText $flyout)'"
        $before = Get-Date
        $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x56))
        $added = WaitFor { [int][CopyReadBack]::Query($Store, 'SELECT COUNT(*) FROM items') -eq $items + 1 } 10000
        $after = Get-Date
        Check $added "Ctrl+V added one item to the stash of $items (G13)"
        $name = [CopyReadBack]::Query($Store, 'SELECT name FROM items ORDER BY added_at DESC, id DESC LIMIT 1')
        $file = [CopyReadBack]::Query($Store, 'SELECT finished_file FROM items ORDER BY added_at DESC, id DESC LIMIT 1')
        $format = [CopyReadBack]::Query($Store, 'SELECT format FROM items ORDER BY added_at DESC, id DESC LIMIT 1')
        $named = @($before.AddSeconds(-1), $before, $after) | ForEach-Object { 'Screenshot ' + $_.ToString('yyyy-MM-dd HH.mm.ss') }
        Check (($named -contains $name) -and ($file -eq "$name.png") -and ($format -eq 'png')) "it is a PNG named like a screenshot at the paste's local time, its file named for it (F12, F3): '$name', '$file', $format, pasted between $($before.ToString('HH:mm:ss.fff')) and $($after.ToString('HH:mm:ss.fff'))"
        $path = Join-Path $stash $file
        $newInRoot = @(Get-ChildItem -Path $stash -File -Force | ForEach-Object { $_.Name } | Where-Object { $root -notcontains $_ })
        Check ((($newInRoot -join '|') -eq $file) -and (Test-Path $path)) "the stash root holds that one new file and nothing else new (R2): $($newInRoot -join ', ')"
        $stored = if (Test-Path $path) { [IO.File]::ReadAllBytes($path) } else { @() }
        Check ([Convert]::ToBase64String($png) -eq [Convert]::ToBase64String($stored)) "the file is the clipboard's PNG as it was ($($png.Length) bytes, stored $($stored.Length))"
        $decoded = if (Test-Path $path) { [Drawing.Bitmap]::new([IO.MemoryStream]::new($stored)) } else { $null }
        Check (($null -ne $decoded) -and ($decoded.Width -eq $width) -and ($decoded.Height -eq $height) -and ($decoded.GetPixel(0, 0).A -eq 0) -and ($decoded.GetPixel(1, 0).A -eq 255)) "it decodes as the copied $width by $height picture with its transparent corner (G13)"
        if ($decoded) { $decoded.Dispose() }
        Check (@(Get-ChildItem -Path (Join-Path $stash '.work') -Force -ErrorAction SilentlyContinue).Count -eq 0) "$stash\.work\ holds nothing of the finished paste: its file was renamed into place and its step cleared (State, R2)"
        $notice = Named $flyout 'Added 1 to your stash:*'
        Check ($notice -eq "Added 1 to your stash: $name. $stillOnly") "the notice says it was added and that an animated picture is added by dragging its file in (G13): '$notice'"
        Check ((SearchText $flyout) -eq '') "the paste's import cleared the search (G12): '$(SearchText $flyout)'"
        # Recent lists only the few items used so far; All lists every item, the pasted one newest-added and so first (G4).
        $count = Named $flyout '* items*'
        Check ($count -eq "$($items + 1) items") "the window shows All, every item counted, not Recent or the search's matches (G12): '$count'"

        # G12, G9: the pasted item is the one selected, so Ctrl+C copies it, and the window stays.
        Start-Sleep -Milliseconds 300
        $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x43))
        $copied = WaitFor { [CopyReadBack]::Clipboard() -eq "files=$path png=True dibv5=True effect=1" } 3000
        Check $copied "Ctrl+C then copied the pasted item, so it is the one selected (G12, G9): $([CopyReadBack]::Clipboard())"
        Check ([PasteDesk]::Flyout($copy.Id, $copiedTitle) -eq $flyout) 'the flyout still shows after the paste and the copy'
    }
    else {
        Write-Host 'skipped: the flyout was not in front with its search box focused, so no real keystroke would reach it'
    }

    $null = [PasteDesk]::Press($hotKey)
    Check (WaitFor { [PasteDesk]::Flyout($copy.Id, $copiedTitle) -eq [IntPtr]::Zero } 2000) 'the shortcut hid the flyout (F5, G2)'
}
catch {
    Write-Host "FAIL the paste-in phase stopped: $($_.Exception.Message)"
    $failed++
}
exit $failed
