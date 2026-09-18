using System.Globalization;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Validation;

namespace ProtoFast.Segmentation.Core.Tree;

/// <summary>The assembled tree, or the exact reason the plan could not be applied.</summary>
public sealed record MaterializationResult(SectionNode? Root, ValidationResult Validation)
{
    public bool Success => Validation.Passed && Root is not null;
}

/// <summary>
/// Applies an <see cref="AssemblyPlan"/> to the window agents' subtrees (orchestrator plan §4.3).
///
/// <para>Pure code, and deliberately the only thing in the orchestration that can produce a tree.
/// The consequence is worth stating plainly: paragraph coverage becomes a property of this class
/// rather than a check that can fail downstream. Today's <c>id-coverage</c> catches a structurer
/// that dropped a paragraph after the fact and pays a repair round for it; here a plan that would
/// drop one cannot be materialized, and the error names the orphaned range so the orchestrator's
/// repair round has something to act on.</para>
///
/// <para>The orchestrator therefore never emits a paragraph id and never emits paragraph text —
/// it composes references to nodes the window agents already produced. That strengthens the
/// <c>text-integrity</c> invariant rather than weakening it, which is the point of adding an agent
/// here at all.</para>
/// </summary>
public static class OrchestratedTreeMaterializer
{
    /// <summary>
    /// Indexes one window's subtree by reference, in document order. <c>n0</c> is the window's own
    /// synthetic root, which a plan is expected never to name — dropping that wrapper is what
    /// <c>StructureInPartsAsync</c> does blindly today and what a plan does deliberately.
    /// </summary>
    public static IReadOnlyDictionary<string, SectionNode> Index(int windowIndex, SectionNode subtree)
    {
        ArgumentNullException.ThrowIfNull(subtree);

        var map = new Dictionary<string, SectionNode>(StringComparer.Ordinal);
        var counter = 0;

        foreach (var node in subtree.Descend())
        {
            map[NodeRefs.For(windowIndex, counter++)] = node;
        }

        return map;
    }

    public static MaterializationResult Apply(
        AssemblyPlan plan,
        IReadOnlyDictionary<int, SectionNode> windowSubtrees,
        IReadOnlyList<string> expectedParagraphIds,
        string documentTitle)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(windowSubtrees);
        ArgumentNullException.ThrowIfNull(expectedParagraphIds);

        var index = new Dictionary<string, SectionNode>(StringComparer.Ordinal);

        // By reference, not by value. SectionNode is a record, so two windows that produced the
        // same title and the same shape would be the same key under structural equality — and
        // "is this node already placed?" would then be answered about the wrong node.
        var refOf = new Dictionary<SectionNode, string>(ByReference.Instance);

        foreach (var (windowIndex, subtree) in windowSubtrees.OrderBy(kv => kv.Key))
        {
            foreach (var (reference, node) in Index(windowIndex, subtree))
            {
                index[reference] = node;
                refOf.TryAdd(node, reference);
            }
        }

        var errors = new List<string>();
        var resolved = Resolve(plan.Outline, index, errors);

        if (errors.Count > 0)
        {
            return Failed(errors);
        }

        CheckNoNesting(resolved, refOf, errors);
        if (errors.Count > 0)
        {
            return Failed(errors);
        }

        var root = Build(resolved, documentTitle, errors);
        if (errors.Count > 0 || root is null)
        {
            return Failed(errors.Count > 0 ? errors : ["the plan produced no sections"]);
        }

        var placed = root.Descend().SelectMany(n => n.ParagraphIds).ToList();
        var coverage = Checks.CheckIdCoverage(expectedParagraphIds, placed);
        if (!coverage.Passed)
        {
            return new MaterializationResult(null, coverage);
        }

        var shape = Checks.CheckTreeShape(root);
        return shape.Passed
            ? new MaterializationResult(root, ValidationResult.Pass(Checks.TreeShape))
            : new MaterializationResult(null, shape);
    }

    private static MaterializationResult Failed(IReadOnlyList<string> errors) =>
        new(null, ValidationResult.Fail(AssemblyCheck, errors));

    /// <summary>The check id a failed assembly reports under; it is not one of the pipeline's checks.</summary>
    public const string AssemblyCheck = "assembly-plan";

    private sealed record ResolvedRow(int Index, int Depth, string Title, IReadOnlyList<SectionNode> Sources);

    private static List<ResolvedRow> Resolve(
        IReadOnlyList<AssemblyRow> outline,
        IReadOnlyDictionary<string, SectionNode> index,
        List<string> errors)
    {
        var rows = new List<ResolvedRow>(outline.Count);
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        var previousDepth = 0;

        for (var i = 0; i < outline.Count; i++)
        {
            var row = outline[i];
            var label = $"row {i + 1}";

            if (row.Depth < 1)
            {
                errors.Add($"{label}: depth {row.Depth} is below 1; the shallowest section is depth 1");
            }
            else if (row.Depth > previousDepth + 1)
            {
                errors.Add(
                    $"{label}: depth {row.Depth} follows depth {previousDepth}; a row may be at most one "
                    + "level deeper than the row above it");
            }

            var sources = new List<SectionNode>(row.Sources.Count);
            foreach (var reference in row.Sources)
            {
                if (!index.TryGetValue(reference, out var node))
                {
                    errors.Add(
                        $"{label}: '{reference}' is not a node any window returned — references are "
                        + "issued by the pipeline and may not be invented");
                    continue;
                }

                if (NodeRefs.TryParse(reference, out _, out var nodeIndex) && nodeIndex == 0)
                {
                    errors.Add(
                        $"{label}: '{reference}' is the window's own wrapper, not a section of the "
                        + "document — place the sections inside it instead");
                    continue;
                }

                if (used.TryGetValue(reference, out var firstRow))
                {
                    errors.Add($"{label}: '{reference}' was already placed by row {firstRow + 1}");
                    continue;
                }

                used[reference] = i;
                sources.Add(node);
            }

            if (sources.Count == 0 && string.IsNullOrWhiteSpace(row.Title))
            {
                errors.Add($"{label}: a row with no sources introduces a new parent and needs a title");
            }

            // A retitle replaces words the model chose. Where a window anchored the section to a
            // real heading, the title is the document's own and replacing it would make the node
            // claim an inferred title while pointing at a source line — which heading-anchor
            // rejects, and rightly: those two statements contradict each other.
            if (!string.IsNullOrWhiteSpace(row.Title) && sources.Count > 0 && sources[0].HeadingLineId is { } anchor)
            {
                errors.Add(
                    $"{label}: '{row.Sources[0]}' is anchored to heading '{anchor}', so its title is the "
                    + "document's own words and may not be replaced. Drop the title, or group this node "
                    + "under a new titled parent instead.");
            }

            rows.Add(new ResolvedRow(i, Math.Max(1, row.Depth), row.Title.Trim(), sources));
            previousDepth = Math.Max(1, row.Depth);
        }

        if (rows.Count == 0)
        {
            errors.Add("the outline is empty");
        }
        else if (rows[0].Depth != 1)
        {
            errors.Add("row 1 must be at depth 1");
        }

        return rows;
    }

    /// <summary>
    /// No placed node may be an ancestor or a descendant of another. Without this the coverage
    /// assertion below would be satisfiable twice over — a parent and its child both placed puts
    /// the same paragraphs in two sections, and the duplicate would be reported as a mystery
    /// rather than as the structural mistake it is.
    /// </summary>
    private static void CheckNoNesting(
        List<ResolvedRow> rows,
        Dictionary<SectionNode, string> refOf,
        List<string> errors)
    {
        var placed = new Dictionary<SectionNode, int>(
            rows.SelectMany(r => r.Sources.Select(s => KeyValuePair.Create(s, r.Index))),
            ByReference.Instance);

        foreach (var (node, rowIndex) in placed)
        {
            foreach (var descendant in node.Descend().Skip(1))
            {
                if (placed.TryGetValue(descendant, out var otherRow))
                {
                    errors.Add(
                        $"row {rowIndex + 1} places '{Name(refOf, node)}' and row {otherRow + 1} places "
                        + $"'{Name(refOf, descendant)}', which is inside it — place the parent or its "
                        + "children, never both");
                }
            }
        }
    }

    private static string Name(Dictionary<SectionNode, string> refOf, SectionNode node) =>
        refOf.TryGetValue(node, out var reference) ? reference : node.Title;

    private static SectionNode? Build(List<ResolvedRow> rows, string documentTitle, List<string> errors)
    {
        var root = new Mutable(documentTitle, titleInferred: true, headingLineId: null);
        var stack = new List<Mutable> { root };

        foreach (var row in rows)
        {
            while (stack.Count > row.Depth)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            var parent = stack[^1];

            if (parent.IsTerminal)
            {
                errors.Add(
                    $"row {row.Index + 1} nests under row {parent.RowLabel}, which already places a "
                    + "window node and carries its own subtree — nest under a titled parent instead");
                return null;
            }

            var node = row.Sources.Count == 0
                ? new Mutable(row.Title, titleInferred: true, headingLineId: null)
                {
                    RowLabel = (row.Index + 1).ToString(CultureInfo.InvariantCulture),
                }
                : Place(row);

            parent.Children.Add(node);
            stack.Add(node);
        }

        foreach (var node in root.Descend().Where(n => n.Children.Count == 0 && n.ParagraphIds.Count == 0))
        {
            errors.Add(
                $"row {node.RowLabel}: '{node.Title}' introduces a parent but nothing is nested under it — "
                + "a row with no sources needs at least one deeper row after it");
        }

        return errors.Count > 0 ? null : root.Freeze(depth: 1, new Counter());
    }

    /// <summary>
    /// Turns one row's sources into a single node. One source is a placement; several are the
    /// merge of orchestrator plan §4.3 — a section the windows split at a boundary, put back
    /// together in document order.
    /// </summary>
    private static Mutable Place(ResolvedRow row)
    {
        var first = row.Sources[0];
        var title = string.IsNullOrWhiteSpace(row.Title) ? first.Title : row.Title;
        var inferred = string.IsNullOrWhiteSpace(row.Title) ? first.TitleInferred : true;
        var node = new Mutable(title, inferred, first.HeadingLineId)
        {
            IsTerminal = true,
            RowLabel = (row.Index + 1).ToString(CultureInfo.InvariantCulture),
        };

        // All leaves is the case this exists for: the continuation of a section had no heading of
        // its own, so the two halves are one run of paragraphs. Anything else keeps each source's
        // own shape as a child, because flattening a titled subtree into a paragraph list would
        // throw away structure a window agent was right about.
        if (row.Sources.All(s => s.Children.Count == 0))
        {
            node.ParagraphIds.AddRange(row.Sources.SelectMany(s => s.ParagraphIds));
            return node;
        }

        foreach (var source in row.Sources)
        {
            if (source.Children.Count > 0)
            {
                node.Children.AddRange(source.Children.Select(Mutable.From));
                continue;
            }

            node.Children.Add(Mutable.From(source));
        }

        return node;
    }

    private sealed class Counter
    {
        public int Next { get; set; }
    }

    private sealed class ByReference : IEqualityComparer<SectionNode>
    {
        public static readonly ByReference Instance = new();

        public bool Equals(SectionNode? x, SectionNode? y) => ReferenceEquals(x, y);

        public int GetHashCode(SectionNode obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private sealed class Mutable(string title, bool titleInferred, string? headingLineId)
    {
        public string Title { get; } = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim();

        public bool TitleInferred { get; } = titleInferred;

        public string? HeadingLineId { get; } = headingLineId;

        public List<Mutable> Children { get; } = [];

        public List<string> ParagraphIds { get; } = [];

        /// <summary>Set on a node that came from a window; nothing may be nested under it.</summary>
        public bool IsTerminal { get; init; }

        public string RowLabel { get; init; } = "0";

        public static Mutable From(SectionNode node)
        {
            var copy = new Mutable(node.Title, node.TitleInferred, node.HeadingLineId) { IsTerminal = true };
            copy.Children.AddRange(node.Children.Select(From));
            copy.ParagraphIds.AddRange(node.ParagraphIds);
            return copy;
        }

        public IEnumerable<Mutable> Descend()
        {
            yield return this;
            foreach (var node in Children.SelectMany(c => c.Descend()))
            {
                yield return node;
            }
        }

        public SectionNode Freeze(int depth, Counter counter) => new(
            Ids.Section(counter.Next++),
            Title,
            TitleInferred,
            HeadingLineId,
            Math.Min(depth, 6),
            [.. Children.Select(c => c.Freeze(depth + 1, counter))],
            [.. ParagraphIds]);
    }
}
