using Microsoft.Playwright;
using RuriLib.Attributes;
using RuriLib.Functions.Puppeteer;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Playwright.Elements
{
    [BlockCategory("Elements", "Blocks for interacting with elements on Playwright pages", "#9370db")]
    public static class Methods
    {
        [Block("Clicks on an element", name = "Click Element")]
        public static async Task PlaywrightClickElement(BotData data, FindElementBy findBy, string identifier, int index = 0, int timeoutSeconds = 30)
        {
            LogMethodStart(data, $"Clicking element: {findBy} {identifier}");
            var page = GetPage(data);

            if (findBy == FindElementBy.XPath)
            {
                var elements = await page.Locator("xpath=" + identifier).AllAsync();
                if (elements.Count <= index)
                    throw new Exception($"Expected at least {index + 1} elements to be found but {elements.Count} were found");
                await elements[index].ClickAsync(CreateElementOptions<LocatorClickOptions>(timeoutSeconds));
            }
            else
            {
                var selector = BuildSelector(findBy, identifier);
                var elements = await page.Locator(selector).AllAsync();
                if (elements.Count <= index)
                    throw new Exception($"Expected at least {index + 1} elements to be found but {elements.Count} were found");
                await elements[index].ClickAsync(CreateElementOptions<LocatorClickOptions>(timeoutSeconds));
            }

            data.Logger.Log($"Clicked element: {findBy} {identifier} at index {index}", LogColors.Tomato);
        }

        [Block("Types text into an element", name = "Type Text")]
        [Obsolete]

        public static async Task PlaywrightTypeText(BotData data, FindElementBy findBy, string identifier, string text, int index = 0, int timeoutSeconds = 30, int delayMs = 0)
        {
            LogMethodStart(data, $"Typing text into element: {findBy} {identifier}");
            var page = GetPage(data);

            if (findBy == FindElementBy.XPath)
            {
                var elements = await page.Locator("xpath=" + identifier).AllAsync();
                if (elements.Count <= index)
                    throw new Exception($"Expected at least {index + 1} elements to be found but {elements.Count} were found");
                var options = CreateElementOptions<LocatorTypeOptions>(timeoutSeconds);
                if (delayMs > 0) options.Delay = delayMs;
                await elements[index].TypeAsync(text, options);
            }
            else
            {
                var selector = BuildSelector(findBy, identifier);
                var elements = await page.Locator(selector).AllAsync();
                if (elements.Count <= index)
                    throw new Exception($"Expected at least {index + 1} elements to be found but {elements.Count} were found");
                var options = CreateElementOptions<LocatorTypeOptions>(timeoutSeconds);
                if (delayMs > 0) options.Delay = delayMs;
                await elements[index].TypeAsync(text, options);
            }

            data.Logger.Log($"Typed '{text}' into element: {findBy} {identifier} at index {index}", LogColors.Tomato);
        }

        [Block("Fills an input element with text", name = "Fill Element")]
        public static async Task PlaywrightFillElement(BotData data, FindElementBy findBy, string identifier, string text, int index = 0, int timeoutSeconds = 30)
        {
            LogMethodStart(data, $"Filling element: {findBy} {identifier}");
            var page = GetPage(data);

            if (findBy == FindElementBy.XPath)
            {
                var elements = await page.Locator("xpath=" + identifier).AllAsync();
                if (elements.Count <= index)
                    throw new Exception($"Expected at least {index + 1} elements to be found but {elements.Count} were found");
                await elements[index].FillAsync(text, CreateElementOptions<LocatorFillOptions>(timeoutSeconds));
            }
            else
            {
                var selector = BuildSelector(findBy, identifier);
                var elements = await page.Locator(selector).AllAsync();
                if (elements.Count <= index)
                    throw new Exception($"Expected at least {index + 1} elements to be found but {elements.Count} were found");
                await elements[index].FillAsync(text, CreateElementOptions<LocatorFillOptions>(timeoutSeconds));
            }

            data.Logger.Log($"Filled element {findBy} {identifier} at index {index} with: {text}", LogColors.Tomato);
        }

        [Block("Clears an input element", name = "Clear Element")]
        public static async Task PlaywrightClearElement(BotData data, string selector, int timeoutSeconds = 30)
        {
            LogMethodStart(data, $"Clearing element: {selector}");
            var page = GetPage(data);
            await page.FillAsync(selector, "", CreateElementOptions<PageFillOptions>(timeoutSeconds));
            data.Logger.Log($"Cleared element: {selector}", LogColors.Tomato);
        }

        [Block("Gets text content from an element", name = "Get Text")]
        public static async Task PlaywrightGetText(BotData data, string selector, string variableName = "text", int timeoutSeconds = 30)
        {
            LogMethodStart(data, $"Getting text from element: {selector}");
            var page = GetPage(data);
            var element = await page.WaitForSelectorAsync(selector, CreateWaitOptions(timeoutSeconds));
            var text = await element.TextContentAsync();
            data.SetObject(variableName, text ?? "");
            data.Logger.Log($"Got text from {selector}: {text}", LogColors.Tomato);
        }

        [Block("Gets inner text from an element", name = "Get Inner Text")]
        public static async Task PlaywrightGetInnerText(BotData data, FindElementBy findBy, string identifier, string variableName = "innerText", int index = 0, int timeoutSeconds = 30)
        {
            LogMethodStart(data, $"Getting inner text from element: {findBy} {identifier}");
            var page = GetPage(data);
            string innerText;

            if (findBy == FindElementBy.XPath)
            {
                var elements = await page.Locator("xpath=" + identifier).AllAsync();
                if (elements.Count <= index)
                    throw new Exception($"Expected at least {index + 1} elements to be found but {elements.Count} were found");
                innerText = await elements[index].InnerTextAsync(CreateElementOptions<LocatorInnerTextOptions>(timeoutSeconds));
            }
            else
            {
                var selector = BuildSelector(findBy, identifier);
                var elements = await page.Locator(selector).AllAsync();
                if (elements.Count <= index)
                    throw new Exception($"Expected at least {index + 1} elements to be found but {elements.Count} were found");
                innerText = await elements[index].InnerTextAsync(CreateElementOptions<LocatorInnerTextOptions>(timeoutSeconds));
            }

            data.SetObject(variableName, innerText);
            data.Logger.Log($"Got inner text from {findBy} {identifier} at index {index}: {innerText}", LogColors.Tomato);
        }

        [Block("Gets inner HTML from an element", name = "Get Inner HTML")]
        public static async Task PlaywrightGetInnerHTML(BotData data, string selector, string variableName = "innerHTML", int timeoutSeconds = 30)
        {
            LogMethodStart(data, $"Getting inner HTML from element: {selector}");
            var page = GetPage(data);
            var innerHTML = await page.InnerHTMLAsync(selector, CreateElementOptions<PageInnerHTMLOptions>(timeoutSeconds));
            data.SetObject(variableName, innerHTML);
            data.Logger.Log($"Got inner HTML from {selector} ({innerHTML?.Length ?? 0} characters)", LogColors.Tomato);
        }

        [Block("Gets the value of an attribute of an element", name = "Get Attribute Value")]
        public static async Task<string> PlaywrightGetAttributeValue(BotData data, FindElementBy findBy, string identifier, int index,
            string attributeName = "innerText", int timeoutSeconds = 30)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            string elemScript;
            if (findBy == FindElementBy.XPath)
            {
                elemScript = $"document.evaluate(\"{identifier.Replace("\"", "\\\"")}\", document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue";
            }
            else
            {
                var selector = BuildSelector(findBy, identifier);
                elemScript = $"document.querySelectorAll('{selector}')[{index}]";
            }
            var script = $"{elemScript}.{attributeName};";
            var value = await page.EvaluateAsync<string>(script);

            data.Logger.Log($"Got value {value} of attribute {attributeName} by executing {script}", LogColors.Tomato);
            return value;
        }

        [Block("Sets an attribute value on an element", name = "Set Attribute")]
        public static async Task PlaywrightSetAttribute(BotData data, string selector, string attributeName, string value, int timeoutSeconds = 30)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            await page.EvaluateAsync($"document.querySelector('{selector}').setAttribute('{attributeName}', '{value}')");

            data.Logger.Log($"Set attribute '{attributeName}' to '{value}' on element: {selector}", LogColors.Tomato);
        }

        [Block("Checks if an element exists", name = "Element Exists")]
        public static async Task PlaywrightElementExists(BotData data, FindElementBy findBy, string identifier, string variableName = "exists", int index = 0, int timeoutSeconds = 5)
        {
            LogMethodStart(data, $"Checking if element exists: {findBy} {identifier}");
            var page = GetPage(data);
            bool exists = false;

            try
            {
                if (findBy == FindElementBy.XPath)
                {
                    var elements = await page.Locator("xpath=" + identifier).AllAsync();
                    exists = elements.Count > index;
                }
                else
                {
                    var selector = BuildSelector(findBy, identifier);
                    var elements = await page.Locator(selector).AllAsync();
                    exists = elements.Count > index;
                }
            }
            catch
            {
                // Element doesn't exist
            }

            data.SetObject(variableName, exists);
            data.Logger.Log($"Element {findBy} {identifier} at index {index} exists: {exists}", LogColors.Tomato);
        }

        [Block("Waits for an element to appear", name = "Wait For Element")]
        public static async Task PlaywrightWaitForElement(BotData data, FindElementBy findBy, string identifier, int index = 0, int timeoutSeconds = 30)
        {
            LogMethodStart(data, $"Waiting for element: {findBy} {identifier}");
            var page = GetPage(data);

            if (findBy == FindElementBy.XPath)
            {
                await page.Locator("xpath=" + identifier).Nth(index).WaitForAsync(CreateElementOptions<LocatorWaitForOptions>(timeoutSeconds));
            }
            else
            {
                var selector = BuildSelector(findBy, identifier);
                await page.Locator(selector).Nth(index).WaitForAsync(CreateElementOptions<LocatorWaitForOptions>(timeoutSeconds));
            }

            data.Logger.Log($"Element appeared: {findBy} {identifier} at index {index}", LogColors.Tomato);
        }

        [Block("Hovers over an element", name = "Hover Element")]
        public static async Task PlaywrightHoverElement(BotData data, string selector, int timeoutSeconds = 30)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            await page.HoverAsync(selector, new PageHoverOptions { Timeout = timeoutSeconds * 1000 });

            data.Logger.Log($"Hovered over element: {selector}", LogColors.Tomato);
        }

        [Block("Double clicks on an element", name = "Double Click")]
        public static async Task PlaywrightDoubleClick(BotData data, string selector, int timeoutSeconds = 30)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            await page.DblClickAsync(selector, new PageDblClickOptions { Timeout = timeoutSeconds * 1000 });

            data.Logger.Log($"Double clicked element: {selector}", LogColors.Tomato);
        }

        [Block("Right clicks on an element", name = "Right Click")]
        public static async Task PlaywrightRightClick(BotData data, string selector, int timeoutSeconds = 30)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            await page.ClickAsync(selector, new PageClickOptions
            {
                Timeout = timeoutSeconds * 1000,
                Button = MouseButton.Right
            });

            data.Logger.Log($"Right clicked element: {selector}", LogColors.Tomato);
        }

        [Block("Selects an option from a dropdown", name = "Select Option")]
        public static async Task PlaywrightSelectOption(BotData data, string selector, string value, int timeoutSeconds = 30)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            await page.SelectOptionAsync(selector, value, new PageSelectOptionOptions { Timeout = timeoutSeconds * 1000 });

            data.Logger.Log($"Selected option '{value}' from dropdown: {selector}", LogColors.Tomato);
        }

        [Block("Checks a checkbox or radio button", name = "Check Element")]
        public static async Task PlaywrightCheckElement(BotData data, string selector, int timeoutSeconds = 30)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            await page.CheckAsync(selector, new PageCheckOptions { Timeout = timeoutSeconds * 1000 });

            data.Logger.Log($"Checked element: {selector}", LogColors.Tomato);
        }

        [Block("Unchecks a checkbox", name = "Uncheck Element")]
        public static async Task PlaywrightUncheckElement(BotData data, string selector, int timeoutSeconds = 30)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            await page.UncheckAsync(selector, new PageUncheckOptions { Timeout = timeoutSeconds * 1000 });

            data.Logger.Log($"Unchecked element: {selector}", LogColors.Tomato);
        }

        [Block("Focuses on an element", name = "Focus Element")]
        public static async Task PlaywrightFocusElement(BotData data, string selector, int timeoutSeconds = 30)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            await page.FocusAsync(selector, new PageFocusOptions { Timeout = timeoutSeconds * 1000 });

            data.Logger.Log($"Focused on element: {selector}", LogColors.Tomato);
        }

        [Block("Presses a key on an element", name = "Press Key")]
        public static async Task PlaywrightPressKey(BotData data, FindElementBy findBy, string identifier, string key, int index = 0, int timeoutSeconds = 30)
        {
            LogMethodStart(data, $"Pressing key '{key}' on element: {findBy} {identifier}");
            var page = GetPage(data);

            if (findBy == FindElementBy.XPath)
            {
                var elements = await page.Locator("xpath=" + identifier).AllAsync();
                if (elements.Count <= index)
                    throw new Exception($"Expected at least {index + 1} elements to be found but {elements.Count} were found");
                await elements[index].PressAsync(key, new LocatorPressOptions { Timeout = timeoutSeconds * 1000 });
            }
            else
            {
                var selector = BuildSelector(findBy, identifier);
                var elements = await page.Locator(selector).AllAsync();
                if (elements.Count <= index)
                    throw new Exception($"Expected at least {index + 1} elements to be found but {elements.Count} were found");
                await elements[index].PressAsync(key, new LocatorPressOptions { Timeout = timeoutSeconds * 1000 });
            }

            data.Logger.Log($"Pressed key '{key}' on element: {findBy} {identifier} at index {index}", LogColors.Tomato);
        }

        private static IPage GetPage(BotData data)
        {
            var page = data.TryGetObject<IPage>("playwrightPage");
            return page ?? throw new Exception("No page available. Use the 'New Page' block first");
        }

        private static void LogMethodStart(BotData data, string action)
        {
            data.Logger.LogHeader();
            data.Logger.Log(action, LogColors.Tomato);
        }

        private static T CreateElementOptions<T>(int timeoutSeconds) where T : new()
        {
            var options = new T();
            if (typeof(T).GetProperty("Timeout") != null)
            {
                typeof(T).GetProperty("Timeout")!.SetValue(options, (float)(timeoutSeconds * 1000));
            }
            return options;
        }

        private static PageWaitForSelectorOptions CreateWaitOptions(int timeoutSeconds, WaitForSelectorState state = WaitForSelectorState.Visible)
        {
            return new PageWaitForSelectorOptions
            {
                Timeout = timeoutSeconds * 1000f,
                State = state
            };
        }

        private static string BuildSelector(FindElementBy findBy, string identifier)
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
    }
}