namespace ProtoFast.DocumentImport.Screenplay.Tagging;

/// <param name="Candidate">Id of the candidate phrase.</param>
/// <param name="Entry">Id of the library entry it names.</param>
public sealed record AliasLink(int Candidate, int Entry);
