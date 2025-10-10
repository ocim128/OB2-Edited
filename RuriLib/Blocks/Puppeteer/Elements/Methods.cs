using PuppeteerSharp;
using RuriLib.Attributes;
using RuriLib.Functions.Files;
using RuriLib.Functions.Puppeteer;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Puppeteer.Elements
{
    [BlockCategory("Elements", "Blocks for interacting with elements on a puppeteer browser page", "#e9967a")]
    public static class Methods
    {
        [Block("Sets the value of the specified attribute of an element", name = "Set Attribute Value")]
        public static async Task PuppeteerSetAttributeValue(BotData data, FindElementBy findBy, string identifier, int index,
            string attributeName, string value)
        {
            data.Logger.LogHeader();

            var elemScript = GetElementScript(findBy, identifier, index);
            var frame = GetFrame(data);
            var script = elemScript + $".setAttribute('{attributeName}', '{value}');";
            await frame.EvaluateExpressionAsync(script);

            data.Logger.Log($"Set value {value} of attribute {attributeName} by executing {script}", LogColors.DarkSalmon);
        }

        [Block("Types text in an input field", name = "Type")]
        public static async Task PuppeteerTypeElement(BotData data, FindElementBy findBy, string identifier, int index,
            string text, int timeBetweenKeystrokes = 0)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            var elem = await GetElement(frame, findBy, identifier, index);
            await elem.TypeAsync(text, new PuppeteerSharp.Input.TypeOptions { Delay = timeBetweenKeystrokes });

            data.Logger.Log($"Typed {text}", LogColors.DarkSalmon);
        }

        [Block("Types text in an input field with human-like random delays", name = "Type Human")]
        public static async Task PuppeteerTypeElementHuman(BotData data, FindElementBy findBy, string identifier, int index,
            string text)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            var elem = await GetElement(frame, findBy, identifier, index);

            foreach (var c in text)
            {
                await elem.TypeAsync(c.ToString());
                await Task.Delay(data.Random.Next(100, 300)); // Wait between 100 and 300 ms (average human type speed is 60 WPM ~ 360 CPM)
            }

            data.Logger.Log($"Typed {text}", LogColors.DarkSalmon);
        }

        [Block("Clicks an element", name = "Click")]
        public static async Task PuppeteerClick(BotData data, FindElementBy findBy, string identifier, int index,
            PuppeteerSharp.Input.MouseButton mouseButton = PuppeteerSharp.Input.MouseButton.Left, int clickCount = 1,
            int timeBetweenClicks = 0)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            var elem = await GetElement(frame, findBy, identifier, index);
            await elem.ClickAsync(new PuppeteerSharp.Input.ClickOptions { Button = mouseButton, ClickCount = clickCount, Delay = timeBetweenClicks });

            data.Logger.Log($"Clicked {clickCount} time(s) with {mouseButton} button", LogColors.DarkSalmon);
        }

        [Block("Submits a form", name = "Submit")]
        public static async Task PuppeteerSubmit(BotData data, FindElementBy findBy, string identifier, int index)
        {
            data.Logger.LogHeader();

            var elemScript = GetElementScript(findBy, identifier, index);
            var frame = GetFrame(data);
            var script = elemScript + ".submit();";
            await frame.EvaluateExpressionAsync(script);

            data.Logger.Log($"Submitted the form by executing {script}", LogColors.DarkSalmon);
        }

        [Block("Selects a value in a select element", name = "Select")]
        public static async Task PuppeteerSelect(BotData data, FindElementBy findBy, string identifier, int index, string value)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            var elem = await GetElement(frame, findBy, identifier, index);
            await elem.SelectAsync(value);

            data.Logger.Log($"Selected value {value}", LogColors.DarkSalmon);
        }

        [Block("Selects a value by index in a select element", name = "Select by Index")]
        public static async Task PuppeteerSelectByIndex(BotData data, FindElementBy findBy, string identifier, int index, int selectionIndex)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            var elemScript = GetElementScript(findBy, identifier, index);
            var script = elemScript + $".getElementsByTagName('option')[{selectionIndex}].value;";
            var value = (await frame.EvaluateExpressionAsync(script)).ToString();

            var elem = await GetElement(frame, findBy, identifier, index);
            await elem.SelectAsync(value);

            data.Logger.Log($"Selected value {value}", LogColors.DarkSalmon);
        }

        [Block("Selects a value by text in a select element", name = "Select by Text")]
        public static async Task PuppeteerSelectByText(BotData data, FindElementBy findBy, string identifier, int index, string text)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            var elemScript = GetElementScript(findBy, identifier, index);
            var script = $"el={elemScript};for(let i=0;i<el.options.length;i++){{if(el.options[i].text=='{text}'){{el.selectedIndex = i;break;}}}}";
            await frame.EvaluateExpressionAsync(script);

            data.Logger.Log($"Selected text {text}", LogColors.DarkSalmon);
        }

        [Block("Gets the value of an attribute of an element", name = "Get Attribute Value")]
        public static async Task<string> PuppeteerGetAttributeValue(BotData data, FindElementBy findBy, string identifier, int index,
            string attributeName = "innerText")
        {
            data.Logger.LogHeader();

            var elemScript = GetElementScript(findBy, identifier, index);
            var frame = GetFrame(data);
            var script = $"{elemScript}.{attributeName};";
            var value = await frame.EvaluateExpressionAsync<string>(script);

            data.Logger.Log($"Got value {value} of attribute {attributeName} by executing {script}", LogColors.DarkSalmon);
            return value;
        }

        [Block("Gets the values of an attribute of multiple elements", name = "Get Attribute Value All")]
        public static async Task<List<string>> PuppeteerGetAttributeValueAll(BotData data, FindElementBy findBy, string identifier,
            string attributeName = "innerText")
        {
            data.Logger.LogHeader();

            var elemScript = GetElementsScript(findBy, identifier);
            var frame = GetFrame(data);
            var script = $"Array.prototype.slice.call({elemScript}).map((item) => item.{attributeName})";
            var values = await frame.EvaluateExpressionAsync<string[]>(script);

            data.Logger.Log($"Got {values.Length} values for attribute {attributeName} by executing {script}", LogColors.DarkSalmon);
            return values.ToList();
        }

        // New: Get HTML attribute via getAttribute for a single element
        [Block("Gets an HTML attribute via getAttribute", name = "Get Attribute")]
        public static async Task<string> PuppeteerGetAttribute(BotData data, FindElementBy findBy, string identifier, int index,
            string attributeName)
        {
            data.Logger.LogHeader();

            var elemScript = GetElementScript(findBy, identifier, index);
            var frame = GetFrame(data);
            var safeAttr = attributeName?.Replace("'", "\\'") ?? string.Empty;
            var script = $"(function(){{var el = {elemScript}; if(!el) return ''; var v = el.getAttribute('{safeAttr}'); return v == null ? '' : String(v);}})()";
            var value = await frame.EvaluateExpressionAsync<string>(script);

            data.Logger.Log($"Got attribute {attributeName} value '{value}' by executing {script}", LogColors.DarkSalmon);
            return value;
        }

        // New: Get HTML attribute via getAttribute for multiple elements
        [Block("Gets HTML attribute values via getAttribute for multiple elements", name = "Get Attribute All")]
        public static async Task<List<string>> PuppeteerGetAttributeAll(BotData data, FindElementBy findBy, string identifier,
            string attributeName)
        {
            data.Logger.LogHeader();

            var elemScript = GetElementsScript(findBy, identifier);
            var frame = GetFrame(data);
            var safeAttr = attributeName?.Replace("'", "\\'") ?? string.Empty;
            var script = $"Array.prototype.slice.call({elemScript}).map((item) => {{ var v = item ? item.getAttribute('{safeAttr}') : null; return v == null ? '' : String(v); }})";
            var values = await frame.EvaluateExpressionAsync<string[]>(script);

            data.Logger.Log($"Got {values.Length} attribute '{attributeName}' values by executing {script}", LogColors.DarkSalmon);
            return values.ToList();
        }

        // New: Get DOM property value using bracket notation for dynamic property names
        [Block("Gets a DOM property value of an element", name = "Get Property Value")]
        public static async Task<string> PuppeteerGetPropertyValue(BotData data, FindElementBy findBy, string identifier, int index,
            string propertyName = "innerText")
        {
            data.Logger.LogHeader();

            var elemScript = GetElementScript(findBy, identifier, index);
            var frame = GetFrame(data);
            var safeProp = propertyName?.Replace("'", "\\'") ?? string.Empty;
            var script = $"(function(){{var el = {elemScript}; if(!el) return ''; var v = el['{safeProp}']; return v == null ? '' : String(v);}})()";
            var value = await frame.EvaluateExpressionAsync<string>(script);

            data.Logger.Log($"Got property {propertyName} value '{value}' by executing {script}", LogColors.DarkSalmon);
            return value;
        }

        // New: Get DOM property values for multiple elements
        [Block("Gets DOM property values of multiple elements", name = "Get Property Value All")]
        public static async Task<List<string>> PuppeteerGetPropertyValueAll(BotData data, FindElementBy findBy, string identifier,
            string propertyName = "innerText")
        {
            data.Logger.LogHeader();

            var elemScript = GetElementsScript(findBy, identifier);
            var frame = GetFrame(data);
            var safeProp = propertyName?.Replace("'", "\\'") ?? string.Empty;
            var script = $"Array.prototype.slice.call({elemScript}).map((item) => {{ var v = item ? item['{safeProp}'] : null; return v == null ? '' : String(v); }})";
            var values = await frame.EvaluateExpressionAsync<string[]>(script);

            data.Logger.Log($"Got {values.Length} property '{propertyName}' values by executing {script}", LogColors.DarkSalmon);
            return values.ToList();
        }

        [Block("Checks if an element is currently being displayed on the page", name = "Is Displayed")]
        public static async Task<bool> PuppeteerIsDisplayed(BotData data, FindElementBy findBy, string identifier, int index)
        {
            data.Logger.LogHeader();

            var elemScript = GetElementScript(findBy, identifier, index);
            var frame = GetFrame(data);
            var script = $"window.getComputedStyle({elemScript}).display !== 'none';";
            var displayed = await frame.EvaluateExpressionAsync<bool>(script);

            data.Logger.Log($"Found out the element is{(displayed ? "" : " not")} displayed by executing {script}", LogColors.DarkSalmon);
            return displayed;
        }

        [Block("Checks if an element exists on the page", name = "Exists")]
        public static async Task<bool> PuppeteerExists(BotData data, FindElementBy findBy, string identifier, int index)
        {
            data.Logger.LogHeader();

            var elemScript = GetElementScript(findBy, identifier, index);
            var frame = GetFrame(data);
            var script = $"window.getComputedStyle({elemScript}).display !== 'none';";

            try
            {
                var displayed = await frame.EvaluateExpressionAsync<bool>(script);
                data.Logger.Log("The element exists", LogColors.DarkSalmon);
                return true;
            }
            catch
            {
                data.Logger.Log("The element does not exist", LogColors.DarkSalmon);
                return false;
            }
        }

        [Block("Uploads one or more files to the selected element", name = "Upload Files")]
        public static async Task PuppeteerUploadFiles(BotData data, FindElementBy findBy, string identifier, int index, List<string> filePaths)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            var elem = await GetElement(frame, findBy, identifier, index);
            await elem.UploadFileAsync(filePaths.ToArray());

            data.Logger.Log($"Uploaded {filePaths.Count} files to the element", LogColors.DarkSalmon);
        }

        [Block("Gets the X coordinate of the element in pixels", name = "Get Position X")]
        public static async Task<int> PuppeteerGetPositionX(BotData data, FindElementBy findBy, string identifier, int index)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            var elem = await GetElement(frame, findBy, identifier, index);
            var x = (int)(await elem.BoundingBoxAsync()).X;

            data.Logger.Log($"The X coordinate of the element is {x}", LogColors.DarkSalmon);
            return x;
        }

        [Block("Gets the Y coordinate of the element in pixels", name = "Get Position Y")]
        public static async Task<int> PuppeteerGetPositionY(BotData data, FindElementBy findBy, string identifier, int index)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            var elem = await GetElement(frame, findBy, identifier, index);
            var y = (int)(await elem.BoundingBoxAsync()).Y;

            data.Logger.Log($"The Y coordinate of the element is {y}", LogColors.DarkSalmon);
            return y;
        }

        [Block("Gets the width of the element in pixels", name = "Get Width")]
        public static async Task<int> PuppeteerGetWidth(BotData data, FindElementBy findBy, string identifier, int index)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            var elem = await GetElement(frame, findBy, identifier, index);
            var width = (int)(await elem.BoundingBoxAsync()).Width;

            data.Logger.Log($"The width of the element is {width}", LogColors.DarkSalmon);
            return width;
        }

        [Block("Gets the height of the element in pixels", name = "Get Height")]
        public static async Task<int> PuppeteerGetHeight(BotData data, FindElementBy findBy, string identifier, int index)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            var elem = await GetElement(frame, findBy, identifier, index);
            var height = (int)(await elem.BoundingBoxAsync()).Height;

            data.Logger.Log($"The height of the element is {height}", LogColors.DarkSalmon);
            return height;
        }

        [Block("Takes a screenshot of the element and saves it to an output file", name = "Screenshot Element")]
        public static async Task PuppeteerScreenshotElement(BotData data, FindElementBy findBy, string identifier, int index,
            string fileName, bool fullPage = false, bool omitBackground = false)
        {
            data.Logger.LogHeader();

            if (data.Providers.Security.RestrictBlocksToCWD)
                FileUtils.ThrowIfNotInCWD(fileName);

            var frame = GetFrame(data);
            var elem = await GetElement(frame, findBy, identifier, index);
            await elem.ScreenshotAsync(fileName, new ScreenshotOptions 
            {
                FullPage = fullPage,
                OmitBackground = omitBackground,
                Type = omitBackground ? ScreenshotType.Png : ScreenshotType.Jpeg,
                Quality = omitBackground ? null : 100
            });

            data.Logger.Log($"Took a screenshot of the element and saved it to {fileName}", LogColors.DarkSalmon);
        }

        [Block("Takes a screenshot of the element and converts it to a base64 string", name = "Screenshot Element Base64")]
        public static async Task<string> PuppeteerScreenshotBase64(BotData data, FindElementBy findBy, string identifier, int index,
            bool fullPage = false, bool omitBackground = false)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            var elem = await GetElement(frame, findBy, identifier, index);
            var base64 = await elem.ScreenshotBase64Async(new ScreenshotOptions 
            { 
                FullPage = fullPage,
                OmitBackground = omitBackground,
                Type = omitBackground ? ScreenshotType.Png : ScreenshotType.Jpeg,
                Quality = omitBackground ? null : 100
            });

            data.Logger.Log($"Took a screenshot of the element as base64", LogColors.DarkSalmon);
            return base64;
        }

        [Block("Switches to a different iframe", name = "Switch to Frame")]
        public static async Task PuppeteerSwitchToFrame(BotData data, FindElementBy findBy, string identifier, int index, int timeoutMs = 3000)
        {
            data.Logger.LogHeader();

            // Ensure we are operating on a valid (non-detached) frame; if not, reset to MainFrame
            var frame = GetFrame(data);
            bool frameValid = false;
            try
            {
                var pingTask = frame.EvaluateExpressionAsync<bool>("true");
                var finished = await Task.WhenAny(pingTask, Task.Delay(300));
                if (finished == pingTask)
                {
                    frameValid = true; // ping succeeded quickly
                }
            }
            catch (PuppeteerSharp.PuppeteerException ex)
            {
                if (ex.Message != null && ex.Message.IndexOf("detached frame", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    frameValid = false;
                }
            }
            catch
            {
                frameValid = false;
            }

            if (!frameValid)
            {
                var page = GetPage(data);
                frame = page.MainFrame;
                data.SetObject("puppeteerFrame", frame);
                data.Logger.Log("Current frame is detached; reset to MainFrame", LogColors.Yellow);
            }

            // Acquire element with timeout to avoid hangs if the context is flaky
            IElementHandle elem;
            {
                var getTask = GetElement(frame, findBy, identifier, index);
                var done = await Task.WhenAny(getTask, Task.Delay(500));
                if (done == getTask)
                {
                    elem = await getTask;
                }
                else
                {
                    // If timed out, try once more after resetting to MainFrame
                    var page = GetPage(data);
                    frame = page.MainFrame;
                    data.SetObject("puppeteerFrame", frame);
                    var retryTask = GetElement(frame, findBy, identifier, index);
                    var retryDone = await Task.WhenAny(retryTask, Task.Delay(500));
                    if (retryDone != retryTask)
                        throw new TimeoutException("Timeout locating iframe element");
                    elem = await retryTask;
                }
            }

            // Quick validation: ensure the element is an IFRAME without blocking
            try
            {
                var tagTask = elem.EvaluateFunctionAsync<string>("e => e && e.tagName");
                var tagCompleted = await Task.WhenAny(tagTask, Task.Delay(300));
                if (tagCompleted == tagTask)
                {
                    var tag = await tagTask;
                    if (!string.Equals(tag, "IFRAME", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception("Selected element is not an iframe");
                    }
                }
            }
            catch
            {
                // If evaluation fails or times out, proceed best-effort
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(Math.Max(0, timeoutMs));
            IFrame? targetFrame = null;

            // Single loop: try to acquire content frame; if handle is stale, re-acquire; never block indefinitely
            while (DateTime.UtcNow < deadline)
            {
                // Re-validate frame quickly; if invalid, reset to MainFrame before continuing
                bool valid = false;
                try
                {
                    var pingTask = frame.EvaluateExpressionAsync<bool>("true");
                    var finished = await Task.WhenAny(pingTask, Task.Delay(200));
                    valid = finished == pingTask;
                }
                catch { valid = false; }
                if (!valid)
                {
                    var page = GetPage(data);
                    frame = page.MainFrame;
                    data.SetObject("puppeteerFrame", frame);
                    // Re-acquire element after frame reset (with timeout)
                    var reacquireTask = GetElement(frame, findBy, identifier, index);
                    var reacquireDone = await Task.WhenAny(reacquireTask, Task.Delay(400));
                    if (reacquireDone == reacquireTask)
                    {
                        elem = await reacquireTask;
                    }
                    else
                    {
                        await Task.Delay(200);
                        continue;
                    }
                }

                var contentFrameTask = elem.ContentFrameAsync();
                var completed = await Task.WhenAny(contentFrameTask, Task.Delay(400));

                if (completed == contentFrameTask)
                {
                    try
                    {
                        targetFrame = await contentFrameTask;
                        if (targetFrame != null) break;
                    }
                    catch
                    {
                        // ignore and retry
                    }
                }

                // Re-acquire the element in case the handle is stale or detached (with timeout)
                var reacquireTask2 = GetElement(frame, findBy, identifier, index);
                var reacquireDone2 = await Task.WhenAny(reacquireTask2, Task.Delay(300));
                if (reacquireDone2 == reacquireTask2)
                {
                    try { elem = await reacquireTask2; } catch { }
                }

                await Task.Delay(200);
            }

            if (targetFrame == null)
            {
                // Last-chance fallback: pick a non-main frame with a real URL if there's only one candidate
                var page = GetPage(data);
                var candidates = page.Frames.Where(f => f != page.MainFrame && !string.IsNullOrEmpty(f.Url) && !f.Url.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase)).ToList();
                if (candidates.Count == 1)
                {
                    targetFrame = candidates[0];
                    data.Logger.Log($"Fallback selected frame by Url: {targetFrame.Url}", LogColors.Yellow);
                }
            }

            // Attempt targeted fallback by matching iframe src/name to existing frames
            if (targetFrame == null)
            {
                string? src = null;
                string? name = null;
                try
                {
                    var srcTask = elem.EvaluateFunctionAsync<string>("e => e && (e.getAttribute('src') || e.src) || ''");
                    var nameTask = elem.EvaluateFunctionAsync<string>("e => e && (e.getAttribute('name') || e.name) || ''");
                    var srcDone = await Task.WhenAny(srcTask, Task.Delay(300));
                    var nameDone = await Task.WhenAny(nameTask, Task.Delay(300));
                    if (srcDone == srcTask) src = await srcTask;
                    if (nameDone == nameTask) name = await nameTask;
                }
                catch { }

                var page = GetPage(data);
                if (!string.IsNullOrWhiteSpace(src))
                {
                    var byUrl = page.Frames.FirstOrDefault(f => string.Equals(f.Url, src, StringComparison.OrdinalIgnoreCase))
                               ?? page.Frames.FirstOrDefault(f => f.Url.IndexOf(src, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (byUrl != null)
                    {
                        targetFrame = byUrl;
                        data.Logger.Log($"Fallback matched frame by src: {src}", LogColors.Yellow);
                    }
                }
                if (targetFrame == null && !string.IsNullOrWhiteSpace(name))
                {
                    var byName = page.Frames.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (byName != null)
                    {
                        targetFrame = byName;
                        data.Logger.Log($"Fallback matched frame by name: {name}", LogColors.Yellow);
                    }
                }
            }

            if (targetFrame == null)
            {
                data.Logger.Log("Timeout acquiring iframe content frame", LogColors.DarkSalmon);
                throw new TimeoutException("Timeout acquiring iframe content frame");
            }

            data.SetObject("puppeteerFrame", targetFrame);
            data.Logger.Log("Switched to iframe", LogColors.DarkSalmon);
        }

        [Block("Waits for an element to appear on the page", name = "Wait for Element")]
        public static async Task PuppeteerWaitForElement(BotData data, FindElementBy findBy, string identifier, bool hidden = false, bool visible = true,
            int timeout = 30000)
        {
            data.Logger.LogHeader();

            var frame = GetFrame(data);
            var options = new WaitForSelectorOptions { Hidden = hidden, Visible = visible, Timeout = timeout };

            if (findBy == FindElementBy.XPath)
            {
                await frame.WaitForXPathAsync(identifier, options);
            }
            else
            {
                await frame.WaitForSelectorAsync(BuildSelector(findBy, identifier), options);
            }

            data.Logger.Log($"Waited for element with {findBy} {identifier}", LogColors.DarkSalmon);
        }

        private static async Task<IElementHandle> GetElement(IFrame frame, FindElementBy findBy, string identifier, int index)
        {
            var elements = findBy == FindElementBy.XPath
                ? await frame.XPathAsync(identifier)
                : await frame.QuerySelectorAllAsync(BuildSelector(findBy, identifier));

            if (elements.Length < index + 1)
            {
                throw new Exception($"Expected at least {index + 1} elements to be found but {elements.Length} were found");
            }

            return elements[index];
        }

        private static string GetElementsScript(FindElementBy findBy, string identifier)
        {
            if (findBy == FindElementBy.XPath)
            {
                var script = $"document.evaluate(\"{identifier.Replace("\"", "\\\"")}\", document, null, XPathResult.ORDERED_NODE_SNAPSHOT_TYPE, null)";
                return $"Array.from({{ length: {script}.snapshotLength }}, (_, index) => {script}.snapshotItem(index))";
            }
            else
            {
                return $"document.querySelectorAll('{BuildSelector(findBy, identifier)}')";
            }
        }

        private static string GetElementScript(FindElementBy findBy, string identifier, int index)
            => findBy == FindElementBy.XPath
            ? $"document.evaluate(\"{identifier.Replace("\"", "\\\"")}\", document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue"
            : $"document.querySelectorAll('{BuildSelector(findBy, identifier)}')[{index}]";

        private static string BuildSelector(FindElementBy findBy, string identifier)
            => findBy switch
            {
                FindElementBy.Id => '#' + identifier,
                FindElementBy.ClassName => '.' + string.Join('.', identifier.Split(' ')), // "class1 class2" => ".class1.class2"
                FindElementBy.CssSelector => identifier,
                FindElementBy.Selector => identifier,
                _ => throw new NotSupportedException()
            };

        private static IBrowser GetBrowser(BotData data)
            => data.TryGetObject<IBrowser>("puppeteer") ?? throw new Exception("The browser is not open!");

        private static IPage GetPage(BotData data)
            => data.TryGetObject<IPage>("puppeteerPage") ?? throw new Exception("No pages open!");

        private static IFrame GetFrame(BotData data)
            => data.TryGetObject<IFrame>("puppeteerFrame") ?? GetPage(data).MainFrame;

        // New: Get innerHTML of a single element
        [Block("Gets the innerHTML of an element", name = "Get InnerHTML")]
        public static async Task<string> PuppeteerGetInnerHTML(BotData data, FindElementBy findBy, string identifier, int index)
        {
            data.Logger.LogHeader();

            var elemScript = GetElementScript(findBy, identifier, index);
            var frame = GetFrame(data);
            var script = $"(function(){{var el = {elemScript}; return el ? String(el.innerHTML || '') : '';}})()";
            var value = await frame.EvaluateExpressionAsync<string>(script);

            data.Logger.Log($"Got innerHTML length {value?.Length ?? 0}", LogColors.DarkSalmon);
            return value;
        }

        // New: Get outerHTML of a single element
        [Block("Gets the outerHTML of an element", name = "Get OuterHTML")]
        public static async Task<string> PuppeteerGetOuterHTML(BotData data, FindElementBy findBy, string identifier, int index)
        {
            data.Logger.LogHeader();

            var elemScript = GetElementScript(findBy, identifier, index);
            var frame = GetFrame(data);
            var script = $"(function(){{var el = {elemScript}; return el ? String(el.outerHTML || '') : '';}})()";
            var value = await frame.EvaluateExpressionAsync<string>(script);

            data.Logger.Log($"Got outerHTML length {value?.Length ?? 0}", LogColors.DarkSalmon);
            return value;
        }

        // New: Get innerHTML of multiple elements
        [Block("Gets the innerHTML values of multiple elements", name = "Get InnerHTML All")]
        public static async Task<List<string>> PuppeteerGetInnerHTMLAll(BotData data, FindElementBy findBy, string identifier)
        {
            data.Logger.LogHeader();

            var elemScript = GetElementsScript(findBy, identifier);
            var frame = GetFrame(data);
            var script = $"Array.prototype.slice.call({elemScript}).map((item) => {{ var v = item ? item.innerHTML : null; return v == null ? '' : String(v); }})";
            var values = await frame.EvaluateExpressionAsync<string[]>(script);

            data.Logger.Log($"Got {values.Length} innerHTML values", LogColors.DarkSalmon);
            return values.ToList();
        }

        // New: Get outerHTML of multiple elements
        [Block("Gets the outerHTML values of multiple elements", name = "Get OuterHTML All")]
        public static async Task<List<string>> PuppeteerGetOuterHTMLAll(BotData data, FindElementBy findBy, string identifier)
        {
            data.Logger.LogHeader();

            var elemScript = GetElementsScript(findBy, identifier);
            var frame = GetFrame(data);
            var script = $"Array.prototype.slice.call({elemScript}).map((item) => {{ var v = item ? item.outerHTML : null; return v == null ? '' : String(v); }})";
            var values = await frame.EvaluateExpressionAsync<string[]>(script);

            data.Logger.Log($"Got {values.Length} outerHTML values", LogColors.DarkSalmon);
            return values.ToList();
        }
    }
}
