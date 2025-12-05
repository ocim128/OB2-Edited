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

    private static readonly Lazy<string> PackagedRuntimePath = new(() =>
        Path.Combine(AppContext.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory, "ms-playwright"));

    private static readonly Lazy<string> UserRuntimePath = new(() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenBullet2", "ms-playwright"));

    private static string _activeRuntimePath = string.Empty;
    private static bool _environmentPrepared;

    /// <summary>
    /// Returns the currently active runtime path (packaged or user-local) that Playwright will read from.
    /// </summary>
    public static string ActiveRuntimePath => EnsureRuntimePath();

    /// <summary>
    /// Creates a new <see cref="IPlaywright"/> instance while ensuring that the requested browser binaries are available.
    /// </summary>
    public static async Task<IPlaywright> CreateAsync(
        PlaywrightBrowserType ensureBrowser,
        string? executableOverride = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureBrowserInstalledAsync(ensureBrowser, executableOverride, log, cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(executableOverride) && File.Exists(executableOverride))
        {
            // Custom executable provided by the user, nothing to install.
            return;
        }

        var runtimePath = EnsureRuntimePath();
        if (IsBrowserInstalled(runtimePath, browserType))
        {
            return;
        }

        await InstallGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsBrowserInstalled(runtimePath, browserType))
            {
                return;
            }

            log?.Invoke($"Installing Playwright {browserType} browser bundle to '{runtimePath}'...");
            var installArgs = new[] { "install", GetBrowserCliName(browserType) };
            var exitCode = await Task.Run(() => Microsoft.Playwright.Program.Main(installArgs), cancellationToken)
                .ConfigureAwait(false);

            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Playwright CLI exited with code {exitCode} while installing {browserType}. " +
                    $"See earlier logs for details.");
            }

            log?.Invoke($"Playwright {browserType} installation completed.");
        }
        finally
        {
            InstallGate.Release();
        }
    }

    private static string EnsureRuntimePath()
    {
        if (_environmentPrepared && Directory.Exists(_activeRuntimePath))
        {
            return _activeRuntimePath;
        }

        var packagedPath = PackagedRuntimePath.Value;
        if (Directory.Exists(packagedPath))
        {
            _activeRuntimePath = packagedPath;
        }
        else
        {
            _activeRuntimePath = UserRuntimePath.Value;
            Directory.CreateDirectory(_activeRuntimePath);
        }

        Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", _activeRuntimePath);
        _environmentPrepared = true;
        return _activeRuntimePath;
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
            return Directory.EnumerateDirectories(runtimePath, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Any(folderName => folderName != null &&
                                   tokens.Any(token => folderName.StartsWith(token, StringComparison.OrdinalIgnoreCase)));
        }
        catch
        {
            return false;
        }
    }

    private static string GetBrowserCliName(PlaywrightBrowserType browserType) => browserType switch
    {
        PlaywrightBrowserType.Chromium => "chromium",
        PlaywrightBrowserType.Firefox => "firefox",
        PlaywrightBrowserType.Webkit => "webkit",
        _ => throw new ArgumentOutOfRangeException(nameof(browserType), browserType, "Unsupported browser type")
    };
}
