using MahApps.Metro.Controls;
using ControlzEx.Theming;
using Flux.Core.Models.Settings;
using Flux.Core.Services;
using AppBrush = Flux.Native.Helpers.Brush;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Flux.Native.Services
{
    public class ThemeService : IThemeService
    {
        private static readonly IReadOnlyDictionary<string, string> LightModernPalette = new Dictionary<string, string>
        {
            ["Modern.ThemeMain"] = "#2563EB",
            ["Modern.ThemeAccent"] = "#8B5CF6",
            ["Modern.ThemeSecondary"] = "#1D4ED8",
            ["Modern.BackgroundMain"] = "#F8FAFC",
            ["Modern.BackgroundSecondary"] = "#FFFFFF",
            ["Modern.BackgroundTertiary"] = "#E2E8F0",
            ["Modern.BackgroundInput"] = "#FFFFFF",
            ["Modern.BackgroundCard"] = "#FFFFFF",
            ["Modern.BackgroundElevated"] = "#E2E8F0",
            ["Modern.BackgroundHover"] = "#E2E8F0",
            ["Modern.ForegroundMain"] = "#0F172A",
            ["Modern.ForegroundSecondary"] = "#334155",
            ["Modern.ForegroundMuted"] = "#64748B",
            ["Modern.ForegroundInput"] = "#0F172A",
            ["Modern.TextSecondary"] = "#64748B",
            ["Modern.TextOnAccent"] = "#FFFFFF",
            ["Modern.BorderMain"] = "#CBD5E1",
            ["Modern.BorderSecondary"] = "#E2E8F0",
            ["Modern.BorderInput"] = "#CBD5E1",
            ["Modern.BorderFocus"] = "#2563EB",
            ["Modern.BorderHover"] = "#94A3B8",
            ["Modern.BackgroundDark"] = "#F1F5F9",
            ["Modern.BackgroundDarkSecondary"] = "#E2E8F0",
            ["Modern.BackgroundDarkTertiary"] = "#CBD5E1",
            ["Modern.StatusRunning"] = "#3B82F6",
            ["Modern.StatusPaused"] = "#F59E0B",
            ["Modern.StatusStopped"] = "#6B7280",
            ["Modern.StatusCompleted"] = "#10B981",
            ["Modern.ForegroundLight"] = "#0F172A"
        };

        private static readonly IReadOnlyDictionary<string, string> DarkModernPalette = new Dictionary<string, string>
        {
            ["Modern.ThemeMain"] = "#3B82F6",
            ["Modern.ThemeAccent"] = "#8B5CF6",
            ["Modern.ThemeSecondary"] = "#2563EB",
            ["Modern.BackgroundMain"] = "#0F172A",
            ["Modern.BackgroundSecondary"] = "#1E293B",
            ["Modern.BackgroundTertiary"] = "#334155",
            ["Modern.BackgroundInput"] = "#1E293B",
            ["Modern.BackgroundCard"] = "#1E293B",
            ["Modern.BackgroundElevated"] = "#374151",
            ["Modern.BackgroundHover"] = "#374151",
            ["Modern.ForegroundMain"] = "#F8FAFC",
            ["Modern.ForegroundSecondary"] = "#CBD5E1",
            ["Modern.ForegroundMuted"] = "#94A3B8",
            ["Modern.ForegroundInput"] = "#F8FAFC",
            ["Modern.TextSecondary"] = "#94A3B8",
            ["Modern.TextOnAccent"] = "#FFFFFF",
            ["Modern.BorderMain"] = "#475569",
            ["Modern.BorderSecondary"] = "#334155",
            ["Modern.BorderInput"] = "#475569",
            ["Modern.BorderFocus"] = "#3B82F6",
            ["Modern.BorderHover"] = "#64748B",
            ["Modern.BackgroundDark"] = "#0C0C0C",
            ["Modern.BackgroundDarkSecondary"] = "#1A1A1A",
            ["Modern.BackgroundDarkTertiary"] = "#252525",
            ["Modern.StatusRunning"] = "#3B82F6",
            ["Modern.StatusPaused"] = "#F59E0B",
            ["Modern.StatusStopped"] = "#6B7280",
            ["Modern.StatusCompleted"] = "#10B981",
            ["Modern.ForegroundLight"] = "#F0F4F8"
        };

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
            if (customization == null) return;

            if (mainWindow == null && Application.Current?.MainWindow is MetroWindow metroWindow)
            {
                mainWindow = metroWindow;
            }

            var normalizedThemeMode = NormalizeThemeMode(customization);
            NormalizeLegacyCustomizationPalette(customization, normalizedThemeMode);
            ApplyMahAppsBaseTheme(normalizedThemeMode);
            ApplyModernPalette(normalizedThemeMode);

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
            ApplyModernCustomizationOverrides(customization);
            ApplyReadableToolTipStyle();

            // BACKGROUND
            if (mainWindow != null)
            {
                mainWindow.SetResourceReference(MetroWindow.OverrideDefaultWindowCommandsBrushProperty, "Modern.ForegroundMain");

                mainWindow.Background = File.Exists(customization.BackgroundImagePath)
                    ? new ImageBrush(new System.Windows.Media.Imaging.BitmapImage(new Uri(customization.BackgroundImagePath)))
                    {
                        Opacity = customization.BackgroundOpacity / 100,
                        Stretch = Stretch.UniformToFill
                    }
                    : AppBrush.Get("BackgroundMain");
            }
        }

        private void ApplyReadableToolTipStyle()
        {
            var borderBrush = Application.Current.TryFindResource("Modern.BorderMain") as Brush
                ?? new SolidColorBrush(Color.FromRgb(203, 213, 225));

            // Use theme-aware background instead of hardcoded dark color
            var bgBrush = Application.Current.TryFindResource("Modern.BackgroundElevated") as Brush
                ?? new SolidColorBrush(Color.FromArgb(242, 31, 41, 55));

            // Use theme-aware foreground - white for dark mode, dark for light mode
            var fgBrush = Application.Current.TryFindResource("Modern.TextOnAccent") as Brush
                ?? Brushes.White;

            var tooltipStyle = new Style(typeof(ToolTip));
            tooltipStyle.Setters.Add(new Setter(Control.BackgroundProperty, bgBrush));
            tooltipStyle.Setters.Add(new Setter(Control.BorderBrushProperty, borderBrush));
            tooltipStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            tooltipStyle.Setters.Add(new Setter(Control.ForegroundProperty, fgBrush));
            tooltipStyle.Setters.Add(new Setter(TextElement.ForegroundProperty, fgBrush));
            tooltipStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 8, 12, 8)));
            tooltipStyle.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Segoe UI")));
            tooltipStyle.Setters.Add(new Setter(Control.FontSizeProperty, 13d));
            tooltipStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Medium));
            tooltipStyle.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 300d));
            tooltipStyle.Setters.Add(new Setter(FrameworkElement.UseLayoutRoundingProperty, true));
            tooltipStyle.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));

            Application.Current.Resources[typeof(ToolTip)] = tooltipStyle;
            Application.Current.Resources["MahApps.Styles.ToolTip"] = tooltipStyle;

            if (mainWindow != null)
            {
                mainWindow.Resources[typeof(ToolTip)] = tooltipStyle;
                mainWindow.Resources["MahApps.Styles.ToolTip"] = tooltipStyle;
            }
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

        private static string NormalizeThemeMode(CustomizationSettings customization)
        {
            if (string.Equals(customization.NativeThemeMode, "Dark", StringComparison.OrdinalIgnoreCase))
            {
                customization.NativeThemeMode = "Dark";
                return customization.NativeThemeMode;
            }

            customization.NativeThemeMode = "Light";
            return customization.NativeThemeMode;
        }

        private static void NormalizeLegacyCustomizationPalette(CustomizationSettings customization, string themeMode)
        {
            if (string.Equals(themeMode, "Light", StringComparison.OrdinalIgnoreCase)
                && (IsLegacyDarkPalette(customization) || IsCurrentDarkPalette(customization)))
            {
                ApplyLightPreset(customization);
                return;
            }

            if (string.Equals(themeMode, "Dark", StringComparison.OrdinalIgnoreCase) && IsCurrentLightPalette(customization))
            {
                ApplyDarkPreset(customization);
            }
        }

        private static bool IsLegacyDarkPalette(CustomizationSettings customization)
            => EqualsHex(customization.BackgroundMain, "#222")
               && EqualsHex(customization.BackgroundInput, "#282828")
               && EqualsHex(customization.BackgroundSecondary, "#111")
               && EqualsHex(customization.ForegroundMain, "#DCDCDC")
               && EqualsHex(customization.ForegroundInput, "#DCDCDC")
               && EqualsHex(customization.SuccessButton, "#2F5738")
               && EqualsHex(customization.PrimaryButton, "#3B3A63")
               && EqualsHex(customization.WarningButton, "#7A552A")
               && EqualsHex(customization.DangerButton, "#693838")
               && EqualsHex(customization.ForegroundButton, "#DCDCDC")
               && EqualsHex(customization.BackgroundButton, "#282828");

        private static bool IsCurrentLightPalette(CustomizationSettings customization)
            => EqualsHex(customization.BackgroundMain, "#F8FAFC")
               && EqualsHex(customization.BackgroundInput, "#FFFFFF")
               && EqualsHex(customization.BackgroundSecondary, "#EEF2F7")
               && EqualsHex(customization.ForegroundMain, "#0F172A")
               && EqualsHex(customization.ForegroundInput, "#0F172A")
               && EqualsHex(customization.SuccessButton, "#10B981")
               && EqualsHex(customization.PrimaryButton, "#2563EB")
               && EqualsHex(customization.WarningButton, "#F59E0B")
               && EqualsHex(customization.DangerButton, "#EF4444")
               && EqualsHex(customization.ForegroundButton, "#0F172A")
               && EqualsHex(customization.BackgroundButton, "#E2E8F0");

        private static bool IsCurrentDarkPalette(CustomizationSettings customization)
            => EqualsHex(customization.BackgroundMain, "#0F172A")
               && EqualsHex(customization.BackgroundInput, "#1E293B")
               && EqualsHex(customization.BackgroundSecondary, "#1E293B")
               && EqualsHex(customization.ForegroundMain, "#F8FAFC")
               && EqualsHex(customization.ForegroundInput, "#F8FAFC")
               && EqualsHex(customization.SuccessButton, "#10B981")
               && EqualsHex(customization.PrimaryButton, "#3B82F6")
               && EqualsHex(customization.WarningButton, "#F59E0B")
               && EqualsHex(customization.DangerButton, "#EF4444")
               && EqualsHex(customization.ForegroundButton, "#F8FAFC")
               && EqualsHex(customization.BackgroundButton, "#374151");

        private static bool EqualsHex(string left, string right)
            => string.Equals(NormalizeHex(left), NormalizeHex(right), StringComparison.OrdinalIgnoreCase);

        private static string NormalizeHex(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(value.Trim());
                return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ThemeService] Failed to parse color value '{value}': {ex.Message}");
                return value.Trim().ToUpperInvariant();
            }
        }

        private static void ApplyLightPreset(CustomizationSettings customization)
        {
            customization.BackgroundMain = "#F8FAFC";
            customization.BackgroundInput = "#FFFFFF";
            customization.BackgroundSecondary = "#EEF2F7";
            customization.ForegroundMain = "#0F172A";
            customization.ForegroundInput = "#0F172A";
            customization.ForegroundGood = "#10B981";
            customization.ForegroundBad = "#EF4444";
            customization.ForegroundCustom = "#F97316";
            customization.ForegroundRetry = "#EAB308";
            customization.ForegroundBanned = "#8B5CF6";
            customization.ForegroundToCheck = "#14B8A6";
            customization.ForegroundMenuSelected = "#2563EB";
            customization.SuccessButton = "#10B981";
            customization.PrimaryButton = "#2563EB";
            customization.WarningButton = "#F59E0B";
            customization.DangerButton = "#EF4444";
            customization.ForegroundButton = "#0F172A";
            customization.BackgroundButton = "#E2E8F0";
        }

        private static void ApplyDarkPreset(CustomizationSettings customization)
        {
            customization.BackgroundMain = "#0F172A";
            customization.BackgroundInput = "#1E293B";
            customization.BackgroundSecondary = "#1E293B";
            customization.ForegroundMain = "#F8FAFC";
            customization.ForegroundInput = "#F8FAFC";
            customization.ForegroundGood = "#10B981";
            customization.ForegroundBad = "#EF4444";
            customization.ForegroundCustom = "#F97316";
            customization.ForegroundRetry = "#EAB308";
            customization.ForegroundBanned = "#8B5CF6";
            customization.ForegroundToCheck = "#14B8A6";
            customization.ForegroundMenuSelected = "#3B82F6";
            customization.SuccessButton = "#10B981";
            customization.PrimaryButton = "#3B82F6";
            customization.WarningButton = "#F59E0B";
            customization.DangerButton = "#EF4444";
            customization.ForegroundButton = "#F8FAFC";
            customization.BackgroundButton = "#374151";
        }

        private static void ApplyMahAppsBaseTheme(string themeMode)
        {
            var baseColor = string.Equals(themeMode, "Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
            ThemeManager.Current.ChangeThemeBaseColor(Application.Current, baseColor);

            if (Application.Current.MainWindow != null)
            {
                ThemeManager.Current.ChangeThemeBaseColor(Application.Current.MainWindow, baseColor);
            }
        }

        private static void ApplyModernPalette(string themeMode)
        {
            var palette = string.Equals(themeMode, "Dark", StringComparison.OrdinalIgnoreCase)
                ? DarkModernPalette
                : LightModernPalette;

            foreach (var entry in palette)
            {
                SetBrushResourceColor(entry.Key, entry.Value);
            }

            // Apply theme-aware gradient resources for dialogs
            ApplyDialogGradients(themeMode);
        }

        private static void ApplyDialogGradients(string themeMode)
        {
            var isDark = string.Equals(themeMode, "Dark", StringComparison.OrdinalIgnoreCase);

            // Dialog background gradient
            var dialogBgGradient = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(0, 1)
            };

            if (isDark)
            {
                dialogBgGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#121520"), 0));
                dialogBgGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#1B1F2C"), 0.6));
                dialogBgGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#11141E"), 1));
            }
            else
            {
                // Light mode: use light gray/white gradients
                dialogBgGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#F8FAFC"), 0));
                dialogBgGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#F1F5F9"), 0.6));
                dialogBgGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#E2E8F0"), 1));
            }
            Application.Current.Resources["Modern.Gradient.DialogBackground"] = dialogBgGradient;

            // Card dark gradient (used for section cards in dialogs)
            var cardDarkGradient = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(0, 1)
            };

            if (isDark)
            {
                cardDarkGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#2A2F44"), 0));
                cardDarkGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#232839"), 1));
            }
            else
            {
                // Light mode: use white/light gray card gradients
                cardDarkGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FFFFFF"), 0));
                cardDarkGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#F8FAFC"), 1));
            }
            Application.Current.Resources["Modern.Gradient.CardDark"] = cardDarkGradient;

            // Panel dark resources
            SetBrushResourceColor("Modern.PanelDark", isDark ? "#1C2030" : "#F8FAFC");
            SetBrushResourceColor("Modern.PanelDarkSecondary", isDark ? "#1B1E2B" : "#F1F5F9");

            // Overlay resources for dialog borders
            SetBrushResourceColor("Modern.OverlayLight", isDark ? "#22FFFFFF" : "#15000000");
            SetBrushResourceColor("Modern.OverlayMedium", isDark ? "#30FFFFFF" : "#20000000");

            // Muted text colors for better readability in dialogs
            SetBrushResourceColor("Modern.TextMuted", isDark ? "#B0B0B0" : "#64748B");
            SetBrushResourceColor("Modern.TextMutedLight", isDark ? "#CCCCCC" : "#475569");
            SetBrushResourceColor("Modern.TextMutedOnDark", isDark ? "#99FFFFFF" : "#334155");
        }

        private static void ApplyModernCustomizationOverrides(CustomizationSettings customization)
        {
            // Keep Modern.* resources aligned with customization values so mixed legacy/modern pages
            // stay readable after mode switches.
            SetBrushResourceColor("Modern.BackgroundMain", customization.BackgroundMain);
            SetBrushResourceColor("Modern.BackgroundSecondary", customization.BackgroundSecondary);
            SetBrushResourceColor("Modern.BackgroundInput", customization.BackgroundInput);
            SetBrushResourceColor("Modern.BackgroundCard", customization.BackgroundSecondary);
            SetBrushResourceColor("Modern.ForegroundMain", customization.ForegroundMain);
            SetBrushResourceColor("Modern.ForegroundInput", customization.ForegroundInput);
        }

        private static void SetBrushResourceColor(string resourceKey, string color)
        {
            var targetColor = (Color)ColorConverter.ConvertFromString(color);
            if (!TrySetBrushResourceColor(Application.Current.Resources, resourceKey, targetColor))
            {
                Application.Current.Resources[resourceKey] = new SolidColorBrush(targetColor);
            }
        }

        private static bool TrySetBrushResourceColor(ResourceDictionary dictionary, string resourceKey, Color targetColor)
        {
            if (dictionary.Contains(resourceKey))
            {
                if (dictionary[resourceKey] is SolidColorBrush existingBrush && !existingBrush.IsFrozen)
                {
                    existingBrush.Color = targetColor;
                    return true;
                }

                dictionary[resourceKey] = new SolidColorBrush(targetColor);
                return true;
            }

            foreach (var mergedDictionary in dictionary.MergedDictionaries)
            {
                if (TrySetBrushResourceColor(mergedDictionary, resourceKey, targetColor))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
