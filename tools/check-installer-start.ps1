<#
.SYNOPSIS
Judges, without installing it, the step the MSI -Msi takes to start ImageStash once it has installed, upgraded or
repaired it (G3, F1), from the package's own tables: one custom action runs the installed ImageStash.Windows.exe from
the install folder with no argument, so the start is a launch by hand; it is immediate and not marked to run without
impersonation, so it runs as the signed-in user; it is asynchronous with its result ignored, so the install neither
waits for ImageStash, which never exits, nor fails when it cannot start it; it is sequenced after InstallFinalize and
after the earlier version's removal (RemoveExistingProducts), once the new files are in place and nothing of the
earlier version is left to end the copy it starts; and its condition, as Windows Installer itself evaluates it, holds for a first install, an
upgrade, a repair and a run over the installed version, and for no removal, an uninstall or this version's removal
inside a later upgrade, naming no property but REMOVE. Prints an ok or FAIL line per judgement and exits with the
number that failed. The release check's assets phase runs it on each MSI it builds; it needs no desktop.

.EXAMPLE
powershell.exe -NoProfile -File tools/check-installer-start.ps1 -Msi $env:TEMP\ImageStash-assets\ImageStash-0.2.2-win-x64.msi
#>
param(
    [Parameter(Mandatory)] [string] $Msi
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'msi-tables.ps1')
$failed = 0

function Check([bool] $held, [string] $what) {
    if ($held) { Write-Host "ok   $what" } else { Write-Host "FAIL $what"; $script:failed++ }
}

# Windows Installer's custom action type bits, as WiX 6.0.2 names them (WindowsInstallerConstants.MsidbCustomActionType*).
$exeInDirectory = 0x22   # Exe (0x2) with its working folder in Source (Directory, 0x20)
$continue = 0x40         # its result ignored
$async = 0x80            # not waited for (with Continue, not even at the end of the install)
$inScript = 0x400        # deferred into the install script
$noImpersonate = 0x800   # run as the installer's own account
$commandLine = '"[INSTALLFOLDER]ImageStash.Windows.exe"'
$someProduct = '{00000000-0000-0000-0000-000000000001}'

# What the installer is doing, the properties Windows Installer sets for it, and whether the step must run then.
$cases = @(
    @{ What = 'a first install'; Properties = @{}; Runs = $true },
    @{ What = 'an upgrade, seen from the newer package'; Properties = @{ WIX_UPGRADE_DETECTED = $someProduct }; Runs = $true },
    @{ What = 'a repair (msiexec /fa)'; Properties = @{ Installed = '1'; REINSTALL = 'ALL'; REINSTALLMODE = 'a' }; Runs = $true },
    @{ What = 'a run over the installed version'; Properties = @{ Installed = '1' }; Runs = $true },
    @{ What = 'an uninstall'; Properties = @{ Installed = '1'; REMOVE = 'ALL' }; Runs = $false },
    @{ What = "this version's removal inside a later upgrade"; Properties = @{ Installed = '1'; REMOVE = 'ALL'; UPGRADINGPRODUCTCODE = $someProduct }; Runs = $false }
)
$caseProperties = @($cases | ForEach-Object { $_.Properties.Keys } | Sort-Object -Unique)

# Whether <condition> holds with each case's properties set, as the package's own session evaluates it: an empty
# condition always holds. The session ignores this PC's state and runs no action.
function ConditionHolds([string] $msi, [string] $condition) {
    $session = Com $installer 'OpenPackage' 'InvokeMethod' @($msi, [object] 1)
    try {
        foreach ($case in $cases) {
            foreach ($name in $caseProperties) {
                $value = if ($case.Properties.ContainsKey($name)) { $case.Properties[$name] } else { '' }
                # Cast: a name that came through a pipeline reaches COM wrapped, which it refuses as a type mismatch.
                $null = Com $session 'Property' 'SetProperty' @([string] $name, [string] $value)
            }
            # 0 false, 1 true, 2 no condition, 3 a condition that does not parse.
            $result = if ($condition) { [int] (Com $session 'EvaluateCondition' 'InvokeMethod' @($condition)) } else { 2 }
            @{ Case = $case; Runs = (($result -eq 1) -or ($result -eq 2)); Result = $result }
        }
    }
    finally {
        $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($session)
    }
}

# Each judgement of the package's start step; a missing step or sequence row ends the judging there.
function JudgeStart([string] $package) {
    $name = Split-Path $package -Leaf
    $starts = @(MsiRows $package 'SELECT Action, Type, Source, Target FROM CustomAction' | Where-Object { "$($_[3])" -match 'ImageStash\.Windows\.exe' })
    Check ($starts.Count -eq 1) "${name}: one custom action starts ImageStash.Windows.exe: $(@($starts | ForEach-Object { $_[0] }) -join ', ')"
    if ($starts.Count -ne 1) { return }
    $action, $typeText, $source, $target = $starts[0]
    $type = [int] $typeText
    Check ((($type -band 0x3F) -eq $exeInDirectory) -and ($source -eq 'INSTALLFOLDER') -and ($target -ceq $commandLine)) "${name}: $action runs $commandLine from INSTALLFOLDER, the installed executable with no argument (G3): type $type, source $source, target $target"
    Check (($type -band ($async -bor $continue)) -eq ($async -bor $continue)) "${name}: $action is not waited for and its result is ignored, so the install ends while ImageStash runs, or cannot start (G3): type $type"
    Check (($type -band ($inScript -bor $noImpersonate)) -eq 0) "${name}: $action is immediate and impersonates, so it runs as the signed-in user (F1, F6): type $type"
    $sequence = @(MsiRows $package 'SELECT Action, Condition, Sequence FROM InstallExecuteSequence')
    $step = @($sequence | Where-Object { $_[0] -eq $action })
    $finalize = @($sequence | Where-Object { $_[0] -eq 'InstallFinalize' })
    Check (($step.Count -eq 1) -and ($finalize.Count -eq 1) -and ([int] $step[0][2] -gt [int] $finalize[0][2])) "${name}: $action is in InstallExecuteSequence after InstallFinalize, once the new files are in place (G3, G31): $(@($step | ForEach-Object { $_[2] }) -join ', ') after $(@($finalize | ForEach-Object { $_[2] }) -join ', ')"
    # The earlier version's removal ends any running copy by name, so a start before it would be ended there.
    $removal = @($sequence | Where-Object { $_[0] -eq 'RemoveExistingProducts' })
    Check (($step.Count -eq 1) -and ($removal.Count -eq 1) -and ([int] $step[0][2] -gt [int] $removal[0][2])) "${name}: $action comes after RemoveExistingProducts, so an upgrade's removal of the earlier version cannot end the copy it starts (G3): $(@($step | ForEach-Object { $_[2] }) -join ', ') after $(@($removal | ForEach-Object { $_[2] }) -join ', ')"
    if ($step.Count -ne 1) { return }
    $condition = "$($step[0][1])"
    foreach ($judged in (ConditionHolds $package $condition)) {
        $must = if ($judged.Case.Runs) { 'runs' } else { 'does not run' }
        Check ((@(0, 1, 2) -contains $judged.Result) -and ($judged.Runs -eq $judged.Case.Runs)) "${name}: on $($judged.Case.What), $action $must (G3): condition '$condition' evaluates $($judged.Result)"
    }
    # Every property the condition names, its quoted strings and operators left out: none may switch the step off.
    $named = @([regex]::Matches(($condition -replace '"[^"]*"', ''), '[A-Za-z_][A-Za-z0-9_.]*') | ForEach-Object { $_.Value } | Where-Object { @('NOT', 'AND', 'OR', 'XOR', 'EQV', 'IMP') -notcontains $_.ToUpperInvariant() } | Sort-Object -Unique)
    Check (($named -join ',') -ceq 'REMOVE') "${name}: the condition names no property but REMOVE, so nothing else can skip the start (G3): $($named -join ', ')"
}

JudgeStart (Resolve-Path -LiteralPath $Msi).ProviderPath
if ($failed -gt 0) { Write-Host "$failed check(s) failed." } else { Write-Host 'Every check held.' }
exit $failed
