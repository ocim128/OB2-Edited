using PuppeteerSharp;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using RuriLib.Models.Configs.Settings;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Puppeteer.Browser;

public static partial class Methods
{
    private static readonly string[] UserAgents =
    {
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

    private static async Task ApplyConfiguredStealthMeasuresAsync(BotData data, IPage page, bool logActivation,
        string defaultModeMessage = null)
    {
        switch (data.ConfigSettings.BrowserSettings.StealthMode)
        {
            case BrowserStealthMode.Option4:
                await ApplyOption4StealthMeasures(page).ConfigureAwait(false);
                if (logActivation)
                {
                    data.Logger.Log("Option4 Stealth Mode activated - maximum anti-detection active.", LogColors.Green);
                }
                break;

            case BrowserStealthMode.EnhancedStealth:
                await ApplyEnhancedStealthMeasures(page).ConfigureAwait(false);
                if (logActivation)
                {
                    data.Logger.Log("Enhanced Stealth Mode activated - ultra anti-detection active.", LogColors.Green);
                }
                break;

            case BrowserStealthMode.Stealth:
                await ApplyStealthMeasures(page).ConfigureAwait(false);
                if (logActivation)
                {
                    data.Logger.Log("Stealth Mode activated - Cloudflare bypass active.", LogColors.Green);
                }
                break;

            case BrowserStealthMode.Default:
            default:
                if (logActivation && !string.IsNullOrWhiteSpace(defaultModeMessage))
                {
                    data.Logger.Log(defaultModeMessage, LogColors.Green);
                }
                break;
        }
    }

    private static async Task ApplyStealthMeasures(IPage page)
    {
        await page.SetUserAgentAsync(UserAgents[Random.Next(UserAgents.Length)]).ConfigureAwait(false);

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

        await Task.WhenAll(
            page.EvaluateExpressionAsync(stealthScript),
            page.SetExtraHttpHeadersAsync(DefaultHeaders)).ConfigureAwait(false);
    }

    private static async Task ApplyEnhancedStealthMeasures(IPage page)
    {
        await page.SetUserAgentAsync(UserAgents[Random.Next(UserAgents.Length)]).ConfigureAwait(false);

        const string enhancedStealthScript = @"
Object.defineProperty(navigator,'webdriver',{get:()=>undefined,configurable:true});
delete navigator.__proto__.webdriver;
if(!window.chrome||!window.chrome.runtime){window.chrome=window.chrome||{};window.chrome.runtime=window.chrome.runtime||{};}
const langs=[['en-US','en'],['en-GB','en'],['en-CA','en']];
const rLang=langs[Math.floor(Math.random()*langs.length)];
Object.defineProperty(navigator,'languages',{get:()=>rLang,configurable:true});
Object.defineProperty(navigator,'language',{get:()=>rLang[0],configurable:true});
Object.defineProperty(navigator,'plugins',{get:()=>[
    {description:'Portable Document Format',filename:'internal-pdf-viewer',length:1,name:'Chrome PDF Plugin'},
    {description:'Portable Document Format',filename:'mhjfbmdgcfjbbpaeojofohoefgiehjai',length:1,name:'Chrome PDF Viewer'}
],configurable:true});
const oQuery=navigator.permissions.query;
navigator.permissions.query=p=>p.name==='notifications'?Promise.resolve({state:'default'}):oQuery(p);
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
['hardwareConcurrency','deviceMemory','platform','vendor','userAgent','appVersion','vendorSub','productSub']
.forEach((prop,i)=>Object.defineProperty(navigator,prop,{
    get:()=>[8,8,'Win32','Google Inc.',navigator.userAgent,'5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36','',''][i],
    configurable:true
}));
['__webdriver_evaluate','__selenium_evaluate','__webdriver_script_function','__webdriver_script_func','__webdriver_script_fn','__fxdriver_evaluate','__driver_unwrapped','__webdriver_unwrapped','__selenium_unwrapped','__fxdriver_unwrapped','__webdriver_value','__selenium_value','__fxdriver_value','__driver_value','__webdriver_script_result','__selenium_script_result','__fxdriver_script_result']
.forEach(prop=>{if(window[prop])delete window[prop];});
if(window.cdc_adoQpoasnfa76pfcZLmcfl_Array)delete window.cdc_adoQpoasnfa76pfcZLmcfl_Array;
if(window.cdc_adoQpoasnfa76pfcZLmcfl_Promise)delete window.cdc_adoQpoasnfa76pfcZLmcfl_Promise;
if(window.cdc_adoQpoasnfa76pfcZLmcfl_Symbol)delete window.cdc_adoQpoasnfa76pfcZLmcfl_Symbol;
const oToString=Function.prototype.toString;
Function.prototype.toString=function(){
    if(this===navigator.webdriver||this===Object.getOwnPropertyDescriptor(navigator,'webdriver')?.get){
        return 'function webdriver() { [native code] }';
    }
    return oToString.call(this);
};
const getImageData=CanvasRenderingContext2D.prototype.getImageData;
CanvasRenderingContext2D.prototype.getImageData=function(...args){
    const imageData=getImageData.apply(this,args);
    for(let i=0;i<imageData.data.length;i+=4){
        imageData.data[i]+=Math.floor(Math.random()*2)-1;
    }
    return imageData;
};
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

        await Task.WhenAll(
            page.EvaluateExpressionAsync(enhancedStealthScript),
            page.SetExtraHttpHeadersAsync(DefaultHeaders)).ConfigureAwait(false);
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
    if (parameter === 37445) return webGLVendors[randomVendorIndex];
    if (parameter === 37446) return webGLRenderers[randomVendorIndex];
    return originalGetParameter.call(this, parameter);
};
WebGLRenderingContext.prototype.getParameter = getParameterOverride(WebGLRenderingContext.prototype.getParameter);
if (typeof WebGL2RenderingContext !== 'undefined') {
    WebGL2RenderingContext.prototype.getParameter = getParameterOverride(WebGL2RenderingContext.prototype.getParameter);
}
const hardwareConcurrencies = [4, 8, 16];
const deviceMemories = [4, 8, 16];
const platforms = ['Win32', 'MacIntel', 'Linux x86_64'];
const randomIndex = Math.floor(Math.random() * 3);
Object.defineProperty(navigator, 'hardwareConcurrency', { get: () => hardwareConcurrencies[randomIndex] });
Object.defineProperty(navigator, 'deviceMemory', { get: () => deviceMemories[randomIndex] });
Object.defineProperty(navigator, 'platform', { get: () => platforms[randomIndex] });
Object.defineProperty(navigator, 'vendor', { get: () => 'Google Inc.' });
['cdc_adoQpoasnfa76pfcZLmcfl_Array', 'cdc_adoQpoasnfa76pfcZLmcfl_Promise', 'cdc_adoQpoasnfa76pfcZLmcfl_Symbol'].forEach(prop => {
    if (window[prop]) delete window[prop];
});
const originalToString = Function.prototype.toString;
Function.prototype.toString = function () {
    if (this === navigator.webdriver || this === Object.getOwnPropertyDescriptor(navigator, 'webdriver')?.get) {
        return 'function webdriver() { [native code] }';
    }
    return originalToString.call(this);
};
const originalGetImageData = CanvasRenderingContext2D.prototype.getImageData;
CanvasRenderingContext2D.prototype.getImageData = function (...args) {
    const imageData = originalGetImageData.apply(this, args);
    const data = imageData.data;
    for (let i = 0; i < data.length; i += 4) {
        data[i] = data[i] + Math.floor(Math.random() * 3) - 1;
        data[i + 1] = data[i + 1] + Math.floor(Math.random() * 3) - 1;
        data[i + 2] = data[i + 2] + Math.floor(Math.random() * 3) - 1;
    }
    return imageData;
};
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
Object.defineProperty(window, 'outerWidth', { get: () => window.innerWidth + Math.floor(Math.random() * 20) + 10 });
Object.defineProperty(window, 'outerHeight', { get: () => window.innerHeight + Math.floor(Math.random() * 50) + 50 });
navigator.maxTouchPoints = 0;
";

        await page.EvaluateExpressionAsync(stealthScript).ConfigureAwait(false);
    }
}
