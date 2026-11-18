<div align="center">

<img src="icon.ico" alt="NEXUS System Optimizer Logo" width="84" />

# NEXUS System Optimizer

**Aplikasi Desktop Native Windows (C# · .NET 10 · WinUI 3) untuk Pemantauan & Optimasi RAM, CPU, Aplikasi Idle, System Tray, dan Layanan Windows secara Aman**

[![Latest Release](https://img.shields.io/github/v/release/digitcodestudio/nexus?label=Release%20Terbaru&style=for-the-badge&color=0067C0)](https://github.com/digitcodestudio/nexus/releases/latest)
[![Download Windows EXE](https://img.shields.io/badge/Download-Windows%20Installer%20(.exe)-3FB950?style=for-the-badge&logo=windows)](https://github.com/digitcodestudio/nexus/releases/latest)
[![CI Build & Test](https://img.shields.io/github/actions/workflow/status/digitcodestudio/nexus/ci-and-release.yml?branch=master&label=Build%20%26%20Tests&style=for-the-badge)](https://github.com/digitcodestudio/nexus/actions)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue?style=for-the-badge)](./LICENSE)
[![Platform](https://img.shields.io/badge/Windows-10%20%7C%2011%20(x64)-4CC2FF?style=for-the-badge)](https://github.com/digitcodestudio/nexus/releases)

</div>

---

## Download Aplikasi (.exe Terbaru)

Tidak perlu kompilasi manual untuk langsung menggunakan NEXUS:

1. Buka halaman **[GitHub Releases Terbaru](https://github.com/digitcodestudio/nexus/releases/latest)**.
2. Unduh salah satu paket di bawah **Assets**:
   - **`NEXUS-Setup-vX.Y.Z-win-x64.exe`** *(Direkomendasikan)* — Installer resmi Windows lengkap dengan pintasan Desktop/Start Menu, Uninstaller, dan opsi Startup otomatis.
   - **`NEXUS-vX.Y.Z-win-x64-portable.zip`** — Versi portabel (ekstrak lalu jalankan `Nexus.UI.exe` atau `Install-NEXUS.ps1`).

> **Persyaratan Sistem:** Windows 10 22H2 (Build 19045+) atau Windows 11 (x64). Pemantauan berjalan tanpa hak Administrator; hak Administrator hanya diminta saat mengubah/menghentikan Windows Service.

---

## Fitur Utama

- **100% Native Windows Fluent Design (WinUI 3 + Ribbon Menu):** Bukan Electron atau WebView. Ringan, cepat, mendukung **Mica** di Windows 11 dan fallback **Acrylic / Dark Charcoal** di Windows 10, serta berjalan di **System Tray** saat jendela ditutup (`X`).
- **Real-Time Telemetry (Data Asli Windows API):** Grafik live CPU total & per-core (`NtQuerySystemInformation`), RAM fisik/available/cached/committed (`GlobalMemoryStatusEx`, `GetPerformanceInfo`), Disk I/O, dan Network throughput.
- **Target Guardrail RAM ≤ 75% & CPU ≤ 65% (Dapat Disesuaikan):** Memantau tekanan RAM dan beban CPU berkelanjutan agar PC selalu responsif tanpa mengganggu kerja aktif.
- **Optimasi CPU Aman (Windows EcoQoS / Efficiency Mode):** Menerapkan `SetProcessInformation(ProcessPowerThrottling)` dan penyesuaian prioritas reversibel pada proses latar belakang idle + pengalih Mode Daya Windows.
- **Manajer Proses Pintar & Deteksi Idle 30 Menit:** Mengelompokkan aplikasi multi-proses secara otomatis (seperti **Google Chrome**, **Edge**, **VS Code**, **Antigravity IDE**), mengecek 6 sinyal aktivitas (input user, fokus, CPU, Disk I/O, build, database), serta menyediakan **Whitelist Proteksi**.
- **Pembersih System Tray Otomatis (Tray Hygiene):** Menyapu ikon tray mati/hantu di taskbar dan menutup instansi duplikat idle aplikasi tray (misalnya ikon **Telegram** yang menumpuk) dengan tetap mempertahankan 1 jendela aktif.
- **Manajer Layanan Windows (SCM) + Rollback 1-Klik:** Klasifikasi otomatis layanan (termasuk kandidat aman seperti updater browser/vendor), inspeksi dependensi, **kunci permanen** layanan kritis Windows (Defender, RPC, PnP, DHCP, DNS, Audio, Update, Auth, Storage), dan **snapshot otomatis SQLite** sebelum setiap perubahan.
- **4 Profil Optimasi SQLite:** `Seimbang`, `Pengembangan`, `Beban Berat`, dan `Hemat Daya` + tombol **STOP DARURAT (Emergency Stop)** yang langsung menjeda otomatisasi dan mengembalikan semua proses EcoQoS ke normal.

---

## Prinsip Keamanan & Stabilitas

- Tidak pernah melakukan *fake RAM cleaner* (`EmptyWorkingSet` massal ke semua proses).
- Tidak pernah mematikan atau menonaktifkan layanan inti Windows secara otomatis.
- Tidak pernah memanipulasi voltase atau overclock CPU yang berisiko.
- Semua perubahan konfigurasi layanan dicadangkan otomatis di SQLite (`%LocalAppData%\NEXUS\nexus.db`) dengan fitur **Rollback 1-Klik**.

---

## Membangun dari Source Code (Untuk Developer)

### Prasyarat
- Windows 10 (19045+) atau Windows 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows 11 SDK (`10.0.26100.0`)
- *(Opsional untuk membuat Setup `.exe` lokal)* [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`)

### Build & Jalankan Tests
```powershell
# Clone repository
git clone git@github.com:digitcodestudio/nexus.git
cd nexus

# Build seluruh 13 proyek solution
dotnet build Nexus.sln -p:Platform=x64

# Jalankan seluruh automated tests
dotnet test tests\Nexus.Tests\Nexus.Tests.csproj -p:Platform=x64

# Jalankan aplikasi UI
Start-Process "src\Nexus.UI\bin\x64\Debug\net10.0-windows10.0.26100.0\Nexus.UI.exe"
```

### Build Installer & Paket Release
```powershell
powershell -ExecutionPolicy Bypass -File .\installer\build-release.ps1 -Version "1.0.0"
```
Output akan dibuat di folder `release/`:
- `NEXUS-v1.0.0-win-x64-portable.zip`
- `NEXUS-Setup-v1.0.0-win-x64.exe` *(jika Inno Setup 6 terpasang, atau otomatis dibuat oleh GitHub Actions saat push tag `v1.0.0`)*

---

## Kontribusi Open Source

Kami sangat terbuka untuk kontribusi komunitas! Untuk menjaga branch **`master`** tetap stabil dan aman:
- **Dilarang push langsung ke branch `master`.**
- Semua perubahan wajib melalui **Fork → Feature/Fix Branch → Pull Request**.
- Setiap Pull Request wajib lulus validasi otomatis (**CI Build & Unit Tests**) serta mendapatkan **review & persetujuan (approval) dari Maintainer (`@digitcodestudio`)** sebelum dapat di-merge.

Baca panduan lengkapnya di [CONTRIBUTING.md](./CONTRIBUTING.md), [CODE_OF_CONDUCT.md](./CODE_OF_CONDUCT.md), dan [SECURITY.md](./SECURITY.md).

---

## Lisensi

Proyek ini dirilis di bawah lisensi **MIT** — lihat file [LICENSE](./LICENSE) untuk detail lengkap.

