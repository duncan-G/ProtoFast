using ProtoFast.DocumentImport.Screenplay.Drafts;

namespace ProtoFast.DocumentImport.Screenplay.Tagging;

/// <param name="Alias">Another name the manuscript uses for the entry; null for the entry's own name.</param>
internal sealed record LibraryName(MentionKind Kind, string Name, string? Description, string? Alias = null)
{
    public string Spelling => Alias ?? Name;
}
