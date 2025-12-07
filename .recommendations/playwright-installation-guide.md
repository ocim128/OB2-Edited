# Playwright Browser Installation Guide for OpenBullet2

## Quick Start

Playwright browsers should install automatically when you first use them. If installation fails, use one of these manual methods:

---

## Method 1: PowerShell Script (Easiest)

1. **Navigate to your OpenBullet2 output directory:**
   ```powershell
   cd "C:\path\to\OpenBullet2\bin\Debug\net8.0-windows"
   # or
   cd "C:\path\to\OpenBullet2\bin\Release\net8.0-windows"
   ```

2. **Run the installation script:**
   ```powershell
   # Install all browsers
   .\playwright.ps1 install
   
   # Or install specific browsers
   .\playwright.ps1 install chromium
   .\playwright.ps1 install firefox
   .\playwright.ps1 install webkit
   ```

---

## Method 2: Use System Browsers (No Installation)

Instead of installing Playwright browsers, you can use browsers already on your system:

### For Chrome/Edge:
1. Open OpenBullet2
2. Go to **RL Settings** → **Playwright Settings**
3. Set **Browser Type** to `Chromium`
4. Set **Chromium Binary Location** to one of:
   - Chrome: `C:\Program Files\Google\Chrome\Application\chrome.exe`
   - Edge: `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`
   - Brave: `C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe`

### For Firefox:
1. Open OpenBullet2
2. Go to **RL Settings** → **Playwright Settings**
3. Set **Browser Type** to `Firefox`
4. Set **Firefox Binary Location** to:
   - Firefox: `C:\Program Files\Mozilla Firefox\firefox.exe`
   - LibreWolf: `C:\Program Files\LibreWolf\librewolf.exe`

---

## Method 3: Global Playwright CLI Installation

1. **Install the Playwright CLI globally:**
   ```powershell
   dotnet tool install -g Microsoft.Playwright.CLI
   ```

2. **Install browsers:**
   ```powershell
   playwright install chromium
   playwright install firefox
   playwright install webkit
   ```

---

## Troubleshooting

### Error: "Insufficient disk space"
- **Cause:** Less than 500MB free space
- **Solution:** Free up at least 500MB on your system drive

### Error: "No write permission"
- **Cause:** Cannot write to `%LOCALAPPDATA%\ms-playwright`
- **Solution:** Run OpenBullet2 as Administrator or use Method 2 (system browsers)

### Error: "Browser installation failed"
- **Common causes:**
  - No internet connection
  - Firewall/antivirus blocking downloads
  - Proxy settings
- **Solution:** 
  1. Check your internet connection
  2. Temporarily disable antivirus
  3. Use Method 2 (system browsers) as a workaround

### Error: "Browser not found after installation"
- **Solution:** 
  1. Check `%LOCALAPPDATA%\ms-playwright` for browser folders
  2. If empty, use Method 1 or Method 2
  3. Restart OpenBullet2

---

## Verification

To verify browsers are installed correctly:

1. **Check installation directory:**
   ```powershell
   explorer %LOCALAPPDATA%\ms-playwright
   ```
   You should see folders like:
   - `chromium-xxxx`
   - `firefox-xxxx`
   - `webkit-xxxx`

2. **Test in OpenBullet2:**
   - Create a simple config
   - Add "Open Browser" block
   - Run the config
   - Browser should launch successfully

---

## Browser Storage Locations

- **Playwright browsers:** `%LOCALAPPDATA%\ms-playwright`
- **Chrome:** `C:\Program Files\Google\Chrome\Application`
- **Edge:** `C:\Program Files (x86)\Microsoft\Edge\Application`
- **Firefox:** `C:\Program Files\Mozilla Firefox`

---

## Disk Space Requirements

Each browser requires approximately:
- **Chromium:** ~300MB
- **Firefox:** ~250MB
- **Webkit:** ~200MB

**Recommendation:** Have at least 1GB free space if installing all browsers.

---

## Advanced: Manual Browser Download

If all methods fail, you can manually download and extract browsers:

1. Visit: https://playwright.dev/dotnet/docs/browsers
2. Follow the manual installation instructions
3. Extract browsers to: `%LOCALAPPDATA%\ms-playwright`
4. Ensure folder naming follows pattern:
   - `chromium-xxxx`
   - `firefox-xxxx`
   - `webkit-xxxx`

---

## Getting Help

If you continue to experience issues:

1. Check the OpenBullet2 logs for detailed error messages
2. Ensure you have the latest version of OpenBullet2
3. Report issues with:
   - Full error message
   - OpenBullet2 version
   - Windows version
   - Method attempted
   - Screenshot of `%LOCALAPPDATA%\ms-playwright` contents

---

## Tips

- ✅ **Chrome/Edge are recommended** for best compatibility
- ✅ **Use system browsers** (Method 2) if installation issues persist
- ✅ **Check disk space** before installing all browsers
- ⚠️ **Webkit has limited Windows support** - prefer Chromium or Firefox
- ⚠️ **Always run PowerShell as Administrator** for Method 1
