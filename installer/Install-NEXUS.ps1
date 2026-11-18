param(
    [string]$InstallDir = "$env:LocalAppData\Programs\NEXUS System Optimizer",
    [switch]$EnableStartup
)

$ErrorActionPreference = "Stop"
$SourceDir = $PSScriptRoot
if (-not (Test-Path (Join-Path $SourceDir "Nexus.UI.exe"))) {
    throw "Jalankan Install-NEXUS.ps1 dari dalam folder paket release yang berisi Nexus.UI.exe."
}

Write-Host "Menginstal NEXUS System Optimizer ke: $InstallDir" -ForegroundColor Cyan
Get-Process -Name "Nexus.UI" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path "$SourceDir\*" -Destination $InstallDir -Recurse -Force

$exePath = Join-Path $InstallDir "Nexus.UI.exe"
$iconPath = Join-Path $InstallDir "Assets\icon.ico"

# Buat Pintasan Desktop & Start Menu
$wsh = New-Object -ComObject WScript.Shell
$desktopLink = Join-Path ([Environment]::GetFolderPath("Desktop")) "NEXUS System Optimizer.lnk"
$startMenuDir = Join-Path ([Environment]::GetFolderPath("Programs")) "NEXUS System Optimizer"
New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null
$startMenuLink = Join-Path $startMenuDir "NEXUS System Optimizer.lnk"

foreach ($lnkPath in @($desktopLink, $startMenuLink)) {
    $sc = $wsh.CreateShortcut($lnkPath)
    $sc.TargetPath = $exePath
    $sc.WorkingDirectory = $InstallDir
    if (Test-Path $iconPath) { $sc.IconLocation = $iconPath }
    $sc.Description = "NEXUS System Optimizer — Optimasi RAM, CPU & Layanan Windows"
    $sc.Save()
}

# Daftarkan ke Apps & Features (Uninstall)
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\NEXUS_System_Optimizer"
New-Item -Path $uninstallKey -Force | Out-Null
Set-ItemProperty -Path $uninstallKey -Name "DisplayName" -Value "NEXUS System Optimizer"
Set-ItemProperty -Path $uninstallKey -Name "Publisher" -Value "DigitCode Studio"
Set-ItemProperty -Path $uninstallKey -Name "DisplayVersion" -Value "1.0.0"
Set-ItemProperty -Path $uninstallKey -Name "DisplayIcon" -Value $iconPath
Set-ItemProperty -Path $uninstallKey -Name "InstallLocation" -Value $InstallDir
Set-ItemProperty -Path $uninstallKey -Name "UninstallString" -Value "powershell.exe -ExecutionPolicy Bypass -File `"$InstallDir\Uninstall-NEXUS.ps1`""

if ($EnableStartup) {
    Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "NEXUS_System_Optimizer" -Value "`"$exePath`" --minimized"
}

Write-Host "Instalasi NEXUS System Optimizer selesai! Membuka aplikasi..." -ForegroundColor Green
Start-Process -FilePath $exePath

