namespace ProtoFast.DocumentImport.Engine.Storage;

/// <param name="Decisions">The loop owner's, in ledger order; each stage's own sit on its record.</param>
public sealed record RunDetail(
    RunHeader Header,
    IReadOnlyList<RecordedStage> Stages,
    IReadOnlyList<RecordedDecision> Decisions,
    RunProgress? Progress,
    int MessageCount);
