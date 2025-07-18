@echo off
title OpenBullet2 Testing Build
color 0A

echo.
echo ================================================
echo  OpenBullet2.Native - Memory-Optimized Build
echo ================================================
echo.

:: Store current directory and start timer
set "SCRIPT_DIR=%~dp0"
set "PROJECT_DIR=%SCRIPT_DIR%.."
set "BUILD_DIR=%SCRIPT_DIR%testing"
set "USERDATA_SOURCE=%PROJECT_DIR%\UserData"
set "USERDATA_BUILD=%BUILD_DIR%\UserData"
set "USERDATA_BACKUP=%SCRIPT_DIR%UserData_backup"
set "START_TIME=%time%"

:: Change to project directory
cd /d "%PROJECT_DIR%"

:: Terminate any running instances to prevent file locks
echo [1/7] Terminating existing OpenBullet2.Native processes...
taskkill /f /im "OpenBullet2.Native.exe" >nul 2>&1
if %ERRORLEVEL% equ 0 (
echo      ✓ Terminated running OpenBullet2.Native processes
timeout /t 1 /nobreak >nul
) else (
echo      ℹ No running OpenBullet2.Native processes found
)

echo.
echo [2/7] Backing up existing UserData from build directory...
if exist "%USERDATA_BUILD%" (
if exist "%USERDATA_BACKUP%" rmdir /s /q "%USERDATA_BACKUP%"
robocopy "%USERDATA_BUILD%" "%USERDATA_BACKUP%" /E /NP /NDL /NJH /NJS >nul 2>&1
echo      ✓ UserData backed up to bin\UserData_backup
) else (
echo      ℹ No existing UserData in build directory
)

echo.
echo [3/7] Starting memory-optimized build process...
echo      ⚡ Attempting with minimal icon packs first...

:: Restore packages first (newly added)
dotnet restore --verbosity quiet --nologo >nul 2>&1

:: Clean build with memory-optimized settings
dotnet clean --configuration testing --verbosity quiet >nul 2>&1
dotnet publish --configuration testing --self-contained false --output "%BUILD_DIR%" --verbosity quiet --nologo --no-restore /p:WarningLevel=0 /p:TreatWarningsAsErrors=false /p:GenerateDocumentationFile=false /p:DebugType=none /p:DebugSymbols=false 2>build_errors.log

:: Check if build failed due to missing icons
if %ERRORLEVEL% neq 0 (
findstr /c:"PackIcon" build_errors.log >nul
if %ERRORLEVEL% equ 0 (
echo      ⚠ Build failed due to missing icon packs - adding required packs...
echo      ⚡ Rebuilding with full icon compatibility...
