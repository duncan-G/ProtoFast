using System.Text.RegularExpressions;

namespace ProtoFast.Segmentation.Core.Ingest;

/// <summary>
/// Aligns the converter's layout lines to the Markdown lines they produced (plan §9.2 step 3).
///
/// <para>The two sequences describe the same document in the same reading order but are not
/// index-identical: Markdown adds blank lines, fence markers and <c>#</c> prefixes the layout
/// file knows nothing about. So this walks both forward and matches on normalized text, which
/// stays correct when the converter drops or merges the odd line. A Markdown line with no match
/// simply gets no layout, and every downstream rule already handles <c>Layout == null</c>.</para>
/// </summary>
internal static partial class LayoutJoiner
{
    public static IReadOnlyList<RawLayoutLine?> Join(IReadOnlyList<string> markdownLines, LayoutDocument? layout)
    {
        var result = new RawLayoutLine?[markdownLines.Count];
        if (layout is null || layout.Lines.Count == 0)
        {
            return result;
        }

        var layoutLines = layout.Lines;
        var cursor = 0;

        for (var i = 0; i < markdownLines.Count && cursor < layoutLines.Count; i++)
        {
            var key = NormalizeForMatch(markdownLines[i]);
            if (key.Length == 0)
            {
                continue;
            }

            // Look ahead a bounded distance only. An unbounded search would happily match a
            // repeated running header ten pages away and scramble the alignment for good.
            const int lookahead = 8;
            var limit = Math.Min(layoutLines.Count, cursor + lookahead);
            for (var j = cursor; j < limit; j++)
            {
                if (NormalizeForMatch(layoutLines[j].Text) != key)
                {
                    continue;
                }

                result[i] = layoutLines[j];
                cursor = j + 1;
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Case-folded, punctuation-free, whitespace-collapsed, with Markdown's own decorations
    /// stripped — so <c>## 2 Methods</c> and the layout's <c>2 Methods</c> are the same key.
    /// </summary>
    internal static string NormalizeForMatch(string text) =>
        DecorationPattern().Replace(text, string.Empty).ToLowerInvariant().Trim();

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")]
    private static partial Regex DecorationPattern();
}
