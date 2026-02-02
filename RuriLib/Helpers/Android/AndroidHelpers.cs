using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using RuriLib.Models.Bots;
using RuriLib.Models.Settings;

namespace RuriLib.Helpers.Android
{
    /// <summary>
    /// Helper utilities for Android Appium automation.
    /// </summary>
    public static class AndroidHelpers
    {
        /// <summary>
        /// Keys used to store Android objects in BotData.
        /// </summary>
        public static class Keys
        {
            public const string DriverWrapper = "androidDriverWrapper";
            public const string AppPackage = "androidAppPackage";
            public const string DeviceId = "androidDeviceId";
        }

        /// <summary>
        /// Gets the AndroidDriver from BotData.
        /// </summary>
        /// <exception cref="System.Exception">Thrown when no driver is connected.</exception>
        public static AndroidDriver GetDriver(BotData data)
        {
            var wrapper = data.TryGetObject<AndroidDriverWrapper>(Keys.DriverWrapper);
            if (wrapper == null)
            {
                throw new System.Exception("No Android driver connected. Use the 'Android Connect' block first.");
            }
            return wrapper.Driver;
        }

        /// <summary>
        /// Tries to get the AndroidDriver from BotData, returns null if not found.
        /// </summary>
        public static AndroidDriver? TryGetDriver(BotData data)
        {
            var wrapper = data.TryGetObject<AndroidDriverWrapper>(Keys.DriverWrapper);
            return wrapper?.Driver;
        }

        /// <summary>
        /// Gets the wrapper for cleanup purposes.
        /// </summary>
        public static AndroidDriverWrapper? TryGetWrapper(BotData data)
        {
            return data.TryGetObject<AndroidDriverWrapper>(Keys.DriverWrapper);
        }

        /// <summary>
        /// Builds an Appium locator based on selector type.
        /// </summary>
        public static By BuildLocator(AndroidSelectorType selectorType, string selector)
        {
            return selectorType switch
            {
                AndroidSelectorType.Id => By.Id(selector),
                AndroidSelectorType.XPath => By.XPath(selector),
                AndroidSelectorType.AccessibilityId => MobileBy.AccessibilityId(selector),
                AndroidSelectorType.ClassName => By.ClassName(selector),
                AndroidSelectorType.Text => By.XPath($"//*[@text='{EscapeXPath(selector)}']"),
                AndroidSelectorType.PartialText => By.XPath($"//*[contains(@text, '{EscapeXPath(selector)}')]"),
                AndroidSelectorType.UiAutomator => MobileBy.AndroidUIAutomator(selector),
                _ => throw new System.ArgumentException($"Unknown selector type: {selectorType}")
            };
        }

        /// <summary>
        /// Escapes special characters in XPath strings.
        /// </summary>
        private static string EscapeXPath(string value)
        {
            if (!value.Contains('\''))
            {
                return value;
            }
            if (!value.Contains('"'))
            {
                return value;
            }
            // Handle strings with both single and double quotes
            return "concat('" + value.Replace("'", "', \"'\", '") + "')";
        }

        /// <summary>
        /// Validates that a driver is connected and responsive.
        /// </summary>
        public static bool IsConnected(BotData data)
        {
            try
            {
                var driver = TryGetDriver(data);
                if (driver == null) return false;
                
                // Try to get page source to verify connection
                _ = driver.PageSource;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

