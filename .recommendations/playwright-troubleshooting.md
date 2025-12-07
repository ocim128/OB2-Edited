# Playwright "Not Responding" Troubleshooting Guide

## Problem: Browser Hangs/Not Responding on Open Browser Block

### Symptoms
- ✅ Browsers are installed in `%LOCALAPPDATA%\ms-playwright`
- ✅ Version matches between PCs
- ❌ Application hangs when executing "Open Browser" Playwright block
- ❌ No error message, just freezes/not responding

---

## Root Causes & Fixes

### 🔴 Cause #1: Missing Visual C++ Redistributables (Most Common)

**Why this happens:**
- Chromium and Firefox browsers require Visual C++ Runtime
- Your build PC likely has Visual Studio installed (includes these)
- The other PC probably doesn't have them

**Solution:**

1. **Check if installed:**
   - Open: `Control Panel` → `Programs` → `Programs and Features`
   - Look for: "Microsoft Visual C++ 2015-2022 Redistributable (x64)"
   - Also check for 2013, 2015, 2017, 2019, 2022 versions

2. **Install the missing redistributables:**
   
   **Option A: Install Latest All-in-One (Recommended)**
   ```powershell
   # Download and install Visual C++ Runtime Installer (All-in-One)
   # Visit: https://www.techpowerup.com/download/visual-c-redistributable-runtime-package-all-in-one/
   ```

   **Option B: Install from Microsoft**
   - Download latest C++ Redistributable: https://aka.ms/vs/17/release/vc_redist.x64.exe
   - Run installer as Administrator
   - Also install x86 version if using 32-bit app: https://aka.ms/vs/17/release/vc_redist.x86.exe

3. **Restart your computer** after installation

4. **Test again** - Playwright should now work

---

### 🔴 Cause #2: Missing Browser Dependencies

**Why this happens:**
- Playwright browsers have dependencies that might not install automatically
- Windows Media Feature Pack might be missing (especially Windows N/KN editions)

**Solution:**

**Run Playwright installation with dependencies:**
```powershell
# Navigate to your output directory
cd "C:\path\to\OpenBullet2\bin\Debug\net8.0-windows"

# Install browsers WITH dependencies
.\playwright.ps1 install chromium --with-deps
.\playwright.ps1 install firefox --with-deps

# Or use this alternative:
playwright install --with-deps
```

**For Windows N/KN editions:**
- Install Media Feature Pack: https://support.microsoft.com/windows/media-feature-pack-windows-10/11

---

### 🔴 Cause #3: Antivirus/Windows Defender Blocking

**Why this happens:**
- Antivirus sees browser automation as suspicious
- Blocks browser process from starting

**Solution:**

1. **Add exclusions to Windows Defender:**
   ```powershell
   # Run PowerShell as Administrator
   Add-MpPreference -ExclusionPath "$env:LOCALAPPDATA\ms-playwright"
   Add-MpPreference -ExclusionPath "C:\path\to\OpenBullet2"
   ```

2. **Check if antivirus is blocking:**
   - Temporarily disable antivirus
   - Try running Playwright again
   - If it works, add exclusions permanently

3. **Windows SmartScreen:**
   - Right-click the browser executable in `%LOCALAPPDATA%\ms-playwright`
   - Properties → General → Check "Unblock" → Apply

---

### 🔴 Cause #4: Different Windows Editions/Versions

**Why this happens:**
- Some Windows editions have different default configurations
- Windows Server editions might be missing components

**Check Windows version:**
```powershell
# Run in PowerShell
winver
systeminfo | findstr /B /C:"OS Name" /C:"OS Version"
```

**Solutions by edition:**

**Windows Server:**
- Install Desktop Experience features
- Enable Media Foundation components

**Windows 10/11 LTSC/Enterprise:**
- May need optional features enabled
- Check: Settings → Apps → Optional features

---

### 🔴 Cause #5: Corrupted Browser Installation

**Why this happens:**
- Installation was interrupted
- Files copied but not properly extracted
- Version mismatch between metadata and binaries

**Solution:**

1. **Clean reinstall:**
   ```powershell
   # Delete existing installation
   Remove-Item -Path "$env:LOCALAPPDATA\ms-playwright" -Recurse -Force
   
   # Reinstall
   cd "C:\path\to\OpenBullet2\bin\Debug\net8.0-windows"
   .\playwright.ps1 install --with-deps
   ```

2. **Verify installation:**
   ```powershell
   # Check browser folders exist
   dir "$env:LOCALAPPDATA\ms-playwright"
   
   # Should see folders like:
   # chromium-xxxx
   # firefox-xxxx
   ```

---

### 🔴 Cause #6: User Account Permissions

**Why this happens:**
- Different user account on second PC
- Restricted user permissions

**Solution:**

1. **Run as Administrator:**
   - Right-click OpenBullet2.exe
   - "Run as administrator"

2. **Check folder permissions:**
   ```powershell
   # Check if you can write to playwright folder
   icacls "$env:LOCALAPPDATA\ms-playwright"
   ```

3. **Grant full control:**
   - Right-click `%LOCALAPPDATA%\ms-playwright` folder
   - Properties → Security → Edit
   - Add your user → Full control

---

### 🔴 Cause #7: .NET Runtime Issues

**Why this happens:**
- Different .NET runtime versions
- Missing .NET components

**Solution:**

1. **Verify .NET 8.0 is installed:**
   ```powershell
   dotnet --list-runtimes
   ```
   
   Should show: `Microsoft.NETCore.App 8.0.x`

2. **Install .NET 8.0 Runtime:**
   - Desktop Runtime: https://dotnet.microsoft.com/download/dotnet/8.0
   - Choose "Desktop Runtime (x64)"

---

## Quick Diagnostic Steps

Run these in order on the problematic PC:

### Step 1: Check System Requirements
```powershell
# PowerShell script to check requirements
Write-Host "=== Playwright System Check ===" -ForegroundColor Cyan

# Check Windows version
Write-Host "`nWindows Version:" -ForegroundColor Yellow
(Get-CimInstance Win32_OperatingSystem).Caption

# Check .NET Runtime
Write-Host "`n.NET Runtimes:" -ForegroundColor Yellow
dotnet --list-runtimes | Select-String "8.0"

# Check Visual C++ Redistributables
Write-Host "`nVisual C++ Redistributables:" -ForegroundColor Yellow
Get-ItemProperty HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\* | 
  Where-Object DisplayName -like "*Visual C++*" | 
  Select-Object DisplayName, DisplayVersion

# Check Playwright browsers
Write-Host "`nPlaywright Browsers:" -ForegroundColor Yellow
if (Test-Path "$env:LOCALAPPDATA\ms-playwright") {
    Get-ChildItem "$env:LOCALAPPDATA\ms-playwright" -Directory | 
      Select-Object Name
} else {
    Write-Host "NOT FOUND" -ForegroundColor Red
}

# Check disk space
Write-Host "`nDisk Space (C:):" -ForegroundColor Yellow
Get-PSDrive C | Select-Object Used, Free

Write-Host "`n=== End Check ===" -ForegroundColor Cyan
```

### Step 2: Test Browser Manually
```powershell
# Try launching browser directly
cd "$env:LOCALAPPDATA\ms-playwright"

# For Chromium
cd (Get-ChildItem -Filter "chromium-*" | Select-Object -First 1).FullName
.\chrome-win\chrome.exe --version

# For Firefox  
cd ..
cd (Get-ChildItem -Filter "firefox-*" | Select-Object -First 1).FullName
.\firefox\firefox.exe --version
```

If these commands hang/freeze, the issue is with browser dependencies, not OpenBullet2.

---

## Most Likely Solution (Try This First!)

Based on your symptoms, **99% of the time** this is fixed by installing Visual C++ Redistributables:

```powershell
# Download and run these installers on the problematic PC:
# 1. VC++ 2015-2022 x64: https://aka.ms/vs/17/release/vc_redist.x64.exe
# 2. VC++ 2015-2022 x86: https://aka.ms/vs/17/release/vc_redist.x86.exe

# After installing both:
# - Restart the computer
# - Try OpenBullet2 again
```

---

## Alternative: Use System Browsers

If nothing works, bypass Playwright browsers entirely:

1. **Install Chrome/Firefox normally** on the PC
2. **In OpenBullet2:**
   - RL Settings → Playwright Settings
   - Set browser path to system browser:
     - Chrome: `C:\Program Files\Google\Chrome\Application\chrome.exe`
     - Firefox: `C:\Program Files\Mozilla Firefox\firefox.exe`

This always works because system browsers come with all required dependencies.

---

## Still Not Working?

### Collect Diagnostic Info:

1. **OpenBullet2 Version:** (Check About)
2. **Windows Version:** Run `winver`
3. **Installed Visual C++:** Check Programs and Features
4. **Browser folders:** Screenshot of `%LOCALAPPDATA%\ms-playwright`
5. **Error logs:** Check OpenBullet2 logs folder

### Enable Verbose Logging:

In your config, before "Open Browser" block, add:
- Set environment variable: `DEBUG=pw:*`

This will show detailed Playwright logs.

---

## Prevention for Future Deployments

**When deploying to other PCs, always:**

1. ✅ Install Visual C++ Redistributables 2015-2022 (both x64 and x86)
2. ✅ Install .NET 8.0 Desktop Runtime
3. ✅ Run `playwright install --with-deps` as Administrator
4. ✅ Add antivirus exclusions before running
5. ✅ Test browser launch manually first

**Or simply:** Use system browsers (Chrome/Firefox) instead of Playwright browsers
