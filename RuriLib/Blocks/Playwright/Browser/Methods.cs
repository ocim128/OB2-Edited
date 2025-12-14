using Microsoft.Playwright;
using RuriLib.Attributes;
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
using System.Threading;

namespace RuriLib.Blocks.Playwright.Browser
{
    [BlockCategory("Browser", "Blocks for managing Playwright browser instances", "#9370db")]
    public static partial class Methods
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

            // Cleanup any previous state
            var previousCleanupState = data.TryGetObject<PlaywrightCleanupState>(PlaywrightCleanupStateKey);
            previousCleanupState?.Cleanup(null);

            var tempEntriesBeforeLaunch = CapturePlaywrightTempEntries();
            var provider = data.Providers.PlaywrightBrowser;

            // Build configuration
            var config = new BrowserLaunchConfig
            {
                BrowserType = browserType ?? provider.BrowserType,
                Headless = headless ?? provider.Headless,
                ExtensionPath = extensionPath,
                FirefoxAddonPath = firefoxAddonPath,
                FirefoxProfilePath = firefoxProfilePath,
                IgnoreHttpsErrors = provider.IgnoreHTTPSErrors
            };

            // Configure extensions and profile
            var args = (extraArgs ?? provider.ExtraArgs)?.ToList() ?? new List<string>();
            ConfigureChromiumExtension(config, args, data);
            config.FirefoxProfilePath = ConfigureFirefoxProfile(config, data);

            // Prepare launch arguments
            PlaywrightLaunchConfigurator.StripIncompatibleFlags(args, config.BrowserType);
            PlaywrightLaunchConfigurator.EnsureSandboxFlags(args, config.BrowserType);
            config.ExtraArgs = args.ToArray();

            // Resolve executable and timeout
            config.Timeout = ResolveLaunchTimeout(provider.TimeoutMilliseconds, config.BrowserType);
            config.ExecutablePath = GetExecutablePath(config.BrowserType, data);
            config.BrowserType = ValidateBrowserType(config.BrowserType, config.ExecutablePath, data);
            StoreBrowserRuntimeState(data, config.BrowserType, config.Headless);

            // Capture Firefox processes before launch for cleanup tracking
            if (config.BrowserType == PlaywrightBrowserType.Firefox)
            {
                config.FirefoxProcessesBeforeLaunch = CaptureFirefoxProcessSnapshot();
                data.Logger.Log("Firefox GPU disabled and sandbox relaxed to avoid RADAR_PRE_LEAK_64.", LogColors.MediumPurple);
            }

            // Create Playwright instance
            Action<string> runtimeLog = message => data.Logger.Log(message, LogColors.MediumPurple);
            var playwright = await PlaywrightRuntimeService.CreateAsync(config.BrowserType, config.ExecutablePath, runtimeLog);
            data.SetObject("playwrightInstance", playwright);

            // Launch browser or context
            IBrowser? browser = null;
            IBrowserContext? context = null;

            try
            {
                context = await LaunchFirefoxWithProfileAsync(playwright, config, data);
                context ??= await LaunchChromiumWithExtensionAsync(playwright, config, data);

                if (context == null)
                {
                    browser = await LaunchRegularBrowserAsync(playwright, config, data);
                }
            }
            catch (Exception ex) when (context == null && browser == null)
            {
                HandleBrowserLaunchError(ex, config.BrowserType, config.ExecutablePath, data);
                throw new Exception($"Failed to launch {config.BrowserType} browser. See details above.", ex);
            }

            // Setup page and register cleanup
            await SetupBrowserPageAsync(browser, context, config, data);
            RegisterCleanupState(data, browser ?? context?.Browser, tempEntriesBeforeLaunch);
            StoreFirefoxProcessDelta(data, config.FirefoxProcessesBeforeLaunch);

            var manualCloseWatcherEnabled = !config.Headless && config.BrowserType == PlaywrightBrowserType.Firefox;
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

            // Perform unified cleanup (handles Firefox processes, temp files, and state)
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
            var currentPage = data.TryGetObject<IPage>("playwrightPage");
            
            // Get browser type from objects dictionary (enum is a value type, can't use TryGetObject)
            PlaywrightBrowserType? browserType = null;
#pragma warning disable CS0618 // Type or member is obsolete
            if (data.Objects.TryGetValue("playwrightBrowserType", out var btObj) && btObj is PlaywrightBrowserType bt)
            {
                browserType = bt;
            }
#pragma warning restore CS0618

            // Prefer using the context to create a new tab (not a new window)
            // browser.NewPageAsync() creates a new context which opens a new window
            IBrowserContext? targetContext = context;
            if (targetContext == null && browser != null)
            {
                // Fall back to the browser's first context if available
                targetContext = browser.Contexts.FirstOrDefault();
            }

            IPage page;
            if (targetContext != null)
            {
                // Firefox has a quirk where context.NewPageAsync() opens a new window instead of a tab
                // Use JavaScript window.open() to force opening as a tab in Firefox
                if (browserType == PlaywrightBrowserType.Firefox && currentPage != null)
                {
                    // Use window.open() which opens as a tab in Firefox
                    var pageTask = targetContext.WaitForPageAsync();
                    await currentPage.EvaluateAsync("() => window.open('about:blank', '_blank')");
                    page = await pageTask;
                    data.Logger.Log("Created a new tab using window.open() for Firefox", LogColors.MediumPurple);
                }
                else
                {
                    page = await targetContext.NewPageAsync();
                    data.Logger.Log("Created a new page (tab)", LogColors.MediumPurple);
                }
            }
            else if (browser != null)
            {
                // Last resort: create a new context (this will open a new window)
                // This should rarely happen as Open Browser always creates a context
                var newContext = await browser.NewContextAsync();
                data.SetObject("playwrightContext", newContext);
                page = await newContext.NewPageAsync();
                data.Logger.Log("Created new context for new page", LogColors.Yellow);
            }
            else
            {
                throw new Exception("No browser or context open. Use the 'Open Browser' block first");
            }

            data.SetObject("playwrightPage", page);
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
        public static void PlaywrightGetPages(BotData data)
        {
            data.Logger.LogHeader();

            var pages = GetAllPages(data);
            data.SetObject("playwrightPages", pages);

            data.Logger.Log($"Found {pages.Length} open pages", LogColors.MediumPurple);
        }

        [Block("Switches to a page by index", name = "Switch to Page")]
        public static void PlaywrightSwitchToPage(BotData data, int index)
        {
            data.Logger.LogHeader();

            var pages = GetAllPages(data);

            if (index < 0 || index >= pages.Length)
                throw new ArgumentException($"Invalid page index {index}. Available pages: {pages.Length}");

            var page = pages[index];
            data.SetObject("playwrightPage", page);

            data.Logger.Log($"Switched to page {index}", LogColors.MediumPurple);
        }

        /// <summary>
        /// Gets all pages from browser contexts or from direct context.
        /// Supports both regular browser and persistent context (Firefox profiles).
        /// </summary>
        private static IPage[] GetAllPages(BotData data)
        {
            var browser = data.TryGetObject<IBrowser>(PlaywrightHelpers.Keys.Browser);
            var context = data.TryGetObject<IBrowserContext>(PlaywrightHelpers.Keys.Context);

            // Try to get pages from browser contexts first
            if (browser != null)
            {
                var pages = browser.Contexts.SelectMany(c => c.Pages).ToArray();
                if (pages.Length > 0)
                {
                    return pages;
                }
            }

            // Fall back to direct context pages (for persistent contexts)
            if (context != null)
            {
                return context.Pages.ToArray();
            }

            throw new Exception("No browser open. Use the 'Open Browser' block first.");
        }

        private static IBrowser GetBrowser(BotData data) => PlaywrightHelpers.GetBrowser(data);

        private static IPage GetPage(BotData data) => PlaywrightHelpers.GetPage(data);

        private static int ResolveLaunchTimeout(int configuredTimeout, PlaywrightBrowserType browserType)
        {
            var baseline = configuredTimeout > 0 ? configuredTimeout : 30000;
            if (browserType == PlaywrightBrowserType.Firefox)
            {
                return Math.Max(baseline, 60000);
            }

            return baseline;
        }

        private static void StoreBrowserRuntimeState(BotData data, PlaywrightBrowserType browserType, bool headless)
        {
            data.SetObject("playwrightBrowserType", browserType);
            data.SetObject("playwrightHeadless", headless);
        }

    }
}
