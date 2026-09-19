using ProtoFast.Segmentation.Core.Classification;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Validation;

namespace ProtoFast.Segmentation.UnitTests;

/// <summary>
/// Phase 5 (scene plan §5, §8.5). One asymmetry drives every test here: <b>under-removal is
/// recoverable in review; over-removal silently loses the work.</b>
/// </summary>
public class PresentationTests
{
    private static readonly PresentationOptions Options = new();

    private static IReadOnlyList<Paragraph> Document(params (string Id, string Text)[] paragraphs) =>
        [.. paragraphs.Select(p => SceneFixtures.Paragraph(p.Id, p.Text))];

    [Fact]
    public void BodyProseAtTheDocumentEdgesIsNotACandidate()
    {
        // Position is a filter, not a reason. Offering every edge paragraph would cost a model call
        // on every document and buy nothing on the ones whose edges are plainly text — and a clean
        // Markdown upload has to finish end to end with no provider at all.
        var paragraphs = Document(
            ("P00001", "Field instruments drift over a season of transport and temperature cycling, "
                + "and a device that read correctly at the factory will not read correctly later."),
            ("P00002", "This document describes the procedure used to detect that drift and to "
                + "correct it before the next deployment window closes."));

        Assert.Empty(PresentationCandidates.Propose(paragraphs, new HashSet<string>(), Options));
    }

    [Fact]
    public void ACopyrightLineAndAContentsListAreCandidates()
    {
        var paragraphs = Document(
            ("P00001", "© 2026 Ultra Motion Press. All rights reserved."),
            ("P00002", "Introduction .......... 1\nMethods .......... 14\nResults .......... 39"),
            ("P00003", "The instrument fleet was assembled over two seasons of field work, and each "
                + "unit carries a service record from the date it entered the rotation."));

        var candidates = PresentationCandidates.Propose(paragraphs, new HashSet<string>(), Options);

        Assert.Equal(2, candidates.Count);
        Assert.Equal(MetadataClass.FrontMatter, candidates[0].Suggested);
        Assert.Equal(MetadataClass.FrontMatter, candidates[1].Suggested);
        Assert.DoesNotContain(candidates, c => c.ParagraphId == "P00003");
    }

    [Fact]
    public void AnUnansweredCandidateWithNoCodeOpinionResolvesToDisplayable()
    {
        // §8.5 step 4. The default is the whole of what protects the work.
        var paragraphs = Document(("P00001", "A Novel"), ("P00002", "Body text follows here and it is long enough to be prose."));

        var candidates = PresentationCandidates.Propose(paragraphs, new HashSet<string>(), Options);
        var resolved = PresentationCandidates.Resolve(
            paragraphs, candidates, new Dictionary<string, ParagraphPresentation>());

        Assert.All(resolved, p => Assert.True(p.IsDisplayable));
    }

    [Fact]
    public void AMetadataVerdictWithNoClassIsReadAsDisplayable()
    {
        // A verdict that says "remove this" without saying what it is has not made a claim, and the
        // expensive mistake is the one that acts on it.
        var paragraphs = Document(("P00001", "© 2026 Ultra Motion Press."));
        var candidates = PresentationCandidates.Propose(paragraphs, new HashSet<string>(), Options);

        var resolved = PresentationCandidates.Resolve(
            paragraphs, candidates,
            new Dictionary<string, ParagraphPresentation>
            {
                ["P00001"] = new("P00001", Presentation.Metadata, null, [], 0.9),
            });

        Assert.True(resolved[0].IsDisplayable);
    }

    [Fact]
    public void FrontMatterIsAPrefixAndBackMatterASuffix()
    {
        // The invariant the window planner guarantees, enforced in code (§8.2). A classifier that
        // put front matter in the middle of the document produced an answer the document's shape
        // rules out.
        var presentations = new List<ParagraphPresentation>
        {
            ParagraphPresentation.Metadata("P00001", MetadataClass.FrontMatter, [], 0.9),
            ParagraphPresentation.Displayable("P00002"),
            ParagraphPresentation.Metadata("P00003", MetadataClass.FrontMatter, [], 0.9),
            ParagraphPresentation.Displayable("P00004"),
            ParagraphPresentation.Metadata("P00005", MetadataClass.BackMatter, [], 0.9),
        };

        var enforced = PresentationCandidates.EnforceEdges(presentations);

        Assert.False(enforced[0].IsDisplayable);
        Assert.True(enforced[2].IsDisplayable);   // taken back: not in the prefix
        Assert.False(enforced[4].IsDisplayable);
    }

    [Fact]
    public void ExclusionIsNeverDeletionAndIntegrityStillAccountsForEveryParagraph()
    {
        // §5.3. "Removed" means absent from the scene stream; the frozen record keeps every
        // paragraph with its verdict, which is what lets text-integrity stay green.
        var paragraphs = Document(
            ("P00001", "© 2026 Ultra Motion Press."),
            ("P00002", "The work itself begins here."));

        var presentations = new List<ParagraphPresentation>
        {
            ParagraphPresentation.Metadata("P00001", MetadataClass.FrontMatter, ["P00001"], 0.9),
            ParagraphPresentation.Displayable("P00002"),
        };

        Assert.True(SceneChecks.CheckPresentationIntegrity(paragraphs, presentations).Passed);
        Assert.Equal(2, presentations.Count);
    }

    [Fact]
    public void AParagraphWithNoVerdictFailsIntegrity()
    {
        var paragraphs = Document(("P00001", "One."), ("P00002", "Two."));

        var result = SceneChecks.CheckPresentationIntegrity(
            paragraphs, [ParagraphPresentation.Displayable("P00001")]);

        Assert.False(result.Passed);
        Assert.Contains("absence is not a class", result.ErrorReport, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("VANCE: We are not finished. REED: I think we are.", CompositionFamily.Transcript)]
    [InlineData("Consider the following. Note that you will need the value of x for each step you take.", CompositionFamily.Textbook)]
    public void TheCompositionFamilyIsReConfirmedFromParagraphEvidence(string text, string expected)
    {
        // Phase 0 guesses from converter metadata and line shapes; this is the first point at which
        // dialogue ratio, tense and person mean anything (§6, §8.5 step 5).
        var evidence = FamilyEvidenceExtractor.Extract([SceneFixtures.Paragraph("P00001", text)]);

        Assert.Equal(expected, FamilyEvidenceExtractor.Confirm(evidence, CompositionFamily.Unknown));
    }
}
