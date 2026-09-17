using ProtoFast.Segmentation.Core.Ingest;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Validation;

namespace ProtoFast.Segmentation.UnitTests;

/// <summary>
/// The hard gate (plan N1). These are the tests that have to keep working: a failure here means
/// the pipeline can lose or alter a user's text without anything noticing.
/// </summary>
public class TextIntegrityTests
{
    [Fact]
    public void CleanMarkdownRoundTripsThroughAssemblyExactly()
    {
        const string markdown = """
            # Methods

            We collected samples from three sites between March and June. Each sample was
            stored at -20 degrees and processed within a week.

            Sampling followed the protocol described in the appendix.

            ## Data collection

            Instruments were calibrated daily against a reference standard.
            """;

        var run = Fixtures.Run(markdown);
        var result = Checks.CheckTextIntegrity(run.Cleaning.IntegrityBaseline, run.Assembly.Paragraphs, run.Assembly.Headings);

        Assert.True(result.Passed, result.ErrorReport);
    }

    [Fact]
    public void HyphenJoinPreservesTextExactly()
    {
        // The hyphen join rewrites a line, so it is the one cleaning rule that could break the
        // baseline. Both sides go through LineJoiner precisely so this holds.
        const string markdown = """
            The calibration procedure requires an instru-
            ment that has been serviced within the calendar year.
            """;

        var run = Fixtures.Run(markdown);

        Assert.Contains("instrument", run.Assembly.Paragraphs[0].Text, StringComparison.Ordinal);
        Assert.True(
            Checks.CheckTextIntegrity(run.Cleaning.IntegrityBaseline, run.Assembly.Paragraphs, run.Assembly.Headings).Passed);
    }

    [Fact]
    public void AlteredParagraphTextFailsTheCheck()
    {
        // The exact failure the gate exists for: a model that "helpfully" rewrote a word.
        var run = Fixtures.Run("The quick brown fox jumps over the lazy dog.");
        var tampered = run.Assembly.Paragraphs
            .Select(p => p with { Text = p.Text.Replace("quick", "fast", StringComparison.Ordinal) })
            .ToList();

        var result = Checks.CheckTextIntegrity(run.Cleaning.IntegrityBaseline, tampered, run.Assembly.Headings);

        Assert.False(result.Passed);
        Assert.Contains("Mismatch at char", result.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void DroppedParagraphFailsTheCheck()
    {
        var run = Fixtures.Run("First paragraph here.\n\nSecond paragraph here.\n\nThird paragraph here.");
        var truncated = run.Assembly.Paragraphs.Take(run.Assembly.Paragraphs.Count - 1).ToList();

        Assert.False(Checks.CheckTextIntegrity(run.Cleaning.IntegrityBaseline, truncated, run.Assembly.Headings).Passed);
    }

    [Fact]
    public void NormalizationCollapsesWhitespaceAndNothingElse()
    {
        Assert.Equal("a b c", TextMetrics.NormalizeWhitespace("  a \t\n b   c  "));

        // Case, punctuation and Unicode must survive, or the gate would pass on altered text.
        Assert.Equal("A—b, Ç", TextMetrics.NormalizeWhitespace("A—b,   Ç"));
    }

    [Fact]
    public void ArtifactLinesAreExcludedFromBothSides()
    {
        var texts = new List<string>();
        for (var page = 1; page <= 6; page++)
        {
            texts.Add($"Journal of Testing, Vol {page}");
            texts.Add($"Body sentence on page {page} that carries the actual content.");
        }

        var layout = HeaderLayout(texts);
        var run = Fixtures.Run(string.Join("\n\n", texts), layout);

        Assert.NotEmpty(run.Cleaning.ArtifactLineIds);
        Assert.DoesNotContain("Journal of Testing", run.Cleaning.IntegrityBaseline, StringComparison.Ordinal);
        Assert.True(Checks.CheckTextIntegrity(run.Cleaning.IntegrityBaseline, run.Assembly.Paragraphs, run.Assembly.Headings).Passed);
    }

    /// <summary>Alternating header/body lines, header pinned to the top margin of each page.</summary>
    private static LayoutDocument HeaderLayout(IReadOnlyList<string> texts)
    {
        var lines = new List<RawLayoutLine>();
        for (var i = 0; i < texts.Count; i++)
        {
            var isHeader = i % 2 == 0;
            lines.Add(new RawLayoutLine
            {
                Page = i / 2 + 1,
                Text = texts[i],
                X = 50,
                Y = isHeader ? 10 : 200,
                Width = isHeader ? 200 : 500,
                Height = 12,
                PageWidth = 600,
                PageHeight = 800,
                FontSize = 10,
            });
        }

        return new LayoutDocument { Lines = lines };
    }
}
