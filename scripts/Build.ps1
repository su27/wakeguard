$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    dotnet run --project .\tools\WakeGuard.IconGenerator\WakeGuard.IconGenerator.csproj --configuration Release -- .\assets\icon-source .\src\WakeGuard.Tray\Assets
    dotnet test .\WakeGuard.slnx --configuration Release
    $manifest = '.\src\WakeGuard.Tauri\src-tauri\Cargo.toml'
    cargo fmt --manifest-path $manifest -- --check
    cargo test --locked --manifest-path $manifest
    node --check .\src\WakeGuard.Tauri\ui\app.js
    cargo build --locked --release --manifest-path $manifest
    $trayPublishDir = Join-Path $projectRoot 'artifacts\publish\win-x64\Tray'
    New-Item -ItemType Directory -Path $trayPublishDir -Force | Out-Null
    Copy-Item -LiteralPath '.\src\WakeGuard.Tauri\src-tauri\target\release\wakeguard-tray.exe' -Destination (Join-Path $trayPublishDir 'WakeGuard.Tray.exe') -Force
    dotnet publish .\src\WakeGuard.Service\WakeGuard.Service.csproj -p:PublishProfile=win-x64
    dotnet build .\installer\WakeGuard.Installer.wixproj --configuration Release -p:InstallerCompression=high
}
finally {
    Pop-Location
}
