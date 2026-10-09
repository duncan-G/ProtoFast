using System.Text.RegularExpressions;

namespace ProtoFast.Api.Services.Directing;

/// <summary>
/// A cue's performance is the line's own words with delivery tags in square brackets, e.g.
/// <c>[sad] I'm sorry.</c> These helpers keep the words and the tags apart; the client's
/// <c>performance.ts</c> mirrors them.
/// </summary>
public static partial class Performances
{
    /// <summary>Two tags plus one non-verbal; more sounds overacted.</summary>
    public const int MaxTags = 3;

    public const int MaxLength = 4000;

    public static IReadOnlyList<string> Tags(string performance) =>
        TagPattern().Matches(performance).Select(m => m.Groups[1].Value.Trim().ToLowerInvariant()).ToList();

    /// <summary>The performance without its tags, with the spacing collapsed.</summary>
    public static string Strip(string performance) => Collapse(TagPattern().Replace(performance, " "));

    /// <summary>Words lowercased and without surrounding punctuation, so pause punctuation does not count.</summary>
    public static IReadOnlyList<string> Words(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => new string(token.Where(ch => char.IsLetterOrDigit(ch) || ch is '\'' or '’').ToArray()).ToLowerInvariant())
            .Where(word => word.Length > 0)
            .ToList();

    public static bool SameWords(string performance, string text) => Words(Strip(performance)).SequenceEqual(Words(text));

    /// <summary>
    /// The line's own words with the tags of <paramref name="marked"/> (the line written back with
    /// tags among its words), each before the word it preceded there; null when the words differ.
    /// Tags <paramref name="accept"/> refuses, repeats, and those over <see cref="MaxTags"/> are dropped.
    /// </summary>
    public static string? Mark(string text, string marked, Func<string, string?> accept)
    {
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var wordTokens = Enumerable.Range(0, tokens.Length).Where(i => Words(tokens[i]).Count > 0).ToList();
        var seen = new List<string>();
        var before = new Dictionary<int, List<string>>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var spaced = TagPattern().Replace(marked, m => $" {m.Value} ");
        foreach (var token in spaced.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (TagPattern().Match(token) is { Success: true } match && match.Length == token.Length)
            {
                if (used.Count < MaxTags && accept(Clean(match.Groups[1].Value)) is { } tag && used.Add(tag))
                {
                    var at = seen.Count < wordTokens.Count ? wordTokens[seen.Count] : tokens.Length;
                    if (!before.TryGetValue(at, out var list))
                    {
                        before[at] = list = [];
                    }

                    list.Add(tag);
                }
            }
            else
            {
                seen.AddRange(Words(token));
            }
        }

        if (!seen.SequenceEqual(Words(text)))
        {
            return null;
        }

        var parts = new List<string>();
        for (var i = 0; i <= tokens.Length; i++)
        {
            if (before.TryGetValue(i, out var tags))
            {
                parts.AddRange(tags.Select(t => $"[{t}]"));
            }

            if (i < tokens.Length)
            {
                parts.Add(tokens[i]);
            }
        }

        return string.Join(' ', parts);
    }

    public static string Collapse(string text) => Spaces().Replace(text, " ").Trim();

    private static string Clean(string tag) => Collapse(tag.Trim('[', ']')).ToLowerInvariant();

    [GeneratedRegex(@"\[([^\[\]\n]{1,40})\]")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
