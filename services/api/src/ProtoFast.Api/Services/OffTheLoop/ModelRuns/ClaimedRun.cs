using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop.Entities;

namespace ProtoFast.Api.Services.OffTheLoop.ModelRuns;

/// <summary>A run a worker holds the lease on.</summary>
public sealed record ClaimedRun(
    Guid Id,
    string UserId,
    ModelRunKind Kind,
    string Label,
    RunWork Work,
    RunProgress Progress,
    int Attempts,
    string? TraceParent);
