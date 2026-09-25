namespace ProtoFast.DocumentImport.Engine;

public sealed record RunSummary(
    string RunId, Signature Signature, RunMode Mode,
    IReadOnlyList<StageRecord> Stages, TraceRef? Trace,
    IReadOnlyList<Decision> Decisions);       // the loop owner's, not an executor's
