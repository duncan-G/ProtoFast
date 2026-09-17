namespace ProtoFast.Segmentation.Core.Model;

/// <summary>
/// Per-line page geometry from the upstream converter, already normalized against the
/// document's own statistics (plan §9.2 step 5) so every field is comparable across
/// documents. All of it is optional: a plain Markdown upload has no layout at all.
/// </summary>
/// <param name="Page">1-based page number.</param>
/// <param name="Top">Vertical position, 0..1 of page height.</param>
/// <param name="Indent">Left offset relative to the column edge, 0..1 of column width.</param>
/// <param name="Width">Line width relative to column width. Below ~0.8 often ends a paragraph.</param>
/// <param name="GapAbove">Space above, over the document's median line spacing.</param>
/// <param name="FontScale">Font size over the document's body font size.</param>
public sealed record LayoutFeatures(
    int Page,
    double Top,
    double Indent,
    double Width,
    double GapAbove,
    double FontScale,
    bool IsBold,
    bool IsItalic,
    int ColumnIndex);

/// <summary>
/// The smallest unit the pipeline works in: one visual line or layout block from conversion.
/// <see cref="LineId"/> is issued by <c>Ids</c> and is stable for the life of the run — every
/// model output refers to lines by this id and never by text (plan §8.5).
/// </summary>
public sealed record LineRecord(
    string LineId,
    string Text,
    LayoutFeatures? Layout,
    SourceHint Hint,
    int? HeadingLevelHint);

/// <summary>One agent's judgement about one line. Confidence drives the follow-up round (plan §10.1).</summary>
public sealed record LineLabelResult(
    string LineId,
    LineLabel Label,
    int? HeadingLevel,
    double Confidence,
    OtherKind OtherKind = OtherKind.None);

/// <summary>A boundary sits immediately <em>before</em> <paramref name="BeforeLineId"/>.</summary>
public sealed record Boundary(
    string BeforeLineId,
    BoundaryKind Kind,
    BoundarySource Source,
    double Confidence)
{
    /// <summary>Trusted and human boundaries survive every repair pass (check <c>trusted-respect</c>).</summary>
    public bool IsImmovable => Source is BoundarySource.Trusted or BoundarySource.Human;
}

/// <summary>
/// The leaf unit of the tree and of augmentation. <see cref="Text"/> is joined from cleaned
/// source lines by code — it never comes back through a model (plan §1), which is what makes
/// the <c>text-integrity</c> check a real guarantee rather than a hope.
/// </summary>
public sealed record Paragraph(
    string ParagraphId,
    string FirstLineId,
    string LastLineId,
    string Text,
    int WordCount,
    ParagraphKind Kind,
    string ContentHash)
{
    /// <summary>Line ids this paragraph consumed, in order. Used by contiguity and lineage checks.</summary>
    public IReadOnlyList<string> LineIds { get; init; } = [];

    /// <summary>Paragraph ids this one was split from or merged out of, empty for originals (plan §8.5).</summary>
    public IReadOnlyList<string> Lineage { get; init; } = [];
}

/// <summary>
/// A tree node. The invariant — children xor paragraphs — is enforced by the
/// <c>tree-shape</c> check rather than by the type, because a model's proposal has to be
/// representable before it can be rejected with a useful error.
/// </summary>
public sealed record SectionNode(
    string SectionId,
    string Title,
    bool TitleInferred,
    string? HeadingLineId,
    int Level,
    IReadOnlyList<SectionNode> Children,
    IReadOnlyList<string> ParagraphIds)
{
    public bool IsLeaf => Children.Count == 0;

    /// <summary>Depth-first walk including this node.</summary>
    public IEnumerable<SectionNode> Descend()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var node in child.Descend())
            {
                yield return node;
            }
        }
    }
}

public sealed record DocumentTree(
    string DocumentId,
    string RunId,
    SectionNode Root,
    string TreeHash,
    DateTimeOffset FrozenAt);

/// <summary>
/// Document-wide measurements computed once in phase 0 and used to normalize every layout
/// feature. Without them <c>FontScale</c> and <c>GapAbove</c> would be raw points and
/// meaningless across documents.
/// </summary>
public sealed record DocumentStatistics(
    int LineCount,
    int PageCount,
    double MedianLineSpacing,
    double BodyFontSize,
    double MedianLineWidth,
    int ColumnCount,
    int MedianBlockWords,
    bool HasLayout)
{
    public static readonly DocumentStatistics Empty = new(0, 0, 1, 1, 1, 1, 0, false);
}

/// <summary>A contiguous span of lines classified by triage (plan §9.4).</summary>
public sealed record Region(
    int StartIndex,
    int EndIndexExclusive,
    bool IsSuspect,
    IReadOnlyList<string> Reasons)
{
    public int LineCount => EndIndexExclusive - StartIndex;
}

/// <summary>
/// A labeling unit: <see cref="StartIndex"/>..<see cref="EndIndexExclusive"/> is what the model
/// sees, <see cref="CommitStart"/>..<see cref="CommitEndExclusive"/> is what its answer is kept
/// for. Overlap outside the commit region is context only (plan §10.1).
/// </summary>
public sealed record LabelWindow(
    int WindowIndex,
    int StartIndex,
    int EndIndexExclusive,
    int CommitStart,
    int CommitEndExclusive,
    IReadOnlyList<LineRecord> Lines)
{
    public bool Commits(int lineIndex) => lineIndex >= CommitStart && lineIndex < CommitEndExclusive;
}

/// <summary>A reversible record of one cleaning edit, so review can show what code changed and why.</summary>
public sealed record CleaningEdit(
    string LineId,
    string Rule,
    string Before,
    string After);

/// <summary>A structure or augmentation reviewer's finding (plan §10.3).</summary>
public sealed record Finding(
    FindingSeverity Severity,
    string Type,
    IReadOnlyList<string> Ids,
    string Message);

/// <summary>A paragraph split/merge the structurer proposed; applied once, in phase 6.</summary>
public sealed record ParagraphEdit(
    string Op,
    string ParagraphId,
    string? WithParagraphId,
    int? BeforeSentence,
    string Reason);
