<#
.SYNOPSIS
Publishes ImageStash as one self-contained win-x64 folder, the build a user copies onto a PC and runs (F1).

-Version, when given, stamps that version on the build in place of Directory.Build.props's.

.EXAMPLE
powershell.exe -NoProfile -File tools/publish.ps1 -Output $env:TEMP\ImageStash
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $Output,
    [string] $Version = ''
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\ImageStash.Windows\ImageStash.Windows.csproj'
$stamp = @()
if ($Version) { $stamp = @("-p:Version=$Version") }
& dotnet publish $project -c Release -r win-x64 --self-contained -o $Output @stamp
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}
