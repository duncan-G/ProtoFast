namespace ProtoFast.DocumentImport.Engine.Storage;

/// <param name="FromSequence">The first transcript message the model saw under this prompt.</param>
public sealed record RunSystemPrompt(int FromSequence, string Prompt, DateTimeOffset RecordedAt);
