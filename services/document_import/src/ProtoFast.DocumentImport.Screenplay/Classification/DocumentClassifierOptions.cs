using ProtoFast.DocumentImport.Engine.Executors;

namespace ProtoFast.DocumentImport.Screenplay.Classification;

public sealed class DocumentClassifierOptions
{
    public string SynopsisModelClass { get; set; } = ModelClasses.Small;

    public string ModelClass { get; set; } = ModelClasses.Medium;

    /// <summary>A longer manuscript reaches the synopsis model as its opening, a slice of its middle and its ending.</summary>
    public int MaxExcerptChars { get; set; } = 40_000;
}
