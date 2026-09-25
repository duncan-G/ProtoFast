using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.UnitTests.Engine.Fakes;

internal sealed class FixedClassifier(string family) : IClassifier
{
    public Task<Signature> ClassifyAsync(ArtifactRef input, CancellationToken ct) =>
        Task.FromResult(new Signature(family, new Dictionary<string, string>()));
}
