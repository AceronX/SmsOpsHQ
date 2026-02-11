using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SmsOpsHQ.Desktop.Converters;

// Converts bool to Visibility. True = Visible, False = Collapsed.
// Pass "Invert" as parameter to reverse the logic.
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool boolValue = value is true;
        bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        if (invert) boolValue = !boolValue;
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

// Converts null/empty string to Visibility.Collapsed.
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Converts an int to Visibility. Default: Visible when count > 0. Parameter "WhenZero": Visible when count == 0.
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int count) return Visibility.Collapsed;
        bool whenZero = parameter is string s && s.Equals("WhenZero", StringComparison.OrdinalIgnoreCase);
        return whenZero ? (count == 0 ? Visibility.Visible : Visibility.Collapsed) : (count > 0 ? Visibility.Visible : Visibility.Collapsed);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
