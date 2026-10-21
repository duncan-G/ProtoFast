namespace ProtoFast.DocumentImport.History.Corpus;

/// <param name="Id">The Project Gutenberg ebook number.</param>
/// <param name="FirstYear">The earliest year the book's subjects cover, when they name one.</param>
public sealed record CorpusBook(
    int Id, string Title, string Authors, string Issued, string Subjects, string Locc, string Bookshelves, int? FirstYear, int? LastYear);
