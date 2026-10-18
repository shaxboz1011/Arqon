<#
.SYNOPSIS
  Downloads the official WinDivert release and copies WinDivert.dll and WinDivert64.sys next to Arqon.exe.

.DESCRIPTION
  WinDivert (LGPLv3) is the packet driver Arqon's system mode uses. Its binaries are not stored in this repository.
  The script refuses to install the driver unless Windows reports a valid Authenticode signature on it.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\get-windivert.ps1 -Destination src\Arqon.App\bin\Debug\net10.0-windows
#>
param(
    [Parameter(Mandatory = $true)][string]$Destination,
    [string]$Version = "2.2.2",
    [string]$ExpectedSha256 = ""
)

$ErrorActionPreference = "Stop"
$url = "https://github.com/basil00/WinDivert/releases/download/v$Version/WinDivert-$Version-A.zip"
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("arqon-windivert-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work | Out-Null

try {
    $zip = Join-Path $work "windivert.zip"
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing

    $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash
    Write-Host "SHA-256 of the archive: $hash"
    if ($ExpectedSha256 -and $hash -ne $ExpectedSha256.ToUpperInvariant()) {
        throw "The archive hash does not match the expected value. Nothing was installed."
    }

    Expand-Archive -Path $zip -DestinationPath $work -Force
    $dll = Get-ChildItem -Path $work -Recurse -Filter "WinDivert.dll" | Where-Object { $_.FullName -match "x64" } | Select-Object -First 1
    $sys = Get-ChildItem -Path $work -Recurse -Filter "WinDivert64.sys" | Select-Object -First 1
    if (-not $dll -or -not $sys) { throw "The archive does not contain the 64-bit WinDivert files." }

    $signature = Get-AuthenticodeSignature $sys.FullName
    Write-Host "Driver signature: $($signature.Status) - $($signature.SignerCertificate.Subject)"
    if ($signature.Status -ne "Valid") { throw "The driver is not validly signed. Nothing was installed." }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item $dll.FullName, $sys.FullName -Destination $Destination -Force
    Write-Host "Installed WinDivert.dll and WinDivert64.sys into $Destination"
    Write-Host "Start Arqon as administrator and switch on System mode in Settings."
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
