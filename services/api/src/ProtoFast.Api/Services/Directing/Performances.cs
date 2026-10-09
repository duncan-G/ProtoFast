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
    /// The line's words with tags placed: <paramref name="leading"/> at the start, each inline tag
    /// before the word at its index. Over <see cref="MaxTags"/>, the later tags are dropped.
    /// </summary>
    public static string Assemble(string text, IEnumerable<string> leading, IEnumerable<(int BeforeWord, string Tag)> inline)
    {
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var budget = MaxTags;
        var used = new HashSet<string>(StringComparer.Ordinal);
        var before = new Dictionary<int, List<string>>();
        var start = new List<string>();
        foreach (var tag in leading.Select(Clean).Where(t => t.Length > 0))
        {
            if (budget > 0 && used.Add(tag))
            {
                start.Add(tag);
                budget--;
            }
        }

        foreach (var (index, tag) in inline.Select(i => (i.BeforeWord, Clean(i.Tag))).Where(i => i.Item2.Length > 0).OrderBy(i => i.BeforeWord))
        {
            if (budget == 0 || !used.Add(tag))
            {
                continue;
            }

            var at = Math.Clamp(index, 0, tokens.Length);
            if (!before.TryGetValue(at, out var list))
            {
                before[at] = list = [];
            }

            list.Add(tag);
            budget--;
        }

        var parts = new List<string>(start.Select(t => $"[{t}]"));
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
