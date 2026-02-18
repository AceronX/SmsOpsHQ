using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SmsOpsHQ.Desktop.Converters;

public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string hexColor)
            return new SolidColorBrush(Colors.Gray);

        try
        {
            // Remove # if present
            hexColor = hexColor.TrimStart('#');
            
            // Parse hex color
            if (hexColor.Length == 6)
            {
                byte r = byte.Parse(hexColor.Substring(0, 2), NumberStyles.HexNumber);
                byte g = byte.Parse(hexColor.Substring(2, 2), NumberStyles.HexNumber);
                byte b = byte.Parse(hexColor.Substring(4, 2), NumberStyles.HexNumber);
                
                // If parameter is provided, apply opacity (0.0-1.0)
                if (parameter is string opacityStr && double.TryParse(opacityStr, NumberStyles.Float, culture, out double opacity))
                {
                    opacity = Math.Max(0.0, Math.Min(1.0, opacity));
                    return new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), r, g, b));
                }
                
                return new SolidColorBrush(Color.FromRgb(r, g, b));
            }
        }
        catch
        {
            // Fallback to gray on parse error
        }

        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
