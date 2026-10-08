namespace ProtoFast.DocumentImport.Engine.Executors;

public sealed record ExecutorSpec(
    ExecutorRef Ref,
    Tier Tier,
    string? ModelClass,
    PlaybookRef? Playbook,
    IReadOnlyList<string> Tools,
    string? CodeAssembly,
    ExecutorOrigin Origin,
    bool Promoted)
{
    // An agent-tier executor the agent defined has already run under verifiers; code and
    // distilled executors wait for a human.
    public bool PromotedOnPublish => Origin switch
    {
        ExecutorOrigin.Seed => Promoted,
        ExecutorOrigin.AgentDefined => CodeAssembly is null && Tier != Tier.Codified,
        _ => false,
    };
}
