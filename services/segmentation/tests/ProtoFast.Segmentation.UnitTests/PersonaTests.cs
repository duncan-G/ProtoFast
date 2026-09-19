using ProtoFast.Segmentation.Core.Families;
using ProtoFast.Segmentation.Core.Items;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Personas;

namespace ProtoFast.Segmentation.UnitTests;

/// <summary>
/// Phase 9's materializer (scene plan §8.7). The point of these tests is that
/// <c>tag-resolution</c>, <c>group-closure</c> and <c>persona-scope</c> are <b>properties</b> of
/// this class: a plan that would violate one cannot be applied, and the error names the offending
/// row.
/// </summary>
public class PersonaTests
{
    private static readonly SectionNode Tree = new(
        "S0000", "Anthology", true, null, 1,
        [
            SceneFixtures.Leaf("S0001", "The First Story", "P00001"),
            SceneFixtures.Leaf("S0002", "The Second Story", "P00002"),
        ],
        []);

    private static FamilyResolver Resolver(params FamilyScope[] scopes) =>
        new(Tree, scopes, "novel");

    private static (CandidateDigest Digest, IReadOnlyList<SceneItem> Items) TwoWindows()
    {
        var first = SceneFixtures.Paragraph("P00001", "Dr. Vance turned away.");
        var second = SceneFixtures.Paragraph("P00002", "The professor waited.");

        var itemsA = SceneFixtures.Items(first, [new ItemCut(0, ItemKind.Action)],
            [new TagProposal(TagKind.Persona, 0, 9, "Dr. Vance", 0.9)]);
        var itemsB = SceneFixtures.Items(second, [new ItemCut(0, ItemKind.Action)],
            [new TagProposal(TagKind.Persona, 0, 13, "The professor", 0.7)], itemCounter: 1, tagCounter: 1);

        var digest = new CandidateDigest(
        [
            new CandidateWindow(0, "P00001", "P00001",
            [
                new ReferentCandidate(
                    CandidateRefs.For(0, 0), TagKind.Persona, "Dr. Vance", ["Dr. Vance"],
                    [itemsA[0].Tags[0].TagId], "S0001", null, [], false, 0.9),
            ], []),
            new CandidateWindow(1, "P00002", "P00002",
            [
                new ReferentCandidate(
                    CandidateRefs.For(1, 0), TagKind.Persona, "The professor", ["The professor"],
                    [itemsB[0].Tags[0].TagId], "S0002", null, [], false, 0.7),
            ], []),
        ]);

        return (digest, [.. itemsA, .. itemsB]);
    }

    [Fact]
    public void AMergePlanBindsBothTagsToOnePersona()
    {
        var (digest, items) = TwoWindows();

        var result = RegistryMaterializer.Apply(
            new RegistryPlan
            {
                Registry =
                [
                    new RegistryRow
                    {
                        Candidates = [CandidateRefs.For(0, 0), CandidateRefs.For(1, 0)],
                        CanonicalName = "Dr. Vance",
                    },
                ],
            },
            digest, items, Resolver());

        Assert.True(result.Success, result.Validation.ErrorReport);
        Assert.Single(result.Registries.Personas);
        Assert.All(
            result.Items.SelectMany(i => i.Tags),
            tag => Assert.Equal(result.Registries.Personas[0].PersonaId, tag.ReferentId));
    }

    [Fact]
    public void AnOrphanedCandidateCannotBeMaterialized()
    {
        // "Every candidate is assigned exactly once" is the invariant the materializer asserts. A
        // candidate left out is a reference that would resolve to nothing, and the error names it.
        var (digest, items) = TwoWindows();

        var result = RegistryMaterializer.Apply(
            new RegistryPlan
            {
                Registry =
                [
                    new RegistryRow { Candidates = [CandidateRefs.For(0, 0)], CanonicalName = "Dr. Vance" },
                ],
            },
            digest, items, Resolver());

        Assert.False(result.Success);
        Assert.Contains(CandidateRefs.For(1, 0), result.Validation.ErrorReport, StringComparison.Ordinal);
        Assert.Contains("exactly once", result.Validation.ErrorReport, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInventedCandidateReferenceIsRejected()
    {
        var (digest, items) = TwoWindows();

        var result = RegistryMaterializer.Apply(
            new RegistryPlan
            {
                Registry =
                [
                    new RegistryRow
                    {
                        Candidates = [CandidateRefs.For(0, 0), CandidateRefs.For(1, 0), "W00009:c3"],
                        CanonicalName = "Dr. Vance",
                    },
                ],
            },
            digest, items, Resolver());

        Assert.False(result.Success);
        Assert.Contains("may not be invented", result.Validation.ErrorReport, StringComparison.Ordinal);
    }

    [Fact]
    public void APersonaMayNotMergeAcrossAFamilyScope()
    {
        // The one rule that makes an anthology safe (§6.1). The orchestrator judges whether two
        // surface forms co-refer; code decides whether they were ever eligible to — and a false
        // merge bleeds one story's voice into another's, nearly invisibly.
        var (digest, items) = TwoWindows();

        var resolver = Resolver(
            new FamilyScope("S0001", "novel", [], 0.9),
            new FamilyScope("S0002", "textbook", [], 0.9));

        var result = RegistryMaterializer.Apply(
            new RegistryPlan
            {
                Registry =
                [
                    new RegistryRow
                    {
                        Candidates = [CandidateRefs.For(0, 0), CandidateRefs.For(1, 0)],
                        CanonicalName = "Dr. Vance",
                    },
                ],
            },
            digest, items, resolver);

        Assert.False(result.Success);
        Assert.Contains("different family scopes", result.Validation.ErrorReport, StringComparison.Ordinal);
        Assert.Contains("link between registries", result.Validation.ErrorReport, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisteringSeparatelyAcrossAScopeIsFine()
    {
        // The other side of the same rule: two Alices in two works are two Alices, and that is the
        // answer rather than a defect. K3 promises consistency WITHIN a work.
        var (digest, items) = TwoWindows();

        var result = RegistryMaterializer.Apply(
            new RegistryPlan
            {
                Registry =
                [
                    new RegistryRow { Candidates = [CandidateRefs.For(0, 0)], CanonicalName = "Dr. Vance" },
                    new RegistryRow { Candidates = [CandidateRefs.For(1, 0)], CanonicalName = "The professor" },
                ],
            },
            digest, items,
            Resolver(
                new FamilyScope("S0001", "novel", [], 0.9),
                new FamilyScope("S0002", "textbook", [], 0.9)));

        Assert.True(result.Success, result.Validation.ErrorReport);
        Assert.Equal(2, result.Registries.Personas.Count);
        Assert.Equal(["S0001", "S0002"], result.Registries.Personas.Select(p => p.ScopeRootSectionId));
    }

    [Fact]
    public void ASpeakerLabelClusterBindsTheSpeakerEvenWithNoPersonaTagInTheSpan()
    {
        // The transcript and screenplay case, and the quoted-attribution case in prose: the speaker
        // is known from the attribution clause, and there is no Persona TAG to bind through. The
        // cluster carries the item ids instead, and the speaker has to resolve from those — binding
        // only through tags would leave every deterministically-resolved utterance unattributed.
        var paragraph = SceneFixtures.Paragraph("P00001", "\"Stop,\" Vance said quietly.");
        var items = SceneFixtures.Items(paragraph,
            [new ItemCut(0, ItemKind.Speech, SpeechAttributes.Dialogue("Vance"))]);

        var digest = new CandidateDigest(
        [
            new CandidateWindow(0, "P00001", "P00001",
            [
                new ReferentCandidate(
                    CandidateRefs.For(0, 0), TagKind.Persona, "Vance", ["Vance"],
                    [items[0].ItemId], "S0001", null, [], false, 1.0),
            ], []),
        ]);

        var result = RegistryMaterializer.Apply(
            new RegistryPlan
            {
                Registry = [new RegistryRow { Candidates = [CandidateRefs.For(0, 0)], CanonicalName = "Vance" }],
            },
            digest, items, Resolver());

        Assert.True(result.Success, result.Validation.ErrorReport);
        Assert.Equal(
            result.Registries.Personas.Single().PersonaId,
            result.Items[0].Speech!.SpeakerPersonaId);
    }

    [Fact]
    public void AnUnattributedSpeakerBindsToARealPersonaRatherThanToNothing()
    {
        // C8: no anonymous speakers. "Unattributed" is a registry entry precisely so that rule is
        // enforceable rather than aspirational.
        var paragraph = SceneFixtures.Paragraph("P00001", "Somewhere a voice said stop.");
        var items = SceneFixtures.Items(paragraph,
            [new ItemCut(0, ItemKind.Speech, new SpeechAttributes(string.Empty, null, Embodiment.Disembodied, Addressee.Audience, true))]);

        var (closed, registries) = RegistryMaterializer.CloseCast(items, Registries.Empty);

        Assert.Single(registries.Personas);
        Assert.Equal(RegistryMaterializer.UnattributedName, registries.Personas[0].CanonicalName);
        Assert.Equal(registries.Personas[0].PersonaId, closed[0].Speech!.SpeakerPersonaId);
    }

    [Fact]
    public void GroupMembershipIsPerReferenceRatherThanPerRegistryEntry()
    {
        // "The brothers" may be two in chapter 1 and three in chapter 20, with neither reference
        // wrong (§4.3). The registry holds identity; the tag holds the roster.
        var paragraph = SceneFixtures.Paragraph("P00001", "The brothers arrived.");
        var items = SceneFixtures.Items(paragraph, [new ItemCut(0, ItemKind.Action)],
            [new TagProposal(TagKind.Group, 0, 12, "The brothers", 0.9)]);

        var tagId = items[0].Tags[0].TagId;

        var digest = new CandidateDigest(
        [
            new CandidateWindow(0, "P00001", "P00001",
            [
                new ReferentCandidate(
                    CandidateRefs.For(0, 0), TagKind.Persona, "Alan", ["Alan"], [], "S0001", null, [], false, 1),
                new ReferentCandidate(
                    CandidateRefs.For(0, 1), TagKind.Persona, "Ben", ["Ben"], [], "S0001", null, [], false, 1),
                new ReferentCandidate(
                    CandidateRefs.For(0, 2), TagKind.Group, "The brothers", ["The brothers"], [tagId],
                    "S0001", GroupFormation.Enumerated,
                    [CandidateRefs.For(0, 0), CandidateRefs.For(0, 1)], true, 0.9),
            ], []),
        ]);

        var result = RegistryMaterializer.Apply(
            new RegistryPlan
            {
                Registry =
                [
                    new RegistryRow { Candidates = [CandidateRefs.For(0, 0)], CanonicalName = "Alan" },
                    new RegistryRow { Candidates = [CandidateRefs.For(0, 1)], CanonicalName = "Ben" },
                    new RegistryRow
                    {
                        Candidates = [CandidateRefs.For(0, 2)],
                        CanonicalName = "The brothers",
                        Kind = "group",
                        Formation = "enumerated",
                    },
                ],
            },
            digest, items, Resolver());

        Assert.True(result.Success, result.Validation.ErrorReport);

        var membership = result.Items[0].Tags.Single(t => t.Kind == TagKind.Group).Membership;

        Assert.NotNull(membership);
        Assert.Equal(2, membership!.MemberPersonaIds.Count);
        Assert.True(membership.IsComplete);
        Assert.Equal(PersonaKind.Group, result.Registries.Personas.Single(p => p.CanonicalName == "The brothers").Kind);
    }

    [Fact]
    public void ASingleFamilyDocumentResolvesToTheRunFamilyEverywhereAndCarriesNoScopeRoot()
    {
        var resolver = Resolver();

        Assert.Equal("novel", resolver.Resolve("S0002"));
        Assert.Null(resolver.ScopeRootOf("S0002"));
        Assert.False(resolver.HasScopes);
    }

    [Fact]
    public void FamilyScopesAreDerivedFromParagraphEvidenceWithNoSecondDetector()
    {
        // The anthology case (§6.1): one subtree reads as a different kind of work, and phase 7
        // notices in code from the vector phase 5 already emitted.
        var novel = Enumerable.Range(0, 80)
            .Select(i => new FamilyEvidence($"P{i:D5}", 0.35, false, 0, 0, 0.12, 60))
            .ToList();

        var textbook = Enumerable.Range(80, 80)
            .Select(i => new FamilyEvidence($"P{i:D5}", 0.0, false, 0.4, 0.05, 0.0, 60))
            .ToList();

        var root = new SectionNode("S0000", "Collection", true, null, 1,
        [
            new SectionNode("S0001", "A Story", false, "L000001", 2, [], [.. novel.Select(e => e.ParagraphId)]),
            new SectionNode("S0002", "A Lesson", false, "L000002", 2, [], [.. textbook.Select(e => e.ParagraphId)]),
        ], []);

        var scopes = FamilyScopeDeriver.Derive(
            root, [.. novel, .. textbook], "novel", new FamilyScopeOptions());

        var scope = Assert.Single(scopes);
        Assert.Equal("S0002", scope.SectionId);
        Assert.Equal("textbook", scope.CompositionFamily);
    }

    [Fact]
    public void AShortVignetteIsNotASecondCompositionFamily()
    {
        // The MinFamilyScopeParagraphs guard. A three-paragraph Enacted island inside a textbook is
        // exactly what the plan says it is — an island, not a work.
        var textbook = Enumerable.Range(0, 100)
            .Select(i => new FamilyEvidence($"P{i:D5}", 0.0, false, 0.4, 0.05, 0.0, 60))
            .ToList();

        var vignette = Enumerable.Range(100, 3)
            .Select(i => new FamilyEvidence($"P{i:D5}", 0.6, false, 0, 0, 0.3, 60))
            .ToList();

        var root = new SectionNode("S0000", "Textbook", true, null, 1,
        [
            new SectionNode("S0001", "Chapter", false, "L000001", 2, [], [.. textbook.Select(e => e.ParagraphId)]),
            new SectionNode("S0002", "Example", false, "L000002", 2, [], [.. vignette.Select(e => e.ParagraphId)]),
        ], []);

        Assert.Empty(FamilyScopeDeriver.Derive(
            root, [.. textbook, .. vignette], "textbook", new FamilyScopeOptions()));
    }
}
