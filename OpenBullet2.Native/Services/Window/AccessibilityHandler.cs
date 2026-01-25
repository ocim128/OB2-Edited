using OpenBullet2.Core.Models.Settings;
using OpenBullet2.Core.Services;
using OpenBullet2.Native.Services;
using System.Windows.Controls;
using System.Windows;
using System.Linq;

namespace OpenBullet2.Native.Services.Window;

public class AccessibilityHandler
{
    private readonly IThemeService _themeService;
    private readonly OpenBulletSettingsService _settingsService;

    public AccessibilityHandler(
        IThemeService themeService,
        OpenBulletSettingsService settingsService)
    {
        _themeService = themeService;
        _settingsService = settingsService;
    }

    public void ApplyAccessibilitySettings(MainWindow window, Button[] navigationButtons, Button[] submenuButtons, FrameworkElement configSubmenu)
    {
        var accessibility = _settingsService.Settings.AccessibilitySettings ?? new AccessibilitySettings();
        if (_settingsService.Settings.AccessibilitySettings == null)
        {
            _settingsService.Settings.AccessibilitySettings = accessibility;
        }

        _themeService.ApplyAccessibilitySettings();

        var focusStyle = accessibility.AlwaysShowFocusVisuals
            ? window.TryFindResource("HighVisibilityFocusStyle") as Style
            : null;

        foreach (var button in navigationButtons?.Where(static b => b != null) ?? Enumerable.Empty<Button>())
        {
            button.FocusVisualStyle = focusStyle;
            ApplyButtonSpacing(button, accessibility.UseComfortableSpacing);
            ConfigureTooltips(button, accessibility.ShowHelpfulTooltips);
        }

        foreach (var button in submenuButtons?.Where(static b => b != null) ?? Enumerable.Empty<Button>())
        {
            button.FocusVisualStyle = focusStyle;
            ApplyButtonSpacing(button, accessibility.UseComfortableSpacing);
            ConfigureTooltips(button, accessibility.ShowHelpfulTooltips);
        }

        if (configSubmenu != null)
        {
            ConfigureTooltips(configSubmenu, accessibility.ShowHelpfulTooltips);
        }
    }

    private static void ApplyButtonSpacing(Button button, bool comfortable)
    {
        if (button == null) return;
        button.Padding = comfortable ? new Thickness(14, 10, 14, 10) : new Thickness(8, 6, 8, 6);
        button.Margin = comfortable ? new Thickness(4, 0, 4, 0) : new Thickness(2, 0, 2, 0);
    }

    private static void ConfigureTooltips(DependencyObject target, bool helpful)
    {
        if (target == null) return;

        if (helpful)
        {
            ToolTipService.SetInitialShowDelay(target, 150);
            ToolTipService.SetShowDuration(target, 12000);
            ToolTipService.SetBetweenShowDelay(target, 300);
        }
        else
        {
            ToolTipService.SetInitialShowDelay(target, 400);
            ToolTipService.SetShowDuration(target, 4000);
        }
    }
}
