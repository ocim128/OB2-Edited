namespace RuriLib.Models.Settings
{
    public class GlobalSettings
    {
        public GlobalGeneralSettings GeneralSettings { get; set; } = new GlobalGeneralSettings();
        public CaptchaSettings CaptchaSettings { get; set; } = new CaptchaSettings();
        public GlobalProxySettings ProxySettings { get; set; } = new GlobalProxySettings();
        public PuppeteerSettings PuppeteerSettings { get; set; } = new PuppeteerSettings();
        public PlaywrightSettings PlaywrightSettings { get; set; } = new PlaywrightSettings();
        public SeleniumSettings SeleniumSettings { get; set; } = new SeleniumSettings();
    }
}
