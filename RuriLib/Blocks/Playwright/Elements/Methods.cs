using Microsoft.Playwright;
using RuriLib.Attributes;
using RuriLib.Functions.Puppeteer;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            var frame = GetFrame(data);

            if (findBy == FindElementBy.XPath)
            {
                var elements = await frame.Locator("xpath=" + identifier).AllAsync();
                if (elements.Count <= index)
                    throw new Exception($"Expected at least {index + 1} elements to be found but {elements.Count} were found");
                await elements[index].ClickAsync(CreateElementOptions<LocatorClickOptions>(timeoutSeconds));
            }
            else
            {
                var selector = BuildSelector(findBy, identifier);
                var elements = await frame.Locator(selector).AllAsync();
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
            var frame = GetFrame(data);
            var selector = findBy == FindElementBy.XPath ? "xpath=" + identifier : BuildSelector(findBy, identifier);
            var elements = await frame.Locator(selector).AllAsync();

            if (elements.Count <= index)
                throw new Exception($"Expected at least {index + 1} elements to be found but {elements.Count} were found");

            var target = elements[index];

            try
            {
                await target.FillAsync(text, CreateElementOptions<LocatorFillOptions>(timeoutSeconds));
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("not an <input>, <textarea> or [contenteditable] element"))
            {
                // FillAsync retargets to the closest <button>/[role=button] ancestor, so it refuses an
                // editable element nested inside a clickable wrapper (e.g. the Threads composer).
                // Only take over when the element itself really is editable, otherwise the caller
                // pointed the block at the wrong element and must see that instead.
                if (!await IsEditableAsync(target))
                {
                    throw new Exception($"The element {findBy} {identifier} at index {index} is not a text input or editable element. " +
                        "Target the element with contenteditable=\"true\" or role=\"textbox\" instead of a wrapper or button that only contains it.");
                }

                await SelectAllTextAsync(target);
                await GetPage(data).Keyboard.InsertTextAsync(text);

                data.Logger.Log($"Filled element {findBy} {identifier} at index {index} by typing into it (editable element inside a clickable wrapper)", LogColors.Tomato);
                return;
            }

            data.Logger.Log($"Filled element {findBy} {identifier} at index {index} with: {text}", LogColors.Tomato);
        }

        private static async Task<bool> IsEditableAsync(ILocator locator)
            => await locator.EvaluateAsync<bool>(
                "e => e.isContentEditable === true || e.getAttribute('role') === 'textbox'");

        // Mirrors what Playwright's fill does for contenteditable elements: select everything
        // so the following insert replaces it, without relying on a platform-specific hotkey.
        private static async Task SelectAllTextAsync(ILocator locator)
            => await locator.EvaluateAsync(@"e => {
                   e.focus();
                   const range = document.createRange();
                   range.selectNodeContents(e);
                   const selection = window.getSelection();
                   selection.removeAllRanges();
                   selection.addRange(range);
               }");

        [Block("Clears an input element", name = "Clear Element")]
        public static async Task PlaywrightClearElement(BotData data, string selector, int timeoutSeconds = 30)
        {
            LogMethodStart(data, $"Clearing element: {selector}");
            var frame = GetFrame(data);
            await frame.FillAsync(selector, "", CreateElementOptions<FrameFillOptions>(timeoutSeconds));
            data.Logger.Log($"Cleared element: {selector}", LogColors.Tomato);
        }

        [Block("Gets text content from an element", name = "Get Text")]
        public static async Task PlaywrightGetText(BotData data, string selector, string variableName = "text", int timeoutSeconds = 30)
        {
            LogMethodStart(data, $"Getting text from element: {selector}");
            var frame = GetFrame(data);
            var element = await frame.WaitForSelectorAsync(selector, new FrameWaitForSelectorOptions { Timeout = timeoutSeconds * 1000 });
            var text = await element.TextContentAsync();
            data.SetObject(variableName, text ?? "");
            data.Logger.Log($"Got text from {selector}: {text}", LogColors.Tomato);
        }

        [Block("Gets inner text from an element", name = "Get Inner Text")]
        public static async Task PlaywrightGetInnerText(BotData data, FindElementBy findBy, string identifier, string variableName = "innerText", int index = 0, int timeoutSeconds = 30)
        {
            LogMethodStart(data, $"Getting inner text from element: {findBy} {identifier}");
            var frame = GetFrame(data);
            string innerText;

            if (findBy == FindElementBy.XPath)
            {
                var elements = await frame.Locator("xpath=" + identifier).AllAsync();
                if (elements.Count <= index)
                    throw new Exception($"Expected at least {index + 1} elements to be found but {elements.Count} were found");
                innerText = await elements[index].InnerTextAsync(CreateElementOptions<LocatorInnerTextOptions>(timeoutSeconds));
            }
            else
            {
                var selector = BuildSelector(findBy, identifier);
                var elements = await frame.Locator(selector).AllAsync();
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
            var frame = GetFrame(data);
            var innerHTML = await frame.InnerHTMLAsync(selector, new FrameInnerHTMLOptions { Timeout = timeoutSeconds * 1000 });
            data.SetObject(variableName, innerHTML);
            data.Logger.Log($"Got inner HTML from {selector} ({innerHTML?.Length ?? 0} characters)", LogColors.Tomato);
        }

        [Block("Gets the value of an attribute of an element", name = "Get Attribute Value")]
        public static async Task<string> PlaywrightGetAttributeValue(BotData data, FindElementBy findBy, string identifier, int index,
            string attributeName = "innerText", int timeoutSeconds = 30)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
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
            var value = await frame.EvaluateAsync<string>(script);

            data.Logger.Log($"Got value {value} of attribute {attributeName} by executing {script}", LogColors.Tomato);
            return value;
        }

        [Block("Sets an attribute value on an element", name = "Set Attribute")]
        public static async Task PlaywrightSetAttribute(BotData data, string selector, string attributeName, string value, int timeoutSeconds = 30)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            await frame.EvaluateAsync($"document.querySelector('{selector}').setAttribute('{attributeName}', '{value}')");

            data.Logger.Log($"Set attribute '{attributeName}' to '{value}' on element: {selector}", LogColors.Tomato);
        }

        [Block("Checks if an element exists", name = "Element Exists")]
        public static async Task PlaywrightElementExists(BotData data, FindElementBy findBy, string identifier, string variableName = "exists", int index = 0, int timeoutSeconds = 5)
        {
            LogMethodStart(data, $"Checking if element exists: {findBy} {identifier}");
            var frame = GetFrame(data);
            bool exists = false;

            try
            {
                if (findBy == FindElementBy.XPath)
                {
                    var elements = await frame.Locator("xpath=" + identifier).AllAsync();
                    exists = elements.Count > index;
                }
                else
                {
                    var selector = BuildSelector(findBy, identifier);
                    var elements = await frame.Locator(selector).AllAsync();
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
            var frame = GetFrame(data);

            if (findBy == FindElementBy.XPath)
            {
                await frame.Locator("xpath=" + identifier).Nth(index).WaitForAsync(CreateElementOptions<LocatorWaitForOptions>(timeoutSeconds));
            }
            else
            {
                var selector = BuildSelector(findBy, identifier);
                await frame.Locator(selector).Nth(index).WaitForAsync(CreateElementOptions<LocatorWaitForOptions>(timeoutSeconds));
            }

            data.Logger.Log($"Element appeared: {findBy} {identifier} at index {index}", LogColors.Tomato);
        }

        [Block("Hovers over an element", name = "Hover Element")]
        public static async Task PlaywrightHoverElement(BotData data,
            [BlockParam("Find By")] FindElementBy findBy = FindElementBy.Selector,
            [BlockParam("Identifier")] string selector = "",
            int index = 0,
            int timeoutSeconds = 30)
        {
            LogMethodStart(data, $"Hovering over element: {findBy} {selector}");

            var frame = GetFrame(data);
            await GetLocator(frame, findBy, selector, index)
                .HoverAsync(CreateElementOptions<LocatorHoverOptions>(timeoutSeconds));

            data.Logger.Log($"Hovered over element: {findBy} {selector} at index {index}", LogColors.Tomato);
        }

        [Block("Double clicks on an element", name = "Double Click")]
        public static async Task PlaywrightDoubleClick(BotData data, string selector, int timeoutSeconds = 30)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            await frame.DblClickAsync(selector, new FrameDblClickOptions { Timeout = timeoutSeconds * 1000 });

            data.Logger.Log($"Double clicked element: {selector}", LogColors.Tomato);
        }

        [Block("Right clicks on an element", name = "Right Click")]
        public static async Task PlaywrightRightClick(BotData data, string selector, int timeoutSeconds = 30)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            await frame.ClickAsync(selector, new FrameClickOptions
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

            var frame = GetFrame(data);
            await frame.SelectOptionAsync(selector, value, new FrameSelectOptionOptions { Timeout = timeoutSeconds * 1000 });

            data.Logger.Log($"Selected option '{value}' from dropdown: {selector}", LogColors.Tomato);
        }

        [Block("Checks a checkbox or radio button", name = "Check Element")]
        public static async Task PlaywrightCheckElement(BotData data, string selector, int timeoutSeconds = 30)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            await frame.CheckAsync(selector, new FrameCheckOptions { Timeout = timeoutSeconds * 1000 });

            data.Logger.Log($"Checked element: {selector}", LogColors.Tomato);
        }

        [Block("Unchecks a checkbox", name = "Uncheck Element")]
        public static async Task PlaywrightUncheckElement(BotData data, string selector, int timeoutSeconds = 30)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            await frame.UncheckAsync(selector, new FrameUncheckOptions { Timeout = timeoutSeconds * 1000 });

            data.Logger.Log($"Unchecked element: {selector}", LogColors.Tomato);
        }

        [Block("Focuses on an element", name = "Focus Element")]
        public static async Task PlaywrightFocusElement(BotData data,
            [BlockParam("Find By")] FindElementBy findBy = FindElementBy.Selector,
            [BlockParam("Identifier")] string selector = "",
            int index = 0,
            int timeoutSeconds = 30)
        {
            LogMethodStart(data, $"Focusing element: {findBy} {selector}");

            var frame = GetFrame(data);
            await GetLocator(frame, findBy, selector, index)
                .FocusAsync(CreateElementOptions<LocatorFocusOptions>(timeoutSeconds));

            data.Logger.Log($"Focused on element: {findBy} {selector} at index {index}", LogColors.Tomato);
        }

        [Block("Presses a key on an element", name = "Press Key")]
        public static async Task PlaywrightPressKey(BotData data, FindElementBy findBy, string identifier, string key, int index = 0, int timeoutSeconds = 30)
        {
            LogMethodStart(data, $"Pressing key '{key}' on element: {findBy} {identifier}");
            var frame = GetFrame(data);

            if (findBy == FindElementBy.XPath)
            {
                var elements = await frame.Locator("xpath=" + identifier).AllAsync();
                if (elements.Count <= index)
                    throw new Exception($"Expected at least {index + 1} elements to be found but {elements.Count} were found");
                await elements[index].PressAsync(key, new LocatorPressOptions { Timeout = timeoutSeconds * 1000 });
            }
            else
            {
                var selector = BuildSelector(findBy, identifier);
                var elements = await frame.Locator(selector).AllAsync();
                if (elements.Count <= index)
                    throw new Exception($"Expected at least {index + 1} elements to be found but {elements.Count} were found");
                await elements[index].PressAsync(key, new LocatorPressOptions { Timeout = timeoutSeconds * 1000 });
            }

            data.Logger.Log($"Pressed key '{key}' on element: {findBy} {identifier} at index {index}", LogColors.Tomato);
        }

        [Block("Uploads one or more files to a file input, or to a button that opens the file chooser", name = "Upload Files")]
        public static async Task PlaywrightUploadFiles(BotData data, FindElementBy findBy, string identifier, int index, List<string> filePaths, int timeoutSeconds = 30)
        {
            LogMethodStart(data, $"Uploading files to element: {findBy} {identifier}");

            if (filePaths == null || filePaths.Count == 0)
                throw new Exception("No files were specified for upload");

            var missingFiles = filePaths.Where(f => !File.Exists(f)).ToList();
            if (missingFiles.Count > 0)
                throw new Exception($"The following files do not exist: {string.Join(", ", missingFiles)}");

            var page = GetPage(data);
            var frame = GetFrame(data);
            var matches = await frame.Locator(GetSelector(findBy, identifier)).CountAsync();

            if (matches <= index)
            {
                throw new Exception(matches == 0
                    ? $"No element matched {findBy} {identifier}. Point the block at the file input or at the button that opens " +
                      "the upload dialog. XPath cannot match SVG child elements, so an XPath ending in a svg/path icon locates nothing; " +
                      "drop the svg/path part and target the button element instead."
                    : $"Expected at least {index + 1} elements to be found but {matches} were found");
            }

            var target = GetLocator(frame, findBy, identifier, index);

            // A file input (also hidden, or a label bound to one) accepts the files directly.
            var fileInput = await IsFileInputAsync(target) ? target : null;

            // A wrapper element that owns a single file input can be used as-is.
            if (fileInput == null)
            {
                var nested = target.Locator("input[type='file']");
                if (await nested.CountAsync() == 1)
                    fileInput = nested;
            }

            if (fileInput != null)
            {
                await fileInput.SetInputFilesAsync(filePaths, CreateElementOptions<LocatorSetInputFilesOptions>(timeoutSeconds));
                data.Logger.Log($"Uploaded {filePaths.Count} file(s) to element: {findBy} {identifier} at index {index}", LogColors.Tomato);
                return;
            }

            // Not a file input: act like a user and go through the file chooser dialog it opens.
            IFileChooser chooser;
            try
            {
                chooser = await page.RunAndWaitForFileChooserAsync(
                    () => target.ClickAsync(CreateElementOptions<LocatorClickOptions>(timeoutSeconds)),
                    new PageRunAndWaitForFileChooserOptions { Timeout = timeoutSeconds * 1000 });
            }
            catch (TimeoutException)
            {
                throw new Exception($"The element {findBy} {identifier} at index {index} is not a file input and clicking it did not open a file chooser within {timeoutSeconds} seconds. " +
                    "Click the control that opens the upload dialog (not its icon), or select the file input itself.");
            }

            await chooser.SetFilesAsync(filePaths, new FileChooserSetFilesOptions { Timeout = timeoutSeconds * 1000 });
            data.Logger.Log($"Uploaded {filePaths.Count} file(s) through the file chooser opened by: {findBy} {identifier} at index {index}", LogColors.Tomato);
        }

        private static async Task<bool> IsFileInputAsync(ILocator locator)
        {
            return await locator.EvaluateAsync<bool>(
                "e => (e.tagName === 'INPUT' && (e.type || '').toLowerCase() === 'file') " +
                "|| (e.tagName === 'LABEL' && !!e.control && e.control.tagName === 'INPUT' && (e.control.type || '').toLowerCase() === 'file')");
        }

        private static string GetSelector(FindElementBy findBy, string identifier)
            => findBy == FindElementBy.XPath ? "xpath=" + identifier : BuildSelector(findBy, identifier);

        [Block("Switches to a different iframe", name = "Switch to Frame")]
        public static async Task PlaywrightSwitchToFrame(BotData data, FindElementBy findBy, string identifier, int index = 0, int timeoutSeconds = 30)
        {
            LogMethodStart(data, $"Switching to iframe: {findBy} {identifier}");
            var page = GetPage(data);

            IFrame frame;
            if (findBy == FindElementBy.XPath)
            {
                var elementHandle = await page.QuerySelectorAsync($"xpath={identifier}");
                if (elementHandle == null)
                    throw new Exception($"Element not found: {findBy} {identifier}");
                frame = await elementHandle.ContentFrameAsync();
            }
            else
            {
                var selector = BuildSelector(findBy, identifier);
                var elementHandle = await page.QuerySelectorAsync(selector);
                if (elementHandle == null)
                    throw new Exception($"Element not found: {findBy} {identifier}");
                frame = await elementHandle.ContentFrameAsync();
            }

            if (frame == null)
                throw new Exception("The specified element is not a frame or iframe");

            PlaywrightHelpers.SetFrame(data, frame);
            data.Logger.Log($"Switched to iframe: {findBy} {identifier}", LogColors.Tomato);
        }

        private static IPage GetPage(BotData data) => PlaywrightHelpers.GetPage(data);

        private static IFrame GetFrame(BotData data) => PlaywrightHelpers.GetFrame(data);

        private static void LogMethodStart(BotData data, string action) => PlaywrightHelpers.LogMethodStart(data, action, LogColors.Tomato);

        private static T CreateElementOptions<T>(int timeoutSeconds) where T : new()
            => PlaywrightHelpers.CreateOptionsWithForce<T>(timeoutSeconds);

        private static PageWaitForSelectorOptions CreateWaitOptions(int timeoutSeconds, WaitForSelectorState state = WaitForSelectorState.Visible)
        {
            return new PageWaitForSelectorOptions
            {
                Timeout = timeoutSeconds * 1000f,
                State = state
            };
        }

        private static string BuildSelector(FindElementBy findBy, string identifier)
            => PlaywrightHelpers.BuildSelector(findBy, identifier);

        private static ILocator GetLocator(IFrame frame, FindElementBy findBy, string identifier, int index)
            => (findBy == FindElementBy.XPath
                ? frame.Locator("xpath=" + identifier)
                : frame.Locator(BuildSelector(findBy, identifier)))
                .Nth(index);
    }
}
