using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Engine.Executors;

public sealed record StageResult(
    ArtifactRef Output,
    TraceRef? Trace,
    Cost Cost,
    IReadOnlyList<Decision> Decisions);
