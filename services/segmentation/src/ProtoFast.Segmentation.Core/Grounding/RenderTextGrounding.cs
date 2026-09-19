using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Validation;

namespace ProtoFast.Segmentation.Core.Grounding;

/// <summary>
/// <c>render-text-grounding</c> (scene plan §3.7): the hard gate on the re-writer's output.
///
/// <para>Render text is <b>the one place in the pipeline a model writes prose</b>, and this is the
/// fourth side of the fence that keeps "models label, never rewrite" true. The other three are
/// structural: it is generated metadata C9 already carves out, the frozen record is untouched
/// because every gate runs over span text alone, and it is the exception rather than the rule
/// because only items flagged <c>IsStandalone = false</c> are ever rewritten.</para>
///
/// <para>The rule admits three sources and nothing else:</para>
/// <list type="number">
/// <item>Lemmas of the item's <b>own</b> span.</item>
/// <item>Canonical names of the personas the span's <b>own</b> tags resolve.</item>
/// <item>The <b>closed-class allowlist</b> — determiners, copulas, auxiliaries, prepositions,
/// conjunctions, pronouns, possessive markers, sentence punctuation.</item>
/// </list>
///
/// <para>So rewriting "he" to "James" is legal, because a <c>Persona</c> tag inside the span
/// resolves to James. Introducing a door the span never mentions is not.</para>
///
/// <para><b>It is a gate on the output, never on the run.</b> A failing output is rejected, the item
/// keeps <c>RenderText = null</c>, and the renderer falls back to the span — which is K7, not a
/// blocked run.</para>
/// </summary>
public static class RenderTextGrounding
{
    public const string CheckId = "render-text-grounding";

    public static ValidationResult Check(
        string renderText,
        string spanText,
        IReadOnlyList<Persona> resolvedPersonas,
        ClosedClassLexicon? lexicon = null)
    {
        ArgumentNullException.ThrowIfNull(renderText);
        ArgumentNullException.ThrowIfNull(spanText);
        ArgumentNullException.ThrowIfNull(resolvedPersonas);

        var closedClass = lexicon ?? ClosedClassLexicon.English;

        if (string.IsNullOrWhiteSpace(renderText))
        {
            return ValidationResult.Fail(CheckId, "the rewrite is empty");
        }

        // Row one. Lemmas rather than surface forms, so "ran" may be rewritten as "runs" — an
        // inflection is not an invention.
        var admitted = ClosedClassLexicon.Tokenize(spanText)
            .Select(ClosedClassLexicon.Lemma)
            .Where(lemma => lemma.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Row two. The personas the span's OWN tags resolve — not the scene's cast, and not the
        // document's registry. A name the item never referred to is an invention even when the
        // person is standing in the room.
        foreach (var name in resolvedPersonas.SelectMany(p => ClosedClassLexicon.Tokenize(p.CanonicalName)))
        {
            admitted.Add(ClosedClassLexicon.Lemma(name));
        }

        var ungrounded = ClosedClassLexicon.Tokenize(renderText)
            .Where(token => !closedClass.Admits(token))
            .Where(token => !admitted.Contains(ClosedClassLexicon.Lemma(token)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ungrounded.Count == 0
            ? ValidationResult.Pass(CheckId)
            : ValidationResult.Fail(
                CheckId,
                ungrounded.Select(word =>
                    $"'{word}' is in the rewrite but not in the item's span, not a name the span's own "
                    + "tags resolve, and not a function word — a rewrite may restate its span, never add to it"));
    }
}
