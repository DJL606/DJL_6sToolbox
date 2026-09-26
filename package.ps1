$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root "dist"
$version = "1.0.0"
$exeName = "DJL_6sToolbox.exe"
$out = Join-Path $root "DJL_6sToolbox-v$version-win-x64.zip"

if (-not (Test-Path (Join-Path $dist $exeName))) {
    & (Join-Path $root "build.cmd")
}

if (Test-Path $out) { Remove-Item $out -Force }

Compress-Archive -Path (Join-Path $dist $exeName) -DestinationPath $out -CompressionLevel Optimal
Write-Host "Package created: $out"
