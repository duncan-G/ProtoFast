namespace ProtoFast.DocumentImport.Screenplay.Models;

/// <summary>The provider kept failing for longer than the retry window allows; nothing was wrong with the request.</summary>
public sealed class LanguageModelUnavailableException(string modelId, int attempts, TimeSpan waited, Exception inner)
    : Exception($"{modelId} was unavailable across {attempts} attempts over {waited:g}: {inner.Message}", inner)
{
    public string ModelId => modelId;
}
