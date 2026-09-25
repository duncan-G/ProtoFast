using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Engine.Workflows;

public interface IClassifier
{
    Task<Signature> ClassifyAsync(ArtifactRef input, CancellationToken ct);
}
