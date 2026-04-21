using Microsoft.Playwright;
using RuriLib.Helpers.Playwright;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using RuriLib.Models.Settings;
using RuriLib.Providers.Playwright;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Playwright.Browser
{
    public static partial class Methods
    {
        private static string? GetExecutablePath(PlaywrightBrowserType browserType, BotData data, bool useBuildPath)
        {
            var provider = data.Providers.PlaywrightBrowser;
            var configuredPath = browserType switch
            {
                PlaywrightBrowserType.Chromium => provider.ChromiumBinaryLocation,
                PlaywrightBrowserType.Firefox => provider.FirefoxBinaryLocation,
                PlaywrightBrowserType.Webkit => provider.WebkitBinaryLocation,
                _ => null
            };

            return TryResolveExecutableOverride(configuredPath, browserType, data, useBuildPath);
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

        private static string? TryResolveExecutableOverride(string? configuredPath, PlaywrightBrowserType browserType, BotData data, bool useBuildPath)
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

            data.Logger.Log($"Configured {browserType} executable not found at '{expandedPath}'. Falling back to Playwright managed runtime at '{PlaywrightRuntimeService.GetRuntimePath(useBuildPath)}'.", LogColors.Yellow);
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
                    InstallSingleAddon(addonPath, extensionsDir, data);
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
                        InstallSingleAddon(xpiFile, extensionsDir, data);
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

        private static void InstallSingleAddon(string xpiPath, string extensionsDir, BotData data)
        {
            try
            {
                var id = GetIdFromManifest(xpiPath);
                string properFileName;

                if (!string.IsNullOrEmpty(id))
                {
                    properFileName = $"{id}.xpi";
                    data.Logger.Log($"Extracted ID from manifest: {id}", LogColors.MediumPurple);
                }
                else
                {
                    var originalFileName = Path.GetFileName(xpiPath);
                    properFileName = GenerateProperAddonFileName(originalFileName);
                    data.Logger.Log($"⚠️ Could not extract ID from manifest, using generated name: {properFileName}", LogColors.Orange);
                }

                var destinationPath = Path.Combine(extensionsDir, properFileName);
                File.Copy(xpiPath, destinationPath, true);
                data.Logger.Log($"Installed Firefox addon: {properFileName}", LogColors.Green);
            }
            catch (Exception ex)
            {
                data.Logger.Log($"❌ Failed to process addon {Path.GetFileName(xpiPath)}: {ex.Message}", LogColors.Red);
            }
        }

        private static string? GetIdFromManifest(string xpiPath)
        {
            try
            {
                using var stream = File.OpenRead(xpiPath);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                var manifestEntry = archive.GetEntry("manifest.json");
                
                if (manifestEntry == null) return null;

                using var entryStream = manifestEntry.Open();
                using var reader = new StreamReader(entryStream);
                var content = reader.ReadToEnd();
                var json = JObject.Parse(content);

                // Check browser_specific_settings.gecko.id (Manifest V2/V3 standard)
                var id = json.SelectToken("browser_specific_settings.gecko.id")?.ToString();
                
                // Check applications.gecko.id (Legacy Manifest V2)
                if (string.IsNullOrEmpty(id))
                {
                    id = json.SelectToken("applications.gecko.id")?.ToString();
                }

                return id;
            }
            catch
            {
                return null;
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
            bool ignoreHttpsErrors,
            Dictionary<string, object>? firefoxUserPrefs = null)
        {
            var options = new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = headless,
                Args = args,
                Timeout = timeout,
                ExecutablePath = executablePath,
                IgnoreHTTPSErrors = ignoreHttpsErrors,
                FirefoxUserPrefs = firefoxUserPrefs
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
            public Dictionary<string, object> FirefoxUserPrefs { get; set; } = new Dictionary<string, object>();
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

            // Fix for issue where Playwright looks for extension in the browser folder instead of build folder
            var resolvedPath = config.ExtensionPath;
            if (!Path.IsPathRooted(resolvedPath))
            {
                resolvedPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, resolvedPath));
            }

            if (config.BrowserType == PlaywrightBrowserType.Chromium)
            {
                args.Add($"--disable-extensions-except={resolvedPath}");
                args.Add($"--load-extension={resolvedPath}");
                data.Logger.Log($"Loading Chromium extension from: {resolvedPath}", LogColors.MediumPurple);
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
                    data.PlaywrightSession.TempFirefoxProfile = profilePath;
                    data.Logger.Log($"📁 Created temporary Firefox profile for addon installation: {profilePath}", LogColors.Yellow);
                }
            }

            // Create dedicated profile for visible Firefox mode
            if (config.BrowserType == PlaywrightBrowserType.Firefox && string.IsNullOrEmpty(profilePath) && !config.Headless)
            {
                profilePath = Path.Combine(Path.GetTempPath(), "firefox_visible_profile_" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(profilePath);
                data.PlaywrightSession.TempFirefoxProfile = profilePath;
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

            // force prefs to allow unsigned extensions
            if (config.FirefoxUserPrefs == null)
            {
                config.FirefoxUserPrefs = new Dictionary<string, object>();
            }
            
            config.FirefoxUserPrefs["xpinstall.signatures.required"] = false;
            config.FirefoxUserPrefs["extensions.autoDisableScopes"] = 0;

            var persistentOptions = CreatePersistentContextOptions(
                config.BrowserType, config.Headless, config.ExtraArgs, config.Timeout,
                config.ExecutablePath, config.IgnoreHttpsErrors, config.FirefoxUserPrefs);

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
                    null, config.IgnoreHttpsErrors, config.FirefoxUserPrefs);

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
            data.PlaywrightSession.TempChromiumUserData = tempUserDataDir;

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
        /// For Chromium, also injects stealth scripts to hide navigator.webdriver and DevTools detection.
        /// </summary>
        internal static async Task<IPage> SetupBrowserPageAsync(
            IBrowser? browser,
            IBrowserContext? context,
            BrowserLaunchConfig config,
            BotData data)
        {
            if (browser != null)
            {
                PlaywrightHelpers.SetBrowser(data, browser);
                data.Logger.Log($"Opened {config.BrowserType} browser (headless: {config.Headless})", LogColors.MediumPurple);

                var contextOptions = CreateContextOptions(config.Headless, config.IgnoreHttpsErrors);
                var freshContext = await browser.NewContextAsync(contextOptions);
                PlaywrightHelpers.SetContext(data, freshContext);

                // Inject stealth init script for Chromium
                if (config.BrowserType == PlaywrightBrowserType.Chromium)
                {
                    await InjectChromiumStealthScriptAsync(freshContext, data);
                }

                var page = await freshContext.NewPageAsync();
                PlaywrightHelpers.SetPage(data, page);

                // Apply CDP-level stealth (debugger skip) on the initial page
                if (config.BrowserType == PlaywrightBrowserType.Chromium)
                {
                    await ApplyChromiumCdpStealthAsync(page, data);
                }

                data.Logger.Log("Created new browser context and page", LogColors.MediumPurple);
                return page;
            }

            if (context != null)
            {
                // Store both the context AND the browser reference for persistent contexts
                // This is critical for operations like Switch to Page and Get Pages that need the browser
                PlaywrightHelpers.SetContext(data, context);
                if (context.Browser != null)
                {
                    PlaywrightHelpers.SetBrowser(data, context.Browser);
                }
                data.Logger.Log($"Opened {config.BrowserType} browser with persistent context (headless: {config.Headless})", LogColors.MediumPurple);

                // Inject stealth init script for Chromium persistent contexts
                if (config.BrowserType == PlaywrightBrowserType.Chromium)
                {
                    await InjectChromiumStealthScriptAsync(context, data);
                }

                var existingPages = context.Pages.Where(p => !p.IsClosed).ToList();
                if (config.BrowserType == PlaywrightBrowserType.Chromium)
                {
                    existingPages = await CloseChromiumRuntimeDirectoryPagesAsync(context, existingPages, data).ConfigureAwait(false);
                }

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

                PlaywrightHelpers.SetPage(data, page);

                // Apply CDP-level stealth (debugger skip) on the initial page
                if (config.BrowserType == PlaywrightBrowserType.Chromium)
                {
                    await ApplyChromiumCdpStealthAsync(page, data);
                }

                return page;
            }

            throw new Exception("Neither browser nor context was successfully created.");
        }

        private static async Task<List<IPage>> CloseChromiumRuntimeDirectoryPagesAsync(
            IBrowserContext context,
            List<IPage> pages,
            BotData data)
        {
            if (pages.Count == 0)
            {
                return pages;
            }

            var runtimePages = pages
                .Where(p => IsManagedChromiumRuntimeDirectoryPage(p.Url))
                .ToList();

            if (runtimePages.Count == 0)
            {
                return pages;
            }

            foreach (var runtimePage in runtimePages)
            {
                try
                {
                    await runtimePage.CloseAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    data.Logger.Log($"Failed to close Chromium runtime tab '{runtimePage.Url}': {ex.Message}", LogColors.Orange);
                }
            }

            data.Logger.Log($"Closed {runtimePages.Count} stray Chromium runtime tab(s) opened at startup", LogColors.Yellow);

            return context.Pages
                .Where(p => !p.IsClosed)
                .ToList();
        }

        private static bool IsManagedChromiumRuntimeDirectoryPage(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.IsFile)
            {
                return false;
            }

            var localPath = uri.LocalPath;
            if (string.IsNullOrWhiteSpace(localPath) || !Directory.Exists(localPath))
            {
                return false;
            }

            var normalizedPath = Path.GetFullPath(localPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var directoryName = Path.GetFileName(normalizedPath);
            if (!directoryName.Equals("chrome-win", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var parentDirectory = Path.GetDirectoryName(normalizedPath);
            if (string.IsNullOrWhiteSpace(parentDirectory))
            {
                return false;
            }

            var parentName = Path.GetFileName(parentDirectory);
            return parentName.StartsWith("chromium-", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Injects JavaScript that hides navigator.webdriver and DevTools detection heuristics.
        /// Applied via AddInitScriptAsync so it runs before any page script on every navigation.
        /// </summary>
        private static async Task InjectChromiumStealthScriptAsync(IBrowserContext context, BotData data)
        {
            const string stealthScript = @"() => {
                // === navigator.webdriver ===
                try {
                    Object.defineProperty(navigator, 'webdriver', {
                        get: () => undefined,
                        configurable: true
                    });
                } catch(e) {}

                try {
                    Object.defineProperty(Object.getPrototypeOf(navigator), 'webdriver', {
                        get: () => undefined,
                        configurable: true
                    });
                } catch(e) {}

                // === Permissions API ===
                try {
                    const origQuery = window.navigator.permissions.query;
                    window.navigator.permissions.query = (parameters) => (
                        parameters.name === 'notifications' ?
                            Promise.resolve({ state: Notification.permission }) :
                            origQuery(parameters)
                    );
                } catch(e) {}

                // === Chrome runtime ===
                try {
                    if (!window.chrome) window.chrome = {};
                    if (!window.chrome.runtime) {
                        // Minimal but realistic surface: real Chrome has connect/sendMessage.
                        // An empty {} is trivially detectable via Object.keys(chrome.runtime).length === 0.
                        window.chrome.runtime = {
                            connect: function() {
                                return { onDisconnect: { addListener: function(){} }, onMessage: { addListener: function(){} }, postMessage: function(){}, disconnect: function(){} };
                            },
                            sendMessage: function() {},
                            id: undefined
                        };
                    }
                } catch(e) {}

                // === Plugins/languages ===
                try {
                    // Build a realistic PluginArray mimic with standard Chrome PDF plugins.
                    // The previous [1,2,3,4,5] fake was trivially detectable (numbers instead of Plugin objects,
                    // missing .item()/.namedItem(), wrong typeof).
                    var _pluginData = [
                        { name: 'PDF Viewer', description: 'Portable Document Format', filename: 'internal-pdf-viewer' },
                        { name: 'Chrome PDF Viewer', description: 'Portable Document Format', filename: 'internal-pdf-viewer' },
                        { name: 'Chromium PDF Viewer', description: 'Portable Document Format', filename: 'internal-pdf-viewer' },
                        { name: 'Microsoft Edge PDF Viewer', description: 'Portable Document Format', filename: 'internal-pdf-viewer' },
                        { name: 'WebKit built-in PDF', description: 'Portable Document Format', filename: 'internal-pdf-viewer' }
                    ];
                    var _plugins = [];
                    for (var pi = 0; pi < _pluginData.length; pi++) {
                        var pd = _pluginData[pi];
                        var mt = { type: 'application/pdf', suffixes: 'pdf', description: pd.description };
                        var pl = Object.create(null);
                        pl.name = pd.name;
                        pl.filename = pd.filename;
                        pl.description = pd.description;
                        pl.length = 1;
                        pl[0] = mt;
                        pl.item = function(idx) { return this[idx]; };
                        pl.namedItem = function() { return null; };
                        _plugins.push(pl);
                    }
                    var _pluginArray = Object.create(null);
                    _pluginArray.length = _plugins.length;
                    _pluginArray.item = function(idx) { return _plugins[idx]; };
                    _pluginArray.namedItem = function(name) {
                        for (var ni = 0; ni < _plugins.length; ni++) { if (_plugins[ni].name === name) return _plugins[ni]; }
                        return null;
                    };
                    _pluginArray.refresh = function() {};
                    for (var pj = 0; pj < _plugins.length; pj++) { _pluginArray[pj] = _plugins[pj]; }
                    Object.defineProperty(navigator, 'plugins', {
                        get: () => _pluginArray,
                        configurable: true
                    });
                    Object.defineProperty(navigator, 'languages', {
                        get: () => ['en-US', 'en'],
                        configurable: true
                    });
                } catch(e) {}

                // === DevTools detection: outerWidth/Height differential ===
                // Detectors check outerHeight - innerHeight: if too large, DevTools is open.
                // Real Chrome without DevTools: outerHeight - innerHeight ≈ 85px (title + tabs + address bar).
                // Previous formula (innerHeight + screenY) was wrong — screenY is viewport position, not chrome height.
                try {
                    Object.defineProperty(window, 'outerWidth', {
                        get: () => window.innerWidth,
                        configurable: true
                    });
                    Object.defineProperty(window, 'outerHeight', {
                        get: () => window.innerHeight + 85,
                        configurable: true
                    });
                } catch(e) {}

                // === DevTools detection: console getter/log timing ===
                // Detectors create objects with toString/valueOf that fire when console logs them.
                // We strip getter side-effects from console methods.
                try {
                    var _patchedFns = [];
                    var origLog = console.log;
                    var origTable = console.table;
                    var origDir = console.dir;
                    var origDebug = console.debug;
                    var origInfo = console.info;
                    var origWarn = console.warn;
                    var origError = console.error;
                    var origTrace = console.trace;

                    // Wrap each console method to stringify args first (triggers toString eagerly)
                    // but swallow any errors from getter traps. Track names for toString protection.
                    function safeWrap(origFn, name) {
                        var wrapped = function() {
                            try {
                                for (var i = 0; i < arguments.length; i++) {
                                    try { String(arguments[i]); } catch(e) {}
                                }
                                origFn.apply(console, arguments);
                            } catch(e) {}
                        };
                        _patchedFns.push({ ref: wrapped, name: name });
                        return wrapped;
                    }
                    console.log = safeWrap(origLog, 'log');
                    console.table = safeWrap(origTable, 'table');
                    console.dir = safeWrap(origDir, 'dir');
                    console.debug = safeWrap(origDebug, 'debug');
                    console.info = safeWrap(origInfo, 'info');
                    console.warn = safeWrap(origWarn, 'warn');
                    console.error = safeWrap(origError, 'error');
                    console.trace = safeWrap(origTrace, 'trace');
                } catch(e) {}

                // === DevTools detection: toString on native functions ===
                // Some detectors check if a function's toString contains [native code].
                // Must also cover the console methods wrapped above — without this,
                // console.log.toString() would leak wrapper source code.
                try {
                    var origToString = Function.prototype.toString;
                    var nativeToStringFunctionString = origToString.call(origToString);
                    Function.prototype.toString = function() {
                        if (this === Function.prototype.toString) {
                            return nativeToStringFunctionString;
                        }
                        // Return native-looking string for patched console methods
                        if (typeof _patchedFns !== 'undefined') {
                            for (var i = 0; i < _patchedFns.length; i++) {
                                if (this === _patchedFns[i].ref) {
                                    return 'function ' + _patchedFns[i].name + '() { [native code] }';
                                }
                            }
                        }
                        return origToString.call(this);
                    };
                } catch(e) {}

                // === CDP bindings ===
                try { delete window.__cdp_bindings__; } catch(e) {}
                try { delete window._cdp; } catch(e) {}
            }";

            try
            {
                await context.AddInitScriptAsync(stealthScript).ConfigureAwait(false);
                data.Logger.Log("Injected Chromium stealth script (navigator.webdriver + DevTools evasion)", LogColors.MediumPurple);
            }
            catch (Exception ex)
            {
                data.Logger.Log($"⚠️ Could not inject stealth script: {ex.Message}", LogColors.Orange);
            }
        }

        /// <summary>
        /// Uses CDP to disable debugger pauses on a page.
        /// This is the primary defense against DevTools detection -- makes debugger statements
        /// into no-ops so timing-based detection cannot work.
        /// Must be called per-page since CDP sessions are page-scoped.
        /// </summary>
        internal static async Task ApplyChromiumCdpStealthAsync(IPage page, BotData data)
        {
            try
            {
                var session = await page.Context.NewCDPSessionAsync(page).ConfigureAwait(false);

                // Make all debugger statements into no-ops (no pause, no timing spike)
                await session.SendAsync("Debugger.enable").ConfigureAwait(false);
                await session.SendAsync("Debugger.setSkipAllPauses", new Dictionary<string, object>
                {
                    ["skip"] = true
                }).ConfigureAwait(false);

                // Disable CDP Runtime domain leaking (some detectors check for CDP artifacts)
                // We don't need Runtime events for stealth purposes
                try
                {
                    await session.SendAsync("Runtime.disable").ConfigureAwait(false);
                }
                catch { }

                data.Logger.Log("Applied CDP stealth (debugger skip enabled)", LogColors.MediumPurple);
            }
            catch (Exception ex)
            {
                data.Logger.Log($"⚠️ Could not apply CDP stealth: {ex.Message}", LogColors.Orange);
            }
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
