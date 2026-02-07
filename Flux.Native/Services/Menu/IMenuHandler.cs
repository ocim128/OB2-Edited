using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Flux.Native.Enums;
using Flux.Native.Services.Navigation;

namespace Flux.Native.Services.Menu;

public interface IMenuHandler
{
    void Initialize(FrameworkElement configSubmenu, FrameworkElement configsChevron);
    void UpdateMenuHighlight(MainWindowPage page);
    void InitializePageButtonMap(Button[] navigationButtons);
    Button GetButtonForPage(MainWindowPage page);
    void HandleConfigSubmenu(bool show);
    void CloseSubmenu();
    void UpdateButtonHighlight(Button previous, Button current);
    void MapButton(MainWindowPage page, Button button);
    Task OnConfigSubmenuMouseEnterAsync();
    Task OnConfigSubmenuMouseLeaveAsync();
    Task OnConfigsMenuOptionMouseEnterAsync();
    Task OnConfigsMenuOptionMouseLeaveAsync();
}
