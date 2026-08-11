using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CodeCrackApp.Converters;

/// Maps a finding severity ("high"/"medium"/other) to a chip background brush.
public sealed class SeverityToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var sev = (value as string)?.ToLowerInvariant();
        return sev switch
        {
            "high" => new SolidColorBrush(Color.FromRgb(0xD1, 0x3A, 0x3A)),   // red
            "medium" => new SolidColorBrush(Color.FromRgb(0xC9, 0x7A, 0x14)), // amber
            _ => new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),        // gray
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
