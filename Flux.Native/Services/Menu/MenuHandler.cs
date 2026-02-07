using System;
using Flux.Native.Enums;
using Flux.Native.Services.Navigation;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;
using System.Linq;

namespace Flux.Native.Services.Menu;

public class MenuHandler : IMenuHandler
{
    private readonly PageButtonMapper _buttonMapper;
    private readonly SubmenuController _submenuController;
    private Button _currentSelectedButton;
    private bool _hoveringConfigSubmenu;
    private bool _hoveringConfigsMenuOption;

    public MenuHandler(PageButtonMapper buttonMapper, SubmenuController submenuController)
    {
        _buttonMapper = buttonMapper;
        _submenuController = submenuController;
    }

    public void Initialize(FrameworkElement configSubmenu, FrameworkElement configsChevron)
    {
        _submenuController.Initialize(configSubmenu, configsChevron);
    }

    public void MapButton(MainWindowPage page, Button button)
    {
        _buttonMapper.MapButton(page, button);
    }

    public Button GetButtonForPage(MainWindowPage page)
    {
        return _buttonMapper.GetButtonForPage(page);
    }

    public void UpdateMenuHighlight(MainWindowPage page)
    {
        var button = _buttonMapper.GetButtonForPage(page);

        if (button == _currentSelectedButton)
            return;

        // Revert previous button to normal style
        if (_currentSelectedButton != null)
        {
            _currentSelectedButton.Style = _currentSelectedButton.Name.StartsWith("menuOptionConfig")
                ? Application.Current.FindResource("SidebarSubmenuButton") as Style
                : Application.Current.FindResource("SidebarNavButton") as Style;
        }

        // Apply active style to new button
        if (button != null)
        {
            button.Style = Application.Current.FindResource("SidebarNavButtonActive") as Style;
            _currentSelectedButton = button;
        }
    }

    public void InitializePageButtonMap(Button[] navigationButtons)
    {
        // Handled by MainWindow explicitly mapping for now
    }

    public void HandleConfigSubmenu(bool show)
    {
        if (show) _submenuController.Open();
        else _submenuController.Close();
    }

    public void CloseSubmenu()
    {
        _submenuController.Close();
    }

    public void UpdateButtonHighlight(Button previous, Button current)
    {
        if (previous != null)
        {
            previous.Style = previous.Name.StartsWith("menuOptionConfig")
                ? Application.Current.FindResource("SidebarSubmenuButton") as Style
                : Application.Current.FindResource("SidebarNavButton") as Style;
        }

        if (current != null)
        {
            current.Style = Application.Current.FindResource("SidebarNavButtonActive") as Style;
        }
        _currentSelectedButton = current;
    }

    public async Task OnConfigSubmenuMouseEnterAsync()
    {
        _hoveringConfigSubmenu = true;
        HandleConfigSubmenu(true);
    }

    public async Task OnConfigSubmenuMouseLeaveAsync()
    {
        _hoveringConfigSubmenu = false;
        await CheckCloseSubmenuAsync();
    }

    public async Task OnConfigsMenuOptionMouseEnterAsync()
    {
        _hoveringConfigsMenuOption = true;
        HandleConfigSubmenu(true);
    }

    public async Task OnConfigsMenuOptionMouseLeaveAsync()
    {
        _hoveringConfigsMenuOption = false;
        await CheckCloseSubmenuAsync();
    }

    private async Task CheckCloseSubmenuAsync()
    {
        await Task.Delay(200);
        if (!_hoveringConfigSubmenu && !_hoveringConfigsMenuOption)
        {
            HandleConfigSubmenu(false);
        }
    }
}
