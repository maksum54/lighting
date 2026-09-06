# build.ps1 — membangun add-in Luxora untuk Autodesk Revit 2025.
#
# Prasyarat:
#   - Revit 2025 terpasang (menyediakan RevitAPI.dll & RevitAPIUI.dll) ATAU
#     set $env:RevitInstallPath ke folder berisi kedua DLL itu.
#   - .NET SDK 8 (untuk net8.0-windows).
#
# Jalankan dari folder repo root (lighting/):
#   powershell -ExecutionPolicy Bypass -File revit/build.ps1
#
# Hasil:
#   revit/bin/Release/net8.0-windows/LuxoraRevit.dll
#   dan (opsional, -Install) menyalin .addin + DLL ke folder add-in Revit 2025.

param(
    [switch]$Install,          # ikut pasang ke folder add-in Revit 2025
    [string]$RevitInstallPath = $env:RevitInstallPath,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path   # lighting/revit
$repo = Split-Path -Parent $root                           # lighting/

if (-not $RevitInstallPath) {
    $candidates = @(
        "$env:ProgramFiles\Autodesk\Revit 2025",
        "C:\Program Files\Autodesk\Revit 2025"
    )
    foreach ($c in $candidates) {
        if (Test-Path "$c\RevitAPI.dll") { $RevitInstallPath = $c; break }
    }
}
if (-not $RevitInstallPath -or -not (Test-Path "$RevitInstallPath\RevitAPI.dll")) {
    Write-Host "ERROR: RevitAPI.dll tidak ditemukan." -ForegroundColor Red
    Write-Host "  Pasang Revit 2025, atau tentukan folder via:" -ForegroundColor Yellow
    Write-Host "  powershell -File revit/build.ps1 -RevitInstallPath 'C:\Program Files\Autodesk\Revit 2025'" -ForegroundColor Yellow
    exit 1
}
Write-Host "Revit API: $RevitInstallPath"

$env:RevitInstallPath = $RevitInstallPath   # dipakai HintPath di .csproj
& dotnet build "$root\LuxoraRevit.sln" -c $Configuration
if ($LASTEXITCODE -ne 0) { Write-Host "Build gagal." -ForegroundColor Red; exit $LASTEXITCODE }

$dll = Join-Path $root "bin\$Configuration\net8.0-windows\LuxoraRevit.dll"
Write-Host ""
Write-Host "OK. DLL add-in:" -ForegroundColor Green
Write-Host "  $dll"

if ($Install) {
    $addinFolder = "C:\ProgramData\Autodesk\Revit\Addins\2025"
    if (-not (Test-Path $addinFolder)) {
        Write-Host "Folder add-in tidak ada: $addinFolder" -ForegroundColor Yellow
        Write-Host "Buat manual atau jalankan Revit sekali (akan dibuat otomatis)."
    } else {
        Copy-Item $dll $addinFolder -Force
        Copy-Item "$root\LuxoraRevit.addin" $addinFolder -Force
        Write-Host "Dipasang ke $addinFolder" -ForegroundColor Green
        Write-Host "Mulai ulang Revit, lalu cari tab 'Luxora' di Ribbon."
    }
}
