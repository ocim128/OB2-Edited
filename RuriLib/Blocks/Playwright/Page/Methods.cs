using Microsoft.Playwright;
using RuriLib.Attributes;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.IO;
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
            data.SetObject("playwrightFrame", page.MainFrame);

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
            data.SetObject("playwrightFrame", page.MainFrame);
            data.Logger.Log("Reloaded the page", LogColors.MediumPurple);
        }

        [Block("Goes back in browser history", name = "Go Back")]
        public static async Task PlaywrightGoBack(BotData data, int timeoutSeconds = 30)
        {
            LogMethodStart(data, "Going back in history");
            var page = GetPage(data);
            await page.GoBackAsync(CreatePageOptions<PageGoBackOptions>(timeoutSeconds));
            // Switch to main frame after navigation
            data.SetObject("playwrightFrame", page.MainFrame);
            data.Logger.Log("Went back in history", LogColors.MediumPurple);
        }

        [Block("Goes forward in browser history", name = "Go Forward")]
        public static async Task PlaywrightGoForward(BotData data, int timeoutSeconds = 30)
        {
            LogMethodStart(data, "Going forward in history");
            var page = GetPage(data);
            await page.GoForwardAsync(CreatePageOptions<PageGoForwardOptions>(timeoutSeconds));
            // Switch to main frame after navigation
            data.SetObject("playwrightFrame", page.MainFrame);
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
            var response = await frame.EvaluateAsync(expression);

            var json = response != null ? response.ToString() : "undefined";

            data.Logger.Log($"Evaluated {expression}", LogColors.MediumPurple);
            data.Logger.Log($"Got result: {json}", LogColors.MediumPurple);

            return json;
        }

        private static IFrame GetFrame(BotData data)
        {
            var frame = data.TryGetObject<IFrame>("playwrightFrame");
            return frame ?? GetPage(data).MainFrame;
        }

        [Block("Sets the viewport size", name = "Set Viewport")]
        public static async Task PlaywrightSetViewport(BotData data, int width, int height)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            await page.SetViewportSizeAsync(width, height);

            data.Logger.Log($"Set viewport to {width}x{height}", LogColors.MediumPurple);
        }

        [Block("Sets a custom user agent", name = "Set User Agent")]
        public static async Task PlaywrightSetUserAgent(BotData data, string userAgent)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            await page.SetExtraHTTPHeadersAsync(new Dictionary<string, string> { { "User-Agent", userAgent } });

            data.Logger.Log($"Set user agent: {userAgent}", LogColors.MediumPurple);
        }

        [Block("Switches to the main frame of the page", name = "Switch to Main Frame")]
        public static void PlaywrightSwitchToMainFrame(BotData data)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            data.SetObject("playwrightFrame", page.MainFrame);
            data.Logger.Log("Switched to main frame", LogColors.MediumPurple);
        }

        private static IPage GetPage(BotData data)
        {
            var page = data.TryGetObject<IPage>("playwrightPage");
            return page ?? throw new Exception("No page available. Use the 'New Page' block first");
        }

        private static void LogMethodStart(BotData data, string action)
        {
            data.Logger.LogHeader();
            data.Logger.Log(action, LogColors.MediumPurple);
        }

        private static T CreatePageOptions<T>(int timeoutSeconds) where T : new()
        {
            var options = new T();
            if (typeof(T).GetProperty("Timeout") != null)
            {
                typeof(T).GetProperty("Timeout")!.SetValue(options, (float)(timeoutSeconds * 1000));
            }
            return options;
        }

        private static PageWaitForLoadStateOptions CreateWaitOptions(int timeoutSeconds)
        {
            return new PageWaitForLoadStateOptions { Timeout = timeoutSeconds * 1000f };
        }
    }
}