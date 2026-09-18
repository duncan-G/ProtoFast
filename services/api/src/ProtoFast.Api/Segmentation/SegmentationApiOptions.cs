namespace ProtoFast.Api;

/// <summary>Bound from <c>Api_Segmentation__*</c> (plan §20.1).</summary>
public sealed class SegmentationApiOptions
{
    public const string SectionName = "Segmentation";

    /// <summary>Realm role required to see and decide other people's review tasks.</summary>
    public string ReviewerRole { get; set; } = "segmentation-reviewer";

    /// <summary>Realm role required to read the model registry.</summary>
    public string AdminRole { get; set; } = "segmentation-admin";

    /// <summary>
    /// Refused above this, and refused <em>by S3</em>: the number becomes the
    /// <c>content-length-range</c> condition of the signed POST policy, which S3 evaluates against
    /// the bytes that actually arrive and answers with <c>EntityTooLarge</c>. The check here only
    /// avoids minting a doomed URL for a caller that already declared too large a size — it is the
    /// policy, not this property, that a client cannot skip (ingest plan §7).
    /// </summary>
    public long MaxUploadBytes { get; set; } = ProtoFast.Segmentation.Core.Ingest.SourceFormats.DefaultMaxBytes;

    /// <summary>How often <c>WatchRun</c> polls <c>run_events</c> for new rows.</summary>
    public TimeSpan WatchPollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Augmentation types a run may request. Empty means "any the worker has registered".</summary>
    public List<string> AllowedAugmentations { get; set; } = [];
}
