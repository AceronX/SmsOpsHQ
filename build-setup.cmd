@echo off
REM Build SmsOps HQ installer without changing PowerShell execution policy.
REM Double-click this file, or run from cmd: build-setup.cmd

cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-setup.ps1" %*
if errorlevel 1 (
    echo.
    echo Build failed. See messages above.
    pause
    exit /b 1
)
echo.
pause
