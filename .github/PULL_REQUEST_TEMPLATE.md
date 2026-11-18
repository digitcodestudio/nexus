## Ringkasan Perubahan
<!-- Jelaskan secara singkat apa yang diubah dan mengapa -->

## Jenis Perubahan
- [ ] Fitur baru (`feat`)
- [ ] Perbaikan bug (`fix`)
- [ ] Peningkatan performa / optimasi (`perf`)
- [ ] Refaktor / kebersihan kode (`refactor`)
- [ ] Dokumentasi / pengujian (`docs` / `test`)

## Checklist Validasi Wajib
- [ ] Perubahan dibuat pada branch terpisah (bukan langsung di `master`).
- [ ] `dotnet build Nexus.sln -p:Platform=x64` sukses tanpa error.
- [ ] `dotnet test tests\Nexus.Tests\Nexus.Tests.csproj -p:Platform=x64` 100% lulus.
- [ ] Tidak menambahkan WebView/Electron atau metrik palsu.
- [ ] Tidak melanggar daftar proteksi layanan kritis / proses inti Windows.
- [ ] Screenshot UI dilampirkan (jika mengubah tampilan WinUI 3).

## Tangkapan Layar (Jika Ada Perubahan UI)
<!-- Tempel screenshot di sini -->

