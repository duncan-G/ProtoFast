using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Core.Tree;

/// <summary>
/// Turns a <see cref="TreeProposal"/> into a <see cref="SectionNode"/> tree with section ids
/// minted by code (plan §8.5) — the model returns titles and references, never identifiers.
/// Ids are assigned in document order so <c>S0001</c> is always the first section a reader sees.
/// </summary>
public static class TreeMaterializer
{
    public static SectionNode Materialize(TreeProposalNode proposal, string rootTitle = "Document")
    {
        ArgumentNullException.ThrowIfNull(proposal);
        var counter = 0;
        return Convert(proposal, depth: 1, ref counter, rootTitle);
    }

    private static SectionNode Convert(TreeProposalNode node, int depth, ref int counter, string? titleOverride = null)
    {
        var sectionId = Ids.Section(counter++);
        var children = new List<SectionNode>();

        foreach (var child in node.Children ?? [])
        {
            children.Add(Convert(child, depth + 1, ref counter));
        }

        return new SectionNode(
            SectionId: sectionId,
            Title: titleOverride ?? (string.IsNullOrWhiteSpace(node.Title) ? "Untitled" : node.Title.Trim()),
            TitleInferred: node.Inferred,
            HeadingLineId: string.IsNullOrWhiteSpace(node.HeadingLineId) ? null : node.HeadingLineId,
            // A node's Level IS its depth. The model's own level claim is advisory — it shapes
            // the nesting it proposed, and the nesting is what survives — so it is dropped here
            // rather than carried forward to contradict the tree it came in.
            Level: Math.Min(depth, 6),
            Children: children,
            ParagraphIds: [.. node.Paragraphs ?? []]);
    }

    public static IReadOnlyList<ParagraphEdit> ToEdits(TreeProposal proposal) =>
    [
        .. proposal.ParagraphEdits.Select(e =>
            new ParagraphEdit(e.Op, e.ParagraphId, e.WithParagraphId, e.BeforeSentence, e.Reason)),
    ];
}
