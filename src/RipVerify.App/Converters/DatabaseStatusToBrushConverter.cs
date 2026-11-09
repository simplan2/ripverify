using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using RipVerify.Core.Models;

namespace RipVerify.App.Converters;

public sealed class DatabaseStatusToBrushConverter : IValueConverter
{
    private static readonly IBrush Verified = new SolidColorBrush(Color.Parse("#2ECC71"));
    private static readonly IBrush Missing = new SolidColorBrush(Color.Parse("#E67E22"));
    private static readonly IBrush Failed = new SolidColorBrush(Color.Parse("#FF5C4D"));
    private static readonly IBrush Neutral = new SolidColorBrush(Color.Parse("#95A5A6"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            AccurateRipStatus.AccuratelyRipped => Verified,
            AccurateRipStatus.NotFound => Missing,
            AccurateRipStatus.Inaccurate => Failed,
            AccurateRipStatus.Unprocessable => Neutral,
            CtdbStatus.AccuratelyRipped => Verified,
            CtdbStatus.NotFound => Missing,
            CtdbStatus.Inaccurate => Failed,
            CtdbStatus.Unprocessable => Neutral,
            _ => Neutral
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
