using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using RuriLib.Attributes;
using RuriLib.Helpers.Android;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Android.Driver
{
    [BlockCategory("Android Driver", "Blocks for managing Android Appium driver connections", "#4CAF50")]
    public static class Methods
    {
        /// <summary>
        /// Connects to an Android emulator or device via Appium.
        /// </summary>
        [Block("Connects to an Android emulator/device via Appium", name = "Android Connect")]
        public static async Task AndroidConnect(BotData data,
            string appiumUrl = "http://127.0.0.1:4723",
            string deviceId = "emulator-5554",
            string platformVersion = "11.0",
            string appPackage = "",
            string appActivity = "",
            bool noReset = true,
            int commandTimeoutSeconds = 60)
        {
            data.Logger.LogHeader();

            // Check if already connected
            var existingDriver = AndroidHelpers.TryGetDriver(data);
            if (existingDriver != null)
            {
                data.Logger.Log("Already connected to Android device. Disconnect first to reconnect.", LogColors.DarkOrange);
                return;
            }

            data.Logger.Log($"Connecting to Appium server at {appiumUrl}...", LogColors.LimeGreen);
            data.Logger.Log($"Device: {deviceId}, Platform: Android {platformVersion}", LogColors.LimeGreen);

            var options = new AppiumOptions
            {
                PlatformName = "Android",
                AutomationName = "UiAutomator2",
                DeviceName = deviceId,
                PlatformVersion = platformVersion
            };

            // Add optional capabilities
            if (!string.IsNullOrEmpty(appPackage))
            {
                options.AddAdditionalAppiumOption("appPackage", appPackage);
                data.Logger.Log($"App Package: {appPackage}", LogColors.LimeGreen);
            }

            if (!string.IsNullOrEmpty(appActivity))
            {
                options.AddAdditionalAppiumOption("appActivity", appActivity);
                data.Logger.Log($"App Activity: {appActivity}", LogColors.LimeGreen);
            }

            options.AddAdditionalAppiumOption("noReset", noReset);
            options.AddAdditionalAppiumOption("newCommandTimeout", commandTimeoutSeconds);

            // Connect to Appium server
            var serverUri = new Uri(appiumUrl);
            var driver = await Task.Run(() => new AndroidDriver(serverUri, options, TimeSpan.FromSeconds(commandTimeoutSeconds))).ConfigureAwait(false);

            // Wrap driver in disposable wrapper for automatic cleanup when bot ends
            var wrapper = new AndroidDriverWrapper(driver);
            
            // Store wrapper and metadata (wrapper is IDisposable, so BotData will auto-cleanup)
            data.SetObject(AndroidHelpers.Keys.DriverWrapper, wrapper);
            data.SetObject(AndroidHelpers.Keys.DeviceId, deviceId);
            if (!string.IsNullOrEmpty(appPackage))
            {
                data.SetObject(AndroidHelpers.Keys.AppPackage, appPackage);
            }

            data.Logger.Log("Successfully connected to Android device!", LogColors.LimeGreen);
            data.Logger.Log("Note: Driver will be automatically cleaned up when bot ends.", LogColors.Yellow);
        }

        /// <summary>
        /// Disconnects from the Android device.
        /// </summary>
        [Block("Disconnects from Android device", name = "Android Disconnect")]
        public static async Task AndroidDisconnect(BotData data)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.TryGetDriver(data);
            if (driver == null)
            {
                data.Logger.Log("No Android driver connected.", LogColors.DarkOrange);
                return;
            }

            var wrapper = AndroidHelpers.TryGetWrapper(data);
            
            await Task.Run(() =>
            {
                try
                {
                    wrapper?.Dispose(); // This calls driver.Quit() internally
                }
                catch (Exception ex)
                {
                    data.Logger.Log($"Warning during disconnect: {ex.Message}", LogColors.Yellow);
                }
            }).ConfigureAwait(false);

            // Note: We don't need to manually clear objects as wrapper is already disposed
            // Setting to null removes reference (optional, but explicit)

            data.Logger.Log("Disconnected from Android device.", LogColors.LimeGreen);
        }

        /// <summary>
        /// Launches an app by package name.
        /// </summary>
        [Block("Launches an Android app by package name", name = "Android Launch App")]
        public static async Task AndroidLaunchApp(BotData data,
            string appPackage,
            string appActivity = "")
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);

            data.Logger.Log($"Launching app: {appPackage}", LogColors.LimeGreen);

            await Task.Run(() =>
            {
                if (!string.IsNullOrEmpty(appActivity))
                {
                    // Use mobile: startActivity for launching specific activity
                    var args = new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "appPackage", appPackage },
                        { "appActivity", appActivity }
                    };
                    driver.ExecuteScript("mobile: startActivity", args);
                }
                else
                {
                    driver.ActivateApp(appPackage);
                }
            }).ConfigureAwait(false);

            data.SetObject(AndroidHelpers.Keys.AppPackage, appPackage);
            data.Logger.Log($"App {appPackage} launched successfully!", LogColors.LimeGreen);
        }

        /// <summary>
        /// Closes the current app.
        /// </summary>
        [Block("Closes the current Android app", name = "Android Close App")]
        public static async Task AndroidCloseApp(BotData data, string appPackage = "")
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);

            // Use provided package or get stored one
            var packageToClose = !string.IsNullOrEmpty(appPackage) 
                ? appPackage 
                : data.TryGetObject<string>(AndroidHelpers.Keys.AppPackage);

            if (string.IsNullOrEmpty(packageToClose))
            {
                throw new Exception("No app package specified and no app was previously launched.");
            }

            data.Logger.Log($"Closing app: {packageToClose}", LogColors.LimeGreen);

            await Task.Run(() => driver.TerminateApp(packageToClose)).ConfigureAwait(false);

            data.Logger.Log($"App {packageToClose} closed.", LogColors.LimeGreen);
        }

        /// <summary>
        /// Gets the current app package name.
        /// </summary>
        [Block("Gets the current foreground app package", name = "Android Get Current App")]
        public static async Task<string> AndroidGetCurrentApp(BotData data)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);

            var currentPackage = await Task.Run(() => driver.CurrentPackage).ConfigureAwait(false);

            data.Logger.Log($"Current app: {currentPackage}", LogColors.LimeGreen);

            return currentPackage;
        }

        /// <summary>
        /// Checks if the Android driver is connected.
        /// </summary>
        [Block("Checks if connected to Android device", name = "Android Is Connected")]
        public static bool AndroidIsConnected(BotData data)
        {
            data.Logger.LogHeader();

            var isConnected = AndroidHelpers.IsConnected(data);

            data.Logger.Log($"Connected: {isConnected}", LogColors.LimeGreen);

            return isConnected;
        }
    }
}
