using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Support.UI;
using RuriLib.Attributes;
using RuriLib.Helpers.Android;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using RuriLib.Models.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Android.Elements
{
    [BlockCategory("Android Elements", "Blocks for interacting with Android UI elements", "#8BC34A")]
    public static class Methods
    {
        /// <summary>
        /// Clicks an element by selector.
        /// </summary>
        [Block("Clicks an Android UI element", name = "Android Click")]
        public static async Task AndroidClick(BotData data,
            AndroidSelectorType selectorType,
            string selector,
            int timeoutMs = 5000)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);
            var locator = AndroidHelpers.BuildLocator(selectorType, selector);

            data.Logger.Log($"Clicking element: {selectorType}='{selector}'", LogColors.LimeGreen);

            await Task.Run(() =>
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromMilliseconds(timeoutMs));
                var element = wait.Until(d => d.FindElement(locator));
                element.Click();
            });

            data.Logger.Log("Element clicked!", LogColors.LimeGreen);
        }

        /// <summary>
        /// Types text into an element.
        /// </summary>
        [Block("Types text into an Android UI element", name = "Android Type")]
        public static async Task AndroidType(BotData data,
            AndroidSelectorType selectorType,
            string selector,
            string text,
            bool clearFirst = true,
            int timeoutMs = 5000)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);
            var locator = AndroidHelpers.BuildLocator(selectorType, selector);

            data.Logger.Log($"Typing into element: {selectorType}='{selector}'", LogColors.LimeGreen);

            await Task.Run(() =>
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromMilliseconds(timeoutMs));
                var element = wait.Until(d => d.FindElement(locator));
                
                if (clearFirst)
                {
                    element.Clear();
                }
                element.SendKeys(text);
            });

            data.Logger.Log($"Typed: {text}", LogColors.LimeGreen);
        }

        /// <summary>
        /// Gets text from an element.
        /// </summary>
        [Block("Gets text from an Android UI element", name = "Android Get Text")]
        public static async Task<string> AndroidGetText(BotData data,
            AndroidSelectorType selectorType,
            string selector,
            int timeoutMs = 5000)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);
            var locator = AndroidHelpers.BuildLocator(selectorType, selector);

            data.Logger.Log($"Getting text from: {selectorType}='{selector}'", LogColors.LimeGreen);

            var text = await Task.Run(() =>
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromMilliseconds(timeoutMs));
                var element = wait.Until(d => d.FindElement(locator));
                return element.Text;
            });

            data.Logger.Log($"Text: {text}", LogColors.LimeGreen);

            return text;
        }

        /// <summary>
        /// Gets attribute value from an element.
        /// </summary>
        [Block("Gets an attribute value from an Android UI element", name = "Android Get Attribute")]
        public static async Task<string> AndroidGetAttribute(BotData data,
            AndroidSelectorType selectorType,
            string selector,
            string attributeName,
            int timeoutMs = 5000)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);
            var locator = AndroidHelpers.BuildLocator(selectorType, selector);

            data.Logger.Log($"Getting attribute '{attributeName}' from: {selectorType}='{selector}'", LogColors.LimeGreen);

            var value = await Task.Run(() =>
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromMilliseconds(timeoutMs));
                var element = wait.Until(d => d.FindElement(locator));
                return element.GetAttribute(attributeName) ?? string.Empty;
            });

            data.Logger.Log($"Attribute value: {value}", LogColors.LimeGreen);

            return value;
        }

        /// <summary>
        /// Waits for an element to appear.
        /// </summary>
        [Block("Waits for an Android UI element to appear", name = "Android Wait Element")]
        public static async Task AndroidWaitElement(BotData data,
            AndroidSelectorType selectorType,
            string selector,
            int timeoutMs = 10000)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);
            var locator = AndroidHelpers.BuildLocator(selectorType, selector);

            data.Logger.Log($"Waiting for element: {selectorType}='{selector}'", LogColors.LimeGreen);

            await Task.Run(() =>
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromMilliseconds(timeoutMs));
                wait.Until(d => d.FindElement(locator));
            });

            data.Logger.Log("Element found!", LogColors.LimeGreen);
        }

        /// <summary>
        /// Checks if an element exists.
        /// </summary>
        [Block("Checks if an Android UI element exists", name = "Android Element Exists")]
        public static async Task<bool> AndroidElementExists(BotData data,
            AndroidSelectorType selectorType,
            string selector,
            int timeoutMs = 2000)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);
            var locator = AndroidHelpers.BuildLocator(selectorType, selector);

            data.Logger.Log($"Checking if element exists: {selectorType}='{selector}'", LogColors.LimeGreen);

            var exists = await Task.Run(() =>
            {
                try
                {
                    var wait = new WebDriverWait(driver, TimeSpan.FromMilliseconds(timeoutMs));
                    wait.Until(d => d.FindElement(locator));
                    return true;
                }
                catch (WebDriverTimeoutException)
                {
                    return false;
                }
                catch (NoSuchElementException)
                {
                    return false;
                }
            });

            data.Logger.Log($"Element exists: {exists}", LogColors.LimeGreen);

            return exists;
        }

        /// <summary>
        /// Clears an input element.
        /// </summary>
        [Block("Clears an Android input element", name = "Android Clear")]
        public static async Task AndroidClear(BotData data,
            AndroidSelectorType selectorType,
            string selector,
            int timeoutMs = 5000)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);
            var locator = AndroidHelpers.BuildLocator(selectorType, selector);

            data.Logger.Log($"Clearing element: {selectorType}='{selector}'", LogColors.LimeGreen);

            await Task.Run(() =>
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromMilliseconds(timeoutMs));
                var element = wait.Until(d => d.FindElement(locator));
                element.Clear();
            });

            data.Logger.Log("Element cleared!", LogColors.LimeGreen);
        }

        /// <summary>
        /// Finds multiple elements matching selector.
        /// </summary>
        [Block("Finds all matching Android UI elements", name = "Android Find Elements")]
        public static async Task<List<string>> AndroidFindElements(BotData data,
            AndroidSelectorType selectorType,
            string selector,
            string attributeToGet = "text")
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);
            var locator = AndroidHelpers.BuildLocator(selectorType, selector);

            data.Logger.Log($"Finding elements: {selectorType}='{selector}'", LogColors.LimeGreen);

            var results = await Task.Run(() =>
            {
                var elements = driver.FindElements(locator);
                return elements.Select(e => 
                    attributeToGet.ToLower() == "text" 
                        ? e.Text 
                        : e.GetAttribute(attributeToGet) ?? string.Empty
                ).ToList();
            });

            data.Logger.Log($"Found {results.Count} elements", LogColors.LimeGreen);

            return results;
        }

        /// <summary>
        /// Long presses an element.
        /// </summary>
        [Block("Long presses an Android UI element", name = "Android Long Press Element")]
        public static async Task AndroidLongPressElement(BotData data,
            AndroidSelectorType selectorType,
            string selector,
            int durationMs = 1000,
            int timeoutMs = 5000)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);
            var locator = AndroidHelpers.BuildLocator(selectorType, selector);

            data.Logger.Log($"Long pressing element: {selectorType}='{selector}'", LogColors.LimeGreen);

            await Task.Run(() =>
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromMilliseconds(timeoutMs));
                var element = wait.Until(d => d.FindElement(locator));
                
                // Get element center coordinates
                var location = element.Location;
                var size = element.Size;
                var centerX = location.X + size.Width / 2;
                var centerY = location.Y + size.Height / 2;

                // Perform long press using W3C Actions
                var finger = new OpenQA.Selenium.Interactions.PointerInputDevice(OpenQA.Selenium.Interactions.PointerKind.Touch, "finger");
                var actions = new OpenQA.Selenium.Interactions.ActionSequence(finger);
                
                actions.AddAction(finger.CreatePointerMove(OpenQA.Selenium.Interactions.CoordinateOrigin.Viewport, centerX, centerY, TimeSpan.Zero));
                actions.AddAction(finger.CreatePointerDown(OpenQA.Selenium.Interactions.MouseButton.Left));
                actions.AddAction(finger.CreatePause(TimeSpan.FromMilliseconds(durationMs)));
                actions.AddAction(finger.CreatePointerUp(OpenQA.Selenium.Interactions.MouseButton.Left));
                
                driver.PerformActions(new List<OpenQA.Selenium.Interactions.ActionSequence> { actions });
            });

            data.Logger.Log("Long press completed!", LogColors.LimeGreen);
        }

        /// <summary>
        /// Scrolls to find an element.
        /// </summary>
        [Block("Scrolls to find an Android UI element", name = "Android Scroll To Element")]
        public static async Task AndroidScrollToElement(BotData data,
            string scrollableSelector,
            string targetText,
            int maxScrolls = 10)
        {
            data.Logger.LogHeader();

            var driver = AndroidHelpers.GetDriver(data);

            data.Logger.Log($"Scrolling to find: '{targetText}'", LogColors.LimeGreen);

            await Task.Run(() =>
            {
                // Use UiScrollable to scroll to element
                var scrollCommand = $"new UiScrollable(new UiSelector().scrollable(true)).scrollIntoView(new UiSelector().textContains(\"{targetText}\"))";
                driver.FindElement(MobileBy.AndroidUIAutomator(scrollCommand));
            });

            data.Logger.Log($"Found element with text: '{targetText}'", LogColors.LimeGreen);
        }
    }
}

