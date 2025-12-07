# Playwright Firefox Environment Diagnostic
# Run this on BOTH PCs and compare the results

Write-Host "`n=== PLAYWRIGHT FIREFOX DIAGNOSTIC ===" -ForegroundColor Cyan
Write-Host "PC Name: $env:COMPUTERNAME" -ForegroundColor Yellow
Write-Host "User: $env:USERNAME" -ForegroundColor Yellow
Write-Host "Date: $(Get-Date)" -ForegroundColor Yellow

# 1. Windows Information
Write-Host "`n[1] WINDOWS VERSION & EDITION" -ForegroundColor Green
$os = Get-CimInstance Win32_OperatingSystem
Write-Host "  Edition: $($os.Caption)"
Write-Host "  Version: $($os.Version)"
Write-Host "  Build: $($os.BuildNumber)"
Write-Host "  Architecture: $($os.OSArchitecture)"
Write-Host "  Install Date: $($os.InstallDate)"

# 2. Graphics/Display Information
Write-Host "`n[2] GRAPHICS DRIVERS" -ForegroundColor Green
Get-WmiObject Win32_VideoController | ForEach-Object {
    Write-Host "  Name: $($_.Name)"
    Write-Host "  Driver Version: $($_.DriverVersion)"
    Write-Host "  Driver Date: $($_.DriverDate)"
    Write-Host "  Status: $($_.Status)"
    Write-Host "  ---"
}

# 3. .NET Runtime
Write-Host "`n[3] .NET RUNTIME" -ForegroundColor Green
try {
    $dotnetVersions = dotnet --list-runtimes | Select-String "8.0"
    $dotnetVersions | ForEach-Object { Write-Host "  $_" }
} catch {
    Write-Host "  ERROR: $($_.Exception.Message)" -ForegroundColor Red
}

# 4. Visual C++ Redistributables
Write-Host "`n[4] VISUAL C++ REDISTRIBUTABLES" -ForegroundColor Green
$vcRedist = Get-ItemProperty HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*, 
                              HKLM:\Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\* |
    Where-Object DisplayName -like "*Visual C++*" | 
    Select-Object DisplayName, DisplayVersion, Publisher |
    Sort-Object DisplayName

if ($vcRedist) {
    $vcRedist | ForEach-Object {
        Write-Host "  $($_.DisplayName) - $($_.DisplayVersion)"
    }
} else {
    Write-Host "  WARNING: No Visual C++ Redistributables found!" -ForegroundColor Red
}

# 5. Windows Media Foundation (required for Firefox)
Write-Host "`n[5] WINDOWS MEDIA FOUNDATION" -ForegroundColor Green
try {
    $mediaFeatures = Get-WindowsOptionalFeature -Online | Where-Object {
        $_.FeatureName -like "*Media*" -or $_.FeatureName -like "*DirectX*"
    } | Select-Object FeatureName, State
    
    if ($mediaFeatures) {
        $mediaFeatures | ForEach-Object {
            $color = if ($_.State -eq "Enabled") { "Green" } else { "Red" }
            Write-Host "  $($_.FeatureName): $($_.State)" -ForegroundColor $color
        }
    }
} catch {
    Write-Host "  ERROR: $($_.Exception.Message)" -ForegroundColor Red
}

# 6. Firefox Process Information
Write-Host "`n[6] FIREFOX PROCESSES" -ForegroundColor Green
$firefoxProcs = Get-Process firefox -ErrorAction SilentlyContinue
if ($firefoxProcs) {
    Write-Host "  WARNING: Firefox is currently running!" -ForegroundColor Yellow
    $firefoxProcs | ForEach-Object {
        Write-Host "  PID: $($_.Id), Memory: $([math]::Round($_.WorkingSet64/1MB, 2)) MB"
    }
} else {
    Write-Host "  No Firefox processes running"
}

# 7. Playwright Installation
Write-Host "`n[7] PLAYWRIGHT INSTALLATION" -ForegroundColor Green
$playwrightPath = "$env:LOCALAPPDATA\ms-playwright"
if (Test-Path $playwrightPath) {
    Write-Host "  Path: $playwrightPath"
    Get-ChildItem $playwrightPath -Directory | ForEach-Object {
        $size = (Get-ChildItem $_.FullName -Recurse -File | Measure-Object -Property Length -Sum).Sum
        $sizeMB = [math]::Round($size/1MB, 2)
        Write-Host "  $($_.Name): $sizeMB MB"
    }
} else {
    Write-Host "  ERROR: Playwright not found at $playwrightPath" -ForegroundColor Red
}

# 8. Firefox Binary Check
Write-Host "`n[8] FIREFOX BINARY VALIDATION" -ForegroundColor Green
$firefoxDirs = Get-ChildItem "$env:LOCALAPPDATA\ms-playwright" -Directory -Filter "firefox-*" -ErrorAction SilentlyContinue
if ($firefoxDirs) {
    foreach ($dir in $firefoxDirs) {
        $firefoxExe = Join-Path $dir.FullName "firefox\firefox.exe"
        if (Test-Path $firefoxExe) {
            Write-Host "  ✅ Found: $firefoxExe" -ForegroundColor Green
            
            # Check file properties
            $fileInfo = Get-Item $firefoxExe
            Write-Host "     Size: $([math]::Round($fileInfo.Length/1MB, 2)) MB"
            Write-Host "     Modified: $($fileInfo.LastWriteTime)"
            
            # Try to get version
            try {
                $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($firefoxExe)
                Write-Host "     Version: $($versionInfo.FileVersion)"
            } catch {}
            
            # Check dependencies
            Write-Host "     Checking dependencies..."
            $requiredDlls = @("xul.dll", "mozglue.dll", "nss3.dll")
            foreach ($dll in $requiredDlls) {
                $dllPath = Join-Path (Split-Path $firefoxExe) $dll
                if (Test-Path $dllPath) {
                    Write-Host "       ✅ $dll" -ForegroundColor Green
                } else {
                    Write-Host "       ❌ $dll MISSING!" -ForegroundColor Red
                }
            }
        } else {
            Write-Host "  ❌ Missing: $firefoxExe" -ForegroundColor Red
        }
    }
} else {
    Write-Host "  ERROR: No Firefox directories found" -ForegroundColor Red
}

# 9. Antivirus/Security Status
Write-Host "`n[9] SECURITY SOFTWARE" -ForegroundColor Green
try {
    $defender = Get-MpComputerStatus -ErrorAction SilentlyContinue
    if ($defender) {
        Write-Host "  Windows Defender Status:"
        Write-Host "    Real-time Protection: $($defender.RealTimeProtectionEnabled)"
        Write-Host "    Antivirus Enabled: $($defender.AntivirusEnabled)"
        Write-Host "    Behavior Monitor: $($defender.BehaviorMonitorEnabled)"
        Write-Host "    IoavProtection: $($defender.IoavProtectionEnabled)"
    }
} catch {
    Write-Host "  Could not query Windows Defender"
}

# Check for other AV
$avProducts = Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction SilentlyContinue
if ($avProducts) {
    Write-Host "  Installed Antivirus:"
    $avProducts | ForEach-Object {
        Write-Host "    $($_.displayName)"
    }
}

# 10. Environment Variables
Write-Host "`n[10] RELEVANT ENVIRONMENT VARIABLES" -ForegroundColor Green
$envVars = @("PLAYWRIGHT_BROWSERS_PATH", "MOZ_HEADLESS", "DISPLAY", "MOZ_DISABLE_CONTENT_SANDBOX")
foreach ($var in $envVars) {
    $value = [Environment]::GetEnvironmentVariable($var)
    if ($value) {
        Write-Host "  $var = $value"
    } else {
        Write-Host "  $var = (not set)"
    }
}

# 11. Disk Space
Write-Host "`n[11] DISK SPACE" -ForegroundColor Green
$drive = Get-PSDrive C
$freeMB = [math]::Round($drive.Free/1MB, 2)
$usedMB = [math]::Round($drive.Used/1MB, 2)
$totalMB = [math]::Round(($drive.Free + $drive.Used)/1MB, 2)
Write-Host "  C: Drive - Free: $freeMB MB / Total: $totalMB MB"

# 12. User Permissions
Write-Host "`n[12] USER PERMISSIONS" -ForegroundColor Green
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Host "  Running as Administrator: $isAdmin"

# Check write access to playwright folder
try {
    $testFile = "$env:LOCALAPPDATA\ms-playwright\.write_test"
    "test" | Out-File -FilePath $testFile -Force
    Remove-Item $testFile -Force
    Write-Host "  Write access to Playwright folder: ✅ OK" -ForegroundColor Green
} catch {
    Write-Host "  Write access to Playwright folder: ❌ DENIED" -ForegroundColor Red
}

# 13. System Locale/Language
Write-Host "`n[13] SYSTEM LOCALE" -ForegroundColor Green
$culture = Get-Culture
Write-Host "  Current Culture: $($culture.Name)"
Write-Host "  Display Name: $($culture.DisplayName)"

# 14. Firefox Manual Launch Test
Write-Host "`n[14] FIREFOX MANUAL LAUNCH TEST" -ForegroundColor Green
$firefoxExePath = (Get-ChildItem "$env:LOCALAPPDATA\ms-playwright\firefox-*\firefox\firefox.exe" -ErrorAction SilentlyContinue | Select-Object -First 1).FullName

if ($firefoxExePath) {
    Write-Host "  Attempting to launch Firefox manually..."
    Write-Host "  Path: $firefoxExePath"
    
    try {
        # Try to get version (quick test)
        $proc = Start-Process -FilePath $firefoxExePath -ArgumentList "--version" -PassThru -NoNewWindow -Wait -RedirectStandardOutput "$env:TEMP\ff_version.txt" -RedirectStandardError "$env:TEMP\ff_error.txt"
        
        if (Test-Path "$env:TEMP\ff_version.txt") {
            $version = Get-Content "$env:TEMP\ff_version.txt"
            Write-Host "  ✅ Firefox Version: $version" -ForegroundColor Green
            Remove-Item "$env:TEMP\ff_version.txt" -Force
        }
        
        if (Test-Path "$env:TEMP\ff_error.txt") {
            $errors = Get-Content "$env:TEMP\ff_error.txt"
            if ($errors) {
                Write-Host "  Errors: $errors" -ForegroundColor Red
            }
            Remove-Item "$env:TEMP\ff_error.txt" -Force
        }
    } catch {
        Write-Host "  ❌ Failed to launch: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "  ❌ Firefox executable not found" -ForegroundColor Red
}

Write-Host "`n=== DIAGNOSTIC COMPLETE ===" -ForegroundColor Cyan
Write-Host "`nSave this output and compare with the other PC!" -ForegroundColor Yellow
Write-Host "Look for differences in:" -ForegroundColor Yellow
Write-Host "  - Windows version/edition" -ForegroundColor Yellow
Write-Host "  - Graphics drivers" -ForegroundColor Yellow
Write-Host "  - Visual C++ versions" -ForegroundColor Yellow
Write-Host "  - Media Foundation status" -ForegroundColor Yellow
Write-Host "  - Antivirus settings" -ForegroundColor Yellow
Write-Host "  - Missing DLL files" -ForegroundColor Yellow
