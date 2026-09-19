using ProtoFast.Segmentation.Core.Grounding;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.UnitTests;

/// <summary>
/// <c>render-text-grounding</c> (scene plan §3.7) — the fence around the one place in the pipeline
/// a model writes prose.
/// </summary>
public class GroundingTests
{
    private const string Span = " as he ran for the door.";

    private static readonly IReadOnlyList<Persona> James = [SceneFixtures.Persona("PR000", "James")];

    [Fact]
    public void RewritingAPronounToANameTheSpanTagsResolveIsLegal()
    {
        // The whole purpose of the augmentation: "as he ran for the door" is not stageable and
        // "James ran for the door." is. It is legal because a Persona tag inside the span binds to
        // James — not because James is in the scene.
        Assert.True(RenderTextGrounding.Check("James ran for the door.", Span, James).Passed);
    }

    [Fact]
    public void IntroducingANounTheSpanNeverMentionsIsRejected()
    {
        var result = RenderTextGrounding.Check("James ran for the corridor.", Span, James);

        Assert.False(result.Passed);
        Assert.Contains("corridor", result.ErrorReport, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void APersonaTheSpansOwnTagsDoNotResolveIsRejected()
    {
        // Row two admits the personas the ITEM's own tags bind — not the scene's cast, and not the
        // document's registry. A name the item never referred to is an invention even when the
        // person is standing in the room.
        Assert.False(RenderTextGrounding.Check("Reed ran for the door.", Span, James).Passed);
    }

    [Fact]
    public void FunctionWordsTheFragmentLacksAreAdmitted()
    {
        // The closed-class allowlist is what makes the gate affordable as a HARD gate: every feared
        // false failure was a function word, and function words carry no referents.
        Assert.True(RenderTextGrounding.Check("James was at the door.", Span, James).Passed);
        Assert.True(RenderTextGrounding.Check("It was James's door.", Span, James).Passed);
    }

    [Fact]
    public void AnInflectionOfTheSpansOwnWordIsNotAnInvention()
    {
        // Lemmas, not surface forms: a rewrite that re-tenses a regular verb has not added a word.
        Assert.True(RenderTextGrounding.Check(
            "James walks for the door.", " as he walked for the door.", James).Passed);
    }

    [Fact]
    public void AnIrregularVerbIsRejectedRatherThanGuessedAt()
    {
        // The English stemmer the gate ships with does not know "ran" and "runs" are one verb, so a
        // rewrite that re-tenses one is rejected and the item keeps its span. That is the correct
        // direction to fail in: a rejected good rewrite costs a fallback, an admitted invention
        // costs the reader.
        Assert.False(RenderTextGrounding.Check("James runs for the door.", Span, James).Passed);
    }

    [Fact]
    public void AnEmptyRewriteFails()
    {
        Assert.False(RenderTextGrounding.Check("   ", Span, James).Passed);
    }

    [Fact]
    public void TheLexiconIsAnAssetAndParsesCommentsAndBlankLines()
    {
        // "The list is an asset, not code" (§3.7): a new language is a file, so the format has to
        // be something a person adding one can write.
        var lexicon = ClosedClassLexicon.Parse("""
            # a comment
            der
            die

            das   # trailing comment
            """);

        Assert.True(lexicon.Admits("der"));
        Assert.True(lexicon.Admits("das"));
        Assert.False(lexicon.Admits("Hund"));
        Assert.False(lexicon.Admits("comment"));
    }
}
