namespace ProtoFast.DocumentImport.Screenplay.Drafts;

/// <param name="AvatarShape">An <c>AvatarShape</c> name; an unknown or missing one becomes Circle.</param>
public sealed record CharacterKindDraft(string Label, string? AvatarShape);
