namespace ProtoFast.DocumentImport.Worker.HistoricalStories;

public sealed record BriefEvent(string When, string What, IReadOnlyList<long>? Sources);
