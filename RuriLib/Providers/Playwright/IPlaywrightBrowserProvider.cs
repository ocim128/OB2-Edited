using RuriLib.Models.Settings;

namespace RuriLib.Providers.Playwright
{
    public interface IPlaywrightBrowserProvider
    {
        PlaywrightBrowserType BrowserType { get; }
        string ChromiumBinaryLocation { get; }
        string FirefoxBinaryLocation { get; }
        string WebkitBinaryLocation { get; }
        bool Headless { get; }
        bool DrawMouseMovement { get; }
        int TimeoutMilliseconds { get; }
        bool IgnoreHTTPSErrors { get; }
        string[] ExtraArgs { get; }
    }
}