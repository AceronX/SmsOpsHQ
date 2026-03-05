using System.Globalization;
using System.Windows.Data;

namespace SmsOpsHQ.Desktop.Converters;

// Inverts a bool value. Used for IsEnabled when IsBusy is true.
public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : false;
}

// Converts IsBusy bool to button text. Parameter optional: "NormalText|BusyText" (e.g. "Send|Sending...").
// Default: "Sign In" / "Signing in...".
public sealed class BusyToButtonTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool busy = value is true;
        if (parameter is string param && param.Contains('|'))
        {
            string[] parts = param.Split('|');
            return busy ? (parts.Length > 1 ? parts[1].Trim() : parts[0]) : parts[0].Trim();
        }
        return busy ? "Signing in..." : "Sign In";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Converts a nullable value to Visibility: null -> Collapsed, not null -> Visible.
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Converts a nullable value to Visibility: null -> Visible, not null -> Collapsed (for placeholders).
public sealed class NullToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Empty or whitespace string -> Visible; otherwise -> Collapsed (for search placeholders).
public sealed class EmptyToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s && string.IsNullOrWhiteSpace(s)
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
