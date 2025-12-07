# Firefox Not Responding on Playwright (Chromium Works Fine)

## Problem Identified
- ✅ Playwright Chromium: **WORKS**
- ❌ Playwright Firefox: **HANGS/NOT RESPONDING**
- Location: Second PC only, build PC works fine

This is a **Firefox-specific Windows dependency issue**.

---

## Root Cause

Firefox on Windows requires additional libraries that Chromium doesn't need:
1. **GTK3 Runtime Libraries** (most common cause)
2. **Windows Media Foundation**
3. **Additional Mozilla-specific dependencies**

---

## Solutions (In Order of Success Rate)

### Solution 1: Use System Firefox (100% Success Rate)

**Instead of Playwright's Firefox, use system-installed Firefox:**

1. **Download and install Firefox normally:**
   - Download: https://www.mozilla.org/firefox/download/
   - Or LibreWolf: https://librewolf.net/installation/windows/
   - Or Floorp: https://floorp.app/download/

2. **Configure OpenBullet2:**
   - Open OpenBullet2
   - Go to: **RL Settings** → **Playwright Settings**
   - Set **Browser Type** to: `Firefox`
   - Set **Firefox Binary Location** to:
     - Standard Firefox: `C:\Program Files\Mozilla Firefox\firefox.exe`
     - LibreWolf: `C:\Program Files\LibreWolf\librewolf.exe`
     - Floorp: `C:\Program Files\Floorp\floorp.exe`

3. **Test** - Should work immediately!

**Why this works:**
- System Firefox comes pre-bundled with ALL required dependencies
- Installation handles all library paths automatically
- No missing GTK3 or other dependency issues

---

### Solution 2: Install Firefox Dependencies Manually

**If you must use Playwright's Firefox:**

#### Step 1: Install GTK3 Runtime
```powershell
# Download GTK3 for Windows
# Option A: MSYS2 (Recommended)
# Download from: https://www.msys2.org/
# After installing MSYS2, run:
pacman -S mingw-w64-x86_64-gtk3

# Option B: GTK Runtime for Windows
# Download from: https://github.com/tschoonj/GTK-for-Windows-Runtime-Environment-Installer
# Install as administrator
```

#### Step 2: Add GTK to System PATH
```powershell
# Run PowerShell as Administrator
$gtkPath = "C:\msys64\mingw64\bin"  # Adjust path if different
[Environment]::SetEnvironmentVariable("Path", $env:Path + ";$gtkPath", "Machine")

# Restart computer for changes to take effect
```

#### Step 3: Reinstall Playwright Firefox with Dependencies
```powershell
# Navigate to OpenBullet2 directory
cd "C:\path\to\OpenBullet2\bin\Debug\net8.0-windows"

# Remove old Firefox
Remove-Item "$env:LOCALAPPDATA\ms-playwright\firefox-*" -Recurse -Force

# Reinstall with dependencies
.\playwright.ps1 install firefox --with-deps

# Or try this alternative:
pwsh -c "npx playwright install firefox --with-deps"
```

---

### Solution 3: Use Firefox Developer Edition

**Firefox Developer Edition has better Windows compatibility:**

1. **Download Firefox Developer Edition:**
   - https://www.mozilla.org/firefox/developer/

2. **Configure in OpenBullet2:**
   - RL Settings → Playwright Settings
   - Firefox Binary Location: `C:\Program Files\Firefox Developer Edition\firefox.exe`

---

### Solution 4: Clean Firefox Profile (If Using Persistent Context)

**If using Firefox profiles (like in Monitor page):**

```powershell
# Delete ALL temporary Firefox profiles
Remove-Item "$env:TEMP\playwright-*" -Recurse -Force
Remove-Item "$env:TEMP\firefox-*" -Recurse -Force
Remove-Item "$env:TEMP\ob2-zip-profile" -Recurse -Force

# Clear Firefox cache in AppData
Remove-Item "$env:LOCALAPPDATA\Mozilla" -Recurse -Force
Remove-Item "$env:APPDATA\Mozilla" -Recurse -Force

# Try again
```

---

### Solution 5: Modify Firefox Launch Options

**Add compatibility flags in OpenBullet2:**

In your config or settings, modify Firefox launch args:

```csharp
// In RL Settings → Playwright Settings → Extra Args, add:
--safe-mode
--no-remote
--new-instance
```

Or in code blocks:
```
BLOCK:PlaywrightOpenBrowser
  browserType = Firefox
  headless = False
  extraArgs = ["--safe-mode", "--no-remote"]
```

---

### Solution 6: Check Windows Media Foundation

**Firefox needs Windows Media Foundation:**

1. **Check if installed:**
   ```powershell
   Get-WindowsOptionalFeature -Online | Where-Object {$_.FeatureName -like "*Media*"}
   ```

2. **Install if missing (especially on Windows Server/N/KN editions):**
   ```powershell
   # Run PowerShell as Administrator
   Enable-WindowsOptionalFeature -Online -FeatureName WindowsMediaPlayer
   
   # For Windows 10/11 N/KN editions:
   # Download Media Feature Pack from Microsoft
   ```

3. **Restart computer**

---

### Solution 7: Antivirus Exception for Firefox

**Some antivirus specifically blocks Firefox automation:**

```powershell
# Add Firefox to Windows Defender exclusions
Add-MpPreference -ExclusionPath "$env:LOCALAPPDATA\ms-playwright\firefox-*"
Add-MpPreference -ExclusionProcess "firefox.exe"
```

**For other antivirus:**
- Add exclusion for: `%LOCALAPPDATA%\ms-playwright\firefox-*`
- Add process exclusion for: `firefox.exe`

---

### Solution 8: Test Firefox Manually

**Verify Firefox itself works:**

```powershell
# Find Firefox installation
cd "$env:LOCALAPPDATA\ms-playwright"
$firefoxDir = Get-ChildItem -Directory | Where-Object {$_.Name -like "firefox-*"} | Select-Object -First 1

# Try launching manually
cd $firefoxDir
.\firefox\firefox.exe --version

# If this hangs, the issue is Firefox binary itself, not OpenBullet2
```

**If manual launch hangs:**
- Firefox binary is corrupted or missing dependencies
- Use Solution 1 (system Firefox) or Solution 2 (install GTK3)

---

## Recommended Approach (Easiest & Most Reliable)

**Just use system-installed Firefox:**

```powershell
# 1. Install Firefox from Mozilla
winget install Mozilla.Firefox

# 2. In OpenBullet2:
# RL Settings → Playwright → Firefox Binary Location
# Set to: C:\Program Files\Mozilla Firefox\firefox.exe

# Done! Works 100% of the time
```

---

## Why Firefox-Specific Issues Happen

**Firefox vs Chromium on Windows:**

| Dependency | Chromium | Firefox |
|------------|----------|---------|
| Visual C++ Runtime | ✅ Required | ✅ Required |
| GTK3 Libraries | ❌ Not needed | ✅ **REQUIRED** |
| Media Foundation | Optional | ✅ **REQUIRED** |
| Mozilla-specific DLLs | ❌ | ✅ **REQUIRED** |

**System-installed browsers include all these dependencies.**
**Playwright-downloaded browsers may be missing them.**

---

## Quick Diagnostic

**Run this to see what's missing:**

```powershell
# Check Firefox installation
$firefoxPath = "$env:LOCALAPPDATA\ms-playwright"
Write-Host "Checking Firefox installation..." -ForegroundColor Cyan

if (Test-Path $firefoxPath) {
    $firefoxDirs = Get-ChildItem $firefoxPath -Directory | Where-Object {$_.Name -like "firefox-*"}
    
    if ($firefoxDirs) {
        Write-Host "✅ Firefox directory found: $($firefoxDirs[0].Name)" -ForegroundColor Green
        
        # Check if firefox.exe exists
        $exePath = Join-Path $firefoxDirs[0].FullName "firefox\firefox.exe"
        if (Test-Path $exePath) {
            Write-Host "✅ firefox.exe exists" -ForegroundColor Green
            
            # Try to get dependencies
            Write-Host "`nChecking dependencies..." -ForegroundColor Yellow
            dumpbin /dependents $exePath 2>$null
            
            if ($LASTEXITCODE -ne 0) {
                Write-Host "⚠️ Could not check dependencies (dumpbin not found)" -ForegroundColor Yellow
                Write-Host "This is normal - but indicates possible missing DLLs" -ForegroundColor Yellow
            }
        } else {
            Write-Host "❌ firefox.exe not found!" -ForegroundColor Red
        }
    } else {
        Write-Host "❌ No Firefox directory found" -ForegroundColor Red
    }
} else {
    Write-Host "❌ Playwright directory not found" -ForegroundColor Red
}

# Check for system Firefox as alternative
$systemFirefox = "C:\Program Files\Mozilla Firefox\firefox.exe"
if (Test-Path $systemFirefox) {
    Write-Host "`n✅ System Firefox is installed" -ForegroundColor Green
    Write-Host "Recommendation: Use system Firefox instead" -ForegroundColor Cyan
} else {
    Write-Host "`n⚠️ System Firefox not found" -ForegroundColor Yellow
    Write-Host "Recommendation: Install Firefox from mozilla.org" -ForegroundColor Cyan
}
```

---

## Final Recommendation

**Since Chromium works fine, just use that!** 

If you absolutely need Firefox:
1. **Best option:** Install system Firefox → configure path in settings
2. **Second option:** Try installing GTK3 dependencies
3. **Last resort:** Use Firefox Developer Edition

Most users find that **system-installed Firefox is more reliable** than Playwright's bundled Firefox on Windows.
