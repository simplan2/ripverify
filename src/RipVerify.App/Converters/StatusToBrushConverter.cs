using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using RipVerify.Core.Models;

namespace RipVerify.App.Converters;

public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly IBrush Verified = new SolidColorBrush(Color.Parse("#2ECC71"));
    private static readonly IBrush AuthenticWithLog = new SolidColorBrush(Color.Parse("#27AE60"));
    private static readonly IBrush Streaming = new SolidColorBrush(Color.Parse("#F1C40F"));
    private static readonly IBrush Undetermined = new SolidColorBrush(Color.Parse("#E67E22"));
    private static readonly IBrush Fake = new SolidColorBrush(Color.Parse("#FF5C4D"));
    private static readonly IBrush Unprocessable = new SolidColorBrush(Color.Parse("#95A5A6"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            VerificationStatus.VerifiedCdRip => Verified,
            VerificationStatus.VerifiedWithWarnings => Verified,
            VerificationStatus.AuthenticRipWithLog => AuthenticWithLog,
            VerificationStatus.LikelyStreaming => Streaming,
            VerificationStatus.UndeterminedLossless => Undetermined,
            VerificationStatus.FakeLossless => Fake,
            VerificationStatus.Unprocessable => Unprocessable,
            _ => Undetermined
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
