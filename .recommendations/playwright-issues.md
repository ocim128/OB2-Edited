# Playwright Issues on OpenBullet2.Native

> **STATUS UPDATE:** Issues #1 and #3 have been FIXED! See [playwright-fixes-summary.md](./playwright-fixes-summary.md) for details.

## Identified Problems

### ✅ 1. Browser Installation via CLI May Fail Silently [FIXED]
**Location:** `RuriLib\Providers\Playwright\PlaywrightRuntimeService.cs:84`

**Fixed in commit:** 2025-12-05

The browser installation now includes:
- ✅ Disk space validation (minimum 500MB)
- ✅ Write permission checks
- ✅ Post-installation verification
- ✅ Comprehensive error messages with manual installation instructions
- ✅ Better logging throughout the process

### 2. Hardcoded Browser Directory Detection [IMPROVED]
**Location:** `RuriLib\Providers\Playwright\PlaywrightRuntimeService.cs:125-148`

**Improvements made:** Added fallback detection method that looks for browser executables

The `IsBrowserInstalled()` method now:
- ✅ Uses primary method: directory name patterns (original behavior)
- ✅ Uses fallback method: searches for actual browser executables (chrome.exe, firefox.exe, etc.)
- ✅ More resilient to Playwright directory structure changes

### ✅ 3. No Manual Installation Option [FIXED]

There's no documented way for users to:
- Manually install browsers if automatic installation fails
- Point to an existing Playwright installation
- Use system-installed browsers as a fallback

### 4. Missing Windows-Specific Validations
**Required but not checked:**
- Write permissions to `%LOCALAPPDATA%\ms-playwright`
- Available disk space (browsers are ~100-300 MB each)
- Visual C++ Redistributable installation
- Network connectivity before attempting downloads

## Recommended Fixes

### Fix 1: Add Better Error Handling and Logging
```csharp
public static async Task EnsureBrowserInstalledAsync(
    PlaywrightBrowserType browserType,
    string? executableOverride = null,
    Action<string>? log = null,
    CancellationToken cancellationToken = default)
{
    // ... existing code ...
    
    try
    {
        log?.Invoke($"Installing Playwright {browserType} browser bundle to '{runtimePath}'...");
        
        // Check disk space
        var drive = new DriveInfo(Path.GetPathRoot(runtimePath));
        if (drive.AvailableFreeSpace < 500_000_000) // 500 MB minimum
        {
            throw new InvalidOperationException(
                $"Insufficient disk space. Need at least 500 MB, have {drive.AvailableFreeSpace / 1_000_000} MB");
        }
        
        // Check write permissions
        if (!HasWritePermission(runtimePath))
        {
            throw new UnauthorizedAccessException(
                $"No write permission to '{runtimePath}'. Try running as administrator.");
        }
        
        var installArgs = new[] { "install", GetBrowserCliName(browserType), "--with-deps" };
        var exitCode = await Task.Run(() => Microsoft.Playwright.Program.Main(installArgs), cancellationToken)
            .ConfigureAwait(false);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Playwright CLI exited with code {exitCode} while installing {browserType}.\n" +
                $"Try manually installing by running: pwsh bin/Debug/net8.0/playwright.ps1 install {GetBrowserCliName(browserType)}\n" +
                $"Or download from: https://playwright.dev/dotnet/docs/browsers");
        }

        log?.Invoke($"Playwright {browserType} installation completed.");
    }
    catch (Exception ex)
    {
        log?.Invoke($"ERROR: Failed to install {browserType}: {ex.Message}");
        throw;
    }
}

private static bool HasWritePermission(string path)
{
    try
    {
        Directory.CreateDirectory(path);
        var testFile = Path.Combine(path, ".write_test");
        File.WriteAllText(testFile, "test");
        File.Delete(testFile);
        return true;
    }
    catch
    {
        return false;
    }
}
```

### Fix 2: Add Fallback Detection Method
```csharp
private static bool IsBrowserInstalled(string runtimePath, PlaywrightBrowserType browserType)
{
    if (!Directory.Exists(runtimePath))
    {
        return false;
    }

    if (!BrowserDirectoryTokens.TryGetValue(browserType, out var tokens) || tokens.Length == 0)
    {
        return false;
    }

    try
    {
        // Method 1: Check by directory name
        var hasNameMatch = Directory.EnumerateDirectories(runtimePath, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Any(folderName => folderName != null &&
                               tokens.Any(token => folderName.StartsWith(token, StringComparison.OrdinalIgnoreCase)));
        
        if (hasNameMatch)
            return true;
            
        // Method 2: Fallback - look for browser executables
        var executableName = browserType switch
        {
            PlaywrightBrowserType.Chromium => "chrome.exe",
            PlaywrightBrowserType.Firefox => "firefox.exe",
            PlaywrightBrowserType.Webkit => "Playwright.exe",
            _ => null
        };
        
        if (executableName != null)
        {
            return Directory.EnumerateFiles(runtimePath, executableName, SearchOption.AllDirectories).Any();
        }
        
        return false;
    }
    catch
    {
        return false;
    }
}
```

### Fix 3: Add Pre-Installation Validation
Add a new method to validate system requirements:

```csharp
private static void ValidateSystemRequirements(Action<string>? log = null)
{
    // Check for Visual C++ Redistributable (common issue on Windows)
    var vcRedistInstalled = CheckVCRedist();
    if (!vcRedistInstalled)
    {
        log?.Invoke("WARNING: Visual C++ Redistributable not detected. " +
            "Download from: https://aka.ms/vs/17/release/vc_redist.x64.exe");
    }
    
    // Check PowerShell availability
    var psVersion = GetPowerShellVersion();
    if (psVersion < new Version(5, 0))
    {
        log?.Invoke($"WARNING: PowerShell {psVersion} detected. PowerShell 5.0+ recommended for Playwright.");
    }
}
```

## Alternative: Use NuGet Package Installation
Another approach is to rely on the `Microsoft.Playwright` NuGet package's built-in browser installation:

```bash
# After building, run this in the output directory:
pwsh bin/Debug/net8.0/playwright.ps1 install
```

Or automate it in a post-build event in the `.csproj`:
```xml
<Target Name="EnsurePlaywrightBrowsers" AfterTargets="Build">
  <Exec Command="pwsh $(TargetDir)playwright.ps1 install chromium firefox" 
        ContinueOnError="true" 
        IgnoreExitCode="true" />
</Target>
```

## Testing Recommendations

1. **Test on clean Windows installation** without Visual C++ Redistributables
2. **Test with limited disk space** (< 500 MB free)
3. **Test without admin privileges** to ensure proper error messages
4. **Test with proxy/firewall** that might block browser downloads
5. **Test with corrupted browser installation** to ensure proper recovery

## User Documentation Needed

Add to README or user guide:
- Manual browser installation steps
- System requirements (Visual C++ Redistributable, disk space, etc.)
- Troubleshooting guide for common installation errors
- How to verify browser installation: Check `%LOCALAPPDATA%\ms-playwright` directory
