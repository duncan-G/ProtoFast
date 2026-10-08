namespace ProtoFast.DocumentImport.Screenplay.Models;

/// <summary>The provider's safety filter declined the request; another provider may not.</summary>
public sealed class LanguageModelRefusedException(string modelId, string? explanation)
    : InvalidOperationException($"{modelId} refused the request: {explanation}")
{
    public string ModelId => modelId;
}
