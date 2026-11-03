@echo off
:: =========================================================
::  CreateShortcut.bat
::  Creates a Desktop shortcut for WinyWash (.exe)
:: =========================================================

set "SCRIPT_DIR=%~dp0"
echo Creating Desktop shortcut for WinyWash...

set "TARGET_EXE=%SCRIPT_DIR%publish\WinyWash\WinyWash.exe"
set "WORKING_DIR=%SCRIPT_DIR%publish\WinyWash"

if not exist "%TARGET_EXE%" (
    if exist "%SCRIPT_DIR%WinyWash.exe" (
        set "TARGET_EXE=%SCRIPT_DIR%WinyWash.exe"
        set "WORKING_DIR=%SCRIPT_DIR%"
    )
)

if not exist "%TARGET_EXE%" (
    echo [FAIL] WinyWash.exe not found. Run publish.ps1 first.
    pause
    exit /b 1
)

powershell -NoProfile -Command "$ws = New-Object -ComObject WScript.Shell; $desk = [Environment]::GetFolderPath('Desktop'); $sc = $ws.CreateShortcut((Join-Path $desk 'WinyWash.lnk')); $sc.TargetPath = '%TARGET_EXE%'; $sc.WorkingDirectory = '%WORKING_DIR%'; $sc.Description = 'WinyWash - Windows 11 Native PC Manager (WinUI 3)'; $sc.IconLocation = '%TARGET_EXE%,0'; $sc.Save()"

if %errorlevel% equ 0 (
    echo [OK] Shortcut created successfully on your Desktop!
) else (
    echo [FAIL] Could not create shortcut.
)

pause
