@echo off
setlocal

:: The apps share one JSON datastore file at the repository root, so there is no database server to start.
:: Kept under its old name so existing shortcuts still work.
set "ROOT=%~dp0"

if not exist "%ROOT%campus_system_unified_data_schema.json" (
    echo campus_system_unified_data_schema.json is missing.
    echo Restore it from campus_system_seed_data.json with: .\Reset-CampusJsonData.ps1
    exit /b 1
)

echo Starting Campus System Maintenance Dashboard...
call "%ROOT%Maintenance\Run-MaintenanceMonitoring.bat"

endlocal
