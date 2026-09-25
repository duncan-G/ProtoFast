using ProtoFast.DocumentImport.Engine;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

internal sealed class FixedClassifier(string family) : IClassifier
{
    public Task<Signature> ClassifyAsync(ArtifactRef input, CancellationToken ct) =>
        Task.FromResult(new Signature(family, new Dictionary<string, string>()));
}
