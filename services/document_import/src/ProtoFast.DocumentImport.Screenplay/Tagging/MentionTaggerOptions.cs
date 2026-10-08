using ProtoFast.DocumentImport.Engine.Executors;

namespace ProtoFast.DocumentImport.Screenplay.Tagging;

public sealed class MentionTaggerOptions
{
    public string ModelClass { get; set; } = ModelClasses.Small;

    public int MaxCandidatesPerCall { get; set; } = 50;

    /// <summary>Characters of the element's text shown on each side of a candidate.</summary>
    public int ContextChars { get; set; } = 80;

    public string AliasModelClass { get; set; } = ModelClasses.Medium;

    /// <summary>The most frequent capitalised phrases offered as aliases; rarer ones stay plain text.</summary>
    public int MaxAliasCandidates { get; set; } = 300;

    public int AliasSamples { get; set; } = 2;
}
