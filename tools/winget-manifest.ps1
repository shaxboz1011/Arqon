<#
.SYNOPSIS
  Writes the three winget manifests (version, installer, defaultLocale) of one Arqon release, ready for `wingetcreate submit`.
  Arqon is one exe, so winget installs it as a "portable" package: no installer runs, `arqon` becomes a command.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\winget-manifest.ps1 -Exe publish\win-x64\Arqon.exe
  powershell -ExecutionPolicy Bypass -File tools\winget-manifest.ps1 -Exe out\Arqon.exe -Version 0.3.0 -Output out\winget
#>
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$Version,
    [string]$Output = (Join-Path $PSScriptRoot "..\publish\winget")
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$id = "shaxboz1011.Arqon"
$repo = "https://github.com/shaxboz1011/Arqon"

if (-not $Version) {
    [xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
    $Version = "$(@($props.Project.PropertyGroup.Version) | Where-Object { $_ } | Select-Object -First 1)".Trim()
}
if ($Version -notmatch '^\d+(\.\d+){1,3}(-[0-9A-Za-z.]+)?$') { throw "Not a version: '$Version'" }

$hash = (Get-FileHash -Algorithm SHA256 $Exe).Hash.ToUpperInvariant()
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$Output = (Resolve-Path $Output).Path
$schema = "1.6.0"

function Write-Manifest([string]$Name, [string]$Text) {
    $path = Join-Path $Output $Name
    [System.IO.File]::WriteAllText($path, $Text.Replace("`r`n", "`n"), (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Wrote $path"
}

Write-Manifest "$id.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema

"@

Write-Manifest "$id.installer.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
InstallerType: portable
Commands:
- arqon
ReleaseDate: $(Get-Date -Format yyyy-MM-dd)
Installers:
- Architecture: x64
  InstallerUrl: $repo/releases/download/v$Version/Arqon.exe
  InstallerSha256: $hash
ManifestType: installer
ManifestVersion: $schema

"@

Write-Manifest "$id.locale.en-US.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: Arqon contributors
PublisherUrl: https://github.com/shaxboz1011
PublisherSupportUrl: $repo/issues
PackageName: Arqon
PackageUrl: $repo
License: MIT
LicenseUrl: $repo/blob/main/LICENSE
ShortDescription: Use Wi-Fi, Ethernet and your phone's internet at once for faster downloads and smooth games.
Description: Arqon spreads downloads, torrents and (in system mode) the connections of every app over all your internet links, and keeps game ping low while something downloads.
Moniker: arqon
Tags:
- bandwidth
- download-manager
- multi-link
- network
- tethering
- torrent
ReleaseNotesUrl: $repo/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $schema

"@
