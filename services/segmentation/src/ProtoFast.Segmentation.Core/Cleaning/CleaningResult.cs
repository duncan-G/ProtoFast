using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Core.Cleaning;

/// <summary>
/// Phase 1's output (plan §9.3). <see cref="Lines"/> is the cleaned text — the integrity
/// baseline every later phase is checked against — and <see cref="Edits"/> makes every change
/// reversible in review.
/// </summary>
/// <param name="ContinuationLineIds">
/// Lines deterministically established as mid-paragraph (a page-break continuation, or the tail
/// of a hyphen join). Phase 3 is told these rather than asked about them, and no boundary may be
/// placed before one.
/// </param>
/// <param name="HyphenJoinedLineIds">
/// The subset of continuations whose previous line lost a trailing hyphen, so the two join with
/// no separator. See <see cref="LineJoiner"/> for why this has to be carried explicitly.
/// </param>
public sealed record CleaningResult(
    IReadOnlyList<LineRecord> Lines,
    IReadOnlyList<Boundary> Boundaries,
    IReadOnlySet<string> ArtifactLineIds,
    IReadOnlyDictionary<string, OtherKind> OtherLineKinds,
    IReadOnlySet<string> ContinuationLineIds,
    IReadOnlySet<string> HyphenJoinedLineIds,
    IReadOnlyList<CleaningEdit> Edits)
{
    /// <summary>
    /// The text later phases must reproduce exactly: cleaned lines with artifacts excluded,
    /// joined by <see cref="LineJoiner"/>. The <c>text-integrity</c> check compares against this.
    /// </summary>
    public string IntegrityBaseline { get; } = LineJoiner.Join(
        Lines.Where(l => !ArtifactLineIds.Contains(l.LineId)),
        HyphenJoinedLineIds);

    /// <summary>Content lines in reading order — artifacts removed, everything else kept.</summary>
    public IReadOnlyList<LineRecord> ContentLines { get; } =
        [.. Lines.Where(l => !ArtifactLineIds.Contains(l.LineId))];
}
