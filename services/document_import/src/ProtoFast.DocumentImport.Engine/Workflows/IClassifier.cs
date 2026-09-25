namespace ProtoFast.DocumentImport.Engine;

public interface IClassifier
{
    Task<Signature> ClassifyAsync(ArtifactRef input, CancellationToken ct);
}
