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

namespace RuriLib.Blocks.Puppeteer.Browser;

[BlockCategory("Browser", "Blocks for interacting with a puppeteer browser", "#e9967a")]
public static class Methods
{
    [Block("Opens a new puppeteer browser", name = "Open Browser")]
    public static async Task PuppeteerOpenBrowser(BotData data, string extraCmdLineArgs = "")
    {
        data.Logger.LogHeader();

        // Check if there is already an open browser
        var oldBrowser = data.TryGetObject<IBrowser>("puppeteer");
        if (oldBrowser?.IsClosed == false)
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
        data.Logger.Log("Opened a new page", LogColors.DarkSalmon);
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
        {
            await page.BringToFrontAsync();
        }

        data.Logger.Log("Closed the active page", LogColors.DarkSalmon);
    }

    [Block("Switches to the browser tab with a specified index", name = "Switch to Tab")]
    public static async Task PuppeteerSwitchToTab(BotData data, int index)
    {
        data.Logger.LogHeader();

        var browser = GetBrowser(data);

        // Workaround https://github.com/hardkoded/puppeteer-sharp/issues/1587
        _ = await browser.GetVersionAsync();

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
        _ = await page.ReloadAsync();
        SwitchToMainFramePrivate(data);

        data.Logger.Log("Reloaded the page", LogColors.DarkSalmon);
    }

    [Block("Goes back to the previously visited page", name = "Go Back")]
    public static async Task PuppeteerGoBack(BotData data)
    {
        data.Logger.LogHeader();

        var page = GetPage(data);
        _ = await page.GoBackAsync();
        SwitchToMainFramePrivate(data);

        data.Logger.Log("Went back to the previously visited page", LogColors.DarkSalmon);
    }

    [Block("Goes forward to the next visited page", name = "Go Forward")]
    public static async Task PuppeteerGoForward(BotData data)
    {
        data.Logger.LogHeader();

        var page = GetPage(data);
        _ = await page.GoForwardAsync();
        SwitchToMainFramePrivate(data);

        data.Logger.Log("Went forward to the next visited page", LogColors.DarkSalmon);
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
            _ = data.ConfigSettings.BrowserSettings.LoadOnlyDocumentAndScript &&
                e.Request.ResourceType != ResourceType.Document && e.Request.ResourceType != ResourceType.Script
                ? e.Request.AbortAsync()
                : data.ConfigSettings.BrowserSettings.BlockedUrls
                                    .Any(u => !string.IsNullOrWhiteSpace(u) && e.Request.Url.Contains(u, StringComparison.OrdinalIgnoreCase))
                    ? e.Request.AbortAsync()
                    : e.Request.ContinueAsync();
        };

        if (data.ConfigSettings.BrowserSettings.DismissDialogs)
        {
            page.Dialog += (sender, e) =>
            {
                data.Logger.Log($"Dialog automatically dismissed: {e.Dialog.Message}", LogColors.DarkSalmon);
                _ = e.Dialog.Dismiss();
            };
        }
    }

    // Cache static data for better performance
    private static readonly string[] UserAgents = {
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:109.0) Gecko/20100101 Firefox/119.0"
    };

    private static readonly Random Random = new();

    private static readonly Dictionary<string, string> DefaultHeaders = new()
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
    };

    private static async Task ApplyStealthMeasures(IPage page)
    {
        // Set random user agent from cached array
        await page.SetUserAgentAsync(UserAgents[Random.Next(UserAgents.Length)]);

        // Optimized stealth script that preserves extension functionality
        const string stealthScript = @"
Object.defineProperty(navigator,'webdriver',{get:()=>undefined});
delete navigator.__proto__.webdriver;
if(!window.chrome||!window.chrome.runtime){window.chrome=window.chrome||{};window.chrome.runtime=window.chrome.runtime||{};}
const langs=[['en-US','en'],['en-GB','en']];
const rLang=langs[Math.floor(Math.random()*langs.length)];
Object.defineProperty(navigator,'languages',{get:()=>rLang});
Object.defineProperty(navigator,'language',{get:()=>rLang[0]});
Object.defineProperty(navigator,'plugins',{get:()=>[{description:'Portable Document Format',filename:'internal-pdf-viewer',length:1,name:'Chrome PDF Plugin'}]});
const oQuery=navigator.permissions.query;
navigator.permissions.query=p=>p.name==='notifications'?Promise.resolve({state:'default'}):oQuery(p);
const gParam=WebGLRenderingContext.prototype.getParameter;
WebGLRenderingContext.prototype.getParameter=function(p){return p===37445?'Intel Inc.':p===37446?'Intel(R) HD Graphics':gParam.call(this,p)};
if(typeof WebGL2RenderingContext!=='undefined'){const gParam2=WebGL2RenderingContext.prototype.getParameter;WebGL2RenderingContext.prototype.getParameter=function(p){return p===37445?'Intel Inc.':p===37446?'Intel(R) HD Graphics':gParam2.call(this,p)};}
['hardwareConcurrency','deviceMemory','platform','vendor'].forEach((prop,i)=>Object.defineProperty(navigator,prop,{get:()=>[8,8,'Win32','Google Inc.'][i]}));
if(window.cdc_adoQpoasnfa76pfcZLmcfl_Array)delete window.cdc_adoQpoasnfa76pfcZLmcfl_Array;
if(window.cdc_adoQpoasnfa76pfcZLmcfl_Promise)delete window.cdc_adoQpoasnfa76pfcZLmcfl_Promise;
if(window.cdc_adoQpoasnfa76pfcZLmcfl_Symbol)delete window.cdc_adoQpoasnfa76pfcZLmcfl_Symbol;
const oToString=Function.prototype.toString;
Function.prototype.toString=function(){return this===navigator.webdriver||this===Object.getOwnPropertyDescriptor(navigator,'webdriver')?.get?'function webdriver() { [native code] }':oToString.call(this)};
";

        // Execute stealth script and set headers efficiently
        await Task.WhenAll(
            page.EvaluateExpressionAsync(stealthScript),
            page.SetExtraHttpHeadersAsync(DefaultHeaders)
        );
    }

    private static async Task OpenRealBrowserWithFallback(BotData data, string extraCmdLineArgs = "")
    {
        data.Logger.Log("🛡️ Starting Integrated Real Browser...", LogColors.DarkSalmon);
        await OpenIntegratedRealBrowser(data, extraCmdLineArgs);
    }

    private static List<string> ParseCommandLineArgs(string commandLine)
    {
        var args = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine))
            return args;

        var currentArg = new System.Text.StringBuilder();
        var inQuotes = false;
        var escapeNext = false;

        for (int i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];

            if (escapeNext)
            {
                currentArg.Append(c);
                escapeNext = false;
            }
            else if (c == '\\' && i + 1 < commandLine.Length && (commandLine[i + 1] == '"' || commandLine[i + 1] == '\\'))
            {
                escapeNext = true;
            }
            else if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (currentArg.Length > 0)
                {
                    args.Add(currentArg.ToString());
                    currentArg.Clear();
                }
            }
            else
            {
                currentArg.Append(c);
            }
        }

        if (currentArg.Length > 0)
        {
            args.Add(currentArg.ToString());
        }

        return args;
    }

    // Cache optimized Chrome arguments for better performance
    private static readonly List<string> BaseStealthArgs = new()
    {
        "--no-sandbox",
        "--disable-blink-features=AutomationControlled",
        "--disable-popup-blocking",
        "--no-first-run",
        "--no-default-browser-check",
        "--disable-background-networking",
        "--disable-client-side-phishing-detection",
        "--disable-gpu",
        "--disable-logging",
        "--ignore-certificate-errors",
        "--ignore-ssl-errors",
        "--allow-running-insecure-content"
    };

    private static readonly Dictionary<string, string> BrowserHeaders = new()
    {
        ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8",
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
    };

    private static async Task OpenIntegratedRealBrowser(BotData data, string extraCmdLineArgs = "")
    {
        var stealthArgs = new List<string>(BaseStealthArgs);

        // Add user-provided arguments with proper quoted argument handling
        if (!string.IsNullOrWhiteSpace(data.ConfigSettings.BrowserSettings.CommandLineArgs))
        {
            stealthArgs.AddRange(ParseCommandLineArgs(data.ConfigSettings.BrowserSettings.CommandLineArgs));
        }

        if (!string.IsNullOrWhiteSpace(extraCmdLineArgs))
        {
            stealthArgs.AddRange(ParseCommandLineArgs(extraCmdLineArgs));
        }

        // Remove arguments that conflict with extension loading
        stealthArgs.RemoveAll(arg => arg.Contains("--disable-extensions") ||
                                   arg.Contains("--disable-plugins") ||
                                   arg.Contains("--disable-default-apps"));

        // Always enable extensions for full functionality
        if (!stealthArgs.Contains("--enable-extensions"))
        {
            stealthArgs.Add("--enable-extensions");
        }

        // Handle --load-extension arguments with --disable-extensions-except for better compatibility
        var loadExtensionArgs = stealthArgs.Where(arg => arg.StartsWith("--load-extension=")).ToList();
        if (loadExtensionArgs.Any())
        {
            // Remove existing load-extension args to rebuild them with quotes
            stealthArgs.RemoveAll(arg => arg.StartsWith("--load-extension="));

            foreach (var loadExtArg in loadExtensionArgs)
            {
                var extensionPath = loadExtArg.Substring("--load-extension=".Length);

                // Re-add the load-extension argument with quotes for proper path handling
                stealthArgs.Add($"--load-extension=\"{extensionPath}\"");

                // Add --disable-extensions-except for better Chrome compatibility
                if (!stealthArgs.Any(arg => arg.StartsWith("--disable-extensions-except=")))
                {
                    stealthArgs.Add($"--disable-extensions-except=\"{extensionPath}\"");
                }
            }
        }

        // Add Chrome Web Store access
        stealthArgs.Add("--disable-web-security");
        stealthArgs.Add("--disable-features=VizDisplayCompositor");

        // Configure proxy if needed
        if (data.UseProxy && data.Proxy != null)
        {
            stealthArgs.Add($"--proxy-server={data.Proxy.Type.ToString().ToLower(System.Globalization.CultureInfo.CurrentCulture)}://{data.Proxy.Host}:{data.Proxy.Port}");
            if (data.Proxy.NeedsAuthentication)
            {
                stealthArgs.Add($"--proxy-auth={data.Proxy.Username}:{data.Proxy.Password}");
            }
        }

        // Remove duplicates and conflicting arguments
        stealthArgs = stealthArgs.Distinct().Where(static arg => !string.IsNullOrWhiteSpace(arg)).ToList();

        // Debug: Log all command line arguments being passed to browser
        data.Logger.Log($"🔧 Browser arguments: {string.Join(" ", stealthArgs)}", LogColors.Yellow);

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
            IgnoredDefaultArgs =
            [
                "--enable-automation",
                "--enable-blink-features=IdleDetection",
                "--enable-blink-features=AutomationControlled",
                "--disable-extensions",
                "--disable-component-extensions-with-background-pages"
            ]
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

        // Set cached headers for better performance
        await page.SetExtraHttpHeadersAsync(BrowserHeaders);

        // Save objects for further use
        data.SetObject("puppeteer", browser);
        SetPageAndFrame(data, page);
        await SetPageLoadingOptions(data, page);

        // Handle proxy authentication
        if (data.UseProxy && data.Proxy is { NeedsAuthentication: true, Type: ProxyType.Http } proxy)
        {
            await page.AuthenticateAsync(new Credentials { Username = proxy.Username, Password = proxy.Password });
        }

        data.Logger.Log("🛡️ Integrated Real Browser ready - Maximum Cloudflare bypass active!", LogColors.Green);
    }
}
