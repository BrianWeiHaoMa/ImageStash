<#
.SYNOPSIS
The comprehensive suite a release must pass, as one command run whole on a Windows PC before a tag, and with
-NoDesktop by the CI and Release workflows. Its phases, in order, stopping at the first that fails:

  identity   only with -Tag: the tag is v<a.b.c>, <a.b.c> is Directory.Build.props's version, and CHANGELOG.md has a
             "## [<a.b.c>]" section, whose body is written to -Notes when given (the release's notes)
  build      dotnet build ImageStash.sln -c Release
  portable   dotnet test tests/ImageStash.Tests, failing on a nonzero exit or on any check skipped (inconclusive)
  desktop    dotnet test tests/ImageStash.Windows.Tests, failing the same way
  published  tools/check-published.ps1, failing on a nonzero exit or on any line beginning "skipped:" but the
             earlier-build phase's "skipped: no -EarlierCommit ..." line, which every run without that parameter prints
  assets     tools/build-release-assets.ps1 into -Output, and at the version with its patch raised by one into
             "<-Output>-upgrade"
  installer  tools/check-installer.ps1 -Assets <-Output> -Upgrade <-Output>-upgrade

Each phase prints a line as it starts and as it ends, with its seconds. A failing phase ends the run with a line
naming the phase and, for a child process, its exit code; the script then exits 1. -Only runs one phase alone.
-NoDesktop runs, in order, only the phases that need no desktop: identity (with -Tag), build, portable and assets,
which is what GitHub's hosted runner can judge (its job runs elevated, so the desktop, published and installer phases
cannot pass or prove anything there). -NoDesktop with -Only is refused with a "refused:" line before any phase.

-Output, a new or empty folder, is required for a whole run, for -NoDesktop and for -Only assets or installer (which
reads the two folders assets wrote), and is ignored by the other phases. -Changelog names the changelog identity
reads, by default the repository's CHANGELOG.md. -PublishedCheck names the script the published phase runs, by
default tools/check-published.ps1.

The desktop, published and installer phases use the real desktop, %LOCALAPPDATA%\ImageStash, the Run value, the
clipboard and the Recycle Bin, so a run that includes any of them refuses, before any phase starts, while ImageStash
runs or while %LOCALAPPDATA%\ImageStash exists. A whole run takes about forty minutes; minimise every window on the
primary display first. Start it from PowerShell, never Git Bash.

.EXAMPLE
powershell.exe -NoProfile -File tools/check-release.ps1 -Output $env:TEMP\ImageStash-release

.EXAMPLE
powershell.exe -NoProfile -File tools/check-release.ps1 -NoDesktop -Output $env:TEMP\ImageStash-hosted

.EXAMPLE
powershell.exe -NoProfile -File tools/check-release.ps1 -Tag v1.0.0 -Only identity -Notes $env:TEMP\notes.md
#>
param(
    [string] $Output = '',
    [string] $Tag = '',
    [string] $Notes = '',
    [ValidateSet('identity', 'build', 'portable', 'desktop', 'published', 'assets', 'installer')]
    [string] $Only = '',
    [switch] $NoDesktop,
    [string] $Changelog = '',
    [string] $PublishedCheck = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$phases = @('identity', 'build', 'portable', 'desktop', 'published', 'assets', 'installer')
$usesDesktop = @('desktop', 'published', 'installer')
$needsOutput = @('assets', 'installer')
$earlierBuildLine = 'skipped: no -EarlierCommit '

function Stop-Run([string] $message) {
    Write-Host "check-release: $message"
    exit 1
}

function Read-Version {
    $props = [xml](Get-Content -Raw -LiteralPath (Join-Path $root 'Directory.Build.props'))
    "$($props.Project.PropertyGroup.Version)".Trim()
}

function Resolve-Full([string] $path) {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($path)
}

# Runs a child process, relaying each of its lines as it comes, and returns its exit code; $onLine sees every line.
# Stderr is folded into the relay, so the error preference must not turn its first line into a terminating error.
function Invoke-Child([string] $file, [string[]] $arguments, [scriptblock] $onLine = $null) {
    $ErrorActionPreference = 'Continue'
    & $file @arguments 2>&1 | ForEach-Object {
        $line = "$_"
        Write-Host $line
        if ($onLine) { & $onLine $line }
    }
    $LASTEXITCODE
}

# The children are this repository's own scripts: whoever could start this one may run them, whatever execution policy
# a fresh powershell.exe would otherwise apply to them.
function Invoke-Script([string] $script, [string[]] $arguments, [scriptblock] $onLine = $null) {
    Invoke-Child 'powershell.exe' (@('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script) + $arguments) $onLine
}

# Each phase returns nothing when it held, or the reason it failed.
function Test-Identity {
    if ($Tag -notmatch '^v(\d+\.\d+\.\d+)$') {
        return "the tag '$Tag' is not of the form v<a>.<b>.<c>"
    }
    $tagVersion = $Matches[1]
    $version = Read-Version
    if ($tagVersion -ne $version) {
        return "the tag $Tag names $tagVersion, but Directory.Build.props says $version"
    }
    $changelogPath = if ($Changelog) { Resolve-Full $Changelog } else { Join-Path $root 'CHANGELOG.md' }
    $changelogName = Split-Path $changelogPath -Leaf
    if (-not (Test-Path -LiteralPath $changelogPath -PathType Leaf)) {
        return "$changelogPath does not exist, so the tag $Tag has no notes"
    }
    $lines = [IO.File]::ReadAllLines($changelogPath)
    $heading = '## [' + $version + ']'
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].StartsWith($heading)) { $start = $i; break }
    }
    if ($start -lt 0) {
        return "$changelogName has no '$heading' section for the tag $Tag"
    }
    $end = $lines.Count
    for ($i = $start + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i].StartsWith('## ')) { $end = $i; break }
    }
    $body = @(if ($start + 1 -lt $end) { $lines[($start + 1)..($end - 1)] })
    while ($body.Count -gt 0 -and -not $body[0].Trim()) { $body = @($body | Select-Object -Skip 1) }
    while ($body.Count -gt 0 -and -not $body[-1].Trim()) { $body = @($body | Select-Object -SkipLast 1) }
    if ($body.Count -eq 0) {
        return "$changelogName's '$heading' section is empty, so the tag $Tag has no notes"
    }
    Write-Host "  the tag $Tag matches Directory.Build.props and $changelogName's '$heading' section ($($body.Count) lines)"
    if ($Notes) {
        $notesPath = Resolve-Full $Notes
        [IO.File]::WriteAllText($notesPath, (($body -join "`n") + "`n"), (New-Object Text.UTF8Encoding $false))
        Write-Host "  wrote the notes to $notesPath"
    }
}

function Test-Build {
    $code = Invoke-Child 'dotnet' @('build', (Join-Path $root 'ImageStash.sln'), '-c', 'Release', '-nologo')
    if ($code -ne 0) { return "dotnet build exited $code" }
}

# A test project's run holds only when every check in it ran and passed: dotnet test exits 0 when a check ends
# inconclusive (reported as skipped), and a release runs them all, as the published phase's skipped: rule says.
function Test-Project([string] $project) {
    $summaries = New-Object System.Collections.Generic.List[string]
    $skipped = New-Object System.Collections.Generic.List[string]
    $code = Invoke-Child 'dotnet' @('test', (Join-Path $root $project)) {
        param($line)
        if ($line -match '^(Passed|Failed)!\s+-\s+Failed:\s*\d+,\s*Passed:\s*\d+,\s*Skipped:\s*(\d+),') { $summaries.Add($Matches[2]) }
        elseif ($line -match '^\s+Skipped\s+(\S+)') { $skipped.Add($Matches[1]) }
    }
    $name = $project.Replace('\', '/')
    if ($code -ne 0) { return "dotnet test $name exited $code" }
    if ($summaries.Count -eq 0) { return "dotnet test $name exited 0 but printed no Passed! summary, so what ran is unknown" }
    $count = 0
    foreach ($each in $summaries) { $count += [int] $each }
    if ($count -gt 0) {
        return "dotnet test $name exited 0 but skipped $count check(s), and a release runs them all: $($skipped -join ' | ')"
    }
}

function Test-Portable { Test-Project 'tests\ImageStash.Tests' }

function Test-Desktop { Test-Project 'tests\ImageStash.Windows.Tests' }

function Test-Published {
    $script = if ($PublishedCheck) { Resolve-Full $PublishedCheck } else { Join-Path $PSScriptRoot 'check-published.ps1' }
    $skips = New-Object System.Collections.Generic.List[string]
    $code = Invoke-Script $script @() {
        param($line)
        if ($line -match '^\s*skipped:' -and -not $line.TrimStart().StartsWith($earlierBuildLine)) { $skips.Add($line) }
    }
    if ($code -ne 0) { return "$(Split-Path $script -Leaf) exited $code" }
    if ($skips.Count -gt 0) {
        return "$(Split-Path $script -Leaf) exited 0 but skipped $($skips.Count) part(s), and a release runs them all: $($skips -join ' | ')"
    }
}

function Test-Assets {
    $version = Read-Version
    if ($version -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
        return "Directory.Build.props's version '$version' is not three numbers"
    }
    $upgradeVersion = '{0}.{1}.{2}' -f $Matches[1], $Matches[2], ([int] $Matches[3] + 1)
    $builder = Join-Path $PSScriptRoot 'build-release-assets.ps1'
    $code = Invoke-Script $builder @('-Output', $assetsFolder, '-Version', $version)
    if ($code -ne 0) { return "build-release-assets.ps1 -Output $assetsFolder exited $code" }
    $code = Invoke-Script $builder @('-Output', $upgradeFolder, '-Version', $upgradeVersion)
    if ($code -ne 0) { return "build-release-assets.ps1 -Output $upgradeFolder (version $upgradeVersion) exited $code" }
}

function Test-Installer {
    foreach ($folder in $assetsFolder, $upgradeFolder) {
        if (-not (Test-Path -LiteralPath $folder -PathType Container)) {
            return "$folder does not exist; the assets phase writes it"
        }
    }
    $code = Invoke-Script (Join-Path $PSScriptRoot 'check-installer.ps1') @('-Assets', $assetsFolder, '-Upgrade', $upgradeFolder)
    if ($code -ne 0) { return "check-installer.ps1 exited $code" }
}

if ($NoDesktop -and $Only) {
    Stop-Run "refused: -NoDesktop and -Only $Only both choose the phases; give one of them; nothing was run"
}
if ($Only -eq 'identity' -and -not $Tag) {
    Stop-Run '-Only identity needs -Tag v<a.b.c>; nothing was run'
}
$run = if ($Only) { @($Only) } else {
    @($phases | Where-Object { ($_ -ne 'identity' -or $Tag) -and -not ($NoDesktop -and $usesDesktop -contains $_) })
}
if (-not $Output -and @($run | Where-Object { $needsOutput -contains $_ }).Count -gt 0) {
    Stop-Run "-Output <new or empty folder> is needed by the $(@($run | Where-Object { $needsOutput -contains $_ }) -join ' and ') phase(s); nothing was run"
}
if ($Output) {
    $assetsFolder = Resolve-Full $Output
    $upgradeFolder = $assetsFolder.TrimEnd('\') + '-upgrade'
}
if (@($run | Where-Object { $usesDesktop -contains $_ }).Count -gt 0) {
    if (Get-Process -Name ImageStash.Windows -ErrorAction SilentlyContinue) {
        Stop-Run 'refused: ImageStash is running; quit it from its tray menu, then run this check again; nothing was run'
    }
    $appData = Join-Path $env:LOCALAPPDATA 'ImageStash'
    if (Test-Path -LiteralPath $appData) {
        Stop-Run "refused: $appData exists, and the suite runs ImageStash over it; move it aside, then run this check again; nothing was run"
    }
}

$suite = [Diagnostics.Stopwatch]::StartNew()
foreach ($phase in $run) {
    Write-Host "check-release: phase $phase started"
    $clock = [Diagnostics.Stopwatch]::StartNew()
    # An error a phase throws (a notes path that cannot be written, a child that cannot start) ends the run on the same
    # one line as a failure it returns, so the last line of a log always names the phase.
    try {
        $failure = switch ($phase) {
            'identity' { Test-Identity }
            'build' { Test-Build }
            'portable' { Test-Portable }
            'desktop' { Test-Desktop }
            'published' { Test-Published }
            'assets' { Test-Assets }
            'installer' { Test-Installer }
        }
    }
    catch {
        $failure = "$($_.Exception.Message)"
    }
    $seconds = [math]::Round($clock.Elapsed.TotalSeconds)
    if ($failure) {
        Stop-Run "phase $phase failed after $seconds s: $failure"
    }
    Write-Host "check-release: phase $phase held ($seconds s)"
}
Write-Host "check-release: every phase held: $($run -join ', ') ($([math]::Round($suite.Elapsed.TotalMinutes, 1)) min)"
exit 0
