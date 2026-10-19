using Microsoft.Playwright;
using RuriLib.Helpers.Playwright;
using RuriLib.Models.Settings;

namespace RuriLib.Tests.Functions.Playwright;

/// <summary>
/// The "New Page" block opens Firefox tabs through window.open(), which Firefox refuses
/// without a user gesture unless the popup blocker is disabled. These tests pin the launch
/// prefs that make that tab open natively instead of falling back to a separate window.
/// </summary>
public class PlaywrightLaunchConfiguratorFirefoxPrefsTests
{
    [Fact]
    public void PersistentContext_DisablesPopupBlocker_SoWindowOpenCreatesTheTab()
    {
        var options = new BrowserTypeLaunchPersistentContextOptions();

        PlaywrightLaunchConfigurator.ApplyFirefoxSafeDefaults(options);

        Assert.Equal(false, GetPref(options.FirefoxUserPrefs!, "dom.disable_open_during_load"));
    }

    [Fact]
    public void PersistentContext_RoutesPopupsToATab_NotANewWindow()
    {
        var options = new BrowserTypeLaunchPersistentContextOptions();

        PlaywrightLaunchConfigurator.ApplyFirefoxSafeDefaults(options);

        // 3 == open in a new tab; 0 == no restriction that would force a window.
        Assert.Equal(3, GetPref(options.FirefoxUserPrefs!, "browser.link.open_newwindow"));
        Assert.Equal(0, GetPref(options.FirefoxUserPrefs!, "browser.link.open_newwindow.restriction"));
    }

    [Fact]
    public void RegularLaunch_GetsTheSamePopupPrefs()
    {
        var options = new BrowserTypeLaunchOptions();

        PlaywrightLaunchConfigurator.ApplyFirefoxSafeDefaults(options);

        Assert.Equal(false, GetPref(options.FirefoxUserPrefs!, "dom.disable_open_during_load"));
    }

    [Fact]
    public void CallerPrefs_ArePreserved_AndPopupBlockingIsStillDisabled()
    {
        var options = new BrowserTypeLaunchPersistentContextOptions
        {
            FirefoxUserPrefs = new Dictionary<string, object>
            {
                ["dom.disable_open_during_load"] = true,
                ["custom.pref"] = "keep-me"
            }
        };

        PlaywrightLaunchConfigurator.ApplyFirefoxSafeDefaults(options);

        Assert.Equal("keep-me", GetPref(options.FirefoxUserPrefs!, "custom.pref"));
        Assert.Equal(false, GetPref(options.FirefoxUserPrefs!, "dom.disable_open_during_load"));
    }

    private static object? GetPref(IEnumerable<KeyValuePair<string, object>> prefs, string name)
        => prefs.Single(p => p.Key == name).Value;
}
