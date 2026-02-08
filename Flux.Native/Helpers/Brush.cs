using System.Windows;
using System.Windows.Media;

namespace Flux.Native.Helpers
{
    public static class Brush
    {
        public static Color GetColor(string propertyName)
        {
            try
            {
                return ((SolidColorBrush)Application.Current.Resources[propertyName]).Color; 
            }
            catch
            {
                return ((SolidColorBrush)Application.Current.Resources["ForegroundMain"]).Color;
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
                return (SolidColorBrush)Application.Current.Resources["ForegroundMain"];
            }
        }

        public static SolidColorBrush FromHex(string hex)
            => new((Color)ColorConverter.ConvertFromString(hex));

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
