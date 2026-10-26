<#
.SYNOPSIS  Produce release builds in artifacts\publish (Windows, x64).
.DESCRIPTION
  self-contained       : one BatteryRescueAlert.exe that needs nothing installed (larger file). Use this for the installer.
  framework-dependent  : small exe that needs the ".NET Desktop Runtime 8" (winget install Microsoft.DotNet.DesktopRuntime.8).
.EXAMPLE   powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
#>
param([string]$Version = '1.0.5')

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$project = 'src\BatteryRescueAlert.App\BatteryRescueAlert.App.csproj'

dotnet publish $project -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none -p:Version=$Version -o artifacts\publish\self-contained

dotnet publish $project -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:DebugType=none -p:Version=$Version -o artifacts\publish\framework-dependent

Get-ChildItem artifacts\publish -Recurse -Filter BatteryRescueAlert.exe |
  ForEach-Object { '{0}  {1:N1} MB' -f $_.FullName, ($_.Length / 1MB) }
Write-Host "Published." -ForegroundColor Green
