using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Cleaning;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Tree;
using ProtoFast.Segmentation.Core.Triage;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.Pipeline.Executors;

/// <summary>
/// The parts of <see cref="CleaningResult"/> that are not the lines themselves. Written beside
/// <c>01_clean.jsonl</c> so the cleaned text stays a plain JSONL file anyone can read, while the
/// sets and the reversible edit log travel as structured JSON (plan §9.1).
/// </summary>
public sealed record CleaningSidecar(
    IReadOnlyList<Boundary> Boundaries,
    IReadOnlyList<string> ArtifactLineIds,
    IReadOnlyDictionary<string, OtherKind> OtherLineKinds,
    IReadOnlyList<string> ContinuationLineIds,
    IReadOnlyList<string> HyphenJoinedLineIds,
    IReadOnlyList<CleaningEdit> Edits)
{
    public static CleaningSidecar From(CleaningResult result) => new(
        result.Boundaries,
        [.. result.ArtifactLineIds],
        result.OtherLineKinds,
        [.. result.ContinuationLineIds],
        [.. result.HyphenJoinedLineIds],
        result.Edits);

    public CleaningResult ToResult(IReadOnlyList<LineRecord> lines) => new(
        lines,
        Boundaries,
        ArtifactLineIds.ToHashSet(StringComparer.Ordinal),
        OtherLineKinds,
        ContinuationLineIds.ToHashSet(StringComparer.Ordinal),
        HyphenJoinedLineIds.ToHashSet(StringComparer.Ordinal),
        Edits);
}

/// <summary>
/// Phase 5's artifact: the tree, the edits it proposed, and which model produced it.
///
/// <para><see cref="Strategy"/> is recorded so ThePlot — and the A/B run of orchestrator plan §9
/// — can say which path produced a given tree without re-deriving it from the run's options,
/// which may have been changed since.</para>
/// </summary>
public sealed record TreeArtifact(SectionNode Root, IReadOnlyList<ParagraphEdit> Edits, string? ModelKey)
{
    public StructureStrategy Strategy { get; init; } = StructureStrategy.Chunked;
}

/// <summary>
/// One window agent's reply, as stored (orchestrator plan §5). The subtree is kept whole rather
/// than as an outline: the orchestrator only ever sees the outline, but the materializer needs
/// every node, and a resumed run must not have to re-ask for them.
/// </summary>
public sealed record StructureWindowArtifact(
    int WindowIndex,
    SectionNode Root,
    IReadOnlyList<ParagraphEdit> Edits,
    IReadOnlyList<string> OpenQuestions,
    string? ModelKey);

/// <summary>Phase 12's artifact.</summary>
public sealed record ReviewArtifact(IReadOnlyList<Finding> Findings, string Verdict);

/// <summary>
/// Phase 5's artifact (scene plan §8.5). <see cref="CompositionFamily"/> is carried here rather
/// than only on the run row because it is what this phase <em>re-confirmed</em> from paragraph-level
/// evidence, and a reader of the artifact should not have to trust that the row still says the same.
/// </summary>
public sealed record PresentationArtifact(
    IReadOnlyList<ParagraphPresentation> Presentations,
    string CompositionFamily,
    string? ModelKey);

/// <summary>Phase 9's artifact: the three registries and which models produced them.</summary>
public sealed record RegistriesArtifact(
    Registries Registries,
    string? WindowerModelKey,
    string? OrchestratorModelKey);

/// <summary>
/// Phase 10's artifact. The two counters are measurements the plan asks for rather than decoration:
/// <see cref="SuppressedByFloor"/> is <c>scene-cuts-suppressed-by-floor</c> (§8.8 rule 3), and the
/// <c>Continues</c> pair is split by boundary provenance because a link across a trusted heading is
/// expected while one across an inferred boundary is evidence the inference was wrong [unit §9].
/// </summary>
public sealed record ScenesArtifact(
    IReadOnlyList<Scene> Scenes,
    int SuppressedByFloor,
    int ContinuesAcrossTrustedHeadings,
    int ContinuesAcrossInferredBoundaries,
    string? ModelKey);

/// <summary>Phase 11's artifact. <see cref="ModelCalls"/> is zero on a skipped run (§8.9).</summary>
public sealed record SceneLinksArtifact(
    IReadOnlyList<SceneLink> Links,
    int ModelCalls,
    string? WindowerModelKey,
    string? OrchestratorModelKey);

/// <summary>
/// Phase 9's artifact — the frozen document (plan §9.11). Written with an object-lock retention,
/// which is what makes "frozen" a fact about storage rather than a convention in code.
/// </summary>
public sealed record FrozenDocument(
    string RunId,
    string DocumentId,
    string DocumentTitle,
    SectionNode Root,
    IReadOnlyList<Paragraph> Paragraphs,
    IReadOnlyList<HeadingRecord> Headings,
    string TreeHash,
    string ParagraphsHash,
    DateTimeOffset FrozenAt)
{
    /// <summary>
    /// The scene layers, frozen under the same hash as the paragraphs and the tree (C10).
    ///
    /// <para>Init-only rather than positional because a run that predates the scene phases — or one
    /// whose document yielded no displayable text — freezes with all of them empty, and a
    /// constructor that required them would say that case is an error. It is not: a frozen document
    /// with no scenes is a frozen document.</para>
    /// </summary>
    public IReadOnlyList<ParagraphPresentation> Presentations { get; init; } = [];

    public IReadOnlyList<FamilyScope> FamilyScopes { get; init; } = [];

    public IReadOnlyList<SceneItem> Items { get; init; } = [];

    public Registries Registries { get; init; } = Registries.Empty;

    public IReadOnlyList<Scene> Scenes { get; init; } = [];

    /// <summary>Displayable paragraphs, the only ones items exist over (§2, C1).</summary>
    public IReadOnlyList<Paragraph> DisplayableParagraphs
    {
        get
        {
            if (Presentations.Count == 0)
            {
                return Paragraphs;
            }

            var displayable = Presentations
                .Where(p => p.IsDisplayable)
                .Select(p => p.ParagraphId)
                .ToHashSet(StringComparer.Ordinal);

            return [.. Paragraphs.Where(p => displayable.Contains(p.ParagraphId))];
        }
    }
}

/// <summary>Phase 12's artifact: everything a reader of the finished run needs.</summary>
public sealed record ResultArtifact(
    string RunId,
    string DocumentId,
    SectionNode Root,
    IReadOnlyList<Paragraph> Paragraphs,
    IReadOnlyList<AugmentationRecord> Augmentations,
    IReadOnlyList<Finding> Findings,
    string TreeHash,
    DateTimeOffset FrozenAt,
    DateTimeOffset PublishedAt)
{
    /// <summary>
    /// The scene layers, carried so this artifact and <c>run_results</c> say the same thing.
    /// Init-only for the reason <see cref="FrozenDocument"/>'s are: a run that published before
    /// the scene phases existed is a valid artifact with none of them.
    /// </summary>
    public IReadOnlyList<Scene> Scenes { get; init; } = [];

    public IReadOnlyList<SceneItem> Items { get; init; } = [];

    public Registries Registries { get; init; } = Registries.Empty;
}

public sealed record AugmentationRecord(string ParagraphId, string Type, string Json, string ReviewVerdict);

/// <summary>One turn of the orchestration, as written to <c>05_structure/chat.jsonl</c>.</summary>
public sealed record TranscriptEntry(int Round, string Role, string Text);

/// <summary>
/// Typed reads and writes of the phase artifacts.
///
/// <para>It exists so the key layout and the serialized shape of each phase live next to each
/// other rather than being restated at every executor. An executor that built its own key would
/// be outside the bucket's lifecycle rules and outside the IAM prefix condition (plan §19).</para>
/// </summary>
public sealed class RunArtifacts(IArtifactStore store)
{
    public IArtifactStore Store => store;

    public Task<ArtifactRef> WriteLinesAsync(string runId, IReadOnlyList<LineRecord> lines, string key, CancellationToken ct) =>
        store.WriteJsonLinesAsync(ArtifactKeys.Phase(runId, PipelinePhase.Ingest), lines, key, ct);

    public Task<IReadOnlyList<LineRecord>> ReadLinesAsync(string runId, CancellationToken ct) =>
        store.ReadJsonLinesAsync<LineRecord>(ArtifactKeys.Phase(runId, PipelinePhase.Ingest), ct);

    public Task<ArtifactRef> WriteStatisticsAsync(string runId, DocumentStatistics statistics, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.IngestStats(runId), statistics, key, ct);

    public async Task<DocumentStatistics> ReadStatisticsAsync(string runId, CancellationToken ct) =>
        await store.ReadAsync<DocumentStatistics>(ArtifactKeys.IngestStats(runId), ct) ?? DocumentStatistics.Empty;

    public async Task<(ArtifactRef Clean, ArtifactRef Sidecar)> WriteCleaningAsync(
        string runId, CleaningResult cleaning, string key, CancellationToken ct)
    {
        var clean = await store.WriteJsonLinesAsync(
            ArtifactKeys.Phase(runId, PipelinePhase.Clean), cleaning.Lines, key, ct);
        var sidecar = await store.WriteAsync(
            ArtifactKeys.CleanBoundaries(runId), CleaningSidecar.From(cleaning), key, ct);
        return (clean, sidecar);
    }

    public async Task<CleaningResult?> ReadCleaningAsync(string runId, CancellationToken ct)
    {
        var lines = await store.ReadJsonLinesAsync<LineRecord>(ArtifactKeys.Phase(runId, PipelinePhase.Clean), ct);
        var sidecar = await store.ReadAsync<CleaningSidecar>(ArtifactKeys.CleanBoundaries(runId), ct);
        return lines.Count == 0 || sidecar is null ? null : sidecar.ToResult(lines);
    }

    public Task<ArtifactRef> WriteTriageAsync(string runId, TriageResult triage, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.Phase(runId, PipelinePhase.Triage), triage, key, ct);

    public Task<TriageResult?> ReadTriageAsync(string runId, CancellationToken ct) =>
        store.ReadAsync<TriageResult>(ArtifactKeys.Phase(runId, PipelinePhase.Triage), ct);

    public Task<ArtifactRef> WriteWindowLabelsAsync(
        string runId, int windowIndex, IReadOnlyList<LineLabelResult> labels, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.LabelWindow(runId, windowIndex), labels, key, ct);

    public Task<IReadOnlyList<LineLabelResult>?> ReadWindowLabelsAsync(string runId, int windowIndex, CancellationToken ct) =>
        store.ReadAsync<IReadOnlyList<LineLabelResult>>(ArtifactKeys.LabelWindow(runId, windowIndex), ct);

    public Task<ArtifactRef> WriteLabelsAsync(string runId, IReadOnlyList<LineLabelResult> labels, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.Phase(runId, PipelinePhase.Label), labels, key, ct);

    public async Task<IReadOnlyList<LineLabelResult>> ReadLabelsAsync(string runId, CancellationToken ct) =>
        await store.ReadAsync<IReadOnlyList<LineLabelResult>>(ArtifactKeys.Phase(runId, PipelinePhase.Label), ct) ?? [];

    public async Task<(ArtifactRef Paragraphs, ArtifactRef Headings)> WriteAssemblyAsync(
        string runId, AssemblyResult assembly, string key, CancellationToken ct)
    {
        var paragraphs = await store.WriteJsonLinesAsync(
            ArtifactKeys.Phase(runId, PipelinePhase.Assemble), assembly.Paragraphs, key, ct);
        var headings = await store.WriteAsync(
            ArtifactKeys.AssemblyHeadings(runId), assembly.Headings, key, ct);

        // The outliers are part of the phase's output, not a detail of the object that produced
        // them: phases 6 and 9 waive exactly these ids, and every one of them reads the assembly
        // back from storage. Dropping the list here made the waiver empty and turned every
        // flagged paragraph into a freeze failure (plan §9.6).
        await store.WriteAsync(
            ArtifactKeys.AssemblySizeOutliers(runId), assembly.SizeOutlierParagraphIds, key, ct);

        return (paragraphs, headings);
    }

    public async Task<AssemblyResult?> ReadAssemblyAsync(string runId, CancellationToken ct)
    {
        var paragraphs = await store.ReadJsonLinesAsync<Paragraph>(
            ArtifactKeys.Phase(runId, PipelinePhase.Assemble), ct);
        var headings = await store.ReadAsync<IReadOnlyList<HeadingRecord>>(
            ArtifactKeys.AssemblyHeadings(runId), ct);
        var outliers = await store.ReadAsync<IReadOnlyList<string>>(
            ArtifactKeys.AssemblySizeOutliers(runId), ct);

        return paragraphs.Count == 0 ? null : new AssemblyResult(paragraphs, headings ?? [], outliers ?? []);
    }

    public Task<ArtifactRef> WriteTreeAsync(string runId, TreeArtifact tree, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.Phase(runId, PipelinePhase.InferStructure), tree, key, ct);

    public Task<TreeArtifact?> ReadTreeAsync(string runId, CancellationToken ct) =>
        store.ReadAsync<TreeArtifact>(ArtifactKeys.Phase(runId, PipelinePhase.InferStructure), ct);

    public Task<ArtifactRef> WriteStructureWindowAsync(
        string runId, StructureWindowArtifact window, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.StructureWindow(runId, window.WindowIndex), window, key, ct);

    public Task<StructureWindowArtifact?> ReadStructureWindowAsync(
        string runId, int windowIndex, CancellationToken ct) =>
        store.ReadAsync<StructureWindowArtifact>(ArtifactKeys.StructureWindow(runId, windowIndex), ct);

    public Task<ArtifactRef> WriteAssemblyPlanAsync(
        string runId, AssemblyPlan plan, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.AssemblyPlan(runId), plan, key, ct);

    /// <summary>
    /// The orchestration transcript, one message per line. Written for audit rather than for
    /// resume (orchestrator plan §6), which is why it is appended whole at the end of the phase
    /// instead of after each round.
    /// </summary>
    public Task<ArtifactRef> WriteChatTranscriptAsync(
        string runId, IReadOnlyList<TranscriptEntry> transcript, string key, CancellationToken ct) =>
        store.WriteJsonLinesAsync(ArtifactKeys.StructureChat(runId), transcript, key, ct);

    public Task<ArtifactRef> WriteCapabilityGapsAsync(
        string runId, IReadOnlyList<CapabilityGapProposal> gaps, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.CapabilityGaps(runId), gaps, key, ct);

    // ---- The five scene phases (scene plan §8.1) ----------------------------------------------

    public Task<ArtifactRef> WritePresentationAsync(
        string runId, PresentationArtifact presentation, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.Phase(runId, PipelinePhase.ClassifyPresentation), presentation, key, ct);

    public Task<PresentationArtifact?> ReadPresentationAsync(string runId, CancellationToken ct) =>
        store.ReadAsync<PresentationArtifact>(
            ArtifactKeys.Phase(runId, PipelinePhase.ClassifyPresentation), ct);

    public Task<ArtifactRef> WriteFamilyEvidenceAsync(
        string runId, IReadOnlyList<FamilyEvidence> evidence, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.FamilyEvidence(runId), evidence, key, ct);

    public async Task<IReadOnlyList<FamilyEvidence>> ReadFamilyEvidenceAsync(string runId, CancellationToken ct) =>
        await store.ReadAsync<IReadOnlyList<FamilyEvidence>>(ArtifactKeys.FamilyEvidence(runId), ct) ?? [];

    public Task<ArtifactRef> WriteFamilyScopesAsync(
        string runId, IReadOnlyList<FamilyScope> scopes, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.FamilyScopes(runId), scopes, key, ct);

    public async Task<IReadOnlyList<FamilyScope>> ReadFamilyScopesAsync(string runId, CancellationToken ct) =>
        await store.ReadAsync<IReadOnlyList<FamilyScope>>(ArtifactKeys.FamilyScopes(runId), ct) ?? [];

    public Task<ArtifactRef> WriteItemWindowAsync(
        string runId, Agents.ItemWindowResult window, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.ItemWindow(runId, window.WindowIndex), window, key, ct);

    public Task<Agents.ItemWindowResult?> ReadItemWindowAsync(string runId, int windowIndex, CancellationToken ct) =>
        store.ReadAsync<Agents.ItemWindowResult>(ArtifactKeys.ItemWindow(runId, windowIndex), ct);

    public Task<ArtifactRef> WriteItemsAsync(
        string runId, IReadOnlyList<SceneItem> items, string key, CancellationToken ct) =>
        store.WriteJsonLinesAsync(ArtifactKeys.Phase(runId, PipelinePhase.TypeItems), items, key, ct);

    public Task<IReadOnlyList<SceneItem>> ReadItemsAsync(string runId, CancellationToken ct) =>
        store.ReadJsonLinesAsync<SceneItem>(ArtifactKeys.Phase(runId, PipelinePhase.TypeItems), ct);

    /// <summary>
    /// The items with every tag bound. A second artifact rather than an overwrite of the phase-8
    /// one, so "what did typing produce" and "what did resolution produce" stay separately
    /// inspectable — which is the pipeline's rule that every phase leaves its own artifact.
    /// </summary>
    public Task<ArtifactRef> WriteBoundItemsAsync(
        string runId, IReadOnlyList<SceneItem> items, string key, CancellationToken ct) =>
        store.WriteJsonLinesAsync(ArtifactKeys.BoundItems(runId), items, key, ct);

    public Task<IReadOnlyList<SceneItem>> ReadBoundItemsAsync(string runId, CancellationToken ct) =>
        store.ReadJsonLinesAsync<SceneItem>(ArtifactKeys.BoundItems(runId), ct);

    public Task<ArtifactRef> WritePersonaWindowAsync(
        string runId, Core.Personas.CandidateWindow window, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.PersonaWindow(runId, window.WindowIndex), window, key, ct);

    public Task<Core.Personas.CandidateWindow?> ReadPersonaWindowAsync(
        string runId, int windowIndex, CancellationToken ct) =>
        store.ReadAsync<Core.Personas.CandidateWindow>(ArtifactKeys.PersonaWindow(runId, windowIndex), ct);

    public Task<ArtifactRef> WriteRegistriesAsync(
        string runId, RegistriesArtifact registries, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.Phase(runId, PipelinePhase.ResolveReferents), registries, key, ct);

    public Task<RegistriesArtifact?> ReadRegistriesAsync(string runId, CancellationToken ct) =>
        store.ReadAsync<RegistriesArtifact>(ArtifactKeys.Phase(runId, PipelinePhase.ResolveReferents), ct);

    public Task<ArtifactRef> WriteRegistryPlanAsync(
        string runId, Core.Personas.RegistryPlan plan, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.RegistryPlan(runId), plan, key, ct);

    public Task<ArtifactRef> WritePersonaChatAsync(
        string runId, IReadOnlyList<TranscriptEntry> transcript, string key, CancellationToken ct) =>
        store.WriteJsonLinesAsync(ArtifactKeys.PersonaChat(runId), transcript, key, ct);

    public Task<ArtifactRef> WriteScenesAsync(
        string runId, ScenesArtifact scenes, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.Phase(runId, PipelinePhase.CutScenes), scenes, key, ct);

    public Task<ScenesArtifact?> ReadScenesAsync(string runId, CancellationToken ct) =>
        store.ReadAsync<ScenesArtifact>(ArtifactKeys.Phase(runId, PipelinePhase.CutScenes), ct);

    public Task<ArtifactRef> WriteSceneLinkWindowAsync(
        string runId, Core.Scenes.SceneLinkWindowResult window, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.SceneLinkWindow(runId, window.WindowIndex), window, key, ct);

    public Task<Core.Scenes.SceneLinkWindowResult?> ReadSceneLinkWindowAsync(
        string runId, int windowIndex, CancellationToken ct) =>
        store.ReadAsync<Core.Scenes.SceneLinkWindowResult>(
            ArtifactKeys.SceneLinkWindow(runId, windowIndex), ct);

    public Task<ArtifactRef> WriteSceneLinksAsync(
        string runId, SceneLinksArtifact links, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.Phase(runId, PipelinePhase.LinkScenes), links, key, ct);

    public Task<SceneLinksArtifact?> ReadSceneLinksAsync(string runId, CancellationToken ct) =>
        store.ReadAsync<SceneLinksArtifact>(ArtifactKeys.Phase(runId, PipelinePhase.LinkScenes), ct);

    public Task<ArtifactRef> WriteSceneLinkPlanAsync(
        string runId, Core.Scenes.SceneLinkPlan plan, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.SceneLinkPlan(runId), plan, key, ct);

    public Task<ArtifactRef> WriteSceneLinkChatAsync(
        string runId, IReadOnlyList<TranscriptEntry> transcript, string key, CancellationToken ct) =>
        store.WriteJsonLinesAsync(ArtifactKeys.SceneLinkChat(runId), transcript, key, ct);

    public Task<ArtifactRef> WriteValidationAsync(string runId, ValidationReport report, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.Phase(runId, PipelinePhase.Validate), report, key, ct);

    public Task<ArtifactRef> WriteReviewAsync(string runId, ReviewArtifact review, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.Phase(runId, PipelinePhase.ReviewStructure), review, key, ct);

    public Task<ReviewArtifact?> ReadReviewAsync(string runId, CancellationToken ct) =>
        store.ReadAsync<ReviewArtifact>(ArtifactKeys.Phase(runId, PipelinePhase.ReviewStructure), ct);

    public Task<ArtifactRef> WriteDecisionAsync(string runId, ReviewDecision decision, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.Phase(runId, PipelinePhase.HumanGate), decision, key, ct);

    /// <summary>
    /// Writes the frozen document under the run prefix <em>and</em> under the stable
    /// <c>frozen/</c> pointer. The run copy carries the object-lock retention; the pointer is the
    /// read path, and is named by tree hash so re-publishing the same structure is a no-op
    /// (plan §16.1).
    /// </summary>
    public async Task<ArtifactRef> WriteFrozenAsync(
        string runId, FrozenDocument frozen, string key, CancellationToken ct)
    {
        var artifact = await store.WriteFrozenAsync(
            ArtifactKeys.Phase(runId, PipelinePhase.Freeze), frozen, key, ct);

        await store.WriteAsync(
            ArtifactKeys.FrozenPointer(frozen.DocumentId, frozen.TreeHash), frozen, key, ct);

        return artifact;
    }

    public Task<FrozenDocument?> ReadFrozenAsync(string runId, CancellationToken ct) =>
        store.ReadAsync<FrozenDocument>(ArtifactKeys.Phase(runId, PipelinePhase.Freeze), ct);

    public Task<ArtifactRef> WriteAugmentationAsync(
        string runId, string type, AugmentationRecord record, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.Augmentation(runId, type, record.ParagraphId), record, key, ct);

    public Task<AugmentationRecord?> ReadAugmentationAsync(
        string runId, string type, string paragraphId, CancellationToken ct) =>
        store.ReadAsync<AugmentationRecord>(ArtifactKeys.Augmentation(runId, type, paragraphId), ct);

    public Task<ArtifactRef> WriteResultAsync(string runId, ResultArtifact result, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.Phase(runId, PipelinePhase.Publish), result, key, ct);
}
