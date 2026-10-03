using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Engine.Workflows;

/// <summary>Classifies an input into its family's current generation.</summary>
public sealed class DocumentSignatures(IDocumentClassifier classifier, IDocumentFamilyGenerations generations)
{
    public async Task<DocumentSignature> ClassifyAsync(ArtifactRef input, CancellationToken ct)
    {
        var signature = await classifier.ClassifyAsync(input, ct);
        var generation = await generations.CurrentAsync(signature.Family, ct);

        // Generation 0 keeps the bare name, so families that predate resets keep what they learned.
        return generation == 0 ? signature : signature with { Family = $"{signature.Family}#{generation}" };
    }
}
