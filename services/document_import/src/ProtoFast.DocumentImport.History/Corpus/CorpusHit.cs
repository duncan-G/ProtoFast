namespace ProtoFast.DocumentImport.History.Corpus;

/// <param name="Snippet">The matching fragments of the passage, joined by " … ".</param>
public sealed record CorpusHit(
    long Id, int BookId, int Seq, string Title, string Authors, int? FirstYear, int? LastYear, string Snippet, float Rank);
