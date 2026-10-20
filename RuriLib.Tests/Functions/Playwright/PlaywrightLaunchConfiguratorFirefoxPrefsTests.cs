using Microsoft.Playwright;
using RuriLib.Helpers.Playwright;
using RuriLib.Models.Settings;

namespace RuriLib.Tests.Functions.Playwright;

/// <summary>
/// The "New Page" block opens Firefox tabs with window.open(), falling back to a trusted click
/// on a target="_blank" anchor because Firefox only allows a scripted window.open() when the
/// document has transient user activation. These prefs support that, so pin them here.
/// </summary>
public class PlaywrightLaunchConfiguratorFirefoxPrefsTests
{
    [Fact]
    public void PersistentContext_DisablesPopupBlocker_SoTheTabClickIsNeverBlocked()
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

    [Fact]
    public void PersistentContext_KeepsMultiProcessEnabled_SoReusedProfilesRetainMouseInput()
    {
        var options = new BrowserTypeLaunchPersistentContextOptions();

        PlaywrightLaunchConfigurator.ApplyFirefoxSafeDefaults(options);

        // Firefox persists a non-default browser.tabs.remote.autostart=false into the profile's
        // prefs.js. From the next launch onwards every Playwright mouse action then hangs (click
        // and hover time out while keyboard still works), which breaks all click/fill blocks for
        // anyone reusing a profile created through the "Firefox profile path" field.
        Assert.Equal(true, GetPref(options.FirefoxUserPrefs!, "browser.tabs.remote.autostart"));
    }

    private static object? GetPref(IEnumerable<KeyValuePair<string, object>> prefs, string name)
        => prefs.Single(p => p.Key == name).Value;
}
