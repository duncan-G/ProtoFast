using ProtoFast.Segmentation.Core.Items;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Scenes;
using ProtoFast.Segmentation.Core.Validation;

namespace ProtoFast.Segmentation.UnitTests;

/// <summary>
/// Phase 10 (scene plan §8.8) and the constraints of [unit §5]. The cut rule earns its keep on
/// two failures in particular — a dialogue shredded into one scene per turn, and a lecture left as
/// one scene — so both are tested directly.
/// </summary>
public class SceneTests
{
    private static readonly Persona Vance = SceneFixtures.Persona("PR000", "Dr. Vance");
    private static readonly Persona Reed = SceneFixtures.Persona("PR001", "Reed");

    private static SceneItem Speech(string id, string paragraphId, string speaker, Addressee addressee) =>
        new(id, paragraphId, 0, 10, ItemKind.Speech,
            new SpeechAttributes(speaker, speaker == "Dr. Vance" ? Vance.PersonaId : Reed.PersonaId,
                Embodiment.Embodied, addressee, true),
            null, [], true, null, []);

    [Fact]
    public void AlternatingSpeakersDoNotCutADialogue()
    {
        // The single most expensive mistake available to a scene cutter. Cast is the set of people
        // present, not the current speaker [unit §3.3] — a scene ends when someone joins or leaves.
        var items = new List<SceneItem>
        {
            Speech("I000000", "P00001", "Dr. Vance", Addressee.InScene),
            Speech("I000001", "P00001", "Reed", Addressee.InScene),
            Speech("I000002", "P00001", "Dr. Vance", Addressee.InScene),
            Speech("I000003", "P00001", "Reed", Addressee.InScene),
            Speech("I000004", "P00001", "Dr. Vance", Addressee.InScene),
        };

        var proposal = SceneCutter.Propose(items, Text(items), minSceneSpan: 2);

        // Two boundaries at most: the first time each speaker appears. Never one per turn.
        Assert.True(proposal.Boundaries.Count <= 1, $"{proposal.Boundaries.Count} boundaries in one exchange");
    }

    [Fact]
    public void AModeChangeIsAlwaysACut()
    {
        // Mode is live in every mode [unit §4.2], and it is derived from the Speech attributes
        // rather than from the Action/Description split precisely so it does not hang on a
        // distinction the model gets wrong sometimes (§8.3).
        var items = new List<SceneItem>
        {
            Speech("I000000", "P00001", "Dr. Vance", Addressee.Audience),
            Speech("I000001", "P00001", "Dr. Vance", Addressee.Audience),
            Speech("I000002", "P00001", "Dr. Vance", Addressee.InScene),
            Speech("I000003", "P00001", "Dr. Vance", Addressee.InScene),
        };

        var proposal = SceneCutter.Propose(items, Text(items), minSceneSpan: 1);

        Assert.Contains(proposal.Boundaries, b => b is { ItemIndex: 2, Reason: BoundaryReason.ModeChange });
        Assert.Equal(SceneMode.Expounded, proposal.ModeByItem[0]);
        Assert.Equal(SceneMode.Enacted, proposal.ModeByItem[2]);
    }

    [Fact]
    public void AnUnmarkedOneSentenceModeChangeIsSuppressedByTheFloor()
    {
        // The passing aside in a keynote — "when I was in Berlin, anyway —" [unit §7 case 2]. This
        // is the only place a length heuristic does any judging, and the counter it emits is the
        // entire tuning signal S4 sweeps against.
        var paragraph = SceneFixtures.Paragraph(
            "P00001", "A point about method. An aside. Back to the method at hand now.");

        var items = SceneFixtures.Items(paragraph,
        [
            new ItemCut(0, ItemKind.Speech, SpeechAttributes.Narration("the speaker")),
            new ItemCut(30, ItemKind.Speech, SpeechAttributes.Dialogue("the speaker")),
            new ItemCut(42, ItemKind.Speech, SpeechAttributes.Narration("the speaker")),
        ]);

        var loose = SceneCutter.Propose(items, Text(items), minSceneSpan: 1);
        var strict = SceneCutter.Propose(items, Text(items), minSceneSpan: 3);

        Assert.NotEmpty(loose.Boundaries);
        Assert.Equal(0, loose.SuppressedByFloor);
        Assert.True(strict.SuppressedByFloor > 0);
        Assert.True(strict.Boundaries.Count < loose.Boundaries.Count);
    }

    [Fact]
    public void AMarkedBoundaryIsNeverSuppressedHoweverShortTheScene()
    {
        // An explicit Transition IS evidence, and the floor never overrules evidence (§8.8 rule 2).
        // That is what confines the heuristic to prose that shifts without saying so.
        var paragraph = SceneFixtures.Paragraph("P00001", "She left. Three years later. He returned.");

        var items = SceneFixtures.Items(paragraph,
        [
            new ItemCut(0, ItemKind.Action),
            new ItemCut(10, ItemKind.Transition),
            new ItemCut(28, ItemKind.Action),
        ]);

        var proposal = SceneCutter.Propose(items, Text(items), minSceneSpan: 9);

        Assert.Contains(proposal.Boundaries, b => b.Reason == BoundaryReason.Transition);
        Assert.Equal(0, proposal.SuppressedByFloor);
    }

    [Fact]
    public void AScenePartitionsItsItemsAndEveryCoordinateHasAValue()
    {
        // C1, C2, C5, C6. The nothing-values are what make "the pipeline can be uncertain about a
        // scene without failing the run" true (K7) — an unassigned scene is a legal scene.
        var paragraph = SceneFixtures.Paragraph("P00001", "The door stood open. She waited.");
        var items = SceneFixtures.Items(paragraph,
            [new ItemCut(0, ItemKind.Description), new ItemCut(20, ItemKind.Action)]);

        var proposal = SceneCutter.Propose(items, Text(items), minSceneSpan: 1);

        var result = SceneMaterializer.Materialize(
            "S0001", items, proposal.Boundaries, proposal.ModeByItem,
            new Dictionary<int, SceneAssignment>(), Registries.Empty, new SceneCutOptions(), 0);

        Assert.True(result.Success, result.Validation.ErrorReport);
        Assert.True(SceneChecks.CheckScenePartition(items, result.Scenes).Passed);

        foreach (var scene in result.Scenes)
        {
            Assert.Null(scene.Situation.PlaceId);                                 // Void
            Assert.Equal(TimeRelation.Unanchored, scene.Situation.Time.Relation);  // Unanchored
            Assert.Empty(scene.Situation.Cast);                                    // empty cast
            Assert.True(Enum.IsDefined(scene.Situation.Mode));
        }

        Assert.All(SceneChecks.CheckSituations(result.Scenes, Registries.Empty), r => Assert.True(r.Passed));
    }

    [Fact]
    public void ASceneIdIsStableAndDistinguishesTwoScenesStartingInOneParagraph()
    {
        // C10: two scenes may begin in one paragraph, which a bare paragraph id cannot distinguish,
        // and the id is anchored to the hashed artifact so it survives a re-cut (K2).
        var first = Ids.Scene(0, "P00031", 0);
        var second = Ids.Scene(1, "P00031", 240);

        Assert.NotEqual(first, second);
        Assert.Equal(first, Ids.Scene(0, "P00031", 0));
        Assert.True(Ids.IsSceneId(first));
        Assert.StartsWith("SC0000", first, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEnactedSceneWithNoPlaceIsFlaggedButNotBlocked()
    {
        // [unit §3.1]: people are acting somewhere. A rule over the tuple, evaluated after
        // assignment — it costs nothing at assignment time and never blocks.
        var paragraph = SceneFixtures.Paragraph("P00001", "He crossed the room and opened it.");
        var items = SceneFixtures.Items(paragraph,
            [new ItemCut(0, ItemKind.Speech, SpeechAttributes.Dialogue("he"))]);

        var bound = items.Select(i => i with
        {
            Speech = i.Speech! with { SpeakerPersonaId = Vance.PersonaId },
        }).ToList();

        var proposal = SceneCutter.Propose(bound, Text(bound), minSceneSpan: 1);

        var result = SceneMaterializer.Materialize(
            "S0001", bound, proposal.Boundaries, proposal.ModeByItem,
            new Dictionary<int, SceneAssignment>(), SceneFixtures.Registry(Vance), new SceneCutOptions(), 0);

        Assert.True(result.Success, result.Validation.ErrorReport);
        Assert.Equal(SceneMode.Enacted, result.Scenes[0].Situation.Mode);
        Assert.Contains(result.Scenes[0].Flags, f => f.Kind == Flag.UnlocatedEnactment);
    }

    [Fact]
    public void ACastEntryThatIsNotInTheRegistryIsRejected()
    {
        // C8, checked at assignment rather than patched afterwards — which is the whole reason
        // referents resolve at phase 9, between typing and cutting (§8.4).
        var paragraph = SceneFixtures.Paragraph("P00001", "Stop, she said.");
        var items = SceneFixtures.Items(paragraph,
            [new ItemCut(0, ItemKind.Speech, new SpeechAttributes("she", "PR999", Embodiment.Embodied, Addressee.InScene, true))]);

        var proposal = SceneCutter.Propose(items, Text(items), minSceneSpan: 1);

        var result = SceneMaterializer.Materialize(
            "S0001", items, proposal.Boundaries, proposal.ModeByItem,
            new Dictionary<int, SceneAssignment>(), SceneFixtures.Registry(Vance), new SceneCutOptions(), 0);

        Assert.False(result.Success);
        Assert.Contains("PR999", result.Validation.ErrorReport, StringComparison.Ordinal);
        Assert.Contains("C8", result.Validation.ErrorReport, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInventedPlaceIsAValidationFailureRatherThanAFallback()
    {
        // C11. Fabricating a coordinate is not a degraded answer; Void is the degraded answer, and
        // it is always available.
        var paragraph = SceneFixtures.Paragraph("P00001", "They waited.");
        var items = SceneFixtures.Items(paragraph, [new ItemCut(0, ItemKind.Action)]);

        var result = SceneMaterializer.Materialize(
            "S0001", items, [], SceneCutter.Propose(items, Text(items), 1).ModeByItem,
            new Dictionary<int, SceneAssignment>
            {
                [0] = new(0, "PL999", "waiting", TimeValue.Unanchored, null, []),
            },
            Registries.Empty, new SceneCutOptions(), 0);

        Assert.False(result.Success);
        Assert.Contains("never a name", result.Validation.ErrorReport, StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> Text(IReadOnlyList<SceneItem> items) =>
        items
            .Select(i => i.ParagraphId)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, _ => new string('x', 400), StringComparer.Ordinal);
}
