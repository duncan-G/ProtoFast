using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Pipeline;
using ProtoFast.Segmentation.Pipeline.Executors;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.IntegrationTests;

/// <summary>
/// The five scene phases, end to end, with no provider configured (scene plan §8.1).
///
/// <para>This is the scene half of the pipeline's local-development promise. Every one of the five
/// has a complete deterministic answer — a candidate set, a partition, a registry, a cut, an empty
/// link set — so a run with no qualified model still produces scenes rather than failing. That is
/// K7 stated as a test: <b>the pipeline can be uncertain about a scene without failing the
/// run.</b></para>
/// </summary>
public class ScenePipelineTests
{
    /// <summary>
    /// Deliberately mixed: quoted dialogue, an action clause that cannot stand alone, narration, a
    /// table, and a front-matter block — so the run exercises item typing, the standalone flag, the
    /// exhibit path and presentation classification rather than one of them.
    /// </summary>
    private const string SceneDocument = """
        # The Calibration Run

        © 2026 Ultra Motion Press. All rights reserved.

        ## 1 The morning check

        The instrument sat where it had been left, its case still open to the cold air of the
        equipment room, and the reference standard lay beside it where nobody had put it away.

        "We are not finished," Vance said as she reached for the case.

        Reed looked at the log sheet and then at the window, where the light had not yet come up
        over the ridge, and said nothing at all for a long moment.

        ## 2 Results

        Eleven of the fourteen instruments in the fleet required at least one unscheduled
        calibration over the season, which is more than the procedure anticipates.

        | Instrument | Drift |
        | A-14 | 0.3 |
        | A-15 | 1.1 |
        """;

    [Fact]
    public async Task EveryScenePhaseRunsAndLeavesItsArtifactWithNoProvider()
    {
        await using var fixture = new PipelineHostFixture();
        await fixture.ApproveFamilyAsync("unknown");
        var runId = await fixture.SubmitAsync(SceneDocument);

        var outcome = await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.Completed, outcome);

        foreach (var phase in new[]
                 {
                     PipelinePhase.ClassifyPresentation, PipelinePhase.TypeItems,
                     PipelinePhase.ResolveReferents, PipelinePhase.CutScenes, PipelinePhase.LinkScenes,
                 })
        {
            Assert.Contains(ArtifactKeys.Phase(runId, phase), fixture.Artifacts.Keys);
        }

        var run = await fixture.LoadRunAsync(runId);
        Assert.Null(run!.Error);

        Assert.All(
            run.Phases.Where(p => p.Phase is PipelinePhase.ClassifyPresentation or PipelinePhase.TypeItems
                or PipelinePhase.ResolveReferents or PipelinePhase.CutScenes or PipelinePhase.LinkScenes),
            p => Assert.Equal(PhaseState.Done, p.State));
    }

    [Fact]
    public async Task TheItemsPartitionEveryDisplayableParagraphExactly()
    {
        // item-coverage and display-integrity, over the artifact the run actually wrote rather than
        // over a fixture. They are properties of the materializer, so this asserts the wiring keeps
        // them properties.
        await using var fixture = new PipelineHostFixture();
        await fixture.ApproveFamilyAsync("unknown");
        var runId = await fixture.SubmitAsync(SceneDocument);

        await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        var frozen = await fixture.Artifacts.ReadAsync<FrozenDocument>(
            ArtifactKeys.Phase(runId, PipelinePhase.Freeze), TestContext.Current.CancellationToken);

        Assert.NotNull(frozen);
        Assert.NotEmpty(frozen!.Items);

        Assert.True(SceneChecks.CheckItemCoverage(frozen.DisplayableParagraphs, frozen.Items).Passed);
        Assert.True(SceneChecks.CheckDisplayIntegrity(frozen.DisplayableParagraphs, frozen.Items).Passed);
        Assert.True(SceneChecks.CheckTagBounds(frozen.Items).Passed);
    }

    [Fact]
    public async Task TheScenesPartitionTheItemsAndEveryHardGateIsGreenAtTheFreeze()
    {
        await using var fixture = new PipelineHostFixture();
        await fixture.ApproveFamilyAsync("unknown");
        var runId = await fixture.SubmitAsync(SceneDocument);

        await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        var frozen = await fixture.Artifacts.ReadAsync<FrozenDocument>(
            ArtifactKeys.Phase(runId, PipelinePhase.Freeze), TestContext.Current.CancellationToken);

        Assert.NotNull(frozen);
        Assert.NotEmpty(frozen!.Scenes);

        // C1–C3, and the freeze gate the executor already ran — asserted again here because the
        // claim is about the stored artifact, not about the code path that produced it.
        Assert.True(SceneChecks.CheckScenePartition(frozen.Items, frozen.Scenes).Passed);
        Assert.True(SceneChecks.CheckTreeLeafScenes(frozen.Root, frozen.Scenes).Passed);
        Assert.All(
            SceneChecks.CheckSituations(frozen.Scenes, frozen.Registries),
            result => Assert.True(result.Passed, result.ErrorReport));

        // C6: every coordinate has a value, and with no model every one of them is the
        // nothing-value. An unassigned scene is a legal scene (K7).
        Assert.All(frozen.Scenes, scene =>
        {
            Assert.True(Enum.IsDefined(scene.Situation.Mode));
            Assert.NotNull(scene.Situation.Time);
            Assert.NotNull(scene.Situation.Subject);
        });
    }

    [Fact]
    public async Task ScenesAreTheLeavesOfTheSectionTree()
    {
        // C4 and §2: a section holds sections or scenes, never both. It is what makes C12 a
        // structural fact rather than a check.
        await using var fixture = new PipelineHostFixture();
        await fixture.ApproveFamilyAsync("unknown");
        var runId = await fixture.SubmitAsync(SceneDocument);

        await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        var frozen = await fixture.Artifacts.ReadAsync<FrozenDocument>(
            ArtifactKeys.Phase(runId, PipelinePhase.Freeze), TestContext.Current.CancellationToken);

        var sectionsWithScenes = frozen!.Scenes.Select(s => s.SectionId).Distinct(StringComparer.Ordinal);

        foreach (var sectionId in sectionsWithScenes)
        {
            var node = frozen.Root.Descend().Single(n => n.SectionId == sectionId);
            Assert.True(node.IsLeaf, $"{sectionId} owns scenes and child sections");
        }
    }

    [Fact]
    public async Task TheFrontMatterIsWithheldFromTheSceneStreamAndKeptInTheRecord()
    {
        // §5.3: "removed" means absent from the scene stream. text-integrity still concatenates
        // everything, which is why the freeze passes with the copyright line classified out.
        await using var fixture = new PipelineHostFixture();
        await fixture.ApproveFamilyAsync("unknown");
        var runId = await fixture.SubmitAsync(SceneDocument);

        await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        var presentation = await fixture.Artifacts.ReadAsync<PresentationArtifact>(
            ArtifactKeys.Phase(runId, PipelinePhase.ClassifyPresentation),
            TestContext.Current.CancellationToken);

        Assert.NotNull(presentation);

        var copyright = presentation!.Presentations
            .Single(p => p.ParagraphId == CopyrightParagraphId(presentation));

        Assert.False(copyright.IsDisplayable);
        Assert.Equal(MetadataClass.FrontMatter, copyright.Class);

        var frozen = await fixture.Artifacts.ReadAsync<FrozenDocument>(
            ArtifactKeys.Phase(runId, PipelinePhase.Freeze), TestContext.Current.CancellationToken);

        // Still in the record, and no item owns it.
        Assert.Contains(frozen!.Paragraphs, p => p.ParagraphId == copyright.ParagraphId);
        Assert.DoesNotContain(frozen.Items, i => i.ParagraphId == copyright.ParagraphId);
    }

    [Fact]
    public async Task PhaseElevenMakesNoModelCallsOnADocumentWithNoFrames()
    {
        // The deterministic skip (§8.9). A phase that costs nothing on documents without frames is
        // not overhead, and this document has none.
        await using var fixture = new PipelineHostFixture();
        await fixture.ApproveFamilyAsync("unknown");
        var runId = await fixture.SubmitAsync(SceneDocument);

        await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        var links = await fixture.Artifacts.ReadAsync<SceneLinksArtifact>(
            ArtifactKeys.Phase(runId, PipelinePhase.LinkScenes), TestContext.Current.CancellationToken);

        Assert.NotNull(links);
        Assert.Equal(0, links!.ModelCalls);
    }

    [Fact]
    public async Task ReRunningTheScenePhasesIsFree()
    {
        await using var fixture = new PipelineHostFixture();
        await fixture.ApproveFamilyAsync("unknown");
        var runId = await fixture.SubmitAsync(SceneDocument);

        await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);
        var first = fixture.Artifacts.WriteCounts[ArtifactKeys.Phase(runId, PipelinePhase.CutScenes)];

        await fixture.Host.RunOrResumeAsync(runId, null, TestContext.Current.CancellationToken);

        Assert.Equal(first, fixture.Artifacts.WriteCounts[ArtifactKeys.Phase(runId, PipelinePhase.CutScenes)]);
    }

    /// <summary>The copyright line, found by its text rather than by a position this test would then pin.</summary>
    private static string CopyrightParagraphId(PresentationArtifact presentation) =>
        presentation.Presentations
            .First(p => p.Class == MetadataClass.FrontMatter)
            .ParagraphId;
}
