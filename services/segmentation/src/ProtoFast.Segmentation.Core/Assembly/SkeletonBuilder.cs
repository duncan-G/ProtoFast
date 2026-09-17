using System.Globalization;
using System.Text;
using ProtoFast.Segmentation.Core.Ingest;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Core.Assembly;

/// <summary>
/// Builds the compact document skeleton the structurer sees (plan §10.2).
///
/// <para>The structurer never receives paragraph text — only a first-sentence excerpt per
/// paragraph and an id. That is a cost decision and a safety one: it keeps a 400-page document
/// inside a single context, and it means the model has nothing to rewrite even if it wanted to,
/// because the only thing it can return is ids.</para>
/// </summary>
public static class SkeletonBuilder
{
    public const int ExcerptWords = 20;

    /// <summary>One skeleton row, in the order the document reads.</summary>
    public sealed record SkeletonEntry(
        bool IsHeading,
        string Id,
        int? Level,
        double Confidence,
        BoundarySource Source,
        int WordCount,
        ParagraphKind Kind,
        string Excerpt);

    public static IReadOnlyList<SkeletonEntry> Build(
        IReadOnlyList<Paragraph> paragraphs,
        IReadOnlyList<HeadingRecord> headings,
        IReadOnlyDictionary<string, string>? paragraphSummaries = null)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        ArgumentNullException.ThrowIfNull(headings);

        // Headings and paragraphs both anchor to line ids, and paragraphs carry their first line,
        // so a single ordering key puts them back into reading order without a second walk of the
        // document.
        var entries = new List<(string SortKey, SkeletonEntry Entry)>(paragraphs.Count + headings.Count);

        foreach (var heading in headings)
        {
            entries.Add((heading.LineId, new SkeletonEntry(
                IsHeading: true,
                Id: heading.LineId,
                Level: heading.Level,
                Confidence: heading.Confidence,
                Source: heading.Source,
                WordCount: TextMetrics.WordCount(heading.Text),
                Kind: ParagraphKind.Body,
                Excerpt: heading.Text)));
        }

        foreach (var paragraph in paragraphs)
        {
            var excerpt = paragraph.Kind switch
            {
                ParagraphKind.Table => "[table]",
                ParagraphKind.Code => "[code]",
                ParagraphKind.Equation => "[equation]",
                _ => paragraphSummaries is not null && paragraphSummaries.TryGetValue(paragraph.ParagraphId, out var summary)
                    ? summary
                    : Excerpt(paragraph.Text),
            };

            entries.Add((paragraph.FirstLineId, new SkeletonEntry(
                IsHeading: false,
                Id: paragraph.ParagraphId,
                Level: null,
                Confidence: 1.0,
                Source: BoundarySource.Deterministic,
                WordCount: paragraph.WordCount,
                Kind: paragraph.Kind,
                Excerpt: excerpt)));
        }

        return [.. entries.OrderBy(e => e.SortKey, StringComparer.Ordinal).Select(e => e.Entry)];
    }

    /// <summary>Renders the skeleton in the wire format of plan §10.2.</summary>
    public static string Render(IEnumerable<SkeletonEntry> entries)
    {
        var builder = new StringBuilder();
        foreach (var entry in entries)
        {
            if (entry.IsHeading)
            {
                builder.Append(CultureInfo.InvariantCulture,
                    $"H  {entry.Id}  lvl={entry.Level?.ToString(CultureInfo.InvariantCulture) ?? "?"} ");
                builder.Append(CultureInfo.InvariantCulture, $"conf={entry.Confidence:0.00} ");
                builder.Append(CultureInfo.InvariantCulture, $"src={entry.Source.ToString().ToLowerInvariant()}  ");
                builder.AppendLine($"\"{entry.Excerpt}\"");
                continue;
            }

            var marker = entry.Kind switch
            {
                ParagraphKind.Table => 'T',
                ParagraphKind.Code => 'C',
                _ => 'P',
            };

            builder.Append(CultureInfo.InvariantCulture, $"{marker}  {entry.Id}   words={entry.WordCount} ");
            builder.Append(CultureInfo.InvariantCulture, $"kind={entry.Kind.ToString().ToLowerInvariant()} ");
            builder.AppendLine($"\"{entry.Excerpt}\"");
        }

        return builder.ToString();
    }

    /// <summary>First sentence, truncated to <see cref="ExcerptWords"/> words (plan §10.2).</summary>
    internal static string Excerpt(string text)
    {
        var sentences = SentenceSplitter.Split(text);
        var first = sentences.Count > 0 ? sentences[0] : text;
        var words = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= ExcerptWords
            ? first
            : string.Join(' ', words.Take(ExcerptWords)) + "…";
    }
}
