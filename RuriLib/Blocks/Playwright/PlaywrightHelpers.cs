using Microsoft.Playwright;
using RuriLib.Functions.Puppeteer;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RuriLib.Blocks.Playwright
{
    /// <summary>
    /// Shared helper methods for Playwright blocks.
    /// Centralizes common operations to avoid code duplication across partial classes.
    /// </summary>
    internal static class PlaywrightHelpers
    {
        #region Object Keys
        
        /// <summary>Keys used for storing/retrieving Playwright objects in BotData.</summary>
        public static class Keys
        {
            public const string Browser = "playwright";
            public const string Page = "playwrightPage";
            public const string Context = "playwrightContext";
            public const string Instance = "playwrightInstance";
            public const string Frame = "playwrightFrame";
            public const string CleanupState = "playwright.cleanupState";
            public const string FirefoxProcessIds = "playwright.firefoxProcessIds";
            public const string TempFirefoxProfile = "playwright.tempFirefoxProfile";
            public const string TempChromiumUserData = "playwright.tempChromiumUserData";
            public const string TempArtifacts = "playwright.tempArtifacts";
            public const string BrowserType = "playwrightBrowserType";
            public const string Headless = "playwrightHeadless";
            public const string RealBrowserProcessId = "playwright.realBrowserProcessId";
        }

        #endregion

        #region Core Accessors

        /// <summary>
        /// Gets the current Playwright page from BotData.
        /// </summary>
        /// <exception cref="Exception">Thrown when no page is available.</exception>
        public static IPage GetPage(BotData data)
        {
            var page = data.TryGetObject<IPage>(Keys.Page);
            return page ?? throw new Exception("No page available. Use the 'Open Browser' or 'New Page' block first.");
        }

        /// <summary>
        /// Gets the current frame, falling back to the main frame of the page if not set.
        /// </summary>
        /// <exception cref="Exception">Thrown when no page is available.</exception>
        public static IFrame GetFrame(BotData data)
        {
            var frame = data.TryGetObject<IFrame>(Keys.Frame);
            return frame ?? GetPage(data).MainFrame;
        }

        /// <summary>
        /// Gets the current browser from BotData.
        /// </summary>
        /// <exception cref="Exception">Thrown when no browser is open.</exception>
        public static IBrowser GetBrowser(BotData data)
        {
            var browser = data.TryGetObject<IBrowser>(Keys.Browser);
            return browser ?? throw new Exception("No browser open. Use the 'Open Browser' block first.");
        }

        /// <summary>
        /// Gets the current browser context from BotData.
        /// </summary>
        /// <exception cref="Exception">Thrown when no context is available.</exception>
        public static IBrowserContext GetContext(BotData data)
        {
            var context = data.TryGetObject<IBrowserContext>(Keys.Context);
            return context ?? throw new Exception("No browser context available. Use the 'Open Browser' block first.");
        }

        /// <summary>
        /// Tries to get the browser, returning null if not available.
        /// </summary>
        public static IBrowser? TryGetBrowser(BotData data)
        {
            return data.TryGetObject<IBrowser>(Keys.Browser);
        }

        /// <summary>
        /// Tries to get the browser context, returning null if not available.
        /// </summary>
        public static IBrowserContext? TryGetContext(BotData data)
        {
            return data.TryGetObject<IBrowserContext>(Keys.Context);
        }

        #endregion

        #region Logging Helpers

        /// <summary>
        /// Logs a method start with header and action message.
        /// Uses MediumPurple color by default.
        /// </summary>
        public static void LogMethodStart(BotData data, string action)
        {
            data.Logger.LogHeader();
            data.Logger.Log(action, LogColors.MediumPurple);
        }

        /// <summary>
        /// Logs a method start with header and action message using a custom color.
        /// </summary>
        public static void LogMethodStart(BotData data, string action, string color)
        {
            data.Logger.LogHeader();
            data.Logger.Log(action, color);
        }

        #endregion

        #region Window Helpers

        /// <summary>
        /// Safely gets the main window handle of a process, returning IntPtr.Zero on failure.
        /// </summary>
        public static IntPtr SafeGetMainWindowHandle(Process process)
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

        /// <summary>
        /// Checks if a window handle represents a visible window.
        /// </summary>
        public static bool HasVisibleWindow(IntPtr handle)
        {
            return handle != IntPtr.Zero && NativeMethods.IsWindow(handle) && NativeMethods.IsWindowVisible(handle);
        }

        #endregion

        #region Native Methods

        /// <summary>
        /// P/Invoke declarations for Windows user32.dll functions.
        /// Centralized here to avoid duplication across Playwright block files.
        /// </summary>
        public static class NativeMethods
        {
            [DllImport("user32.dll")]
            public static extern bool IsWindow(IntPtr hWnd);

            [DllImport("user32.dll")]
            public static extern bool IsWindowVisible(IntPtr hWnd);

            [DllImport("user32.dll")]
            public static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

            [DllImport("user32.dll")]
            public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

            [StructLayout(LayoutKind.Sequential)]
            public struct Rect
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }
        }

        #endregion

        #region Option Helpers

        /// <summary>
        /// Creates Playwright options with Timeout set using reflection.
        /// Used for page-level operations that only need timeout.
        /// </summary>
        public static T CreateOptions<T>(int timeoutSeconds) where T : new()
        {
            var options = new T();
            var timeoutProperty = typeof(T).GetProperty("Timeout");
            timeoutProperty?.SetValue(options, (float)(timeoutSeconds * 1000));
            return options;
        }

        /// <summary>
        /// Creates Playwright options with Timeout and Force=true set using reflection.
        /// Used for element operations that need to bypass strict actionability checks.
        /// </summary>
        public static T CreateOptionsWithForce<T>(int timeoutSeconds) where T : new()
        {
            var options = new T();
            var timeoutProperty = typeof(T).GetProperty("Timeout");
            timeoutProperty?.SetValue(options, (float)(timeoutSeconds * 1000));

            // Ensure operations work even when the browser is not in the foreground
            var forceProperty = typeof(T).GetProperty("Force");
            forceProperty?.SetValue(options, true);

            return options;
        }

        #endregion

        #region Selector Helpers

        /// <summary>
        /// Builds a CSS/Playwright selector string from a FindElementBy type and identifier.
        /// </summary>
        public static string BuildSelector(FindElementBy findBy, string identifier)
        {
            return findBy switch
            {
                FindElementBy.Id => $"#{identifier}",
                FindElementBy.ClassName => $".{identifier}",
                FindElementBy.CssSelector => identifier,
                FindElementBy.Selector => identifier,
                FindElementBy.TagName => identifier,
                FindElementBy.Name => $"[name='{identifier}']",
                FindElementBy.LinkText => $"text={identifier}",
                FindElementBy.PartialLinkText => $"text*={identifier}",
                _ => identifier
            };
        }

        #endregion
    }
}
