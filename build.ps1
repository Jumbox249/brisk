# Builds Brisk into .\dist
# Requires: Rust (cargo) and the .NET 10 SDK.
# Native tools write progress to stderr, so rely on exit codes rather than ErrorActionPreference.
Set-Location $PSScriptRoot

cargo build --release --manifest-path native/adblock-ffi/Cargo.toml
if ($LASTEXITCODE -ne 0) { throw 'cargo build failed' }

dotnet publish src/Brisk/Brisk.csproj -c Release -o dist
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

$size = (Get-ChildItem dist -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
"Built dist\Brisk.exe ({0:N1} MB total)" -f $size
