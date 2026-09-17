using System.Text;
using ProtoFast.Segmentation.Core.Ingest;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Core.Cleaning;

/// <summary>
/// The single rule for turning cleaned lines back into text.
///
/// <para>It exists because <c>text-integrity</c> (plan §11) compares the concatenation of every
/// paragraph against the concatenation of the cleaned source, and those two concatenations are
/// produced by different code in different phases. If they disagreed about one space — say,
/// because cleaning removed a hyphen and only one of them knew the next line glues on with no
/// separator — the hard gate would fail on documents that are perfectly correct. So both call
/// this.</para>
/// </summary>
public static class LineJoiner
{
    /// <summary>
    /// Joins with a single space, except where the line is a hyphen-join continuation — there the
    /// previous line's trailing hyphen was removed and the two halves are one word.
    /// </summary>
    public static string Join(IEnumerable<LineRecord> lines, IReadOnlySet<string> hyphenJoinedLineIds)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            var text = line.Text;
            if (builder.Length > 0 && !hyphenJoinedLineIds.Contains(line.LineId))
            {
                builder.Append(' ');
            }

            builder.Append(text);
        }

        return TextMetrics.NormalizeWhitespace(builder.ToString());
    }
}
