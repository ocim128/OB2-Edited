using Flux.Core.Models.Settings;

namespace Flux.Native.Services
{
    public interface IThemeService
    {
        void Initialize(MahApps.Metro.Controls.MetroWindow window);
        void SetTheme(CustomizationSettings customization);
        void ApplyAccessibilitySettings();
    }
}
