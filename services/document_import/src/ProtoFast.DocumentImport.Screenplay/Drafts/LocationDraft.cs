namespace ProtoFast.DocumentImport.Screenplay.Drafts;

/// <param name="Setting">"Interior" or "Exterior".</param>
public sealed record LocationDraft(string Name, string? Setting, string? Description);
