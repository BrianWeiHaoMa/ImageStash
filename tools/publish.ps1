<#
.SYNOPSIS
Publishes ImageStash as one self-contained win-x64 folder, the build a user copies onto a PC and runs (F1).

.EXAMPLE
powershell.exe -NoProfile -File tools/publish.ps1 -Output $env:TEMP\ImageStash
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $Output
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\ImageStash.Windows\ImageStash.Windows.csproj'
& dotnet publish $project -c Release -r win-x64 --self-contained -o $Output
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}
