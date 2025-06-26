using Yove.Proxy;
using PuppeteerExtraSharp;
using PuppeteerExtraSharp.Plugins.ExtraStealth;
using PuppeteerSharp;
using RuriLib.Attributes;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ProxyType = RuriLib.Models.Proxies.ProxyType;
using RuriLib.Helpers;

namespace RuriLib.Blocks.Puppeteer.Browser
{
    [BlockCategory("Browser", "Blocks for interacting with a puppeteer browser", "#e9967a")]
    public static class Methods
    {
        [Block("Opens a new puppeteer browser", name = "Open Browser")]
        public static async Task PuppeteerOpenBrowser(BotData data, string extraCmdLineArgs = "")
        {
            data.Logger.LogHeader();

            // Check if there is already an open browser
            var oldBrowser = data.TryGetObject<IBrowser>("puppeteer");
            if (oldBrowser is not null && !oldBrowser.IsClosed)
            {
                data.Logger.Log("The browser is already open, close it if you want to open a new browser", LogColors.DarkSalmon);
                return;
            }

            var args = data.ConfigSettings.BrowserSettings.CommandLineArgs;

            // Enhanced stealth arguments to avoid bot detection
            var stealthArgs = new[]
            {
                "--no-first-run",
                "--disable-blink-features=AutomationControlled",
                "--disable-features=VizDisplayCompositor",
                "--disable-ipc-flooding-protection",
                "--disable-renderer-backgrounding",
                "--disable-backgrounding-occluded-windows",
                "--disable-background-timer-throttling",
                "--disable-features=TranslateUI",
                "--disable-domain-reliability",
                "--disable-client-side-phishing-detection",
                "--disable-component-update",
                "--disable-default-apps",
                "--disable-dev-shm-usage",
                "--disable-hang-monitor",
                "--disable-prompt-on-repost",
                "--disable-sync",
                "--disable-web-security",
                "--hide-scrollbars",
                "--no-sandbox",
                "--disable-background-networking",
                "--disable-background-media-suspend",
                "--disable-field-trial-config",
                "--disable-back-forward-cache",
                "--disable-popup-blocking",
                "--flag-switches-begin",
                "--disable-site-isolation-trials",
                "--flag-switches-end"
            };

            // Combine with existing args
            if (!string.IsNullOrWhiteSpace(args))
            {
                args += " " + string.Join(" ", stealthArgs);
            }
            else
            {
                args = string.Join(" ", stealthArgs);
            }

            // Extra command line args (to have dynamic args via variables)
            if (!string.IsNullOrWhiteSpace(extraCmdLineArgs))
            {
                args += ' ' + extraCmdLineArgs;
            }

            // If it's running in docker, currently it runs under root, so add the --no-sandbox otherwise chrome won't work
            if (Utils.IsDocker())
            {
                args += " --no-sandbox";
            }

            if (data.Proxy != null && data.UseProxy)
            {
                if (data.Proxy.Type == ProxyType.Http || !data.Proxy.NeedsAuthentication)
                {
                    args += $" --proxy-server={data.Proxy.Type.ToString().ToLower()}://{data.Proxy.Host}:{data.Proxy.Port}";
                }
                else
                {
                    var proxyType = data.Proxy.Type == ProxyType.Socks5 ? Yove.Proxy.ProxyType.Socks5 : Yove.Proxy.ProxyType.Socks4;
                    var proxyClient = new ProxyClient(
                        data.Proxy.Host, data.Proxy.Port,
                        data.Proxy.Username, data.Proxy.Password, 
                        proxyType);
                    data.SetObject("puppeteer.yoveproxy", proxyClient);
                    args += $" --proxy-server={proxyClient.GetProxy(null).Authority}";
                }
            }

            // Configure the options
            var launchOptions = new LaunchOptions
            {
                Args = new string[] { args },
                ExecutablePath = data.Providers.PuppeteerBrowser.ChromeBinaryLocation,
                IgnoredDefaultArgs = new string[] { "--enable-automation", "--enable-blink-features=AutomationControlled" },
                Headless = data.ConfigSettings.BrowserSettings.Headless,
                DefaultViewport = null // This is important
            };

            // Add the plugins
            var extra = new PuppeteerExtra();
            extra.Use(new StealthPlugin());

            // Launch the browser
            var browser = await extra.LaunchAsync(launchOptions);
            browser.IgnoreHTTPSErrors = data.ConfigSettings.BrowserSettings.IgnoreHttpsErrors;

            // Save the browser for further use
            data.SetObject("puppeteer", browser);
            var page = (await browser.PagesAsync()).First();
            SetPageAndFrame(data, page);
            await SetPageLoadingOptions(data, page);
            
            // Apply additional stealth measures after page creation
            await ApplyStealthMeasures(page);

            // Authenticate if the proxy requires auth
            if (data.UseProxy && data.Proxy is { NeedsAuthentication: true, Type: ProxyType.Http } proxy)
                await page.AuthenticateAsync(new Credentials { Username = proxy.Username, Password = proxy.Password });

            data.Logger.Log($"{(launchOptions.Headless ? "Headless " : "")}Browser opened successfully!", LogColors.DarkSalmon);
        }

        [Block("Closes an open puppeteer browser", name = "Close Browser")]
        public static async Task PuppeteerCloseBrowser(BotData data)
        {
            data.Logger.LogHeader();

            var browser = GetBrowser(data);
            await browser.CloseAsync();
            StopYoveProxyInternalServer(data);
            data.Logger.Log("Browser closed successfully!", LogColors.DarkSalmon);
        }

        [Block("Opens a new page in a new browser tab", name = "New Tab")]
        public static async Task PuppeteerNewTab(BotData data)
        {
            data.Logger.LogHeader();

            var browser = GetBrowser(data);
            var page = await browser.NewPageAsync();
            await SetPageLoadingOptions(data, page);

            // Apply stealth measures to the new tab
            await ApplyStealthMeasures(page);

            SetPageAndFrame(data, page); // Set the new page as active
            data.Logger.Log($"Opened a new page", LogColors.DarkSalmon);
        }

        [Block("Closes the currently active browser tab", name = "Close Tab")]
        public static async Task PuppeteerCloseTab(BotData data)
        {
            data.Logger.LogHeader();

            var browser = GetBrowser(data);
            var page = GetPage(data);
            
            // Close the page
            await page.CloseAsync();
            
            // Set the first page as active
            page = (await browser.PagesAsync()).FirstOrDefault();
            SetPageAndFrame(data, page);

            if (page != null)
                await page.BringToFrontAsync();

            data.Logger.Log($"Closed the active page", LogColors.DarkSalmon);
        }

        [Block("Switches to the browser tab with a specified index", name = "Switch to Tab")]
        public static async Task PuppeteerSwitchToTab(BotData data, int index)
        {
            data.Logger.LogHeader();

            var browser = GetBrowser(data);
            
            // Workaround https://github.com/hardkoded/puppeteer-sharp/issues/1587
            await browser.GetVersionAsync();
            
            var pages = await browser.PagesAsync();
            var page = pages[index];
            
            await page.BringToFrontAsync();
            SetPageAndFrame(data, page);

            data.Logger.Log($"Switched to tab with index {index}", LogColors.DarkSalmon);
        }

        [Block("Reloads the current page", name = "Reload")]
        public static async Task PuppeteerReload(BotData data)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            await page.ReloadAsync();
            SwitchToMainFramePrivate(data);

            data.Logger.Log($"Reloaded the page", LogColors.DarkSalmon);
        }

        [Block("Goes back to the previously visited page", name = "Go Back")]
        public static async Task PuppeteerGoBack(BotData data)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            await page.GoBackAsync();
            SwitchToMainFramePrivate(data);

            data.Logger.Log($"Went back to the previously visited page", LogColors.DarkSalmon);
        }

        [Block("Goes forward to the next visited page", name = "Go Forward")]
        public static async Task PuppeteerGoForward(BotData data)
        {
            data.Logger.LogHeader();

            var page = GetPage(data);
            await page.GoForwardAsync();
            SwitchToMainFramePrivate(data);

            data.Logger.Log($"Went forward to the next visited page", LogColors.DarkSalmon);
        }

        private static IBrowser GetBrowser(BotData data)
            => data.TryGetObject<IBrowser>("puppeteer") ?? throw new Exception("The browser is not open!");

        private static IPage GetPage(BotData data)
            => data.TryGetObject<IPage>("puppeteerPage") ?? throw new Exception("No pages open!");

        private static void SwitchToMainFramePrivate(BotData data)
            => data.SetObject("puppeteerFrame", GetPage(data).MainFrame);

        private static void SetPageAndFrame(BotData data, IPage page)
        {
            data.SetObject("puppeteerPage", page, false);
            SwitchToMainFramePrivate(data);
        }

        private static void StopYoveProxyInternalServer(BotData data)
            => data.TryGetObject<ProxyClient>("puppeteer.yoveproxy")?.Dispose();

        private static async Task SetPageLoadingOptions(BotData data, IPage page)
        {
            await page.SetRequestInterceptionAsync(true);
            page.Request += (sender, e) =>
            {
                // If we only want documents and scripts but the resource is not one of those, block
                if (data.ConfigSettings.BrowserSettings.LoadOnlyDocumentAndScript && 
                    e.Request.ResourceType != ResourceType.Document && e.Request.ResourceType != ResourceType.Script)
                {
                    e.Request.AbortAsync();
                }

                // If the url contains one of the blocked urls
                else if (data.ConfigSettings.BrowserSettings.BlockedUrls
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Any(u => e.Request.Url.Contains(u, StringComparison.OrdinalIgnoreCase)))
                {
                    e.Request.AbortAsync();
                }

                // Otherwise all good, continue
                else
                {
                    e.Request.ContinueAsync();
                }
            };

            if (data.ConfigSettings.BrowserSettings.DismissDialogs)
            {
                page.Dialog += (sender, e) =>
                {
                    data.Logger.Log($"Dialog automatically dismissed: {e.Dialog.Message}", LogColors.DarkSalmon);
                    e.Dialog.Dismiss();
                };
            }
        }

        private static async Task ApplyStealthMeasures(IPage page)
        {
            // Random realistic user agents for Windows 10/11
            var userAgents = new[]
            {
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/119.0",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/120.0"
            };

            var random = new Random();
            var selectedUserAgent = userAgents[random.Next(userAgents.Length)];
            
            // Set random user agent
            await page.SetUserAgentAsync(selectedUserAgent);

            // Comprehensive stealth JavaScript to be injected before any page loads
            var stealthScript = @"
            // Remove webdriver property
            Object.defineProperty(navigator, 'webdriver', {
                get: () => undefined,
            });

            // Remove automation indicator
            delete navigator.__proto__.webdriver;

            // Mock chrome runtime
            window.chrome = {
                runtime: {
                    onConnect: undefined,
                    onMessage: undefined
                }
            };

            // Mock languages with randomization
            const languages = [
                ['en-US', 'en'],
                ['en-GB', 'en'],
                ['en-CA', 'en']
            ];
            const randomLang = languages[Math.floor(Math.random() * languages.length)];
            
            Object.defineProperty(navigator, 'languages', {
                get: () => randomLang,
            });
            
            Object.defineProperty(navigator, 'language', {
                get: () => randomLang[0],
            });

            // Mock plugins
            Object.defineProperty(navigator, 'plugins', {
                get: () => [
                    {
                        0: {
                            type: 'application/x-google-chrome-pdf',
                            suffixes: 'pdf',
                            description: 'Portable Document Format',
                            enabledPlugin: true
                        },
                        description: 'Portable Document Format',
                        filename: 'internal-pdf-viewer',
                        length: 1,
                        name: 'Chrome PDF Plugin'
                    },
                    {
                        0: {
                            type: 'application/pdf',
                            suffixes: 'pdf',
                            description: '',
                            enabledPlugin: true
                        },
                        description: '',
                        filename: 'mhjfbmdgcfjbbpaeojofohoefgiehjai',
                        length: 1,
                        name: 'Chrome PDF Viewer'
                    }
                ],
            });

            // Mock permissions
            const originalQuery = window.navigator.permissions.query;
            window.navigator.permissions.query = (parameters) => (
                parameters.name === 'notifications' ?
                    Promise.resolve({ state: typeof Notification !== 'undefined' ? Notification.permission : 'default' }) :
                    originalQuery(parameters)
            );

            // Mock WebGL vendor and renderer
            const getParameter = WebGLRenderingContext.getParameter;
            WebGLRenderingContext.prototype.getParameter = function(parameter) {
                if (parameter === 37445) {
                    return 'Intel Inc.'; // UNMASKED_VENDOR_WEBGL
                }
                if (parameter === 37446) {
                    return 'Intel(R) HD Graphics'; // UNMASKED_RENDERER_WEBGL
                }
                return getParameter(parameter);
            };

            // Mock WebGL2 as well
            if (typeof WebGL2RenderingContext !== 'undefined') {
                const getParameter2 = WebGL2RenderingContext.getParameter;
                WebGL2RenderingContext.prototype.getParameter = function(parameter) {
                    if (parameter === 37445) {
                        return 'Intel Inc.';
                    }
                    if (parameter === 37446) {
                        return 'Intel(R) HD Graphics';
                    }
                    return getParameter2(parameter);
                };
            }

            // Mock screen properties with realistic values
            const screenWidth = window.screen.width || 1920;
            const screenHeight = window.screen.height || 1080;
            const availWidth = window.screen.availWidth || screenWidth;
            const availHeight = window.screen.availHeight || (screenHeight - 40); // Account for taskbar

            Object.defineProperty(screen, 'width', {
                get: () => screenWidth,
            });
            Object.defineProperty(screen, 'height', {
                get: () => screenHeight,
            });
            Object.defineProperty(screen, 'availWidth', {
                get: () => availWidth,
            });
            Object.defineProperty(screen, 'availHeight', {
                get: () => availHeight,
            });
            Object.defineProperty(screen, 'colorDepth', {
                get: () => 24,
            });
            Object.defineProperty(screen, 'pixelDepth', {
                get: () => 24,
            });

            // Mock navigator properties
            Object.defineProperty(navigator, 'hardwareConcurrency', {
                get: () => 8,
            });

            Object.defineProperty(navigator, 'deviceMemory', {
                get: () => 8,
            });

            Object.defineProperty(navigator, 'platform', {
                get: () => 'Win32',
            });

            Object.defineProperty(navigator, 'vendor', {
                get: () => 'Google Inc.',
            });

            // Mock battery API
            if ('getBattery' in navigator) {
                navigator.getBattery = () => Promise.resolve({
                    charging: true,
                    chargingTime: 0,
                    dischargingTime: Infinity,
                    level: 1,
                    addEventListener: () => {},
                    removeEventListener: () => {},
                    dispatchEvent: () => {}
                });
            }

            // Mock connection API
            Object.defineProperty(navigator, 'connection', {
                get: () => ({
                    effectiveType: '4g',
                    rtt: 50,
                    downlink: 10,
                    saveData: false
                }),
            });

            // Override Date to avoid timezone detection
            const originalDate = Date;
            const fakeTimezoneOffset = -300; // EST timezone
            Date = class extends originalDate {
                getTimezoneOffset() {
                    return fakeTimezoneOffset;
                }
                static now() {
                    return originalDate.now();
                }
            };

            // Mock iframe contentWindow
            const originalCreateElement = document.createElement;
            document.createElement = function(...args) {
                const element = originalCreateElement.apply(this, args);
                if (args[0] === 'iframe') {
                    try {
                        element.contentWindow = window;
                    } catch (e) {}
                }
                return element;
            };

            // Prevent function source code detection
            const originalToString = Function.prototype.toString;
            Function.prototype.toString = function() {
                if (this === navigator.webdriver || 
                    this === Object.getOwnPropertyDescriptor(navigator, 'webdriver').get) {
                    return 'function webdriver() { [native code] }';
                }
                return originalToString.call(this);
            };

            // Mock Notification permission
            if (typeof Notification !== 'undefined') {
                Object.defineProperty(Notification, 'permission', {
                    get: () => 'default',
                });
            }

            // Mock media devices
            if (navigator.mediaDevices && navigator.mediaDevices.enumerateDevices) {
                navigator.mediaDevices.enumerateDevices = () => Promise.resolve([
                    {
                        deviceId: 'default',
                        kind: 'audioinput',
                        label: 'Default - Microphone (Realtek High Definition Audio)',
                        groupId: 'group1'
                    },
                    {
                        deviceId: 'default',
                        kind: 'audiooutput',
                        label: 'Default - Speaker (Realtek High Definition Audio)',
                        groupId: 'group1'
                    },
                    {
                        deviceId: 'camera1',
                        kind: 'videoinput',
                        label: 'Integrated Camera (USB Camera)',
                        groupId: 'group2'
                    }
                ]);
            }

            // Remove CDP runtime detection
            if (window.cdc_adoQpoasnfa76pfcZLmcfl_Array ||
                window.cdc_adoQpoasnfa76pfcZLmcfl_Promise ||
                window.cdc_adoQpoasnfa76pfcZLmcfl_Symbol) {
                delete window.cdc_adoQpoasnfa76pfcZLmcfl_Array;
                delete window.cdc_adoQpoasnfa76pfcZLmcfl_Promise;
                delete window.cdc_adoQpoasnfa76pfcZLmcfl_Symbol;
            }

            // Prevent automation detection through error stack traces
            const originalStackTrace = Error.prepareStackTrace;
            Error.prepareStackTrace = function(_, stack) {
                const filteredStack = stack.filter(frame => {
                    const name = frame.getFunctionName();
                    return !name || (!name.includes('puppeteer') && !name.includes('automation'));
                });
                if (originalStackTrace) {
                    return originalStackTrace(_, filteredStack);
                }
                return filteredStack;
            };

            console.log('🥷 Stealth mode activated successfully!');
            ";

            // Execute stealth script
            await page.EvaluateExpressionAsync(stealthScript);

            // Note: Removed fixed viewport to allow natural browser window adaptation
        }
    }
}
