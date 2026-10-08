namespace ProtoFast.DocumentImport.Screenplay.Tagging;

/// <param name="Samples">Where it occurs, mid-sentence occurrences first.</param>
internal sealed record AliasCandidate(string Spelling, int Count, IReadOnlyList<(int Element, int Start)> Samples);
