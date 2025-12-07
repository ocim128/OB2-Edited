using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using RuriLib.Helpers.Playwright;
using RuriLib.Models.Settings;

namespace OpenBullet2.Native.Services;

public sealed class ZipProfileLauncher
{
    public async Task<LaunchedZipProfile> LaunchAsync(
        ZipProfileLaunchRequest request,
        IProgress<ZipLaunchStatus>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Settings);

        if (string.IsNullOrWhiteSpace(request.ZipArchivePath) || !File.Exists(request.ZipArchivePath))
        {
            throw new FileNotFoundException("ZIP archive not found.", request.ZipArchivePath);
        }

        if (string.IsNullOrWhiteSpace(request.OptionName))
        {
            throw new ArgumentException("Folder name inside ZIP cannot be empty.", nameof(request.OptionName));
        }

        if (string.IsNullOrWhiteSpace(request.FirefoxBinaryPath) || !File.Exists(request.FirefoxBinaryPath))
        {
            throw new FileNotFoundException("Firefox binary path is invalid.", request.FirefoxBinaryPath);
        }

        var profileRoot = CreateProfileRoot();
        Directory.CreateDirectory(profileRoot);

        IPlaywright? playwright = null;
        IBrowserContext? context = null;

        try
        {
            Report(progress, ZipLaunchStatus.Info($"Preparing profile '{request.OptionName}'..."));
            await Task.Run(() => ExtractZipFolder(request.ZipArchivePath, request.OptionName, profileRoot), cancellationToken)
                .ConfigureAwait(false);

            Report(progress, ZipLaunchStatus.Info($"Launching Firefox for '{request.OptionName}'..."));
            (playwright, context) = await LaunchFirefoxPersistentContextAsync(
                profileRoot,
                request.FirefoxBinaryPath,
                request.Settings!,
                progress,
                cancellationToken).ConfigureAwait(false);

            await TryNavigateAsync(context, request.TargetUrl, request.OptionName, progress).ConfigureAwait(false);

            var cookiesPath = Path.Combine(profileRoot, "cookies.sqlite");
            if (!File.Exists(cookiesPath))
            {
                Report(progress, ZipLaunchStatus.Warning($"Launched '{request.OptionName}' but cookies.sqlite was not found."));
            }
            else if (string.IsNullOrWhiteSpace(request.TargetUrl))
            {
                Report(progress, ZipLaunchStatus.Success($"Launched Firefox profile '{request.OptionName}'."));
            }

            var profile = new LaunchedZipProfile(playwright, context, profileRoot, request.OptionName);
            playwright = null;
            context = null;

            return profile;
        }
        catch
        {
            await DisposeContextAsync(context).ConfigureAwait(false);
            playwright?.Dispose();
            DeleteDirectorySafe(profileRoot);
            throw;
        }
    }

    private static async Task<(IPlaywright Playwright, IBrowserContext Context)> LaunchFirefoxPersistentContextAsync(
        string profileRoot,
        string firefoxBinaryPath,
        PlaywrightSettings settings,
        IProgress<ZipLaunchStatus>? progress,
        CancellationToken cancellationToken)
    {
        var playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        var timeoutMs = settings.TimeoutMilliseconds <= 0 ? 60000 : settings.TimeoutMilliseconds;

        try
        {
            var sanitizedArgs = new List<string>(settings.ExtraArgs ?? Array.Empty<string>());
            PlaywrightLaunchConfigurator.EnsureSandboxFlags(sanitizedArgs);

            var launchOptions = new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = settings.Headless,
                ExecutablePath = firefoxBinaryPath,
                Timeout = timeoutMs,
                Args = sanitizedArgs.ToArray(),
                IgnoreHTTPSErrors = settings.IgnoreHTTPSErrors,
                AcceptDownloads = false,
                JavaScriptEnabled = true
            };

            PlaywrightLaunchConfigurator.ApplyFirefoxSafeDefaults(launchOptions);

            Report(progress, ZipLaunchStatus.Info($"Launching Firefox with timeout: {timeoutMs}ms..."));

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

            var launchTask = playwright.Firefox.LaunchPersistentContextAsync(profileRoot, launchOptions);
            var completedTask = await Task.WhenAny(launchTask, Task.Delay(Timeout.Infinite, timeoutCts.Token)).ConfigureAwait(false);

            if (completedTask != launchTask)
            {
                throw CreateTimeoutException(timeoutMs);
            }

            timeoutCts.Cancel();
            var context = await launchTask.ConfigureAwait(false);
            Report(progress, ZipLaunchStatus.Success("Firefox context created successfully!"));
            return (playwright, context);
        }
        catch (TimeoutException)
        {
            playwright.Dispose();
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            playwright.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            playwright.Dispose();
            throw new Exception(
                $"Failed to launch Firefox persistent context: {ex.Message}\n" +
                $"Timeout: {timeoutMs}ms\n" +
                $"Profile: {profileRoot}\n" +
                $"Binary: {firefoxBinaryPath}\n" +
                $"Headless: {settings.Headless}\n" +
                "Suggested solutions:\n" +
                "1. Increase timeout in RL Settings > Playwright\n" +
                "2. Use system Firefox: C\\Program Files\\Mozilla Firefox\\firefox.exe\n" +
                "3. Use Chromium browser instead",
                ex);
        }
    }

    private static async Task<bool> TryNavigateAsync(
        IBrowserContext context,
        string? targetUrl,
        string optionName,
        IProgress<ZipLaunchStatus>? progress)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return false;
        }

        try
        {
            Report(progress, ZipLaunchStatus.Info($"Navigating to {targetUrl}..."));
            var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync().ConfigureAwait(false);
            await page.GotoAsync(targetUrl, new() { Timeout = 30000 }).ConfigureAwait(false);
            Report(progress, ZipLaunchStatus.Success($"Launched Firefox profile '{optionName}' and navigated to {targetUrl}."));
            return true;
        }
        catch (Exception ex)
        {
            Report(progress, ZipLaunchStatus.Warning($"Launched Firefox profile '{optionName}' but failed to navigate to {targetUrl}: {ex.Message}"));
            return false;
        }
    }

    private static void ExtractZipFolder(string archivePath, string folderName, string destination)
    {
        using var stream = File.OpenRead(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        var prefix = folderName.TrimEnd('/') + "/";
        Directory.CreateDirectory(destination);

        foreach (var entry in archive.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = normalized[prefix.Length..];
            if (string.IsNullOrEmpty(relative))
            {
                continue;
            }

            var targetPath = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));

            if (normalized.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    private static void DeleteDirectorySafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, true);
        }
        catch
        {
            // Ignored
        }
    }

    private static async Task DisposeContextAsync(IBrowserContext? context)
    {
        if (context == null)
        {
            return;
        }

        try
        {
            await context.CloseAsync().ConfigureAwait(false);
        }
        catch
        {
            // Ignored
        }
    }

    private static string CreateProfileRoot() =>
        Path.Combine(Path.GetTempPath(), "ob2-zip-profile", Guid.NewGuid().ToString("N"));

    private static TimeoutException CreateTimeoutException(int timeoutMs) =>
        new($"Firefox launch timed out after {timeoutMs}ms. Try increasing timeout in RL Settings > Playwright > Timeout or use a system-installed Firefox browser instead.");

    private static void Report(IProgress<ZipLaunchStatus>? progress, ZipLaunchStatus status)
        => progress?.Report(status);
}

public sealed record ZipProfileLaunchRequest(
    string ZipArchivePath,
    string OptionName,
    PlaywrightSettings Settings,
    string FirefoxBinaryPath,
    string? TargetUrl);

public enum ZipLaunchStatusLevel
{
    Info,
    Success,
    Warning,
    Error
}

public readonly record struct ZipLaunchStatus(ZipLaunchStatusLevel Level, string Message)
{
    public static ZipLaunchStatus Info(string message) => new(ZipLaunchStatusLevel.Info, message);
    public static ZipLaunchStatus Success(string message) => new(ZipLaunchStatusLevel.Success, message);
    public static ZipLaunchStatus Warning(string message) => new(ZipLaunchStatusLevel.Warning, message);
    public static ZipLaunchStatus Error(string message) => new(ZipLaunchStatusLevel.Error, message);
}

public sealed class LaunchedZipProfile
{
    public LaunchedZipProfile(IPlaywright playwright, IBrowserContext context, string profilePath, string optionName)
    {
        Playwright = playwright ?? throw new ArgumentNullException(nameof(playwright));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        ProfilePath = profilePath;
        OptionName = optionName;
    }

    public IPlaywright Playwright { get; }
    public IBrowserContext Context { get; }
    public string ProfilePath { get; }
    public string OptionName { get; }
}
