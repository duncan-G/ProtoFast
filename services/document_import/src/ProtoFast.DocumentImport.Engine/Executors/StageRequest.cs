using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Executors;

public sealed record StageRequest(
    string RunId,
    StageDefinition Stage,
    DocumentSignature DocumentSignature,
    IReadOnlyList<ArtifactRef> Inputs);
