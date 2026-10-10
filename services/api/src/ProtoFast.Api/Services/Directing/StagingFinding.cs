using FrameRecord = ProtoFast.Data.ThePlot.Entities.Frame;

namespace ProtoFast.Api.Services.Directing;

/// <summary>Something a verifier found wrong with a directed frame's staging.</summary>
/// <param name="Check">One of <see cref="FrameChecks"/>.</param>
/// <param name="Issue">What is wrong and what would fix it.</param>
public sealed record StagingFinding(FrameRecord Frame, string Check, string Issue);
