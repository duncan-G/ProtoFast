using System.Globalization;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Storage;

/// <summary>
/// The S3 key layout of plan §16.1, in one place.
///
/// <para>Keys are not cosmetic here: the bucket's lifecycle rules key off the <c>uploads/</c> and
/// <c>runs/</c> prefixes, and the IAM policy's <c>s3:prefix</c> condition lists them explicitly
/// (plan §19.2, §19.4). A key built ad hoc at a call site would be denied by IAM or silently
/// exempt from expiry, so every key in the system comes from here.</para>
/// </summary>
public static class ArtifactKeys
{
    public const string UploadsPrefix = "uploads/";
    public const string RunsPrefix = "runs/";
    public const string FrozenPrefix = "frozen/";
    public const string EvalPrefix = "eval/";

    public static string Upload(string ownerSubject, string uploadId) =>
        $"{UploadsPrefix}{ownerSubject}/{uploadId}.md";

    public static string UploadLayout(string ownerSubject, string uploadId) =>
        $"{UploadsPrefix}{ownerSubject}/{uploadId}.layout.json";

    /// <summary>
    /// The original the browser uploaded (ingest plan §13). For a <c>.md</c> upload this IS
    /// <see cref="Upload"/> — the passthrough case is literally "the source is already the
    /// markdown", which is what lets phase 0 skip the converter without a second code path.
    ///
    /// <para><paramref name="extension"/> comes from <c>SourceFormats</c>, never from the
    /// uploaded filename: a filename is attacker-controlled and this string ends up both in the
    /// key and in the signed POST policy.</para>
    /// </summary>
    public static string UploadSource(string ownerSubject, string uploadId, string extension) =>
        $"{UploadsPrefix}{ownerSubject}/{uploadId}{extension}";

    /// <summary>The converter's report for this upload (ingest plan appendix B).</summary>
    public static string UploadConversion(string ownerSubject, string uploadId) =>
        $"{UploadsPrefix}{ownerSubject}/{uploadId}.conversion.json";

    public static string RunPrefix(string runId) => $"{RunsPrefix}{runId}/";

    public static string Checkpoints(string runId) => $"{RunPrefix(runId)}_checkpoints/";

    public static string Checkpoint(string runId, string checkpointId) =>
        $"{Checkpoints(runId)}{checkpointId}.json";

    public static string CheckpointIndex(string runId) => $"{Checkpoints(runId)}index.json";

    /// <summary>The phase's own artifact, named by the plan's phase map (§9.1).</summary>
    public static string Phase(string runId, PipelinePhase phase) =>
        RunPrefix(runId) + phase switch
        {
            PipelinePhase.Ingest => "00_lines.jsonl",
            PipelinePhase.Clean => "01_clean.jsonl",
            PipelinePhase.Triage => "02_triage.json",
            PipelinePhase.Label => "03_labels_merged.json",
            PipelinePhase.Assemble => "04_paragraphs.jsonl",
            PipelinePhase.InferStructure => "05_tree.json",
            PipelinePhase.Validate => "06_validation.json",
            PipelinePhase.ReviewStructure => "07_review.json",
            PipelinePhase.HumanGate => "08_decision.json",
            PipelinePhase.Freeze => "09_frozen.json",
            PipelinePhase.Augment => "10_augmented/index.json",
            PipelinePhase.ReviewAugmentation => "11_aug_review.json",
            PipelinePhase.Publish => "12_result.json",
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "unknown phase"),
        };

    public static string IngestStats(string runId) => RunPrefix(runId) + "00_stats.json";

    /// <summary>Phase 0's copy of the Markdown it read, so a re-run does not depend on the upload.</summary>
    public static string RunSource(string runId) => RunPrefix(runId) + "00_source.md";

    /// <summary>Phase 0's copy of the layout, kept for the same reason as <see cref="RunSource"/>.</summary>
    public static string RunSourceLayout(string runId) => RunPrefix(runId) + "00_source_layout.json";

    /// <summary>Phase 0's copy of the conversion report; absent for a passthrough upload.</summary>
    public static string RunConversion(string runId) => RunPrefix(runId) + "00_conversion.json";

    public static string CleanBoundaries(string runId) => RunPrefix(runId) + "01_boundaries.json";

    /// <summary>Phase 4's headings, beside the paragraphs JSONL.</summary>
    public static string AssemblyHeadings(string runId) => RunPrefix(runId) + "04_headings.json";

    /// <summary>
    /// Phase 4's size outliers. These travel with the assembly rather than being recomputed at
    /// read time: <c>size-bounds</c> waives the paragraphs assembly flagged, so a paragraph a
    /// later phase made oversized still fails the check (plan §9.6, §9.8).
    /// </summary>
    public static string AssemblySizeOutliers(string runId) => RunPrefix(runId) + "04_size_outliers.json";

    public static string LabelWindow(string runId, int windowIndex) =>
        RunPrefix(runId) + $"03_labels/window_{windowIndex.ToString("D5", CultureInfo.InvariantCulture)}.json";

    /// <summary>
    /// One window agent's subtree (orchestrator plan §5). Beside the phase's own artifact rather
    /// than inside it, so a resumed run re-reads the windows it had finished for free — the same
    /// mechanism that makes a mid-run deploy cheap for labelling.
    /// </summary>
    public static string StructureWindow(string runId, int windowIndex) =>
        RunPrefix(runId) + $"05_structure/window_{windowIndex.ToString("D5", CultureInfo.InvariantCulture)}.json";

    /// <summary>The orchestrator's assembly plan, kept as the audit record of how the tree was built.</summary>
    public static string AssemblyPlan(string runId) => RunPrefix(runId) + "05_structure/plan.json";

    /// <summary>
    /// The orchestration transcript. An audit record for the experiment, deliberately not resume
    /// state: the loop is one superstep, and a worker that dies mid-conversation replays the
    /// orchestrator rounds against outlines rather than re-reading the document
    /// (orchestrator plan §6).
    /// </summary>
    public static string StructureChat(string runId) => RunPrefix(runId) + "05_structure/chat.jsonl";

    /// <summary>The capability gaps this run's orchestrator reported (orchestrator plan §12.3(d)).</summary>
    public static string CapabilityGaps(string runId) => RunPrefix(runId) + "05_structure/gaps.json";

    public static string Augmentation(string runId, string augmentationType, string paragraphId) =>
        RunPrefix(runId) + $"10_augmented/{augmentationType}/{paragraphId}.json";

    /// <summary>
    /// The stable read path for frozen output. Named by tree hash, so the same structure written
    /// twice is the same object and re-publishing is idempotent (plan §16.1).
    /// </summary>
    public static string FrozenPointer(string documentId, string treeHash) =>
        $"{FrozenPrefix}{documentId}/{treeHash}.json";

    public static string GoldDocument(string split, string documentId) =>
        $"{EvalPrefix}gold/{split}/{documentId}.json";

    public static string EvaluationReport(string timestamp, string name) =>
        $"{EvalPrefix}reports/{timestamp}/{name}";

    /// <summary>
    /// True when <paramref name="key"/> is inside this run's prefix. <c>GetArtifact</c> presigns
    /// only keys that pass this <em>and</em> belong to a run the caller owns — a request naming a
    /// key outside its own run must not be able to read another user's document (plan §24.1).
    /// </summary>
    public static bool BelongsToRun(string key, string runId) =>
        key.StartsWith(RunPrefix(runId), StringComparison.Ordinal)
        && !key.Contains("..", StringComparison.Ordinal);
}
