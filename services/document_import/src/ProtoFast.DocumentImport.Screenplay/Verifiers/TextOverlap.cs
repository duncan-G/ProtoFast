using System.Text;

namespace ProtoFast.DocumentImport.Screenplay.Verifiers;

/// <summary>
/// Word-level overlap between a manuscript and the text units of a story drawn from it, measured in
/// both directions. A word is shared when it sits in a run of <see cref="Run"/> words that both texts
/// have in the same order (a unit shorter than that counts when the whole unit does), so a paraphrase
/// shares almost nothing while a split paragraph with a repaired edge loses only the edge.
/// </summary>
public static class TextOverlap
{
    public const int Run = 4;

    private const int SnippetWords = 12;

    public static Overlap Measure(string manuscript, IReadOnlyList<TextUnit> units)
    {
        var source = Tokenize(manuscript);
        var index = Index(source);
        var joined = Join(source, out var starts);
        var sourceHit = new bool[source.Count];

        var results = new List<UnitOverlap>(units.Count);
        foreach (var unit in units)
        {
            var words = Tokenize(unit.Text);
            var hit = new bool[words.Count];
            if (words.Count > 0 && words.Count < Run)
            {
                var phrase = " " + string.Join(' ', words.Select(w => w.Word)) + " ";
                for (var at = joined.IndexOf(phrase, StringComparison.Ordinal);
                     at >= 0;
                     at = joined.IndexOf(phrase, at + 1, StringComparison.Ordinal))
                {
                    Array.Fill(sourceHit, true, Array.BinarySearch(starts, at + 1), words.Count);
                    Array.Fill(hit, true);
                }
            }
            else
            {
                for (var i = 0; i + Run <= words.Count; i++)
                {
                    if (!index.TryGetValue(Gram(words, i), out var positions))
                    {
                        continue;
                    }

                    Array.Fill(hit, true, i, Run);
                    foreach (var position in positions)
                    {
                        Array.Fill(sourceHit, true, position, Run);
                    }
                }
            }

            results.Add(new UnitOverlap(unit.Path, words.Count, hit.Count(h => h), Snippet(unit.Text, words)));
        }

        var storyWords = results.Sum(r => r.Words);
        return new Overlap(
            storyWords == 0 ? 1 : (double)results.Sum(r => r.Kept) / storyWords,
            source.Count == 0 ? 1 : (double)sourceHit.Count(h => h) / source.Count,
            results,
            Missing(manuscript, source, sourceHit));
    }

    /// <summary>Lower-cased runs of letters and digits; everything else, quotes and dashes included, separates.</summary>
    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var inWord = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (inWord && start < 0)
            {
                start = i;
            }
            else if (!inWord && start >= 0)
            {
                tokens.Add(new Token(text[start..i].ToLowerInvariant(), start, i));
                start = -1;
            }
        }

        return tokens;
    }

    private static Dictionary<string, List<int>> Index(List<Token> source)
    {
        var index = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i + Run <= source.Count; i++)
        {
            var gram = Gram(source, i);
            if (!index.TryGetValue(gram, out var positions))
            {
                index[gram] = positions = [];
            }

            positions.Add(i);
        }

        return index;
    }

    private static string Join(List<Token> source, out int[] starts)
    {
        var joined = new StringBuilder(" ");
        starts = new int[source.Count];
        for (var i = 0; i < source.Count; i++)
        {
            starts[i] = joined.Length;
            joined.Append(source[i].Word).Append(' ');
        }

        return joined.ToString();
    }

    private static List<MissingPassage> Missing(string manuscript, List<Token> source, bool[] hit)
    {
        var passages = new List<MissingPassage>();
        for (var i = 0; i < source.Count;)
        {
            if (hit[i])
            {
                i++;
                continue;
            }

            var end = i;
            while (end < source.Count && !hit[end])
            {
                end++;
            }

            if (end - i >= Run)
            {
                var last = source[Math.Min(end, i + SnippetWords) - 1];
                passages.Add(new MissingPassage(end - i, Collapse(manuscript[source[i].Start..last.End]) + (end - i > SnippetWords ? "…" : "")));
            }

            i = end;
        }

        return passages.OrderByDescending(p => p.Words).ToList();
    }

    private static string Gram(List<Token> words, int at) =>
        string.Join(' ', words.Skip(at).Take(Run).Select(w => w.Word));

    private static string Snippet(string text, List<Token> words) =>
        words.Count == 0 ? "" : Collapse(text[words[0].Start..words[Math.Min(words.Count, SnippetWords) - 1].End]) + (words.Count > SnippetWords ? "…" : "");

    private static string Collapse(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private readonly record struct Token(string Word, int Start, int End);
}
