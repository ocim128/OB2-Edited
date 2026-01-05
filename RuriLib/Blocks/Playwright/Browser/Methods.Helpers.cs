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

            if (browserType == PlaywrightBrowserType.Chromium)
            {
                options.IgnoreDefaultArgs = new[] { "--enable-automation" };
            }

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

            if (browserType == PlaywrightBrowserType.Chromium)
            {
                options.IgnoreDefaultArgs = new[] { "--enable-automation" };
            }

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
                await ApplyStealthScriptsAsync(freshContext, config.BrowserType);
                data.SetObject(PlaywrightHelpers.Keys.Context, freshContext);

                var page = await freshContext.NewPageAsync();
                data.SetObject(PlaywrightHelpers.Keys.Page, page);
                data.Logger.Log("Created new browser context and page", LogColors.MediumPurple);
                return page;
            }

            if (context != null)
            {
                // Store both the context AND the browser reference for persistent contexts
                // This is critical for operations like Switch to Page and Get Pages that need the browser
                data.SetObject(PlaywrightHelpers.Keys.Context, context);
                await ApplyStealthScriptsAsync(context, config.BrowserType);
                if (context.Browser != null)
                {
                    data.SetObject(PlaywrightHelpers.Keys.Browser, context.Browser);
                }
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

        private static async Task ApplyStealthScriptsAsync(IBrowserContext context, PlaywrightBrowserType browserType)
        {
            if (browserType == PlaywrightBrowserType.Chromium)
            {
                await context.AddInitScriptAsync(@"
                    // --- Stealth Evasion Script (Comprehensive CDP & DevTools Bypass) ---

                    // 0. GLOBAL TOSTRING SPOOFING (Robust)
                    const mocks = new WeakMap();
                    
                    const originalFunctionToString = Function.prototype.toString;
                    const nativeToStringStr = 'function toString() { [native code] }';
                    
                    Function.prototype.toString = function() {
                        if (mocks.has(this)) {
                            return mocks.get(this);
                        }
                        return originalFunctionToString.apply(this, arguments);
                    };
                    mocks.set(Function.prototype.toString, nativeToStringStr);

                    const stealthify = (obj, prop, mockImpl, nativeString) => {
                        try {
                            const str = nativeString || `function ${prop}() { [native code] }`;
                            Object.defineProperty(mockImpl, 'name', { value: prop, configurable: true });
                            mocks.set(mockImpl, str);

                            Object.defineProperty(obj, prop, {
                                value: mockImpl,
                                configurable: true,
                                writable: true,
                                enumerable: true
                            });
                        } catch(e) {}
                    };

                    // ==============================================
                    // 1. CDP DETECTION EVASION (Primary Fix)
                    // ==============================================
                    
                    // 1a. Remove CDP-related artifacts from window
                    const cdpArtifacts = [
                        '__playwright',
                        '__playwright_routeHandler',
                        '__pw_manual_listeners',
                        '__PW_inspect',
                        'cdc_adoQpoasnfa76pfcZLmcfl_Array',
                        'cdc_adoQpoasnfa76pfcZLmcfl_Promise',
                        'cdc_adoQpoasnfa76pfcZLmcfl_Symbol'
                    ];
                    for (const art of cdpArtifacts) {
                        try { delete window[art]; } catch(e) {}
                    }

                    // 1b. Hook Error stack traces to remove CDP-related paths
                    const originalPrepareStackTrace = Error.prepareStackTrace;
                    Error.prepareStackTrace = function(error, stack) {
                        const filtered = stack.filter(frame => {
                            const fn = frame.getFunctionName() || '';
                            const file = frame.getFileName() || '';
                            // Filter out Playwright/CDP internal frames
                            return !fn.includes('__playwright') && 
                                   !file.includes('pptr:') && 
                                   !file.includes('playwright');
                        });
                        if (originalPrepareStackTrace) {
                            return originalPrepareStackTrace(error, filtered);
                        }
                        return filtered.map(f => f.toString()).join('\n');
                    };

                    // 1c. Mask Runtime.enable detection by hooking performance.getEntries
                    // CDP detection often checks for unusual resource timing entries
                    if (window.PerformanceObserver) {
                        const OriginalObserver = window.PerformanceObserver;
                        window.PerformanceObserver = function(callback) {
                            const wrappedCallback = (list) => {
                                const entries = list.getEntries().filter(entry => {
                                    const name = entry.name || '';
                                    return !name.includes('devtools') && !name.includes('pptr');
                                });
                                callback({ getEntries: () => entries });
                            };
                            return new OriginalObserver(wrappedCallback);
                        };
                        window.PerformanceObserver.prototype = OriginalObserver.prototype;
                        stealthify(window, 'PerformanceObserver', window.PerformanceObserver);
                    }

                    // 1d. Mask getOwnPropertyDescriptor to hide CDP artifacts AND mask webdriver
                    const originalGetOwnPropertyDescriptor = Object.getOwnPropertyDescriptor;
                    Object.getOwnPropertyDescriptor = function(obj, prop) {
                        // Handle CDP artifacts
                        if (obj === window && typeof prop === 'string') {
                            if (prop.includes('cdc_') || prop.includes('__playwright') || 
                                prop.includes('$chrome_') || prop.includes('$wdc_')) {
                                return undefined;
                            }
                        }
                        
                        // Handle webdriver property
                        if (prop === 'webdriver') {
                            // For navigator INSTANCE, return undefined (property is only on prototype)
                            if (obj === navigator) {
                                return undefined;
                            }
                            // For navigator PROTOTYPE, return proper descriptor
                            if (obj === Object.getPrototypeOf(navigator)) {
                                const nativeGetter = function webdriver() { return false; };
                                Object.defineProperty(nativeGetter, 'name', { value: 'get webdriver' });
                                
                                // Register with our robust toString mocker
                                mocks.set(nativeGetter, 'function get webdriver() { [native code] }');
                                
                                return {
                                    get: nativeGetter,
                                    set: undefined,
                                    enumerable: true,
                                    configurable: true
                                };
                            }
                        }
                        return originalGetOwnPropertyDescriptor.apply(this, arguments);
                    };

                    // 1e. Mask getOwnPropertyNames to hide CDP artifacts
                    const originalGetOwnPropertyNames = Object.getOwnPropertyNames;
                    Object.getOwnPropertyNames = function(obj) {
                        const props = originalGetOwnPropertyNames.apply(this, arguments);
                        if (obj === window) {
                            return props.filter(p => 
                                !p.includes('cdc_') && 
                                !p.includes('__playwright') && 
                                !p.includes('$chrome_') && 
                                !p.includes('$wdc_')
                            );
                        }
                        return props;
                    };

                    // ==============================================
                    // 2. DEVTOOLS DETECTION EVASION (isDevtoolOpen fix)
                    // ==============================================

                    // 2a. Prevent debugger statement detection
                    // Some sites use debugger statements to detect if DevTools is open
                    // by measuring execution time differences
                    const originalDateNow = Date.now;
                    let timeOffset = 0;
                    Date.now = function() {
                        return originalDateNow.call(Date) + timeOffset;
                    };
                    stealthify(Date, 'now', Date.now);

                    // 2b. Block Firebug detection
                    Object.defineProperty(window, 'Firebug', {
                        get: () => undefined,
                        set: () => {},
                        configurable: true
                    });

                    // 2c. Prevent console.profile timing detection
                    // Detection method: console.profile() takes longer when DevTools is open
                    if (window.console) {
                        const fakeProfile = function profile() {};
                        const fakeProfileEnd = function profileEnd() {};
                        stealthify(console, 'profile', fakeProfile);
                        stealthify(console, 'profileEnd', fakeProfileEnd);
                    }

// Console proxy removed (dead code)

                    // Replace console methods with stealthified versions
                    const consoleMethods = [
                        'debug', 'error', 'info', 'log', 'warn', 'dir', 'dirxml', 'table', 
                        'trace', 'group', 'groupCollapsed', 'groupEnd', 'clear', 'assert', 
                        'count', 'countReset', 'time', 'timeEnd', 'timeLog', 'timeStamp'
                    ];
                    
                    consoleMethods.forEach(method => {
                        if (console[method]) {
                            const mock = function() {};
                            stealthify(console, method, mock);
                        }
                    });

                    // 2e. Prevent outerHeight/outerWidth detection
                    // DevTools changes these values when docked
                    const realOuterWidth = window.outerWidth;
                    const realOuterHeight = window.outerHeight;
                    const realInnerWidth = window.innerWidth;
                    const realInnerHeight = window.innerHeight;
                    
                    Object.defineProperty(window, 'outerWidth', {
                        get: () => realInnerWidth + 16, // Normal window chrome offset
                        configurable: true
                    });
                    Object.defineProperty(window, 'outerHeight', {
                        get: () => realInnerHeight + 88, // Normal window chrome offset
                        configurable: true
                    });

                    // 2f. Prevent window.chrome.devtools detection
                    if (window.chrome) {
                        Object.defineProperty(window.chrome, 'devtools', {
                            get: () => undefined,
                            configurable: true
                        });
                    }

                    // ==============================================
                    // 3. NAVIGATOR.WEBDRIVER MASKING (Critical)
                    // ==============================================
                    
                    // Real Chrome returns FALSE for navigator.webdriver when not in automation.
                    // Playwright sets it to TRUE. We need to make it return FALSE.
                    // Important: returning undefined is detectable as manual tampering!
                    
                    try {
                        const navigatorProto = Object.getPrototypeOf(navigator);
                        if (navigatorProto) {
                            // Create a native-looking getter that returns false
                            const webdriverGetter = function webdriver() { return false; };
                            // Fix the name property to be 'get webdriver' (this is what real Chrome has)
                            Object.defineProperty(webdriverGetter, 'name', { value: 'get webdriver' });
                            
                            // Critical: Register with global toString hook
                            const str = 'function get webdriver() { [native code] }';
                            mocks.set(webdriverGetter, str);
                            
                            // Delete any existing property first
                            delete navigatorProto.webdriver;
                            
                            // Redefine with our native-looking getter
                            Object.defineProperty(navigatorProto, 'webdriver', {
                                get: webdriverGetter,
                                set: undefined,
                                enumerable: true,
                                configurable: true
                            });
                        }
                    } catch(e) {}
                    
                    // Also delete from navigator instance if it exists there
                    try {
                        if (navigator.hasOwnProperty && navigator.hasOwnProperty('webdriver')) {
                            delete navigator.webdriver;
                        }
                    } catch(e) {}

                    // ==============================================
                    // 4. WINDOW.CHROME MOCKING
                    // ==============================================
                    
                    if (!window.chrome) {
                        const chromeObj = {
                            runtime: {
                                connect: function connect() {},
                                sendMessage: function sendMessage() {},
                                onMessage: { addListener: function() {} },
                                onConnect: { addListener: function() {} }
                            },
                            loadTimes: function() { return {}; },
                            csi: function() { return {}; },
                            app: {
                                isInstalled: false,
                                getDetails: function getDetails() { return null; },
                                getIsInstalled: function getIsInstalled() { return false; },
                                installState: function installState() { return 'not_installed'; },
                                runningState: function runningState() { return 'cannot_run'; }
                            }
                        };
                        
                        // Apply stealth to all chrome methods
                        stealthify(chromeObj.runtime, 'connect', chromeObj.runtime.connect);
                        stealthify(chromeObj.runtime, 'sendMessage', chromeObj.runtime.sendMessage);
                        stealthify(chromeObj, 'loadTimes', chromeObj.loadTimes);
                        stealthify(chromeObj, 'csi', chromeObj.csi);
                        stealthify(chromeObj.app, 'getDetails', chromeObj.app.getDetails);
                        stealthify(chromeObj.app, 'getIsInstalled', chromeObj.app.getIsInstalled);
                        stealthify(chromeObj.app, 'installState', chromeObj.app.installState);
                        stealthify(chromeObj.app, 'runningState', chromeObj.app.runningState);

                        Object.defineProperty(window, 'chrome', {
                            value: chromeObj,
                            writable: true,
                            enumerable: true,
                            configurable: false
                        });
                    }

                    // ==============================================
                    // 5. PERMISSIONS MOCKING
                    // ==============================================
                    
                    if (navigator.permissions && navigator.permissions.query) {
                        const originalQuery = navigator.permissions.query.bind(navigator.permissions);
                        const mockedQuery = function query(parameters) {
                            if (parameters && parameters.name === 'notifications') {
                                return Promise.resolve({ state: Notification.permission, onchange: null });
                            }
                            return originalQuery(parameters);
                        };
                        stealthify(navigator.permissions, 'query', mockedQuery);
                    }

                    // ==============================================
                    // 6. CLEANUP AUTOMATION INDICATORS
                    // ==============================================
                    
                    const cleanWindow = () => {
                        const patterns = ['$cdc_', '$wdc_', '__webdriver', '__selenium', '__driver', 'callPhantom', '_phantom'];
                        for (const prop in window) {
                            try {
                                for (const pattern of patterns) {
                                    if (prop.includes(pattern)) {
                                        delete window[prop];
                                        break;
                                    }
                                }
                            } catch(e) {}
                        }
                    };
                    cleanWindow();

                    // ==============================================
                    // 6b. POPUP DETECTOR CRASH FIX
                    // ==============================================

                    // Hook window.open globally to prevent crash exploits
                    try {
                        const originalOpen = window.open;
                        window.open = function(url, target, features) {
                            if (features && (features.includes('top=9999') || features.includes('left=9999'))) {
                                return null;
                            }
                            return originalOpen.apply(this, arguments);
                        };
                        stealthify(window, 'open', window.open);
                    } catch(e) {}

                    // ==============================================
                    // 7. IFRAME PROTECTION
                    // ==============================================
                    
                    // Apply stealth patches to iframes
                    const originalCreateElement = document.createElement.bind(document);
                    document.createElement = function(tagName) {
                        const element = originalCreateElement(tagName);
                        if (tagName.toLowerCase() === 'iframe') {
                            element.addEventListener('load', function() {
                                try {
                                    const iframeWindow = element.contentWindow;
                                    if (iframeWindow && iframeWindow.navigator) {
                                        Object.defineProperty(iframeWindow.navigator, 'webdriver', {
                                            get: () => false,
                                            configurable: true
                                        });
                                    }
                                } catch(e) {}
                            });
                        }
                        return element;
                    };
                    stealthify(document, 'createElement', document.createElement);

                    // Hook Node.prototype.appendChild to intercept iframe creation and patch window.open immediately
                    const originalAppendChild = Node.prototype.appendChild;
                    Node.prototype.appendChild = function(node) {
                        const result = originalAppendChild.apply(this, arguments);
                        
                        if (node.tagName && node.tagName.toLowerCase() === 'iframe') {
                            try {
                                if (node.contentWindow) {
                                    const frameWin = node.contentWindow;
                                    const originalFrameOpen = frameWin.open;
                                    frameWin.open = function(url, target, features) {
                                        if (features && (features.includes('top=9999') || features.includes('left=9999'))) {
                                            return null;
                                        }
                                        return originalFrameOpen.apply(this, arguments);
                                    };
                                }
                            } catch(e) {}
                        }
                        return result;
                    };
                    stealthify(Node.prototype, 'appendChild', Node.prototype.appendChild);
                ");
            }
        }

        #endregion
    }
}
