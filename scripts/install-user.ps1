<#
.SYNOPSIS  Installer-free install for the current user (no administrator rights): copies the exe, adds a Start Menu shortcut,
           optionally enables "Start with Windows".
.EXAMPLE   powershell -ExecutionPolicy Bypass -File scripts\install-user.ps1 -Exe artifacts\publish\self-contained\BatteryRescueAlert.exe -StartWithWindows
.EXAMPLE   powershell -ExecutionPolicy Bypass -File scripts\install-user.ps1 -Uninstall
#>
param(
  [string]$Exe = 'artifacts\publish\self-contained\BatteryRescueAlert.exe',
  [switch]$StartWithWindows,
  [switch]$Uninstall,
  [switch]$RemoveSettings
)

$ErrorActionPreference = 'Stop'
$target = Join-Path $env:LOCALAPPDATA 'Programs\Battery Rescue Alert'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Battery Rescue Alert.lnk'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

if ($Uninstall) {
  Get-Process -Name BatteryRescueAlert -ErrorAction SilentlyContinue | ForEach-Object { $_.CloseMainWindow() | Out-Null; Start-Sleep 1; if (-not $_.HasExited) { $_.Kill() } }
  Remove-ItemProperty -Path $runKey -Name BatteryRescueAlert -ErrorAction SilentlyContinue
  Remove-Item $shortcut -ErrorAction SilentlyContinue
  Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
  if ($RemoveSettings) { Remove-Item (Join-Path $env:APPDATA 'BatteryRescueAlert') -Recurse -Force -ErrorAction SilentlyContinue }
  Write-Host 'Battery Rescue Alert was removed.' -ForegroundColor Green
  return
}

$Exe = (Resolve-Path $Exe).Path
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item $Exe (Join-Path $target 'BatteryRescueAlert.exe') -Force
$installed = Join-Path $target 'BatteryRescueAlert.exe'

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $installed
$link.WorkingDirectory = $target
$link.Description = 'Early-warning battery notifications'
$link.Save()

if ($StartWithWindows) {
  Set-ItemProperty -Path $runKey -Name BatteryRescueAlert -Value ('"{0}" --background' -f $installed)
}
Start-Process $installed
Write-Host "Installed to $target" -ForegroundColor Green
