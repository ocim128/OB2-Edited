using Microsoft.Playwright;
using RuriLib.Attributes;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using RuriLib.Models.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Playwright.Page
{
    [BlockCategory("Page", "Blocks for interacting with Playwright pages", "#4169e1")]
    public static class Methods
    {
        [Block("Navigates to a URL", name = "Navigate To")]
        public static async Task PlaywrightNavigateTo(BotData data, string url, int timeoutSeconds = 30, WaitUntilState waitUntil = WaitUntilState.Load, string referer = "")
        {
            LogMethodStart(data, $"Navigating to {url}");
            var page = GetPage(data);

            // Configure navigation options with customizable timeout, wait condition and optional referer
            var options = CreatePageOptions<PageGotoOptions>(timeoutSeconds);
            options.WaitUntil = waitUntil;
            if (!string.IsNullOrWhiteSpace(referer))
                options.Referer = referer;

            var response = await page.GotoAsync(url, options);

            // Switch to main frame after navigation
            PlaywrightHelpers.SetFrame(data, page.MainFrame);

            // Provide richer logging information (status code when available)
            var statusInfo = response != null ? $" | Status: {response.Status}" : string.Empty;
            data.Logger.Log($"Navigated to {url}{statusInfo}", LogColors.MediumPurple);
        }

        [Block("Reloads the current page", name = "Reload")]
        public static async Task PlaywrightReload(BotData data, int timeoutSeconds = 30)
        {
            LogMethodStart(data, "Reloading page");
            var page = GetPage(data);
            await page.ReloadAsync(CreatePageOptions<PageReloadOptions>(timeoutSeconds));
            // Switch to main frame after reload
            PlaywrightHelpers.SetFrame(data, page.MainFrame);
            data.Logger.Log("Reloaded the page", LogColors.MediumPurple);
        }

        [Block("Goes back in browser history", name = "Go Back")]
        public static async Task PlaywrightGoBack(BotData data, int timeoutSeconds = 30)
        {
            LogMethodStart(data, "Going back in history");
            var page = GetPage(data);
            await page.GoBackAsync(CreatePageOptions<PageGoBackOptions>(timeoutSeconds));
            // Switch to main frame after navigation
            PlaywrightHelpers.SetFrame(data, page.MainFrame);
            data.Logger.Log("Went back in history", LogColors.MediumPurple);
        }

        [Block("Goes forward in browser history", name = "Go Forward")]
        public static async Task PlaywrightGoForward(BotData data, int timeoutSeconds = 30)
        {
            LogMethodStart(data, "Going forward in history");
            var page = GetPage(data);
            await page.GoForwardAsync(CreatePageOptions<PageGoForwardOptions>(timeoutSeconds));
            // Switch to main frame after navigation
            PlaywrightHelpers.SetFrame(data, page.MainFrame);
            data.Logger.Log("Went forward in history", LogColors.MediumPurple);
        }

        [Block("Gets the current page URL", name = "Get URL")]
        public static async Task PlaywrightGetUrl(BotData data, string variableName = "url")
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            var url = frame.Url;
            data.SetObject(variableName, url);

            data.Logger.Log($"Got URL: {url}", LogColors.MediumPurple);
        }

        [Block("Gets the page title", name = "Get Title")]
        public static async Task PlaywrightGetTitle(BotData data, string variableName = "title")
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            var title = await frame.TitleAsync();
            data.SetObject(variableName, title);

            data.Logger.Log($"Got title: {title}", LogColors.MediumPurple);
        }

        [Block("Gets the page source HTML", name = "Get Source")]
        public static async Task PlaywrightGetSource(BotData data, string variableName = "source")
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            var source = await frame.ContentAsync();
            data.SetObject(variableName, source);

            data.Logger.Log($"Got page source ({source.Length} characters)", LogColors.MediumPurple);
        }

        [Block("Takes a screenshot of the page", name = "Screenshot")]
        public static async Task PlaywrightScreenshot(BotData data, string filePath, bool fullPage = false)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = filePath,
                FullPage = fullPage
            });

            data.Logger.Log($"Screenshot saved to {filePath}", LogColors.MediumPurple);
        }

        [Block("Waits for a specific amount of time", name = "Wait")]
        public static async Task PlaywrightWait(BotData data, int milliseconds)
        {
            data.Logger.LogHeader();

            await Task.Delay(milliseconds);

            data.Logger.Log($"Waited {milliseconds} ms", LogColors.MediumPurple);
        }

        [Block("Waits for page to load", name = "Wait for Load")]
        public static async Task PlaywrightWaitForLoad(BotData data, int timeoutSeconds = 30)
        {
            LogMethodStart(data, "Waiting for page to load");
            var frame = GetFrame(data);
            await frame.WaitForLoadStateAsync(LoadState.Load, new FrameWaitForLoadStateOptions { Timeout = timeoutSeconds * 1000 });
            data.Logger.Log("Page loaded", LogColors.MediumPurple);
        }

        [Block("Waits for network to be idle", name = "Wait for Network Idle")]
        public static async Task PlaywrightWaitForNetworkIdle(BotData data, int timeoutSeconds = 30)
        {
            LogMethodStart(data, "Waiting for network to be idle");
            var frame = GetFrame(data);
            await frame.WaitForLoadStateAsync(LoadState.NetworkIdle, new FrameWaitForLoadStateOptions { Timeout = timeoutSeconds * 1000 });
            data.Logger.Log("Network is idle", LogColors.MediumPurple);
        }

        [Block("Evaluates a js expression in the current page and returns a json response", name = "Execute JS")]
        public static async Task<string> PlaywrightExecuteJs(BotData data, [MultiLine] string expression)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            // Use eval on the page side so large snippets (with quotes/backticks) do not break Playwright parsing
            var response = await frame.EvaluateAsync<object>("(script) => eval(script)", expression);

            var json = response != null ? response.ToString() : "undefined";

            data.Logger.Log($"Evaluated {expression}", LogColors.MediumPurple);
            data.Logger.Log($"Got result: {json}", LogColors.MediumPurple);

            return json;
        }

        private static async Task<bool> TryResizeChromiumWindowAsync(IPage page, int width, int height)
        {
            try
            {
                var session = await page.Context.NewCDPSessionAsync(page).ConfigureAwait(false);
                var windowInfo = await session.SendAsync("Browser.getWindowForTarget").ConfigureAwait(false);
                var windowId = ExtractWindowId(windowInfo);

                if (windowId == 0)
                    return false;

                var parameters = new Dictionary<string, object>
                {
                    ["windowId"] = windowId,
                    ["bounds"] = new Dictionary<string, object>
                    {
                        ["width"] = width,
                        ["height"] = height,
                        ["windowState"] = "normal"
                    }
                };

                await session.SendAsync("Browser.setWindowBounds", parameters).ConfigureAwait(false);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int ExtractWindowId(object windowInfo)
        {
            if (windowInfo == null)
                return 0;

            if (windowInfo is JsonElement json && json.ValueKind == JsonValueKind.Object && json.TryGetProperty("windowId", out var idElement))
            {
                return idElement.GetInt32();
            }

            if (windowInfo is IDictionary<string, object> dict && dict.TryGetValue("windowId", out var dictValue))
            {
                return Convert.ToInt32(dictValue);
            }

            var type = windowInfo.GetType();
            var property = type.GetProperty("windowId") ?? type.GetProperty("WindowId");
            if (property?.GetValue(windowInfo) is object value)
            {
                return Convert.ToInt32(value);
            }

            return 0;
        }

        private static async Task<bool> TryResizeFirefoxWindowAsync(BotData data, IPage page, int width, int height)
        {
            try
            {
                var chromeOffsets = await page.EvaluateAsync<JsonElement>(@"() => ({
                    width: window.outerWidth - window.innerWidth,
                    height: window.outerHeight - window.innerHeight
                })").ConfigureAwait(false);

                var chromeWidth = chromeOffsets.TryGetProperty("width", out var wProp) ? wProp.GetInt32() : 0;
                var chromeHeight = chromeOffsets.TryGetProperty("height", out var hProp) ? hProp.GetInt32() : 0;

                var targetWidth = width + chromeWidth;
                var targetHeight = height + chromeHeight;

                var trackedPids = data.PlaywrightSession.FirefoxProcessIds;
                if (trackedPids == null || trackedPids.Length == 0)
                {
                    return false;
                }

                foreach (var pid in trackedPids)
                {
                    try
                    {
                        var proc = Process.GetProcessById(pid);
                        if (proc.HasExited)
                            continue;

                        var handle = PlaywrightHelpers.SafeGetMainWindowHandle(proc);
                        if (!PlaywrightHelpers.HasVisibleWindow(handle))
                            continue;

                        if (!PlaywrightHelpers.NativeMethods.GetWindowRect(handle, out var rect))
                            continue;

                        PlaywrightHelpers.NativeMethods.MoveWindow(handle, rect.Left, rect.Top, targetWidth, targetHeight, true);
                        return true;
                    }
                    catch
                    {
                        // Ignore and continue with next process
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static IFrame GetFrame(BotData data) => PlaywrightHelpers.GetFrame(data);

        [Block("Scrolls to the top of the page", name = "Scroll to Top")]
        public static async Task PlaywrightScrollToTop(BotData data)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            await page.EvaluateAsync("window.scrollTo(0, 0);");
            data.Logger.Log("Scrolled to the top of the page", LogColors.MediumPurple);
        }

        [Block("Scrolls to the bottom of the page", name = "Scroll to Bottom")]
        public static async Task PlaywrightScrollToBottom(BotData data)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight);");
            data.Logger.Log("Scrolled to the bottom of the page", LogColors.MediumPurple);
        }

        [Block("Sets the viewport size", name = "Set Viewport")]
        public static async Task PlaywrightSetViewport(BotData data, int width, int height)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            var headless = data.PlaywrightSession.Headless ?? data.Providers.PlaywrightBrowser.Headless;
            var browserType = data.PlaywrightSession.BrowserType ?? data.Providers.PlaywrightBrowser.BrowserType;

            if (!headless && browserType == PlaywrightBrowserType.Chromium)
            {
                if (await TryResizeChromiumWindowAsync(page, width, height))
                {
                    data.Logger.Log($"Resized browser window to {width}x{height}", LogColors.MediumPurple);
                    return;
                }

                data.Logger.Log("Failed to resize Chromium window via DevTools, falling back to viewport resize", LogColors.Orange);
            }
            else if (!headless && browserType == PlaywrightBrowserType.Firefox)
            {
                if (await TryResizeFirefoxWindowAsync(data, page, width, height))
                {
                    data.Logger.Log($"Resized browser window to {width}x{height}", LogColors.MediumPurple);
                    return;
                }

                data.Logger.Log("Failed to resize Firefox window, falling back to viewport resize", LogColors.Orange);
            }

            await page.SetViewportSizeAsync(width, height);

            data.Logger.Log($"Set viewport to {width}x{height}", LogColors.MediumPurple);
        }

        [Block("Sets a custom user agent", name = "Set User Agent")]
        public static async Task PlaywrightSetUserAgent(BotData data, string userAgent)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            var userAgentOverrideScript =
                $"Object.defineProperty(navigator, 'userAgent', {{ get: () => {JsonSerializer.Serialize(userAgent)}, configurable: true }});";

            // Override the HTTP header for server-side UA checks
            await page.SetExtraHTTPHeadersAsync(new Dictionary<string, string> { { "User-Agent", userAgent } });

            // Persist the JS-side override for future navigations, then update the current document immediately.
            await page.AddInitScriptAsync(userAgentOverrideScript);
            await page.EvaluateAsync(userAgentOverrideScript);

            data.Logger.Log($"Set user agent: {userAgent}", LogColors.MediumPurple);
        }

        [Block("Switches to the main frame of the page", name = "Switch to Main Frame")]
        public static void PlaywrightSwitchToMainFrame(BotData data)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            PlaywrightHelpers.SetFrame(data, page.MainFrame);
            data.Logger.Log("Switched to main frame", LogColors.MediumPurple);
        }

        private static IPage GetPage(BotData data) => PlaywrightHelpers.GetPage(data);

        private static void LogMethodStart(BotData data, string action) => PlaywrightHelpers.LogMethodStart(data, action);

        private static T CreatePageOptions<T>(int timeoutSeconds) where T : new()
            => PlaywrightHelpers.CreateOptions<T>(timeoutSeconds);

        private static PageWaitForLoadStateOptions CreateWaitOptions(int timeoutSeconds)
        {
            return new PageWaitForLoadStateOptions { Timeout = timeoutSeconds * 1000f };
        }
    }
}
