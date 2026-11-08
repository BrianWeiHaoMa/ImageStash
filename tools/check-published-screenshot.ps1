<#
.SYNOPSIS
Take a screenshot's three ways in on the published copy, run by check-published.ps1 inside its session after
check-published-paste-in.ps1 (it uses check-published-paste.ps1's PasteDesk and stash-windows.ps1's StashWindows and check-published.ps1's
CopyReadBack): the real Ctrl+PrtScn shows the screenshot's overlay over every display, in front, within a second, and
a real Esc closes it; the tray menu, opened with a posted right-click, lists the entries G34 orders that are built, and
its Take a screenshot, chosen through UI Automation, shows the overlay; the flyout a posted tray click shows, its +
menu's Take a screenshot chosen the same way, is hidden while the overlay is up and shows again once it closes. Each
capture is cancelled, so it adds nothing (read from the store with -Store). Exits with the number of checks that
failed.

An overlay shown without real input is closed with WM_CLOSE, which cancels as Esc does: Windows does not bring a
window to the front for a choice made through UI Automation, so no real Esc would reach it.

Sends real keystrokes (Ctrl+PrtScn, Esc), posts clicks to the tray icon, shows the overlay over every display for a
moment at a time, and leaves the copy running for check-published.ps1 to stop.
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [string] $Store = ''
)
$ErrorActionPreference = 'Stop'
$failed = 0
$overlayTitle = 'ImageStash screenshot'
$ellipsis = [char]0x2026

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

// The copy's visible top-level windows by their title, and the number of displays.
public static class ScreenshotDesk
{
    private delegate bool EnumProc(IntPtr window, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    /// <summary>The process's visible windows titled exactly <paramref name="title"/>, minimised ones left out.</summary>
    public static List<IntPtr> Titled(int processId, string title)
    {
        var found = new List<IntPtr>();
        EnumWindows(delegate (IntPtr window, IntPtr data)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            var text = new StringBuilder(256);
            GetWindowTextW(window, text, text.Capacity);
            if (owner == processId && IsWindowVisible(window) && !IsIconic(window) && text.ToString() == title) { found.Add(window); }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>SM_CMONITORS: the displays on the desktop.</summary>
    public static int Displays() { return GetSystemMetrics(80); }
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

function Overlays { [ScreenshotDesk]::Titled($running, $overlayTitle) }

function CloseOverlays { foreach ($window in Overlays) { [PasteDesk]::Close($window) } }

# A cancelled capture adds nothing (G14, E3); over the user's stash the store is not read.
function NothingAdded([string] $how) {
    if ($Store) {
        $now = [int][CopyReadBack]::Query($Store, 'SELECT COUNT(*) FROM items')
        Check ($now -eq $items) "$how added nothing to the stash of $items (G14, E3): $now"
    }
}

$copies = @(Get-Process -Name ImageStash.Windows -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe })
if ($copies.Count -ne 1) {
    Write-Host "FAIL the screenshot phase found $($copies.Count) running copies of $Exe, not one"
    exit 1
}
$running = $copies[0].Id
$displays = [ScreenshotDesk]::Displays()
$items = if ($Store) { [int][CopyReadBack]::Query($Store, 'SELECT COUNT(*) FROM items') } else { 0 }
$desktop = [Windows.Automation.AutomationElement]::RootElement
$menuItem = [Windows.Automation.ControlType]::MenuItem
try {
    # F5, G14: the real shortcut, registered at the copy's start, reaches the act.
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x2C))
    $shown = WaitFor { (Overlays).Count -eq $displays } 3000
    $took = $clock.ElapsedMilliseconds
    Check ($shown -and ($took -le 1000) -and ((Overlays) -contains [PasteDesk]::Foreground())) "the real Ctrl+PrtScn showed the screenshot's overlay over all $displays display(s), in front, within a second (F5, G14): $took ms, $((Overlays).Count) shown"
    # A person's first key comes a moment after the overlay, not the instant it is in front.
    Start-Sleep -Milliseconds 300
    $null = [PasteDesk]::Press([uint16[]]@(0x1B))
    Check (WaitFor { (Overlays).Count -eq 0 } 2000) 'a real Esc closed it, and no overlay is left (G14, F4)'
    NothingAdded 'that capture'

    # G34: the tray menu, in G34's order of what is built, and its Take a screenshot.
    Check ([StashWindows]::RightClickTrayIcon($running)) 'a right-click posted to the tray icon'
    $null = WaitFor { (Elements $desktop 'Quit ImageStash' $menuItem).Count -gt 0 } 3000
    $entries = (Elements $desktop '' $menuItem | ForEach-Object { $_.Current.Name }) -join ' | '
    Check ($entries -eq 'Open ImageStash | Take a screenshot | Record a clip | Stash clipboard image | Pause shortcuts | Settings | Quit ImageStash') "the tray menu lists Open ImageStash, Take a screenshot, Record a clip, Stash clipboard image, Pause shortcuts, Settings and Quit ImageStash (G34): $entries"
    $entry = @(Elements $desktop 'Take a screenshot' $menuItem)
    if ($entry.Count -eq 1) {
        Choose $entry[0]
        Check (WaitFor { (Overlays).Count -eq $displays } 3000) "the tray's Take a screenshot showed the overlay over all $displays display(s) (G34, G14)"
        CloseOverlays
        Check (WaitFor { (Overlays).Count -eq 0 } 2000) 'closed, the overlay cancelled and none is left (F4)'
        NothingAdded 'that capture'
    }
    else {
        Check $false "the tray menu holds one Take a screenshot to choose: $($entry.Count)"
    }

    # G2, G3: the flyout's + menu, which hides the flyout like any other start and shows it again at the end.
    # Waited for by its title: a notification still up counts as a showing window to ClickTrayIcon.
    $null = [StashWindows]::ClickTrayIcon($running, 5000)
    $null = WaitFor { @([ScreenshotDesk]::Titled($running, 'ImageStash')).Count -eq 1 } 3000
    $flyouts = @([ScreenshotDesk]::Titled($running, 'ImageStash'))
    Check ($flyouts.Count -eq 1) "a posted click on the tray icon showed the stash window: $($flyouts.Count) shown"
    if ($flyouts.Count -eq 1) {
        $flyout = $flyouts[0]
        $add = @(Elements ([Windows.Automation.AutomationElement]::FromHandle($flyout)) 'Add to your stash' ([Windows.Automation.ControlType]::Button))
        Check ($add.Count -eq 1) 'the window holds its + button, Add to your stash'
        if ($add.Count -eq 1) {
            Choose $add[0]
            $null = WaitFor { (Elements $desktop 'Take a screenshot' $menuItem).Count -gt 0 } 3000
            $entries = (Elements $desktop '' $menuItem | ForEach-Object { $_.Current.Name }) -join ' | '
            Check ($entries -eq "Import files$ellipsis | Paste image | Take a screenshot | Record a clip") "the + menu lists Import files$ellipsis, Paste image, Take a screenshot and Record a clip (G2): $entries"
            $entry = @(Elements $desktop 'Take a screenshot' $menuItem)
            if ($entry.Count -eq 1) {
                Choose $entry[0]
                $shown = WaitFor { (Overlays).Count -eq $displays } 3000
                Check ($shown -and ([ScreenshotDesk]::Titled($running, 'ImageStash').Count -eq 0)) "the + menu's Take a screenshot showed the overlay with the flyout hidden (G3, G14)"
                CloseOverlays
                Check (WaitFor { ((Overlays).Count -eq 0) -and ([ScreenshotDesk]::Titled($running, 'ImageStash') -contains $flyout) } 2000) 'once the overlay closed, the flyout shows again (G3)'
                NothingAdded 'that capture'
            }
        }
    }
}
catch {
    Write-Host "FAIL the screenshot phase stopped: $($_.Exception.Message)"
    $failed++
}
finally {
    CloseOverlays
}
exit $failed
