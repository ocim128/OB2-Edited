using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OpenBullet2.Native.Converters;

// From https://stackoverflow.com/a/5182660/4332314
public class BooleanConverter<T>(T trueValue, T falseValue) : IValueConverter
{
    public T True { get; set; } = trueValue;
    public T False { get; set; } = falseValue;

    public virtual object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? True : False;

    public virtual object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is T convertedValue && EqualityComparer<T>.Default.Equals(convertedValue, True);
}

public sealed class BoolToVisibilityConverter : BooleanConverter<Visibility>
{
    public BoolToVisibilityConverter() :
        base(Visibility.Visible, Visibility.Collapsed)
    {
    }
}

public sealed class BoolToThicknessConverter : BooleanConverter<Thickness>
{
    public BoolToThicknessConverter() :
        base(new Thickness(1), new Thickness(0))
    {
    }
}

public sealed class BoolToTextWrappingConverter : BooleanConverter<TextWrapping>
{
    public BoolToTextWrappingConverter() :
        base(TextWrapping.Wrap, TextWrapping.NoWrap)
    {
    }
}

public sealed class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string parameterString)
        {
            var colors = parameterString.Split('|');
            if (colors.Length == 2)
            {
                return boolValue ? colors[0] : colors[1];
            }
        }
        return "#6B7280"; // Default color
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public sealed class BoolToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string parameterString)
        {
            var icons = parameterString.Split('|');
            if (icons.Length == 2)
            {
                return boolValue ? icons[0] : icons[1];
            }
        }
        return "Help"; // Default icon
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public sealed class LessThanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double doubleValue && parameter is string parameterString && double.TryParse(parameterString, out var threshold))
        {
            return doubleValue < threshold;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
