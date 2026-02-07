using Flux.Native.Enums;
using System.Collections.Generic;
using System.Windows.Controls;

namespace Flux.Native.Services.Navigation;

public class PageButtonMapper
{
    private readonly Dictionary<MainWindowPage, Button> _pageButtonMap = new();

    public void MapButton(MainWindowPage page, Button button)
    {
        if (button != null)
        {
            _pageButtonMap[page] = button;
            if (button.Tag == null)
            {
                button.Tag = page;
            }
        }
    }

    public Button GetButtonForPage(MainWindowPage page)
    {
        return _pageButtonMap.TryGetValue(page, out var button) ? button : null;
    }

    public void InitializeStandardButtons(Button[] buttons)
    {
        if (buttons == null || buttons.Length < 12) return;

        MapButton(MainWindowPage.Home, buttons[0]);
        MapButton(MainWindowPage.Jobs, buttons[1]);
        MapButton(MainWindowPage.Tools, buttons[2]);
        MapButton(MainWindowPage.Proxies, buttons[3]);
        MapButton(MainWindowPage.Wordlists, buttons[4]);
        MapButton(MainWindowPage.Configs, buttons[5]);
        MapButton(MainWindowPage.Hits, buttons[6]);
        MapButton(MainWindowPage.Plugins, buttons[7]);
        MapButton(MainWindowPage.OBSettings, buttons[8]);
        MapButton(MainWindowPage.RLSettings, buttons[9]);
        MapButton(MainWindowPage.CheckUpdate, buttons[10]);
        MapButton(MainWindowPage.About, buttons[11]);
    }

    public void InitializeConfigSubmenuButtons(Button[] buttons)
    {
        if (buttons == null || buttons.Length < 6) return;

        MapButton(MainWindowPage.ConfigMetadata, buttons[0]);
        MapButton(MainWindowPage.ConfigReadme, buttons[1]);
        MapButton(MainWindowPage.ConfigStacker, buttons[2]);
        MapButton(MainWindowPage.ConfigLoliCode, buttons[3]);
        MapButton(MainWindowPage.ConfigSettings, buttons[4]);
        MapButton(MainWindowPage.ConfigCSharpCode, buttons[5]);
    }
}
