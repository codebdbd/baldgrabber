using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace BaldGrabber.Converters;

public class SelectedStateConverter : IValueConverter
{
    private static readonly SolidColorBrush SelectedBrush = new(new Windows.UI.Color { A = 0x20, R = 0x8B, G = 0x5C, B = 0xF6 });
    private static readonly SolidColorBrush TransparentBrush = new(Windows.UI.Color.FromArgb(0x00, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush AccentBorderBrush = new(new Windows.UI.Color { A = 0xFF, R = 0x8B, G = 0x5C, B = 0xF6 });
    private static readonly SolidColorBrush GrayCircleBorderBrush = new(new Windows.UI.Color { A = 0xFF, R = 0x6B, G = 0x72, B = 0x80 });
    private static readonly SolidColorBrush AccentIconBrush = new(new Windows.UI.Color { A = 0xFF, R = 0x8B, G = 0x5C, B = 0xF6 });
    private static readonly SolidColorBrush GrayIconBrush = new(new Windows.UI.Color { A = 0xFF, R = 0xC3, G = 0xC6, B = 0xD4 });
    private static readonly Thickness BorderThickness = new(2);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not bool selected || parameter is not string param)
        {
            return parameter?.ToString() switch
            {
                "Brush" => TransparentBrush,
                "BorderBrush" => TransparentBrush,
                "Thickness" => BorderThickness,
                "CircleBrush" => TransparentBrush,
                "CircleBorderBrush" => GrayCircleBorderBrush,
                "IconBrush" => GrayIconBrush,
                "CheckVisible" => Visibility.Collapsed,
                _ => TransparentBrush
            };
        }

        return param switch
        {
            "Brush" => selected ? SelectedBrush : TransparentBrush,
            "BorderBrush" => selected ? AccentBorderBrush : TransparentBrush,
            "Thickness" => BorderThickness,
            "CircleBrush" => selected ? TransparentBrush : TransparentBrush,
            "CircleBorderBrush" => selected ? AccentBorderBrush : GrayCircleBorderBrush,
            "IconBrush" => selected ? AccentIconBrush : GrayIconBrush,
            "CheckVisible" => selected ? Visibility.Visible : Visibility.Collapsed,
            _ => TransparentBrush
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
