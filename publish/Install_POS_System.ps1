#Requires -Version 5.1

param(
    [string]$InstallPath = "$env:ProgramFiles\POS System"
)

Write-Host "=== POS System Installer ===" -ForegroundColor Green
Write-Host "Installing to: $InstallPath" -ForegroundColor Yellow

# Create installation directory
if (!(Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
    Write-Host "Created installation directory" -ForegroundColor Green
}

# Copy all files from current directory to install path
$sourcePath = Split-Path -Parent $MyInvocation.MyCommand.Path
Write-Host "Copying files from $sourcePath to $InstallPath..." -ForegroundColor Yellow

Copy-Item "$sourcePath\*" $InstallPath -Recurse -Force

# Create desktop shortcut
$desktopPath = [Environment]::GetFolderPath("Desktop")
$shortcutPath = Join-Path $desktopPath "POS System.lnk"

$WshShell = New-Object -comObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($shortcutPath)
$Shortcut.TargetPath = Join-Path $InstallPath "Run_POS_System.bat"
$Shortcut.WorkingDirectory = $InstallPath
$Shortcut.IconLocation = Join-Path $InstallPath "PosSystem.Main.exe"
$Shortcut.Save()

Write-Host "Created desktop shortcut" -ForegroundColor Green

# Create start menu entry
$startMenuPath = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\POS System"
if (!(Test-Path $startMenuPath)) {
    New-Item -ItemType Directory -Path $startMenuPath -Force | Out-Null
}

Copy-Item $shortcutPath (Join-Path $startMenuPath "POS System.lnk") -Force

Write-Host "Created start menu entry" -ForegroundColor Green
Write-Host ""
Write-Host "Installation completed successfully!" -ForegroundColor Green
Write-Host "You can now run POS System from the desktop shortcut or start menu." -ForegroundColor White
Write-Host ""
Read-Host "Press Enter to exit"