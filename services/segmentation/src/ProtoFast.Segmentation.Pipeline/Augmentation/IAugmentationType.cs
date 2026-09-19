using System.Text.Json;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Validation;

namespace ProtoFast.Segmentation.Pipeline.Augmentation;

/// <summary>
/// What one augmentation call sees (plan §12.1). The neighbours are context only; a type that
/// summarised them would produce output attributed to the wrong paragraph.
/// </summary>
public sealed record AugmentationContext(
    string DocumentTitle,
    IReadOnlyList<string> SectionPath,
    string? PreviousParagraphText,
    string? NextParagraphText,
    string ParagraphId,
    string ParagraphText);

/// <summary>
/// What one <b>item-scoped</b> augmentation call sees (scene plan §10).
///
/// <para><see cref="ParagraphText"/> is the frozen paragraph, so the item's offsets resolve;
/// <see cref="ResolvedPersonas"/> is exactly those the item's <em>own</em> tags bind, which is what
/// row two of the grounding rule admits (§3.7). Together with the item's span that is the
/// re-writer's entire input — which is why placing it after the freeze costs nothing: a frozen item
/// already carries all of it, so reading it later is a read rather than a re-derivation.</para>
/// </summary>
public sealed record ItemAugmentationContext(
    SceneItem Item,
    string ParagraphText,
    IReadOnlyList<Persona> ResolvedPersonas,
    string SceneId);

/// <summary>
/// The pluggable augmentation contract (plan §12.1). A type supplies a skill, a schema and its
/// own acceptance checks; everything else — fan-out, batching, idempotency, review sampling — is
/// the pipeline's and is identical for every type.
/// </summary>
public interface IAugmentationType
{
    /// <summary>Stable name; part of the S3 key and of the idempotency key, so it may not change.</summary>
    string Name { get; }

    ModelTier Tier { get; }

    /// <summary>
    /// What one call covers (scene plan §10). <b>Granularity is a property of the type, not of the
    /// pipeline</b>: the contract was already pluggable, so admitting item, scene, section and
    /// persona scope costs a property rather than an architecture, and no single granularity is
    /// imposed on every type.
    /// </summary>
    AugmentationScope Scope => AugmentationScope.Paragraph;

    /// <summary>Path under <c>Assets/</c>, e.g. <c>skills/augment/key-points/SKILL.md</c>.</summary>
    string SkillPath { get; }

    /// <summary>Schema name under <c>Assets/schemas/</c>, without the <c>.schema.json</c> suffix.</summary>
    string SchemaName { get; }

    /// <summary>Paragraphs outside this range are skipped rather than augmented badly.</summary>
    int MinParagraphWords { get; }

    int MaxParagraphWords { get; }

    /// <summary>Sampling temperature. Zero for anything whose output is checked against the source.</summary>
    float Temperature { get; }

    AugmentationContext BuildContext(
        SectionNode root, Paragraph paragraph, IReadOnlyList<Paragraph> all, string documentTitle);

    /// <summary>
    /// The type's own acceptance criteria, on top of the schema. This is where "grounded in this
    /// paragraph" becomes something code can check rather than something a reviewer has to read.
    /// </summary>
    IEnumerable<ValidationResult> Validate(Paragraph paragraph, JsonElement output);

    /// <summary>
    /// True when this paragraph should be augmented at all. Tables, code and equations are opaque
    /// blocks the pipeline deliberately does not interpret (plan §12.2).
    /// </summary>
    bool Applies(Paragraph paragraph) =>
        paragraph.Kind is not (ParagraphKind.Table or ParagraphKind.Code or ParagraphKind.Equation)
        && paragraph.WordCount >= MinParagraphWords
        && paragraph.WordCount <= MaxParagraphWords;
}

/// <summary>
/// An augmentation type whose unit is the <see cref="SceneItem"/> rather than the paragraph
/// (scene plan §10). <c>aug-grounding</c> reads "its own target id" where it read "its own
/// paragraph id", and the rest of the contract is unchanged.
/// </summary>
public interface IItemAugmentationType : IAugmentationType
{
    AugmentationScope IAugmentationType.Scope => AugmentationScope.Item;

    /// <summary>True when this item should be augmented at all — the type's own selector.</summary>
    bool Applies(SceneItem item);

    ItemAugmentationContext BuildContext(
        SceneItem item, string paragraphText, IReadOnlyList<Persona> personas, string sceneId);

    /// <summary>The type's acceptance criteria over an item's output.</summary>
    IEnumerable<ValidationResult> Validate(ItemAugmentationContext context, JsonElement output);
}

/// <summary>Resolves a configured augmentation name to its implementation.</summary>
public interface IAugmentationCatalogue
{
    IReadOnlyList<IAugmentationType> All { get; }

    IAugmentationType? Find(string name);
}

public sealed class AugmentationCatalogue(IEnumerable<IAugmentationType> types) : IAugmentationCatalogue
{
    public IReadOnlyList<IAugmentationType> All { get; } = [.. types];

    public IAugmentationType? Find(string name) =>
        All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
}
