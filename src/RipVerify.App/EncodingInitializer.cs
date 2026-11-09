using System.Text;

namespace RipVerify.App;

internal static class EncodingInitializer
{
    public static void Initialize()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
