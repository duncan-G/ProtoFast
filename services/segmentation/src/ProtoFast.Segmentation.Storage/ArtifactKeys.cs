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

    public static string CleanBoundaries(string runId) => RunPrefix(runId) + "01_boundaries.json";

    public static string LabelWindow(string runId, int windowIndex) =>
        RunPrefix(runId) + $"03_labels/window_{windowIndex.ToString("D5", CultureInfo.InvariantCulture)}.json";

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
