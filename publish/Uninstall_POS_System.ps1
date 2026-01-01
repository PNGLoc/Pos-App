#Requires -Version 5.1

param(
    [string]$InstallPath = "$env:ProgramFiles\POS System"
)

Write-Host "=== POS System Uninstaller ===" -ForegroundColor Red
Write-Host "Removing from: $InstallPath" -ForegroundColor Yellow

# Remove desktop shortcut
$desktopPath = [Environment]::GetFolderPath("Desktop")
$shortcutPath = Join-Path $desktopPath "POS System.lnk"
if (Test-Path $shortcutPath) {
    Remove-Item $shortcutPath -Force
    Write-Host "Removed desktop shortcut" -ForegroundColor Green
}

# Remove start menu entry
$startMenuPath = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\POS System"
if (Test-Path $startMenuPath) {
    Remove-Item $startMenuPath -Recurse -Force
    Write-Host "Removed start menu entry" -ForegroundColor Green
}

# Remove installation directory
if (Test-Path $InstallPath) {
    Remove-Item $InstallPath -Recurse -Force
    Write-Host "Removed installation directory" -ForegroundColor Green
}

Write-Host ""
Write-Host "Uninstallation completed successfully!" -ForegroundColor Green
Write-Host ""
Read-Host "Press Enter to exit"