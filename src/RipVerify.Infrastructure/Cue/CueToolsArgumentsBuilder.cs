namespace RipVerify.Infrastructure.Cue;

internal static class CueToolsArgumentsBuilder
{
    public static IReadOnlyList<string> BuildVerifyArguments(string cuePath)
    {
        return new List<string> { "-v", cuePath };
    }
}
