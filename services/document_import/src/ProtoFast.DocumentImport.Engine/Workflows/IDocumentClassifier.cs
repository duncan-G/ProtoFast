using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Engine.Workflows;

public interface IDocumentClassifier
{
    Task<DocumentSignature> ClassifyAsync(ArtifactRef input, CancellationToken ct);
}
