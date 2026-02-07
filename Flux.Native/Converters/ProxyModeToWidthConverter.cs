using System;
using System.Globalization;
using System.Windows.Data;
using RuriLib.Models.Jobs;

namespace Flux.Native.Converters
{
    public class ProxyModeToWidthConverter : IValueConverter
    {
        public double Width { get; set; } = 150.0;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is JobProxyMode mode)
            {
                return mode == JobProxyMode.Off ? 0.0 : Width;
            }
            return Width;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
