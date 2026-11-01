namespace ProtoFast.DocumentImport.History.Corpus;

/// <param name="Seq">The passage's place in its book, from 0.</param>
public sealed record CorpusPassage(
    long Id, int BookId, int Seq, string Title, string Authors, int? FirstYear, int? LastYear, string Text);
