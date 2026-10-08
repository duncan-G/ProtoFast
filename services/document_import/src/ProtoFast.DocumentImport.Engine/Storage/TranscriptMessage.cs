namespace ProtoFast.DocumentImport.Engine.Storage;

/// <param name="Json">The journaled message as the agent wrote it; the engine does not read it.</param>
public sealed record TranscriptMessage(int Sequence, string Json, DateTimeOffset RecordedAt);
