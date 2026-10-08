using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Storage;

/// <param name="Passed">The last non-shadow attempt at every stage passed, and there was at least one.</param>
/// <param name="Cost">USD over every recorded attempt, shadows included.</param>
/// <param name="SourceId">The source whose progress names this run; null for shadow runs and superseded attempts.</param>
public sealed record RunHeader(
    string RunId,
    DocumentSignature DocumentSignature,
    RunMode Mode,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? AbandonedAt,
    string? Failure,
    TraceRef? Trace,
    int StageAttempts,
    bool Passed,
    decimal Cost,
    string? SourceId)
{
    public RunStatus Status =>
        AbandonedAt is not null ? RunStatus.Abandoned : ClosedAt is not null ? RunStatus.Closed : RunStatus.Open;
}
