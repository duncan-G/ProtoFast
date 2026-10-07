namespace ProtoFast.DocumentImport.Screenplay.Classification;

/// <param name="Form">The kind of document in the model's words: feature screenplay, novel manuscript, treatment…</param>
/// <param name="Structure">How the text is laid out: scene headings and dialogue, chapters of prose, headed sections…</param>
public sealed record Synopsis(string Summary, string? Form, string? Language, string? Structure);
