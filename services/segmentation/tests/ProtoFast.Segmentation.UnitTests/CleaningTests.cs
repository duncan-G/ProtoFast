using ProtoFast.Segmentation.Core.Ingest;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.UnitTests;

public class CleaningTests
{
    [Fact]
    public void RunningHeaderRepeatingWithDifferentPageNumbersIsRemoved()
    {
        // Digit masking is what makes "Chapter 1 · Page 3" and "Chapter 1 · Page 4" one header.
        var texts = new List<string>();
        for (var page = 1; page <= 8; page++)
        {
            texts.Add($"Chapter One - Page {page}");
            texts.Add($"Content line for page {page}.");
        }

        var run = Fixtures.Run(string.Join("\n\n", texts), MarginLayout(texts));

        Assert.Equal(8, run.Cleaning.ArtifactLineIds.Count);
        Assert.All(run.Assembly.Paragraphs, p =>
            Assert.DoesNotContain("Chapter One", p.Text, StringComparison.Ordinal));
    }

    [Fact]
    public void AHeaderAppearingOnTooFewPagesIsKept()
    {
        // 2 of 8 pages is below the 30% threshold: this is content that happens to repeat.
        // The other margin lines must be genuinely distinct — digit masking would make
        // "Unique line 3".."Unique line 8" one repeating key, which is the rule working, not failing.
        string[] distinct = ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta"];
        var texts = new List<string>();
        for (var page = 1; page <= 8; page++)
        {
            texts.Add(page <= 2 ? "Repeated note" : distinct[page - 3]);
            texts.Add($"Content line for page {page}.");
        }

        var run = Fixtures.Run(string.Join("\n\n", texts), MarginLayout(texts));

        Assert.Empty(run.Cleaning.ArtifactLineIds);
    }

    [Fact]
    public void BarePageNumbersAreRemoved()
    {
        var run = Fixtures.Run("Some body text here.\n\n42\n\nMore body text here.");

        Assert.Single(run.Cleaning.ArtifactLineIds);
        Assert.DoesNotContain("42", run.Cleaning.IntegrityBaseline, StringComparison.Ordinal);
    }

    [Fact]
    public void ANumberedListItemIsNotMistakenForAPageNumber()
    {
        var run = Fixtures.Run("Intro line.\n\n1. First item\n2. Second item\n");

        Assert.Empty(run.Cleaning.ArtifactLineIds);
    }

    [Fact]
    public void GenuineCompoundKeepsItsHyphen()
    {
        // "well" is a word this document uses, so "well-/known" is a compound, not hyphenation.
        const string markdown = """
            The results are well known in the field, and the procedure is well-
            known to every practitioner who has read the manual.
            """;

        var run = Fixtures.Run(markdown);

        Assert.Contains("well-", run.Assembly.Paragraphs[0].Text, StringComparison.Ordinal);
        Assert.Single(run.Assembly.Paragraphs);
    }

    [Fact]
    public void JoinedFormAttestedElsewhereIsAlwaysJoined()
    {
        const string markdown = """
            The instrument was calibrated. A second instru-
            ment was held in reserve.
            """;

        var run = Fixtures.Run(markdown);

        Assert.Contains("second instrument was", run.Assembly.Paragraphs[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AMarkdownHeadingContradictedByLayoutIsNotTrusted()
    {
        // A '#' on a full-width, body-size line is the converter guessing. Cleaning refuses to
        // trust it; triage routes the block to a model instead.
        var texts = new[] { "# This line is marked as a heading but is really body text running full width", "Following body line." };
        var layout = Fixtures.BodyLayout(
            [texts[0][2..], texts[1]]);

        var run = Fixtures.Run(string.Join("\n\n", texts), layout);

        Assert.DoesNotContain(run.Cleaning.Boundaries, b => b.Kind == BoundaryKind.Heading);
    }

    [Fact]
    public void AMarkdownHeadingCorroboratedByFontSizeIsTrusted()
    {
        var layout = new LayoutDocument
        {
            Lines =
            [
                new RawLayoutLine { Page = 1, Text = "Methods", X = 50, Y = 50, Width = 120, Height = 18, PageWidth = 600, PageHeight = 800, FontSize = 18 },
                new RawLayoutLine { Page = 1, Text = "Body text follows the heading.", X = 50, Y = 80, Width = 500, Height = 12, PageWidth = 600, PageHeight = 800, FontSize = 10 },
                new RawLayoutLine { Page = 1, Text = "More body text on the next line.", X = 50, Y = 94, Width = 500, Height = 12, PageWidth = 600, PageHeight = 800, FontSize = 10 },
            ],
        };

        var run = Fixtures.Run("# Methods\n\nBody text follows the heading.\nMore body text on the next line.", layout);

        Assert.Contains(run.Cleaning.Boundaries, b => b.Kind == BoundaryKind.Heading);
        Assert.Single(run.Assembly.Headings);
        Assert.Equal("Methods", run.Assembly.Headings[0].Text);
    }

    [Fact]
    public void WithoutLayoutANumberedMarkdownHeadingIsTrusted()
    {
        var run = Fixtures.Run("## 2.1 Data collection\n\nWe collected the data.\n");

        Assert.Single(run.Assembly.Headings);
        Assert.Equal("2.1 Data collection", run.Assembly.Headings[0].Text);
    }

    /// <summary>Alternating margin-zone header and body-zone content, one of each per page.</summary>
    private static LayoutDocument MarginLayout(IReadOnlyList<string> texts)
    {
        var lines = new List<RawLayoutLine>();
        for (var i = 0; i < texts.Count; i++)
        {
            var isMargin = i % 2 == 0;
            lines.Add(new RawLayoutLine
            {
                Page = i / 2 + 1,
                Text = texts[i],
                X = 50,
                Y = isMargin ? 20 : 300,
                Width = isMargin ? 180 : 500,
                Height = 12,
                PageWidth = 600,
                PageHeight = 800,
                FontSize = 10,
            });
        }

        return new LayoutDocument { Lines = lines };
    }
}
