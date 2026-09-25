namespace ProtoFast.DocumentImport.Engine;

public sealed record ExecutorSpec(
    ExecutorRef Ref,
    Tier Tier,
    string? ModelClass,
    PlaybookRef? Playbook,
    IReadOnlyList<string> Tools,
    string? CodeAssembly,
    ExecutorOrigin Origin,
    bool Promoted);
