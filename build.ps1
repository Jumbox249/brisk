# Builds Brisk into .\dist, and with -Installer also .\out\Brisk-Setup-<version>.exe
# Requires: Rust (cargo) and the .NET 10 SDK; Inno Setup 6 for the installer.
# Native tools write progress to stderr, so rely on exit codes rather than ErrorActionPreference.
param([switch]$Installer)
Set-Location $PSScriptRoot

cargo build --release --manifest-path native/adblock-ffi/Cargo.toml
if ($LASTEXITCODE -ne 0) { throw 'cargo build failed' }

dotnet publish src/Brisk/Brisk.csproj -c Release -o dist
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

$size = (Get-ChildItem dist -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
"Built dist\Brisk.exe ({0:N1} MB total)" -f $size

if ($Installer) {
    $iscc = @(
        (Get-Command iscc -ErrorAction SilentlyContinue).Source,
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found' }
    $version = ([xml](Get-Content src/Brisk/Brisk.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    & $iscc /Q "/DVersion=$version" installer/brisk.iss
    if ($LASTEXITCODE -ne 0) { throw 'installer build failed' }
    "Built out\Brisk-Setup-$version.exe"
}
