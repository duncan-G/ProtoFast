using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Core.Ingest;

/// <summary>
/// Guesses the document family from converter metadata and signature features (plan §9.2 step 6).
/// A family selects a prompt skill and gates the human review requirement, so guessing wrong is
/// cheap (a less specific skill) while guessing confidently wrong is not — hence
/// <see cref="Unknown"/> rather than a nearest match.
/// </summary>
public static class FamilyDetector
{
    public const string Unknown = "unknown";
    public const string Transcript = "transcript";
    public const string SlideExport = "slide-export";
    public const string ScannedBook = "scanned-book";
    public const string LegalFiling = "legal-filing";

    public static string Detect(
        IReadOnlyList<LineRecord> lines,
        DocumentStatistics statistics,
        LayoutDocument? layout)
    {
        if (lines.Count == 0)
        {
            return Unknown;
        }

        var producer = layout?.Producer?.ToLowerInvariant() ?? string.Empty;
        if (producer.Contains("powerpoint") || producer.Contains("keynote") || producer.Contains("slides"))
        {
            return SlideExport;
        }

        // A transcript is speaker-prefixed and shows it on most lines; one "Name:" on a legal
        // filing's signature block must not swing this.
        var speakerLines = lines.Count(l => LooksLikeSpeakerTurn(l.Text));
        if (speakerLines * 100 >= lines.Count * 30)
        {
            return Transcript;
        }

        var legalMarkers = lines.Count(l =>
            l.Text.Contains("PLAINTIFF", StringComparison.OrdinalIgnoreCase)
            || l.Text.Contains("DEFENDANT", StringComparison.OrdinalIgnoreCase)
            || l.Text.Contains("Case No", StringComparison.OrdinalIgnoreCase));
        if (legalMarkers >= 3)
        {
            return LegalFiling;
        }

        // Many pages, layout present, and almost no heading hints: an OCR'd book whose structure
        // the converter could not recover.
        if (statistics is { HasLayout: true, PageCount: >= 20 }
            && lines.Count(l => l.Hint == SourceHint.MarkdownHeading) * 200 < lines.Count)
        {
            return ScannedBook;
        }

        return Unknown;
    }

    private static bool LooksLikeSpeakerTurn(string text)
    {
        var colon = text.IndexOf(':');
        if (colon is <= 0 or > 40)
        {
            return false;
        }

        var speaker = text[..colon].Trim();
        return speaker.Length > 0
            && speaker.All(c => char.IsLetter(c) || char.IsWhiteSpace(c) || c is '.' or '-' or '\'')
            && TextMetrics.WordCount(speaker) <= 4;
    }
}
