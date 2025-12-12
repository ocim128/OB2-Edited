using Microsoft.Playwright;
using RuriLib.Helpers.Playwright;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using RuriLib.Models.Settings;
using RuriLib.Providers.Playwright;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Playwright.Browser
{
    public static partial class Methods
    {
        private static string? GetExecutablePath(PlaywrightBrowserType browserType, BotData data)
        {
            var provider = data.Providers.PlaywrightBrowser;
            var configuredPath = browserType switch
            {
                PlaywrightBrowserType.Chromium => provider.ChromiumBinaryLocation,
                PlaywrightBrowserType.Firefox => provider.FirefoxBinaryLocation,
                PlaywrightBrowserType.Webkit => provider.WebkitBinaryLocation,
                _ => null
            };

            return TryResolveExecutableOverride(configuredPath, browserType, data);
        }

        private static PlaywrightBrowserType ValidateBrowserType(PlaywrightBrowserType browserType, string? executablePath, BotData data)
        {
            if (browserType == PlaywrightBrowserType.Webkit && !string.IsNullOrEmpty(executablePath))
            {
                var executableName = Path.GetFileNameWithoutExtension(executablePath).ToLowerInvariant();
                if (executableName.Contains("firefox") || executableName.Contains("camoufox") || executableName.Contains("librewolf"))
                {
                    data.Logger.Log($"⚠️ Detected Firefox-based browser ({executableName}) configured as Webkit. Switching to Firefox browser type.", LogColors.Orange);
                    return PlaywrightBrowserType.Firefox;
                }
            }

            return browserType;
        }

        private static async Task<IBrowser> LaunchBrowserWithRetry(IPlaywright playwright, PlaywrightBrowserType browserType, BrowserTypeLaunchOptions options, BotData data)
        {
            return browserType switch
            {
                PlaywrightBrowserType.Chromium => await playwright.Chromium.LaunchAsync(options).ConfigureAwait(false),
                PlaywrightBrowserType.Firefox => await playwright.Firefox.LaunchAsync(options).ConfigureAwait(false),
                PlaywrightBrowserType.Webkit => await playwright.Webkit.LaunchAsync(options).ConfigureAwait(false),
                _ => throw new ArgumentException($"Unsupported browser type: {browserType}")
            };
        }

        private static string? TryResolveExecutableOverride(string? configuredPath, PlaywrightBrowserType browserType, BotData data)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return null;
            }

            var expandedPath = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
            if (!Path.IsPathRooted(expandedPath))
            {
                expandedPath = Path.GetFullPath(expandedPath);
            }

            if (File.Exists(expandedPath))
            {
                return expandedPath;
            }

            if (Directory.Exists(expandedPath))
            {
                var executableCandidates = GetBrowserExecutableCandidates(browserType);
                foreach (var candidate in executableCandidates)
                {
                    var match = Directory.EnumerateFiles(expandedPath, candidate, SearchOption.AllDirectories).FirstOrDefault();
                    if (match != null)
                    {
                        data.Logger.Log($"Resolved {browserType} executable to '{match}'", LogColors.MediumPurple);
                        return match;
                    }
                }
            }

            data.Logger.Log($"Configured {browserType} executable not found at '{expandedPath}'. Falling back to Playwright managed runtime at '{PlaywrightRuntimeService.ActiveRuntimePath}'.", LogColors.Yellow);
            return null;
        }

        private static IEnumerable<string> GetBrowserExecutableCandidates(PlaywrightBrowserType browserType) =>
            browserType switch
            {
                PlaywrightBrowserType.Chromium => new[] { "chrome.exe", "msedge.exe", "chromium.exe" },
                PlaywrightBrowserType.Firefox => new[] { "firefox.exe", "librewolf.exe", "waterfox.exe" },
                PlaywrightBrowserType.Webkit => new[] { "Playwright.exe" },
                _ => Array.Empty<string>()
            };

        private static void HandleBrowserLaunchError(Exception ex, PlaywrightBrowserType browserType, string? executablePath, BotData data)
        {
            data.Logger.Log("❌ Browser launch failed with error:", LogColors.Red);
            data.Logger.Log($"   Exception Type: {ex.GetType().Name}", LogColors.Red);
            data.Logger.Log($"   Message: {ex.Message}", LogColors.Red);
            if (ex.InnerException != null)
            {
                data.Logger.Log($"   Inner Exception: {ex.InnerException.Message}", LogColors.Red);
            }

            data.Logger.Log("💡 Troubleshooting tips:", LogColors.Yellow);
            data.Logger.Log("   - Verify browser executable path is correct", LogColors.Yellow);
            data.Logger.Log("   - Check if browser supports automation", LogColors.Yellow);
            data.Logger.Log("   - Try using Playwright's built-in browsers", LogColors.Yellow);
        }

        private static void InstallFirefoxAddon(string profilePath, string addonPath, BotData data)
        {
            try
            {
                var extensionsDir = Path.Combine(profilePath, "extensions");
                if (!Directory.Exists(extensionsDir))
                {
                    Directory.CreateDirectory(extensionsDir);
                    data.Logger.Log($"Created extensions directory: {extensionsDir}", LogColors.MediumPurple);
                }

                if (File.Exists(addonPath) && Path.GetExtension(addonPath).Equals(".xpi", StringComparison.OrdinalIgnoreCase))
                {
                    var originalFileName = Path.GetFileName(addonPath);
                    var properFileName = GenerateProperAddonFileName(originalFileName);
                    var destinationPath = Path.Combine(extensionsDir, properFileName);
                    File.Copy(addonPath, destinationPath, true);

                    if (!originalFileName.Equals(properFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        data.Logger.Log($"Renamed addon from '{originalFileName}' to '{properFileName}' for proper Firefox recognition", LogColors.MediumPurple);
                    }

                    data.Logger.Log($"Installed Firefox addon: {properFileName}", LogColors.Green);
                }
                else if (Directory.Exists(addonPath))
                {
                    var xpiFiles = Directory.GetFiles(addonPath, "*.xpi", SearchOption.TopDirectoryOnly);
                    if (xpiFiles.Length == 0)
                    {
                        data.Logger.Log($"⚠️ No .xpi files found in directory: {addonPath}", LogColors.Orange);
                        return;
                    }

                    foreach (var xpiFile in xpiFiles)
                    {
                        var originalFileName = Path.GetFileName(xpiFile);
                        var properFileName = GenerateProperAddonFileName(originalFileName);
                        var destinationPath = Path.Combine(extensionsDir, properFileName);
                        File.Copy(xpiFile, destinationPath, true);

                        if (!originalFileName.Equals(properFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            data.Logger.Log($"Renamed addon from '{originalFileName}' to '{properFileName}' for proper Firefox recognition", LogColors.MediumPurple);
                        }

                        data.Logger.Log($"Installed Firefox addon: {properFileName}", LogColors.Green);
                    }
                }
                else
                {
                    data.Logger.Log($"⚠️ Firefox addon path not found or invalid: {addonPath}", LogColors.Orange);
                }
            }
            catch (Exception ex)
            {
                data.Logger.Log($"❌ Failed to install Firefox addon: {ex.Message}", LogColors.Red);
            }
        }

        private static string GenerateProperAddonFileName(string originalFileName)
        {
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(originalFileName);
            if (nameWithoutExtension.StartsWith("addon@", StringComparison.OrdinalIgnoreCase) && nameWithoutExtension.Contains('.'))
            {
                return originalFileName;
            }

            var cleanName = nameWithoutExtension
                .ToLowerInvariant()
                .Replace(" ", string.Empty)
                .Replace("-", string.Empty)
                .Replace("_", string.Empty);

            if (string.IsNullOrEmpty(cleanName))
            {
                cleanName = "extension";
            }

            return $"addon@{cleanName}.com.xpi";
        }

        /// <summary>
        /// Creates configured BrowserTypeLaunchOptions with browser-specific defaults applied.
        /// Firefox safe defaults (GPU disabled, sandbox relaxed) are applied automatically.
        /// </summary>
        private static BrowserTypeLaunchOptions CreateLaunchOptions(
            PlaywrightBrowserType browserType,
            bool headless,
            string[] args,
            int timeout,
            string? executablePath)
        {
            var options = new BrowserTypeLaunchOptions
            {
                Headless = headless,
                Args = args,
                Timeout = timeout,
                ExecutablePath = executablePath
            };

            if (browserType == PlaywrightBrowserType.Firefox)
            {
                PlaywrightLaunchConfigurator.ApplyFirefoxSafeDefaults(options);
            }

            return options;
        }

        /// <summary>
        /// Creates configured BrowserTypeLaunchPersistentContextOptions with browser-specific defaults applied.
        /// Firefox safe defaults (GPU disabled, sandbox relaxed) are applied automatically.
        /// </summary>
        private static BrowserTypeLaunchPersistentContextOptions CreatePersistentContextOptions(
            PlaywrightBrowserType browserType,
            bool headless,
            string[] args,
            int timeout,
            string? executablePath,
            bool ignoreHttpsErrors)
        {
            var options = new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = headless,
                Args = args,
                Timeout = timeout,
                ExecutablePath = executablePath,
                IgnoreHTTPSErrors = ignoreHttpsErrors
            };

            if (!headless)
            {
                options.ViewportSize = ViewportSize.NoViewport;
            }

            if (browserType == PlaywrightBrowserType.Firefox)
            {
                PlaywrightLaunchConfigurator.ApplyFirefoxSafeDefaults(options);
            }

            return options;
        }

        #region Browser Launch Configuration Helpers

        /// <summary>
        /// Configuration context passed between browser launch helper methods.
        /// </summary>
        internal sealed class BrowserLaunchConfig
        {
            public PlaywrightBrowserType BrowserType { get; set; }
            public bool Headless { get; set; }
            public string[] ExtraArgs { get; set; } = Array.Empty<string>();
            public string? FirefoxProfilePath { get; set; }
            public string? ExtensionPath { get; set; }
            public string? FirefoxAddonPath { get; set; }
            public string? ExecutablePath { get; set; }
            public int Timeout { get; set; }
            public bool IgnoreHttpsErrors { get; set; }
            public Dictionary<int, string>? FirefoxProcessesBeforeLaunch { get; set; }
        }

        /// <summary>
        /// Configures Chromium extension loading arguments.
        /// </summary>
        internal static void ConfigureChromiumExtension(BrowserLaunchConfig config, List<string> args, BotData data)
        {
            if (string.IsNullOrEmpty(config.ExtensionPath))
            {
                return;
            }

            if (config.BrowserType == PlaywrightBrowserType.Chromium)
            {
                args.Add($"--disable-extensions-except={config.ExtensionPath}");
                args.Add($"--load-extension={config.ExtensionPath}");
                data.Logger.Log($"Loading Chromium extension from: {config.ExtensionPath}", LogColors.MediumPurple);
            }
            else
            {
                data.Logger.Log($"⚠️ Extension path specified but browser type is {config.BrowserType}. Extensions are only supported with Chromium browsers.", LogColors.Orange);
            }
        }

        /// <summary>
        /// Configures Firefox profile and addon settings.
        /// Returns the resolved Firefox profile path (may create a temporary profile).
        /// </summary>
        internal static string? ConfigureFirefoxProfile(BrowserLaunchConfig config, BotData data)
        {
            var profilePath = config.FirefoxProfilePath;

            // Handle Firefox addon path validation
            if (!string.IsNullOrEmpty(config.FirefoxAddonPath))
            {
                if (config.BrowserType != PlaywrightBrowserType.Firefox)
                {
                    data.Logger.Log($"⚠️ Firefox addon path specified but browser type is {config.BrowserType}. Firefox addons are only supported with Firefox browsers.", LogColors.Orange);
                }
                else if (string.IsNullOrEmpty(profilePath))
                {
                    // Create a temporary profile path for addon installation
                    profilePath = Path.Combine(Path.GetTempPath(), "firefox_temp_profile_" + Guid.NewGuid().ToString("N")[..8]);
                    Directory.CreateDirectory(profilePath);
                    data.SetObject(PlaywrightHelpers.Keys.TempFirefoxProfile, profilePath);
                    data.Logger.Log($"📁 Created temporary Firefox profile for addon installation: {profilePath}", LogColors.Yellow);
                }
            }

            // Create dedicated profile for visible Firefox mode
            if (config.BrowserType == PlaywrightBrowserType.Firefox && string.IsNullOrEmpty(profilePath) && !config.Headless)
            {
                profilePath = Path.Combine(Path.GetTempPath(), "firefox_visible_profile_" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(profilePath);
                data.SetObject(PlaywrightHelpers.Keys.TempFirefoxProfile, profilePath);
                data.Logger.Log($"Created dedicated Firefox profile for visible mode: {profilePath}", LogColors.MediumPurple);
            }

            return profilePath;
        }

        /// <summary>
        /// Launches browser with persistent context for Firefox profile.
        /// </summary>
        internal static async Task<IBrowserContext?> LaunchFirefoxWithProfileAsync(
            IPlaywright playwright,
            BrowserLaunchConfig config,
            BotData data)
        {
            if (config.BrowserType != PlaywrightBrowserType.Firefox || string.IsNullOrEmpty(config.FirefoxProfilePath))
            {
                return null;
            }

            data.Logger.Log($"Using Firefox profile: {config.FirefoxProfilePath}", LogColors.MediumPurple);

            // Handle Firefox addon installation
            if (!string.IsNullOrEmpty(config.FirefoxAddonPath))
            {
                InstallFirefoxAddon(config.FirefoxProfilePath, config.FirefoxAddonPath, data);
            }

            var persistentOptions = CreatePersistentContextOptions(
                config.BrowserType, config.Headless, config.ExtraArgs, config.Timeout,
                config.ExecutablePath, config.IgnoreHttpsErrors);

            try
            {
                return await playwright.Firefox.LaunchPersistentContextAsync(config.FirefoxProfilePath, persistentOptions);
            }
            catch (Exception ex) when (!string.IsNullOrEmpty(config.ExecutablePath))
            {
                data.Logger.Log($"❌ Custom Firefox with profile launch failed: {ex.GetType().Name} - {ex.Message}", LogColors.Red);
                data.Logger.Log($"🔄 Attempting fallback to Playwright's built-in Firefox with profile...", LogColors.Yellow);

                var fallbackOptions = CreatePersistentContextOptions(
                    config.BrowserType, config.Headless, config.ExtraArgs, config.Timeout,
                    null, config.IgnoreHttpsErrors);

                try
                {
                    var context = await playwright.Firefox.LaunchPersistentContextAsync(config.FirefoxProfilePath, fallbackOptions);
                    data.Logger.Log($"✅ Successfully launched Playwright's built-in Firefox with profile", LogColors.Green);
                    return context;
                }
                catch (Exception fallbackEx)
                {
                    LogFirefoxFallbackFailure(data, fallbackEx, withProfile: true);
                    throw new Exception($"Failed to launch Firefox browser with profile using both custom and built-in executables.", ex);
                }
            }
        }

        /// <summary>
        /// Launches browser with persistent context for Chromium extension.
        /// </summary>
        internal static async Task<IBrowserContext?> LaunchChromiumWithExtensionAsync(
            IPlaywright playwright,
            BrowserLaunchConfig config,
            BotData data)
        {
            if (config.BrowserType != PlaywrightBrowserType.Chromium || string.IsNullOrEmpty(config.ExtensionPath))
            {
                return null;
            }

            data.Logger.Log($"Using persistent context for Chromium extension: {config.ExtensionPath}", LogColors.MediumPurple);

            var tempUserDataDir = Path.Combine(Path.GetTempPath(), "playwright-chromium-" + Guid.NewGuid().ToString());
            data.SetObject(PlaywrightHelpers.Keys.TempChromiumUserData, tempUserDataDir);

            var persistentOptions = CreatePersistentContextOptions(
                config.BrowserType, config.Headless, config.ExtraArgs, config.Timeout,
                config.ExecutablePath, config.IgnoreHttpsErrors);

            return await playwright.Chromium.LaunchPersistentContextAsync(tempUserDataDir, persistentOptions);
        }

        /// <summary>
        /// Launches a regular browser (no persistent context).
        /// </summary>
        internal static async Task<IBrowser?> LaunchRegularBrowserAsync(
            IPlaywright playwright,
            BrowserLaunchConfig config,
            BotData data)
        {
            var launchOptions = CreateLaunchOptions(
                config.BrowserType, config.Headless, config.ExtraArgs, config.Timeout, config.ExecutablePath);

            try
            {
                return await LaunchBrowserWithRetry(playwright, config.BrowserType, launchOptions, data);
            }
            catch (Exception ex) when (config.BrowserType == PlaywrightBrowserType.Firefox && !string.IsNullOrEmpty(config.ExecutablePath))
            {
                data.Logger.Log($"❌ Custom Firefox launch failed: {ex.GetType().Name} - {ex.Message}", LogColors.Red);
                data.Logger.Log($"🔄 Attempting fallback to Playwright's built-in Firefox...", LogColors.Yellow);

                var fallbackOptions = CreateLaunchOptions(
                    config.BrowserType, config.Headless, config.ExtraArgs, config.Timeout, null);

                try
                {
                    var browser = await playwright.Firefox.LaunchAsync(fallbackOptions);
                    data.Logger.Log($"✅ Successfully launched Playwright's built-in Firefox", LogColors.Green);
                    return browser;
                }
                catch (Exception fallbackEx)
                {
                    LogFirefoxFallbackFailure(data, fallbackEx, withProfile: false);
                    throw new Exception($"Failed to launch Firefox browser with both custom and built-in executables.", ex);
                }
            }
        }

        /// <summary>
        /// Sets up the page after browser/context is created.
        /// </summary>
        internal static async Task<IPage> SetupBrowserPageAsync(
            IBrowser? browser,
            IBrowserContext? context,
            BrowserLaunchConfig config,
            BotData data)
        {
            if (browser != null)
            {
                data.SetObject(PlaywrightHelpers.Keys.Browser, browser);
                data.Logger.Log($"Opened {config.BrowserType} browser (headless: {config.Headless})", LogColors.MediumPurple);

                var contextOptions = CreateContextOptions(config.Headless, config.IgnoreHttpsErrors);
                var freshContext = await browser.NewContextAsync(contextOptions);
                data.SetObject(PlaywrightHelpers.Keys.Context, freshContext);

                var page = await freshContext.NewPageAsync();
                data.SetObject(PlaywrightHelpers.Keys.Page, page);
                data.Logger.Log("Created new browser context and page", LogColors.MediumPurple);
                return page;
            }

            if (context != null)
            {
                data.SetObject(PlaywrightHelpers.Keys.Context, context);
                data.Logger.Log($"Opened {config.BrowserType} browser with persistent context (headless: {config.Headless})", LogColors.MediumPurple);

                var existingPages = context.Pages;
                IPage page;

                if (existingPages.Count > 0)
                {
                    page = existingPages[0];
                    data.Logger.Log("Using existing page from persistent context", LogColors.MediumPurple);
                }
                else
                {
                    page = await context.NewPageAsync();
                    data.Logger.Log("Created new page from persistent context", LogColors.MediumPurple);
                }

                data.SetObject(PlaywrightHelpers.Keys.Page, page);
                return page;
            }

            throw new Exception("Neither browser nor context was successfully created.");
        }

        private static void LogFirefoxFallbackFailure(BotData data, Exception ex, bool withProfile)
        {
            var suffix = withProfile ? " with profile" : "";
            data.Logger.Log($"❌ Fallback also failed: {ex.GetType().Name} - {ex.Message}", LogColors.Red);
            data.Logger.Log($"💡 Both custom and built-in Firefox{suffix} failed. Consider:", LogColors.Yellow);
            data.Logger.Log($"   - Installing Playwright browsers: playwright install firefox", LogColors.Yellow);
            data.Logger.Log($"   - Using a different browser type (Chromium/Webkit)", LogColors.Yellow);
            if (withProfile)
            {
                data.Logger.Log($"   - Checking if the Firefox profile path is valid and accessible", LogColors.Yellow);
            }
        }

        private static BrowserNewContextOptions CreateContextOptions(bool headless, bool ignoreHttpsErrors)
        {
            var options = new BrowserNewContextOptions
            {
                IgnoreHTTPSErrors = ignoreHttpsErrors
            };

            if (!headless)
            {
                options.ViewportSize = ViewportSize.NoViewport;
            }

            return options;
        }

        #endregion
    }
}
