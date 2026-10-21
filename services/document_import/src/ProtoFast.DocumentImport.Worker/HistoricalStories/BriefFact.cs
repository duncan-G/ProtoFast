namespace ProtoFast.DocumentImport.Worker.HistoricalStories;

public sealed record BriefFact(string Text, IReadOnlyList<long>? Sources);
