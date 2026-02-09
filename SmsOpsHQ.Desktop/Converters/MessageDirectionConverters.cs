using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SmsOpsHQ.Desktop.Converters;

// Aligns message bubbles: Outbound = Right, Inbound = Left, Note = Center.
public sealed class DirectionToAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string direction = value as string ?? "Inbound";
        return direction switch
        {
            "Outbound" => HorizontalAlignment.Right,
            "Note" => HorizontalAlignment.Center,
            _ => HorizontalAlignment.Left
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Colors message bubbles by direction.
public sealed class DirectionToBubbleColorConverter : IValueConverter
{
    private static readonly SolidColorBrush OutboundBrush = new(Color.FromRgb(37, 99, 235));    // #2563EB
    private static readonly SolidColorBrush InboundBrush = new(Color.FromRgb(243, 244, 246));   // #F3F4F6
    private static readonly SolidColorBrush NoteBrush = new(Color.FromRgb(254, 243, 199));      // #FEF3C7

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string direction = value as string ?? "Inbound";
        return direction switch
        {
            "Outbound" => OutboundBrush,
            "Note" => NoteBrush,
            _ => InboundBrush
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Text color: white for outbound (blue bg), dark for inbound/note.
public sealed class DirectionToTextColorConverter : IValueConverter
{
    private static readonly SolidColorBrush WhiteBrush = new(Colors.White);
    private static readonly SolidColorBrush DarkBrush = new(Color.FromRgb(17, 24, 39));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string direction = value as string ?? "Inbound";
        return direction == "Outbound" ? WhiteBrush : DarkBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
