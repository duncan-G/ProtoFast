using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Tree;
using ProtoFast.Segmentation.Core.Validation;

namespace ProtoFast.Segmentation.UnitTests;

public class StructureTests
{
    [Fact]
    public void ACleanDocumentProducesAValidTreeWithNoModelCalls()
    {
        // The plan's local-development promise (§22.1): a clean fixture runs end to end with no
        // provider at all. Everything below is deterministic code.
        const string markdown = """
            # Introduction

            This document describes the calibration procedure used throughout the study.

            # Methods

            ## Sampling

            We collected samples from three sites between March and June of the study year.

            ## Analysis

            Each sample was analysed in triplicate using the reference protocol.
            """;

        var run = Fixtures.Run(markdown);
        var root = DeterministicTreeBuilder.Build(run.Assembly.Paragraphs, run.Assembly.Headings);

        Assert.True(run.Triage.CanSkipLabeling, "a clean document should need no labeling");

        var report = Checks.CheckAll(
            run.Cleaning, run.Labels, run.Assembly.Paragraphs, run.Assembly.Headings,
            root, new Core.Options.ParagraphOptions { MinWords = 1, MaxWords = 1000 });

        Assert.True(report.Passed, report.ErrorReport);
    }

    [Fact]
    public void LooseParagraphsBesideChildSectionsAreWrappedInAnOverview()
    {
        // "Methods" holds both a paragraph and two subsections, which the children-xor-paragraphs
        // rule forbids. The builder's answer is the plan's: an inferred Overview.
        const string markdown = """
            # Methods

            A preamble paragraph that belongs to Methods itself rather than to any subsection.

            ## Sampling

            Sampling paragraph.

            ## Analysis

            Analysis paragraph.
            """;

        var run = Fixtures.Run(markdown);
        var root = DeterministicTreeBuilder.Build(run.Assembly.Paragraphs, run.Assembly.Headings);

        var methods = root.Descend().Single(n => n.Title == "Methods");
        Assert.Equal(3, methods.Children.Count);
        Assert.Equal("Overview", methods.Children[0].Title);
        Assert.True(methods.Children[0].TitleInferred);
        Assert.Empty(methods.ParagraphIds);
        Assert.True(Checks.CheckTreeShape(root).Passed);
    }

    [Fact]
    public void NumberedHeadingsGetLevelsFromTheirNumberingDepth()
    {
        var headings = new List<HeadingRecord>
        {
            new("L000000", "1 Introduction", null, 1, BoundarySource.Trusted, null),
            new("L000010", "1.1 Background", null, 1, BoundarySource.Trusted, null),
            new("L000020", "1.1.1 Prior work", null, 1, BoundarySource.Trusted, null),
            new("L000030", "2 Methods", null, 1, BoundarySource.Trusted, null),
        };

        var assigned = HeadingLevelAssigner.Assign(headings);

        Assert.Equal([1, 2, 3, 1], assigned.Select(h => h.Level));
        Assert.False(HeadingLevelAssigner.NeedsModel(headings));
    }

    [Fact]
    public void FontScaleTiersBecomeLevelsWhenThereIsNoNumbering()
    {
        var headings = new List<HeadingRecord>
        {
            new("L000000", "Introduction", null, 1, BoundarySource.Trusted, Layout(1.8)),
            new("L000010", "Background", null, 1, BoundarySource.Trusted, Layout(1.4)),
            new("L000020", "Methods", null, 1, BoundarySource.Trusted, Layout(1.8)),
        };

        Assert.Equal([1, 2, 1], HeadingLevelAssigner.Assign(headings).Select(h => h.Level));
    }

    [Fact]
    public void LevelGapsAreClosedSoTreeShapeCanPass()
    {
        // A document that jumps h1 -> h4 is not describing four levels of hierarchy.
        var headings = new List<HeadingRecord>
        {
            new("L000000", "Top", 1, 1, BoundarySource.Trusted, null),
            new("L000010", "Deep", 4, 1, BoundarySource.Trusted, null),
        };

        Assert.Equal([1, 2], HeadingLevelAssigner.Normalize(headings).Select(h => h.Level));
    }

    [Fact]
    public void ALevellessUnlaidOutDocumentIsSentToTheModel()
    {
        var headings = new List<HeadingRecord>
        {
            new("L000000", "Introduction", 1, 1, BoundarySource.Llm, null),
            new("L000010", "Background", 1, 1, BoundarySource.Llm, null),
        };

        Assert.True(HeadingLevelAssigner.NeedsModel(headings));
    }

    [Fact]
    public void AModelProposalIsMaterializedWithPipelineIssuedSectionIds()
    {
        var proposal = new TreeProposalNode
        {
            Title = "Document",
            Children =
            [
                new TreeProposalNode { Title = "Methods", HeadingLineId = "L000014", Level = 1, Children =
                [
                    new TreeProposalNode { Title = "Overview", Inferred = true, Paragraphs = ["P00031", "P00032"] },
                ] },
            ],
        };

        var root = TreeMaterializer.Materialize(proposal);

        Assert.Equal("S0000", root.SectionId);
        Assert.Equal(["S0000", "S0001", "S0002"], root.Descend().Select(n => n.SectionId));
        // Depth wins over the model's own level claim, because tree-shape checks depth.
        Assert.Equal([1, 2, 3], root.Descend().Select(n => n.Level));
    }

    [Fact]
    public void TheTreeHashIsStableAcrossEquivalentTreesAndChangesWithStructure()
    {
        var a = DeterministicTreeBuilder.Build(Fixtures.Run(Sample).Assembly.Paragraphs, Fixtures.Run(Sample).Assembly.Headings);
        var b = DeterministicTreeBuilder.Build(Fixtures.Run(Sample).Assembly.Paragraphs, Fixtures.Run(Sample).Assembly.Headings);

        Assert.Equal(TreeCanonicalizer.Hash(a), TreeCanonicalizer.Hash(b));

        var moved = a with { Title = "Different" };
        Assert.NotEqual(TreeCanonicalizer.Hash(a), TreeCanonicalizer.Hash(moved));
    }

    [Fact]
    public void SentenceSplittingSurvivesAbbreviationsDecimalsAndInitials()
    {
        var sentences = SentenceSplitter.Split(
            "Dr. Smith measured 3.14 units. J. Doe confirmed the result, e.g. in trial two. Done!");

        Assert.Equal(3, sentences.Count);
        Assert.Equal("Dr. Smith measured 3.14 units.", sentences[0]);
        Assert.Equal("Done!", sentences[2]);
    }

    [Fact]
    public void SplitAtRejectsAnOutOfRangeSentenceIndex()
    {
        Assert.Null(SentenceSplitter.SplitAt("One. Two.", 5));
        Assert.Null(SentenceSplitter.SplitAt("One. Two.", 1));
        Assert.Equal(("One.", "Two."), SentenceSplitter.SplitAt("One. Two.", 2));
    }

    [Fact]
    public void RunIdsSortByCreationTime()
    {
        var earlier = Ids.NewRunId(DateTimeOffset.UnixEpoch.AddSeconds(1), new byte[10]);
        var later = Ids.NewRunId(DateTimeOffset.UnixEpoch.AddSeconds(2), new byte[10]);

        Assert.Equal(26, earlier.Length);
        Assert.True(string.CompareOrdinal(earlier, later) < 0);
    }

    [Fact]
    public void IdFormatsAreValidatedSoInventedIdsCanBeRejected()
    {
        Assert.True(Ids.IsLineId("L000412"));
        Assert.False(Ids.IsLineId("L412"));
        Assert.True(Ids.IsParagraphId("P00031a"));
        Assert.False(Ids.IsParagraphId("paragraph-31"));
        Assert.Equal("P00031b", Ids.SplitChild("P00031", 1));
    }

    private const string Sample = "# A\n\nFirst paragraph.\n\n# B\n\nSecond paragraph.\n";

    private static LayoutFeatures Layout(double fontScale) =>
        new(1, 0.5, 0, 0.4, 2.0, fontScale, true, false, 0);
}
