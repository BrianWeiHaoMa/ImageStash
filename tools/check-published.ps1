<#
.SYNOPSIS
Checks the published folder the way a user meets it: publishes into %TEMP%, copies the folder elsewhere, runs the
copy, launches it a second time, summons its window over a stash of 5,000 pictures that the focused check builds, then
has check-published-paste.ps1 drive the paste moment into a window of its own (the tray's posted click and Enter, the
real Ctrl+Alt+Space and Enter, a target closed before Enter, a shortcut another program holds) and
check-published-paste-in.ps1 paste a copied picture into the stash with the real Ctrl+V, then has
check-published-screenshot.ps1 start Take a screenshot by each of its three ways in and cancel it, and
check-published-capture.ps1 start Record a clip from the tray and the + menu and cancel it, then stash the clipboard's
image, take a screenshot, select waiting regions, start two clips by tray and shortcut, then cancel a third region
while waiting (G15, G34, F5, F12, F4, G3, E3), and check-published-editor.ps1 caption a screenshot through its card's
Edit and trim a recording through its card's Trim, each saved and read back (G16, G20, G21, G22, G26), and
check-published-open-with.ps1 open the shell's Open-with dialog from the item menu and the preview's More options menu,
write over the copies as another program would, and read back each adoption, its Undo, its expiry and an undecodable
rewrite refused (G27, G2, G19, G29, G35), and check-published-settings.ps1 open the Settings page from the tray, have
its shortcut fields refuse a combination another program holds, one that types a character and Ctrl+V, remap the
screenshot shortcut, open it again on a shortcut another program took, switch Start with Windows off and on, set Theme
and open the stash folder through Open folder (G32, G33, G34, F1, F5, F10, G35), and check-published-move.ps1 have
Move... refuse a folder that is not empty, the Open with... folder and the stash itself, then carry the stash three
times, waiting for a recording, a Delete's Undo and an edit and refusing a capture and a paste meanwhile, and open it
at its new place after a restart (G30, G32, G35, R9, E2), and, last, check-published-earlier-build.ps1 publish the
commit -EarlierCommit names, have that build fill a stash and save settings, and open them with the copy (G31, F11),
and cleans up everything it made. Exits 0 when every check held.

Runs against the signed-in user's real %LOCALAPPDATA%\ImageStash, Run key, clipboard and Recycle Bin; it refuses to
start while ImageStash runs, and puts back what it found there, except the clipboard, which keeps the copy. While that
folder exists it skips the first-run checks, the stash folder that cannot be created, the 5,000-picture stash, and the
paste phases, captures, edits and adoptions that would record a use in the user's stash, add an item to it or replace
an item's picture, the Settings phase, which would change the user's settings, and the Move and earlier-build phases,
which would move or replace the user's stash, printing a line beginning "skipped:" for each, and still exits 0 when
every check it ran held. Without -EarlierCommit, a commit this repository holds whose store is at version 1, the
earlier-build phase is skipped too; this repository's own history begins at store version 3, so it holds no such commit.

.EXAMPLE
powershell.exe -NoProfile -File tools/check-published.ps1
#>
param([string] $EarlierCommit = '')
$ErrorActionPreference = 'Stop'

$work = Join-Path $env:TEMP ('ImageStash-published-check-' + [guid]::NewGuid().ToString('N'))
$publish = Join-Path $work 'publish'
$copy = Join-Path $work 'copy'
$exe = Join-Path $copy 'ImageStash.Windows.exe'
$appData = Join-Path $env:LOCALAPPDATA 'ImageStash'
$settingsFile = Join-Path $appData 'settings.json'
$stash = Join-Path $appData 'stash'
$storeFile = Join-Path $stash 'stash.db'
$openWithMarker = Join-Path $appData 'open-with\README.txt'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$failures = New-Object System.Collections.Generic.List[string]

# The process's top-level windows as Windows sees them: its visible ones, and Avalonia's hidden platform window, which
# receives the tray icon's messages (Avalonia 12.1.4 TrayIconImpl: WM_USER + 1024, wParam the icon's ID, 1 for the
# process's first and only icon, lParam the mouse message).
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class StashWindows
{
    private delegate bool EnumProc(IntPtr window, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr window, StringBuilder name, int length);
    [DllImport("user32.dll")] private static extern int GetWindowLongW(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static List<IntPtr> Of(int processId)
    {
        var found = new List<IntPtr>();
        EnumWindows(delegate (IntPtr window, IntPtr data)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == processId) { found.Add(window); }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Each visible window: "pinned" for one with a title bar, "flyout" for one without, joined by commas; "" for none.</summary>
    public static string Showing(int processId)
    {
        var shown = new List<string>();
        foreach (var window in Of(processId))
        {
            if (IsWindowVisible(window))
            {
                // GWL_STYLE has WS_CAPTION.
                shown.Add((GetWindowLongW(window, -16) & 0x00C00000) == 0x00C00000 ? "pinned" : "flyout");
            }
        }
        return string.Join(",", shown);
    }

    private static IntPtr PlatformWindow(int processId)
    {
        var platform = IntPtr.Zero;
        foreach (var window in Of(processId))
        {
            var name = new StringBuilder(256);
            GetClassNameW(window, name, name.Capacity);
            if (name.ToString().StartsWith("AvaloniaMessageWindow ")) { platform = window; }
        }
        return platform;
    }

    /// <summary>Posts a left click on the tray icon and returns the milliseconds until a window shows, or -1.</summary>
    public static long ClickTrayIcon(int processId, int timeoutMs)
    {
        var platform = PlatformWindow(processId);
        if (platform == IntPtr.Zero) { return -1; }
        var clock = Stopwatch.StartNew();
        PostMessageW(platform, 0x0400 + 1024, new IntPtr(1), new IntPtr(0x0202));
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            if (Showing(processId) != "") { return clock.ElapsedMilliseconds; }
            Thread.Sleep(5);
        }
        return -1;
    }

    /// <summary>Posts a right click on the tray icon, which opens its menu; false when the process has no platform window.</summary>
    public static bool RightClickTrayIcon(int processId)
    {
        var platform = PlatformWindow(processId);
        return platform != IntPtr.Zero && PostMessageW(platform, 0x0400 + 1024, new IntPtr(1), new IntPtr(0x0205));
    }

    /// <summary>Posts Enter, down and up, to the process's visible window; false when none shows.</summary>
    public static bool PostEnter(int processId)
    {
        foreach (var window in Of(processId))
        {
            if (!IsWindowVisible(window)) { continue; }
            // WM_KEYDOWN and WM_KEYUP for VK_RETURN, its scan code 0x1C in the key data.
            PostMessageW(window, 0x0100, new IntPtr(0x0D), new IntPtr(0x001C0001));
            PostMessageW(window, 0x0101, new IntPtr(0x0D), new IntPtr(unchecked((int)0xC01C0001)));
            return true;
        }
        return false;
    }
}

// What the check reads back after the copy: the clipboard's formats as a paste finds them, and the store's rows through
// the SQLite Windows itself carries (winsqlite3.dll), opened read-only.
public static class CopyReadBack
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint DragQueryFileW(IntPtr drop, uint index, StringBuilder file, uint length);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_open_v2")] private static extern int Open(byte[] path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_busy_timeout")] private static extern int BusyTimeout(IntPtr db, int ms);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_prepare_v2")] private static extern int Prepare(IntPtr db, byte[] sql, int length, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_step")] private static extern int Step(IntPtr statement);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_text")] private static extern IntPtr ColumnText(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_column_bytes")] private static extern int ColumnBytes(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_finalize")] private static extern int Finalize(IntPtr statement);
    [DllImport("winsqlite3.dll", EntryPoint = "sqlite3_close")] private static extern int Close(IntPtr db);

    /// <summary>"files=a|b png=True dibv5=True effect=1", or "unopened" when the clipboard stayed held.</summary>
    public static string Clipboard()
    {
        for (var attempt = 0; !OpenClipboard(IntPtr.Zero); attempt++)
        {
            if (attempt == 40) { return "unopened"; }
            Thread.Sleep(50);
        }
        try
        {
            var files = new List<string>();
            var drop = GetClipboardData(15);
            if (drop != IntPtr.Zero)
            {
                var count = DragQueryFileW(drop, 0xFFFFFFFF, null, 0);
                for (uint i = 0; i < count; i++)
                {
                    var name = new StringBuilder(1024);
                    DragQueryFileW(drop, i, name, (uint)name.Capacity);
                    files.Add(name.ToString());
                }
            }
            var effect = -1;
            var memory = GetClipboardData(RegisterClipboardFormatW("Preferred DropEffect"));
            if (memory != IntPtr.Zero)
            {
                effect = Marshal.ReadInt32(GlobalLock(memory));
                GlobalUnlock(memory);
            }
            return string.Format("files={0} png={1} dibv5={2} effect={3}", string.Join("|", files),
                IsClipboardFormatAvailable(RegisterClipboardFormatW("PNG")), IsClipboardFormatAvailable(17), effect);
        }
        finally { CloseClipboard(); }
    }

    /// <summary>The first column of the first row <paramref name="sql"/> returns from the store, as text; null for no row.</summary>
    public static string Query(string store, string sql)
    {
        IntPtr db;
        // SQLITE_OPEN_READONLY; a store write in progress is waited for, as SQLite waits.
        if (Open(Encoding.UTF8.GetBytes(store + "\0"), out db, 1, IntPtr.Zero) != 0) { Close(db); throw new InvalidOperationException("the store did not open"); }
        try
        {
            BusyTimeout(db, 2000);
            IntPtr statement;
            if (Prepare(db, Encoding.UTF8.GetBytes(sql + "\0"), -1, out statement, IntPtr.Zero) != 0) { throw new InvalidOperationException("the query did not prepare: " + sql); }
            try
            {
                var stepped = Step(statement);
                if (stepped == 101) { return null; }
                if (stepped != 100) { throw new InvalidOperationException("the query failed with " + stepped + ": " + sql); }
                var bytes = new byte[ColumnBytes(statement, 0)];
                Marshal.Copy(ColumnText(statement, 0), bytes, 0, bytes.Length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally { Finalize(statement); }
        }
        finally { Close(db); }
    }
}
'@

function Check([bool] $held, [string] $what) {
    if ($held) { Write-Host "ok   $what" } else { Write-Host "FAIL $what"; $failures.Add($what) }
}

function RunValue {
    $entry = Get-ItemProperty -Path $runKey -Name ImageStash -ErrorAction SilentlyContinue
    if ($entry) { $entry.ImageStash } else { $null }
}

# The Run value as stored: its registry type and its data with environment variables left unexpanded.
function StoredRunValue {
    $key = Get-Item -Path $runKey
    if ($key.GetValueNames() -notcontains 'ImageStash') { return $null }
    @{ Kind = $key.GetValueKind('ImageStash'); Data = $key.GetValue('ImageStash', $null, 'DoNotExpandEnvironmentNames') }
}

function CopyProcesses {
    @(Get-Process -Name ImageStash.Windows -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
}

function WaitFor([scriptblock] $condition, [int] $seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 200
    }
    return [bool](& $condition)
}

# The store's header without SQLite (sqlite.org/fileformat.html, 1.3): byte 18 is 1 for a rollback journal and 2 for
# WAL; bytes 60 to 63 are the user version, big-endian. Read sharing what the running store holds open.
function StoreHeader {
    if (-not (Test-Path $storeFile)) { return $null }
    $stream = [IO.FileStream]::new($storeFile, 'Open', 'Read', 'ReadWrite, Delete')
    try {
        $header = New-Object byte[] 64
        if ($stream.Read($header, 0, 64) -lt 64) { return $null }
        $header
    }
    finally { $stream.Dispose() }
}

# The version a store this build creates carries: StoreSchema.CurrentVersion.
$storeVersion = 3

# A rollback-journal commit is complete only once its journal is deleted, after the header already reads the new version.
function StoreAtCurrentVersion {
    $header = StoreHeader
    ($null -ne $header) -and ($header[18] -eq 1) -and ($header[60] -eq 0) -and ($header[61] -eq 0) -and ($header[62] -eq 0) -and ($header[63] -eq $storeVersion) -and
        -not (Test-Path ($storeFile + '-journal'))
}

function Snapshot {
    Get-ChildItem -Path $appData -Recurse -Force | Sort-Object FullName |
        ForEach-Object { '{0}|{1}|{2}' -f $_.FullName, $_.LastWriteTimeUtc.Ticks, $_.Length } | Out-String
}

if (Get-Process -Name ImageStash.Windows -ErrorAction SilentlyContinue) {
    Write-Host 'ImageStash is running; quit it from its tray menu, then run this check again.'
    exit 1
}

$createdAppData = -not (Test-Path $appData)
$foundInAppData = @()
if (-not $createdAppData) {
    $foundInAppData = @(Get-ChildItem -Path $appData -Recurse -Force | ForEach-Object { $_.FullName })
}
$runBefore = StoredRunValue

# What start must do with the Run value: the default turns it on; a user's existing settings may turn it off, and
# settings that cannot be read leave it alone, so nothing about it is checked then.
$expectRun = $true
if (Test-Path $settingsFile) {
    try {
        $expectRun = (Get-Content -Raw -Path $settingsFile | ConvertFrom-Json).startWithWindows -ne $false
    }
    catch {
        $expectRun = $null
    }
}

function RunAsExpected {
    if ($null -eq $expectRun) { return $true }
    if ($expectRun) { return (RunValue) -eq $expectedRun }
    return $null -eq (RunValue)
}

try {
    & (Join-Path $PSScriptRoot 'publish.ps1') -Output $publish | Out-Host
    Write-Host "publish folder: $publish"
    Check (Test-Path (Join-Path $publish 'ImageStash.Windows.exe')) 'the folder holds ImageStash.Windows.exe'
    Check (Test-Path (Join-Path $publish 'coreclr.dll')) 'the folder holds the .NET runtime (coreclr.dll), so it is self-contained'

    Copy-Item -Path $publish -Destination $copy -Recurse
    $first = Start-Process -FilePath $exe -PassThru
    Write-Host "launched a copy of the folder: $exe"
    $expectedRun = '"' + $exe + '"'
    $settled = WaitFor { (Test-Path $settingsFile) -and (RunAsExpected) } 20
    if ($null -eq $expectRun) {
        Write-Host "skipped: $settingsFile could not be read, so ImageStash leaves the Run value as it is"
    }
    elseif ($expectRun) {
        Check $settled "the Run value ImageStash names the copy's executable: $(RunValue)"
    }
    else {
        Check $settled 'the Run value ImageStash is absent, as the existing settings turn Start with Windows off'
    }
    Start-Sleep -Seconds 1
    Check (-not $first.HasExited) 'the first launch keeps running'
    if ($createdAppData) {
        Check (WaitFor { [StashWindows]::Showing($first.Id) -eq 'pinned' } 20) "on a first run with an empty stash the first launch shows one window, the stash window pinned with a title bar, and no other (G2, F4): '$([StashWindows]::Showing($first.Id))'"
    }
    else {
        Write-Host "skipped: folder existed ($appData), so whether this is a first run is the user's; showing: '$([StashWindows]::Showing($first.Id))'"
    }

    if ($createdAppData) {
        $settings = Get-Content -Raw -Path $settingsFile | ConvertFrom-Json
        $defaults = ($settings.version -eq 1) -and
            ($settings.shortcuts.open -eq 'Ctrl+Alt+Space') -and ($settings.shortcuts.screenshot -eq 'Ctrl+PrtScn') -and
            ($settings.shortcuts.record -eq 'Ctrl+Shift+PrtScn') -and ($settings.shortcuts.stashClipboardImage -eq 'Ctrl+Alt+V') -and
            ($settings.alsoCopyCapture -eq $true) -and ($settings.recordingMaxSeconds -eq 30) -and ($settings.recordPointer -eq $true) -and
            ($null -eq $settings.stashFolder) -and ($settings.startWithWindows -eq $true) -and ($settings.theme -eq 'system')
        Check $defaults "$settingsFile holds G32's defaults"
    }
    else {
        Write-Host "skipped: folder existed ($appData), so its settings are the user's, not the defaults"
    }

    if ($createdAppData) {
        Check (WaitFor { StoreAtCurrentVersion } 20) "$storeFile exists as a rollback-journal store (header byte 18 is 1) at user version $storeVersion (bytes 60 to 63), its first commit complete"
        Check ((Test-Path (Join-Path $stash 'originals') -PathType Container) -and (Test-Path (Join-Path $stash '.work') -PathType Container)) "$stash holds originals\ and .work\"
        Check (@(Get-ChildItem -Path (Join-Path $stash '.work') -Force -ErrorAction SilentlyContinue).Count -eq 0) "$stash\.work\ is empty"
    }
    else {
        Write-Host "skipped: folder existed ($appData), so the stash there is the user's"
    }
    # Every start makes the Open with folder and its marker where missing, so the folder is never empty (G30, R8).
    Check (WaitFor { Test-Path $openWithMarker -PathType Leaf } 20) "the start made $openWithMarker"

    # The file system refreshes a folder's listed times lazily after the first launch's own writes, so the comparison
    # starts from a listing that has stopped changing.
    $before = Snapshot
    $deadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 500
        $previous = $before
        $before = Snapshot
    } while (($before -ne $previous) -and ((Get-Date) -lt $deadline))
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $second = Start-Process -FilePath $exe -PassThru
    $null = $second.Handle
    $exited = $second.WaitForExit(5000)
    Check ($exited -and $second.ExitCode -eq 0) ("a second launch exited with 0 within five seconds (after {0} ms)" -f $clock.ElapsedMilliseconds)
    Check ((CopyProcesses).Count -eq 1) 'one ImageStash.Windows process remained'
    $expectShown = if ($createdAppData) { 'pinned' } else { 'flyout' }
    Check (WaitFor { [StashWindows]::Showing($first.Id) -eq $expectShown } 5) "the second launch had the running instance show its window as a tray click would, the $expectShown one, and no other (G3, F4): '$([StashWindows]::Showing($first.Id))'"
    $after = Snapshot
    Check ($after -eq $before) "the second launch changed no file under $appData"
    if ($after -ne $before) {
        Compare-Object ($before -split "`r?`n") ($after -split "`r?`n") | ForEach-Object { Write-Host "     $($_.SideIndicator) $($_.InputObject)" }
    }

    Stop-Process -Id $first.Id -Force
    $first.WaitForExit()
    Remove-ItemProperty -Path $runKey -Name ImageStash -ErrorAction SilentlyContinue
    if ($createdAppData) { Remove-Item -Path $openWithMarker -Force }
    $fresh = Start-Process -FilePath $exe -PassThru
    if ($expectRun) {
        Check (WaitFor { (RunValue) -eq $expectedRun } 20) 'after the first ended, a fresh launch runs and writes the Run value again at its start'
    }
    else {
        Start-Sleep -Seconds 3
    }
    Check (-not $fresh.HasExited) 'after the first ended, a fresh launch keeps running'
    Check (WaitFor { Test-Path $openWithMarker -PathType Leaf } 20) "after the first ended, the fresh launch made $openWithMarker again at its start"
    $running = $fresh
    $pasteStore = ''

    if ($createdAppData) {
        # A stash folder that cannot be created: start names it in the tray tooltip and keeps running with Quit (G35).
        Stop-Process -Id $fresh.Id -Force
        $fresh.WaitForExit()
        $blocker = Join-Path $appData 'blocker'
        Set-Content -Path $blocker -Value 'a file where a folder should be'
        $settings = Get-Content -Raw -Path $settingsFile | ConvertFrom-Json
        $settings.stashFolder = Join-Path $blocker 'stash'
        $settings | ConvertTo-Json -Depth 5 | Set-Content -Path $settingsFile -Encoding UTF8
        # Gone, so a start that ignored the setting and opened the default location would show by recreating it.
        Remove-Item -Path $stash -Recurse -Force
        $blocked = Start-Process -FilePath $exe -PassThru
        Start-Sleep -Seconds 3
        Check (-not $blocked.HasExited) 'with a stash folder that cannot be created, ImageStash keeps running'
        Check ((Get-Item $blocker) -is [IO.FileInfo]) 'the file in the way of the stash folder is left as it was'
        Check (-not (Test-Path $stash)) 'it opened no stash at the default location instead'

        # E4 with G1: 5,000 pictures in the stash the focused check builds and leaves here when asked; settings from
        # an earlier run, so start shows no window; then the tray icon's click shows it within the second.
        Stop-Process -Id $blocked.Id -Force
        $blocked.WaitForExit()
        Remove-Item -Path $blocker -Force
        $large = Join-Path $work 'stash-of-5000'
        $env:IMAGESTASH_FIVE_THOUSAND_STASH = $large
        try {
            & dotnet test (Join-Path (Split-Path $PSScriptRoot -Parent) 'tests\ImageStash.Tests') --filter 'FullyQualifiedName~FiveThousandPictures' | Out-Host
        }
        finally {
            Remove-Item -Path Env:\IMAGESTASH_FIVE_THOUSAND_STASH
        }
        Check (Test-Path (Join-Path $large 'stash.db')) "the 5,000-picture check left its stash at $large"
        $settings = Get-Content -Raw -Path $settingsFile | ConvertFrom-Json
        $settings.stashFolder = $large
        $settings | ConvertTo-Json -Depth 5 | Set-Content -Path $settingsFile -Encoding UTF8
        $big = Start-Process -FilePath $exe -PassThru
        # The stash opens at start, before a summon reads it (State: Every change); 5,000 rows take a moment.
        Start-Sleep -Seconds 3
        Check ((-not $big.HasExited) -and ([StashWindows]::Showing($big.Id) -eq '')) 'over 5,000 pictures, with settings from an earlier run, start shows no window'
        $running = $big
        $pasteStore = Join-Path $large 'stash.db'
    }
    else {
        Write-Host "skipped: folder existed ($appData), so a stash folder that cannot be created and the 5,000-picture stash are not tried"
    }

    # G1, G8, G10, G16, G33 and N3 on the running copy: over the check's own 5,000 pictures, or over the user's stash
    # without the phases that record a use. Its lines are printed as it goes; its exit counts what failed.
    & (Join-Path $PSScriptRoot 'check-published-paste.ps1') -Exe $exe -Running $running.Id -Store $pasteStore -Work $work
    Check ($LASTEXITCODE -eq 0) "the paste phases held: $LASTEXITCODE of them failed"

    # G13, G12, F12, G7 on the running copy: a copied picture pasted into the check's own 5,000 pictures with the real
    # Ctrl+V; over the user's stash it is skipped, since it adds an item there.
    & (Join-Path $PSScriptRoot 'check-published-paste-in.ps1') -Exe $exe -Store $pasteStore
    Check ($LASTEXITCODE -eq 0) "the paste-in phase held: $LASTEXITCODE of its checks failed"

    # F5, G14, G34, G2, G3 on the running copy: Take a screenshot from the real Ctrl+PrtScn, the tray menu and the
    # flyout's + menu, each capture cancelled, so it adds nothing and runs over the user's stash too.
    & (Join-Path $PSScriptRoot 'check-published-screenshot.ps1') -Exe $exe -Store $pasteStore
    Check ($LASTEXITCODE -eq 0) "the screenshot phase held: $LASTEXITCODE of its checks failed"

    # F5, G13, G14, G15, G16, G34, G2, G3, F4 on the running copy: Record a clip's tray and + menu entries, cancelled;
    # over the check's own stash, the three captures from their real shortcuts, saved; over the user's it is skipped.
    & (Join-Path $PSScriptRoot 'check-published-capture.ps1') -Exe $exe -Store $pasteStore
    Check ($LASTEXITCODE -eq 0) "the capture phase held: $LASTEXITCODE of its checks failed"

    # G16, G20, G21, G22, G26, F3 on the running copy: a screenshot captioned through its card's Edit and a recording
    # trimmed through its card's Trim, saved and read back; over the user's stash it is skipped, since it adds items there.
    & (Join-Path $PSScriptRoot 'check-published-editor.ps1') -Exe $exe -Store $pasteStore
    Check ($LASTEXITCODE -eq 0) "the editor phase held: $LASTEXITCODE of its checks failed"

    # G27, G2, G19, G29, G35, R9 on the running copy: Open with... on an unedited still and on the editor phase's edited
    # item, a script writing over the copies; over the user's stash it is skipped, since it replaces items' pictures there.
    & (Join-Path $PSScriptRoot 'check-published-open-with.ps1') -Exe $exe -Store $pasteStore
    Check ($LASTEXITCODE -eq 0) "the Open with... phase held: $LASTEXITCODE of its checks failed"

    # G32, G33, G34, F1, F5, F10, G35 on the running copy: the Settings page from the tray, its shortcut fields refusing
    # and remapping, Start with Windows, Theme and Open folder; over the user's settings it is skipped, since it changes them.
    & (Join-Path $PSScriptRoot 'check-published-settings.ps1') -Exe $exe -Store $pasteStore
    Check ($LASTEXITCODE -eq 0) "the Settings phase held: $LASTEXITCODE of its checks failed"

    # G30, G32, G35, R9, G16, G18, G20, E2 on the running copy: Move... refused three folders, then carrying the stash
    # three times, past a recording, a Delete's Undo and an edit; over the user's stash it is skipped, since it moves it.
    & (Join-Path $PSScriptRoot 'check-published-move.ps1') -Exe $exe -Store $pasteStore -Work $work
    Check ($LASTEXITCODE -eq 0) "the Move phase held: $LASTEXITCODE of its checks failed"

    # G31, F11, Done when 4, last: the copy stopped, an earlier build fills a stash and saves settings, and the copy
    # opens them; over the user's stash and settings it is skipped, since it replaces them.
    & (Join-Path $PSScriptRoot 'check-published-earlier-build.ps1') -Exe $exe -Store $pasteStore -Work $work -EarlierCommit $EarlierCommit
    Check ($LASTEXITCODE -eq 0) "the earlier-build phase held: $LASTEXITCODE of its checks failed"
}
finally {
    Write-Host 'cleanup:'
    foreach ($process in CopyProcesses) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
        Write-Host "  stopped process $($process.Id)"
    }
    # The product wrote this entry for the copy; put back what was there before the check.
    if ($null -ne $runBefore) {
        New-ItemProperty -Path $runKey -Name ImageStash -PropertyType $runBefore.Kind -Value $runBefore.Data -Force | Out-Null
        Write-Host '  restored the Run value ImageStash the check found'
    }
    elseif ($null -ne (RunValue)) {
        Remove-ItemProperty -Path $runKey -Name ImageStash
        Write-Host '  removed the Run value ImageStash the product wrote'
    }
    if (Test-Path $work) {
        Remove-Item -Path $work -Recurse -Force
        Write-Host "  removed the copy and the publish folder ($work)"
    }
    if ($createdAppData -and (Test-Path $appData)) {
        Remove-Item -Path $appData -Recurse -Force
        Write-Host "  removed $appData, which the check created"
    }
    elseif (-not $createdAppData) {
        # Deepest first, so a folder the check created is empty of what the check made before it goes.
        $made = @(Get-ChildItem -Path $appData -Recurse -Force | Where-Object { $foundInAppData -notcontains $_.FullName } |
            Sort-Object { $_.FullName.Length } -Descending)
        foreach ($item in $made) {
            Remove-Item -LiteralPath $item.FullName -Recurse -Force
            Write-Host "  removed $($item.FullName), which the check created"
        }
    }
}

Write-Host ''
Write-Host 'A person observes, with a published copy running:'
Write-Host '  1. the ImageStash icon is in the notification area, among the hidden icons as a new icon is;'
Write-Host '  2. a right-click on it opens a menu holding Open ImageStash beside Ctrl+Alt+Space, Take a screenshot beside Ctrl+PrtScn, Record a clip beside Ctrl+Shift+PrtScn, Stash clipboard image beside Ctrl+Alt+V, Pause shortcuts, Settings and Quit ImageStash; Quit ImageStash ends the process and removes the icon (G34);'
Write-Host '  3. on a first run the window opens pinned with "Your stash is empty", dropping files, Paste a copied image with Ctrl+V, Take a screenshot with Ctrl+PrtScn, Record a clip with'
Write-Host '     Ctrl+Shift+PrtScn, Stash the clipboard image with Ctrl+Alt+V and Import files with Ctrl+O; it takes a drop;'
Write-Host '     the look is Paper, Fluent with Segoe UI Variable text and the system''s accent, in the system''s theme, light and dark; a focused'
Write-Host '     field keeps its grey edge all round with its bottom edge in the accent, and a selected tile''s ring stands a little off the tile (G2, F10);'
Write-Host '  4. a left-click on the icon, Open ImageStash, and launching the executable again each show the window above the icon within a'
Write-Host '     second, the search box focused with its text selected, showing what it was left on (Recent at the first show, or All while Recent'
Write-Host '     is empty, with its first item selected); with the pinned window behind another window, Open ImageStash brings it to the front where it'
Write-Host '     is, every time; Close hides it and ImageStash keeps running (G1, G3, G34);'
Write-Host '  5. one drop from File Explorer of a JPG, a transparent PNG, an animated GIF, an animated WebP, an MP4 and a link: the card counts four'
Write-Host '     and two before release; four are added once, selected, first in All, and the MP4 and the link are refused by name; the same GIF'
Write-Host '     again is already in the stash; Undo of a two-file drop leaves the four; a picture dragged out of Edge or Chrome is added, and a'
Write-Host '     link dragged from the address bar is refused with nothing fetched: record what each arrived as (G11, G12, N2, N4);'
Write-Host '  6. Ctrl+O and the + menu''s Import files... open the picker, the flyout stays up while it is open, and the chosen files are added (G2, G12);'
Write-Host '  7. the import''s notice stays six seconds, longer while hovered, keeps running while the flyout is hidden, and its Undo acts until it goes (G16);'
Write-Host '  8. the flyout hides when another app takes focus and the pinned window stays; Esc clears the search, then hides the flyout; typing'
Write-Host '     searches, Up and Down move the selection from the box and Left and Right its caret, Tab reaches the grid; a tag chip adds its word to the'
Write-Host '     search in place of a typed word the tag contains (typing jo keeps the chips joy and joyous in the row, and a click on joy makes the search'
Write-Host '     joy), several narrowing together, a lit one takes its word out, and when the row cannot hold them all a chip at its right edge, which'
Write-Host '     stays put as the other chips come and go, lists every tag; the pinned size and place come back, also after a minimise from its title bar'
Write-Host '     and a Quit while minimised, on either display; the pinned window''s top or left edge dragged past its minimum stops there and the window'
Write-Host '     does not move,'
Write-Host '     on either display (G2, G4, G5, G7, G17, G20, G21);'
Write-Host '  9. an animated tile plays while hovered or selected, one at a time, and stops when the pointer leaves; Ctrl+wheel and Ctrl with + or -'
Write-Host '     step the thumbnails through three sizes, the same size after Quit and a new start (G6);'
Write-Host ' 10. over 5,000 pictures, typing in the search answers as the keys are pressed (E4): set IMAGESTASH_FIVE_THOUSAND_STASH to an empty'
Write-Host '     folder, run the focused check FiveThousandPictures, and point settings.json''s stashFolder at that folder;'
Write-Host ' 11. with a stash folder that cannot be created, the icon''s tooltip reads "Open the stash failed: ..." and Quit still works;'
Write-Host " 12. from a real click on the tray the footer reads ""Enter $([char]0xB7) Copy""; with the transparent PNG selected, Enter: a paste into an Explorer folder makes a copy of"
Write-Host '     its file, and Paint and Word paste the picture, record whether its transparency shows; the window stays and Recent lists it'
Write-Host '     first; a double-click and the item menu''s Copy do the same; the animated GIF, the animated WebP and three selected items paste'
Write-Host '     into a folder as their files (G8, G10, G4);'
Write-Host ' 13. Ctrl+C from the search box and from the grid copies the same, and the window stays (G7, G9);'
Write-Host ' 14. a tile dragged into an Explorer folder lands there as a copy, the stash file stays, and the flyout stays up until the drop; three'
Write-Host '     selected tiles land as three files; Esc during a drag lands nothing and Recent does not change; a tile dragged into Microsoft'
Write-Host '     Teams'' composer: record what Teams shows (G9, G2, G10);'
Write-Host ' 15. a press on one of three selected tiles, released without moving, selects that tile alone (G17);'
Write-Host ' 16. from a real click on the tray, a right-click on an unselected tile selects it and its menu opens at the pointer, reading Copy (Enter), Preview (Space),'
Write-Host '     Add to Favorites (Ctrl+D), Add tag, Save a copy..., Reveal in File Explorer, Delete (Del), with Remove tag after Add tag while the tile carries a'
Write-Host '     tag; on one of three selected tiles the selection stays and the menu reads Copy, Add to Favorites, Add tag, Delete 3 items; Add'
Write-Host '     tag''s submenu offers every tag and Type a tag..., which shows the tag field over the grid with its suggestions and, while nothing is'
Write-Host '     typed, offers every tag on one click, the commonest first, marking the tags every selected item carries, a pick giving the tag to every'
Write-Host '     selected item or taking it off all of them when all carry it, the field following at once; Ctrl+V there with words on the clipboard'
Write-Host '     puts them in the box as typed and Enter adds each as a tag; Esc with that list open closes the list alone, in the preview too, and the'
Write-Host '     next Esc the field or the preview;'
Write-Host '     Delete there and Del in the grid remove the items at once with a notice and its Undo, which brings them back (G8, G17, G18);'
Write-Host ' 17. Save a copy... suggests the item''s file name and format, the flyout stays up while it is open, the dialog asks before replacing an'
Write-Host '     existing file and after Yes the file is replaced byte for byte; a path inside the stash folder is refused by name (G9, S16, G29);'
Write-Host ' 18. Reveal in File Explorer opens the stash folder with the item''s file selected (G9);'
Write-Host ' 19. in each of Discord, Slack and Microsoft Teams installed, composer focused: Ctrl+Alt+Space opens the flyout beside the caret within a'
Write-Host "     second, the footer reading ""Enter $([char]0xB7) Paste into <app>""; type two letters, Enter: the flyout hides, the picture is in the composer"
Write-Host '     and nothing is sent; repeat with the transparent PNG, the animated GIF, the animated WebP and three items; record per app what the'
Write-Host '     composer shows and whether the flyout opened beside the caret or at the pointer (T1) (G1, G8, N3);'
Write-Host " 20. over Notepad started as administrator, and over a window whose process is suspended, Enter copies and ""Copied $([char]0xB7) press Ctrl+V"" shows"
Write-Host '     above the tray without taking focus, six seconds, longer while hovered, the flyout hidden so that app is in front for its Ctrl+V, a pinned'
Write-Host '     window staying; the suspended one, resumed, shows no paste (G8, G2, G16, E6);'
Write-Host ' 21. Pause shortcuts in the tray menu (checked): Ctrl+Alt+Space does nothing; toggled back, it opens the window; with a second keyboard layout'
Write-Host '     on which Ctrl+Alt+Space types a character, switching to it names the shortcut in the tooltip within a second (G33, G34, F5);'
Write-Host ' 22. from a real click on the tray to a double-click on a tile with the pointer alone, and from the shortcut to Enter with the keyboard'
Write-Host '     alone, the paste path runs with visible focus (G7);'
Write-Host ' 23. a picture copied with Copy image in the browser you use, then Ctrl+V in the search box, in the grid and the + menu''s Paste image'
Write-Host '     (listed with Ctrl+V after Import files...): each adds a "Screenshot YYYY-MM-DD HH.MM.SS" PNG, first and selected, transparent where'
Write-Host '     the picture was, with a notice saying an animated picture is added by dragging its file in; files copied in File Explorer are'
Write-Host '     added as a drop would add them; from the search box copied words go into the box; from the grid a copied address, an empty'
Write-Host '     clipboard and copied words each get their notice and add nothing (G13, G2);'
Write-Host ' 24. over a chat, its composer focused, Ctrl+PrtScn dims every display and a drag shows its rectangle with its size; on release the'
Write-Host '     picture is in the stash and on the clipboard, the notification shows it and typing in the chat was not interrupted; a name typed'
Write-Host '     in the notification with Enter renames the item; a click anywhere else on it, outside its field, buttons and the close button, opens the'
Write-Host '     stash window with that item selected and no editor, and the notification goes; after Edit it stays; the notification has rounded corners;'
Write-Host '     the tray menu''s and the + menu''s Take a screenshot do the same; Esc and a click without a drag save nothing; the flyout, or the pinned'
Write-Host '     window, comes back as it was, and a hidden flyout or a pinned window minimised before the press, on either display, stays so'
Write-Host '     (G14, G3, G16, In use);'
Write-Host " 25. over a chat, its composer focused, Ctrl+Shift+PrtScn dims every display with ""Drag over what you want $([char]0xB7) release, then Record to start $([char]0xB7) Esc to cancel"";"
Write-Host '     a drag and release lift the dimming and show a thin red frame with the size and no REC, and a bar offering Record, Up to 0:30 and Cancel,'
Write-Host '     the bar with rounded corners, light under the light theme and dark under the dark, Record, Stop and Cancel readable while hovered;'
Write-Host '     the region waits, .work\ stays empty, and the tray menu reads Start recording with no red dot; Cancel here closes both and adds nothing silently;'
Write-Host '     Record, the shortcut again, the tray''s Start recording and the + menu''s Record a clip each start it: REC appears with the size, the bar shows'
Write-Host '     time against 0:30, size growing, Stop and Cancel, and the tray gets its red dot and Stop recording; the bar stays beneath the region, above it'
Write-Host '     near the bottom of the display, or over its bottom edge for a region the height of the display; the chat keeps its keys and clicks inside the'
Write-Host '     region, and Esc does not end recording; Stop, the shortcut again, the tray''s Stop recording and the cap end it; Cancel during recording discards it;'
Write-Host '     the GIF is named Recording YYYY-MM-DD HH.MM.SS at Record in local time and lasts from that act to its end, excluding the wait, with the pointer'
Write-Host '     and nothing of the frame, bar or notification in it; its notification shows a frame, length and size, and it pastes into a chat as a GIF;'
Write-Host '     the tray''s and + menu''s Record a clip also select a waiting region; the window comes back as it was, and one hidden or minimised before'
Write-Host '     the press, or restored and minimised again by you meanwhile, stays so (G15, G34, F5, F12, F4, G3, E3, G16, In use);'
Write-Host ' 26. with a picture copied in a browser, Ctrl+Alt+V over a chat adds a "Screenshot YYYY-MM-DD HH.MM.SS" PNG without opening a window;'
Write-Host '     its notification shows the picture, its size and that an animated picture is added by dragging its file in, the chat keeps'
Write-Host '     its keystrokes, and a name typed in the notification with Enter renames the item; with words or nothing on the clipboard a'
Write-Host '     notification says there is no image (G13, G16, In use);'
Write-Host ' 27. Ctrl+E on an item with a long name: the name ends with an ellipsis clear of Compare and the More options button, also at the window''s smallest;'
Write-Host '     on the display of the other scaling the tooltips of Label and of Thin, Medium and Thick show their whole word;'
Write-Host '     on a transparent PNG: Meme text in Comic Sans MS with a fill and an outline colour, turned by its handle with Shift held,'
Write-Host '     an arrow, a box and a marker stroke, a blur and a black box; Save: the tile, the preview ("Edited here") and a paste into Paint and'
Write-Host '     Word show it as the editor did, record whether its transparency shows; Edit again: every layer live; Save as new item, and Revert to original with'
Write-Host '     its Undo; a JPEG saved once is not visibly degraded (G20, G21, G23, G24, G26, T2);'
Write-Host ' 28. on an animated GIF the editor plays the clip, Space pauses it, Left and Right step a frame and the filmstrip''s playhead follows;'
Write-Host '     Trim''s handles dim the frames outside them and a label''s bar over the filmstrip drags; the saved GIF plays the kept frames at'
Write-Host '     their pace with the label only in its range; a capture card''s Edit, or Trim on a clip''s, opens it there (G22, G25, G16);'
Write-Host ' 29. a right-click on one tile offers Open with... between Save a copy... and Reveal in File Explorer, and on three selected tiles it is'
Write-Host '     absent; it, and Open with... in the preview''s More options menu, open the Windows dialog listing the programs that open the'
Write-Host '     picture, the flyout staying up while it is open and hiding once Paint, chosen there, takes the focus; the picture changed and'
Write-Host "     saved in Paint: a notification above the tray reads ""Replaced $([char]0x201C)<name>$([char]0x201D) with the picture another program saved."""
Write-Host '     with Undo and its close button, six seconds, longer while hovered, without taking focus, and the window summoned again shows'
Write-Host '     the saved picture on the tile and in the preview; record how long the notification took to come; Undo brings the picture back as it was,'
Write-Host '     with its layers on an edited item, and the saved one is in the Recycle Bin; left to go by itself, the previous picture is there'
Write-Host '     instead; saved again in Paint while the item is open in the editor, it is taken only after Save or Cancel (G27, G2, G16, G19, G20, G29);'
Write-Host ' 30. with Paint still open on that picture, Quit ImageStash, save in Paint and start ImageStash again: the saved picture is the item''s'
Write-Host '     when the stash shows, with the notification and its Undo; once the item is deleted and its notice has gone, its folder under'
Write-Host '     %LOCALAPPDATA%\ImageStash\open-with\ is gone too (G27, G28, G18);'
Write-Host ' 31. the tray menu''s Settings, and the Settings button beside the + button, open "ImageStash settings" centred on the display: Shortcuts with the four fields, After a screenshot or a recording,'
Write-Host "     Recording, Library with the stash folder, Open folder and Move$([char]0x2026), then Export stash$([char]0x2026) and Import stash$([char]0x2026), General with Start with Windows and Theme; light and dark; made"
Write-Host '     as small as it goes, its rows wrap and it scrolls with nothing cut; Esc closes it, and asked again while open, from either, it comes forward (G32, F4, F10, L2);'
Write-Host " 32. a click on a shortcut field reads ""Press a new shortcut$([char]0x2026)"" with its $([char]0xD7), and the shortcuts do nothing until it ends; a combination another app holds,"
Write-Host '     Shift+A and Ctrl+V each get an amber line under the row saying why, the field unchanged; with a second keyboard layout on which'
Write-Host '     Ctrl+Alt+E types a character, that combination is refused as typing one; a free combination shows in the field, the tray menu, the'
Write-Host '     + menu and the empty stash, and works at once (G32, G33, G34, F5, G2);'
Write-Host ' 33. with another program holding Ctrl+Alt+V when ImageStash starts, the icon''s tooltip names it with why; Settings opens on that row'
Write-Host '     with the same words, and once the other program lets it go, recording it there registers it and the tooltip''s line is gone (G33);'
Write-Host ' 34. Theme set to Dark turns the page and the stash window dark at once, and they are dark after Quit and a new start; Also copy off: a'
Write-Host '     screenshot is in the stash and not on the clipboard; Maximum length 10 seconds: a recording stops itself at 0:10; Show the mouse'
Write-Host '     pointer off: the recording has no pointer in it; Start with Windows off: no ImageStash after signing in again (G32, F1, F10, G14, G15);'
Write-Host " 35. on your own stash, Settings' Move$([char]0x2026) beside Open folder opens a folder picker; choose an empty folder, on another drive too, and one a sync"
Write-Host '     service mirrors, used from this PC alone: while a recording runs, an edit is open or a notice''s Undo stands, the line under the path'
Write-Host "     reads ""Waiting for <what> before moving your stash$([char]0x2026)"" with the page's other rows greyed, then counts the files copied; the stash window"
Write-Host '     says "Moved your stash to <folder>."; File Explorer shows the new folder holding stash.db, the pictures and originals\, and the old'
Write-Host '     folder gone; after Quit and a new start every item, tag, favorite and edit is there (G30, G32, G35, R9, E2);'
Write-Host " 36. Move$([char]0x2026) to a folder holding a file, or to the stash folder itself, is refused on the row with why, and nothing moves (G30, G35);"
Write-Host ' 37. signed in as a second Windows account on the same PC while the first account''s ImageStash runs, launch a copy: it runs with its own'
Write-Host '     tray icon and its own empty stash, opening pinned with the empty state, and the first account''s stash is as it was; on a PC with one'
Write-Host '     account, check this in the built system instead: the one-instance hold is scoped to the Windows session and the default stash and'
Write-Host '     settings folders are the account''s own (Done when 5, E1, F6, S7);'
Write-Host ' 38. after the earlier-build phase, or with a stash and settings an earlier build saved: nothing looks different once the current build'
Write-Host '     opens them, the thumbnails at their size, the pinned window at its place, the tags, favorites and Recent as they were (G31, Done when 4);'
Write-Host " 39. Settings' Export stash$([char]0x2026) opens a save picker suggesting ""ImageStash export <date and time>.zip""; while it writes, the line under the row"
Write-Host "     names the export and the path with the page greyed, then says ""Exported your stash to <path>."" there and in the stash window, the stash as"
Write-Host '     it was. Signed in as a second Windows account (on a PC with one account, into a fresh stash with default settings standing in for'
Write-Host "     that account's, F6), Import stash$([char]0x2026) with that file: the row says how many items it holds and that your stash and"
Write-Host '     the shortcuts, Also copy, the recording''s length and pointer, Start with Windows and Theme will be replaced, with Import and Cancel; Cancel'
Write-Host '     changes nothing; Import with an edit open waits saying so under the row, then the stash window lists every exported item with its tags,'
Write-Host '     favorite, uses and edits, the exported Theme and shortcuts work at once, and the replaced folder is in the Recycle Bin; a picture chosen'
Write-Host '     instead is refused on the row as not an export. Back on the first account (or the first stash), importing the same file leaves the'
Write-Host '     stash as it was in content, its replaced files in the Recycle Bin (G36, G32, G29, G33, R9, Done when 4).'

if ($failures.Count -gt 0) {
    Write-Host "$($failures.Count) check(s) failed."
    exit 1
}
Write-Host 'Every check held.'
exit 0
