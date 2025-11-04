using RuriLib.Services;

namespace RuriLib.Providers.Puppeteer
{
    public class DefaultPuppeteerBrowserProvider : IPuppeteerBrowserProvider
    {
        public string ChromeBinaryLocation { get; }
        public bool UseRealBrowser { get; }

        public DefaultPuppeteerBrowserProvider(RuriLibSettingsService settings)
        {
            ChromeBinaryLocation = settings.RuriLibSettings.PuppeteerSettings.ChromeBinaryLocation;
            UseRealBrowser = settings.RuriLibSettings.PuppeteerSettings.UseRealBrowser;
        }
    }
}
