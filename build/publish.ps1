<#
.SYNOPSIS
    Builds the Windows release of European Basketball Manager (docs/RELEASE.md).
.DESCRIPTION
    Publishes Ebm.Desktop as a self-contained single-file executable for win-x64 (no .NET install needed), runs the
    smoke test against a throw-away data folder, and zips the result to artifacts/EuropeanBasketballManager-<version>-win-x64.zip.
#>
param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$out = Join-Path $root "artifacts/publish/$Runtime"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish (Join-Path $root 'src/Ebm.Desktop/Ebm.Desktop.csproj') -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# Smoke test: start, migrate a fresh database, render the main window, exit 0.
$smokeData = Join-Path $root 'artifacts/smoke-data'
if (Test-Path $smokeData) { Remove-Item $smokeData -Recurse -Force }
$process = Start-Process -FilePath (Join-Path $out 'Ebm.Desktop.exe') -ArgumentList '--smoke-test', "--Ebm:Paths:DataRoot=$smokeData" -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Smoke test failed with exit code $($process.ExitCode)" }
Remove-Item $smokeData -Recurse -Force

$zip = Join-Path $root "artifacts/EuropeanBasketballManager-$version-$Runtime.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
# The executable can stay locked for a moment after the smoke test (antivirus scan): retry.
for ($attempt = 1; ; $attempt++) {
    try { Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -Force; break }
    catch { if ($attempt -ge 10) { throw }; Start-Sleep -Seconds 3 }
}
Write-Host "Release: $zip"
