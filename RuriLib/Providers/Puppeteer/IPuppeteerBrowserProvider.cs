namespace RuriLib.Providers.Puppeteer
{
    public interface IPuppeteerBrowserProvider
    {
        string ChromeBinaryLocation { get; }
        bool UseRealBrowser { get; }
    }
}
