using Yove.Proxy;
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

            // Always try real browser first for maximum effectiveness against Cloudflare
            await OpenRealBrowserWithFallback(data, extraCmdLineArgs);
        }

        [Block("Closes an open puppeteer browser", name = "Close Browser")]
        public static async Task PuppeteerCloseBrowser(BotData data)
        {
            data.Logger.LogHeader();

            var browser = GetBrowser(data);
            await browser.CloseAsync();
            StopYoveProxyInternalServer(data);
            
            // Clean up real browser process if it exists
            var realBrowserProcessIdObj = data.TryGetObject<object>("puppeteer.realBrowserProcessId");
            if (realBrowserProcessIdObj is int realBrowserProcessId)
            {
                try
                {
                    var process = System.Diagnostics.Process.GetProcessById(realBrowserProcessId);
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch
                {
                    // Ignore if process doesn't exist
                }
                data.SetObject("puppeteer.realBrowserProcessId", null);
            }
            
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

            // Add canvas fingerprinting protection
            const originalToDataURL = HTMLCanvasElement.prototype.toDataURL;
            HTMLCanvasElement.prototype.toDataURL = function(...args) {
                const context = this.getContext('2d');
                if (context) {
                    const imageData = context.getImageData(0, 0, this.width, this.height);
                    for (let i = 0; i < imageData.data.length; i += 4) {
                        imageData.data[i] += Math.floor(Math.random() * 3) - 1;
                        imageData.data[i + 1] += Math.floor(Math.random() * 3) - 1;
                        imageData.data[i + 2] += Math.floor(Math.random() * 3) - 1;
                    }
                    context.putImageData(imageData, 0, 0);
                }
                return originalToDataURL.apply(this, args);
            };

            // Block automation detection through performance timing
            const originalPerformanceNow = performance.now;
            let performanceOffset = Math.random() * 100;
            performance.now = function() {
                return originalPerformanceNow.call(this) + performanceOffset;
            };

            console.log('🛡️ Enhanced stealth mode activated successfully!');
            ";

            // Execute stealth script
            await page.EvaluateExpressionAsync(stealthScript);

            // Set realistic extra headers to avoid detection
            await page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
            {
                ["Accept-Language"] = "en-US,en;q=0.9",
                ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8",
                ["Accept-Encoding"] = "gzip, deflate, br",
                ["Cache-Control"] = "no-cache", 
                ["Pragma"] = "no-cache",
                ["Sec-Fetch-Site"] = "none",
                ["Sec-Fetch-Mode"] = "navigate",
                ["Sec-Fetch-User"] = "?1",
                ["Sec-Fetch-Dest"] = "document",
                ["Upgrade-Insecure-Requests"] = "1"
            });
        }

        private static async Task OpenRealBrowserWithFallback(BotData data, string extraCmdLineArgs = "")
        {
            data.Logger.Log("🛡️ Starting Integrated Real Browser...", LogColors.DarkSalmon);
            await OpenIntegratedRealBrowser(data, extraCmdLineArgs);
        }

        private static async Task OpenIntegratedRealBrowser(BotData data, string extraCmdLineArgs = "")
        {
            // Ultra-advanced Chrome arguments for maximum stealth (equivalent to puppeteer-real-browser)
            var stealthArgs = new List<string>
            {
                // Core stealth arguments
                "--no-sandbox",
                "--disable-setuid-sandbox", 
                "--disable-dev-shm-usage",
                "--disable-web-security",
                "--disable-features=VizDisplayCompositor",
                
                // Advanced bot detection bypass
                "--disable-blink-features=AutomationControlled",
                "--disable-component-extensions-with-background-pages",
                "--disable-default-apps",
                "--disable-extensions",
                "--disable-plugins",
                "--disable-hang-monitor",
                "--disable-popup-blocking",
                "--disable-prompt-on-repost",
                "--disable-sync",
                "--disable-translate",
                "--disable-background-timer-throttling",
                "--disable-backgrounding-occluded-windows",
                "--disable-renderer-backgrounding",
                
                // Memory and performance optimization
                "--memory-pressure-off",
                "--max_old_space_size=4096",
                "--no-zygote",
                "--no-first-run",
                "--no-default-browser-check",
                
                // Network and security bypasses
                "--disable-ipc-flooding-protection",
                "--disable-background-networking",
                "--disable-client-side-phishing-detection",
                "--disable-component-update",
                "--disable-domain-reliability",
                
                // Canvas and WebGL fingerprinting protection
                "--disable-accelerated-2d-canvas",
                "--disable-accelerated-video-decode",
                "--disable-gpu",
                "--disable-software-rasterizer",
                
                // Additional anti-detection
                "--disable-features=TranslateUI,BlinkGenPropertyTrees",
                "--disable-logging",
                "--disable-login-animations",
                "--disable-notifications",
                "--mute-audio",
                "--no-sandbox",
                "--ignore-certificate-errors",
                "--ignore-ssl-errors",
                "--ignore-certificate-errors-spki-list",
                "--ignore-certificate-errors-ssl",
                "--allow-running-insecure-content",
                
                // Cloudflare specific bypasses
                "--disable-features=VizDisplayCompositor,VizHitTestSurfaceLayer",
                "--enable-features=NetworkService,NetworkServiceLogging",
                "--force-webrtc-ip-handling-policy=default_public_interface_only",
                "--enable-aggressive-domstorage-flushing"
            };

            // Add user-provided arguments
            if (!string.IsNullOrWhiteSpace(data.ConfigSettings.BrowserSettings.CommandLineArgs))
            {
                stealthArgs.AddRange(data.ConfigSettings.BrowserSettings.CommandLineArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
            
            if (!string.IsNullOrWhiteSpace(extraCmdLineArgs))
            {
                stealthArgs.AddRange(extraCmdLineArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }

            // Configure proxy if needed
            if (data.UseProxy && data.Proxy != null)
            {
                stealthArgs.Add($"--proxy-server={data.Proxy.Type.ToString().ToLower()}://{data.Proxy.Host}:{data.Proxy.Port}");
                if (data.Proxy.NeedsAuthentication)
                {
                    stealthArgs.Add($"--proxy-auth={data.Proxy.Username}:{data.Proxy.Password}");
                }
            }

            // Remove duplicates and conflicting arguments
            stealthArgs = stealthArgs.Distinct().Where(arg => !string.IsNullOrWhiteSpace(arg)).ToList();

            // Capture headless setting
            var headless = data.ConfigSettings.BrowserSettings.Headless;
            var launchOptions = new LaunchOptions
            {
                Headless = headless,
                Args = stealthArgs.ToArray(),
                IgnoreHTTPSErrors = data.ConfigSettings.BrowserSettings.IgnoreHttpsErrors,
                SlowMo = 0,
                Timeout = 30000,
                ExecutablePath = data.Providers.PuppeteerBrowser.ChromeBinaryLocation,
                DefaultViewport = null,
                IgnoredDefaultArgs = new[]
                {
                    "--enable-automation",
                    "--enable-blink-features=IdleDetection",
                    "--enable-blink-features=AutomationControlled"
                }
            };

            // Launch browser with maximum stealth
            var browser = await PuppeteerSharp.Puppeteer.LaunchAsync(launchOptions);
            
            data.Logger.Log("✅ Integrated Real Browser launched successfully!", LogColors.Green);

            // Reuse the first existing page to avoid extra blank tab
            var existingPages = await browser.PagesAsync();
            var page = existingPages.FirstOrDefault() ?? await browser.NewPageAsync();

            // Close any additional about:blank pages
            foreach (var p in await browser.PagesAsync())
            {
                if (p != page && p.Url == "about:blank")
                {
                    await p.CloseAsync();
                }
            }

            // User agent is set later via ApplyStealthMeasures.
            
            await page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
            {
                ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7",
                ["Accept-Language"] = "en-US,en;q=0.9",
                ["Accept-Encoding"] = "gzip, deflate, br",
                ["DNT"] = "1",
                ["Connection"] = "keep-alive",
                ["Upgrade-Insecure-Requests"] = "1",
                ["Sec-Fetch-Site"] = "none",
                ["Sec-Fetch-Mode"] = "navigate",
                ["Sec-Fetch-User"] = "?1",
                ["Sec-Fetch-Dest"] = "document",
                ["Cache-Control"] = "max-age=0"
            });

            // Save objects for further use
            data.SetObject("puppeteer", browser);
            SetPageAndFrame(data, page);
            await SetPageLoadingOptions(data, page);

            // Handle proxy authentication
            if (data.UseProxy && data.Proxy is { NeedsAuthentication: true, Type: ProxyType.Http } proxy)
                await page.AuthenticateAsync(new Credentials { Username = proxy.Username, Password = proxy.Password });

            data.Logger.Log("🛡️ Integrated Real Browser ready - Maximum Cloudflare bypass active!", LogColors.Green);
        }

        private static async Task InjectAdvancedStealthScripts(IPage page, BotData data)
        {
            data.Logger.Log("💉 Injecting advanced stealth scripts for maximum anti-detection...", LogColors.Yellow);

            // Phase 1: Core WebDriver hiding and navigator spoofing
            await page.EvaluateFunctionOnNewDocumentAsync(@"() => {
                // Hide webdriver property
                Object.defineProperty(navigator, 'webdriver', {
                    get: () => undefined,
                    configurable: true
                });

                // Hide automation flags  
                delete window.cdc_adoQpoasnfa76pfcZLmcfl_Array;
                delete window.cdc_adoQpoasnfa76pfcZLmcfl_Promise;
                delete window.cdc_adoQpoasnfa76pfcZLmcfl_Symbol;

                // Spoof chrome runtime
                window.chrome = {
                    app: {
                        isInstalled: false,
                        InstallState: { DISABLED: 'disabled', INSTALLED: 'installed', NOT_INSTALLED: 'not_installed' },
                        RunningState: { CANNOT_RUN: 'cannot_run', READY_TO_RUN: 'ready_to_run', RUNNING: 'running' }
                    },
                    runtime: {
                        onConnect: null,
                        onMessage: null,
                        onConnectExternal: null,
                        onMessageExternal: null
                    }
                };

                // Spoof permissions API
                const originalQuery = window.navigator.permissions.query;
                window.navigator.permissions.query = (parameters) => (
                    parameters.name === 'notifications' ?
                    Promise.resolve({ state: Notification.permission }) :
                    originalQuery(parameters)
                );

                // Spoof plugins array
                Object.defineProperty(navigator, 'plugins', {
                    get: () => [
                        { 0: { type: 'application/x-google-chrome-pdf', suffixes: 'pdf', description: 'Portable Document Format', enabledPlugin: null }, 
                          description: 'Portable Document Format', filename: 'internal-pdf-viewer', length: 1, name: 'Chrome PDF Plugin' },
                        { 0: { type: 'application/pdf', suffixes: 'pdf', description: '', enabledPlugin: null }, 
                          description: 'Portable Document Format', filename: 'mhjfbmdgcfjbbpaeojofohoefgiehjai', length: 1, name: 'Chrome PDF Viewer' },
                        { 0: { type: 'application/x-nacl', suffixes: '', description: 'Native Client Executable', enabledPlugin: null }, 
                          1: { type: 'application/x-pnacl', suffixes: '', description: 'Portable Native Client Executable', enabledPlugin: null }, 
                          description: '', filename: 'internal-nacl-plugin', length: 2, name: 'Native Client' }
                    ]
                });
            }");

            // Phase 2: Advanced fingerprinting protection
            await page.EvaluateFunctionOnNewDocumentAsync(@"() => {
                // Canvas fingerprinting protection
                const getContext = HTMLCanvasElement.prototype.getContext;
                HTMLCanvasElement.prototype.getContext = function(type) {
                    if (type === '2d') {
                        const context = getContext.apply(this, arguments);
                        const originalFillText = context.fillText;
                        context.fillText = function() {
                            // Add slight noise to prevent fingerprinting
                            const noise = Math.random() * 0.01 - 0.005;
                            arguments[1] += noise;
                            arguments[2] += noise;
                            return originalFillText.apply(this, arguments);
                        };
                        return context;
                    }
                    return getContext.apply(this, arguments);
                };

                // WebGL fingerprinting protection
                const getParameter = WebGLRenderingContext.prototype.getParameter;
                WebGLRenderingContext.prototype.getParameter = function(parameter) {
                    if (parameter === 37445) { // UNMASKED_VENDOR_WEBGL
                        return 'Intel Inc.';
                    }
                    if (parameter === 37446) { // UNMASKED_RENDERER_WEBGL  
                        return 'Intel Iris OpenGL Engine';
                    }
                    return getParameter.apply(this, arguments);
                };

                // AudioContext fingerprinting protection
                const AudioContext = window.AudioContext || window.webkitAudioContext;
                if (AudioContext) {
                    const originalCreateAnalyser = AudioContext.prototype.createAnalyser;
                    AudioContext.prototype.createAnalyser = function() {
                        const analyser = originalCreateAnalyser.apply(this, arguments);
                        const originalGetFloatFrequencyData = analyser.getFloatFrequencyData;
                        analyser.getFloatFrequencyData = function(array) {
                            const result = originalGetFloatFrequencyData.apply(this, arguments);
                            // Add noise to prevent audio fingerprinting
                            for (let i = 0; i < array.length; i++) {
                                array[i] += Math.random() * 0.001 - 0.0005;
                            }
                            return result;
                        };
                        return analyser;
                    };
                }
            }");

            // Phase 3: Performance timing and memory spoofing
            await page.EvaluateFunctionOnNewDocumentAsync(@"() => {
                // Performance timing spoofing
                if (window.performance && window.performance.timing) {
                    const originalTiming = window.performance.timing;
                    Object.defineProperty(window.performance, 'timing', {
                        get: () => {
                            const timing = {};
                            for (const key in originalTiming) {
                                if (typeof originalTiming[key] === 'number') {
                                    // Add random variance to timing values
                                    timing[key] = originalTiming[key] + Math.floor(Math.random() * 5) - 2;
                                } else {
                                    timing[key] = originalTiming[key];
                                }
                            }
                            return timing;
                        }
                    });
                }

                // Memory info spoofing
                if (window.performance && window.performance.memory) {
                    Object.defineProperty(window.performance, 'memory', {
                        get: () => ({
                            usedJSHeapSize: 16777216 + Math.floor(Math.random() * 1000000),
                            totalJSHeapSize: 33554432 + Math.floor(Math.random() * 2000000), 
                            jsHeapSizeLimit: 2172649472 + Math.floor(Math.random() * 100000000)
                        })
                    });
                }

                // Battery API blocking
                if (navigator.getBattery) {
                    navigator.getBattery = () => Promise.resolve({
                        charging: true,
                        chargingTime: 0,
                        dischargingTime: Infinity,
                        level: 1
                    });
                }
            }");

            // Phase 4: Advanced Cloudflare-specific bypasses
            await page.EvaluateFunctionOnNewDocumentAsync(@"() => {
                // Override toString methods to prevent detection
                const originalToString = Function.prototype.toString;
                Function.prototype.toString = function() {
                    if (this === window.chrome.runtime.onConnect ||
                        this === window.chrome.runtime.onMessage ||
                        this === navigator.permissions.query) {
                        return 'function () { [native code] }';
                    }
                    return originalToString.apply(this, arguments);
                };

                // Hide CDP runtime
                delete window.Runtime;
                
                // Spoof connection properties for Cloudflare
                Object.defineProperty(navigator, 'connection', {
                    get: () => ({
                        effectiveType: '4g',
                        rtt: 50 + Math.floor(Math.random() * 50),
                        downlink: 10 + Math.random() * 10
                    })
                });

                // Spoof hardwareConcurrency
                Object.defineProperty(navigator, 'hardwareConcurrency', {
                    get: () => 4 + Math.floor(Math.random() * 4)
                });

                // Remove any automation indicators
                ['webdriver', '__webdriver_evaluate', '__selenium_evaluate', '__webdriver_script_function', '__webdriver_script_func', '__webdriver_script_fn', '__fxdriver_evaluate', '__driver_unwrapped', '__webdriver_unwrapped', '__driver_evaluate', '__selenium_unwrapped', '__fxdriver_unwrapped'].forEach(prop => {
                    delete window[prop];
                });

                // Spoof screen properties with realistic values
                Object.defineProperty(screen, 'colorDepth', { get: () => 24 });
                Object.defineProperty(screen, 'pixelDepth', { get: () => 24 });

                // Add realistic language properties
                Object.defineProperty(navigator, 'languages', {
                    get: () => ['en-US', 'en']
                });
            }");

            // Phase 5: Error handling and stack trace cleaning
            await page.EvaluateFunctionOnNewDocumentAsync(@"() => {
                // Clean error stack traces to remove automation traces
                const originalPrepareStackTrace = Error.prepareStackTrace;
                Error.prepareStackTrace = function(error, stack) {
                    if (originalPrepareStackTrace) {
                        return originalPrepareStackTrace.call(this, error, stack);
                    }
                    return error.stack;
                };

                // Override error toString to hide automation
                const originalErrorToString = Error.prototype.toString;
                Error.prototype.toString = function() {
                    const result = originalErrorToString.apply(this, arguments);
                    return result.replace(/puppeteer|automation|webdriver/gi, '');
                };

                // Final stealth check - ensure no automation properties exist
                setTimeout(() => {
                    const automationProps = ['webdriver', '__webdriver_evaluate', '__selenium_evaluate', '__webdriver_script_function'];
                    automationProps.forEach(prop => {
                        if (window[prop] || navigator[prop]) {
                            delete window[prop];
                            delete navigator[prop];
                        }
                    });
                }, 100);
            }");

            data.Logger.Log("✅ Advanced stealth scripts injected successfully!", LogColors.Green);
        }

        private static async Task OpenEnhancedAntiBotBrowser(BotData data, string extraCmdLineArgs = "")
        {
            data.Logger.Log("Starting Enhanced Anti-Bot Browser (Integrated Solution)...", LogColors.DarkSalmon);

            var args = data.ConfigSettings.BrowserSettings.CommandLineArgs;

            // Advanced anti-detection arguments (combines best practices from multiple sources)
            var antiBotArgs = new[]
            {
                // Core anti-automation flags
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
                "--disable-site-isolation-trials",
                
                // Enhanced stealth flags
                "--disable-extensions-except",
                "--disable-plugins-discovery",
                "--disable-component-extensions-with-background-pages",
                "--disable-background-mode",
                "--disable-breakpad",
                "--disable-crash-reporter",
                "--disable-logging",
                "--disable-gpu-sandbox",
                "--disable-software-rasterizer",
                "--disable-threaded-animation",
                "--disable-threaded-scrolling",
                "--disable-in-process-stack-traces",
                "--disable-histogram-customizer",
                "--disable-gl-extensions",
                "--disable-composited-antialiasing",
                "--disable-canvas-aa",
                "--disable-3d-apis",
                "--disable-accelerated-2d-canvas",
                "--disable-accelerated-jpeg-decoding",
                "--disable-accelerated-mjpeg-decode",
                "--disable-app-list-dismiss-on-blur",
                "--disable-accelerated-video-decode",
                
                // Memory and performance optimization
                "--memory-pressure-off",
                "--max_old_space_size=4096",
                "--aggressive-cache-discard",
                "--enable-features=NetworkService,NetworkServiceLogging",
                
                // User agent and fingerprint evasion
                "--user-agent-override",
                "--disable-features=UserAgentClientHint",
                "--enable-features=UserAgentOverride"
            };

            // Combine with existing args
            if (!string.IsNullOrWhiteSpace(args))
            {
                args += " " + string.Join(" ", antiBotArgs);
            }
            else
            {
                args = string.Join(" ", antiBotArgs);
            }

            // Extra command line args (to have dynamic args via variables)
            if (!string.IsNullOrWhiteSpace(extraCmdLineArgs))
            {
                args += ' ' + extraCmdLineArgs;
            }

            // If it's running in docker, add the --no-sandbox 
            if (Utils.IsDocker())
            {
                args += " --no-sandbox";
            }

            // Handle proxy settings
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

            // Configure launch options with enhanced settings
            var launchOptions = new LaunchOptions
            {
                Args = new string[] { args },
                ExecutablePath = data.Providers.PuppeteerBrowser.ChromeBinaryLocation,
                IgnoredDefaultArgs = new string[] { 
                    "--enable-automation", 
                    "--enable-blink-features=AutomationControlled",
                    "--disable-extensions",
                    "--disable-component-extensions-with-background-pages",
                    "--disable-default-apps",
                    "--enable-crash-reporter"
                },
                Headless = data.ConfigSettings.BrowserSettings.Headless,
                DefaultViewport = null, // This is crucial for natural window sizing
                SlowMo = 50, // Add slight delay to mimic human behavior
                IgnoreHTTPSErrors = data.ConfigSettings.BrowserSettings.IgnoreHttpsErrors,
                Timeout = 60000 // Increased timeout for complex pages
            };

            // Launch the browser with enhanced settings
            var browser = await PuppeteerSharp.Puppeteer.LaunchAsync(launchOptions);

            // Save the browser for further use
            data.SetObject("puppeteer", browser);
            var page = (await browser.PagesAsync()).First();
            SetPageAndFrame(data, page);
            await SetPageLoadingOptions(data, page);
            
            // Apply comprehensive stealth measures
            await ApplyStealthMeasures(page);

            // Authenticate if the proxy requires auth
            if (data.UseProxy && data.Proxy is { NeedsAuthentication: true, Type: ProxyType.Http } proxy)
                await page.AuthenticateAsync(new Credentials { Username = proxy.Username, Password = proxy.Password });

            data.Logger.Log($"Enhanced Anti-Bot Browser opened successfully with integrated stealth!", LogColors.DarkSalmon);
        }

        private static async Task OpenStandardBrowser(BotData data, string extraCmdLineArgs = "")
        {
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

            // Launch the browser directly (removed PuppeteerExtraSharp stealth plugin)
            var browser = await PuppeteerSharp.Puppeteer.LaunchAsync(launchOptions);
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
    }
}
