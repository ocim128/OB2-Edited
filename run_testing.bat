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

:: Clean build with memory-optimized settings
dotnet clean --configuration testing --verbosity quiet >nul 2>&1
dotnet publish --configuration testing --self-contained false --output "%BUILD_DIR%" --verbosity quiet --nologo --no-restore /p:UseSharedCompilation=false /p:BuildInParallel=false /p:MaxCpuCount=1 /p:WarningLevel=0 /p:TreatWarningsAsErrors=false /p:GenerateDocumentationFile=false /p:DebugType=none /p:DebugSymbols=false 2>build_errors.log

:: Check if build failed due to missing icons
if %ERRORLEVEL% neq 0 (
    findstr /c:"PackIcon" build_errors.log >nul
    if %ERRORLEVEL% equ 0 (
        echo      ⚠ Build failed due to missing icon packs - adding required packs...
        echo      ⚡ Rebuilding with full icon compatibility...
        
        :: Add back required icon packs temporarily
        dotnet add package MahApps.Metro.IconPacks.RemixIcon --version 4.8.0 --no-restore >nul 2>&1
        dotnet add package MahApps.Metro.IconPacks.Unicons --version 4.8.0 --no-restore >nul 2>&1
        dotnet add package MahApps.Metro.IconPacks.ForkAwesome --version 4.8.0 --no-restore >nul 2>&1
        dotnet add package MahApps.Metro.IconPacks.RadixIcons --version 4.8.0 --no-restore >nul 2>&1
        dotnet add package MahApps.Metro.IconPacks.Modern --version 4.8.0 --no-restore >nul 2>&1
        dotnet add package MahApps.Metro.IconPacks.Octicons --version 4.8.0 --no-restore >nul 2>&1
        dotnet add package MahApps.Metro.IconPacks.BootstrapIcons --version 4.8.0 --no-restore >nul 2>&1
        dotnet add package MahApps.Metro.IconPacks.BoxIcons --version 4.8.0 --no-restore >nul 2>&1
        dotnet add package MahApps.Metro.IconPacks.Microns --version 4.8.0 --no-restore >nul 2>&1
        dotnet add package MahApps.Metro.IconPacks.SimpleIcons --version 4.8.0 --no-restore >nul 2>&1
        
        :: Restore and rebuild
        dotnet restore --force >nul 2>&1
        dotnet publish --configuration testing --self-contained false --output "%BUILD_DIR%" --verbosity quiet --nologo --no-restore /p:UseSharedCompilation=false /p:BuildInParallel=false /p:MaxCpuCount=1 /p:WarningLevel=0 /p:TreatWarningsAsErrors=false /p:GenerateDocumentationFile=false /p:DebugType=none /p:DebugSymbols=false
        
        if %ERRORLEVEL% neq 0 (
            echo      ✗ Build failed even with all icon packs! Exiting...
            echo      ✗ Check build_errors.log for details.
            timeout /t 2 /nobreak >nul
            exit /b 1
        )
    ) else (
        echo      ✗ Build failed! Exiting immediately...
        echo      ✗ Check build_errors.log for details.
        timeout /t 2 /nobreak >nul
        exit /b 1
    )
)

:: Clean up error log
if exist build_errors.log del build_errors.log >nul 2>&1

:: Remove unnecessary net8.0-windows folder (optimization step)
echo      🧹 Cleaning up unnecessary framework folder...
if exist "%BUILD_DIR%\net8.0-windows" (
    rmdir /s /q "%BUILD_DIR%\net8.0-windows" >nul 2>&1
    echo      ✓ Removed redundant net8.0-windows folder
)

:: Calculate build time
set "END_TIME=%time%"
for /f "tokens=1-4 delims=:.," %%a in ("%START_TIME%") do (
   set /a "start=(((%%a*60)+1%%b %% 100)*60+1%%c %% 100)*100+1%%d %% 100"
)
for /f "tokens=1-4 delims=:.," %%a in ("%END_TIME%") do (
   set /a "end=(((%%a*60)+1%%b %% 100)*60+1%%c %% 100)*100+1%%d %% 100"
)
set /a "elapsed=(end-start)"
set /a "seconds=elapsed/100"
set /a "hundredths=elapsed%%100"

echo      ✓ Build completed successfully in %seconds%.%hundredths%s

echo.
echo [4/7] Restoring UserData from backup...
if exist "%USERDATA_BACKUP%" (
    if exist "%USERDATA_BUILD%" rmdir /s /q "%USERDATA_BUILD%"
    robocopy "%USERDATA_BACKUP%" "%USERDATA_BUILD%" /E /NP /NDL /NJH /NJS >nul 2>&1
    echo      ✓ UserData restored from backup
) else (
    echo [5/7] Copying fresh UserData from project...
    if exist "%USERDATA_SOURCE%" (
        robocopy "%USERDATA_SOURCE%" "%USERDATA_BUILD%" /E /NP /NDL /NJH /NJS >nul 2>&1
        echo      ✓ Fresh UserData copied
    ) else (
        echo      ℹ No UserData folder found in project
    )
)

echo.
echo [6/7] Starting OpenBullet2.Native...
cd /d "%BUILD_DIR%"
echo      ✓ Launching from: %BUILD_DIR%

echo.
echo [7/7] Launching application immediately...
start "" "OpenBullet2.Native.exe"

:: Auto-close the command window after launching
exit
