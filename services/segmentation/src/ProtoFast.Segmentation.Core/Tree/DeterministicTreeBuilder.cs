using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Core.Tree;

/// <summary>
/// Builds the section tree from trusted headings alone, with no model involved.
///
/// <para>The plan's principle is "trust existing structure" (§1): where the source already has a
/// coherent heading hierarchy and triage found nothing suspect, asking a large model to re-derive
/// it is spend with no upside and a small chance of making it worse. <see cref="IsApplicable"/>
/// decides; everything else goes to the structurer.</para>
///
/// <para>It is also what makes the plan's local-development promise true (§22.1): a clean
/// Markdown fixture runs the whole pipeline end to end with no provider key at all.</para>
/// </summary>
public static class DeterministicTreeBuilder
{
    /// <summary>
    /// Applicable when every paragraph sits under a heading whose level is known, and there is at
    /// least one heading. A document with no headings genuinely needs inference — that is the
    /// structurer's job, not this one's.
    /// </summary>
    public static bool IsApplicable(IReadOnlyList<HeadingRecord> headings) =>
        headings.Count > 0 && headings.All(h => h.Level is > 0);

    public static SectionNode Build(
        IReadOnlyList<Paragraph> paragraphs,
        IReadOnlyList<HeadingRecord> headings,
        string documentTitle = "Document")
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        ArgumentNullException.ThrowIfNull(headings);

        var entries = SkeletonBuilder.Build(paragraphs, headings);
        var counter = 0;

        var root = new MutableNode(Ids.Section(counter++), documentTitle, TitleInferred: true, null, HeadingLevel: 0);
        var stack = new List<MutableNode> { root };

        foreach (var entry in entries)
        {
            if (entry.IsHeading)
            {
                var level = Math.Clamp(entry.Level ?? 1, 1, 6);
                while (stack.Count > 1 && stack[^1].HeadingLevel >= level)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                var node = new MutableNode(Ids.Section(counter++), entry.Excerpt, TitleInferred: false, entry.Id, level);
                stack[^1].Children.Add(node);
                stack.Add(node);
                continue;
            }

            stack[^1].ParagraphIds.Add(entry.Id);
        }

        // Paragraphs that precede the first heading, or that sit beside child sections, are wrapped
        // in an inferred "Overview" — the children-xor-paragraphs rule of plan §11 admits no node
        // that holds both, and the plan names this exact remedy (§10.2).
        WrapLooseParagraphs(root, ref counter);
        PruneEmpty(root);

        // A document whose whole content sits under one top-level heading does not need a
        // synthetic root above it: that heading IS the document's title. Collapsing the wrapper
        // keeps the tree one level shallower and gives the root a real, non-inferred title —
        // which ThePlot renders differently from a title the pipeline made up.
        var collapsed = root.Children is [{ ParagraphIds.Count: 0 } only] && root.ParagraphIds.Count == 0
            ? only
            : root;

        return collapsed.Freeze(depth: 1);
    }

    private static void WrapLooseParagraphs(MutableNode node, ref int counter)
    {
        if (node.Children.Count > 0 && node.ParagraphIds.Count > 0)
        {
            var overview = new MutableNode(Ids.Section(counter++), "Overview", TitleInferred: true, null, node.HeadingLevel + 1);
            overview.ParagraphIds.AddRange(node.ParagraphIds);
            node.ParagraphIds.Clear();
            node.Children.Insert(0, overview);
        }

        foreach (var child in node.Children)
        {
            WrapLooseParagraphs(child, ref counter);
        }
    }

    /// <summary>Drops sections that ended up with neither children nor paragraphs.</summary>
    private static void PruneEmpty(MutableNode node)
    {
        foreach (var child in node.Children)
        {
            PruneEmpty(child);
        }

        node.Children.RemoveAll(c => c.Children.Count == 0 && c.ParagraphIds.Count == 0);
    }

    /// <summary>
    /// <paramref name="HeadingLevel"/> is the source heading's own level and drives nesting while
    /// the tree is being built. The frozen node's <c>Level</c> is its <em>depth</em> instead,
    /// because that is what the <c>tree-shape</c> check compares and what the client renders. The
    /// two differ whenever a document skips a level or a wrapper section is inserted, so they are
    /// kept as separate fields rather than one field that means different things at different
    /// times.
    /// </summary>
    private sealed record MutableNode(
        string SectionId, string Title, bool TitleInferred, string? HeadingLineId, int HeadingLevel)
    {
        public List<MutableNode> Children { get; } = [];

        public List<string> ParagraphIds { get; } = [];

        public SectionNode Freeze(int depth) => new(
            SectionId, Title, TitleInferred, HeadingLineId, Math.Min(depth, 6),
            [.. Children.Select(c => c.Freeze(depth + 1))],
            [.. ParagraphIds]);
    }
}
