using System.Text.RegularExpressions;

namespace ProtoFast.DocumentImport.History.Gutenberg;

/// <summary>
/// The span of years a book's subjects and title name: ranges ("Civil War, 1861-1865", a person's
/// "1732-1799"), single years and centuries ("19th century").
/// </summary>
internal static partial class SubjectYears
{
    private const int Earliest = 1000;
    private const int Latest = 2099;

    public static (int? First, int? Last) Find(params string[] texts)
    {
        int? first = null, last = null;
        foreach (var text in texts)
        {
            foreach (Match m in Range().Matches(text))
            {
                var start = int.Parse(m.Groups["start"].Value);
                var end = start;
                if (m.Groups["end"].Success)
                {
                    var tail = m.Groups["end"].Value;
                    end = tail.Length == 2 ? start / 100 * 100 + int.Parse(tail) : int.Parse(tail);
                    if (end < start)
                    {
                        end = start;
                    }
                }

                Widen(start, end);
            }

            foreach (Match m in Century().Matches(text))
            {
                var century = int.Parse(m.Groups["n"].Value);
                Widen((century - 1) * 100, (century - 1) * 100 + 99);
            }
        }

        return (first, last);

        void Widen(int start, int end)
        {
            if (start < Earliest || end > Latest)
            {
                return;
            }

            first = first is null ? start : Math.Min(first.Value, start);
            last = last is null ? end : Math.Max(last.Value, end);
        }
    }

    [GeneratedRegex(@"(?<![\d.])(?<start>1\d{3}|20\d{2})(?:\s*-\s*(?<end>1\d{3}|20\d{2}|\d{2})(?!\d))?(?![\d])")]
    private static partial Regex Range();

    [GeneratedRegex(@"\b(?<n>1[0-9]|20|21)(?:st|nd|rd|th) century\b", RegexOptions.IgnoreCase)]
    private static partial Regex Century();
}
