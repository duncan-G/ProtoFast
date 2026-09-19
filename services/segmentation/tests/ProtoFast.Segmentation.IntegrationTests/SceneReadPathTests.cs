using System.Text.Json;
using System.Text.Json.Serialization;
using Grpc.Core;
using ProtoFast.Api;
using ProtoFast.Segmentation.Data.Entities;
using CoreModel = ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.IntegrationTests;

/// <summary>
/// The read path of scene plan milestone S7: what <c>GetScenes</c> hands a renderer.
///
/// <para>The property under test throughout is K1 — a scene renders from its own record with no
/// document access. Every assertion about text or a name is really an assertion that the join
/// happened on the server, because a client that has to do it has to have the document, and then
/// the scene record is not self-contained.</para>
/// </summary>
public class SceneReadPathTests
{
    private const string Alice = "alice-subject";
    private const string Bob = "bob-subject";

    /// <summary>The spelling the publish phase writes the scene columns in: enums as text.</summary>
    private static readonly JsonSerializerOptions SceneJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetScenesResolvesEveryItemsTextAndEveryName()
    {
        using var fixture = new SegmentationServiceFixture();
        var runId = await PublishAsync(fixture, Alice);

        var reply = await fixture.Service.GetScenes(
            new GetScenesRequest { RunId = runId }, SegmentationServiceFixture.CallerContext(Alice));

        var scene = Assert.Single(reply.Scenes, s => s.SceneId == "sc_kitchen");

        // The item spans are offsets into a paragraph the client is never sent, so this passing at
        // all is the point: the text came out of the frozen paragraph server-side.
        Assert.Collection(
            scene.Items,
            speech =>
            {
                Assert.Equal("speech", speech.Kind);
                Assert.Equal("“You came back,”", speech.Text);
                Assert.Equal("Mara", speech.Speech.SpeakerName);
                Assert.Equal("in_scene", speech.Speech.Addressee);
            },
            action =>
            {
                Assert.Equal("action", action.Kind);
                // The span cannot be staged alone, so the reply carries both: the words the
                // document used and the render text written to stand in for them (§3.6).
                Assert.Equal("she said from the doorway.", action.Text);
                Assert.Equal("Mara speaks from the doorway.", action.RenderText);
                Assert.False(action.IsStandalone);
            });

        Assert.Equal("The kitchen", scene.Situation.PlaceName);
        Assert.Equal("stated", scene.Situation.SettingSource);
        Assert.Equal("enacted", scene.Situation.Mode);
        Assert.Equal("the next morning", scene.Situation.TimeAnchor);
        Assert.Equal("gap", scene.Situation.TimeRelation);

        var cast = Assert.Single(scene.Situation.Cast);
        Assert.Equal("Mara", cast.Name);
        Assert.Equal("speaking", cast.Role);

        // A tag's referent is an id, but its kind is a name — and the wire spelling of a
        // multi-word member is what a client switches on.
        var tag = Assert.Single(scene.Items[0].Tags);
        Assert.Equal("persona", tag.Kind);
        Assert.Equal("pe_mara", tag.ReferentId);
    }

    [Fact]
    public async Task GetScenesSpellsEveryEnumTheWayTheProtoDocumentsIt()
    {
        using var fixture = new SegmentationServiceFixture();
        var runId = await PublishAsync(fixture, Alice);

        var reply = await fixture.Service.GetScenes(
            new GetScenesRequest { RunId = runId }, SegmentationServiceFixture.CallerContext(Alice));

        // A client switches on these strings, so the snake_case spelling of a multi-word member is
        // part of the contract rather than a formatting choice.
        var framing = Assert.Single(reply.Scenes, s => s.SceneId == "sc_frame");
        Assert.Equal("narrated", framing.Situation.Mode);
        Assert.Equal("unanchored", framing.Situation.TimeRelation);

        var link = Assert.Single(framing.Links);
        Assert.Equal("frames", link.Kind);
        Assert.Equal("sc_kitchen", link.ToSceneId);

        var exhibit = Assert.Single(reply.Scenes, s => s.SceneId == "sc_exhibit");
        Assert.Equal("exhibited", exhibit.Situation.Mode);
        Assert.Equal("exhibit", exhibit.Items[0].Kind);
        Assert.Equal("exhibit_ref", exhibit.Items[0].Tags[0].Kind);

        var local = Assert.Single(reply.Personas, p => p.PersonaId == "pe_narrator");
        Assert.Equal("scene_local", local.Scope);
        Assert.Equal("individual", local.Kind);
    }

    [Fact]
    public async Task GetScenesKeepsDocumentWideOrdinalsWhenFilteredToASection()
    {
        using var fixture = new SegmentationServiceFixture();
        var runId = await PublishAsync(fixture, Alice);

        var whole = await fixture.Service.GetScenes(
            new GetScenesRequest { RunId = runId }, SegmentationServiceFixture.CallerContext(Alice));

        Assert.Equal([0, 1, 2], whole.Scenes.Select(s => s.Ordinal));
        Assert.Equal(3, whole.TotalScenes);

        // A section filter admits the section and its subtree — asking for a part means asking for
        // its chapters — and the ordinals stay the document's, so "scene 3" cites the same scene in
        // both replies.
        var part = await fixture.Service.GetScenes(
            new GetScenesRequest { RunId = runId, SectionId = "sec_part" },
            SegmentationServiceFixture.CallerContext(Alice));

        Assert.Equal(["sc_kitchen", "sc_exhibit"], part.Scenes.Select(s => s.SceneId));
        Assert.Equal([0, 1], part.Scenes.Select(s => s.Ordinal));

        // And the slice still says what it is a slice of, so a reader is not told the document has
        // two scenes when it has three.
        Assert.Equal(3, part.TotalScenes);

        // The cast is the run's, not the slice's: a persona's identity does not change with the
        // section being read.
        Assert.Equal(whole.Personas.Count, part.Personas.Count);
    }

    [Fact]
    public async Task GetScenesRefusesASectionTheTreeDoesNotHave()
    {
        using var fixture = new SegmentationServiceFixture();
        var runId = await PublishAsync(fixture, Alice);

        var failure = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.GetScenes(
            new GetScenesRequest { RunId = runId, SectionId = "sec_nonexistent" },
            SegmentationServiceFixture.CallerContext(Alice)));

        // NotFound rather than an empty reply: an empty scene list is a claim about the document,
        // and a typo in a section id should not be able to make that claim.
        Assert.Equal(StatusCode.NotFound, failure.StatusCode);
    }

    [Fact]
    public async Task ARunPublishedBeforeTheScenePhasesHasNoScenesRatherThanAnError()
    {
        using var fixture = new SegmentationServiceFixture();

        // Exactly the row an older worker wrote: a tree, paragraphs, and the columns at their
        // defaults. The UI distinguishes this from a failure, so the service must not conflate them.
        var runId = await PublishAsync(fixture, Alice, withScenes: false);

        var reply = await fixture.Service.GetScenes(
            new GetScenesRequest { RunId = runId }, SegmentationServiceFixture.CallerContext(Alice));

        Assert.Empty(reply.Scenes);
        Assert.Equal(0, reply.TotalScenes);
        Assert.Empty(reply.Personas);
        Assert.Equal("The Novel", reply.Root.Title);
    }

    [Fact]
    public async Task GetScenesRefusesARunThatHasNotPublished()
    {
        using var fixture = new SegmentationServiceFixture();
        var runId = await SubmitAsync(fixture, Alice);

        var failure = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.GetScenes(
            new GetScenesRequest { RunId = runId }, SegmentationServiceFixture.CallerContext(Alice)));

        Assert.Equal(StatusCode.FailedPrecondition, failure.StatusCode);
    }

    [Fact]
    public async Task AnotherUsersScenesAreNotFound()
    {
        using var fixture = new SegmentationServiceFixture();
        var runId = await PublishAsync(fixture, Alice);

        // Ownership comes from the token. The scene stream is the document's contents, so this is
        // the same security property GetResult has and it is asserted the same way.
        var failure = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.GetScenes(
            new GetScenesRequest { RunId = runId }, SegmentationServiceFixture.CallerContext(Bob)));

        Assert.Equal(StatusCode.NotFound, failure.StatusCode);
    }

    /// <summary>
    /// A run with a published result, seeded the way the publish phase writes one — including the
    /// enum spelling, so a change to it fails here rather than in production.
    /// </summary>
    private static async Task<string> PublishAsync(
        SegmentationServiceFixture fixture, string subject, bool withScenes = true)
    {
        var runId = await SubmitAsync(fixture, subject);
        var kitchen = Paragraph("pa_1", "“You came back,” she said from the doorway.");
        var exhibit = Paragraph("pa_2", "Figure 1. The house, from the road.");
        var framing = Paragraph("pa_3", "Years later she would tell it differently.");

        fixture.Db.RunResults.Add(new RunResult
        {
            RunId = runId,
            OwnerSubject = subject,
            DocumentId = "novel.md",
            TreeJson = JsonSerializer.Serialize(Tree(), Json),
            ParagraphsJson = JsonSerializer.Serialize(new[] { kitchen, exhibit, framing }, Json),
            ScenesJson = withScenes ? JsonSerializer.Serialize(Scenes(), SceneJson) : "[]",
            ItemsJson = withScenes ? JsonSerializer.Serialize(Items(), SceneJson) : "[]",
            RegistriesJson = withScenes ? JsonSerializer.Serialize(Registries(), SceneJson) : "{}",
            TreeHash = "0".PadLeft(64, '0'),
            FrozenAt = DateTimeOffset.UtcNow,
            PublishedAt = DateTimeOffset.UtcNow,
        });

        await fixture.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return runId;
    }

    private static CoreModel.Paragraph Paragraph(string paragraphId, string text) =>
        new(
            paragraphId,
            FirstLineId: $"{paragraphId}-first",
            LastLineId: $"{paragraphId}-last",
            Text: text,
            WordCount: text.Split(' ').Length,
            Kind: CoreModel.ParagraphKind.Body,
            ContentHash: paragraphId);

    private static CoreModel.SectionNode Tree() =>
        new(
            "sec_root", "The Novel", TitleInferred: false, HeadingLineId: null, Level: 0,
            Children:
            [
                new(
                    "sec_part", "Part One", false, null, 1,
                    Children: [new("sec_ch", "Chapter One", false, null, 2, [], ["pa_1", "pa_2"])],
                    ParagraphIds: []),
                new("sec_after", "Afterword", false, null, 1, [], ["pa_3"]),
            ],
            ParagraphIds: []);

    private static CoreModel.Scene[] Scenes() =>
    [
        new(
            "sc_kitchen", "sec_ch", "it_speech", "it_action", ["it_speech", "it_action"],
            new CoreModel.Situation(
                "pl_kitchen",
                CoreModel.Provenance.Stated(["pa_1"]),
                new CoreModel.TimeValue("the next morning", CoreModel.TimeRelation.Gap, null),
                [new CoreModel.CastEntry("pe_mara", CoreModel.CastRole.Speaking, CoreModel.Provenance.Stated(["pa_1"]))],
                CoreModel.SceneMode.Enacted,
                new CoreModel.SubjectValue("a return", null)),
            Links: [],
            Title: "The kitchen, morning",
            TitleInferred: false,
            Flags: [],
            ContentHash: "hash-kitchen")
        {
            ParagraphIds = ["pa_1"],
        },
        new(
            "sc_exhibit", "sec_ch", "it_exhibit", "it_exhibit", ["it_exhibit"],
            CoreModel.Situation.Exhibited(new CoreModel.SubjectValue("the house", null)),
            Links: [],
            Title: null,
            TitleInferred: false,
            Flags: [new CoreModel.Flag(CoreModel.Flag.UnlocatedEnactment, "no place was established")],
            ContentHash: "hash-exhibit")
        {
            ParagraphIds = ["pa_2"],
        },
        new(
            "sc_frame", "sec_after", "it_frame", "it_frame", ["it_frame"],
            new CoreModel.Situation(
                null, null, CoreModel.TimeValue.Unanchored, [],
                CoreModel.SceneMode.Narrated, new CoreModel.SubjectValue("the telling", null)),
            Links: [new CoreModel.SceneLink("sc_frame", "sc_kitchen", CoreModel.SceneLinkKind.Frames, ["pa_3"], 0.82)],
            Title: null,
            TitleInferred: false,
            Flags: [],
            ContentHash: "hash-frame")
        {
            ParagraphIds = ["pa_3"],
        },
    ];

    private static CoreModel.SceneItem[] Items() =>
    [
        new(
            "it_speech", "pa_1", 0, 16, CoreModel.ItemKind.Speech,
            new CoreModel.SpeechAttributes(
                "Mara", "pe_mara", CoreModel.Embodiment.Embodied, CoreModel.Addressee.InScene, Voiced: true),
            ExhibitId: null,
            Tags:
            [
                new CoreModel.Tag(
                    "tg_1", CoreModel.TagKind.Persona, 18, 21, "she", "pe_mara", null, 0.9),
            ],
            IsStandalone: true,
            RenderText: null,
            EvidenceIds: ["pa_1"]),
        new(
            "it_action", "pa_1", 17, 43, CoreModel.ItemKind.Action,
            Speech: null,
            ExhibitId: null,
            Tags: [],
            IsStandalone: false,
            RenderText: "Mara speaks from the doorway.",
            EvidenceIds: ["pa_1"]),
        new(
            "it_exhibit", "pa_2", 0, 35, CoreModel.ItemKind.Exhibit,
            Speech: null,
            ExhibitId: "ex_1",
            Tags:
            [
                new CoreModel.Tag(
                    "tg_2", CoreModel.TagKind.ExhibitRef, 0, 9, "Figure 1", "ex_1", null, 1.0),
            ],
            IsStandalone: true,
            RenderText: null,
            EvidenceIds: ["pa_2"]),
        new(
            "it_frame", "pa_3", 0, 42, CoreModel.ItemKind.Description,
            Speech: null, ExhibitId: null, Tags: [], IsStandalone: true, RenderText: null,
            EvidenceIds: ["pa_3"]),
    ];

    private static CoreModel.Registries Registries() =>
        new(
            [
                new CoreModel.Persona(
                    "pe_mara", "Mara", CoreModel.PersonaKind.Individual, CoreModel.PersonaScope.Persistent,
                    null, ["Mara", "she"], null, ["pa_1"]),
                new CoreModel.Persona(
                    "pe_narrator", "the narrator", CoreModel.PersonaKind.Individual,
                    CoreModel.PersonaScope.SceneLocal, null, [], null, ["pa_3"]),
            ],
            [new CoreModel.PlaceEntry("pl_kitchen", "The kitchen", ["the kitchen"], ["pa_1"])],
            [new CoreModel.ExhibitEntry("ex_1", "Figure 1", "pa_2", ["pa_2"])]);

    private static async Task<string> SubmitAsync(SegmentationServiceFixture fixture, string subject)
    {
        var created = await fixture.Service.CreateUpload(
            new CreateUploadRequest { FileName = "novel.md", SizeBytes = 4096, ContentType = "text/markdown" },
            SegmentationServiceFixture.CallerContext(subject));

        await fixture.Artifacts.WriteTextAsync(
            Storage.ArtifactKeys.Upload(subject, created.UploadId), "# Title\n\nBody.\n", "upload");

        var reply = await fixture.Service.SubmitRun(
            new SubmitRunRequest
            {
                UploadId = created.UploadId,
                DocumentId = "novel.md",
                Sensitivity = Sensitivity.Internal,
                Priority = Priority.Realtime,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
            },
            SegmentationServiceFixture.CallerContext(subject));

        return reply.RunId;
    }
}
