namespace ProtoFast.DocumentImport.Engine;

public sealed record StageResult(
    ArtifactRef Output,
    TraceRef? Trace,
    Cost Cost,
    IReadOnlyList<Decision> Decisions);
