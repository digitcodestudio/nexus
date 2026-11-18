# Panduan Kontribusi — NEXUS System Optimizer

Terima kasih telah tertarik berkontribusi pada pengembangan **NEXUS System Optimizer**!  
Demi menjaga stabilitas, keamanan sistem pengguna Windows, dan kualitas kode pada branch utama (`master`), seluruh kontributor **wajib** mengikuti aturan berikut.

---

## 1. Aturan Perlindungan Branch `master`

1. Branch **`master`** adalah branch produksi yang **dilindungi (protected)**.
2. **Dilarang push langsung** ke `master` (termasuk force-push).
3. Setiap perubahan kode **wajib** masuk melalui **Pull Request (PR)**.
4. Setiap PR wajib memenuhi syarat berikut sebelum di-merge:
   - Lulus seluruh cek otomatis **GitHub Actions CI** (`dotnet build` 0 error & `dotnet test` 100% lulus).
   - Mendapatkan **review dan persetujuan (Approval) eksplisit dari Maintainer / Code Owner (`@digitcodestudio`)**.
   - Tidak ada konflik merge dengan branch `master` terbaru.

---

## 2. Alur Kerja Git (Fork & Branch Workflow)

```bash
# 1. Fork repository di GitHub, lalu clone fork Anda
git clone git@github.com:<username-anda>/nexus.git
cd nexus

# 2. Tambahkan upstream repository resmi
git remote add upstream git@github.com:digitcodestudio/nexus.git

# 3. Pastikan branch master lokal selalu sinkron
git checkout master
git pull upstream master

# 4. Buat branch baru dengan penamaan standar
git checkout -b feat/nama-fitur-singkat
# atau:
git checkout -b fix/deskripsi-bug
```

### Konvensi Penamaan Branch
- `feat/<nama>` — Fitur baru (misal: `feat/disk-smart-monitor`)
- `fix/<nama>` — Perbaikan bug (misal: `fix/tray-overflow-cleanup`)
- `perf/<nama>` — Peningkatan performa & efisiensi RAM/CPU
- `refactor/<nama>` — Refaktor kode tanpa mengubah perilaku
- `docs/<nama>` — Pembaruan README / panduan kontribusi
- `test/<nama>` — Penambahan unit/integration test

### Konvensi Pesan Commit (Conventional Commits)
Gunakan format standar:
```text
<tipe>(<ruang-lingkup>): <ringkasan singkat>

Contoh:
feat(process): tambahkan deteksi grup proses Firefox
fix(ui): perbaiki alignment tombol pada halaman Layanan Windows
test(service): tambahkan unit test proteksi layanan kritis
```

---

## 3. Standar Kode & Keamanan Wajib

1. **100% Native Windows (C# / .NET 10 / WinUI 3):** Jangan menambahkan Electron, WebView2, React, atau UI berbasis web.
2. **Data Sistem Asli:** Jangan pernah menulis metrik dummy/palsu pada jalur produksi.
3. **MVVM & UI Thread Hygiene:** Jangan menaruh logika bisnis atau panggilan Win32 berat di code-behind `.xaml.cs` atau memblokir UI thread.
4. **Aturan Keamanan Non-Negosiabel:**
   - **JANGAN** membuat fitur *RAM cleaner* palsu (`EmptyWorkingSet` massal ke semua proses).
   - **JANGAN** melemahkan atau mem-bypass daftar layanan kritis (`ProtectedLists.ImmutableCriticalServices`) atau proses inti Windows (`ProtectedLists.ImmutableSystemProcesses`).
   - **WAJIB** mencadangkan konfigurasi layanan ke SQLite sebelum mutasi SCM apa pun.
   - **WAJIB** menangani `Win32Exception` / `UnauthorizedAccessException` (`AccessDenied`) secara aman.
5. **Pengujian Lokal Wajib Sebelum PR:**
   ```powershell
   dotnet build Nexus.sln -p:Platform=x64
   dotnet test tests\Nexus.Tests\Nexus.Tests.csproj -p:Platform=x64
   ```

---

## 4. Pengajuan Pull Request (PR)

1. Push branch Anda ke fork:
   ```bash
   git push origin feat/nama-fitur-singkat
   ```
2. Buka **Pull Request** ke branch `master` di `digitcodestudio/nexus`.
3. Lengkapi seluruh checklist pada template PR dan lampirkan screenshot jika ada perubahan UI.
4. Tunggu hasil **CI Validation** dan **Review dari `@digitcodestudio`**.

---

## 5. Rilis Versi Baru (Khusus Maintainer)

Maintainer merilis versi baru menggunakan semantic versioning tag (`vX.Y.Z`):
```bash
git checkout master
git pull origin master
git tag -a v1.0.0 -m "Release v1.0.0 — NEXUS System Optimizer"
git push origin v1.0.0
```
GitHub Actions akan otomatis mem-build, menjalankan seluruh test, membuat `NEXUS-Setup-v1.0.0-win-x64.exe` & `NEXUS-v1.0.0-win-x64-portable.zip`, lalu mempublikasikannya ke halaman **GitHub Releases**.

