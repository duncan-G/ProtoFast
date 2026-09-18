using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Windowing;

namespace ProtoFast.Segmentation.UnitTests;

/// <summary>
/// The structure planner's invariant is the one the assembly step rests on: <b>every skeleton
/// entry is committed by exactly one window</b>. Without it the orchestrator could be handed the
/// same paragraph twice and the materializer's coverage assertion would be unsatisfiable.
/// </summary>
public class StructureWindowPlannerTests
{
    [Fact]
    public void EveryEntryIsCommittedByExactlyOneWindow()
    {
        var entries = Skeleton(500, headingEvery: 37);
        var windows = StructureWindowPlanner.Plan(entries, Headings(entries), overlapEntries: 20, maxEntriesPerWindow: 60);

        var committed = windows.SelectMany(w => w.CommitEntries.Select(e => e.Id)).ToList();

        Assert.Equal(entries.Select(e => e.Id), committed);
        Assert.Equal(entries.Count, committed.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ContextIsTheTailOfWhatCameBeforeAndIsNeverCommitted()
    {
        var entries = Skeleton(300, headingEvery: 0);
        var windows = StructureWindowPlanner.Plan(entries, [], overlapEntries: 10, maxEntriesPerWindow: 100);

        Assert.Equal(3, windows.Count);
        Assert.Empty(windows[0].ContextEntries);

        for (var i = 1; i < windows.Count; i++)
        {
            var previous = windows[i - 1].CommitEntries;
            Assert.Equal(10, windows[i].ContextEntries.Count);
            Assert.Equal(
                previous.TakeLast(10).Select(e => e.Id),
                windows[i].ContextEntries.Select(e => e.Id));
        }

        // Context reaches backwards only. A window that could also see what follows would be able
        // to commit a heading twice — once as its own, once as the next window's context.
        var committed = windows.SelectMany(w => w.CommitEntries.Select(e => e.Id)).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(entries.Count, committed.Count);
    }

    [Fact]
    public void CutsFallAtTopLevelHeadings()
    {
        var entries = Skeleton(90, headingEvery: 30);
        var headings = Headings(entries);

        var windows = StructureWindowPlanner.Plan(entries, headings, overlapEntries: 0, maxEntriesPerWindow: 1_000);

        Assert.Equal(3, windows.Count);
        Assert.All(windows, w => Assert.True(w.CommitEntries[0].IsHeading));
    }

    [Fact]
    public void AHeadinglessDocumentFallsBackToFixedSizeChunks()
    {
        var entries = Skeleton(250, headingEvery: 0);

        var windows = StructureWindowPlanner.Plan(entries, [], overlapEntries: 0, maxEntriesPerWindow: 100);

        Assert.Equal([100, 100, 50], windows.Select(w => w.CommitEntries.Count));
    }

    [Fact]
    public void ASkeletonThatFitsIsOneWindowWithNoContext()
    {
        var entries = Skeleton(40, headingEvery: 0);

        var windows = StructureWindowPlanner.Plan(entries, [], overlapEntries: 20);

        Assert.Single(windows);
        Assert.Empty(windows[0].ContextEntries);
        Assert.Equal(40, windows[0].CommitEntries.Count);
    }

    [Fact]
    public void TheRenderedWindowSeparatesContextFromWhatIsOwned()
    {
        var entries = Skeleton(60, headingEvery: 0);
        var windows = StructureWindowPlanner.Plan(entries, [], overlapEntries: 5, maxEntriesPerWindow: 30);

        var rendered = StructureWindowPlanner.Render(windows[1]);

        // An agent shown two undifferentiated blocks will structure both, and half its answer
        // would then be another window's to give.
        Assert.Contains("Read only", rendered, StringComparison.Ordinal);
        Assert.Contains("Your window", rendered, StringComparison.Ordinal);
        Assert.StartsWith("# Context", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void CommittedIdsAreScopedToTheWindow()
    {
        var entries = Skeleton(60, headingEvery: 20);
        var windows = StructureWindowPlanner.Plan(entries, Headings(entries), overlapEntries: 5);

        foreach (var window in windows)
        {
            Assert.Equal(
                window.CommitEntries.Where(e => !e.IsHeading).Select(e => e.Id),
                window.CommittedParagraphIds);
            Assert.Equal(
                window.CommitEntries.Where(e => e.IsHeading).Select(e => e.Id).ToHashSet(StringComparer.Ordinal),
                window.CommittedHeadingLineIds);
        }
    }

    private static List<SkeletonBuilder.SkeletonEntry> Skeleton(int count, int headingEvery)
    {
        var entries = new List<SkeletonBuilder.SkeletonEntry>(count);
        var paragraphs = 0;

        for (var i = 0; i < count; i++)
        {
            var isHeading = headingEvery > 0 && i % headingEvery == 0;

            entries.Add(new SkeletonBuilder.SkeletonEntry(
                IsHeading: isHeading,
                Id: isHeading ? Ids.Line(i) : Ids.Paragraph(paragraphs++),
                Level: isHeading ? 1 : null,
                Confidence: 1,
                Source: BoundarySource.Deterministic,
                WordCount: 20,
                Kind: ParagraphKind.Body,
                Excerpt: $"entry {i}"));
        }

        return entries;
    }

    private static List<HeadingRecord> Headings(List<SkeletonBuilder.SkeletonEntry> entries) =>
    [
        .. entries
            .Where(e => e.IsHeading)
            .Select(e => new HeadingRecord(e.Id, e.Excerpt, 1, 1, BoundarySource.Deterministic, null)),
    ];
}
