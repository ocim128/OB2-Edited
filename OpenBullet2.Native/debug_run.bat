@echo off
echo [DEBUG] Starting OpenBullet2.Native with error capture...
echo [DEBUG] Current directory: %CD%

if not exist "bin\publish\testing\OpenBullet2.Native.exe" (
    echo [ERROR] Executable not found! 
    pause
    exit /b 1
)

echo [DEBUG] Found executable, changing to published directory...
cd /d "bin\publish\testing"
echo [DEBUG] Working directory: %CD%

echo [DEBUG] Running application...
"OpenBullet2.Native.exe"
echo [DEBUG] Application exited with code: %errorlevel%
pause
