@echo off
setlocal

set PROJECT_DIR=%~dp0
set PROJECT_FILE=%PROJECT_DIR%TestingMain.csproj
set APP_URL=http://localhost:5000
set ADMINER_URL=http://100.98.41.69:8080

cd /d "%PROJECT_DIR%"

echo =========================================================
echo Testing Department - ASP.NET Database Test App
echo =========================================================
echo.
echo Build and run commands:

echo 1) Build project
echo   dotnet build TestingMain.csproj

echo 2) Run project
echo   dotnet run --project TestingMain.csproj --urls %APP_URL%

echo 3) Open Adminer database host
echo   %ADMINER_URL%

echo.

echo Choose an action:

echo [1] Build

echo [2] Run

echo [3] Build and Run

echo [4] Open Adminer in browser

echo [5] Exit

echo.
choice /C 12345 /M "Select option: "

if errorlevel 5 goto exit
if errorlevel 4 goto open_adminer
if errorlevel 3 goto build_and_run
if errorlevel 2 goto run_app
if errorlevel 1 goto build_app

:build_app
    echo Building project...
    dotnet build "%PROJECT_FILE%"
    if errorlevel 1 (
        echo Build failed.
        goto exit
    )
    echo Build complete.
    goto exit

:run_app
    echo Starting app...
    dotnet run --project "%PROJECT_FILE%" --urls %APP_URL%
    goto exit

:build_and_run
    echo Building project...
    dotnet build "%PROJECT_FILE%"
    if errorlevel 1 (
        echo Build failed.
        goto exit
    )
    echo Starting app...
    dotnet run --project "%PROJECT_FILE%" --urls %APP_URL%
    goto exit

:open_adminer
    echo Opening database admin page...
    start "Adminer" "%ADMINER_URL%"
    goto exit

:exit
    echo.
    echo Done.
    exit /b 0
