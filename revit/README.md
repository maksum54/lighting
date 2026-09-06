# Luxora — Add-in Autodesk Revit 2025

Integrasi Revit ↔ website **Luxora** (`maksum54/lighting`): pilih Room/Space, add-in mengukur panjang–lebar ruang, memanggil endpoint kalkulasi website, lalu **memasang family lighting fixture** sesuai jumlah/jarak hasil hitung — dan **menghapus otomatis** lampu yang berada di luar batas ruang.

Requirement yang dipenuhi:
- Klik/pilih **Room** (Revit Architecture) atau **Space** (MEP/mechanical) di dokumen.
- Otomatis membaca **panjang × lebar** ruang (kotak pembatas berorientasi terbaik) & luas.
- **Connect ke website** → `POST /api/calc` → hitung jumlah/grid/posisi lampu di website.
- Dialog input **nama family lighting fixture** (dari daftar family kategori *Lighting Fixtures* di dokumen — wajib cocok persis dgn yang ada di Revit).
- **Push** → lampu tergambar otomatis di posisi yang dihitung, sebanyak hasil website.
- Lampu yang titiknya **melewati/menempel boundary** ruang atau berada **di lubang** (loop dalam) **dihapus otomatis**.

---

## Prasyarat

| Butuh | Keterangan |
|---|---|
| Autodesk Revit **2025** | Versi .NET 8. Add-in ini *tidak* untuk Revit ≤ 2024. |
| .NET SDK 8+ | Untuk `dotnet build`. |
| Server website Luxora | `node server.js` (lokal, port 8787) atau deploy Vercel dengan endpoint `/api/calc`. |

> ⚠️ **RevitAPI.dll & RevitAPIUI.dll tidak ikut di-redistribusi** — add-in ini TIDAK bisa
> dikompilasi tanpa Revit terpasang. Pastikan mesin build punya Revit 2025 (atau folder berisi
> kedua DLL tsb. — lihat `-RevitInstallPath`).

---

## Build

Dari folder repo (yang berisi `revit/`):

```powershell
powershell -ExecutionPolicy Bypass -File revit/build.ps1
```

Hasil: `revit/bin/Release/net8.0-windows/LuxoraRevit.dll`.

Bila folder instalasi Revit beda dari standar, beri tahu skrip:

```powershell
powershell -ExecutionPolicy Bypass -File revit/build.ps1 -RevitInstallPath "C:\Program Files\Autodesk\Revit 2025"
```

---

## Pasang ke Revit (manual, sekali)

1. Tutup Revit.
2. Salin **dua** file ke folder:
   `C:\ProgramData\Autodesk\Revit\Addins\2025\`
   - `LuxoraRevit.dll` (hasil build)
   - `LuxoraRevit.addin` (manifest — cek nama `Assembly` cocok dgn DLL)
3. Buka Revit → tab **Luxora** di Ribbon → tombol **Hitung & Pasang Lampu**.

Atau pasang sekali jalan:

```powershell
powershell -ExecutionPolicy Bypass -File revit/build.ps1 -Install
```

> Semua pengguna di mesin itu akan melihat add-in (folder ProgramData). Untuk per-user,
> gunakan `%AppData%\Autodesk\Revit\Addins\2025\` sebagai gantinya.

---

## Cara pakai

1. Buka dokumen/model yang punya **Room** terplace (Ruang) atau **Space** (MEP) yang sudah dibatasi.
2. Ribbon **Luxora → Hitung & Pasang Lampu**.
3. Klik **Room/Space** pada area yang mau dihitung.
4. Dialog muncul:
   - **P×L terukur** (m) — otomatis dari ruang, bisa disesuaikan.
   - **Tinggi plafon / bidang kerja / target lux / lumen / watt / jenis luminaire**.
   - **Family lighting fixture** — pilih dari daftar *Lighting Fixtures* yang ada di dokumen (atau ketik nama persis).
   - **Base URL** website Luxora (default `http://localhost:8787`).
   - *Preview* jumlah lampu langsung dari `/api/calc` (butuh koneksi).
5. **Hitung & Pasang** → lampu dibuat di posisi grid (tinggi ≈ plafon), lalu **dihapus** bila di luar boundary ruang.
6. Ringkasan: berapa dipasang / dihapus, Eavg, U₀, LPD.

---

## Arsitektur

```
revit/
├─ LuxoraRevit.sln            solusi
├─ LuxoraRevit.addin          manifest add-in
├─ build.ps1                  skrip build (+ opsi -Install)
├─ README.md                  file ini
└─ src/
   ├─ App.cs                  IExternalApplication → tab/panel/tombol ribbon
   ├─ CalcCommand.cs          IExternalCommand → alur utama (pilih→ukur→hitung→pasang)
   ├─ RoomGeometry.cs         baca boundary Room/Space; PCA → P×L & sumbu; lubang; mapping model↔meter
   ├─ CalcClient.cs           HTTP POST /api/calc + DTO
   ├─ CalcDialog.cs           WinForms input + preview
   └─ FamilyPlacer.cs         cari family, tempatkan instance, filter & hapus yg di luar
```

Alur data:

```
Revit (Room/Space)
   │  GetBoundarySegments → outline + lubang; PCA bbox → L & W (meter)
   ▼
POST { L, W, H, wp, F, P, E, lumType, … } ──►  /api/calc (server.js / Vercel)
   ▲                                            calc-core.js (sama dgn logika web)
   └────────  { n, cols, rows, positionsM:[{x,y} m], actual, u0, lpd } ─┘
        ▼
Place FamilySymbol di setiap (x,y) → meter→kaki, z = level + plafon
        ▼
Hapus instance yang titiknya di luar outline / di dalam lubang / <5 cm dari tepi
```

**Kunci kompatibilitas:** `lumType` yang dikirim add-in harus sama persis dengan kunci CU di
website (`calc-core.js` / `index.html`): `0.75`, `0.85`, `0.62`, `bat`, `lb`, `hb`, `flood`, `spot`.

---

## Batasan & catatan

- **P×L ruang** diambil dari kotak pembatas berorientasi terbaik (PCA) terhadap outline boundary.
  Ruang yang sangat tidak beraturan (bentuk-L berat, kurva) tetap terpasang di dalam polygon asli,
  karena langkah penghapusan memfilter per titik. **Dinding interior pemecah ruang tidak dibaca** —
  add-in menangani satu Room/Space sebagai satu ruang (seperti web). Untuk partisi, bagi ruang jadi
  beberapa Room/Space dan jalankan per bagian.
- Nama family **harus persis** dengan family di dokumen (case-insensitive). Add-in hanya mencari
  kategori *Lighting Fixtures*.
- Perlu **koneksi internet/ke server** saat tombol ditekan (untuk `/api/calc`).
- Hasil = estimasi desain (fotometri disederhanakan Lambertian + tabel CU). Validasi akhir dengan
  standar proyek & data fotometrik nyata.
- Source ini tidak menyertakan `.rvt` contoh. Uji pada model Anda sendiri.
- Add-in ditargetkan ke **Revit 2025 (.NET 8)**. Untuk tahun/versi lain, sesuaikan `TargetFramework`
  (Revit ≤ 2024 = .NET Framework 4.8) dan folder `Addins\<tahun>`.

---

## Endpoint website yang dipakai

| Endpoint | Metode | Deskripsi |
|---|---|---|
| `/api/calc` | POST | JSON `{L,W,H,wp,F,P,E,lumType,refC,refW,llf,cuManual,cu}` → layout + `positionsM`. |
| `/api/ai` | POST | (tidak dipakai add-in) asisten AI website. |

Contoh request `/api/calc`:

```json
{ "L": 10, "W": 8, "H": 2.7, "wp": 0.75, "F": 3000, "P": 36,
  "E": 300, "lumType": "0.75", "refC": 0.7, "refW": 0.5, "llf": 0.812, "cuManual": false }
```
