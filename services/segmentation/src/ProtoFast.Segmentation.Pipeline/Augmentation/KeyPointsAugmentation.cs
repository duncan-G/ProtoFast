using System.Text.Json;
using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Ingest;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Validation;

namespace ProtoFast.Segmentation.Pipeline.Augmentation;

/// <summary>
/// ThePlot's first augmentation type: the claims a paragraph makes, the terms it introduces, and
/// the question it answers.
///
/// <para>The plan leaves the first type as an open question (§31.1). This one is chosen because
/// it exercises the whole contract without needing anything the pipeline does not already have —
/// no tools, no external lookup, no second corpus — and because its output is checkable: every
/// claim has to be traceable to the paragraph, which makes <see cref="Validate"/> a real gate
/// rather than a schema check wearing a hat.</para>
/// </summary>
public sealed class KeyPointsAugmentation : IAugmentationType
{
    public const string TypeName = "key-points";

    public string Name => TypeName;

    public ModelTier Tier => ModelTier.Small;

    public string SkillPath => "skills/augment/key-points/SKILL.md";

    public string SchemaName => "augment-key-points";

    /// <summary>
    /// Below fifteen words a paragraph rarely makes a separable claim; above three hundred it
    /// makes too many for four points to represent honestly. Both bounds match the pipeline's
    /// default paragraph size bounds, so a paragraph that passes <c>size-bounds</c> is augmentable.
    /// </summary>
    public int MinParagraphWords => 15;

    public int MaxParagraphWords => 300;

    /// <summary>Zero: the output is checked against the source, so variety is only a way to fail.</summary>
    public float Temperature => 0;

    public AugmentationContext BuildContext(
        SectionNode root, Paragraph paragraph, IReadOnlyList<Paragraph> all, string documentTitle)
    {
        var index = -1;
        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].ParagraphId == paragraph.ParagraphId)
            {
                index = i;
                break;
            }
        }

        return new AugmentationContext(
            DocumentTitle: documentTitle,
            SectionPath: SectionPath(root, paragraph.ParagraphId),
            PreviousParagraphText: index > 0 ? all[index - 1].Text : null,
            NextParagraphText: index >= 0 && index < all.Count - 1 ? all[index + 1].Text : null,
            ParagraphId: paragraph.ParagraphId,
            ParagraphText: paragraph.Text);
    }

    public IEnumerable<ValidationResult> Validate(Paragraph paragraph, JsonElement output)
    {
        var errors = new List<string>();

        // aug-grounding, first half: the output must be about the paragraph it was asked about.
        // A model that echoes a neighbouring id would otherwise silently attach one paragraph's
        // analysis to another (plan §11).
        if (!output.TryGetProperty("paragraphId", out var id) || id.GetString() != paragraph.ParagraphId)
        {
            var claimed = id.ValueKind == JsonValueKind.String ? id.GetString() : "(absent)";
            errors.Add($"output claims paragraphId '{claimed}' but was produced for '{paragraph.ParagraphId}'");
        }

        if (!output.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Array)
        {
            errors.Add("'points' is missing or is not an array");
            return [ValidationResult.Fail(Checks.AugGrounding, errors)];
        }

        if (points.GetArrayLength() > 4)
        {
            errors.Add($"{points.GetArrayLength()} points; the contract allows at most 4");
        }

        // aug-grounding, second half: a point longer than the paragraph is not a point about it.
        foreach (var point in points.EnumerateArray())
        {
            var text = point.GetString() ?? string.Empty;
            if (text.Trim().Length == 0)
            {
                errors.Add("a point is empty");
            }
            else if (TextMetrics.WordCount(text) > paragraph.WordCount)
            {
                errors.Add($"a point is longer than the paragraph it summarises ({TextMetrics.WordCount(text)} words vs {paragraph.WordCount})");
            }
        }

        if (output.TryGetProperty("terms", out var terms) && terms.ValueKind == JsonValueKind.Array)
        {
            foreach (var term in terms.EnumerateArray())
            {
                var word = term.TryGetProperty("term", out var t) ? t.GetString() : null;

                // A term the paragraph never uses was not introduced by it. This is the cheapest
                // possible grounding check and it catches the most common failure — a model
                // defining a term it recognised from the section title.
                if (word is { Length: > 0 }
                    && !paragraph.Text.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"term '{word}' does not appear in the paragraph");
                }
            }
        }

        return [ValidationResult.Fail(Checks.AugGrounding, errors)];
    }

    /// <summary>The titles from the root down to the section holding this paragraph.</summary>
    internal static IReadOnlyList<string> SectionPath(SectionNode root, string paragraphId)
    {
        var path = new List<string>();
        return Walk(root, paragraphId, path) ? path : [];

        static bool Walk(SectionNode node, string paragraphId, List<string> path)
        {
            path.Add(node.Title);

            if (node.ParagraphIds.Contains(paragraphId))
            {
                return true;
            }

            foreach (var child in node.Children)
            {
                if (Walk(child, paragraphId, path))
                {
                    return true;
                }
            }

            path.RemoveAt(path.Count - 1);
            return false;
        }
    }
}
