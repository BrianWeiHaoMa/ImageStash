<#
.SYNOPSIS
The Settings page on the published copy, run by check-published.ps1 inside its session after
check-published-open-with.ps1 (it uses check-published-paste.ps1's PasteDesk, check-published-screenshot.ps1's
ScreenshotDesk and stash-windows.ps1's StashWindows and check-published.ps1's CopyReadBack), over the check's own stash and settings. The
tray menu, opened with a posted right-click, lists Settings between Pause shortcuts and Quit ImageStash, and its
Settings, chosen through UI Automation, brings a window titled "ImageStash settings" to the front with G32's rows named
in order and the stash folder in use. A PowerShell of the phase's own holds Ctrl+Shift+F8: the Take a screenshot field,
clicked with real input, refuses the real Ctrl+Shift+F8 on the spot, its row reading "Register the screenshot shortcut
failed: Ctrl+Shift+F8, another program holds it", then the real Shift+A ("it would type a character") and Ctrl+V ("it
is the paste keystroke the handoff sends"), the field still reading Ctrl+PrtScn, settings.json's shortcuts unchanged,
neither Shift+A nor Ctrl+V left registered, and the real Ctrl+PrtScn still showing the screenshot's overlay after the
first refusal and after the third. With the holder gone the real Ctrl+Shift+F8 is recorded: the field
and settings.json hold it. Start with Windows switched off removes the Run value and writes false, on writes the Run
value naming the copy's executable and true; Theme set to Dark and back to Use system setting is written each time; Open
folder opens a File Explorer window on the stash folder, which the phase closes. Another PowerShell takes Ctrl+Alt+V
while the Stash clipboard image field records, so registering it again at the recording's end is refused and its row
says why; the tray menu then shows Ctrl+Shift+F8 beside Take a screenshot, and its Settings opens the page on that
field with the reason; with that holder gone, the keyboard alone records Ctrl+Alt+V there and ImageStash holds it
again. Once the page is closed, the real Ctrl+Shift+F8 shows the overlay, adding nothing, while Ctrl+PrtScn is free for
another program, whose hot key then takes the press, and shows none (G32, G33, G34, F1, F5, F10, G35, Done when 5).
Exits with the number of checks that failed.

With -Store, the stash and its settings are the check's own and every step runs; without it they are the user's, and
the phase is skipped, since it changes the user's shortcuts, Run value and theme.

Sends real keystrokes (Ctrl+Shift+F8, Shift+A, Ctrl+V, Ctrl+PrtScn, Ctrl+Alt+V, Enter, Esc), clicks the page's
shortcut fields, posts right-clicks to the tray icon, hides the stash window, starts PowerShells without a window that
hold a hot key for a moment, shows the screenshot's overlay and closes it, closes any File Explorer window on the
check's own stash folder, opens one there and closes it, changes the Run value ImageStash (check-published.ps1 puts back what it found), puts the pointer
back, and leaves the copy running, its screenshot shortcut on Ctrl+Shift+F8, for check-published.ps1 to stop.
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [string] $Store = ''
)
$ErrorActionPreference = 'Stop'
$failed = 0
$pageTitle = 'ImageStash settings'
$overlayTitle = 'ImageStash screenshot'
$settingsFile = Join-Path $env:LOCALAPPDATA 'ImageStash\settings.json'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
# The rows' controls as G32 orders them, each named by its row's title, the stash location's with its Open folder, the
# export's and the import's by their buttons' words.
$rows = "Open ImageStash | Take a screenshot | Record a clip | Stash clipboard image | Also copy it to the clipboard | Maximum length | Show the mouse pointer | Where your stash is stored | Open folder | Export stash$([char]0x2026) | Import stash$([char]0x2026) | Start with Windows | Theme"
$held = 'Register the screenshot shortcut failed: Ctrl+Shift+F8, another program holds it'
$typesACharacter = 'Register the screenshot shortcut failed: Shift+A, it would type a character'
$pasteKeystroke = 'Register the screenshot shortcut failed: Ctrl+V, it is the paste keystroke the handoff sends'
$grabbed = 'Register the Stash clipboard image shortcut failed: Ctrl+Alt+V, another program holds it'

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

# Another program holding one hot key: a PowerShell without a window that registers <Modifiers> and <Key> and logs
# "registered", or "refused" with the error, then "hotkey" for each press it takes.
$holderScript = @'
param([string] $Log, [string] $Modifiers, [string] $Key)
Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class SettingsShortcutHolder {
    [StructLayout(LayoutKind.Sequential)]
    struct Message { public IntPtr Window; public uint Id; public IntPtr W, L; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern int GetMessageW(out Message m, IntPtr window, uint a, uint b);
    public static void Run(string path, uint modifiers, uint key) {
        var log = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        log.WriteLine(RegisterHotKey(IntPtr.Zero, 1, modifiers, key) ? "registered" : "refused " + Marshal.GetLastWin32Error());
        Message m;
        while (GetMessageW(out m, IntPtr.Zero, 0, 0) > 0) { if (m.Id == 0x0312) { log.WriteLine("hotkey"); } }
    }
}
"@
[SettingsShortcutHolder]::Run($Log, [uint32]$Modifiers, [uint32]$Key)
'@

function Check([bool] $held, [string] $what) {
    if ($held) { Write-Host "ok   $what" } else { Write-Host "FAIL $what"; $script:failed++ }
}

# A window's elements can go while UI Automation reads them, and the settings file is replaced while it is read, which
# throws: the condition does not hold yet.
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

function Lines([string] $path) {
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    $stream = [IO.FileStream]::new($path, 'Open', 'Read', 'ReadWrite')
    try { $reader = [IO.StreamReader]::new($stream); @($reader.ReadToEnd() -split "`r?`n" | Where-Object { $_ -ne '' }) }
    finally { $stream.Dispose() }
}

# Another program, <name>, holding <modifiers> (MOD_NOREPEAT added) and <key>: its process, its log and the log's first line.
function Hold([string] $name, [int] $modifiers, [int] $key) {
    $log = Join-Path $work "$name.log"
    $command = '& { ' + $holderScript + ' } ' + (($log, [string]($modifiers -bor 0x4000), [string]$key | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }) -join ' ')
    $start = [Diagnostics.ProcessStartInfo]::new('powershell.exe')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.Arguments = '-NoProfile -NonInteractive -EncodedCommand ' + [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $process = [Diagnostics.Process]::Start($start)
    $script:holders += $process
    $null = WaitFor { (Lines $log).Count -gt 0 } 60000
    [pscustomobject]@{ Process = $process; Log = $log; First = (Lines $log | Select-Object -First 1) }
}

function Stop($holder) {
    if ($holder -and -not $holder.Process.HasExited) { $holder.Process.Kill(); $null = $holder.Process.WaitForExit(5000) }
}

function Presses($holder) { @(Lines $holder.Log | Where-Object { $_ -eq 'hotkey' }).Count }

# The copy's elements of <type> that UI Automation finds under <root>, named <name> when one is given.
function Elements($root, [string] $name, $type) {
    $condition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $running),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, $type))
    @($root.FindAll([Windows.Automation.TreeScope]::Descendants, $condition) | Where-Object { (-not $name) -or ($_.Current.Name -eq $name) })
}

# The one element of <found>, needed for <what>.
function One($found, [string] $what) {
    $all = @($found | Where-Object { $null -ne $_ })
    Need ($all.Count -eq 1) "$what is there once: $($all.Count) found"
    $all[0]
}

function Choose($element) { $element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }

function Page { [PasteDesk]::Titled($running, $pageTitle) }

function PageElement { [Windows.Automation.AutomationElement]::FromHandle((Page)) }

function InFront { (Page) -ne [IntPtr]::Zero -and ([PasteDesk]::Foreground() -eq (Page)) }

function Field([string] $title) {
    $found = @(Elements (PageElement) $title $button)
    if ($found.Count -ne 1) { throw "the page shows $($found.Count) fields titled $title, not one" }
    $found[0]
}

# Whether the field titled <title> records: its Stop recording a shortcut button shows.
function Recording([string] $title) { (Elements (Field $title) 'Stop recording a shortcut' $button).Count -eq 1 }

# The keys the field titled <title> shows, joined by '+', its pencil glyph left out.
function KeysOf([string] $title) { (Elements (Field $title) '' $text | ForEach-Object { $_.Current.Name } | Where-Object { -not (($_.Length -eq 1) -and ([int][char]$_ -ge 0xE000) -and ([int][char]$_ -le 0xF8FF)) }) -join '+' }

# A real click at the middle of the field titled <title>, scrolled into view, until the page is in front with it recording.
function Record([string] $title) {
    (Field $title).GetCurrentPattern([Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView()
    Start-Sleep -Milliseconds 200
    $box = (Field $title).Current.BoundingRectangle
    [PasteDesk]::Click([int]($box.X + $box.Width / 2), [int]($box.Y + $box.Height / 2))
    Need (WaitFor { (InFront) -and (Recording $title) } 3000) "a real click on the $title field put the page in front with the field recording, its Stop recording a shortcut button showing (G32)"
}

# Whether <sentence> shows, once, under the row of the field titled <title> and above the text <next> that follows it,
# inside the page's scrolled view and left of its scroll bar; whether its words fit is the desktop check's, at the
# page's smallest size.
function WarnedUnder([string] $title, [string] $next, [string] $sentence) {
    $page = PageElement
    $field = (Field $title).Current.BoundingRectangle
    $after = (Elements $page $next $text)[0].Current.BoundingRectangle
    $view = $page.FindFirst([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty, 'ScrollViewer')).Current.BoundingRectangle
    $bars = @(Elements $page '' ([Windows.Automation.ControlType]::ScrollBar))
    $right = if ($bars.Count -gt 0) { $bars[0].Current.BoundingRectangle.Left } else { $view.Right }
    @(Elements $page $sentence $text | Where-Object {
        $box = $_.Current.BoundingRectangle
        ($box.Top -ge $field.Bottom) -and ($box.Bottom -le $after.Top) -and ($box.Left -ge $view.Left) -and ($box.Right -le $right) -and ($box.Top -ge $view.Top) -and ($box.Bottom -le $view.Bottom)
    }).Count -eq 1
}

# The page's words that begin with <start>.
function WordsFrom([string] $start) { @(Elements (PageElement) '' $text | Where-Object { $_.Current.Name.StartsWith($start) }) }

function Settings { Get-Content -Raw -LiteralPath $settingsFile | ConvertFrom-Json }

function RunValue {
    $entry = Get-ItemProperty -Path $runKey -Name ImageStash -ErrorAction SilentlyContinue
    if ($entry) { $entry.ImageStash } else { $null }
}

function Overlays { [ScreenshotDesk]::Titled($running, $overlayTitle) }

function CloseOverlays { foreach ($window in Overlays) { [PasteDesk]::Close($window) } }

# The tray menu, opened with a posted right-click: its entries, top to bottom.
function TrayMenu {
    Check ([StashWindows]::RightClickTrayIcon($running)) 'a right-click posted to the tray icon'
    $null = WaitFor { (Elements $desktop 'Quit ImageStash' $menuItem).Count -gt 0 } 3000
    @(Elements $desktop '' $menuItem | Sort-Object { $_.Current.BoundingRectangle.Y })
}

# <choice> picked in the list titled <list> as a click on it would: the list opened, the item selected, the list closed.
function Pick([string] $list, [string] $choice) {
    $box = One (Elements (PageElement) $list $comboBox) "the $list list"
    $box.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $null = WaitFor { (Elements $desktop $choice $listItem).Count -eq 1 } 3000
    (One (Elements $desktop $choice $listItem) "$choice in the $list list").GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $null = WaitFor { $box.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Current.ExpandCollapseState -eq 'Collapsed' } 1000
    if ($box.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Current.ExpandCollapseState -ne 'Collapsed') {
        $box.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    }
    $box
}

# File Explorer's windows on <folder>.
function ExplorerOn([string] $folder) {
    foreach ($window in @((New-Object -ComObject Shell.Application).Windows())) {
        try { if ($window.Document.Folder.Self.Path -eq $folder) { $window } } catch { }
    }
}

if (-not $Store) {
    Write-Host "skipped: the stash and settings are the user's, under $(Join-Path $env:LOCALAPPDATA 'ImageStash'), so the Settings phase, which changes the shortcuts, the Run value and the theme there, does not run"
    exit 0
}
$copies = @(Get-Process -Name ImageStash.Windows -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe })
if ($copies.Count -ne 1) {
    Write-Host "FAIL the Settings phase found $($copies.Count) running copies of $Exe, not one"
    exit 1
}
$running = $copies[0].Id
$stash = Split-Path $Store -Parent
$desktop = [Windows.Automation.AutomationElement]::RootElement
$button = [Windows.Automation.ControlType]::Button
$text = [Windows.Automation.ControlType]::Text
$menuItem = [Windows.Automation.ControlType]::MenuItem
$comboBox = [Windows.Automation.ControlType]::ComboBox
$listItem = [Windows.Automation.ControlType]::ListItem
$displays = [ScreenshotDesk]::Displays()
$items = [int][CopyReadBack]::Query($Store, 'SELECT COUNT(*) FROM items')
$pointer = [PasteDesk]::Pointer()
$work = Join-Path $env:TEMP ('ImageStash-settings-phase-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $work
$holders = @()
try {
    # An Explorer window an earlier step left on the check's own stash folder, closed so the one Open folder opens is told apart.
    foreach ($window in @(ExplorerOn $stash)) { $window.Quit() }
    Need (WaitFor { @(ExplorerOn $stash).Count -eq 0 } 5000) "no File Explorer window shows $stash"
    # Out of the way of the page's fields: Close hides the stash window (G2).
    foreach ($window in @([ScreenshotDesk]::Titled($running, 'ImageStash'))) { [PasteDesk]::Close($window) }
    Need (WaitFor { @([ScreenshotDesk]::Titled($running, 'ImageStash')).Count -eq 0 } 3000) 'the stash window hidden'

    # G34, G32, F4: Settings from the tray, the page with its rows.
    $entries = TrayMenu
    $names = ($entries | ForEach-Object { $_.Current.Name }) -join ' | '
    Check ($names -eq 'Open ImageStash | Take a screenshot | Record a clip | Stash clipboard image | Pause shortcuts | Settings | Quit ImageStash') "the tray menu lists Settings between Pause shortcuts and Quit ImageStash (G34): $names"
    $shown = ($entries | Where-Object { $_.Current.Name -eq 'Take a screenshot' } | ForEach-Object { $_.Current.AcceleratorKey }) -join ','
    Check ($shown -eq 'Ctrl+PrintScreen') "its Take a screenshot shows Ctrl+PrtScn (G34): '$shown'"
    Choose (One ($entries | Where-Object { $_.Current.Name -eq 'Settings' }) "the tray menu's Settings")
    Need (WaitFor { InFront } 5000) "the tray's Settings brought a window titled '$pageTitle' to the front (G32, F4)"
    $named = (@((PageElement).FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)) |
        Where-Object { ($_.Current.ControlType -ne $text) -and ($rows.Split('|').Trim() -contains $_.Current.Name) } | ForEach-Object { $_.Current.Name }) -join ' | '
    Check ($named -eq $rows) "the page's controls carry G32's rows in order (G32): $named"
    $folder = (One (Elements (PageElement) 'Where your stash is stored' ([Windows.Automation.ControlType]::Edit)) 'the stash location').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
    Check ($folder -eq $stash) "Where your stash is stored shows the stash folder in use (G32): $folder"
    Check ((KeysOf 'Take a screenshot') -eq 'Ctrl+PrtScn') "the Take a screenshot field reads Ctrl+PrtScn (F5): $(KeysOf 'Take a screenshot')"

    # G32, G33, Done when 5: a combination another program holds, refused on the spot; the old still registered.
    $shortcuts = (Settings).shortcuts | ConvertTo-Json -Compress
    $holder = Hold 'holder' 0x0006 0x77
    Need ($holder.First -eq 'registered') "another program registered Ctrl+Shift+F8 first: $($holder.First)"
    Record 'Take a screenshot'
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x10, 0x77))
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $warned = WaitFor { WarnedUnder 'Take a screenshot' 'Record a clip' $held } 1000
    Check $warned ("within a second ({0} ms) the row under the field reads '$held', in the page's view (G32, G35, Done when 5)" -f $clock.ElapsedMilliseconds)
    Check (WaitFor { (-not $holder.Process.HasExited) -and ((Presses $holder) -eq 1) } 1000) "the other program still held Ctrl+Shift+F8 and took the press: $(Presses $holder) press(es)"
    Need (WaitFor { -not (Recording 'Take a screenshot') } 2000) 'the refusal ended the recording'
    Check ((KeysOf 'Take a screenshot') -eq 'Ctrl+PrtScn') "the field still reads Ctrl+PrtScn (G32): $(KeysOf 'Take a screenshot')"
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x2C))
    $clock = [Diagnostics.Stopwatch]::StartNew()
    Check (WaitFor { (Overlays).Count -eq $displays } 3000) ("the real Ctrl+PrtScn still shows the screenshot's overlay, its registration kept (G32, G33): {0} ms" -f $clock.ElapsedMilliseconds)
    Start-Sleep -Milliseconds 300
    $null = [PasteDesk]::Press([uint16[]]@(0x1B))
    if (-not (WaitFor { (Overlays).Count -eq 0 } 2000)) { CloseOverlays }
    Need (WaitFor { (Overlays).Count -eq 0 } 2000) 'the overlay closed, cancelled'

    # G32: a combination that types a character, and the paste keystroke the handoff sends.
    Record 'Take a screenshot'
    $null = [PasteDesk]::Press([uint16[]]@(0x10, 0x41))
    Check (WaitFor { WarnedUnder 'Take a screenshot' 'Record a clip' $typesACharacter } 1000) "Shift+A refused on the spot, the row reading '$typesACharacter' (G32, G35)"
    Need (WaitFor { -not (Recording 'Take a screenshot') } 2000) 'the refusal ended the recording'
    Record 'Take a screenshot'
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x56))
    Check (WaitFor { WarnedUnder 'Take a screenshot' 'Record a clip' $pasteKeystroke } 1000) "Ctrl+V refused on the spot, the row reading '$pasteKeystroke' (G32, G8, G35)"
    Need (WaitFor { -not (Recording 'Take a screenshot') } 2000) 'the refusal ended the recording'
    Check ((KeysOf 'Take a screenshot') -eq 'Ctrl+PrtScn') "the field still reads Ctrl+PrtScn: $(KeysOf 'Take a screenshot')"
    Start-Sleep -Milliseconds 1000
    Check (((Settings).shortcuts | ConvertTo-Json -Compress) -eq $shortcuts) "settings.json's shortcuts are as they were before the three refusals (G32): $shortcuts"
    $typing = Hold 'typing' 0x0004 0x41
    $paste = Hold 'paste' 0x0002 0x56
    Check (($typing.First -eq 'registered') -and ($paste.First -eq 'registered')) "ImageStash left neither Shift+A nor Ctrl+V registered: another program registered each ($($typing.First), $($paste.First)) (G32, G8)"
    Stop $typing
    Stop $paste
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x2C))
    Check (WaitFor { (Overlays).Count -eq $displays } 3000) 'after the three refusals the real Ctrl+PrtScn still shows the overlay (G32, G33)'
    Start-Sleep -Milliseconds 300
    $null = [PasteDesk]::Press([uint16[]]@(0x1B))
    if (-not (WaitFor { (Overlays).Count -eq 0 } 2000)) { CloseOverlays }
    Need (WaitFor { (Overlays).Count -eq 0 } 2000) 'the overlay closed, cancelled'

    # G32, G33, F5: with the holder gone, the combination recorded, registered and written.
    Stop $holder
    Record 'Take a screenshot'
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x10, 0x77))
    Check (WaitFor { ((KeysOf 'Take a screenshot') -eq 'Ctrl+Shift+F8') -and ((WordsFrom 'Register the screenshot shortcut failed').Count -eq 0) } 1000) "recorded once another program let it go, the field reads Ctrl+Shift+F8 and its row no refusal (G32, F5): $(KeysOf 'Take a screenshot')"
    Check (WaitFor { (Settings).shortcuts.screenshot -eq 'Ctrl+Shift+F8' } 1500) "settings.json holds the screenshot shortcut Ctrl+Shift+F8 (G32, State Settings): $((Settings).shortcuts.screenshot)"

    # F1, G32, State Settings: Start with Windows takes effect at once, in the Run value and the file.
    $toggle = (One (Elements (PageElement) 'Start with Windows' $button) 'the Start with Windows switch').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    Need (($toggle.Current.ToggleState -eq 'On') -and ((RunValue) -eq ('"' + $Exe + '"'))) "Start with Windows is on and the Run value names the copy: $(RunValue)"
    $toggle.Toggle()
    Check (WaitFor { $null -eq (RunValue) } 1000) 'Start with Windows switched off removed the Run value ImageStash within a second (F1, G32)'
    Check (WaitFor { (Settings).startWithWindows -eq $false } 1500) 'and settings.json holds startWithWindows false'
    $toggle.Toggle()
    Check (WaitFor { (RunValue) -eq ('"' + $Exe + '"') } 1000) "switched on, the Run value ImageStash names the copy's executable again within a second (F1, G32): $(RunValue)"
    Check (WaitFor { (Settings).startWithWindows -eq $true } 1500) 'and settings.json holds startWithWindows true'

    # F10, G32: Theme written at once; its look is the person's.
    $theme = Pick 'Theme' 'Dark'
    Check (WaitFor { (Settings).theme -eq 'dark' } 1500) "Theme set to Dark: the list reads $($theme.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value) and settings.json holds theme dark (F10, G32)"
    $theme = Pick 'Theme' 'Use system setting'
    Check (WaitFor { (Settings).theme -eq 'system' } 1500) 'set back to Use system setting, settings.json holds theme system (F10, G32)'

    # G32: Open folder opens the stash folder in File Explorer.
    Choose (One (Elements (PageElement) 'Open folder' $button) 'Open folder')
    Check (WaitFor { @(ExplorerOn $stash).Count -eq 1 } 10000) "Open folder opened a File Explorer window on $stash (G32): $(@(ExplorerOn $stash | ForEach-Object { $_.LocationName }) -join ', ')"
    foreach ($window in @(ExplorerOn $stash)) { $window.Quit() }

    # G33: another program takes Ctrl+Alt+V while the field records, so registering it again at the recording's end is
    # refused; its row says why, and the tray's Settings then opens on that field.
    Record 'Stash clipboard image'
    $grab = Hold 'grab' 0x0003 0x56
    Need ($grab.First -eq 'registered') "while the field records ImageStash's shortcuts are released, and another program registered Ctrl+Alt+V: $($grab.First)"
    Need (Recording 'Stash clipboard image') 'the field still records'
    $null = [PasteDesk]::Press([uint16[]]@(0x1B))
    Check (WaitFor { (-not (Recording 'Stash clipboard image')) -and (WarnedUnder 'Stash clipboard image' 'After a screenshot or a recording' $grabbed) } 2000) "Esc ended the recording alone, the page staying, and the row reads '$grabbed' (G33, G35, G2)"
    $null = [PasteDesk]::Press([uint16[]]@(0x1B))
    Need (WaitFor { (Page) -eq [IntPtr]::Zero } 2000) 'a second Esc closed the page'
    $entries = TrayMenu
    $shown = ($entries | Where-Object { $_.Current.Name -eq 'Take a screenshot' } | ForEach-Object { $_.Current.AcceleratorKey }) -join ','
    Check ($shown -eq 'Ctrl+Shift+F8') "the tray menu's Take a screenshot shows the recorded Ctrl+Shift+F8 (G34): '$shown'"
    Choose (One ($entries | Where-Object { $_.Current.Name -eq 'Settings' }) "the tray menu's Settings")
    $onRow = WaitFor { (InFront) -and ([Windows.Automation.AutomationElement]::FocusedElement.Current.Name -eq 'Stash clipboard image') -and ([Windows.Automation.AutomationElement]::FocusedElement.Current.ProcessId -eq $running) } 3000
    Check ($onRow -and (WarnedUnder 'Stash clipboard image' 'After a screenshot or a recording' $grabbed)) "Settings opened on the unavailable shortcut's field, its row saying why (G33)"
    Stop $grab
    $null = [PasteDesk]::Press([uint16[]]@(0x0D))
    Need (WaitFor { Recording 'Stash clipboard image' } 2000) 'Enter on the focused field began recording'
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x12, 0x56))
    Check (WaitFor { ((WordsFrom 'Register the Stash clipboard image shortcut failed').Count -eq 0) -and ((KeysOf 'Stash clipboard image') -eq 'Ctrl+Alt+V') } 1000) 'changed there with the keyboard alone to Ctrl+Alt+V once the other program let it go, the row no longer names it (G33, G7)'
    $taken = Hold 'taken' 0x0003 0x56
    Check ($taken.First -eq 'refused 1409') "ImageStash holds Ctrl+Alt+V again, so another program's registration is refused: $($taken.First)"
    Stop $taken

    # G32, G33, F5: the page closed, the recorded shortcut takes a screenshot and the old one is free.
    $null = [PasteDesk]::Press([uint16[]]@(0x1B))
    Need (WaitFor { (Page) -eq [IntPtr]::Zero } 2000) 'Esc closed the page'
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x10, 0x77))
    $clock = [Diagnostics.Stopwatch]::StartNew()
    Check (WaitFor { (Overlays).Count -eq $displays } 3000) ("the real Ctrl+Shift+F8 showed the screenshot's overlay (G32, F5): {0} ms" -f $clock.ElapsedMilliseconds)
    Start-Sleep -Milliseconds 300
    $null = [PasteDesk]::Press([uint16[]]@(0x1B))
    if (-not (WaitFor { (Overlays).Count -eq 0 } 2000)) { CloseOverlays }
    Need (WaitFor { (Overlays).Count -eq 0 } 2000) 'the overlay closed, cancelled'
    Check ([int][CopyReadBack]::Query($Store, 'SELECT COUNT(*) FROM items') -eq $items) "the cancelled screenshot added nothing to the stash of $items"
    $old = Hold 'old' 0x0002 0x2C
    Check ($old.First -eq 'registered') "ImageStash let Ctrl+PrtScn go: another program registered it: $($old.First)"
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x2C))
    $reached = WaitFor { (Presses $old) -eq 1 } 2000
    Check ($reached -and -not (WaitFor { (Overlays).Count -gt 0 } 2000)) "the real Ctrl+PrtScn reached that program and showed no overlay within two seconds (G32, F5): $(Presses $old) press(es)"
    Stop $old
}
catch {
    Write-Host "FAIL the Settings phase stopped: $($_.Exception.Message)"
    $failed++
}
finally {
    # Each step on its own, so one that throws (a window gone in the meantime) leaves the others done.
    foreach ($process in $holders) { try { if (-not $process.HasExited) { $process.Kill(); $null = $process.WaitForExit(5000) } } catch { } }
    try { CloseOverlays } catch { }
    try { if ((Page) -ne [IntPtr]::Zero) { [PasteDesk]::Close((Page)) } } catch { }
    try { foreach ($window in @(ExplorerOn $stash)) { $window.Quit() } } catch { }
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    [PasteDesk]::MovePointer($pointer[0], $pointer[1])
}
exit $failed
