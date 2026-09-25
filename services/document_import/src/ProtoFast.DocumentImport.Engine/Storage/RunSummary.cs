using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Storage;

public sealed record RunSummary(
    string RunId, Signature Signature, RunMode Mode,
    IReadOnlyList<StageRecord> Stages, TraceRef? Trace,
    IReadOnlyList<Decision> Decisions);       // the loop owner's, not an executor's
