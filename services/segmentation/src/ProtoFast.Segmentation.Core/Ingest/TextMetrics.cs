using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProtoFast.Segmentation.Core.Ingest;

/// <summary>
/// The small text measurements the deterministic phases share. They live in one place because
/// the <c>text-integrity</c> check and paragraph assembly must agree on what "the same text"
/// means down to the character — two nearly-identical whitespace rules would make the hard gate
/// fail on documents that are actually fine.
/// </summary>
public static partial class TextMetrics
{
    public static int WordCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var count = 0;
        var inWord = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Collapses runs of whitespace to a single space and trims. Deliberately nothing else: no
    /// case folding, no punctuation removal, no Unicode normalization. Anything more would let
    /// the integrity check pass on text a model had altered, which is the one thing it exists to
    /// catch (plan §11).
    /// </summary>
    public static string NormalizeWhitespace(string text) =>
        WhitespaceRunPattern().Replace(text, " ").Trim();

    /// <summary>Terminal punctuation per 40 words — triage's "is this prose?" signal (plan §9.4).</summary>
    public static double TerminalPunctuationRate(string text)
    {
        var words = WordCount(text);
        if (words == 0)
        {
            return 0;
        }

        var terminals = text.Count(c => c is '.' or '!' or '?' or '。' or '？' or '！');
        return terminals * 40.0 / words;
    }

    /// <summary>True when the line ends mid-word with a soft hyphen the converter kept.</summary>
    public static bool EndsWithHyphen(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.Length >= 2
            && trimmed[^1] is '-' or '‐' or '­'
            && char.IsLetter(trimmed[^2]);
    }

    public static bool StartsLowercase(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.Length > 0 && char.IsLower(trimmed[0]);
    }

    public static bool EndsWithTerminalPunctuation(string text)
    {
        var trimmed = text.TrimEnd();
        if (trimmed.Length == 0)
        {
            return false;
        }

        // Trailing quotes and brackets close a sentence that already ended.
        var i = trimmed.Length - 1;
        while (i >= 0 && trimmed[i] is '"' or '\'' or '”' or '’' or ')' or ']' or '»')
        {
            i--;
        }

        return i >= 0 && trimmed[i] is '.' or '!' or '?' or '。' or '？' or '！' or ':';
    }

    /// <summary>
    /// Digits masked to <c>#</c> and case folded, so "Page 12" and "Page 340" are the same key.
    /// That equivalence is what lets the running-header rule find a header that carries a page
    /// number (plan §9.3).
    /// </summary>
    public static string MaskDigits(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(char.IsDigit(c) ? '#' : char.ToLowerInvariant(c));
        }

        return NormalizeWhitespace(builder.ToString());
    }

    /// <summary>A bare page number: arabic, roman, or one wrapped in dashes/brackets.</summary>
    public static bool LooksLikePageNumber(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length is > 0 and <= 12 && PageNumberPattern().IsMatch(trimmed);
    }

    /// <summary>A section numbering prefix such as <c>2</c>, <c>2.1</c> or <c>2.1.3</c>.</summary>
    public static bool HasNumberingPrefix(string text) => NumberingPattern().IsMatch(text);

    /// <summary>Depth implied by a numbering prefix: <c>2.1.3 Foo</c> is 3. Zero when absent.</summary>
    public static int NumberingDepth(string text)
    {
        var match = NumberingPattern().Match(text);
        return match.Success ? match.Groups["number"].Value.Count(c => c == '.') + 1 : 0;
    }

    public static bool IsTitleCase(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return false;
        }

        var capitalized = words.Count(w => w.Length > 0 && char.IsUpper(w[0]));
        return capitalized * 2 >= words.Length;
    }

    public static string ToInvariant(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunPattern();

    [GeneratedRegex(@"^[\[\(\-–—\s]*(\d{1,4}|[ivxlcdmIVXLCDM]{1,7})[\]\)\-–—\.\s]*$")]
    private static partial Regex PageNumberPattern();

    [GeneratedRegex(@"^\s*(?<number>\d+(\.\d+)*)[\.\)]?\s+\S")]
    private static partial Regex NumberingPattern();
}
