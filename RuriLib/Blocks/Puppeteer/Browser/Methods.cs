using Yove.Proxy;
using PuppeteerSharp;
using RuriLib.Attributes;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using RuriLib.Models.Configs.Settings;
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

        // Always use real browser as default, with optional stealth mode
        await OpenIntegratedRealBrowser(data, extraCmdLineArgs);
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

        // Apply stealth measures to the new tab based on selected mode
        switch (data.ConfigSettings.BrowserSettings.StealthMode)
        {
            case BrowserStealthMode.Option4:
                await ApplyOption4StealthMeasures(page);
                break;
            case BrowserStealthMode.EnhancedStealth:
                await ApplyEnhancedStealthMeasures(page);
                break;
            case BrowserStealthMode.Stealth:
                await ApplyStealthMeasures(page);
                break;
            case BrowserStealthMode.Default:
            default:
                // No stealth measures for default mode
                break;
        }

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

    private static async Task ApplyEnhancedStealthMeasures(IPage page)
    {
        // Set random user agent from cached array
        await page.SetUserAgentAsync(UserAgents[Random.Next(UserAgents.Length)]);

        // Enhanced stealth script for maximum anti-detection
        const string enhancedStealthScript = @"
// Enhanced anti-detection measures
Object.defineProperty(navigator,'webdriver',{get:()=>undefined,configurable:true});
delete navigator.__proto__.webdriver;

// Chrome runtime simulation
if(!window.chrome||!window.chrome.runtime){window.chrome=window.chrome||{};window.chrome.runtime=window.chrome.runtime||{};}

// Languages simulation
const langs=[['en-US','en'],['en-GB','en'],['en-CA','en']];
const rLang=langs[Math.floor(Math.random()*langs.length)];
Object.defineProperty(navigator,'languages',{get:()=>rLang,configurable:true});
Object.defineProperty(navigator,'language',{get:()=>rLang[0],configurable:true});

// Plugins simulation
Object.defineProperty(navigator,'plugins',{get:()=>[
    {description:'Portable Document Format',filename:'internal-pdf-viewer',length:1,name:'Chrome PDF Plugin'},
    {description:'Portable Document Format',filename:'mhjfbmdgcfjbbpaeojofohoefgiehjai',length:1,name:'Chrome PDF Viewer'}
],configurable:true});

// Permissions simulation
const oQuery=navigator.permissions.query;
navigator.permissions.query=p=>p.name==='notifications'?Promise.resolve({state:'default'}):oQuery(p);

// WebGL fingerprinting protection
const gParam=WebGLRenderingContext.prototype.getParameter;
WebGLRenderingContext.prototype.getParameter=function(p){
    switch(p){
        case 37445: return 'Intel Inc.';
        case 37446: return 'Intel(R) HD Graphics';
        case 3379: return 16384;
        case 36347: return 4096;
        case 36348: return 30;
        case 36349: return 30;
        case 7936: return 'WebKit';
        case 7937: return 'WebKit WebGL';
        case 7938: return 'WebGL 1.0 (OpenGL ES 2.0 Chromium)';
        default: return gParam.call(this,p);
    }
};

// WebGL2 protection
if(typeof WebGL2RenderingContext!=='undefined'){
    const gParam2=WebGL2RenderingContext.prototype.getParameter;
    WebGL2RenderingContext.prototype.getParameter=function(p){
        switch(p){
            case 37445: return 'Intel Inc.';
            case 37446: return 'Intel(R) HD Graphics';
            default: return gParam2.call(this,p);
        }
    };
}

// Navigator properties simulation
['hardwareConcurrency','deviceMemory','platform','vendor','userAgent','appVersion','vendorSub','productSub']
.forEach((prop,i)=>Object.defineProperty(navigator,prop,{
    get:()=>[8,8,'Win32','Google Inc.',navigator.userAgent,'5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36','',''][i],
    configurable:true
}));

// Remove automation properties
['__webdriver_evaluate','__selenium_evaluate','__webdriver_script_function','__webdriver_script_func','__webdriver_script_fn','__fxdriver_evaluate','__driver_unwrapped','__webdriver_unwrapped','__selenium_unwrapped','__fxdriver_unwrapped','__webdriver_value','__selenium_value','__fxdriver_value','__driver_value','__webdriver_script_result','__selenium_script_result','__fxdriver_script_result']
.forEach(prop=>{if(window[prop])delete window[prop];});

// Remove automation detection variables
if(window.cdc_adoQpoasnfa76pfcZLmcfl_Array)delete window.cdc_adoQpoasnfa76pfcZLmcfl_Array;
if(window.cdc_adoQpoasnfa76pfcZLmcfl_Promise)delete window.cdc_adoQpoasnfa76pfcZLmcfl_Promise;
if(window.cdc_adoQpoasnfa76pfcZLmcfl_Symbol)delete window.cdc_adoQpoasnfa76pfcZLmcfl_Symbol;

// Override toString for webdriver detection
const oToString=Function.prototype.toString;
Function.prototype.toString=function(){
    if(this===navigator.webdriver||this===Object.getOwnPropertyDescriptor(navigator,'webdriver')?.get){
        return 'function webdriver() { [native code] }';
    }
    return oToString.call(this);
};

// Canvas fingerprinting protection
const getImageData=CanvasRenderingContext2D.prototype.getImageData;
CanvasRenderingContext2D.prototype.getImageData=function(...args){
    const imageData=getImageData.apply(this,args);
    // Add subtle noise to prevent fingerprinting
    for(let i=0;i<imageData.data.length;i+=4){
        imageData.data[i]+=Math.floor(Math.random()*2)-1;
    }
    return imageData;
};

// Audio fingerprinting protection
const createAnalyser=BaseAudioContext.prototype.createAnalyser;
BaseAudioContext.prototype.createAnalyser=function(){
    const analyser=createAnalyser.call(this);
    const getFloatFrequencyData=analyser.getFloatFrequencyData;
    analyser.getFloatFrequencyData=function(array){
        getFloatFrequencyData.call(this,array);
        for(let i=0;i<array.length;i++){
            array[i]+=Math.random()*0.000001;
        }
    };
    return analyser;
};

// Battery API protection
if(navigator.getBattery){
    const getBattery=navigator.getBattery;
    navigator.getBattery=function(){
        return getBattery.call(this).then(battery=>{
            Object.defineProperty(battery,'charging',{get:()=>true,configurable:true});
            Object.defineProperty(battery,'chargingTime',{get:()=>0,configurable:true});
            Object.defineProperty(battery,'dischargingTime',{get:()=>Infinity,configurable:true});
            Object.defineProperty(battery,'level',{get:()=>0.99,configurable:true});
            return battery;
        });
    };
}

// WebRTC IP leak protection
if(window.RTCPeerConnection){
    const RTCPeerConnection=window.RTCPeerConnection;
    window.RTCPeerConnection=function(...args){
        const pc=new RTCPeerConnection(...args);
        const createDataChannel=pc.createDataChannel;
        pc.createDataChannel=function(...args){
            const channel=createDataChannel.apply(this,args);
            channel.addEventListener('open',()=>{
                channel.send('');
            });
            return channel;
        };
        return pc;
    };
}
";

        // Execute enhanced stealth script and set headers
        await Task.WhenAll(
            page.EvaluateExpressionAsync(enhancedStealthScript),
            page.SetExtraHttpHeadersAsync(DefaultHeaders)
        );
    }

    private static async Task ApplyOption4StealthMeasures(IPage page)
    {
        const string stealthScript = @"
Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
delete navigator.__proto__.webdriver;
if (!window.chrome || !window.chrome.runtime) {
    window.chrome = window.chrome || {};
    window.chrome.runtime = window.chrome.runtime || {};
    window.chrome.app = {
        isInstalled: false,
        getDetails: () => null,
        getIsInstalled: () => false,
        runningState: () => 'cannot_run'
    };
    window.chrome.csi = () => ({
        startE: Date.now() - Math.floor(Math.random() * 10000),
        pageT: Math.floor(Math.random() * 1000000) + 12345.67,
        tran: 15
    });
    window.chrome.loadTimes = () => {
        const now = performance.now() / 1000;
        return {
            requestTime: now - 100,
            startLoadTime: now - 100,
            commitLoadTime: now - 90,
            finishDocumentLoadTime: now - 80,
            finishLoadTime: now - 70,
            firstPaintTime: now - 60,
            firstPaintAfterLoadTime: 0,
            navigationType: 'Other',
            wasFetchedViaSpdy: true,
            wasNpnNegotiated: true,
            npnNegotiatedProtocol: 'h2',
            wasAlternateProtocolAvailable: false,
            connectionInfo: 'CONNECTION_INFO_UNKNOWN'
        };
    };
}
const languagesOptions = [
    ['en-US', 'en'],
    ['en-GB', 'en'],
    ['fr-FR', 'fr'],
    ['de-DE', 'de'],
    ['es-ES', 'es']
];
const randomLanguages = languagesOptions[Math.floor(Math.random() * languagesOptions.length)];
Object.defineProperty(navigator, 'languages', { get: () => randomLanguages });
Object.defineProperty(navigator, 'language', { get: () => randomLanguages[0] });
const pluginsData = [
    {
        name: 'Chrome PDF Plugin',
        filename: 'internal-pdf-viewer',
        description: 'Portable Document Format'
    },
    {
        name: 'Chrome PDF Viewer',
        filename: 'mhjfbmdgcfjbbpaeojofohoefgiehjai',
        description: ''
    },
    {
        name: 'Native Client',
        filename: 'internal-nacl',
        description: ''
    }
];
const mimeTypesData = [
    { type: 'application/pdf', suffixes: 'pdf', description: '' },
    { type: 'application/x-google-chrome-pdf', suffixes: 'pdf', description: 'Portable Document Format' },
    { type: 'application/x-nacl', suffixes: '', description: 'Native Client Executable' },
    { type: 'application/x-pnacl', suffixes: '', description: 'Portable Native Client Executable' }
];
Object.defineProperty(navigator, 'plugins', {
    get: () => {
        const plugins = [];
        pluginsData.forEach((p, index) => {
            const plugin = {
                name: p.name,
                filename: p.filename,
                description: p.description,
                length: 1,
                0: mimeTypesData[index]
            };
            plugins.push(plugin);
        });
        plugins.length = pluginsData.length;
        return plugins;
    }
});
Object.defineProperty(navigator, 'mimeTypes', {
    get: () => {
        const mimeTypes = [];
        mimeTypesData.forEach(m => {
            const mime = {
                type: m.type,
                suffixes: m.suffixes,
                description: m.description
            };
            mimeTypes.push(mime);
        });
        mimeTypes.length = mimeTypesData.length;
        return mimeTypes;
    }
});
const originalPermissionsQuery = navigator.permissions.query;
navigator.permissions.query = p => p.name === 'notifications' ? Promise.resolve({ state: 'default' }) : originalPermissionsQuery(p);
const webGLVendors = ['Intel Inc.', 'NVIDIA Corporation', 'Qualcomm', 'ATI Technologies Inc.'];
const webGLRenderers = ['Intel(R) HD Graphics', 'NVIDIA GeForce GTX 1050', 'Adreno (TM) 630', 'AMD Radeon Pro 5300M'];
const randomVendorIndex = Math.floor(Math.random() * webGLVendors.length);
const getParameterOverride = (originalGetParameter) => function(parameter) {
    if (parameter === 37445) return webGLVendors[randomVendorIndex]; // UNMASKED_VENDOR_WEBGL
    if (parameter === 37446) return webGLRenderers[randomVendorIndex]; // UNMASKED_RENDERER_WEBGL
    return originalGetParameter.call(this, parameter);
};
WebGLRenderingContext.prototype.getParameter = getParameterOverride(WebGLRenderingContext.prototype.getParameter);
if (typeof WebGL2RenderingContext !== 'undefined') {
    WebGL2RenderingContext.prototype.getParameter = getParameterOverride(WebGL2RenderingContext.prototype.getParameter);
}
// Randomize hardware properties
const hardwareConcurrencies = [4, 8, 16];
const deviceMemories = [4, 8, 16];
const platforms = ['Win32', 'MacIntel', 'Linux x86_64'];
const randomIndex = Math.floor(Math.random() * 3);
Object.defineProperty(navigator, 'hardwareConcurrency', { get: () => hardwareConcurrencies[randomIndex] });
Object.defineProperty(navigator, 'deviceMemory', { get: () => deviceMemories[randomIndex] });
Object.defineProperty(navigator, 'platform', { get: () => platforms[randomIndex] });
Object.defineProperty(navigator, 'vendor', { get: () => 'Google Inc.' });
// Remove CDC properties
['cdc_adoQpoasnfa76pfcZLmcfl_Array', 'cdc_adoQpoasnfa76pfcZLmcfl_Promise', 'cdc_adoQpoasnfa76pfcZLmcfl_Symbol'].forEach(prop => {
    if (window[prop]) delete window[prop];
});
// Function toString override
const originalToString = Function.prototype.toString;
Function.prototype.toString = function () {
    if (this === navigator.webdriver || this === Object.getOwnPropertyDescriptor(navigator, 'webdriver')?.get) {
        return 'function webdriver() { [native code] }';
    }
    return originalToString.call(this);
};
// Canvas fingerprint noise
const originalGetImageData = CanvasRenderingContext2D.prototype.getImageData;
CanvasRenderingContext2D.prototype.getImageData = function (...args) {
    const imageData = originalGetImageData.apply(this, args);
    const data = imageData.data;
    for (let i = 0; i < data.length; i += 4) {
        data[i] = data[i] + Math.floor(Math.random() * 3) - 1;     // R
        data[i + 1] = data[i + 1] + Math.floor(Math.random() * 3) - 1; // G
        data[i + 2] = data[i + 2] + Math.floor(Math.random() * 3) - 1; // B
        // Alpha remains unchanged
    }
    return imageData;
};
// Audio fingerprint noise (for OfflineAudioContext)
if (OfflineAudioContext) {
    const originalCreateBuffer = OfflineAudioContext.prototype.createBuffer;
    OfflineAudioContext.prototype.createBuffer = function (channels, length, sampleRate) {
        const buffer = originalCreateBuffer.call(this, channels, length, sampleRate);
        const originalGetChannelData = buffer.getChannelData;
        buffer.getChannelData = function (channel) {
            const data = originalGetChannelData.call(this, channel);
            for (let i = 0; i < data.length; i++) {
                data[i] += (Math.random() * 2 - 1) * 0.00001;
            }
            return data;
        };
        return buffer;
    };
}
// Window dimensions to mimic real browser
Object.defineProperty(window, 'outerWidth', { get: () => window.innerWidth + Math.floor(Math.random() * 20) + 10 });
Object.defineProperty(window, 'outerHeight', { get: () => window.innerHeight + Math.floor(Math.random() * 50) + 50 });
// Additional properties
navigator.maxTouchPoints = 0;
";

        await page.EvaluateExpressionAsync(stealthScript);
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
    private static readonly List<string> BaseBrowserArgs = new()
    {
        // "--no-sandbox",
        // "--disable-popup-blocking",
        // "--no-first-run",
        // "--no-default-browser-check",
        // "--disable-background-networking",
        // "--disable-client-side-phishing-detection",
        // "--disable-gpu",
        // "--disable-logging",
        // "--ignore-certificate-errors",
        // "--ignore-ssl-errors",
        // "--allow-running-insecure-content"
    };

    private static readonly List<string> StealthArgs = new()
    {
        "--disable-blink-features=AutomationControlled"
    };

    private static readonly List<string> EnhancedStealthArgs = new()
    {
        "--disable-blink-features=AutomationControlled",
        "--disable-features=IsolateOrigins,site-per-process",
        "--disable-site-isolation-trials",
        "--disable-web-security",
        "--disable-features=VizDisplayCompositor",
        "--disable-features=TranslateUI",
        "--disable-extensions-except",
        "--disable-default-apps",
        "--no-default-browser-check",
        "--disable-component-extensions-with-background-pages",
        "--disable-background-timer-throttling",
        "--disable-renderer-backgrounding",
        "--disable-backgrounding-occluded-windows",
        "--disable-ipc-flooding-protection",
        "--password-store=basic",
        "--use-mock-keychain",
        "--disable-dev-shm-usage",
        "--no-sandbox",
        "--disable-setuid-sandbox",
        "--disable-gpu-sandbox",
        "--disable-software-rasterizer"
    };

    private static readonly List<string> Option4StealthArgs = new()
    {
        "--disable-blink-features=AutomationControlled",
        "--disable-features=IsolateOrigins,site-per-process",
        "--disable-site-isolation-trials",
        "--disable-web-security",
        "--disable-features=VizDisplayCompositor",
        "--disable-features=TranslateUI",
        "--disable-extensions-except",
        "--disable-default-apps",
        "--no-default-browser-check",
        "--disable-component-extensions-with-background-pages",
        "--disable-background-timer-throttling",
        "--disable-renderer-backgrounding",
        "--disable-backgrounding-occluded-windows",
        "--disable-ipc-flooding-protection",
        "--password-store=basic",
        "--use-mock-keychain",
        "--disable-dev-shm-usage",
        "--no-sandbox",
        "--disable-setuid-sandbox",
        "--disable-gpu-sandbox",
        "--disable-software-rasterizer",
        "--disable-features=PrivacySandboxSettings4",
        "--disable-features=PrivacySandboxAdsAPIs",
        "--disable-features=TrackingProtection3pcd",
        "--disable-features=InterestCohortFeaturePolicy",
        "--disable-features=Fledge",
        "--disable-features=FledgeBiddingAndAuctionServer",
        "--disable-features=SharedStorage",
        "--disable-features=PrivateAggregationApi",
        "--disable-features=PrivateAggregationApiFledgeExtensions",
        "--disable-features=AttributionReporting",
        "--disable-features=AttributionReportingCrossAppWeb"
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
        var browserArgs = new List<string>(BaseBrowserArgs);

        // Add stealth arguments based on selected mode
        switch (data.ConfigSettings.BrowserSettings.StealthMode)
        {
            case BrowserStealthMode.Option4:
                browserArgs.AddRange(Option4StealthArgs);
                break;
            case BrowserStealthMode.Stealth:
                browserArgs.AddRange(StealthArgs);
                break;
            case BrowserStealthMode.EnhancedStealth:
                browserArgs.AddRange(EnhancedStealthArgs);
                break;
            case BrowserStealthMode.Default:
            default:
                // No additional stealth arguments for default mode
                break;
        }

        // Add user-provided arguments with proper quoted argument handling
        if (!string.IsNullOrWhiteSpace(data.ConfigSettings.BrowserSettings.CommandLineArgs))
        {
            browserArgs.AddRange(ParseCommandLineArgs(data.ConfigSettings.BrowserSettings.CommandLineArgs));
        }

        if (!string.IsNullOrWhiteSpace(extraCmdLineArgs))
        {
            browserArgs.AddRange(ParseCommandLineArgs(extraCmdLineArgs));
        }

        // Remove arguments that conflict with extension loading
        browserArgs.RemoveAll(arg => arg.Contains("--disable-extensions") ||
                                   arg.Contains("--disable-plugins") ||
                                   arg.Contains("--disable-default-apps"));

        // Always enable extensions for full functionality
        if (!browserArgs.Contains("--enable-extensions"))
        {
            browserArgs.Add("--enable-extensions");
        }

        // Handle --load-extension arguments with --disable-extensions-except for better compatibility
        var loadExtensionArgs = browserArgs.Where(arg => arg.StartsWith("--load-extension=")).ToList();
        if (loadExtensionArgs.Any())
        {
            // Remove existing load-extension args to rebuild them with quotes
            browserArgs.RemoveAll(arg => arg.StartsWith("--load-extension="));

            foreach (var loadExtArg in loadExtensionArgs)
            {
                var extensionPath = loadExtArg.Substring("--load-extension=".Length);

                // Re-add the load-extension argument with quotes for proper path handling
                browserArgs.Add($"--load-extension=\"{extensionPath}\"");

                // Add --disable-extensions-except for better Chrome compatibility
                if (!browserArgs.Any(arg => arg.StartsWith("--disable-extensions-except=")))
                {
                    browserArgs.Add($"--disable-extensions-except=\"{extensionPath}\"");
                }
            }
        }

        // Add Chrome Web Store access
        browserArgs.Add("--disable-web-security");
        browserArgs.Add("--disable-features=VizDisplayCompositor");

        // Configure proxy if needed
        if (data.UseProxy && data.Proxy != null)
        {
            browserArgs.Add($"--proxy-server={data.Proxy.Type.ToString().ToLower(System.Globalization.CultureInfo.CurrentCulture)}://{data.Proxy.Host}:{data.Proxy.Port}");
            if (data.Proxy.NeedsAuthentication)
            {
                browserArgs.Add($"--proxy-auth={data.Proxy.Username}:{data.Proxy.Password}");
            }
        }

        // Remove duplicates and conflicting arguments
        browserArgs = browserArgs.Distinct().Where(static arg => !string.IsNullOrWhiteSpace(arg)).ToList();

        // Debug: Log all command line arguments being passed to browser
        data.Logger.Log($"🔧 Browser arguments: {string.Join(" ", browserArgs)}", LogColors.Yellow);

        // Capture headless setting
        var headless = data.ConfigSettings.BrowserSettings.Headless;
        var launchOptions = new LaunchOptions
        {
            Headless = headless,
            Args = browserArgs.ToArray(),
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

        // Apply stealth measures based on selected mode
        switch (data.ConfigSettings.BrowserSettings.StealthMode)
        {
            case BrowserStealthMode.Option4:
                await ApplyOption4StealthMeasures(page);
                data.Logger.Log("🛡️🛡️🛡️ Option4 Stealth Mode activated - Maximum anti-detection active!", LogColors.Green);
                break;
            case BrowserStealthMode.EnhancedStealth:
                await ApplyEnhancedStealthMeasures(page);
                data.Logger.Log("🛡️🛡️ Enhanced Stealth Mode activated - Ultra anti-detection active!", LogColors.Green);
                break;
            case BrowserStealthMode.Stealth:
                await ApplyStealthMeasures(page);
                data.Logger.Log("🛡️ Stealth Mode activated - Cloudflare bypass active!", LogColors.Green);
                break;
            case BrowserStealthMode.Default:
            default:
                data.Logger.Log("✅ Default Mode - Standard browser behavior", LogColors.Green);
                break;
        }

        // Handle proxy authentication
        if (data.UseProxy && data.Proxy is { NeedsAuthentication: true, Type: ProxyType.Http } proxy)
        {
            await page.AuthenticateAsync(new Credentials { Username = proxy.Username, Password = proxy.Password });
        }
    }
}
