using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Cleaning;
using ProtoFast.Segmentation.Core.Model;
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

/// <summary>Phase 5's artifact: the tree, the edits it proposed, and which model produced it.</summary>
public sealed record TreeArtifact(SectionNode Root, IReadOnlyList<ParagraphEdit> Edits, string? ModelKey);

/// <summary>Phase 7's artifact.</summary>
public sealed record ReviewArtifact(IReadOnlyList<Finding> Findings, string Verdict);

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
    DateTimeOffset FrozenAt);

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
    DateTimeOffset PublishedAt);

public sealed record AugmentationRecord(string ParagraphId, string Type, string Json, string ReviewVerdict);

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
            ArtifactKeys.RunPrefix(runId) + "04_headings.json", assembly.Headings, key, ct);
        return (paragraphs, headings);
    }

    public async Task<AssemblyResult?> ReadAssemblyAsync(string runId, CancellationToken ct)
    {
        var paragraphs = await store.ReadJsonLinesAsync<Paragraph>(
            ArtifactKeys.Phase(runId, PipelinePhase.Assemble), ct);
        var headings = await store.ReadAsync<IReadOnlyList<HeadingRecord>>(
            ArtifactKeys.RunPrefix(runId) + "04_headings.json", ct);

        return paragraphs.Count == 0 ? null : new AssemblyResult(paragraphs, headings ?? [], []);
    }

    public Task<ArtifactRef> WriteTreeAsync(string runId, TreeArtifact tree, string key, CancellationToken ct) =>
        store.WriteAsync(ArtifactKeys.Phase(runId, PipelinePhase.InferStructure), tree, key, ct);

    public Task<TreeArtifact?> ReadTreeAsync(string runId, CancellationToken ct) =>
        store.ReadAsync<TreeArtifact>(ArtifactKeys.Phase(runId, PipelinePhase.InferStructure), ct);

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
