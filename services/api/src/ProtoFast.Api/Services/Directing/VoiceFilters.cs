using System.Text.RegularExpressions;

namespace ProtoFast.Api.Services.Directing;

/// <summary>
/// Turns what a sheet says about a character into the speech provider's library labels, so a cast
/// voice can be searched for. Mirrored in <c>clients/theplot/src/app/direction/voice-filters.ts</c>.
/// </summary>
public static partial class VoiceFilters
{
    /// <summary>The accents the library labels reliably; others come back as loose matches.</summary>
    public static readonly IReadOnlyList<string> Accents = ["american", "british", "australian", "canadian", "irish", "scottish", "indian"];

    private static readonly (string Accent, string[] Words)[] AccentWords =
    [
        ("american", ["american", "texan", "southern", "new york", "brooklyn"]),
        ("british", ["british", "english", "london", "cockney", "rp", "received pronunciation", "yorkshire"]),
        ("australian", ["australian", "aussie"]),
        ("canadian", ["canadian"]),
        ("irish", ["irish", "dublin"]),
        ("scottish", ["scottish", "scots", "glaswegian", "glasgow", "edinburgh", "highland"]),
        ("indian", ["indian"]),
    ];

    public static string Gender(string? gender)
    {
        var text = gender?.ToLowerInvariant() ?? "";
        if (Matches(text, "non-binary", "nonbinary", "neutral", "agender", "genderless"))
        {
            return "neutral";
        }

        if (Matches(text, "female", "woman", "girl", "f"))
        {
            return "female";
        }

        return Matches(text, "male", "man", "boy", "m") ? "male" : "";
    }

    /// <summary>"young" under 30, "middle_aged" under 60, "old" from there; words where there is no number.</summary>
    public static string Age(string? age)
    {
        var text = age?.ToLowerInvariant() ?? "";
        if (text.Contains("middle"))
        {
            return "middle_aged";
        }

        if (Number().Match(text) is { Success: true } number && int.TryParse(number.Value, out var years))
        {
            return years < 30 ? "young" : years < 60 ? "middle_aged" : "old";
        }

        if (Matches(text, "child", "kid", "teen", "teenager", "teenage", "young", "youth", "adolescent"))
        {
            return "young";
        }

        return Matches(text, "old", "elderly", "aged", "senior", "ancient") ? "old" : "";
    }

    /// <summary>The accent named first in the texts, or empty.</summary>
    public static string Accent(params string?[] texts)
    {
        var text = string.Join(" ", texts.Where(t => !string.IsNullOrWhiteSpace(t))).ToLowerInvariant();
        return AccentWords
            .SelectMany(a => a.Words.Select(w => (a.Accent, At: Find(text, w))))
            .Where(m => m.At >= 0)
            .OrderBy(m => m.At)
            .Select(m => m.Accent)
            .FirstOrDefault() ?? "";
    }

    private static bool Matches(string text, params string[] words) => words.Any(w => Find(text, w) >= 0);

    private static int Find(string text, string word) =>
        Regex.Match(text, $@"(?<![a-z]){Regex.Escape(word)}(?![a-z])") is { Success: true } m ? m.Index : -1;

    [GeneratedRegex(@"\d{1,3}")]
    private static partial Regex Number();
}
