namespace ProtoFast.DocumentImport.Engine.Executors;

public sealed record Playbook(
    PlaybookRef Ref,
    string Instructions,
    IReadOnlyList<Example> Examples,
    IReadOnlyDictionary<string, string> Rules);   // keyed by Decision.Key
