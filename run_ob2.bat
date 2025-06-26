@echo off
echo Starting OpenBullet2.Native...
echo.

REM Check if executable exists
if not exist "Build_Output\OpenBullet2.Native.exe" (
    echo ERROR: OpenBullet2.Native.exe not found!
    echo Please build the project first using: dotnet build --configuration Release
    pause
    exit /b 1
)

REM Change to Build_Output directory and run the application
echo Running OpenBullet2.Native...
cd Build_Output
start "" "OpenBullet2.Native.exe"
cd ..

echo.
echo OpenBullet2.Native started successfully!
echo Multiple instances are now supported!
echo To start additional instances, run this script again.
echo.
echo Press any key to exit this script...
pause >nul 