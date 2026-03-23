using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Playwright;
using PuppeteerSharp;
using RuriLib.Models.Settings;
using Yove.Proxy;
using PlaywrightBrowser = Microsoft.Playwright.IBrowser;
using PlaywrightContext = Microsoft.Playwright.IBrowserContext;
using PlaywrightFrame = Microsoft.Playwright.IFrame;
using PlaywrightPage = Microsoft.Playwright.IPage;
using PuppeteerBrowser = PuppeteerSharp.IBrowser;
using PuppeteerFrame = PuppeteerSharp.IFrame;
using PuppeteerPage = PuppeteerSharp.IPage;

namespace RuriLib.Models.Bots;

public interface IPlaywrightCleanupState
{
    void SuppressBrowserDisconnect();
    bool Cleanup(string? logMessage);
    void StartManualCloseWatcher(bool enabled);
    void StopManualCloseWatcher();
}

public sealed class PlaywrightSessionState
{
    public PlaywrightBrowser? Browser { get; set; }
    public PlaywrightContext? Context { get; set; }
    public PlaywrightPage? Page { get; set; }
    public PlaywrightFrame? Frame { get; set; }
    public IPlaywright? Instance { get; set; }
    public IPlaywrightCleanupState? CleanupState { get; set; }
    public PlaywrightBrowserType? BrowserType { get; set; }
    public bool? Headless { get; set; }
    public int? RealBrowserProcessId { get; set; }
    public int[]? FirefoxProcessIds { get; set; }
    public string? TempFirefoxProfile { get; set; }
    public string? TempChromiumUserData { get; set; }
    public string[]? TempArtifacts { get; set; }

    public void Clear()
    {
        Browser = null;
        Context = null;
        Page = null;
        Frame = null;
        Instance = null;
        CleanupState = null;
        BrowserType = null;
        Headless = null;
        RealBrowserProcessId = null;
        FirefoxProcessIds = null;
        TempFirefoxProfile = null;
        TempChromiumUserData = null;
        TempArtifacts = null;
    }
}

public sealed class PuppeteerSessionState
{
    public PuppeteerBrowser? Browser { get; set; }
    public PuppeteerPage? Page { get; set; }
    public PuppeteerFrame? Frame { get; set; }
    public List<string>? PageList { get; set; }
    public ProxyClient? YoveProxy { get; set; }
    public Process? RealBrowserProcess { get; set; }
    public int? RealBrowserProcessId { get; set; }

    public void DisposeTrackedResources()
    {
        YoveProxy?.Dispose();
        YoveProxy = null;

        if (RealBrowserProcess is not null)
        {
            try
            {
                if (!RealBrowserProcess.HasExited)
                {
                    RealBrowserProcess.Kill(true);
                }
            }
            catch
            {
            }
            finally
            {
                RealBrowserProcess.Dispose();
                RealBrowserProcess = null;
            }
        }

        RealBrowserProcessId = null;
    }

    public void Clear()
    {
        Browser = null;
        Page = null;
        Frame = null;
        PageList = null;
        RealBrowserProcessId = null;
    }
}
