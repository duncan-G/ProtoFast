namespace ProtoFast.DocumentImport.Data.Postgres.Entities;

public sealed class RunSystemPromptEntry
{
    public required string RunId { get; set; }
    public int FromSequence { get; set; }
    public required string Prompt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
