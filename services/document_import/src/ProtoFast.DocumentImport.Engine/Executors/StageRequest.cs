namespace ProtoFast.DocumentImport.Engine;

public sealed record StageRequest(
    string RunId,
    StageDefinition Stage,
    Signature Signature,
    IReadOnlyList<ArtifactRef> Inputs);
