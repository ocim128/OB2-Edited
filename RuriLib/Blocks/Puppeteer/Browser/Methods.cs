using PuppeteerSharp;
using RuriLib.Attributes;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Puppeteer.Browser;

[BlockCategory("Browser", "Blocks for interacting with a puppeteer browser", "#e9967a")]
public static partial class Methods
{
    [Block("Opens a new puppeteer browser", name = "Open Browser")]
    public static async Task PuppeteerOpenBrowser(BotData data, string extraCmdLineArgs = "", bool? useRealBrowser = null)
    {
        data.Logger.LogHeader();

        var oldBrowser = data.PuppeteerSession.Browser;
        if (oldBrowser?.IsClosed == false)
        {
            data.Logger.Log("The browser is already open, close it if you want to open a new browser", LogColors.DarkSalmon);
            return;
        }

        var providerPreference = data.Providers?.PuppeteerBrowser?.UseRealBrowser ?? false;
        var shouldUseRealBrowser = useRealBrowser ?? providerPreference;

        if (shouldUseRealBrowser)
        {
            await OpenRealBrowserAsync(data, extraCmdLineArgs).ConfigureAwait(false);
        }
        else
        {
            await OpenPuppeteerSharpBrowser(data, extraCmdLineArgs).ConfigureAwait(false);
        }
    }

    [Block("Closes an open puppeteer browser", name = "Close Browser")]
    public static async Task PuppeteerCloseBrowser(BotData data)
    {
        data.Logger.LogHeader();

        var browser = GetBrowser(data);

        try
        {
            await browser.CloseAsync().ConfigureAwait(false);
        }
        finally
        {
            CleanupBrowserSession(data);
        }

        data.Logger.Log("Browser closed successfully!", LogColors.DarkSalmon);
    }

    [Block("Opens a new page in a new browser tab", name = "New Tab")]
    public static async Task PuppeteerNewTab(BotData data)
    {
        data.Logger.LogHeader();

        var browser = GetBrowser(data);
        var page = await browser.NewPageAsync().ConfigureAwait(false);

        await PreparePageAsync(data, page, applyDefaultHeaders: false, authenticateProxy: false)
            .ConfigureAwait(false);

        SetPageAndFrame(data, page);
        data.Logger.Log("Opened a new page", LogColors.DarkSalmon);
    }

    [Block("Closes the currently active browser tab", name = "Close Tab")]
    public static async Task PuppeteerCloseTab(BotData data)
    {
        data.Logger.LogHeader();

        var browser = GetBrowser(data);
        var page = GetPage(data);

        await page.CloseAsync().ConfigureAwait(false);

        var nextPage = (await browser.PagesAsync().ConfigureAwait(false)).FirstOrDefault();
        SetPageAndFrame(data, nextPage);

        if (nextPage != null)
        {
            await nextPage.BringToFrontAsync().ConfigureAwait(false);
        }

        data.Logger.Log("Closed the active page", LogColors.DarkSalmon);
    }

    [Block("Switches to the browser tab with a specified index", name = "Switch to Tab")]
    public static async Task PuppeteerSwitchToTab(BotData data, int index)
    {
        data.Logger.LogHeader();

        var browser = GetBrowser(data);

        _ = await browser.GetVersionAsync().ConfigureAwait(false);

        IPage page = null;
        var pageList = data.PuppeteerSession.PageList;

        if (pageList != null)
        {
            string targetId = null;
            lock (pageList)
            {
                if (index >= 0 && index < pageList.Count)
                {
                    targetId = pageList[index];
                }
            }

            if (targetId != null)
            {
                var pages = await browser.PagesAsync().ConfigureAwait(false);
                page = pages.FirstOrDefault(p => p.Target.TargetId == targetId);
            }
        }

        if (page == null)
        {
            var pages = await browser.PagesAsync().ConfigureAwait(false);
            if (index >= 0 && index < pages.Length)
            {
                page = pages[index];
            }
        }

        if (page == null)
        {
            throw new Exception($"Could not find tab with index {index}");
        }

        await page.BringToFrontAsync().ConfigureAwait(false);
        SetPageAndFrame(data, page);

        data.Logger.Log($"Switched to tab with index {index}", LogColors.DarkSalmon);
    }

    [Block("Reloads the current page", name = "Reload")]
    public static async Task PuppeteerReload(BotData data)
    {
        data.Logger.LogHeader();

        var page = GetPage(data);
        _ = await page.ReloadAsync().ConfigureAwait(false);
        SwitchToMainFrame(data);

        data.Logger.Log("Reloaded the page", LogColors.DarkSalmon);
    }

    [Block("Goes back to the previously visited page", name = "Go Back")]
    public static async Task PuppeteerGoBack(BotData data)
    {
        data.Logger.LogHeader();

        var page = GetPage(data);
        _ = await page.GoBackAsync().ConfigureAwait(false);
        SwitchToMainFrame(data);

        data.Logger.Log("Went back to the previously visited page", LogColors.DarkSalmon);
    }

    [Block("Goes forward to the next visited page", name = "Go Forward")]
    public static async Task PuppeteerGoForward(BotData data)
    {
        data.Logger.LogHeader();

        var page = GetPage(data);
        _ = await page.GoForwardAsync().ConfigureAwait(false);
        SwitchToMainFrame(data);

        data.Logger.Log("Went forward to the next visited page", LogColors.DarkSalmon);
    }
}
