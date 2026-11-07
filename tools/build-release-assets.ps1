<#
.SYNOPSIS
Builds everything a release of ImageStash holds into one folder, from one publish (F1): ImageStash\, the folder
tools/publish.ps1 writes; ImageStash-<version>-win-x64.zip, that folder zipped with its entries under ImageStash/;
ImageStash-<version>-win-x64.msi, the per-user installer built from that same folder by installer\; and
SHA256SUMS.txt, one "<sha256>  <file name>" line for the zip and the MSI.

The version is Directory.Build.props's unless -Version gives another for this run. -Output must be a new or empty
folder; one that holds anything is refused and left as it is. Exits nonzero, naming the step, when any step fails.

.EXAMPLE
powershell.exe -NoProfile -File tools/build-release-assets.ps1 -Output $env:TEMP\ImageStash-assets
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $Output,
    [string] $Version = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
if ((Test-Path -LiteralPath $Output) -and @(Get-ChildItem -LiteralPath $Output -Force).Count -gt 0) {
    throw "-Output must be a new or empty folder, and $Output is not"
}
if (-not $Version) {
    $props = [xml](Get-Content -Raw -Path (Join-Path $root 'Directory.Build.props'))
    $Version = "$($props.Project.PropertyGroup.Version)".Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "the version must be three numbers, major.minor.patch, and '$Version' is not"
}

$name = "ImageStash-$Version-win-x64"
$folder = Join-Path $Output 'ImageStash'
$zip = Join-Path $Output "$name.zip"
$msi = Join-Path $Output "$name.msi"
New-Item -ItemType Directory -Force -Path $Output | Out-Null

& (Join-Path $PSScriptRoot 'publish.ps1') -Output $folder -Version $Version | Out-Host

# Entries under ImageStash/ with forward slashes, so every unzipper opens it as one folder: each is named here, since
# under Windows PowerShell both Compress-Archive and ZipFile.CreateFromDirectory write backslashes into entry names.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $folder -Recurse -File -Force) {
        $entry = 'ImageStash/' + $file.FullName.Substring($folder.Length + 1).Replace('\', '/')
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal)
    }
}
finally {
    $archive.Dispose()
}

$build = Join-Path $env:TEMP ('ImageStash-installer-build-' + [guid]::NewGuid().ToString('N'))
try {
    & dotnet build (Join-Path $root 'installer\ImageStash.Installer.wixproj') -c Release --no-incremental -nologo "-p:PublishDir=$folder" "-p:Version=$Version" -o $build | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "the installer build failed with exit code $LASTEXITCODE"
    }
    Move-Item -LiteralPath (Join-Path $build 'ImageStash.msi') -Destination $msi
}
finally {
    if (Test-Path -LiteralPath $build) { Remove-Item -LiteralPath $build -Recurse -Force }
}

# The sha256sum shape, LF endings and no byte-order mark, so `sha256sum -c SHA256SUMS.txt` reads it.
$sums = foreach ($asset in $msi, $zip) {
    '{0}  {1}' -f (Get-FileHash -Algorithm SHA256 -LiteralPath $asset).Hash.ToLowerInvariant(), (Split-Path $asset -Leaf)
}
[IO.File]::WriteAllText((Join-Path $Output 'SHA256SUMS.txt'), (($sums -join "`n") + "`n"), (New-Object Text.UTF8Encoding $false))
Write-Host "release assets for $Version in ${Output}:"
Get-ChildItem -LiteralPath $Output | ForEach-Object { Write-Host "  $($_.Name)" }
