using Microsoft.Playwright;
using RuriLib.Attributes;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using RuriLib.Models.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Playwright.Browser
{
    [BlockCategory("Browser", "Blocks for managing Playwright browser instances", "#9370db")]
    public static class Methods
    {
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

            var provider = data.Providers.PlaywrightBrowser;
            var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            data.SetObject("playwrightInstance", playwright);

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

            var launchOptions = new BrowserTypeLaunchOptions
            {
                Headless = actualHeadless,
                Args = finalArgs.ToArray(),
                Timeout = provider.TimeoutMilliseconds
            };

            // Efficient browser executable path handling
            launchOptions.ExecutablePath = GetExecutablePath(actualBrowserType, data);

            // Validate and correct browser type if needed
            actualBrowserType = ValidateBrowserType(actualBrowserType, launchOptions.ExecutablePath, data);

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
                        Args = finalArgs.ToArray(),
                        Timeout = provider.TimeoutMilliseconds,
                        ExecutablePath = launchOptions.ExecutablePath
                    };
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
                        Args = finalArgs.ToArray(),
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
                        Args = finalArgs.ToArray(),
                        Timeout = provider.TimeoutMilliseconds
                        // ExecutablePath is intentionally omitted to use built-in Firefox
                    };
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

                // Check if persistent context already has pages
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
        }

        [Block("Closes an open playwright browser", name = "Close Browser")]
        public static async Task PlaywrightCloseBrowser(BotData data)
        {
            data.Logger.LogHeader();

            var context = data.TryGetObject<IBrowserContext>("playwrightContext");
            var browser = data.TryGetObject<IBrowser>("playwright");

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

            var playwright = data.TryGetObject<IPlaywright>("playwrightInstance");
            playwright?.Dispose();

            // Clean up real browser process if it exists
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
                    // Ignore if process doesn't exist
                }
                data.SetObject("playwright.realBrowserProcessId", null);
            }

            // Clean up temporary directories
            var tempFirefoxProfile = data.TryGetObject<string>("playwright.tempFirefoxProfile");
            if (!string.IsNullOrEmpty(tempFirefoxProfile) && Directory.Exists(tempFirefoxProfile))
            {
                try
                {
                    Directory.Delete(tempFirefoxProfile, true);
                    data.Logger.Log($"🗑️ Cleaned up temporary Firefox profile: {tempFirefoxProfile}", LogColors.Yellow);
                }
                catch (Exception ex)
                {
                    data.Logger.Log($"⚠️ Failed to delete temporary Firefox profile {tempFirefoxProfile}: {ex.Message}", LogColors.Orange);
                }
            }

            var tempChromiumUserData = data.TryGetObject<string>("playwright.tempChromiumUserData");
            if (!string.IsNullOrEmpty(tempChromiumUserData) && Directory.Exists(tempChromiumUserData))
            {
                try
                {
                    Directory.Delete(tempChromiumUserData, true);
                    data.Logger.Log($"🗑️ Cleaned up temporary Chromium user data: {tempChromiumUserData}", LogColors.Yellow);
                }
                catch (Exception ex)
                {
                    data.Logger.Log($"⚠️ Failed to delete temporary Chromium user data {tempChromiumUserData}: {ex.Message}", LogColors.Orange);
                }
            }

            // Clear browser-related objects
            data.Objects.Remove("playwright");
            data.Objects.Remove("playwrightContext");
            data.Objects.Remove("playwrightPage");
            data.Objects.Remove("playwrightInstance");
            data.Objects.Remove("playwright.tempFirefoxProfile");
            data.Objects.Remove("playwright.tempChromiumUserData");

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
    }
}