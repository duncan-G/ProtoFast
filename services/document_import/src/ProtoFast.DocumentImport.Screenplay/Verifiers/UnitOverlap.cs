namespace ProtoFast.DocumentImport.Screenplay.Verifiers;

/// <param name="Kept">How many of the unit's words the manuscript has in the same order.</param>
public sealed record UnitOverlap(string Path, int Words, int Kept, string Snippet);
