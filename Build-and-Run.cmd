@echo off
REM Battery Rescue Alert - one-click build and run (Windows 10/11, needs the .NET 8 SDK).
REM Double-click this file. It builds the app, then starts it.
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo The .NET 8 SDK was not found.
  echo Install it with:   winget install Microsoft.DotNet.SDK.8
  echo Then run this file again.
  pause
  exit /b 1
)

echo Building Battery Rescue Alert...
dotnet build src\BatteryRescueAlert.App\BatteryRescueAlert.App.csproj -c Release -o artifacts\run
if errorlevel 1 (
  echo.
  echo Build failed. Please read the messages above.
  pause
  exit /b 1
)

echo Starting Battery Rescue Alert...
start "" "artifacts\run\BatteryRescueAlert.exe"
endlocal
