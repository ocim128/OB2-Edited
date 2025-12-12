using OpenBullet2.Core.Models.Settings;

namespace OpenBullet2.Native.Services
{
    public interface IThemeService
    {
        void Initialize(MahApps.Metro.Controls.MetroWindow window);
        void SetTheme(CustomizationSettings customization);
        void ApplyAccessibilitySettings();
    }
}
