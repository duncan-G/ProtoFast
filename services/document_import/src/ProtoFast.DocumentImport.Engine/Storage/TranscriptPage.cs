namespace ProtoFast.DocumentImport.Engine.Storage;

/// <param name="Total">Messages in the whole transcript, not the page.</param>
/// <param name="SystemPrompts">Every prompt the run used, on the page that starts at sequence 0; empty on later pages.</param>
public sealed record TranscriptPage(IReadOnlyList<TranscriptMessage> Messages, int Total, IReadOnlyList<RunSystemPrompt> SystemPrompts);
