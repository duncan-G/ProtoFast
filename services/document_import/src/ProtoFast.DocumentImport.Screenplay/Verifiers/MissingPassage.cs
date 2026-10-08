namespace ProtoFast.DocumentImport.Screenplay.Verifiers;

/// <summary>A run of manuscript words that no story unit keeps.</summary>
public sealed record MissingPassage(int Words, string Snippet);
