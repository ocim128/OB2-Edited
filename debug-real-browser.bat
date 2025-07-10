@echo off
echo 🛡️ OpenBullet2 Real Browser Debug Suite
echo ========================================
echo.

echo 🔍 Checking prerequisites...

rem Check Node.js installation
node --version >nul 2>&1
if errorlevel 1 (
    echo ❌ Node.js not found! Please install Node.js first.
    echo    Download from: https://nodejs.org/
    pause
    exit /b 1
) else (
    for /f "tokens=*" %%i in ('node --version') do echo ✅ Node.js found: %%i
)

rem Check if Scripts directory exists
if not exist "RuriLib\Scripts" (
    echo ❌ RuriLib\Scripts directory not found!
    pause
    exit /b 1
) else (
    echo ✅ Scripts directory found
)

rem Install dependencies if needed
echo.
echo 📦 Installing/updating dependencies...
cd RuriLib\Scripts
if not exist "node_modules" (
    echo Installing dependencies...
    npm install
    if errorlevel 1 (
        echo ❌ Failed to install dependencies
        pause
        exit /b 1
    )
) else (
    echo Dependencies already installed
)

rem Copy files to build location
echo.
echo 📁 Setting up build directory...
cd ..\..
if not exist "OpenBullet2.Native\bin" mkdir "OpenBullet2.Native\bin"
if not exist "OpenBullet2.Native\bin\testing" mkdir "OpenBullet2.Native\bin\testing"
if not exist "OpenBullet2.Native\bin\testing\Scripts" mkdir "OpenBullet2.Native\bin\testing\Scripts"

robocopy "RuriLib\Scripts" "OpenBullet2.Native\bin\testing\Scripts" /E /NFL /NDL /NJH /NJS

rem Also copy to Build_Output
if not exist "Build_Output\Scripts" mkdir "Build_Output\Scripts"
robocopy "RuriLib\Scripts" "Build_Output\Scripts" /E /NFL /NDL /NJH /NJS

echo ✅ Files copied to build directories

echo.
echo 🧪 Testing Node.js real browser standalone...
cd RuriLib\Scripts
node test-integration.js
set STANDALONE_RESULT=%ERRORLEVEL%

echo.
echo 🔧 Testing C# service integration...
cd ..\..
dotnet run --project test-simple.csproj >nul 2>&1
if errorlevel 1 (
    echo Creating simple test...
    echo using System; > test-simple.cs
    echo using System.Threading.Tasks; >> test-simple.cs
    echo using RuriLib.Services; >> test-simple.cs
    echo. >> test-simple.cs
    echo namespace TestSimple >> test-simple.cs
    echo { >> test-simple.cs
    echo     class Program >> test-simple.cs
    echo     { >> test-simple.cs
    echo         static async Task Main(string[] args^) >> test-simple.cs
    echo         { >> test-simple.cs
    echo             try >> test-simple.cs
    echo             { >> test-simple.cs
    echo                 var service = new PuppeteerRealBrowserService(^); >> test-simple.cs
    echo                 var options = new RealBrowserOptions >> test-simple.cs
    echo                 { >> test-simple.cs
    echo                     Args = new[] { "--no-sandbox" }, >> test-simple.cs
    echo                     Headless = false, >> test-simple.cs
    echo                     Turnstile = true, >> test-simple.cs
    echo                     DisableXvfb = true >> test-simple.cs
    echo                 }; >> test-simple.cs
    echo                 var connection = await service.ConnectAsync(options^); >> test-simple.cs
    echo                 Console.WriteLine(connection.Success ? "✅ SUCCESS" : "❌ FAILED"^); >> test-simple.cs
    echo             } >> test-simple.cs
    echo             catch (Exception ex^) >> test-simple.cs
    echo             { >> test-simple.cs
    echo                 Console.WriteLine($"❌ ERROR: {ex.Message}"^); >> test-simple.cs
    echo             } >> test-simple.cs
    echo         } >> test-simple.cs
    echo     } >> test-simple.cs
    echo } >> test-simple.cs
    
    echo ^<Project Sdk="Microsoft.NET.Sdk"^> > test-simple.csproj
    echo   ^<PropertyGroup^> >> test-simple.csproj
    echo     ^<OutputType^>Exe^</OutputType^> >> test-simple.csproj
    echo     ^<TargetFramework^>net8.0^</TargetFramework^> >> test-simple.csproj
    echo   ^</PropertyGroup^> >> test-simple.csproj
    echo   ^<ItemGroup^> >> test-simple.csproj
    echo     ^<ProjectReference Include="RuriLib\RuriLib.csproj" /^> >> test-simple.csproj
    echo   ^</ItemGroup^> >> test-simple.csproj
    echo ^</Project^> >> test-simple.csproj
)

dotnet run --project test-simple.csproj
set CSHARP_RESULT=%ERRORLEVEL%

echo.
echo 📊 RESULTS SUMMARY:
echo ==================
if %STANDALONE_RESULT%==0 (
    echo ✅ Node.js standalone test: PASSED
) else (
    echo ❌ Node.js standalone test: FAILED
)

if %CSHARP_RESULT%==0 (
    echo ✅ C# integration test: PASSED
) else (
    echo ❌ C# integration test: FAILED
)

echo.
if %STANDALONE_RESULT%==0 if %CSHARP_RESULT%==0 (
    echo 🎉 ALL TESTS PASSED! OpenBullet2 real browser should work now.
    echo.
    echo 🚀 To test in OpenBullet2:
    echo    1. Open OpenBullet2.Native
    echo    2. Create a new config
    echo    3. Add 'Open Browser' block
    echo    4. Test on https://nopecha.com/demo/cloudflare
    echo.
) else (
    echo ⚠️  Some tests failed. Check the output above for details.
    echo.
)

pause 