using RuriLib.Models.Bots;
using System;

namespace RuriLib.Blocks.Puppeteer.Browser;

public static partial class Methods
{
    private static void CleanupBrowserSession(BotData data)
    {
        StopYoveProxyInternalServer(data);
        DisposeTrackedRealBrowserProcess(data);
        data.PuppeteerSession.Clear();
    }

    private static void StopYoveProxyInternalServer(BotData data)
    {
        if (data.PuppeteerSession.YoveProxy is not { } proxyClient)
        {
            return;
        }

        proxyClient.Dispose();
        data.PuppeteerSession.YoveProxy = null;
    }

    private static void DisposeTrackedRealBrowserProcess(BotData data)
    {
        if (data.PuppeteerSession.RealBrowserProcess is not { } storedProcess)
        {
            data.PuppeteerSession.RealBrowserProcess = null;
            data.PuppeteerSession.RealBrowserProcessId = null;
            return;
        }

        try
        {
            if (!storedProcess.HasExited)
            {
                storedProcess.Kill(true);
            }
        }
        catch
        {
        }
        finally
        {
            storedProcess.Dispose();
            data.PuppeteerSession.RealBrowserProcess = null;
            data.PuppeteerSession.RealBrowserProcessId = null;
        }
    }
}
