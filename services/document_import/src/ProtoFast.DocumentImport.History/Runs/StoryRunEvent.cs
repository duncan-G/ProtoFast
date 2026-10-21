namespace ProtoFast.DocumentImport.History.Runs;

/// <param name="Kind">What happened: random, search, read, note, brief, draft, repair, saved or error.</param>
/// <param name="Detail">The full tool result or model text behind the summary.</param>
public sealed record StoryRunEvent(int Seq, DateTimeOffset At, string Kind, string Summary, string? Detail, decimal Cost);
