<#
.SYNOPSIS
A stash and settings an earlier build wrote, opened by the published copy, run by check-published.ps1 inside its
session, last (it uses check-published-paste.ps1's PasteDesk, check-published-screenshot.ps1's ScreenshotDesk,
check-published-open-with.ps1's OpenWithDesk and check-published.ps1's StashWindows and CopyReadBack), over the
check's own %LOCALAPPDATA%\ImageStash. The copy is stopped first, so the earlier build runs alone (G3, E1), and that
folder emptied, so the earlier build meets a first run. The earlier build is the commit -EarlierCommit names, one whose
store is at version 1, taken with git archive into -Work (no worktree, no change to .git) and published by its own
tools/publish.ps1. Started, it opens pinned on an empty stash; through its own window and real input it imports three
pictures of the phase's own with a real Ctrl+V of the files, tags two "mood" through its item menu's Add tag and Type a
tag..., makes one a favorite with Ctrl+D, copies one with Ctrl+C (a use), steps the thumbnails up with Ctrl and +, and
has its pinned window moved and resized; then Quit ImageStash from its tray menu. Its stash.db reads user version 1 in
its header and its settings.json version 1 with the thumbnail size, the pinned size and the pinned position off their
defaults. The copy started again over them: the store is migrated to version 3, every column of every item, every
tag, use and picture file and every value of settings.json as the earlier build wrote them, the three items are found by
name, the tag's chip and its search find the two, Favorites
the one, Recent the used one, and pinning the window puts it at the saved size and place (G31, F11, Done when 4).
Exits with the number of checks that failed.

With -Store, the stash and settings are the check's own and every step runs; without it they are the user's, and the
phase is skipped, since it replaces them. Without -EarlierCommit, a commit this repository holds whose store is at
version 1, it is skipped as well: there is no earlier build to run. This repository's own history begins at store
version 3, so it holds no such commit.

Stops the copy, empties the check's own %LOCALAPPDATA%\ImageStash, publishes -EarlierCommit under -Work, runs that build and
quits it, writes three pictures under -Work, replaces the clipboard, sends real keystrokes (Ctrl+Alt+Space, Ctrl+V,
Ctrl+A, Backspace, Tab, the menu key, Ctrl+D, Ctrl+C, Ctrl and +, Enter, Esc, words), clicks the item menu, moves and
resizes the stash window, posts right-clicks to the tray icon, puts the pointer back, and leaves the copy running for
check-published.ps1 to stop; the Run value either build writes is check-published.ps1's to put back.
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [string] $Store = '',
    [string] $Work = $env:TEMP,
    [string] $EarlierCommit = ''
)
$ErrorActionPreference = 'Stop'
$failed = 0
$ellipsis = [char]0x2026
$appData = Join-Path $env:LOCALAPPDATA 'ImageStash'
$settingsFile = Join-Path $appData 'settings.json'
$stash = Join-Path $appData 'stash'
$storeFile = Join-Path $stash 'stash.db'
$tag = 'mood'

Add-Type -AssemblyName System.Drawing, System.Windows.Forms, UIAutomationClient, UIAutomationTypes

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class EarlierDesk
{
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    /// <summary>The window placed at (x, y) with its frame width by height, in physical pixels, as a person's drag would leave it.</summary>
    public static bool Place(IntPtr window, int x, int y, int width, int height)
    {
        // SWP_NOZORDER | SWP_NOACTIVATE.
        return SetWindowPos(window, IntPtr.Zero, x, y, width, height, 0x0004 | 0x0010);
    }
}
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

# The running build's elements of <type> that UI Automation finds under <root>, named <name> when one is given.
function Elements($root, [string] $name, $type) {
    $condition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $running),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, $type))
    @($root.FindAll([Windows.Automation.TreeScope]::Descendants, $condition) | Where-Object { (-not $name) -or ($_.Current.Name -eq $name) })
}

function One($found, [string] $what) {
    $all = @($found | Where-Object { $null -ne $_ })
    Need ($all.Count -eq 1) "$what is there once: $($all.Count) found"
    $all[0]
}

function Choose($element) { $element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }

# The window's pin, a toggle rather than a button: switched as a click would switch it.
function Flip($element) { $element.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle() }

function ClickOn($element) {
    $box = $element.Current.BoundingRectangle
    [PasteDesk]::Click([int]($box.X + $box.Width / 2), [int]($box.Y + $box.Height / 2))
}

function Query([string] $sql) { [CopyReadBack]::Query($storeFile, $sql) }

function Count([string] $sql) { [int](Query $sql) }

# The store's user version from its header, without SQLite (sqlite.org/fileformat.html, 1.3: bytes 60 to 63,
# big-endian), its rollback journal gone; -1 while there is none to read.
function StoreVersion {
    if (-not (Test-Path -LiteralPath $storeFile) -or (Test-Path -LiteralPath ($storeFile + '-journal'))) { return -1 }
    $stream = [IO.FileStream]::new($storeFile, 'Open', 'Read', 'ReadWrite, Delete')
    try {
        $header = New-Object byte[] 64
        if ($stream.Read($header, 0, 64) -lt 64) { return -1 }
        ($header[60] -shl 24) -bor ($header[61] -shl 16) -bor ($header[62] -shl 8) -bor $header[63]
    }
    finally { $stream.Dispose() }
}

function Settings { Get-Content -Raw -LiteralPath $settingsFile | ConvertFrom-Json }

function StashWindow { @([ScreenshotDesk]::Titled($running, 'ImageStash')) | Select-Object -First 1 }

function StashInFront { $window = StashWindow; ($null -ne $window) -and ([PasteDesk]::Foreground() -eq $window) }

# The running build's stash window brought to the front with its real open shortcut; its root element.
function Summon {
    if (-not (StashInFront)) { $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x12, 0x20)) }
    Need (WaitFor { StashInFront } 5000) 'the real Ctrl+Alt+Space put the stash window in front'
    [Windows.Automation.AutomationElement]::FromHandle((StashWindow))
}

# The words the window counts its listing with: "1 item", "3 items".
function Counted($root) { @(Elements $root '' $text | ForEach-Object { $_.Current.Name } | Where-Object { $_ -match '^\d+ items?\b' }) -join ' | ' }

# <query> typed into the emptied search box with real input; the window's root element once it counts <expected>.
function Search([string] $query, [string] $expected) {
    $root = Summon
    $box = One (Elements $root 'Search your stash' $edit) 'the search box'
    ClickOn $box
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x41))
    $null = [PasteDesk]::Press([uint16[]]@(0x08))
    $null = WaitFor { $box.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -eq '' } 2000
    $null = [OpenWithDesk]::Type($query)
    $found = WaitFor { ($box.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -eq $query) -and ((Counted $root) -like "$expected*") } 5000
    [pscustomobject]@{ Root = $root; Found = $found; Counted = (Counted $root) }
}

# The one item named <name> found and selected, the grid focused (G5, G7).
function SelectNamed([string] $name) {
    $searched = Search $name '1 item'
    Need $searched.Found "'$name' typed into the search box finds one item: $($searched.Counted)"
    $null = [PasteDesk]::Press([uint16[]]@(0x09))
    Start-Sleep -Milliseconds 300
    $searched.Root
}

function ChooseInTrayMenu([string] $name) {
    Need ([StashWindows]::RightClickTrayIcon($running)) 'a right-click posted to the tray icon'
    $null = WaitFor { (Elements $desktop 'Quit ImageStash' $menuItem).Count -gt 0 } 3000
    Choose (One (Elements $desktop $name $menuItem) "the tray menu's $name")
}

# A picture of one flat colour written as <file> under the phase's folder.
function Picture([string] $file, [int] $red, [int] $green, [int] $blue, $format) {
    $path = Join-Path $pictures $file
    $bitmap = New-Object Drawing.Bitmap 24, 16
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([Drawing.Color]::FromArgb(255, $red, $green, $blue))
    $graphics.Dispose()
    $bitmap.Save($path, $format)
    $bitmap.Dispose()
    $path
}

# Every file of the stash but the store's own and .work\'s, by its path below the folder, with its SHA-256, in order.
function PictureFiles {
    $root = $stash.TrimEnd('\') + '\'
    $sha = [Security.Cryptography.SHA256]::Create()
    @(Get-ChildItem -LiteralPath $root -Recurse -File -Force | ForEach-Object { $_.FullName.Substring($root.Length) } |
        Where-Object { ($_ -notlike '.work\*') -and ($_ -ne 'stash.db') -and ($_ -ne 'stash.db-journal') } | Sort-Object |
        ForEach-Object { "$_|" + [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($root + $_))) })
}

function StopEvery([string] $path) {
    foreach ($process in @(Get-Process -Name ImageStash.Windows -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $path })) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
}

if (-not $Store) {
    Write-Host "skipped: the stash and settings are the user's, under $appData, so the earlier-build phase, which replaces them with an earlier build's, does not run"
    exit 0
}
if (-not $EarlierCommit) {
    Write-Host 'skipped: no -EarlierCommit names a commit whose store is at version 1, so the earlier-build phase has no earlier build to run'
    exit 0
}
$desktop = [Windows.Automation.AutomationElement]::RootElement
$button = [Windows.Automation.ControlType]::Button
$text = [Windows.Automation.ControlType]::Text
$edit = [Windows.Automation.ControlType]::Edit
$menuItem = [Windows.Automation.ControlType]::MenuItem
$pointer = [PasteDesk]::Pointer()
$mine = Join-Path $Work ('earlier-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$source = Join-Path $mine 'source'
$publish = Join-Path $mine 'publish'
$pictures = Join-Path $mine 'pictures'
$earlierExe = Join-Path $publish 'ImageStash.Windows.exe'
$running = 0
try {
    # E1, G3: the earlier build runs alone, on a first run of its own.
    StopEvery $Exe
    Need (@(Get-Process -Name ImageStash.Windows -ErrorAction SilentlyContinue).Count -eq 0) 'no ImageStash runs before the earlier build starts'
    Remove-Item -LiteralPath $appData -Recurse -Force
    $null = New-Item -ItemType Directory -Path $source, $pictures

    # Only what a publish reads: the rest of an earlier commit may hold paths past MAX_PATH there.
    $repository = Split-Path $PSScriptRoot -Parent
    $archive = Join-Path $mine 'source.tar'
    & git -C $repository archive --format=tar -o $archive $earlierCommit Directory.Build.props src tools
    Need ($LASTEXITCODE -eq 0) "git archive took commit $earlierCommit's Directory.Build.props, src and tools"
    & tar -x -f $archive -C $source
    Need ($LASTEXITCODE -eq 0) 'the archive unpacked'
    # Its tools/publish.ps1's own command, leaving no build server or Avalonia's build-time collector (opted out as
    # its package's README says) running in the folder, which cleanup removes.
    $project = Join-Path $source 'src\ImageStash.Windows\ImageStash.Windows.csproj'
    $optedOut = $env:AVALONIA_TELEMETRY_OPTOUT
    $env:AVALONIA_TELEMETRY_OPTOUT = '1'
    try { $built = @(& dotnet publish $project -c Release -r win-x64 --self-contained -o $publish -nodeReuse:false -p:UseSharedCompilation=false 2>&1) }
    finally { $env:AVALONIA_TELEMETRY_OPTOUT = $optedOut }
    if ($LASTEXITCODE -ne 0) { $built | Select-Object -Last 8 | ForEach-Object { Write-Host "     $_" } }
    Need (($LASTEXITCODE -eq 0) -and (Test-Path -LiteralPath $earlierExe)) "commit $earlierCommit published as its tools/publish.ps1 publishes, self-contained for win-x64, into $publish"

    $earlier = Start-Process -FilePath $earlierExe -PassThru
    $running = $earlier.Id
    Need (WaitFor { (StoreVersion) -eq 1 -and (Test-Path -LiteralPath $settingsFile) } 20000) "the earlier build made its stash, $storeFile at user version 1, and settings.json"
    Need (WaitFor { [StashWindows]::Showing($running) -eq 'pinned' } 10000) "on its first run it shows its stash window pinned: '$([StashWindows]::Showing($running))'"

    # Three pictures of the phase's own, imported by the earlier build's own Ctrl+V of the files (G13, G12).
    $files = @(
        (Picture 'earlier-red.png' 200 40 40 ([Drawing.Imaging.ImageFormat]::Png)),
        (Picture 'earlier-green.png' 40 180 60 ([Drawing.Imaging.ImageFormat]::Png)),
        (Picture 'earlier-blue.jpg' 30 60 200 ([Drawing.Imaging.ImageFormat]::Jpeg)))
    $list = New-Object System.Collections.Specialized.StringCollection
    foreach ($file in $files) { $null = $list.Add($file) }
    [Windows.Forms.Clipboard]::SetFileDropList($list)
    $root = Summon
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x56))
    Need (WaitFor { (Count 'SELECT COUNT(*) FROM items') -eq 3 } 10000) "the earlier build's Ctrl+V of the three files added three items: $(Count 'SELECT COUNT(*) FROM items')"

    # Two tagged through the item menu's Add tag and Type a tag... (G17).
    # The first through the tag field, the second through the tag Add tag then lists.
    foreach ($name in 'earlier-red', 'earlier-green') {
        $null = SelectNamed $name
        $null = [PasteDesk]::Press([uint16[]]@(0x5D))
        $null = WaitFor { (Elements $desktop 'Add tag' $menuItem).Count -eq 1 } 3000
        ClickOn (One (Elements $desktop 'Add tag' $menuItem) "the item menu's Add tag")
        $entry = if ($name -eq 'earlier-red') { "Type a tag$ellipsis" } else { $tag }
        $null = WaitFor { (Elements $desktop $entry $menuItem).Count -eq 1 } 3000
        ClickOn (One (Elements $desktop $entry $menuItem) "Add tag's $entry")
        if ($name -eq 'earlier-red') {
            Need (WaitFor { [Windows.Automation.AutomationElement]::FocusedElement.Current.Name -eq 'Type a tag' } 3000) 'the tag field is focused'
            $null = [OpenWithDesk]::Type($tag)
            $null = [PasteDesk]::Press([uint16[]]@(0x0D))
        }
        Need (WaitFor { (Count "SELECT COUNT(*) FROM tags JOIN items ON items.id = tags.item_id WHERE tags.tag = '$tag' AND items.name = '$name'") -eq 1 } 5000) "'$name' carries the tag '$tag'"
        $null = [PasteDesk]::Press([uint16[]]@(0x1B))
        Start-Sleep -Milliseconds 300
    }

    # One favorite with Ctrl+D in the grid, one use with Ctrl+C (G17, G10).
    $null = SelectNamed 'earlier-blue'
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x44))
    Need (WaitFor { (Count "SELECT COUNT(*) FROM items WHERE favorite = 1 AND name = 'earlier-blue'") -eq 1 } 5000) "Ctrl+D made 'earlier-blue' a favorite"
    $null = SelectNamed 'earlier-green'
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x43))
    Need (WaitFor { (Count "SELECT COUNT(*) FROM uses JOIN items ON items.id = uses.item_id WHERE items.name = 'earlier-green'") -eq 1 } 5000) "Ctrl+C on 'earlier-green' recorded one use"

    # Settings off their defaults: the thumbnails stepped up, the pinned window moved and resized (G6, G2).
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0xBB))
    # Read only once its write is done: this build gives up on a write that a program reading the file refuses twice.
    Start-Sleep -Seconds 1
    Need (WaitFor { (Settings).thumbnailSize -eq 'large' } 2000) "Ctrl and + stepped the thumbnails up: thumbnailSize '$((Settings).thumbnailSize)'"
    $window = StashWindow
    # 760 by 580 units, above the current build's minimum, which the earlier build did not have, so the current one
    # keeps it as it is; placed in pixels, the size saved in units and the place in pixels (G2).
    $scale = [PasteDesk]::Scaling($window)
    Need ([EarlierDesk]::Place($window, 140, 90, [int](760 * $scale), [int](580 * $scale))) 'the pinned window moved and resized'
    $placed = [PasteDesk]::WindowRect($window)
    Need (WaitFor { $saved = Settings; ($saved.pinnedPosition.x -eq $placed[0]) -and ($saved.pinnedPosition.y -eq $placed[1]) -and ([math]::Abs($saved.pinnedSize.width * $scale - ($placed[2] - $placed[0])) -le 1) -and ([math]::Abs($saved.pinnedSize.height * $scale - ($placed[3] - $placed[1])) -le 1) } 3000) "settings.json holds the pinned window's place and size: $($placed -join ', ') at $scale pixels a unit"
    ChooseInTrayMenu 'Quit ImageStash'
    Need (WaitFor { $earlier.HasExited } 10000) "the earlier build's Quit ImageStash ended it"
    $running = 0

    # What the earlier build wrote, read before the current build sees it.
    $written = Settings
    Check ((StoreVersion) -eq 1) "the earlier build's stash.db reads user version 1 in its header (F11): $(StoreVersion)"
    Check (($written.version -eq 1) -and ($written.thumbnailSize -eq 'large') -and ($null -ne $written.pinnedSize) -and ($null -ne $written.pinnedPosition)) "its settings.json is version 1 with the thumbnail size, the pinned size and the pinned position off their defaults: $($written | ConvertTo-Json -Compress -Depth 5)"
    # Every column the earlier build's store has, read again from the migrated store, which only adds columns.
    $columns = Query "SELECT group_concat('quote(' || name || ')', ' || ''|'' || ') FROM pragma_table_info('items')"
    $itemsWritten = Query "SELECT group_concat(line, char(10)) FROM (SELECT $columns AS line FROM items ORDER BY id)"
    $picturesWritten = PictureFiles
    $tagsWritten = Query "SELECT group_concat(line, char(10)) FROM (SELECT item_id || '|' || tag AS line FROM tags ORDER BY item_id, tag)"
    $usesWritten = Query "SELECT group_concat(line, char(10)) FROM (SELECT item_id || '|' || used_at AS line FROM uses ORDER BY item_id, used_at)"

    # G31, F11, Done when 4: the current build opens them unchanged.
    $current = Start-Process -FilePath $Exe -PassThru
    $running = $current.Id
    Check (WaitFor { (StoreVersion) -eq 3 } 20000) "the current build migrated the store to user version 3, its journal gone (F11): $(StoreVersion)"
    Start-Sleep -Seconds 2
    Check (-not $current.HasExited) 'the current build keeps running over them'
    Check ((Query "SELECT group_concat(line, char(10)) FROM (SELECT $columns AS line FROM items ORDER BY id)") -eq $itemsWritten) "every item is as the earlier build recorded it, in every column its store has (G31, F11)"
    $pictures = PictureFiles
    Check (($pictures.Count -eq 3) -and (($pictures -join "`n") -eq ($picturesWritten -join "`n"))) "every picture file the earlier build wrote is there byte for byte (G31, G29): $($pictures.Count) of $($picturesWritten.Count)"
    Check (((Query "SELECT group_concat(line, char(10)) FROM (SELECT item_id || '|' || tag AS line FROM tags ORDER BY item_id, tag)") -eq $tagsWritten) -and ((Query "SELECT group_concat(line, char(10)) FROM (SELECT item_id || '|' || used_at AS line FROM uses ORDER BY item_id, used_at)") -eq $usesWritten)) 'every tag and use is as it recorded them (G31)'
    $now = Settings
    $changed = @($written.PSObject.Properties | Where-Object { ($now.PSObject.Properties.Name -notcontains $_.Name) -or (($now.($_.Name) | ConvertTo-Json -Compress -Depth 5) -ne ($_.Value | ConvertTo-Json -Compress -Depth 5)) } | ForEach-Object { $_.Name })
    Check ($changed.Count -eq 0) "settings.json keeps every value the earlier build wrote, version 1 among them (G31, State Settings): $(if ($changed.Count -gt 0) { 'changed ' + ($changed -join ', ') } else { 'all ' + @($written.PSObject.Properties).Count + ' kept' })"

    foreach ($name in 'earlier-red', 'earlier-green', 'earlier-blue') {
        $searched = Search $name '1 item'
        Check $searched.Found "the current build finds '$name' by its name (G5, G31): $($searched.Counted)"
    }
    $searched = Search $tag '2 items'
    Check $searched.Found "the search '$tag' finds the two the earlier build tagged (G5): $($searched.Counted)"
    $root = $searched.Root
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x41))
    $null = [PasteDesk]::Press([uint16[]]@(0x08))
    $null = WaitFor { (Elements $root "#$tag" $button).Count -eq 1 } 3000
    Choose (One (Elements $root "#$tag" $button) "the chip #$tag")
    Check (WaitFor { (Counted $root) -like '2 items*' } 3000) "its chip lists the two (G4): $(Counted $root)"
    $files = @{}
    foreach ($row in 'earlier-red', 'earlier-green', 'earlier-blue') { $files[$row] = Join-Path $stash (Query "SELECT finished_file FROM items WHERE name = '$row'") }
    Choose (One (Elements $root 'Recent' $button) 'the Recent chip')
    $null = WaitFor { (Counted $root) -like '1 item*' } 3000
    Set-Clipboard -Value 'words of the earlier-build phase'
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x43))
    Check (WaitFor { [CopyReadBack]::Clipboard() -like "files=$($files['earlier-green']) *" } 3000) "Recent lists the one the earlier build used, first, and Ctrl+C copies it (G4, G10): $(Counted $root); $([CopyReadBack]::Clipboard())"
    Choose (One (Elements $root 'Favorites' $button) 'the Favorites chip')
    $null = WaitFor { (Counted $root) -like '1 item*' } 3000
    Set-Clipboard -Value 'words of the earlier-build phase'
    $null = [PasteDesk]::Press([uint16[]]@(0x11, 0x43))
    Check (WaitFor { [CopyReadBack]::Clipboard() -like "files=$($files['earlier-blue']) *" } 3000) "Favorites lists the earlier build's favorite, and Ctrl+C copies it (G4, G17): $(Counted $root); $([CopyReadBack]::Clipboard())"
    Flip (One (Elements $root 'Keep open' $button) 'the pin')
    $back = WaitFor { $rect = [PasteDesk]::WindowRect((StashWindow)); ($rect[0] -eq $placed[0]) -and ($rect[1] -eq $placed[1]) -and ($rect[2] -eq $placed[2]) -and ($rect[3] -eq $placed[3]) } 3000
    Check $back "pinned, the window takes the place and size the earlier build saved (G2, G31): $(([PasteDesk]::WindowRect((StashWindow))) -join ', ') against $($placed -join ', ')"
    # The grid draws its tiles itself, with nothing UI Automation can measure: their size on screen is the person's.
    Check ((Settings).thumbnailSize -eq 'large') 'settings.json still holds the thumbnail size the earlier build saved (G6, G31)'
    foreach ($shown in @([ScreenshotDesk]::Titled($running, 'ImageStash'))) { [PasteDesk]::Close($shown) }
}
catch {
    Write-Host "FAIL the earlier-build phase stopped: $($_.Exception.Message)"
    $failed++
}
finally {
    try { StopEvery $earlierExe } catch { }
    [PasteDesk]::MovePointer($pointer[0], $pointer[1])
    # The earlier build's source and publish, tried again while a process that just ended still holds a file.
    for ($attempt = 0; ($attempt -lt 20) -and (Test-Path -LiteralPath $mine); $attempt++) {
        # The long-path form: the build's own folders below it pass MAX_PATH.
        try { [IO.Directory]::Delete('\\?\' + $mine, $true) } catch { Start-Sleep -Milliseconds 500 }
    }
}
exit $failed
