<#
.SYNOPSIS
Checks the release's assets and its MSI on the real PC (F1, N8, G31, F6, G3, E3, G28). The two folders
tools/build-release-assets.ps1 wrote, -Assets and -Upgrade at a higher version, each hold the published folder at its
version, the zip of that folder under ImageStash/ with the same files, the MSI at that version and SHA256SUMS.txt
matching both; and the script refuses an -Output that holds a file. Then, from this unelevated shell: the -Assets MSI
installs quietly for the signed-in user, adding no ALLUSERS, with exactly the published files under
%LOCALAPPDATA%\Programs\ImageStash, the Start Menu shortcut and the per-user registration; the shortcut starts it on a
first run, pinned, with its settings, its stash and the Run value; one picture is stashed through the tray menu's
Stash clipboard image; the -Upgrade MSI installs over that running copy, ending it, with the upgrade's files in place,
a folder the person keeps in the install folder untouched and %LOCALAPPDATA%\ImageStash byte for byte as it was; the shortcut starts it again, showing nothing until a tray click,
which lists the item; a deleted file comes back by msiexec /fa; the older MSI is refused; and the uninstall over a
running copy removes the install folder whole (a stray file with it), the shortcut, the registration and its key,
leaving %LOCALAPPDATA%\ImageStash and the Run value as they were. Exits with the number of checks that failed.

It refuses to start while ImageStash runs, while %LOCALAPPDATA%\ImageStash exists, so a person's stash is never under
it, and while ImageStash is installed or its install folder, shortcut or key exists, since its cleanup removes only
what it installed itself. It installs and removes ImageStash for the signed-in user, runs it over the real
%LOCALAPPDATA%\ImageStash and Run value, replaces the clipboard and posts clicks to the tray icon; at the end it
uninstalls what it installed, removes %LOCALAPPDATA%\ImageStash and puts back the Run value it found. Start it from
PowerShell on the interactive desktop.

.EXAMPLE
powershell.exe -NoProfile -File tools/check-installer.ps1 -Assets $env:TEMP\ImageStash-assets -Upgrade $env:TEMP\ImageStash-assets-upgrade
#>
param(
    [Parameter(Mandatory)] [string] $Assets,
    [Parameter(Mandatory)] [string] $Upgrade
)
$ErrorActionPreference = 'Stop'
$failed = 0
$Assets = (Resolve-Path -LiteralPath $Assets).ProviderPath
$Upgrade = (Resolve-Path -LiteralPath $Upgrade).ProviderPath
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\ImageStash'
$installedExe = Join-Path $installDir 'ImageStash.Windows.exe'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'ImageStash.lnk'
$appKey = 'HKCU:\Software\ImageStash'
$appData = Join-Path $env:LOCALAPPDATA 'ImageStash'
$stash = Join-Path $appData 'stash'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$work = Join-Path $env:TEMP ('ImageStash-installer-check-' + [guid]::NewGuid().ToString('N'))
$installer = New-Object -ComObject WindowsInstaller.Installer

. (Join-Path $PSScriptRoot 'stash-windows.ps1')
Add-Type -AssemblyName System.Drawing, System.IO.Compression.FileSystem, UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// A picture on the clipboard as the "PNG" format alone, kept out of the clipboard history, and the copy's notification
// windows by the start of their title.
public static class InstallerDesk
{
    private delegate bool EnumProc(IntPtr window, IntPtr data);

    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string name);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr window, StringBuilder text, int length);

    private static IntPtr Global(byte[] bytes)
    {
        // GMEM_MOVEABLE, as SetClipboardData requires.
        var memory = GlobalAlloc(0x0002, new UIntPtr((uint)bytes.Length));
        Marshal.Copy(bytes, 0, GlobalLock(memory), bytes.Length);
        GlobalUnlock(memory);
        return memory;
    }

    public static bool PutPng(byte[] png)
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
                && SetClipboardData(RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing"), Global(new byte[4])) != IntPtr.Zero;
        }
        finally { CloseClipboard(); }
    }

    /// <summary>Whether the process shows a window whose title starts with <paramref name="start"/>.</summary>
    public static bool ShowsTitled(int processId, string start)
    {
        var found = false;
        EnumWindows(delegate (IntPtr window, IntPtr data)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            var text = new StringBuilder(512);
            GetWindowTextW(window, text, text.Capacity);
            if (owner == processId && IsWindowVisible(window) && text.ToString().StartsWith(start, StringComparison.Ordinal)) { found = true; }
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
        Start-Sleep -Milliseconds 100
    }
    return [bool](& $condition)
}

# A file's SHA-256, read sharing whatever the running copy holds open (its store among them).
function HashOf([string] $path) {
    $stream = [IO.FileStream]::new($path, 'Open', 'Read', 'ReadWrite, Delete')
    try { [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose() }
}

# Every folder and file under <folder>, by its path inside it, each file with its SHA-256: equal text, equal trees.
function Tree([string] $folder) {
    if (-not (Test-Path -LiteralPath $folder)) { return '(absent)' }
    $folder = (Resolve-Path -LiteralPath $folder).ProviderPath.TrimEnd('\')
    $entries = Get-ChildItem -LiteralPath $folder -Recurse -Force | Sort-Object FullName | ForEach-Object {
        $inside = $_.FullName.Substring($folder.Length)
        if ($_.PSIsContainer) { "$inside\" } else { "$inside|$(HashOf $_.FullName)" }
    }
    @($entries) -join "`n"
}

# The tree of %LOCALAPPDATA%\ImageStash once it has stopped changing, so a running copy's own last writes are in it.
function SettledAppData {
    $now = Tree $appData
    $deadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 500
        $previous = $now
        $now = Tree $appData
    } while (($now -ne $previous) -and ((Get-Date) -lt $deadline))
    $now
}

# The Run value as stored, its type and its data with environment variables left unexpanded; '' when absent.
function StoredRunValue {
    $key = Get-Item -Path $runKey
    if ($key.GetValueNames() -notcontains 'ImageStash') { return '' }
    '{0}|{1}' -f $key.GetValueKind('ImageStash'), $key.GetValue('ImageStash', $null, 'DoNotExpandEnvironmentNames')
}

function Com($target, [string] $member, [string] $kind, [object[]] $arguments) {
    $target.GetType().InvokeMember($member, $kind, $null, $target, $arguments)
}

# A value of the MSI's Property table, read from the package itself.
function MsiProperty([string] $msi, [string] $property) {
    $database = Com $installer 'OpenDatabase' 'InvokeMethod' @($msi, [object] 0)
    $view = Com $database 'OpenView' 'InvokeMethod' @("SELECT Value FROM Property WHERE Property = '$property'")
    $null = Com $view 'Execute' 'InvokeMethod' @()
    $record = Com $view 'Fetch' 'InvokeMethod' @()
    $value = if ($record) { Com $record 'StringData' 'GetProperty' @([object] 1) } else { $null }
    $null = Com $view 'Close' 'InvokeMethod' @()
    $value
}

# The product codes Windows Installer has registered, for this user or the machine, under the package's UpgradeCode;
# PowerShell unrolls the StringList it returns into those codes.
function Registered { @(Com $installer 'RelatedProducts' 'GetProperty' @($upgradeCode)) }

function ProductInfo([string] $code, [string] $property) { Com $installer 'ProductInfo' 'GetProperty' @($code, $property) }

function Msiexec([string] $arguments) {
    (Start-Process -FilePath msiexec.exe -ArgumentList $arguments -Wait -PassThru).ExitCode
}

# Windows Installer's own log: the MSI's step that ends a running copy (G3 with E3: an admitted kill) runs before its
# first file operation, so it is there should Restart Manager not have ended the copy already at InstallValidate.
function CloseStepBeforeFiles([string] $log) {
    $lines = Get-Content -LiteralPath $log
    $ended = @($lines | Select-String -SimpleMatch 'ActionStart(Name=Wix4CloseApplicationsDeferred' | Select-Object -First 1)
    $files = @($lines | Select-String -Pattern 'Executing op: File(Remove|Copy)\(' | Select-Object -First 1)
    ($ended.Count -eq 1) -and (($files.Count -eq 0) -or ($ended[0].LineNumber -lt $files[0].LineNumber))
}

function AnyCopies { @(Get-Process -Name ImageStash.Windows -ErrorAction SilentlyContinue) }

function InstalledCopies { @(AnyCopies | Where-Object { $_.Path -eq $installedExe }) }

function StopCopies {
    foreach ($process in InstalledCopies) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
}

# The copy the Start Menu shortcut starts, as a person starts it; $null when none appears within ten seconds.
function StartFromShortcut {
    Start-Process -FilePath $shortcut
    $null = WaitFor { (InstalledCopies).Count -eq 1 } 10000
    $copies = @(InstalledCopies)
    if ($copies.Count -eq 1) { $copies[0] } else { $null }
}

# A tray click on <process>, posted once its platform window exists (a click posted before its start completes waits
# in its queue); the milliseconds until a window showed, or -1.
function SummonByTray($process) {
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $took = [StashWindows]::ClickTrayIcon($process.Id, 5000)
        # -1 at once: no platform window to post to yet; -1 after the wait: posted, and no window showed.
        if (($took -ge 0) -or ($clock.ElapsedMilliseconds -ge 1000)) { return $took }
        Start-Sleep -Milliseconds 100
    }
    -1
}

# The elements of <process>'s windows that UI Automation finds with <name> and <type>.
function Elements($process, [string] $name, $type) {
    $condition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, $type),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $name))
    @([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Descendants, $condition))
}

# The one MSI in a folder build-release-assets.ps1 wrote.
function MsiIn([string] $folder) {
    $msis = @(Get-ChildItem -LiteralPath $folder -Filter 'ImageStash-*-win-x64.msi')
    if ($msis.Count -ne 1) { throw "$folder holds $($msis.Count) ImageStash-<version>-win-x64.msi files, not one" }
    $msis[0].FullName
}

# One folder build-release-assets.ps1 wrote: its version, its files, and the checks of build-release-assets.ps1's
# promises about it (the agreements on the release's assets and its version).
function ReadAssets([string] $folder) {
    $msi = MsiIn $folder
    $version = MsiProperty $msi 'ProductVersion'
    $name = "ImageStash-$version-win-x64"
    $published = Join-Path $folder 'ImageStash'
    $zip = Join-Path $folder "$name.zip"
    $listed = (@(Get-ChildItem -LiteralPath $folder -Force | ForEach-Object { $_.Name } | Sort-Object) -join ', ')
    Check ($listed -eq "ImageStash, $name.msi, $name.zip, SHA256SUMS.txt") "$folder holds ImageStash\, $name.msi, $name.zip and SHA256SUMS.txt, and nothing else: $listed"
    $fileVersion = (Get-Item -LiteralPath (Join-Path $published 'ImageStash.Windows.exe')).VersionInfo.FileVersion
    Check ($fileVersion -eq "$version.0") "its ImageStash\ImageStash.Windows.exe has FileVersion $version.0, the MSI's ProductVersion ${version}: $fileVersion"
    $product = '{0} by {1}' -f (MsiProperty $msi 'ProductName'), (MsiProperty $msi 'Manufacturer')
    Check ($product -eq 'ImageStash by Brian Wei Hao Ma') "the MSI is ImageStash by Brian Wei Hao Ma (F1, F10): $product"
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try { $entries = @($archive.Entries | ForEach-Object { $_.FullName }) }
    finally { $archive.Dispose() }
    $astray = @($entries | Where-Object { (-not $_.StartsWith('ImageStash/')) -or $_.Contains('\') -or ($_ -split '/' -contains '..') })
    Check (($entries.Count -gt 0) -and ($astray.Count -eq 0)) "the zip's $($entries.Count) entries all begin ImageStash/ and hold no backslash or .. step: $($astray | Select-Object -First 3)"
    $unzipped = Join-Path $work ('unzipped-' + $version)
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $unzipped)
    $top = (@(Get-ChildItem -LiteralPath $unzipped -Force | ForEach-Object { $_.Name }) -join ', ')
    Check (($top -eq 'ImageStash') -and ((Tree (Join-Path $unzipped 'ImageStash')) -eq (Tree $published))) "unzipped, it is one folder ImageStash, the published folder file for file by SHA-256 (F1): $top"
    $sums = [IO.File]::ReadAllText((Join-Path $folder 'SHA256SUMS.txt'))
    $expected = (@($msi, $zip | ForEach-Object { '{0}  {1}' -f (Get-FileHash -Algorithm SHA256 -LiteralPath $_).Hash.ToLowerInvariant(), (Split-Path $_ -Leaf) }) -join "`n") + "`n"
    Check ($sums -eq $expected) 'SHA256SUMS.txt holds one "<sha256>  <name>" line for the MSI and the zip, matching Get-FileHash'
    @{ Version = [version] $version; Msi = $msi; Published = $published; Files = Tree $published; UpgradeCode = MsiProperty $msi 'UpgradeCode' }
}

$upgradeCode = MsiProperty (MsiIn $Assets) 'UpgradeCode'
# The products this check may install, and so the only ones its cleanup uninstalls.
$ours = @((MsiProperty (MsiIn $Assets) 'ProductCode'), (MsiProperty (MsiIn $Upgrade) 'ProductCode'))
$refused = @()
if ((AnyCopies).Count -gt 0) { $refused += 'ImageStash is running; quit it from its tray menu' }
if (Test-Path -LiteralPath $appData) { $refused += "$appData exists, and the check runs ImageStash over that folder" }
if ((Registered).Count -gt 0) { $refused += 'ImageStash is installed; uninstall it from Apps' }
foreach ($path in $installDir, $shortcut, $appKey) {
    if (Test-Path -LiteralPath $path) { $refused += "$path exists, and the check removes only what it installed itself" }
}
if ($refused.Count -gt 0) {
    foreach ($reason in $refused) { Write-Host "refused: $reason" }
    Write-Host 'Run this check again once none of these holds.'
    exit 1
}
$runFound = StoredRunValue
New-Item -ItemType Directory -Path $work | Out-Null

try {
    $from = ReadAssets $Assets
    $to = ReadAssets $Upgrade
    Check ($to.Version -gt $from.Version) "-Upgrade's version $($to.Version) is above -Assets' $($from.Version)"
    Check ($to.UpgradeCode -eq $upgradeCode) "both MSIs share the UpgradeCode $upgradeCode, so the newer replaces the older"
    if ($failed -gt 0) { throw 'the assets did not hold, so nothing is installed' }

    # The agreement's refusal: an -Output holding a file is refused by name, and nothing is written into it.
    $occupied = Join-Path $work 'occupied'
    New-Item -ItemType Directory -Path $occupied | Out-Null
    Set-Content -LiteralPath (Join-Path $occupied 'kept.txt') -Value 'already here'
    $said = Join-Path $work 'refusal.txt'
    $refusal = Start-Process -FilePath powershell.exe -ArgumentList "-NoProfile -File `"$(Join-Path $PSScriptRoot 'build-release-assets.ps1')`" -Output `"$occupied`"" -Wait -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $work 'refusal-out.txt') -RedirectStandardError $said
    # Windows PowerShell wraps the error it writes at the console's width, so the words are compared without spaces.
    $words = (Get-Content -Raw -LiteralPath $said) -replace '\s', ''
    $left = (@(Get-ChildItem -LiteralPath $occupied -Force | ForEach-Object { $_.Name }) -join ', ')
    $kept = (Get-Content -Raw -LiteralPath (Join-Path $occupied 'kept.txt')).Trim()
    Check (($refusal.ExitCode -ne 0) -and $words.Contains(($occupied -replace '\s', '')) -and ($left -eq 'kept.txt') -and ($kept -eq 'already here')) "build-release-assets.ps1 refused an -Output holding a file, by name, writing nothing there and leaving its file as it was: exit $($refusal.ExitCode), left $left"

    # F1, N8, F10: the quiet install from this unelevated shell, for the signed-in user only.
    $log = Join-Path $work 'install.log'
    $exit = Msiexec "/i `"$($from.Msi)`" /qn /l*v `"$log`""
    Check ($exit -eq 0) "msiexec /i $(Split-Path $from.Msi -Leaf) /qn exited 0 from an unelevated shell (F1): $exit"
    Check (-not (Select-String -LiteralPath $log -Pattern 'PROPERTY CHANGE: Adding ALLUSERS property' -Quiet)) 'its log never adds ALLUSERS: a per-user install, no elevation (F1)'
    Check ((Tree $installDir) -eq $from.Files) "$installDir holds exactly the published folder's files, each by SHA-256 (F1, Foundations)"
    $target = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut).TargetPath
    Check ($target -eq $installedExe) "the Start Menu shortcut $shortcut starts $installedExe`: '$target'"
    $codes = @(Registered)
    $registration = if ($codes.Count -eq 1) { '{0} {1} by {2}, assignment {3}' -f (ProductInfo $codes[0] 'InstalledProductName'), (ProductInfo $codes[0] 'VersionString'), (ProductInfo $codes[0] 'Publisher'), (ProductInfo $codes[0] 'AssignmentType') } else { "$($codes.Count) registrations" }
    Check ($registration -eq "ImageStash $($from.Version) by Brian Wei Hao Ma, assignment 0") "Windows Installer registers it once, for this user alone (assignment 0), as Apps lists it (F1, F10): $registration"
    Check ((-not (Test-Path -LiteralPath $appData)) -and ((StoredRunValue) -eq $runFound)) "the install made nothing under $appData and left the Run value alone (F6, F1)"

    # G2, F1, G32: the shortcut starts a first run, pinned, which makes the settings, the stash and the Run value.
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $copy = StartFromShortcut
    Check ($null -ne $copy) "the shortcut started $installedExe"
    if ($null -eq $copy) { throw 'nothing further can be checked without the installed copy running' }
    $pinned = WaitFor { [StashWindows]::Showing($copy.Id) -eq 'pinned' } 20000
    Check $pinned "on the first run its one window shows pinned (G2): '$([StashWindows]::Showing($copy.Id))' after $($clock.ElapsedMilliseconds) ms"
    Check (WaitFor { (Test-Path -LiteralPath (Join-Path $appData 'settings.json')) -and (Test-Path -LiteralPath (Join-Path $stash 'stash.db')) } 20000) "it made $appData\settings.json and stash\stash.db (F6)"
    $runValue = "String|`"$installedExe`""
    Check (WaitFor { (StoredRunValue) -eq $runValue } 20000) "the Run value ImageStash names the installed executable, Start with Windows on by default (F1, G32): $(StoredRunValue)"

    # G13, G16, R9: one picture through the product, snapshotted only once the product has said it is in the stash.
    $picture = New-Object Drawing.Bitmap 48, 32
    $graphics = [Drawing.Graphics]::FromImage($picture)
    $graphics.Clear([Drawing.Color]::FromArgb(255, 200, 40, 160))
    $graphics.Dispose()
    $stream = New-Object IO.MemoryStream
    $picture.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $picture.Dispose()
    Check ([InstallerDesk]::PutPng($stream.ToArray())) 'a 48 by 32 PNG on the clipboard'
    Check ([StashWindows]::RightClickTrayIcon($copy.Id)) 'a right-click posted to the tray icon'
    $menuItem = [Windows.Automation.ControlType]::MenuItem
    $null = WaitFor { (Elements $copy 'Stash clipboard image' $menuItem).Count -eq 1 } 5000
    $entry = @(Elements $copy 'Stash clipboard image' $menuItem)
    Check ($entry.Count -eq 1) 'the tray menu offers Stash clipboard image (G34)'
    if ($entry.Count -eq 1) { $entry[0].GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
    Check (WaitFor { [InstallerDesk]::ShowsTitled($copy.Id, "Saved $([char]0x201C)") } 10000) 'its notification says the picture is saved to the stash (G13, G16)'
    $items = @(Get-ChildItem -LiteralPath $stash -Filter '*.png' -File)
    Check ($items.Count -eq 1) "the stash root holds the item's one PNG: $($items.Name -join ', ')"
    $itemBytes = if ($items.Count -eq 1) { HashOf $items[0].FullName } else { '' }
    $beforeUpgrade = SettledAppData
    # E2 admits a stash moved anywhere Settings' Move accepts, this install folder included: it must outlive the upgrade.
    $personal = Join-Path $installDir 'kept by the person'
    New-Item -ItemType Directory -Path (Join-Path $personal 'originals') | Out-Null
    Set-Content -LiteralPath (Join-Path $personal 'stash.db') -Value 'a stash moved here'
    Set-Content -LiteralPath (Join-Path $personal 'originals\picture.png') -Value 'an original'
    $personalTree = Tree $personal

    # G31, F6, G3, E3: the newer MSI over the running copy ends it and keeps the stash and settings byte for byte.
    $log = Join-Path $work 'upgrade.log'
    $exit = Msiexec "/i `"$($to.Msi)`" /qn /l*v `"$log`""
    Check ($exit -eq 0) "msiexec /i $(Split-Path $to.Msi -Leaf) /qn over the running copy exited 0, not 3010 or 1603 (G31): $exit"
    Check (CloseStepBeforeFiles $log) 'its log runs the step that ends a running copy before its first file operation (G3, E3)'
    Check ((AnyCopies).Count -eq 0) 'no ImageStash.Windows process remains'
    Check ((Tree $personal) -eq $personalTree) "a folder the person keeps in $installDir is there after the upgrade, byte for byte (G31, E2)"
    if (Test-Path -LiteralPath $personal) { Remove-Item -LiteralPath $personal -Recurse -Force }
    Check ((Tree $installDir) -eq $to.Files) "$installDir holds exactly the upgrade's published files, each by SHA-256 (G31)"
    $fileVersion = (Get-Item -LiteralPath $installedExe).VersionInfo.FileVersion
    Check ($fileVersion -eq "$($to.Version).0") "the installed executable's FileVersion is $($to.Version).0: $fileVersion"
    $codes = @(Registered)
    $versions = @($codes | ForEach-Object { ProductInfo $_ 'VersionString' }) -join ', '
    Check ($versions -eq "$($to.Version)") "one registration remains, at $($to.Version): $versions"
    $afterUpgrade = Tree $appData
    Check ($afterUpgrade -eq $beforeUpgrade) "every file under $appData is byte-identical to before the upgrade (G31, F6, G28)"
    if ($afterUpgrade -ne $beforeUpgrade) {
        Compare-Object ($beforeUpgrade -split "`n") ($afterUpgrade -split "`n") | ForEach-Object { Write-Host "     $($_.SideIndicator) $($_.InputObject)" }
    }
    Check ((StoredRunValue) -eq $runValue) 'the Run value is unchanged (F1)'

    # G2, G1, G31, G28: started again, the first run is over, so nothing shows until the tray's click lists the item.
    $copy = StartFromShortcut
    Check ($null -ne $copy) 'the shortcut started the upgraded copy'
    if ($null -eq $copy) { throw 'nothing further can be checked without the installed copy running' }
    Check (-not (WaitFor { [StashWindows]::Showing($copy.Id) -ne '' } 3000)) "for three seconds after its start it shows no window: the first run is over (G2): '$([StashWindows]::Showing($copy.Id))'"
    $took = SummonByTray $copy
    Check (($took -ge 0) -and ($took -le 1000)) "a tray click shows its window within a second (G1): $took ms"
    Check (WaitFor { (Elements $copy '1 item' ([Windows.Automation.ControlType]::Text)).Count -eq 1 } 10000) 'the window lists the one item (G31, G28)'
    Check ((@(Get-ChildItem -LiteralPath $stash -Filter '*.png' -File).Count -eq 1) -and ((HashOf $items[0].FullName) -eq $itemBytes)) "the item's file is still in the stash root with its bytes: $($items[0].Name)"

    # N8: a repair puts a deleted file back.
    StopCopies
    $dll = Join-Path $installDir 'ImageStash.dll'
    Remove-Item -LiteralPath $dll
    $exit = Msiexec "/fa `"$($to.Msi)`" /qn /l*v `"$(Join-Path $work 'repair.log')`""
    Check (($exit -eq 0) -and (Test-Path -LiteralPath $dll) -and ((HashOf $dll) -eq (HashOf (Join-Path $to.Published 'ImageStash.dll')))) "with ImageStash.dll deleted, msiexec /fa exited 0 and put it back with its SHA-256 (N8): $exit"

    # The agreement's downgrade refusal: the older MSI over the newer is refused by name and changes nothing.
    $log = Join-Path $work 'downgrade.log'
    $exit = Msiexec "/i `"$($from.Msi)`" /qn /l*v `"$log`""
    Check ($exit -ne 0) "msiexec /i $(Split-Path $from.Msi -Leaf) over $($to.Version) exited nonzero: $exit"
    Check (Select-String -LiteralPath $log -SimpleMatch 'A newer version of ImageStash is already installed.' -Quiet) 'its log names the refusal: A newer version of ImageStash is already installed. (F10)'
    Check ((Tree $installDir) -eq $to.Files) "$installDir is unchanged, file for file"
    $versions = @(Registered | ForEach-Object { ProductInfo $_ 'VersionString' }) -join ', '
    Check ($versions -eq "$($to.Version)") "the one registration is still at $($to.Version): $versions"

    # F1, N8, F6: the uninstall over a running copy removes the folder whole and nothing of the person's.
    $copy = StartFromShortcut
    Check (($null -ne $copy) -and ((SummonByTray $copy) -ge 0)) 'the shortcut started the copy once more, and a tray click shows its window'
    Set-Content -LiteralPath (Join-Path $installDir 'left by the person.txt') -Value 'a stray file'
    $beforeUninstall = SettledAppData
    $runBeforeUninstall = StoredRunValue
    $log = Join-Path $work 'uninstall.log'
    $exit = Msiexec "/x `"$($to.Msi)`" /qn /l*v `"$log`""
    Check ($exit -eq 0) "msiexec /x $(Split-Path $to.Msi -Leaf) /qn over the running copy exited 0, not 3010 (F1, N8): $exit"
    Check (CloseStepBeforeFiles $log) 'its log runs the step that ends a running copy before its first file operation (G3, E3)'
    Check ((AnyCopies).Count -eq 0) 'no ImageStash.Windows process remains'
    Check (-not (Test-Path -LiteralPath $installDir)) "$installDir is gone whole, the stray file with it (F1, N8)"
    Check (-not (Test-Path -LiteralPath $shortcut)) 'the Start Menu shortcut is gone'
    Check ((Registered).Count -eq 0) 'no registration remains, so Apps lists no ImageStash'
    Check (-not (Test-Path -LiteralPath $appKey)) "$appKey is gone"
    $afterUninstall = Tree $appData
    Check ($afterUninstall -eq $beforeUninstall) "every file under $appData is byte-identical to before the uninstall (F6)"
    if ($afterUninstall -ne $beforeUninstall) {
        Compare-Object ($beforeUninstall -split "`n") ($afterUninstall -split "`n") | ForEach-Object { Write-Host "     $($_.SideIndicator) $($_.InputObject)" }
    }
    Check ((StoredRunValue) -eq $runBeforeUninstall) 'the Run value is as it was before the uninstall: ImageStash owns it (F1)'
}
catch {
    Write-Host "FAIL the installer check stopped: $($_.Exception.Message)"
    $failed++
}
finally {
    Write-Host 'cleanup:'
    StopCopies
    foreach ($code in @(Registered | Where-Object { $ours -contains $_ })) {
        $exit = Msiexec "/x $code /qn"
        Write-Host "  uninstalled $code, which the check installed: exit $exit"
    }
    foreach ($path in $installDir, $shortcut, $appKey) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
            Write-Host "  removed $path, which the check made"
        }
    }
    if (Test-Path -LiteralPath $appData) {
        Remove-Item -LiteralPath $appData -Recurse -Force
        Write-Host "  removed $appData, which the copy made"
    }
    # The copy wrote this value; put back what was there before the check.
    if ((StoredRunValue) -ne $runFound) {
        if ($runFound) {
            $kind, $data = $runFound -split '\|', 2
            New-ItemProperty -Path $runKey -Name ImageStash -PropertyType $kind -Value $data -Force | Out-Null
            Write-Host '  restored the Run value ImageStash the check found'
        }
        else {
            Remove-ItemProperty -Path $runKey -Name ImageStash
            Write-Host '  removed the Run value ImageStash the copy wrote'
        }
    }
    if ($failed -eq 0) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
    else {
        Write-Host "  kept Windows Installer's logs in $work"
    }
}
if ($failed -gt 0) { Write-Host "$failed check(s) failed." } else { Write-Host 'Every check held.' }
exit $failed
