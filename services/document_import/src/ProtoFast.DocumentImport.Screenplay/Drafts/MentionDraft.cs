namespace ProtoFast.DocumentImport.Screenplay.Drafts;

/// <param name="Name">A library name of <paramref name="Kind"/>.</param>
/// <param name="Offset">Index of the <c>@</c>, or a tag's first character, in the element's text.</param>
/// <param name="Length"><c>@</c> included.</param>
/// <param name="IsTag">The span is text as written, such as an alias, with no <c>@</c>.</param>
public sealed record MentionDraft(MentionKind Kind, string Name, int Offset, int Length, bool IsTag = false);
