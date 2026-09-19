using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Scenes;
using ProtoFast.Segmentation.Core.Validation;

namespace ProtoFast.Segmentation.UnitTests;

/// <summary>
/// Phase 11 (scene plan §8.9). The four rules the materializer enforces are tested as rules —
/// a plan that breaks one cannot be applied — rather than as checks that happen to fire.
/// </summary>
public class SceneLinkTests
{
    private static Scene Scene(int ordinal, string sectionId, SceneMode mode = SceneMode.Enacted) =>
        new(
            Ids.Scene(ordinal, $"P{ordinal:D5}", 0),
            sectionId,
            $"I{ordinal:D6}",
            $"I{ordinal:D6}",
            [$"I{ordinal:D6}"],
            new Situation(null, null, TimeValue.Unanchored, [], mode, SubjectValue.None),
            [],
            null,
            true,
            [],
            "hash");

    private static readonly IReadOnlyList<Scene> Three =
        [Scene(0, "S0001"), Scene(1, "S0001"), Scene(2, "S0001")];

    [Fact]
    public void AnUncitedLinkIsNotStored()
    {
        // K4 is worth less than C11. A link nobody can check is worse than a link nobody has.
        var result = SceneLinkMaterializer.Apply(
            new SceneLinkPlan
            {
                Links = [new LinkProposal { From = Three[2].SceneId, To = Three[0].SceneId, Kind = "FlashbackOf" }],
            },
            Three, new SceneLinkOptions());

        Assert.False(result.Success);
        Assert.Contains("cites nothing", result.Validation.ErrorReport, StringComparison.Ordinal);
    }

    [Fact]
    public void AForwardFlashbackIsRejectedRatherThanSilentlyFlipped()
    {
        // The orchestrator disagreeing with document order is a signal, not a typo to repair.
        var result = SceneLinkMaterializer.Apply(
            new SceneLinkPlan
            {
                Links =
                [
                    new LinkProposal
                    {
                        From = Three[0].SceneId, To = Three[2].SceneId,
                        Kind = "FlashbackOf", Evidence = ["I000000"],
                    },
                ],
            },
            Three, new SceneLinkOptions());

        Assert.False(result.Success);
        Assert.Contains("does not precede", result.Validation.ErrorReport, StringComparison.Ordinal);
    }

    [Fact]
    public void FramesAndFramedByAreMaterializedInPairs()
    {
        // K5's "every scene that frames another" is an index lookup from EITHER end, which is only
        // true if the inverse is stored rather than derived at read time.
        var result = SceneLinkMaterializer.Apply(
            new SceneLinkPlan
            {
                Links =
                [
                    new LinkProposal
                    {
                        From = Three[0].SceneId, To = Three[1].SceneId,
                        Kind = "Frames", Evidence = ["I000000"], Confidence = 0.8,
                    },
                ],
            },
            Three, new SceneLinkOptions());

        Assert.True(result.Success, result.Validation.ErrorReport);
        Assert.Equal(2, result.Links.Count);
        Assert.Contains(result.Links, l => l.Kind == SceneLinkKind.FramedBy && l.FromSceneId == Three[1].SceneId);
        Assert.True(SceneChecks.CheckLinkIntegrity(result.Scenes).Passed);
    }

    [Fact]
    public void ASelfLinkIsRejected()
    {
        var result = SceneLinkMaterializer.Apply(
            new SceneLinkPlan
            {
                Links =
                [
                    new LinkProposal
                    {
                        From = Three[1].SceneId, To = Three[1].SceneId,
                        Kind = "ConcurrentWith", Evidence = ["I000001"],
                    },
                ],
            },
            Three, new SceneLinkOptions());

        Assert.False(result.Success);
        Assert.Contains("cannot link to itself", result.Validation.ErrorReport, StringComparison.Ordinal);
    }

    [Fact]
    public void ContinuesIsDerivedAtASectionBoundaryWhereTheSituationDidNotChange()
    {
        // The C12 split. The disagreement between the structural boundary and the staging boundary
        // is recorded rather than hidden, which is also what makes it measurable across a corpus.
        var scenes = new[] { Scene(0, "S0001"), Scene(1, "S0002") };

        var tree = new SectionNode("S0000", "Book", true, null, 1,
        [
            SceneFixtures.Leaf("S0001", "Chapter 1", "P00000"),
            SceneFixtures.Leaf("S0002", "Chapter 2", "P00001"),
        ], []);

        var result = ContinuesLinker.Link(scenes, tree);

        var link = Assert.Single(result.Links);
        Assert.Equal(SceneLinkKind.Continues, link.Kind);
        Assert.Equal(scenes[1].SceneId, link.FromSceneId);

        // Split by boundary provenance [unit §9]: across a trusted heading it is expected and needs
        // no action; across an inferred one it is evidence the inference was wrong.
        Assert.Equal(1, result.AcrossTrustedHeadings);
        Assert.Equal(0, result.AcrossInferredBoundaries);
    }

    [Fact]
    public void NoContinuesWhereTheSituationActuallyChanged()
    {
        var scenes = new[] { Scene(0, "S0001"), Scene(1, "S0002", SceneMode.Expounded) };

        var tree = new SectionNode("S0000", "Book", true, null, 1,
        [
            SceneFixtures.Leaf("S0001", "Chapter 1", "P00000"),
            SceneFixtures.Leaf("S0002", "Chapter 2", "P00001"),
        ], []);

        Assert.Empty(ContinuesLinker.Link(scenes, tree).Links);
    }

    [Fact]
    public void ADocumentWithNoBackwardSignalYieldsNoCandidatesAndThereforeNoModelCalls()
    {
        // The deterministic skip (§8.9). Most textbooks and most transcripts land here and pay
        // nothing at all for the phase.
        var digests = Three
            .Select((s, i) => new SceneDigest(
                s.SceneId, s.SectionId, i, null, SceneMode.Expounded,
                TimeValue.Unanchored, [], null, "a topic", null))
            .ToList();

        Assert.Empty(SceneDigests.Candidates(digests));
    }

    [Fact]
    public void ABackwardTimeRelationIsACandidate()
    {
        var digests = Three
            .Select((s, i) => new SceneDigest(
                s.SceneId, s.SectionId, i, null, SceneMode.Enacted,
                i == 2 ? new TimeValue(null, TimeRelation.Earlier, null) : TimeValue.Unanchored,
                [], null, "an event", null))
            .ToList();

        var candidate = Assert.Single(SceneDigests.Candidates(digests));
        Assert.Equal(2, candidate.Ordinal);
    }
}
