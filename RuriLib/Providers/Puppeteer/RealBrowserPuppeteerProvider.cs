using RuriLib.Services;

namespace RuriLib.Providers.Puppeteer
{
    public class RealBrowserPuppeteerProvider : IPuppeteerBrowserProvider
    {
        public string ChromeBinaryLocation { get; }
        public bool UseRealBrowser { get; }

        public RealBrowserPuppeteerProvider(RuriLibSettingsService settings)
        {
            ChromeBinaryLocation = settings.RuriLibSettings.PuppeteerSettings.ChromeBinaryLocation;
            UseRealBrowser = true; // Always use real browser for enhanced bot detection avoidance
        }
    }
} 