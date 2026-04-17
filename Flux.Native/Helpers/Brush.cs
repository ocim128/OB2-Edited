using System.Windows;
using System.Windows.Media;

namespace Flux.Native.Helpers
{
    public static class Brush
    {
        private static readonly Color FallbackColor = Colors.White;

        public static Color GetColor(string propertyName)
        {
            try
            {
                return ((SolidColorBrush)Application.Current.Resources[propertyName]).Color;
            }
            catch
            {
                try
                {
                    return ((SolidColorBrush)Application.Current.Resources["ForegroundMain"]).Color;
                }
                catch
                {
                    return FallbackColor;
                }
            }
        }

        public static SolidColorBrush Get(string propertyName)
        {
            try
            {
                return (SolidColorBrush)Application.Current.Resources[propertyName];
            }
            catch
            {
                try
                {
                    return (SolidColorBrush)Application.Current.Resources["ForegroundMain"];
                }
                catch
                {
                    var fallback = new SolidColorBrush(FallbackColor);
                    fallback.Freeze();
                    return fallback;
                }
            }
        }

        public static SolidColorBrush FromHex(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        public static void SetAppColor(string resourceName, string color)
        {
            var targetColor = (Color)ColorConverter.ConvertFromString(color);

            if (Application.Current.Resources[resourceName] is SolidColorBrush existingBrush && !existingBrush.IsFrozen)
            {
                existingBrush.Color = targetColor;
                return;
            }

            Application.Current.Resources[resourceName] = new SolidColorBrush(targetColor);
        }
    }
}
