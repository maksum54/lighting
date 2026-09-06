# Luxora — Add-in Autodesk Revit 2025

Integrasi Revit ↔ website **Luxora** (`maksum54/lighting`): pilih Room/Space, add-in mengukur panjang–lebar ruang, memanggil endpoint kalkulasi website, lalu **memasang family lighting fixture** sesuai jumlah/jarak hasil hitung — dengan setiap titik **divalidasi lebih dulu** terhadap batas ruang.

Requirement yang dipenuhi:
- Klik/pilih **Room** (Revit Architecture) atau **Space** (MEP/mechanical) di dokumen.
- Otomatis membaca **panjang × lebar** ruang (kotak pembatas berorientasi terbaik) & luas.
- **Connect ke website** → `POST /api/calc` → hitung jumlah/grid/posisi lampu di website.
- Dialog input **nama family lighting fixture** (dari daftar family kategori *Lighting Fixtures* di dokumen — wajib cocok persis dgn yang ada di Revit).
- **Push** → lampu tergambar otomatis di posisi yang dihitung, sebanyak hasil website.
- Titik yang **melewati/menempel boundary** ruang atau berada **di lubang** (loop dalam) tidak pernah
  dipasang: titik yang cuma sedikit meleset **digeser** ke posisi sah terdekat (ruang bentuk-L, sudut
  terpotong, dekat kolom), sisanya **dilewati** dan dilaporkan di ringkasan.
- Family **hosted / work-plane based** (mis. downlight di plafon) ikut didukung: add-in mencari
  plafon/atap/lantai terdekat sebagai host sebelum jatuh ke penempatan berbasis level.

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
powershell -ExecutionPolicy Bypass -File revit/build.ps1 -RevitInstallPath "D:\Autodesk\Revit 2025"
```

Opsi lain:

| Opsi | Guna |
|---|---|
| `-RevitVersion 2024` | Build & pasang untuk tahun Revit lain (sesuaikan juga `TargetFramework` untuk Revit ≤ 2024). |
| `-Configuration Debug` | Build Debug. |
| `-Install` | Sekalian salin DLL + `.addin` ke folder add-in. |
| `-CurrentUser` | Pasang ke `%AppData%` (tanpa hak Administrator). |
| `-Verify` | Periksa pemasangan yang ada (tanpa build): folder mana saja yang punya manifest, dan apakah `<Assembly>` benar-benar menunjuk DLL yang ada. |
| `-Uninstall` | Copot add-in dari kedua lokasi Addins (tanpa build). |

Tanpa `dotnet` di PATH atau tanpa RevitAPI.dll, skrip berhenti dengan pesan yang menyebut
persis apa yang kurang — bukan ratusan error `CS0246`. Proyek juga bisa dibangun langsung:

```powershell
dotnet build revit/LuxoraRevit.sln -c Release -p:RevitInstallPath="C:\Program Files\Autodesk\Revit 2025"
```

---

## Pasang ke Revit (manual, sekali)

1. Tutup Revit.
2. Salin **dua** file ke folder yang **sama**:
   `C:\ProgramData\Autodesk\Revit\Addins\2025\`
   - `LuxoraRevit.dll` (hasil build)
   - `LuxoraRevit.addin` (manifest)

   > `<Assembly>LuxoraRevit.dll</Assembly>` di manifest dibaca **relatif terhadap folder
   > manifest itu sendiri**. Menaruh DLL di sub-folder membuat Revit melewati add-in tanpa
   > pesan apa pun. Kalau ingin DLL di tempat lain, tulis path lengkapnya di `<Assembly>`
   > — itulah yang dilakukan `build.ps1 -Install` secara otomatis.
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
5. **Hitung & Pasang** → lampu dibuat di posisi grid (tinggi ≈ plafon); titik yang di luar boundary
   digeser ke posisi sah terdekat atau dilewati.
6. Ringkasan: berapa dipasang / digeser / dilewati, Eavg, U₀, LPD.

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
   ├─ RoomGeometry.cs         baca boundary Room/Space; PCA → P×L & sumbu; lubang; mapping model↔meter;
   │                          uji titik-dalam-ruang & penggeseran titik ke dalam boundary
   ├─ CalcClient.cs           HTTP POST /api/calc + DTO
   ├─ CalcDialog.cs           WinForms input + preview
   └─ FamilyPlacer.cs         cari family, validasi titik, tempatkan instance (level/host/face)
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
Untuk tiap (x,y): meter→kaki di sumbu ruang, z = level + plafon
        ▼
Titik diuji thd outline / lubang / margin tepi → digeser ke posisi sah terdekat bila perlu
        ▼
Place FamilySymbol (face plafon → elemen host → level, sesuai FamilyPlacementType)
```

**Kunci kompatibilitas:** `lumType` yang dikirim add-in harus sama persis dengan kunci CU di
website (`calc-core.js` / `index.html`): `0.75`, `0.85`, `0.62`, `bat`, `lb`, `hb`, `flood`, `spot`.

---

## Batasan & catatan

- **P×L ruang** diambil dari kotak pembatas berorientasi terbaik (PCA) terhadap outline boundary.
  Ruang yang sangat tidak beraturan (bentuk-L berat, kurva) tetap terpasang di dalam polygon asli,
  karena setiap titik divalidasi sendiri-sendiri. Pada ruang bentuk-L, titik grid yang jatuh di
  bagian "takik" memang dilewati — jumlah lampu terpasang bisa lebih sedikit dari hasil hitung web,
  dan itu dilaporkan di ringkasan. **Dinding interior pemecah ruang tidak dibaca** —
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

---

## Kalau ada yang tidak beres

| Gejala | Penyebab & obatnya |
|---|---|
| **"Tidak ada lampu yang berhasil dipasang" + semua titik dilewati** | Ruang yang dipilih tidak tertutup boundary-nya, atau P×L di dialog ditimpa jauh lebih besar dari ruang aslinya. Cek Room/Space-nya (harus *placed*, luas > 0) dan kembalikan P×L ke nilai terukur. |
| **"Family … tidak ditemukan"** | Family kategori *Lighting Fixtures* belum dimuat ke dokumen (Insert → Load Family). Dialog hanya menampilkan family yang ada di dokumen aktif. |
| **Sebagian titik "gagal dibuat instance-nya"** | Family-nya hosted/work-plane based tetapi tidak ada plafon di ketinggian pemasangan. Buat plafon dulu, atau pakai family non-hosted. |
| **"Gagal terhubung ke website"** | `node server.js` belum jalan, atau Base URL salah. Uji dengan: `curl -X POST http://localhost:8787/api/calc -H "Content-Type: application/json" -d "{\"L\":10,\"W\":8,\"H\":2.7,\"wp\":0.75,\"F\":3000,\"P\":36,\"E\":300,\"lumType\":\"0.75\",\"refC\":0.7,\"refW\":0.5,\"llf\":0.812}"`. |
| **Build: `RevitAPI.dll tidak ditemukan`** | Beri `-RevitInstallPath` ke folder instalasi Revit yang berisi `RevitAPI.dll` **dan** `RevitAPIUI.dll`. |
| **Tab "Luxora" tidak muncul, tanpa pesan error apa pun** | Hampir selalu karena DLL tidak ada di tempat yang ditunjuk manifest: `<Assembly>` dibaca **relatif terhadap folder file `.addin`**, jadi menaruh DLL di sub-folder (`Addins\2025\LuxoraRevit\LuxoraRevit.dll`) membuat Revit melewati add-in ini diam-diam. Periksa dengan `build.ps1 -Verify`, benahi dengan `build.ps1 -Install` (manifest ditulis ulang dgn path DLL absolut). Revit harus dimulai ulang. |
| **Dua salinan add-in (ProgramData & AppData)** | Manifest ber-`AddInId` sama di dua lokasi bisa bentrok. `-Install` otomatis membersihkan keduanya lebih dulu; `-Uninstall` mencopot semuanya. |
