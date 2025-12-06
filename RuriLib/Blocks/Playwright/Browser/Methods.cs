using Microsoft.Playwright;
using RuriLib.Attributes;
using RuriLib.Helpers.Playwright;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using RuriLib.Models.Settings;
using RuriLib.Providers.Playwright;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Http;
using NAudio.Wave;
using System.Speech.Recognition;

namespace RuriLib.Blocks.Playwright.Browser
{
    [BlockCategory("Browser", "Blocks for managing Playwright browser instances", "#9370db")]
    public static class Methods
    {
        private const string PlaywrightCleanupStateKey = "playwright.cleanupState";
        private static readonly TimeSpan ManualClosePollInterval = TimeSpan.FromMilliseconds(750);

        [Block("Opens a new playwright browser", name = "Open Browser")]
        public static async Task PlaywrightOpenBrowser(BotData data, PlaywrightBrowserType? browserType = null,
            bool? headless = null, string[] extraArgs = null, string firefoxProfilePath = null, string extensionPath = null, string firefoxAddonPath = null)
        {
            data.Logger.LogHeader();

            // Check if there is already an open browser
            var oldBrowser = data.TryGetObject<IBrowser>("playwright");
            if (oldBrowser?.IsConnected == true)
            {
                data.Logger.Log("The browser is already open, close it if you want to open a new browser", LogColors.MediumPurple);
                return;
            }

            var previousCleanupState = data.TryGetObject<PlaywrightCleanupState>(PlaywrightCleanupStateKey);
            previousCleanupState?.Cleanup(null);

            var tempEntriesBeforeLaunch = CapturePlaywrightTempEntries();
            Dictionary<int, string> firefoxProcessesBeforeLaunch = null;

            var provider = data.Providers.PlaywrightBrowser;

            // Use provider settings or override with parameters
            var actualBrowserType = browserType ?? provider.BrowserType;
            var actualHeadless = headless ?? provider.Headless;
            var actualExtraArgs = extraArgs ?? provider.ExtraArgs;

            // Handle extension loading for Chromium browsers
            var finalArgs = actualExtraArgs?.ToList() ?? new List<string>();
            if (!string.IsNullOrEmpty(extensionPath))
            {
                if (actualBrowserType == PlaywrightBrowserType.Chromium)
                {
                    finalArgs.Add($"--disable-extensions-except={extensionPath}");
                    finalArgs.Add($"--load-extension={extensionPath}");
                    data.Logger.Log($"Loading Chromium extension from: {extensionPath}", LogColors.MediumPurple);
                }
                else
                {
                    data.Logger.Log($"⚠️ Extension path specified but browser type is {actualBrowserType}. Extensions are only supported with Chromium browsers.", LogColors.Orange);
                }
            }

            // Validate Firefox addon path
            if (!string.IsNullOrEmpty(firefoxAddonPath))
            {
                if (actualBrowserType != PlaywrightBrowserType.Firefox)
                {
                    data.Logger.Log($"⚠️ Firefox addon path specified but browser type is {actualBrowserType}. Firefox addons are only supported with Firefox browsers.", LogColors.Orange);
                }
                else if (string.IsNullOrEmpty(firefoxProfilePath))
                {
                    // Create a temporary profile path for addon installation
                    firefoxProfilePath = Path.Combine(Path.GetTempPath(), "firefox_temp_profile_" + Guid.NewGuid().ToString("N")[..8]);
                    Directory.CreateDirectory(firefoxProfilePath);
                    data.SetObject("playwright.tempFirefoxProfile", firefoxProfilePath);
                    data.Logger.Log($"📁 Created temporary Firefox profile for addon installation: {firefoxProfilePath}", LogColors.Yellow);
                }
            }

            PlaywrightLaunchConfigurator.EnsureSandboxFlags(finalArgs);
            var sanitizedArgs = finalArgs.ToArray();
            actualExtraArgs = sanitizedArgs;

            var launchOptions = new BrowserTypeLaunchOptions
            {
                Headless = actualHeadless,
                Args = sanitizedArgs,
                Timeout = provider.TimeoutMilliseconds
            };

            // Efficient browser executable path handling
            launchOptions.ExecutablePath = GetExecutablePath(actualBrowserType, data);

            // Validate and correct browser type if needed
            actualBrowserType = ValidateBrowserType(actualBrowserType, launchOptions.ExecutablePath, data);
            if (actualBrowserType == PlaywrightBrowserType.Firefox && firefoxProcessesBeforeLaunch == null)
            {
                firefoxProcessesBeforeLaunch = CaptureFirefoxProcessSnapshot();
                PlaywrightLaunchConfigurator.ApplyFirefoxSafeDefaults(launchOptions);
                data.Logger.Log("Firefox GPU disabled and sandbox relaxed to avoid RADAR_PRE_LEAK_64.", LogColors.MediumPurple);
            }

            // Ensure runtime path and required browsers exist before creating the Playwright instance
            Action<string> runtimeLog = message => data.Logger.Log(message, LogColors.MediumPurple);
            await PlaywrightRuntimeService.EnsureBrowserInstalledAsync(actualBrowserType, launchOptions.ExecutablePath, runtimeLog);
            var playwright = await PlaywrightRuntimeService.CreateAsync(actualBrowserType, launchOptions.ExecutablePath, runtimeLog);
            data.SetObject("playwrightInstance", playwright);

            IBrowser browser = null;
            IBrowserContext context = null;

            try
            {
                // Use persistent context for Firefox profile or Chromium extensions
                if (actualBrowserType == PlaywrightBrowserType.Firefox && !string.IsNullOrEmpty(firefoxProfilePath))
                {
                    data.Logger.Log($"Using Firefox profile: {firefoxProfilePath}", LogColors.MediumPurple);

                    // Handle Firefox addon installation
                    if (!string.IsNullOrEmpty(firefoxAddonPath))
                    {
                        await InstallFirefoxAddon(firefoxProfilePath, firefoxAddonPath, data);
                    }

                    var persistentOptions = new BrowserTypeLaunchPersistentContextOptions
                    {
                        Headless = actualHeadless,
                        Args = sanitizedArgs,
                        Timeout = provider.TimeoutMilliseconds,
                        ExecutablePath = launchOptions.ExecutablePath
                    };
                    PlaywrightLaunchConfigurator.ApplyFirefoxSafeDefaults(persistentOptions);
                    context = await playwright.Firefox.LaunchPersistentContextAsync(firefoxProfilePath, persistentOptions);
                    data.SetObject("playwrightContext", context);
                    // For persistent contexts, we don't need to set the browser object as it may be null
                    // The context itself contains all necessary functionality
                }
                else if (actualBrowserType == PlaywrightBrowserType.Chromium && !string.IsNullOrEmpty(extensionPath))
                {
                    // Use persistent context for Chromium extensions - this is required for extensions to work properly
                    data.Logger.Log($"Using persistent context for Chromium extension: {extensionPath}", LogColors.MediumPurple);
                    var tempUserDataDir = Path.Combine(Path.GetTempPath(), "playwright-chromium-" + Guid.NewGuid().ToString());
                    data.SetObject("playwright.tempChromiumUserData", tempUserDataDir);
                    var persistentOptions = new BrowserTypeLaunchPersistentContextOptions
                    {
                        Headless = actualHeadless,
                        Args = sanitizedArgs,
                        Timeout = provider.TimeoutMilliseconds,
                        ExecutablePath = launchOptions.ExecutablePath
                    };
                    context = await playwright.Chromium.LaunchPersistentContextAsync(tempUserDataDir, persistentOptions);
                    data.SetObject("playwrightContext", context);
                }
                else
                {
                    // For regular browsers without special requirements, use regular launch
                    browser = await LaunchBrowserWithRetry(playwright, actualBrowserType, launchOptions, data);
                }
            }
            catch (Exception ex) when (actualBrowserType == PlaywrightBrowserType.Firefox && !string.IsNullOrEmpty(launchOptions.ExecutablePath) && !string.IsNullOrEmpty(firefoxProfilePath) && context == null)
            {
                data.Logger.Log($"❌ Custom Firefox with profile launch failed: {ex.GetType().Name} - {ex.Message}", LogColors.Red);
                data.Logger.Log($"🔄 Attempting fallback to Playwright's built-in Firefox with profile...", LogColors.Yellow);

                try
                {
                    // Try persistent context launch with built-in Firefox
                    var fallbackPersistentOptions = new BrowserTypeLaunchPersistentContextOptions
                    {
                        Headless = actualHeadless,
                        Args = sanitizedArgs,
                        Timeout = provider.TimeoutMilliseconds
                        // ExecutablePath is intentionally omitted to use built-in Firefox
                    };
                    PlaywrightLaunchConfigurator.ApplyFirefoxSafeDefaults(fallbackPersistentOptions);
                    context = await playwright.Firefox.LaunchPersistentContextAsync(firefoxProfilePath, fallbackPersistentOptions);
                    data.SetObject("playwrightContext", context);
                    data.Logger.Log($"✅ Successfully launched Playwright's built-in Firefox with profile", LogColors.Green);
                }
                catch (Exception fallbackEx)
                {
                    data.Logger.Log($"❌ Profile fallback also failed: {fallbackEx.GetType().Name} - {fallbackEx.Message}", LogColors.Red);
                    data.Logger.Log($"💡 Both custom and built-in Firefox with profile failed. Consider:", LogColors.Yellow);
                    data.Logger.Log($"   - Installing Playwright browsers: playwright install firefox", LogColors.Yellow);
                    data.Logger.Log($"   - Using a different browser type (Chromium/Webkit)", LogColors.Yellow);
                    data.Logger.Log($"   - Checking if the Firefox profile path is valid and accessible", LogColors.Yellow);
                    throw new Exception($"Failed to launch Firefox browser with profile using both custom and built-in executables.", ex);
                }
            }
            catch (Exception ex) when (actualBrowserType == PlaywrightBrowserType.Firefox && !string.IsNullOrEmpty(launchOptions.ExecutablePath) && string.IsNullOrEmpty(firefoxProfilePath) && browser == null)
            {
                data.Logger.Log($"❌ Custom Firefox launch failed: {ex.GetType().Name} - {ex.Message}", LogColors.Red);
                data.Logger.Log($"🔄 Attempting fallback to Playwright's built-in Firefox...", LogColors.Yellow);

                try
                {
                    // Try regular launch with built-in Firefox
                    var fallbackOptions = new BrowserTypeLaunchOptions
                    {
                        Headless = actualHeadless,
                        Args = actualExtraArgs,
                        Timeout = provider.TimeoutMilliseconds
                        // ExecutablePath is intentionally omitted to use built-in Firefox
                    };
                    PlaywrightLaunchConfigurator.ApplyFirefoxSafeDefaults(fallbackOptions);
                    browser = await playwright.Firefox.LaunchAsync(fallbackOptions);
                    data.Logger.Log($"✅ Successfully launched Playwright's built-in Firefox", LogColors.Green);
                }
                catch (Exception fallbackEx)
                {
                    data.Logger.Log($"❌ Fallback also failed: {fallbackEx.GetType().Name} - {fallbackEx.Message}", LogColors.Red);
                    data.Logger.Log($"💡 Both custom and built-in Firefox failed. Consider:", LogColors.Yellow);
                    data.Logger.Log($"   - Installing Playwright browsers: playwright install firefox", LogColors.Yellow);
                    data.Logger.Log($"   - Using a different browser type (Chromium/Webkit)", LogColors.Yellow);
                    throw new Exception($"Failed to launch Firefox browser with both custom and built-in executables.", ex);
                }
            }
            catch (Exception ex)
            {
                HandleBrowserLaunchError(ex, actualBrowserType, launchOptions.ExecutablePath, data);
                throw new Exception($"Failed to launch {actualBrowserType} browser. See details above.", ex);
            }

            // Handle both regular browser and persistent context cases
            if (browser != null)
            {
                data.SetObject("playwright", browser);
                data.Logger.Log($"Opened {actualBrowserType} browser (headless: {actualHeadless})", LogColors.MediumPurple);

                // Automatically create a new page to prevent immediate bot termination
                var page = await browser.NewPageAsync();
                data.SetObject("playwrightPage", page);
                data.Logger.Log("Automatically created a new page", LogColors.MediumPurple);
            }
            else if (context != null)
            {
                // For persistent contexts, we work directly with the context
                data.Logger.Log($"Opened {actualBrowserType} browser with persistent context (headless: {actualHeadless})", LogColors.MediumPurple);

                // Check if there is already an open page in the persistent context
                var existingPages = context.Pages;
                IPage page;

                if (existingPages.Count > 0)
                {
                    // Use the first existing page from the persistent context
                    page = existingPages[0];
                    data.Logger.Log("Using existing page from persistent context", LogColors.MediumPurple);
                }
                else
                {
                    // Create a new page only if none exist
                    page = await context.NewPageAsync();
                    data.Logger.Log("Created new page from persistent context", LogColors.MediumPurple);
                }

                data.SetObject("playwrightPage", page);
            }
            else
            {
                throw new Exception("Neither browser nor context was successfully created.");
            }

            RegisterCleanupState(data, browser ?? context?.Browser, tempEntriesBeforeLaunch);
            StoreFirefoxProcessDelta(data, firefoxProcessesBeforeLaunch);
            var manualCloseWatcherEnabled = !actualHeadless && actualBrowserType == PlaywrightBrowserType.Firefox;
            var cleanupState = data.TryGetObject<PlaywrightCleanupState>(PlaywrightCleanupStateKey);
            cleanupState?.StartManualCloseWatcher(manualCloseWatcherEnabled);
        }

        [Block("Closes an open playwright browser", name = "Close Browser")]
        public static async Task PlaywrightCloseBrowser(BotData data)
        {
            data.Logger.LogHeader();

            var context = data.TryGetObject<IBrowserContext>("playwrightContext");
            var browser = data.TryGetObject<IBrowser>("playwright");
            var cleanupState = data.TryGetObject<PlaywrightCleanupState>(PlaywrightCleanupStateKey);

            cleanupState?.SuppressBrowserDisconnect();
            cleanupState?.StopManualCloseWatcher();

            if (context != null)
            {
                await context.CloseAsync();
                data.Logger.Log("Closed the browser context", LogColors.MediumPurple);
            }
            else if (browser != null)
            {
                await browser.CloseAsync();
                data.Logger.Log("Closed the browser", LogColors.MediumPurple);
            }
            else
            {
                throw new Exception("No browser or context open to close");
            }

            // Wait a moment for processes to exit gracefully
            await Task.Delay(500);

            // ALWAYS kill Firefox processes (even after successful close)
            KillPlaywrightFirefoxProcesses(data);

            var cleanupHandled = cleanupState?.Cleanup(null) ?? false;
            if (!cleanupHandled)
            {
                PerformCleanup(data);
            }

            data.Logger.Log("Browser closed successfully!", LogColors.MediumPurple);
        }
        [Block("Creates a new page in the browser", name = "New Page")]
        public static async Task PlaywrightNewPage(BotData data)
        {
            data.Logger.LogHeader();

            var context = data.TryGetObject<IBrowserContext>("playwrightContext");
            var browser = data.TryGetObject<IBrowser>("playwright");

            IPage page;
            if (context != null)
            {
                page = await context.NewPageAsync();
            }
            else if (browser != null)
            {
                page = await browser.NewPageAsync();
            }
            else
            {
                throw new Exception("No browser or context open. Use the 'Open Browser' block first");
            }

            data.SetObject("playwrightPage", page);
            data.Logger.Log("Created a new page", LogColors.MediumPurple);
        }

        [Block("Closes the current page", name = "Close Page")]
        public static async Task PlaywrightClosePage(BotData data)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            await page.CloseAsync();

            data.Logger.Log("Closed the current page", LogColors.MediumPurple);
        }

        [Block("Gets all open pages", name = "Get Pages")]
        public static async Task PlaywrightGetPages(BotData data)
        {
            data.Logger.LogHeader();

            var browser = GetBrowser(data);
            var pages = browser.Contexts.SelectMany(c => c.Pages).ToArray();
            data.SetObject("playwrightPages", pages);

            data.Logger.Log($"Found {pages.Length} open pages", LogColors.MediumPurple);
        }

        [Block("Switches to a page by index", name = "Switch to Page")]
        public static async Task PlaywrightSwitchToPage(BotData data, int index)
        {
            data.Logger.LogHeader();

            var browser = GetBrowser(data);
            var pages = browser.Contexts.SelectMany(c => c.Pages).ToArray();

            if (index < 0 || index >= pages.Length)
                throw new ArgumentException($"Invalid page index {index}. Available pages: {pages.Length}");

            var page = pages[index];
            data.SetObject("playwrightPage", page);

            data.Logger.Log($"Switched to page {index}", LogColors.MediumPurple);
        }

        private static IBrowser GetBrowser(BotData data)
        {
            var browser = data.TryGetObject<IBrowser>("playwright");
            return browser ?? throw new Exception("No browser open. Use the 'Open Browser' block first");
        }

        private static IPage GetPage(BotData data)
        {
            var page = data.TryGetObject<IPage>("playwrightPage");
            return page ?? throw new Exception("No page available. Use the 'New Page' block first");
        }
        private static string GetExecutablePath(PlaywrightBrowserType browserType, BotData data)
        {
            var provider = data.Providers.PlaywrightBrowser;
            return browserType switch
            {
                PlaywrightBrowserType.Chromium => provider.ChromiumBinaryLocation,
                PlaywrightBrowserType.Firefox => provider.FirefoxBinaryLocation,
                PlaywrightBrowserType.Webkit => provider.WebkitBinaryLocation,
                _ => null
            };
        }

        private static PlaywrightBrowserType ValidateBrowserType(PlaywrightBrowserType browserType, string executablePath, BotData data)
        {
            if (browserType == PlaywrightBrowserType.Webkit && !string.IsNullOrEmpty(executablePath))
            {
                var executableName = System.IO.Path.GetFileNameWithoutExtension(executablePath).ToLower();
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
                PlaywrightBrowserType.Chromium => await playwright.Chromium.LaunchAsync(options),
                PlaywrightBrowserType.Firefox => await playwright.Firefox.LaunchAsync(options),
                PlaywrightBrowserType.Webkit => await playwright.Webkit.LaunchAsync(options),
                _ => throw new ArgumentException($"Unsupported browser type: {browserType}")
            };
        }

        private static void HandleBrowserLaunchError(Exception ex, PlaywrightBrowserType browserType, string executablePath, BotData data)
        {
            data.Logger.Log($"❌ Browser launch failed with error:", LogColors.Red);
            data.Logger.Log($"   Exception Type: {ex.GetType().Name}", LogColors.Red);
            data.Logger.Log($"   Message: {ex.Message}", LogColors.Red);
            if (ex.InnerException != null)
            {
                data.Logger.Log($"   Inner Exception: {ex.InnerException.Message}", LogColors.Red);
            }
            data.Logger.Log($"💡 Troubleshooting tips:", LogColors.Yellow);
            data.Logger.Log($"   - Verify browser executable path is correct", LogColors.Yellow);
            data.Logger.Log($"   - Check if browser supports automation", LogColors.Yellow);
            data.Logger.Log($"   - Try using Playwright's built-in browsers", LogColors.Yellow);
        }

        private static async Task InstallFirefoxAddon(string profilePath, string addonPath, BotData data)
        {
            try
            {
                // Ensure the profile extensions directory exists
                var extensionsDir = Path.Combine(profilePath, "extensions");
                if (!Directory.Exists(extensionsDir))
                {
                    Directory.CreateDirectory(extensionsDir);
                    data.Logger.Log($"Created extensions directory: {extensionsDir}", LogColors.MediumPurple);
                }

                // Handle single file or directory with multiple .xpi files
                if (File.Exists(addonPath) && Path.GetExtension(addonPath).ToLower() == ".xpi")
                {
                    // Single .xpi file
                    var originalFileName = Path.GetFileName(addonPath);
                    var properFileName = GenerateProperAddonFileName(originalFileName);
                    var destinationPath = Path.Combine(extensionsDir, properFileName);
                    File.Copy(addonPath, destinationPath, true);

                    if (originalFileName != properFileName)
                    {
                        data.Logger.Log($"Renamed addon from '{originalFileName}' to '{properFileName}' for proper Firefox recognition", LogColors.MediumPurple);
                    }
                    data.Logger.Log($"Installed Firefox addon: {properFileName}", LogColors.Green);
                }
                else if (Directory.Exists(addonPath))
                {
                    // Directory containing .xpi files
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

                        if (originalFileName != properFileName)
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
            // Check if the filename already follows the proper format (starts with addon@ and contains domain)
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(originalFileName);

            // If it already starts with addon@ and contains domain, keep it as is
            if (nameWithoutExtension.StartsWith("addon@") && nameWithoutExtension.Contains("."))
            {
                return originalFileName;
            }

            // Generate a proper addon filename starting with addon@
            // Convert filename to format: addon@originalname.com.xpi
            var cleanName = nameWithoutExtension.ToLower()
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("_", "");

            // Ensure the name is valid for domain format
            if (string.IsNullOrEmpty(cleanName))
            {
                cleanName = "extension";
            }

            return $"addon@{cleanName}.com.xpi";
        }

        [Block("Solves CAPTCHA challenges using audio recognition", name = "Solve CAPTCHA")]
        public static async Task PlaywrightSolveCaptcha(BotData data, int timeoutSeconds = 120, bool useAudioRecognition = true, int checkboxTimeoutMilliseconds = 2000)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            var startTime = DateTime.Now;

            try
            {
                data.Logger.Log("🔍 Looking for CAPTCHA challenges...", LogColors.MediumPurple);

                // Wait for CAPTCHA to appear with timeout
                while ((DateTime.Now - startTime).TotalSeconds < timeoutSeconds)
                {
                    // Enhanced reCAPTCHA detection - check multiple patterns and nested iframes
                    var recaptchaFound = await DetectRecaptcha(page, data);
                    if (recaptchaFound)
                    {
                        data.Logger.Log("🎯 Found reCAPTCHA challenge", LogColors.MediumPurple);
                        await SolveRecaptcha(page, data, useAudioRecognition, checkboxTimeoutMilliseconds);
                        return;
                    }

                    await Task.Delay(1000); // Wait 1 second before checking again
                }

                data.Logger.Log("⏰ Timeout reached - no CAPTCHA found", LogColors.Orange);
            }
            catch (Exception ex)
            {
                data.Logger.Log($"❌ CAPTCHA solving failed: {ex.Message}", LogColors.Red);
                throw;
            }
        }

        private static async Task<bool> DetectRecaptcha(IPage page, BotData data)
        {
            try
            {
                // Method 1: Direct iframe src detection
                var directFrames = await page.QuerySelectorAllAsync("iframe[src*='recaptcha'], iframe[src*='google.com/recaptcha']");
                if (directFrames.Count > 0)
                {
                    data.Logger.Log($"Found {directFrames.Count} reCAPTCHA iframes by src attribute", LogColors.MediumPurple);
                    return true;
                }

                // Method 2: Look for g-recaptcha elements
                var gRecaptchaElements = await page.QuerySelectorAllAsync(".g-recaptcha, [data-sitekey], #g-recaptcha-response");
                if (gRecaptchaElements.Count > 0)
                {
                    data.Logger.Log($"Found {gRecaptchaElements.Count} g-recaptcha elements", LogColors.MediumPurple);
                    return true;
                }

                // Method 3: Search all iframes recursively for reCAPTCHA content
                var allIframes = await page.QuerySelectorAllAsync("iframe");
                data.Logger.Log($"Searching through {allIframes.Count} iframes for reCAPTCHA content...", LogColors.MediumPurple);

                foreach (var iframeElement in allIframes)
                {
                    try
                    {
                        var frame = await iframeElement.ContentFrameAsync();
                        if (frame != null)
                        {
                            // Check for reCAPTCHA indicators in this frame
                            var recaptchaIndicators = await frame.QuerySelectorAllAsync(
                                ".rc-anchor, .recaptcha-checkbox, #recaptcha-anchor, .g-recaptcha, " +
                                "[data-sitekey], #g-recaptcha-response, .rc-anchor-checkbox-holder");

                            if (recaptchaIndicators.Count > 0)
                            {
                                data.Logger.Log($"Found reCAPTCHA indicators in nested iframe", LogColors.MediumPurple);
                                return true;
                            }

                            // Recursively check nested iframes
                            var nestedIframes = await frame.QuerySelectorAllAsync("iframe");
                            foreach (var nestedIframe in nestedIframes)
                            {
                                try
                                {
                                    var nestedFrame = await nestedIframe.ContentFrameAsync();
                                    if (nestedFrame != null)
                                    {
                                        var nestedIndicators = await nestedFrame.QuerySelectorAllAsync(
                                            ".rc-anchor, .recaptcha-checkbox, #recaptcha-anchor, .g-recaptcha, " +
                                            "[data-sitekey], #g-recaptcha-response, .rc-anchor-checkbox-holder");

                                        if (nestedIndicators.Count > 0)
                                        {
                                            data.Logger.Log($"Found reCAPTCHA indicators in deeply nested iframe", LogColors.MediumPurple);
                                            return true;
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    data.Logger.Log($"Error checking nested iframe: {ex.Message}", LogColors.Orange);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        data.Logger.Log($"Error checking iframe: {ex.Message}", LogColors.Orange);
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                data.Logger.Log($"Error in reCAPTCHA detection: {ex.Message}", LogColors.Red);
                return false;
            }
        }

        private static async Task SolveRecaptcha(IPage page, BotData data, bool useAudioRecognition, int checkboxTimeoutMilliseconds = 2000)
        {
            try
            {
                // Enhanced iframe detection - look for all possible reCAPTCHA iframes
                var recaptchaFrames = await GetAllRecaptchaFrames(page, data);
                if (recaptchaFrames.Count == 0)
                {
                    data.Logger.Log("❌ reCAPTCHA iframe not found", LogColors.Red);
                    return;
                }

                data.Logger.Log($"🎯 Found {recaptchaFrames.Count} reCAPTCHA iframes", LogColors.MediumPurple);

                // Try each frame to find the main reCAPTCHA frame
                IFrame? mainFrame = null;
                foreach (var frameElement in recaptchaFrames)
                {
                    var frame = await frameElement.ContentFrameAsync();
                    if (frame != null)
                    {
                        // Check if this frame contains the checkbox
                        var frameCheckbox = await frame.QuerySelectorAsync(".rc-anchor-input, .recaptcha-checkbox, .recaptcha-checkbox-checkmark");
                        if (frameCheckbox != null)
                        {
                            mainFrame = frame;
                            break;
                        }
                    }
                }

                if (mainFrame == null)
                {
                    data.Logger.Log("❌ Could not find reCAPTCHA main frame with checkbox", LogColors.Red);
                    return;
                }

                // Look for checkbox in the main frame
                var checkbox = await mainFrame.QuerySelectorAsync(".rc-anchor-input, .recaptcha-checkbox, .recaptcha-checkbox-checkmark");
                if (checkbox != null)
                {
                    data.Logger.Log("🖱️ Clicking reCAPTCHA checkbox...", LogColors.MediumPurple);
                    await checkbox.ClickAsync();
                    await Task.Delay(checkboxTimeoutMilliseconds);

                    // Check if audio challenge is available
                    if (useAudioRecognition)
                    {
                        await TryAudioChallenge(mainFrame, data);

                        // Enhanced challenge frame detection after clicking checkbox
                        await Task.Delay(2000); // Increased delay for frame to load

                        // Look for challenge frames with multiple selectors
                        var challengeFrameSelectors = new[]
                        {
                            "iframe[src*='recaptcha/api2/bframe']",
                            "iframe[title='recaptcha challenge']",
                            "iframe[src*='bframe']",
                            "iframe[name*='c-']",
                            "iframe[src*='challenge']",
                            "iframe[title*='challenge']",
                            "iframe[src*='recaptcha/api2/anchor']"
                        };

                        var challengeFrames = new List<IElementHandle>();
                        foreach (var selector in challengeFrameSelectors)
                        {
                            var frames = await page.QuerySelectorAllAsync(selector);
                            foreach (var frame in frames)
                            {
                                if (!challengeFrames.Contains(frame))
                                {
                                    challengeFrames.Add(frame);
                                }
                            }
                        }

                        if (challengeFrames.Count > 0)
                        {
                            data.Logger.Log($"🎯 Found {challengeFrames.Count} challenge frame(s) after clicking checkbox", LogColors.MediumPurple);
                            foreach (var challengeFrameElement in challengeFrames)
                            {
                                var challengeFrame = await challengeFrameElement.ContentFrameAsync();
                                if (challengeFrame != null)
                                {
                                    await TryAudioChallenge(challengeFrame, data);
                                }
                            }
                        }
                        else
                        {
                            data.Logger.Log("🔍 No challenge frames found, trying to find audio button in all frames", LogColors.Orange);
                            // Search all iframes on the page for audio challenge button
                            var allIframes = await page.QuerySelectorAllAsync("iframe");
                            foreach (var iframeElement in allIframes)
                            {
                                try
                                {
                                    var frame = await iframeElement.ContentFrameAsync();
                                    if (frame != null)
                                    {
                                        var audioButton = await FindAudioChallengeButton(frame, data);
                                        if (audioButton != null)
                                        {
                                            data.Logger.Log("🎯 Found audio button in alternative frame", LogColors.MediumPurple);
                                            await TryAudioChallenge(frame, data);
                                            break; // Found and processed, exit loop
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    data.Logger.Log($"Error checking iframe for audio button: {ex.Message}", LogColors.Orange);
                                }
                            }
                        }
                    }
                }
                else
                {
                    data.Logger.Log("❌ reCAPTCHA checkbox not found", LogColors.Red);
                }
            }
            catch (Exception ex)
            {
                data.Logger.Log($"❌ reCAPTCHA solving failed: {ex.Message}", LogColors.Red);
            }
        }

        private static async Task<List<IElementHandle>> GetAllRecaptchaFrames(IPage page, BotData data)
        {
            var allFrames = new List<IElementHandle>();

            try
            {
                // Method 1: Direct iframe src detection
                var directFrames = await page.QuerySelectorAllAsync("iframe[src*='recaptcha'], iframe[src*='google.com/recaptcha']");
                allFrames.AddRange(directFrames);

                // Method 2: Search all iframes for reCAPTCHA content
                var allIframes = await page.QuerySelectorAllAsync("iframe");

                foreach (var iframeElement in allIframes)
                {
                    try
                    {
                        var frame = await iframeElement.ContentFrameAsync();
                        if (frame != null)
                        {
                            // Check for reCAPTCHA indicators in this frame
                            var recaptchaIndicators = await frame.QuerySelectorAllAsync(
                                ".rc-anchor, .recaptcha-checkbox, #recaptcha-anchor, .g-recaptcha, " +
                                "[data-sitekey], #g-recaptcha-response, .rc-anchor-checkbox-holder");

                            if (recaptchaIndicators.Count > 0 && !allFrames.Contains(iframeElement))
                            {
                                allFrames.Add(iframeElement);
                            }

                            // Check nested iframes
                            var nestedIframes = await frame.QuerySelectorAllAsync("iframe");
                            foreach (var nestedIframe in nestedIframes)
                            {
                                try
                                {
                                    var nestedFrame = await nestedIframe.ContentFrameAsync();
                                    if (nestedFrame != null)
                                    {
                                        var nestedIndicators = await nestedFrame.QuerySelectorAllAsync(
                                            ".rc-anchor, .recaptcha-checkbox, #recaptcha-anchor, .g-recaptcha, " +
                                            "[data-sitekey], #g-recaptcha-response, .rc-anchor-checkbox-holder");

                                        if (nestedIndicators.Count > 0 && !allFrames.Contains(nestedIframe))
                                        {
                                            allFrames.Add(nestedIframe);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    data.Logger.Log($"Error checking nested iframe: {ex.Message}", LogColors.Orange);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        data.Logger.Log($"Error checking iframe: {ex.Message}", LogColors.Orange);
                    }
                }

                return allFrames;
            }
            catch (Exception ex)
            {
                data.Logger.Log($"Error getting reCAPTCHA frames: {ex.Message}", LogColors.Red);
                return allFrames;
            }
        }




        private static async Task<string> ProcessAudioChallenge(string audioUrl, BotData data)
        {
            var tempDir = Path.GetTempPath();
            var audioPath = Path.Combine(tempDir, $"recaptcha_{Guid.NewGuid()}.mp3");
            var wavPath = Path.Combine(tempDir, $"recaptcha_{Guid.NewGuid()}.wav");

            try
            {
                // Download audio efficiently
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                var audioBytes = await httpClient.GetByteArrayAsync(audioUrl);
                await File.WriteAllBytesAsync(audioPath, audioBytes);

                // Quick format detection
                string format = "MP3"; // Default for reCAPTCHA
                if (audioBytes.Length >= 4)
                {
                    var header = System.Text.Encoding.ASCII.GetString(audioBytes, 0, 4);
                    if (header.StartsWith("ID3") || (audioBytes[0] == 0xFF && (audioBytes[1] & 0xE0) == 0xE0))
                        format = "MP3";
                    else if (header == "RIFF")
                        format = "WAV";
                    else if (header == "OggS")
                        format = "OGG";
                }

                // Convert to WAV efficiently
                WaveStream audioStream = null;
                try
                {
                    audioStream = format switch
                    {
                        "MP3" => new Mp3FileReader(audioPath),
                        "WAV" => new WaveFileReader(audioPath),
                        _ => new Mp3FileReader(audioPath) // Default to MP3
                    };
                }
                catch
                {
                    // Fallback to raw PCM
                    var waveFormat = new WaveFormat(16000, 16, 1);
                    audioStream = new RawSourceWaveStream(new MemoryStream(audioBytes), waveFormat);
                }

                if (audioStream != null)
                {
                    using var waveFileWriter = new WaveFileWriter(wavPath, audioStream.WaveFormat);
                    await Task.Run(() => audioStream.CopyTo(waveFileWriter));
                    audioStream.Dispose();
                }

                // Speech recognition (simplified)
                using var speechRecognition = new SpeechRecognitionEngine();
                speechRecognition.LoadGrammar(new DictationGrammar());

                string recognizedText = "";
                speechRecognition.SpeechRecognized += (sender, e) => recognizedText = e.Result.Text;
                speechRecognition.SetInputToWaveFile(wavPath);

                // Try recognition (max 2 attempts)
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    var result = speechRecognition.Recognize();
                    if (result != null)
                    {
                        recognizedText = result.Text;
                        data.Logger.Log($"🎤 Recognized: {recognizedText}", LogColors.MediumPurple);
                        break;
                    }

                    if (attempt == 2 && string.IsNullOrEmpty(recognizedText))
                    {
                        // Quick async attempt on last try
                        var completed = new TaskCompletionSource<bool>();
                        speechRecognition.SpeechRecognized += (sender, e) => { recognizedText = e.Result.Text; completed.TrySetResult(true); };
                        speechRecognition.RecognizeAsync(RecognizeMode.Single);

                        var timeout = await Task.WhenAny(completed.Task, Task.Delay(3000)) != completed.Task;
                        if (!timeout && !string.IsNullOrEmpty(recognizedText))
                        {
                            data.Logger.Log($"🎤 Recognized: {recognizedText}", LogColors.MediumPurple);
                        }
                    }
                }

                return recognizedText;
            }
            catch (Exception ex)
            {
                data.Logger.Log($"❌ Audio processing failed: {ex.Message}", LogColors.Red);
                return "";
            }
            finally
            {
                // Clean up temp files
                try
                {
                    if (File.Exists(audioPath)) File.Delete(audioPath);
                    if (File.Exists(wavPath)) File.Delete(wavPath);
                }
                catch { /* Ignore cleanup errors */ }
            }
        }

        private static async Task TryAudioChallenge(IFrame frame, BotData data)
        {
            try
            {
                var audioButton = await FindAudioChallengeButton(frame, data);
                if (audioButton == null) return;

                data.Logger.Log("🔊 Clicking audio challenge button...", LogColors.MediumPurple);
                await audioButton.ClickAsync();
                await Task.Delay(2500);

                var audioInterfaceElements = await FindAudioInterfaceElements(frame, data);

                if (audioInterfaceElements.audioSource != null || audioInterfaceElements.audioElement != null || audioInterfaceElements.downloadLink != null)
                {
                    string audioUrl = audioInterfaceElements.audioSource?.GetAttributeAsync("src")?.Result ??
                                     audioInterfaceElements.audioElement?.GetAttributeAsync("src")?.Result ??
                                     audioInterfaceElements.downloadLink?.GetAttributeAsync("href")?.Result ?? "";

                    if (!string.IsNullOrEmpty(audioUrl))
                    {
                        if (!audioUrl.StartsWith("http"))
                            audioUrl = "https://www.google.com" + audioUrl;

                        data.Logger.Log($"🎵 Audio URL: {audioUrl}", LogColors.MediumPurple);

                        string recognizedText = await ProcessAudioChallenge(audioUrl, data);
                        if (string.IsNullOrEmpty(recognizedText)) return;

                        data.Logger.Log($"📝 Entering audio response: {recognizedText}", LogColors.MediumPurple);

                        var audioResponseElements = await FindAudioResponseElements(frame, data);
                        if (audioResponseElements.inputField != null)
                        {
                            await audioResponseElements.inputField.FillAsync(recognizedText);

                            if (audioResponseElements.verifyButton != null)
                            {
                                await audioResponseElements.verifyButton.ClickAsync();
                                data.Logger.Log("✅ Audio challenge submitted", LogColors.Green);
                            }
                            else
                            {
                                await audioResponseElements.inputField.PressAsync("Enter");
                                data.Logger.Log("✅ Audio challenge submitted", LogColors.Green);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                data.Logger.Log($"❌ Audio challenge failed: {ex.Message}", LogColors.Red);
            }
        }

        private static async Task<IElementHandle?> FindAudioChallengeButton(IFrame frame, BotData data)
        {
            try
            {
                var audioButtonSelectors = new[]
                {
                    "button#recaptcha-audio-button",
                    "button[aria-label*='audio']",
                    "button[title*='audio']",
                    "button.rc-button-audio",
                    "#recaptcha-audio-button",
                    ".rc-button-audio",
                    "[role='button'][aria-label*='audio']"
                };

                // Search current frame
                foreach (var selector in audioButtonSelectors)
                {
                    try
                    {
                        var button = await frame.QuerySelectorAsync(selector);
                        if (button != null && await button.IsVisibleAsync())
                            return button;
                    }
                    catch { /* Continue to next selector */ }
                }

                // Search nested iframes
                var nestedIframes = await frame.QuerySelectorAllAsync("iframe");
                foreach (var nestedIframe in nestedIframes)
                {
                    try
                    {
                        var nestedFrame = await nestedIframe.ContentFrameAsync();
                        if (nestedFrame != null)
                        {
                            foreach (var selector in audioButtonSelectors)
                            {
                                try
                                {
                                    var button = await nestedFrame.QuerySelectorAsync(selector);
                                    if (button != null && await button.IsVisibleAsync())
                                        return button;
                                }
                                catch { /* Continue to next selector */ }
                            }
                        }
                    }
                    catch { /* Continue to next iframe */ }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<(IElementHandle? audioSource, IElementHandle? audioElement, IElementHandle? downloadLink)> FindAudioInterfaceElements(IFrame frame, BotData data)
        {
            try
            {
                var audioSourceSelectors = new[]
                {
                    "#audio-source",
                    ".rc-audiochallenge-tdownload-link",
                    "source[type*='audio']",
                    "[src*='recaptcha/api2/payload/audio']"
                };

                var audioElementSelectors = new[]
                {
                    "audio",
                    "audio[controls]",
                    ".rc-audiochallenge-control"
                };

                var downloadLinkSelectors = new[]
                {
                    "a[href*='audio']",
                    "a[href*='payload']",
                    ".rc-audiochallenge-tdownload-link",
                    "[href*='recaptcha/api2/payload']"
                };

                IElementHandle audioSource = null;
                IElementHandle audioElement = null;
                IElementHandle downloadLink = null;

                // Search current frame
                foreach (var selector in audioSourceSelectors)
                {
                    try
                    {
                        audioSource = await frame.QuerySelectorAsync(selector);
                        if (audioSource != null) break;
                    }
                    catch { /* Continue */ }
                }

                foreach (var selector in audioElementSelectors)
                {
                    try
                    {
                        audioElement = await frame.QuerySelectorAsync(selector);
                        if (audioElement != null) break;
                    }
                    catch { /* Continue */ }
                }

                foreach (var selector in downloadLinkSelectors)
                {
                    try
                    {
                        downloadLink = await frame.QuerySelectorAsync(selector);
                        if (downloadLink != null) break;
                    }
                    catch { /* Continue */ }
                }

                // Search nested iframes if needed
                if (audioSource == null && audioElement == null && downloadLink == null)
                {
                    var nestedFrames = frame.ChildFrames;
                    foreach (var nestedFrame in nestedFrames)
                    {
                        try
                        {
                            if (audioSource == null)
                            {
                                foreach (var selector in audioSourceSelectors)
                                {
                                    try
                                    {
                                        audioSource = await nestedFrame.QuerySelectorAsync(selector);
                                        if (audioSource != null) break;
                                    }
                                    catch { /* Continue */ }
                                }
                            }

                            if (audioElement == null)
                            {
                                foreach (var selector in audioElementSelectors)
                                {
                                    try
                                    {
                                        audioElement = await nestedFrame.QuerySelectorAsync(selector);
                                        if (audioElement != null) break;
                                    }
                                    catch { /* Continue */ }
                                }
                            }

                            if (downloadLink == null)
                            {
                                foreach (var selector in downloadLinkSelectors)
                                {
                                    try
                                    {
                                        downloadLink = await nestedFrame.QuerySelectorAsync(selector);
                                        if (downloadLink != null) break;
                                    }
                                    catch { /* Continue */ }
                                }
                            }

                            if (audioSource != null && audioElement != null && downloadLink != null)
                                break;
                        }
                        catch { /* Continue to next iframe */ }
                    }
                }

                return (audioSource, audioElement, downloadLink);
            }
            catch
            {
                return (null, null, null);
            }
        }

        private static async Task<(IElementHandle? inputField, IElementHandle? verifyButton)> FindAudioResponseElements(IFrame frame, BotData data)
        {
            try
            {
                var inputSelectors = new[]
                {
                    "input#audio-response",
                    "input[name*='audio']",
                    "input[aria-label*='audio']",
                    "input[type='text']",
                    "input:not([type])",
                    "textarea[name*='audio']"
                };

                var buttonSelectors = new[]
                {
                    "button#recaptcha-verify-button",
                    "button[aria-label*='verify']",
                    "button[type='submit']",
                    "input[type='submit']"
                };

                // Helper method to check if element is visible
                async Task<bool> IsElementVisible(IElementHandle element)
                {
                    try
                    {
                        return await element.IsVisibleAsync();
                    }
                    catch
                    {
                        return false;
                    }
                }

                // Helper method to find elements in a frame with visibility check
                async Task<(IElementHandle? input, IElementHandle? button)> FindElementsInFrame(IFrame searchFrame, string frameDescription)
                {
                    IElementHandle? foundInput = null;
                    IElementHandle? foundButton = null;

                    // Search for input field
                    foreach (var selector in inputSelectors)
                    {
                        try
                        {
                            var element = await searchFrame.QuerySelectorAsync(selector);
                            if (element != null && await IsElementVisible(element))
                            {
                                foundInput = element;
                                break;
                            }
                        }
                        catch { }
                    }

                    // Search for verify button
                    foreach (var selector in buttonSelectors)
                    {
                        try
                        {
                            var element = await searchFrame.QuerySelectorAsync(selector);
                            if (element != null && await IsElementVisible(element))
                            {
                                foundButton = element;
                                break;
                            }
                        }
                        catch { }
                    }

                    return (foundInput, foundButton);
                }

                IElementHandle? inputField = null;
                IElementHandle? verifyButton = null;

                // Search in current frame first
                var currentFrameElements = await FindElementsInFrame(frame, "current frame");
                inputField = currentFrameElements.input;
                verifyButton = currentFrameElements.button;

                // If both elements found in current frame, we're done
                if (inputField != null && verifyButton != null)
                    return (inputField, verifyButton);

                // Search nested iframes
                if (inputField == null || verifyButton == null)
                {
                    // First pass: try to find both elements in the same nested frame
                    foreach (var childFrame in frame.ChildFrames)
                    {
                        var nestedFrameElements = await FindElementsInFrame(childFrame, "nested iframe");

                        // If both elements found in this frame, prioritize it
                        if (nestedFrameElements.input != null && nestedFrameElements.button != null)
                            return (nestedFrameElements.input, nestedFrameElements.button);

                        // Keep elements we found
                        if (inputField == null && nestedFrameElements.input != null)
                            inputField = nestedFrameElements.input;
                        if (verifyButton == null && nestedFrameElements.button != null)
                            verifyButton = nestedFrameElements.button;

                        // Search deeper nested frames
                        foreach (var deeperFrame in childFrame.ChildFrames)
                        {
                            var deeperFrameElements = await FindElementsInFrame(deeperFrame, "deeper nested iframe");

                            // If both elements found in this deeper frame, prioritize it
                            if (deeperFrameElements.input != null && deeperFrameElements.button != null)
                                return (deeperFrameElements.input, deeperFrameElements.button);

                            // Keep elements we found
                            if (inputField == null && deeperFrameElements.input != null)
                                inputField = deeperFrameElements.input;
                            if (verifyButton == null && deeperFrameElements.button != null)
                                verifyButton = deeperFrameElements.button;
                        }
                    }

                    // Second pass: search iframe elements if ChildFrames didn't work
                    if (inputField == null || verifyButton == null)
                    {
                        var iframes = await frame.QuerySelectorAllAsync("iframe");
                        foreach (var iframe in iframes)
                        {
                            try
                            {
                                var nestedFrame = await iframe.ContentFrameAsync();
                                if (nestedFrame == null) continue;

                                var iframeElements = await FindElementsInFrame(nestedFrame, "iframe content");

                                // If both elements found in this iframe, prioritize it
                                if (iframeElements.input != null && iframeElements.button != null)
                                    return (iframeElements.input, iframeElements.button);

                                // Keep elements we found
                                if (inputField == null && iframeElements.input != null)
                                    inputField = iframeElements.input;
                                if (verifyButton == null && iframeElements.button != null)
                                    verifyButton = iframeElements.button;
                            }
                            catch { }
                        }
                    }
                }

                return (inputField, verifyButton);
            }
            catch
            {
                return (null, null);
            }
        }
        private static void RegisterCleanupState(BotData data, IBrowser? browser, HashSet<string> tempSnapshotBeforeLaunch)
        {
            var cleanupState = new PlaywrightCleanupState(data);
            cleanupState.Register(browser, tempSnapshotBeforeLaunch);
            data.SetObject(PlaywrightCleanupStateKey, cleanupState);
        }

        private static void PerformCleanup(BotData data)
        {
            var cleanupState = data.TryGetObject<PlaywrightCleanupState>(PlaywrightCleanupStateKey);
            cleanupState?.StopManualCloseWatcher();

            var playwrightInstance = data.TryGetObject<IPlaywright>("playwrightInstance");
            if (playwrightInstance != null)
            {
                try
                {
                    playwrightInstance.Dispose();
                }
                catch (Exception ex)
                {
                    data.Logger.Log($"Failed to dispose Playwright instance: {ex.Message}", LogColors.Orange);
                }
            }

            var realBrowserProcessIdObj = data.TryGetObject<object>("playwright.realBrowserProcessId");
            if (realBrowserProcessIdObj is int realBrowserProcessId)
            {
                try
                {
                    var process = System.Diagnostics.Process.GetProcessById(realBrowserProcessId);
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch
                {
                    // Ignore if process is already terminated or inaccessible
                }

                data.SetObject("playwright.realBrowserProcessId", null);
            }

            // ALWAYS kill Playwright Firefox processes on cleanup (even if Close Browser wasn't called)
            // This handles cases where bot stops/errors without explicit browser close
            KillPlaywrightFirefoxProcesses(data);

            DeleteDirectoryIfExists(data, "playwright.tempFirefoxProfile", "temporary Firefox profile");
            DeleteDirectoryIfExists(data, "playwright.tempChromiumUserData", "temporary Chromium user data");
            DeleteTrackedArtifacts(data);

            data.Objects.Remove("playwright");
            data.Objects.Remove("playwrightContext");
            data.Objects.Remove("playwrightPage");
            data.Objects.Remove("playwrightInstance");
            data.Objects.Remove("playwright.tempFirefoxProfile");
            data.Objects.Remove("playwright.tempChromiumUserData");
            data.Objects.Remove("playwright.tempArtifacts");
            data.Objects.Remove("playwright.firefoxProcessIds");
            data.Objects.Remove(PlaywrightCleanupStateKey);
        }

        private static void DeleteDirectoryIfExists(BotData data, string key, string description)
        {
            var directoryPath = data.TryGetObject<string>(key);
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return;
            }

            DeleteFileSystemEntryIfExists(data, directoryPath, description);
        }

        private static void DeleteTrackedArtifacts(BotData data)
        {
            var artifacts = data.TryGetObject<IEnumerable<string>>("playwright.tempArtifacts");
            if (artifacts == null)
            {
                return;
            }

            foreach (var artifactPath in artifacts)
            {
                DeleteFileSystemEntryIfExists(data, artifactPath, "Playwright temporary artifact");
            }
        }

        private static void DeleteFileSystemEntryIfExists(BotData data, string path, string description)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                    data.Logger.Log($"Cleaned up {description}: {path}", LogColors.Yellow);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                    data.Logger.Log($"Cleaned up {description}: {path}", LogColors.Yellow);
                }
            }
            catch (Exception ex)
            {
                data.Logger.Log($"Failed to delete {description} ({path}): {ex.Message}", LogColors.Orange);
            }
        }

        private static void StorePlaywrightTempArtifacts(BotData data, HashSet<string> baseline)
        {
            try
            {
                var currentEntries = CapturePlaywrightTempEntries();
                if (baseline != null && baseline.Count > 0)
                {
                    currentEntries.ExceptWith(baseline);
                }

                if (currentEntries.Count > 0)
                {
                    data.SetObject("playwright.tempArtifacts", currentEntries.ToArray());
                }
                else
                {
                    data.Objects.Remove("playwright.tempArtifacts");
                }
            }
            catch (Exception ex)
            {
                data.Logger.Log($"Failed to track Playwright temporary directories: {ex.Message}", LogColors.Orange);
            }
        }

        private static HashSet<string> CapturePlaywrightTempEntries()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var tempDirectory = Path.GetTempPath();
                foreach (var entry in Directory.EnumerateDirectories(tempDirectory))
                {
                    var name = Path.GetFileName(entry);
                    if (!string.IsNullOrEmpty(name) && IsPlaywrightTempName(name))
                    {
                        result.Add(entry);
                    }
                }
            }
            catch
            {
                // Failing to enumerate temp entries should not block browser launch
            }

            return result;
        }

        private static bool IsPlaywrightTempName(string name)
        {
            return name.StartsWith("playwright", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("pw-", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("ms-playwright", StringComparison.OrdinalIgnoreCase);
        }

        private static readonly string[] FirefoxProcessNames =
        {
            "firefox",
            "nightly",
            "librewolf",
            "camoufox",
            "waterfox"
        };

        private static Dictionary<int, string> CaptureFirefoxProcessSnapshot()
        {
            var snapshot = new Dictionary<int, string>();
            try
            {
                foreach (var process in Process.GetProcesses())
                {
                    if (!IsFirefoxProcessName(process.ProcessName))
                    {
                        continue;
                    }

                    snapshot[process.Id] = SafeGetProcessPath(process);
                }
            }
            catch
            {
                // Swallow - failing to capture snapshot should not block launch
            }

            return snapshot;
        }

        private static void StoreFirefoxProcessDelta(BotData data, Dictionary<int, string> baseline)
        {
            if (baseline == null)
            {
                data.Objects.Remove("playwright.firefoxProcessIds");
                return;
            }

            try
            {
                var current = CaptureFirefoxProcessSnapshot();
                var delta = current.Keys.Except(baseline.Keys).ToArray();
                if (delta.Length > 0)
                {
                    data.SetObject("playwright.firefoxProcessIds", delta, false);
                }
                else
                {
                    data.Objects.Remove("playwright.firefoxProcessIds");
                }
            }
            catch
            {
                data.Objects.Remove("playwright.firefoxProcessIds");
            }
        }

        private static bool IsFirefoxProcessName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            foreach (var alias in FirefoxProcessNames)
            {
                if (name.Equals(alias, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string SafeGetProcessPath(Process process)
        {
            try
            {
                return process.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private sealed class PlaywrightCleanupState
        {
            private readonly BotData _data;
            private IBrowser? _browser;
            private EventHandler<IBrowser>? _browserDisconnectedHandler;
            private int _cleanupTriggered;
            private HashSet<string> _tempSnapshotBeforeLaunch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private CancellationTokenSource? _manualCloseWatcherCts;

            public PlaywrightCleanupState(BotData data)
            {
                _data = data;
            }

            public void Register(IBrowser? browser, HashSet<string> tempSnapshotBeforeLaunch)
            {
                _tempSnapshotBeforeLaunch = tempSnapshotBeforeLaunch != null ? new HashSet<string>(tempSnapshotBeforeLaunch, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _browser = browser;

                if (_browser != null)
                {
                    _browserDisconnectedHandler = (_, _) =>
                    {
                        Cleanup("Playwright browser disconnected unexpectedly. Cleaning up temporary resources.");
                    };
                    _browser.Disconnected += _browserDisconnectedHandler;
                }

                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            }

            public void SuppressBrowserDisconnect()
            {
                if (_browser != null && _browserDisconnectedHandler != null)
                {
                    _browser.Disconnected -= _browserDisconnectedHandler;
                    _browserDisconnectedHandler = null;
                }
            }

            public bool Cleanup(string? logMessage)
            {
                if (Interlocked.Exchange(ref _cleanupTriggered, 1) == 1)
                {
                    return false;
                }

                SuppressBrowserDisconnect();
                StopManualCloseWatcher();
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

                if (!string.IsNullOrWhiteSpace(logMessage))
                {
                    _data.Logger.Log(logMessage!, LogColors.Yellow);
                }

                StorePlaywrightTempArtifacts(_data, _tempSnapshotBeforeLaunch);
                PerformCleanup(_data);
                return true;
            }

            private void OnProcessExit(object? sender, EventArgs e)
            {
                Cleanup(null);
            }

            public void StartManualCloseWatcher(bool enabled)
            {
                StopManualCloseWatcher();

                if (!enabled)
                {
                    return;
                }

                var tracked = _data.TryGetObject<int[]>("playwright.firefoxProcessIds");
                if (tracked == null || tracked.Length == 0)
                {
                    return;
                }

                _manualCloseWatcherCts = new CancellationTokenSource();
                _ = Task.Run(() => MonitorFirefoxManualCloseAsync(_data, tracked, _manualCloseWatcherCts.Token), _manualCloseWatcherCts.Token);
            }

            public void StopManualCloseWatcher()
            {
                if (_manualCloseWatcherCts != null)
                {
                    try
                    {
                        _manualCloseWatcherCts.Cancel();
                    }
                    catch
                    {
                    }
                    finally
                    {
                        _manualCloseWatcherCts.Dispose();
                        _manualCloseWatcherCts = null;
                    }
                }
            }
        }

        /// <summary>
        /// Kills all Firefox processes and deletes Playwright temp profiles.
        /// Aggressive cleanup to ensure no zombie processes or temp files remain.
        /// </summary>
        private static void KillPlaywrightFirefoxProcesses(BotData data)
        {
            var cleanupState = data.TryGetObject<PlaywrightCleanupState>(PlaywrightCleanupStateKey);
            cleanupState?.StopManualCloseWatcher();
            data.Logger.Log("Attempting Firefox cleanup...", LogColors.Yellow);

            try
            {
                var killedPids = KillTrackedFirefoxProcesses(data);

                // Step 2: Delete all Playwright temp profiles from %TEMP%
                try
                {
                    var tempPath = Path.GetTempPath();
                    var patterns = new[] { "playwright-*", "playwright-firefox-*", "playwright_*", "tmp*playwright*" };
                    var deletedCount = 0;
                    
                    foreach (var pattern in patterns)
                    {
                        var dirs = Directory.GetDirectories(tempPath, pattern, SearchOption.TopDirectoryOnly);
                        foreach (var dir in dirs)
                        {
                            try
                            {
                                Directory.Delete(dir, true);
                                deletedCount++;
                            }
                            catch
                            {
                                // In use or permission denied
                            }
                        }
                    }

                    if (deletedCount > 0)
                    {
                        data.Logger.Log($"Deleted {deletedCount} temp profile folder(s)", LogColors.Yellow);
                    }
                }
                catch (Exception ex)
                {
                    data.Logger.Log($"Temp cleanup error: {ex.Message}", LogColors.Orange);
                }

                if (killedPids.Count > 0)
                {
                    data.Logger.Log($"✅ Killed {killedPids.Count} Playwright Firefox process(es)", LogColors.Green);
                }
                else
                {
                    data.Logger.Log("No Playwright Firefox processes to kill", LogColors.Yellow);
                }
            }
            catch (Exception ex)
            {
                data.Logger.Log($"Cleanup failed: {ex.Message}", LogColors.Red);
            }
        }

        private static HashSet<int> KillTrackedFirefoxProcesses(BotData data)
        {
            var killed = new HashSet<int>();
            var tracked = data.TryGetObject<int[]>("playwright.firefoxProcessIds");
            if (tracked == null || tracked.Length == 0)
            {
                return killed;
            }

            foreach (var pid in tracked)
            {
                try
                {
                    var proc = Process.GetProcessById(pid);
                    if (proc.HasExited)
                    {
                        continue;
                    }

                    proc.Kill(true);
                    killed.Add(pid);
                    data.Logger.Log($"  Killed tracked Firefox PID {pid}", LogColors.Yellow);
                }
                catch (Exception ex)
                {
                    data.Logger.Log($"  Failed to kill tracked PID {pid}: {ex.Message}", LogColors.Orange);
                }
            }

            data.Objects.Remove("playwright.firefoxProcessIds");
            return killed;
        }

        private static async Task MonitorFirefoxManualCloseAsync(BotData data, int[] trackedPids, CancellationToken token)
        {
            var logger = data.Logger;
            var seenWindows = new HashSet<int>();

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var anyRunning = false;
                    var manualCloseDetected = false;

                    foreach (var pid in trackedPids)
                    {
                        Process proc;
                        try
                        {
                            proc = Process.GetProcessById(pid);
                        }
                        catch
                        {
                            continue;
                        }

                        if (proc.HasExited)
                        {
                            continue;
                        }

                        anyRunning = true;

                        var handle = SafeGetMainWindowHandle(proc);
                        if (HasVisibleWindow(handle))
                        {
                            seenWindows.Add(pid);
                            continue;
                        }

                        if (seenWindows.Contains(pid))
                        {
                            manualCloseDetected = true;
                            break;
                        }
                    }

                    if (manualCloseDetected)
                    {
                        logger.Log("Detected manual Firefox window close. Cleaning up Playwright resources...", LogColors.Yellow);
                        var cleanupState = data.TryGetObject<PlaywrightCleanupState>(PlaywrightCleanupStateKey);
                        if (cleanupState != null)
                        {
                            cleanupState.Cleanup("Firefox window closed manually. Cleaning up.");
                        }
                        else
                        {
                            KillPlaywrightFirefoxProcesses(data);
                        }
                        return;
                    }

                    if (!anyRunning)
                    {
                        break;
                    }

                    await Task.Delay(ManualClosePollInterval, token);
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.Log($"Manual close watcher error: {ex.Message}", LogColors.Orange);
            }
        }

        private static IntPtr SafeGetMainWindowHandle(Process process)
        {
            try
            {
                return process.MainWindowHandle;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private static bool HasVisibleWindow(IntPtr handle)
        {
            return handle != IntPtr.Zero && IsWindow(handle) && IsWindowVisible(handle);
        }

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
    }
}

