$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$redistDir = Join-Path $repoRoot 'installer\redist'
$runtimeFile = 'windowsdesktop-runtime-8.0.25-win-x64.exe'
$runtimePath = Join-Path $redistDir $runtimeFile
$runtimeUrl = 'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/8.0.25/windowsdesktop-runtime-8.0.25-win-x64.exe'

New-Item -ItemType Directory -Force -Path $redistDir | Out-Null

if (Test-Path $runtimePath) {
    Write-Host "Already present: $runtimePath"
    exit 0
}

Write-Host "Downloading .NET Desktop Runtime 8.0.25 x64..."
Invoke-WebRequest -Uri $runtimeUrl -OutFile $runtimePath

Write-Host "Downloaded: $runtimePath"
