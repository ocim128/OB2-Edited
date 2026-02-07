using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using RuriLib.Models.Settings;

namespace RuriLib.Providers.Playwright;

/// <summary>
/// Centralizes Playwright runtime preparation by making sure the browser bundle exists
/// in a known location and installing it on-demand when the application starts.
/// </summary>
public static class PlaywrightRuntimeService
{
    private static readonly SemaphoreSlim InstallGate = new(1, 1);
    private static readonly Dictionary<PlaywrightBrowserType, string[]> BrowserDirectoryTokens = new()
    {
        { PlaywrightBrowserType.Chromium, new[] { "chromium", "chrome", "msedge" } },
        { PlaywrightBrowserType.Firefox, new[] { "firefox" } },
        { PlaywrightBrowserType.Webkit, new[] { "webkit" } }
    };

    // Default user-local directory used by Playwright for browser binaries.
    private static readonly Lazy<string> RuntimePath = new(() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ms-playwright"));

    private static readonly object _runtimePathLock = new();
    private static string _activeRuntimePath = string.Empty;
    private static volatile bool _environmentPrepared;

    /// <summary>
    /// Returns the currently active runtime path (packaged or user-local) that Playwright will read from.
    /// </summary>
    /// <summary>
    /// Returns the runtime path (packaged or user-local) that Playwright will read from.
    /// </summary>
    public static string GetRuntimePath(bool useBuildPath) => EnsureRuntimePath(useBuildPath);

    /// <summary>
    /// Creates a new <see cref="IPlaywright"/> instance while ensuring that the requested browser binaries are available.
    /// </summary>
    public static async Task<IPlaywright> CreateAsync(
        PlaywrightBrowserType ensureBrowser,
        string? executableOverride = null,
        Action<string>? log = null,
        bool useBuildPath = true,
        CancellationToken cancellationToken = default)
    {
        await EnsureBrowserInstalledAsync(ensureBrowser, executableOverride, log, useBuildPath, cancellationToken).ConfigureAwait(false);
        return await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Makes sure the runtime folder contains the binaries required for the given browser type.
    /// Installations run only once and are cached across application launches.
    /// </summary>
    public static async Task EnsureBrowserInstalledAsync(
        PlaywrightBrowserType browserType,
        string? executableOverride = null,
        Action<string>? log = null,
        bool useBuildPath = true,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(executableOverride) && File.Exists(executableOverride))
        {
            // Custom executable provided by the user, nothing to install.
            log?.Invoke($"Using custom browser executable: {executableOverride}");
            return;
        }

        var runtimePath = EnsureRuntimePath(useBuildPath);
        if (IsBrowserInstalled(runtimePath, browserType))
        {
            log?.Invoke($"Browser {browserType} is already installed at '{runtimePath}'");
            return;
        }

        await InstallGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring the lock
            if (IsBrowserInstalled(runtimePath, browserType))
            {
                log?.Invoke($"Browser {browserType} was installed by another thread");
                return;
            }

            // Validate system requirements before installation
            ValidateSystemRequirements(runtimePath, log);

            log?.Invoke($"Installing Playwright {browserType} browser bundle to '{runtimePath}'...");
            log?.Invoke($"This may take a few minutes depending on your internet connection...");
            
            var browserCliName = GetBrowserCliName(browserType);
            var installArgs = new[] { "install", browserCliName };

            // Redirect input to prevent waiting for user input
            var originalIn = Console.In;
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var inputReader = new StringReader("");
            using var outputWriter = new StringWriter();
            using var errorWriter = new StringWriter();

            try
            {
                Console.SetIn(inputReader);
                Console.SetOut(outputWriter);
                Console.SetError(errorWriter);

                // Run the installation task and poll for log updates
                var installTask = Task.Run(() => Microsoft.Playwright.Program.Main(installArgs), cancellationToken);
                
                // Poll output while running to provide feedback
                while (!installTask.IsCompleted)
                {
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                    var partialOutput = outputWriter.ToString();
                    if (!string.IsNullOrWhiteSpace(partialOutput))
                    {
                        var lastLine = partialOutput.Trim().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                        if (lastLine != null) log?.Invoke(lastLine);
                    }
                }

                var exitCode = await installTask.ConfigureAwait(false);
                var fullOutput = outputWriter.ToString(); 
                var fullError = errorWriter.ToString();

                if (!string.IsNullOrWhiteSpace(fullOutput)) log?.Invoke(fullOutput);
                if (!string.IsNullOrWhiteSpace(fullError)) log?.Invoke($"Error Output: {fullError}");

                if (exitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"Playwright CLI exited with code {exitCode} while installing {browserType}.\n" +
                        $"Output: {fullOutput}\nError: {fullError}\n" +
                        BuildManualInstallationMessage(browserType, browserCliName, runtimePath));
                }

                // Verify installation was successful
                if (!IsBrowserInstalled(runtimePath, browserType))
                {
                    throw new InvalidOperationException(
                        $"Browser installation reported success but {browserType} was not found in '{runtimePath}'.\n" +
                        $"Output: {fullOutput}\nError: {fullError}\n" +
                        BuildManualInstallationMessage(browserType, browserCliName, runtimePath));
                }

                log?.Invoke($"✅ Playwright {browserType} installation completed successfully!");
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                var fullOutput = outputWriter.ToString();
                var fullError = errorWriter.ToString();
                
                // Wrap unexpected exceptions with helpful context
                throw new InvalidOperationException(
                    $"Failed to install Playwright {browserType} browser: {ex.Message}\n" +
                    $"Output: {fullOutput}\nError: {fullError}\n" +
                    BuildManualInstallationMessage(browserType, browserCliName, runtimePath),
                    ex);
            }
            finally
            {
                Console.SetIn(originalIn);
                Console.SetOut(originalOut);
                Console.SetError(originalError);
                InstallGate.Release();
            }
        }
        finally
        {
            // Gate is released in the inner finally block if acquired
        }
    }

    /// <summary>
    /// Validates system requirements before attempting browser installation.
    /// </summary>
    private static void ValidateSystemRequirements(string runtimePath, Action<string>? log)
    {
        // Check disk space (need at least 500MB per browser)
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(runtimePath) ?? runtimePath);
            const long minimumBytes = 500_000_000; // 500 MB
            
            if (drive.AvailableFreeSpace < minimumBytes)
            {
                var availableMB = drive.AvailableFreeSpace / 1_000_000;
                var requiredMB = minimumBytes / 1_000_000;
                throw new InvalidOperationException(
                    $"Insufficient disk space on drive {drive.Name}. " +
                    $"Available: {availableMB} MB, Required: {requiredMB} MB. " +
                    $"Please free up disk space and try again.");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            log?.Invoke($"⚠️ Warning: Could not verify disk space: {ex.Message}");
        }

        // Check write permissions
        try
        {
            Directory.CreateDirectory(runtimePath);
            var testFile = Path.Combine(runtimePath, $".playwright_write_test_{Guid.NewGuid():N}");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
        }
        catch (Exception ex)
        {
            throw new UnauthorizedAccessException(
                $"No write permission to browser installation directory '{runtimePath}'. " +
                $"Error: {ex.Message}\n" +
                $"Solution: Run the application as administrator or choose a different installation directory.",
                ex);
        }
    }

    /// <summary>
    /// Builds a helpful error message with manual installation instructions.
    /// </summary>
    private static string BuildManualInstallationMessage(
        PlaywrightBrowserType browserType, 
        string browserCliName, 
        string runtimePath)
    {
        return $"\n" +
               $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
               $"MANUAL INSTALLATION OPTIONS:\n" +
               $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
               $"\n" +
               $"Option 1: Install using PowerShell (Recommended)\n" +
               $"  1. Open PowerShell as Administrator\n" +
               $"  2. Navigate to your output directory (bin/Debug or bin/Release)\n" +
               $"  3. Run: .\\playwright.ps1 install {browserCliName}\n" +
               $"\n" +
               $"Option 2: Install using dotnet tool\n" +
               $"  1. Open Command Prompt or PowerShell as Administrator\n" +
               $"  2. Run: pwsh -Command \"& {{dotnet tool install -g Microsoft.Playwright.CLI}}\"\n" +
               $"  3. Run: playwright install {browserCliName}\n" +
               $"\n" +
               $"Option 3: Use a custom browser executable\n" +
               $"  1. Download {browserType} browser manually\n" +
               $"  2. In Flux settings, go to Settings > Playwright\n" +
               $"  3. Set the '{browserType} Binary Location' to your browser executable\n" +
               $"     Example paths:\n" +
               GetExampleBrowserPaths(browserType) +
               $"\n" +
               $"Option 4: Download from official website\n" +
               $"  Visit: https://playwright.dev/dotnet/docs/browsers\n" +
               $"\n" +
               $"Installation Directory: {runtimePath}\n" +
               $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
               $"\n" +
               $"If the problem persists, check:\n" +
               $"  • Your internet connection\n" +
               $"  • Firewall/antivirus settings\n" +
               $"  • Available disk space (need ~500MB)\n" +
               $"  • Write permissions to: {runtimePath}";
    }

    /// <summary>
    /// Returns example paths for manually installed browsers based on browser type.
    /// </summary>
    private static string GetExampleBrowserPaths(PlaywrightBrowserType browserType)
    {
        return browserType switch
        {
            PlaywrightBrowserType.Chromium => 
                $"     - Chrome: C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\n" +
                $"     - Edge: C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe\n" +
                $"     - Brave: C:\\Program Files\\BraveSoftware\\Brave-Browser\\Application\\brave.exe\n",
            
            PlaywrightBrowserType.Firefox => 
                $"     - Firefox: C:\\Program Files\\Mozilla Firefox\\firefox.exe\n" +
                $"     - LibreWolf: C:\\Program Files\\LibreWolf\\librewolf.exe\n",
            
            PlaywrightBrowserType.Webkit => 
                $"     - Webkit browsers are not commonly available on Windows\n" +
                $"     - Consider using Chromium or Firefox instead\n",
            
            _ => string.Empty
        };
    }

    private static string EnsureRuntimePath(bool useBuildPath)
    {
        string targetPath;
        if (useBuildPath)
        {
            targetPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".playwright"));
        }
        else
        {
            targetPath = RuntimePath.Value;
        }

        // Fast path: already initialized with correct path
        if (_environmentPrepared && _activeRuntimePath == targetPath && Directory.Exists(_activeRuntimePath))
        {
            return _activeRuntimePath;
        }

        // Slow path: initialize with lock (double-checked locking)
        lock (_runtimePathLock)
        {
            // Re-check after acquiring lock
            if (_environmentPrepared && _activeRuntimePath == targetPath && Directory.Exists(_activeRuntimePath))
            {
                return _activeRuntimePath;
            }

            _activeRuntimePath = targetPath;
            Directory.CreateDirectory(_activeRuntimePath);

            Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", _activeRuntimePath);
            _environmentPrepared = true;
            return _activeRuntimePath;
        }
    }

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
            // Method 1: Check by directory name (primary method)
            var hasDirectoryMatch = Directory.EnumerateDirectories(runtimePath, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Any(folderName => folderName != null &&
                                   tokens.Any(token => folderName.StartsWith(token, StringComparison.OrdinalIgnoreCase)));

            if (hasDirectoryMatch)
            {
                return true;
            }

            // Method 2: Fallback - look for browser executables
            // This provides resilience if Playwright changes its directory structure
            var executableName = GetBrowserExecutableName(browserType);
            if (!string.IsNullOrEmpty(executableName))
            {
                var hasExecutable = Directory.EnumerateFiles(runtimePath, executableName, SearchOption.AllDirectories)
                    .Any();

                if (hasExecutable)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the expected executable name for a browser type on Windows.
    /// </summary>
    private static string GetBrowserExecutableName(PlaywrightBrowserType browserType)
    {
        return browserType switch
        {
            PlaywrightBrowserType.Chromium => "chrome.exe",
            PlaywrightBrowserType.Firefox => "firefox.exe",
            PlaywrightBrowserType.Webkit => "Playwright.exe",
            _ => string.Empty
        };
    }

    private static string GetBrowserCliName(PlaywrightBrowserType browserType) => browserType switch
    {
        PlaywrightBrowserType.Chromium => "chromium",
        PlaywrightBrowserType.Firefox => "firefox",
        PlaywrightBrowserType.Webkit => "webkit",
        _ => throw new ArgumentOutOfRangeException(nameof(browserType), browserType, "Unsupported browser type")
    };
}
