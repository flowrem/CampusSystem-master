@echo off
setlocal

cd /d "%~dp0"
set "PORT=5080"

start "Campus System Maintenance Dashboard" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-MaintenanceDashboard.ps1" -Port %PORT%
start "" "http://localhost:%PORT%/"

endlocal