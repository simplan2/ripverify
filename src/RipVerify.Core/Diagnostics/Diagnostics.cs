namespace RipVerify.Core.Diagnostics;

public static class DiagnosticCodes
{
}

public sealed record DiagnosticMessage
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
