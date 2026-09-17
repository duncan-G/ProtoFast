using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Core.Ingest;

/// <summary>
/// Computes the document-wide baselines every layout feature is expressed against, then
/// normalizes each line into <see cref="LayoutFeatures"/> (plan §9.2 steps 4–5).
///
/// <para>The body font size is the <em>character-weighted mode</em> of font sizes, not the mean:
/// a document with three pages of body text and forty headings still has one body size, and a
/// mean would be dragged upward by every heading until <c>FontScale</c> stopped separating
/// them.</para>
/// </summary>
internal static class DocumentStatisticsCalculator
{
    /// <param name="rawLines">
    /// The source lines <em>including</em> blank ones. The median block size is measured between
    /// blank lines, and the extractor has already dropped them from <paramref name="texts"/> — so
    /// measuring there would make every line its own block and the statistic meaningless.
    /// </param>
    public static DocumentStatistics Compute(
        IReadOnlyList<string> texts,
        IReadOnlyList<RawLayoutLine?> layout,
        IReadOnlyList<string> rawLines)
    {
        var present = layout.Where(l => l is not null).Select(l => l!).ToList();
        var hasLayout = present.Count > 0;

        var medianBlockWords = MedianBlockWords(rawLines);

        if (!hasLayout)
        {
            return DocumentStatistics.Empty with
            {
                LineCount = texts.Count,
                PageCount = 1,
                MedianBlockWords = medianBlockWords,
                HasLayout = false,
            };
        }

        var pageCount = present.Max(l => l.Page);
        var columnCount = present.Max(l => l.Column) + 1;

        var bodyFontSize = CharacterWeightedMode(present);
        var medianLineWidth = Median(present.Select(l => RelativeWidth(l)).ToList());
        var medianSpacing = MedianSpacing(present);

        return new DocumentStatistics(
            LineCount: texts.Count,
            PageCount: pageCount,
            MedianLineSpacing: medianSpacing <= 0 ? 1 : medianSpacing,
            BodyFontSize: bodyFontSize <= 0 ? 1 : bodyFontSize,
            MedianLineWidth: medianLineWidth <= 0 ? 1 : medianLineWidth,
            ColumnCount: columnCount,
            MedianBlockWords: medianBlockWords,
            HasLayout: true);
    }

    public static LayoutFeatures? Normalize(
        RawLayoutLine? raw,
        IReadOnlyList<RawLayoutLine?> all,
        int index,
        DocumentStatistics statistics)
    {
        if (raw is null)
        {
            return null;
        }

        var pageWidth = raw.PageWidth <= 0 ? 1 : raw.PageWidth;
        var pageHeight = raw.PageHeight <= 0 ? 1 : raw.PageHeight;

        // Gap above is measured against the previous line ON THE SAME PAGE AND COLUMN. Across a
        // page break the y-coordinates are unrelated, and a page-top line would otherwise look
        // like the largest gap in the document.
        var previous = PreviousOnSameColumn(all, index, raw.Page, raw.Column);
        var gap = previous is null
            ? statistics.MedianLineSpacing
            : Math.Max(0, raw.Y - (previous.Y + previous.Height));

        return new LayoutFeatures(
            Page: raw.Page,
            Top: Math.Clamp(raw.Y / pageHeight, 0, 1),
            Indent: Math.Clamp(raw.X / pageWidth, 0, 1),
            Width: Math.Clamp(raw.Width / pageWidth / statistics.MedianLineWidth, 0, 4),
            GapAbove: statistics.MedianLineSpacing <= 0 ? 1 : gap / statistics.MedianLineSpacing,
            FontScale: statistics.BodyFontSize <= 0 ? 1 : raw.FontSize / statistics.BodyFontSize,
            IsBold: raw.Bold,
            IsItalic: raw.Italic,
            ColumnIndex: raw.Column);
    }

    private static RawLayoutLine? PreviousOnSameColumn(
        IReadOnlyList<RawLayoutLine?> all, int index, int page, int column)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            if (all[i] is { } candidate && candidate.Page == page && candidate.Column == column)
            {
                return candidate;
            }
        }

        return null;
    }

    private static double RelativeWidth(RawLayoutLine line) =>
        line.PageWidth <= 0 ? 0 : line.Width / line.PageWidth;

    private static double CharacterWeightedMode(IReadOnlyList<RawLayoutLine> lines)
    {
        // Bucket to 0.5pt so the same visual size measured at 11.98 and 12.01 is one bucket.
        var weights = new Dictionary<double, double>();
        foreach (var line in lines)
        {
            if (line.FontSize <= 0)
            {
                continue;
            }

            var bucket = Math.Round(line.FontSize * 2, MidpointRounding.AwayFromZero) / 2;
            weights[bucket] = weights.GetValueOrDefault(bucket) + Math.Max(1, line.Text.Length);
        }

        return weights.Count == 0 ? 0 : weights.MaxBy(kv => kv.Value).Key;
    }

    private static double MedianSpacing(IReadOnlyList<RawLayoutLine> lines)
    {
        var gaps = new List<double>();
        for (var i = 1; i < lines.Count; i++)
        {
            var previous = lines[i - 1];
            var current = lines[i];
            if (previous.Page != current.Page || previous.Column != current.Column)
            {
                continue;
            }

            var gap = current.Y - (previous.Y + previous.Height);
            if (gap is > 0 and < 1000)
            {
                gaps.Add(gap);
            }
        }

        // With no measurable gaps, fall back to line height: it is the same order of magnitude
        // and keeps GapAbove finite rather than dividing by zero.
        return gaps.Count > 0 ? Median(gaps) : Median(lines.Select(l => Math.Max(l.Height, 1)).ToList());
    }

    /// <summary>Words per block, where a block is text between blank lines in the source.</summary>
    internal static int MedianBlockWords(IReadOnlyList<string> texts)
    {
        var blocks = new List<int>();
        var current = 0;
        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                if (current > 0)
                {
                    blocks.Add(current);
                }

                current = 0;
                continue;
            }

            current += TextMetrics.WordCount(text);
        }

        if (current > 0)
        {
            blocks.Add(current);
        }

        return blocks.Count == 0 ? 0 : (int)Math.Round(Median(blocks.Select(b => (double)b).ToList()));
    }

    internal static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
