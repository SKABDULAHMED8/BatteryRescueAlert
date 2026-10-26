<#
.SYNOPSIS  Samples CPU, memory, handles and threads of the running app so performance claims are based on measurements.
.DESCRIPTION
  1. Start BatteryRescueAlert.exe (let it sit in the tray; do not touch it).
  2. Run this script for a few minutes on battery power, ideally also once while plugging/unplugging.
  Results are printed and saved to artifacts\resource-measurements.csv. Do not publish numbers you have not measured.
.EXAMPLE   powershell -ExecutionPolicy Bypass -File scripts\measure-resources.ps1 -Seconds 300
#>
param([int]$Seconds = 120, [int]$IntervalSeconds = 2)

$ErrorActionPreference = 'Stop'
$proc = Get-Process -Name BatteryRescueAlert -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { throw 'BatteryRescueAlert.exe is not running. Start it first.' }

$cores = [Environment]::ProcessorCount
$rows = @()
$prevCpu = $proc.TotalProcessorTime
$prevTime = Get-Date
$end = $prevTime.AddSeconds($Seconds)

while ((Get-Date) -lt $end) {
  Start-Sleep -Seconds $IntervalSeconds
  $proc.Refresh()
  $now = Get-Date
  $cpu = $proc.TotalProcessorTime
  $cpuPct = (($cpu - $prevCpu).TotalSeconds / (($now - $prevTime).TotalSeconds * $cores)) * 100
  $prevCpu = $cpu; $prevTime = $now
  $rows += [pscustomobject]@{
    Time = $now.ToString('s'); CpuPercent = [math]::Round($cpuPct, 3)
    WorkingSetMB = [math]::Round($proc.WorkingSet64 / 1MB, 1)
    PrivateMB = [math]::Round($proc.PrivateMemorySize64 / 1MB, 1)
    Handles = $proc.HandleCount; Threads = $proc.Threads.Count
  }
}

New-Item -ItemType Directory -Force -Path artifacts | Out-Null
$rows | Export-Csv artifacts\resource-measurements.csv -NoTypeInformation
$rows | Measure-Object CpuPercent, WorkingSetMB, PrivateMB, Handles, Threads -Average -Maximum -Minimum |
  Select-Object Property, Average, Maximum, Minimum | Format-Table -AutoSize
Write-Host "Saved artifacts\resource-measurements.csv ($($rows.Count) samples, $cores logical cores)."
