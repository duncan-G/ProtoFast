namespace ProtoFast.DocumentImport.Engine;

public sealed record StageDefinition(
    string Id,
    IReadOnlyList<string> DependsOn,          // empty = root, receives the run input
    ContractRef Input,
    ContractRef Output,
    IReadOnlyList<string> Verifiers,
    Budget Budget);
