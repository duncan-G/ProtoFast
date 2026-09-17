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
    /// Refused above this. The presigned URL itself cannot enforce a size, so the limit is
    /// asserted here and again at ingest — a browser that ignores it wastes its own bandwidth and
    /// the object expires in seven days.
    /// </summary>
    public long MaxUploadBytes { get; set; } = 64 * 1024 * 1024;

    /// <summary>How often <c>WatchRun</c> polls <c>run_events</c> for new rows.</summary>
    public TimeSpan WatchPollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Augmentation types a run may request. Empty means "any the worker has registered".</summary>
    public List<string> AllowedAugmentations { get; set; } = [];
}
