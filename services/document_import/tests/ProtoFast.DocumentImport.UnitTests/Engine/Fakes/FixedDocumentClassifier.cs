using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.UnitTests.Engine.Fakes;

internal sealed class FixedDocumentClassifier(string family) : IDocumentClassifier
{
    public Task<DocumentSignature> ClassifyAsync(ArtifactRef input, CancellationToken ct) =>
        Task.FromResult(new DocumentSignature(family, new Dictionary<string, string>()));
}
