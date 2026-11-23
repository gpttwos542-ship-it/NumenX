# Local build (needs the .NET 8 SDK: winget install Microsoft.DotNet.SDK.8)
# Optional: Inno Setup 6 to also create the installer.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

dotnet publish src/NumenX/NumenX.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish
if ($LASTEXITCODE -ne 0) { Write-Host "Build FAILED" -ForegroundColor Red; exit 1 }

if (-not (Test-Path publish\ffmpeg.exe)) {
    $ff = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
    if ($ff) { Copy-Item $ff publish\ffmpeg.exe }
    else { Write-Host "NOTE: put ffmpeg.exe into the 'publish' folder (winget install Gyan.FFmpeg, then copy it)." -ForegroundColor Yellow }
}

$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) { $iscc = "$env:ProgramFiles\Inno Setup 6\ISCC.exe" }
if (Test-Path $iscc) {
    & $iscc installer\numenx.iss
    Write-Host "Installer: installer\Output\NumenX-Setup.exe" -ForegroundColor Green
} else {
    Write-Host "App built in .\publish  (run publish\NumenX.exe). Install Inno Setup 6 to also build the installer." -ForegroundColor Green
}
