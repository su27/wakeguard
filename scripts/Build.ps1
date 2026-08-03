$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    dotnet run --project .\tools\WakeGuard.IconGenerator\WakeGuard.IconGenerator.csproj --configuration Release -- .\assets\icon-source .\src\WakeGuard.Tray\Assets
    dotnet test .\WakeGuard.slnx --configuration Release
    dotnet publish .\src\WakeGuard.Tray\WakeGuard.Tray.csproj -p:PublishProfile=win-x64
    dotnet publish .\src\WakeGuard.Service\WakeGuard.Service.csproj -p:PublishProfile=win-x64
    dotnet build .\installer\WakeGuard.Installer.wixproj --configuration Release -p:InstallerCompression=high
}
finally {
    Pop-Location
}
