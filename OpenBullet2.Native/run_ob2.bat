@echo off
echo Starting OpenBullet2.Native...
echo.

REM Check if executable exists
if not exist "bin\Release\net8.0-windows\OpenBullet2.Native.exe" (
    echo ERROR: OpenBullet2.Native.exe not found!
    echo Please build the project first using: dotnet build --configuration Release
    pause
    exit /b 1
)

REM Run the application
echo Running OpenBullet2.Native...
start "" "bin\Release\net8.0-windows\OpenBullet2.Native.exe"

echo.
echo OpenBullet2.Native started successfully!
echo You can now:
echo 1. Navigate to the Plugins page
echo 2. Enable the AutoHotkey system
echo 3. Test the 5 hotkey functions
echo.
echo Press any key to exit this script...
pause >nul 