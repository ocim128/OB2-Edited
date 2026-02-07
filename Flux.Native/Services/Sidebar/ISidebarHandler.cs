using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Flux.Native.Services.Sidebar;

public interface ISidebarHandler
{
    bool IsCollapsed { get; }
    void Initialize(
        ColumnDefinition sidebarColumn,
        RotateTransform toggleIconRotation,
        FrameworkElement[] textElements,
        FrameworkElement[] sectionHeaders,
        FrameworkElement sidebarHeader,
        FrameworkElement versionText,
        FrameworkElement bottomSeparator,
        FrameworkElement configSubmenu,
        FrameworkElement configsChevron);
    void Toggle();
    void SetCollapsed(bool collapsed);
    void InitializeCollapsedState();
    event System.EventHandler<bool> SidebarStateChanged;
}
