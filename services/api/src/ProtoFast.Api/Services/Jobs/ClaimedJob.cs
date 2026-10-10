namespace ProtoFast.Api.Services.Jobs;

/// <summary>A job a worker holds the lease on.</summary>
public sealed record ClaimedJob(
    Guid Id,
    string UserId,
    string Label,
    JobSteps Steps,
    int StepsDone,
    bool FollowVariations,
    JobResults Results,
    int Attempts,
    string? TraceParent);
