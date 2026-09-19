using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Validation;

namespace ProtoFast.Segmentation.Core.Items;

/// <summary>A candidate tag as phase 8 emits it: a surface form over a range, with no referent yet (§8.4).</summary>
public sealed record TagProposal(
    TagKind Kind,
    int StartOffset,
    int EndOffset,
    string SurfaceForm,
    double Confidence);

/// <summary>The items of one paragraph, plus the exact reason a proposal could not be applied.</summary>
public sealed record ItemMaterializationResult(IReadOnlyList<SceneItem> Items, ValidationResult Validation)
{
    public bool Success => Validation.Passed;
}

/// <summary>
/// Turns <see cref="ItemCut"/> proposals into the item partition of one paragraph (scene plan §3.4).
///
/// <para>Coverage is a <b>property</b> here, not a check that can fail: the spans are the intervals
/// between consecutive cut points, so <c>item-coverage</c> and <c>display-integrity</c> hold by
/// construction. What can fail is a proposal that is not a set of cut points at all — an offset
/// outside the paragraph, or two cuts at the same place — and those are rejected with an exact
/// error rather than silently clamped.</para>
///
/// <para>The one substantive rule applied here is <b>tag containment</b>: a tag names an offset
/// into the paragraph (§4.1), so a candidate tag is attached to the item whose span contains it,
/// and one that straddles an item boundary is dropped rather than split. A tag is a reference to a
/// referring expression; half of one refers to nothing.</para>
/// </summary>
public static class ItemMaterializer
{
    public const string ItemPlanCheck = "item-plan";

    public static ItemMaterializationResult Materialize(
        Paragraph paragraph,
        IReadOnlyList<ItemCut> cuts,
        IReadOnlyList<TagProposal> tags,
        ref int itemCounter,
        ref int tagCounter)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(cuts);
        ArgumentNullException.ThrowIfNull(tags);

        var text = paragraph.Text;
        var errors = new List<string>();

        if (text.Length == 0)
        {
            return new ItemMaterializationResult([], ValidationResult.Pass(ItemPlanCheck));
        }

        var ordered = cuts.OrderBy(c => c.StartOffset).ToList();

        foreach (var cut in ordered)
        {
            if (cut.StartOffset < 0 || cut.StartOffset >= text.Length)
            {
                errors.Add(
                    $"{paragraph.ParagraphId}: cut at offset {cut.StartOffset} is outside the paragraph "
                    + $"(0..{text.Length - 1})");
            }

            if (!Enum.IsDefined(cut.Kind))
            {
                errors.Add($"{paragraph.ParagraphId}: '{cut.Kind}' is not one of the five item kinds");
            }

            if (cut.Kind == ItemKind.Speech && cut.Speech is null)
            {
                errors.Add(
                    $"{paragraph.ParagraphId}: the cut at {cut.StartOffset} is Speech but carries no "
                    + "speaker surface form — an utterance with no attribution is 'Unattributed', not nothing");
            }

            if (cut.Kind != ItemKind.Speech && cut.Speech is not null)
            {
                errors.Add(
                    $"{paragraph.ParagraphId}: the cut at {cut.StartOffset} carries speech attributes "
                    + $"but is typed {cut.Kind}");
            }
        }

        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].StartOffset == ordered[i - 1].StartOffset)
            {
                errors.Add(
                    $"{paragraph.ParagraphId}: two cuts at offset {ordered[i].StartOffset} — a cut point "
                    + "is where one item ends and the next begins, so it cannot be named twice");
            }
        }

        if (errors.Count > 0)
        {
            return new ItemMaterializationResult([], ValidationResult.Fail(ItemPlanCheck, errors));
        }

        // A paragraph with no cut at 0 still has a first item; supplying it is not a repair of the
        // model's answer but the completion of a partition, which is this class's whole job.
        if (ordered.Count == 0 || ordered[0].StartOffset != 0)
        {
            ordered.Insert(0, new ItemCut(0, ItemKind.Description));
        }

        var items = new List<SceneItem>(ordered.Count);

        for (var i = 0; i < ordered.Count; i++)
        {
            var cut = ordered[i];
            var end = i + 1 < ordered.Count ? ordered[i + 1].StartOffset : text.Length;
            var itemId = Ids.Item(itemCounter++);

            items.Add(new SceneItem(
                itemId,
                paragraph.ParagraphId,
                cut.StartOffset,
                end,
                cut.Kind,
                cut.Speech,
                ExhibitId: null,
                Tags: TagsIn(tags, cut.StartOffset, end, text, ref tagCounter),
                IsStandalone: cut.IsStandalone,
                RenderText: null,
                EvidenceIds: [paragraph.ParagraphId]));
        }

        return new ItemMaterializationResult(items, ValidationResult.Pass(ItemPlanCheck));
    }

    /// <summary>
    /// The candidate tags whose range sits wholly inside one item. Offsets are paragraph-relative
    /// on both sides (§4.1, §3.3), so containment is a comparison between two ranges in the same
    /// coordinate system — which is precisely what <c>tag-bounds</c> checks afterwards.
    /// </summary>
    private static IReadOnlyList<Tag> TagsIn(
        IReadOnlyList<TagProposal> tags, int start, int end, string text, ref int tagCounter)
    {
        var inside = new List<Tag>();

        foreach (var tag in tags.Where(t => t.StartOffset >= start && t.EndOffset <= end)
                     .OrderBy(t => t.StartOffset)
                     .ThenByDescending(t => t.EndOffset))
        {
            if (tag.StartOffset < 0 || tag.EndOffset > text.Length || tag.EndOffset <= tag.StartOffset)
            {
                continue;
            }

            var candidate = new Tag(
                Ids.Tag(tagCounter),
                tag.Kind,
                tag.StartOffset,
                tag.EndOffset,
                tag.SurfaceForm,
                ReferentId: null,
                Membership: null,
                tag.Confidence);

            // Nesting is legal and partial overlap is not (§4.3): an Enumerated group tag spans
            // "Mr and Mrs Smith" with two Persona tags inside it. A partially overlapping tag is
            // dropped rather than trimmed, because a trimmed reference points at half a name.
            if (inside.Any(existing => !candidate.NestsIn(existing)
                    && !existing.NestsIn(candidate)
                    && !candidate.IsDisjointFrom(existing)))
            {
                continue;
            }

            inside.Add(candidate);
            tagCounter++;
        }

        return inside;
    }
}
