using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using Flux.Native.ViewModels.Base;
using Flux.Native.ViewModels.Settings.Metadata;
using RuriLib.Functions.Captchas;
using RuriLib.Models.Settings;
using RuriLib.Parallelization;
using RuriLib.Services;

namespace Flux.Native.ViewModels.Settings;

public class RLSettingsViewModel : ViewModelBase
{
    private readonly RuriLibSettingsService service;

    private GlobalGeneralSettings General => service.RuriLibSettings.GeneralSettings;
    private GlobalProxySettings Proxy => service.RuriLibSettings.ProxySettings;
    private CaptchaSettings Captcha => service.RuriLibSettings.CaptchaSettings;
    private PuppeteerSettings Puppeteer => service.RuriLibSettings.PuppeteerSettings;
    private PlaywrightSettings Playwright => service.RuriLibSettings.PlaywrightSettings;
    private SeleniumSettings Selenium => service.RuriLibSettings.SeleniumSettings;

    public RLSettingsViewModel(RuriLibSettingsService ruriLibSettingsService)
    {
        service = ruriLibSettingsService ?? throw new ArgumentNullException(nameof(ruriLibSettingsService));

        GeneralFields = BuildGeneralFields();
        ProxyFields = BuildProxyFields();
        CaptchaGeneralFields = BuildCaptchaGeneralFields();
        PuppeteerFields = BuildPuppeteerFields();
        PlaywrightFields = BuildPlaywrightFields();
        SeleniumFields = BuildSeleniumFields();
        BuildCaptchaServiceFields();
        RefreshAllFields();
    }

    public IReadOnlyList<MetadataFieldViewModel> GeneralFields { get; }
    public IReadOnlyList<MetadataFieldViewModel> ProxyFields { get; }
    public IReadOnlyList<MetadataFieldViewModel> CaptchaGeneralFields { get; }
    public IReadOnlyList<MetadataFieldViewModel> PuppeteerFields { get; }
    public IReadOnlyList<MetadataFieldViewModel> PlaywrightFields { get; }
    public IReadOnlyList<MetadataFieldViewModel> SeleniumFields { get; }

    private IReadOnlyList<MetadataFieldViewModel> captchaServiceFields = [];
    public IReadOnlyList<MetadataFieldViewModel> CaptchaServiceFields
    {
        get => captchaServiceFields;
        private set
        {
            captchaServiceFields = value;
            OnPropertyChanged();
        }
    }

    public Task<decimal> CheckCaptchaBalance() => CaptchaServiceFactory.GetService(Captcha).GetBalanceAsync();

    public Task Save() => service.Save();

    public async Task InstallPlaywrightBrowsers(Action<string> onLog)
    {
        var browsers = new[] { PlaywrightBrowserType.Chromium, PlaywrightBrowserType.Firefox };

        foreach (var browser in browsers)
        {
            onLog($"Installing {browser}...");
            await RuriLib.Providers.Playwright.PlaywrightRuntimeService.EnsureBrowserInstalledAsync(
                browser,
                null,
                onLog,
                true);
        }

        onLog("All browsers installed successfully!");
    }

    public void Reset()
    {
        service.RuriLibSettings = new GlobalSettings();
        BuildCaptchaServiceFields();
        RefreshAllFields();
    }

    private IReadOnlyList<MetadataFieldViewModel> BuildGeneralFields() =>
    [
        EnumField("Parallelizer Type", () => General.ParallelizerType, value => General.ParallelizerType = (ParallelizerType)value, Enum.GetValues(typeof(ParallelizerType))),
        BoolField("Log job activity to a file", () => General.LogJobActivityToFile, value => General.LogJobActivityToFile = value),
        BoolField(
            "Log all results to the job log file",
            () => General.LogAllResults,
            value => General.LogAllResults = value,
            "High disk usage. Only useful when file logging is enabled.",
            visibleWhen: () => General.LogJobActivityToFile),
        BoolField("Enable logging for bots in MultiRunJob", () => General.EnableBotLogging, value => General.EnableBotLogging = value, "Debugging only. High RAM usage."),
        BoolField("Enable verbose mode", () => General.VerboseMode, value => General.VerboseMode = value, "Adds more output and error context."),
        BoolField("Restrict blocks to current working directory", () => General.RestrictBlocksToCWD, value => General.RestrictBlocksToCWD = value),
        BoolField("Use custom user agents list", () => General.UseCustomUserAgentsList, value => General.UseCustomUserAgentsList = value),
        MultilineField(
            "Custom User Agents",
            () => JoinLines(General.UserAgents),
            value => General.UserAgents = SplitLines(value),
            "One entry per line.",
            visibleWhen: () => General.UseCustomUserAgentsList)
    ];

    private IReadOnlyList<MetadataFieldViewModel> BuildProxyFields() =>
    [
        IntField("Proxy connect timeout (ms)", () => Proxy.ProxyConnectTimeoutMilliseconds, value => Proxy.ProxyConnectTimeoutMilliseconds = value, 0, 100000000, 1000),
        IntField("Proxy read/write timeout (ms)", () => Proxy.ProxyReadWriteTimeoutMilliseconds, value => Proxy.ProxyReadWriteTimeoutMilliseconds = value, 0, 100000000, 1000),
        MultilineField("Global BAN keys", () => JoinLines(Proxy.GlobalBanKeys), value => Proxy.GlobalBanKeys = SplitLines(value), "One key per line."),
        MultilineField("Global RETRY keys", () => JoinLines(Proxy.GlobalRetryKeys), value => Proxy.GlobalRetryKeys = SplitLines(value), "One key per line.")
    ];

    private IReadOnlyList<MetadataFieldViewModel> BuildCaptchaGeneralFields() =>
    [
        IntField("Captcha timeout (seconds)", () => Captcha.TimeoutSeconds, value => Captcha.TimeoutSeconds = value, 1, 1000000, 10),
        IntField("Polling interval (ms)", () => Captcha.PollingIntervalMilliseconds, value => Captcha.PollingIntervalMilliseconds = value, 20, 100000000, 1000),
        BoolField("Check balance before solving", () => Captcha.CheckBalanceBeforeSolving, value => Captcha.CheckBalanceBeforeSolving = value),
        EnumField(
            "Captcha service",
            () => Captcha.CurrentService,
            value => Captcha.CurrentService = (CaptchaServiceType)value,
            Enum.GetValues(typeof(CaptchaServiceType)),
            afterChange: BuildCaptchaServiceFields)
    ];

    private IReadOnlyList<MetadataFieldViewModel> BuildPuppeteerFields() =>
    [
        TextField("Chrome binary path", () => Puppeteer.ChromeBinaryLocation, value => Puppeteer.ChromeBinaryLocation = value)
    ];

    private IReadOnlyList<MetadataFieldViewModel> BuildPlaywrightFields() =>
    [
        TextField("Chromium binary path", () => Playwright.ChromiumBinaryLocation, value => Playwright.ChromiumBinaryLocation = value),
        TextField("Firefox binary path", () => Playwright.FirefoxBinaryLocation, value => Playwright.FirefoxBinaryLocation = value),
        TextField("Webkit binary path", () => Playwright.WebkitBinaryLocation, value => Playwright.WebkitBinaryLocation = value),
        BoolField("Draw mouse movement", () => Playwright.DrawMouseMovement, value => Playwright.DrawMouseMovement = value)
    ];

    private IReadOnlyList<MetadataFieldViewModel> BuildSeleniumFields() =>
    [
        EnumField("Browser type", () => Selenium.BrowserType, value => Selenium.BrowserType = (SeleniumBrowserType)value, Enum.GetValues(typeof(SeleniumBrowserType))),
        TextField("Chrome binary path", () => Selenium.ChromeBinaryLocation, value => Selenium.ChromeBinaryLocation = value),
        TextField("Firefox binary path", () => Selenium.FirefoxBinaryLocation, value => Selenium.FirefoxBinaryLocation = value)
    ];

    private void BuildCaptchaServiceFields()
    {
        CaptchaServiceFields = Captcha.CurrentService switch
        {
            CaptchaServiceType.TwoCaptcha => [TextField("API key", () => Captcha.TwoCaptchaApiKey, value => Captcha.TwoCaptchaApiKey = value)],
            CaptchaServiceType.AntiCaptcha => [TextField("API key", () => Captcha.AntiCaptchaApiKey, value => Captcha.AntiCaptchaApiKey = value)],
            CaptchaServiceType.CustomTwoCaptcha =>
            [
                TextField("API key", () => Captcha.CustomTwoCaptchaApiKey, value => Captcha.CustomTwoCaptchaApiKey = value),
                TextField("Domain", () => Captcha.CustomTwoCaptchaDomain, value => Captcha.CustomTwoCaptchaDomain = value),
                IntField("Port", () => Captcha.CustomTwoCaptchaPort, value => Captcha.CustomTwoCaptchaPort = value, 1, 65535),
                BoolField("Override Host header with 2captcha.com", () => Captcha.CustomTwoCaptchaOverrideHostHeader, value => Captcha.CustomTwoCaptchaOverrideHostHeader = value)
            ],
            CaptchaServiceType.DeathByCaptcha =>
            [
                TextField("Username", () => Captcha.DeathByCaptchaUsername, value => Captcha.DeathByCaptchaUsername = value),
                TextField("Password", () => Captcha.DeathByCaptchaPassword, value => Captcha.DeathByCaptchaPassword = value)
            ],
            CaptchaServiceType.CaptchaCoder => [TextField("API key", () => Captcha.CaptchaCoderApiKey, value => Captcha.CaptchaCoderApiKey = value)],
            CaptchaServiceType.ImageTyperz => [TextField("API key", () => Captcha.ImageTyperzApiKey, value => Captcha.ImageTyperzApiKey = value)],
            CaptchaServiceType.CapMonster =>
            [
                new MetadataMessageFieldViewModel(
                    "This is only for the old CapMonster application. For CapMonster Cloud, use the CustomTwoCaptcha service and disable the Host header override.",
                    Brushes.Goldenrod),
                TextField("Host", () => Captcha.CapMonsterHost, value => Captcha.CapMonsterHost = value),
                IntField("Port", () => Captcha.CapMonsterPort, value => Captcha.CapMonsterPort = value, 1, 65535)
            ],
            CaptchaServiceType.AzCaptcha => [TextField("API key", () => Captcha.AZCaptchaApiKey, value => Captcha.AZCaptchaApiKey = value)],
            CaptchaServiceType.CaptchasIo => [TextField("API key", () => Captcha.CaptchasDotIoApiKey, value => Captcha.CaptchasDotIoApiKey = value)],
            CaptchaServiceType.RuCaptcha => [TextField("API key", () => Captcha.RuCaptchaApiKey, value => Captcha.RuCaptchaApiKey = value)],
            CaptchaServiceType.SolveCaptcha => [TextField("API key", () => Captcha.SolveCaptchaApiKey, value => Captcha.SolveCaptchaApiKey = value)],
            CaptchaServiceType.TrueCaptcha =>
            [
                TextField("API key", () => Captcha.TrueCaptchaApiKey, value => Captcha.TrueCaptchaApiKey = value),
                TextField("Username", () => Captcha.TrueCaptchaUsername, value => Captcha.TrueCaptchaUsername = value)
            ],
            CaptchaServiceType.NineKw => [TextField("API key", () => Captcha.NineKWApiKey, value => Captcha.NineKWApiKey = value)],
            CaptchaServiceType.CustomAntiCaptcha =>
            [
                TextField("API key", () => Captcha.CustomAntiCaptchaApiKey, value => Captcha.CustomAntiCaptchaApiKey = value),
                TextField("Domain", () => Captcha.CustomAntiCaptchaDomain, value => Captcha.CustomAntiCaptchaDomain = value),
                IntField("Port", () => Captcha.CustomAntiCaptchaPort, value => Captcha.CustomAntiCaptchaPort = value, 1, 65535)
            ],
            CaptchaServiceType.CapSolver => [new MetadataMessageFieldViewModel("CapSolver explicitly asked to be removed from the software. Please choose another service.", Brushes.IndianRed)],
            CaptchaServiceType.CapMonsterCloud => [TextField("API key", () => Captcha.CapMonsterCloudApiKey, value => Captcha.CapMonsterCloudApiKey = value)],
            CaptchaServiceType.HumanCoder => [TextField("API key", () => Captcha.HumanCoderApiKey, value => Captcha.HumanCoderApiKey = value)],
            CaptchaServiceType.Nopecha => [TextField("API key", () => Captcha.NopechaApiKey, value => Captcha.NopechaApiKey = value)],
            CaptchaServiceType.NoCaptchaAi => [TextField("API key", () => Captcha.NoCaptchaAiApiKey, value => Captcha.NoCaptchaAiApiKey = value)],
            CaptchaServiceType.MetaBypassTech =>
            [
                TextField("Client ID", () => Captcha.MetaBypassTechClientId, value => Captcha.MetaBypassTechClientId = value),
                TextField("Client secret", () => Captcha.MetaBypassTechClientSecret, value => Captcha.MetaBypassTechClientSecret = value),
                TextField("Username", () => Captcha.MetaBypassTechUsername, value => Captcha.MetaBypassTechUsername = value),
                TextField("Password", () => Captcha.MetaBypassTechPassword, value => Captcha.MetaBypassTechPassword = value)
            ],
            CaptchaServiceType.CaptchaAi => [TextField("API key", () => Captcha.CaptchaAiApiKey, value => Captcha.CaptchaAiApiKey = value)],
            CaptchaServiceType.NextCaptcha => [TextField("API key", () => Captcha.NextCaptchaApiKey, value => Captcha.NextCaptchaApiKey = value)],
            CaptchaServiceType.EzCaptcha => [TextField("API key", () => Captcha.EzCaptchaApiKey, value => Captcha.EzCaptchaApiKey = value)],
            CaptchaServiceType.EndCaptcha =>
            [
                TextField("Username", () => Captcha.EndCaptchaUsername, value => Captcha.EndCaptchaUsername = value),
                TextField("Password", () => Captcha.EndCaptchaPassword, value => Captcha.EndCaptchaPassword = value)
            ],
            CaptchaServiceType.BestCaptchaSolver => [TextField("API key", () => Captcha.BestCaptchaSolverApiKey, value => Captcha.BestCaptchaSolverApiKey = value)],
            CaptchaServiceType.CapGuru => [TextField("API key", () => Captcha.CapGuruApiKey, value => Captcha.CapGuruApiKey = value)],
            CaptchaServiceType.Aycd => [TextField("API key", () => Captcha.AycdApiKey, value => Captcha.AycdApiKey = value)],
            _ => []
        };

        foreach (var field in CaptchaServiceFields)
        {
            field.Refresh();
        }
    }

    private void RefreshAllFields()
    {
        foreach (var field in EnumerateFields())
        {
            field.Refresh();
        }
    }

    private IEnumerable<MetadataFieldViewModel> EnumerateFields() =>
        GeneralFields
            .Concat(ProxyFields)
            .Concat(CaptchaGeneralFields)
            .Concat(CaptchaServiceFields)
            .Concat(PuppeteerFields)
            .Concat(PlaywrightFields)
            .Concat(SeleniumFields);

    private MetadataBooleanFieldViewModel BoolField(
        string label,
        Func<bool> getter,
        Action<bool> setter,
        string? description = null,
        Func<bool>? visibleWhen = null,
        Action? afterChange = null) =>
        new(label, getter, setter, description, RefreshAllFields, visibleWhen, afterChange);

    private MetadataTextFieldViewModel TextField(
        string label,
        Func<string> getter,
        Action<string> setter,
        string? description = null,
        Func<bool>? visibleWhen = null,
        Action? afterChange = null) =>
        new(label, getter, setter, description, RefreshAllFields, visibleWhen, afterChange);

    private MetadataMultilineTextFieldViewModel MultilineField(
        string label,
        Func<string> getter,
        Action<string> setter,
        string? description = null,
        Func<bool>? visibleWhen = null,
        Action? afterChange = null) =>
        new(label, getter, setter, description, RefreshAllFields, visibleWhen, afterChange);

    private MetadataIntegerFieldViewModel IntField(
        string label,
        Func<int> getter,
        Action<int> setter,
        int minimum = 0,
        int maximum = int.MaxValue,
        int interval = 1,
        string? description = null,
        Func<bool>? visibleWhen = null,
        Action? afterChange = null) =>
        new(label, getter, setter, minimum, maximum, interval, description, RefreshAllFields, visibleWhen, afterChange);

    private MetadataEnumFieldViewModel EnumField(
        string label,
        Func<object> getter,
        Action<object> setter,
        Array options,
        string? description = null,
        Func<bool>? visibleWhen = null,
        Action? afterChange = null) =>
        new(label, getter, setter, options, description, RefreshAllFields, visibleWhen, afterChange);

    private static string JoinLines(IEnumerable<string>? values) =>
        values is null ? string.Empty : string.Join(Environment.NewLine, values);

    private static List<string> SplitLines(string value) =>
        value.Split([Environment.NewLine], StringSplitOptions.None).ToList();
}
