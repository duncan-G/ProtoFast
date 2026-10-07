using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.UnitTests.Engine.Fakes;

internal sealed class FixedDocumentClassifier(string family) : IDocumentClassifier
{
    public string Family { get; set; } = family;

    public Task<DocumentSignature> ClassifyAsync(ArtifactRef input, CancellationToken ct) =>
        Task.FromResult(new DocumentSignature(Family, new Dictionary<string, string>()));
}
