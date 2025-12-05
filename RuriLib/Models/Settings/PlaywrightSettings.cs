namespace RuriLib.Models.Settings;

public class PlaywrightSettings
{
    public PlaywrightBrowserType BrowserType { get; set; } = PlaywrightBrowserType.Chromium;
    public string ChromiumBinaryLocation { get; set; } = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    public string FirefoxBinaryLocation { get; set; } = @"C:\Program Files\Mozilla Firefox\firefox.exe";
    public string WebkitBinaryLocation { get; set; } = string.Empty;
    public bool Headless { get; set; } = false;
    public bool DrawMouseMovement { get; set; } = true;
    public int TimeoutMilliseconds { get; set; } = 30000;
    public bool IgnoreHTTPSErrors { get; set; } = true;
    public string[] ExtraArgs { get; set; } = new[] { "--no-sandbox" };
}

public enum PlaywrightBrowserType
{
    Chromium = 0,
    Firefox = 1,
    Webkit = 2
}
