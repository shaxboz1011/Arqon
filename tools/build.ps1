<#
.SYNOPSIS
  Builds one self-contained Arqon.exe (the .NET runtime is inside; nothing to install) and writes its SHA-256 next to it.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\build.ps1
  powershell -ExecutionPolicy Bypass -File tools\build.ps1 -Output C:\Tools\Arqon
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot "..\publish\win-x64"),
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("arqon-publish-" + [guid]::NewGuid().ToString("N"))

try {
    dotnet publish (Join-Path $root "src\Arqon.App") -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none -o $staging
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    New-Item -ItemType Directory -Path $Output -Force | Out-Null
    $exe = Join-Path $Output "Arqon.exe"
    Copy-Item (Join-Path $staging "Arqon.App.exe") $exe -Force

    $hash = (Get-FileHash -Algorithm SHA256 $exe).Hash.ToLowerInvariant()
    Set-Content -Path "$exe.sha256" -Value "$hash  Arqon.exe" -Encoding ascii
    $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "Built $exe ($size MB)"
    Write-Host "SHA-256 $hash"
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
