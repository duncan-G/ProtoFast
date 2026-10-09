using MediaAssetRecord = ProtoFast.Data.ThePlot.Entities.MediaAsset;

namespace ProtoFast.Api.Services.Imaging;

/// <summary>An existing picture a frame could be composed from.</summary>
/// <param name="Label">How the orchestrator names it: r1, r2, …</param>
/// <param name="Name">How the author reads it: "MARA", "Frame 3", "Frame 2 of HARBOUR AT DAWN".</param>
/// <param name="Caption">What it shows, for the orchestrator and the image model.</param>
/// <param name="Stale">What it pictures has changed since it was rendered.</param>
public sealed record ReferenceCandidate(
    string Label,
    ReferenceSource Source,
    Guid SubjectId,
    MediaAssetRecord Asset,
    string Name,
    string Caption,
    bool Stale);
