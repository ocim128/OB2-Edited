using RuriLib.Models.Settings;
using RuriLib.Services;

namespace RuriLib.Providers.Playwright
{
    public class DefaultPlaywrightBrowserProvider : IPlaywrightBrowserProvider
    {
        public PlaywrightBrowserType BrowserType { get; }
        public string ChromiumBinaryLocation { get; }
        public string FirefoxBinaryLocation { get; }
        public string WebkitBinaryLocation { get; }
        public bool Headless { get; }
        public bool DrawMouseMovement { get; }
        public int TimeoutMilliseconds { get; }
        public bool IgnoreHTTPSErrors { get; }
        public string[] ExtraArgs { get; }

        public DefaultPlaywrightBrowserProvider(RuriLibSettingsService settings)
        {
            var playwrightSettings = settings.RuriLibSettings.PlaywrightSettings;
            BrowserType = playwrightSettings.BrowserType;
            ChromiumBinaryLocation = playwrightSettings.ChromiumBinaryLocation;
            FirefoxBinaryLocation = playwrightSettings.FirefoxBinaryLocation;
            WebkitBinaryLocation = playwrightSettings.WebkitBinaryLocation;
            Headless = playwrightSettings.Headless;
            DrawMouseMovement = playwrightSettings.DrawMouseMovement;
            TimeoutMilliseconds = playwrightSettings.TimeoutMilliseconds;
            IgnoreHTTPSErrors = playwrightSettings.IgnoreHTTPSErrors;
            ExtraArgs = playwrightSettings.ExtraArgs;
        }
    }
}