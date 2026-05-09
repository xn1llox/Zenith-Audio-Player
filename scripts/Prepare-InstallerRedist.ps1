$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$redistDir = Join-Path $repoRoot 'installer\redist'
$runtimeFile = 'windowsdesktop-runtime-8.0.25-win-x64.exe'
$runtimePath = Join-Path $redistDir $runtimeFile
$runtimeUrl = 'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/8.0.25/windowsdesktop-runtime-8.0.25-win-x64.exe'
$windowsAppRuntimeFile = 'WindowsAppRuntimeInstall-1.8.260416003-x64.exe'
$windowsAppRuntimePath = Join-Path $redistDir $windowsAppRuntimeFile
$windowsAppRuntimeUrl = 'https://aka.ms/windowsappsdk/1.8/1.8.260416003/windowsappruntimeinstall-x64.exe'

New-Item -ItemType Directory -Force -Path $redistDir | Out-Null

if (Test-Path $runtimePath) {
    Write-Host "Already present: $runtimePath"
}
else {
    Write-Host "Downloading .NET Desktop Runtime 8.0.25 x64..."
    Invoke-WebRequest -Uri $runtimeUrl -OutFile $runtimePath
    Write-Host "Downloaded: $runtimePath"
}

if (Test-Path $windowsAppRuntimePath) {
    Write-Host "Already present: $windowsAppRuntimePath"
}
else {
    Write-Host "Downloading Windows App Runtime 1.8.260416003 x64..."
    Invoke-WebRequest -Uri $windowsAppRuntimeUrl -OutFile $windowsAppRuntimePath
    Write-Host "Downloaded: $windowsAppRuntimePath"
}
