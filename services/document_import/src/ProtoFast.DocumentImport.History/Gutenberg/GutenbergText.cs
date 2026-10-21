using System.Text;
using System.Text.RegularExpressions;

namespace ProtoFast.DocumentImport.History.Gutenberg;

/// <summary>An ebook's plain text without Gutenberg's licence wrapper, packed into passages of whole paragraphs.</summary>
public static partial class GutenbergText
{
    /// <summary>About 700 tokens: enough context to read on its own, small enough to rank and quote.</summary>
    public const int PassageChars = 3000;

    public static string TextUrl(int bookId) => $"https://www.gutenberg.org/cache/epub/{bookId}/pg{bookId}.txt";

    public static string StripBoilerplate(string text)
    {
        var start = Start().Match(text);
        if (start.Success)
        {
            text = text[(start.Index + start.Length)..];
        }

        var end = End().Match(text);
        if (end.Success)
        {
            text = text[..end.Index];
        }

        return text.Trim();
    }

    /// <summary>Packs paragraphs into passages of about <paramref name="size"/> characters; a longer paragraph is split between sentences.</summary>
    public static IReadOnlyList<string> Passages(string text, int size = PassageChars)
    {
        var passages = new List<string>();
        var buffer = new StringBuilder();
        foreach (var raw in ParagraphBreak().Split(text.Replace("\r\n", "\n").Replace('\0', ' ')))
        {
            var paragraph = Whitespace().Replace(raw, " ").Trim();
            if (paragraph.Length == 0)
            {
                continue;
            }

            if (paragraph.Length > size)
            {
                foreach (var sentence in SentenceBreak().Split(paragraph))
                {
                    if (buffer.Length > 0 && buffer.Length + sentence.Length > size)
                    {
                        Flush();
                    }

                    Append(sentence, " ");
                }

                continue;
            }

            if (buffer.Length > 0 && buffer.Length + paragraph.Length > size)
            {
                Flush();
            }

            Append(paragraph, "\n\n");
        }

        Flush();
        return passages;

        void Append(string part, string separator)
        {
            if (buffer.Length > 0)
            {
                buffer.Append(separator);
            }

            buffer.Append(part);
        }

        void Flush()
        {
            if (buffer.Length > 0)
            {
                passages.Add(buffer.ToString());
                buffer.Clear();
            }
        }
    }

    [GeneratedRegex(@"\*\*\*\s*START OF (THE|THIS) PROJECT GUTENBERG EBOOK.*?\*\*\*", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Start();

    [GeneratedRegex(@"\*\*\*\s*END OF (THE|THIS) PROJECT GUTENBERG EBOOK", RegexOptions.IgnoreCase)]
    private static partial Regex End();

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex ParagraphBreak();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBreak();
}
