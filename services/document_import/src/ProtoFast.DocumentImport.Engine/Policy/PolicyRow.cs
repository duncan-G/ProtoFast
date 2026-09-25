namespace ProtoFast.DocumentImport.Engine;

public sealed record PolicyRow(
    string Family,
    string StageId,
    IReadOnlyDictionary<Tier, ExecutorRef> Ladder,
    Tier Primary,
    Confidence Confidence,
    Tier? Shadow,
    Confidence ShadowConfidence,
    DateTimeOffset UpdatedAt)
{
    public static PolicyRow Default(string family, string stageId, ExecutorRef orchestrator, DateTimeOffset at) =>
        new(
            family,
            stageId,
            new Dictionary<Tier, ExecutorRef> { [Tier.Orchestrator] = orchestrator },
            Tier.Orchestrator,
            Confidence.Prior,
            null,
            Confidence.Prior,
            at);
}
