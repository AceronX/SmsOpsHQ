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

// Converts IsBusy bool to button text: true -> "Signing in...", false -> "Sign In".
public sealed class BusyToButtonTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "Signing in..." : "Sign In";

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
