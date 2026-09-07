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
    [switch]$Uninstall,                                   # copot add-in, tanpa build
    [switch]$Verify,                                      # periksa pemasangan yang ada, tanpa build
    [string]$RevitInstallPath = $env:RevitInstallPath,
    [string]$RevitVersion = "2025",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }   # lighting/revit

# Dua lokasi manifest yang dibaca Revit: semua pengguna (ProgramData) & per pengguna (AppData).
$addinFolders = [ordered]@{
    "Semua pengguna" = (Join-Path $env:ProgramData "Autodesk\Revit\Addins\$RevitVersion")
    "Pengguna ini"   = (Join-Path $env:APPDATA     "Autodesk\Revit\Addins\$RevitVersion")
}

# Laporkan isi kedua folder add-in + apakah <Assembly> tiap manifest benar-benar ada.
# Penyebab paling sering "tab tidak muncul": DLL berada di sub-folder, sedangkan
# <Assembly> dibaca RELATIF terhadap folder tempat file .addin berada.
function Show-Pemasangan {
    $adaMasalah = $false
    foreach ($nama in $addinFolders.Keys) {
        $folder = $addinFolders[$nama]
        Write-Host ""
        Write-Host "[$nama] $folder"
        if (-not (Test-Path $folder)) { Write-Host "  (folder belum ada)" -ForegroundColor DarkGray; continue }

        $manifests = @(Get-ChildItem -Path $folder -Filter "LuxoraRevit.addin" -ErrorAction SilentlyContinue)
        if ($manifests.Count -eq 0) { Write-Host "  (tidak ada LuxoraRevit.addin)" -ForegroundColor DarkGray }
        foreach ($m in $manifests) {
            Write-Host "  manifest : $($m.FullName)"
            try {
                $xml = [xml](Get-Content -LiteralPath $m.FullName -Raw)
                $asm = if ($xml.DocumentElement.Name -eq 'RevitAddIns') { $xml.RevitAddIns.AddIn.Assembly } else { $xml.AddIn.Assembly }
                $resolved = if ([IO.Path]::IsPathRooted($asm)) { $asm } else { Join-Path $folder $asm }
                Write-Host "  Assembly : $asm"
                if (Test-Path -LiteralPath $resolved) {
                    Write-Host "  -> DLL ditemukan di $resolved" -ForegroundColor Green
                } else {
                    Write-Host "  -> DLL TIDAK ADA di $resolved" -ForegroundColor Red
                    Write-Host "     Revit akan melewati add-in ini tanpa pesan apa pun." -ForegroundColor Yellow
                    $adaMasalah = $true
                }
            } catch {
                Write-Host "  -> manifest tidak terbaca: $($_.Exception.Message)" -ForegroundColor Red
                $adaMasalah = $true
            }
        }
        $nyasar = @(Get-ChildItem -Path $folder -Recurse -Filter "LuxoraRevit.dll" -ErrorAction SilentlyContinue)
        foreach ($d in $nyasar) { Write-Host "  DLL      : $($d.FullName)" }
        if ($nyasar.Count -eq 0 -and $manifests.Count -gt 0) { $adaMasalah = $true }
    }
    Write-Host ""
    if ($adaMasalah) { Write-Host "Ada pemasangan yang tidak konsisten — jalankan ulang dengan -Install." -ForegroundColor Yellow }
    return $adaMasalah
}

function Remove-Pemasangan {
    foreach ($nama in $addinFolders.Keys) {
        $folder = $addinFolders[$nama]
        if (-not (Test-Path $folder)) { continue }
        foreach ($pola in @("LuxoraRevit.addin", "LuxoraRevit.dll", "LuxoraRevit.pdb", "LuxoraRevit.deps.json")) {
            Get-ChildItem -Path $folder -Filter $pola -ErrorAction SilentlyContinue |
                ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
                                 Write-Host "  dihapus: $($_.FullName)" -ForegroundColor DarkGray }
        }
        # sub-folder LuxoraRevit\ dari pemasangan manual — inilah yang membuat Revit tak menemukan DLL
        $sub = Join-Path $folder "LuxoraRevit"
        if (Test-Path $sub) {
            Remove-Item -LiteralPath $sub -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "  dihapus: $sub" -ForegroundColor DarkGray
        }
    }
}

if ($Verify) {
    Write-Host "Memeriksa pemasangan add-in Luxora untuk Revit $RevitVersion ..."
    $masalah = Show-Pemasangan
    exit ([int][bool]$masalah)
}

if ($Uninstall) {
    Write-Host "Mencopot add-in Luxora (Revit $RevitVersion) ..."
    Remove-Pemasangan
    Write-Host "Selesai. Mulai ulang Revit." -ForegroundColor Green
    exit 0
}

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
    $addinFolder = if ($CurrentUser) { $addinFolders["Pengguna ini"] } else { $addinFolders["Semua pengguna"] }

    # Bersihkan sisa pemasangan lama (termasuk sub-folder & salinan di lokasi satunya) supaya
    # tidak ada dua manifest ber-AddInId sama yang saling bentrok.
    Write-Host ""
    Write-Host "Membersihkan pemasangan lama ..."
    Remove-Pemasangan

    if (-not (Test-Path $addinFolder)) {
        try { New-Item -ItemType Directory -Path $addinFolder -Force | Out-Null }
        catch {
            Write-Host "Gagal membuat folder add-in: $addinFolder" -ForegroundColor Red
            Write-Host "  Jalankan PowerShell sebagai Administrator, atau pakai -CurrentUser." -ForegroundColor Yellow
            exit 1
        }
    }

    $dllTujuan = Join-Path $addinFolder "LuxoraRevit.dll"
    try {
        Copy-Item $dll.FullName $dllTujuan -Force
        $pdb = [IO.Path]::ChangeExtension($dll.FullName, ".pdb")
        if (Test-Path $pdb) { Copy-Item $pdb $addinFolder -Force }

        # Manifest ditulis ulang dgn path DLL absolut: apa pun tata letak foldernya,
        # Revit selalu menemukan assembly-nya.
        $manifestTujuan = Join-Path $addinFolder "LuxoraRevit.addin"
        $xml = [xml](Get-Content -LiteralPath (Join-Path $root "LuxoraRevit.addin") -Raw)
        # Manifest memakai root <RevitAddIns> (format resmi Revit). Ambil node <AddIn> di dalamnya.
        $addInNode = if ($xml.DocumentElement.Name -eq 'RevitAddIns') { $xml.RevitAddIns.AddIn } else { $xml.AddIn }
        $addInNode.Assembly = $dllTujuan
        $xml.Save($manifestTujuan)

        # File hasil unduhan bisa ditandai "blocked" oleh Windows sehingga Revit menolak memuatnya.
        Get-ChildItem -Path $addinFolder -Filter "LuxoraRevit.*" -ErrorAction SilentlyContinue |
            Unblock-File -ErrorAction SilentlyContinue
    } catch {
        Write-Host "Gagal menyalin ke $addinFolder : $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "  Tutup Revit lebih dulu (DLL terkunci), atau jalankan sebagai Administrator / pakai -CurrentUser." -ForegroundColor Yellow
        exit 1
    }

    Write-Host "Dipasang ke $addinFolder" -ForegroundColor Green
    if (Show-Pemasangan) { exit 1 }
    Write-Host "Mulai ulang Revit, lalu cari tab 'Luxora' di Ribbon." -ForegroundColor Green
}
