param(
    [string]$InstallDir = "$env:LocalAppData\Programs\NEXUS System Optimizer"
)

$ErrorActionPreference = "SilentlyContinue"
Write-Host "Menghapus NEXUS System Optimizer..." -ForegroundColor Yellow

Get-Process -Name "Nexus.UI" | Stop-Process -Force
Start-Sleep -Milliseconds 400

Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "NEXUS_System_Optimizer"
Remove-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\NEXUS_System_Optimizer" -Recurse -Force

Remove-Item (Join-Path ([Environment]::GetFolderPath("Desktop")) "NEXUS System Optimizer.lnk") -Force
Remove-Item (Join-Path ([Environment]::GetFolderPath("Programs")) "NEXUS System Optimizer") -Recurse -Force

if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
}

Write-Host "NEXUS System Optimizer berhasil dihapus." -ForegroundColor Green

