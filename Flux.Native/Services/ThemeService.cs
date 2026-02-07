using MahApps.Metro.Controls;
using Flux.Core.Models.Settings;
using Flux.Core.Services;
using AppBrush = Flux.Native.Helpers.Brush;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Flux.Native.Services
{
    public class ThemeService : IThemeService
    {
        private readonly FluxSettingsService fluxSettingsService;
        private MetroWindow mainWindow;

        public ThemeService(FluxSettingsService fluxSettingsService)
        {
            this.fluxSettingsService = fluxSettingsService;
        }

        public void Initialize(MetroWindow window)
        {
            mainWindow = window;
        }

        public void SetTheme(CustomizationSettings customization)
        {
            if (mainWindow == null) return;

            AppBrush.SetAppColor("BackgroundMain", customization.BackgroundMain);
            AppBrush.SetAppColor("BackgroundSecondary", customization.BackgroundSecondary);
            AppBrush.SetAppColor("BackgroundInput", customization.BackgroundInput);
            AppBrush.SetAppColor("ForegroundMain", customization.ForegroundMain);
            AppBrush.SetAppColor("ForegroundInput", customization.ForegroundInput);
            AppBrush.SetAppColor("ForegroundGood", customization.ForegroundGood);
            AppBrush.SetAppColor("ForegroundBad", customization.ForegroundBad);
            AppBrush.SetAppColor("ForegroundCustom", customization.ForegroundCustom);
            AppBrush.SetAppColor("ForegroundRetry", customization.ForegroundRetry);
            AppBrush.SetAppColor("ForegroundBanned", customization.ForegroundBanned);
            AppBrush.SetAppColor("ForegroundToCheck", customization.ForegroundToCheck);
            AppBrush.SetAppColor("ForegroundMenuSelected", customization.ForegroundMenuSelected);
            AppBrush.SetAppColor("SuccessButton", customization.SuccessButton);
            AppBrush.SetAppColor("PrimaryButton", customization.PrimaryButton);
            AppBrush.SetAppColor("WarningButton", customization.WarningButton);
            AppBrush.SetAppColor("DangerButton", customization.DangerButton);
            AppBrush.SetAppColor("ForegroundButton", customization.ForegroundButton);
            AppBrush.SetAppColor("BackgroundButton", customization.BackgroundButton);

            // BACKGROUND
            mainWindow.Background = File.Exists(customization.BackgroundImagePath)
                ? new ImageBrush(new System.Windows.Media.Imaging.BitmapImage(new Uri(customization.BackgroundImagePath)))
                {
                    Opacity = customization.BackgroundOpacity / 100,
                    Stretch = Stretch.UniformToFill
                }
                : AppBrush.Get("BackgroundMain");
        }

        public void ApplyAccessibilitySettings()
        {
            if (mainWindow == null) return;

            var accessibility = fluxSettingsService.Settings.AccessibilitySettings ?? new AccessibilitySettings();
            
            // Ensure settings are initialized if null
            if (fluxSettingsService.Settings.AccessibilitySettings == null)
            {
                fluxSettingsService.Settings.AccessibilitySettings = accessibility;
            }

            // Apply UI Scale
            if (mainWindow.Content is FrameworkElement root)
            {
                root.LayoutTransform = accessibility.UiScale <= 0.1 || Math.Abs(accessibility.UiScale - 1.0) < 0.01
                    ? Transform.Identity
                    : new ScaleTransform(accessibility.UiScale, accessibility.UiScale);
            }

            // High Contrast
            if (accessibility.EnableHighContrast)
            {
                ApplyHighContrastPalette();
            }

            // Focus Visuals & Spacing & Tooltips
            // These require iterating over buttons in the window. 
            // Since we extracted this, we might need to look up controls or rely on styles/attached properties.
            // However, the original code had a list of buttons. 
            // For now, let's keep the globally applicable parts here.
            // The button-specific logic (ApplyButtonSpacing, ConfigureTooltips) is tightly coupled to MainWindow structure.
            // We can leave that in MainWindow or expose a method to pass the buttons.
            // A cleaner way is to use styles or a behavior, but to stick to the plan of extraction:
            
            // We will let MainWindow handle the specific control iteration for now, 
            // OR we can traverse the visual tree, but that's expensive.
            // Let's rely on MainWindow calling a helper method for the collection if needed, 
            // or just handle the global resources/transforms here.
        }

        private void ApplyHighContrastPalette()
        {
            Application.Current.Resources["Modern.BackgroundMain"] = AppBrush.FromHex("#000000");
            Application.Current.Resources["Modern.BackgroundSecondary"] = AppBrush.FromHex("#111111");
            Application.Current.Resources["Modern.BackgroundInput"] = AppBrush.FromHex("#141414");
            Application.Current.Resources["Modern.ForegroundMain"] = AppBrush.FromHex("#FFFFFF");
            Application.Current.Resources["Modern.ForegroundSecondary"] = AppBrush.FromHex("#F5F5F5");
            Application.Current.Resources["Modern.BorderFocus"] = AppBrush.FromHex("#FFFFFF");
            Application.Current.Resources["Modern.ThemeMain"] = AppBrush.FromHex("#FFD700");

            AppBrush.SetAppColor("BackgroundMain", "#000000");
            AppBrush.SetAppColor("BackgroundSecondary", "#111111");
            AppBrush.SetAppColor("ForegroundMain", "#FFFFFF");
            AppBrush.SetAppColor("ForegroundInput", "#FFFFFF");
            AppBrush.SetAppColor("ForegroundMenuSelected", "#FFD700");
            AppBrush.SetAppColor("BackgroundInput", "#141414");
        }
    }
}
