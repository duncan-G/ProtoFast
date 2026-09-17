using System.Text.RegularExpressions;

namespace ProtoFast.Segmentation.Core.Assembly;

/// <summary>
/// A rule-based sentence splitter. It exists so that a structurer's <c>beforeSentence: 3</c>
/// means the same thing to the model and to the code applying the split — the model is shown
/// numbered sentences produced here, and the split is applied at the same index here (plan §10.2).
/// Doing it any other way would let an off-by-one silently cut a paragraph in the wrong place.
/// </summary>
public static partial class SentenceSplitter
{
    /// <summary>Abbreviations whose full stop does not end a sentence.</summary>
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "dr", "prof", "sr", "jr", "st", "vs", "etc", "e.g", "i.e", "cf",
        "fig", "eq", "no", "vol", "pp", "ed", "al", "approx", "inc", "ltd", "co",
    };

    public static IReadOnlyList<string> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var sentences = new List<string>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('.' or '!' or '?'))
            {
                continue;
            }

            // Consume any closing quotes/brackets that belong to this sentence.
            var end = i + 1;
            while (end < text.Length && text[end] is '"' or '\'' or '”' or '’' or ')' or ']')
            {
                end++;
            }

            if (end < text.Length && !char.IsWhiteSpace(text[end]))
            {
                continue;
            }

            if (text[i] == '.' && IsAbbreviationOrNumber(text, i))
            {
                continue;
            }

            sentences.Add(text[start..end].Trim());
            start = end;
            i = end - 1;
        }

        var tail = text[start..].Trim();
        if (tail.Length > 0)
        {
            sentences.Add(tail);
        }

        return sentences;
    }

    /// <summary>Numbered sentences exactly as a prompt shows them, 1-based (plan §10.2).</summary>
    public static string Numbered(string text) =>
        string.Join('\n', Split(text).Select((sentence, i) => $"{i + 1}. {sentence}"));

    /// <summary>
    /// Splits <paramref name="text"/> before the 1-based <paramref name="sentenceIndex"/>, or
    /// returns null when the index is out of range — which is a validation failure, not a
    /// silently clamped edit.
    /// </summary>
    public static (string Head, string Tail)? SplitAt(string text, int sentenceIndex)
    {
        var sentences = Split(text);
        if (sentenceIndex <= 1 || sentenceIndex > sentences.Count)
        {
            return null;
        }

        return (
            string.Join(' ', sentences.Take(sentenceIndex - 1)),
            string.Join(' ', sentences.Skip(sentenceIndex - 1)));
    }

    private static bool IsAbbreviationOrNumber(string text, int dotIndex)
    {
        // "3.14" — a decimal point, not a full stop.
        if (dotIndex > 0 && dotIndex + 1 < text.Length
            && char.IsDigit(text[dotIndex - 1]) && char.IsDigit(text[dotIndex + 1]))
        {
            return true;
        }

        var wordStart = dotIndex;
        while (wordStart > 0 && (char.IsLetter(text[wordStart - 1]) || text[wordStart - 1] == '.'))
        {
            wordStart--;
        }

        var word = text[wordStart..dotIndex].TrimEnd('.');

        // A single capital is an initial ("J. Smith"), never a sentence end.
        return word.Length == 1 && char.IsUpper(word[0]) || Abbreviations.Contains(word);
    }

    [GeneratedRegex(@"\s+")]
    internal static partial Regex WhitespacePattern();
}
