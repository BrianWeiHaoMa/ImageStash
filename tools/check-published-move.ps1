<#
.SYNOPSIS
Move... on the published copy, run by check-published.ps1 inside its session after check-published-settings.ps1 (it
uses the PasteDesk, PasteInClipboard, ScreenshotDesk, CaptureDesk and OpenWithDesk of the paste, paste-in,
screenshot, capture and Open with... phases and stash-windows.ps1's StashWindows and check-published.ps1's CopyReadBack), over the check's
own stash and settings. The Settings page's Move..., chosen through UI Automation, opens Windows' folder picker, into
which the phase types a folder of its own under -Work. A folder that is not empty, the Open with... folder and the
stash folder itself are each refused by name on the row, settings.json unchanged. Then three Moves, each to a new
empty folder: while a recording runs, Move... waits naming it, the page's path reading the old folder and its other
controls disabled; the tray's Take a screenshot and a real Ctrl+V of a picture in the stash window are refused naming
the Move; the recording stopped, its notification's Undo is waited for, and once that notification is closed the Move
carries every file of the root and originals\, byte for byte, and the store with every item's columns, tag and use,
switches settings.json to the new folder with neither Move field left, removes the old folder, and says so on the row
and in the stash window only once the old folder is gone; the clipboard's clip names its new place, and the stash
window lists and copies from there. A Delete's notice is waited for and its Undo, clicked meanwhile, brings the item
back before the Move carries it; an open edit is waited for, and its Cancel's notice after it; each Move carries the
whole stash as it was. Quit and a new start then open the stash at the folder the settings name, nothing made at the
old places (G30, G32, G35, R9, G16, G18, G20, G26, E2, State Move). A kill during a Move, at a moment no script on the desktop can time, is not checked here;
this phase says so and does not try one. Exits with the number of checks that failed.

With -Store, the stash and its settings are the check's own and every step runs; without it they are the user's, and
the phase is skipped, since it moves the user's stash.

Sends real keystrokes (Ctrl+Alt+Space, Ctrl+V, Ctrl+A, Backspace, Tab, the menu key, Ctrl+E, Ctrl+C, Esc, Enter, a
folder's path), drags across the primary display's middle, clicks notices and notifications, posts right-clicks to the
tray icon and a click and OK to the folder picker, replaces the clipboard, records a few seconds of the screen, creates
folders under -Work, moves the check's stash three times, quits the copy and starts it again, removes from the Recycle
Bin what a Delete it did not get to undo left there, puts the pointer back, and leaves the copy running for
check-published.ps1 to stop.
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [string] $Store = '',
    [string] $Work = $env:TEMP
)
$ErrorActionPreference = 'Stop'
$failed = 0
$ellipsis = [char]0x2026
$pageTitle = 'ImageStash settings'
$pickerTitle = 'Choose an empty folder for your stash'
$moveWords = "Move$ellipsis"
$location = 'Where your stash is stored'
$recordTitle = 'ImageStash record a clip'
$frameTitle = 'ImageStash recording'
$barTitle = 'ImageStash recording bar'
$movingAway = 'your stash is being moved'
$appData = Join-Path $env:LOCALAPPDATA 'ImageStash'
$settingsFile = Join-Path $appData 'settings.json'
$openWithRoot = Join-Path $appData 'open-with'

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

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

# The copy's elements of <type> that UI Automation finds under <root>, named <name> when one is given.
# UI Automation can fail a read with E_FAIL while the window redraws, about one read in ten over an open edit: a read
# that failed is not what the window shows, so it is read again.
function Elements($root, [string] $name, $type) {
    $condition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $running),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, $type))
    foreach ($read in 1..3) {
        try { $found = $root.FindAll([Windows.Automation.TreeScope]::Descendants, $condition); break }
        catch { if ($read -eq 3) { throw }; Start-Sleep -Milliseconds 50 }
    }
    @($found | Where-Object { (-not $name) -or ($_.Current.Name -eq $name) })
}

# The one element of <found>, needed for <what>.
function One($found, [string] $what) {
    $all = @($found | Where-Object { $null -ne $_ })
    Need ($all.Count -eq 1) "$what is there once: $($all.Count) found"
    $all[0]
}

function Choose($element) { $element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }

# The window's pin, a toggle rather than a button: switched as a click would switch it.
function Flip($element) { $element.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle() }

# A real click at the middle of <element>, or with -Hover the pointer put there: UI Automation's rectangles are in
# physical pixels, as PasteDesk's input is.
function ClickOn($element, [switch] $Hover) {
    $box = $element.Current.BoundingRectangle
    if ($Hover) { [PasteDesk]::MovePointer([int]($box.X + $box.Width / 2), [int]($box.Y + $box.Height / 2)) }
    else { [PasteDesk]::Click([int]($box.X + $box.Width / 2), [int]($box.Y + $box.Height / 2)) }
}

function Query([string] $sql) { [CopyReadBack]::Query($script:storeNow, $sql) }
function Count([string] $sql) { [int](Query $sql) }
function Of([long] $id, [string] $column) { Query "SELECT $column FROM items WHERE id = $id" }

# What Move must carry of <folder>'s files: each of the root and originals\ by its path below the folder, with its
# SHA-256, in order; .work\ and the store's own files left out, the store being read through SQL.
function FilesOf([string] $folder) {
    $root = [IO.Path]::GetFullPath($folder).TrimEnd('\') + '\'
    $sha = [Security.Cryptography.SHA256]::Create()
    @(Get-ChildItem -LiteralPath $root -Recurse -File -Force | ForEach-Object { $_.FullName.Substring($root.Length) } |
        Where-Object { ($_ -notlike '.work\*') -and ($_ -ne 'stash.db') -and ($_ -ne 'stash.db-journal') } | Sort-Object |
        ForEach-Object { "$_|" + [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($root + $_))) })
}

# The store's facts Move must carry, one line each: every column of every item's row, the columns read from the
# store itself so none is left out, every tag, every use, the version.
function StoreFacts {
    $columns = Query "SELECT group_concat('quote(' || name || ')', ' || ''|'' || ') FROM pragma_table_info('items')"
    @(
        (Query "SELECT IFNULL(group_concat(line, char(10)), '') FROM (SELECT $columns AS line FROM items ORDER BY id)"),
        (Query "SELECT IFNULL(group_concat(line, char(10)), '') FROM (SELECT item_id || '|' || tag AS line FROM tags ORDER BY item_id, tag)"),
        (Query "SELECT IFNULL(group_concat(line, char(10)), '') FROM (SELECT item_id || '|' || used_at AS line FROM uses ORDER BY item_id, used_at)"),
        (Query 'PRAGMA user_version')
    ) -join "`n"
}

# What a Move carried against what was read before it: every file byte for byte, every store fact (G30, F11).
function CheckCarried([string] $to, $files, [string] $facts) {
    $carried = FilesOf $to
    Check (($carried -join "`n") -eq ($files -join "`n")) "the new folder holds every file of the root and originals\, byte for byte: $($carried.Count) of $($files.Count) (G30)"
    Check ((Digest (StoreFacts)) -eq (Digest $facts)) 'its store holds every column of every item, every tag and every use as they were (G30, F11)'
    Check (@(Get-ChildItem -LiteralPath (Join-Path $to '.work') -Force -ErrorAction SilentlyContinue).Count -eq 0) "$to\.work\ holds nothing (State Files)"
}

# A real Ctrl+C in the stash window over a clipboard holding words, so only a fresh write passes: the clipboard then
# names <file> alone, and one use more is recorded (G9, G10).
function CopiedFresh([string] $file) {
    Set-Clipboard -Value 'words of the Move phase'
    $uses = Count 'SELECT COUNT(*) FROM uses'
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x43))
    WaitFor { ([CopyReadBack]::Clipboard() -eq "files=$file png=False dibv5=False effect=1") -and ((Count 'SELECT COUNT(*) FROM uses') -eq $uses + 1) } 3000
}

# The Recycle Bin's records, on -Work's drive, of files that came from under -Work: each $I record names its file's
# old path after a character count at byte 24 (version 2, which Windows 10 and 11 write).
function BinRecords {
    $bin = Join-Path ([IO.Path]::GetPathRoot($Work)) ('$Recycle.Bin\' + [Security.Principal.WindowsIdentity]::GetCurrent().User.Value)
    foreach ($record in @(Get-ChildItem -LiteralPath $bin -Filter '$I*' -Force -ErrorAction SilentlyContinue)) {
        try { $bytes = [IO.File]::ReadAllBytes($record.FullName) } catch { continue }
        if (($bytes.Length -lt 28) -or ([BitConverter]::ToInt64($bytes, 0) -ne 2)) { continue }
        if ([Text.Encoding]::Unicode.GetString($bytes, 28, ([BitConverter]::ToInt32($bytes, 24) - 1) * 2).StartsWith($Work, [StringComparison]::OrdinalIgnoreCase)) { $record.FullName }
    }
}

function Digest([string] $text) { -join ([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($text)) | ForEach-Object { $_.ToString('x2') }) }
function Settings { Get-Content -Raw -LiteralPath $settingsFile | ConvertFrom-Json }
function SettingsText { [IO.File]::ReadAllText($settingsFile) }
function Page { [PasteDesk]::Titled($running, $pageTitle) }
function PageElement { [Windows.Automation.AutomationElement]::FromHandle((Page)) }
function InFront { (Page) -ne [IntPtr]::Zero -and ([PasteDesk]::Foreground() -eq (Page)) }
function Picker { [PasteDesk]::Titled($running, $pickerTitle) }

# The stash folder's path as the page shows it.
function PathShown {
    try { (Elements (PageElement) $location $edit)[0].GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value }
    catch { "(unread: $($_.Exception.Message))" }
}

# The page's words about Move: its busy line while one is asked, then what the last one said, or a failure on the row.
# UI Automation can fail a read while the page redraws: that read answers what it could not read, never throws.
function MoveLine {
    try {
        @(Elements (PageElement) '' $text | ForEach-Object { $_.Current.Name } |
            Where-Object { $_ -match "^(Waiting for |Moving your stash|Moved your stash|Move the stash failed|Finish moving the stash failed)" }) -join ' | '
    }
    catch { "(unread: $($_.Exception.Message))" }
}

# The tray menu, opened with a posted right-click, and the entry named <name> chosen.
function ChooseInTrayMenu([string] $name) {
    Need ([StashWindows]::RightClickTrayIcon($running)) 'a right-click posted to the tray icon'
    $null = WaitFor { (Elements $desktop 'Quit ImageStash' $menuItem).Count -gt 0 } 3000
    Choose (One (Elements $desktop $name $menuItem) "the tray menu's $name")
}

# The Settings page from the tray, in front.
function OpenPage {
    ChooseInTrayMenu 'Settings'
    Need (WaitFor { InFront } 5000) "the tray's Settings brought '$pageTitle' to the front"
}

function ClosePage {
    if ((Page) -ne [IntPtr]::Zero) { [PasteDesk]::Close((Page)) }
    Need (WaitFor { (Page) -eq [IntPtr]::Zero } 3000) 'the page closed'
}

# The words a window of another program shows, as UI Automation reads them.
function WordsOf([IntPtr] $window) {
    try { @([Windows.Automation.AutomationElement]::FromHandle($window).FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) -join ' | ' }
    catch { "(unread: $($_.Exception.Message))" }
}

# Move... chosen on the page, and <folder> chosen in the folder picker it opens as a person chooses one: Alt+D, its
# parent's path typed into the address bar and Enter, then its name typed into the Folder box and Select Folder. Not
# the path itself: the picker opens in the folder last chosen in it, which an earlier phase may have removed, and takes
# no path typed there; and inside a folder holding folders it selects one of them.
function AskMove([string] $folder) {
    Choose (One (Elements (PageElement) $moveWords $button) "the page's $moveWords")
    Need (WaitFor { (Picker) -ne [IntPtr]::Zero } 10000) "$moveWords opened a window titled '$pickerTitle', the folder picker (G32, F4)"
    if (-not (WaitFor { [PasteDesk]::Foreground() -eq (Picker) } 3000)) {
        # Not given the front: a real click on its title bar, as a person's.
        $rect = [PasteDesk]::WindowRect((Picker))
        [PasteDesk]::Click([int](($rect[0] + $rect[2]) / 2), $rect[1] + 12)
    }
    # Keys go to the picker only while it is in front: reaching the page, they would press its focused button.
    Need (WaitFor { [PasteDesk]::Foreground() -eq (Picker) } 3000) "the picker is in front: in front '$([CaptureDesk]::TitleOf([PasteDesk]::Foreground()))'"
    $picker = [Windows.Automation.AutomationElement]::FromHandle((Picker))
    # The common dialog's address bar (1001), Folder box (1152) and Select Folder (1), by their ids: UI Automation
    # shows its controls as named ones or, as it may for that dialog, as bare panes.
    $byId = { param($id) $picker.FindFirst([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $id)) }
    $null = WaitFor { ($null -ne (& $byId '1')) -and ($null -ne (& $byId '1152')) } 5000
    $parent = Split-Path $folder -Parent
    # Only from elsewhere: asked for the folder it is already in, the address bar stays in its edit box and the wait
    # below never ends.
    $was = (& $byId '1001').Current.Name
    if ($was -ne "Address: $parent") {
        $null = [PasteDesk]::Press([uint16[]]@(0x12, 0x44))
        Start-Sleep -Milliseconds 300
        $null = [OpenWithDesk]::Type($parent)
        $null = [PasteDesk]::Press([uint16[]]@(0x0D))
        Need (WaitFor { (& $byId '1001').Current.Name -eq "Address: $parent" } 5000) "the picker opened $parent from its address bar: '$((& $byId '1001').Current.Name)', having read '$was' before the path was typed$(if ((& $byId '1001').Current.Name -ne "Address: $parent") { "; the picker shows $(WordsOf (Picker))" })"
    }
    $box = [IntPtr](& $byId '1152').Current.NativeWindowHandle
    $name = Split-Path $folder -Leaf
    # Posted to the box, not clicked at its place: a flyout with an edit open stays above every window (G2) and, summoned
    # beside a caret, may lie over the box, where a real click would give it the keys. Typed again while the box does not
    # read the name, since the picker takes what the box reads, however little of the name has reached it.
    for ($typed = 0; ($typed -lt 3) -and ([PasteDesk]::Foreground() -eq (Picker)) -and ([PasteDesk]::ControlText($box) -ne $name); $typed++) {
        [PasteDesk]::PostClick($box)
        $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x41))
        $null = [OpenWithDesk]::Type($name)
        $null = WaitFor { [PasteDesk]::ControlText($box) -eq $name } 1000
    }
    Need ([PasteDesk]::ControlText($box) -eq $name) "the picker's Folder box reads $name`: '$([PasteDesk]::ControlText($box))'"
    # Select Folder, posted, as the box's suggestions may cover it. The first answer may only open the folder the box
    # names; a second before that has happened asks for the name inside it, and the picker says the path does not exist.
    [PasteDesk]::PostOk((Picker))
    $null = WaitFor { ((Picker) -eq [IntPtr]::Zero) -or ((& $byId '1001').Current.Name -eq "Address: $folder") } 3000
    if ((Picker) -ne [IntPtr]::Zero) { [PasteDesk]::PostOk((Picker)) }
    $null = WaitFor { (Picker) -eq [IntPtr]::Zero } 3000
    Need ((Picker) -eq [IntPtr]::Zero) "Select Folder took $folder and closed the picker$(if ((Picker) -ne [IntPtr]::Zero) { ": in front $(WordsOf ([PasteDesk]::Foreground()))" })"
}

# The page's line, path and the old folder watched until the Move says it ended: the lines it showed, whether the old
# folder was gone when success was first said, and whether the new folder was ever shown while the old one remained.
function WatchMove([string] $from, [string] $to, [int] $seconds, [switch] $SummonDuringCopy) {
    $lines = New-Object System.Collections.Generic.List[string]
    $result = [pscustomobject]@{ Lines = $lines; End = $null; OldGoneAtEnd = $null; NewShownEarly = $false; Took = 0; SaidInWindow = $false; Slowest = 0; Summoned = $null; SummonedDuringCopy = $false; PathAtEnd = $null }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    if ($SummonDuringCopy) {
        # G1: once the copy has begun, its store file in the new folder and the old folder whole, the open shortcut;
        # the folders read without UI Automation, which waits on the copy's own window.
        $null = WaitFor { (Test-Path -LiteralPath (Join-Path $to 'stash.db')) -or -not (Test-Path -LiteralPath (Join-Path $from 'stash.db')) } ($seconds * 1000)
        if (Test-Path -LiteralPath (Join-Path $from 'stash.db')) {
            $pressed = [Diagnostics.Stopwatch]::StartNew()
            $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x12, 0x20))
            $shown = WaitFor { $null -ne (StashWindow) } 30000
            $result.Summoned = if ($shown) { $pressed.ElapsedMilliseconds } else { -1 }
            $result.SummonedDuringCopy = Test-Path -LiteralPath (Join-Path $from 'stash.db')
        }
    }
    $last = $clock.ElapsedMilliseconds
    while ($clock.Elapsed.TotalSeconds -lt $seconds) {
        $result.Slowest = [Math]::Max($result.Slowest, $clock.ElapsedMilliseconds - $last)
        $last = $clock.ElapsedMilliseconds
        # Read before the folder is looked at, so a folder still there afterwards was there when they were shown.
        $line = MoveLine
        $path = PathShown
        $oldThere = Test-Path -LiteralPath $from
        if ($line -and ($line -notlike '(unread*') -and ($lines.Count -eq 0 -or $lines[$lines.Count - 1] -ne $line)) { $lines.Add($line) }
        if (($path -eq $to) -and $oldThere) { $result.NewShownEarly = $true }
        if (($line -like 'Moved your stash to *') -or ($line -like 'Move the stash failed*') -or ($line -like 'Finish moving the stash failed*')) {
            $result.End = $line
            $result.OldGoneAtEnd = -not $oldThere
            $result.PathAtEnd = PathShown
            break
        }
        Start-Sleep -Milliseconds 30
    }
    $result.Took = $clock.ElapsedMilliseconds
    # The stash window's notice lasts six seconds whether or not the window shows (G16): read at once.
    if ($result.End -like 'Moved your stash to *') {
        # The page closed first: the open shortcut pressed while it is in front also presses its focused button.
        ClosePage
        $root = Summon
        $result.SaidInWindow = WaitFor { (WindowWords $root "Moved your stash to $to.").Count -ge 1 } 2000
        $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x12, 0x20))
    }
    $result
}

# What a Move that ended in success must show: the words after the old folder went, the new one at the path, the
# settings switched with neither Move field left (G30, R9, State Move, State Settings).
function CheckMoved($watched, [string] $from, [string] $to, [string] $what) {
    Write-Host "     the page's line read, in order, its slowest read taking $($watched.Slowest) ms: $($watched.Lines -join ' / ')"
    Need ($watched.End -eq "Moved your stash to $to.") "$what ended in success within $([int]($watched.Took / 1000)) s, the row reading 'Moved your stash to $to.' (G30, G32): '$($watched.End)'"
    Check $watched.OldGoneAtEnd "when the success words were first read, the old folder, $from, was already gone (R9, G35)"
    Check (-not $watched.NewShownEarly) 'the page never showed the new folder while the old one remained (R9)'
    Check $watched.SaidInWindow "the stash window says 'Moved your stash to $to.' (G30, R9)"
    Check ($watched.PathAtEnd -eq $to) "the page's path then reads the new folder (G32): $($watched.PathAtEnd)"
    $settings = Settings
    Check (($settings.stashFolder -eq $to) -and ($null -eq $settings.moveCopyInProgress) -and ($null -eq $settings.previousStashFolderPendingRemoval)) "settings.json names $to as the stash folder, neither Move field left (State Move, State Settings): stashFolder '$($settings.stashFolder)', moveCopyInProgress '$($settings.moveCopyInProgress)', previousStashFolderPendingRemoval '$($settings.previousStashFolderPendingRemoval)'"
    Check (-not (Test-Path -LiteralPath $from)) "the old folder, $from, is gone (G30, G29)"
    Check ((Test-Path -LiteralPath (Join-Path $to 'stash.db') -PathType Leaf) -and (Test-Path -LiteralPath (Join-Path $to 'originals') -PathType Container)) "the new folder holds stash.db and originals\ (G30, F6)"
    $script:storeNow = Join-Path $to 'stash.db'
    $script:stashNow = $to
}

# The stash window, titled for the product; or none.
function StashWindow { @([ScreenshotDesk]::Titled($running, 'ImageStash')) | Select-Object -First 1 }
function StashInFront { $window = StashWindow; ($null -ne $window) -and ([PasteDesk]::Foreground() -eq $window) }

# The stash window summoned with the real open shortcut, in front; its root element.
function Summon {
    if (-not (StashInFront)) { $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x12, 0x20)) }
    Need (WaitFor { StashInFront } 3000) 'the real Ctrl+Alt+Space showed the stash window in front'
    [Windows.Automation.AutomationElement]::FromHandle((StashWindow))
}

# The words the stash window shows that begin with <start>.
function WindowWords($root, [string] $start) { try { @(Elements $root '' $text | ForEach-Object { $_.Current.Name } | Where-Object { $_.StartsWith($start) }) } catch { @() } }

# The item <id> alone in the grid and selected: its name typed into the search box with real input (G5).
function Find([long] $id) {
    $name = Of $id 'name'
    $root = Summon
    # A preview left open comes back with the window (G1); Esc closes it (G2).
    if (-not (WaitFor { (Elements $root 'Search your stash' $edit).Count -eq 1 } 1000)) { $null = [PasteDesk]::Press([uint16[]]@(0x1B)) }
    $null = WaitFor { (Elements $root 'Search your stash' $edit).Count -eq 1 } 2000
    $box = One (Elements $root 'Search your stash' $edit) 'the search box'
    ClickOn $box
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x41))
    $null = [PasteDesk]::Press([uint16[]]@(0x08))
    # All, which clears the search, counts every item, so the one match below is the search's, not a scope's.
    Choose (One (Elements $root 'All' $button) 'the All chip')
    Need (WaitFor { ($box.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -eq '') -and ((Elements $root '1 item' $text).Count -eq 0) } 3000) 'the search box emptied and All shown'
    ClickOn $box
    $null = [OpenWithDesk]::Type($name)
    $typed = WaitFor { ($box.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -eq $name) -and ((Elements $root '1 item' $text).Count -eq 1) } 5000
    Need $typed "'$name' typed into the search box leaves one match, which is selected (G5)"
    $root
}

# A still the search finds alone, its file named after it, neither the one nor the other of <besides>.
function AloneStill([long[]] $besides) {
    $not = if ($besides.Count -gt 0) { 'AND id NOT IN (' + ($besides -join ',') + ')' } else { '' }
    [long](Query "SELECT IFNULL(MAX(id), 0) FROM (SELECT id FROM items WHERE deleted = 0 AND animated = 0 AND layers = '[]' AND finished_file = name || '.png' $not AND NOT EXISTS (SELECT 1 FROM items other WHERE other.id <> items.id AND other.name = items.name) ORDER BY added_at DESC, id DESC LIMIT 1)")
}

# The titles of the copy's notifications now up.
function Notifications { @([CaptureDesk]::Shown($running) | Where-Object { ($_ -ne '') -and ($_ -ne 'ImageStash') -and ($_ -ne $pageTitle) }) }

# A new empty folder under the phase's own, by <name>.
function EmptyFolder([string] $name) { (New-Item -ItemType Directory -Path (Join-Path $mine $name)).FullName }

# Move... to <folder> refused with <reason> on the row, nothing changed (G30, G35).
function Refused([string] $folder, [string] $reason, [string] $what) {
    $before = SettingsText
    OpenPage
    AskMove $folder
    $subject = Split-Path $folder -Leaf
    $sentence = "Move the stash failed: $subject, $reason"
    Check (WaitFor { (MoveLine) -eq $sentence } 5000) "Move... to $what is refused, the row reading '$sentence' (G30, G35): '$(MoveLine)'"
    Check ((PathShown) -eq $stashNow) "the page still shows the stash folder, $stashNow (G32)"
    Check (((SettingsText) -eq $before) -and (Test-Path -LiteralPath $storeNow)) 'settings.json is as it was and the stash is where it was (G30)'
    ClosePage
}

if (-not $Store) {
    Write-Host "skipped: the stash and settings are the user's, under $appData, so the Move phase, which moves the stash there, does not run"
    exit 0
}
$copies = @(Get-Process -Name ImageStash.Windows -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe })
if ($copies.Count -ne 1) {
    Write-Host "FAIL the Move phase found $($copies.Count) running copies of $Exe, not one"
    exit 1
}
$running = $copies[0].Id
$storeNow = $Store
$stashNow = Split-Path $Store -Parent
$desktop = [Windows.Automation.AutomationElement]::RootElement
$button = [Windows.Automation.ControlType]::Button
$text = [Windows.Automation.ControlType]::Text
$edit = [Windows.Automation.ControlType]::Edit
$menuItem = [Windows.Automation.ControlType]::MenuItem
$pointer = [PasteDesk]::Pointer()
$mine = Join-Path $Work ('move-phase-' + [guid]::NewGuid().ToString('N'))
$defaultThere = Test-Path -LiteralPath (Join-Path $appData 'stash')
$binBefore = @(BinRecords)
$null = New-Item -ItemType Directory -Path $mine
try {
    Write-Host 'note: a kill during a Move before it reports completion is not checked here; this phase cannot time one on the desktop and does not try'
    [PasteDesk]::MovePointer(200, 150)
    Need (WaitFor { (Notifications).Count -eq 0 } 20000) "no notification of the copy is up before the phase's own: $((Notifications) -join ' | ')"
    foreach ($window in @([ScreenshotDesk]::Titled($running, 'ImageStash'))) { [PasteDesk]::Close($window) }
    Need (WaitFor { $null -eq (StashWindow) } 3000) 'the stash window hidden'

    # G30, G35: three folders refused by name, nothing changed.
    $full = EmptyFolder 'not-empty'
    Set-Content -LiteralPath (Join-Path $full 'something.txt') -Value 'a file of the check''s own'
    Refused $full 'it is not empty' 'a folder that is not empty'
    Refused $openWithRoot "it lies inside the Open with$ellipsis folder" "the Open with$ellipsis folder, $openWithRoot"
    Refused $stashNow 'it is the stash folder itself' 'the stash folder itself'

    # State Move, G30, G35, R9: a recording in progress waited for, beginnings refused meanwhile, its notification's
    # Undo waited for, then the whole stash carried.
    $items = Count 'SELECT COUNT(*) FROM items'
    $from = $stashNow
    ChooseInTrayMenu 'Record a clip'
    Need (WaitFor { (@([ScreenshotDesk]::Titled($running, $recordTitle)).Count -ge 1) } 3000) "the tray's Record a clip showed the overlay in record mode"
    Start-Sleep -Milliseconds 300
    $null = [CaptureDesk]::Drag(300, 200, 559, 379)
    Need (WaitFor { (@([ScreenshotDesk]::Titled($running, $recordTitle)).Count -eq 0) -and ([ScreenshotDesk]::Titled($running, $barTitle).Count -eq 1) } 3000) 'a real drag selected a region waiting to be recorded'
    ChooseInTrayMenu 'Start recording'
    Need (WaitFor { (Elements ([Windows.Automation.AutomationElement]::FromHandle([ScreenshotDesk]::Titled($running, $barTitle)[0])) 'Stop' $button).Count -eq 1 } 3000) "the tray's Start recording started it: the bar holds Stop"

    $to = EmptyFolder 'moved-1'
    OpenPage
    AskMove $to
    Check (WaitFor { (MoveLine) -eq "Waiting for the recording before moving your stash$ellipsis" } 3000) "Move... waits for the recording, the row reading 'Waiting for the recording before moving your stash$ellipsis' (State Move, G35): '$(MoveLine)'"
    Check ((PathShown) -eq $from) "while it waits the page shows the folder the stash is in, $from (R9): $(PathShown)"
    $enabled = @($moveWords, 'Start with Windows', 'Open folder' | ForEach-Object { @(Elements (PageElement) $_ $button)[0].Current.IsEnabled })
    Check (@($enabled | Where-Object { $_ -ne $false }).Count -eq 0) "while it waits Move..., Start with Windows and Open folder are disabled, the page taking no change (G32, State Move): enabled $($enabled -join ', ')"
    Start-Sleep -Seconds 1
    Check ((Test-Path -LiteralPath $storeNow) -and (@(Get-ChildItem -LiteralPath $to -Force).Count -eq 0) -and ((Settings).stashFolder -eq $from)) 'a second on, nothing has been copied and the settings still name the old folder (State Move)'

    # Nothing else starts: a capture and an import asked now are refused by name.
    $refusedShot = "Take a screenshot failed: the screen, $movingAway"
    ChooseInTrayMenu 'Take a screenshot'
    Check (WaitFor { [CaptureDesk]::TitledFrom($running, $refusedShot) -ne [IntPtr]::Zero } 3000) "the tray's Take a screenshot is refused by name, a notification reading '$refusedShot' (State Move, G35)"
    Check (@([ScreenshotDesk]::Titled($running, 'ImageStash screenshot')).Count -eq 0) 'and no screenshot overlay showed'
    # Closed, so the recording's notification is the only one up when its Close is clicked below.
    ClickOn (One (Elements ([Windows.Automation.AutomationElement]::FromHandle([CaptureDesk]::TitledFrom($running, $refusedShot))) 'Close' $button) "that notification's Close")
    $null = WaitFor { [CaptureDesk]::TitledFrom($running, $refusedShot) -eq [IntPtr]::Zero } 3000
    # One flat colour, its "PNG" and its CF_DIBV5 rows (blue, green, red, alpha) as a browser's Copy image puts them.
    $pixels = [byte[]](@(40, 120, 200, 255) * 24)
    Need ([PasteInClipboard]::Put([OpenWithDesk]::Flat(6, 4, 200, 120, 40, 'png'), [PasteInClipboard]::DibV5(6, 4, $pixels))) 'a picture on the clipboard as a browser''s Copy image places it'
    $root = Summon
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x56))
    $refusedPaste = "Add to the stash failed: the clipboard, $movingAway"
    Check (WaitFor { (WindowWords $root $refusedPaste).Count -ge 1 } 3000) "a real Ctrl+V in the stash window is refused by name, a notice reading '$refusedPaste' (State Move, G35): $((WindowWords $root 'Add to the stash') -join ' | ')"
    Check ((Count 'SELECT COUNT(*) FROM items') -eq $items) "nothing was added to the stash of $items"
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x12, 0x20))
    $null = WaitFor { $null -eq (StashWindow) } 2000
    Check ((Settings).stashFolder -eq $from) 'the Move still waits, the settings naming the old folder'

    # The recording stopped: its notification's Undo stands, and the Move waits for it (G16).
    ChooseInTrayMenu 'Stop recording'
    Need (WaitFor { ((Count 'SELECT COUNT(*) FROM items') -eq $items + 1) -and ([ScreenshotDesk]::Titled($running, $frameTitle).Count -eq 0) -and ([ScreenshotDesk]::Titled($running, $barTitle).Count -eq 0) } 15000) "the tray's Stop recording ended the recording with its clip in the stash"
    $clip = [long](Query 'SELECT id FROM items ORDER BY added_at DESC, id DESC LIMIT 1')
    $clipFile = Of $clip 'finished_file'
    $saved = "Saved $([char]0x201C)$(Of $clip 'name')$([char]0x201D) to your stash."
    Need (WaitFor { [CaptureDesk]::TitledFrom($running, $saved) -ne [IntPtr]::Zero } 5000) "its notification says '$saved'"
    $card = [Windows.Automation.AutomationElement]::FromHandle([CaptureDesk]::TitledFrom($running, $saved))
    ClickOn (One (Elements $card 'Close' $button) "the notification's Close") -Hover
    Check (WaitFor { (MoveLine) -eq "Waiting for the Undo of a notification before moving your stash$ellipsis" } 3000) "the Move then waits for the notification's Undo, the row reading 'Waiting for the Undo of a notification before moving your stash$ellipsis' (State Move, G16): '$(MoveLine)'"
    # The pointer on the notification keeps it up while what the Move must carry is read.
    $files = FilesOf $from
    $facts = StoreFacts
    Check (($files.Count -gt $items) -and ($facts -ne '')) "read what the Move must carry: $($files.Count) files of the root and originals\, and the store's $items + 1 items, tags and uses"
    Check ([CopyReadBack]::Clipboard() -eq "files=$(Join-Path $from $clipFile) png=False dibv5=False effect=1") "the clipboard holds the clip at the old folder, as Also copy put it there: $([CopyReadBack]::Clipboard())"
    Check ((Settings).stashFolder -eq $from) 'nothing has switched while the notification stands'
    ClickOn (One (Elements $card 'Close' $button) "the notification's Close")
    $watched = WatchMove $from $to 600 -SummonDuringCopy
    Check (($watched.Summoned -ge 0) -and ($watched.Summoned -le 1000) -and $watched.SummonedDuringCopy) "during the copy the real Ctrl+Alt+Space showed the stash window within a second (G1): $($watched.Summoned) ms, the copy still running then: $($watched.SummonedDuringCopy)"
    CheckMoved $watched $from $to 'once the notification was closed, the Move'
    CheckCarried $to $files $facts
    Check (WaitFor { [CopyReadBack]::Clipboard() -eq "files=$(Join-Path $to $clipFile) png=False dibv5=False effect=1" } 3000) "the clipboard's clip, ImageStash's own write, now names its new place, as after a Save (G8, G26): $([CopyReadBack]::Clipboard())"
    $null = Find $clip
    Check (CopiedFresh (Join-Path $to $clipFile)) "the stash window lists the clip from the new folder and Ctrl+C copies it from there, a use recorded (G30, G9, G10): $([CopyReadBack]::Clipboard())"
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x12, 0x20))
    $null = WaitFor { $null -eq (StashWindow) } 2000
    ClosePage

    # G18, State Move: a Delete's notice waited for, its Undo acting while the Move waits.
    $from = $stashNow
    $to = EmptyFolder 'moved-2'
    $doomed = AloneStill @($clip)
    Need ($doomed -ne 0) 'the stash holds an unedited still whose name no other item has'
    $doomedFile = Join-Path $from (Of $doomed 'finished_file')
    # The Undo below puts back all the Delete takes, so the Move carries the stash as it is now.
    $files = FilesOf $from
    $facts = StoreFacts
    $root = Find $doomed
    if ((Elements $root 'Keep open' $button).Count -eq 1) { Flip (One (Elements $root 'Keep open' $button) 'the pin') }
    Need (WaitFor { (Elements $root 'Unpin' $button).Count -eq 1 } 3000) 'the stash window pinned, so it stays up while the page is in front'
    $null = [PasteDesk]::Press([uint16[]]@(0x09))
    Start-Sleep -Milliseconds 200
    $null = [PasteDesk]::Press([uint16[]]@(0x5D))
    $null = WaitFor { (Elements $desktop 'Delete' $menuItem).Count -eq 1 } 3000
    ClickOn (One (Elements $desktop 'Delete' $menuItem) "the item menu's Delete")
    $deleted = "Deleted $([char]0x201C)$(Of $doomed 'name')$([char]0x201D)."
    Need (WaitFor { ((WindowWords $root $deleted).Count -ge 1) -and ((Count "SELECT COUNT(*) FROM items WHERE id = $doomed AND deleted = 1") -eq 1) -and -not (Test-Path -LiteralPath $doomedFile) } 5000) "the item menu's Delete deleted it, a notice reading '$deleted' (G18)"
    ClickOn (One @(Elements $root 'Undo' $button) "the notice's Undo") -Hover
    OpenPage
    AskMove $to
    Check (WaitFor { (MoveLine) -eq "Waiting for the Undo of a notice before moving your stash$ellipsis" } 3000) "Move... waits for the Delete notice's Undo, the row reading 'Waiting for the Undo of a notice before moving your stash$ellipsis' (State Move, G18, G35): '$(MoveLine)'"
    Check ((Settings).stashFolder -eq $from) 'nothing has switched while that Undo stands'
    # The pinned window brought forward, over the page, whose controls take nothing while the Move is asked.
    $root = Summon
    ClickOn (One @(Elements $root 'Undo' $button) "the notice's Undo")
    $watched = WatchMove $from $to 600
    CheckMoved $watched $from $to 'once that Undo had acted, the Move'
    CheckCarried $to $files $facts
    Check (((Count "SELECT COUNT(*) FROM items WHERE id = $doomed AND deleted = 0 AND held_copies IS NULL") -eq 1) -and (Test-Path -LiteralPath (Join-Path $to (Of $doomed 'finished_file')) -PathType Leaf)) 'the Undo, clicked while the Move waited, brought the item back, and the Move carried it: its row and its file in the new folder (G18, G30)'
    [PasteDesk]::MovePointer(200, 150)
    # Close unpins the window and hides it (G2).
    foreach ($window in @([ScreenshotDesk]::Titled($running, 'ImageStash'))) { [PasteDesk]::Close($window) }
    $null = WaitFor { $null -eq (StashWindow) } 3000
    ClosePage

    # G20, State Move: an open edit waited for, then its Cancel's notice.
    $from = $stashNow
    $to = EmptyFolder 'moved-3'
    $edited = AloneStill @($clip, $doomed)
    Need ($edited -ne 0) 'the stash holds another unedited still whose name no other item has'
    # A cancelled edit changes nothing, so the Move carries the stash as it is now.
    $files = FilesOf $from
    $facts = StoreFacts
    $null = Find $edited
    $null = [PasteDesk]::Press([uint16[]]@(0x09))
    Start-Sleep -Milliseconds 200
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x45))
    Need (WaitFor { (StashInFront) -and ((Elements ([Windows.Automation.AutomationElement]::FromHandle((StashWindow))) 'Crop' $button).Count -eq 1) } 5000) 'Ctrl+E opened the editor on it (G20)'
    OpenPage
    AskMove $to
    $waiting = "Waiting for the edit before moving your stash$ellipsis"
    Check (WaitFor { (MoveLine) -eq $waiting } 3000) "Move... waits for the open edit, the row reading '$waiting' (State Move, G20, G35): '$(MoveLine)'"
    Start-Sleep -Seconds 2
    Check (((MoveLine) -eq $waiting) -and ((Settings).stashFolder -eq $from)) 'two seconds on it still waits, the settings naming the old folder'
    $root = Summon
    Need ((Elements $root 'Crop' $button).Count -eq 1) 'the open shortcut brought the stash window forward with the edit as it was left (G2, G20)'
    $null = [PasteDesk]::Press([uint16[]]@(0x1B))
    $discarded = "Discarded the edit of $([char]0x201C)$(Of $edited 'name')$([char]0x201D)."
    Check (WaitFor { ((Elements $root 'Crop' $button).Count -eq 0) -and ((WindowWords $root $discarded).Count -ge 1) } 3000) "Esc cancelled the edit, a notice reading '$discarded' (G20)"
    [PasteDesk]::MovePointer(200, 150)
    Check (WaitFor { (MoveLine) -eq "Waiting for the Undo of a notice before moving your stash$ellipsis" } 3000) "the Move then waits for that notice's Undo (State Move, G20): '$(MoveLine)'"
    $watched = WatchMove $from $to 600
    CheckMoved $watched $from $to 'once that notice had gone, the Move'
    CheckCarried $to $files $facts
    foreach ($window in @([ScreenshotDesk]::Titled($running, 'ImageStash'))) { [PasteDesk]::Close($window) }
    $null = WaitFor { $null -eq (StashWindow) } 2000
    ClosePage

    # E2: Quit, and the next start opens the stash where the settings put it, making nothing at the old places.
    $items = Count 'SELECT COUNT(*) FROM items'
    ChooseInTrayMenu 'Quit ImageStash'
    Need (WaitFor { @(Get-Process -Id $running -ErrorAction SilentlyContinue).Count -eq 0 } 10000) "the tray's Quit ImageStash ended the copy"
    $again = Start-Process -FilePath $Exe -PassThru
    $running = $again.Id
    Start-Sleep -Seconds 3
    Check (-not $again.HasExited) 'the copy started again'
    $null = Find $clip
    Check (CopiedFresh (Join-Path $to $clipFile)) "the next start lists the stash from $to and Ctrl+C copies from there, a use recorded (E2, G30): $([CopyReadBack]::Clipboard())"
    $old = @((Split-Path $Store -Parent), (Join-Path $mine 'moved-1'), (Join-Path $mine 'moved-2'))
    if (-not $defaultThere) { $old += Join-Path $appData 'stash' }
    Check (@($old | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 0) "nothing was made at the old places or the default location: $(@($old | Where-Object { Test-Path -LiteralPath $_ }) -join ', ')"
    Check ((Count 'SELECT COUNT(*) FROM items') -eq $items) "its store holds the $items items"
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x12, 0x20))
    $null = WaitFor { $null -eq (StashWindow) } 2000
}
catch {
    Write-Host "FAIL the Move phase stopped: $($_.Exception.Message)"
    $failed++
}
finally {
    # Each step on its own, so one that throws (a window gone in the meantime) leaves the others done.
    try { foreach ($window in @([ScreenshotDesk]::Titled($running, $recordTitle))) { [PasteDesk]::Close($window) } } catch { }
    try { if ((Picker) -ne [IntPtr]::Zero) { [PasteDesk]::Close((Picker)) } } catch { }
    try { if ((Page) -ne [IntPtr]::Zero) { [PasteDesk]::Close((Page)) } } catch { }
    # A Delete the phase did not get to undo leaves its file in the Recycle Bin: removed, record and file.
    try {
        foreach ($record in @(BinRecords | Where-Object { $binBefore -notcontains $_ })) {
            Remove-Item -LiteralPath $record, (Join-Path (Split-Path $record -Parent) ('$R' + (Split-Path $record -Leaf).Substring(2))) -Recurse -Force -ErrorAction SilentlyContinue
        }
    } catch { }
    [PasteDesk]::MovePointer($pointer[0], $pointer[1])
}
exit $failed
