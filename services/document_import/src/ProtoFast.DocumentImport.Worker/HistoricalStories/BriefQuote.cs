namespace ProtoFast.DocumentImport.Worker.HistoricalStories;

/// <param name="Words">Exactly as the source gives them.</param>
public sealed record BriefQuote(string Speaker, string Words, string? Context, long Source);
