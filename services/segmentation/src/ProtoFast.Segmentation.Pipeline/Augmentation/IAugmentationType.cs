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
/// The pluggable augmentation contract (plan §12.1). A type supplies a skill, a schema and its
/// own acceptance checks; everything else — fan-out, batching, idempotency, review sampling — is
/// the pipeline's and is identical for every type.
/// </summary>
public interface IAugmentationType
{
    /// <summary>Stable name; part of the S3 key and of the idempotency key, so it may not change.</summary>
    string Name { get; }

    ModelTier Tier { get; }

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
