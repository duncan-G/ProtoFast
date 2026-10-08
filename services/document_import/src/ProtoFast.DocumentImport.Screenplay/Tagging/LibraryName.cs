using ProtoFast.DocumentImport.Screenplay.Drafts;

namespace ProtoFast.DocumentImport.Screenplay.Tagging;

internal sealed record LibraryName(MentionKind Kind, string Name, string? Description);
