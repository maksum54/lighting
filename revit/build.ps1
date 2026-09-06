# build.ps1 — membangun add-in Luxora untuk Autodesk Revit 2025.
#
# Prasyarat:
#   - Revit 2025 terpasang (menyediakan RevitAPI.dll & RevitAPIUI.dll) ATAU
#     set -RevitInstallPath / $env:RevitInstallPath ke folder berisi kedua DLL itu.
#   - .NET SDK 8 (untuk net8.0-windows).
#
# Jalankan dari mana saja:
#   powershell -ExecutionPolicy Bypass -File revit/build.ps1
#
# Hasil:
#   revit/bin/Release/net8.0-windows/LuxoraRevit.dll
#   dan (opsional, -Install) menyalin .addin + DLL ke folder add-in Revit.

[CmdletBinding()]
param(
    [switch]$Install,                                     # ikut pasang ke folder add-in Revit
    [switch]$CurrentUser,                                 # pasang ke %AppData% (bukan ProgramData)
    [string]$RevitInstallPath = $env:RevitInstallPath,
    [string]$RevitVersion = "2025",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }   # lighting/revit

# --- .NET SDK ---------------------------------------------------------------
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Host "ERROR: 'dotnet' tidak ditemukan di PATH." -ForegroundColor Red
    Write-Host "  Pasang .NET SDK 8 dari https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    exit 1
}

# --- folder RevitAPI --------------------------------------------------------
if ($RevitInstallPath) { $RevitInstallPath = $RevitInstallPath.TrimEnd('\', '/') }
if (-not $RevitInstallPath) {
    # $env:ProgramW6432 / $env:ProgramFiles bisa kosong — susun string apa adanya agar aman.
    $bases = @($env:ProgramW6432, ${env:ProgramFiles}, "C:\Program Files") | Where-Object { $_ } | Select-Object -Unique
    $candidates = $bases | ForEach-Object { "$_\Autodesk\Revit $RevitVersion" }
    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c "RevitAPI.dll")) { $RevitInstallPath = $c; break }
    }
}
if (-not $RevitInstallPath -or -not (Test-Path (Join-Path $RevitInstallPath "RevitAPI.dll"))) {
    Write-Host "ERROR: RevitAPI.dll tidak ditemukan." -ForegroundColor Red
    Write-Host "  Pasang Revit $RevitVersion, atau tentukan foldernya:" -ForegroundColor Yellow
    Write-Host "  powershell -File revit/build.ps1 -RevitInstallPath 'C:\Program Files\Autodesk\Revit $RevitVersion'" -ForegroundColor Yellow
    exit 1
}
if (-not (Test-Path (Join-Path $RevitInstallPath "RevitAPIUI.dll"))) {
    Write-Host "ERROR: RevitAPIUI.dll tidak ada di '$RevitInstallPath'." -ForegroundColor Red
    Write-Host "  Folder harus berisi RevitAPI.dll DAN RevitAPIUI.dll." -ForegroundColor Yellow
    exit 1
}
Write-Host "Revit API   : $RevitInstallPath"
Write-Host "Konfigurasi : $Configuration (Revit $RevitVersion)"

# --- build ------------------------------------------------------------------
# Properti dikirim eksplisit ke MSBuild supaya tidak bergantung variabel lingkungan.
& dotnet build (Join-Path $root "LuxoraRevit.sln") -c $Configuration `
    "-p:RevitInstallPath=$RevitInstallPath" `
    "-p:RevitVersion=$RevitVersion"
if ($LASTEXITCODE -ne 0) { Write-Host "Build gagal." -ForegroundColor Red; exit $LASTEXITCODE }

# Cari DLL hasil build (BaseOutputPath diarahkan ke revit/bin, tapi tetap dicari agar
# skrip tidak pernah lagi menunjuk path yang salah bila konfigurasi proyek berubah).
$dll = Get-ChildItem -Path $root -Recurse -Filter "LuxoraRevit.dll" -ErrorAction SilentlyContinue |
       Where-Object { $_.FullName -match [regex]::Escape("\$Configuration\") } |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $dll) {
    Write-Host "ERROR: build selesai tetapi LuxoraRevit.dll tidak ditemukan di $root." -ForegroundColor Red
    exit 1
}
Write-Host ""
Write-Host "OK. DLL add-in:" -ForegroundColor Green
Write-Host "  $($dll.FullName)"

# --- install (opsional) -----------------------------------------------------
if ($Install) {
    $addinFolder = if ($CurrentUser) {
        Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
    } else {
        Join-Path $env:ProgramData "Autodesk\Revit\Addins\$RevitVersion"
    }
    if (-not (Test-Path $addinFolder)) {
        try { New-Item -ItemType Directory -Path $addinFolder -Force | Out-Null }
        catch {
            Write-Host "Gagal membuat folder add-in: $addinFolder" -ForegroundColor Red
            Write-Host "  Jalankan PowerShell sebagai Administrator, atau pakai -CurrentUser." -ForegroundColor Yellow
            exit 1
        }
    }
    try {
        Copy-Item $dll.FullName $addinFolder -Force
        $pdb = [IO.Path]::ChangeExtension($dll.FullName, ".pdb")
        if (Test-Path $pdb) { Copy-Item $pdb $addinFolder -Force }
        Copy-Item (Join-Path $root "LuxoraRevit.addin") $addinFolder -Force
    } catch {
        Write-Host "Gagal menyalin ke $addinFolder : $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "  Tutup Revit lebih dulu (DLL terkunci), atau jalankan sebagai Administrator / pakai -CurrentUser." -ForegroundColor Yellow
        exit 1
    }
    Write-Host "Dipasang ke $addinFolder" -ForegroundColor Green
    Write-Host "Mulai ulang Revit, lalu cari tab 'Luxora' di Ribbon."
}
