using ProtoFast.DocumentImport.Engine.Executors;

namespace ProtoFast.DocumentImport.Screenplay.Tagging;

public sealed class MentionTaggerOptions
{
    public string ModelClass { get; set; } = ModelClasses.Small;

    public int MaxCandidatesPerCall { get; set; } = 50;

    /// <summary>Characters of the element's text shown on each side of a candidate.</summary>
    public int ContextChars { get; set; } = 80;
}
