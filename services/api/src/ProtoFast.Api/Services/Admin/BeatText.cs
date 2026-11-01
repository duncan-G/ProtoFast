using System.Text.RegularExpressions;

namespace ProtoFast.Api.Services.Admin;

internal static partial class BeatText
{
    /// <summary>The words without the <c>@</c> that marks a mention.</summary>
    public static string Plain(string text) => Mention().Replace(text, "").Trim();

    [GeneratedRegex(@"@(?=\S)")]
    private static partial Regex Mention();
}
