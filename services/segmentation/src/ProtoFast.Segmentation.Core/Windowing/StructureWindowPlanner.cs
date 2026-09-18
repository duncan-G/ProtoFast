using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Core.Windowing;

/// <summary>
/// One structuring unit: <see cref="ContextEntries"/> is shown, <see cref="CommitEntries"/> is
/// what the window's answer is kept for.
///
/// <para>The split mirrors <see cref="LabelWindow"/>'s commit region for the same reason: a
/// section that began before this window is invisible without context, and an agent that can see
/// the previous window's tail can say "this continues something" instead of inventing a title for
/// a fragment. Context is never committed, so the document's entries are still partitioned.</para>
/// </summary>
public sealed record StructureWindow(
    int WindowIndex,
    IReadOnlyList<SkeletonBuilder.SkeletonEntry> ContextEntries,
    IReadOnlyList<SkeletonBuilder.SkeletonEntry> CommitEntries)
{
    /// <summary>Paragraph ids this window is answerable for — the coverage scope of its reply.</summary>
    public IReadOnlyList<string> CommittedParagraphIds =>
        [.. CommitEntries.Where(e => !e.IsHeading).Select(e => e.Id)];

    public IReadOnlySet<string> CommittedHeadingLineIds =>
        CommitEntries.Where(e => e.IsHeading).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

    public string FirstEntryId => CommitEntries.Count > 0 ? CommitEntries[0].Id : string.Empty;

    public string LastEntryId => CommitEntries.Count > 0 ? CommitEntries[^1].Id : string.Empty;
}

/// <summary>
/// Cuts a skeleton into structuring windows (orchestrator plan §4.4).
///
/// <para>This is <c>StructurerAgent.SplitSkeleton</c> lifted into Core so both strategies use one
/// splitter, plus the thing the original lacked: overlap context. Cuts fall at level-1 headings
/// and, failing that, at a fixed entry cap — the plan suggests embedding-based cut points for a
/// headingless document (§9.7) and this deliberately does not, because a wrong cut is repaired by
/// the assembly step either way and an embedding call per paragraph is a real cost on exactly the
/// documents that are already the most expensive.</para>
/// </summary>
public static class StructureWindowPlanner
{
    /// <summary>
    /// Skeleton entries per structuring call. Chosen so a window's rendered skeleton stays well
    /// inside a large model's context with room for the tree it has to return, which is several
    /// times larger than the skeleton itself.
    /// </summary>
    public const int MaxEntriesPerWindow = 1_200;

    public static IReadOnlyList<StructureWindow> Plan(
        IReadOnlyList<SkeletonBuilder.SkeletonEntry> entries,
        IReadOnlyList<HeadingRecord> headings,
        int overlapEntries = 0,
        int maxEntriesPerWindow = MaxEntriesPerWindow)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(headings);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntriesPerWindow, 1);

        var commits = SplitCommitRegions(entries, headings, maxEntriesPerWindow);
        var windows = new List<StructureWindow>(commits.Count);
        var consumed = 0;

        foreach (var commit in commits)
        {
            // Context reaches backwards only. A window that could also see what follows would be
            // able to commit a heading twice — once as its own and once as the next window's
            // context — and the partition is the whole reason the assembly step can assert
            // coverage rather than check it.
            var contextStart = Math.Max(0, consumed - Math.Max(0, overlapEntries));

            windows.Add(new StructureWindow(
                WindowIndex: windows.Count,
                ContextEntries: [.. entries.Skip(contextStart).Take(consumed - contextStart)],
                CommitEntries: commit));

            consumed += commit.Count;
        }

        return windows;
    }

    private static List<List<SkeletonBuilder.SkeletonEntry>> SplitCommitRegions(
        IReadOnlyList<SkeletonBuilder.SkeletonEntry> entries,
        IReadOnlyList<HeadingRecord> headings,
        int maxEntriesPerWindow)
    {
        var topLevel = headings
            .Where(h => h.Level is null or 1)
            .Select(h => h.LineId)
            .ToHashSet(StringComparer.Ordinal);

        var parts = new List<List<SkeletonBuilder.SkeletonEntry>>();
        var current = new List<SkeletonBuilder.SkeletonEntry>();

        foreach (var entry in entries)
        {
            var atTopLevelHeading = entry.IsHeading && topLevel.Contains(entry.Id);
            var atCapacity = current.Count >= maxEntriesPerWindow;

            if (current.Count > 0 && (atTopLevelHeading || atCapacity))
            {
                parts.Add(current);
                current = [];
            }

            current.Add(entry);
        }

        if (current.Count > 0)
        {
            parts.Add(current);
        }

        return parts;
    }

    /// <summary>
    /// Renders a window for a prompt: the context entries marked as read-only, then the entries
    /// this window owns. The marker matters — an agent shown two undifferentiated blocks will
    /// structure both, and half its answer would then be another window's to give.
    /// </summary>
    public static string Render(StructureWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.ContextEntries.Count == 0)
        {
            return SkeletonBuilder.Render(window.CommitEntries);
        }

        return "# Context — the tail of the previous window. Read only; do not place these.\n"
            + SkeletonBuilder.Render(window.ContextEntries)
            + "\n# Your window — place every entry below, and only these.\n"
            + SkeletonBuilder.Render(window.CommitEntries);
    }
}
