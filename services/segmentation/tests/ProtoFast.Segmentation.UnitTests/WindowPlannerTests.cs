using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Windowing;

namespace ProtoFast.Segmentation.UnitTests;

/// <summary>
/// The planner's two invariants are what make label merging a total function (plan §10.1), so
/// they are tested directly rather than through the pipeline.
/// </summary>
public class WindowPlannerTests
{
    [Fact]
    public void EveryLineInTheRegionIsCommittedByExactlyOneWindow()
    {
        var lines = Lines(1000);
        var options = Fixtures.Options();
        options.Windowing.MaxLines = 100;

        var windows = new WindowPlanner(options).Plan(
            lines, new Region(0, lines.Count, true, []), new HashSet<string>());

        var commitCounts = lines.ToDictionary(l => l.LineId, _ => 0, StringComparer.Ordinal);
        foreach (var window in windows)
        {
            for (var i = window.CommitStart; i < window.CommitEndExclusive; i++)
            {
                commitCounts[lines[i].LineId]++;
            }
        }

        Assert.All(commitCounts, kv => Assert.Equal(1, kv.Value));
    }

    [Fact]
    public void NoWindowCrossesATrustedBoundary()
    {
        var lines = Lines(300);
        var trusted = new HashSet<string>(StringComparer.Ordinal) { lines[120].LineId, lines[240].LineId };
        var options = Fixtures.Options();
        options.Windowing.MaxLines = 100;

        var windows = new WindowPlanner(options).Plan(lines, new Region(0, lines.Count, true, []), trusted);

        foreach (var window in windows)
        {
            for (var i = window.CommitStart + 1; i < window.CommitEndExclusive; i++)
            {
                Assert.DoesNotContain(lines[i].LineId, trusted);
            }
        }
    }

    [Fact]
    public void WindowsCarryOverlapContextOnBothSides()
    {
        var lines = Lines(400);
        var options = Fixtures.Options();
        options.Windowing.MaxLines = 100;

        var windows = new WindowPlanner(options).Plan(lines, new Region(0, lines.Count, true, []), new HashSet<string>());

        Assert.True(windows.Count >= 4);
        var middle = windows[1];
        Assert.True(middle.StartIndex < middle.CommitStart, "no leading context");
        Assert.True(middle.EndIndexExclusive > middle.CommitEndExclusive, "no trailing context");
    }

    [Fact]
    public void ChunksAreBalancedRatherThanGreedy()
    {
        // 210 lines with a 200 ceiling: two windows of ~105, not 200 + a runt of 10 whose
        // context would outweigh its content.
        var lines = Lines(210);
        var windows = new WindowPlanner(Fixtures.Options()).Plan(
            lines, new Region(0, lines.Count, true, []), new HashSet<string>());

        Assert.Equal(2, windows.Count);
        var sizes = windows.Select(w => w.CommitEndExclusive - w.CommitStart).ToList();
        Assert.True(sizes.Max() - sizes.Min() <= 1, $"unbalanced: {string.Join(", ", sizes)}");
    }

    [Fact]
    public void AShortRegionBecomesASingleWindow()
    {
        var lines = Lines(20);
        var windows = new WindowPlanner(Fixtures.Options()).Plan(
            lines, new Region(0, lines.Count, true, []), new HashSet<string>());

        Assert.Single(windows);
        Assert.Equal(0, windows[0].CommitStart);
        Assert.Equal(20, windows[0].CommitEndExclusive);
    }

    [Fact]
    public void PlanAllNumbersWindowsContinuouslyAcrossRegions()
    {
        var lines = Lines(600);
        var options = Fixtures.Options();
        options.Windowing.MaxLines = 100;

        var windows = new WindowPlanner(options).PlanAll(
            lines,
            [new Region(0, 250, true, []), new Region(300, 600, true, [])],
            new HashSet<string>());

        Assert.Equal(Enumerable.Range(0, windows.Count), windows.Select(w => w.WindowIndex));
    }

    [Fact]
    public void TheTokenBudgetShrinksWindowsForLongLines()
    {
        var longLines = Enumerable.Range(0, 200)
            .Select(i => new LineRecord(Ids.Line(i), new string('x', 2000), null, SourceHint.None, null))
            .ToList();

        var options = Fixtures.Options();
        options.Windowing.MaxInputTokens = 6000;

        var windows = new WindowPlanner(options).Plan(
            longLines, new Region(0, longLines.Count, true, []), new HashSet<string>());

        Assert.True(windows.Count > 1, "a 400k-character region must not become one window");
    }

    private static List<LineRecord> Lines(int count) =>
    [
        .. Enumerable.Range(0, count).Select(i =>
            new LineRecord(Ids.Line(i), $"line {i} with some representative body text", null, SourceHint.None, null)),
    ];
}
