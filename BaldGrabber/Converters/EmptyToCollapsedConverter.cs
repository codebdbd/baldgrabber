using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace BaldGrabber.Converters;

public class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        var isEmpty = string.IsNullOrEmpty(value as string);
        
        if (invert)
        {
            return isEmpty ? Visibility.Visible : Visibility.Collapsed;
        }
        
        return isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
