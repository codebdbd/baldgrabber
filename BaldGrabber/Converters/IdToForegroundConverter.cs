using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace BaldGrabber.Converters;

public class IdToForegroundConverter : IValueConverter
{
    public Brush? NormalBrush { get; set; }
    public Brush? PlaceholderBrush { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string id && string.IsNullOrEmpty(id))
            return PlaceholderBrush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xC3, 0xC6, 0xD4));
        return NormalBrush ?? new SolidColorBrush(Microsoft.UI.Colors.White);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
