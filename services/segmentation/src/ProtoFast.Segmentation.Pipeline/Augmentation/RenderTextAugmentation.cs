using System.Text.Json;
using ProtoFast.Segmentation.Core.Grounding;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Validation;

namespace ProtoFast.Segmentation.Pipeline.Augmentation;

/// <summary>
/// The re-writer (scene plan §3.6): a generated, independently stageable rendition of an item's own
/// span. <c>" as he ran for the door."</c> becomes <c>"James ran for the door."</c>
///
/// <para><b>It is an augmentation type, not a phase</b>, and four properties follow from that
/// placement — which together are why its cost needs no measurement to price:</para>
///
/// <list type="bullet">
/// <item><b>Cost scales with what is rendered, not with the corpus.</b> Augmentation fans out per
/// item over the frozen record, on demand. A document nobody stages costs nothing, so whether the
/// non-standalone rate is 5% or 60% it moves the bill of a render job rather than the shape of the
/// pipeline.</item>
/// <item><b>Re-running it cannot disturb the freeze.</b> The freeze covers items and spans; render
/// text is produced against a frozen item hash and keyed by it, so regenerating re-derives an
/// idempotency key rather than invalidating an artifact.</item>
/// <item><b>A failure degrades one item.</b> An output failing §3.7 is rejected and the item keeps
/// <c>RenderText = null</c>; the renderer falls back to the span. That is K7, not a blocked run.</item>
/// <item><b>It needs no role of its own.</b> It runs under <see cref="AgentRole.Augmenter"/> and is
/// reviewed by <c>AugmentReviewer</c>, so the re-writer is a <em>type</em> — not a tier, a
/// qualification key or a registry seed.</item>
/// </list>
/// </summary>
public sealed class RenderTextAugmentation : IItemAugmentationType
{
    public const string TypeName = "render-text";

    public string Name => TypeName;

    /// <summary>Mid: one clause, rewritten with its own words and one name. Nothing here needs Large.</summary>
    public ModelTier Tier => ModelTier.Mid;

    public string SkillPath => "skills/augment/render-text/SKILL.md";

    public string SchemaName => "augment-render-text";

    public int MinParagraphWords => 0;

    public int MaxParagraphWords => int.MaxValue;

    /// <summary>Zero: the output is checked against the span, so variety is only a way to fail.</summary>
    public float Temperature => 0;

    /// <summary>
    /// The selector: items phase 8 flagged as not stageable alone. An item whose span already stands
    /// alone — the large majority — carries no render text and costs nothing, which is the fourth
    /// side of the fence (§3.6).
    /// </summary>
    public bool Applies(SceneItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return !item.IsStandalone && item.Kind is not (ItemKind.Exhibit or ItemKind.Transition);
    }

    public ItemAugmentationContext BuildContext(
        SceneItem item, string paragraphText, IReadOnlyList<Persona> personas, string sceneId) =>
        new(item, paragraphText, personas, sceneId);

    /// <summary>
    /// <c>render-text-grounding</c> (§3.7), which sits exactly where <c>aug-grounding</c> sits, with
    /// the regenerate-once-then-flag path behind it. It is a <b>hard gate on the output</b> and never
    /// on the run: rejecting the output is the complete remedy, so there is nothing to block.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ItemAugmentationContext context, JsonElement output)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!output.TryGetProperty("renderText", out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            yield return ValidationResult.Fail(
                RenderTextGrounding.CheckId, "the reply had no 'renderText' string");
            yield break;
        }

        yield return RenderTextGrounding.Check(
            property.GetString() ?? string.Empty,
            context.Item.SpanOf(context.ParagraphText),
            context.ResolvedPersonas);
    }

    // The paragraph-scoped half of the contract is not reachable for an item type; these exist so
    // one interface can carry both granularities rather than the catalogue carrying two.
    AugmentationContext IAugmentationType.BuildContext(
        SectionNode root, Paragraph paragraph, IReadOnlyList<Paragraph> all, string documentTitle) =>
        throw new NotSupportedException($"'{TypeName}' is an item-scoped augmentation (scene plan §3.6).");

    IEnumerable<ValidationResult> IAugmentationType.Validate(Paragraph paragraph, JsonElement output) => [];

    bool IAugmentationType.Applies(Paragraph paragraph) => false;
}
