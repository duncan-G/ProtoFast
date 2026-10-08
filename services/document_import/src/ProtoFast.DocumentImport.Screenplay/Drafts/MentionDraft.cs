namespace ProtoFast.DocumentImport.Screenplay.Drafts;

/// <param name="Name">A library name of <paramref name="Kind"/>.</param>
/// <param name="Offset">Index of the <c>@</c> in the element's text.</param>
/// <param name="Length"><c>@</c> included.</param>
public sealed record MentionDraft(MentionKind Kind, string Name, int Offset, int Length);
