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

                # Revit hanya menerima manifest ber-root <RevitAddIns>. Root lain (mis. <AddIn>
                # langsung) diabaikan total: tab tidak muncul dan tidak ada pesan error.
                $root = $xml.DocumentElement.Name
                if ($root -ne "RevitAddIns") {
                    Write-Host "  -> ROOT SALAH: <$root>, seharusnya <RevitAddIns>." -ForegroundColor Red
                    Write-Host "     Revit mengabaikan manifest ini tanpa pesan apa pun." -ForegroundColor Yellow
                    $adaMasalah = $true
                }

                $node = $xml.SelectSingleNode("//AddIn/Assembly")
                $asm = if ($node) { $node.InnerText } else { "" }
                if (-not $asm) {
                    Write-Host "  -> <Assembly> tidak ada di manifest." -ForegroundColor Red
                    $adaMasalah = $true
                } else {
                    $resolved = if ([IO.Path]::IsPathRooted($asm)) { $asm } else { Join-Path $folder $asm }
                    Write-Host "  Assembly : $asm"
                    if (Test-Path -LiteralPath $resolved) {
                        Write-Host "  -> DLL ditemukan di $resolved" -ForegroundColor Green
                    } else {
                        Write-Host "  -> DLL TIDAK ADA di $resolved" -ForegroundColor Red
                        Write-Host "     Revit akan melewati add-in ini tanpa pesan apa pun." -ForegroundColor Yellow
                        $adaMasalah = $true
                    }
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

# Diagnosa lanjutan bila manifest & DLL sudah benar tetapi tab tetap tidak muncul.
function Show-Diagnosa {
    # 1) Add-in terpasang untuk versi Revit yang mana saja? (salah tahun = tak akan muncul)
    Write-Host ""
    Write-Host "--- Versi Revit yang punya manifest Luxora ---"
    $adaVersi = $false
    foreach ($akar in @((Join-Path $env:ProgramData "Autodesk\Revit\Addins"), (Join-Path $env:APPDATA "Autodesk\Revit\Addins"))) {
        if (-not (Test-Path $akar)) { continue }
        foreach ($tahun in (Get-ChildItem -Path $akar -Directory -ErrorAction SilentlyContinue)) {
            if (Test-Path (Join-Path $tahun.FullName "LuxoraRevit.addin")) {
                Write-Host "  Revit $($tahun.Name) : $($tahun.FullName)"
                $adaVersi = $true
            }
        }
    }
    if (-not $adaVersi) { Write-Host "  (tidak ada)" -ForegroundColor Yellow }
    Write-Host "  -> pastikan Anda membuka Revit versi tsb. (skrip ini menargetkan $RevitVersion)."

    # 2) Berkas ter-blokir Windows (Mark of the Web) — .NET menolak memuatnya.
    Write-Host ""
    Write-Host "--- Status blokir berkas (Mark of the Web) ---"
    foreach ($nama in $addinFolders.Keys) {
        $folder = $addinFolders[$nama]
        if (-not (Test-Path $folder)) { continue }
        foreach ($f in (Get-ChildItem -Path $folder -Filter "LuxoraRevit.*" -ErrorAction SilentlyContinue)) {
            $blocked = $false
            try { if (Get-Item -LiteralPath $f.FullName -Stream "Zone.Identifier" -ErrorAction SilentlyContinue) { $blocked = $true } } catch { }
            if ($blocked) {
                Write-Host "  TERBLOKIR: $($f.FullName) — dibuka blokirnya sekarang." -ForegroundColor Yellow
                Unblock-File -LiteralPath $f.FullName -ErrorAction SilentlyContinue
            } else {
                Write-Host "  bersih   : $($f.Name)" -ForegroundColor DarkGray
            }
        }
    }

    # 3) DLL terpasang menargetkan runtime apa? Revit 2025 = .NET 8, Revit <= 2024 = .NET Framework 4.8.
    Write-Host ""
    Write-Host "--- Target framework DLL terpasang ---"
    foreach ($nama in $addinFolders.Keys) {
        $folder = $addinFolders[$nama]
        $d = Join-Path $folder "LuxoraRevit.dll"
        if (-not (Test-Path $d)) { continue }
        $info = Get-Item -LiteralPath $d
        Write-Host "  $d"
        Write-Host "    ukuran $([math]::Round($info.Length/1KB)) KB, diubah $($info.LastWriteTime)"
        try {
            $teks = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($d))
            $m = [regex]::Match($teks, '\.NET(CoreApp|Framework),Version=v[0-9.]+')
            if ($m.Success) { Write-Host "    target: $($m.Value)" } else { Write-Host "    target: (tidak terbaca)" -ForegroundColor Yellow }
        } catch { Write-Host "    target: (gagal dibaca)" -ForegroundColor Yellow }
    }

    # 4) Log startup add-in — penentu apakah Revit benar-benar memuat DLL ini.
    Write-Host ""
    Write-Host "--- Log startup add-in ---"
    $log = Join-Path $env:TEMP "LuxoraRevit-startup.log"
    if (Test-Path $log) {
        Write-Host "  $log" -ForegroundColor Green
        Get-Content -LiteralPath $log -Tail 20 | ForEach-Object { Write-Host "    $_" }
        Write-Host "  -> DLL BERHASIL dimuat Revit. Kalau baris terakhir 'GAGAL', itulah sebabnya." -ForegroundColor Green
    } else {
        Write-Host "  Belum ada $log" -ForegroundColor Yellow
        Write-Host "  -> Revit BELUM PERNAH memuat DLL ini (manifest tidak terbaca, versi Revit lain," -ForegroundColor Yellow
        Write-Host "     berkas ter-blokir, atau add-in ditolak oleh pengaturan keamanan Revit)." -ForegroundColor Yellow
        Write-Host "     Catatan: log baru terisi setelah Revit dijalankan dgn add-in versi terbaru." -ForegroundColor DarkGray
    }

    # 5) Journal Revit — sumber kebenaran soal add-in yang ditolak/gagal dimuat.
    Write-Host ""
    Write-Host "--- Journal Revit terbaru (baris yang menyebut Luxora) ---"
    $journalDir = Join-Path $env:LOCALAPPDATA "Autodesk\Revit\Autodesk Revit $RevitVersion\Journals"
    if (Test-Path $journalDir) {
        $jr = Get-ChildItem -Path $journalDir -Filter "journal*.txt" -ErrorAction SilentlyContinue |
              Sort-Object LastWriteTime -Descending | Select-Object -First 2
        $ketemu = $false
        foreach ($j in $jr) {
            $hits = Select-String -LiteralPath $j.FullName -Pattern "Luxora" -SimpleMatch -ErrorAction SilentlyContinue
            foreach ($h in $hits) { Write-Host "  [$($j.Name)] $($h.Line.Trim())"; $ketemu = $true }
        }
        if (-not $ketemu) {
            Write-Host "  Tidak ada satu pun baris menyebut Luxora di 2 journal terakhir." -ForegroundColor Yellow
            Write-Host "  -> Revit tidak pernah mencoba memuat add-in ini. Cek folder & versi di atas." -ForegroundColor Yellow
        }
    } else {
        Write-Host "  Folder journal tidak ada: $journalDir" -ForegroundColor DarkGray
        Write-Host "  (Revit $RevitVersion mungkin belum pernah dijalankan di akun ini.)" -ForegroundColor DarkGray
    }

    # 6) Keputusan "Do Not Load" yang pernah dipilih pada dialog add-in tak bertanda tangan
    #    disimpan Revit di registry, dan sesudahnya add-in dilewati TANPA pesan apa pun.
    Write-Host ""
    Write-Host "--- Jejak keputusan add-in di registry (HKCU) ---"
    $addInId = "8c4e0a2e-9b6f-4d1a-8f2c-6e0d5a3b7c90"
    $regAkar = "HKCU:\Software\Autodesk\Revit"
    $jejak = @()
    if (Test-Path $regAkar) {
        $kunci = @(Get-Item -LiteralPath $regAkar) + @(Get-ChildItem -Path $regAkar -Recurse -ErrorAction SilentlyContinue)
        foreach ($k in $kunci) {
            if ($k.Name -match "Luxora|$addInId") { $jejak += "kunci : $($k.Name)"; continue }
            $props = Get-ItemProperty -LiteralPath $k.PSPath -ErrorAction SilentlyContinue
            if (-not $props) { continue }
            foreach ($pn in $props.PSObject.Properties.Name) {
                if ($pn -like "PS*") { continue }
                $nilai = "$($props.$pn)"
                if ($pn -match "Luxora|$addInId" -or $nilai -match "Luxora|$addInId") {
                    $jejak += "nilai : $($k.Name) -> $pn = $nilai"
                }
            }
        }
    }
    if ($jejak.Count -gt 0) {
        foreach ($j in $jejak) { Write-Host "  $j" -ForegroundColor Yellow }
        Write-Host "  -> Bila ini keputusan 'Do Not Load' yang pernah dipilih, hapus nilai/kunci tsb." -ForegroundColor Yellow
        Write-Host "     lalu jalankan Revit lagi supaya dialog keamanan muncul ulang (pilih Always Load)." -ForegroundColor Yellow
    } else {
        Write-Host "  Tidak ada jejak keputusan untuk add-in ini." -ForegroundColor DarkGray
    }
    Write-Host ""
}

if ($Verify) {
    Write-Host "Memeriksa pemasangan add-in Luxora untuk Revit $RevitVersion ..."
    $masalah = Show-Pemasangan
    Show-Diagnosa
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
        if ($xml.DocumentElement.Name -ne "RevitAddIns") {
            Write-Host "Manifest sumber rusak: root <$($xml.DocumentElement.Name)>, seharusnya <RevitAddIns>." -ForegroundColor Red
            exit 1
        }
        $node = $xml.SelectSingleNode("//AddIn/Assembly")
        if (-not $node) {
            Write-Host "Manifest sumber rusak: elemen <Assembly> tidak ada." -ForegroundColor Red
            exit 1
        }
        $node.InnerText = $dllTujuan
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
