param(
    [string]$Version = "1.0.0",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ReleaseRoot = Join-Path $RepoRoot "release"
$PublishDir = Join-Path $ReleaseRoot "publish-win-x64"
$PortableZip = Join-Path $ReleaseRoot "NEXUS-v$Version-win-x64-portable.zip"

Write-Host "=== NEXUS System Optimizer Release Builder (v$Version) ===" -ForegroundColor Cyan

# Tutup instance aktif agar DLL tidak terkunci
Get-Process -Name "Nexus.UI" -ErrorAction SilentlyContinue | Stop-Process -Force

if (-not $SkipTests) {
    Write-Host "[1/4] Menjalankan seluruh unit & integration tests..." -ForegroundColor Yellow
    dotnet test (Join-Path $RepoRoot "tests\Nexus.Tests\Nexus.Tests.csproj") -c Release -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw "Test gagal. Build release dibatalkan." }
}

Write-Host "[2/4] Mem-publish Nexus.UI (Release, win-x64, self-contained)..." -ForegroundColor Yellow
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $ReleaseRoot | Out-Null

dotnet publish (Join-Path $RepoRoot "src\Nexus.UI\Nexus.UI.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Platform=x64 `
    -p:Version=$Version `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish gagal." }

# Sertakan skrip installer lokal di dalam paket portable
Copy-Item (Join-Path $PSScriptRoot "Install-NEXUS.ps1") (Join-Path $PublishDir "Install-NEXUS.ps1") -Force
Copy-Item (Join-Path $PSScriptRoot "Uninstall-NEXUS.ps1") (Join-Path $PublishDir "Uninstall-NEXUS.ps1") -Force

Write-Host "[3/4] Membuat paket Portable ZIP: $PortableZip" -ForegroundColor Yellow
if (Test-Path $PortableZip) { Remove-Item $PortableZip -Force }
Compress-Archive -Path "$PublishDir\*" -DestinationPath $PortableZip -CompressionLevel Optimal

Write-Host "[4/4] Memeriksa Inno Setup Compiler (ISCC.exe)..." -ForegroundColor Yellow
$iscc = Get-Command "iscc" -ErrorAction SilentlyContinue
if (-not $iscc) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )
    $found = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($found) { $iscc = Get-Item $found }
}

if ($iscc) {
    $isccPath = if ($iscc -is [System.Management.Automation.CommandInfo]) { $iscc.Source } else { $iscc.FullName }
    Write-Host "Menjalankan Inno Setup Compiler: $isccPath" -ForegroundColor Green
    & $isccPath "/DAppVersion=$Version" "/DPublishDir=$PublishDir" (Join-Path $PSScriptRoot "NexusInstaller.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup gagal membuat installer EXE." }
    Write-Host "Installer Setup EXE siap di: $ReleaseRoot\NEXUS-Setup-v$Version-win-x64.exe" -ForegroundColor Green
} else {
    Write-Host "Inno Setup 6 belum terpasang lokal (GitHub Actions workflow akan otomatis membangun NEXUS-Setup-v$Version-win-x64.exe saat tag v$Version di-push, atau pasang lokal via: winget install JRSoftware.InnoSetup)." -ForegroundColor DarkYellow
}

Write-Host "=== Selesai! Output tersedia di folder: $ReleaseRoot ===" -ForegroundColor Cyan

