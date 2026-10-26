<#
.SYNOPSIS  Restore, build and test the whole solution (Windows).
.EXAMPLE   powershell -ExecutionPolicy Bypass -File scripts\build.ps1
#>
param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

dotnet --version | Out-Null
dotnet restore BatteryRescueAlert.sln
dotnet build BatteryRescueAlert.sln -c $Configuration --no-restore
dotnet test tests\BatteryRescueAlert.Core.Tests -c $Configuration --no-build --logger "console;verbosity=normal"
Write-Host "Build and tests finished." -ForegroundColor Green
