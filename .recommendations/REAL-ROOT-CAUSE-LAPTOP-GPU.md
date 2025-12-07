# REAL Root Cause Analysis

## Critical Differences Between PCs

### PRIMARY PC (WORKS):
- **PC Type**: DESKTOP
- **Windows**: Windows 11 Education
- **Graphics**: 
  - NVIDIA GeForce RTX 4070 Ti SUPER (Desktop, dedicated)
  - Intel UHD Graphics 770 (Desktop integrated)
- **Driver Dates**: Aug 2025 (newer)

### OLD PC (DOESN'T WORK):
- **PC Type**: LAPTOP  ← KEY DIFFERENCE!
- **Windows**: Windows 11 Home Single Language
- **Graphics**: 
  - AMD Radeon Graphics (Laptop integrated)
  - NVIDIA GeForce RTX 3050 Laptop GPU (Laptop dedicated)
- **Driver Dates**: Aug-Sep 2024 (6 months older)

---

## THE REAL ISSUE: LAPTOP HYBRID GRAPHICS + FIREFOX

Firefox on Windows laptops with NVIDIA Optimus (or AMD equivalent) has a known issue:
- Firefox tries to initialize on the integrated GPU (AMD Radeon)
- Optimus/hybrid graphics switches it to dedicated GPU (NVIDIA 3050)
- During the GPU switch, `NewPageAsync()` hangs waiting for graphics context

This is why:
- ✅ Chromium works (better GPU switching support)
- ✅ System Firefox works (configured for the laptop's hybrid graphics)
- ❌ Playwright Firefox hangs (no GPU preference configured)

---

## THE FIX: Force Firefox to Use Specific GPU

### Option 1: Force Firefox to Use Integrated GPU (Fastest)

```powershell
# Run PowerShell as Administrator
$firefoxPath = "C:\Users\maula\AppData\Local\ms-playwright\firefox-1429\firefox\firefox.exe"

# Add graphics preference to Windows
$registryPath = "HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences"
$value = "GpuPreference=1;" # 1 = Power Saving (Integrated), 2 = High Performance (Dedicated)

reg add $registryPath /v $firefoxPath /t REG_SZ /d $value /f
```

### Option 2: Force Firefox to Use Dedicated GPU

```powershell
# Run PowerShell as Administrator
$firefoxPath = "C:\Users\maula\AppData\Local\ms-playwright\firefox-1429\firefox\firefox.exe"
$registryPath = "HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences"
$value = "GpuPreference=2;" # 2 = High Performance (NVIDIA)

reg add $registryPath /v $firefoxPath /t REG_SZ /d $value /f
```

### Option 3: Add Firefox Launch Args to Disable GPU

In OpenBullet2 config:
```
BLOCK:PlaywrightOpenBrowser
  browserType = Firefox
  headless = False
  extraArgs = ["--disable-gpu", "--disable-software-rasterizer", "--disable-gpu-compositing"]
END
```

### Option 4: Update NVIDIA Drivers

Your NVIDIA driver is from Sep 2024. Update to latest:
```powershell
# Check for driver updates
# Or download from: https://www.nvidia.com/Download/index.aspx
# GeForce RTX 3050 Laptop GPU
```

### Option 5: Configure in NVIDIA Control Panel

1. Open **NVIDIA Control Panel**
2. **Manage 3D Settings** → **Program Settings**
3. Click **Add** → Browse to:
   `C:\Users\maula\AppData\Local\ms-playwright\firefox-1429\firefox\firefox.exe`
4. Set **OpenGL rendering GPU** to:
   - **NVIDIA GeForce RTX 3050** (preferred)
   - OR **Auto-select** (might work)
5. Set **Power management mode** to **Prefer maximum performance**
6. Click **Apply**

---

## Why This Is Different from Primary PC

**Desktop (Primary PC):**
- No GPU switching (dedicated GPU always active)
- No Optimus/hybrid graphics complications
- Firefox gets consistent GPU context

**Laptop (Old PC):**
- GPU switching between AMD (integrated) and NVIDIA (dedicated)
- Firefox hangs during GPU context switch
- `NewPageAsync()` waits for graphics initialization that never completes

---

## Test the GPU Theory

Run this on the LAPTOP:

```powershell
# Force GPU to NVIDIA before launching Firefox
$env:MESA_LOADER_DRIVER_OVERRIDE = "nvidia"
$env:__GLX_VENDOR_LIBRARY_NAME = "nvidia"

# Then test OpenBullet2
```

---

## Windows Edition Difference (Secondary Issue)

**"Home Single Language"** might be missing components, but this is less likely the issue.

Check if Windows Media Feature Pack is installed:
```powershell
# Run as Administrator
Get-WindowsOptionalFeature -Online -FeatureName "WindowsMediaPlayer"
```

If State = Disabled:
```powershell
Enable-WindowsOptionalFeature -Online -FeatureName WindowsMediaPlayer -NoRestart
```

---

## Recommended Solution (Easiest to Hardest)

1. **Try GPU launch args first** (no restart needed)
   - Add `--disable-gpu` to Extra Args

2. **Set GPU preference in registry** (requires restart)
   - Force integrated OR dedicated GPU

3. **Configure NVIDIA Control Panel** (no restart)
   - Add Firefox to program settings

4. **Update NVIDIA drivers** (requires restart)
   - Get latest from nvidia.com

5. **Use system Firefox** (always works)
   - Bypass Playwright Firefox entirely

---

## The smoking gun:
**LAPTOP with hybrid graphics (AMD + NVIDIA) + Playwright Firefox = NewPageAsync hang**

This is a known issue with Firefox on laptops with GPU switching.
