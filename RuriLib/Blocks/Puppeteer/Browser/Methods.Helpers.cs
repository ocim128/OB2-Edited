using PuppeteerSharp;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ProxyType = RuriLib.Models.Proxies.ProxyType;

namespace RuriLib.Blocks.Puppeteer.Browser;

public static partial class Methods
{
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

    private static IBrowser GetBrowser(BotData data)
        => data.PuppeteerSession.Browser ?? throw new Exception("The browser is not open!");

    private static IPage GetPage(BotData data)
        => data.PuppeteerSession.Page ?? throw new Exception("No pages open!");

    private static void SwitchToMainFrame(BotData data)
        => data.PuppeteerSession.Frame = GetPage(data).MainFrame;

    private static void SetPageAndFrame(BotData data, IPage page)
    {
        if (page == null)
        {
            data.PuppeteerSession.Page = null;
            data.PuppeteerSession.Frame = null;
            return;
        }

        data.PuppeteerSession.Page = page;
        data.PuppeteerSession.Frame = page.MainFrame;
    }

    private static async Task PreparePageAsync(BotData data, IPage page, bool applyDefaultHeaders, bool authenticateProxy)
    {
        if (applyDefaultHeaders)
        {
            await page.SetExtraHttpHeadersAsync(BrowserHeaders).ConfigureAwait(false);
        }

        await SetPageLoadingOptions(data, page).ConfigureAwait(false);

        if (authenticateProxy)
        {
            await AuthenticateProxyIfNeededAsync(data, page).ConfigureAwait(false);
        }
    }

    private static async Task AuthenticateProxyIfNeededAsync(BotData data, IPage page)
    {
        if (data.UseProxy && data.Proxy is { NeedsAuthentication: true, Type: ProxyType.Http } proxy)
        {
            await page.AuthenticateAsync(new Credentials
            {
                Username = proxy.Username,
                Password = proxy.Password
            }).ConfigureAwait(false);
        }
    }

    private static async Task SetPageLoadingOptions(BotData data, IPage page)
    {
        var blockedUrls = data.ConfigSettings.BrowserSettings.BlockedUrls ?? new List<string>();
        var needsInterception = data.ConfigSettings.BrowserSettings.LoadOnlyDocumentAndScript
                                || blockedUrls.Any(u => !string.IsNullOrWhiteSpace(u));
        var isRealBrowser = data.PuppeteerSession.RealBrowserProcess is not null;

        if (needsInterception)
        {
            await page.SetRequestInterceptionAsync(true).ConfigureAwait(false);
            page.Request += async (_, e) =>
            {
                if (data.ConfigSettings.BrowserSettings.LoadOnlyDocumentAndScript
                    && e.Request.ResourceType != ResourceType.Document
                    && e.Request.ResourceType != ResourceType.Script)
                {
                    await e.Request.AbortAsync().ConfigureAwait(false);
                    return;
                }

                var shouldBlock = blockedUrls.Any(u =>
                    !string.IsNullOrWhiteSpace(u)
                    && e.Request.Url.Contains(u, StringComparison.OrdinalIgnoreCase));

                if (shouldBlock)
                {
                    await e.Request.AbortAsync().ConfigureAwait(false);
                }
                else
                {
                    await e.Request.ContinueAsync().ConfigureAwait(false);
                }
            };
        }
        else if (isRealBrowser)
        {
            await page.SetRequestInterceptionAsync(false).ConfigureAwait(false);
        }

        if (data.ConfigSettings.BrowserSettings.DismissDialogs)
        {
            page.Dialog += (_, e) =>
            {
                data.Logger.Log($"Dialog automatically dismissed: {e.Dialog.Message}", LogColors.DarkSalmon);
                _ = e.Dialog.Dismiss();
            };
        }
    }
}
