namespace ProtoFast.DocumentImport.Worker.HistoricalStories;

public sealed record BriefPlace(string Name, string Description, IReadOnlyList<long>? Sources);
