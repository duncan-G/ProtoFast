using ProtoFast.Segmentation.Core.Items;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Validation;

namespace ProtoFast.Segmentation.UnitTests;

/// <summary>
/// The item layer (scene plan §3). The two gates it exists to satisfy — <c>item-coverage</c> and
/// <c>display-integrity</c> — are tested against the materializer rather than against hand-built
/// items, because the claim being made is that they <em>cannot</em> fail, not that they happen not to.
/// </summary>
public class ItemTests
{
    private const string MixedSentence = """James shouted, "Bye" as he ran for the door.""";

    [Fact]
    public void AMixedSentenceIsCutIntoSpeechAndAction()
    {
        // The plan's worked example (§3.3). One sentence, two items — and the attribution clause
        // belongs to the speech item it attributes, not to the action.
        var cuts = ItemSegmenter.PreSegment(SceneFixtures.Paragraph("P00001", MixedSentence));

        Assert.Equal(2, cuts.Count);
        Assert.Equal(ItemKind.Speech, cuts[0].Kind);
        Assert.Equal(0, cuts[0].StartOffset);
        Assert.Equal(ItemKind.Action, cuts[1].Kind);

        var paragraph = SceneFixtures.Paragraph("P00001", MixedSentence);
        var items = SceneFixtures.Items(paragraph, cuts);

        Assert.Equal(MixedSentence[..20], items[0].SpanOf(paragraph.Text));
        Assert.Equal(" as he ran for the door.", items[1].SpanOf(paragraph.Text));
    }

    [Fact]
    public void AnAttributionAfterTheQuoteJoinsItsSpeechItemAndYieldsTheSpeaker()
    {
        // The mirror of the plan's example: the attribution clause belongs to the speech item it
        // attributes whichever side of the quote it sits on, and it is where the speaker surface
        // form is read from. It stops at the verb, so the action after it stays an Action item.
        const string text = "\"We are not finished,\" Vance said as she reached for the case.";
        var paragraph = SceneFixtures.Paragraph("P00001", text);
        var cuts = ItemSegmenter.PreSegment(paragraph);

        Assert.Equal(2, cuts.Count);
        Assert.Equal(ItemKind.Speech, cuts[0].Kind);
        Assert.Equal("Vance", cuts[0].Speech!.SurfaceForm);
        Assert.Equal(ItemKind.Action, cuts[1].Kind);
        Assert.False(cuts[1].IsStandalone);

        var items = SceneFixtures.Items(paragraph, cuts);
        Assert.Equal("\"We are not finished,\" Vance said", items[0].SpanOf(text));
    }

    [Fact]
    public void TheTrailingFragmentIsFlaggedAsNotStageableAlone()
    {
        // This flag is the ONLY selector for the render-text augmentation (§3.6). A fragment that
        // is not flagged is a fragment no re-writer will ever see, so the flag is doing the whole
        // job of pricing that augmentation.
        var paragraph = SceneFixtures.Paragraph("P00001", MixedSentence);
        var items = SceneFixtures.Items(paragraph, ItemSegmenter.PreSegment(paragraph));

        Assert.True(items[0].IsStandalone);
        Assert.False(items[1].IsStandalone);
    }

    [Fact]
    public void CoverageAndIntegrityHoldForEveryCutPlanTheMaterializerAccepts()
    {
        var paragraph = SceneFixtures.Paragraph(
            "P00001", "The door stood open. She waited. Nothing moved in the corridor beyond.");

        // A deliberately awkward plan: cuts at odd offsets, out of order, mid-word.
        var items = SceneFixtures.Items(paragraph,
        [
            new ItemCut(40, ItemKind.Description),
            new ItemCut(0, ItemKind.Description),
            new ItemCut(20, ItemKind.Action),
        ]);

        Assert.True(SceneChecks.CheckItemCoverage([paragraph], items).Passed);
        Assert.True(SceneChecks.CheckDisplayIntegrity([paragraph], items).Passed);
        Assert.Equal(paragraph.Text, string.Concat(items.Select(i => i.SpanOf(paragraph.Text))));
    }

    [Fact]
    public void ACutOutsideTheParagraphIsRejectedRatherThanClamped()
    {
        // Clamping would turn a model's mistake into a boundary nobody chose. The error names the
        // offset so the repair round has something to act on.
        var paragraph = SceneFixtures.Paragraph("P00001", "Short.");
        var itemCounter = 0;
        var tagCounter = 0;

        var result = ItemMaterializer.Materialize(
            paragraph, [new ItemCut(0, ItemKind.Description), new ItemCut(99, ItemKind.Action)],
            [], ref itemCounter, ref tagCounter);

        Assert.False(result.Success);
        Assert.Contains("99", result.Validation.ErrorReport, StringComparison.Ordinal);
    }

    [Fact]
    public void ASpeechCutWithNoSpeakerIsRejected()
    {
        // C8 has no anonymous speakers, and the surface form is what phase 9 binds. A speech item
        // with neither is an utterance nothing can ever attribute.
        var paragraph = SceneFixtures.Paragraph("P00001", "Hello there.");
        var itemCounter = 0;
        var tagCounter = 0;

        var result = ItemMaterializer.Materialize(
            paragraph, [new ItemCut(0, ItemKind.Speech)], [], ref itemCounter, ref tagCounter);

        Assert.False(result.Success);
        Assert.Contains("Unattributed", result.Validation.ErrorReport, StringComparison.Ordinal);
    }

    [Fact]
    public void TagsAttachToTheItemThatContainsThemAndNestingIsAllowed()
    {
        // "Mr and Mrs Smith" is a group tag with two persona tags inside it (§4.3). Nesting is
        // legal; partial overlap is not.
        const string text = "Mr and Mrs Smith arrived.";
        var paragraph = SceneFixtures.Paragraph("P00001", text);

        var items = SceneFixtures.Items(paragraph, [new ItemCut(0, ItemKind.Action)],
        [
            new TagProposal(TagKind.Group, 0, 16, "Mr and Mrs Smith", 0.9),
            new TagProposal(TagKind.Persona, 0, 2, "Mr", 0.8),
            new TagProposal(TagKind.Persona, 7, 16, "Mrs Smith", 0.8),
        ]);

        Assert.Equal(3, items[0].Tags.Count);
        Assert.True(SceneChecks.CheckTagBounds(items).Passed);
    }

    [Fact]
    public void APartiallyOverlappingTagIsDroppedRatherThanTrimmed()
    {
        // A trimmed reference points at half a name, which is worse than no reference at all.
        const string text = "Dr. Vance turned away.";
        var paragraph = SceneFixtures.Paragraph("P00001", text);

        var items = SceneFixtures.Items(paragraph, [new ItemCut(0, ItemKind.Action)],
        [
            new TagProposal(TagKind.Persona, 0, 9, "Dr. Vance", 0.9),
            new TagProposal(TagKind.Persona, 4, 15, "Vance turne", 0.4),
        ]);

        Assert.Single(items[0].Tags);
        Assert.Equal("Dr. Vance", items[0].Tags[0].SurfaceForm);
    }

    [Fact]
    public void AnOpaqueBlockIsOneExhibitItem()
    {
        // Tables, code and equations are blocks the pipeline deliberately does not interpret;
        // cutting inside one would claim it had.
        var table = SceneFixtures.Paragraph("P00001", "| a | b |\n| 1 | 2 |") with
        {
            Kind = ParagraphKind.Table,
        };

        var cuts = ItemSegmenter.PreSegment(table);

        Assert.Single(cuts);
        Assert.Equal(ItemKind.Exhibit, cuts[0].Kind);
        Assert.True(ItemSegmenter.IsSettled(table));
    }

    [Fact]
    public void ASpeakerLabelledTurnNeedsNoModel()
    {
        // The transcript and screenplay families resolve here with no call at all: the label IS the
        // surface form (§8.4).
        var turn = SceneFixtures.Paragraph("P00001", "VANCE: We are not finished.");

        Assert.True(ItemSegmenter.IsSettled(turn));

        var cuts = ItemSegmenter.PreSegment(turn);

        Assert.Single(cuts);
        Assert.Equal(ItemKind.Speech, cuts[0].Kind);
        Assert.Equal("VANCE", cuts[0].Speech!.SurfaceForm);
    }

    [Fact]
    public void ItemCoverageCatchesAGapAndNamesIt()
    {
        // The gate is only worth having if its message is actionable, since it is fed straight back
        // to whoever produced the artifact.
        var paragraph = SceneFixtures.Paragraph("P00001", "One two three four.");

        var truncated = new SceneItem(
            "I000000", "P00001", 0, 5, ItemKind.Description, null, null, [], true, null, []);

        var result = SceneChecks.CheckItemCoverage([paragraph], [truncated]);

        Assert.False(result.Passed);
        Assert.Contains("ends at 5 of 19", result.ErrorReport, StringComparison.Ordinal);
    }
}
