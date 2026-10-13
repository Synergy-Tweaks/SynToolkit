@echo off
title SynToolkit - Toggle Microsoft Defender real-time protection
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
  "$current = (Get-MpPreference -ErrorAction Stop).DisableRealtimeMonitoring; Set-MpPreference -DisableRealtimeMonitoring (-not $current) -ErrorAction Stop; Write-Host ('Real-time protection disabled: ' + (-not $current))"
if errorlevel 1 (
    echo Microsoft Defender rejected the change. Check Tamper Protection and administrator permissions.
)
set "SYNTK_ERR=%errorlevel%"
pause
exit /b %SYNTK_ERR%
