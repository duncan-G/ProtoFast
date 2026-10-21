namespace ProtoFast.DocumentImport.Worker.HistoricalStories;

public sealed record BriefPerson(string Name, string Role, string? Description, IReadOnlyList<string>? Facts, IReadOnlyList<long>? Sources);
