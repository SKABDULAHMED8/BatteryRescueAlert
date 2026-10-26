<#
.SYNOPSIS  Build the per-user installer with Inno Setup 6 (install it with: winget install JRSoftware.InnoSetup).
.PARAMETER Sign  Optional: path to a code-signing .pfx. The password is read from the environment variable BRA_SIGN_PASSWORD
                 (never put secrets on the command line or in source control).
.EXAMPLE   powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
#>
param([string]$Version = '1.0.5', [string]$Sign = '')

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

& "$PSScriptRoot\publish.ps1" -Version $Version

$exe = 'artifacts\publish\self-contained\BatteryRescueAlert.exe'
if ($Sign) {
  if (-not $env:BRA_SIGN_PASSWORD) { throw 'Set BRA_SIGN_PASSWORD first.' }
  signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f $Sign /p $env:BRA_SIGN_PASSWORD $exe
}

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
  Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup' }

& $iscc "/DMyAppVersion=$Version" installer\BatteryRescueAlert.iss
$setup = "artifacts\installer\BatteryRescueAlert-Setup-$Version.exe"
if ($Sign) { signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f $Sign /p $env:BRA_SIGN_PASSWORD $setup }
Get-FileHash $setup -Algorithm SHA256 | Format-List
Write-Host "Installer: $setup" -ForegroundColor Green
